using System.Text.RegularExpressions;
using AgentEye.Core.Localization;

namespace AgentEye.Core.Providers;

/// <summary>识别结果:detect(key, hintUrl) 的产出。</summary>
public sealed record DetectOutcome(string Kind, string BaseUrl, string Confidence, string Notes);

/// <summary>
/// Key 形态识别:根据 API key 前缀 + 可选 base_url 提示推断 provider 类型。
/// 端口自 Python 版 detect.py,分支顺序与文案逐条对齐。
/// </summary>
public static partial class Detect
{
    private static readonly (string Prefix, string Kind, string DefaultUrl, string Confidence, string NoteKey)[]
        KeyPrefixes =
        [
            ("sk-cp-", "minimax", "https://api.minimaxi.com", "high",
             "det.minimaxCp"),
        ];

    // "ey" 开头的三段式 JWT:MiniMax 的 token_plan 普通 key 和 OpenCode Go
    // 的 key 都是这种形态,光看形状分不出来。
    [GeneratedRegex(@"^ey[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$")]
    private static partial Regex JwtShape();

    private static readonly (string Marker, string Kind, string DefaultUrl, string NoteKey)[]
        UrlHostHints =
        [
            ("deepseek.com", "deepseek", "https://api.deepseek.com", "det.urlDeepseek"),
            ("bigmodel.cn", "zhipu", "https://open.bigmodel.cn", "det.urlZhipu"),
            ("minimaxi.com", "minimax", "https://api.minimaxi.com", "det.urlMinimax"),
            ("minimax.cn", "minimax", "https://api.minimaxi.com", "det.urlMinimaxCn"),
            ("opencode.ai", "opencode_go", "https://opencode.ai", "det.urlOc"),
            // 1.6.0 新增
            ("siliconflow", "siliconflow", "https://api.siliconflow.cn", "det.urlSiliconflow"),
            ("openrouter.ai", "openrouter", "https://openrouter.ai", "det.urlOpenrouter"),
        ];

    public static DetectOutcome Run(string key = "", string hintUrl = "")
    {
        key = (key ?? "").Trim();
        hintUrl = (hintUrl ?? "").Trim().TrimEnd('/');

        if (hintUrl.Length > 0)
        {
            var host = Regex.Replace(hintUrl, @"^https?://", "").ToLowerInvariant();
            foreach (var (marker, kind, defaultUrl, noteKey) in UrlHostHints)
            {
                if (host.Contains(marker, StringComparison.Ordinal))
                    return new DetectOutcome(kind, defaultUrl, "high", L.T(noteKey));
            }
            if (hintUrl.EndsWith("/v1", StringComparison.Ordinal))
                return new DetectOutcome("generic_openai", hintUrl, "medium",
                    L.T("det.urlV1"));
            return new DetectOutcome("generic_openai", hintUrl, "low",
                L.T("det.fallbackGeneric"));
        }

        if (key.Length == 0)
            return new DetectOutcome("generic_openai", "", "low",
                L.T("det.needMore"));

        foreach (var (prefix, kind, defaultUrl, confidence, noteKey) in KeyPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                return new DetectOutcome(kind, defaultUrl, confidence, L.T(noteKey));
        }

        if (JwtShape().IsMatch(key))
        {
            // 两种 JWT 分不出谁是谁:给 opencode_go 默认 URL 但置信度降为
            // low,让用户核对。
            return new DetectOutcome("opencode_go", "https://opencode.ai", "low",
                L.T("det.jwtAmbiguous"));
        }

        if (key.StartsWith("sk-", StringComparison.Ordinal))
            return new DetectOutcome("generic_openai", "", "low",
                L.T("det.skNeedsUrl"));

        return new DetectOutcome("generic_openai", "", "low",
            L.F("det.unknownKeyShape", key.Length));
    }
}
