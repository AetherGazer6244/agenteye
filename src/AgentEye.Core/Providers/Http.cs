using System.Globalization;
using System.Text.Json;
using AgentEye.Core.Localization;

namespace AgentEye.Core.Providers;

/// <summary>请求路线:直连(绕开系统代理)或走系统代理。</summary>
public enum NetRoute
{
    /// <summary>直连,无视系统代理。</summary>
    Direct,
    /// <summary>走系统代理(env 变量 → WinINET → PAC 全由 .NET 决定)。</summary>
    Proxy,
}

/// <summary>代理模式(ui.proxy_mode):Direct=直连优先(缺省),Auto=代理优先,System=只代理。</summary>
public enum ProxyMode
{
    /// <summary>直连优先:先直连,网络层失败才回退系统代理(缺省,死代理彻底不参与)。</summary>
    Direct,
    /// <summary>跟随系统代理:先系统代理,失败才直连(覆盖只有代理能通的中转站)。</summary>
    Auto,
    /// <summary>严格跟随系统代理:只走代理,不做任何兜底。</summary>
    System,
}

/// <summary>共享 HttpClient + 适配器用的 JSON/格式化小工具。</summary>
public static class Http
{
    /// <summary>系统代理客户端(共享)。属性可写是给测试换替身留的缝,产品代码别动。</summary>
    public static HttpClient Client { get; set; } = CreateClient(useProxy: true);

    /// <summary>直连客户端(无视系统代理)。与系统代理 Client 并存,按路线顺序取用。</summary>
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
    public static ProxyMode Mode { get; set; } = ProxyMode.Direct;

    /// <summary>文本 → 模式;未知/空一律 Direct(手改 config 写错值不至于被死代理拖死)。</summary>
    public static ProxyMode ParseMode(string? raw) => raw switch
    {
        "auto" => ProxyMode.Auto,
        "system" => ProxyMode.System,
        _ => ProxyMode.Direct,
    };

    /// <summary>
    /// 「路线疑似不可用」记忆:刚在网络层失败的那条路记 10 分钟,窗口内把它挪到
    /// 尝试顺序末尾,省掉每轮一次注定失败的尝试(挂起而非拒连时一次就是整个
    /// 超时);该路走通即解除,过期自动复探。只记进程内,不出任何横幅/通知。
    /// </summary>
    private static readonly TimeSpan SuspectTtl = TimeSpan.FromMinutes(10);
    private static readonly object Gate = new();
    private static NetRoute? _suspectRoute;
    private static Uri? _suspectProxy;
    private static string? _suspectReason;
    private static DateTimeOffset _suspectUntil = DateTimeOffset.MinValue;

    /// <summary>路线状态快照:Suspect=被判降权的路线,Proxy=代理路线失败时的代理地址。</summary>
    public sealed record ProxyState(NetRoute? Suspect, Uri? Proxy, string? Reason);

    public static ProxyState Status
    {
        get
        {
            lock (Gate)
            {
                var active = DateTimeOffset.Now < _suspectUntil;
                return new ProxyState(
                    active ? _suspectRoute : null,
                    active ? _suspectProxy : null,
                    active ? _suspectReason : null);
            }
        }
    }

    /// <summary>
    /// 本轮尝试顺序:模式给基础顺序(Direct=直连优先 / Auto=代理优先 /
    /// System=只代理),刚失败过的路线挪到末尾。SendAsync 与下载器选路共用。
    /// </summary>
    public static NetRoute[] RouteOrder(ProxyMode mode)
    {
        if (mode == ProxyMode.System) return [NetRoute.Proxy];
        var directFirst = mode == ProxyMode.Direct;
        lock (Gate)
        {
            if (DateTimeOffset.Now < _suspectUntil && _suspectRoute is { } bad &&
                directFirst == (bad == NetRoute.Direct))
                directFirst = !directFirst; // 首位正是刚失败的路 → 换到次位
        }
        return directFirst ? [NetRoute.Direct, NetRoute.Proxy] : [NetRoute.Proxy, NetRoute.Direct];
    }

    /// <summary>当前首选路线(受 suspect 记忆影响)。下载器选第一跳用。</summary>
    public static NetRoute FirstRoute => RouteOrder(Mode)[0];

    private static void MarkSuspect(NetRoute route, Uri target, Exception? err)
    {
        // 代理路线失败时顺手记下代理地址(文案/诊断用),直连路线失败不记地址
        Uri? proxy = route == NetRoute.Proxy ? EffectiveProxy(target) : null;
        lock (Gate)
        {
            _suspectRoute = route;
            _suspectProxy = proxy;
            _suspectReason = err is null ? null : Describe(err);
            _suspectUntil = DateTimeOffset.Now + SuspectTtl;
        }
    }

    private static void ClearSuspect()
    {
        lock (Gate)
        {
            _suspectRoute = null;
            _suspectUntil = DateTimeOffset.MinValue;
        }
    }

    /// <summary>清掉路线记忆。设置页切代理模式后调,新模式立刻干净生效。</summary>
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

    /// <summary>
    /// 单条路线的失败文案:代理路线点名代理地址(能查到时)配短因;直连路线给
    /// 「直连失败:原始摘要」——摘要自带目标地址,比压短后的固定短语更有用。
    /// </summary>
    private static string RouteErrText(NetRoute route, Uri target, Exception err)
    {
        if (route == NetRoute.Direct) return L.F("net.directFailed", Describe(err));
        var proxy = EffectiveProxy(target);
        return proxy is null ? Describe(err) : L.F("net.proxyDead", proxy.Authority, Classify(err));
    }

    /// <summary>全部路线皆败:按尝试顺序把每条路的原因拼起来(首选路的排前面)。</summary>
    private static string AllFailedText(NetRoute[] order, Uri target, Exception[] errs)
    {
        var parts = new string[order.Length];
        for (var i = 0; i < order.Length; i++)
            parts[i] = RouteErrText(order[i], target, errs[i]);
        return string.Join(";", parts);
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
    /// 统一请求出口(按路线顺序尝试):
    /// - Direct(缺省):直连优先,网络层失败才走系统代理;
    /// - Auto:代理优先,失败才直连(覆盖只有代理能通的海外中转站);
    /// - System:只走代理。
    /// 首选路失败即记入 suspect 窗口(下轮把它挪到末尾,省掉注定失败的尝试);
    /// 被怀疑的路走通了就解除。全败文案按尝试顺序点名每条路的原因。
    /// </summary>
    private static async Task<(bool Ok, int Status, string? Text, string? NetErr)>
        SendAsync(HttpMethod method, string url,
        IReadOnlyDictionary<string, string>? headers, string? jsonPayload,
        int timeoutSec, CancellationToken ct)
    {
        var target = new Uri(url);
        var order = RouteOrder(Mode); // 快照:请求中途切设置不影响本轮
        var errs = new Exception[order.Length];
        for (var i = 0; i < order.Length; i++)
        {
            var route = order[i];
            var wasSuspect = route == Status.Suspect;
            var (ok, status, text, err) = await TryOnceAsync(
                route == NetRoute.Direct ? DirectClient : Client,
                method, target, headers, jsonPayload, timeoutSec, ct);
            if (ok)
            {
                if (wasSuspect) ClearSuspect(); // 被怀疑的路走通了 → 解除,回基础顺序
                return (true, status, text, null);
            }
            errs[i] = err!;
            if (i == 0 && order.Length > 1)
                MarkSuspect(route, target, err); // 首选路挂了:下轮把它挪到末尾
        }
        return (false, 0, null, AllFailedText(order, target, errs));
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
