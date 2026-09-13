// 外部登录品牌 UI 纯逻辑:可被 vitest 直接驱动,不依赖浏览器。
// 契约见 docs/adr/0007-external-login-brand-ui-and-providers.md。
import { t } from '#/locales'

/** 登录页最多平铺的品牌圆钮数;超出进「…」菜单。 */
export const SSO_VISIBLE_MAX = 4

/** 预置精修品牌标的 provider code(大小写不敏感)。 */
export const BRAND_CODES = ['github', 'wechat', 'wecom', 'dingtalk', 'gitee', 'qq'] as const
export type BrandCode = (typeof BRAND_CODES)[number]

const BRAND_SET = new Set<string>(BRAND_CODES)

/**
 * 登录页临时展示全部品牌圆标(图标验收用)。
 * true:铺全 6 个,不做 N=4 溢出;false:恢复仅后端 enabled providers。
 * 图标验收时在本地把 false 改成 true,验收完改回来,不要带着 true 提交。
 */
export const PREVIEW_ALL_SSO_BRANDS = false

/**
 * 内置品牌显示名。走 i18n 而非硬编码中文常量表 —— 硬编码在英文界面下会露出「微信/企业微信/钉钉」。
 * 调用方须在 computed 里取值,切语言才会重算。
 */
export function brandDisplayName(code: BrandCode): string {
  return t(`oauth.brand.${code}`)
}

/** 图标预览用的假 provider 列表(与后端是否启用无关)。 */
export function previewAllBrandProviders(): SsoProviderLike[] {
  return BRAND_CODES.map(code => ({ code, displayName: brandDisplayName(code) }))
}

/**
 * 配置页卡片上给出「按账号自动关联」开关的 provider。只有企业微信的外部标识是企业统一分配的账号(userid),
 * 钉钉 / 微信 / GitHub 的标识是 unionid 或数字 id,按账号名对不上本地账号;
 * 自定义 provider 真要开(如 sub 就是工号的 OIDC),到「其他配置」加 sys.externalauth.{code}.linkByAccount。
 */
export const LINK_BY_ACCOUNT_CODES: readonly string[] = ['wecom']

/** 配置页一行:内置品牌全展示;registered=后端已装包/已配密钥。 */
export type ConfigProviderRow = {
  code: string
  displayName: string
  icon?: string | null
  registered: boolean
  enabled: boolean
  /** 未绑定的外部身份按账号名关联同名本地账号(sys.externalauth.{code}.linkByAccount) */
  linkByAccount: boolean
}

/**
 * 配置页列表 = 预置品牌(全量) + 其它已注册 code(如 oidc-demo)。
 * 未注册项 registered=false、两个开关强制 false(未部署的方式开了也没用)。
 */
export function buildConfigProviderRows(
  registered: readonly {
    code: string
    displayName: string
    icon?: string | null
    enabled: boolean
    linkByAccount?: boolean
  }[],
): ConfigProviderRow[] {
  const byCode = new Map(registered.map(p => [p.code, p]))
  const rows: ConfigProviderRow[] = []
  const seen = new Set<string>()

  for (const code of BRAND_CODES) {
    seen.add(code)
    const r = byCode.get(code)
    rows.push({
      code,
      displayName: r?.displayName || brandDisplayName(code),
      icon: r?.icon ?? null,
      registered: !!r,
      enabled: r ? r.enabled : false,
      linkByAccount: r?.linkByAccount ?? false,
    })
  }
  for (const p of registered) {
    if (seen.has(p.code)) continue
    seen.add(p.code)
    rows.push({
      code: p.code,
      displayName: p.displayName,
      icon: p.icon,
      registered: true,
      enabled: p.enabled,
      linkByAccount: p.linkByAccount ?? false,
    })
  }
  return rows
}

export type SsoProviderLike = {
  code: string
  displayName: string
  icon?: string | null
}

export type BindingLike = {
  provider: string
  displayName?: string | null
  boundAt: string
}

export type IconResolve =
  | { kind: 'brand'; code: BrandCode }
  | { kind: 'iconify'; name: string }
  | { kind: 'letter'; letter: string }

export type BindingRow = {
  code: string
  displayName: string
  icon?: string | null
  /** false = 运营已关但仍有绑定 */
  enabled: boolean
  binding?: BindingLike
}

/** 是否为可渲染的 Iconify 名称;拒绝 URL / data URI。 */
export function isIconifyName(icon: string | null | undefined): boolean {
  if (icon == null) return false
  const s = icon.trim()
  if (!s) return false
  if (/^https?:\/\//i.test(s) || s.startsWith('//') || /^data:/i.test(s)) return false
  // collection:name(离线 Iconify 常见形态)
  return /^[a-z0-9][a-z0-9-]*:[a-z0-9][a-z0-9._-]*$/i.test(s)
}

export function isBrandCode(code: string | null | undefined): code is BrandCode {
  return !!code && BRAND_SET.has(code.toLowerCase())
}

/** brand map → Iconify 名 → 首字母。 */
export function resolveProviderIcon(code: string, icon?: string | null): IconResolve {
  const c = (code ?? '').trim().toLowerCase()
  if (isBrandCode(c)) return { kind: 'brand', code: c }
  if (isIconifyName(icon)) return { kind: 'iconify', name: icon!.trim() }
  const letter = (code ?? '?').trim().charAt(0).toUpperCase() || '?'
  return { kind: 'letter', letter }
}

const WECOM_AUTO_LOGIN_KEY = 'smart.sso.wecomAutoLogin'

/**
 * 在企业微信客户端里打开登录页时,是否直接发起企业微信登录(客户端内走网页授权,静默、不用点)。
 * 企业微信的 UA 带 wxwork(同时也带微信的 MicroMessenger,不能拿它判断)。
 * 每个浏览器会话只返回一次 true:登录失败或主动退出后再回到登录页就停下,让人自己选登录方式,不会来回跳。
 */
export function claimWeComAutoLogin(opts: {
  userAgent: string
  providers: readonly { code: string }[]
  storage: Pick<Storage, 'getItem' | 'setItem'>
}): boolean {
  if (!/wxwork/i.test(opts.userAgent)) return false
  if (!opts.providers.some(p => p.code === 'wecom')) return false
  try {
    if (opts.storage.getItem(WECOM_AUTO_LOGIN_KEY)) return false
    opts.storage.setItem(WECOM_AUTO_LOGIN_KEY, '1')
    return true
  } catch {
    // 存储不可用时记不住「已经跳过一次」,宁可不自动跳
    return false
  }
}

/** 严格保序切分:前 max 平铺,其余进溢出。 */
export function splitLoginProviders<T>(
  providers: readonly T[],
  maxVisible = SSO_VISIBLE_MAX,
): {
  visible: T[]
  overflow: T[]
} {
  const list = providers.slice()
  if (list.length <= maxVisible) return { visible: list, overflow: [] }
  return { visible: list.slice(0, maxVisible), overflow: list.slice(maxVisible) }
}

/**
 * 绑定页行:已启用 providers(API 序) ∪ 仅存在于 bindings 的已停用项(接在后面)。
 * 不做 N=4 截断。
 */
export function mergeBindingRows(
  providers: readonly SsoProviderLike[],
  bindings: readonly BindingLike[],
): BindingRow[] {
  const bindByProvider = new Map<string, BindingLike>()
  for (const b of bindings) bindByProvider.set(b.provider, b)

  const rows: BindingRow[] = []
  const seen = new Set<string>()

  for (const p of providers) {
    seen.add(p.code)
    rows.push({
      code: p.code,
      displayName: p.displayName,
      icon: p.icon,
      enabled: true,
      binding: bindByProvider.get(p.code),
    })
  }

  for (const b of bindings) {
    if (seen.has(b.provider)) continue
    rows.push({
      code: b.provider,
      displayName: (b.displayName && b.displayName.trim()) || b.provider,
      icon: null,
      enabled: false,
      binding: b,
    })
  }

  return rows
}
