# Routing & Dynamic Menus

You add a menu in menu administration, fill in the component path, hit save — and it becomes a real, clickable page, with no routing table you wrote by hand anywhere in between. The frontend's routes come from two unrelated sources: a **static shell** frozen at build time, and **dynamic routes** rebuilt at runtime from the current app's menu tree after login.

How the portal decides which app to enter, and how the guards stitch the two sides together, aren't covered here — that's the job of [Multi-App Portal & Router Guards](/frontend/portal-guards).

```text
staticRoutes (router/routes.ts)         buildRoutesForModule (useAuthMenu.ts)
  ├─ /login                               fetch personalApi.menu(moduleId)
  ├─ /oauth/callback                      flatten the menu tree
  ├─ /mfa/bind                            for each Menu-type node:
  ├─ /boot-error                            component string → loader in the page table (viewRegistry)
  ├─ /module  (app chooser)                 router.addRoute('layout', { name: 'menu-{id}', ... })
  └─ /  → layout (default.vue)
        ├─ /personal → PersonalLayout (second-level shell)
        │     └─ profile / password / security / sessions / bindings
        ├─ /personal/notice
        └─ /:pathMatch(.*)*  (404)

Frozen at build time, unchanged          Rebuilt once each on login / app switch /
between deploys.                          hard refresh.
```

The static side never changes between deploys. The dynamic side is driven entirely by the menu tree the currently-selected app returns — different users, different roles, different apps all end up with a different set of routes hanging off `layout`.

## Static routes

`router/routes.ts` defines one top-level tree (excerpted; see the source for the full definition):

```ts
export const staticRoutes: RouteRecordRaw[] = [
  { path: '/login', name: 'login', component: () => import('#/views/login/index.vue'), meta: { public: true } },
  { path: '/oauth/callback', name: 'oauth-callback', component: () => import('#/views/oauth/callback.vue'), meta: { public: true } },
  { path: '/mfa/bind', name: 'mfa-bind', component: () => import('#/views/mfa/index.vue'), meta: { public: true } },
  { path: '/boot-error', name: 'boot-error', component: () => import('#/views/error/BootError.vue') },
  { path: '/module', name: 'module', component: () => import('#/views/module/index.vue'), meta: { title: 'module.choose' } },
  {
    path: '/',
    name: 'layout',
    component: () => import('#/layouts/default.vue'),
    children: [
      {
        path: '/personal',
        name: 'personal',
        component: () => import('#/layouts/PersonalLayout.vue'),
        redirect: '/personal/profile',
        children: [
          // profile / password / security / sessions / bindings, each wrapped through namedPage
        ],
      },
      { path: '/personal/notice', name: 'personal-notice', component: namedPage('personal-notice', () => import('#/views/personal/notice.vue')), meta: { title: 'menu.notice' } },
      { path: '/:pathMatch(.*)*', name: 'not-found', component: namedPage('not-found', () => import('#/views/error/404.vue')) },
    ],
  },
]
```

Several choices here are deliberate:

- **`/` has no static `redirect`.** A `redirect` is evaluated at route-resolve time, which runs *before* the global guard — and at that point the menu tree usually isn't built yet, so any landing spot computed there is guaranteed wrong. Where `/` actually lands is decided by the guard in `router.beforeEach` (see [Multi-App Portal & Router Guards](/frontend/portal-guards)).
- **The 404 is nested inside the shell, not at the top level.** Mistype a URL and the sidebar, tab bar, and logout button are all still there — the user isn't flung out onto a bare page.
- **`/oauth/callback` and `/mfa/bind` carry `meta.public: true`; `/boot-error` doesn't.** The first two need to be reachable before login (an SSO callback result, MFA bind/recovery); `/boot-error` is where a failed portal rebuild lands, and by then the user is usually already logged in — the dynamic routes just failed to build — so public or not doesn't matter there.
- **`/personal` is a second-level shell.** `PersonalLayout` carries five child pages — `profile`/`password`/`security`/`sessions`/`bindings` — all under `/personal/*`; `/personal/notice` sits outside that shell as its own sibling static route. Both are guarded on the backend by `[ActiveSession]` (any logged-in user can read them, no specific permission code needed) — making them menus would mean seeding them and then granting them to every role, pure busywork. Their entry points are the "view all" link on the header's notification bell and the header user dropdown.

When an app needs a top-level route outside the layout shell — a wall-mounted dashboard, a standalone print page — it hands it to `createSmartAdmin({ routes })`. Those routes are registered with `router.addRoute` during assembly, as siblings of `/login` and `/module`, and anything not marked `meta.public` still goes through the login guard.

## Dynamic routes: menu tree → real routes

Everything under `layout` other than the static personal-center routes and the 404 fallback comes from `buildRoutesForModule` in `useAuthMenu.ts`:

```ts
export async function buildRoutesForModule(moduleId: number): Promise<void> {
  const auth = useAuthStore()
  const tree = await personalApi.menu(moduleId)
  auth.menuTree = tree
  auth.currentModuleId = moduleId

  const viewKeys = new Set(viewComponentPaths())
  resetRouter()
  for (const node of flatten(tree)) {
    const route = describeMenuRoute(node, viewKeys)
    if (!route) continue

    if (router.hasRoute(route.name)) router.removeRoute(route.name)
    if (route.kind === 'iframe') {
      router.addRoute('layout', {
        path: route.path,
        name: route.name,
        component: namedPage(route.name, () => import('#/views/embed/iframe.vue')),
        meta: { title: route.title, icon: route.icon, keepAlive: true, iframeSrc: route.iframeSrc },
      })
      registerDynamic(route.name)
      continue
    }

    if (route.kind === 'missing') {
      console.warn('[menu] 缺少视图组件:', route.component)
      router.addRoute('layout', {
        path: route.path,
        name: route.name,
        component: namedPage(route.name, () => import('#/views/error/MissingRoute.vue')),
        meta: { title: route.title, icon: route.icon, keepAlive: true, missingComponent: route.component },
      })
      registerDynamic(route.name)
      continue
    }

    const loader = getView(route.viewKey)!
    router.addRoute('layout', {
      path: route.path,
      name: route.name,
      component: namedPage(route.name, loader),
      meta: { title: route.title, icon: route.icon, keepAlive: true },
    })
    registerDynamic(route.name)
  }
  registerDetailRoutes()
  auth.routesReady = true
}
```

The whole chain: fetch the current app's menu tree (`personalApi.menu(moduleId)`), flatten it into a one-dimensional array, and for every node whose `type` is `MenuType.Menu` (a `Catalog` has no page and a `Button` isn't a route — both are skipped) look its `component` string up in the page table.

The page table lives in `router/viewRegistry.ts`. Built-in pages are registered inside the package; plugin and app pages are merged in through `createSmartAdmin({ views })`, and on a name clash the later one wins. The convention is direct: a menu's `component` field is the path after `views/`, minus the `.vue` extension — so `system/user/index` maps to the built-in `views/system/user/index.vue`, and an app that ships its own `src/views/system/user/index.vue` takes that key over.

**A missing component keeps the original menu path.** If `node.component` doesn't match any key in the page table, `buildRoutesForModule` logs one `console.warn` and materializes the route as `MissingRoute`. Following the menu link then shows the missing component path directly instead of an unexplained 404. `viewComponentPaths()` lists every key in the page table, and the menu form's component-path dropdown takes its options from there. Choosing from that dropdown normally prevents the diagnostic branch from being needed.

Each registered route carries `name: 'menu-{id}'` and `meta.keepAlive: true`, and is hung under the `layout` parent via `router.addRoute('layout', ...)`. Every name added this way is tracked through `registerDynamic(name)`, so it can be torn down precisely on logout or app switch (see `resetRouter` in `router/index.ts`).

## External links and embedded pages: no new menu type

`MenuType` has only `Catalog`/`Menu`/`Button`. External links and iframe embeds reuse the existing `Path` and `Component` fields, with `isHttpUrl()` deciding whether a value starts with `http(s)://`.

| Effect you want | How to configure it | Runtime behavior |
| --- | --- | --- |
| External-link menu | Put the full URL in `Path`; leave `Component` empty | `buildRoutesForModule` builds no route. The sidebar and search open the URL with `window.open` |
| Embedded iframe menu | Put an internal path in `Path`; put the full URL in `Component` | Registers the shared `views/embed/iframe.vue` route and stores the URL in `route.meta.iframeSrc` |

Both cases come down to picking one of `Path`/`Component` and checking whether it "looks like a URL" — no new backend field, no migration to run.

## APIs without a page: the directory is the permission group

Still no new menu type. Endpoints that only mobile apps, handheld scanners or third-party systems call have no page. Their permission buttons hang directly under a directory, and that directory leaves `Path` and `Component` empty. The directory is the permission group: `buildRoutesForModule` skips it (it is not a `Menu`), the sidebar hides it because it has no visible children, and global search does not index it. The role-grant screen renders buttons hanging under a directory as an "API only (no page)" row inside that directory's group, checkable like any other. On the menu-management page a directory row also has a "Permissions" entry, so the first button can be added directly on the directory.

The kernel seed hangs `GET:/api/v1/ping` this way (`Id=301`, under the "Operations" directory).

The iframe view snapshots `route.meta.iframeSrc` during setup instead of reacting to every route change. A cached iframe instance therefore keeps its own URL when the user switches tabs; it cannot be recomputed to `undefined` or to another embedded page.

## Convention-based detail routes

Dropping a `detail.vue` anywhere under `views` adds a `/<module path>/:id/detail` route without touching any route table, and that includes an app's own `src/views`. `registerDetailRoutes()` in `router/detailRoutes.ts` picks the page-table keys ending in `/detail` and runs beside menu-route registration, so login, app switching, and hard-refresh reconstruction restore both sets together.

Detail routes default to `meta.noCache: true` and start with the shared detail title. Once the record loads, `useTabTitle()` may replace the current tab title with the record name. That setter should only be called when the detail page is the current, standalone tab — call it while a detail view is expanded in place inside a list page (switching within the same tab) and it renames the list's tab title instead. The parameter name is fixed to `:id`; use an explicit static route when a page needs another parameter shape or several detail routes in one directory.

## Page caching & named components

`layouts/default.vue` caches pages like this:

```vue
<keep-alive :include="tabs.cachedNames" :exclude="tabs.excludeName">
  <component :is="Component" v-if="rvShow" :key="activeKey" />
</keep-alive>
```

`keep-alive`'s `:include` matches by the rendered component's **`name`**. For a `<script setup>` single-file component, Vue infers that `name` from the filename — and with dozens of identically-named `index.vue` files across `views/**`, those inferred names collide with each other and don't line up with the route's own name (`menu-{id}`). `router/namedPage.ts` plugs that hole:

```ts
export function namedPage(name: string, loader: AsyncComponentLoader) {
  const hit = cache.get(name)
  if (hit?.loader === loader) return hit.comp

  const inner = defineAsyncComponent({ loader, loadingComponent: LOADING, delay: 0 })
  const comp = defineComponent({ name, render: () => h('div', { class: 'page-view' }, h(inner)) })
  cache.set(name, { loader, comp })
  return comp
}
```

Static or dynamic, every page component is wrapped through `namedPage`, giving it an explicit `name` equal to the route name — which is precisely what lets `:include="tabs.cachedNames"` (really an array of `TabItem.name`, i.e. route names) match it. The wrapper is memoized in a `Map` keyed by name and rebuilt only when the underlying **loader reference** changes: `import.meta.glob` returns the same stable function per file, so editing an unrelated menu and triggering a full `buildRoutesForModule` rebuild still reuses the same component object for routes whose `component` path didn't change — their `keep-alive` cache entries are left untouched, not forced to remount. It also wraps the lazy component in a single `<div class="page-view">` root node, because `default.vue`'s `<transition mode="out-in">` requires a single element root, while plenty of page templates are themselves multi-root (a main body plus a few side-by-side modals).

`stores/tabs.ts` adds a safety net on top of this: its `cachedNames` getter filters the tab list down to those where `router.hasRoute(n)` is true, so during the brief window after a menu rebuild — when an old tab's route hasn't been re-registered yet — `keep-alive` is never asked to match a name that doesn't exist. `refreshTab(name)` forces a genuine remount (bypassing the cache) by setting `excludeName` and bumping `reloadKey`; `default.vue` watches `reloadKey` and briefly `v-if`-unmounts the router outlet before restoring it.

::: tip Progress bar and browser title
The routing progress bar is Naive's LoadingBar: `beforeEach` starts it, `afterEach` finishes it, and `onError` turns it red on a failed load. The browser title isn't the guard's business — `usePageTitle()`, called from `App.vue`, builds it from the current page title and the site name, and follows language switches too.
:::

To walk this whole pipeline from scratch — create the view component, seed a menu, get the component path right — see [Add a Frontend Page](/guide/frontend-page).
