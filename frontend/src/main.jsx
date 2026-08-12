import React from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'

const opts = (typeof window !== 'undefined' && window.__PL_OPTIONS__) || {};

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
