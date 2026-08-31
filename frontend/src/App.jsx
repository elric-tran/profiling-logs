import React, { useState, useEffect, useCallback, useRef } from 'react'
import Footer from './components/Footer'
import CoffeePanel from './components/CoffeePanel'
import RequestList from './components/RequestList'
import RequestDetail from './components/RequestDetail'
import CompareModal from './components/CompareModal'

function ToastHost({ toasts, onDismiss, isDark }) {
  return (
    <div style={{
      position: 'fixed',
      top: 16,
      right: 16,
      zIndex: 200000,
      display: 'flex',
      flexDirection: 'column',
      gap: 8,
      pointerEvents: 'none',
      maxWidth: 360,
    }}>
      {toasts.map(t => {
        const palette = t.kind === 'error'
          ? { bg: isDark ? '#3b1f1f' : '#fef2f2', border: '#ef4444', color: isDark ? '#fecaca' : '#991b1b' }
          : t.kind === 'success'
            ? { bg: isDark ? '#17361f' : '#f0fdf4', border: '#22c55e', color: isDark ? '#bbf7d0' : '#166534' }
            : { bg: isDark ? '#1e3a5f' : '#eff6ff', border: '#3b82f6', color: isDark ? '#bfdbfe' : '#1d4ed8' };

        return (
          <div
            key={t.id}
            style={{
              pointerEvents: 'auto',
              display: 'flex',
              alignItems: 'flex-start',
              gap: 10,
              padding: '10px 12px',
              borderRadius: 8,
              border: `1px solid ${palette.border}`,
              background: palette.bg,
              color: palette.color,
              boxShadow: '0 10px 24px rgba(0,0,0,0.18)',
            }}
          >
            <div style={{ flex: 1, fontSize: 13, lineHeight: 1.4 }}>{t.message}</div>
            <button
              type="button"
              onClick={() => onDismiss(t.id)}
              style={{
                background: 'transparent',
                border: 'none',
                cursor: 'pointer',
                color: palette.color,
                fontSize: 16,
                lineHeight: 1,
                padding: 0,
              }}
              title="Dismiss"
            >
              ×
            </button>
          </div>
        );
      })}
    </div>
  );
}

function ConfirmModal({ open, title, message, confirmLabel = 'Confirm', cancelLabel = 'Cancel', isDark, onConfirm, onCancel }) {
  if (!open) return null;

  return (
    <div style={{
      position: 'fixed',
      inset: 0,
      zIndex: 200001,
      background: 'rgba(0,0,0,0.45)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      padding: 16,
    }} onClick={onCancel}>
      <div
        style={{
          width: 420,
          maxWidth: '100%',
          background: isDark ? '#1e1e1e' : '#fff',
          color: isDark ? '#d4d4d4' : '#111827',
          borderRadius: 8,
          border: `1px solid ${isDark ? '#444' : '#d1d5db'}`,
          boxShadow: '0 18px 48px rgba(0,0,0,.28)',
          overflow: 'hidden',
        }}
        onClick={e => e.stopPropagation()}
      >
        <div style={{
          padding: '12px 14px',
          borderBottom: `1px solid ${isDark ? '#333' : '#e5e7eb'}`,
          fontWeight: 700,
        }}>
          {title}
        </div>
        <div style={{ padding: '14px', fontSize: 13, lineHeight: 1.5 }}>
          {message}
        </div>
        <div style={{
          display: 'flex',
          justifyContent: 'flex-end',
          gap: 8,
          padding: '0 14px 14px',
        }}>
          <button
            type="button"
            onClick={onCancel}
            style={{
              background: isDark ? '#3b3b3b' : '#e5e7eb',
              border: 'none',
              borderRadius: 6,
              padding: '6px 12px',
              cursor: 'pointer',
              color: isDark ? '#d4d4d4' : '#111827',
            }}
          >
            {cancelLabel}
          </button>
          <button
            type="button"
            onClick={onConfirm}
            style={{
              background: '#2563eb',
              color: '#fff',
              border: 'none',
              borderRadius: 6,
              padding: '6px 12px',
              cursor: 'pointer',
            }}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

export default function App({ options }) {
  const apiPath = options.ApiResultsPath || '/profiler/api/results';
  const clearPath = options.ClearPath || '/profiler/clear-cache';
  const scheme = options.Scheme || 'vscode';

  const [isDark, setIsDark] = useState(true);
  const [requests, setRequests] = useState([]);
  const [selectedId, setSelectedId] = useState(null);
  const [detail, setDetail] = useState(null);
  const [loading, setLoading] = useState(true);
  const [expanded, setExpanded] = useState(false);
  const [compareRows, setCompareRows] = useState([]);
  const [compareOpen, setCompareOpen] = useState(false);
  const [toasts, setToasts] = useState([]);
  const [clearConfirmOpen, setClearConfirmOpen] = useState(false);
  const toastTimers = useRef(new Map());

  const fetchDetailById = useCallback(async (id) => {
    const r = await fetch(`${apiPath}/${id}`);
    return r.ok ? r.json() : null;
  }, [apiPath]);

  const curlQuote = useCallback((value) => (
    `"${String(value ?? '')
      .replace(/\\/g, '\\\\')
      .replace(/"/g, '\\"')
      .replace(/\r?\n/g, '\\n')}"`
  ), []);

  const buildCurlCommand = useCallback((detailData) => {
    const parts = ['curl', '--location'];
    const method = (detailData.method || 'GET').toUpperCase();
    parts.push('--request', curlQuote(method));
    if (detailData.url) {
      parts.push(curlQuote(detailData.url));
    }

    const headers = detailData.headers && typeof detailData.headers === 'object' ? Object.entries(detailData.headers) : [];
    headers.forEach(([key, value]) => {
      if (!key || value == null || value === '') return;
      parts.push('--header', curlQuote(`${key}: ${value}`));
    });

    if (detailData.body && method !== 'GET' && method !== 'HEAD') {
      parts.push('--data-raw', curlQuote(detailData.body));
    }

    return parts.join(' ');
  }, [curlQuote]);

  const removeToast = useCallback((id) => {
    setToasts(prev => prev.filter(t => t.id !== id));
    const timer = toastTimers.current.get(id);
    if (timer) {
      clearTimeout(timer);
      toastTimers.current.delete(id);
    }
  }, []);

  const showToast = useCallback((message, kind = 'info') => {
    const id = `${Date.now()}-${Math.random().toString(36).slice(2)}`;
    setToasts(prev => [...prev, { id, message, kind }]);
    const timer = setTimeout(() => {
      setToasts(prev => prev.filter(t => t.id !== id));
      toastTimers.current.delete(id);
    }, 5000);
    toastTimers.current.set(id, timer);
  }, []);

  useEffect(() => () => {
    toastTimers.current.forEach(clearTimeout);
    toastTimers.current.clear();
  }, []);

  const fetchResults = useCallback(() => {
    fetch(apiPath)
      .then(r => r.json())
      .then(data => { setRequests(data); setLoading(false); })
      .catch(() => setLoading(false));
  }, [apiPath]);

  useEffect(() => {
    fetchResults();
    const interval = setInterval(fetchResults, 3000);
    return () => clearInterval(interval);
  }, [fetchResults]);

  useEffect(() => {
    if (!selectedId) { setDetail(null); setExpanded(false); return; }
    fetch(`${apiPath}/${selectedId}`)
      .then(r => r.json())
      .then(setDetail)
      .catch(() => setDetail(null));
  }, [selectedId, apiPath]);

  const performClear = () => {
    const keep = [];
    if (detail && !keep.some(r => r.id === detail.id)) keep.push(detail);
    for (const row of compareRows) {
      if (!keep.some(r => r.id === row.id)) keep.push(row);
    }

    fetch(clearPath, {
      method: 'POST',
      headers: { 'X-Requested-With': 'fetch', 'Content-Type': 'application/json' },
      body: JSON.stringify(keep),
    })
      .then(() => {
        setRequests(keep);
        setCompareRows([]);
        if (keep.length > 0) {
          const nextSelected = keep.find(r => r.id === selectedId) || keep[0];
          setSelectedId(nextSelected.id);
          setDetail(nextSelected);
        } else {
          setSelectedId(null);
          setDetail(null);
        }
        showToast('Profiler results cleared.', 'success');
      })
      .catch(() => showToast('Clear failed.', 'error'));
  };

  const handleClear = () => {
    setClearConfirmOpen(true);
  };

  const handleClose = () => {
    setSelectedId(null);
    setExpanded(false);
  };

  const handleReply = (id) => {
    fetch(`${apiPath}/${id}/reply`, {
      method: 'POST',
      headers: { 'X-Requested-With': 'fetch' },
    })
      .then(r => r.json().then(data => ({ ok: r.ok, data })))
      .then(({ ok, data }) => {
        if (!ok || data.error) {
          showToast(data.error || 'Reply failed.', 'error');
          return;
        }

        showToast(`Replayed request. Response status: ${data.statusCode}`, 'success');
        fetchResults();
      })
      .catch(() => showToast('Reply failed.', 'error'));
  };

  const handleCopyCurl = useCallback(async (id) => {
    try {
      const detailData = await fetchDetailById(id);
      if (!detailData) {
        showToast('Could not load request detail.', 'error');
        return;
      }

      await navigator.clipboard.writeText(buildCurlCommand(detailData));
      showToast('cURL copied to clipboard.', 'success');
    } catch (err) {
      showToast(err.message || 'Failed to copy cURL.', 'error');
    }
  }, [buildCurlCommand, fetchDetailById, showToast]);

  const handleToggleCompareRow = async (row) => {
    setCompareRows(prev => {
      if (prev.some(r => r.id === row.id)) {
        return prev.filter(r => r.id !== row.id);
      }
      return prev;
    });

    const alreadySelected = compareRows.some(r => r.id === row.id);
    if (alreadySelected) {
      return;
    }

    const fullDetail = detail?.id === row.id ? detail : await fetchDetailById(row.id);
    if (!fullDetail) {
      showToast('Could not load request details for compare.', 'error');
      return;
    }

    setCompareRows(prev => {
      if (prev.some(r => r.id === row.id)) return prev;
      return [...prev, fullDetail];
    });
  };

  const handleToggleSelectAll = async (rows) => {
    const visibleIds = rows.map(r => r.id);
    const visibleSelected = visibleIds.length > 0 && visibleIds.every(id => compareRows.some(r => r.id === id));

    if (visibleSelected) {
      setCompareRows(prev => prev.filter(r => !visibleIds.includes(r.id)));
      return;
    }

    const additions = [];
    for (const row of rows) {
      if (compareRows.some(r => r.id === row.id) || additions.some(r => r.id === row.id)) continue;
      const fullDetail = detail?.id === row.id ? detail : await fetchDetailById(row.id);
      if (fullDetail) additions.push(fullDetail);
    }

    setCompareRows(prev => {
      const next = prev.filter(r => !visibleIds.includes(r.id));
      for (const item of additions) {
        if (!next.some(r => r.id === item.id)) next.push(item);
      }
      return next;
    });
  };

  const handleOpenCompare = () => {
    if (compareRows.length !== 2) return;
    setCompareOpen(true);
  };

  const btnBase = {
    background: 'none', border: `1px solid ${isDark ? '#555' : '#d1d5db'}`,
    borderRadius: 6, cursor: 'pointer', padding: '4px 8px',
    color: isDark ? '#d4d4d4' : '#111827', lineHeight: 1,
  };

  const styles = {
    container: {
      display: 'flex', flexDirection: 'column', height: '100vh', overflow: 'hidden',
      fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif',
      background: isDark ? '#1e1e1e' : '#f5f7fb',
      color: isDark ? '#d4d4d4' : '#111827',
    },
    header: {
      flexShrink: 0, position: 'relative',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      padding: '14px 24px', borderBottom: `1px solid ${isDark ? '#333' : '#e5e7eb'}`,
      background: isDark ? '#252526' : '#fff',
    },
    title: { fontSize: '1.5rem', fontWeight: 700, margin: 0, textAlign: 'center' },
    badge: {
      background: isDark ? '#3b3b3b' : '#e5e7eb', borderRadius: 12,
      padding: '2px 10px', fontSize: 13, marginLeft: 10,
    },
    headerActions: {
      position: 'absolute', right: 24, top: '50%', transform: 'translateY(-50%)',
      display: 'flex', alignItems: 'center', gap: 8,
    },
    main: { flex: 1, overflow: 'hidden', display: 'flex', minHeight: 0 },
  };

  return (
    <div style={styles.container}>
      <ToastHost toasts={toasts} onDismiss={removeToast} isDark={isDark} />
      <ConfirmModal
        open={clearConfirmOpen}
        title="Clear all profiler results?"
        message="This will remove all stored requests except the ones you currently selected for detail or compare."
        confirmLabel="Clear All"
        cancelLabel="Cancel"
        isDark={isDark}
        onCancel={() => setClearConfirmOpen(false)}
        onConfirm={() => {
          setClearConfirmOpen(false);
          performClear();
        }}
      />
      <header style={styles.header}>
        <h1 style={styles.title}>
          Profiling Logs
          <span style={styles.badge}>{requests.length}</span>
        </h1>
        <div style={styles.headerActions}>
          <button
            style={{ ...btnBase, fontSize: 13, fontWeight: 600, color: '#e74c3c' }}
            onClick={handleClear}
            title="Clear all profiler results"
          >
            🗑 Clear
          </button>
          <button
            style={{ ...btnBase, fontSize: 18 }}
            onClick={() => setIsDark(d => !d)}
            title={isDark ? 'Light mode' : 'Dark mode'}
          >
            {isDark ? '☀️' : '🌙'}
          </button>
        </div>
      </header>
      <div style={styles.main}>
        {!expanded && (
          <RequestList
            requests={requests}
            loading={loading}
            selectedId={selectedId}
            onSelect={setSelectedId}
            onReply={handleReply}
            onCopyCurl={handleCopyCurl}
            compareRows={compareRows}
            onToggleCompareRow={handleToggleCompareRow}
            onToggleSelectAll={handleToggleSelectAll}
            onOpenCompare={handleOpenCompare}
            isDark={isDark}
          />
        )}
        <RequestDetail
          detail={detail}
          onClose={handleClose}
          isDark={isDark}
          scheme={scheme}
          expanded={expanded}
          onToggleExpand={() => setExpanded(e => !e)}
          explainPath={options.ApiExplainPath || '/profiler/api/explain'}
          indexMetadataPath={options.ApiIndexMetadataPath || '/profiler/api/index-metadata'}
        />
      </div>
      {compareOpen && compareRows.length === 2 && (
        <CompareModal
          left={compareRows[0]}
          right={compareRows[1]}
          isDark={isDark}
          onClose={() => setCompareOpen(false)}
        />
      )}
      <CoffeePanel qrData={options.CoffeeQrData} isDark={isDark} />
      <Footer />
    </div>
  )
}
