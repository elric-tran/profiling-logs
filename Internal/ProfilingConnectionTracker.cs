using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ProfilingLogs.Internal;

/// <summary>
/// Observes EF Core diagnostic events: colors connection Open/Close and injects a comment
/// (with an IDE deep-link) pointing to the source line that produced the SQL query.
/// </summary>
internal sealed class ProfilingConnectionTracker : IObserver<KeyValuePair<string, object?>>
{
    private static readonly string[] ConnColors =
    [
        "🔴", "🟠", "🟡", "🟢", "🔵", "🟣", "🟤", "⚪", "⚫",
        "🟥", "🟧", "🟨", "🟩", "🟦", "🟪", "🟫", "⬜", "⬛",
        "❤️", "🧡", "💛", "💚", "💙", "💜", "🤎", "🤍", "🖤"
    ];

    private static int _colorSeq = -1;

    private static readonly ConcurrentDictionary<string, ConcurrentStack<string>> OpenColors = new();

    private readonly ProfilingLogsOptions _options;
    private readonly ProfilingStore? _store;

    public ProfilingConnectionTracker(ProfilingLogsOptions options, ProfilingStore? store = null)
    {
        _options = options;
        _store = store;
    }

    private static string NextColor()
    {
        var idx = (int)((uint)Interlocked.Increment(ref _colorSeq) % (uint)ConnColors.Length);
        return ConnColors[idx];
    }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (_options.EnableConnectionColors)
        {
            if (value.Key == RelationalEventId.ConnectionOpening.Name && value.Value is ConnectionEventData openData)
            {
                var connId = openData.Connection.GetHashCode().ToString("X");
                var color = NextColor();
                OpenColors.GetOrAdd(connId, _ => new ConcurrentStack<string>()).Push(color);

                ProfilingStore.CurrentRequest.Value?.ConnectionEvents.Add(
                    $"{color} [CONN OPEN] -> Id: #{connId}");

                if (_store != null && string.IsNullOrEmpty(_store.CapturedConnectionString))
                {
                    var cs = openData.Connection.ConnectionString;
                    if (!string.IsNullOrEmpty(cs))
                        _store.CapturedConnectionString = cs;
                }
            }
            else if (value.Key == RelationalEventId.ConnectionClosed.Name && value.Value is ConnectionEndEventData closeData)
            {
                var connId = closeData.Connection.GetHashCode().ToString("X");
                var color = "⚪";
                if (OpenColors.TryGetValue(connId, out var stack) && stack.TryPop(out var matched))
                {
                    color = matched;
                }

                ProfilingStore.CurrentRequest.Value?.ConnectionEvents.Add(
                    $"{color} [CONN CLOSE] <- Id: #{connId}");
            }
        }

        if (_options.EnableCallerComment
            && (value.Key == RelationalEventId.CommandInitialized.Name
                || value.Key == RelationalEventId.CommandExecuting.Name)
            && value.Value is CommandEventData commandData)
        {
            AppendCallerComment(commandData);
        }
    }

    private const string CallerMarker = "-- \uD83D\uDD17 From:";

    private sealed record CallerInfo(string FileName, int LineNumber, string FullPath, string? MethodName);

    private sealed class CallerBox { public CallerInfo? Value; }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Data.Common.DbConnection, CallerBox> ConnectionCallers = new();

    private void AppendCallerComment(CommandEventData commandData)
    {
        var command = commandData.Command;
        if (command?.CommandText == null)
        {
            return;
        }

        if (command.CommandText.Contains(CallerMarker, StringComparison.Ordinal))
        {
            return;
        }

        var caller = ResolveCallerFromStack();
        var connection = command.Connection;
        if (caller != null)
        {
            if (connection != null)
            {
                ConnectionCallers.GetValue(connection, _ => new CallerBox()).Value = caller;
            }
        }
        else if (connection != null && ConnectionCallers.TryGetValue(connection, out var box))
        {
            caller = box.Value;
        }

        if (caller == null)
        {
            return;
        }

        var ideLink = _options.BuildIdeLink(caller.FullPath, caller.LineNumber);

        // Record the query in the current profiled request
        var currentRequest = ProfilingStore.CurrentRequest.Value;
        if (currentRequest != null)
        {
            var connHashId = connection?.GetHashCode().ToString("X");
            string? connColor = null;
            if (connHashId != null && OpenColors.TryGetValue(connHashId, out var stack) && stack.TryPeek(out var color))
            {
                connColor = color;
            }

            var parameters = ExtractParameters(command);

            currentRequest.Queries.Add(new ProfiledQuery(
                Sql: command.CommandText,
                DurationMs: 0,
                CallerFile: caller.FileName,
                CallerLine: caller.LineNumber,
                CallerMethod: caller.MethodName,
                IdeLink: ideLink,
                ConnColor: connColor,
                ConnId: connHashId,
                ConnEvent: connColor != null ? "OPEN" : null,
                Parameters: parameters));
        }

        command.CommandText =
            command.CommandText + "\r\n\n" +
            $"{CallerMarker} {caller.FileName} (Line {caller.LineNumber}) -> {caller.MethodName}\r\n" +
            $"-- {ideLink}\r\n";
    }

    private static List<ProfiledParameter>? ExtractParameters(System.Data.Common.DbCommand command)
    {
        if (command.Parameters.Count == 0)
            return null;

        var list = new List<ProfiledParameter>(command.Parameters.Count);
        foreach (System.Data.Common.DbParameter p in command.Parameters)
        {
            var name = p.ParameterName;
            var sqlType = MapDbTypeToSql(p.DbType, p.Size);
            string? val;
            if (p.Value == null || p.Value == DBNull.Value)
            {
                val = "NULL";
            }
            else
            {
                val = p.DbType switch
                {
                    System.Data.DbType.String or
                    System.Data.DbType.StringFixedLength =>
                        $"N'{EscapeSqlString(p.Value.ToString())}'",

                    System.Data.DbType.AnsiString or
                    System.Data.DbType.AnsiStringFixedLength =>
                        $"'{EscapeSqlString(p.Value.ToString())}'",

                    System.Data.DbType.Xml => $"N'{EscapeSqlString(p.Value.ToString())}'",

                    System.Data.DbType.Date => $"'{((DateTime)p.Value):yyyy-MM-dd}'",

                    System.Data.DbType.DateTime or
                    System.Data.DbType.DateTime2 => $"'{((DateTime)p.Value):yyyy-MM-ddTHH:mm:ss.fff}'",

                    System.Data.DbType.DateTimeOffset =>
                        $"'{((DateTimeOffset)p.Value):yyyy-MM-ddTHH:mm:ss.fffffffzzz}'",

                    System.Data.DbType.Time => $"'{p.Value}'",

                    System.Data.DbType.Boolean => (bool)p.Value ? "1" : "0",

                    System.Data.DbType.Guid => $"'{p.Value}'",

                    _ => p.Value.ToString()
                };
            }
            list.Add(new ProfiledParameter(name, sqlType, val));
        }
        return list;
    }

    private static string MapDbTypeToSql(System.Data.DbType dbType, int size)
    {
        var sizeSpec = size > 0 && size < 8000 ? $"({size})" : "(max)";
        return dbType switch
        {
            System.Data.DbType.Boolean => "bit",
            System.Data.DbType.Byte => "tinyint",
            System.Data.DbType.Int16 => "smallint",
            System.Data.DbType.Int32 => "int",
            System.Data.DbType.Int64 => "bigint",
            System.Data.DbType.Single => "real",
            System.Data.DbType.Double => "float",
            System.Data.DbType.Decimal or System.Data.DbType.Currency => "decimal(18,2)",
            System.Data.DbType.String or System.Data.DbType.StringFixedLength => $"nvarchar{sizeSpec}",
            System.Data.DbType.AnsiString or System.Data.DbType.AnsiStringFixedLength => $"varchar{sizeSpec}",
            System.Data.DbType.Guid => "uniqueidentifier",
            System.Data.DbType.Date => "date",
            System.Data.DbType.DateTime => "datetime",
            System.Data.DbType.DateTime2 => "datetime2",
            System.Data.DbType.DateTimeOffset => "datetimeoffset",
            System.Data.DbType.Time => "time",
            System.Data.DbType.Binary => $"varbinary{sizeSpec}",
            System.Data.DbType.Xml => "xml",
            _ => "sql_variant"
        };
    }

    private static string? EscapeSqlString(string? value) =>
        value?.Replace("'", "''");

    private CallerInfo? ResolveCallerFromStack()
    {
        var frames = new StackTrace(true).GetFrames();
        if (frames == null)
        {
            return null;
        }

        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (type?.FullName == null
                || !type.FullName.Contains(_options.SqlNamespaceFilter)
                || type == typeof(ProfilingConnectionTracker))
            {
                continue;
            }

            var fullPath = frame.GetFileName();
            var fileName = Path.GetFileName(fullPath);
            var lineNumber = frame.GetFileLineNumber();

            if (string.IsNullOrEmpty(fileName) || lineNumber <= 0 || string.IsNullOrEmpty(fullPath))
            {
                continue;
            }

            return new CallerInfo(fileName, lineNumber, fullPath!, GetFriendlyMethodName(method));
        }

        return null;
    }

    private static string? GetFriendlyMethodName(System.Reflection.MethodBase? method)
    {
        var typeName = method?.DeclaringType?.Name;
        if (typeName != null && typeName.Length > 1 && typeName[0] == '<')
        {
            var end = typeName.IndexOf('>');
            if (end > 1)
            {
                return typeName.Substring(1, end - 1);
            }
        }

        return method?.Name;
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }
}

/// <summary>
/// Listens to <see cref="DiagnosticListener"/> instances and attaches a
/// <see cref="ProfilingConnectionTracker"/> to the EF Core listener.
/// </summary>
internal sealed class ProfilingDiagnosticObserver : IObserver<DiagnosticListener>
{
    private readonly ProfilingLogsOptions _options;
    private readonly ProfilingStore? _store;

    public ProfilingDiagnosticObserver(ProfilingLogsOptions options, ProfilingStore? store = null)
    {
        _options = options;
        _store = store;
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == DbLoggerCategory.Name)
        {
            value.Subscribe(new ProfilingConnectionTracker(_options, _store));
        }
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }
}
