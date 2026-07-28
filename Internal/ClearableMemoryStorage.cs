using System.Collections.Concurrent;
using StackExchange.Profiling;
using StackExchange.Profiling.Storage;

namespace ProfilingLogs.Internal;

/// <summary>
/// A simple in-memory <see cref="IAsyncStorage"/> that keeps every captured <see cref="MiniProfiler"/>
/// in a <see cref="ConcurrentDictionary{TKey,TValue}"/> so the whole set can be wiped on demand via
/// <see cref="Clear"/>. The default <c>MemoryCacheStorage</c> exposes no way to flush all results,
/// which is what the "Clear all profiler results" button on the results-index page needs.
/// </summary>
internal sealed class ClearableMemoryStorage : IAsyncStorage
{
    private readonly ConcurrentDictionary<Guid, MiniProfiler> _profilers = new();
    private readonly ConcurrentDictionary<Guid, byte> _unviewed = new();

    /// <summary>Removes every stored profiling result.</summary>
    public void Clear()
    {
        _profilers.Clear();
        _unviewed.Clear();
    }

    public void Save(MiniProfiler profiler)
    {
        _profilers[profiler.Id] = profiler;
        _unviewed[profiler.Id] = 0;
    }

    public Task SaveAsync(MiniProfiler profiler)
    {
        Save(profiler);
        return Task.CompletedTask;
    }

    public MiniProfiler? Load(Guid id) => _profilers.TryGetValue(id, out var p) ? p : null;

    public Task<MiniProfiler?> LoadAsync(Guid id) => Task.FromResult(Load(id));

    public void SetViewed(string? user, Guid id) => _unviewed.TryRemove(id, out _);

    public Task SetViewedAsync(string? user, Guid id)
    {
        SetViewed(user, id);
        return Task.CompletedTask;
    }

    public void SetUnviewed(string? user, Guid id) => _unviewed[id] = 0;

    public Task SetUnviewedAsync(string? user, Guid id)
    {
        SetUnviewed(user, id);
        return Task.CompletedTask;
    }

    public List<Guid> GetUnviewedIds(string? user) => _unviewed.Keys.ToList();

    public Task<List<Guid>> GetUnviewedIdsAsync(string? user) => Task.FromResult(GetUnviewedIds(user));

    public IEnumerable<Guid> List(
        int maxResults,
        DateTime? start = null,
        DateTime? finish = null,
        ListResultsOrder orderBy = ListResultsOrder.Descending)
    {
        IEnumerable<MiniProfiler> query = _profilers.Values;

        if (start is not null)
        {
            query = query.Where(p => p.Started >= start.Value);
        }

        if (finish is not null)
        {
            query = query.Where(p => p.Started <= finish.Value);
        }

        query = orderBy == ListResultsOrder.Ascending
            ? query.OrderBy(p => p.Started)
            : query.OrderByDescending(p => p.Started);

        return query.Take(maxResults).Select(p => p.Id).ToList();
    }

    public Task<IEnumerable<Guid>> ListAsync(
        int maxResults,
        DateTime? start = null,
        DateTime? finish = null,
        ListResultsOrder orderBy = ListResultsOrder.Descending)
        => Task.FromResult(List(maxResults, start, finish, orderBy));
}
