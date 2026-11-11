using Microsoft.UI.Dispatching;
using AgentEye.Core.Config;
using AgentEye.Core.Poll;
using AgentEye.Core.Providers;

namespace AgentEye.WinUI.Services;

/// <summary>
/// 轮询引擎:PeriodicTimer 后台循环 + 单飞(进行中的轮询不叠加)。
/// 完成后回 UI 线程发 ResultsChanged;退出用 CancellationToken,进程关
/// 窗即取消,在途请求直接掐(对齐 Python daemon 线程语义)。
/// 1.6.0:ui.adaptive_poll 开启时间隔自适应(IntervalPolicy);
/// 另提供行级「立即刷新」(RefreshProviderAsync,独立单飞)。
/// </summary>
public sealed class PollerService : IDisposable
{
    private readonly AppServices _app;
    private readonly DispatcherQueue _dq;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _inFlight;
    private int _rowInFlight;
    private int _errorRounds; // 连续「整轮全错」计数(自适应退避;任一行拉通即归零)
    public DateTimeOffset? LastRefresh { get; private set; }

    public PollerService(AppServices app)
    {
        _app = app;
        _dq = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>启动周期轮询(15-3600 秒,读 cfg)。</summary>
    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async void Reschedule()
    {
        // 重建循环让新周期生效
        Stop();
        await Task.CompletedTask;
        Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _loop = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var sec = ConfigStore.ClampIntervalV2(_app.Cfg);
            try
            {
                var results = await RefreshNowAsync(ct);
                if (results is not null && results.Count > 0 && _app.AdaptivePoll)
                {
                    sec = IntervalPolicy.NextSec(
                        sec, results.Select(r => r.Level).ToList(), _errorRounds, adaptive: true);
                }
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(sec));
                await timer.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // 单轮失败不终止循环;退避 5 秒再进下一轮
                try { await Task.Delay(5000, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>
    /// 立即刷一轮(首启/设置保存/行右键菜单调用)。进行中则忽略,不叠加。
    /// 返回本轮结果(循环用来自适应算间隔);并发抢占时返回 null。
    /// </summary>
    public async Task<List<ProviderResult>?> RefreshNowAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return null;
        try
        {
            var results = await ProviderRegistry.FetchAllAsync(
                _app.Cfg, _app.Paused, ct);
            await AlertService.EvaluateAsync(_app, results, ct);
            LastRefresh = DateTimeOffset.Now;
            // 自适应退避的计数:整轮没有一行拉通才算一次失败
            _errorRounds = results.Count > 0 && results.All(r => r.Level == Levels.Error)
                ? _errorRounds + 1 : 0;
            _dq.TryEnqueue(() => _app.SetResults(results));
            return results;
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    /// <summary>
    /// 行级「立即刷新」(1.6.0):只拉一个 provider,合并进当前快照并触发一次
    /// 告警评估(语义与整轮一致)。与全局轮询各自单飞、互不取消;与整轮的
    /// 冲突按「后完成者胜」收敛,行刷新只动自己那一行。进行中重复点击忽略。
    /// </summary>
    public async Task<bool> RefreshProviderAsync(string name)
    {
        if (Interlocked.CompareExchange(ref _rowInFlight, 1, 0) != 0) return false;
        try
        {
            var entry = _app.FindProviderByName(name);
            if (entry is null) return false;
            var kind = entry.TryGetValue("kind", out var k) && k is string ks ? ks : "";
            var result = await ProviderRegistry.FetchOneAsync(kind, entry, _app.Cfg);
            List<ProviderResult> merged = [];
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            // 合并要在 UI 线程做:结果列表归 _app 所有,SetResults 只能在原线程发
            _dq.TryEnqueue(() =>
            {
                try
                {
                    var cur = _app.Results.ToList();
                    var idx = cur.FindIndex(r => r.Name == name);
                    if (idx >= 0) cur[idx] = result;
                    else cur.Add(result);
                    _app.SetResults(cur);
                    merged = cur;
                }
                finally { done.TrySetResult(); }
            });
            await done.Task;
            try
            {
                await AlertService.EvaluateAsync(_app, merged, CancellationToken.None);
            }
            catch
            {
                // 行刷新的告警评估失败不拦主流程(增强面契约)
            }
            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _rowInFlight, 0);
        }
    }

    public void Dispose() => Stop();
}
