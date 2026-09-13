namespace SmartAdmin.Core;

/// <summary>
/// 装配完成标记:<c>AddSmartAdmin()</c> / <c>AddSmartAdminWorker()</c> 跑过一次后注册进容器。
/// <para>两个用途:内核据此对重复调用短路(不重复注册托管服务);可选包(Excel / Redis)据此拒绝晚于内核的装配——
/// 内核用 TryAdd 先占了坑,后来者不会生效,与其运行时静默失效,不如装配期就报错。</para>
/// </summary>
public sealed class SmartAdminRegistered;
