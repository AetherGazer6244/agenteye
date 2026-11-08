using System.Globalization;
using System.Text.Json;
using AgentEye.Core.Localization;

namespace AgentEye.Core.Providers;

/// <summary>代理模式(ui.proxy_mode):Auto=跟随系统代理并带直连兜底(默认)。</summary>
public enum ProxyMode
{
    /// <summary>跟随系统代理;代理拒连/超时自动改直连重试一次(默认)。</summary>
    Auto,
    /// <summary>严格走系统代理,不做兜底。</summary>
    System,
    /// <summary>忽略系统代理,一律直连。</summary>
    Direct,
}

/// <summary>共享 HttpClient + 适配器用的 JSON/格式化小工具。</summary>
public static class Http
{
    /// <summary>系统代理客户端(共享)。属性可写是给测试换替身留的缝,产品代码别动。</summary>
    public static HttpClient Client { get; set; } = CreateClient(useProxy: true);

    /// <summary>直连客户端(无视系统代理):Auto 模式兜底与 Direct 模式专用。</summary>
    public static HttpClient DirectClient { get; set; } = CreateClient(useProxy: false);

    private static HttpClient CreateClient(bool useProxy) => new(
        new SocketsHttpHandler { UseProxy = useProxy, PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
    {
        Timeout = Timeout.InfiniteTimeSpan, // 超时按请求用 CTS 控制
    };

    public const int DefaultTimeoutSec = 12;

    // ------------------------------------------------------------------
    // 代理模式与「代理疑似不可用」状态
    // ------------------------------------------------------------------

    /// <summary>当前代理模式。启动时由壳层按 ui.proxy_mode 注入;设置页切换即时生效。</summary>
    public static ProxyMode Mode { get; set; } = ProxyMode.Auto;

    /// <summary>文本 → 模式;未知/空一律 Auto(手改 config 写错值不至于断网)。</summary>
    public static ProxyMode ParseMode(string? raw) => raw switch
    {
        "system" => ProxyMode.System,
        "direct" => ProxyMode.Direct,
        _ => ProxyMode.Auto,
    };

    /// <summary>
    /// 代理「疑似不可用」窗口:判死后窗口内请求先直连,省掉每轮一次注定失败的
    /// 系统代理重试(代理挂起而非拒连时一次就是整个超时);过期自动复探系统代理,
    /// 代理恢复即自动切回。
    /// </summary>
    private static readonly TimeSpan SuspectTtl = TimeSpan.FromMinutes(10);
    private static readonly object Gate = new();
    private static Uri? _suspectProxy;
    private static string? _suspectReason;
    private static DateTimeOffset _suspectUntil = DateTimeOffset.MinValue;

    /// <summary>代理状态快照(总览横幅用):Proxy=判死的代理地址,Suspect=兜底直连生效中。</summary>
    public sealed record ProxyState(Uri? Proxy, bool Suspect, string? Reason);

    public static ProxyState Status
    {
        get
        {
            lock (Gate)
            {
                var active = DateTimeOffset.Now < _suspectUntil;
                return new ProxyState(
                    active ? _suspectProxy : null, active, active ? _suspectReason : null);
            }
        }
    }

    private static void MarkSuspect(Uri target, Exception? err)
    {
        // 代理地址拿不到就不记:横幅没处显示;此时兜底决策退化为「每轮先试系统代理」
        var proxy = EffectiveProxy(target);
        if (proxy is null) return;
        lock (Gate)
        {
            _suspectProxy = proxy;
            _suspectReason = err is null ? null : Describe(err);
            _suspectUntil = DateTimeOffset.Now + SuspectTtl;
        }
    }

    private static void ClearSuspect()
    {
        lock (Gate) _suspectUntil = DateTimeOffset.MinValue;
    }

    /// <summary>清掉「代理疑似不可用」记忆。设置页切代理模式后调,新模式立刻干净生效。</summary>
    public static void ResetProxyState() => ClearSuspect();

    /// <summary>
    /// 目标 URL 当前会经哪个代理:问 .NET(HttpClient.DefaultProxy),env 变量 →
    /// WinINET → PAC 的规则全跟它走,不自己读注册表。无代理(未配 / 命中 bypass /
    /// GetProxy 原样返回目标)返回 null。
    /// </summary>
    public static Uri? EffectiveProxy(Uri target)
    {
        try
        {
            var p = HttpClient.DefaultProxy;
            if (p.IsBypassed(target)) return null;
            var u = p.GetProxy(target);
            if (u is null) return null;
            return u.Host == target.Host && u.Port == target.Port ? null : u;
        }
        catch
        {
            return null;
        }
    }

    // ---------------- 网络错误摘要(错误行 detail 带底层原因) ----------------

    /// <summary>网络失败文案:有摘要时带原因(如「代理 127.0.0.1:9652 拒绝连接」)。</summary>
    public static string NetErrText(string? why) => why is { Length: > 0 }
        ? L.F("pr.netErrWhy", why)
        : L.T("pr.netErr");

    /// <summary>在既有失败文案(probe 模板带端点 label)后追加原因。无摘要时原样返回。</summary>
    public static string WithReason(string baseText, string? why) =>
        why is { Length: > 0 } ? $"{baseText}: {why}" : baseText;

    /// <summary>异常摘要:超时给专门短语;其余取异常消息、压空白、截 100 字符。
    /// 摘要随返回值元组传给调用方——AsyncLocal 写入不会向上传播到 await
    /// 调用方的上下文(实测踩坑),静态字段在并发拉取下会串线,都不能用。</summary>
    private static string Describe(Exception e)
    {
        if (e is TaskCanceledException) return L.T("pr.netTimeoutWhy");
        var msg = e.Message.Replace('\n', ' ').Replace('\r', ' ');
        while (msg.Contains("  ")) msg = msg.Replace("  ", " ");
        msg = msg.Trim();
        return msg.Length > 100 ? msg[..97] + "..." : msg;
    }

    /// <summary>异常短因:拒连/DNS/不可达给固定短语,拼进「系统代理 X 无响应(原因)」。</summary>
    private static string Classify(Exception e)
    {
        if (e is TaskCanceledException) return L.T("pr.netTimeoutWhy");
        for (var ex = e; ex is not null; ex = ex.InnerException)
        {
            if (ex is System.Net.Sockets.SocketException se)
            {
                return se.SocketErrorCode switch
                {
                    System.Net.Sockets.SocketError.ConnectionRefused => L.T("net.reason.refused"),
                    System.Net.Sockets.SocketError.HostNotFound => L.T("net.reason.dns"),
                    System.Net.Sockets.SocketError.TimedOut => L.T("pr.netTimeoutWhy"),
                    System.Net.Sockets.SocketError.NetworkUnreachable or
                        System.Net.Sockets.SocketError.HostUnreachable =>
                            L.T("net.reason.unreachable"),
                    _ => Describe(e),
                };
            }
        }
        return Describe(e);
    }

    /// <summary>单路失败文案:能确定代理地址时点明「系统代理 X 无响应」,否则回退原始摘要。</summary>
    private static string SingleErrText(Uri target, Exception? err)
    {
        if (err is null) return L.T("pr.netErr");
        var proxy = EffectiveProxy(target);
        return proxy is null ? Describe(err) : L.F("net.proxyDead", proxy.Authority, Classify(err));
    }

    /// <summary>双路皆败:系统代理侧为主因(用户环境里代理多半是根因),直连失败附在后面。</summary>
    private static string BothErrText(Uri target, Exception? primary, Exception? fallback)
    {
        if (primary is null) return L.T("pr.netErr");
        var proxy = EffectiveProxy(target);
        if (proxy is null) return Describe(primary);
        return fallback is null
            ? L.F("net.proxyDead", proxy.Authority, Classify(primary))
            : L.F("net.proxyBothFailed", proxy.Authority, Classify(primary), Describe(fallback));
    }

    // ------------------------------------------------------------------
    // 请求出口:Get/Post 都走 SendAsync(带代理兜底)
    // ------------------------------------------------------------------

    /// <summary>单次请求,不兜底。网络层异常返回 (false,0,null,异常本身)。</summary>
    private static async Task<(bool Ok, int Status, string? Text, Exception? Err)>
        TryOnceAsync(HttpClient client, HttpMethod method, Uri target,
        IReadOnlyDictionary<string, string>? headers, string? jsonPayload,
        int timeoutSec, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            using var req = new HttpRequestMessage(method, target);
            if (headers is not null)
                foreach (var (k, v) in headers)
                    req.Headers.TryAddWithoutValidation(k, v);
            if (jsonPayload is not null)
                req.Content = new StringContent(jsonPayload,
                    System.Text.Encoding.UTF8, "application/json");
            using var resp = await client.SendAsync(req,
                HttpCompletionOption.ResponseContentRead, cts.Token);
            // 错误状态码不读 body(行为同旧版:调用方按状态分支处理,用不到错误页)
            var text = resp.IsSuccessStatusCode
                ? await resp.Content.ReadAsStringAsync(cts.Token)
                : null;
            return (true, (int)resp.StatusCode, text, null);
        }
        catch (Exception e) when (
            e is HttpRequestException or TaskCanceledException
                or InvalidOperationException or SocketExceptionPlaceholder)
        {
            return (false, 0, null, e);
        }
    }

    /// <summary>
    /// 统一请求出口(带代理兜底):
    /// - Auto:正常先走系统代理;网络层失败换直连重试一次,直连通了记 suspect
    ///   (窗口内后续请求先直连);系统代理这条道走通了就清掉。直连首发失败时
    ///   反向回试系统代理,覆盖「只有代理能通」的海外中转站场景。
    /// - System:只走系统代理;Direct:只直连。
    /// 网络失败文案能确定代理地址时点明「系统代理 X 无响应」,不再只给裸 .NET 消息。
    /// </summary>
    private static async Task<(bool Ok, int Status, string? Text, string? NetErr)>
        SendAsync(HttpMethod method, string url,
        IReadOnlyDictionary<string, string>? headers, string? jsonPayload,
        int timeoutSec, CancellationToken ct)
    {
        var target = new Uri(url);
        var mode = Mode; // 快照:请求中途切设置不影响本轮
        var firstIsDirect = mode switch
        {
            ProxyMode.Direct => true,
            ProxyMode.System => false,
            _ => Status.Suspect, // Auto:代理疑似挂了先直连
        };
        var first = firstIsDirect ? DirectClient : Client;
        var fallback = mode == ProxyMode.Auto
            ? (firstIsDirect ? Client : DirectClient) : null;

        var (ok, status, text, err) =
            await TryOnceAsync(first, method, target, headers, jsonPayload, timeoutSec, ct);
        if (ok)
        {
            if (!firstIsDirect) ClearSuspect(); // 系统代理是通的 → 兜底状态该清了
            return (true, status, text, null);
        }
        if (fallback is null)
            return (false, 0, null, SingleErrText(target, err));

        var (ok2, status2, text2, err2) =
            await TryOnceAsync(fallback, method, target, headers, jsonPayload, timeoutSec, ct);
        if (ok2)
        {
            if (firstIsDirect) ClearSuspect(); // 直连不行、系统代理行 → 代理自己好了
            else MarkSuspect(target, err);      // 系统代理不行、直通行 → 判死代理进兜底窗口
            return (true, status2, text2, null);
        }
        if (firstIsDirect) ClearSuspect(); // 双路皆败且已先直连过:锅多半不在代理
        else MarkSuspect(target, err);
        // 主因取系统代理侧(有代理的环境里它才是根因),直连失败附在后面
        return (false, 0, null,
            BothErrText(target, firstIsDirect ? err2 : err, firstIsDirect ? err : err2));
    }

    /// <summary>
    /// GET 并解析 JSON。网络层异常返回 (ok:false, 0, default, 摘要);
    /// 摘要给「系统代理 127.0.0.1:9652 无响应」这类根因,调用方经 NetErrText 拼文案。
    /// </summary>
    public static async Task<(bool Ok, int Status, JsonElement Body, string? NetErr)>
        GetJsonAsync(
        string url, IReadOnlyDictionary<string, string>? headers, int timeoutSec,
        CancellationToken ct = default)
    {
        var (ok, status, text, netErr) =
            await SendAsync(HttpMethod.Get, url, headers, null, timeoutSec, ct);
        if (!ok) return (false, 0, default, netErr);
        if (status is < 200 or >= 300) return (true, status, default, null);
        try
        {
            var doc = JsonDocument.Parse(text ?? "");
            return (true, 200, doc.RootElement.Clone(), null);
        }
        catch (JsonException)
        {
            // 200 但响应非 JSON:让调用方按"非JSON"分支处理
            return (true, 200, JsonDocument.Parse("\"<not-json>\"").RootElement, null);
        }
    }

    /// <summary>内部占位:SocketException 在不同 TFM 上类型不同,统一按网络错。</summary>
    public sealed class SocketExceptionPlaceholder : Exception { }

    /// <summary>POST JSON 并解析(中转站登录)。网络层异常带摘要,同 GetJsonAsync。</summary>
    public static Task<(bool Ok, int Status, JsonElement? Body, string? NetErr)> PostJsonAsync(
        string url, object payload, int timeoutSec, CancellationToken ct = default) =>
        PostJsonAsync(url, null, payload, timeoutSec, ct);

    /// <summary>POST JSON 并解析,可带额外 headers(模型探测)。</summary>
    public static async Task<(bool Ok, int Status, JsonElement? Body, string? NetErr)>
        PostJsonAsync(
        string url, Dictionary<string, string>? headers, object payload,
        int timeoutSec, CancellationToken ct = default)
    {
        var (ok, status, text, netErr) = await SendAsync(HttpMethod.Post, url, headers,
            System.Text.Json.JsonSerializer.Serialize(payload), timeoutSec, ct);
        if (!ok) return (false, 0, null, netErr);
        if (status != 200) return (true, status, null, null);
        try
        {
            return (true, 200, JsonDocument.Parse(text ?? "").RootElement.Clone(), null);
        }
        catch (JsonException)
        {
            return (true, 200, null, null);
        }
    }

    // ------------------------------------------------------------------
    // JsonElement 取值小工具(带形状防御:手改响应/网关错误页都可能怪)
    // ------------------------------------------------------------------

    public static bool IsObj(JsonElement el) =>
        el.ValueKind == JsonValueKind.Object;

    public static JsonElement? Obj(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        return el.TryGetProperty(key, out var v) ? v : null;
    }

    public static string? Str(JsonElement el, string key)
    {
        var v = Obj(el, key);
        return v is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
    }

    public static double? Num(JsonElement el, string key)
    {
        var v = Obj(el, key);
        return v is null ? null : v.Value.ValueKind switch
        {
            JsonValueKind.Number => v.Value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                v.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    public static List<JsonElement>? Arr(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        return el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToList() : null;
    }

    // ------------------------------------------------------------------
    // 数字格式化(不变文化,对齐 Python f-string 的 locale 无关语义)
    // ------------------------------------------------------------------

    public static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
    public static string F0(double v) => v.ToString("F0", CultureInfo.InvariantCulture);
    public static string N2(double v) => v.ToString("N2", CultureInfo.InvariantCulture);

    public static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
}
