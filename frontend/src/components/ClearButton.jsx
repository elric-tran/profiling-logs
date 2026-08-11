import React, { useState } from 'react'

export default function ClearButton({ clearPath }) {
  const [clearing, setClearing] = useState(false);

  const handleClear = () => {
    if (!window.confirm('Clear ALL stored profiler results (every captured API call)?')) return;
    setClearing(true);
    fetch(clearPath, { method: 'POST', headers: { 'X-Requested-With': 'fetch' } })
      .then(() => window.location.reload())
      .catch(() => {
        setClearing(false);
        alert('Clear failed.');
      });
  };

  return (
    <button
      id="pl-clear-cache-btn"
      type="button"
      onClick={handleClear}
      disabled={clearing}
      style={{
        position: 'fixed',
        top: 10,
        right: 10,
        zIndex: 2147483647,
        padding: '8px 14px',
        background: '#c0392b',
        color: '#fff',
        border: 'none',
        borderRadius: 4,
        cursor: clearing ? 'default' : 'pointer',
        font: '13px/1.2 sans-serif',
        boxShadow: '0 1px 4px rgba(0,0,0,.3)'
      }}
    >
      {clearing ? 'Clearing\u2026' : '\uD83D\uDDD1 Clear all profiler results'}
    </button>
  )
}
