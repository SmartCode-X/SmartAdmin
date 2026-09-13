# SQL 控制台日志

一个请求到底打了 2 条 SQL 还是 143 条，逐条看是看不出来的，因为每条都很快。开了 `SmartAdmin:Database:SqlLog`，每条语句打一个块，请求结束再补一行汇总，差别就落在那行汇总上。默认关着，它是开发期的工具。

慢 SQL 阈值 `Database:SlowSqlMillis` 和失败 SQL 那两条诊断日志是另一回事，它们默认就在、生产也该开着，配置说明在[部署指南](/zh/guide/deployment/)。

## 开关

```json
{
  "SmartAdmin": {
    "Database": {
      "SqlLog": {
        "Enabled": true,
        "MinMillis": 0,
        "MaxSqlChars": 4000,
        "RequestSummary": true,
        "Color": null
      }
    }
  }
}
```

| 键 | 默认 | 作用 |
| --- | --- | --- |
| `Enabled` | `false` | 总开关 |
| `MinMillis` | `0` | 只打耗时不低于本值（毫秒）的语句；`0` 全打 |
| `MaxSqlChars` | `4000` | 单条语句的打印上限，超出截断并在末尾注明。批量插入的语句能有几十万字符 |
| `RequestSummary` | `true` | 请求结束是否补一行汇总 |
| `Color` | 自动 | ANSI 配色。不配则 stdout 是终端、且未设 `NO_COLOR` 时才上色 |

同时开着文件日志的话，`Color` 请显式配 `false`，否则文件里会混进一堆转义序列。

`MinMillis` 只挡语句块，不挡统计：被它滤掉的语句照样计入汇总的条数与合计耗时。N+1 的每一条都不慢，要是按耗时过滤之后连条数也跟着少了，这个开关就白开了。

## 一条语句打一个块

```text
info: SmartAdmin.Sql[0]
      ┌ SQL #1 查询 [SmartAdmin] 3.4 ms
      │ SELECT COUNT(1) FROM "sys_user" WHERE ( "IsDelete" = 0 ) AND ( "OrgId" IN (100,101) )
      └ 参数 2 个(已内联)
info: SmartAdmin.Sql[0]
      ┌ SQL #2 查询 [SmartAdmin] 11.8 ms
      │ SELECT "Id","Account","RealName","OrgId" FROM "sys_user" WHERE ( "IsDelete" = 0 ) AND ( "OrgId" IN (100,101) ) ORDER BY "Id" DESC LIMIT 10 OFFSET 0
      └ 参数 2 个(已内联)
```

块头依次是序号、语句类型、`ConfigId`、耗时。序号和汇总行里「最慢第几条」对得上。类型按首个关键字判，带 CTE 的查询以 `WITH` 开头，也算查询。`ConfigId` 主库固定是 `SmartAdmin`，副库是你在 `AdditionalDatabases` 里给的那个名字，所以一眼看得出这条打的是哪个库。

颜色落在两处。类型按种类上色：查询绿、新增黄、修改蓝、删除红、建表紫、其它灰。耗时按慢 SQL 阈值分三档：越线红，到一半黄，其余绿。

参数不另起一行列出来，直接内联进语句。排查时要的是一条能贴进客户端就跑的 SQL，而不是「语句在这里、参数在那里」再人肉拼一遍。布尔统一渲染成 `1` 或 `0`，SQLite、MySQL、SqlServer 直接能跑，PostgreSQL 上要自己改一下。

## 汇总行才是查 N+1 的入口

```text
info: SmartAdmin.Sql[0]
      └ SQL 汇总 GET /api/v1/sys/user/page → 2 条 / 15.2 ms / 最慢 #2 11.8 ms
```

条数、合计耗时、最慢的是第几条，开头那句话就靠这一行兑现。条数到 20 还会在行尾直接点名：

```text
info: SmartAdmin.Sql[0]
      └ SQL 汇总 GET /api/v1/sys/order/page → 143 条 / 812.6 ms / 最慢 #7 24.1 ms  ← 143 条,疑似 N+1
```

汇总只有 HTTP 请求有。一条语句都没跑的请求（静态资源、404）不占这一行；定时任务触发走的是同一条逐语句路径，语句块照打，但没有汇总行。

## 敏感参数只打 `'***'`

参数名命中 `password`、`pwd`、`secret`、`token`、`credential`、`header`、`authorization`、`apikey`、`api_key`、`cookie` 的，值一律渲染成 `'***'`。是子串匹配，所以 `newPassword`、`access_token` 都算。

```text
      ┌ SQL #4 修改 [SmartAdmin] 2.1 ms
      │ UPDATE "sys_user" SET "Password"='***' , "UpdateTime"='2026-09-06 10:21:33.417' WHERE "Id"=1
      └ 参数 3 个(已内联)
```

内核没有用 SqlSugar 自带的 `UtilMethods.GetSqlString` 来拼这一行，就是因为它会把口令哈希、TOTP 种子这类值原样吐出来。这份日志是要被截图贴进工单的。

名单（`SensitiveKeys`）与操作日志的入参脱敏共用一份，失败 SQL、慢 SQL 那两条诊断日志也照它打码。两条出口各拿一份名单，补了这边漏了那边，敏感值照样从另一条路落盘。

::: warning 打码只认名字
判据是参数名像不像口令，不看值里装的是什么。消费方业务表里那些名字不带这些词的敏感列（身份证号、银行卡号、体检结论），内联进语句之后就是原文。
:::

## 通道是 `ILogger("SmartAdmin.Sql")`

语句块和汇总行都从这个日志类别出去，不是直接写控制台。于是：

- 开了文件日志（`Logging:File`）同样收得到，不必守着终端看。
- 级别能单独调。`Logging:LogLevel:SmartAdmin.Sql` 设成 `Warning`，普通语句块（`Information`）就安静了，越过慢 SQL 阈值的那些（`Warning`）还在。
- 结构化 sink 收到的是模板加参数，序号、类型、`ConfigId`、耗时各是一个字段。

## 生产别开

日志量是第一层：每请求若干条语句，每条至少三行，还都带着完整语句。第二层更值钱，上面那条打码只认参数名，命不中名单的值会原样落进日志文件、日志采集，以及任何有权限看这些的人手里。

`MinMillis` 调大不算关掉它，那只是少打几条语句块，统计与汇总照跑。要关就把 `Enabled` 置回 `false`。

生产上想留一条性能线索，配 `Database:SlowSqlMillis` 就够：只有越线的语句才打，量级差着好几个数量级。
