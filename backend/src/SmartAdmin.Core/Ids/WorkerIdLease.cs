using System.Text;

namespace SmartAdmin.Core;

/// <summary>
/// 雪花机器号的文件锁租约:启动时依次独占打开 <c>worker-00.lock … worker-63.lock</c>,
/// 第一个锁得住的文件名就是本进程的机器号,句柄一直持有到进程退出。
/// <para>为什么要抢锁:同一台机器上的多个进程(IIS 应用池重叠回收、Web 园、同机多份部署)读同一份配置,
/// 拿到同一个机器号,同一毫秒各发一个号就撞主键。抢锁后旧进程占着 0,新进程自动落到 1,同号不可能发生。</para>
/// <para>文件锁只在同一台机器内互斥。跨机器/跨容器仍要显式配 <c>SmartAdmin:Id:WorkerId</c>,每实例不同。</para>
/// <para>释放只关句柄、不删文件:删了之后 Linux 上另一进程可能在同一路径新建文件,两个进程各锁各的却都以为占着同一个号。</para>
/// </summary>
public sealed class WorkerIdLease : IDisposable
{
    /// <summary>抢到的机器号(0–63)。</summary>
    public long WorkerId { get; }

    /// <summary>持有的锁文件路径,排查"哪个进程占了哪个号"时看它。</summary>
    public string LockFile { get; }

    private FileStream? _handle;

    private WorkerIdLease(long workerId, string lockFile, FileStream handle)
    {
        WorkerId = workerId;
        LockFile = lockFile;
        _handle = handle;
    }

    /// <summary>
    /// 默认锁目录:Windows 为 <c>%ProgramData%\SmartAdmin\workerid</c>,其它系统为 <c>/tmp/smartadmin/workerid</c>。
    /// 必须是机器级路径:跟着程序目录走,同机两份部署会各自抢到 0 号,等于没锁。
    /// </summary>
    public static string DefaultLockDir => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SmartAdmin", "workerid")
        : Path.Combine(Path.GetTempPath(), "smartadmin", "workerid");

    /// <summary>抢一个空闲槽位。目录不可用或 64 个槽位全被占满时抛异常,拒绝启动,不发可能重复的号。</summary>
    /// <param name="lockDir">锁目录,空则用 <see cref="DefaultLockDir"/>。</param>
    public static WorkerIdLease Acquire(string? lockDir = null)
    {
        var dir = string.IsNullOrWhiteSpace(lockDir) ? DefaultLockDir : Path.GetFullPath(lockDir);
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"机器号锁目录 {dir} 不可用,无法保证雪花 Id 不重复,拒绝启动。" +
                "请给运行账户该目录的写权限,或用 SmartAdmin:Id:WorkerIdLockDir 指定一个可写的机器级目录。", ex);
        }

        for (long id = 0; id <= SnowflakeIdGenerator.MaxWorkerId; id++)
        {
            var file = Path.Combine(dir, $"worker-{id:D2}.lock");
            FileStream handle;
            try
            {
                // FileShare.None:Windows 走共享冲突,Unix 走 flock(LOCK_EX),两边都按打开句柄互斥
                handle = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            }
            catch (IOException) { continue; }                // 被别的进程锁着
            catch (UnauthorizedAccessException) { continue; } // 文件属于别的账户,或处于待删除态

            try
            {
                handle.SetLength(0);
                handle.Write(Encoding.UTF8.GetBytes(
                    $"pid={Environment.ProcessId} machine={Environment.MachineName} since={DateTimeOffset.Now:O}"));
                handle.Flush();
            }
            catch (IOException) { /* 内容只供排查,写不进不影响互斥 */ }

            return new WorkerIdLease(id, file, handle);
        }

        throw new InvalidOperationException(
            $"机器号 0–{SnowflakeIdGenerator.MaxWorkerId} 已被占满(目录 {dir}),拒绝发号以避免重复。请检查本机是否有进程未正常退出。");
    }

    /// <summary>释放槽位:只关句柄,不删文件。</summary>
    public void Dispose() => Interlocked.Exchange(ref _handle, null)?.Dispose();
}
