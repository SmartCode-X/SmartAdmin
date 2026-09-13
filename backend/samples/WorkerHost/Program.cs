using Microsoft.Extensions.Hosting;
using SmartAdmin.Caching.Redis;
using SmartAdmin.Services;

// 独立调度 Worker——「API 停了任务照跑」的官方配方。
// 消费者默认不需要这个项目:AddSmartAdmin() 的默认形态就把调度器跑在 API 进程内,多副本靠 DB 选主互备。
// 只有想要任务生命周期独立于 API、或把任务负载隔离出 API 进程时才照抄本项目。
var builder = Host.CreateApplicationBuilder(args);
// 必须在 AddSmartAdminWorker 之前:Worker 与 API 共享缓存是多进程部署的前提(强退、权限缓存、限流计数都在这上面),
// 漏了这行内核会静默退回进程内缓存,顺序反了则装配期直接抛。
builder.Services.AddSmartAdminRedisCache(builder.Configuration);
builder.Services.AddSmartAdminWorker(builder.Configuration);
await builder.Build().RunAsync();
