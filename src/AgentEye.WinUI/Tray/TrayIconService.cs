using System.Runtime.InteropServices;
using AgentEye.Core.Providers;
using AgentEye.WinUI.Services;

namespace AgentEye.WinUI.Tray;

/// <summary>
/// 托盘图标:Shell_NotifyIconW + GDI 数字渲染(端口自 Python 版 ui/tray.py)。
/// - 图标上直接画最紧张 provider 的"已用百分比"
/// - 左键唤起主窗口,右键弹出菜单
/// - explorer 重启(TaskbarCreated)后自动补挂
/// 任何 Win32 步骤失败静默降级,不把主流程带崩。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint WM_APP_TRAY = 0x8000;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const int NIN_SELECT = 0x0400 + 0;   // WM_USER
    private const int NIN_CONTEXTMENU = 0x0400 + 5;
    private const uint WM_CONTEXTMENU = 0x007B;  // version 4 的右键事件

    [StructLayout(LayoutKind.Sequential, Pack = 2, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;   // union 起点(pre-Vista 布局)
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
    }

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
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint hWnd, uint msg, nint wp, nint lp);
    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint hWnd, int idx);
    [DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hWnd, int idx, nint value);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);
    [DllImport("user32.dll")]
    private static extern nint LoadCursorW(nint hInst, uint id);
    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(nint menu, uint flags, nint id, string? text);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    private const uint MF_STRING = 0, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    private static readonly string ClassName = "AgentEyeTray_" + Guid.NewGuid().ToString("N")[..8];

    private readonly AppServices _app;
    private readonly MainWindow _main;
    private readonly nint _hwnd;
    private readonly WndProcDelegate _proc;      // 防止委托被 GC
    private readonly uint _taskbarCreatedMsg;
    private nint _icon;
    private string _tip = "AgentEye";
    private string _lastKey = "";                // 上次渲染的数字(变了才重画)
    private bool _added;
    private bool _disposed;
    private Thread? _thread;
    private volatile bool _quit;

    public TrayIconService(AppServices app, MainWindow main)
    {
        _app = app;
        _main = main;
        _proc = WndProc;
        _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");
        _app.ResultsChanged += OnResultsChanged;

        var t = new Thread(Run) { IsBackground = true, Name = "tray" };
        _thread = t;
        t.Start();
        _hwnd = WaitForWindow();
        if (_hwnd == nint.Zero) throw new InvalidOperationException("tray window failed");
        AddIcon();
        UpdateIcon();
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

    private volatile bool _hwndReady;
    private nint _hwndValue;

    private void Run()
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
        _hwndValue = CreateWindowExW(WS_EX_TOOLWINDOW_L(), ClassName, "AgentEyeTray",
            0, 0, 0, 0, 0, nint.Zero, nint.Zero, hInst, nint.Zero);
        // 隐藏 top-level(不能 message-only):TaskbarCreated 广播到不了只读窗口
        if (_hwndValue != nint.Zero)
        {
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
    }

    private static uint WS_EX_TOOLWINDOW_L() => 0; // 创建后再补 EXSTYLE,创建参数给 0

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_APP_TRAY)
        {
            var evt = (long)lParam & 0xFFFF; // NOTIFYICON_VERSION_4:低字事件,高字图标 id
            if (evt is 0x0202 or (uint)NIN_SELECT) // LBUTTONUP / select
            {
                try { _main.RestoreFromTray(); } catch { }
            }
            else if (evt == (uint)NIN_CONTEXTMENU || evt == WM_CONTEXTMENU || evt == 0x0205)
            {
                ShowMenu(hWnd);
            }
            return nint.Zero;
        }
        if (msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
        {
            // explorer 重启:补挂
            RemoveIcon();
            AddIcon();
            UpdateIcon(force: true);
            return nint.Zero;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu(nint hWnd)
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero) return;
        AppendMenuW(menu, MF_STRING, new nint(1), "打开主面板");
        AppendMenuW(menu, MF_STRING, new nint(2), "切换小组件");
        AppendMenuW(menu, MF_SEPARATOR, nint.Zero, null);
        AppendMenuW(menu, MF_STRING, new nint(3), "设置");
        AppendMenuW(menu, MF_STRING, new nint(4), "退出");
        GetCursorPos(out var pt);
        // TrackPopupMenu 前必须 SetForegroundWindow,否则菜单点外部不消失(经典坑)
        SetForegroundWindow(hWnd);
        var cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD,
            pt.X, pt.Y, hWnd, nint.Zero);
        PostMessageW(hWnd, 0 /*WM_NULL*/, nint.Zero, nint.Zero);
        DestroyMenu(menu);
        switch (cmd)
        {
            case 1: try { _main.RestoreFromTray(); } catch { } break;
            case 2: try { _main.ToggleWidget(); } catch { } break;
            case 3: try { _main.RestoreFromTray(); _main.GoSettings(); } catch { } break;
            case 4: try { _main.Close(); } catch { } break;
        }
    }

    // ------------------------------------------------------------------
    // 图标渲染:32×32 32bpp DIB + 圆角底 + 数字
    // ------------------------------------------------------------------

    private void UpdateIcon(bool force = false)
    {
        if (_hwnd == nint.Zero) return;
        var (digits, key, tip) = Pick();
        if (!force && key == _lastKey) return;
        _lastKey = key;
        _tip = tip;
        var old = _icon;
        _icon = Draw(digits);
        ModifyIcon();
        if (old != nint.Zero) { try { DestroyIcon(old); } catch { } }
    }

    private (string Digits, string Key, string Tip) Pick()
    {
        var rows = _app.Results;
        // 最紧张 = 有 pct 的行里 pct 最小者;全没有 → 取第一行
        ProviderResult? worst = rows
            .Where(r => r.Pct is not null && !r.Paused)
            .OrderBy(r => r.Pct)
            .FirstOrDefault();
        if (worst is null && rows.Count > 0)
            worst = rows.OrderBy(r => r.Level switch
            {
                Levels.Critical => 0, Levels.Warn => 1, Levels.Ok => 2,
                Levels.Unconfigured => 3, Levels.Paused => 4, _ => 5,
            }).First();
        if (worst is null)
            return ("··", "empty", "AgentEye — 没有已接入的 provider");
        if (worst.Level is Levels.Unconfigured or Levels.Error or Levels.Paused)
            return ("!", $"{worst.Name}|{worst.Level}", $"AgentEye — {worst.Name}({worst.Level})");
        var used = worst.Pct is null ? 0 : (int)Math.Round(100 - worst.Pct.Value);
        used = Math.Clamp(used, 0, 100);
        return (used.ToString(), $"{worst.Name}|{used}",
            $"AgentEye — {worst.Name} 已用 {used}%");
    }

    /// <summary>托盘图标:应用 A 标 + 数字牌(渲染细节见 TrayIconRenderer)。</summary>
    private nint Draw(string digits)
        => TrayIconRenderer.Render(digits, AeAppIcon.IcoPath, TrayIconRenderer.Default);

    // ------------------------------------------------------------------
    // 注册/注销
    // ------------------------------------------------------------------

    private NOTIFYICONDATAW Data() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
        szTip = _tip.Length > 127 ? _tip[..127] : _tip,
    };

    private void AddIcon()
    {
        if (_hwnd == nint.Zero || _disposed || _added) return;
        var data = Data();
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
        if (_added)
        {
            var v = data;
            v.uVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIconW(NIM_SETVERSION, ref v);
        }
    }

    private void ModifyIcon()
    {
        if (!_added) { AddIcon(); return; }
        var data = Data();
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private void RemoveIcon()
    {
        if (!_added) return;
        var data = Data();
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _added = false;
    }

    private void OnResultsChanged() => UpdateIcon();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _app.ResultsChanged -= OnResultsChanged;
        RemoveIcon();
        if (_icon != nint.Zero) { try { DestroyIcon(_icon); } catch { } }
        _quit = true;
        if (_hwnd != nint.Zero) { try { PostMessageW(_hwnd, 0x0010, nint.Zero, nint.Zero); } catch { } } // WM_CLOSE
        try { _thread?.Join(1000); } catch { }
        if (_hwnd != nint.Zero) { try { DestroyWindow(_hwnd); } catch { } }
    }
}
