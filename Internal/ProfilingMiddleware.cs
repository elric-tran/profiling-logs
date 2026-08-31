using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;

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
        string? body = null;
        if (context.Request.ContentLength is > 0)
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }

        var request = new ProfiledRequest
        {
            Version = _store.AppVersion,
            Method = context.Request.Method,
            Name = context.Request.Path.Value ?? string.Empty,
            Url = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}",
            Authorization = context.Request.Headers["Authorization"].ToString(),
            ContentType = context.Request.ContentType,
            Body = body,
            Headers = context.Request.Headers
                .Where(h => !string.IsNullOrWhiteSpace(h.Key))
                .ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase)
        };
        ProfilingStore.CurrentRequest.Value = request;

        var sw = Stopwatch.StartNew();
        Exception? capturedException = null;
        var originalResponseBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            capturedException = ex;
            request.StatusCode = StatusCodes.Status500InternalServerError;
            request.ErrorMessage = BuildConsoleErrorMessage(context, request, ex);
            throw;
        }
        finally
        {
            try
            {
                context.Response.Body = originalResponseBody;
                responseBuffer.Position = 0;
                string? responseBodyText = null;
                if (responseBuffer.Length > 0)
                {
                    using var reader = new StreamReader(responseBuffer, Encoding.UTF8, leaveOpen: true);
                    responseBodyText = await reader.ReadToEndAsync();
                    responseBuffer.Position = 0;
                    await responseBuffer.CopyToAsync(originalResponseBody);
                    responseBuffer.Position = 0;
                }

                sw.Stop();
                request.DurationMs = sw.Elapsed.TotalMilliseconds;
                request.StatusCode = context.Response.StatusCode;
                var featureException = context.Features.Get<IExceptionHandlerFeature>()?.Error
                    ?? context.Features.Get<IExceptionHandlerPathFeature>()?.Error;

                if (capturedException != null && string.IsNullOrWhiteSpace(request.ErrorMessage))
                {
                    request.ErrorMessage = BuildConsoleErrorMessage(context, request, capturedException);
                }
                else if (featureException != null && string.IsNullOrWhiteSpace(request.ErrorMessage))
                {
                    request.ErrorMessage = BuildConsoleErrorMessage(context, request, featureException);
                }
                else if (context.Response.StatusCode >= StatusCodes.Status400BadRequest
                         && string.IsNullOrWhiteSpace(request.ErrorMessage))
                {
                    var reason = ReasonPhrases.GetReasonPhrase(context.Response.StatusCode);
                    var parsedError = TryExtractErrorMessage(responseBodyText);
                    request.ErrorMessage = !string.IsNullOrWhiteSpace(parsedError)
                        ? parsedError
                        : string.IsNullOrWhiteSpace(reason)
                            ? $"HTTP {context.Response.StatusCode}"
                            : $"HTTP {context.Response.StatusCode} {reason}";
                }

                if (context.Response.StatusCode >= StatusCodes.Status400BadRequest
                    && !string.IsNullOrWhiteSpace(responseBodyText))
                {
                    request.ResponseBody = TrimForStorage(responseBodyText);
                }

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

    private static string? TryExtractErrorMessage(string? responseBodyText)
    {
        if (string.IsNullOrWhiteSpace(responseBodyText))
        {
            return null;
        }

        var trimmed = responseBodyText.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "error", "message", "detail", "title", "errorMessage", "exceptionMessage" })
                {
                    if (doc.RootElement.TryGetProperty(key, out var prop)
                        && prop.ValueKind == JsonValueKind.String)
                    {
                        var value = prop.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }

                if (doc.RootElement.TryGetProperty("errors", out var errors))
                {
                    var text = errors.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed JSON and fall back to raw text.
        }

        return trimmed.Length > 1000 ? trimmed[..1000] + "..." : trimmed;
    }

    private static string TrimForStorage(string text, int maxLength = 4000)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "...";
    }

    private static string BuildConsoleErrorMessage(HttpContext context, ProfiledRequest request, Exception exception)
    {
        var actionDescriptor = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (actionDescriptor?.ControllerTypeInfo?.FullName is { Length: > 0 } controllerType)
        {
            var actionName = !string.IsNullOrWhiteSpace(actionDescriptor.ActionName)
                ? actionDescriptor.ActionName
                : request.Name;
            return $"fail: {controllerType}[0]\n      {actionName}\n      {exception}";
        }

        var endpointName = context.GetEndpoint()?.DisplayName;
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            endpointName = request.Name;
        }

        var actionLine = string.IsNullOrWhiteSpace(request.Name) ? endpointName : request.Name;
        return $"fail: {endpointName}[0]\n      {actionLine}\n      {exception}";
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
