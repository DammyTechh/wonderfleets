import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': path.resolve(__dirname, './src') } },
  server: {
    port: 5173,
    // In development the browser only ever talks to Vite; these forward to the API.
    // /hubs carries the SignalR live-tracking connection, which needs WebSocket upgrades.
    proxy: {
      '/api': 'http://localhost:8080',
      '/hubs': { target: 'http://localhost:8080', ws: true },
    },
  },
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
