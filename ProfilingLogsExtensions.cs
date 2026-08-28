using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProfilingLogs.Internal;

namespace ProfilingLogs;

/// <summary>
/// Extension methods to enable ProfilingLogs (standalone SQL profiling + connection coloring +
/// caller comment + IDE deep-links) with just two lines in <c>Program.cs</c>.
/// </summary>
public static class ProfilingLogsExtensions
{
    /// <summary>
    /// Registers ProfilingLogs using code-first configuration.
    /// </summary>
    public static IServiceCollection AddProfilingLogs(this IServiceCollection services, Action<ProfilingLogsOptions>? configure = null)
    {
        var options = new ProfilingLogsOptions();
        configure?.Invoke(options);
        return AddCore(services, options);
    }

    /// <summary>
    /// Registers ProfilingLogs, binding configuration from an <see cref="IConfiguration"/> section.
    /// </summary>
    public static IServiceCollection AddProfilingLogs(this IServiceCollection services, IConfiguration section)
    {
        var options = new ProfilingLogsOptions();
        section.Bind(options);
        return AddCore(services, options);
    }

    private static IServiceCollection AddCore(IServiceCollection services, ProfilingLogsOptions options)
    {
        services.AddSingleton(options);

        if (!options.Enabled)
        {
            return services;
        }

        services.AddSingleton<ProfilingStore>();

        return services;
    }

    /// <summary>
    /// Adds the ProfilingLogs middleware to the pipeline. No-op when <c>Enabled = false</c>.
    /// </summary>
    public static IApplicationBuilder UseProfilingLogs(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<ProfilingLogsOptions>();
        if (!options.Enabled)
        {
            return app;
        }

        var store = app.ApplicationServices.GetRequiredService<ProfilingStore>();

        DiagnosticListener.AllListeners.Subscribe(new ProfilingDiagnosticObserver(options, store));

        // Serve the profiler UI and JSON API
        app.UseMiddleware<ProfilerIdeLinkMiddleware>(options);

        // Per-request timing + route name enrichment
        app.UseMiddleware<ProfilingMiddleware>();

        return app;
    }
}
