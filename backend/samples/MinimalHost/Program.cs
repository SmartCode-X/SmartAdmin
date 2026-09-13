using SmartAdmin.AspNetCore;
using SmartAdmin.Auth.DingTalk;
using SmartAdmin.Auth.GitHub;
using SmartAdmin.Auth.WeChat;
using SmartAdmin.Auth.WeCom;
using SmartAdmin.Caching.Redis;
using SmartAdmin.Excel;

// 验收基准:去掉下面那些可选包,只剩 AddSmartAdmin / MapSmartAdmin,零配置即跑。
var builder = WebApplication.CreateBuilder(args);

// 可选包 SmartAdmin.Caching.Redis。必须在 AddSmartAdmin **之前**调用才赢 TryAdd。
// 不配 SmartAdmin:Cache:Provider=Redis 时它是**空操作** —— 零配置体验不变,仍走进程内缓存。
// 多副本下它不是可选优化而是**前置条件**:进程内缓存意味着副本 A 的强退/权限失效传不到副本 B,
// 而会话缓存的 TTL 是刷新令牌寿命(天级)——强制下线会在半数请求上失效好几天。
builder.Services.AddSmartAdminRedisCache(builder.Configuration);

// 可选包:外部登录 / SSO。同 Redis,必须在 AddSmartAdmin **之前**调用;不配对应配置节时是**空操作**(不点亮按钮)。
// 内置 OIDC provider 由 AddSmartAdmin 按 SmartAdmin:ExternalAuth:Oidc 自动装配,无需在此显式调用。
builder.Services.AddSmartAdminWeComAuth(builder.Configuration);
builder.Services.AddSmartAdminDingTalkAuth(builder.Configuration);
builder.Services.AddSmartAdminGitHubAuth(builder.Configuration);
builder.Services.AddSmartAdminWeChatAuth(builder.Configuration);

// 可选包:xlsx 导入导出。必须在 AddSmartAdmin **之前**调用才赢 TryAdd;
// 不调则 codec 走 MissingExcelProvider,任意导入/导出端点抛 46001。
builder.Services.AddSmartAdminExcel();

builder.Services.AddSmartAdmin(builder.Configuration);
var app = builder.Build();
app.MapSmartAdmin();
app.Run();
