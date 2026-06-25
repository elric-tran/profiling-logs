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
    private readonly string _resultsPath;
    private readonly string _script;

    public ProfilerIdeLinkMiddleware(RequestDelegate next, ProfilingLogsOptions options)
    {
        _next = next;
        var basePath = string.IsNullOrWhiteSpace(options.RouteBasePath) ? "/profiler" : options.RouteBasePath.TrimEnd('/');
        _resultsPath = basePath + "/results";
        _script = BuildScript(options);
    }

    private static string BuildScript(ProfilingLogsOptions options)
    {
        var scheme = options.ResolveScheme();
        var doLinkify = options.EnableVsCodeLinks ? "true" : "false";
        var doHide = options.HideDefaultConnRows ? "true" : "false";

        const string template = """
<script>
(function () {
    var doLinkify = __LINKIFY__;
    var doHide = __HIDE__;
    var rxTest = /__SCHEME__:\/\/[^\s<>"']+/;
    var rxAll = /__SCHEME__:\/\/[^\s<>"']+/g;
    var defaultConnRx = /^sql\s*-\s*(Open|Close)/i;

    function linkify(root) {
        if (!root || !root.querySelectorAll) return;
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null, false);
        var targets = [];
        while (walker.nextNode()) {
            var n = walker.currentNode;
            if (n.parentNode && n.parentNode.nodeName !== 'A' && rxTest.test(n.nodeValue)) {
                targets.push(n);
            }
        }
        targets.forEach(function (n) {
            var span = document.createElement('span');
            span.innerHTML = n.nodeValue.replace(rxAll, function (m) {
                return '<a href="' + m + '" title="Open in IDE" style="color:#3794ff;text-decoration:underline;cursor:pointer">' + m + '</a>';
            });
            n.parentNode.replaceChild(span, n);
        });
    }

    function hideDefaultConn(root) {
        if (!root) return;
        var rows = [];
        if (root.matches && root.matches('tr[data-timing-id]')) rows.push(root);
        if (root.querySelectorAll) {
            Array.prototype.push.apply(rows, root.querySelectorAll('tr[data-timing-id]'));
        }
        rows.forEach(function (tr) {
            var ct = tr.querySelector('.mp-call-type');
            if (ct && defaultConnRx.test((ct.textContent || '').trim())) {
                tr.style.display = 'none';
            }
        });
    }

    function process(root) {
        if (doLinkify) linkify(root);
        if (doHide) hideDefaultConn(root);
    }

    function init() {
        process(document.body);
        var obs = new MutationObserver(function (muts) {
            muts.forEach(function (m) {
                Array.prototype.forEach.call(m.addedNodes, function (nd) {
                    if (nd.nodeType === 1) process(nd);
                });
            });
        });
        obs.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
</script>
""";

        return template
            .Replace("__LINKIFY__", doLinkify)
            .Replace("__HIDE__", doHide)
            .Replace("__SCHEME__", scheme);
    }

    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.Contains(_resultsPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

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

                html = html.Contains("</body>", StringComparison.OrdinalIgnoreCase)
                    ? html.Replace("</body>", _script + "</body>")
                    : html + _script;

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
