import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // Mock OpenAI-compatible endpoint; avoids CORS during local dev.
      '/v1': 'http://127.0.0.1:8787',
    },
  },
  build: {
    target: 'es2022',
  },
})
