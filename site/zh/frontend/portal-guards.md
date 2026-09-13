# 多应用门户与路由守卫

登录后进哪个应用，由一道阶梯逐级判定：记住的、唯一的、默认的，一个都不成立，才弹选择器让用户自己挑。一个用户可能同时被授权好几个应用（模块），进哪个不能写死，得按当前用户现算。

## 登录之后进哪个应用：enterInitial

SmartAdmin 的外壳是个多应用门户：每个用户被授权若干个应用，右上角有个九宫格选择器随时切换。登录后或硬刷新后，决定「直接进某个应用」还是「弹选择器」的，是 `composables/useModule.ts` 里的 `enterInitial()`：

```ts
async function enterInitial(): Promise<EnterResult> {
  const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
    personalApi.modules(),
    personalApi.permissions().then((codes) => ({ ok: true, codes })).catch(() => ({ ok: false, codes: [] as string[] })),
    personalApi.profile().then((p) => ({ sadm: p.isSuperAdmin })).catch(() => ({ sadm: false })),
  ])
  auth.modules = modules
  auth.defaultModuleId = defaultModuleId ?? null
  auth.permissionCodes = perm.codes
  auth.permissionsLoaded = perm.ok
  auth.isSuperAdmin = profile.sadm
  if (modules.length === 0) return { chooser: true }
  const remembered = auth.currentModuleId
  if (remembered && modules.some((m) => m.id === remembered)) return enter(remembered)
  if (modules.length === 1) return enter(modules[0]!.id)
  if (defaultModuleId && modules.some((m) => m.id === defaultModuleId)) return enter(defaultModuleId)
  return { chooser: true }
}
```

模块列表、权限码、超管标记，三者并行拉取。后两者都是失败即收紧。`personalApi.permissions()` 一旦失败，`permissionsLoaded` 就留在 `false`。这时 `v-auth` 指令把它当成「藏起来」，而不是在拿不准的时候放行。`profile` 拉不到就按普通用户处理，不会把谁误当成超管。这一步不阻断进门户。权限拿不到你照样能进，只是除超管外的所有按钮先按「没权限」处理。超管例外，它走 `hasPerm` 里 `isSuperAdmin` 那条 fail-open 分支，权限码拉不到也照显，最后有服务端的 `sadm` 兜底。

拉完这些数据，`enterInitial` 走一个「进哪个应用」的判定阶梯，自上而下，第一个命中的赢：

- **一个应用都没分配** → 弹选择器，选择页里显示一条「未分配应用」的空态提示。
- **有记住的应用，而且它还在你的应用列表里** → 直接进它。这个「记住的应用」是 `auth.currentModuleId`，也是 auth store 里唯一持久化的字段（`buildRoutesForModule` 每次进某个应用时把它写进去）。硬刷新或深链能落回上次那个应用，靠的就是这一条。
- **只有一个应用** → 直接进，没必要弹选择器。
- **配了默认应用**（`defaultModuleId`，可在选择页用 `setDefault` 设定）且它在列表里 → 直接进。
- **以上都不满足** → 弹选择器。

切换应用走的是另一条路。`switchModule(moduleId)` 先重新 `enter()` 一次，重建那个应用的动态路由。`enter()` 内部就是调一次[路由与动态菜单](/zh/frontend/routing)那页讲过的 `buildRoutesForModule(moduleId)`。接着清空标签页 store，换了应用，标签栏理应从零开始。最后把当前路由替换成新应用自己的 `homePath`。`homePath` 是 auth store 的一个 getter。它优先取模块自己的 `defaultRoute`，没有就退到菜单树的第一个叶子，再没有就兜底回 `/module`。一个菜单都没配的应用根本没有首页可言。这种时候把人送回选择器，好过让他撞上一个不属于本应用的路径吃 404。

## 守卫：每次导航都要过一遍 beforeEach

`router/index.ts` 的 `beforeEach` 就是把静态壳、动态路由和门户状态缝在一起的接缝（节选，完整定义见源码）：

```ts
router.beforeEach(async to => {
  const user = useUserStore()
  const auth = useAuthStore()

  // Cookie 会话:access 只在内存,F5 后靠 cookieSession 标记 + HttpOnly refresh 静默换发。
  if (!user.accessToken && (user.cookieSession || user.refreshToken)) {
    const ok = await ensureAccessToken()
    if (!ok) user.clear()
  }

  if (to.name === 'login') {
    const needReauth = !!(to.query.pendingLink || to.query.totpChallenge)
    if (needReauth) {
      if (user.accessToken || user.refreshToken || user.cookieSession) {
        resetRouter()
        auth.reset()
        user.clear()
      }
      return true
    }
    return user.isLoggedIn ? { path: '/', replace: true } : true
  }

  // 公开的 OAuth 回调和 MFA 绑定/恢复页不能被登录守卫送回登录页。
  if (to.meta.public) return true

  if (!user.isLoggedIn) return { path: '/login', replace: true }

  if (user.userInfo?.mustChangePassword) {
    return to.path === '/personal/password' ? true : { path: '/personal/password', replace: true }
  }

  // 重建失败页本身就是重建失败的落点,放行,否则下面的重建守卫会在它身上再试一次,变成死循环。
  if (to.name === 'boot-error') return true

  if (!auth.routesReady) {
    try {
      const { useModule } = await import('#/composables/useModule')
      const res = await useModule().enterInitial()
      if (res.chooser) return to.name === 'module' ? true : { path: '/module', replace: true }
      if (to.name === 'module') return true
      if (to.path === '/') return { path: auth.homePath, replace: true }
      return to.fullPath
    } catch (e) {
      // 只有会话确实失效才清会话;网络抖动/5xx/限流留在原地给可重试的错误页(见 bootFailure.ts)。
      if (isSessionDead(e)) {
        resetRouter()
        auth.reset()
        user.clear()
        return { path: '/login', replace: true }
      }
      return { path: '/boot-error', query: { redirect: to.fullPath }, replace: true }
    }
  }

  if (to.path === '/') return { path: auth.homePath, replace: true }
  return true
})
```

它按顺序把这些事料理掉：

**Cookie 会话的静默续令牌。** 内存里没有 `accessToken`、但要么带着 `cookieSession` 标记、要么还留着 `refreshToken` 时，先 `await ensureAccessToken()` 试一次静默刷新。Cookie 会话下 `accessToken` 只活在内存里，F5 之后必然是空的，得先靠 HttpOnly 的 refresh Cookie 换一次；默认模式下令牌已经从 `localStorage` 水合好，这一步立即成立，直接跳过。刷新失败就清掉本地残留的登录态。

**登录跳转，含 SSO 二次验证。** `/login` 是唯一免认证页；已登录的人再访问会被弹回 `/`。但 URL 带着 `pendingLink`（SSO 未绑定）或 `totpChallenge`（SSO 之后的二次验证）时是例外——这两种场景必须停在登录页完成账密或二次验证，哪怕这时候还残留着一份未清干净的会话（不清掉的话，用户会看到「解绑第三方账号后再登录，页面却直接跳过登录页」这种怪现象）。

**`meta.public` 直接放行。** OAuth 回调、MFA 绑定/恢复这些公开路由，不会被下面「未登录则送去 `/login`」这条判断拦下。

**强制改密。** `mustChangePassword` 一旦为真，除了 `/personal/password` 本身，任何导航都被拦下、重定向到那里。这个标志是管理员建号或重置密码后首登带上的。这一判定刻意放在下面的动态路由重建**之前**。为什么？改密页是静态路由，不依赖菜单树就能渲染，先放行它，能避免「重建 → 选应用 → 又被弹回改密页」这种绕圈。改密成功后现有流程会强制登出重登，标志由后端清零。

**`boot-error` 自身放行。** 这是门户重建失败时的落点，本身不能再被下面的重建守卫拦下来重试一次，否则就是死循环。

**刷新 / 深链的重建，按错误类型分诊。** 动态路由只活在 router 的内存路由表里，不持久化。硬刷新或者直接打开一条深链时，`auth.routesReady` 必然是 `false`，任何 `menu-{id}` 路由都还没注册。守卫一检测到这点，就在这里把 `enterInitial()` 调进来。它既重建路由，又填好 `auth.modules`。所以门户的判定和守卫的重建，其实共用这同一次调用。拿到结果，守卫再决定去向。结果是选择器，就去 `/module`。要是本来就去 `/module`，直接放行，因为渲染选择页要的数据这时已经齐了。这种情况不能弹回 `/`，否则默认应用一旦设定，就再没入口去改它。目标是 `/`，就直接给出 `auth.homePath`。其余情况，重新返回 `to.fullPath`，让同一个 URL 在路由建好之后再解析一次。`enterInitial()` 抛错时不会一律清空登录态：`isSessionDead(e)`（`router/bootFailure.ts`）先分诊——只有 HTTP 401，或后端明说令牌失效的两个业务码，才判定会话真的没了，清会话送回 `/login`；网络抖动、5xx、被限流不是会话丢了，留在原地跳到一个可重试的 `/boot-error`，没必要连当前页面也一起搭进去。

这里有两个位置不返回 `to.fullPath`，值得留意。第一个，目标是 `/` 的时候。返回 `to.fullPath` 等于重定向到自身，而 `/` 没有静态 `redirect`，Vue Router 会判成无限重定向。另一个坑更隐蔽。这段重建逻辑不能用 `to.meta.public` 提前短路。因为一条还没注册的动态路由会先命中 catch-all(404)，而它带着 `public` 标记。真按 `public` 放行，用户看到的就是一个错的 404，而不是重建后的正确页面。

**`/` 永远落到 `auth.homePath`。** 路由已经就绪的正常导航里，访问 `/` 同样交给守卫算首页。这条判断不能写成 `layout` 路由上的静态 `redirect`，原因和上面一样。`redirect` 在 resolve 阶段求值，早于这个守卫。那时候菜单树还没准备好，`homePath` 自然也没有，算出来的落点必然是错的。

导航确认之后，`afterEach` 把访问过的页面记成标签，但有三类要跳过：标了 `meta.public` 的页面、`login`/`module`/`not-found`/`personal` 这四个固定名字、还有没挂在 `layout` 下的路由。最后这一类不属于任何应用的工作区，不该在标签栏留痕：

```ts
router.afterEach(to => {
  if (to.meta.public) return
  if (['login', 'module', 'not-found', 'personal'].includes(to.name as string)) return
  if (!to.matched.some(r => r.name === 'layout')) return
  useTabsStore().addTab(to)
})
```

要是你找的是动态路由本身怎么从菜单树长出来的，去[路由与动态菜单](/zh/frontend/routing)。`buildRoutesForModule` 怎么把每个菜单节点的 `component` 字符串换成真实的懒加载组件，`namedPage` 又怎么给它一个稳定身份、好让 `keep-alive` 认得出，都在那页。门户「进哪个应用」的决策，还有守卫每次导航时怎么把它调进来，到这里就讲完了。
