using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace ProfilingLogs.Internal;

/// <summary>
/// Serves the standalone profiler UI and JSON API endpoints.
/// </summary>
internal sealed class ProfilerIdeLinkMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ProfilingLogsOptions _options;
    private readonly ProfilingStore _store;
    private readonly string _basePath;
    private readonly string _resultsIndexPath;
    private readonly string _apiResultsPath;
    private readonly string _apiExplainPath;
    private readonly string _clearPath;
    private readonly string _indexHtml;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public ProfilerIdeLinkMiddleware(RequestDelegate next, ProfilingLogsOptions options, ProfilingStore store)
    {
        _next = next;
        _options = options;
        _store = store;
        _basePath = string.IsNullOrWhiteSpace(options.RouteBasePath) ? "/profiler" : options.RouteBasePath.TrimEnd('/');
        _resultsIndexPath = _basePath + "/results-index";
        _apiResultsPath = _basePath + "/api/results";
        _apiExplainPath = _basePath + "/api/explain";
        _clearPath = _basePath + "/clear-cache";
        _indexHtml = BuildIndexHtml(options);
    }

    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.Equals(_resultsIndexPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(_indexHtml);
            return;
        }

        if (path.Equals(_apiResultsPath, StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsGet(context.Request.Method))
        {
            var results = _store.List();
            var payload = results.Select(r => new
            {
                r.Id, r.Name, r.Method, r.Started, r.DurationMs, r.StatusCode,
                QueryCount = r.Queries.Count
            });
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOpts));
            return;
        }

        if (path.StartsWith(_apiResultsPath + "/", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsGet(context.Request.Method))
        {
            var idStr = path.Substring(_apiResultsPath.Length + 1);
            if (Guid.TryParse(idStr, out var id))
            {
                var result = _store.Get(id);
                if (result != null)
                {
                    var payload = new
                    {
                        result.Id, result.Name, result.Method, result.Started,
                        result.DurationMs, result.StatusCode,
                        Queries = result.Queries.Select(q => new
                        {
                            q.Sql, q.DurationMs, q.CallerFile, q.CallerLine,
                            q.CallerMethod, q.IdeLink, q.ConnColor, q.ConnId,
                            q.ConnEvent, q.Parameters
                        }),
                        ConnectionEvents = result.ConnectionEvents.ToList()
                    };
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOpts));
                    return;
                }
            }
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Execution plan
        if (path.Equals(_apiExplainPath, StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsPost(context.Request.Method))
        {
            await HandleExplain(context);
            return;
        }

        if (path.Equals(_clearPath, StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsPost(context.Request.Method))
        {
            _store.Clear();
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        await _next(context);
    }

    private async Task HandleExplain(HttpContext context)
    {
        var connectionString = _store.CapturedConnectionString;
        if (string.IsNullOrEmpty(connectionString))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "No database connection string captured yet. Execute at least one query first." }, JsonOpts));
            return;
        }

        ExplainRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ExplainRequest>(context.Request.Body, JsonOpts);
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (body?.Sql == null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Missing 'sql' field." }, JsonOpts));
            return;
        }

        try
        {
            var planXml = await GetExecutionPlan(connectionString, body.Sql, body.Parameters);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { plan = planXml }, JsonOpts));
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts));
        }
    }

    private static async Task<string> GetExecutionPlan(string connectionString, string sql, List<ExplainParameter>? parameters)
    {
        var literalSql = sql;
        if (parameters is { Count: > 0 })
        {
            foreach (var p in parameters.OrderByDescending(p => p.Name?.Length ?? 0))
            {
                if (p.Name != null)
                    literalSql = literalSql.Replace(p.Name, p.Value ?? "NULL");
            }
        }

        // Strip any profiling comments appended by the tracker
        var markerIdx = literalSql.IndexOf("-- \uD83D\uDD17 From:", StringComparison.Ordinal);
        if (markerIdx > 0)
            literalSql = literalSql.Substring(0, markerIdx).TrimEnd();

        // Use Microsoft.Data.SqlClient or System.Data.SqlClient depending on what's available
        var connection = CreateConnection(connectionString);
        await using (connection)
        {
            await connection.OpenAsync();

            await using var cmdOn = connection.CreateCommand();
            cmdOn.CommandText = "SET SHOWPLAN_XML ON";
            await cmdOn.ExecuteNonQueryAsync();

            await using var cmdPlan = connection.CreateCommand();
            cmdPlan.CommandText = literalSql;
            var result = await cmdPlan.ExecuteScalarAsync();

            await using var cmdOff = connection.CreateCommand();
            cmdOff.CommandText = "SET SHOWPLAN_XML OFF";
            await cmdOff.ExecuteNonQueryAsync();

            return result?.ToString() ?? "<no plan returned>";
        }
    }

    private static DbConnection CreateConnection(string connectionString)
    {
        // Try Microsoft.Data.SqlClient first, then System.Data.SqlClient
        var mdsType = Type.GetType("Microsoft.Data.SqlClient.SqlConnection, Microsoft.Data.SqlClient");
        if (mdsType != null)
            return (DbConnection)Activator.CreateInstance(mdsType, connectionString)!;

        var sdsType = Type.GetType("System.Data.SqlClient.SqlConnection, System.Data.SqlClient");
        if (sdsType != null)
            return (DbConnection)Activator.CreateInstance(sdsType, connectionString)!;

        throw new InvalidOperationException(
            "Cannot create a SQL connection. Ensure Microsoft.Data.SqlClient or System.Data.SqlClient is referenced.");
    }

    private sealed class ExplainRequest
    {
        public string? Sql { get; set; }
        public List<ExplainParameter>? Parameters { get; set; }
    }

    private sealed class ExplainParameter
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }

    private string BuildIndexHtml(ProfilingLogsOptions options)
    {
        var bundleJs = ReadEmbeddedBundle();
        var isDark = string.Equals(options.ColorScheme, "Dark", StringComparison.OrdinalIgnoreCase);
        var bgColor = isDark ? "#1e1e1e" : "#f5f7fb";
        var textColor = isDark ? "#d4d4d4" : "#111827";

        var optionsJson = JsonSerializer.Serialize(new
        {
            Scheme = options.ResolveScheme(),
            options.EnableVsCodeLinks,
            options.HideDefaultConnRows,
            options.EnableClearCacheButton,
            options.EnableHttpMethodColumn,
            CoffeeQrData = CoffeeAssets.QrDataUri,
            CoffeeUrl = CoffeeAssets.BuyMeACoffeeUrl,
            ClearPath = _clearPath,
            ApiResultsPath = _apiResultsPath,
            ApiExplainPath = _apiExplainPath,
            IsResultsIndex = true,
            IsDark = isDark
        }, JsonOpts);

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>Profiling Logs</title>
  <style>
    *, *::before, *::after { box-sizing: border-box; }
    html, body { height: 100%; margin: 0; }
    body { background: {{bgColor}}; color: {{textColor}}; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; }
  </style>
</head>
<body>
  <div id="pl-root"></div>
  <script>window.__PL_OPTIONS__ = {{optionsJson}};</script>
  <script>{{bundleJs}}</script>
</body>
</html>
""";
    }

    private static string ReadEmbeddedBundle()
    {
        var assembly = typeof(ProfilerIdeLinkMiddleware).Assembly;
        using var stream = assembly.GetManifestResourceStream("profilinglogs-ui.js");
        if (stream == null) return "/* profilinglogs-ui.js embedded resource not found */";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
