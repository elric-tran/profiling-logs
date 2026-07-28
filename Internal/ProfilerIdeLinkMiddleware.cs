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
    private readonly string _resultsIndexScript;
    private readonly string _footerScript;

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
        _resultsIndexScript = BuildResultsIndexScript(options.EnableHttpMethodColumn);
        _footerScript = BuildFooterScript();
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
                // The SQL formatter can place a statement terminator (or other punctuation)
                // directly after the deep-link, e.g. "vscode://file/...:194;". Trailing
                // punctuation is not part of the URL, so strip it from the href (and keep it
                // as plain text) - otherwise the link is malformed and the IDE won't open it.
                var trail = '';
                var t = m.match(/[;,.)\]}>]+$/);
                if (t) { trail = t[0]; m = m.slice(0, m.length - trail.length); }
                return '<a href="' + m + '" title="Open in IDE" style="color:#3794ff;text-decoration:underline;cursor:pointer">' + m + '</a>' + trail;
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

    private static string BuildResultsIndexScript(bool enableMethodColumn)
    {
        // MiniProfiler ships the results-index table with an EMPTY <tbody> and fills it
        // client-side (via /results-list JSON, polled every ~4s, inserted with
        // insertAdjacentHTML("afterbegin", ...)). So we must:
        //  - tolerate the table/rows not existing yet at load time,
        //  - re-apply the active per-column filter whenever new rows are injected,
        //  - keep the header (with sort + search inputs) pinned above the injected rows.
        const string template = """
<script>
(function () {
    var table = null;
    var inputs = [];
    var sort = { col: -1, dir: 1 };
    var scheduled = false;
    var addMethodCol = __METHODCOL__;
    var VERBS = { GET: 1, POST: 1, PUT: 1, DELETE: 1, PATCH: 1, HEAD: 1, OPTIONS: 1, TRACE: 1, CONNECT: 1 };

    function getTable() {
        var t = document.querySelector('table.mp-results-index');
        if (t) return t;
        var all = document.querySelectorAll('table');
        for (var i = 0; i < all.length; i++) {
            if (all[i].tHead && all[i].tHead.rows.length) return all[i];
        }
        return null;
    }

    function dataRows() {
        if (!table) return [];
        var all = table.querySelectorAll('tr');
        var out = [];
        for (var i = 0; i < all.length; i++) {
            var tr = all[i];
            if (table.tHead && table.tHead.contains(tr)) continue;
            if (tr.className && String(tr.className).indexOf('pl-filter-row') !== -1) continue;
            out.push(tr);
        }
        return out;
    }

    function cellText(row, idx) {
        var c = row.cells[idx];
        return c ? (c.textContent || '').trim() : '';
    }

    function decorateRows() {
        if (!table) return;
        var rows = dataRows();
        for (var i = 0; i < rows.length; i++) {
            var row = rows[i];
            if (row.getAttribute('data-pl-m')) continue;
            row.setAttribute('data-pl-m', '1');

            // Drop the trailing "Dom Complete" value to match the removed header column.
            var none = row.querySelector('td.mp-results-none');
            if (none && none.colSpan > 1) {
                none.colSpan = none.colSpan - 1;
            } else if (row.cells.length >= 7) {
                row.deleteCell(row.cells.length - 1);
            }

            if (!addMethodCol) continue;
            var nameCell = row.cells[0];
            var txt = nameCell ? (nameCell.textContent || '').trim() : '';
            var sp = txt.indexOf(' ');
            var verb = sp > 0 ? txt.slice(0, sp) : txt;
            var method = '';
            if (VERBS[verb.toUpperCase()]) {
                method = verb.toUpperCase();
                var rest = sp > 0 ? txt.slice(sp + 1) : '';
                var a = nameCell ? nameCell.querySelector('a') : null;
                if (a) a.textContent = rest;
                else if (nameCell) nameCell.textContent = rest;
            }
            var td = document.createElement('td');
            td.textContent = method;
            td.style.fontWeight = '600';
            row.insertBefore(td, row.cells[1] || null);
        }
    }

    function applyFilters() {
        decorateRows();
        if (!inputs.length) return;
        var terms = inputs.map(function (i) { return i.value.toLowerCase(); });
        var active = terms.some(function (t) { return t !== ''; });
        dataRows().forEach(function (row) {
            var show = true;
            if (active) {
                for (var c = 0; c < terms.length; c++) {
                    if (terms[c] && cellText(row, c).toLowerCase().indexOf(terms[c]) === -1) {
                        show = false; break;
                    }
                }
            }
            row.style.display = show ? '' : 'none';
        });
    }

    function compare(a, b) {
        var na = parseFloat(a.replace(/[^0-9.\-]/g, ''));
        var nb = parseFloat(b.replace(/[^0-9.\-]/g, ''));
        var aNum = a !== '' && /[0-9]/.test(a) && !isNaN(na);
        var bNum = b !== '' && /[0-9]/.test(b) && !isNaN(nb);
        if (aNum && bNum) return na - nb;
        return a.localeCompare(b);
    }

    function updateSortIcons(idx) {
        var ths = table.tHead.rows[0].cells;
        for (var k = 0; k < ths.length; k++) {
            var ic = ths[k].querySelector('.pl-sort-icon');
            if (!ic) continue;
            if (k === idx) { ic.textContent = sort.dir === 1 ? ' \u25B2' : ' \u25BC'; ic.style.opacity = '1'; }
            else { ic.textContent = ' \u2195'; ic.style.opacity = '0.45'; }
        }
    }

    function doSort(idx) {
        decorateRows();
        if (sort.col === idx) sort.dir = -sort.dir;
        else { sort.col = idx; sort.dir = 1; }
        var rows = dataRows();
        if (!rows.length) return;
        var parent = rows[0].parentNode;
        rows.sort(function (r1, r2) {
            return compare(cellText(r1, idx), cellText(r2, idx)) * sort.dir;
        });
        rows.forEach(function (r) { parent.appendChild(r); });
        updateSortIcons(idx);
        applyFilters();
    }

    function keepHeaderFirst() {
        if (table && table.tHead && table.firstChild !== table.tHead) {
            table.insertBefore(table.tHead, table.firstChild);
        }
    }

    function schedule() {
        if (scheduled) return;
        scheduled = true;
        setTimeout(function () { scheduled = false; keepHeaderFirst(); applyFilters(); }, 50);
    }

    function addTitle() {
        if (document.getElementById('pl-title')) return;
        var h = document.createElement('h1');
        h.id = 'pl-title';
        h.textContent = 'Profiling Logs';
        h.style.cssText = 'text-align:center;font-size:2.4rem;font-weight:700;' +
            'margin:28px auto 18px;font-family:sans-serif;color:#fff';
        document.body.insertBefore(h, document.body.firstChild);
    }

    function build() {
        keepHeaderFirst();
        table.style.margin = '0 auto';
        var headerRow = table.tHead.rows[0];

        // Drop the "Dom Complete" column.
        for (var d = headerRow.cells.length - 1; d >= 0; d--) {
            if ((headerRow.cells[d].textContent || '').trim().toLowerCase().indexOf('dom complete') !== -1) {
                headerRow.deleteCell(d);
            }
        }

        // Insert the Method column right after the Name column (index 1).
        if (addMethodCol) {
            var mth = document.createElement('th');
            mth.textContent = 'Method';
            headerRow.insertBefore(mth, headerRow.cells[1] || null);
        }
        var ths = headerRow.cells;

        for (var i = 0; i < ths.length; i++) {
            (function (idx) {
                var th = ths[idx];
                th.style.cursor = 'pointer';
                th.style.userSelect = 'none';
                var icon = document.createElement('span');
                icon.className = 'pl-sort-icon';
                icon.textContent = ' \u2195';
                icon.style.opacity = '0.45';
                th.appendChild(icon);
                th.addEventListener('click', function () { doSort(idx); });
            })(i);
        }

        var filterRow = document.createElement('tr');
        filterRow.className = 'pl-filter-row';
        for (var j = 0; j < ths.length; j++) {
            var cell = document.createElement('th');
            cell.style.padding = '4px 6px';
            var input = document.createElement('input');
            input.type = 'text';
            input.placeholder = 'Search\u2026';
            input.style.cssText = 'width:100%;box-sizing:border-box;padding:4px 6px;' +
                'font:12px sans-serif;border:1px solid #bbb;border-radius:3px';
            input.addEventListener('input', applyFilters);
            inputs.push(input);
            cell.appendChild(input);
            filterRow.appendChild(cell);
        }
        table.tHead.appendChild(filterRow);
    }

    function stickyBg() {
        var probe = table.tHead.rows[0].cells[0];
        var bg = probe ? getComputedStyle(probe).backgroundColor : '';
        if (!bg || bg === 'rgba(0, 0, 0, 0)' || bg === 'transparent') {
            bg = document.documentElement.classList.contains('mp-scheme-dark') ? '#2d2d2d' : '#ffffff';
        }
        return bg;
    }

    function setupFullScreen() {
        document.documentElement.style.height = '100%';
        var b = document.body.style;
        b.margin = '0';
        b.height = '100vh';
        b.boxSizing = 'border-box';
        b.overflow = 'hidden';
        b.display = 'flex';
        b.flexDirection = 'column';

        var scroll = document.getElementById('pl-scroll');
        if (!scroll) {
            scroll = document.createElement('div');
            scroll.id = 'pl-scroll';
            scroll.style.cssText = 'flex:1 1 auto;overflow:auto;width:100%;box-sizing:border-box;padding:0 16px 24px';
            table.parentNode.insertBefore(scroll, table);
            scroll.appendChild(table);
        }

        if (table.tHead) {
            table.tHead.style.position = 'sticky';
            table.tHead.style.top = '0';
            table.tHead.style.zIndex = '3';
            var bg = stickyBg();
            for (var r = 0; r < table.tHead.rows.length; r++) {
                var cells = table.tHead.rows[r].cells;
                for (var c = 0; c < cells.length; c++) {
                    cells[c].style.backgroundColor = bg;
                }
            }
        }
    }

    function addCoffee() {
        if (document.getElementById('pl-coffee')) return;
        var url = '__PL_COFFEE_URL__';
        var panel = document.createElement('div');
        panel.id = 'pl-coffee';
        panel.style.cssText = 'position:fixed;right:12px;bottom:56px;' +
            'width:132px;background:#fff;color:#222;border:1px solid #e2e2e2;border-radius:8px;' +
            'box-shadow:0 3px 12px rgba(0,0,0,.25);padding:8px;text-align:center;' +
            'font-family:sans-serif;z-index:2147483646';
        panel.innerHTML =
            '<a href="' + url + '" target="_blank" rel="noopener noreferrer">' +
            '<img src="__PL_QR_DATA__" alt="Buy Me A Coffee QR" width="96" height="96" ' +
            'style="display:block;margin:0 auto 6px;border:1px solid #eee;border-radius:4px" /></a>' +
            '<a href="' + url + '" target="_blank" rel="noopener noreferrer" ' +
            'style="display:inline-block;background:#FFDD00;color:#000;font-weight:700;font-size:11px;' +
            'text-decoration:none;padding:5px 8px;border-radius:5px">\u2615 Buy me a coffee</a>';
        document.body.appendChild(panel);
    }

    function init() {
        addTitle();
        table = getTable();
        if (!table) { setTimeout(init, 200); return; }
        if (!table.getAttribute('data-pl-enhanced')) {
            table.setAttribute('data-pl-enhanced', '1');
            build();
            setupFullScreen();
            addCoffee();
        }
        var obs = new MutationObserver(function (muts) {
            for (var i = 0; i < muts.length; i++) {
                var t = muts[i].target;
                if (t && t.className && String(t.className).indexOf('pl-filter-row') !== -1) continue;
                schedule();
                break;
            }
        });
        obs.observe(table, { childList: true, subtree: true });
        schedule();
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
            .Replace("__METHODCOL__", enableMethodColumn ? "true" : "false")
            .Replace("__PL_QR_DATA__", CoffeeAssets.QrDataUri)
            .Replace("__PL_COFFEE_URL__", CoffeeAssets.BuyMeACoffeeUrl);
    }

    private static string BuildFooterScript()
    {
        // Fixed footer shown on every profiler HTML page (single result + results-index).
        return """
<script>
(function () {
    function addFooter() {
        if (!document.body || document.getElementById('pl-footer')) return;
        var f = document.createElement('div');
        f.id = 'pl-footer';
        f.innerHTML = '\u00A9 2026<br/>Elric Tran';
        f.style.cssText = 'position:fixed;left:0;right:0;bottom:0;z-index:2147483644;' +
            'text-align:center;font:12px/1.35 sans-serif;color:#aaa;padding:6px 8px;' +
            'background:rgba(0,0,0,.4);pointer-events:none';
        document.body.appendChild(f);
        var pad = (parseInt(getComputedStyle(document.body).paddingBottom, 10) || 0) + 44;
        document.body.style.paddingBottom = pad + 'px';
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', addFooter);
    } else {
        addFooter();
    }
})();
</script>
""";
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

                var injection = _script + _footerScript;
                if (isResultsIndex)
                {
                    injection += _resultsIndexScript;
                    if (_options.EnableClearCacheButton)
                    {
                        injection += _clearButtonScript;
                    }
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
