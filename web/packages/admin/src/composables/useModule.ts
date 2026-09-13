// 多应用门户的进门/切换流程:登录后该直接进哪个应用、还是弹应用选择器,以及切换时清理旧应用的路由与标签。
// enterInitial() 也是 F5/深链的恢复入口(动态路由只在内存里),路由守卫会调它。
// 坑:权限码拉取失败时 permissionsLoaded 保持 false,v-auth 走 fail-closed 藏按钮 —— 宁可少显示,不谎报有权限。
import { useAuthStore } from '#/stores/auth'
import { useTabsStore } from '#/stores/tabs'
import { useUserStore } from '#/stores/user'
import { personalApi } from '#/api'
import { buildRoutesForModule } from './useAuthMenu'
import { router } from '#/router'

type EnterResult = { chooser: true } | { chooser: false; moduleId: number }

// 进门单飞:F5/深链时守卫可能被并发导航各调一次 enterInitial,而 buildRoutesForModule 起手就 resetRouter()
// —— 后一次会把前一次刚注册的动态路由整片摘掉,表现是偶发 404/白屏。模块级 promise 让并发合流成一次。
let entering: Promise<EnterResult> | null = null

/** 进指定应用:重建它的动态路由。 */
async function enter(moduleId: number): Promise<EnterResult> {
  await buildRoutesForModule(moduleId)
  return { chooser: false, moduleId }
}

/** 门户:登录后决定直接进 / 进默认 / 弹选择器,以及切换应用。逻辑与 Naive 无关。 */
export function useModule() {
  const auth = useAuthStore()

  function enterInitial(): Promise<EnterResult> {
    entering ??= doEnterInitial().finally(() => {
      entering = null
    })
    return entering
  }

  async function doEnterInitial(): Promise<EnterResult> {
    // 并行拉模块 + 当前用户权限码。权限码喂 v-auth:成功(哪怕空集=超管)才标 loaded;
    // 失败不阻断进门户,但 permissionsLoaded 保持 false → v-auth fail-closed(藏按钮),不谎报"有权限"。
    // profile 拿超管标记喂 v-auth(只对超管 fail-open,普通用户空集则隐藏)+ 顺手回填顶栏头像;失败按普通用户处理(安全侧,不误放行)。
    const [{ modules, defaultModuleId }, perm, profile] = await Promise.all([
      personalApi.modules(),
      personalApi
        .permissions()
        .then(codes => ({ ok: true, codes }))
        .catch(() => ({ ok: false, codes: [] as string[] })),
      personalApi
        .profile()
        .then(p => ({ sadm: p.isSuperAdmin, avatar: p.avatar ?? null }))
        .catch(() => ({ sadm: useUserStore().userInfo?.isSuperAdmin ?? false, avatar: null })),
    ])
    auth.modules = modules
    auth.defaultModuleId = defaultModuleId ?? null
    auth.permissionCodes = perm.codes
    auth.permissionsLoaded = perm.ok
    auth.isSuperAdmin = profile.sadm
    // 顶栏头像:登录出参不含 avatar,这里回填(取不到按无头像处理,顶栏回落图标)
    const user = useUserStore()
    if (user.userInfo) user.userInfo.avatar = profile.avatar
    if (modules.length === 0) return { chooser: true } // 空态:选择器里提示未分配应用
    // F5/深链优先重建"上次所在应用"(持久化的 currentModuleId),让其动态路由复活,跨应用深链不落 404。
    const remembered = auth.currentModuleId
    if (remembered && modules.some(m => m.id === remembered)) return enter(remembered)
    if (modules.length === 1) return enter(modules[0]!.id)
    if (defaultModuleId && modules.some(m => m.id === defaultModuleId))
      return enter(defaultModuleId)
    return { chooser: true }
  }

  async function switchModule(moduleId: number): Promise<void> {
    await enter(moduleId)
    useTabsStore().clearTabs() // 切应用 → 标签归零(新应用路由已重建)
    router.replace(auth.homePath) // 落到新应用自己的首页
  }

  async function setDefault(moduleId: number): Promise<void> {
    await personalApi.setDefaultModule(moduleId)
    auth.defaultModuleId = moduleId // 本地同步,选择页角标立刻转移,不必重拉 /personal/modules
  }

  return { enter, enterInitial, switchModule, setDefault }
}
