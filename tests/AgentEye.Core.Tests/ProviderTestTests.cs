using AgentEye.Core.Localization;
using AgentEye.Core.Providers;
using Xunit;

namespace AgentEye.Core.Tests;

/// <summary>
/// ProviderTest「测试 Key」编排的短路路径:空 url / 空 key 时适配器直接
/// 返回错误,不打网络请求,断言可确定。
/// </summary>
public class ProviderTestTests
{
    [Fact]
    public async Task Generic_Without_Url_Fails_With_BaseUrl_Error()
    {
        var outcome = await ProviderTest.RunAsync("generic_openai", "", "sk-xxx", "");
        Assert.False(outcome.Ok);
        Assert.Equal(L.T("gen.noBaseUrl"), outcome.Message);
        Assert.Empty(outcome.Models);
    }

    [Fact]
    public async Task Official_Without_Key_Fails_Unconfigured()
    {
        var outcome = await ProviderTest.RunAsync(
            "deepseek", "https://api.deepseek.com", "", "");
        Assert.False(outcome.Ok);
        Assert.Equal(L.T("pr.noKey"), outcome.Message);
    }

    [Fact]
    public async Task Official_BaseUrl_Is_Trimmed_Not_Required_To_Be_NonEmpty()
    {
        // 官方 kind 空 URL 也允许(拉取层有 fallback):key 空的报错先到,
        // 证明请求没有被 base_url 校验拦下
        var outcome = await ProviderTest.RunAsync("zhipu", "  ", "", "");
        Assert.False(outcome.Ok);
        Assert.Equal(L.T("pr.noKey"), outcome.Message);
    }
}
