import { useEffect, useRef } from 'react'

const VERBS = { GET: 1, POST: 1, PUT: 1, DELETE: 1, PATCH: 1, HEAD: 1, OPTIONS: 1, TRACE: 1, CONNECT: 1 };

function getTable() {
  const t = document.querySelector('table.mp-results-index');
  if (t) return t;
  const all = document.querySelectorAll('table');
  for (let i = 0; i < all.length; i++) {
    if (all[i].tHead && all[i].tHead.rows.length) return all[i];
  }
  return null;
}

function dataRows(table) {
  if (!table) return [];
  const all = table.querySelectorAll('tr');
  const out = [];
  for (let i = 0; i < all.length; i++) {
    const tr = all[i];
    if (table.tHead && table.tHead.contains(tr)) continue;
    if (tr.className && String(tr.className).indexOf('pl-filter-row') !== -1) continue;
    out.push(tr);
  }
  return out;
}

function cellText(row, idx) {
  const c = row.cells[idx];
  return c ? (c.textContent || '').trim() : '';
}

function compare(a, b) {
  const na = parseFloat(a.replace(/[^0-9.\-]/g, ''));
  const nb = parseFloat(b.replace(/[^0-9.\-]/g, ''));
  const aNum = a !== '' && /[0-9]/.test(a) && !isNaN(na);
  const bNum = b !== '' && /[0-9]/.test(b) && !isNaN(nb);
  if (aNum && bNum) return na - nb;
  return a.localeCompare(b);
}

export default function ResultsIndexEnhancer({ enableMethodColumn }) {
  const stateRef = useRef({ table: null, inputs: [], sort: { col: -1, dir: 1 }, scheduled: false });

  useEffect(() => {
    const state = stateRef.current;

    function decorateRows() {
      if (!state.table) return;
      const rows = dataRows(state.table);
      for (let i = 0; i < rows.length; i++) {
        const row = rows[i];
        if (row.getAttribute('data-pl-m')) continue;
        row.setAttribute('data-pl-m', '1');
        const none = row.querySelector('td.mp-results-none');
        if (none && none.colSpan > 1) {
          none.colSpan = none.colSpan - 1;
        } else if (row.cells.length >= 7) {
          row.deleteCell(row.cells.length - 1);
        }
        if (!enableMethodColumn) continue;
        const nameCell = row.cells[0];
        const txt = nameCell ? (nameCell.textContent || '').trim() : '';
        const sp = txt.indexOf(' ');
        const verb = sp > 0 ? txt.slice(0, sp) : txt;
        let method = '';
        if (VERBS[verb.toUpperCase()]) {
          method = verb.toUpperCase();
          const rest = sp > 0 ? txt.slice(sp + 1) : '';
          const a = nameCell ? nameCell.querySelector('a') : null;
          if (a) a.textContent = rest;
          else if (nameCell) nameCell.textContent = rest;
        }
        const td = document.createElement('td');
        td.textContent = method;
        td.style.fontWeight = '600';
        row.insertBefore(td, row.cells[1] || null);
      }
    }

    function applyFilters() {
      decorateRows();
      if (!state.inputs.length) return;
      const terms = state.inputs.map(i => i.value.toLowerCase());
      const active = terms.some(t => t !== '');
      dataRows(state.table).forEach(row => {
        let show = true;
        if (active) {
          for (let c = 0; c < terms.length; c++) {
            if (terms[c] && cellText(row, c).toLowerCase().indexOf(terms[c]) === -1) {
              show = false; break;
            }
          }
        }
        row.style.display = show ? '' : 'none';
      });
    }

    function updateSortIcons(idx) {
      const ths = state.table.tHead.rows[0].cells;
      for (let k = 0; k < ths.length; k++) {
        const ic = ths[k].querySelector('.pl-sort-icon');
        if (!ic) continue;
        if (k === idx) { ic.textContent = state.sort.dir === 1 ? ' \u25B2' : ' \u25BC'; ic.style.opacity = '1'; }
        else { ic.textContent = ' \u2195'; ic.style.opacity = '0.45'; }
      }
    }

    function doSort(idx) {
      decorateRows();
      if (state.sort.col === idx) state.sort.dir = -state.sort.dir;
      else { state.sort.col = idx; state.sort.dir = 1; }
      const rows = dataRows(state.table);
      if (!rows.length) return;
      const parent = rows[0].parentNode;
      rows.sort((r1, r2) => compare(cellText(r1, idx), cellText(r2, idx)) * state.sort.dir);
      rows.forEach(r => parent.appendChild(r));
      updateSortIcons(idx);
      applyFilters();
    }

    function keepHeaderFirst() {
      if (state.table && state.table.tHead && state.table.firstChild !== state.table.tHead) {
        state.table.insertBefore(state.table.tHead, state.table.firstChild);
      }
    }

    function schedule() {
      if (state.scheduled) return;
      state.scheduled = true;
      setTimeout(() => { state.scheduled = false; keepHeaderFirst(); applyFilters(); }, 50);
    }

    function addTitle() {
      if (document.getElementById('pl-title')) return;
      const h = document.createElement('h1');
      h.id = 'pl-title';
      h.textContent = 'Profiling Logs';
      h.style.cssText = 'text-align:center;font-size:2.4rem;font-weight:700;margin:28px auto 18px;font-family:sans-serif;color:#fff';
      document.body.insertBefore(h, document.body.firstChild);
    }

    function stickyBg() {
      const probe = state.table.tHead.rows[0].cells[0];
      let bg = probe ? getComputedStyle(probe).backgroundColor : '';
      if (!bg || bg === 'rgba(0, 0, 0, 0)' || bg === 'transparent') {
        bg = document.documentElement.classList.contains('mp-scheme-dark') ? '#2d2d2d' : '#ffffff';
      }
      return bg;
    }

    function setupFullScreen() {
      document.documentElement.style.height = '100%';
      const b = document.body.style;
      b.margin = '0';
      b.height = '100vh';
      b.boxSizing = 'border-box';
      b.overflow = 'hidden';
      b.display = 'flex';
      b.flexDirection = 'column';
      let scroll = document.getElementById('pl-scroll');
      if (!scroll) {
        scroll = document.createElement('div');
        scroll.id = 'pl-scroll';
        scroll.style.cssText = 'flex:1 1 auto;overflow:auto;width:100%;box-sizing:border-box;padding:0 16px 24px';
        state.table.parentNode.insertBefore(scroll, state.table);
        scroll.appendChild(state.table);
      }
      if (state.table.tHead) {
        state.table.tHead.style.position = 'sticky';
        state.table.tHead.style.top = '0';
        state.table.tHead.style.zIndex = '3';
        const bg = stickyBg();
        for (let r = 0; r < state.table.tHead.rows.length; r++) {
          const cells = state.table.tHead.rows[r].cells;
          for (let c = 0; c < cells.length; c++) {
            cells[c].style.backgroundColor = bg;
          }
        }
      }
    }

    function build() {
      keepHeaderFirst();
      state.table.style.margin = '0 auto';
      const headerRow = state.table.tHead.rows[0];
      for (let d = headerRow.cells.length - 1; d >= 0; d--) {
        if ((headerRow.cells[d].textContent || '').trim().toLowerCase().indexOf('dom complete') !== -1) {
          headerRow.deleteCell(d);
        }
      }
      if (enableMethodColumn) {
        const mth = document.createElement('th');
        mth.textContent = 'Method';
        headerRow.insertBefore(mth, headerRow.cells[1] || null);
      }
      const ths = headerRow.cells;
      for (let i = 0; i < ths.length; i++) {
        ((idx) => {
          const th = ths[idx];
          th.style.cursor = 'pointer';
          th.style.userSelect = 'none';
          const icon = document.createElement('span');
          icon.className = 'pl-sort-icon';
          icon.textContent = ' \u2195';
          icon.style.opacity = '0.45';
          th.appendChild(icon);
          th.addEventListener('click', () => doSort(idx));
        })(i);
      }
      const filterRow = document.createElement('tr');
      filterRow.className = 'pl-filter-row';
      for (let j = 0; j < ths.length; j++) {
        const cell = document.createElement('th');
        cell.style.padding = '4px 6px';
        const input = document.createElement('input');
        input.type = 'text';
        input.placeholder = 'Search\u2026';
        input.style.cssText = 'width:100%;box-sizing:border-box;padding:4px 6px;font:12px sans-serif;border:1px solid #bbb;border-radius:3px';
        input.addEventListener('input', applyFilters);
        state.inputs.push(input);
        cell.appendChild(input);
        filterRow.appendChild(cell);
      }
      state.table.tHead.appendChild(filterRow);
    }

    function init() {
      addTitle();
      state.table = getTable();
      if (!state.table) { setTimeout(init, 200); return; }
      if (!state.table.getAttribute('data-pl-enhanced')) {
        state.table.setAttribute('data-pl-enhanced', '1');
        build();
        setupFullScreen();
      }
      const obs = new MutationObserver(muts => {
        for (let i = 0; i < muts.length; i++) {
          const t = muts[i].target;
          if (t && t.className && String(t.className).indexOf('pl-filter-row') !== -1) continue;
          schedule();
          break;
        }
      });
      obs.observe(state.table, { childList: true, subtree: true });
      schedule();
    }

    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', init);
    } else {
      init();
    }
  }, [enableMethodColumn]);

  return null;
}
