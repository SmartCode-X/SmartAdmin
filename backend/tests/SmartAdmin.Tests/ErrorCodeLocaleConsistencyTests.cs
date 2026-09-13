using SmartAdmin.Core;

namespace SmartAdmin.Tests;

/// <summary>
/// 后端每个 <see cref="ErrorCode"/> 带 <c>[MsgKey]</c>,前端按 key 查 i18n 文案显示。
/// 没有任何东西保证"新加了错误码就配了两种语言的文案"——漏一个,用户看到的就是原始 key(如 <c>error.auth.xxx</c>)。
/// 这条守卫锁死:每个 MsgKey 的叶子段在 zh-CN 与 en-US 里都存在。
/// <para>叶子段匹配是刻意的轻量做法:跨 C# 解析 TS 对象树不划算,而"漏翻译"几乎总是整条 key 缺失,
/// 查叶子 <c>xxx:</c> 足以抓住。本地找不到语言包(独立打包场景)时跳过;CI 是全量检出,找不到只能是路径漂了,直接失败。</para>
/// </summary>
public class ErrorCodeLocaleConsistencyTests
{
    private static string? FindLocaleDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "web", "packages", "admin", "src", "locales");
            if (File.Exists(Path.Combine(candidate, "zh-CN.ts"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Every_error_code_msgkey_is_translated_in_both_locales()
    {
        var localeDir = FindLocaleDir();
        if (localeDir is null)
        {
            Assert.True(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is null,
                "CI 找不到 web/packages/admin/src/locales/zh-CN.ts:语言包路径变了,这条守卫静默跳过等于没测。");
            return;
        }

        var zh = File.ReadAllText(Path.Combine(localeDir, "zh-CN.ts"));
        var en = File.ReadAllText(Path.Combine(localeDir, "en-US.ts"));

        var missing = new List<string>();
        foreach (var code in Enum.GetValues<ErrorCode>())
        {
            var key = code.GetMsgKey();
            if (string.IsNullOrEmpty(key)) continue;
            var leaf = key[(key.LastIndexOf('.') + 1)..];
            var needle = leaf + ":";
            if (!zh.Contains(needle)) missing.Add($"{key} (zh-CN 缺 {leaf})");
            if (!en.Contains(needle)) missing.Add($"{key} (en-US 缺 {leaf})");
        }

        Assert.True(missing.Count == 0, "以下错误码缺少 i18n 文案:\n" + string.Join("\n", missing));
    }
}
