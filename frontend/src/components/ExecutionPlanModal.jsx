import React, { useState, useRef, useEffect, useCallback } from 'react'

// ── XML → Tree parser ──────────────────────────────────────────────────────

function parsePlanXml(xml) {
  try {
    const doc = new DOMParser().parseFromString(xml, 'text/xml');
    const ns = doc.documentElement.namespaceURI || '';
    const selNs = (el, tag) => Array.from(
      ns ? el.getElementsByTagNameNS(ns, tag) : el.getElementsByTagName(tag)
    );

    const stmts = selNs(doc, 'StmtSimple');
    if (stmts.length === 0) return null;

    const roots = [];
    for (const stmt of stmts) {
      const topOps = selNs(stmt, 'RelOp');
      if (topOps.length === 0) continue;
      const root = buildNode(topOps[0], ns);
      if (root) {
        root.stmtText = stmt.getAttribute('StatementText') || '';
        roots.push(root);
      }
    }
    return roots.length > 0 ? roots : null;
  } catch {
    return null;
  }
}

function buildNode(el, ns) {
  const node = {
    physicalOp: el.getAttribute('PhysicalOp') || '',
    logicalOp: el.getAttribute('LogicalOp') || '',
    estimateRows: parseFloat(el.getAttribute('EstimateRows')) || 0,
    estimateCPU: parseFloat(el.getAttribute('EstimateCPU')) || 0,
    estimateIO: parseFloat(el.getAttribute('EstimateIO')) || 0,
    subtreeCost: parseFloat(el.getAttribute('SubtreeCost')) || 0,
    nodeId: el.getAttribute('NodeId') || '',
    parallel: el.getAttribute('Parallel') === '1',
    children: [],
    details: {},
  };

  node.operatorCost = node.estimateCPU + node.estimateIO;

  for (const child of el.children) {
    const tag = child.localName || child.tagName;
    if (tag === 'RelOp' || tag === 'OutputList') continue;
    for (const attr of child.attributes) {
      node.details[`${tag}.${attr.name}`] = attr.value;
    }
    for (const gc of child.children) {
      const gcTag = gc.localName || gc.tagName;
      for (const attr of gc.attributes) {
        node.details[`${gcTag}.${attr.name}`] = attr.value;
      }
    }
  }

  for (const child of el.children) {
    const tag = child.localName || child.tagName;
    if (tag === 'RelOp') {
      node.children.push(buildNode(child, ns));
    } else {
      for (const gc of child.children) {
        if ((gc.localName || gc.tagName) === 'RelOp') {
          node.children.push(buildNode(gc, ns));
        }
      }
    }
  }

  return node;
}

// ── SVG Operator Icons (SSMS-style) ─────────────────────────────────────

function OpIcon({ op, size = 32 }) {
  const v = `0 0 ${size} ${size}`;
  const common = { width: size, height: size, display: 'block' };

  switch (op) {
    case 'SELECT':
      return (
        <svg style={common} viewBox={v}>
          <polygon points="6,4 26,16 6,28" fill="#4caf50" />
          <polygon points="6,4 26,16 6,28" fill="none" stroke="#2e7d32" strokeWidth="1.5" />
        </svg>
      );

    case 'Clustered Index Scan':
      return (
        <svg style={common} viewBox={v}>
          <rect x="3" y="5" width="18" height="22" rx="2" fill="#ffca28" stroke="#f9a825" strokeWidth="1" />
          <line x1="3" y1="11" x2="21" y2="11" stroke="#f9a825" strokeWidth=".7" />
          <line x1="3" y1="17" x2="21" y2="17" stroke="#f9a825" strokeWidth=".7" />
          <line x1="3" y1="23" x2="21" y2="23" stroke="#f9a825" strokeWidth=".7" />
          <circle cx="24" cy="22" r="5.5" fill="none" stroke="#1565c0" strokeWidth="1.5" />
          <line x1="28" y1="26" x2="31" y2="29" stroke="#1565c0" strokeWidth="1.8" strokeLinecap="round" />
        </svg>
      );

    case 'Index Scan':
      return (
        <svg style={common} viewBox={v}>
          <rect x="3" y="5" width="18" height="22" rx="2" fill="#90caf9" stroke="#1976d2" strokeWidth="1" />
          <line x1="3" y1="11" x2="21" y2="11" stroke="#1976d2" strokeWidth=".7" />
          <line x1="3" y1="17" x2="21" y2="17" stroke="#1976d2" strokeWidth=".7" />
          <line x1="3" y1="23" x2="21" y2="23" stroke="#1976d2" strokeWidth=".7" />
          <circle cx="24" cy="22" r="5.5" fill="none" stroke="#1565c0" strokeWidth="1.5" />
          <line x1="28" y1="26" x2="31" y2="29" stroke="#1565c0" strokeWidth="1.8" strokeLinecap="round" />
        </svg>
      );

    case 'Clustered Index Seek':
      return (
        <svg style={common} viewBox={v}>
          <rect x="3" y="5" width="18" height="22" rx="2" fill="#ffca28" stroke="#f9a825" strokeWidth="1" />
          <line x1="3" y1="11" x2="21" y2="11" stroke="#f9a825" strokeWidth=".7" />
          <line x1="3" y1="17" x2="21" y2="17" stroke="#f9a825" strokeWidth=".7" />
          <polyline points="22,26 27,16 32,26" fill="none" stroke="#e65100" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
          <line x1="27" y1="16" x2="27" y2="8" stroke="#e65100" strokeWidth="1.8" strokeLinecap="round" />
        </svg>
      );

    case 'Index Seek':
      return (
        <svg style={common} viewBox={v}>
          <rect x="3" y="5" width="18" height="22" rx="2" fill="#90caf9" stroke="#1976d2" strokeWidth="1" />
          <line x1="3" y1="11" x2="21" y2="11" stroke="#1976d2" strokeWidth=".7" />
          <line x1="3" y1="17" x2="21" y2="17" stroke="#1976d2" strokeWidth=".7" />
          <polyline points="22,26 27,16 32,26" fill="none" stroke="#e65100" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" />
          <line x1="27" y1="16" x2="27" y2="8" stroke="#e65100" strokeWidth="1.8" strokeLinecap="round" />
        </svg>
      );

    case 'Table Scan':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="2" fill="#a5d6a7" stroke="#388e3c" strokeWidth="1" />
          <line x1="4" y1="10" x2="28" y2="10" stroke="#388e3c" strokeWidth=".8" />
          <line x1="4" y1="16" x2="28" y2="16" stroke="#388e3c" strokeWidth=".8" />
          <line x1="4" y1="22" x2="28" y2="22" stroke="#388e3c" strokeWidth=".8" />
          <line x1="16" y1="4" x2="16" y2="28" stroke="#388e3c" strokeWidth=".8" />
        </svg>
      );

    case 'Nested Loops':
      return (
        <svg style={common} viewBox={v}>
          <circle cx="12" cy="16" r="8" fill="none" stroke="#ff7043" strokeWidth="2" />
          <circle cx="20" cy="16" r="8" fill="none" stroke="#ff7043" strokeWidth="2" />
          <path d="M16,9 A8,8 0 0,1 16,23" fill="#ffccbc" opacity=".5" />
        </svg>
      );

    case 'Hash Match':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="3" fill="#ce93d8" stroke="#7b1fa2" strokeWidth="1.2" />
          <text x="16" y="22" textAnchor="middle" fontSize="16" fontWeight="bold" fill="#4a148c">#</text>
        </svg>
      );

    case 'Merge Join':
      return (
        <svg style={common} viewBox={v}>
          <path d="M6,8 L20,16" stroke="#42a5f5" strokeWidth="2.5" strokeLinecap="round" />
          <path d="M6,24 L20,16" stroke="#42a5f5" strokeWidth="2.5" strokeLinecap="round" />
          <path d="M20,16 L28,16" stroke="#42a5f5" strokeWidth="2.5" strokeLinecap="round" />
          <polygon points="28,16 23,12 23,20" fill="#42a5f5" />
        </svg>
      );

    case 'Sort':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="3" fill="#fff9c4" stroke="#f9a825" strokeWidth="1.2" />
          <text x="16" y="13" textAnchor="middle" fontSize="11" fontWeight="bold" fill="#e65100">A</text>
          <text x="16" y="26" textAnchor="middle" fontSize="11" fontWeight="bold" fill="#e65100">Z</text>
          <line x1="22" y1="10" x2="22" y2="24" stroke="#e65100" strokeWidth="1.2" />
          <polyline points="19,21 22,25 25,21" fill="none" stroke="#e65100" strokeWidth="1.2" />
        </svg>
      );

    case 'Filter':
      return (
        <svg style={common} viewBox={v}>
          <polygon points="4,6 28,6 20,18 20,28 12,28 12,18" fill="#b3e5fc" stroke="#0277bd" strokeWidth="1.2" strokeLinejoin="round" />
        </svg>
      );

    case 'Compute Scalar':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="3" fill="#e8eaf6" stroke="#3949ab" strokeWidth="1.2" />
          <text x="16" y="22" textAnchor="middle" fontSize="15" fontWeight="bold" fontStyle="italic" fill="#1a237e">fx</text>
        </svg>
      );

    case 'Stream Aggregate':
    case 'Scalar Aggregate':
    case 'Hash Aggregate':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="3" fill="#f3e5f5" stroke="#7b1fa2" strokeWidth="1.2" />
          <text x="16" y="23" textAnchor="middle" fontSize="20" fontWeight="bold" fill="#4a148c">&Sigma;</text>
        </svg>
      );

    case 'Key Lookup':
    case 'RID Lookup':
      return (
        <svg style={common} viewBox={v}>
          <circle cx="10" cy="14" r="6" fill="#ffca28" stroke="#f57f17" strokeWidth="1.2" />
          <line x1="14" y1="18" x2="26" y2="26" stroke="#f57f17" strokeWidth="2.5" strokeLinecap="round" />
          <line x1="26" y1="26" x2="26" y2="20" stroke="#f57f17" strokeWidth="1.5" strokeLinecap="round" />
          <line x1="26" y1="26" x2="20" y2="26" stroke="#f57f17" strokeWidth="1.5" strokeLinecap="round" />
        </svg>
      );

    case 'Constant Scan':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="3" fill="#e0e0e0" stroke="#757575" strokeWidth="1.2" />
          <text x="16" y="22" textAnchor="middle" fontSize="18" fontWeight="bold" fill="#424242">&empty;</text>
        </svg>
      );

    case 'Top':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="2" fill="#c8e6c9" stroke="#388e3c" strokeWidth="1" />
          <line x1="4" y1="12" x2="28" y2="12" stroke="#388e3c" strokeWidth="1.5" />
          <rect x="6" y="5" width="20" height="6" fill="#66bb6a" opacity=".5" />
          <line x1="4" y1="18" x2="28" y2="18" stroke="#388e3c" strokeWidth=".5" opacity=".4" />
          <line x1="4" y1="24" x2="28" y2="24" stroke="#388e3c" strokeWidth=".5" opacity=".4" />
        </svg>
      );

    case 'Parallelism':
      return (
        <svg style={common} viewBox={v}>
          <path d="M6,8 L26,8" stroke="#f57c00" strokeWidth="2.5" strokeLinecap="round" />
          <path d="M6,16 L26,16" stroke="#f57c00" strokeWidth="2.5" strokeLinecap="round" />
          <path d="M6,24 L26,24" stroke="#f57c00" strokeWidth="2.5" strokeLinecap="round" />
          <polygon points="26,8 22,5 22,11" fill="#f57c00" />
          <polygon points="26,16 22,13 22,19" fill="#f57c00" />
          <polygon points="26,24 22,21 22,27" fill="#f57c00" />
        </svg>
      );

    case 'Spool':
    case 'Table Spool':
    case 'Index Spool':
    case 'Row Count Spool':
      return (
        <svg style={common} viewBox={v}>
          <ellipse cx="16" cy="8" rx="10" ry="4" fill="#b0bec5" stroke="#546e7a" strokeWidth="1" />
          <rect x="6" y="8" width="20" height="16" fill="#b0bec5" />
          <line x1="6" y1="8" x2="6" y2="24" stroke="#546e7a" strokeWidth="1" />
          <line x1="26" y1="8" x2="26" y2="24" stroke="#546e7a" strokeWidth="1" />
          <ellipse cx="16" cy="24" rx="10" ry="4" fill="#b0bec5" stroke="#546e7a" strokeWidth="1" />
        </svg>
      );

    case 'Concatenation':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="3" fill="#dcedc8" stroke="#558b2f" strokeWidth="1.2" />
          <text x="16" y="22" textAnchor="middle" fontSize="18" fontWeight="bold" fill="#33691e">&cup;</text>
        </svg>
      );

    case 'Assert':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="3" fill="#ffcdd2" stroke="#c62828" strokeWidth="1.2" />
          <text x="16" y="23" textAnchor="middle" fontSize="18" fontWeight="bold" fill="#b71c1c">!!</text>
        </svg>
      );

    case 'Segment':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="3" fill="#e0e0e0" stroke="#616161" strokeWidth="1.2" />
          <line x1="10" y1="8" x2="10" y2="24" stroke="#424242" strokeWidth="2" />
          <line x1="16" y1="8" x2="16" y2="24" stroke="#424242" strokeWidth="2" />
          <line x1="22" y1="8" x2="22" y2="24" stroke="#424242" strokeWidth="2" />
        </svg>
      );

    case 'Sequence Project':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="3" fill="#e8eaf6" stroke="#3949ab" strokeWidth="1.2" />
          <path d="M8,16 L24,16" stroke="#283593" strokeWidth="2" strokeLinecap="round" />
          <polygon points="24,16 19,12 19,20" fill="#283593" />
        </svg>
      );

    case 'Window Aggregate':
    case 'Window Spool':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="3" fill="#e1f5fe" stroke="#0277bd" strokeWidth="1.2" />
          <rect x="8" y="8" width="16" height="16" rx="1" fill="none" stroke="#01579b" strokeWidth="1.5" />
          <line x1="8" y1="14" x2="24" y2="14" stroke="#01579b" strokeWidth=".7" />
          <line x1="8" y1="20" x2="24" y2="20" stroke="#01579b" strokeWidth=".7" />
          <line x1="16" y1="8" x2="16" y2="24" stroke="#01579b" strokeWidth=".7" />
        </svg>
      );

    case 'Bitmap':
      return (
        <svg style={common} viewBox={v}>
          <rect x="4" y="4" width="24" height="24" rx="2" fill="#e0e0e0" stroke="#616161" strokeWidth="1" />
          {[0,1,2,3].map(r => [0,1,2,3].map(c => (
            <rect key={`${r}${c}`} x={6+c*6} y={6+r*6} width="4" height="4"
              fill={(r+c)%2===0 ? '#424242' : '#bdbdbd'} />
          )))}
        </svg>
      );

    case 'Adaptive Join':
      return (
        <svg style={common} viewBox={v}>
          <path d="M6,8 L16,16 L6,24" fill="none" stroke="#ab47bc" strokeWidth="2" strokeLinejoin="round" />
          <path d="M16,16 L28,16" stroke="#ab47bc" strokeWidth="2" strokeLinecap="round" />
          <polygon points="28,16 23,12 23,20" fill="#ab47bc" />
        </svg>
      );

    default:
      return (
        <svg style={common} viewBox={v}>
          <circle cx="16" cy="16" r="11" fill="#e0e0e0" stroke="#757575" strokeWidth="1.2" />
          <text x="16" y="20" textAnchor="middle" fontSize="12" fontWeight="bold" fill="#424242">?</text>
        </svg>
      );
  }
}

// ── Formatting helpers ──────────────────────────────────────────────────

function formatRows(n) {
  if (n >= 1e6) return `${(n / 1e6).toFixed(1)}M`;
  if (n >= 1e3) return `${(n / 1e3).toFixed(1)}K`;
  return n.toFixed(0);
}

function subtreeCostSum(node) {
  let sum = node.operatorCost;
  for (const c of node.children) sum += subtreeCostSum(c);
  return sum;
}

function countDescendants(node) {
  let n = node.children.length;
  for (const c of node.children) n += countDescendants(c);
  return n;
}

function getLineThickness(rows) {
  return Math.max(1, Math.min(8, Math.log10(Math.max(rows, 1)) * 1.5));
}

// ── Tree layout: SSMS-style left-to-right with connecting lines ─────────

function PlanTree({ node, totalCost, isDark, onSelect, selectedId, collapsedSet, onToggleCollapse }) {
  const lineColor = isDark ? '#e0e0e0' : '#333';
  const children = node.children;
  const hasChildren = children.length > 0;
  const isCollapsed = collapsedSet.has(node.nodeId);

  return (
    <div style={{ display: 'inline-flex', alignItems: 'center' }}>
      <NodeBox
        node={node} totalCost={totalCost} isDark={isDark}
        onSelect={onSelect} selectedId={selectedId}
        hasChildren={hasChildren} isCollapsed={isCollapsed}
        onToggleCollapse={onToggleCollapse}
        collapsedSet={collapsedSet}
      />

      {hasChildren && !isCollapsed && (
        <>
          <div style={{ width: 20, height: 1, background: lineColor, flexShrink: 0 }} />
          <ChildrenBranches
            children={children} lineColor={lineColor} totalCost={totalCost}
            isDark={isDark} onSelect={onSelect} selectedId={selectedId}
            collapsedSet={collapsedSet} onToggleCollapse={onToggleCollapse}
          />
        </>
      )}
    </div>
  );
}

function ChildrenBranches({ children, lineColor, totalCost, isDark, onSelect, selectedId, collapsedSet, onToggleCollapse }) {
  const n = children.length;

  return (
    <div style={{ display: 'flex', flexDirection: 'column' }}>
      {children.map((child, i) => {
        const isFirst = i === 0;
        const isLast = i === n - 1;
        const isOnly = n === 1;
        const thickness = getLineThickness(child.estimateRows);
        const rowLabel = formatRows(child.estimateRows);

        return (
          <div key={i} style={{
            display: 'flex', alignItems: 'center',
            marginTop: i > 0 ? 8 : 0,
          }}>
            <div style={{
              position: 'relative', width: 80, flexShrink: 0,
              alignSelf: 'stretch', minHeight: 60,
            }}>
              {!isOnly && (
                <div style={{
                  position: 'absolute', left: 0,
                  top: isFirst ? '50%' : 0,
                  bottom: isLast ? '50%' : 0,
                  width: 0,
                  borderLeft: `1px solid ${lineColor}`,
                }} />
              )}

              <div style={{
                position: 'absolute', left: 0, right: 6,
                top: '50%', transform: 'translateY(-50%)',
                height: Math.max(thickness, 1),
                background: lineColor,
                borderRadius: thickness > 2 ? 1 : 0,
              }} />

              <div style={{
                position: 'absolute', left: -6, top: '50%',
                transform: 'translateY(-50%)',
                width: 0, height: 0,
                borderTop: '5px solid transparent',
                borderBottom: '5px solid transparent',
                borderRight: `8px solid ${lineColor}`,
              }} />

              <div style={{
                position: 'absolute',
                left: 10, top: '50%',
                transform: 'translateY(-150%)',
                fontSize: 9, whiteSpace: 'nowrap',
                color: isDark ? '#aaa' : '#666',
                pointerEvents: 'none',
                fontFamily: 'monospace',
              }}>
                {rowLabel} rows
              </div>
            </div>

            <PlanTree
              node={child} totalCost={totalCost} isDark={isDark}
              onSelect={onSelect} selectedId={selectedId}
              collapsedSet={collapsedSet} onToggleCollapse={onToggleCollapse}
            />
          </div>
        );
      })}
    </div>
  );
}

// ── Node box (SSMS-style card) ──────────────────────────────────────────

function NodeBox({ node, totalCost, isDark, onSelect, selectedId, hasChildren, isCollapsed, onToggleCollapse, collapsedSet }) {
  const ownCostPct = totalCost > 0 ? (node.operatorCost / totalCost) * 100 : 0;
  const displayCostPct = isCollapsed && hasChildren
    ? (totalCost > 0 ? (subtreeCostSum(node) / totalCost) * 100 : 0)
    : ownCostPct;
  const hiddenCount = isCollapsed ? countDescendants(node) : 0;

  const isSelected = selectedId === node.nodeId;
  const objectName = node.details['Object.Table'] || node.details['Object.Index'] || '';
  const isHot = displayCostPct > 30;
  const isWarm = displayCostPct > 10;

  const handleToggle = (e) => {
    e.stopPropagation();
    onToggleCollapse(node.nodeId);
  };

  return (
    <div style={{ position: 'relative', flexShrink: 0 }}>
      <div
        onClick={(e) => { e.stopPropagation(); onSelect(node); }}
        style={{
          display: 'flex', flexDirection: 'column', alignItems: 'center',
          padding: '8px 10px', minWidth: 100, maxWidth: 150,
          background: isSelected
            ? (isDark ? '#264f78' : '#dbeafe')
            : (isDark ? '#2d2d2d' : '#fff'),
          border: `2px solid ${
            isSelected ? '#3794ff'
            : isHot ? '#ef4444'
            : isWarm ? '#f59e0b'
            : (isDark ? '#555' : '#d1d5db')
          }`,
          borderRadius: 6, cursor: 'pointer', textAlign: 'center',
          transition: 'border-color 0.15s, background 0.15s',
          boxShadow: isSelected ? `0 0 0 1px ${isDark ? '#3794ff' : '#93c5fd'}` : 'none',
        }}
      >
        <OpIcon op={node.physicalOp} size={32} />
        <span style={{
          fontSize: 10, fontWeight: 700, marginTop: 4, lineHeight: 1.2,
          color: isDark ? '#ddd' : '#1f2937',
          wordBreak: 'break-word',
        }}>
          {node.physicalOp}
        </span>
        {objectName && (
          <span style={{
            fontSize: 9, marginTop: 2, color: isDark ? '#999' : '#6b7280',
            maxWidth: 130, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
          }} title={objectName}>
            [{objectName}]
          </span>
        )}
        <span style={{
          fontSize: 10, marginTop: 3, fontWeight: 700,
          color: isHot ? '#ef4444' : isWarm ? '#f59e0b' : (isDark ? '#6b9e6b' : '#16a34a'),
        }}>
          Cost: {displayCostPct.toFixed(0)}%
          {isCollapsed && <span style={{ fontWeight: 400, fontSize: 9 }}> (subtree)</span>}
        </span>
        {isCollapsed && hiddenCount > 0 && (
          <span style={{
            fontSize: 9, marginTop: 2, color: isDark ? '#888' : '#9ca3af',
          }}>
            +{hiddenCount} hidden
          </span>
        )}
      </div>

      {hasChildren && (
        <button
          onClick={handleToggle}
          title={isCollapsed ? 'Expand children' : 'Collapse children'}
          style={{
            position: 'absolute', right: -8, top: '50%', transform: 'translateY(-50%)',
            width: 16, height: 16, borderRadius: '50%', padding: 0,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            background: isDark ? '#444' : '#d1d5db',
            border: `1px solid ${isDark ? '#666' : '#9ca3af'}`,
            color: isDark ? '#eee' : '#333',
            cursor: 'pointer', fontSize: 10, fontWeight: 700, lineHeight: 1,
            zIndex: 3,
          }}
        >
          {isCollapsed ? '+' : '\u2212'}
        </button>
      )}
    </div>
  );
}

// ── Node details panel ──────────────────────────────────────────────────

function NodeDetails({ node, isDark }) {
  if (!node) return null;

  const rows = [
    ['Physical Op', node.physicalOp],
    ['Logical Op', node.logicalOp],
    ['Est. Rows', node.estimateRows.toFixed(1)],
    ['Est. CPU', node.estimateCPU.toFixed(6)],
    ['Est. I/O', node.estimateIO.toFixed(6)],
    ['Subtree Cost', node.subtreeCost.toFixed(6)],
    ['Operator Cost', node.operatorCost.toFixed(6)],
    ['Parallel', node.parallel ? 'Yes' : 'No'],
  ];

  const detailEntries = Object.entries(node.details);

  return (
    <div style={{
      padding: '8px 12px', fontSize: 11,
      fontFamily: '"Cascadia Code", "Fira Code", Consolas, monospace',
      background: isDark ? '#252526' : '#f9fafb',
      borderTop: `1px solid ${isDark ? '#444' : '#e5e7eb'}`,
      maxHeight: 200, overflow: 'auto', flexShrink: 0,
    }}>
      <div style={{
        fontWeight: 700, marginBottom: 4, fontSize: 12, fontFamily: 'sans-serif',
        display: 'flex', alignItems: 'center', gap: 6,
      }}>
        <OpIcon op={node.physicalOp} size={20} />
        {node.physicalOp}
      </div>
      <table style={{ borderCollapse: 'collapse', width: '100%' }}>
        <tbody>
          {rows.map(([k, v]) => (
            <tr key={k}>
              <td style={{ padding: '1px 8px 1px 0', color: isDark ? '#888' : '#6b7280', whiteSpace: 'nowrap' }}>{k}</td>
              <td style={{ padding: '1px 0', color: isDark ? '#d4d4d4' : '#1f2937' }}>{v}</td>
            </tr>
          ))}
          {detailEntries.map(([k, v]) => (
            <tr key={k}>
              <td style={{ padding: '1px 8px 1px 0', color: isDark ? '#888' : '#6b7280', whiteSpace: 'nowrap' }}>{k}</td>
              <td style={{ padding: '1px 0', color: isDark ? '#ce9178' : '#d97706', wordBreak: 'break-all' }}>{v}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── Formatted XML fallback ──────────────────────────────────────────────

function formatXml(xml) {
  let formatted = '';
  let indent = 0;
  const tab = '  ';
  xml = xml.replace(/(>)(<)(\/*)/g, '$1\n$2$3');
  xml.split('\n').forEach(line => {
    line = line.trim();
    if (!line) return;
    if (line.match(/^<\/\w/)) indent--;
    formatted += tab.repeat(Math.max(indent, 0)) + line + '\n';
    if (line.match(/^<\w[^>]*[^\/]>$/) && !line.match(/^<\?/)) indent++;
  });
  return formatted.trim();
}

// ── Modal ───────────────────────────────────────────────────────────────

export default function ExecutionPlanModal({ planXml, error, loading, onClose, isDark }) {
  const [zoom, setZoom] = useState(1);
  const [selectedNode, setSelectedNode] = useState(null);
  const [viewMode, setViewMode] = useState('visual');
  const [collapsedSet, setCollapsedSet] = useState(new Set());
  const containerRef = useRef(null);
  const dragging = useRef(false);
  const dragStart = useRef({ x: 0, y: 0, scrollLeft: 0, scrollTop: 0 });

  const zoomIn = () => setZoom(z => Math.min(z + 0.2, 4));
  const zoomOut = () => setZoom(z => Math.max(z - 0.2, 0.3));
  const zoomReset = () => setZoom(1);

  const handleWheel = useCallback((e) => {
    if (e.ctrlKey || e.metaKey) {
      e.preventDefault();
      setZoom(z => Math.max(0.3, Math.min(4, z + (e.deltaY > 0 ? -0.1 : 0.1))));
    }
  }, []);

  useEffect(() => {
    const el = containerRef.current;
    if (!el) return;
    el.addEventListener('wheel', handleWheel, { passive: false });
    return () => el.removeEventListener('wheel', handleWheel);
  }, [handleWheel]);

  const onMouseDown = (e) => {
    if (e.button !== 0) return;
    const el = containerRef.current;
    if (!el) return;
    dragging.current = true;
    dragStart.current = { x: e.clientX, y: e.clientY, scrollLeft: el.scrollLeft, scrollTop: el.scrollTop };
    el.style.cursor = 'grabbing';
    e.preventDefault();
  };

  const onMouseMove = useCallback((e) => {
    if (!dragging.current) return;
    const el = containerRef.current;
    if (!el) return;
    el.scrollLeft = dragStart.current.scrollLeft - (e.clientX - dragStart.current.x);
    el.scrollTop = dragStart.current.scrollTop - (e.clientY - dragStart.current.y);
  }, []);

  const onMouseUp = useCallback(() => {
    dragging.current = false;
    if (containerRef.current) containerRef.current.style.cursor = 'grab';
  }, []);

  useEffect(() => {
    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', onMouseUp);
    return () => { document.removeEventListener('mousemove', onMouseMove); document.removeEventListener('mouseup', onMouseUp); };
  }, [onMouseMove, onMouseUp]);

  useEffect(() => {
    const onKey = (e) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  const onToggleCollapse = useCallback((nodeId) => {
    setCollapsedSet(prev => {
      const next = new Set(prev);
      if (next.has(nodeId)) next.delete(nodeId);
      else next.add(nodeId);
      return next;
    });
  }, []);

  const planTrees = planXml ? parsePlanXml(planXml) : null;

  let totalCost = 0;
  const allNodeIds = [];
  if (planTrees) {
    const walk = (n) => {
      totalCost += n.operatorCost;
      if (n.children.length > 0) allNodeIds.push(n.nodeId);
      n.children.forEach(walk);
    };
    planTrees.forEach(root => walk(root));
  }

  const collapseAll = () => setCollapsedSet(new Set(allNodeIds));
  const expandAll = () => setCollapsedSet(new Set());

  const s = {
    overlay: {
      position: 'fixed', inset: 0, zIndex: 100000,
      background: 'rgba(0,0,0,0.6)',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
    },
    modal: {
      width: '90vw', height: '90vh',
      display: 'flex', flexDirection: 'column',
      background: isDark ? '#1e1e1e' : '#fff',
      border: `1px solid ${isDark ? '#444' : '#d1d5db'}`,
      borderRadius: 8, boxShadow: '0 8px 32px rgba(0,0,0,.4)',
      overflow: 'hidden',
    },
    header: {
      flexShrink: 0, display: 'flex', alignItems: 'center', justifyContent: 'space-between',
      padding: '8px 14px',
      background: isDark ? '#2d2d2d' : '#f3f4f6',
      borderBottom: `1px solid ${isDark ? '#444' : '#e5e7eb'}`,
      fontSize: 13, fontWeight: 600,
    },
    controls: { display: 'flex', alignItems: 'center', gap: 6 },
    btn: {
      background: isDark ? '#3b3b3b' : '#e5e7eb',
      border: 'none', borderRadius: 4, cursor: 'pointer',
      padding: '3px 8px', fontSize: 13, lineHeight: 1,
      color: isDark ? '#ccc' : '#374151',
    },
    btnActive: {
      background: isDark ? '#264f78' : '#dbeafe',
      border: 'none', borderRadius: 4, cursor: 'pointer',
      padding: '3px 8px', fontSize: 13, lineHeight: 1,
      color: isDark ? '#fff' : '#1d4ed8',
    },
    closeBtn: {
      background: 'none', border: 'none', cursor: 'pointer',
      fontSize: 18, color: isDark ? '#999' : '#6b7280', padding: '0 4px',
    },
    container: { flex: 1, overflow: 'auto', cursor: 'grab', minHeight: 0 },
    content: { transformOrigin: '0 0', transform: `scale(${zoom})`, padding: 24, minWidth: 'max-content' },
  };

  let body;
  if (loading) {
    body = <div style={{ padding: 40, textAlign: 'center', color: '#999' }}>Loading execution plan...</div>;
  } else if (error) {
    body = <div style={{ padding: 40, textAlign: 'center', color: '#ef4444' }}>Error: {error}</div>;
  } else if (viewMode === 'visual' && planTrees) {
    body = (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
        {planTrees.map((root, i) => (
          <div key={i}>
            {root.stmtText && (
              <div style={{
                fontSize: 10, color: isDark ? '#888' : '#6b7280', marginBottom: 12,
                maxWidth: 800, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
              }} title={root.stmtText}>
                {root.stmtText}
              </div>
            )}
            <PlanTree
              node={root} totalCost={totalCost} isDark={isDark}
              onSelect={setSelectedNode} selectedId={selectedNode?.nodeId}
              collapsedSet={collapsedSet} onToggleCollapse={onToggleCollapse}
            />
          </div>
        ))}
      </div>
    );
  } else {
    body = (
      <pre style={{
        fontFamily: '"Cascadia Code", "Fira Code", Consolas, monospace',
        fontSize: 12, lineHeight: 1.6, margin: 0, whiteSpace: 'pre',
        color: isDark ? '#d4d4d4' : '#1f2937',
      }}>
        {planXml ? formatXml(planXml) : 'No plan data.'}
      </pre>
    );
  }

  return (
    <div style={s.overlay} onClick={onClose}>
      <div style={s.modal} onClick={e => e.stopPropagation()}>
        <div style={s.header}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <span>Execution Plan</span>
            {planTrees && (
              <div style={{ display: 'flex', gap: 2 }}>
                <button
                  style={viewMode === 'visual' ? s.btnActive : s.btn}
                  onClick={() => setViewMode('visual')}
                >Visual</button>
                <button
                  style={viewMode === 'xml' ? s.btnActive : s.btn}
                  onClick={() => setViewMode('xml')}
                >XML</button>
              </div>
            )}
          </div>
          <div style={s.controls}>
            {viewMode === 'visual' && planTrees && (
              <>
                <button style={s.btn} onClick={collapseAll} title="Collapse all nodes">Collapse</button>
                <button style={s.btn} onClick={expandAll} title="Expand all nodes">Expand</button>
                <span style={{ width: 1, height: 16, background: isDark ? '#555' : '#d1d5db', margin: '0 2px' }} />
              </>
            )}
            <button style={s.btn} onClick={zoomOut} title="Zoom out">&minus;</button>
            <span style={{ fontSize: 11, minWidth: 40, textAlign: 'center' }}>{Math.round(zoom * 100)}%</span>
            <button style={s.btn} onClick={zoomIn} title="Zoom in">+</button>
            <button style={s.btn} onClick={zoomReset} title="Reset zoom">1:1</button>
            <span style={{ width: 1, height: 16, background: isDark ? '#555' : '#d1d5db', margin: '0 4px' }} />
            <button style={s.closeBtn} onClick={onClose} title="Close">&times;</button>
          </div>
        </div>

        <div ref={containerRef} style={s.container} onMouseDown={onMouseDown}>
          <div style={s.content}>{body}</div>
        </div>

        {viewMode === 'visual' && <NodeDetails node={selectedNode} isDark={isDark} />}
      </div>
    </div>
  );
}
