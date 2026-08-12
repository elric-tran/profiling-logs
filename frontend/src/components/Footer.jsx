import React from 'react'

export default function Footer() {
  return (
    <div
      id="pl-footer"
      style={{
        position: 'fixed',
        left: 0,
        right: 0,
        bottom: 0,
        zIndex: 2147483644,
        textAlign: 'center',
        font: '12px/1.35 sans-serif',
        color: '#aaa',
        padding: '6px 8px',
        background: 'rgba(0,0,0,.4)',
        pointerEvents: 'none'
      }}
    >
      &copy; 2026<br />Elric Tran
    </div>
  )
}
