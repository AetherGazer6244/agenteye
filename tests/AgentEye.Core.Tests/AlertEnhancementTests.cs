using AgentEye.Core.Alerts;
using AgentEye.Core.Cache;
using AgentEye.Core.Providers;
using Xunit;

namespace AgentEye.Core.Tests;

/// <summary>免打扰时段解析与判定(1.6.0)。</summary>
public class QuietHoursTests
{
    [Theory]
    [InlineData("23:00", 1380)]
    [InlineData("08:30", 510)]
    [InlineData("0:00", 0)]
    [InlineData(" 12:05 ", 725)]
    public void Parse_Valid(string text, int expected) =>
        Assert.Equal(expected, QuietHours.ParseMinutes(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("-1:30")]
    [InlineData("12")]
    [InlineData("ab:cd")]
    [InlineData("12:30:00")]
    public void Parse_Invalid_Returns_Null(string? text) =>
        Assert.Null(QuietHours.ParseMinutes(text));

    [Fact]
    public void Parse_Lenient_Single_Digit_Minute()
    {
        // "12:3" 按宽松解析 = 12:03(手改配置容错)
        Assert.Equal(12 * 60 + 3, QuietHours.ParseMinutes("12:3"));
    }

    [Fact]
    public void Contains_NormalWindow_Boundaries()
    {
        // 12:00–13:00:start 命中,end 已出窗
        Assert.True(QuietHours.Contains(720, 720, 780));
        Assert.True(QuietHours.Contains(779, 720, 780));
        Assert.False(QuietHours.Contains(780, 720, 780));
        Assert.False(QuietHours.Contains(719, 720, 780));
    }

    [Fact]
    public void Contains_Overnight_Wrap()
    {
        // 23:00–08:00:深夜与清晨在窗内,中午在窗外
        Assert.True(QuietHours.Contains(23 * 60, 23 * 60, 8 * 60));
        Assert.True(QuietHours.Contains(2 * 60, 23 * 60, 8 * 60));
        Assert.True(QuietHours.Contains(7 * 60 + 59, 23 * 60, 8 * 60));
        Assert.False(QuietHours.Contains(8 * 60, 23 * 60, 8 * 60));
        Assert.False(QuietHours.Contains(12 * 60, 23 * 60, 8 * 60));
    }

    [Fact]
    public void Contains_Equal_StartEnd_Is_Off()
    {
        Assert.False(QuietHours.Contains(720, 720, 720));
        Assert.False(QuietHours.Contains(0, 0, 0));
    }
}

/// <summary>
/// alert_state.json 的 "errors" 键(连续失败轮数)与 "notified" 键(冷却)
/// 同文件互存:谁写盘都不许冲掉对方的键(1.6.0)。
/// </summary>
public class ErrorStreakStateTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ae-tests", Guid.NewGuid().ToString("N")[..8]);
    private readonly CacheStore _cache;

    public ErrorStreakStateTests() => _cache = new CacheStore(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void ErrorStreaks_RoundTrip_FiltersBadRows()
    {
        var now = Http.Now();
        _cache.SaveErrorStreaks(new Dictionary<string, (int, double)>
        {
            ["DS"] = (3, now - 60),
            ["Zero"] = (0, now),          // 0 轮无意义:丢弃
            ["Future"] = (2, now + 3600), // 未来时间戳:丢弃
        });
        var streaks = _cache.LoadErrorStreaks();
        Assert.Single(streaks);
        Assert.Equal(3, streaks["DS"].Streak);
    }

    [Fact]
    public void SaveErrorStreaks_Preserves_Notified()
    {
        _cache.SaveAlertState(new Dictionary<string, (string, double)>
        {
            ["DS"] = ("critical", Http.Now()),
        });
        _cache.SaveErrorStreaks(new Dictionary<string, (int, double)>
        {
            ["DS"] = (3, Http.Now()),
        });
        // 冷却记录不能被 errors 写盘冲掉
        var notified = _cache.LoadAlertState();
        Assert.True(notified.ContainsKey("DS"));
        Assert.Equal("critical", notified["DS"].Level);
    }

    [Fact]
    public void SaveAlertState_Preserves_ErrorStreaks()
    {
        _cache.SaveErrorStreaks(new Dictionary<string, (int, double)>
        {
            ["DS"] = (3, Http.Now()),
        });
        _cache.SaveAlertState(new Dictionary<string, (string, double)>
        {
            ["DS"] = ("critical", Http.Now()),
        });
        var streaks = _cache.LoadErrorStreaks();
        Assert.True(streaks.ContainsKey("DS"));
        Assert.Equal(3, streaks["DS"].Streak);
    }

    [Fact]
    public void AlertState_Error_Level_Survives_RoundTrip()
    {
        // 1.6.0 起冷却等级集合含 error(连续失败告警共用冷却)
        _cache.SaveAlertState(new Dictionary<string, (string, double)>
        {
            ["DS"] = ("error", Http.Now()),
            ["Bad"] = ("ok", Http.Now()), // 依旧非法:丢弃
        });
        var state = _cache.LoadAlertState();
        Assert.Single(state);
        Assert.Equal("error", state["DS"].Level);
    }

    [Fact]
    public void Corrupt_File_Returns_Empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "alert_state.json"), "not json");
        Assert.Empty(_cache.LoadErrorStreaks());
        Assert.Empty(_cache.LoadAlertState());
    }
}
