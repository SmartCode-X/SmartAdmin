# SmartIcon and SmartIconPicker

> `smart-naive-icon` — an offline-first icon package for Vue 3 + Naive UI, built on [Iconify](https://iconify.design): `SmartIcon` is the renderer and `SmartIconPicker` the picker. It is a standalone npm package that `smart-admin-web` declares as a peer dependency (`^2.0.0`), consumed through just three thin wrappers inside the kernel package.

<div style="display:flex;gap:.5rem;flex-wrap:wrap;margin:1rem 0">
  <a href="https://www.npmjs.com/package/smart-naive-icon"><img src="https://img.shields.io/npm/v/smart-naive-icon?color=cb3837&logo=npm" alt="npm"></a>
</div>

The menu table has an `icon` field: put a string in it and the sidebar, breadcrumb, and buttons can draw the matching icon. Picking an icon, storing it, and rendering it — SmartAdmin hands all three to this package. It's offline-first: icons render from local data bundled into the app, with no requests to Iconify's online API. This page is about the package itself — its value contract, how to add icon sets, how to drop in local SVGs, and how to translate its copy. How icons are registered app-wide in SmartAdmin and how `AppIcon` renders them across the site belongs to the [Theme & Icons](/frontend/appearance) page; the integration conventions aren't repeated here.

## Picking an icon in menu management

Only three files in the kernel package touch this package, all under `web/packages/admin/src`:

- `setupIcons()` in `lib/icons.ts`, called once by `createSmartAdmin()`, registers the offline icon sets and local SVGs globally;
- `components/IconPicker/index.vue` wraps `SmartIconPicker` and is the app's picker, used on the menu `icon` field under **System → Menu Management**;
- `components/AppIcon.vue` wraps `SmartIcon` and is the app's renderer — every icon drawn across the site goes through it.

The last two are exported from `smart-admin-web` as `IconPicker` and `AppIcon`, so an app's pages can use them too. The menu-management page (`views/system/menu/index.vue`) ties the two ends together: pick with the picker in the form, display with the renderer in a table column.

```vue
<!-- form: pick an icon and store it in form.icon -->
<IconPicker :model-value="form.icon ?? ''" @update:model-value="(v: string) => (form.icon = v)" />

<!-- table column: draw the stored string -->
<AppIcon :icon="row.icon" :size="18" />
```

The `v-model` value (written here as `model-value`) is just **a string**, shaped like `ph:house-duotone` — i.e. `prefix:icon-name`; a local SVG is `local:icon-name`. That string goes into the database's `icon` field as-is, and handing it back to `AppIcon` renders it. The whole contract is this one line: one field stores one string, and both the picker and the renderer understand it.

SmartAdmin's picker wrapper doesn't pass `collections` again — `setupIcons()` already registered them globally, so it just reuses them; it does only one thing the package itself doesn't handle: computing vue-i18n copy into `labels` and injecting it (see below).

## What offline-first means

"Offline-first" doesn't mean the component runs offline — it means **the icon sets you register render only from the local data bundled into your build, and never touch `api.iconify.design`**. Each set (`@iconify-json/<prefix>`) is a separate lazy-loaded chunk in your build, pulled in only the first time you open its tab or first render an icon from it.

The cost is size: every set you register adds a chunk, and big sets aren't cheap (Phosphor ≈ 946 KB gz, Lucide ≈ 85 KB gz), so register on demand rather than dumping them all in at once. In return, in a deployment with no internet or restricted egress, icons behave exactly as they do online. The package also keeps one online fallback: type an unregistered Iconify name by hand and it loads online temporarily when connected — that's for emergencies, not the norm.

## Registering more icon sets

`setupSmartIcon` is the package's single configuration entry point — configure every set at once. SmartAdmin's `setupIcons()` is one wrapper around it (the kernel package's `lib/icons.ts`):

```ts
import { setupSmartIcon } from 'smart-naive-icon'

setupSmartIcon({
  collections: [
    { prefix: 'ph', name: 'Phosphor', loader: () => import('@iconify-json/ph/icons.json').then((m) => m.default) },
    { prefix: 'lucide', name: 'Lucide', loader: () => import('@iconify-json/lucide/icons.json').then((m) => m.default) },
    // one more set: first npm i @iconify-json/<prefix>, then add a loader line with the same prefix
  ],
  preloadPrefix: '__none__', // the package has no "don't preload" switch, so pass a prefix that can never match
})
```

Each collection is a tab in the picker, and the first in `collections` is the one open by default (`ph` in SmartAdmin). Browse the sets you want at [icon-sets.iconify.design](https://icon-sets.iconify.design), note the `prefix`, and install the matching `@iconify-json/<prefix>`. Stored values carry the prefix (`ant-design:home-outlined`), so no matter which tab you originally picked from, `AppIcon` can always match it.

SmartAdmin doesn't use `preloadPrefix` to warm an entire collection — the full `ph` set is 946 KB gz, too much to eat on first paint. It takes a different path instead: a `ph` subset is registered synchronously at startup, and only names outside it fall back to `AppIcon` lazy-loading the full `ph` set. There are two subsets — the kernel's ships with the package, and an app generates its own from its `src` with `smart-admin-icons` — see [Theme & Icons](/frontend/appearance).

If you **never call** `setupSmartIcon` at all, the package ships one Lucide set as a fallback (it lists `@iconify-json/lucide` as a runtime dependency), usable on import — SmartAdmin doesn't take that default path, instead explicitly registering four sets: ph / lucide / ep / ant-design. That list lives in the kernel package; icons an app needs beyond it go in as local SVGs, covered next.

## Dropping in your own SVGs

When a design's icon isn't in any Iconify set, register it as a local SVG. Under Vite, use a glob to read a directory as raw strings; the filename (minus `.svg`) becomes the icon name:

```ts
import { registerLocalIcons } from 'smart-naive-icon'

registerLocalIcons(
  import.meta.glob('/src/assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }),
)
// src/assets/svg/star.svg  ->  stored as local:star
```

SmartAdmin folds this step into `setupIcons()`'s `localIcons` option. An app hands the same glob to `createSmartAdmin({ icons })`, and it's registered alongside the kernel's own SVGs:

```ts
createSmartAdmin({
  icons: import.meta.glob('./assets/svg/*.svg', { query: '?raw', import: 'default', eager: true }),
})
```

Drop an SVG into the app's `src/assets/svg/`, restart dev, and it shows up on the picker's "Local" tab.

## Copy and localization

The package carries **no i18n framework of its own**: every visible label in `SmartIconPicker` comes from a single `labels` object (English by default), and you override just the keys you need. SmartAdmin's picker wrapper computes vue-i18n copy into `labels` and passes it in (the kernel package's `components/IconPicker/index.vue`); wrapped in a `computed`, the picker's copy switches along with the language:

```vue
<SmartIconPicker v-model="icon" :labels="{ placeholder: 'Choose an icon', title: 'Icon' }" />
```

Overridable keys: `placeholder` / `title` / `search` / `local` / `online` / `onlinePlaceholder` / `use` / `offlineHint` / `loading` / `empty` / `more` (which carries a `{n}` placeholder, replaced with the count at render time).

## Using it outside SmartAdmin

It's a standalone package, so it also installs into any other Vue 3 + Naive UI project. It doesn't bundle Vue or Naive UI, providing them as peer dependencies: `vue ^3.3`, `naive-ui ^2.34`, `@iconify/vue ^4 || ^5`; styles are injected by the component automatically, with no separate CSS import. Browser-only (it uses `navigator.onLine` and `v-html`), and the picker must sit inside the app's `<n-config-provider>` for the theme variables to resolve.

```bash
npm i smart-naive-icon
```

For `SmartIconPicker`'s full props list (`collections` / `localIcons` / `cap` / `clearable`, etc.), the `SmartIcon` API, and SSR/Nuxt notes, see the [package README](https://github.com/SmartCode-X/smart-naive-icon/blob/main/README.en.md).
