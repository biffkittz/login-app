import { fileURLToPath, URL } from 'node:url'

import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'
import vueDevTools from 'vite-plugin-vue-devtools'

/**
 * Where `npm run dev` forwards `/api` requests: the ASP.NET Core API's "http" launch profile
 * (backend/Properties/launchSettings.json). Set API_PROXY_TARGET to point somewhere else.
 */
const apiProxyTarget = process.env.API_PROXY_TARGET ?? 'http://localhost:5082'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), vueDevTools(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    // Proxying keeps the SPA and the API on one origin during development, exactly like production, where the API
    // serves the built SPA. That single origin is what lets the auth cookie be HttpOnly + SameSite=Strict
    // without any CORS configuration.
    // `ws` also forwards the grocery list's realtime WebSocket (/api/grocery-list/hub).
    proxy: {
      '/api': { target: apiProxyTarget, ws: true },
    },
  },
})
