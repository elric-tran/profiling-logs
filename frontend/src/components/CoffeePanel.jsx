import React from 'react'

export default function CoffeePanel({ qrData, url }) {
  if (!url) return null;

  return (
    <div
      id="pl-coffee"
      style={{
        position: 'fixed',
        right: 12,
        bottom: 56,
        width: 132,
        background: '#fff',
        color: '#222',
        border: '1px solid #e2e2e2',
        borderRadius: 8,
        boxShadow: '0 3px 12px rgba(0,0,0,.25)',
        padding: 8,
        textAlign: 'center',
        fontFamily: 'sans-serif',
        zIndex: 2147483646
      }}
    >
      <a href={url} target="_blank" rel="noopener noreferrer">
        <img
          src={qrData}
          alt="Buy Me A Coffee QR"
          width={96}
          height={96}
          style={{
            display: 'block',
            margin: '0 auto 6px',
            border: '1px solid #eee',
            borderRadius: 4
          }}
        />
      </a>
      <a
        href={url}
        target="_blank"
        rel="noopener noreferrer"
        style={{
          display: 'inline-block',
          background: '#FFDD00',
          color: '#000',
          fontWeight: 700,
          fontSize: 11,
          textDecoration: 'none',
          padding: '5px 8px',
          borderRadius: 5
        }}
      >
        &#9749; Buy me a coffee
      </a>
    </div>
  )
}
