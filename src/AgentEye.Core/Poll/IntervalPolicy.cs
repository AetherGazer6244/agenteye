using AgentEye.Core.Providers;

namespace AgentEye.Core.Poll;

/// <summary>
/// 自适应轮询间隔(纯逻辑,PollerService 每轮调一次):
/// - 有 critical 行:间隔减半,下限 30 秒(别把被监控方的额度端点打疼);
///   结果不比基础间隔更慢(用户手调的 15s 基线在 critical 时保持 15s)。
/// - 整轮全 ok(含 unconfigured/paused):间隔 ×1.5 放缓,上限 1 小时。
/// - 连续整轮全错:×2^errorRounds 指数退避,上限 1 小时;任一行拉通即归零重计。
/// - 混合状态(有 warn / 部分 error):维持基础间隔,不猜。
/// 未启用自适应(ui.adaptive_poll=false)一律返回基础间隔(夹取 15s~1h)。
/// </summary>
public static class IntervalPolicy
{
    public const int MinSec = 15;
    public const int MaxSec = 3600;
    /// <summary>critical 加密的下限:再急也不快过 30 秒一轮。</summary>
    public const int CriticalFloorSec = 30;
    /// <summary>退避指数上限(2^5=32 倍,再往上 MaxSec 兜底)。</summary>
    public const int MaxBackoffPower = 5;

    public static int NextSec(
        int baseSec, IReadOnlyCollection<string> levels, int errorRounds, bool adaptive)
    {
        var sec = Math.Clamp(baseSec, MinSec, MaxSec);
        if (!adaptive || levels.Count == 0) return sec;

        if (levels.All(l => l == Levels.Error))
        {
            var power = Math.Clamp(errorRounds, 1, MaxBackoffPower);
            return Math.Min(MaxSec, sec * (int)Math.Pow(2, power));
        }
        if (levels.Contains(Levels.Critical))
        {
            var halved = Math.Max(CriticalFloorSec, sec / 2);
            return Math.Min(sec, halved); // 加密 ≠ 比基线更慢
        }
        if (levels.All(l => l is Levels.Ok or Levels.Unconfigured or Levels.Paused))
            return Math.Min(MaxSec, sec * 3 / 2);
        return sec;
    }
}
