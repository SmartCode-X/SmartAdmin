# locales/ext —— i18n 扩展位

## 用法

按 `ext/<locale>/<模块>.ts` 放文件,默认导出该模块的键。文件名即顶层命名空间：

```ts
// src/locales/ext/zh-CN/sample.ts
export default {
  title: '标题',
  addTitle: '新增文档',
}
```

```ts
// src/locales/ext/en-US/sample.ts
export default {
  title: 'Title',
  addTitle: 'Add Document',
}
```

页面里照常 `t('sample.title')`。`main.ts` 里的

```ts
createSmartAdmin({
  locales: import.meta.glob('./locales/ext/*/*.ts', { eager: true }),
})
```

会把这里的文件按 `<locale>/<模块>.ts` 解析出 locale 与命名空间,自动并入内核内置文案,不需要额外注册。

## 深合并

并入是**深合并**,所以你可以往内置命名空间的任意深度补键,而不会把兄弟键顶掉：

```ts
// src/locales/ext/zh-CN/error.ts
// 键要和后端 [MsgKey("error.doc.titleDuplicated")] 逐字对上,去掉 `error.` 前缀。
// translateError 按信封里的 msgKey 取字;按数字码查文案的兜底是内核内部的一张小表,只收内核自己的几个码,
// 应用的错误码走不到。写成 { 60001: '...' } 是死文案,永远没人读,必须走 [MsgKey] + 同名 i18n 键这条路;
// 带 [MsgKey] 的错误码枚举放在 ApplicationAssemblies 登记过的程序集里,后端才解析得到它的 msgKey。
export default {
  doc: { titleDuplicated: '文档标题重复' },
}
```

覆写内置文案同理,且只动写到的那一个键：

```ts
// src/locales/ext/zh-CN/error.ts —— captchaExpired 等 auth.* 兄弟键不受影响
export default {
  auth: { passwordWrong: '账号或密码不正确' },
}
```
