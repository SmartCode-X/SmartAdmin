using Microsoft.AspNetCore.SignalR;
using SmartAdmin.Core;

namespace SmartAdmin.AspNetCore;

/// <summary>Hub 连接分组名——供 <see cref="SmartHub"/>(入组)与 <see cref="SignalRRealtimePublisher"/>(推送)共用,防两处命名漂移。</summary>
internal static class RealtimeGroups
{
    public static string User(long userId) => "user-" + userId;
    public static string User(string userId) => "user-" + userId;
    public static string Session(string sessionId) => "session-" + sessionId;
}

/// <summary>
/// 基于 SignalR 的 <see cref="IRealtimePublisher"/> 实现。仅在 <c>SmartAdmin:Realtime:Enabled</c>
/// 开启时由 <c>SmartAdminSetup</c> 在 <c>AddSmartAdminServices()</c> <b>之前</b>前置注册,压过 Services 层的
/// <c>NoopRealtimePublisher</c>(TryAdd 先到者胜)。
/// <para>经 <see cref="IHubContext{THub}"/> 向 <see cref="SmartHub"/> 的连接组推送。<b>进程内 Hub</b>:推送只达连到同一副本的连接;
/// 多副本要即时跨副本时给 SignalR 叠 Redis backplane(消费者侧,<c>AddStackExchangeRedis</c>)。</para>
/// </summary>
public sealed class SignalRRealtimePublisher(IHubContext<SmartHub> hub) : IRealtimePublisher
{
    /// <inheritdoc />
    public Task NotifyUserAsync(long userId, string @event, object? data = null, CancellationToken cancellationToken = default)
        => hub.Clients.Group(RealtimeGroups.User(userId)).SendAsync(@event, data, cancellationToken);

    /// <inheritdoc />
    public Task NotifyAllAsync(string @event, object? data = null, CancellationToken cancellationToken = default)
        => hub.Clients.All.SendAsync(@event, data, cancellationToken);

    /// <inheritdoc />
    public Task NotifySessionAsync(string sessionId, string @event, object? data = null, CancellationToken cancellationToken = default)
        => hub.Clients.Group(RealtimeGroups.Session(sessionId)).SendAsync(@event, data, cancellationToken);
}
