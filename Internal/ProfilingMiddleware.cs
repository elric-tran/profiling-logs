using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ProfilingLogs.Internal;

/// <summary>
/// Lightweight middleware that times each HTTP request and stores the result in <see cref="ProfilingStore"/>.
/// Sets <see cref="ProfilingStore.CurrentRequest"/> via <see cref="AsyncLocal{T}"/> so the
/// <see cref="ProfilingConnectionTracker"/> can attach SQL queries to the active request.
/// </summary>
internal sealed class ProfilingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ProfilingStore _store;
    public ProfilingMiddleware(RequestDelegate next, ProfilingStore store, ProfilingLogsOptions options)
    {
        _next = next;
        _store = store;
    }

    public async Task Invoke(HttpContext context)
    {
        var request = new ProfiledRequest
        {
            Method = context.Request.Method,
            Name = context.Request.Path.Value ?? string.Empty
        };
        ProfilingStore.CurrentRequest.Value = request;

        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            try
            {
                sw.Stop();
                request.DurationMs = sw.Elapsed.TotalMilliseconds;
                request.StatusCode = context.Response.StatusCode;

                EnrichRequestName(context, request);

                _store.Save(request);
            }
            catch
            {
                // Profiling must never interfere with the real request pipeline
            }
            finally
            {
                ProfilingStore.CurrentRequest.Value = null;
            }
        }
    }

    private static void EnrichRequestName(HttpContext context, ProfiledRequest request)
    {
        var name = context.Request.Path.Value ?? string.Empty;
        var routeData = context.GetRouteData();

        if (routeData?.Values.TryGetValue("controller", out var controller) == true && controller is not null)
        {
            routeData.Values.TryGetValue("action", out var action);
            name = routeData.Values.TryGetValue("area", out var area) == true
                   && area is not null && area.ToString()!.Length > 0
                ? $"{area}/{controller}/{action}"
                : $"{controller}/{action}";
        }
        else if (routeData?.Values.TryGetValue("page", out var page) == true && page is not null)
        {
            name = page.ToString() ?? name;
        }
        else
        {
            var endpoint = context.GetEndpoint();
            if (endpoint is not null && !string.IsNullOrEmpty(endpoint.DisplayName))
            {
                name = endpoint.DisplayName;
            }
        }

        if (name.StartsWith("HTTP: ", StringComparison.OrdinalIgnoreCase))
            name = name.Substring("HTTP: ".Length);

        var method = request.Method;
        if (!string.IsNullOrEmpty(method) && name.StartsWith(method + " ", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(method.Length + 1);

        request.Name = name;
    }
}
