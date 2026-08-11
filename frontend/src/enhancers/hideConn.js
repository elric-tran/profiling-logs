export function initHideConn() {
  const defaultConnRx = /^sql\s*-\s*(Open|Close)/i;

  function hide(root) {
    if (!root) return;
    const rows = [];
    if (root.matches && root.matches('tr[data-timing-id]')) rows.push(root);
    if (root.querySelectorAll) {
      Array.prototype.push.apply(rows, root.querySelectorAll('tr[data-timing-id]'));
    }
    rows.forEach(tr => {
      const ct = tr.querySelector('.mp-call-type');
      if (ct && defaultConnRx.test((ct.textContent || '').trim())) {
        tr.style.display = 'none';
      }
    });
  }

  hide(document.body);

  const obs = new MutationObserver(muts => {
    muts.forEach(m => {
      Array.prototype.forEach.call(m.addedNodes, nd => {
        if (nd.nodeType === 1) hide(nd);
      });
    });
  });
  obs.observe(document.body, { childList: true, subtree: true });
}
