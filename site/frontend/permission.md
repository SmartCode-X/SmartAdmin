# Frontend Permissions

Every action button wants to show or hide by permission: the people with the right see it, the people without it never get the button at all — they can't click it, so they can't click it and eat a 403. The frontend collapses this rule into a single decision, then applies it to pages two ways — the `v-auth` directive in templates, and the `hasPerm` getter called directly inside render functions. To see how it fits together, start with where the authorization state lives: two Pinia stores, split by their different persistence needs.

## The big picture: two stores, split by what persists

- **`user` store** (the kernel package's `stores/user.ts`) — `accessToken`, `refreshToken`, `cookieSession`, `userInfo` (`userId`, `account`, `name`, `mustChangePassword`, an `isSuperAdmin` snapshot taken at login), plus the `isLoggedIn` getter and the `setSession`/`clear` actions. Persistence goes through a custom `serializer`: in the default mode the tokens and profile are written to `localStorage` wholesale, which is what keeps you logged in across a refresh; in Cookie-session mode (`cookieSession === true`) the token fields always serialize to empty strings and only the `cookieSession` flag is kept — the refresh token lives solely in an HttpOnly cookie, never in `localStorage`.
- **`auth` store** (`stores/auth.ts`) — `modules`, `currentModuleId`, `defaultModuleId`, `menuTree`, `permissionCodes`, `permissionsLoaded`, `isSuperAdmin`, `routesReady`, plus the `homePath` and `hasPerm` getters. Declared `persist: { pick: ['currentModuleId'] }` — **only `currentModuleId` is persisted**.

The split exists because the two sides have different lifetimes. Tokens and profile have to survive a refresh (otherwise "stay logged in" means nothing). Permission codes, the menu tree, and `routesReady` are the opposite — re-fetched every time the app boots: dynamic routes live only in the router's memory, and once `routesReady` is persisted as `true`, a refresh would skip the route rebuild and every dynamic route would 404. `currentModuleId` is the one exception — it's persisted so an F5 or a deep link can first restore "which app you were last in," and then the guard re-fetches everything else via `useModule().enterInitial()`.

## `v-auth`: the button-level directive in templates

Registered globally while `createSmartAdmin()` assembles the app, so an app's own pages can use it directly:

```ts
app.directive('auth', vAuth)
```

The toolbar buttons on a page are where it earns its keep. The built-in user-management page (`web/packages/admin/src/views/system/user/index.vue`) writes its toolbar like this:

```vue
<template #toolbar>
  <n-button v-auth="'POST:/api/v1/sys/user'" type="primary" @click="openAdd">
    {{ t('common.add') }}
  </n-button>
  <n-button
    v-auth="'POST:/api/v1/sys/user/batch-delete'"
    type="error"
    :disabled="!hasSelection"
    @click="batchDelete"
  >
    {{ t('common.batchDelete') }}
  </n-button>
</template>
```

The directive value is the permission code for the endpoint behind that button. Besides a single string, it also accepts an array of codes (OR by default: show if any one matches) and an array with the `.and` modifier (AND: show only if all match):

```vue
<n-button v-auth="['a', 'b']">shown if a OR b matches</n-button>
<n-button v-auth.and="['a', 'b']">shown only if a AND b match</n-button>
```

The directive implements two hooks, `mounted` and `unmounted`. On mount it subscribes to `authStore.hasPerm` with `watchEffect`, so a change in the permission-code set (say, after a permission refresh) re-evaluates without remounting the button; a fail sets `display` to `none`:

```ts
export const vAuth: Directive<HTMLElement, string | string[]> = {
  mounted(el, binding) {
    const auth = useAuthStore()
    const need = binding.value
    const mode = binding.modifiers.and ? 'every' : 'some'
    const stop = watchEffect(() => {
      const ok = Array.isArray(need) ? need[mode](c => auth.hasPerm(c)) : auth.hasPerm(need)
      el.style.display = ok ? '' : 'none'
    })
    stopHandles.set(el, stop)
  },
  unmounted(el) {
    stopHandles.get(el)?.()
    stopHandles.delete(el)
  },
}
```

**It hides the element with `display: none`, closer to `v-show` than to `v-if`'s tear-down-and-rebuild.** The node stays in the DOM, just invisible and unclickable; a permission refresh re-runs the check and the button reappears on its own, no full-page reload needed. `unmounted` tears down that subscription so an unmounted button doesn't leave a listener behind. Don't mistake this for a security boundary, though — `display: none` can be reverted from dev tools. The real authorization decision always happens server-side (the backend's `[RolePermission]` filter is the authority), and this directive is UX only, keeping buttons the user can't use out of sight.

## `hasPerm`: super admin passes / not-loaded hides / exact match

The `v-auth` directive and the buttons in render functions go through the same getter, so the show/hide rule is collapsed into a single place:

```ts
hasPerm(state): (code: string) => boolean {
  return (code) => (state.isSuperAdmin ? true : state.permissionsLoaded && state.permissionCodes.includes(code))
},
```

Three states:

1. **Super admin (`isSuperAdmin`) → fail-open.** Everything is shown, echoing the `sadm`-claim bypass in the backend's `[RolePermission]`.
2. **Permission codes not loaded yet (`permissionsLoaded === false`) → fail-closed.** Every gated button is hidden. This isn't a corner case to wave off. The guard `await`s `enterInitial`, so a normal login never actually passes through a flicker window where permissions aren't in yet — what fail-closed really guards against is `/personal/permissions` failing to fetch at all. When you genuinely don't know whether a user has a permission, wrongly saying "yes" is far worse than wrongly saying "no." If "not loaded" were treated as "has permission," every gated button (including the ones the user has no rights to) would flash into view and then vanish. Fail-closed guarantees the user only ever sees buttons they can use — no flash of forbidden UI.
3. **Loaded regular user → exact match against `permissionCodes`.** An empty `permissionCodes` (a user with no grants at all) can never match any code, so gated buttons all stay hidden. `isSuperAdmin` and `permissionCodes` are entirely separate fields, so an empty permission set is never mistaken for a super admin and can't accidentally unlock everything.

## Spreading the gate across every action button

`v-auth` is template syntax — it only works inside `<template>`. But a list page's inline row actions — edit, delete, copy, reset password, force-logout, restore — are assembled with `h()` inside a column's `render` function, out of the directive's reach. List pages, tree-table pages, and menu management's own button editor (`ButtonManager.vue`) all spread their operation columns with the same decision: render calls `authStore.hasPerm(code)` directly, `h()`-ing the button on a hit and returning `null` on a miss. Same rule behind it as the directive, just imperative instead of declarative.

The user-management page's operations column is the canonical form:

```ts
// views/system/user/index.vue — operations column
render: (r) =>
  h(NSpace, { size: 4 }, () => [
    authStore.hasPerm('PUT:/api/v1/sys/user/{id}')
      ? h(NButton, { onClick: () => openEdit(r) }, () => t('common.edit'))
      : null,
    authStore.hasPerm('PUT:/api/v1/sys/user/{id}/password')
      ? h(NButton, { onClick: () => openReset(r) }, () => t('user.resetPassword'))
      : null,
    // No delete button on a super-admin row: no permission for it, and it guards against accidents; regular users toggle by the DELETE code
    r.isSuperAdmin || !authStore.hasPerm('DELETE:/api/v1/sys/user/{id}')
      ? null
      : h(NPopconfirm, { onPositiveClick: () => remove(r) }, {
          trigger: () => h(NButton, { type: 'error' }, () => t('common.delete')),
          default: () => t('user.deleteConfirm', { name: r.name }),
        }),
  ]),
```

When there are too many actions for one row (the org page collapses four into "Edit + More ▾"), each option in the dropdown is filtered by code too, and if none survive the dropdown simply isn't rendered:

```ts
// views/system/org/index.vue — operations column
const dropdownOptions = [
  authStore.hasPerm('POST:/api/v1/sys/org/add') ? { key: 'addChild', label: t('org.addChild') } : null,
  authStore.hasPerm('POST:/api/v1/sys/org/{id}/copy') ? { key: 'copy', label: t('org.copy') } : null,
  authStore.hasPerm('DELETE:/api/v1/sys/org/{id}') ? { key: 'delete', label: t('common.delete') } : null,
].filter((o) => o !== null)
// ...
dropdownOptions.length ? h(NDropdown, { options: dropdownOptions }) : null
```

Not every gate is a hide. A button like an enable/disable switch is better disabled than hidden — hide it and the user assumes the feature doesn't exist; disable it and you're telling them "there's a switch here, you just can't work it." So the status column's `StatusSwitch` wires the permission into `disabled`:

```ts
// views/system/user/index.vue — status column
h(StatusSwitch, {
  value: r.enabled,
  // Super admin can't be disabled (prevents self-lockout — disable it and there's no way back from the UI, and the backend protects it too); no enable/disable permission is likewise greyed out
  disabled: r.isSuperAdmin || !authStore.hasPerm('PUT:/api/v1/sys/user/{id}/enabled'),
  request: (next: boolean) => userApi.setEnabled(r.id, next),
})
```

## The permission-code convention

A permission code is the normalized route itself — `{METHOD}:/{route template}` (e.g. `GET:/api/v1/ping`) — and there's no separate string vocabulary to keep in sync. When the frontend logs into the portal, `useModule().enterInitial()` calls `GET /personal/permissions` in parallel to fetch the current user's code set (stored in `authStore.permissionCodes`) and `GET /personal/profile` to get the super-admin flag (stored in `authStore.isSuperAdmin`); `permissionsLoaded` goes true only when the code fetch succeeds, and a failed fetch hides every gated button (fail-closed); a failed profile fetch treats the user as ordinary. Either way it errs toward less permission, never toward over-permission.

What a role checks on the grant screen is a button, and a button may carry several route codes joined by `;` — "User · Query", for instance, covers the user list and detail routes. `/personal/permissions` returns the expanded set of single codes, so `v-auth` and `hasPerm` only ever see single route codes; how many a button carries is invisible to them.

Since the permission code *is* the route, the frontend has no reason to invent its own permission vocabulary. This also draws the line between the two ends cleanly: the frontend only decides button show/hide and disable by code, while the backend computes and enforces that same code — how it normalizes a route into a permission code, and how `[RolePermission]` validates the session and the grant, is in the [Request Pipeline](/backend/request-pipeline); the design around swapping the authorization step (a different permission computation, a different session check) is in the [Replaceability Model](/backend/replaceability).
