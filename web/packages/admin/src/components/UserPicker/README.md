# UserPicker

授权用户选择器。左侧机构树过滤，右侧用户表格多选，确认后 `emit('confirm', ids)`；用于角色授权等“按机构找人”的场景。

```vue
<UserPicker ref="userPickerRef" @confirm="onUserConfirm" />
```

- `excludeIds`：排除指定用户 Id。
- `confirm`：携带选中用户 Id 数组。
- 打开时调用 `ref.open(ids?)`：不传按空态打开；传一个已选用户 Id 数组则先回显（角色授权页编辑时这样回填已有成员），也可用 `@confirm` 回调统一提交。
