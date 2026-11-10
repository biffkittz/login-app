# Nibbles: frontend

The Vue 3 + TypeScript single-page app for Nibbles (the Login App), built with Vite and styled with Tailwind CSS. In production
it is served by the ASP.NET Core API (see [../../docs/BUILD_AND_DEPLOY.md](../../docs/BUILD_AND_DEPLOY.md)); in
development the Vite dev server proxies `/api` to the API running on <http://localhost:5082>.

Set-up, conventions and troubleshooting live in [../../docs/DEVELOPMENT.md](../../docs/DEVELOPMENT.md), and the
structure is explained in [../../docs/ARCHITECTURE.md](../../docs/ARCHITECTURE.md#frontend).

## Scripts

| Command | What it does |
| --- | --- |
| `npm ci` | Install the exact dependency versions from `package-lock.json` |
| `npm run dev` | Dev server with hot module replacement on <http://localhost:5173> (needs the API running) |
| `npm run build` | Type-check (vue-tsc) and build for production into `dist/` |
| `npm run test` | Run the unit and component tests once (Vitest + Vue Test Utils) |
| `npm run test:unit` | Run the tests in watch mode |
| `npm run type-check` | Type-check the app and the tests in strict mode |
| `npm run lint` | oxlint, then ESLint (`plugin:vue/recommended` plus the project rules), fixing what it can |
| `npm run format` | Format `src/` with Prettier |

Set `API_PROXY_TARGET` to proxy to an API somewhere other than `http://localhost:5082`.
