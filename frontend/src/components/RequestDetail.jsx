import React, { useState } from 'react'
import ExecutionPlanModal from './ExecutionPlanModal'

function CopyButton({ text, isDark }) {
  const [copied, setCopied] = useState(false);
  const handleCopy = (e) => {
    e.stopPropagation();
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 1500);
    });
  };
  return (
    <button onClick={handleCopy} title="Copy SQL"
      style={{
        background: isDark ? '#3b3b3b' : '#e5e7eb', border: 'none', borderRadius: 4,
        cursor: 'pointer', padding: '3px 7px', fontSize: 12, lineHeight: 1,
        color: isDark ? '#ccc' : '#374151', opacity: copied ? 1 : 0.7, transition: 'opacity 0.15s',
      }}
      onMouseEnter={e => e.currentTarget.style.opacity = '1'}
      onMouseLeave={e => { if (!copied) e.currentTarget.style.opacity = '0.7'; }}
    >
      {copied ? '✓' : '⎘'}
    </button>
  );
}

function linkifyIdeLinks(text, scheme) {
  if (!text || !scheme) return text;
  const rx = new RegExp(`(${scheme}://[^\\s<>"']+)`, 'g');
  const parts = text.split(rx);
  return parts.map((part, i) => {
    if (rx.test(part)) {
      rx.lastIndex = 0;
      return <a key={i} href={part} title="Open in IDE"
        style={{ color: '#3794ff', textDecoration: 'underline', wordBreak: 'break-all' }}>{part}</a>;
    }
    return part;
  });
}

function stripProfilingComments(sql) {
  const marker = '-- \u{1F517} From:';
  const idx = sql.indexOf(marker);
  return idx > 0 ? sql.substring(0, idx).trimEnd() : sql;
}

function buildExecutableSql(rawSql, params) {
  const sql = stripProfilingComments(rawSql);
  if (!params || params.length === 0) return sql;

  const pad = 'DECLARE '.length;
  const lines = params.map((p, i) => {
    const prefix = i === 0 ? 'DECLARE ' : ' '.repeat(pad);
    const sep = i < params.length - 1 ? ',' : ';';
    return `${prefix}${p.name} ${p.sqlType} = ${p.value ?? 'NULL'}${sep}`;
  });

  return lines.join('\n') + '\n\n' + sql;
}

export default function RequestDetail({ detail, onClose, isDark, scheme, expanded, onToggleExpand, explainPath }) {
  const [planState, setPlanState] = useState({ open: false, loading: false, planXml: null, error: null });

  const handleExplain = (sql, parameters) => {
    setPlanState({ open: true, loading: true, planXml: null, error: null });
    fetch(explainPath, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sql, parameters }),
    })
      .then(r => r.json())
      .then(data => {
        if (data.error) setPlanState(s => ({ ...s, loading: false, error: data.error }));
        else setPlanState(s => ({ ...s, loading: false, planXml: data.plan }));
      })
      .catch(err => setPlanState(s => ({ ...s, loading: false, error: err.message })));
  };

  if (!detail) {
    return (
      <div style={{
        flex: '1 1 50%', display: 'flex', alignItems: 'center', justifyContent: 'center',
        color: isDark ? '#666' : '#9ca3af', fontSize: 14,
      }}>
        Select a request to view SQL queries
      </div>
    );
  }

  const s = {
    panel: { flex: expanded ? '1 1 100%' : '1 1 50%', overflow: 'auto', padding: 0, background: isDark ? '#1e1e1e' : '#fff' },
    header: {
      position: 'sticky', top: 0, zIndex: 2, padding: '12px 16px',
      background: isDark ? '#252526' : '#f9fafb',
      borderBottom: `1px solid ${isDark ? '#333' : '#e5e7eb'}`,
      display: 'flex', alignItems: 'center', justifyContent: 'space-between',
    },
    headerBtn: { background: 'none', border: 'none', cursor: 'pointer', fontSize: 16, color: isDark ? '#999' : '#6b7280', padding: '2px 6px' },
    info: { fontSize: 12, color: isDark ? '#888' : '#6b7280', marginTop: 4 },
    queryCard: {
      margin: '8px 12px', padding: '10px 14px',
      background: isDark ? '#252526' : '#f9fafb', borderRadius: 6,
      border: `1px solid ${isDark ? '#333' : '#e5e7eb'}`,
    },
    callerLine: { fontSize: 12, marginBottom: 6, display: 'flex', alignItems: 'center', gap: 6, color: isDark ? '#9cdcfe' : '#2563eb' },
    connBadge: {
      display: 'inline-flex', alignItems: 'center', gap: 3, fontSize: 11, padding: '1px 6px',
      borderRadius: 3, background: isDark ? '#333' : '#e5e7eb', color: isDark ? '#aaa' : '#6b7280',
    },
    sqlToolbar: { display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 4, marginBottom: 4 },
    iconBtn: {
      background: isDark ? '#3b3b3b' : '#e5e7eb', border: 'none', borderRadius: 4,
      cursor: 'pointer', padding: '3px 7px', fontSize: 12, lineHeight: 1,
      color: isDark ? '#ccc' : '#374151', opacity: 0.7, transition: 'opacity 0.15s',
    },
    explainBtn: {
      background: '#FFDD00', border: 'none', borderRadius: 4,
      cursor: 'pointer', padding: '3px 7px', fontSize: 12, lineHeight: 1,
      color: '#000', opacity: 0.85, transition: 'opacity 0.15s',
    },
    sql: {
      fontFamily: '"Cascadia Code", "Fira Code", "Consolas", monospace',
      fontSize: 12, lineHeight: 1.5, whiteSpace: 'pre-wrap', wordBreak: 'break-word',
      background: isDark ? '#1e1e1e' : '#fff', padding: '8px 10px', borderRadius: 4, margin: 0,
      border: `1px solid ${isDark ? '#333' : '#e5e7eb'}`,
      maxHeight: expanded ? 'none' : 300, overflow: 'auto',
    },
    connEvents: {
      margin: '8px 12px', padding: '8px 14px', background: isDark ? '#252526' : '#f9fafb',
      borderRadius: 6, border: `1px solid ${isDark ? '#333' : '#e5e7eb'}`, fontSize: 12,
    },
  };

  const queries = detail.queries || [];
  const connEvents = detail.connectionEvents || [];

  return (
    <div style={s.panel}>
      <div style={s.header}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontWeight: 600, fontSize: 14 }}>{detail.name}</div>
          <div style={s.info}>
            {detail.durationMs.toFixed(1)} ms &middot; Status {detail.statusCode} &middot; {queries.length} {queries.length === 1 ? 'query' : 'queries'}
            &middot; {new Date(detail.started).toLocaleString()}
          </div>
        </div>
        <div style={{ display: 'flex', gap: 4, flexShrink: 0 }}>
          <button style={s.headerBtn} onClick={onToggleExpand} title={expanded ? 'Collapse' : 'Expand full screen'}>
            <svg width="16" height="16" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg" style={{ transform: expanded ? 'rotate(180deg)' : 'none' }}>
              <g fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2">
                <polyline points="3 17.3 3 21 6.7 21"/>
                <line x1="10" x2="3.8" y1="14" y2="20.2"/>
                <line x1="14" x2="20.2" y1="10" y2="3.8"/>
                <polyline points="21 6.7 21 3 17.3 3"/>
              </g>
            </svg>
          </button>
          <button style={s.headerBtn} onClick={onClose} title="Close">&times;</button>
        </div>
      </div>

      {queries.length === 0 ? (
        <div style={{ padding: 24, textAlign: 'center', color: '#999', fontSize: 13 }}>No SQL queries captured for this request.</div>
      ) : (
        queries.map((q, i) => {
          const execSql = buildExecutableSql(q.sql, q.parameters);
          return (
            <div key={i} style={s.queryCard}>
              <div style={s.callerLine}>
                {q.connColor && (
                  <span style={s.connBadge}>{q.connColor} {q.connEvent || 'OPEN'} #{q.connId}</span>
                )}
                {q.callerFile && (
                  <span>🔗 {q.callerFile} (Line {q.callerLine}) → {q.callerMethod}</span>
                )}
                {q.durationMs > 0 && (
                  <span style={{ color: isDark ? '#888' : '#9ca3af', marginLeft: 'auto' }}>{q.durationMs.toFixed(1)} ms</span>
                )}
              </div>
              {q.ideLink && (
                <div style={{ fontSize: 11, marginBottom: 6 }}>{linkifyIdeLinks(q.ideLink, scheme)}</div>
              )}
              <div style={s.sqlToolbar}>
                <button style={s.explainBtn} title="Get execution plan"
                  onClick={() => handleExplain(q.sql, q.parameters)}
                  onMouseEnter={e => e.currentTarget.style.opacity = '1'}
                  onMouseLeave={e => e.currentTarget.style.opacity = '0.7'}>🗄</button>
                <CopyButton text={execSql} isDark={isDark} />
              </div>
              <pre style={s.sql}>{execSql}</pre>
            </div>
          );
        })
      )}

      {planState.open && (
        <ExecutionPlanModal
          planXml={planState.planXml} error={planState.error} loading={planState.loading}
          onClose={() => setPlanState({ open: false, loading: false, planXml: null, error: null })}
          isDark={isDark}
        />
      )}
    </div>
  );
}
