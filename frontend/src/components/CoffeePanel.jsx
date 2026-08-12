import React, { useState } from 'react'

const COFFEE_URL = 'https://buymeacoffee.com/kiettranvq';
const BADGE_URL = 'https://img.shields.io/badge/Buy%20Me%20A%20Coffee-FFDD00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=black';

export default function CoffeePanel({ qrData, isDark }) {
  const [open, setOpen] = useState(false);

  const iconBtn = {
    position: 'fixed', right: 14, bottom: 40, zIndex: 2147483646,
    width: 36, height: 36, borderRadius: '50%',
    background: '#FFDD00', border: 'none', cursor: 'pointer',
    display: 'flex', alignItems: 'center', justifyContent: 'center',
    fontSize: 20, lineHeight: 1,
    boxShadow: '0 2px 8px rgba(0,0,0,.3)',
    transition: 'transform 0.15s',
  };

  const panel = {
    position: 'fixed', right: 14, bottom: 84, zIndex: 2147483646,
    width: 180, background: isDark ? '#2d2d2d' : '#fff',
    color: isDark ? '#d4d4d4' : '#222',
    border: `1px solid ${isDark ? '#444' : '#e2e2e2'}`,
    borderRadius: 8, boxShadow: '0 3px 12px rgba(0,0,0,.25)',
    padding: 12, textAlign: 'center', fontFamily: 'sans-serif',
  };

  return (
    <>
      <button
        style={iconBtn}
        onClick={() => setOpen(o => !o)}
        title="Buy me a coffee"
        onMouseEnter={e => e.currentTarget.style.transform = 'scale(1.1)'}
        onMouseLeave={e => e.currentTarget.style.transform = 'scale(1)'}
      >
        ☕
      </button>
      {open && (
        <div style={panel}>
          {qrData && (
            <a href={COFFEE_URL} target="_blank" rel="noopener noreferrer">
              <img
                src={qrData}
                alt="Buy Me A Coffee QR"
                width={120}
                height={120}
                style={{ display: 'block', margin: '0 auto 8px', border: `1px solid ${isDark ? '#444' : '#eee'}`, borderRadius: 4 }}
              />
            </a>
          )}
          <a href={COFFEE_URL} target="_blank" rel="noopener noreferrer">
            <img
              src={BADGE_URL}
              alt="Buy Me A Coffee"
              style={{ display: 'block', margin: '0 auto', height: 30, borderRadius: 4 }}
            />
          </a>
        </div>
      )}
    </>
  );
}
