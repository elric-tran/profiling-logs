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

export default function RequestList({ requests, loading, selectedId, onSelect, onReply, onCopyCurl, compareRows, onToggleCompareRow, onToggleSelectAll, onOpenCompare, isDark }) {
  const [search, setSearch] = useState({ method: '', name: '', endpoint: '' });
  const [sortCol, setSortCol] = useState('started');
  const [sortDir, setSortDir] = useState(-1);

  const normalizeEndpoint = (value) => {
    if (!value) return '';
    try {
      const u = new URL(value);
      return `${u.pathname}${u.search}`;
    } catch {
      return value;
    }
  };

  const filtered = useMemo(() => {
    let items = requests.filter(r => (r.method || '').toUpperCase() !== 'OPTIONS');
    const methodFilter = search.method.trim().toLowerCase();
    const nameFilter = search.name.trim().toLowerCase();
    const endpointFilter = search.endpoint.trim().toLowerCase();

    if (methodFilter || nameFilter || endpointFilter) {
      items = items.filter(r => {
        const methodText = (r.method || '').toLowerCase();
        const nameText = (r.name || '').toLowerCase();
        const endpointText = normalizeEndpoint(r.url).toLowerCase();

        return (!methodFilter || methodText.includes(methodFilter))
          && (!nameFilter || nameText.includes(nameFilter))
          && (!endpointFilter || endpointText.includes(endpointFilter));
      });
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
  }, [requests, search, sortCol, sortDir]);

  const allVisibleSelected = filtered.length > 0 && filtered.every(r => compareRows.some(x => x.id === r.id));

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
    filterInput: {
      width: '100%',
      boxSizing: 'border-box',
      border: `1px solid ${isDark ? '#444' : '#d1d5db'}`,
      borderRadius: 4,
      padding: '4px 6px',
      fontSize: 12,
      background: isDark ? '#1e1e1e' : '#fff',
      color: isDark ? '#d4d4d4' : '#111',
      outline: 'none',
    },
    replyBtn: {
      background: '#2563eb',
      border: 'none',
      borderRadius: 4,
      cursor: 'pointer',
      padding: '4px 8px',
      fontSize: 12,
      lineHeight: 1,
      color: '#fff',
      opacity: 0.9,
    },
    curlBtn: {
      background: isDark ? '#3b3b3b' : '#dbeafe',
      border: 'none',
      borderRadius: 4,
      cursor: 'pointer',
      padding: '4px 8px',
      fontSize: 12,
      lineHeight: 1,
      color: isDark ? '#d4d4d4' : '#1d4ed8',
      opacity: 0.9,
    },
  };

  const row = (r) => {
    const isSelected = r.id === selectedId;
    const isCompared = compareRows.some(x => x.id === r.id);
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
        <td style={{ ...s.td, textAlign: 'center' }}>
          <input
            type="checkbox"
            checked={isCompared}
            onClick={e => e.stopPropagation()}
            onChange={() => onToggleCompareRow(r)}
          />
        </td>
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
        <td
          style={{ ...s.td, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', fontSize: 12, color: isDark ? '#cbd5e1' : '#475569' }}
          title={normalizeEndpoint(r.url) || ''}
        >
          {normalizeEndpoint(r.url) || '—'}
        </td>
        <td style={s.td}><StatusBadge code={r.statusCode} /></td>
        <td style={{ ...s.td, textAlign: 'right', fontFamily: 'monospace', whiteSpace: 'nowrap' }}>
          {r.durationMs.toFixed(1)} ms
        </td>
        <td style={{ ...s.td, textAlign: 'right' }}>{r.queryCount}</td>
        <td style={{ ...s.td, fontSize: 11, color: isDark ? '#888' : '#9ca3af', whiteSpace: 'nowrap' }}>
          {new Date(r.started).toLocaleTimeString()}
        </td>
        <td style={{ ...s.td, textAlign: 'right' }}>
          <button
            type="button"
            style={s.replyBtn}
            title="Reply XHR"
            onClick={(e) => {
              e.stopPropagation();
              onReply(r.id);
            }}
            onMouseEnter={e => e.currentTarget.style.opacity = '1'}
            onMouseLeave={e => e.currentTarget.style.opacity = '0.9'}
          >
            Reply
          </button>
        </td>
      </tr>
    );
  };

  return (
    <div style={s.panel}>
      <div style={s.scrollArea}>
        <table style={s.table}>
          <colgroup>
            <col style={{ width: '4%' }} />
            <col style={{ width: '8%' }} />
            <col style={{ width: '20%' }} />
            <col />
            <col style={{ width: '6%' }} />
            <col style={{ width: '9%' }} />
            <col style={{ width: '7%' }} />
            <col style={{ width: '8%' }} />
            <col style={{ width: '8%' }} />
          </colgroup>
          <thead>
            <tr>
              <th style={{ ...s.th, textAlign: 'center', cursor: 'default' }} />
              <th style={s.th} onClick={() => toggleSort('method')}>Method{sortIcon('method')}</th>
              <th style={s.th}>Endpoint</th>
              <th style={s.th} onClick={() => toggleSort('name')}>Name{sortIcon('name')}</th>
              <th style={s.th} onClick={() => toggleSort('status')}>Status{sortIcon('status')}</th>
              <th style={{ ...s.th, textAlign: 'right' }} onClick={() => toggleSort('duration')}>Duration{sortIcon('duration')}</th>
              <th style={{ ...s.th, textAlign: 'right' }} onClick={() => toggleSort('queries')}>Queries{sortIcon('queries')}</th>
              <th style={s.th} onClick={() => toggleSort('started')}>Time{sortIcon('started')}</th>
              <th style={{ ...s.th, textAlign: 'right' }}>Action</th>
            </tr>
            <tr>
              <th style={{ ...s.th, cursor: 'default', padding: '6px' }} />
              <th style={{ ...s.th, cursor: 'default', padding: '6px' }}>
                <input
                  type="text"
                  placeholder="Search method"
                  value={search.method}
                  onChange={e => setSearch(s => ({ ...s, method: e.target.value }))}
                  style={s.filterInput}
                  onClick={e => e.stopPropagation()}
                />
              </th>
              <th style={{ ...s.th, cursor: 'default', padding: '4px' }}>
                <input
                  type="text"
                  placeholder="Search endpoint"
                  value={search.endpoint}
                  onChange={e => setSearch(s => ({ ...s, endpoint: e.target.value }))}
                  style={s.filterInput}
                  onClick={e => e.stopPropagation()}
                />
              </th>
              <th style={{ ...s.th, cursor: 'default', padding: '4px' }}>
                <input
                  type="text"
                  placeholder="Search name"
                  value={search.name}
                  onChange={e => setSearch(s => ({ ...s, name: e.target.value }))}
                  style={s.filterInput}
                  onClick={e => e.stopPropagation()}
                />
              </th>
              <th style={{ ...s.th, cursor: 'default', padding: '4px' }} />
              <th style={{ ...s.th, cursor: 'default', padding: '4px' }} />
              <th style={{ ...s.th, cursor: 'default', padding: '4px' }} />
              <th style={{ ...s.th, cursor: 'default', padding: '4px' }} />
              <th style={{ ...s.th, cursor: 'default', padding: '4px', textAlign: 'right' }}>
                <button
                  type="button"
                  onClick={onOpenCompare}
                  disabled={compareRows.length !== 2}
                  style={{
                    background: compareRows.length === 2 ? '#2563eb' : (isDark ? '#3b3b3b' : '#e5e7eb'),
                    color: compareRows.length === 2 ? '#fff' : (isDark ? '#999' : '#9ca3af'),
                    border: 'none',
                    borderRadius: 4,
                    padding: '3px 8px',
                    fontSize: 11,
                    cursor: compareRows.length === 2 ? 'pointer' : 'default',
                    opacity: compareRows.length === 2 ? 1 : 0.6,
                  }}
                  title="Compare selected rows"
                >
                  Compare
                </button>
              </th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={9} style={{ padding: 24, textAlign: 'center', color: '#999' }}>Loading...</td>
              </tr>
            ) : filtered.length === 0 ? null : (
              filtered.map(row)
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
