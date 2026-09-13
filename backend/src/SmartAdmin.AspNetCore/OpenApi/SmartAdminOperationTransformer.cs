using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SmartAdmin.Core;
using System.Text.Json.Nodes;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 给每个操作补上稳定的 <c>operationId</c> 与三个扩展字段。
/// <para><b>为什么要</b>:不加这层,同名操作(如 <c>Delete</c>/<c>Update</c>,跨多个资源各来一份)全靠路径区分——
/// SDK 生成器、MCP 工具面、LLM 的工具调用都得靠猜。而"这个端点要什么权限"这件事,
/// 明明在内核里是可计算的(权限码就是规范化路由),契约里却一个字都没有。</para>
/// <list type="bullet">
/// <item><c>operationId</c> = <c>{控制器}_{动作}</c>,重名时按顺序加后缀(生成的客户端方法名要稳定)。</item>
/// <item><c>x-permission-code</c>:该端点的权限码,与角色授权页勾选的那一条同源。</item>
/// <item><c>x-auth</c>:<c>anonymous</c> | <c>permission</c> | <c>session</c> | <c>authenticated</c>。</item>
/// <item><c>x-module</c>:所属模块名(可经 <c>Api:DisabledModules</c> 整体关掉的那个名字)。</item>
/// </list>
/// </summary>
internal sealed class SmartAdminOperationTransformer : IOpenApiOperationTransformer
{
    // 同名操作的出现次数。文档生成是单线程逐个操作跑的,但字典跨操作共享,故实例按文档创建。
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var description = context.Description;

        if (description.ActionDescriptor is ControllerActionDescriptor action)
            operation.OperationId = UniqueId($"{action.ControllerName}_{action.ActionName}");

        var metadata = description.ActionDescriptor.EndpointMetadata;
        var anonymous = metadata.OfType<IAllowAnonymous>().Any();
        var apiKey = metadata.OfType<ApiKeyAttribute>().Any();
        var permission = metadata.OfType<RolePermissionAttribute>().Any();
        var session = metadata.OfType<ActiveSessionAttribute>().Any();

        // apikey 优先于 permission:调用方先得知道"拿什么凭证",再看要不要授权;权限码另有 x-permission-code
        Set(operation, "x-auth",
            anonymous ? "anonymous"
            : apiKey ? "apikey"
            : permission ? "permission"
            : session ? "session"
            : "authenticated");

        // 权限码与授权判定同源:两边都走 PermissionCode.Build,契约与角色授权页不会各说各话
        if (permission && description.RelativePath is not null)
            Set(operation, "x-permission-code", PermissionCode.Build(description.HttpMethod ?? "GET", description.RelativePath));

        if (metadata.OfType<ModuleAttribute>().FirstOrDefault() is { } module)
            Set(operation, "x-module", module.Name);

        WrapBareSuccessSchema(operation, description);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 裸返回 <c>dto</c> 的端点,契约里记的是 <c>dto</c>,而运行时 <see cref="ResultEnvelopeFilter"/> 会把它包成
    /// <c>Result&lt;dto&gt;</c>——照这份契约生成的前端类型会把 <c>data</c> 当成顶层字段,而且没有任何报错。
    /// 这里按同一条规则(声明类型不是信封就会被包)把 200 的 schema 补成信封外壳。
    /// <para>内置控制器全部显式返回 <c>Result&lt;T&gt;</c>,对它们是空操作;受影响的是消费者自己写的控制器。</para>
    /// </summary>
    private static void WrapBareSuccessSchema(OpenApiOperation operation, ApiDescription description)
    {
        // [SkipEnvelope] 的端点运行时不包,契约也不包——两边同一条规则
        if (description.ActionDescriptor.EndpointMetadata.OfType<SkipEnvelopeAttribute>().Any()) return;
        var success = description.SupportedResponseTypes
            .FirstOrDefault(r => r.StatusCode is >= 200 and < 300);
        if (success?.Type is not { } type) return;
        if (type == typeof(void) || typeof(IResultEnvelope).IsAssignableFrom(type)) return;
        // 文件下载、纯状态码返回不经信封,ApiExplorer 给的也不是 JSON
        if (typeof(IActionResult).IsAssignableFrom(type) || typeof(Stream).IsAssignableFrom(type)) return;

        if (operation.Responses is null) return;
        foreach (var (status, response) in operation.Responses)
        {
            if (!status.StartsWith('2') || response.Content is null) continue;
            if (!response.Content.TryGetValue("application/json", out var media) || media.Schema is null) continue;
            media.Schema = Envelope(media.Schema);
        }
    }

    /// <summary>统一信封的外壳 schema,<c>data</c> 挂原始那个。字段名与 <c>Result&lt;T&gt;</c> 一致。</summary>
    private static OpenApiSchema Envelope(IOpenApiSchema data) => new()
    {
        Type = JsonSchemaType.Object,
        Description = "统一信封(运行时由 ResultEnvelopeFilter 包裹;业务错误走 200 + code)",
        Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
        {
            ["code"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            ["msgKey"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
            ["message"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
            ["data"] = data,
        },
    };

    /// <summary>重名就加后缀:客户端生成器要的是稳定且唯一的方法名,而不是让它自己去编。</summary>
    private string UniqueId(string id)
    {
        var count = _seen.TryGetValue(id, out var n) ? n : 0;
        _seen[id] = count + 1;
        return count == 0 ? id : $"{id}_{count + 1}";
    }

    private static void Set(OpenApiOperation operation, string name, string value)
    {
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        operation.Extensions[name] = new JsonNodeExtension(JsonValue.Create(value));
    }
}
