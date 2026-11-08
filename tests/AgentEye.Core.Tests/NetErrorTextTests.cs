using AgentEye.Core.Localization;
using AgentEye.Core.Providers;
using Xunit;

namespace AgentEye.Core.Tests;

/// <summary>
/// 网络错误摘要:GetJsonAsync 失败时元组第 4 元要带底层原因
/// (如「代理 127.0.0.1:9652 拒绝连接」),经 NetErrText/WithReason
/// 拼进卡片明细,不再只有笼统的「网络异常」。
/// 连本机必拒端口触发真实 HttpRequestException(连接即时被拒,不耗时)。
/// ProxyTests 会在测试里改 HttpClient.DefaultProxy(进程级静态),
/// 两者入同一 collection 串行跑,互不污染。
/// </summary>
[Collection("http-global")]
public class NetErrorTextTests
{
    [Fact]
    public async Task GetJson_Failure_NetErr_CarriesReason()
    {
        L.Init("zh-Hans");
        var (ok, _, _, why) = await Http.GetJsonAsync(
            "http://127.0.0.1:1/", null, Http.DefaultTimeoutSec);
        Assert.False(ok);
        Assert.NotNull(why);
        Assert.Contains("127.0.0.1:1", why); // 带着被拒的地址
        var text = Http.NetErrText(why);
        Assert.StartsWith("网络错误:", text);
        Assert.Contains(why, text);
    }

    [Fact]
    public void NetErrText_NullWhy_FallsBackToGeneric()
    {
        L.Init("zh-Hans");
        Assert.Equal(L.T("pr.netErr"), Http.NetErrText(null));
        Assert.Equal(L.T("pr.netErr"), Http.NetErrText(""));
    }

    [Fact]
    public void WithReason_AppendsOrNull()
    {
        L.Init("zh-Hans");
        Assert.Equal("probe: 网络错误: 超时", Http.WithReason("probe: 网络错误", "超时"));
        Assert.Equal("probe: 网络错误", Http.WithReason("probe: 网络错误", null));
    }

    [Fact]
    public async Task GetJson_Failure_WithReason_JoinsEndpointLabel()
    {
        L.Init("zh-Hans");
        var (ok, _, _, why) = await Http.GetJsonAsync(
            "http://127.0.0.1:1/", null, Http.DefaultTimeoutSec);
        Assert.False(ok);
        var joined = Http.WithReason("probe: 网络错误", why);
        Assert.StartsWith("probe: 网络错误: ", joined);
        Assert.Contains("127.0.0.1:1", joined);
    }
}
