import { describe, expect, it } from 'vitest'
import { withExt, registerLocales, i18n } from './index'

const mod = (keys: Record<string, unknown>) => ({ default: keys })

describe('withExt', () => {
  // 形状照内置真实结构:error 是嵌套语义键(后端 msgKey = error.dict.typeNotFound),不是扁平数字码。
  const base = {
    user: { title: '用户', name: '姓名' },
    error: {
      _fallback: '操作失败',
      auth: { passwordWrong: '账号或密码错误', captchaExpired: '验证码已过期' },
    },
  }

  it('无 ext 文件时原样返回', () => {
    expect(withExt(base, {}, 'zh-CN')).toEqual(base)
  })

  it('新命名空间按文件名并入', () => {
    const out = withExt(base, { './ext/zh-CN/sampleDoc.ts': mod({ title: '标题' }) }, 'zh-CN')
    expect(out.sampleDoc).toEqual({ title: '标题' })
    expect(out.user).toEqual(base.user)
  })

  // 消费者给自己的错误码加文案是文档里明写的常规操作,写法是嵌套的 { doc: { titleDuplicated } }
  // ——必须并进内置 error,不能把 _fallback / auth 顶掉。
  it('自定义错误码并入内置 error,内置键存活', () => {
    const out = withExt(
      base,
      { './ext/zh-CN/error.ts': mod({ doc: { titleDuplicated: '文档标题重复' } }) },
      'zh-CN',
    )
    expect(out.error).toEqual({
      _fallback: '操作失败',
      auth: { passwordWrong: '账号或密码错误', captchaExpired: '验证码已过期' },
      doc: { titleDuplicated: '文档标题重复' },
    })
  })

  // 深层覆写:改写内置一条错误文案,不能连坐同一子树的其他键(浅合并会在这里静默抹掉 captchaExpired)。
  it('深层覆写只动那一个键,兄弟键不连坐', () => {
    const out = withExt(
      base,
      { './ext/zh-CN/error.ts': mod({ auth: { passwordWrong: '账号或密码不正确' } }) },
      'zh-CN',
    )
    expect(out.error.auth).toEqual({
      passwordWrong: '账号或密码不正确',
      captchaExpired: '验证码已过期',
    })
    expect(out.error._fallback).toBe('操作失败')
  })

  it('同键时 ext 覆盖内置', () => {
    const out = withExt(base, { './ext/zh-CN/user.ts': mod({ title: '员工' }) }, 'zh-CN')
    expect(out.user).toEqual({ title: '员工', name: '姓名' })
  })

  it('对象覆盖字符串时 ext 侧胜出,不炸', () => {
    const out = withExt(base, { './ext/zh-CN/user.ts': mod({ title: { a: 'x' } }) }, 'zh-CN')
    expect(out.user.title).toEqual({ a: 'x' })
  })

  it('只吃当前 locale 的文件', () => {
    const mods = {
      './ext/zh-CN/sampleDoc.ts': mod({ title: '标题' }),
      './ext/en-US/sampleDoc.ts': mod({ title: 'Title' }),
    }
    expect(withExt(base, mods, 'en-US').sampleDoc).toEqual({ title: 'Title' })
    expect(withExt(base, mods, 'zh-CN').sampleDoc).toEqual({ title: '标题' })
  })

  it('不修改传入的 base(含深层)', () => {
    withExt(base, { './ext/zh-CN/error.ts': mod({ auth: { passwordWrong: 'x' } }) }, 'zh-CN')
    expect(base.error.auth).toEqual({
      passwordWrong: '账号或密码错误',
      captchaExpired: '验证码已过期',
    })
  })

  // 数组不是"普通对象",不能往下钻:isPlainObject 一旦漏掉 !Array.isArray(v),
  // ['a','b','c'] 与 ['x'] 会按下标逐项合并成 ['x','b','c'] —— 而上面 8 条一条都不红(实测)。
  // 用行内 base,不碰上面共享的那个。
  it('数组不当作对象往下钻(否则会按下标逐项合并出四不像)', () => {
    const out = withExt(
      { m: { tags: ['a', 'b', 'c'] } },
      { './ext/zh-CN/m.ts': mod({ tags: ['x'] }) },
      'zh-CN',
    )
    expect(out.m).toEqual({ tags: ['x'] })
  })

  it('接受 ./locales/ext/<locale>/<模块>.ts 形式的键(前缀不同也并入)', () => {
    const out = withExt(
      base,
      { './locales/ext/zh-CN/sampleDoc.ts': mod({ title: '标题' }) },
      'zh-CN',
    )
    expect(out.sampleDoc).toEqual({ title: '标题' })
  })

  it('不认的键被忽略(缺命名空间层或非 .ts)', () => {
    const out = withExt(
      base,
      {
        './ext/README.md': mod({ title: '不应出现' }),
        './ext/zh-CN.ts': mod({ title: '不应出现' }),
      },
      'zh-CN',
    )
    expect(out).toEqual(base)
  })
})

describe('registerLocales', () => {
  it('并入后 i18n.global.t 能取到新键', () => {
    registerLocales({ './ext/zh-CN/__specA.ts': mod({ hello: '你好' }) })
    expect(i18n.global.t('__specA.hello')).toBe('你好')
  })

  it('同一命名空间调两次,后一次覆盖前一次同键、保留前一次独有键', () => {
    registerLocales({ './ext/zh-CN/__specB.ts': mod({ a: '甲', b: '乙' }) })
    registerLocales({ './ext/zh-CN/__specB.ts': mod({ b: '乙二', c: '丙' }) })
    expect(i18n.global.t('__specB.a')).toBe('甲')
    expect(i18n.global.t('__specB.b')).toBe('乙二')
    expect(i18n.global.t('__specB.c')).toBe('丙')
  })

  it('内置键(如 common.search)不因并入而丢失', () => {
    registerLocales({ './ext/zh-CN/__specC.ts': mod({ x: '1' }) })
    expect(i18n.global.t('common.search')).toBe('查询')
  })
})
