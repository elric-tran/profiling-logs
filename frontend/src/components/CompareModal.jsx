import React, { useEffect, useState } from 'react'

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
    <button
      onClick={handleCopy}
      title="Copy SQL"
      style={{
        background: isDark ? '#3b3b3b' : '#e5e7eb',
        border: 'none',
        borderRadius: 4,
        cursor: 'pointer',
        padding: '3px 7px',
        fontSize: 12,
        lineHeight: 1,
        color: isDark ? '#ccc' : '#374151',
        opacity: copied ? 1 : 0.7,
        transition: 'opacity 0.15s',
      }}
      onMouseEnter={e => e.currentTarget.style.opacity = '1'}
      onMouseLeave={e => { if (!copied) e.currentTarget.style.opacity = '0.7'; }}
    >
      {copied ? '✓' : '⎘'}
    </button>
  );
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

function splitLines(text) {
  return String(text ?? '').replace(/\r\n/g, '\n').split('\n');
}

function normalizeForDiff(line, ignoreWhitespace) {
  if (!ignoreWhitespace) return line;
  return String(line ?? '').replace(/\s+/g, ' ').trim();
}

function buildSideBySideDiff(leftText, rightText, ignoreWhitespace = false) {
  const leftLines = splitLines(leftText);
  const rightLines = splitLines(rightText);
  const n = leftLines.length;
  const m = rightLines.length;

  const dp = Array.from({ length: n + 1 }, () => Array(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i -= 1) {
    for (let j = m - 1; j >= 0; j -= 1) {
      dp[i][j] = normalizeForDiff(leftLines[i], ignoreWhitespace) === normalizeForDiff(rightLines[j], ignoreWhitespace)
        ? dp[i + 1][j + 1] + 1
        : Math.max(dp[i + 1][j], dp[i][j + 1]);
    }
  }

  const ops = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (normalizeForDiff(leftLines[i], ignoreWhitespace) === normalizeForDiff(rightLines[j], ignoreWhitespace)) {
      ops.push({ type: 'equal', leftLineNo: i + 1, rightLineNo: j + 1, leftText: leftLines[i], rightText: rightLines[j] });
      i += 1;
      j += 1;
    } else if (dp[i + 1][j] >= dp[i][j + 1]) {
      ops.push({ type: 'delete', leftLineNo: i + 1, leftText: leftLines[i] });
      i += 1;
    } else {
      ops.push({ type: 'insert', rightLineNo: j + 1, rightText: rightLines[j] });
      j += 1;
    }
  }

  while (i < n) {
    ops.push({ type: 'delete', leftLineNo: i + 1, leftText: leftLines[i] });
    i += 1;
  }

  while (j < m) {
    ops.push({ type: 'insert', rightLineNo: j + 1, rightText: rightLines[j] });
    j += 1;
  }

  const rows = [];
  for (let k = 0; k < ops.length; k += 1) {
    const current = ops[k];
    const next = ops[k + 1];
    if (current?.type === 'delete' && next?.type === 'insert') {
      rows.push({
        type: 'replace',
        leftLineNo: current.leftLineNo,
        rightLineNo: next.rightLineNo,
        leftText: current.leftText,
        rightText: next.rightText,
      });
      k += 1;
      continue;
    }

    rows.push(current);
  }

  return rows;
}

function buildUnifiedDiffRows(rows) {
  const unified = [];
  rows.forEach(row => {
    if (row.type === 'equal') {
      unified.push({ marker: ' ', lineNo: row.leftLineNo, text: row.leftText, changed: false });
      return;
    }

    if (row.type === 'replace') {
      unified.push({ marker: '-', lineNo: row.leftLineNo, text: row.leftText, changed: true });
      unified.push({ marker: '+', lineNo: row.rightLineNo, text: row.rightText, changed: true });
      return;
    }

    if (row.type === 'delete') {
      unified.push({ marker: '-', lineNo: row.leftLineNo, text: row.leftText, changed: true });
      return;
    }

    if (row.type === 'insert') {
      unified.push({ marker: '+', lineNo: row.rightLineNo, text: row.rightText, changed: true });
    }
  });
  return unified;
}

function QueryList({ request, isDark, selectedQueryKey, onSelectQuery, side }) {
  const queries = request.queries || [];
  const s = {
    card: {
      marginBottom: 10,
      padding: '10px 12px',
      background: isDark ? '#252526' : '#f9fafb',
      borderRadius: 6,
      border: `1px solid ${isDark ? '#333' : '#e5e7eb'}`,
    },
    head: { fontSize: 12, marginBottom: 6, display: 'flex', alignItems: 'center', gap: 6 },
    selectRow: { display: 'flex', alignItems: 'center', gap: 8, marginBottom: 6, fontSize: 12 },
    selectLabel: { color: isDark ? '#9ca3af' : '#6b7280' },
    sqlToolbar: { display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 4, marginBottom: 4 },
    sql: {
      fontFamily: '"Cascadia Code", "Fira Code", "Consolas", monospace',
      fontSize: 12,
      lineHeight: 1.5,
      whiteSpace: 'pre-wrap',
      wordBreak: 'break-word',
      background: isDark ? '#1e1e1e' : '#fff',
      padding: '8px 10px',
      borderRadius: 4,
      margin: 0,
      border: `1px solid ${isDark ? '#333' : '#e5e7eb'}`,
    },
    error: {
      marginBottom: 6,
      padding: '6px 8px',
      borderRadius: 4,
      background: isDark ? '#4b1f1f' : '#fee2e2',
      color: isDark ? '#fecaca' : '#b91c1c',
      fontSize: 12,
      whiteSpace: 'pre-wrap',
    },
    meta: { color: isDark ? '#888' : '#6b7280', fontSize: 11 },
  };

  return (
    <div>
      {queries.map((q, i) => {
        const execSql = buildExecutableSql(q.sql, q.parameters);
        const isError = !!q.errorMessage;
        const queryKey = `${side}:${q.sequence ?? i}`;
        const isSelected = selectedQueryKey === queryKey;
        return (
          <div
            key={q.sequence ?? i}
            style={{
              ...s.card,
              borderColor: isSelected ? '#ec4899' : s.card.border,
              boxShadow: isSelected ? '0 0 0 1px rgba(236,72,153,.35)' : 'none',
            }}
          >
            <div style={s.head}>
              <label style={s.selectRow}>
                <input
                  type="checkbox"
                  checked={isSelected}
                  onChange={() => onSelectQuery(queryKey)}
                />
                <span style={s.selectLabel}>Select for diff</span>
              </label>
              <span style={s.meta}>{isError ? `Failed in ${q.durationMs.toFixed(1)} ms` : `Executed in ${q.durationMs.toFixed(1)} ms`}</span>
              <span style={{ marginLeft: 'auto', color: isDark ? '#9cdcfe' : '#2563eb' }}>
                {q.callerFile} (Line {q.callerLine}) → {q.callerMethod}
              </span>
            </div>
            {isError && q.errorMessage && <div style={s.error}>{q.errorMessage}</div>}
            <div style={s.sqlToolbar}>
              <CopyButton text={execSql} isDark={isDark} />
            </div>
            <pre style={s.sql}>{execSql}</pre>
          </div>
        );
      })}
    </div>
  );
}

function DiffModal({ title, leftLabel, rightLabel, leftSql, rightSql, isDark, onClose }) {
  const [ignoreWhitespace, setIgnoreWhitespace] = useState(true);
  const [viewMode, setViewMode] = useState('side');
  const rows = buildSideBySideDiff(leftSql, rightSql, ignoreWhitespace);
  const unifiedRows = buildUnifiedDiffRows(rows);
  const s = {
    overlay: {
      position: 'fixed',
      inset: 0,
      zIndex: 100002,
      background: 'rgba(0,0,0,0.72)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
    },
    modal: {
      width: '96vw',
      height: '92vh',
      display: 'flex',
      flexDirection: 'column',
      background: isDark ? '#1e1e1e' : '#fff',
      border: `1px solid ${isDark ? '#444' : '#d1d5db'}`,
      borderRadius: 8,
      boxShadow: '0 8px 32px rgba(0,0,0,.4)',
      overflow: 'hidden',
    },
    header: {
      flexShrink: 0,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: 12,
      padding: '10px 14px',
      background: isDark ? '#2d2d2d' : '#f3f4f6',
      borderBottom: `1px solid ${isDark ? '#444' : '#e5e7eb'}`,
    },
    title: { fontSize: 14, fontWeight: 700 },
    meta: { fontSize: 12, color: isDark ? '#9ca3af' : '#6b7280' },
    closeBtn: {
      background: 'none',
      border: 'none',
      cursor: 'pointer',
      fontSize: 18,
      color: isDark ? '#999' : '#6b7280',
      padding: '0 4px',
    },
    body: {
      flex: 1,
      minHeight: 0,
      overflow: 'auto',
      padding: 12,
      display: 'flex',
      flexDirection: 'column',
      gap: 10,
    },
    controls: {
      display: 'flex',
      alignItems: 'center',
      gap: 12,
      flexWrap: 'wrap',
      fontSize: 12,
      color: isDark ? '#9ca3af' : '#6b7280',
    },
    toggleBtn: {
      background: isDark ? '#3b3b3b' : '#e5e7eb',
      border: 'none',
      borderRadius: 4,
      cursor: 'pointer',
      padding: '5px 10px',
      fontSize: 12,
      lineHeight: 1,
      color: isDark ? '#d4d4d4' : '#374151',
    },
    table: {
      width: '100%',
      borderCollapse: 'collapse',
      fontFamily: '"Cascadia Code", "Fira Code", "Consolas", monospace',
      fontSize: 12,
      tableLayout: 'fixed',
    },
    th: {
      position: 'sticky',
      top: 0,
      background: isDark ? '#262626' : '#f9fafb',
      color: isDark ? '#9ca3af' : '#6b7280',
      textAlign: 'left',
      padding: '8px 10px',
      borderBottom: `1px solid ${isDark ? '#444' : '#e5e7eb'}`,
      zIndex: 1,
    },
    td: {
      padding: '6px 10px',
      borderBottom: `1px solid ${isDark ? '#2d2d2d' : '#f3f4f6'}`,
      verticalAlign: 'top',
      whiteSpace: 'pre-wrap',
      wordBreak: 'break-word',
    },
    no: {
      width: 56,
      color: isDark ? '#9ca3af' : '#6b7280',
      textAlign: 'right',
      whiteSpace: 'nowrap',
    },
    diff: {
      background: isDark ? '#4b1f3a' : '#fce7f3',
    },
    empty: {
      color: isDark ? '#6b7280' : '#9ca3af',
    },
    unifiedLine: {
      display: 'flex',
      alignItems: 'flex-start',
      gap: 10,
      padding: '4px 8px',
      fontFamily: '"Cascadia Code", "Fira Code", "Consolas", monospace',
      fontSize: 12,
      borderBottom: `1px solid ${isDark ? '#2d2d2d' : '#f3f4f6'}`,
    },
    unifiedMarker: { width: 18, fontWeight: 700 },
    unifiedNo: { width: 56, textAlign: 'right', color: isDark ? '#9ca3af' : '#6b7280' },
    unifiedText: { flex: 1, whiteSpace: 'pre-wrap', wordBreak: 'break-word' },
  };

  return (
    <div style={s.overlay} onClick={onClose}>
      <div style={s.modal} onClick={e => e.stopPropagation()}>
        <div style={s.header}>
          <div>
            <div style={s.title}>{title}</div>
            <div style={s.meta}>{leftLabel} vs {rightLabel}</div>
          </div>
          <button style={s.closeBtn} onClick={onClose} title="Close">&times;</button>
        </div>
        <div style={s.body}>
          <div style={s.controls}>
            <label style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
              <input
                type="checkbox"
                checked={ignoreWhitespace}
                onChange={e => setIgnoreWhitespace(e.target.checked)}
              />
              Ignore whitespace
            </label>
            <button
              type="button"
              style={s.toggleBtn}
              onClick={() => setViewMode(mode => (mode === 'side' ? 'unified' : 'side'))}
            >
              {viewMode === 'side' ? 'Unified +/- view' : 'Side-by-side view'}
            </button>
          </div>

          {viewMode === 'side' ? (
            <table style={s.table}>
              <colgroup>
                <col style={{ width: 56 }} />
                <col style={{ width: 'calc(50% - 28px)' }} />
                <col style={{ width: 56 }} />
                <col style={{ width: 'calc(50% - 28px)' }} />
              </colgroup>
              <thead>
                <tr>
                  <th style={s.th}>L#</th>
                  <th style={s.th}>Left</th>
                  <th style={s.th}>R#</th>
                  <th style={s.th}>Right</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, idx) => {
                  const rowStyle = row.type === 'equal' ? {} : s.diff;
                  return (
                    <tr key={idx} style={rowStyle}>
                      <td style={{ ...s.td, ...s.no }}>{row.leftLineNo ?? ''}</td>
                      <td style={{ ...s.td, ...(row.type !== 'equal' ? s.diff : {}), ...(row.leftText == null ? s.empty : {}) }}>
                        {row.leftText ?? ''}
                      </td>
                      <td style={{ ...s.td, ...s.no }}>{row.rightLineNo ?? ''}</td>
                      <td style={{ ...s.td, ...(row.type !== 'equal' ? s.diff : {}), ...(row.rightText == null ? s.empty : {}) }}>
                        {row.rightText ?? ''}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          ) : (
            <div style={{ border: `1px solid ${isDark ? '#444' : '#e5e7eb'}`, borderRadius: 6, overflow: 'hidden' }}>
              {unifiedRows.map((row, idx) => (
                <div key={idx} style={{ ...s.unifiedLine, background: row.changed ? s.diff.background : 'transparent' }}>
                  <div style={s.unifiedMarker}>{row.marker}</div>
                  <div style={s.unifiedNo}>{row.lineNo ?? ''}</div>
                  <div style={{ ...s.unifiedText, ...(row.changed ? {} : s.empty) }}>
                    {row.text ?? ''}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default function CompareModal({ left, right, isDark, onClose }) {
  const [leftSelectedQueryKey, setLeftSelectedQueryKey] = useState('');
  const [rightSelectedQueryKey, setRightSelectedQueryKey] = useState('');
  const [diffOpen, setDiffOpen] = useState(false);

  useEffect(() => {
    setLeftSelectedQueryKey('');
    setRightSelectedQueryKey('');
    setDiffOpen(false);
  }, [left?.id, right?.id]);

  const leftQueries = left?.queries || [];
  const rightQueries = right?.queries || [];
  const leftSelectedQuery = leftQueries.find((q, i) => `${'left'}:${q.sequence ?? i}` === leftSelectedQueryKey);
  const rightSelectedQuery = rightQueries.find((q, i) => `${'right'}:${q.sequence ?? i}` === rightSelectedQueryKey);
  const compareEnabled = !!leftSelectedQuery && !!rightSelectedQuery;

  const s = {
    overlay: {
      position: 'fixed',
      inset: 0,
      zIndex: 100001,
      background: 'rgba(0,0,0,0.65)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
    },
    modal: {
      width: '96vw',
      height: '92vh',
      display: 'flex',
      flexDirection: 'column',
      background: isDark ? '#1e1e1e' : '#fff',
      border: `1px solid ${isDark ? '#444' : '#d1d5db'}`,
      borderRadius: 8,
      boxShadow: '0 8px 32px rgba(0,0,0,.4)',
      overflow: 'hidden',
    },
    header: {
      flexShrink: 0,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      padding: '10px 14px',
      background: isDark ? '#2d2d2d' : '#f3f4f6',
      borderBottom: `1px solid ${isDark ? '#444' : '#e5e7eb'}`,
    },
    title: { fontSize: 14, fontWeight: 700 },
    closeBtn: {
      background: 'none',
      border: 'none',
      cursor: 'pointer',
      fontSize: 18,
      color: isDark ? '#999' : '#6b7280',
      padding: '0 4px',
    },
    body: {
      flex: 1,
      minHeight: 0,
      display: 'grid',
      gridTemplateColumns: '1fr 1fr',
      gap: 0,
    },
    pane: {
      minHeight: 0,
      overflow: 'auto',
      padding: 12,
      borderRight: `1px solid ${isDark ? '#333' : '#e5e7eb'}`,
    },
    paneTitle: { fontSize: 14, fontWeight: 700, marginBottom: 4 },
    paneMeta: { fontSize: 12, color: isDark ? '#9ca3af' : '#6b7280', marginBottom: 10 },
    headerActions: { display: 'flex', alignItems: 'center', gap: 8 },
    compareBtn: {
      background: compareEnabled ? '#ec4899' : (isDark ? '#3b3b3b' : '#d1d5db'),
      border: 'none',
      borderRadius: 4,
      cursor: compareEnabled ? 'pointer' : 'not-allowed',
      padding: '6px 10px',
      fontSize: 12,
      lineHeight: 1,
      color: compareEnabled ? '#fff' : (isDark ? '#888' : '#6b7280'),
      opacity: compareEnabled ? 0.95 : 0.55,
      fontWeight: 700,
    },
  };

  if (!left || !right) {
    return null;
  }

  return (
    <>
      <div style={s.overlay} onClick={onClose}>
        <div style={s.modal} onClick={e => e.stopPropagation()}>
          <div style={s.header}>
            <div style={s.title}>
              Compare API Versions: {left.version} vs {right.version}
            </div>
            <div style={s.headerActions}>
              <button
                style={s.compareBtn}
                onClick={() => setDiffOpen(true)}
                title={compareEnabled ? 'Compare selected SQL' : 'Select one query on each side'}
                disabled={!compareEnabled}
              >
                Compare
              </button>
              <button style={s.closeBtn} onClick={onClose} title="Close">&times;</button>
            </div>
          </div>

          <div style={s.body}>
            <div style={s.pane}>
              <div style={s.paneTitle}>{left.name}</div>
              <div style={s.paneMeta}>
                {left.method} · {left.url} · Status {left.statusCode} · {left.durationMs.toFixed(1)} ms
              </div>
              <QueryList
                request={left}
                isDark={isDark}
                selectedQueryKey={leftSelectedQueryKey}
                side="left"
                onSelectQuery={(key) => setLeftSelectedQueryKey(prev => prev === key ? '' : key)}
              />
            </div>
            <div style={s.pane}>
              <div style={s.paneTitle}>{right.name}</div>
              <div style={s.paneMeta}>
                {right.method} · {right.url} · Status {right.statusCode} · {right.durationMs.toFixed(1)} ms
              </div>
              <QueryList
                request={right}
                isDark={isDark}
                selectedQueryKey={rightSelectedQueryKey}
                side="right"
                onSelectQuery={(key) => setRightSelectedQueryKey(prev => prev === key ? '' : key)}
              />
            </div>
          </div>
        </div>
      </div>

      {diffOpen && compareEnabled && (
        <DiffModal
          title="SQL Diff"
          leftLabel={`${left.version} / ${left.name}`}
          rightLabel={`${right.version} / ${right.name}`}
          leftSql={buildExecutableSql(leftSelectedQuery.sql, leftSelectedQuery.parameters)}
          rightSql={buildExecutableSql(rightSelectedQuery.sql, rightSelectedQuery.parameters)}
          isDark={isDark}
          onClose={() => setDiffOpen(false)}
        />
      )}
    </>
  );
}
