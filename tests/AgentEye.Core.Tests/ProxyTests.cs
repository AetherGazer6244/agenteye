using System.Net;
using System.Net.Sockets;
using System.Text;
using AgentEye.Core.Localization;
using AgentEye.Core.Providers;
using Xunit;

namespace AgentEye.Core.Tests;

/// <summary>
/// 代理容错(1.5.2):死系统代理把所有 provider 打死是真实场景(代理软件退出、
/// 系统代理残留)。契约:
/// - ParseMode 未知/空一律 Auto,手改 config 写错值不至于断网;
/// - Auto 模式系统代理拒连时自动直连兜底,救回来要记 suspect(总览横幅靠它),
///   系统代理走通了要清掉;双路皆败文案点明「系统代理 X 无响应」;
/// - System 模式不兜底、不记 suspect;Direct 模式无视代理。
///
/// 测试注记:.NET 8 的 HttpClient.DefaultProxy 是进程级一次性快照,handler 启动
/// 后再换 DefaultProxy 不改路由——所以这里直接换 Http.Client 为「指着必拒端口」
/// 的替身(handler 级显式 Proxy,路由确定),同时把 DefaultProxy 换成同一台
/// 代理让 EffectiveProxy/文案/标记路径一并走通。全局静态全部 try/finally 还原;
/// 与 NetErrorTextTests 同 collection 串行。
/// </summary>
[Collection("http-global")]
public class ProxyTests
{
    // ------------------------------------------------------------------
    // ParseMode(纯函数)
    // ------------------------------------------------------------------

    [Fact]
    public void ParseMode_Unknown_Or_Empty_Falls_Back_To_Auto()
    {
        Assert.Equal(ProxyMode.Auto, Http.ParseMode(null));
        Assert.Equal(ProxyMode.Auto, Http.ParseMode(""));
        Assert.Equal(ProxyMode.Auto, Http.ParseMode("auto"));
        Assert.Equal(ProxyMode.Auto, Http.ParseMode("AUTO")); // 认不出一律回 Auto
        Assert.Equal(ProxyMode.Auto, Http.ParseMode("bogus"));
    }

    [Fact]
    public void ParseMode_Known_Values_Map_Exact()
    {
        Assert.Equal(ProxyMode.System, Http.ParseMode("system"));
        Assert.Equal(ProxyMode.Direct, Http.ParseMode("direct"));
    }

    // ------------------------------------------------------------------
    // Auto:系统代理死、直连活 → 兜底救回 + 记 suspect
    // ------------------------------------------------------------------

    [Fact]
    public async Task Auto_DeadProxy_LiveDirect_Fetches_And_Marks_Suspect()
    {
        L.Init("zh-Hans");
        using var server = new StubJsonServer();
        var restore = UseDeadProxy();
        try
        {
            var (ok, status, body, why) = await Http.GetJsonAsync(
                $"http://127.0.0.1:{server.Port}/user/balance", null, Http.DefaultTimeoutSec);
            Assert.True(ok, $"直连兜底应救回请求: {why}");
            Assert.Equal(200, status);
            Assert.True(Http.IsObj(body)); // stub 回 {}
            var st = Http.Status;
            Assert.True(st.Suspect, "兜底成功要进 suspect 窗口,总览横幅靠它显示");
            Assert.NotNull(st.Proxy);
            Assert.Equal("127.0.0.1:1", st.Proxy!.Authority);
        }
        finally
        {
            restore();
        }
    }

    // ------------------------------------------------------------------
    // Auto:双路皆败 → 失败文案点明「系统代理 X 无响应」
    // ------------------------------------------------------------------

    [Fact]
    public async Task Auto_Both_Dead_Errors_Name_The_Proxy()
    {
        L.Init("zh-Hans");
        var restore = UseDeadProxy();
        try
        {
            var (ok, _, _, why) = await Http.GetJsonAsync(
                "http://127.0.0.1:2/", null, Http.DefaultTimeoutSec);
            Assert.False(ok);
            Assert.NotNull(why);
            Assert.Contains("系统代理", why);
            Assert.Contains("127.0.0.1:1", why); // 点名判死的代理地址
            Assert.Contains("直连失败", why);
            // 经适配器的 NetErrText 拼完仍带「网络错误:」前缀(错误行文案契约)
            Assert.StartsWith("网络错误:", Http.NetErrText(why));
            Assert.True(Http.Status.Suspect, "双路皆败且代理在列,应保持 suspect");
        }
        finally
        {
            restore();
        }
    }

    // ------------------------------------------------------------------
    // System:不兜底、不进 suspect;失败文案点名代理,拒连不误判成超时
    // ------------------------------------------------------------------

    [Fact]
    public async Task System_Mode_Does_Not_Fallback_Or_Mark_Suspect()
    {
        L.Init("zh-Hans");
        var restore = UseDeadProxy(ProxyMode.System);
        try
        {
            var (ok, _, _, why) = await Http.GetJsonAsync(
                "http://127.0.0.1:2/", null, Http.DefaultTimeoutSec);
            Assert.False(ok);
            Assert.NotNull(why);
            Assert.Contains("系统代理 127.0.0.1:1 无响应(", why);
            Assert.DoesNotContain("请求超时", why); // 拒连不能被误判成超时
            Assert.False(Http.Status.Suspect, "System 模式没有兜底,不该有 suspect 态");
        }
        finally
        {
            restore();
        }
    }

    // ------------------------------------------------------------------
    // Direct:无视死代理,直连该通就通,不记 suspect
    // ------------------------------------------------------------------

    [Fact]
    public async Task Direct_Mode_Ignores_Dead_Proxy()
    {
        L.Init("zh-Hans");
        using var server = new StubJsonServer();
        var restore = UseDeadProxy(ProxyMode.Direct);
        try
        {
            var (ok, status, _, why) = await Http.GetJsonAsync(
                $"http://127.0.0.1:{server.Port}/x", null, Http.DefaultTimeoutSec);
            Assert.True(ok, $"Direct 模式不该碰代理: {why}");
            Assert.Equal(200, status);
            Assert.False(Http.Status.Suspect);
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
    /// 把 Http.Client 换成「指着本机必拒端口 127.0.0.1:1」的替身(handler 级显式
    /// Proxy,路由确定,不赌 DefaultProxy 的快照时机),DefaultProxy 也指过去让
    /// EffectiveProxy/文案/标记路径看得到同一台代理。返回还原委托。
    /// </summary>
    private static Action UseDeadProxy(ProxyMode mode = ProxyMode.Auto)
    {
        var origClient = Http.Client;
        var origProxy = HttpClient.DefaultProxy;
        var origMode = Http.Mode;
        var dead = new WebProxy("http://127.0.0.1:1/");
        Http.Client = new HttpClient(
            new SocketsHttpHandler { UseProxy = true, Proxy = dead })
        { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        HttpClient.DefaultProxy = dead;
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
