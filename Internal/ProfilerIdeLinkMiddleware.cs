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
    private readonly string _apiIndexMetadataPath;
    private readonly string _apiExplainPath;
    private readonly string _apiReplyPathSuffix = "/reply";
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
        _apiIndexMetadataPath = _basePath + "/api/index-metadata";
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
                r.Id, r.Version, r.Name, r.Url, r.Method, r.Started, r.DurationMs, r.StatusCode, r.ErrorMessage,
                QueryCount = r.Queries.Count
            });
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOpts));
            return;
        }

        if (path.StartsWith(_apiResultsPath + "/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(_apiReplyPathSuffix, StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsPost(context.Request.Method))
        {
            var idStr = path.Substring(_apiResultsPath.Length + 1, path.Length - _apiResultsPath.Length - 1 - _apiReplyPathSuffix.Length);
            if (Guid.TryParse(idStr, out var id))
            {
                var result = _store.Get(id);
                if (result != null)
                {
                    await HandleReply(context, result);
                    return;
                }
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
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
                        result.Id, result.Version, result.Name, result.Url, result.Method, result.Started, result.ErrorMessage,
                        result.DurationMs, result.StatusCode, result.ResponseBody, result.Headers,
                        Queries = result.Queries.OrderBy(q => q.Sequence).Select(q => new
                        {
                            q.Sequence, q.Sql, q.DurationMs, q.ErrorMessage, q.CallerFile, q.CallerLine,
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

        if (path.Equals(_apiIndexMetadataPath, StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsPost(context.Request.Method))
        {
            await HandleIndexMetadata(context);
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
            List<ProfiledRequest>? keepRequests = null;
            if (context.Request.ContentLength is > 0)
            {
                try
                {
                    keepRequests = await JsonSerializer.DeserializeAsync<List<ProfiledRequest>>(context.Request.Body, JsonOpts);
                }
                catch
                {
                    keepRequests = null;
                }
            }

            if (keepRequests is { Count: > 0 })
                _store.ReplaceAll(keepRequests);
            else
                _store.Clear();

            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        await _next(context);
    }

    private async Task HandleReply(HttpContext context, ProfiledRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Missing request URL." }, JsonOpts));
            return;
        }

        try
        {
            using var client = new HttpClient();
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);

            if (!string.IsNullOrWhiteSpace(request.Authorization))
            {
                message.Headers.TryAddWithoutValidation("Authorization", request.Authorization);
            }

            if (!string.IsNullOrWhiteSpace(request.Body) && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                var mediaType = request.ContentType ?? "application/json";
                var charsetIdx = mediaType.IndexOf(';');
                if (charsetIdx >= 0)
                {
                    mediaType = mediaType.Substring(0, charsetIdx).Trim();
                }

                if (string.IsNullOrWhiteSpace(mediaType))
                {
                    mediaType = "application/json";
                }

                message.Content = new StringContent(
                    request.Body,
                    Encoding.UTF8,
                    mediaType);
            }

            using var response = await client.SendAsync(message);
            var responseBody = await response.Content.ReadAsStringAsync();

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                ok = response.IsSuccessStatusCode,
                statusCode = (int)response.StatusCode,
                reasonPhrase = response.ReasonPhrase,
                body = responseBody
            }, JsonOpts));
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts));
        }
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

    private async Task HandleIndexMetadata(HttpContext context)
    {
        var connectionString = _store.CapturedConnectionString;
        if (string.IsNullOrEmpty(connectionString))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "No database connection string captured yet. Execute at least one query first." }, JsonOpts));
            return;
        }

        IndexMetadataRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<IndexMetadataRequest>(context.Request.Body, JsonOpts);
        }
        catch
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var tableName = body?.TableName;
        if (string.IsNullOrWhiteSpace(tableName))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Missing 'tableName' field." }, JsonOpts));
            return;
        }

        try
        {
            var metadata = await GetTableMetadata(connectionString, tableName);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(metadata, JsonOpts));
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

    private static async Task<TableMetadataResponse> GetTableMetadata(string connectionString, string tableName)
    {
        var (schemaName, pureTableName) = ParseTableName(tableName);
        var connection = CreateConnection(connectionString);
        await using (connection)
        {
            await connection.OpenAsync();

            var columns = await LoadColumns(connection, schemaName, pureTableName);
            var indexes = await LoadIndexes(connection, schemaName, pureTableName);

            return new TableMetadataResponse
            {
                DatabaseName = connection.Database ?? string.Empty,
                SchemaName = schemaName,
                TableName = pureTableName,
                Columns = columns,
                Indexes = indexes
            };
        }
    }

    private static async Task<List<TableColumnMetadata>> LoadColumns(DbConnection connection, string schemaName, string tableName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
SELECT c.column_id, c.name AS column_name, ty.name AS data_type, c.max_length, c.precision, c.scale, c.is_nullable,
       c.is_identity, c.is_computed
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON c.user_type_id = ty.user_type_id
WHERE s.name = @schemaName AND t.name = @tableName
ORDER BY c.column_id;
""";

        AddParameter(cmd, "@schemaName", schemaName);
        AddParameter(cmd, "@tableName", tableName);

        var items = new List<TableColumnMetadata>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new TableColumnMetadata
            {
                Ordinal = reader.GetInt32(0),
                Name = reader.GetString(1),
                DataType = reader.GetString(2),
                MaxLength = reader.GetInt16(3),
                Precision = reader.GetByte(4),
                Scale = reader.GetByte(5),
                IsNullable = reader.GetBoolean(6),
                IsIdentity = reader.GetBoolean(7),
                IsComputed = reader.GetBoolean(8)
            });
        }

        return items;
    }

    private static async Task<List<TableIndexMetadata>> LoadIndexes(DbConnection connection, string schemaName, string tableName)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
SELECT i.index_id, i.name, i.type_desc, i.is_primary_key, i.is_unique, i.is_unique_constraint,
       ic.key_ordinal, ic.is_included_column, ic.is_descending_key, c.name AS column_name, ic.index_column_id,
       sp.last_updated, sp.[rows] AS stats_rows, sp.rows_sampled, p.reserved_pages
FROM sys.tables t
JOIN sys.schemas s ON t.schema_id = s.schema_id
JOIN sys.indexes i ON i.object_id = t.object_id
LEFT JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
LEFT JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
OUTER APPLY sys.dm_db_stats_properties(i.object_id, i.index_id) sp
LEFT JOIN (
    SELECT object_id, index_id, SUM(reserved_page_count) AS reserved_pages
    FROM sys.dm_db_partition_stats
    GROUP BY object_id, index_id
) p ON p.object_id = i.object_id AND p.index_id = i.index_id
WHERE s.name = @schemaName AND t.name = @tableName AND i.name IS NOT NULL
ORDER BY i.index_id, ic.key_ordinal, ic.index_column_id;
""";

        AddParameter(cmd, "@schemaName", schemaName);
        AddParameter(cmd, "@tableName", tableName);

        var map = new Dictionary<string, TableIndexMetadata>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var indexId = reader.GetInt32(0);
            var indexName = reader.GetString(1);
            if (!map.TryGetValue(indexName, out var index))
            {
                index = new TableIndexMetadata
                {
                    IndexId = indexId,
                    Name = indexName,
                    TypeDesc = reader.GetString(2),
                    IsPrimaryKey = reader.GetBoolean(3),
                    IsUnique = reader.GetBoolean(4),
                    IsUniqueConstraint = reader.GetBoolean(5),
                    Columns = new List<TableIndexColumnMetadata>(),
                    LastStatisticsUpdate = reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    StatsRows = reader.IsDBNull(12) ? null : reader.GetInt64(12),
                    RowsSampled = reader.IsDBNull(13) ? null : reader.GetInt64(13),
                    EstimatedSizeMb = reader.IsDBNull(14) ? null : Math.Round((reader.GetInt64(14) * 8.0) / 1024.0, 2)
                };
                map[indexName] = index;
            }

            if (!reader.IsDBNull(9))
            {
                index.Columns.Add(new TableIndexColumnMetadata
                {
                    Name = reader.GetString(9),
                    KeyOrdinal = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    IsIncluded = reader.GetBoolean(7),
                    IsDescending = reader.GetBoolean(8)
                });
            }
        }

        return map.Values
            .Select(i =>
            {
                i.Columns = i.Columns
                    .OrderBy(c => c.IsIncluded ? int.MaxValue : c.KeyOrdinal ?? int.MaxValue)
                    .ThenBy(c => c.Name)
                    .ToList();
                return i;
            })
            .OrderByDescending(i => i.IsPrimaryKey)
            .ThenBy(i => i.IndexId)
            .ToList();
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static (string SchemaName, string TableName) ParseTableName(string tableName)
    {
        var trimmed = tableName.Trim().Replace("[", string.Empty).Replace("]", string.Empty);
        var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            >= 3 => (parts[^2], parts[^1]),
            2 => (parts[0], parts[1]),
            1 => ("dbo", parts[0]),
            _ => ("dbo", trimmed)
        };
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

    private sealed class IndexMetadataRequest
    {
        public string? TableName { get; set; }
    }

    private sealed class TableMetadataResponse
    {
        public string DatabaseName { get; set; } = string.Empty;
        public string SchemaName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public List<TableColumnMetadata> Columns { get; set; } = [];
        public List<TableIndexMetadata> Indexes { get; set; } = [];
    }

    private sealed class TableColumnMetadata
    {
        public int Ordinal { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public short MaxLength { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public bool IsNullable { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
    }

    private sealed class TableIndexMetadata
    {
        public int IndexId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TypeDesc { get; set; } = string.Empty;
        public bool IsPrimaryKey { get; set; }
        public bool IsUnique { get; set; }
        public bool IsUniqueConstraint { get; set; }
        public DateTime? LastStatisticsUpdate { get; set; }
        public long? StatsRows { get; set; }
        public long? RowsSampled { get; set; }
        public double? EstimatedSizeMb { get; set; }
        public List<TableIndexColumnMetadata> Columns { get; set; } = [];
    }

    private sealed class TableIndexColumnMetadata
    {
        public string Name { get; set; } = string.Empty;
        public int? KeyOrdinal { get; set; }
        public bool IsIncluded { get; set; }
        public bool IsDescending { get; set; }
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
            ApiIndexMetadataPath = _apiIndexMetadataPath,
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
