# Project Structure & Startup

Move `app.use(pinia)` below `app.use(router)` and the route guard can no longer read the store. Most of what governs the `web/` side lives in orderings like that one, not in some extra layer of convention on top. What sits where, and what runs when, is answered by opening the file.

The reasoning behind the design choices (dynamic routing, data scope, replaceability) lives in [Core Concepts](/guide/concepts). A point-by-point reference of directory responsibilities and development conventions is in [Frontend Standards](/standard/frontend).

## Directory layout

The frontend comes in two layers. The kernel is the npm package `smart-admin-web` — layouts, routing, stores, the built-in pages and the shared components all live in it, published under the same version number as the NuGet packages. Your app is a thin shell pulled out of `web/template`, holding only your own pages, text, API types and startup config. The kernel repo's `web/` contains both: `packages/admin` is the package, `template` is the shell.

The app layer, with paths relative to the app's `web/`:

| Path | Purpose |
|---|---|
| `src/main.ts` | Calls `createSmartAdmin()`, hands your page table, text, `apiBase` and version to the kernel, then mounts |
| `src/api/client.ts` | The app's typed client, `createApiClient()`, sharing the kernel's middleware chain. It starts typed against the kernel endpoints (`KernelPaths`); once you have endpoints of your own, run `gen:api` to generate `src/api/schema.d.ts` and switch to its `paths` |
| `src/views/` | Your own pages; one with the same key as a built-in page replaces it |
| `src/locales/ext/` | Your own text, deep-merged into the built-in dictionaries by namespace |
| `vite.config.ts` | Dev proxy, `__APP_VERSION__`, manual chunking |

The kernel package layer, with paths relative to `web/packages/admin/src/`:

| Directory | Purpose |
|---|---|
| `api/` | `client.ts` (`createApiClient` and the middleware chain) + `index.ts` (built-in endpoints grouped by domain, plus primitives like `unwrap`) + the generated `schema.d.ts` |
| `assets/` | The offline icon subset `icons/ph-subset.json`, the kernel's own SVGs, third-party login brand marks |
| `components/` | Reusable components (FormContainer, the Dict* suite, and more — see `web/COMPONENTS.md`) |
| `composables/` | UI-library-agnostic `use*` logic |
| `directives/` | Custom directives — `auth.ts` defines `v-auth` |
| `layouts/` | Layout shell: header, sidebar, tabs, settings drawer |
| `lib/` | Small setup helpers — `runtime.ts` holds the runtime config, `icons.ts` exports `setupIcons()` |
| `locales/` | Built-in dictionaries plus the `i18n` instance (`index.ts`) |
| `router/` | Static routes (`routes.ts`), the page table (`viewRegistry.ts`), guards and dynamic-route bookkeeping (`index.ts`) |
| `stores/` | Pinia stores (`app`, `auth`, `user`, `tabs`, `dict`) |
| `styles/` | Design tokens (`tokens.css`) and global CSS (`index.css`, `table.css`, `layout.css`) |
| `theme/` | Naive UI theme overrides (`naive-theme.ts`, `accents.ts`, `mix.ts`) |
| `types/` | Hand-written types (`menu.ts`, `api.ts`) |
| `utils/` | Helpers (`error.ts`, `chunkUpload.ts`, `tree.ts`, `ua.ts`, and more) |
| `views/` | Built-in pages, organized by module |

Three more files sit at the root of `src/`. `createSmartAdmin.ts` is the assembly entry point. `index.ts` is the package's public API surface — an app can only reach what it exports, through `import { … } from 'smart-admin-web'`. `App.vue` is the root component.

## Bootstrap sequence

The app's `main.ts` is a single call:

```ts
import { createSmartAdmin } from 'smart-admin-web'
import 'smart-admin-web/style.css'
import phSubset from './assets/icons/ph-subset.json'

createSmartAdmin({
  views: import.meta.glob('./views/**/*.vue'),
  locales: import.meta.glob('./locales/ext/*/*.ts', { eager: true }),
  iconSets: [phSubset],
  apiBase: import.meta.env.VITE_API_BASE,
  dev: import.meta.env.DEV,
  version: __APP_VERSION__,
}).mount('#app')
```

The assembly order lives in the package's `createSmartAdmin.ts` (excerpted; see the source for the full definition):

```ts
export function createSmartAdmin(options: SmartAdminOptions = {}): SmartAdminApp {
  runtime.apiBase = trimTrailingSlashes(options.apiBase ?? '')
  // …version / dev / brand are written into runtime the same way

  const layers: SmartAdminPlugin[] = [...(options.plugins ?? []), options]
  const icons: Record<string, string> = {}
  const iconSets: IconifyJSON[] = []
  for (const layer of layers) {
    if (layer.views) registerViews(layer.views)
    if (layer.locales) registerLocales(layer.locales)
    if (layer.menuTitles) registerMenuTitles(layer.menuTitles)
    if (layer.icons) Object.assign(icons, layer.icons)
    if (layer.iconSets) iconSets.push(...layer.iconSets)
    for (const route of layer.routes ?? []) router.addRoute(route)
  }
  setupIcons(icons, iconSets) // registers the offline sets (ph subsets loaded synchronously) + local SVGs, no whole-set preload

  window.addEventListener('unhandledrejection', e => {
    if (reloadOnChunkError(e.reason)) e.preventDefault()
  })
  window.addEventListener('vite:preloadError', e => {
    if (reloadOnce()) e.preventDefault()
  })

  const pinia = createPinia()
  pinia.use(piniaPluginPersistedstate)

  const app = createApp(AppRoot)
  app.use(pinia) // must precede router — the guard reads from stores
  app.use(router)
  app.use(i18n)
  app.directive('auth', vAuth)
  app.config.errorHandler = (err, _instance, info) => {
    if (reloadOnChunkError(err)) return
    console.error('[SmartAdmin] 未捕获异常', info, err)
  }

  app.provide(SMART_TABLE_DEFAULTS, createSmartTableDefaults({ /* density, pageSizes, labels */ }))

  for (const layer of layers) layer.install?.(app)

  return {
    app,
    router,
    pinia,
    i18n,
    mount: target => {
      app.mount(target)
      return app
    },
  }
}
```

1. **Runtime config** is written into `lib/runtime.ts` first. The rest of the kernel reads it there instead of `import.meta.env`: the package is precompiled, so those values were fixed when the library was built.
2. **Layers merge in order**: plugins in array order, the app itself last, and a later layer wins on a name clash. Then `setupIcons()` registers the offline icon sets and local SVGs. `ph` gets only a small subset — generated from the names actually used — registered synchronously; names outside it are lazy-loaded as the full set by `AppIcon`, with no whole-set preload.
3. **Two global fallbacks** are wired up before the app is even created: `unhandledrejection` (a stray Promise rejection) and `vite:preloadError` (a lazy chunk that failed to load). Both defer to `lib/chunkReload.ts`'s `reloadOnChunkError`/`reloadOnce` — after a new deploy, a 404 on a stale chunk triggers one automatic reload to fetch the new `index.html`; a `sessionStorage` flag limits this to once per tab, after which it's left to the content area's `ErrorBoundary`.
4. **Pinia**, with `pinia-plugin-persistedstate` installed, registered *before* the router — the router guard reads store state.
5. Then **router**, then **i18n**.
6. The **`v-auth` directive** registered globally (`directives/auth.ts`) — shows or hides elements by permission code — immediately followed by `app.config.errorHandler`, which catches whatever slips past `ErrorBoundary` during render: a chunk error triggers the same reload, anything else is logged with `console.error`.
7. **SmartTable defaults**: `SMART_TABLE_DEFAULTS` is provided with a `computed` set of labels (search/reset/refresh/density/column settings, and so on) that read `i18n.global.t`. Because it's a `computed` subscribed to the active locale, switching languages updates every table's labels instantly — no page has to pass `:labels` by hand. Density and page sizes can be changed through the `table` option.
8. **Each layer's `install(app)`** runs before mount, in order. Registrations such as `registerHeaderTool`, `registerMenuBadge`, or your own `app.use` go here.
9. **Mount**: `main.ts` calls `mount('#app')` on the returned object.

The package's `index.ts` imports four stylesheets — `tokens.css`, `index.css`, `table.css`, `layout.css` — and the library build extracts them into a single `style.css`. The app imports `smart-admin-web/style.css` once, at the top of `main.ts`.

`App.vue` is the mount target, and picks up what the assembly function leaves undone:

- Wraps everything in `n-config-provider`, passing `:theme`/`:theme-overrides` from the `useTheme()` composable and `:locale`/`:date-locale` computed from the app store's locale (naive-ui's `zhCN`/`enUS` and `dateZhCN`/`dateEnUS`).
- Nests `n-loading-bar-provider` > `n-message-provider` > `n-dialog-provider` > `router-view` inside it, with `ReauthModal` (the re-authentication prompt) mounted alongside the router view.
- On `onMounted`, uses `useSite()`'s `loadSite()` to fetch the anonymous, site-wide branding info once. The browser title and `<html lang>` are left to `usePageTitle()`, which keeps them following the route and the language.
- Watches the app store's `locale` and keeps `i18n.global.locale.value` in sync (`immediate: true`), so a locale change anywhere in the app is reflected in translations immediately.

## Dev proxy

The app's `vite.config.ts` proxies requests so the browser only ever talks to `:5173`:

```ts
const apiTarget = process.env.SMART_API_TARGET ?? 'http://localhost:5100'

server: {
  port: 5173,
  strictPort: true,
  proxy: {
    '/api': { target: apiTarget, changeOrigin: true },
    '/openapi': { target: apiTarget, changeOrigin: true },
    '/hub': { target: apiTarget, changeOrigin: true, ws: true }, // SignalR notification hub; ws:true proxies the WebSocket upgrade
  },
},
```

The backend defaults to port 5100 in dev. To point the dev server at a different backend instance, set `SMART_API_TARGET` before starting Vite. The backend's CORS defaults to deny-all — same-origin access in local dev works only because of this proxy layer.

`vite.config.ts` also `define`s `__APP_VERSION__` from `package.json`'s `version` field at build time; `main.ts` passes it to the kernel as `version`, and it shows in the login-page footer — frozen at build, not backend-configurable.

## Common scripts

Run the following from the app's `web/`:

| Script | Command |
|---|---|
| `npm run dev` | `vite` — dev server on `:5173` |
| `npm run build` | `vue-tsc --noEmit && vite build` |
| `npm run preview` | `vite preview` |
| `npm run typecheck` | `vue-tsc --noEmit` |
| `npm run gen:api` | `node scripts/gen-api.mjs` (fetches `$SMART_API_TARGET/openapi/v1.json`, default `http://localhost:5100`) |

In the kernel repo, `web/` is the workspace root and its scripts forward to the two layers. `npm run dev` starts the template with `smart-admin-web` pointed at the `packages/admin/src` source, so kernel pages hot-reload. `npm run build` builds the package, then the template. `npm test`, `gen:api` and `gen:icons` act on the package alone; `lint`, `format:check` and `typecheck` cover both.

::: warning gen:api needs a running backend
`gen:api` fetches `/openapi/v1.json` from a live backend, so start the backend first (`dotnet run --project backend/samples/MinimalHost`, or just run `dev-start.bat`). Never hand-edit the generated `schema.d.ts` — the next `gen:api` run overwrites it.
:::

At the repo root, two batch scripts manage the whole stack at once:

| Script | Effect |
|---|---|
| `dev-start.bat` | Opens two windows: backend (`dotnet run --project samples/MinimalHost`, `:5100`) and frontend (`npm install && npm run dev`, `:5173`) |
| `dev-stop.bat` | Kills whatever is listening on ports `5100` and `5173` |

Once this structure is up and running: how routes get stitched together from the backend menu tree is covered in [Routing](/frontend/routing), and how a single API call travels through the typed client is covered in [Request Flow](/frontend/request).
