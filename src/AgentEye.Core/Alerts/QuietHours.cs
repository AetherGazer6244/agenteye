namespace AgentEye.Core.Alerts;

/// <summary>
/// 免打扰时段解析与判定(纯逻辑,WinUI 壳层弹 toast 前调用)。
/// 配置存 alert.quiet_start / alert.quiet_end("HH:mm" 文本,自由字典加键,
/// schema_version 仍为 2,Python 版 merge 保留未知键);任一键缺失或非法
/// = 免打扰关闭。支持跨午夜(如 23:00–08:00)。
/// </summary>
public static class QuietHours
{
    /// <summary>"HH:mm" → 当日分钟数(0..1439);空串/格式/范围非法一律 null(视为关闭)。</summary>
    public static int? ParseMinutes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m))
            return null;
        if (h is < 0 or > 23 || m is < 0 or > 59) return null;
        return h * 60 + m;
    }

    /// <summary>
    /// now/start/end 均为当日分钟数。左闭右开:start 时刻命中,end 时刻已出窗。
    /// start == end 视为关闭(零长窗口没法表达"全天",直接关掉最不出错)。
    /// </summary>
    public static bool Contains(int nowMinutes, int startMinutes, int endMinutes)
    {
        if (startMinutes == endMinutes) return false;
        if (startMinutes < endMinutes)
            return nowMinutes >= startMinutes && nowMinutes < endMinutes;
        // 跨午夜(如 23:00–08:00):窗口 = [start, 24:00) ∪ [0, end)
        return nowMinutes >= startMinutes || nowMinutes < endMinutes;
    }
}
