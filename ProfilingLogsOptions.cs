using StackExchange.Profiling;

namespace ProfilingLogs;

/// <summary>
/// The IDE used to build deep-links that jump straight to the source file/line that produced a SQL query.
/// </summary>
public enum ProfilingIde
{
    /// <summary>Visual Studio Code: <c>vscode://file/{path}:{line}</c></summary>
    VSCode,

    /// <summary>Cursor: <c>cursor://file/{path}:{line}</c></summary>
    Cursor,

    /// <summary>JetBrains Rider: <c>jetbrains://rider/navigate/reference?path={path}&amp;line={line}</c></summary>
    Rider,

    /// <summary>
    /// Visual Studio: <c>devenv://open?file={path}&amp;line={line}</c>.
    /// Visual Studio has no native URL scheme, so this requires a one-time protocol-handler
    /// registration on the machine (see <c>tools/</c> in the repository).
    /// </summary>
    VisualStudio,

    /// <summary>Use the custom template defined in <see cref="ProfilingLogsOptions.IdeUrlFormat"/>.</summary>
    Custom
}

/// <summary>
/// Configuration for ProfilingLogs. Every flag is supplied by the caller; defaults are
/// production-safe (<see cref="Enabled"/> = false makes the whole feature a no-op).
/// </summary>
public sealed class ProfilingLogsOptions
{
    /// <summary>
    /// Master switch. When false, MiniProfiler is not registered, no DiagnosticListener is subscribed,
    /// and no middleware is added. Pass <c>builder.Environment.IsDevelopment()</c> here.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>MiniProfiler base path. Defaults to <c>/profiler</c>.</summary>
    public string RouteBasePath { get; set; } = "/profiler";

    /// <summary>Turn the IDE deep-link text on the results page into clickable &lt;a&gt; anchors.</summary>
    public bool EnableVsCodeLinks { get; set; } = true;

    /// <summary>Add a color (emoji) marker for each connection Open/Close pair.</summary>
    public bool EnableConnectionColors { get; set; } = true;

    /// <summary>Inject a <c>-- 🔗 From ...</c> comment (with an IDE deep-link) into the SQL text.</summary>
    public bool EnableCallerComment { get; set; } = true;

    /// <summary>Hide the default <c>sql - Open/Close</c> rows produced by MiniProfiler.EntityFrameworkCore.</summary>
    public bool HideDefaultConnRows { get; set; } = true;

    /// <summary>
    /// Show a "Clear all profiler results" button on the <c>{RouteBasePath}/results-index</c> page
    /// that wipes every stored profiling result (all captured API calls).
    /// </summary>
    public bool EnableClearCacheButton { get; set; } = true;

    /// <summary>Only walk stack frames whose type namespace contains this value. Defaults to <c>"Services"</c>.</summary>
    public string SqlNamespaceFilter { get; set; } = "Services";

    /// <summary>The IDE used to build deep-links. Defaults to <see cref="ProfilingIde.VSCode"/>.</summary>
    public ProfilingIde Ide { get; set; } = ProfilingIde.VSCode;

    /// <summary>
    /// Custom URL template that overrides the <see cref="Ide"/> preset.
    /// Supports the placeholders <c>{path}</c>, <c>{line}</c> and <c>{col}</c>.
    /// </summary>
    public string? IdeUrlFormat { get; set; }

    /// <summary>
    /// Map remote -&gt; local path prefixes (so links open correctly when viewing from another machine).
    /// Example: <c>{ "/app/src", "D:/work/src" }</c>.
    /// </summary>
    public IDictionary<string, string>? PathMap { get; set; }

    /// <summary>MiniProfiler color scheme. Defaults to <see cref="ColorScheme.Dark"/>.</summary>
    public ColorScheme ColorScheme { get; set; } = ColorScheme.Dark;

    /// <summary>Returns the effective URL template (prefers <see cref="IdeUrlFormat"/>, then the preset).</summary>
    internal string ResolveIdeFormat() => IdeUrlFormat ?? Ide switch
    {
        ProfilingIde.VSCode => "vscode://file/{path}:{line}",
        ProfilingIde.Cursor => "cursor://file/{path}:{line}",
        ProfilingIde.Rider => "jetbrains://rider/navigate/reference?path={path}&line={line}",
        ProfilingIde.VisualStudio => "devenv://open?file={path}&line={line}",
        _ => "vscode://file/{path}:{line}"
    };

    /// <summary>Builds an IDE deep-link from an absolute path + line number (after applying <see cref="PathMap"/>).</summary>
    internal string BuildIdeLink(string fullPath, int line)
    {
        var path = fullPath.Replace('\\', '/');

        if (PathMap is { Count: > 0 })
        {
            foreach (var map in PathMap)
            {
                var from = map.Key.Replace('\\', '/');
                if (path.StartsWith(from, StringComparison.OrdinalIgnoreCase))
                {
                    path = map.Value.Replace('\\', '/') + path.Substring(from.Length);
                    break;
                }
            }
        }

        return ResolveIdeFormat()
            .Replace("{path}", EncodePath(path))
            .Replace("{line}", line.ToString())
            .Replace("{col}", "1");
    }

    /// <summary>
    /// URL-encodes a forward-slash path so the deep-link stays a single token. Spaces (and other
    /// unsafe characters) are percent-encoded; the path separators <c>/</c> and a leading Windows
    /// drive letter (e.g. <c>D:</c>) are preserved. Without this, a path containing a space would be
    /// truncated by the client-side linkify regex and the IDE link would not open.
    /// </summary>
    private static string EncodePath(string path)
    {
        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length == 0)
            {
                continue;
            }

            // Preserve a leading Windows drive letter like "D:" (do not encode the colon).
            if (i == 0 && segments[i].Length == 2 && char.IsLetter(segments[i][0]) && segments[i][1] == ':')
            {
                continue;
            }

            segments[i] = Uri.EscapeDataString(segments[i]);
        }

        return string.Join("/", segments);
    }

    /// <summary>The deep-link scheme (the part before <c>://</c>) used by the client-side linkify regex.</summary>
    internal string ResolveScheme()
    {
        var format = ResolveIdeFormat();
        var idx = format.IndexOf("://", StringComparison.Ordinal);
        return idx > 0 ? format.Substring(0, idx) : "vscode";
    }
}
