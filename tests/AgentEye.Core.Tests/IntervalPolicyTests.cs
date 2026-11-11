using AgentEye.Core.Poll;
using AgentEye.Core.Providers;
using Xunit;

namespace AgentEye.Core.Tests;

/// <summary>自适应轮询间隔(1.6.0):critical 加密 / 全 ok 放缓 / 全错退避 / 未启用直通。</summary>
public class IntervalPolicyTests
{
    [Theory]
    [InlineData(10, 15)]    // 下夹取到 MinSec
    [InlineData(7200, 3600)] // 上夹取
    public void Clamps_Base_Seconds(int baseSec, int expected) =>
        Assert.Equal(expected, IntervalPolicy.NextSec(baseSec, [Levels.Ok], 0, adaptive: false));

    [Fact]
    public void Adaptive_Off_Returns_Base()
    {
        // 即使全 critical、退避轮数拉满:未启用 = 基础间隔
        Assert.Equal(60, IntervalPolicy.NextSec(60, [Levels.Critical], 9, adaptive: false));
    }

    [Fact]
    public void Critical_Halves_With_Floor()
    {
        Assert.Equal(30, IntervalPolicy.NextSec(60, [Levels.Critical], 0, adaptive: true));
        Assert.Equal(45, IntervalPolicy.NextSec(90, [Levels.Ok, Levels.Critical], 0, adaptive: true));
        // 基线比下限还短:保持基线(加密 ≠ 更慢)
        Assert.Equal(15, IntervalPolicy.NextSec(15, [Levels.Critical], 0, adaptive: true));
    }

    [Fact]
    public void All_Ok_Slows_Down()
    {
        Assert.Equal(90, IntervalPolicy.NextSec(60, [Levels.Ok], 0, adaptive: true));
        Assert.Equal(90, IntervalPolicy.NextSec(60,
            [Levels.Ok, Levels.Unconfigured, Levels.Paused], 0, adaptive: true));
        Assert.Equal(IntervalPolicy.MaxSec, IntervalPolicy.NextSec(3000, [Levels.Ok], 0, adaptive: true));
    }

    [Fact]
    public void All_Error_Backsoff_Exponentially()
    {
        // 第 1 次全错 ×2,第 2 次 ×4;指数封顶 2^5,秒数封顶 MaxSec
        Assert.Equal(120, IntervalPolicy.NextSec(60, [Levels.Error], 1, adaptive: true));
        Assert.Equal(240, IntervalPolicy.NextSec(60, [Levels.Error], 2, adaptive: true));
        Assert.Equal(1920, IntervalPolicy.NextSec(60, [Levels.Error], 5, adaptive: true));
        Assert.Equal(1920, IntervalPolicy.NextSec(60, [Levels.Error], 9, adaptive: true)); // 指数封顶
        Assert.Equal(IntervalPolicy.MaxSec,
            IntervalPolicy.NextSec(200, [Levels.Error], 9, adaptive: true)); // 秒数封顶
    }

    [Fact]
    public void Mixed_States_Keep_Base()
    {
        // 有 warn / 部分 error:不猜,维持基础间隔
        Assert.Equal(60, IntervalPolicy.NextSec(60, [Levels.Ok, Levels.Warn], 0, adaptive: true));
        Assert.Equal(60, IntervalPolicy.NextSec(60, [Levels.Ok, Levels.Error], 3, adaptive: true));
    }

    [Fact]
    public void Empty_Levels_Returns_Base()
    {
        Assert.Equal(45, IntervalPolicy.NextSec(45, [], 0, adaptive: true));
    }
}
