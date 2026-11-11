using AgentEye.Core.Alerts;
using AgentEye.Core.Cache;
using AgentEye.Core.Localization;
using AgentEye.Core.Providers;

namespace AgentEye.WinUI.Services;

/// <summary>
/// 告警评估(1.6.0 重写):
/// - alert.enable 是总闸门(此前开关只在设置页显隐阈值行,弹 toast 不受控——已修);
/// - 跨阈值弹一次、同 provider 同等级 60 分钟冷却(跨重启)、warn→critical 升级视为新告警;
/// - 一轮多 provider 合并成一条 toast;
/// - 回落到 ok 且之前告警过 → 恢复 toast(行消失/暂停/未配置只清状态,不算恢复);
/// - error 轮不清冷却记录(网络抖动恢复后不该把老告警重弹一遍),
///   连续 3 轮 error 触发「Key 可能失效」toast,共用同一份冷却;
/// - 免打扰时段(alert.quiet_start/quiet_end)内状态照记、toast 一律不弹,
///   结束后不补发(冷却已被消耗,不会突然轰炸)。
/// </summary>
public static class AlertService
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(1);

    /// <summary>连续 error 达到该轮数 → 提示 Key 可能失效。</summary>
    public const int ErrorStreakThreshold = 3;

    /// <summary>alert.enable 总闸门;键缺失视为开(手改 config 删键不至于静音)。</summary>
    public static bool Enabled(Dictionary<string, object?> cfg) =>
        !cfg.TryGetValue("alert", out var a) || a is not Dictionary<string, object?> ad ||
        !ad.TryGetValue("enable", out var e) || e is not false;

    /// <summary>
    /// 免打扰判定:alert.quiet_start/quiet_end("HH:mm",可跨午夜)。
    /// 任一键缺失或非法 = 关闭;start == end 也视为关闭。
    /// </summary>
    public static bool QuietActive(Dictionary<string, object?> cfg, DateTimeOffset now)
    {
        if (!cfg.TryGetValue("alert", out var a) || a is not Dictionary<string, object?> ad)
            return false;
        var start = QuietHours.ParseMinutes(ad.TryGetValue("quiet_start", out var qs) ? qs as string : null);
        var end = QuietHours.ParseMinutes(ad.TryGetValue("quiet_end", out var qe) ? qe as string : null);
        if (start is null || end is null || start.Value == end.Value) return false;
        var local = now.LocalDateTime;
        return QuietHours.Contains(local.Hour * 60 + local.Minute, start.Value, end.Value);
    }

    public static async Task EvaluateAsync(
        AppServices app, IReadOnlyList<ProviderResult> results, CancellationToken ct)
    {
        if (!Enabled(app.Cfg)) return; // 总闸门:关了整轮静默,状态也不动
        var notified = app.Cache.LoadAlertState();
        var streaks = app.Cache.LoadErrorStreaks();
        var now = Http.Now();
        var changedNotified = false;
        var changedStreaks = false;
        var fired = new List<ProviderResult>();
        var recovered = new List<ProviderResult>();
        var errFired = new List<ProviderResult>();
        var present = results.Select(r => r.Name).ToHashSet();

        foreach (var row in results)
        {
            var name = row.Name;
            switch (row.Level)
            {
                case Levels.Warn or Levels.Critical:
                    // 拉通了(哪怕额度低)就不算「持续失败」
                    if (streaks.Remove(name)) changedStreaks = true;
                    if (notified.TryGetValue(name, out var prev) &&
                        prev.Level == row.Level && now - prev.Ts < Cooldown.TotalSeconds)
                        break; // 同等级且还在冷却窗内:不重复
                    notified[name] = (row.Level, now);
                    changedNotified = true;
                    fired.Add(row);
                    break;

                case Levels.Ok:
                    if (streaks.Remove(name)) changedStreaks = true;
                    // 曾告警过 → 恢复通知;从没告警过的正常行不弹(避免每次启动轰炸)
                    if (notified.Remove(name))
                    {
                        changedNotified = true;
                        recovered.Add(row);
                    }
                    break;

                case Levels.Error:
                {
                    // 坏状态保持:清掉 notified 会丢「仍在告警中」的事实,
                    // 网络抖动恢复后老告警会被重弹一遍
                    var streak = streaks.TryGetValue(name, out var prevS) ? prevS.Streak + 1 : 1;
                    streaks[name] = (streak, now);
                    changedStreaks = true;
                    if (streak < ErrorStreakThreshold) break;
                    if (notified.TryGetValue(name, out var prevErr) &&
                        prevErr.Level == Levels.Error && now - prevErr.Ts < Cooldown.TotalSeconds)
                        break; // 失败告警也走 60 分钟冷却,别每轮都弹
                    notified[name] = (Levels.Error, now);
                    changedNotified = true;
                    errFired.Add(row);
                    break;
                }

                default:
                    // paused / unconfigured / unknown:静默清状态,
                    // 既不算恢复(不弹)也不保留冷却(取消暂停后可立即再告警)
                    if (notified.Remove(name)) changedNotified = true;
                    if (streaks.Remove(name)) changedStreaks = true;
                    break;
            }
        }

        // 行消失(provider 已删):静默清理,不算恢复
        foreach (var name in notified.Keys.Where(n => !present.Contains(n)).ToList())
        {
            notified.Remove(name);
            changedNotified = true;
        }
        foreach (var name in streaks.Keys.Where(n => !present.Contains(n)).ToList())
        {
            streaks.Remove(name);
            changedStreaks = true;
        }

        if (changedNotified) app.Cache.SaveAlertState(notified);
        if (changedStreaks) app.Cache.SaveErrorStreaks(streaks);

        // 免打扰:冷却状态上面已照记(时钟在走),toast 一律不出——结束后不补发
        if (QuietActive(app.Cfg, DateTimeOffset.Now)) return;

        if (fired.Count > 0)
            await app.Notify.ShowAlertAsync(fired, ct);
        if (recovered.Count > 0)
            await app.Notify.ShowRecoveryAsync(recovered, ct);
        if (errFired.Count > 0)
            await app.Notify.ShowErrorStreakAsync(errFired, ct);
    }
}

/// <summary>
/// 通知:WinRT/AppSDK toast 优先,失败回退 MessageBeep。
/// 任何一步失败都静默降级,不把轮询主流程带崩。
/// </summary>
public sealed class NotifyService
{
    private bool _registered;
    private bool _registerFailed;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);

    /// <summary>合并告警 toast(标题 + 每行一段明细)。</summary>
    public async Task ShowAlertAsync(IReadOnlyList<ProviderResult> rows, CancellationToken ct)
    {
        var title = rows.Count == 1
            ? L.F("notify.titleOne", rows[0].Name)
            : L.F("notify.titleMany", rows.Count);
        var body = string.Join("\n", rows.Select(r =>
            $"{r.Name}: {(r.Pct is null ? r.Detail : L.F("notify.body", Http.F0(r.Pct.Value), r.Detail))}"));
        await ShowAsync(title, body);
        await Task.CompletedTask;
    }

    /// <summary>恢复 toast:之前告警过的 provider 回到 ok。</summary>
    public async Task ShowRecoveryAsync(IReadOnlyList<ProviderResult> rows, CancellationToken ct)
    {
        var title = rows.Count == 1
            ? L.F("notify.recoverTitleOne", rows[0].Name)
            : L.F("notify.recoverTitleMany", rows.Count);
        var body = string.Join("\n", rows.Select(r =>
            $"{r.Name}: {(r.Detail is { Length: > 0 } d ? d : L.T("ov.level.ok"))}"));
        await ShowAsync(title, body);
        await Task.CompletedTask;
    }

    /// <summary>连续拉取失败 toast:error 明细可能很长,每行截 60 字符。</summary>
    public async Task ShowErrorStreakAsync(IReadOnlyList<ProviderResult> rows, CancellationToken ct)
    {
        var title = rows.Count == 1
            ? L.F("notify.errorTitleOne", rows[0].Name)
            : L.F("notify.errorTitleMany", rows.Count);
        var body = string.Join("\n", rows.Select(r =>
        {
            var err = r.Error ?? r.Detail;
            if (err is { Length: > 60 } e) err = e[..60] + "…";
            return $"{r.Name}: {err}";
        }));
        await ShowAsync(title, body);
        await Task.CompletedTask;
    }

    public async Task ShowAsync(string title, string body)
    {
        var shown = false;
        try
        {
            if (!_registerFailed)
            {
                var manager = Microsoft.Windows.AppNotifications.AppNotificationManager.Default;
                if (!_registered)
                {
                    manager.NotificationInvoked += (_, _) => { };
                    manager.Register();
                    _registered = true;
                }
                var builder = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                    .AddText(title)
                    .AddText(body);
                manager.Show(builder.BuildNotification());
                shown = true;
            }
        }
        catch (Exception)
        {
            _registerFailed = true;
        }
        if (!shown)
        {
            try { MessageBeep(0x00000030); } catch { /* 静默 */ }
        }
        await Task.CompletedTask;
    }
}
