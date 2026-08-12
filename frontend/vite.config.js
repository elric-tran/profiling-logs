import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: 'dist',
    assetsDir: '',
    rollupOptions: {
      input: 'src/main.jsx',
      output: {
        entryFileNames: 'profilinglogs-ui.js',
        chunkFileNames: 'profilinglogs-[name].js',
        assetFileNames: '[name].[ext]',
        inlineDynamicImports: true,
        format: 'iife'
      }
    }
  }
})
