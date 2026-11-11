using AgentEye.Core.Localization;

namespace AgentEye.Core.Providers;

/// <summary>
/// OpenRouter 额度:GET /api/v1/credits,Bearer key。
/// 返回 data.total_credits(充值总额)与 data.total_usage(已用),单位 $。
/// 剩余 = credits − usage;两者皆 0 视为免费账号,灰显(免费户 $0 常驻 critical
/// 没有意义)。无 pct,等级走 alert.critical_amount_usd 绝对额判定。
/// </summary>
public static class OpenRouterProvider
{
    private const string DefaultBase = "https://openrouter.ai";
    private const string Path = "/api/v1/credits";

    public static async Task<FetchPayload> FetchAsync(
        GenericOpenAi.LegacyEntry e, CancellationToken ct = default)
    {
        var key = e.ApiKey ?? "";
        if (key.Length == 0)
            return FetchPayload.Err(L.T("pr.noKey"), unconfigured: true);

        var bas = string.IsNullOrEmpty(e.BaseUrl) ? DefaultBase : e.BaseUrl.TrimEnd('/');
        var (ok, status, body, why) = await Http.GetJsonAsync(bas + Path,
            new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + key,
                ["Accept"] = "application/json",
            }, Http.DefaultTimeoutSec, ct);
        if (!ok) return FetchPayload.Err(Http.NetErrText(why));
        if (status is 401 or 403) return FetchPayload.Err(L.F("pr.keyInvalid", status));
        if (status != 200) return FetchPayload.Err($"HTTP {status}");
        if (!Http.IsObj(body)) return FetchPayload.Err(L.T("pr.notJson"));

        var data = Http.Obj(body, "data");
        if (data is null || data.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
            return FetchPayload.Err(L.T("pr.structUnknown"));

        return ParseBody(Http.Num(data.Value, "total_credits"),
                         Http.Num(data.Value, "total_usage"))
               ?? FetchPayload.Err(L.T("pr.structUnknown"));
    }

    /// <summary>纯解析(测试直喂):任一字段缺失 = 结构不识别;全 0 = 免费账号灰显。</summary>
    public static FetchPayload? ParseBody(double? credits, double? usage)
    {
        if (credits is null || usage is null) return null;
        var remaining = Math.Max(0.0, credits.Value - usage.Value);
        if (credits <= 0 && usage <= 0)
            return FetchPayload.Err(L.T("or.noBalance"), unconfigured: true);
        return new FetchPayload
        {
            Remaining = remaining, Used = usage, Total = credits,
            Unit = "$", Detail = L.F("gen.detailUsedTotal", Http.F2(usage.Value), Http.F2(credits.Value)),
            IsEstimate = false,
        };
    }
}
