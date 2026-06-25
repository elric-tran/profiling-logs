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
            && value.Key == RelationalEventId.CommandExecuting.Name
            && value.Value is CommandEventData commandData)
        {
            AppendCallerComment(commandData);
        }
    }

    private void AppendCallerComment(CommandEventData commandData)
    {
        var frames = new StackTrace(true).GetFrames();
        if (frames == null)
        {
            return;
        }

        foreach (var frame in frames)
        {
            var type = frame.GetMethod()?.DeclaringType;
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

            var methodName = frame.GetMethod()?.Name;
            var ideLink = _options.BuildIdeLink(fullPath, lineNumber);

            commandData.Command.CommandText =
                commandData.Command.CommandText + "\r\n\n" +
                $"-- 🔗 From: {fileName} (Line {lineNumber}) -> {methodName}\r\n" +
                $"-- {ideLink}\r\n";
            return;
        }
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
