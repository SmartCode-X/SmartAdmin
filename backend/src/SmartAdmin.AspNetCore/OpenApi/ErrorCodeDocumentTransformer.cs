using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using SmartAdmin.Core;
using System.Text.Json.Nodes;

namespace SmartAdmin.AspNetCore;

/// <summary>
/// 把错误码目录塞进契约的 <c>components.schemas.ErrorCode</c>。
/// <para><b>为什么要</b>:业务错误全走 200 + 信封里的 <c>code</c>,而契约里 <c>code</c> 只是个 <c>integer</c>——
/// 拿着这份契约的人(前端生成器、外部集成方、按契约调用的 agent)看不出 42024 是什么意思,
/// 也不知道该去查哪个 i18n 键。这里连数值、枚举成员名、msgKey 一起给出,含消费者登记进来的码。</para>
/// </summary>
internal sealed class ErrorCodeDocumentTransformer(IErrorCodeCatalog catalog) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var codes = catalog.All;
        if (codes.Count == 0) return Task.CompletedTask;

        // 保持开放的 integer,**不写 enum**。两个理由,后一个更要命:
        //   1. 生成器会把带 enum 的整数渲染成字面量联合,调用方传一个普通 number 就编译不过;
        //   2. 码空间本来就是开放的——消费者登记自己的枚举(60000 起),闭合的 enum 等于在契约里
        //      宣告那些码非法,而它们每天都在真实地飞。
        // 码表放进扩展字段:人和 agent 读得到,生成器忽略,两边都不亏。
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Integer,
            Format = "int32",
            Description = "业务错误码。0 = 成功;码表见 x-enum-values / x-enum-varnames / x-msg-keys,"
                + "或调 GET /api/v1/meta/error-codes(消费者登记的码也在里面)。",
            Extensions = new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal)
            {
                ["x-enum-values"] = new JsonNodeExtension(new JsonArray([.. codes.Select(c => (JsonNode)JsonValue.Create(c.Code))])),
                // x-enum-varnames 是 openapi-generator 一系的既有约定(生成带名字的枚举)
                ["x-enum-varnames"] = new JsonNodeExtension(new JsonArray([.. codes.Select(c => (JsonNode)JsonValue.Create(c.Name))])),
                // x-msg-keys 是本仓自己的:把码直接对到前端的 i18n 键上,省掉一张手工维护的对照表
                ["x-msg-keys"] = new JsonNodeExtension(new JsonArray([.. codes.Select(c => (JsonNode)JsonValue.Create(c.MsgKey))])),
            },
        };

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        document.Components.Schemas["ErrorCode"] = schema;
        return Task.CompletedTask;
    }
}
