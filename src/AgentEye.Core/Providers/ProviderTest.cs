using AgentEye.Core.Localization;

namespace AgentEye.Core.Providers;

/// <summary>
/// 表单「测试」编排:用表单当前值跑一次真实拉取,不落盘、不改配置。
/// 供设置页在保存前验证 key;顺带回带模型列表(UI 填下拉用)。
/// 适配器对空 url/key 都会短路返回错误,不会真的打出网络请求。
/// </summary>
public static class ProviderTest
{
    public sealed record Outcome(bool Ok, string Message, IReadOnlyList<string> Models);

    public static async Task<Outcome> RunAsync(
        string kind, string baseUrl, string key, string model,
        CancellationToken ct = default)
    {
        var raw = new Dictionary<string, object?>();
        if (model.Length > 0) raw["model"] = model;
        var entry = new GenericOpenAi.LegacyEntry(
            Name: null, ApiKey: key, BaseUrl: baseUrl.TrimEnd('/'), Raw: raw);
        try
        {
            var payload = kind switch
            {
                "deepseek" => await DeepSeekProvider.FetchAsync(entry, ct),
                "zhipu" => await ZhipuProvider.FetchAsync(entry, ct),
                "minimax" => await MiniMaxProvider.FetchAsync(entry, ct),
                "opencode_go" => await OpenCodeGoProvider.FetchAsync(entry, ct),
                "relay" => await RelayProvider.FetchAsync(entry, ct),
                "siliconflow" => await SiliconFlowProvider.FetchAsync(entry, ct),
                "openrouter" => await OpenRouterProvider.FetchAsync(entry, ct),
                _ => await GenericOpenAi.FetchAsync(entry, ct),
            };
            if (payload.Error is not null)
                return new Outcome(false, payload.Error, []);
            var detail = payload.Detail is { Length: > 0 } d ? d : L.T("pt.ok");
            return new Outcome(true, detail, payload.Models ?? []);
        }
        catch (Exception e)
        {
            return new Outcome(false, L.F("pt.err", e.Message), []);
        }
    }
}
