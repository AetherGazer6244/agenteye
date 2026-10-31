using System.Text.Json;
using AgentEye.Core.Localization;

namespace AgentEye.Core.Providers;

/// <summary>
/// 通用 OpenAI 兼容 provider 适配器 + 模型探测(端口自 generic.py)。
/// </summary>
public static class GenericOpenAi
{
    private static readonly (string Label, string Path, Func<JsonElement, LegacyEntry, FetchPayload?> Parse)[]
        QuotaProbes =
        [
            ("openai_billing", "/dashboard/billing/credit_grants", ParseOpenaiBilling),
            ("openai_subscription", "/dashboard/billing/subscription", ParseOpenaiSubscription),
            ("deepseek_balance", "/user/balance", ParseDeepseekBalance),
            ("new_api_self", "/api/user/self", ParseNewApiSelf),
        ];

    /// <summary>legacy 形态的条目(等价 Python _to_legacy_entry 的产出)。</summary>
    public sealed record LegacyEntry(
        string? Name, string ApiKey, string BaseUrl,
        Dictionary<string, object?> Raw);

    public static async Task<FetchPayload> FetchAsync(
        LegacyEntry e, CancellationToken ct = default)
    {
        var bas = (e.BaseUrl ?? "").TrimEnd('/');
        var key = e.ApiKey ?? "";
        if (bas.Length == 0) return FetchPayload.Err(L.T("gen.noBaseUrl"), unconfigured: true);
        if (key.Length == 0) return FetchPayload.Err(L.T("gen.noKey"), unconfigured: true);

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {key}",
            ["Accept"] = "application/json",
        };
        var errors = new List<string>();
        FetchPayload? parsed = null;
        var quotaEndpoint = "";

        for (var i = 0; i < QuotaProbes.Length; i++)
        {
            var (label, path, parse) = QuotaProbes[i];
            var isLast = i + 1 >= QuotaProbes.Length;
            var (ok, status, body) = await Http.GetJsonAsync(
                bas + path, headers, Http.DefaultTimeoutSec, ct);
            if (!ok)
            {
                errors.Add(L.F("gen.labelNet", label));
                continue;
            }
            if (status == 401)
                // 鉴权层拒绝与路径无关,直接判 key 无效,不重试其它端点
                return FetchPayload.Err(L.F("pr.keyInvalid", 401));
            if (status == 403)
            {
                // 403 只在末位(已知正确的)端点上判 key 无效:nginx/WAF
                // 对猜测性的管理路径返回 403 很常见
                if (isLast) return FetchPayload.Err(L.F("pr.keyInvalid", 403));
                errors.Add($"{label}: HTTP 403");
                continue;
            }
            if (status != 200)
            {
                errors.Add($"{label}: HTTP {status}");
                continue;
            }
            if (!Http.IsObj(body))
            {
                errors.Add(L.F("gen.labelNotJson", label));
                continue;
            }
            var result = parse(body, e);
            if (result is not null)
            {
                result.QuotaEndpoint = path;
                quotaEndpoint = path;
                parsed = result;
                break;
            }
            errors.Add(L.F("gen.labelStruct", label));
        }

        if (parsed is null)
        {
            // billing 端点全不认识:配了 model 就发一次真实对话探测,
            // 至少能给出 key+model 是否可用(很多站不暴露 billing 端点)
            var model = e.Raw.TryGetValue("model", out var mObj) && mObj is string ms
                ? ms.Trim() : "";
            if (model.Length > 0)
            {
                var probe = await ProbeModelAsync(e, model, ct);
                if (probe is not null) return probe;
            }
            return FetchPayload.Err(string.Join("; ", errors.Take(3)));
        }

        var modelsResult = await FetchModelsAsync(e, ct);
        if (modelsResult.Error is null)
        {
            parsed.Models = modelsResult.Models!;
            parsed.ModelsCount = modelsResult.Models!.Count;
            // 同步进模型缓存:probe_models / 模型面板只认缓存
            try
            {
                Cache.CacheStore.Default.SetModels(bas, key, modelsResult.Models!);
            }
            catch (IOException)
            {
                // 只捕写盘的预期失败;编程错误要炸出来暴露
            }
        }

        var detail = parsed.Detail ?? "";
        parsed.Detail = $"[{quotaEndpoint}] {detail}".Trim();
        return parsed;
    }

    public sealed record ModelsOutcome(List<string>? Models, string? Error);

    public sealed record ModelProbeOutcome(bool Ok, string Message);

    /// <summary>
    /// 纯函数:模型探测响应分类(不发网络,单测直接打)。
    /// 200 = key+model 可用;401/403 = key 无效;404 = 端点或模型不存在。
    /// </summary>
    public static ModelProbeOutcome ClassifyModelProbe(bool netOk, int status) =>
        !netOk ? new ModelProbeOutcome(false, L.T("gen.probeNet"))
        : status == 200 ? new ModelProbeOutcome(true, L.T("gen.probeOk"))
        : status is 401 or 403 ? new ModelProbeOutcome(false, L.F("pr.keyInvalid", status))
        : status == 404 ? new ModelProbeOutcome(false, L.T("gen.probe404"))
        : new ModelProbeOutcome(false, L.F("gen.probeHttp", status));

    /// <summary>带 model 的真实对话探测;未配 model 返回 null(调用方走原错误)。</summary>
    public static async Task<FetchPayload?> ProbeModelAsync(
        LegacyEntry e, string model, CancellationToken ct = default)
    {
        var bas = (e.BaseUrl ?? "").TrimEnd('/');
        var url = bas.EndsWith("/v1") ? bas + "/chat/completions"
            : bas + "/v1/chat/completions";
        var (ok, status, _) = await Http.PostJsonAsync(url,
            new Dictionary<string, string> { ["Authorization"] = $"Bearer {e.ApiKey}" },
            new Dictionary<string, object?>
            {
                ["model"] = model,
                ["messages"] = new List<object?>
                {
                    new Dictionary<string, object?> { ["role"] = "user", ["content"] = "ping" },
                },
                ["max_tokens"] = 1,
                ["stream"] = false,
            }, Http.DefaultTimeoutSec, ct);
        var outcome = ClassifyModelProbe(ok, status);
        return outcome.Ok
            ? new FetchPayload { Detail = outcome.Message, IsEstimate = true }
            : FetchPayload.Err(outcome.Message);
    }

    public static async Task<ModelsOutcome> FetchModelsAsync(
        LegacyEntry e, CancellationToken ct = default)
    {
        var bas = (e.BaseUrl ?? "").TrimEnd('/');
        var key = e.ApiKey ?? "";
        if (bas.Length == 0) return new ModelsOutcome(null, L.T("gen.noBaseUrl"));
        if (key.Length == 0) return new ModelsOutcome(null, L.T("gen.noKey"));

        var modelsUrl = bas.EndsWith("/v1") ? bas + "/models" : bas + "/v1/models";
        var (ok, status, body) = await Http.GetJsonAsync(modelsUrl,
            new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {key}",
                ["Accept"] = "application/json",
            }, Http.DefaultTimeoutSec, ct);
        if (!ok) return new ModelsOutcome(null, L.T("pr.netErr"));
        if (status is 401 or 403) return new ModelsOutcome(null, L.F("pr.keyInvalid", status));
        if (status == 404) return new ModelsOutcome(null, L.T("gen.noModels"));
        if (status != 200) return new ModelsOutcome(null, $"HTTP {status}");

        JsonElement raw;
        var dataEl = Http.Obj(body, "data");
        if (dataEl is { } d)
            raw = d;
        else
            raw = body;
        if (raw.ValueKind != JsonValueKind.Array)
            return new ModelsOutcome(null, L.T("pr.structUnknown"));

        var models = new List<string>();
        foreach (var item in raw.EnumerateArray())
        {
            if (!Http.IsObj(item)) continue;
            var mid = Http.Str(item, "id") ?? Http.Str(item, "name") ?? Http.Str(item, "model");
            if (string.IsNullOrEmpty(mid)) continue;
            models.Add(mid!);
        }
        return new ModelsOutcome(models, null);
    }

    private static FetchPayload? ParseOpenaiBilling(JsonElement body, LegacyEntry entry)
    {
        if (!Http.IsObj(body)) return null;
        var granted = Http.Num(body, "total_granted") ?? Http.Num(body, "granted_amount") ?? 0;
        // one-api 系的 billing 端点字段是 total_used;个别站点会包装成
        // total_used_amount,两个都接受。
        var used = Http.Num(body, "total_used") ?? Http.Num(body, "total_used_amount") ?? 0;
        var available = Http.Num(body, "total_available") ?? 0;
        if (granted <= 0 && available <= 0) return null;
        return new FetchPayload
        {
            Remaining = available,
            Used = used,
            Total = granted,
            Unit = "$",
            Pct = granted > 0 ? available / granted * 100 : null,
            Detail = L.F("gen.detailUsedTotal", Http.F2(used), Http.F2(granted)),
            IsEstimate = false,
        };
    }

    private static FetchPayload? ParseOpenaiSubscription(JsonElement body, LegacyEntry entry)
    {
        if (!Http.IsObj(body)) return null;
        // 标准响应把字段包在 data 里;个别站点直接平铺在顶层,两种都接受
        var dataEl = Http.Obj(body, "data");
        var data = dataEl is { ValueKind: JsonValueKind.Object } ? dataEl.Value : body;
        if (Http.Num(data, "hard_limit_usd") is not { } limit) return null;
        if (limit <= 0) return null;
        var used = Http.Num(data, "usage") ?? 0;
        var remaining = Math.Max(0.0, limit - used);
        return new FetchPayload
        {
            Remaining = remaining,
            Used = used,
            Total = limit,
            Unit = "$",
            Pct = remaining / limit * 100,
            Detail = L.F("gen.detailUsedTotal", Http.F2(used), Http.F2(limit)),
            IsEstimate = false,
        };
    }

    private static FetchPayload? ParseDeepseekBalance(JsonElement body, LegacyEntry entry)
    {
        if (!Http.IsObj(body)) return null;
        var infos = Http.Arr(body, "balance_infos");
        if (infos is not { Count: > 0 }) return null;
        if (!Http.IsObj(infos[0])) return null;
        var info = infos[0];
        var currency = Http.Str(info, "currency") ?? "CNY";
        var sym = currency == "USD" ? "$" : currency == "CNY" ? "¥" : currency + " ";
        var total = Http.Num(info, "total_balance") ?? 0;
        var granted = Http.Num(info, "granted_balance") ?? 0;
        var topped = Http.Num(info, "topped_up_balance") ?? 0;
        return new FetchPayload
        {
            Remaining = total,
            Used = null,
            Total = null,
            Unit = sym,
            Pct = null,
            Detail = L.F("pr.detailGranted", sym, Http.F2(granted), sym, Http.F2(topped)),
            IsEstimate = false,
        };
    }

    private static FetchPayload? ParseNewApiSelf(JsonElement body, LegacyEntry entry)
    {
        if (!Http.IsObj(body)) return null;
        var dataEl = Http.Obj(body, "data");
        var data = dataEl is { ValueKind: JsonValueKind.Object } ? dataEl.Value : body;
        if (Http.Num(data, "quota") is not { } quota) return null;
        var used = Http.Num(data, "used_quota") ?? 0;
        // 用户配置的换算率优先(one-api 系响应里经常不给 quota_per_usd)
        var perUsd = entry.Raw.TryGetValue("quota_per_usd", out var qpu) && qpu is not null
            ? Convert.ToDouble(qpu)
            : Http.Num(data, "quota_per_usd") ?? 500000;
        var remaining = quota / perUsd;
        var usedUsd = used / perUsd;
        var total = remaining + usedUsd;
        return new FetchPayload
        {
            Remaining = remaining,
            Used = usedUsd,
            Total = total,
            Unit = "$",
            Pct = total > 0 ? remaining / total * 100 : null,
            Detail = L.F("pr.detailRemainUsd", Http.F2(remaining), Http.F2(usedUsd)),
            IsEstimate = false,
        };
    }
}
