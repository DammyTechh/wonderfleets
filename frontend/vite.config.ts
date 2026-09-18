import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': path.resolve(__dirname, './src') } },
  server: { port: 5173, proxy: { '/api': 'http://localhost:8080' } },
  build: {
    outDir: 'dist',
    sourcemap: false,
    rollupOptions: {
      output: {
        // Charts and the realtime client are heavy and change rarely: keep them cacheable.
        manualChunks: {
          react: ['react', 'react-dom', 'react-router-dom'],
          charts: ['recharts'],
          realtime: ['@microsoft/signalr'],
          data: ['@tanstack/react-query', 'axios'],
        },
      },
    },
  },
})
