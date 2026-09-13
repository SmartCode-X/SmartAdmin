import { createI18n } from 'vue-i18n'
import zhCN from './zh-CN'
import enUS from './en-US'

/**
 * 消费方 i18n 扩展接缝:按 `locales/ext/<locale>/<模块>.ts`(默认导出该模块的键)放文件,
 * 把 `import.meta.glob('./locales/ext/*\/*.ts', { eager: true })` 交给 createSmartAdmin({ locales }) 即并入,
 * 不必改内核语言包。文件名即顶层命名空间(`sample.ts` → `t('sample.xxx')`)。
 *
 * 合并是<b>递归深合并</b>而非整体覆盖。这一点是必需的,不是讲究:后端 msgKey 是嵌套语义键
 * (`error.dict.typeNotFound`,见 ErrorCode.GetMsgKey),消费方给自己的错误码加文案要写成 `{ doc: { titleDuplicated } }`;
 * 而一旦有人想改写内置文案(`{ auth: { passwordWrong: '...' } }`),浅合并会把整个 auth 子树连同
 * captchaExpired/accountLocked 一起静默抹掉,且只在真报那个错时才暴露。深合并让「补一个键」永远只是补一个键。
 */
export type ExtModule = { default: Record<string, unknown> }
type Messages = Record<string, Record<string, unknown>>

const isPlainObject = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v)

/** 递归合并:两边都是普通对象才往下钻,否则 ext 侧胜出。不改动任何入参。 */
function deepMerge(
  base: Record<string, unknown>,
  ext: Record<string, unknown>,
): Record<string, unknown> {
  const out: Record<string, unknown> = { ...base }
  for (const [key, extVal] of Object.entries(ext)) {
    const baseVal = out[key]
    out[key] = isPlainObject(baseVal) && isPlainObject(extVal) ? deepMerge(baseVal, extVal) : extVal
  }
  return out
}

// glob 键只看结尾的 `<locale>/<命名空间>.ts`,前面是 `./ext/` 还是 `./locales/ext/` 都行(取决于消费方在哪个文件里写 glob)。
const EXT_KEY = /(?:^|\/)([^/]+)\/([^/]+)\.ts$/

/** glob 结果按 locale 筛出、按命名空间(文件名)深合并进 base。`mods` 参数化只为可测(见 index.spec.ts)。 */
export function withExt(base: Messages, mods: Record<string, ExtModule>, locale: string): Messages {
  const merged: Messages = { ...base }
  for (const [path, mod] of Object.entries(mods)) {
    const m = EXT_KEY.exec(path)
    if (!m || m[1] !== locale) continue
    const ns = m[2]!
    merged[ns] = deepMerge(merged[ns] ?? {}, mod.default)
  }
  return merged
}

export const i18n = createI18n({
  legacy: false,
  locale: 'zh-CN',
  fallbackLocale: 'en-US',
  messages: {
    'zh-CN': zhCN as Messages,
    'en-US': enUS as Messages,
  },
})

/** 把一批 ext 模块并进当前词典;可多次调用(插件一批、应用一批),后并入的同键覆盖先并入的。 */
export function registerLocales(mods: Record<string, ExtModule>): void {
  for (const locale of i18n.global.availableLocales) {
    const current = i18n.global.getLocaleMessage(locale) as Messages
    i18n.global.setLocaleMessage(locale, withExt(current, mods, locale))
  }
}

/** 供非 setup 上下文(工具函数)使用的翻译器。 */
export const t = i18n.global.t
