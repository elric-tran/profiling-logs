using System.Text;
using Microsoft.AspNetCore.Http;

namespace ProfilingLogs.Internal;

/// <summary>
/// MiniProfiler HTML-encodes the SQL text, so an &lt;a&gt; tag cannot be injected server-side.
/// This middleware injects a script into the results page to:
///  - turn IDE deep-link strings ("vscode://", "cursor://", ...) into clickable links;
///  - hide the default connection Open/Close rows produced by MiniProfiler.EntityFrameworkCore.
/// </summary>
internal sealed class ProfilerIdeLinkMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ProfilingLogsOptions _options;
    private readonly ClearableMemoryStorage? _storage;
    private readonly string _resultsPath;
    private readonly string _resultsIndexPath;
    private readonly string _clearPath;
    private readonly string _bundleJs;
    private readonly string _injectionResults;
    private readonly string _injectionResultsIndex;

    public ProfilerIdeLinkMiddleware(RequestDelegate next, ProfilingLogsOptions options, ClearableMemoryStorage? storage = null)
    {
        _next = next;
        _options = options;
        _storage = storage;
        var basePath = string.IsNullOrWhiteSpace(options.RouteBasePath) ? "/profiler" : options.RouteBasePath.TrimEnd('/');
        _resultsPath = basePath + "/results";
        _resultsIndexPath = basePath + "/results-index";
        _clearPath = basePath + "/clear-cache";
        _bundleJs = ReadEmbeddedBundle();
        _injectionResults = BuildInjectionScript(options, _clearPath, isResultsIndex: false);
        _injectionResultsIndex = BuildInjectionScript(options, _clearPath, isResultsIndex: true);
    }
    private string BuildInjectionScript(ProfilingLogsOptions options, string clearPath, bool isResultsIndex)
    {
        var payload = new
        {
            Scheme = options.ResolveScheme(),
            EnableVsCodeLinks = options.EnableVsCodeLinks,
            HideDefaultConnRows = options.HideDefaultConnRows,
            EnableClearCacheButton = options.EnableClearCacheButton,
            EnableHttpMethodColumn = options.EnableHttpMethodColumn,
            CoffeeQrData = CoffeeAssets.QrDataUri,
            CoffeeUrl = CoffeeAssets.BuyMeACoffeeUrl,
            ClearPath = clearPath,
            IsResultsIndex = isResultsIndex
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        return $"<script>window.__PL_OPTIONS__ = {json};</script><script>{_bundleJs}</script>";
    }

    private static string ReadEmbeddedBundle()
    {
        var assembly = typeof(ProfilerIdeLinkMiddleware).Assembly;
        using var stream = assembly.GetManifestResourceStream("profilinglogs-ui.js");
        if (stream == null) return "/* profilinglogs-ui.js embedded resource not found */";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Handle the "clear all results" endpoint before anything else.
        if (_options.EnableClearCacheButton
            && path.Equals(_clearPath, StringComparison.OrdinalIgnoreCase))
        {
            _storage?.Clear();
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        if (!path.Contains(_resultsPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var isResultsIndex = path.Contains(_resultsIndexPath, StringComparison.OrdinalIgnoreCase);

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            buffer.Seek(0, SeekOrigin.Begin);
            var contentType = context.Response.ContentType ?? string.Empty;

            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                var html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();

                var injection = isResultsIndex ? _injectionResultsIndex : _injectionResults;

                html = html.Contains("</body>", StringComparison.OrdinalIgnoreCase)
                    ? html.Replace("</body>", injection + "</body>")
                    : html + injection;

                var bytes = Encoding.UTF8.GetBytes(html);
                context.Response.ContentLength = bytes.Length;
                await originalBody.WriteAsync(bytes);
            }
            else
            {
                buffer.Seek(0, SeekOrigin.Begin);
                await buffer.CopyToAsync(originalBody);
            }
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
