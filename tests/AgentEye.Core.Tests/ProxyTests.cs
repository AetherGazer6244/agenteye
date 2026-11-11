using System.Net;
using System.Net.Sockets;
using System.Text;
using AgentEye.Core.Localization;
using AgentEye.Core.Providers;
using Xunit;

namespace AgentEye.Core.Tests;

/// <summary>
/// 代理选路(1.5.2):默认直连,连不上才回退系统代理。契约:
/// - ParseMode 未知/空一律 Direct(缺省),手改 config 写错值不至于被死代理拖死;
/// - Direct:直连优先,直连网络层失败才走代理;首选失败要记 suspect(下轮换序),
///   被怀疑的路走通要清除;双路皆败文案按尝试顺序点名;
/// - Auto:代理优先,失败才直连(覆盖只有代理能通的海外中转站);
/// - System:只走代理,不兜底、不记 suspect。
///
/// 测试注记:.NET 8 的 HttpClient.DefaultProxy 是进程级一次性快照,handler 启动
/// 后再换 DefaultProxy 不改路由——所以这里直接换 Http.Client 为「强制走指定
/// 代理」的替身(handler 级显式 Proxy,路由确定),同时把 DefaultProxy 换成
/// 同一台代理让 EffectiveProxy/文案/标记路径一并走通。全局静态全部 try/finally
/// 还原;与 NetErrorTextTests 同 collection 串行。
/// </summary>
[Collection("http-global")]
public class ProxyTests
{
    /// <summary>本机必拒端口:连接即时被拒,不耗时。</summary>
    private static readonly Uri DeadProxyUri = new("http://127.0.0.1:1/");

    // ------------------------------------------------------------------
    // ParseMode(纯函数)
    // ------------------------------------------------------------------

    [Fact]
    public void ParseMode_Unknown_Or_Empty_Falls_Back_To_Direct()
    {
        Assert.Equal(ProxyMode.Direct, Http.ParseMode(null));
        Assert.Equal(ProxyMode.Direct, Http.ParseMode("")); // 缺省:直连优先
        Assert.Equal(ProxyMode.Direct, Http.ParseMode("DIRECT"));
        Assert.Equal(ProxyMode.Direct, Http.ParseMode("bogus"));
    }

    [Fact]
    public void ParseMode_Known_Values_Map_Exact()
    {
        Assert.Equal(ProxyMode.Auto, Http.ParseMode("auto"));
        Assert.Equal(ProxyMode.System, Http.ParseMode("system"));
        Assert.Equal(ProxyMode.Direct, Http.ParseMode("direct"));
    }

    // ------------------------------------------------------------------
    // Direct(缺省):直连活 → 代理完全不参与,suspect 保持干净
    // ------------------------------------------------------------------

    [Fact]
    public async Task Direct_LiveTarget_Never_Touches_Proxy()
    {
        L.Init("zh-Hans");
        using var server = new StubJsonServer();
        var restore = UseProxyClient(ProxyMode.Direct, DeadProxyUri);
        try
        {
            var (ok, status, body, why) = await Http.GetJsonAsync(
                $"http://127.0.0.1:{server.Port}/user/balance", null, Http.DefaultTimeoutSec);
            Assert.True(ok, $"直连优先不该碰死代理: {why}");
            Assert.Equal(200, status);
            Assert.True(Http.IsObj(body)); // stub 回 {}
            Assert.Null(Http.Status.Suspect);
        }
        finally
        {
            restore();
        }
    }

    // ------------------------------------------------------------------
    // Direct:直连死、代理活 → 回退救回 + 记 suspect(直连),下轮代理优先
    // ------------------------------------------------------------------

    [Fact]
    public async Task Direct_DeadTarget_LiveProxy_Falls_Back_And_Marks_Suspect()
    {
        L.Init("zh-Hans");
        using var server = new StubJsonServer(); // stub 兼任假代理:回 200 {},不看请求行
        var restore = UseProxyClient(
            ProxyMode.Direct, new Uri($"http://127.0.0.1:{server.Port}/"));
        try
        {
            var (ok, status, _, why) = await Http.GetJsonAsync(
                "http://127.0.0.1:2/x", null, Http.DefaultTimeoutSec);
            Assert.True(ok, $"代理回退应救回请求: {why}");
            Assert.Equal(200, status);
            Assert.Equal(NetRoute.Direct, Http.Status.Suspect); // 直连被判降权
            Assert.Null(Http.Status.Proxy); // 直连路线失败,不记代理地址
            Assert.Equal(NetRoute.Proxy, Http.FirstRoute); // 下轮代理优先
        }
        finally
        {
            restore();
        }
    }

    // ------------------------------------------------------------------
    // Direct:双路皆败 → 文案按尝试顺序,直连段在前、代理段点名地址
    // ------------------------------------------------------------------

    [Fact]
    public async Task Direct_Both_Dead_Text_Leads_With_Direct()
    {
        L.Init("zh-Hans");
        var restore = UseProxyClient(ProxyMode.Direct, DeadProxyUri);
        try
        {
            var (ok, _, _, why) = await Http.GetJsonAsync(
                "http://127.0.0.1:2/", null, Http.DefaultTimeoutSec);
            Assert.False(ok);
            Assert.NotNull(why);
            Assert.StartsWith("直连失败:", why); // 尝试顺序:直连在前
            Assert.Contains("系统代理", why);
            Assert.Contains("127.0.0.1:1", why); // 代理段点名地址
            // 经适配器的 NetErrText 拼完仍带「网络错误:」前缀(错误行文案契约)
            Assert.StartsWith("网络错误:", Http.NetErrText(why));
            Assert.Equal(NetRoute.Direct, Http.Status.Suspect); // 首选失败进窗口
        }
        finally
        {
            restore();
        }
    }

    // ------------------------------------------------------------------
    // Auto:代理优先;代理死、直连活 → 救回 + 记 suspect(代理),下轮直连优先
    // ------------------------------------------------------------------

    [Fact]
    public async Task Auto_DeadProxy_LiveDirect_Fetches_And_Marks_Suspect()
    {
        L.Init("zh-Hans");
        using var server = new StubJsonServer();
        var restore = UseProxyClient(ProxyMode.Auto, DeadProxyUri);
        try
        {
            var (ok, status, body, why) = await Http.GetJsonAsync(
                $"http://127.0.0.1:{server.Port}/user/balance", null, Http.DefaultTimeoutSec);
            Assert.True(ok, $"直连兜底应救回请求: {why}");
            Assert.Equal(200, status);
            Assert.True(Http.IsObj(body));
            Assert.Equal(NetRoute.Proxy, Http.Status.Suspect);
            Assert.Equal("127.0.0.1:1", Http.Status.Proxy!.Authority);
            Assert.Equal(NetRoute.Direct, Http.FirstRoute); // 下轮直连优先
        }
        finally
        {
            restore();
        }
    }

    // ------------------------------------------------------------------
    // Auto:双路皆败 → 文案代理段在前
    // ------------------------------------------------------------------

    [Fact]
    public async Task Auto_Both_Dead_Text_Leads_With_Proxy()
    {
        L.Init("zh-Hans");
        var restore = UseProxyClient(ProxyMode.Auto, DeadProxyUri);
        try
        {
            var (ok, _, _, why) = await Http.GetJsonAsync(
                "http://127.0.0.1:2/", null, Http.DefaultTimeoutSec);
            Assert.False(ok);
            Assert.NotNull(why);
            Assert.StartsWith("系统代理 127.0.0.1:1 无响应(", why);
            Assert.Contains("直连失败:", why);
            Assert.Equal(NetRoute.Proxy, Http.Status.Suspect);
        }
        finally
        {
            restore();
        }
    }

    // ------------------------------------------------------------------
    // System:只走代理,不兜底、不记 suspect;拒连不能被误判成超时
    // ------------------------------------------------------------------

    [Fact]
    public async Task System_Mode_Does_Not_Fallback_Or_Mark_Suspect()
    {
        L.Init("zh-Hans");
        var restore = UseProxyClient(ProxyMode.System, DeadProxyUri);
        try
        {
            var (ok, _, _, why) = await Http.GetJsonAsync(
                "http://127.0.0.1:2/", null, Http.DefaultTimeoutSec);
            Assert.False(ok);
            Assert.NotNull(why);
            Assert.Contains("系统代理 127.0.0.1:1 无响应(", why);
            Assert.DoesNotContain("请求超时", why);
            Assert.Null(Http.Status.Suspect);
        }
        finally
        {
            restore();
        }
    }

    // ------------------------------------------------------------------
    // 根因分类:拒连给固定短语(拼进「系统代理 X 无响应(原因)」)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Classify_Refused_Comes_Out_As_Short_Reason()
    {
        L.Init("zh-Hans");
        var restore = UseProxyClient(ProxyMode.System, DeadProxyUri);
        try
        {
            var (_, _, _, why) = await Http.GetJsonAsync(
                "http://127.0.0.1:2/", null, Http.DefaultTimeoutSec);
            Assert.NotNull(why);
            Assert.Contains("连接被拒绝", why);
        }
        finally
        {
            restore();
        }
    }

    // ------------------------------------------------------------------
    // 测试脚手架
    // ------------------------------------------------------------------

    /// <summary>
    /// 把 Http.Client 换成「强制走指定代理」的替身(handler 级显式 Proxy,路由
    /// 确定,不赌 DefaultProxy 的快照时机),DefaultProxy 也指过去让
    /// EffectiveProxy/文案/标记路径看得到同一台代理。返回还原委托。
    /// </summary>
    private static Action UseProxyClient(ProxyMode mode, Uri proxyUri)
    {
        var origClient = Http.Client;
        var origProxy = HttpClient.DefaultProxy;
        var origMode = Http.Mode;
        var px = new WebProxy(proxyUri);
        Http.Client = new HttpClient(
            new SocketsHttpHandler { UseProxy = true, Proxy = px })
        { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        HttpClient.DefaultProxy = px;
        Http.Mode = mode;
        Http.ResetProxyState();
        return () =>
        {
            Http.Client = origClient;
            HttpClient.DefaultProxy = origProxy;
            Http.Mode = origMode;
            Http.ResetProxyState();
        };
    }

    /// <summary>
    /// 本地 TCP stub:对每个请求回 200 + "{}"。用裸 TcpListener 而不是
    /// HttpListener——后者在本机非管理员下要 URLACL,CI 上容易翻车。
    /// 兼任假代理:正向代理收到的请求行是绝对 URI,stub 不看内容照回,
    /// 所以同一个服务既能当直连目标也能当替身代理。
    /// </summary>
    private sealed class StubJsonServer : IDisposable
    {
        private readonly TcpListener _listener;

        public int Port { get; }

        public StubJsonServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync();
                    using var stream = client.GetStream();
                    // 读到请求头结束(\r\n\r\n)再回,维持 HTTP 语义
                    var buf = new byte[4096];
                    var total = 0;
                    var done = false;
                    while (!done && total < buf.Length)
                    {
                        var n = await stream.ReadAsync(buf.AsMemory(total));
                        if (n <= 0) break;
                        total += n;
                        done = Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n");
                    }
                    var body = "{}";
                    var head = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
                               $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n" +
                               "Connection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(head + body));
                }
            }
            catch
            {
                // Dispose 后 Accept 会炸:测试服务器,静默收场
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { /* 已停就随它去 */ }
        }
    }
}
