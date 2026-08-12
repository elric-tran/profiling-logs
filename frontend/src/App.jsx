import React, { useState, useEffect, useCallback } from 'react'
import Footer from './components/Footer'
import CoffeePanel from './components/CoffeePanel'
import RequestList from './components/RequestList'
import RequestDetail from './components/RequestDetail'

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

  const handleClear = () => {
    if (!window.confirm('Clear ALL stored profiler results?')) return;
    fetch(clearPath, { method: 'POST', headers: { 'X-Requested-With': 'fetch' } })
      .then(() => { setRequests([]); setSelectedId(null); setDetail(null); setExpanded(false); })
      .catch(() => alert('Clear failed.'));
  };

  const handleClose = () => {
    setSelectedId(null);
    setExpanded(false);
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
        />
      </div>
      <CoffeePanel qrData={options.CoffeeQrData} isDark={isDark} />
      <Footer />
    </div>
  )
}
