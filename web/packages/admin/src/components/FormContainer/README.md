# FormContainer — 弹窗/抽屉二合一表单容器

消化 CRUD 页「`saving` ref + 手写 footer + `n-modal` 包装」样板。全站表单弹层可在设置抽屉(通用 → 表单形态)一键切换弹窗/抽屉;`variant` 可按实例覆盖全局偏好。

## Props

| Prop           | 类型                       | 默认                  | 说明                                      |
| -------------- | -------------------------- | --------------------- | ----------------------------------------- |
| `v-model:show` | `boolean`                  | `false`               | 显隐                                      |
| `title`        | `string`                   | 必传                  | 标题                                      |
| `variant`      | `'modal' \| 'drawer'`      | 跟随 `app.formStyle`  | 按实例覆盖全局偏好                        |
| `width`        | `number`                   | `560`                 | 四档取值,见下;实际生效 `min(width, 90vw)` |
| `onConfirm`    | `() => unknown \| Promise` | —                     | 确认回调,见下方协议;不传则确认钮直接关闭  |
| `confirmText`  | `string`                   | `t('common.confirm')` | 确认钮文案                                |
| `cancelText`   | `string`                   | `t('common.cancel')`  | 取消钮文案                                |
| `maskClosable` | `boolean`                  | `false`               | 表单防误触丢输入,默认点遮罩不关           |
| `fullscreen`   | `boolean \| 'auto'`        | `false`               | 铺满视口;`'auto'` = 紧凑屏才全屏,见下     |

## width 四档

`480` 字段很少 / `640` 标准(默认档) / `1000` 双列或字段多 / `1500` 内含明细表格。只用这四档,同类弹窗宽度一致,`:width` 一眼能看出这是哪种弹窗。超过 1728 的数字在 1920 屏上会被 `90vw` 吃掉,写了也不生效。确有超宽明细表要破档,在 `<FormContainer` 标签上一行写一句原因 —— 注释放不进属性列表,Vue 模板会把 `<!-- -->` 当属性名解析。

## fullscreen(平板 / 小屏)

字段多的表单在平板上被压进 560px 的卡片里很难用。`:fullscreen="true"` 让弹窗铺满视口(抽屉则铺满宽度),
`'auto'` 交给 `useCompactScreen()` 判(宽 ≤ 900 或高 ≤ 640,平板竖屏与矮屏笔记本都命中)。

**默认是 `false` 而不是 `'auto'`** —— 没写这个 prop 的既有页面不该因为换了台设备就换一种形态;要跟着屏幕走请显式写出来。

全屏样式(`.smart-form--fullscreen`)写在本组件的**非 scoped** `<style>` 里:弹层 teleport 到 body,
scoped 编译出的 `data-v` 属性到不了那棵子树,写了等于没写 —— 这是「全屏样式不生效」最常见的原因。

## Slots

| Slot           | 说明                                              |
| -------------- | ------------------------------------------------- |
| `default`      | 表单体                                            |
| `header-extra` | 标题右侧附加区(drawer 无原生槽,传了才自拼 header) |
| `footer`       | 整体替换默认「取消/确认」底栏                     |

无 Emits(除 `update:show`)、无 Expose——loading 由 onConfirm 协议驱动,不需要业务碰。

## onConfirm 协议(核心)

- 返回 Promise → 确认钮自动 loading 至 settle;
- **reject 或 resolve(false) → 不关闭**(校验失败/接口失败,用户可改后重试);
- 其余情况 → 自动 `show = false`。
- loading 期间 esc / 遮罩 / 取消钮 / 关闭钮全部锁死,防提交中途关闭。

校验联动**不需要传 formRef**:把 `validate()` 放 onConfirm 第一行,失败 reject 即挡住关闭(内联错误提示由 n-form 负责):

```vue
<FormContainer
  v-model:show="show"
  :title="editingId === null ? t('common.add') : t('common.edit')"
  :width="640"
  :on-confirm="save"
  :confirm-text="t('common.save')"
>
  <n-form ref="formRef" :model="form" :rules="rules" label-placement="left" :label-width="96">…</n-form>
</FormContainer>
```

```ts
async function save() {
  await formRef.value?.validate() // 校验失败 reject → 弹层不关
  try {
    await xxApi.add({ ...form })
    message.success(t('xx.saved'))
    await load()
  } catch (e) {
    message.error(translateError(e))
    return false // API 失败 → 弹层不关
  }
}
```

## 边界与注意

- keep-alive 页签切走时容器 `onDeactivated` 自动收起弹层,业务无需处理。收起是**有意的**:弹层 teleport 到 body,不收就会残留在新页签上方(输入保留但遮罩错位);代价是切页丢弹层内未保存输入。
- 弹窗/抽屉默认都是关闭即销毁(`display-directive: 'if'` 语义),每次打开是干净表单;要保活透传 `display-directive="show"`。

## 可加但先不加

`size` 档位 prop(四档是约定,写死在各调用点的 `:width`)、`onBeforeClose` 拦截钩子、抽屉 `placement`(固定 right)、「无确认钮」模式(纯预览弹窗用裸 `n-modal`)。

范例页:`src/views/system/menu/index.vue`、`src/views/system/module/index.vue`。
