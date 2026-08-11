import React from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import { initLinkify } from './enhancers/linkify'
import { initHideConn } from './enhancers/hideConn'

const opts = (typeof window !== 'undefined' && window.__PL_OPTIONS__) || {};

// Mount React overlay components into a dedicated root
let root = document.getElementById('pl-root');
if (!root) {
  root = document.createElement('div');
  root.id = 'pl-root';
  document.body.appendChild(root);
}

createRoot(root).render(
  <React.StrictMode>
    <App options={opts} />
  </React.StrictMode>
)

// Non-React DOM enhancers that must mutate the MiniProfiler HTML directly
if (opts.EnableVsCodeLinks) {
  initLinkify(opts.Scheme || 'vscode');
}
if (opts.HideDefaultConnRows) {
  initHideConn();
}
