using AgentEye.Core.Config;
using AgentEye.Core.Providers;
using Xunit;

namespace AgentEye.Core.Tests;

/// <summary>
/// 1.6.0 新增 provider:SiliconFlow / OpenRouter 的纯解析、
/// $ 绝对额等级判定(critical_amount_usd)与 Detect URL 提示。
/// </summary>
public class NewProviderTests
{
    // ------------------------------------------------------------------
    // SiliconFlow:balance(¥ 字符串)/ totalBalance
    // ------------------------------------------------------------------

    [Fact]
    public void SiliconFlow_Parses_Balance_And_Total()
    {
        var p = SiliconFlowProvider.ParseBody(12.34, 100.0);
        Assert.NotNull(p);
        Assert.Equal(12.34, p!.Remaining);
        Assert.Equal(100.0, p.Total);
        Assert.Equal("¥", p.Unit);
        Assert.Null(p.Error);
        Assert.Contains("12.34", p.Detail);
    }

    [Fact]
    public void SiliconFlow_Missing_Balance_Is_StructError()
    {
        Assert.Null(SiliconFlowProvider.ParseBody(null, 100.0));
    }

    [Fact]
    public void SiliconFlow_AllZero_Is_Unconfigured()
    {
        // 免费户 $0 常驻 critical 没有意义:灰显 + 提示文案
        var p = SiliconFlowProvider.ParseBody(0, 0);
        Assert.NotNull(p);
        Assert.NotNull(p!.Error);
        Assert.True(p.Unconfigured);
    }

    [Fact]
    public void SiliconFlow_ZeroBalance_WithTotal_Still_Works()
    {
        // 有总资产(赠金)就不是免费户:正常展示
        var p = SiliconFlowProvider.ParseBody(0, 5.0);
        Assert.NotNull(p);
        Assert.Null(p!.Error);
        Assert.Equal(0, p.Remaining);
    }

    // ------------------------------------------------------------------
    // OpenRouter:total_credits / total_usage($)
    // ------------------------------------------------------------------

    [Fact]
    public void OpenRouter_Parses_Credits_Minus_Usage()
    {
        var p = OpenRouterProvider.ParseBody(10.0, 3.5);
        Assert.NotNull(p);
        Assert.Equal(6.5, p!.Remaining);
        Assert.Equal(3.5, p.Used);
        Assert.Equal(10.0, p.Total);
        Assert.Equal("$", p.Unit);
        Assert.Null(p.Error);
    }

    [Fact]
    public void OpenRouter_Missing_Fields_Is_StructError()
    {
        Assert.Null(OpenRouterProvider.ParseBody(10.0, null));
        Assert.Null(OpenRouterProvider.ParseBody(null, null));
    }

    [Fact]
    public void OpenRouter_AllZero_Is_Unconfigured()
    {
        var p = OpenRouterProvider.ParseBody(0, 0);
        Assert.NotNull(p);
        Assert.NotNull(p!.Error);
        Assert.True(p.Unconfigured);
    }

    [Fact]
    public void OpenRouter_Overdraft_Clamps_To_Zero()
    {
        // 用超了不该出现负余额
        var p = OpenRouterProvider.ParseBody(1.0, 2.0);
        Assert.NotNull(p);
        Assert.Equal(0.0, p!.Remaining);
    }

    // ------------------------------------------------------------------
    // ComputeLevel:$ 绝对额分支(1.6.0 补的缺口)
    // ------------------------------------------------------------------

    private static Dictionary<string, object?> Cfg(double? usd = null, double? yuan = null) =>
        new()
        {
            ["alert"] = new Dictionary<string, object?>
            {
                ["critical_amount_yuan"] = yuan ?? 5.0,
                ["critical_amount_usd"] = usd ?? 2.0,
            },
        };

    private static ProviderResult Row(string unit, double? remaining, double? pct = null) =>
        new() { Unit = unit, Remaining = remaining, Pct = pct, Name = "t", Kind = "k", Type = "k" };

    [Fact]
    public void Usd_Absolute_NoPct_Below_Threshold_Is_Critical()
    {
        Assert.Equal(Levels.Critical,
            ProviderRegistry.ComputeLevel(Row("$", 1.5), new(), Cfg()));
        Assert.Equal(Levels.Ok,
            ProviderRegistry.ComputeLevel(Row("$", 2.5), new(), Cfg()));
    }

    [Fact]
    public void Usd_Absolute_Threshold_Customizable()
    {
        Assert.Equal(Levels.Critical,
            ProviderRegistry.ComputeLevel(Row("$", 4.0), new(), Cfg(usd: 5.0)));
    }

    [Fact]
    public void Usd_WithPct_Keeps_Pct_Path()
    {
        // opencode_go 等「$ + pct 双字段」行不受 $ 分支影响:仍按 warn_pct 走
        var res = Row("$", 55.0, pct: 40);
        Assert.Equal(Levels.Ok, ProviderRegistry.ComputeLevel(res, new(), Cfg()));
        var warn = Row("$", 55.0, pct: 20);
        Assert.Equal(Levels.Warn, ProviderRegistry.ComputeLevel(warn, new(), Cfg()));
    }

    [Fact]
    public void Usd_NoRemaining_Is_Ok()
    {
        Assert.Equal(Levels.Ok, ProviderRegistry.ComputeLevel(Row("$", null), new(), Cfg()));
    }

    [Fact]
    public void Unconfigured_FreeAccount_Stays_Unconfigured()
    {
        // 适配器把免费户回成 Error + Unconfigured:等级判定保持灰
        var res = Row("$", null);
        res.Error = "free";
        res.Unconfigured = true;
        Assert.Equal(Levels.Unconfigured, ProviderRegistry.ComputeLevel(res, new(), Cfg()));
    }

    // ------------------------------------------------------------------
    // Detect:URL 提示
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("https://api.siliconflow.cn/v1", "siliconflow", "https://api.siliconflow.cn")]
    [InlineData("https://openrouter.ai/api/v1", "openrouter", "https://openrouter.ai")]
    public void Detect_Url_Hints(string url, string kind, string baseUrl)
    {
        var d = Detect.Run(key: "", hintUrl: url);
        Assert.Equal(kind, d.Kind);
        Assert.Equal(baseUrl, d.BaseUrl);
        Assert.Equal("high", d.Confidence);
    }

    // ------------------------------------------------------------------
    // ConfigStore:默认端点 / 环境变量 / 模板阈值键
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("siliconflow", "https://api.siliconflow.cn")]
    [InlineData("openrouter", "https://openrouter.ai")]
    public void DefaultBaseUrl_Known(string kind, string expected) =>
        Assert.Equal(expected, ConfigStore.DefaultBaseUrl(kind));

    [Fact]
    public void Template_Has_Usd_Threshold_And_Quiet_Keys()
    {
        var alert = Assert.IsType<Dictionary<string, object?>>(ConfigStore.Template()["alert"]);
        Assert.Equal(2.0, alert["critical_amount_usd"]);
        Assert.Equal("", alert["quiet_start"]);
        Assert.Equal("", alert["quiet_end"]);
    }

    [Fact]
    public void Template_Has_AdaptivePoll_Default_Off()
    {
        var ui = Assert.IsType<Dictionary<string, object?>>(ConfigStore.Template()["ui"]);
        Assert.Equal(false, ui["adaptive_poll"]);
    }
}
