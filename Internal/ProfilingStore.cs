using System.Collections.Concurrent;
using System.Text.Json;

namespace ProfilingLogs.Internal;

internal sealed record ProfiledParameter(string Name, string SqlType, string? Value);

internal sealed class ProfiledQuery
{
    public long Sequence { get; init; }
    public string Sql { get; init; } = string.Empty;
    public double DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CallerFile { get; init; }
    public int CallerLine { get; init; }
    public string? CallerMethod { get; init; }
    public string? IdeLink { get; init; }
    public string? ConnColor { get; init; }
    public string? ConnId { get; init; }
    public string? ConnEvent { get; init; }
    public List<ProfiledParameter>? Parameters { get; init; }
}

internal sealed class ProfiledRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Authorization { get; set; }
    public string? ContentType { get; set; }
    public string? Body { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ErrorMessage { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime Started { get; set; } = DateTime.UtcNow;
    public double DurationMs { get; set; }
    public int StatusCode { get; set; }
    public ConcurrentBag<ProfiledQuery> Queries { get; } = new();
    public ConcurrentBag<string> ConnectionEvents { get; } = new();
}

/// <summary>
/// Standalone in-memory store for profiled HTTP requests and their SQL queries.
/// </summary>
internal sealed class ProfilingStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly object _fileLock = new();
    private readonly string _storePath = Path.Combine(AppContext.BaseDirectory, "profilinglogs-store.json");
    private readonly ConcurrentDictionary<Guid, ProfiledRequest> _requests = new();

    public string AppVersion { get; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    /// <summary>The currently active request for each async flow (set by <see cref="ProfilingMiddleware"/>).</summary>
    internal static readonly AsyncLocal<ProfiledRequest?> CurrentRequest = new();

    /// <summary>Last observed connection string (captured from DiagnosticSource).</summary>
    internal volatile string? CapturedConnectionString;

    public ProfilingStore()
    {
        LoadFromDisk();
    }

    public void Save(ProfiledRequest request)
    {
        _requests[request.Id] = request;
        Persist();
    }

    public ProfiledRequest? Get(Guid id) =>
        _requests.TryGetValue(id, out var r) ? r : null;

    public IReadOnlyList<ProfiledRequest> List(int maxResults = 200) =>
        _requests.Values
            .OrderByDescending(r => r.Started)
            .Take(maxResults)
            .ToList();

    public void Clear()
    {
        _requests.Clear();
        Persist();
    }

    public void ReplaceAll(IEnumerable<ProfiledRequest> requests)
    {
        _requests.Clear();
        foreach (var request in requests)
        {
            _requests[request.Id] = request;
        }

        Persist();
    }

    public int Count => _requests.Count;

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return;
            }

            var json = File.ReadAllText(_storePath);
            var items = JsonSerializer.Deserialize<List<ProfiledRequest>>(json, JsonOpts) ?? [];
            foreach (var request in items)
            {
                _requests[request.Id] = request;
            }
        }
        catch
        {
            // File persistence must never block profiling.
        }
    }

    private void Persist()
    {
        try
        {
            lock (_fileLock)
            {
                var snapshot = _requests.Values
                    .OrderByDescending(r => r.Started)
                    .ToList();
                File.WriteAllText(_storePath, JsonSerializer.Serialize(snapshot, JsonOpts));
            }
        }
        catch
        {
            // File persistence must never block profiling.
        }
    }
}
