using AgentEye.Core.Localization;

namespace AgentEye.Core.Providers;

/// <summary>
/// SiliconFlow(硅基流动)余额:GET /v1/user/info,Bearer key。
/// 返回 data.balance(可用余额,¥ 字符串)与 data.totalBalance(总资产,含赠金)。
/// 面板主值用 balance(真实可花),明细附总资产;两者皆 0 视为免费账号,灰显。
/// </summary>
public static class SiliconFlowProvider
{
    private const string DefaultBase = "https://api.siliconflow.cn";
    private const string Path = "/v1/user/info";

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

        // 个别网关把 code 回成字符串,与 Zhipu 同款防御:数字/字符串都能比
        var code = Http.Num(body, "code");
        if (code is { } c && (int)c != 200)
            return FetchPayload.Err(L.F("pr.apiError",
                Http.Str(body, "message") ?? Http.Str(body, "msg") ?? $"{c}"));

        var data = Http.Obj(body, "data");
        if (data is null || data.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
            return FetchPayload.Err(L.T("pr.structUnknown"));

        // balance/totalBalance 都是字符串数字,Http.Num 兼容两种形态
        return ParseBody(Http.Num(data.Value, "balance"),
                         Http.Num(data.Value, "totalBalance"))
               ?? FetchPayload.Err(L.T("pr.structUnknown"));
    }

    /// <summary>纯解析(测试直喂):balance 缺失 = 结构不识别;全 0 = 免费账号灰显。</summary>
    public static FetchPayload? ParseBody(double? balance, double? totalBalance)
    {
        if (balance is null) return null;
        if (balance <= 0 && totalBalance is null or <= 0)
            return FetchPayload.Err(L.T("sf.noBalance"), unconfigured: true);
        var detail = totalBalance is { } t
            ? L.F("pr.detailBalance", Http.F2(balance.Value), Http.F2(t))
            : L.F("pr.detailBalance", Http.F2(balance.Value), "?");
        return new FetchPayload
        {
            Remaining = balance, Used = null, Total = totalBalance,
            Unit = "¥", Detail = detail, IsEstimate = false,
        };
    }
}
