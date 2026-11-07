using System.Globalization;
using System.Text.Json;
using AgentEye.Core.Localization;

namespace AgentEye.Core.Providers;

/// <summary>共享 HttpClient + 适配器用的 JSON/格式化小工具。</summary>
public static class Http
{
    public static readonly HttpClient Client = new(
        new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) })
    {
        Timeout = Timeout.InfiniteTimeSpan, // 超时按请求用 CTS 控制
    };

    public const int DefaultTimeoutSec = 12;

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

    /// <summary>
    /// GET 并解析 JSON。网络层异常返回 (ok:false, 0, default, 摘要);
    /// 摘要给「代理 X 拒绝连接」这类底层原因,调用方经 NetErrText 拼文案。
    /// </summary>
    public static async Task<(bool Ok, int Status, JsonElement Body, string? NetErr)>
        GetJsonAsync(
        string url, IReadOnlyDictionary<string, string>? headers, int timeoutSec,
        CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (headers is not null)
                foreach (var (k, v) in headers)
                    req.Headers.TryAddWithoutValidation(k, v);
            using var resp = await Client.SendAsync(req,
                HttpCompletionOption.ResponseContentRead, cts.Token);
            if (!resp.IsSuccessStatusCode)
                return (true, (int)resp.StatusCode, default, null);
            var text = await resp.Content.ReadAsStringAsync(cts.Token);
            try
            {
                var doc = JsonDocument.Parse(text);
                return (true, 200, doc.RootElement.Clone(), null);
            }
            catch (JsonException)
            {
                // 200 但响应非 JSON:让调用方按"非JSON"分支处理
                return (true, 200, JsonDocument.Parse("\"<not-json>\"").RootElement, null);
            }
        }
        catch (Exception e) when (
            e is HttpRequestException or TaskCanceledException
                or InvalidOperationException or SocketExceptionPlaceholder)
        {
            return (false, 0, default, Describe(e));
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
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            if (headers is not null)
                foreach (var (k, v) in headers)
                    req.Headers.TryAddWithoutValidation(k, v);
            req.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8, "application/json");
            using var resp = await Client.SendAsync(req,
                HttpCompletionOption.ResponseContentRead, cts.Token);
            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
                return (true, (int)resp.StatusCode, null, null);
            var text = await resp.Content.ReadAsStringAsync(cts.Token);
            try
            {
                return (true, 200, JsonDocument.Parse(text).RootElement.Clone(), null);
            }
            catch (JsonException)
            {
                return (true, 200, null, null);
            }
        }
        catch (Exception e) when (
            e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return (false, 0, null, Describe(e));
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
        if (v is null) return null;
        return v.Value.ValueKind switch
        {
            JsonValueKind.Number => v.Value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                v.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    public static List<JsonElement>? Arr(JsonElement el, string key)
    {
        var v = Obj(el, key);
        if (v is null || v.Value.ValueKind != JsonValueKind.Array) return null;
        return v.Value.EnumerateArray().ToList();
    }

    // ------------------------------------------------------------------
    // 数字格式化(不变文化,对齐 Python f-string 的 locale 无关语义)
    // ------------------------------------------------------------------

    public static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
    public static string F0(double v) => v.ToString("F0", CultureInfo.InvariantCulture);
    public static string N2(double v) => v.ToString("N2", CultureInfo.InvariantCulture);

    public static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
}
