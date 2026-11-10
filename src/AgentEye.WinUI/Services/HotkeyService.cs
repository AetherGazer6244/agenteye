using System.Runtime.InteropServices;
using AgentEye.Core.Hotkeys;
using AgentEye.Core.Localization;

namespace AgentEye.WinUI.Services;

/// <summary>
/// 热键注册状态。UI 判定"要不要弹窗"靠这个枚举,不再靠文案前缀猜
/// (以前只有「注册失败…」开头的会弹,服务不可用/超时/组合无效全静默)。
/// </summary>
public enum HotkeyState
{
    /// <summary>还没 apply 过(刚起来还没装配)。</summary>
    Unknown,
    /// <summary>用户主动关闭 / 未启用(不是错误)。</summary>
    Disabled,
    /// <summary>已注册生效。</summary>
    Active,
    /// <summary>组合非法(解析不过)。</summary>
    Invalid,
    /// <summary>被别的程序占用(1409)。</summary>
    InUse,
    /// <summary>其它 Win32 注册错误。</summary>
    RegError,
    /// <summary>热键服务不可用(窗口线程起不来 / 已关闭)。</summary>
    ServiceDown,
    /// <summary>热键服务无响应。</summary>
    NoResponse,
    /// <summary>应用组合超时。</summary>
    Timeout,
    /// <summary>应用组合时内部异常。</summary>
    ApplyFailed,
}

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
    // SetLastError=true 必带:否则 GetLastWin32Error() 拿到的是 CLR 内部的
    // 垃圾值(实测 -130988),1409「组合被占用」这条友好文案永远走不到
    [DllImport("user32.dll", SetLastError = true)]
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

    /// <summary>当前注册状态(UI 按状态判定要不要弹窗,别去 match 文案)。</summary>
    public HotkeyState State => _state;

    /// <summary>热键是否处在"正常"状态(已生效,或用户主动关掉)。</summary>
    public bool Ok => _state is HotkeyState.Active or HotkeyState.Disabled;

    private volatile HotkeyState _state = HotkeyState.Unknown;

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
            catch
            {
                _state = HotkeyState.ApplyFailed;
                result = L.T("hotkey.applyFailed");
            }
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
            _state = HotkeyState.ServiceDown;
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
            _state = HotkeyState.NoResponse;
            Status = L.T("hotkey.stNoResponse");
            return;
        }
        if (!_applied.Wait(2000))
        {
            _state = HotkeyState.Timeout;
            Status = L.T("hotkey.stTimeout");
            return;
        }
        Status = _result;
    }

    /// <summary>在窗口线程上执行:注销旧组合 → 按需注册新组合。</summary>
    private string DoApply(string text, bool enabled)
    {
        Unregister();
        if (!enabled)
        {
            _state = HotkeyState.Disabled;
            return L.T("hotkey.stOff");
        }
        if (!HotkeySpec.TryParse(text, out var spec, out var error) || spec is null)
        {
            _state = HotkeyState.Invalid;
            return L.F("hotkey.stInvalid", error);
        }
        var mods = (uint)(spec.Modifiers | MOD_NOREPEAT);
        if (!RegisterHotKey(_hwnd, HotkeyId, mods, (uint)spec.VirtualKey))
        {
            var err = Marshal.GetLastWin32Error();
            _state = err == ErrorHotkeyAlready ? HotkeyState.InUse : HotkeyState.RegError;
            return _state == HotkeyState.InUse
                ? L.T("hotkey.stInUse")
                : L.F("hotkey.stRegError", err);
        }
        _registered = true;
        _state = HotkeyState.Active;
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
