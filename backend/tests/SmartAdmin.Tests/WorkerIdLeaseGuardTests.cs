using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using SmartAdmin.Core;
using SmartAdmin.Services;
using SmartAdmin.SqlSugar;

namespace SmartAdmin.Tests;

/// <summary>
/// WorkerId 数据库租约守卫——同一 WorkerId 不能被两个<b>活着的</b>实例同时持有,
/// 但被硬杀的上一世留下的残留租约必须能被同机同号的新进程立刻接管(否则开发期每次停止调试都要干等一个 TTL)。
/// <para>身份(机器名 / pid / pid 是否活着)从构造参数注入,测试才有办法模拟"另一台机器"和"已经死掉的前任"。</para>
/// </summary>
public class WorkerIdLeaseGuardTests
{
    private static (ServiceProvider Sp, string DbFile) BuildProvider()
    {
        var id = $"wlg-{Guid.NewGuid():N}";
        var dbFile = Path.Combine(Path.GetTempPath(), $"smart-{id}.db");
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new AdminIdOptions { WorkerId = 0 });
        services.AddSingleton(new AdminJobsOptions());
        services.AddSmartAdminSqlSugar(new AdminDatabaseOptions
        {
            DbType = TestDb.DbType,
            ConnectionString = TestDb.ConnectionString(id, dbFile),
        });
        return (services.BuildServiceProvider(), dbFile);
    }

    /// <param name="alive">"库里那行租约的 pid 还活着吗"的回答。</param>
    private static WorkerIdLeaseGuard NewGuard(IServiceProvider sp, ISqlSugarClient db, string machine, int pid, bool alive = true)
        => new(db,
            new AdminIdOptions { WorkerId = 0 },
            new AdminJobsOptions(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<WorkerIdLeaseGuard>(),
            time: null,
            identity: new WorkerLeaseIdentity(machine, pid, _ => alive));

    [Fact]
    public async Task Second_instance_on_another_machine_throws()
    {
        var (sp, dbFile) = BuildProvider();
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var guard1 = NewGuard(sp, db, "host-a", 100);
            await guard1.StartAsync(CancellationToken.None);

            // 另一台机器配了同一个机器号,连的还是同一个库——真冲突,拦
            var guard2 = NewGuard(sp, db, "host-b", 200);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => guard2.StartAsync(CancellationToken.None));
            Assert.Contains("WorkerId", ex.Message);
            Assert.Contains("host-a#0", ex.Message);

            await guard1.StopAsync(CancellationToken.None);
            guard1.Dispose();
            guard2.Dispose();
        }

        TestDb.Cleanup(dbFile, dbFile);
    }

    [Fact]
    public async Task Same_machine_live_process_throws()
    {
        var (sp, dbFile) = BuildProvider();
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var guard1 = NewGuard(sp, db, "host-a", 100);
            await guard1.StartAsync(CancellationToken.None);

            // 同机另一个进程,而且它还活着——同样会撞号,拦
            var guard2 = NewGuard(sp, db, "host-a", 200, alive: true);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => guard2.StartAsync(CancellationToken.None));
            Assert.Contains("仍在运行", ex.Message);

            await guard1.StopAsync(CancellationToken.None);
            guard1.Dispose();
            guard2.Dispose();
        }

        TestDb.Cleanup(dbFile, dbFile);
    }

    [Fact]
    public async Task Same_machine_dead_process_lease_is_taken_over()
    {
        var (sp, dbFile) = BuildProvider();
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            // guard1 拿到租约后被硬杀:租约行留在库里,没到期,StopAsync 没跑过
            var guard1 = NewGuard(sp, db, "host-a", 100);
            await guard1.StartAsync(CancellationToken.None);
            guard1.Dispose();

            // 同机同号重启,前任 pid 已经不在——直接接管,不等 TTL
            var guard2 = NewGuard(sp, db, "host-a", 200, alive: false);
            await guard2.StartAsync(CancellationToken.None);

            var row = await db.Queryable<SysWorkerLease>().Where(l => l.WorkerId == 0).FirstAsync();
            Assert.Equal(200, row.Pid);
            Assert.StartsWith("host-a#0@", row.NodeName);

            await guard2.StopAsync(CancellationToken.None);
            guard2.Dispose();
        }

        TestDb.Cleanup(dbFile, dbFile);
    }

    [Fact]
    public async Task Same_machine_recycled_pid_is_taken_over()
    {
        var (sp, dbFile) = BuildProvider();
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            // 容器重启:主机名不变,新进程的 pid 又是 1——残留行的 pid 查起来"活着",因为那就是我自己
            var guard1 = NewGuard(sp, db, "pod-0", 1);
            await guard1.StartAsync(CancellationToken.None);
            guard1.Dispose();

            var guard2 = NewGuard(sp, db, "pod-0", 1, alive: true);
            await guard2.StartAsync(CancellationToken.None);

            var row = await db.Queryable<SysWorkerLease>().Where(l => l.WorkerId == 0).FirstAsync();
            Assert.Equal(1, row.Pid);

            await guard2.StopAsync(CancellationToken.None);
            guard2.Dispose();
        }

        TestDb.Cleanup(dbFile, dbFile);
    }

    [Fact]
    public async Task Stop_releases_lease_allowing_new_instance()
    {
        var (sp, dbFile) = BuildProvider();
        await using (sp)
        {
            var db = sp.GetRequiredService<ISqlSugarClient>();
            var guard1 = NewGuard(sp, db, "host-a", 100);

            await guard1.StartAsync(CancellationToken.None);
            await guard1.StopAsync(CancellationToken.None);
            guard1.Dispose();

            // 正常停机把租约行删了,另一台机器也能立刻接手
            var guard2 = NewGuard(sp, db, "host-b", 200);
            await guard2.StartAsync(CancellationToken.None);
            await guard2.StopAsync(CancellationToken.None);
            guard2.Dispose();
        }

        TestDb.Cleanup(dbFile, dbFile);
    }
}
