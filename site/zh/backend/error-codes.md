# 错误码

信封里的 `code` 是个数字，文案在前端按 `msgKey` 查。给自己的模块加一种错误，只要挑一个不撞车的号、标上语义键、让内核扫到它，此外不必碰内核的任何一段代码。

码在前端怎么变成一句中文或英文提示，归[响应契约与错误码](/zh/frontend/api-contract)那页。

## 分段

以 `Core/ErrorCode.cs` 为准，新增内核码按段落位，不跨段挪用。

| 段 | 用途 |
| --- | --- |
| `0` | 成功 |
| `40000`–`40999` | 认证与登录 |
| `41000`–`41999` | 权限与数据范围 |
| `42000`–`42999` | 用户、组织、角色、菜单 |
| `43000`–`43999` | 字典、配置 |
| `44000`–`44999` | 文件上传 |
| `45000`–`45999` | 消息通知 |
| `46000`–`46999` | 导入、导出 |
| `47000`–`47999` | 定时任务 |
| `48000`–`48999` | 请求参数 |
| `50000`–`50999` | 系统内部错误 |

消费方的码另占一段，惯例从 `60000` 起，避开内核已占的 `40000`–`48999` 与 `50000`–`50999`。

## 登记自己的错误码

枚举照内核的写法：另占码段，每个成员标一个 `[MsgKey]`。

```csharp
public enum WoErrorCode
{
    [MsgKey("error.wo.notFound")]
    WoNotFound = 60001,

    [MsgKey("error.wo.statusConflict")]
    WoStatusConflict = 60002,
}
```

抛的时候强转成内核的 `ErrorCode`。`AdminException` 只收内核那个枚举类型，这条强转就是接口：

```csharp
AdminException.ThrowIf(wo is null, (ErrorCode)WoErrorCode.WoNotFound);
```

登记有两个入口：

- **`ApplicationAssemblies` 自动扫描**，日常走这条。业务程序集本来就要登记给内核（它的实体要参与建表、控制器要挂载），装配时会顺带把里面每个带 `[MsgKey]` 成员的枚举扫进码表，你一行都不用写。
- **`options.ErrorCodeEnums` 显式登记**。错误码定义在别的程序集、而那个程序集又没被登记为业务程序集时，才需要这一条。

```csharp
builder.Services.AddSmartAdmin(builder.Configuration, o =>
{
    o.ApplicationAssemblies.Add(typeof(WoService).Assembly);   // WoErrorCode 顺带被扫到
    o.ErrorCodeEnums.Add(typeof(SharedErrorCode));             // 不在业务程序集里的才要显式加
});
```

登记之后，`(ErrorCode)WoErrorCode.WoNotFound` 抛出的异常，信封里的 `msgKey` 就是 `error.wo.notFound`。不登记则回退成 `error.code.60001`，前端语言包里没有这个键，用户界面上弹出来的就是这串原始文字。登记这一步省下的，正是消费方为了拿到正确 `msgKey` 而去整段复制内核异常过滤器、另建一张码表的功夫。

两个枚举抢同一个数值，装配时直接抛，消息里点名冲突的两边。前端按 `code` 查文案，一个数字对两个键必然错一个，这种冲突不能等到运行期才发现。

## `GET /api/v1/meta/error-codes`

匿名端点，返回码表全集，含消费方登记进来的码。

```json
{
  "code": 0,
  "msgKey": "common.success",
  "data": [
    { "code": 0, "name": "Success", "msgKey": "common.success", "source": "ErrorCode" },
    { "code": 40001, "name": "PasswordWrong", "msgKey": "error.auth.passwordWrong", "source": "ErrorCode" },
    { "code": 60001, "name": "WoNotFound", "msgKey": "error.wo.notFound", "source": "WoErrorCode" }
  ]
}
```

`source` 是这个码来自哪个枚举，内核的写 `ErrorCode`，你自己的写你那个类型名。

它匿名是因为它就是一张公开码表，不含任何业务数据，而需要它的场合（登录页要翻译一条错误、外部集成方对接、按契约调用的 agent）多半还没有令牌。整个模块可经 `Api:DisabledModules=["Meta"]` 关掉。

没有配套的 `meta/permissions`。权限码清单在 `menu/routes` 已经有了，而那一条是挂权限的；再开一个匿名别名，等于把「这个系统有哪些端点」白送给侦察。

## 契约里的 `ErrorCode`

`/openapi/v1.json` 的 `components.schemas.ErrorCode` 带着同一张码表：

```json
{
  "type": "integer",
  "format": "int32",
  "description": "业务错误码。0 = 成功;码表见 x-enum-values / x-enum-varnames / x-msg-keys,或调 GET /api/v1/meta/error-codes(消费者登记的码也在里面)。",
  "x-enum-values": [0, 40001, 60001],
  "x-enum-varnames": ["Success", "PasswordWrong", "WoNotFound"],
  "x-msg-keys": ["common.success", "error.auth.passwordWrong", "error.wo.notFound"]
}
```

三个数组按下标一一对应。`x-enum-varnames` 是 openapi-generator 一系的既有约定，用来生成带名字的枚举；`x-msg-keys` 是本仓自己加的，把码直接对到前端的 i18n 键上，省掉一张手工维护的对照表。

`type` 是开放的 `integer`，**刻意不写 `enum`**。写了的话，生成器会把它渲染成字面量联合，调用方传一个普通 number 就编译不过；更要命的是码空间对消费方本来就是开放的，闭合的 `enum` 等于在契约里宣告 `60000` 段那些码非法，可它们每天都在真实地飞。
