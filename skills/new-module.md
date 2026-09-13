# 新增业务模块全流程 (New Module)

端到端串起一个完整模块:实体 → 后端 CRUD → 测试 → API 契约 → 前端页面 → i18n → 菜单/权限 → 验证。
每一步的细节在对应的专项 skill 里,本文只管**顺序、模式分叉和步骤之间的交接点**。

> 前端内核是 npm 包 `smart-admin-web`(Vue 3 + Naive UI):系统模块改它在本仓的源码 `web/packages/admin/src/`;业务模块写在消费方应用(从 `web/template` degit 出来的薄壳)自己的 `src/` 里,应用只装包、不含内核源码。

## 第零步:确定模式(决定后面每一步怎么走)

| | 系统模块(内核维护者) | 业务模块(消费者二开) |
|---|---|---|
| 代码位置 | `backend/src/SmartAdmin.*` 分层 | 你自己的 Assembly(`dotnet new smart-app` 的 `Modules/`) |
| 表名 / 路由 | `sys_*` / `api/v1/sys/*` | `biz_*` / `api/v1/biz/*`(或自定前缀) |
| DI 注册 | `ServicesSetup.cs` 里 `TryAddScoped`(可替换性契约) | 自己 `Program.cs` 里普通 `AddScoped` |
| ErrorCode | `ErrorCode.cs` 42xxx 段追加(看枚举头部分段表取下一个号) | 自建枚举,从 60000 起步 |
| 菜单/权限 | `DefaultMenuSeed.cs` 追加种子,Id 按编号规则取:百位分区、十位页面、个位 1 查询 2 新增 3 更新 4 删除(见 `create-crud-backend.md`) | 后台「菜单管理」UI 添加;要预置则自注册 `ISeedData<T>`,Id ≥ `SmartSeedIds.ConsumerMin`(1000),编号照同一套规则 |
| 程序集挂载 | 内置 | `options.ApplicationAssemblies.Add(typeof(Program).Assembly)`(缺这行:表不建、Controller 404) |
| 前端类型 / API | 追加进 `web/packages/admin/src/types/api.ts` / `api/index.ts` | **新建** `src/types/<模块>.ts` / `src/api/<域>.ts`(`unwrap`/`pageParams`/`toPage` 从 `'smart-admin-web'` 导入) |
| 前端 i18n | 追加进 `web/packages/admin/src/locales/zh-CN.ts` + `en-US.ts` | **新建** `src/locales/ext/zh-CN/<模块>.ts` + `ext/en-US/<模块>.ts`(`createSmartAdmin({ locales })` 深合并,无需注册) |
| 前端页面 | `web/packages/admin/src/views/<模块>/`(包内自动登记) | `src/views/<模块>/`(`createSmartAdmin({ views })` 登记;与内置页同 key 即覆盖内置页) |

> 前端这三行和后端同一个道理:内核以包交付,业务代码不进内核。应用里没有内核源码,扩展只走 `createSmartAdmin` 的选项(`views` / `locales` / `routes` / `menuTitles` / `icons` / `iconSets` / `install` / `plugins`)和包的公开导出(`web/packages/admin/src/index.ts`);内核组件、composable、store、工具一律 `import { … } from 'smart-admin-web'`。

## 步骤

1. **建实体** → `create-entity.md`(BaseEntity 还是 DataEntity 的选型判据在那里;机构数据隔离选 DataEntity)。
2. **后端 CRUD 六件产出** → `create-crud-backend.md`(Models / Interface / Service / ErrorCode / DI / Controller,菜单种子取号规则也在那)。
3. **后端测试**:xUnit + `AdminAppFactory`(见 `backend/tests/SmartAdmin.Tests/` 现成写法);跑 `dotnet test backend/SmartAdmin.slnx`。
4. **刷新 API 契约**:先把后端跑起来(`dotnet run --project backend/samples/MinimalHost` 或你的 host),再生成类型:系统模块 `cd web && npm run gen:api`(写 `web/packages/admin/src/api/schema.d.ts`);业务模块在应用根目录 `npm run gen:api`(写 `src/api/schema.d.ts`,首次生成后 `src/api/client.ts` 改用生成的 `paths`,见 `create-crud-frontend.md`「前置步骤」)。**绝不手改 `schema.d.ts`**。
5. **前端页面** → `create-crud-frontend.md`,平铺 CRUD;树表/主从分栏/侧栏筛选 → `create-page-variant.md`;组件契约总索引 → `web/COMPONENTS.md`,设计规范 → `web/DESIGN.md`。
6. **i18n**:双语两处都要加(模块 key + `error.*` key),文件按第零步的模式选——系统模块进 `web/packages/admin/src/locales/zh-CN.ts`/`en-US.ts`,业务模块新建 `src/locales/ext/<locale>/<模块>.ts`(错误码键进 `src/locales/ext/<locale>/error.ts`)。键结构见 `create-crud-frontend.md` 的「i18n」节。**`error.*` 的键必须和后端 `[MsgKey]` 字符串逐字对上(嵌套形态,去掉 `error.` 前缀)**——`translateError` 优先按 `msgKey` 取字,数字 `code` 只在内置 `CODE_MSG_KEY`(`web/packages/admin/src/utils/error.ts`)命中时才兜底,新模块的错误码走不到那张表,漏写 `msgKey` 就是没人读的死文案。
7. **菜单/权限接线**:
   - 系统模块:`DefaultMenuSeed` 加页面节点 + 权限按钮。
   - 消费者:菜单管理 UI 建节点,`component` 填应用 `src/views/` 下的相对路径、去掉 `.vue`(如 `biz/product/index`),动态路由自动注册,**不写任何路由代码**。
   - 接口没有页面(只给移动端 / 第三方调):建一个目录当权限组(不填 path/component),按钮直接挂目录下;别建假页面。写法见 `create-crud-backend.md` 菜单种子一节。
8. **可选加挂**:这个模块要定时跑点什么(对账、清理、推送)→ `create-job.md`;要 xlsx 导入导出 → `wire-import-export.md`。两者都不改动上面任何一步的产出,是纯加法。
9. **验证**(顺序跑,两个重进程不要并发):
   - `dotnet build backend/SmartAdmin.slnx -c Release` → `dotnet test backend/SmartAdmin.slnx`
   - 前端:系统模块 `cd web && npm run typecheck && npm run lint && npm run format:check && npm test`;业务模块在应用根目录 `npm run typecheck`(模板没有配 lint 脚本)
   - `npm run dev` 手工走查:列表/搜索/新增/编辑/删除/StatusSwitch 不回弹/无权限按钮被隐藏(工具栏 `v-auth`、行内 `authStore.hasPerm`)/错误提示走 i18n。

## 交接点清单(步骤之间最容易断的地方)

- **一个权限码,四处一致**:Controller 路由模板 = 菜单按钮 `Permission` = 前端 `v-auth` 值,格式统一 `METHOD:/api/v1/...`(路径参数保留 `{id}` 占位)。错一个字符 = 静默 403。
- **ErrorCode 的 `[MsgKey]` = 前端 `locales` 的 `error.*` 键**,zh/en 两个语言包都要有,漏了显示兜底文案。
- **`gen:api` 依赖后端在跑**;新端点没出现在 `/openapi/v1.json` 里就去查 Controller 是否注册(消费者:`ApplicationAssemblies` 挂了没)。
- **种子 Id 有保留区间**:内核 `[1, 999]`、消费者从 `SmartSeedIds.ConsumerMin`(1000)起,上限是启动时动态算出的雪花地板(远大于 4095,可用语义化大号段);越界/撞号启动即拒(`DatabaseInitializer` 强制)。
- 编码规范总纲(命名/注释/事务/缓存失效等)→ `docs/coding-standards.md`。
