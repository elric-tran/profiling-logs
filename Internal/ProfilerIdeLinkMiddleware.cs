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
    private readonly ProfilingLogsOptions _options;
    private readonly ClearableMemoryStorage? _storage;
    private readonly string _resultsPath;
    private readonly string _resultsIndexPath;
    private readonly string _clearPath;
    private readonly string _script;
    private readonly string _clearButtonScript;

    public ProfilerIdeLinkMiddleware(RequestDelegate next, ProfilingLogsOptions options, ClearableMemoryStorage? storage = null)
    {
        _next = next;
        _options = options;
        _storage = storage;
        var basePath = string.IsNullOrWhiteSpace(options.RouteBasePath) ? "/profiler" : options.RouteBasePath.TrimEnd('/');
        _resultsPath = basePath + "/results";
        _resultsIndexPath = basePath + "/results-index";
        _clearPath = basePath + "/clear-cache";
        _script = BuildScript(options);
        _clearButtonScript = BuildClearButtonScript(_clearPath);
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

    private static string BuildClearButtonScript(string clearPath)
    {
        const string template = """
<script>
(function () {
    function addButton() {
        if (document.getElementById('pl-clear-cache-btn')) return;
        var btn = document.createElement('button');
        btn.id = 'pl-clear-cache-btn';
        btn.type = 'button';
        btn.textContent = '🗑 Clear all profiler results';
        btn.style.cssText = 'position:fixed;top:10px;right:10px;z-index:2147483647;padding:8px 14px;' +
            'background:#c0392b;color:#fff;border:none;border-radius:4px;cursor:pointer;' +
            'font:13px/1.2 sans-serif;box-shadow:0 1px 4px rgba(0,0,0,.3)';
        btn.addEventListener('click', function () {
            if (!window.confirm('Clear ALL stored profiler results (every captured API call)?')) return;
            btn.disabled = true;
            btn.textContent = 'Clearing…';
            fetch('__CLEARPATH__', { method: 'POST', headers: { 'X-Requested-With': 'fetch' } })
                .then(function () { window.location.reload(); })
                .catch(function () { btn.disabled = false; btn.textContent = '🗑 Clear all profiler results'; alert('Clear failed.'); });
        });
        document.body.appendChild(btn);
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', addButton);
    } else {
        addButton();
    }
})();
</script>
""";

        return template.Replace("__CLEARPATH__", clearPath);
    }

    public async Task Invoke(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Handle the "clear all results" endpoint before anything else.
        if (_options.EnableClearCacheButton
            && path.Equals(_clearPath, StringComparison.OrdinalIgnoreCase))
        {
            _storage?.Clear();
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        if (!path.Contains(_resultsPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var isResultsIndex = path.Contains(_resultsIndexPath, StringComparison.OrdinalIgnoreCase);

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

                var injection = _script;
                if (isResultsIndex && _options.EnableClearCacheButton)
                {
                    injection += _clearButtonScript;
                }

                html = html.Contains("</body>", StringComparison.OrdinalIgnoreCase)
                    ? html.Replace("</body>", injection + "</body>")
                    : html + injection;

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
