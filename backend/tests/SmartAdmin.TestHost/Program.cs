using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartAdmin.AspNetCore;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;
using SmartAdmin.TestHost;

// 集成测试宿主 = 一个"用户 App":除内置能力外,还带自己的业务模块(实体 + 种子 + 控制器)。
// 把本程序集登记为 ApplicationAssemblies——验证用户实体能 CodeFirst 建表、控制器能注册路由。
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSmartAdmin(builder.Configuration, o => o.ApplicationAssemblies.Add(typeof(Program).Assembly));
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SampleWidgetSeed>());   // 用户自定义种子
builder.Services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedData, SampleWidgetDetailSeed>());   // 明细表(PrimaryId)种子
builder.Services.TryAddSingleton<ReadyHookRecord>();                                                   // 钩子调用现场(用例断言用)
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IDatabaseReadyHook, SampleReadyHook>());    // 库就绪钩子
builder.Services.AddRecycleBinType<SampleWidget>("widget", w => w.Name);               // 消费者软删表接进回收站
builder.Services.TryAddScoped<ISampleDocService, SampleDocService>();   // 示例机构隔离业务服务(DataEntity 范本)
builder.Services.TryAddScoped<SampleDocExportProfile>();                // 示例导出档案(消费方接导出的范本)
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IAdminJob, SampleJob>());   // 示例定时任务处理器
var app = builder.Build();
app.MapSmartAdmin();
app.Run();

// WebApplicationFactory<Program> 需要可见的入口点类型
public partial class Program { }
