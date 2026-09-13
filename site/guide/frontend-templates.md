# The Frontend Template

The frontend kernel is an npm package, `smart-admin-web`: login, layout, dynamic routing and every system-administration page live inside it. The template is a thin shell around that package — once you pull it, `main.ts` holds a single `createSmartAdmin(...)` call, and the rest of the directory is for your own pages and text.

Routing, portal guards, the request layer, permissions, i18n and theming are covered in the [frontend deep dive](/frontend/structure).

## The Stack

| Item | Value |
|---|---|
| Template directory | `web/template` in the repository |
| Frontend package | `smart-admin-web`, same version as the NuGet packages |
| Stack | Vue 3 + Naive UI |
| State | Pinia |
| Table wrapper | SmartTable (`smart-naive-table`) |
| Dev port | `5173` |

The typed client is generated from the backend's `/openapi/v1.json`; `npm run gen:api` refreshes it. Error codes, data permissions and dynamic menus are all defined by the backend contract — the frontend only renders them.

## Pull a Copy as Your Starting Point

To run it straight from the repo, see [Getting Started](/guide/getting-started). To use it as your own project's frontend, pull the template without any git history:

```bash
npx degit SmartCode-X/SmartAdmin/web/template web
cd web
npm install
npm run dev
```

The dev server runs on `5173` and proxies `/api`, `/openapi` and `/hub` to the backend on `:5100`; point it elsewhere with the `SMART_API_TARGET` environment variable. Every file you pulled is yours:

| File | What it does |
|---|---|
| `src/main.ts` | Calls `createSmartAdmin` and hands your pages and text to the kernel |
| `src/api/client.ts` | This app's typed client. The template ships `createApiClient<KernelPaths>()`; after the first `gen:api`, switch it to your own `paths` |
| `src/views/` | Your pages |
| `src/locales/ext/` | Your text, one `<locale>/<module>.ts` file per module |
| `scripts/gen-api.mjs` | Implements `npm run gen:api`, generating `src/api/schema.d.ts` from a running backend |
| `vite.config.ts` | Dev proxy and build chunking |
| `public/` | Static files such as the favicon and logo |

The kernel isn't in any of these files. Upgrading it means changing the `smart-admin-web` version — see [Upgrading to a New Release](/guide/upgrade).

## Plugging into the Kernel

That one call in `main.ts` is the whole integration point:

```ts
import { createSmartAdmin } from 'smart-admin-web'
import 'smart-admin-web/style.css'

createSmartAdmin({
  views: import.meta.glob('./views/**/*.vue'),
  locales: import.meta.glob('./locales/ext/*/*.ts', { eager: true }),
  apiBase: import.meta.env.VITE_API_BASE,
  dev: import.meta.env.DEV,
  version: __APP_VERSION__,
}).mount('#app')
```

A page's key is its path after `views/` minus `.vue`: `src/views/sample/doc/index.vue` has the key `sample/doc/index`, which is exactly what goes into the component path in Menu Management, and a key that matches a built-in page overrides it. By convention, `<module>/detail.vue` becomes the detail route `/<module>/:id/detail`. Add the other options as you need them:

| Option | Purpose |
|---|---|
| `routes` | Top-level static routes outside the layout shell, such as a full-screen dashboard or a standalone print page |
| `menuTitles` | Menu path → i18n key mapping, for databases whose menu titles are stored in Chinese |
| `icons` | Local SVG icons: `import.meta.glob('./assets/svg/*.svg', { query: '?raw', import: 'default', eager: true })` |
| `install(app)` | Registration that runs before mount, such as `registerHeaderTool`, `registerMenuBadge` or your own `app.use` |
| `plugins` | An array of objects shaped like the options above. Precedence runs kernel, then plugins, then the app; later registrations win |
| `brand` | `{ title, logo }`, branding defaults: used while the `sys.site.title` / `sys.site.logo` configs are empty, and for the first frame before site info arrives. A configured value always wins |
| `table` | SmartTable's global default density and page-size options |

## Changing a Built-in Page

Built-in pages live in the package, so they can't be edited in place, and there are two ways forward. One is to copy the whole page into your own `views/` under the same key to override it; that page is then yours to maintain, and upgrades stop updating it. The other is to turn the requirement into a kernel config option. The logo takes the second route: it reads the `sys.site.logo` config, so you change one config entry in the admin UI and touch no page at all.
