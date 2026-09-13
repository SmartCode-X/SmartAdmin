using SmartAdmin.Core;

namespace SmartAdmin.Tests;

/// <summary>
/// 雪花机器号的文件锁租约。线上"高并发同一时刻 Id 重复"的真正来源不是算法,是同机多个进程
/// (IIS 应用池重叠回收、Web 园、同机多份部署)读同一份配置拿到同一个机器号,各算各的序列。
/// 文件锁让同机进程结构上不可能同号。同进程两次 Acquire 与两个进程在文件系统语义上等价
/// (Windows 共享冲突 / Unix flock 都按打开句柄互斥),故用同进程两把租约模拟两进程。
/// </summary>
public sealed class WorkerIdLeaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"smart-wid-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 某用例把 _dir 建成了文件 */ }
        try { File.Delete(_dir); } catch { /* 目录用例已删干净 */ }
    }

    [Fact]
    public void Two_live_leases_in_one_dir_get_distinct_slots()
    {
        using var first = WorkerIdLease.Acquire(_dir);
        using var second = WorkerIdLease.Acquire(_dir);

        Assert.Equal(0L, first.WorkerId);
        Assert.Equal(1L, second.WorkerId);   // 0 被占着,新进程自动落到 1
        Assert.True(File.Exists(first.LockFile));
    }

    [Fact]
    public void Disposing_frees_the_slot_but_keeps_the_file()
    {
        var first = WorkerIdLease.Acquire(_dir);
        var file = first.LockFile;
        first.Dispose();

        using var second = WorkerIdLease.Acquire(_dir);
        Assert.Equal(0L, second.WorkerId);   // 旧进程退出,0 号立刻可复用
        Assert.Equal(file, second.LockFile);
        Assert.True(File.Exists(file));      // 只关句柄不删文件:删了 Unix 上会出现两个 inode 各锁各的
    }

    [Fact]
    public void Lock_file_records_the_holder_for_diagnosis()
    {
        var lease = WorkerIdLease.Acquire(_dir);
        lease.Dispose();                     // 独占期间别的句柄读不了,关掉再读

        var content = File.ReadAllText(lease.LockFile);
        Assert.Contains($"pid={Environment.ProcessId}", content);
        Assert.Contains($"machine={Environment.MachineName}", content);
    }

    [Fact]
    public void Full_house_refuses_to_issue_instead_of_reusing()
    {
        var all = new List<WorkerIdLease>();
        try
        {
            for (var i = 0; i <= SnowflakeIdGenerator.MaxWorkerId; i++) all.Add(WorkerIdLease.Acquire(_dir));
            Assert.Equal(SnowflakeIdGenerator.MaxWorkerId, all[^1].WorkerId);

            var ex = Assert.Throws<InvalidOperationException>(() => WorkerIdLease.Acquire(_dir));
            Assert.Contains("占满", ex.Message);
        }
        finally
        {
            all.ForEach(l => l.Dispose());
        }
    }

    [Fact]
    public void Unusable_lock_dir_refuses_to_start_and_names_the_config_key()
    {
        File.WriteAllText(_dir, "not a directory");   // 同名文件挡住建目录,跨平台都会抛

        var ex = Assert.Throws<InvalidOperationException>(() => WorkerIdLease.Acquire(_dir));
        Assert.Contains("WorkerIdLockDir", ex.Message);
    }

    [Fact]
    public void Default_lock_dir_is_machine_level_not_program_relative()
    {
        // 跟着程序目录走,同机两份部署会各自抢到 0 号,等于没锁
        var dir = WorkerIdLease.DefaultLockDir;
        Assert.True(Path.IsPathRooted(dir), dir);
        Assert.False(dir.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase), dir);
    }

    [Fact]
    public void Leased_worker_id_lands_in_the_snowflake_machine_bits()
    {
        using var placeholder = WorkerIdLease.Acquire(_dir);   // 占住 0,让被测租约拿到非零号
        using var lease = WorkerIdLease.Acquire(_dir);

        var id = new SnowflakeIdGenerator(lease.WorkerId).NextId();
        Assert.Equal(lease.WorkerId, (id >> 6) & 63);
    }
}
