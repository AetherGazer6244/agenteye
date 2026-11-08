using System.Security.Cryptography;
using AgentEye.Core.Localization;
using AgentEye.Core.Providers;

namespace AgentEye.Core.Update;

/// <summary>
/// 安装包下载与落地:流式下载(带进度)→ sha256 校验。
/// 下载产物是 Inno Setup 安装器(AgentEye_Setup.exe),装与重启由它负责,
/// 这里不再解包(zip 相关逻辑随 robocopy 方案一起废弃)。
/// </summary>
public static class UpdateDownloader
{
    /// <summary>下载文件到本地,返回落盘路径;失败抛异常(调用方负责降级)。</summary>
    public static async Task<string> DownloadAsync(
        string url, string destFile, IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(10));
        using var resp = await GetResponseAsync(url, cts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new IOException(L.F("dl.http", (int)resp.StatusCode));
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(cts.Token);
        await using var dst = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.None);
        var buf = new byte[81920];
        long done = 0;
        int read;
        while ((read = await src.ReadAsync(buf, cts.Token)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read), cts.Token);
            done += read;
            progress?.Report(done);
        }
        await dst.FlushAsync(cts.Token);
        dst.Flush(flushToDisk: true);
        if (total > 0 && done != total)
            throw new IOException(L.F("dl.incomplete", done, total));
        return destFile;
    }

    /// <summary>
    /// 下载响应:选路语义与 Http.SendAsync 一致——Auto 模式下系统代理网络层失败
    /// 就换直连重试一次(面板直连能刷出来,下载器也得跟着直连,不然更新卡死在
    /// 死代理上);suspect 窗口内先直连。状态记忆不用维护:轮询每轮都在刷。
    /// </summary>
    private static async Task<HttpResponseMessage> GetResponseAsync(
        string url, CancellationToken ct)
    {
        var firstIsDirect = Http.Mode switch
        {
            ProxyMode.Direct => true,
            ProxyMode.System => false,
            _ => Http.Status.Suspect,
        };
        var first = firstIsDirect ? Http.DirectClient : Http.Client;
        try
        {
            return await first.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception e) when (
            Http.Mode == ProxyMode.Auto &&
            e is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            var fb = firstIsDirect ? Http.Client : Http.DirectClient;
            return await fb.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        }
    }

    /// <summary>文件 sha256(小写十六进制)。</summary>
    public static string ComputeSha256(string file)
    {
        using var fs = File.OpenRead(file);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    /// <summary>校验 sha256;期望值缺失时返回 true(跳过校验)。</summary>
    public static bool VerifySha256(string file, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex)) return true;
        var actual = ComputeSha256(file);
        return string.Equals(actual, expectedHex.Trim().ToLowerInvariant(),
            StringComparison.Ordinal);
    }
}
