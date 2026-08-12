import React, { useState, useMemo } from 'react'

const METHOD_COLORS = {
  GET: '#61affe', POST: '#49cc90', PUT: '#fca130',
  DELETE: '#f93e3e', PATCH: '#50e3c2', HEAD: '#9012fe',
  OPTIONS: '#0d5aa7',
};

function StatusBadge({ code }) {
  let bg = '#49cc90';
  if (code >= 500) bg = '#f93e3e';
  else if (code >= 400) bg = '#fca130';
  else if (code >= 300) bg = '#61affe';

  return (
    <span style={{
      background: bg, color: '#fff', borderRadius: 4,
      padding: '1px 6px', fontSize: 11, fontWeight: 600,
    }}>
      {code}
    </span>
  );
}

export default function RequestList({ requests, loading, selectedId, onSelect, isDark }) {
  const [filter, setFilter] = useState('');
  const [sortCol, setSortCol] = useState('started');
  const [sortDir, setSortDir] = useState(-1);

  const filtered = useMemo(() => {
    let items = requests.filter(r => (r.method || '').toUpperCase() !== 'OPTIONS');
    if (filter) {
      const lower = filter.toLowerCase();
      items = items.filter(r =>
        (r.name || '').toLowerCase().includes(lower) ||
        (r.method || '').toLowerCase().includes(lower) ||
        String(r.statusCode).includes(lower)
      );
    }
    return [...items].sort((a, b) => {
      let av, bv;
      if (sortCol === 'started') { av = new Date(a.started).getTime(); bv = new Date(b.started).getTime(); }
      else if (sortCol === 'duration') { av = a.durationMs; bv = b.durationMs; }
      else if (sortCol === 'queries') { av = a.queryCount; bv = b.queryCount; }
      else if (sortCol === 'status') { av = a.statusCode; bv = b.statusCode; }
      else if (sortCol === 'name') { av = (a.name || ''); bv = (b.name || ''); return av.localeCompare(bv) * sortDir; }
      else if (sortCol === 'method') { av = (a.method || ''); bv = (b.method || ''); return av.localeCompare(bv) * sortDir; }
      else { av = 0; bv = 0; }
      return (av - bv) * sortDir;
    });
  }, [requests, filter, sortCol, sortDir]);

  const toggleSort = (col) => {
    if (sortCol === col) setSortDir(d => -d);
    else { setSortCol(col); setSortDir(-1); }
  };

  const sortIcon = (col) => sortCol === col ? (sortDir === 1 ? ' ▲' : ' ▼') : ' ↕';

  const s = {
    panel: {
      flex: '0 0 50%', maxWidth: '50%', display: 'flex', flexDirection: 'column', overflow: 'hidden',
      borderRight: `1px solid ${isDark ? '#333' : '#e5e7eb'}`,
    },
    toolbar: {
      flexShrink: 0, display: 'flex', alignItems: 'center',
      borderBottom: `1px solid ${isDark ? '#333' : '#e5e7eb'}`,
      background: isDark ? '#2d2d2d' : '#fff',
    },
    search: {
      flex: 1, padding: '8px 12px', border: 'none',
      background: 'transparent',
      color: isDark ? '#d4d4d4' : '#111',
      fontSize: 13, outline: 'none',
    },
    scrollArea: { flex: 1, overflow: 'auto', minHeight: 0 },
    table: { width: '100%', borderCollapse: 'collapse', fontSize: 13, tableLayout: 'fixed' },
    th: {
      position: 'sticky', top: 0, zIndex: 2,
      background: isDark ? '#2d2d2d' : '#f9fafb',
      padding: '8px 6px', textAlign: 'left', cursor: 'pointer',
      borderBottom: `2px solid ${isDark ? '#444' : '#e5e7eb'}`,
      userSelect: 'none', fontSize: 12, fontWeight: 600,
      color: isDark ? '#999' : '#6b7280',
    },
    td: {
      padding: '6px',
      borderBottom: `1px solid ${isDark ? '#2d2d2d' : '#f3f4f6'}`,
    },
  };

  const row = (r) => {
    const isSelected = r.id === selectedId;
    return (
      <tr
        key={r.id}
        onClick={() => onSelect(r.id)}
        style={{
          cursor: 'pointer',
          background: isSelected
            ? (isDark ? '#37373d' : '#eff6ff')
            : 'transparent',
        }}
        onMouseEnter={e => { if (!isSelected) e.currentTarget.style.background = isDark ? '#2a2d2e' : '#f9fafb'; }}
        onMouseLeave={e => { if (!isSelected) e.currentTarget.style.background = 'transparent'; }}
      >
        <td style={{ ...s.td, fontWeight: 600, color: METHOD_COLORS[r.method] || (isDark ? '#d4d4d4' : '#111'), whiteSpace: 'nowrap' }}>
          {r.method}
        </td>
        <td style={{
          ...s.td, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
          fontWeight: isSelected ? 700 : 400,
          color: isSelected ? (isDark ? '#4fc1ff' : '#1d4ed8') : undefined,
        }}>
          {r.name}
        </td>
        <td style={s.td}><StatusBadge code={r.statusCode} /></td>
        <td style={{ ...s.td, textAlign: 'right', fontFamily: 'monospace', whiteSpace: 'nowrap' }}>
          {r.durationMs.toFixed(1)} ms
        </td>
        <td style={{ ...s.td, textAlign: 'right' }}>{r.queryCount}</td>
        <td style={{ ...s.td, fontSize: 11, color: isDark ? '#888' : '#9ca3af', whiteSpace: 'nowrap' }}>
          {new Date(r.started).toLocaleTimeString()}
        </td>
      </tr>
    );
  };

  return (
    <div style={s.panel}>
      <div style={s.toolbar}>
        <input
          type="text"
          placeholder="Search requests..."
          value={filter}
          onChange={e => setFilter(e.target.value)}
          style={s.search}
        />
      </div>
      <div style={s.scrollArea}>
        {loading ? (
          <div style={{ padding: 24, textAlign: 'center', color: '#999' }}>Loading...</div>
        ) : filtered.length === 0 ? (
          <div style={{ padding: 24, textAlign: 'center', color: '#999' }}>
            {requests.length === 0 ? 'No profiled requests yet.' : 'No matching requests.'}
          </div>
        ) : (
          <table style={s.table}>
            <colgroup>
              <col style={{ width: '12%' }} />
              <col />
              <col style={{ width: '9%' }} />
              <col style={{ width: '10%' }} />
              <col style={{ width: '8%' }} />
              <col style={{ width: '10%' }} />
            </colgroup>
            <thead>
              <tr>
                <th style={s.th} onClick={() => toggleSort('method')}>Method{sortIcon('method')}</th>
                <th style={s.th} onClick={() => toggleSort('name')}>Name{sortIcon('name')}</th>
                <th style={s.th} onClick={() => toggleSort('status')}>Status{sortIcon('status')}</th>
                <th style={{ ...s.th, textAlign: 'right' }} onClick={() => toggleSort('duration')}>Duration{sortIcon('duration')}</th>
                <th style={{ ...s.th, textAlign: 'right' }} onClick={() => toggleSort('queries')}>Queries{sortIcon('queries')}</th>
                <th style={s.th} onClick={() => toggleSort('started')}>Time{sortIcon('started')}</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(row)}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
