using System.Collections.Concurrent;

namespace ProfilingLogs.Internal;

internal sealed record ProfiledParameter(string Name, string SqlType, string? Value);

internal sealed record ProfiledQuery(
    string Sql,
    double DurationMs,
    string? CallerFile,
    int CallerLine,
    string? CallerMethod,
    string? IdeLink,
    string? ConnColor,
    string? ConnId,
    string? ConnEvent,
    List<ProfiledParameter>? Parameters);

internal sealed class ProfiledRequest
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public DateTime Started { get; } = DateTime.UtcNow;
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
    private readonly ConcurrentDictionary<Guid, ProfiledRequest> _requests = new();

    /// <summary>The currently active request for each async flow (set by <see cref="ProfilingMiddleware"/>).</summary>
    internal static readonly AsyncLocal<ProfiledRequest?> CurrentRequest = new();

    /// <summary>Last observed connection string (captured from DiagnosticSource).</summary>
    internal volatile string? CapturedConnectionString;

    public void Save(ProfiledRequest request)
    {
        _requests[request.Id] = request;
    }

    public ProfiledRequest? Get(Guid id) =>
        _requests.TryGetValue(id, out var r) ? r : null;

    public IReadOnlyList<ProfiledRequest> List(int maxResults = 200) =>
        _requests.Values
            .OrderByDescending(r => r.Started)
            .Take(maxResults)
            .ToList();

    public void Clear() => _requests.Clear();

    public int Count => _requests.Count;
}
