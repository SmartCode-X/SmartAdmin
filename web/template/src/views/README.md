# views —— 业务页面

页面放这里,`main.ts` 用 `import.meta.glob('./views/**/*.vue')` 采集。

菜单管理里的「组件路径」= 相对本目录、去掉 `.vue` 后缀的路径,例如 `src/views/system/user/index.vue`
填 `system/user/index`。与内核内置页同名即覆盖内置页,不需要额外配置。

约定:`<模块>/detail.vue` 对应详情路由 `/<模块>/:id/detail`。
