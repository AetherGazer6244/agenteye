using System.Runtime.InteropServices;

namespace AgentEye.WinUI.Services;

/// <summary>
/// 单实例互斥(1.5.3):全局热键、托盘图标、轮询都是**进程级**的——第二个实例
/// 会去抢第一个的热键(1409「该组合已被其它程序占用」报错),还会多出一份
/// 托盘图标 / 小组件 / 轮询,用户看到的现象是"报了个快捷键的错"。
///
/// 做法(自建 Win32,照 HotkeyService 的隐藏窗口范式):
/// - 命名互斥体 `Local\AgentEye.Singleton`:拿到 = 主实例,已存在 = 第二实例;
/// - 主实例另建一个**从不显示**的顶层窗口,类名前缀固定,专等唤醒消息;
/// - 第二实例枚举顶层窗口认出主实例那个,发消息让它把面板叫到前台,
///   然后自己立刻退出(不建窗口/托盘/小组件/热键/轮询)。
///
/// 不用 WinAppSDK 的 AppInstance.FindOrRegisterForKey:unpackaged(本项目
/// WindowsPackageType=None)下 RedirectActivationToAsync 的 Activated 回调
/// 可靠性要实测,自建更可控——本项目托盘与热键本来就是自建 Win32。
///
/// 失败一律静默降级:互斥体/窗口建不起来 → 返回 null,调用方按多实例照常
/// 跑(宁可多开,不许起不来)。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>命名互斥体(Local\ = 当前登录会话,跨用户不互斥)。</summary>
    public const string MutexName = @"Local\AgentEye.Singleton";

    /// <summary>主实例隐藏窗口的类名前缀(第二实例靠它认人)。</summary>
    public const string WindowClassPrefix = "AgentEyeSingleton_";

    /// <summary>唤醒消息:wParam=1 拉面板到前台,0 = 只保托盘(静默启动)。</summary>
    private const uint WM_APP_ACTIVATE = 0x8002;

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hWnd, char[] buf, int max);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW cls);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint exStyle, string cls, string name,
        uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool GetMessageW(out MSG msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll")]
    private static extern bool DispatchMessageW(ref MSG msg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll")]
    private static extern nint LoadCursorW(nint hInst, uint id);

    // 主实例的隐藏窗口类名(带随机后缀,避免与别的进程撞名)
    private static readonly string ClassName =
        WindowClassPrefix + Guid.NewGuid().ToString("N")[..8];

    private readonly Mutex? _mutex;
    private readonly Thread? _thread;
    private readonly WndProcDelegate _proc;   // 防止委托被 GC
    private readonly Action<bool> _onActivate;
    private volatile bool _quit;
    private volatile bool _hwndReady;
    private nint _hwndValue;
    private bool _disposed;

    /// <summary>本进程是不是主实例?false = 已有实例在跑(唤醒消息已发出)。</summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// 尝试成为主实例。
    /// - IsPrimary=true:本进程是主实例,正常继续启动;
    /// - IsPrimary=false:已有实例在跑,唤醒消息已发给它,调用方应立刻退出;
    /// - 返回 null:互斥体/隐藏窗口建不起来(增强面降级),调用方按多实例照常跑。
    ///
    /// quiet:本进程是静默启动(开机自启 --minimized)时为 true——唤醒已有
    /// 实例时只让它保住托盘,不弹面板(免得开机弹窗打扰)。
    /// </summary>
    public static SingleInstance? TryAcquire(bool quiet, Action<bool> onActivate)
    {
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(true, MutexName, out var createdNew);
            if (createdNew)
                return new SingleInstance(mutex, onActivate); // 主实例

            // 已有实例:不是主实例,把唤醒消息发过去
            mutex.Dispose();
            mutex = null;
            NotifyPrimary(quiet);
            return new SingleInstance(null, onActivate); // IsPrimary=false
        }
        catch
        {
            try { mutex?.Dispose(); } catch { /* 增强面 */ }
            return null; // 互斥体建不起来:降级成多实例
        }
    }

    private SingleInstance(Mutex? mutex, Action<bool> onActivate)
    {
        _mutex = mutex;
        _onActivate = onActivate;
        _proc = WndProc;
        IsPrimary = mutex is not null;
        if (!IsPrimary) return;

        _thread = new Thread(Run) { IsBackground = true, Name = "single-instance" };
        _thread.Start();
        _hwndValue = WaitForWindow();
    }

    private nint WaitForWindow()
    {
        for (var i = 0; i < 100 && !_quit; i++)
        {
            if (_hwndReady) return _hwndValue;
            Thread.Sleep(20);
        }
        return _hwndValue;
    }

    /// <summary>主实例的窗口线程:建隐藏窗口 + 跑自己的消息泵。</summary>
    private void Run()
    {
        try
        {
            var hInst = GetModuleHandleW(null);
            var cls = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = hInst,
                lpszClassName = ClassName,
                hCursor = LoadCursorW(nint.Zero, 32512),
            };
            RegisterClassExW(ref cls);
            // 顶层窗口但从不 ShowWindow:EnumWindows 能枚举到,用户看不见
            _hwndValue = CreateWindowExW(0, ClassName, "AgentEyeSingleton",
                0, 0, 0, 0, 0, nint.Zero, nint.Zero, hInst, nint.Zero);
            _hwndReady = true;
            if (_hwndValue == nint.Zero) return;
            while (GetMessageW(out var msg, nint.Zero, 0, 0) && !_quit)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        catch
        {
            // 增强面:窗口线程起不来就算了(第二实例找不到就静默退出)
            _hwndReady = true;
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_APP_ACTIVATE)
        {
            try { _onActivate(wParam != 0); } catch { /* 增强面:回调失败不许带崩 */ }
            return nint.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// 第二实例:找主实例的隐藏窗口并发唤醒消息。主实例可能刚启动、窗口还没
    /// 建好(毫秒级竞态),所以重试一小段;实在找不到就静默退出——宁可少开
    /// 一个实例,也不双开。
    /// </summary>
    private static void NotifyPrimary(bool quiet)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var hwnd = FindPrimaryWindow();
            if (hwnd != nint.Zero)
            {
                try { PostMessageW(hwnd, WM_APP_ACTIVATE, quiet ? 0 : 1, 0); } catch { }
                return;
            }
            Thread.Sleep(50);
        }
    }

    /// <summary>按类名前缀找主实例的隐藏窗口(找不到返回 0)。</summary>
    private static nint FindPrimaryWindow()
    {
        nint found = nint.Zero;
        EnumWindowsProc cb = (hWnd, _) =>
        {
            var buf = new char[128];
            var len = GetClassNameW(hWnd, buf, buf.Length);
            if (len > 0 && new string(buf, 0, len)
                    .StartsWith(WindowClassPrefix, StringComparison.Ordinal))
            {
                found = hWnd;
                return false; // 找到就停
            }
            return true;
        };
        try { EnumWindows(cb, nint.Zero); } catch { /* 增强面 */ }
        return found;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _quit = true;
        var hwnd = _hwndValue;
        if (hwnd != nint.Zero && _thread is not null)
        {
            try { PostMessageW(hwnd, 0x0010, nint.Zero, nint.Zero); } catch { } // WM_CLOSE
            try { _thread.Join(1000); } catch { }
            try { DestroyWindow(hwnd); } catch { }
        }
        // 互斥体只在主实例手里;ReleaseMutex 要求持有线程,失败无妨——
        // 关掉句柄后对象随之销毁,下一实例照样能拿到。
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
    }
}
