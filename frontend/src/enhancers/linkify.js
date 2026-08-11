export function initLinkify(scheme) {
  const rxTest = new RegExp(scheme + '://[^\\s<>"\']+');
  const rxAll = new RegExp(scheme + '://[^\\s<>"\']+', 'g');

  function linkify(root) {
    if (!root || !root.querySelectorAll) return;
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, null, false);
    const targets = [];
    while (walker.nextNode()) {
      const n = walker.currentNode;
      if (n.parentNode && n.parentNode.nodeName !== 'A' && rxTest.test(n.nodeValue)) {
        targets.push(n);
      }
    }
    targets.forEach(n => {
      const span = document.createElement('span');
      span.innerHTML = n.nodeValue.replace(rxAll, m => {
        let trail = '';
        const t = m.match(/[;,.)\]}>]+$/);
        if (t) { trail = t[0]; m = m.slice(0, m.length - trail.length); }
        return '<a href="' + m + '" title="Open in IDE" style="color:#3794ff;text-decoration:underline;cursor:pointer">' + m + '</a>' + trail;
      });
      n.parentNode.replaceChild(span, n);
    });
  }

  function process(root) {
    linkify(root);
  }

  process(document.body);

  const obs = new MutationObserver(muts => {
    muts.forEach(m => {
      Array.prototype.forEach.call(m.addedNodes, nd => {
        if (nd.nodeType === 1) process(nd);
      });
    });
  });
  obs.observe(document.body, { childList: true, subtree: true });
}
