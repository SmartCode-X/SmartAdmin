using System.Runtime.ExceptionServices;
using SqlSugar;

namespace SmartAdmin.SqlSugar;

/// <summary>
/// 事务包装:成功提交,失败回滚并把原异常<b>原样重抛</b>。
/// <para>为什么不直接用 SqlSugar 的 <c>UseTranAsync</c>:它把异常吞进 <c>DbResult.ErrorException</c> 返回,
/// 调用方必须记得判 <c>IsSuccess</c> 再手动抛——漏判就是事务回滚了、业务却当成功继续往下走。
/// 而重抛 <c>result.ErrorException</c> 又会重置堆栈,原始抛出点丢失。这里用
/// <see cref="ExceptionDispatchInfo"/> 保住堆栈,业务异常照常冒泡到统一异常过滤器。</para>
/// </summary>
public static class SqlSugarTransactionExtensions
{
    /// <inheritdoc cref="SqlSugarTransactionExtensions"/>
    public static async Task RunInTransactionAsync(this ISqlSugarClient db, Func<Task> work)
    {
        var result = await db.Ado.UseTranAsync(work);
        if (!result.IsSuccess) ExceptionDispatchInfo.Throw(result.ErrorException);
    }

    /// <inheritdoc cref="SqlSugarTransactionExtensions"/>
    public static async Task<T> RunInTransactionAsync<T>(this ISqlSugarClient db, Func<Task<T>> work)
    {
        var value = default(T)!;
        var result = await db.Ado.UseTranAsync(async () => value = await work());
        if (!result.IsSuccess) ExceptionDispatchInfo.Throw(result.ErrorException);
        return value;
    }
}
