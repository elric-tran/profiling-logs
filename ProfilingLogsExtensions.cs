using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProfilingLogs.Internal;
using StackExchange.Profiling;
using StackExchange.Profiling.SqlFormatters;

namespace ProfilingLogs;

/// <summary>
/// Extension methods to enable ProfilingLogs (MiniProfiler + connection coloring + caller comment
/// + IDE deep-links) with just two lines in <c>Program.cs</c>.
/// </summary>
public static class ProfilingLogsExtensions
{
    /// <summary>
    /// Registers ProfilingLogs using code-first configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddProfilingLogs(o =>
    /// {
    ///     o.Enabled = builder.Environment.IsDevelopment();
    ///     o.Ide = ProfilingIde.VSCode;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddProfilingLogs(this IServiceCollection services, Action<ProfilingLogsOptions>? configure = null)
    {
        var options = new ProfilingLogsOptions();
        configure?.Invoke(options);
        return AddCore(services, options);
    }

    /// <summary>
    /// Registers ProfilingLogs, binding configuration from an <see cref="IConfiguration"/> section
    /// (e.g. a "ProfilingLogs" section in appsettings).
    /// </summary>
    public static IServiceCollection AddProfilingLogs(this IServiceCollection services, IConfiguration section)
    {
        var options = new ProfilingLogsOptions();
        section.Bind(options);
        return AddCore(services, options);
    }

    private static IServiceCollection AddCore(IServiceCollection services, ProfilingLogsOptions options)
    {
        // Always register the options so UseProfilingLogs can read the Enabled flag.
        services.AddSingleton(options);

        if (!options.Enabled)
        {
            return services;
        }

        // Custom storage so the results-index "Clear all" button can flush every stored result.
        var storage = new ClearableMemoryStorage();
        services.AddSingleton(storage);

        services.AddMiniProfiler(mp =>
        {
            mp.RouteBasePath = options.RouteBasePath;
            mp.ResultsAuthorize = _ => true;
            mp.SqlFormatter = new SqlServerFormatter();
            mp.ColorScheme = options.ColorScheme;
            mp.ShowControls = true;
            mp.PopupShowTimeWithChildren = true;
            mp.PopupRenderPosition = RenderPosition.BottomLeft;
            mp.PopupShowTrivial = true;
            mp.TrivialDurationThresholdMilliseconds = 1.0M;
            mp.Storage = storage;
        }).AddEntityFramework();

        return services;
    }

    /// <summary>
    /// Adds the MiniProfiler + ProfilingLogs middleware to the pipeline. No-op when <c>Enabled = false</c>.
    /// </summary>
    public static IApplicationBuilder UseProfilingLogs(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<ProfilingLogsOptions>();
        if (!options.Enabled)
        {
            return app;
        }

        if (options.EnableConnectionColors || options.EnableCallerComment)
        {
            DiagnosticListener.AllListeners.Subscribe(new ProfilingDiagnosticObserver(options));
        }

        if (options.EnableVsCodeLinks || options.HideDefaultConnRows || options.EnableClearCacheButton)
        {
            app.UseMiddleware<ProfilerIdeLinkMiddleware>(options);
        }

        app.UseMiniProfiler();

        // Runs downstream of MiniProfiler (inside the profiled pipeline). After the endpoint has
        // executed, MiniProfiler.Current is set and routing info is available, so we enrich the
        // profiler name with the HTTP verb. MiniProfiler's EnsureName only assigns a name when it
        // is still empty, so setting it here is preserved.
        if (options.EnableHttpMethodColumn)
        {
            app.Use(async (context, next) =>
            {
                await next();
                SetRequestName(context);
            });
        }

        return app;
    }

    private static void SetRequestName(HttpContext context)
    {
        var mp = MiniProfiler.Current;
        if (mp is null)
        {
            return;
        }

        var method = context.Request.Method;
        if (string.IsNullOrEmpty(method))
        {
            return;
        }

        var name = context.Request.Path.Value ?? string.Empty;
        var routeData = context.GetRouteData();
        if (routeData is not null && routeData.Values.TryGetValue("controller", out var controller) && controller is not null)
        {
            routeData.Values.TryGetValue("action", out var action);
            name = routeData.Values.TryGetValue("area", out var area) && area is not null && area.ToString()!.Length > 0
                ? $"{area}/{controller}/{action}"
                : $"{controller}/{action}";
        }
        else if (routeData is not null && routeData.Values.TryGetValue("page", out var page) && page is not null)
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

        // Minimal-API endpoint display names carry a "HTTP: GET /path" prefix; strip it so we
        // don't produce "GET HTTP: GET /path".
        if (name.StartsWith("HTTP: ", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring("HTTP: ".Length);
        }

        mp.Name = name.StartsWith(method + " ", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{method} {name}";
    }
}
