using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StackExchange.Profiling;

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

    public ProfilingConnectionTracker(ProfilingLogsOptions options)
    {
        _options = options;
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
                MiniProfiler.Current?.CustomTiming("sql-connection-open", $"{color} [CONN OPEN] -> Id: #{connId}");
            }
            else if (value.Key == RelationalEventId.ConnectionClosed.Name && value.Value is ConnectionEndEventData closeData)
            {
                var connId = closeData.Connection.GetHashCode().ToString("X");
                var color = "⚪";
                if (OpenColors.TryGetValue(connId, out var stack) && stack.TryPop(out var matched))
                {
                    color = matched;
                }
                MiniProfiler.Current?.CustomTiming("sql-connection-close", $"{color} [CONN CLOSE] <- Id: #{connId}")?.Stop();
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

    // Remembers the caller resolved for each physical DbConnection. EF Core creates the DbCommand
    // synchronously, but for patterns like `await CountAsync(); await ToListAsync();` (e.g. a
    // ToPagedListAsync helper) the second command runs on a thread-pool continuation *after* an
    // await, so the caller's Services frame is no longer on the stack - and an AsyncLocal set during
    // the first command does not flow across EF's internal async boundaries either. Both commands
    // run on the same DbContext/DbConnection, so keying the last resolved caller on the connection
    // lets the second query inherit it. The ConditionalWeakTable never keeps a connection alive.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<System.Data.Common.DbConnection, CallerBox> ConnectionCallers = new();

    private void AppendCallerComment(CommandEventData commandData)
    {
        var command = commandData.Command;
        if (command?.CommandText == null)
        {
            return;
        }

        // Already annotated (e.g. at CommandInitialized) - don't append twice at CommandExecuting.
        if (command.CommandText.Contains(CallerMarker, StringComparison.Ordinal))
        {
            return;
        }

        // NOTE: EF Core builds the command (CommandInitialized) synchronously *before*
        // `await connection.OpenAsync(...)`. For async queries against a not-yet-open
        // connection, the open awaits and the later CommandExecuting event resumes on a
        // thread-pool continuation whose stack no longer contains the caller frames - so
        // walking the stack there finds nothing and no link is produced. Capturing at
        // CommandInitialized keeps the caller's synchronous stack intact, which is why the
        // link now resolves for both already-open (sync) and freshly-opened (async) connections.
        var caller = ResolveCallerFromStack();
        var connection = command.Connection;
        if (caller != null)
        {
            // Fresh, accurate caller - remember it for later commands on this connection.
            if (connection != null)
            {
                ConnectionCallers.GetValue(connection, _ => new CallerBox()).Value = caller;
            }
        }
        else if (connection != null && ConnectionCallers.TryGetValue(connection, out var box))
        {
            // No Services frame on the stack (e.g. a query materialized past an await inside a
            // helper). Fall back to the caller captured for the previous command on this connection.
            caller = box.Value;
        }

        if (caller == null)
        {
            return;
        }

        var ideLink = _options.BuildIdeLink(caller.FullPath, caller.LineNumber);
        command.CommandText =
            command.CommandText + "\r\n\n" +
            $"{CallerMarker} {caller.FileName} (Line {caller.LineNumber}) -> {caller.MethodName}\r\n" +
            $"-- {ideLink}\r\n";
    }

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

    /// <summary>
    /// Async methods appear on the stack as the compiler-generated state machine's
    /// <c>MoveNext</c>. Recover the original method name from the state-machine type
    /// (e.g. <c>&lt;GetIncidentsAsync&gt;d__12</c> -> <c>GetIncidentsAsync</c>).
    /// </summary>
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

    public ProfilingDiagnosticObserver(ProfilingLogsOptions options)
    {
        _options = options;
    }

    public void OnNext(DiagnosticListener value)
    {
        if (value.Name == DbLoggerCategory.Name)
        {
            value.Subscribe(new ProfilingConnectionTracker(_options));
        }
    }

    public void OnCompleted() { }

    public void OnError(Exception error) { }
}
