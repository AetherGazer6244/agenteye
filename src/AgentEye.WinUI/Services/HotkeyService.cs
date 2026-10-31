using System.Runtime.InteropServices;
using AgentEye.Core.Hotkeys;
using AgentEye.Core.Localization;

namespace AgentEye.WinUI.Services;

/// <summary>
/// 全局快捷键服务:RegisterHotKey + 自建隐藏窗口(独立线程 + 自己的消息泵,
/// 照 TrayIconService 的范式,不去抢 XAML 的消息泵)。
///
/// 两个 Win32 铁律(实测踩过):
/// - RegisterHotKey 必须在**窗口所属线程**上调用,否则直接失败(1408);
///   所以注册/注销都通过自定义消息交给窗口线程执行,调用方等结果。
/// - 组合被别的程序占用时返回 false,错误码 1409;只落状态文案,不崩主流程。
///
/// 组合文本由 Core 的 HotkeySpec 解析(Core 不引 Win32,修饰键/键码同 Win32 值)。
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_APP_APPLY = 0x8001;
    private const int HotkeyId = 0xA17;
    private const uint MOD_NOREPEAT = 0x4000;   // 按住不连发(Win7+)
    private const int ErrorHotkeyAlready = 1409;  // ERROR_HOTKEY_ALREADY_REGISTERED

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const long WS_EX_NOACTIVATE = 0x08000000;

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
    private struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public POINT pt; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint style, string cls, string name,
        uint styleEx, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);
    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wp, nint lp);
    [DllImport("user32.dll")]
    private static extern bool GetMessageW(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")]
    private static extern nint LoadCursorW(nint hInst, uint id);
    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint hWnd, uint msg, nint wp, nint lp);
    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint hWnd, int idx);
    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int idx, nint value);

    private static readonly string ClassName = "AgentEyeHotkey_" + Guid.NewGuid().ToString("N")[..8];

    private readonly Action _onTriggered;
    private readonly WndProcDelegate _proc;   // 防止委托被 GC
    private readonly Thread _thread;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _applied = new(false);
    private volatile bool _quit;
    private volatile bool _hwndReady;
    private nint _hwndValue;
    private nint _hwnd;
    private bool _registered;
    private bool _disposed;

    // 待应用的组合(调用方写,窗口线程读)
    private string _pendingText = "";
    private bool _pendingEnabled;
    private string _result = "";

    /// <summary>当前状态文案(设置页回显;失败原因也在这里)。</summary>
    public string Status { get; private set; } = L.T("hotkey.stDisabled");

    public HotkeyService(Action onTriggered)
    {
        _onTriggered = onTriggered;
        _proc = WndProc;
        _thread = new Thread(Run) { IsBackground = true, Name = "hotkey" };
        _thread.Start();
        _hwnd = WaitForWindow();
        if (_hwnd == nint.Zero) throw new InvalidOperationException("hotkey window failed");
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
            _hwndValue = CreateWindowExW(0, ClassName, "AgentEyeHotkey",
                0, 0, 0, 0, 0, nint.Zero, nint.Zero, hInst, nint.Zero);
            if (_hwndValue == nint.Zero) return;
            // 隐藏且不抢焦点(纯消息窗口:注册热键只要它收 WM_HOTKEY)
            var ex = GetWindowLongPtrW(_hwndValue, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtrW(_hwndValue, GWL_EXSTYLE,
                new nint(ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
            _hwndReady = true;
            while (GetMessageW(out var msg, nint.Zero, 0, 0) && !_quit)
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
        catch
        {
            // 增强面:窗口线程起不来就算了,宿主会看到 Status
            _hwndReady = true;
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            try { _onTriggered(); } catch { /* 增强面:回调失败不许带崩 */ }
            return nint.Zero;
        }
        if (msg == WM_APP_APPLY)
        {
            string text;
            bool enabled;
            lock (_gate)
            {
                text = _pendingText;
                enabled = _pendingEnabled;
            }
            string result;
            try { result = DoApply(text, enabled); }
            catch { result = L.T("hotkey.applyFailed"); }
            _result = result;
            _applied.Set();
            return nint.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// 按组合文本重注册热键(设置页改键后立即调用)。
    /// 注册动作在窗口线程上做(RegisterHotKey 的硬性要求),这里等结果。
    /// </summary>
    public void Apply(string? text, bool enabled)
    {
        if (_disposed)
        {
            Status = L.T("hotkey.stSvcClosed");
            return;
        }
        lock (_gate)
        {
            _pendingText = text ?? "";
            _pendingEnabled = enabled;
        }
        _applied.Reset();
        if (!PostMessageW(_hwnd, WM_APP_APPLY, nint.Zero, nint.Zero))
        {
            Status = L.T("hotkey.stNoResponse");
            return;
        }
        if (!_applied.Wait(2000))
        {
            Status = L.T("hotkey.stTimeout");
            return;
        }
        Status = _result;
    }

    /// <summary>在窗口线程上执行:注销旧组合 → 按需注册新组合。</summary>
    private string DoApply(string text, bool enabled)
    {
        Unregister();
        if (!enabled) return L.T("hotkey.stOff");
        if (!HotkeySpec.TryParse(text, out var spec, out var error) || spec is null)
            return L.F("hotkey.stInvalid", error);
        var mods = (uint)(spec.Modifiers | MOD_NOREPEAT);
        if (!RegisterHotKey(_hwnd, HotkeyId, mods, (uint)spec.VirtualKey))
        {
            var err = Marshal.GetLastWin32Error();
            return err == ErrorHotkeyAlready
                ? L.T("hotkey.stInUse")
                : L.F("hotkey.stRegError", err);
        }
        _registered = true;
        return L.F("hotkey.stActive", spec.ToText());
    }

    private void Unregister()
    {
        if (!_registered) return;
        _registered = false;
        try { UnregisterHotKey(_hwnd, HotkeyId); } catch { /* 增强面 */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _quit = true;
        if (_hwnd != nint.Zero)
        {
            // 窗口销毁时系统会自动注销热键,这里只需把窗口关掉
            try { PostMessageW(_hwnd, 0x0010, nint.Zero, nint.Zero); } catch { } // WM_CLOSE
        }
        try { _thread.Join(1000); } catch { }
        if (_hwnd != nint.Zero)
        {
            try { DestroyWindow(_hwnd); } catch { }
        }
    }
}
