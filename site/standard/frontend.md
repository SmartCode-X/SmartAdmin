# Frontend Standards (Vue 3 + Naive UI)

Check your work against this list before writing a page or wiring up an API. The stack is `<script setup>` + Naive UI + Pinia (persisted) + vue-router + vue-i18n + VueUse. In an app the path alias is `@` → `src`, and the kernel's components, composables, stores and API primitives all come through `import { … } from 'smart-admin-web'`; inside the kernel package the alias is `#/` → `src`. See [Core Concepts](/guide/concepts) for the overall architecture, [`web/COMPONENTS.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/COMPONENTS.md) for component usage, and [`web/DESIGN.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/DESIGN.md) for the design system.

## Where things go

- Pages are organized by module/entity: `views/<module>/<entity>/index.vue` — in an app that's `src/views/`, collected into the page table by `createSmartAdmin({ views })`; follow the kernel package's `views/system/menu/index.vue` for a full CRUD example (`SmartTable` + a `FormContainer` modal form + `useConfirm` confirmation).
- `composables/` (`use*`) holds the single source of logic, decoupled from the UI library by default, with error and message callbacks injected by the view. The explicit exceptions are ones that genuinely need a Naive provider in context: `useConfirm` calls `useDialog`/`useMessage` directly, `useTheme` calls `darkTheme` directly, and both can only be called from inside `setup`.
- An app's `api/`: `client.ts` (`createApiClient<paths>()`) + one `<domain>.ts` per domain + the generated `schema.d.ts`; the kernel package's `api/` is `client.ts` + `index.ts` (built-in endpoints grouped by domain) + `schema.d.ts`. See [project structure](/frontend/structure) for the other directories' responsibilities.

## API contract

::: warning schema.d.ts is a generated artifact — don't hand-edit it
`schema.d.ts` is generated from the backend's OpenAPI (`npm run gen:api`, which needs **the backend running** to fetch `/openapi/v1.json`); hand-edits are overwritten the next time you generate — to change a type, change the backend endpoint/DTO and regenerate. This endpoint isn't mounted in production; see the [FAQ](/faq) for details.
:::

- API calls are centralized in the `api/` layer, grouped by domain (`authApi`/`userApi`/`moduleApi`/`menuApi`… in the kernel package's `api/index.ts`; your own modules in `src/api/<domain>.ts`, using the app's `./client` and importing `unwrap`/`pageParams`/`toPage` from `smart-admin-web`); each method is shaped like `client.X(...).then(r => unwrap<T>(r))` — never call `client` bare in a view.
- `unwrap` unwraps the envelope uniformly; failures (`code≠0` or non-2xx) all normalize to `ApiError` (carrying `code`/`msgKey`), and the view `catch`es it and produces copy with `translateError(e)`.
- Pagination is normalized at the API layer into `{ items, total }` to fit SmartTable's `fetcher` (the backend returns `PagedList<T>{current,size,total,items}`).
- Query parameter names use PascalCase (required by ASP.NET model binding).
- Set `VITE_API_BASE` at build time only when the frontend and backend are genuinely cross-origin (CDN / separate domain) — the template's `main.ts` hands it to the kernel as `apiBase` — and the backend must then explicitly configure `SmartAdmin:Api:Cors:AllowedOrigins` (deny-all by default). See [the HTTP request layer](/frontend/request) for the auth / 401-refresh middleware and [consuming backend responses](/frontend/api-contract) for the envelope-unwrapping details.

## Routing

- `router/routes.ts` holds only static routes (login, error, shell/layout); the real menu tree is fetched from the backend after login and injected as dynamic routes (in-memory only, never persisted).
- A menu node's `component` string (e.g. `system/user/index`) is a page-table key — the path after `views/`, minus `.vue`; a page with the same key in an app's `src/views/` replaces the built-in one. A route's `name = menu-${id}`, mounted under `layout`.
- Logout / app switching uses `registerDynamic` / `resetRouter` to add/remove dynamic routes precisely, not resetting the whole route tree.
- An external-link menu (`path` holds a URL, `component` left empty) and an embedded-iframe menu (`component` holds a URL) reuse existing fields instead of adding a new menu type; `views/**/detail.vue` is a convention-based detail route (`/<module>/:id/detail`), paired with the `DetailPage` component and `useTabTitle()`. Both conventions' mechanics are in [Routing & Dynamic Menus](/frontend/routing).

::: danger Don't persist routesReady / menuTree
Persisting them skips the refresh-rebuild flow and sends you straight to a 404 after a refresh — these two pieces of state must live in memory only. See [routing & dynamic menus](/frontend/routing) for the rebuild mechanism.
:::

## State (Pinia)

- `defineStore` + `actions`; **persist selectively** with `pick` — don't blindly persist a whole store in full. `auth` persists only `currentModuleId`; `tabs` persists only `tabs`, to `sessionStorage`. `user`'s tokens and profile would log you out on a refresh if any one were missing, so they go through a custom `serializer` that writes the whole thing to `localStorage`; in Cookie-session mode that same serializer forces the token fields to empty strings and keeps only the `cookieSession` flag.
- Existing stores: `auth` (module/menu/permission codes/`routesReady`), `user` (token/login state), `app` (theme/preferences), `tabs` (tab pages), `dict` (dictionary cache, session-scoped memory only, never persisted, invalidated via `invalidate` after any create/update/delete). Logout goes through `reset()` to clear the auth state and the tabs.

## Composables

- Named `use*`, returning reactive refs and methods.
- List pages uniformly use `smart-naive-table`'s `SmartTable` in remote mode: pass it `:fetcher` with the signature `(p: { page, pageSize, ...params }) => Promise<{ items, total }>`, and SmartTable manages pagination and loading itself.
- Existing ones include `useConfirm` (confirmation dialogs), `useTabTitle` (a detail page's dynamic tab title), and `useRealtime` (the SignalR real-time client, started when the authenticated shell mounts) — usage is in each one's own source header comment and in `web/COMPONENTS.md`.

## Button-level permissions

```vue
<n-button v-auth="'POST:/api/v1/sys/user'">Add</n-button>
```

- A single permission code takes a string; an array is OR by default, and the `.and` modifier does AND; a non-match hides the element with `display: none` (reactive — it shows again once a permission refresh grants it), the element itself stays in the DOM.
- Permission-code values are the backend's normalized routes (same source as `[RolePermission]`), not custom-invented permission strings. See [frontend permissions](/frontend/permission) for details.

## Shared components

- The admin backend has **no component-demo menu**; component usage is consolidated in [`web/COMPONENTS.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/COMPONENTS.md) — read it before writing a page to avoid reinventing the wheel, and update it when you add a new general-purpose component.
- Existing ones include SmartTable / FormContainer / `useConfirm` / StatusSwitch / the dict components (DictSelect, DictTag) / OrgTreeSelect / FileUpload (`chunked` for resumable upload) / ApiSelect (from which UserSelect derives) / UserPicker / PasswordStrength / Chart / CodeBlock / MarkdownEditor / DetailPage (the detail-page shell, paired with `useTabTitle`) / IconPicker, and more — treat `web/COMPONENTS.md` as the authoritative full list, and see the kernel package's `components/<component>/README.md` for each one's detailed API. SmartTable is imported from `smart-naive-table`, everything else from `smart-admin-web`.

## i18n

- All visible text in views goes through `t('...')` — hardcoded Chinese/English literals are forbidden.
- Error copy never comes from the backend: it sends `code` + `msgKey`, and `translateError` **resolves by `msgKey` first**, falling back to a built-in numeric-code whitelist (`CODE_MSG_KEY` in `utils/error.ts`, covering only the kernel's own codes) when there's no `msgKey`. So a locale key must mirror the backend's `[MsgKey]` string exactly — a backend tagging `error.dict.typeNotFound` needs a frontend dictionary entry of the same name; a custom error code isn't in that whitelist, so a numeric entry like `error: { 60001: '...' }` is never read either — it still has to go through `[MsgKey]`. Built-in copy lives in the kernel package's `locales/zh-CN.ts`/`en-US.ts`; your own goes in the app's `src/locales/ext/<locale>/<module>.ts`, merged in through `createSmartAdmin({ locales })`. See [Internationalization](/frontend/i18n) for the mechanism.

## Design system

- Business code consumes only the role-token layer (e.g. `--color-text-primary`), never the primitive layer directly (e.g. `--color-gray-500`); the single source of tokens is the kernel package's `styles/tokens.css`, shipped to the app inside `smart-admin-web/style.css`.
- Component styles use `scoped` + CSS variables (`var(--gap-card)`, etc.), never hardcoded colors/spacing.
- Light/dark switches on `<html data-theme="dark">`, defaulting to light when unset; role tokens / primary color / semantic colors / shadows all flip as a group under it. See [`web/DESIGN.md`](https://github.com/SmartCode-X/SmartAdmin/blob/main/web/DESIGN.md) and [Theme & Icons](/frontend/appearance) for the full spec.

## Before committing

```bash
npm run lint        # oxlint (lint:fix to autofix)
npm run typecheck   # vue-tsc --noEmit
npm run build       # vue-tsc --noEmit && vite build
```

Only when all three pass is it done — don't run just one and assume you're fine. An app pulled from the template has no lint configured; the other two still have to pass. A change under the kernel repo's `web/` also runs `npm run format:check` and `npm test`, which CI's frontend check runs as well.
