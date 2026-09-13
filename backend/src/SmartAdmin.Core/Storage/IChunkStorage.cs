namespace SmartAdmin.Core;

/// <summary>
/// 分片上传的临时存储扩展点。默认实现落本地磁盘;多副本部署时分片会散在各副本本地盘上,
/// 续传打到别的副本就找不到已收分片——那种场景把本接口换成共享盘/对象存储实现即可,不必 fork。
/// <para>合并临时文件由实现自行清理;<see cref="SweepStaleAsync"/> 由内核的文件回收任务周期调用。</para>
/// </summary>
public interface IChunkStorage
{
    /// <summary>落一个分片(幂等:重传覆盖,天然支持断点续传)。</summary>
    Task SaveChunkAsync(string uploadId, int index, Stream content, CancellationToken cancellationToken = default);

    /// <summary>已收分片下标集合(升序);断点续传时客户端据此跳过已传分片。会话不存在则空集。</summary>
    Task<IReadOnlyCollection<int>> GetReceivedIndexesAsync(string uploadId, CancellationToken cancellationToken = default);

    /// <summary>按序合并分片 <c>0..chunkCount-1</c>,返回可读流与内容 SHA-256;缺片抛 <see cref="ErrorCode.ChunkMissing"/>。</summary>
    Task<MergedChunks> MergeAsync(string uploadId, int chunkCount, CancellationToken cancellationToken = default);

    /// <summary>清理某 uploadId 的全部分片(合并完成或放弃时)。</summary>
    Task DiscardAsync(string uploadId, CancellationToken cancellationToken = default);

    /// <summary>清扫超龄的分片临时物,返回清掉的项数。</summary>
    Task<int> SweepStaleAsync(TimeSpan ttl, DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <summary>合并结果:可读流(调用方负责释放,默认实现随释放删临时文件)+ 内容 SHA-256(hex)+ 字节数。</summary>
public sealed record MergedChunks(Stream Content, string Sha256, long Size);
