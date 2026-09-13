# IconPicker

图标选择器。封装 `smart-naive-icon` 的 `SmartIconPicker`，只做两层收口：注入本仓的 i18n `labels`，并使用 `ph` 图标集作为选择器自身 UI。

```vue
<IconPicker v-model="form.icon" clearable />
```

图标集和本地 SVG 由 `createSmartAdmin` 启动时调用的 `setupIcons()` 全局注册(应用的本地 SVG 经 `createSmartAdmin({ icons })` 传入)，本组件不接收 `collections` prop。

## Props

| Prop          | 类型      | 默认   | 说明                                        |
| ------------- | --------- | ------ | ------------------------------------------- |
| `placeholder` | `string`  | `''`   | 空值时退回 i18n 的 `iconPicker.placeholder` |
| `clearable`   | `boolean` | `true` | 是否显示清空按钮                            |

`v-model` 是一个 `prefix:name` 字符串（本地 SVG 为 `local:name`），默认值 `''`。
