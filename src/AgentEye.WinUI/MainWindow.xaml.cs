using AgentEye.Core.Localization;
using AgentEye.Core.Providers;
using AgentEye.WinUI.Pages;
using AgentEye.WinUI.Services;
using AgentEye.WinUI.Tray;
using AgentEye.WinUI.Widget;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using WinRT.Interop;
using Windows.Graphics;

namespace AgentEye;

/// <summary>
/// 主窗口:Acrylic 背板 + 紧凑单窗(图标头部,无导航栏;总览/设置靠可见性切换)。
/// 装配面:配置加载、轮询启动、托盘与小组件挂载、几何持久化。
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// 窗口尺寸契约(物理像素,与 config 存的 ui.width/height 同源):
    /// 420×560 是主尺寸(用户确认的紧凑面板),其余尺寸靠自适应布局兜住,
    /// 最小/最大用来防止拖到布局撑不住的极端尺寸。
    /// </summary>
    private const int DefaultWidth = 420, DefaultHeight = 560;
    private const int MinWidth = 320, MinHeight = 400;
    private const int MaxWidth = 800, MaxHeight = 1200;

    private const uint WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SUBCLASSPROC(
        nint hWnd, uint uMsg, nint wParam, nint lParam, nuint uIdSubclass, nint dwSubclassParam);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(
        nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass, nuint dwSubclassParam);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(
        nint hWnd, SUBCLASSPROC pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint hWnd, uint uMsg, nint wParam, nint lParam);

    // 委托必须一直存活,否则被 GC 回收后子类化失效
    private static readonly SUBCLASSPROC SizeConstraintCallback = SizeConstraintProc;
    private bool _subclassed;

    private static nint SizeConstraintProc(
        nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nint param)
    {
        if (msg == WM_GETMINMAXINFO && lParam != nint.Zero)
        {
            try
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                mmi.ptMinTrackSize = new POINT { X = MinWidth, Y = MinHeight };
                mmi.ptMaxTrackSize = new POINT { X = MaxWidth, Y = MaxHeight };
                Marshal.StructureToPtr(mmi, lParam, false);
            }
            catch { /* 结构写失败:保持系统默认限制 */ }
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private readonly AppServices _app = new();
    private OverviewPage? _overview;
    private SettingsPage? _settings;
    private TrayIconService? _tray;
    private WidgetWindow? _widget;
    private HotkeyService? _hotkey;
    private string _hotkeyStatus = "";
    private bool _closed;
    private bool _forceClose;    // 托盘"退出"/更新重启置位:绕过关窗进托盘拦截

    /// <summary>
    /// 更新流程放行:安装器拉起后 Restart Manager 要关闭本应用,
    /// 关窗拦截必须让路,否则静默升级被卡死(安装器契约 CloseApplications=yes)。
    /// </summary>
    internal static volatile bool CloseAllowedByUpdate;

    public MainWindow()
    {
        // 语言定型已在 App ctor(资源系统初始化前)完成,这里直接进 UI 装配
        InitializeComponent();
        // 头部按钮 tooltip(语言启动定型,重启生效)
        ToolTipService.SetToolTip(BtnBack, L.T("win.backTooltip"));
        ToolTipService.SetToolTip(BtnSettings, L.T("win.settingsTooltip"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // 主题:加载即应用(ui.theme)
        ApplyTheme(_app.ThemeChoice);

        // 限制拖拽范围(最小/最大),避免拖到布局撑不住的极端尺寸
        ApplySizeConstraints();

        // 应用图标(任务栏/标题栏)
        ApplyAppIcon();

        // 几何恢复(ui.x/y/width/height)
        RestoreGeometry();

        Closed += (_, _) => OnClosed();
        // 关主窗 X:ui.close_to_tray(默认开)→ 隐藏进托盘,进程/托盘/轮询都还在;
        // 关闭开关(或托盘"退出"/更新安装)才真退出
        try { AppWindow.Closing += OnAppWindowClosing; }
        catch { /* 老系统 AppWindow.Closing 不可用:退回旧行为(关闭即退出) */ }
    }

    /// <summary>把 A 标图标挂到窗口上;失败静默(增强面)。</summary>
    private void ApplyAppIcon()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            AeAppIcon.Apply(AppWindow, hwnd);
        }
        catch { /* 增强面:图标不影响主流程 */ }
    }

    /// <summary>给窗口挂 WM_GETMINMAXINFO 子类,限制最小/最大尺寸。</summary>
    private void ApplySizeConstraints()
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == nint.Zero) return;
            _subclassed = SetWindowSubclass(hwnd, SizeConstraintCallback, 1, 0);
        }
        catch { /* comctl32 不可用:退化成无限制,布局本身已自适应 */ }
    }

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        // 首次进总览页并启动轮询(放 Loaded:DispatcherQueue 就绪)
        _overview = new OverviewPage(_app, this);
        _settings = new SettingsPage(_app, this);
        PageHost.Children.Add(_overview);
        PageHost.Children.Add(_settings);
        ShowPage("overview");

        // 主题在 ctor 里设过一次,但 Root 那时还没走完首次布局,
        // 部分场景(先建页再改主题的时序)会渲染回退;树加载后再应用一次,
        // 保证 dark/light 配置启动即生效(实测 B/C 两轮构建复现过浅色残留)。
        ApplyTheme(_app.ThemeChoice);

        // 头部状态胶囊:随刷新更新(ResultsChanged 约定在 UI 线程触发)
        _app.ResultsChanged += UpdateStatusPill;
        UpdateStatusPill();

        _app.Poller.Start();

        // 空配置首启也刷一轮:让"未配置"空态立刻可见
        _ = _app.Poller.RefreshNowAsync();

        // 托盘常驻;失败静默降级(不把主流程带崩)
        try
        {
            _tray = new TrayIconService(_app, this);
        }
        catch { _tray = null; }

        // 小组件按 ui.widget 恢复
        try
        {
            if (_app.UiDict().TryGetValue("widget", out var w) && w is true)
                ShowWidget();
        }
        catch { /* 增强面,失败静默 */ }

        // 开机自启自愈 + 镜像同步:注册表指向的 exe 路径与当前不一致(挪目录/换盘)
        // 时重写;镜像值以注册表为准回填,修掉历史遗留的分叉。失败静默(增强面)。
        try
        {
            StartupService.EnsureCurrent();
            var regOn = StartupService.IsEnabled();
            if (_app.Autostart != regOn)
            {
                _app.SetAutostart(regOn);
                _app.TrySaveCfg();
            }
        }
        catch { /* 增强面 */ }

        // 全局快捷键(唤醒/隐藏主面板);失败静默降级(不把主流程带崩)
        try
        {
            _hotkey = new HotkeyService(TogglePanel);
            ApplyHotkey();
        }
        catch { _hotkey = null; _hotkeyStatus = L.T("hotkey.svcDown"); }

        // 主题跟随系统:auto 时系统切换会触发这里,同步按钮配色即可,
        // 页面/小组件各自监听 ActualThemeChanged 重渲染
        Root.ActualThemeChanged += (_, _) =>
        {
            SyncCaptionButtons();
            UpdateStatusPill();   // 点色是代码取的画刷,主题切换后要重取
        };

        // 标题栏右侧留白 = 物理像素换算 DIP,别硬编码
        try
        {
            HeaderButtons.Margin = new Thickness(
                0, 0, AppWindow.TitleBar.RightInset / Root.XamlRoot.RasterizationScale, 0);
        }
        catch { /* XamlRoot 未就绪等:忽略,按钮区右侧留白稍宽无碍 */ }

        // 启动静默检查更新(同一自然日只查一次);发现新版弹一次 toast
        _ = Task.Run(async () =>
        {
            try
            {
                if (!_app.Update.ShouldCheckNow()) return;
                await _app.Update.CheckAsync(silent: true);
            }
            catch { /* 增强面:静默失败 */ }
        });
    }

    public void ApplyTheme(string choice)
    {
        Root.RequestedTheme = choice switch
        {
            "dark" => ElementTheme.Dark,
            "light" => ElementTheme.Light,
            _ => ElementTheme.Default,
        };
        // caption 按钮字形颜色跟系统主题,不跟 Root.RequestedTheme,
        // 必须手动同步(否则系统深色+选浅色 → 浅底白按钮看不见)
        SyncCaptionButtons();
        _widget?.ApplyTheme(choice);
    }

    private void SyncCaptionButtons()
    {
        try
        {
            var light = Root.ActualTheme == ElementTheme.Light;
            var bar = AppWindow.TitleBar;
            var fg = light
                ? Windows.UI.Color.FromArgb(255, 0x1A, 0x1A, 0x1A)
                : Windows.UI.Color.FromArgb(255, 0xFF, 0xFF, 0xFF);
            var hoverBg = light
                ? Windows.UI.Color.FromArgb(0x14, 0x00, 0x00, 0x00)
                : Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
            bar.ButtonForegroundColor = fg;
            bar.ButtonHoverForegroundColor = fg;
            bar.ButtonPressedForegroundColor = fg;
            bar.ButtonInactiveForegroundColor = fg;
            bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            bar.ButtonHoverBackgroundColor = hoverBg;
            bar.ButtonPressedBackgroundColor = hoverBg;
        }
        catch { /* 老系统上 TitleBar API 可能不全:静默降级 */ }
    }

    public void ShowPage(string tag)
    {
        if (_overview is null || _settings is null) return;
        _overview.Visibility = tag == "overview" ? Visibility.Visible : Visibility.Collapsed;
        _settings.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        _settings.OnShown();
        BtnBack.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        AppTitle.Text = tag == "overview" ? "AgentEye" : L.T("win.titleSettings");
    }

    // ---------------- 头部状态胶囊 ----------------

    /// <summary>
    /// 头部胶囊:最紧张 provider 的等级色点 + provider 数。
    /// ResultsChanged / 主题切换时重刷;窗口关闭后不再更新(_closed 兜底)。
    /// </summary>
    private void UpdateStatusPill()
    {
        if (_closed || StatusPill is null) return;
        var rows = _app.Results;
        if (rows.Count == 0)
        {
            StatusPill.Visibility = Visibility.Collapsed;
            return;
        }
        var worst = rows.OrderBy(SeverityOf).First().Level;
        try
        {
            StatusDot.Fill = AeRes.Brush(Root, worst switch
            {
                Levels.Critical => "AeCriticalBrush",
                Levels.Error => "AeErrorBrush",
                Levels.Warn => "AeWarnBrush",
                Levels.Ok => "AeOkBrush",
                _ => "AePausedBrush",
            });
        }
        catch { /* 主题资源未就绪等:胶囊仍显示文案 */ }
        StatusPillText.Text = L.F("ov.count", rows.Count);
        StatusPill.Visibility = Visibility.Visible;
    }

    /// <summary>等级严重度排序:紧张 &gt; 异常 &gt; 注意 &gt; 未配置 &gt; 暂停 &gt; 正常(小 = 严重)。</summary>
    private static int SeverityOf(ProviderResult r) => r.Level switch
    {
        Levels.Critical => 0,
        Levels.Error => 1,
        Levels.Warn => 2,
        Levels.Unconfigured => 3,
        Levels.Paused => 4,
        _ => 5,
    };

    // ---------------- 头部图标按钮 ----------------

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
        => ShowPage("settings");

    private void BtnBack_Click(object sender, RoutedEventArgs e)
        => ShowPage("overview");

    /// <summary>托盘/外部入口:封送 UI 线程后进设置页。</summary>
    public void GoSettings() => DispatcherQueue.TryEnqueue(() => ShowPage("settings"));

    public SettingsPage Settings => _settings!;

    public void RefreshOverviewPalette() => _overview?.RefreshPalette();

    public void ShowWidget()
    {
        if (_widget is null)
        {
            _widget = new WidgetWindow(_app, this);
            _widget.ApplyTheme(_app.ThemeChoice);
        }
        _widget.ShowBar();
    }

    public void HideWidget() => _widget?.HideBar();

    public bool WidgetVisible => _widget?.IsVisible ?? false;

    public void RestoreFromTray() => WakeUp();

    /// <summary>
    /// 唤醒主面板:最小化先还原,再显示并置前。
    /// 托盘左键 / 小组件点击 / 全局热键三条入口共用这一条路径。
    /// </summary>
    public void WakeUp() => DispatcherQueue.TryEnqueue(ShowPanel);

    /// <summary>当前热键状态文案(设置页回显;失败原因也在这里)。</summary>
    public string HotkeyStatus => _hotkeyStatus;

    /// <summary>按当前配置重注册全局热键(设置页改键后立即调用)。</summary>
    public void ApplyHotkey()
    {
        try
        {
            if (_hotkey is null)
            {
                _hotkeyStatus = L.T("hotkey.svcDown");
                return;
            }
            _hotkey.Apply(_app.HotkeyText, _app.HotkeyUsable);
            _hotkeyStatus = _hotkey.Status;
        }
        catch { _hotkeyStatus = L.T("hotkey.applyFailed"); }
    }

    /// <summary>
    /// 全局热键语义:面板在前台就藏回托盘,否则把它拉到前台。
    /// (隐藏 ≠ 退出:进程、托盘、小组件都还在,再按一次就回来)
    /// </summary>
    public void TogglePanel() => DispatcherQueue.TryEnqueue(() =>
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            var minimized = presenter is not null &&
                            presenter.State == OverlappedPresenterState.Minimized;
            var inFront = hwnd != nint.Zero && GetForegroundWindow() == hwnd;
            if (!minimized && inFront)
            {
                AppWindow.Hide();
                return;
            }
            ShowPanel();
        }
        catch { /* 增强面:切换失败不许崩主流程 */ }
    });

    /// <summary>把主面板显示出来并置前(最小化先还原)。</summary>
    private void ShowPanel()
    {
        try
        {
            // 最小化状态要先还原(Show 不会把最小化的窗口拉回来)
            if (AppWindow.Presenter is OverlappedPresenter op &&
                op.State == OverlappedPresenterState.Minimized)
                op.Restore();
            else
                AppWindow.Show();
            Activate();
            BringToFront(WindowNative.GetWindowHandle(this));
        }
        catch { /* 增强面:唤醒失败不许崩主流程 */ }
    }

    /// <summary>隐藏主面板(进程存活,托盘/小组件继续;热键再按可唤回)。</summary>
    public void HidePanel()
    {
        try { AppWindow.Hide(); }
        catch { /* 增强面 */ }
    }

    /// <summary>
    /// 关主窗 X 的拦截:close_to_tray 开(默认)时取消关闭、藏进托盘。
    /// 真退出的三条路不受影响:托盘菜单"退出"、更新安装(Restart Manager)、
    /// 关闭开关关掉后的旧行为。
    /// </summary>
    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_forceClose || _closed || CloseAllowedByUpdate) return; // 真:退出
        if (!_app.CloseToTray) return;                              // 用户选了直接退出
        try
        {
            args.Cancel = true;
            SaveGeometry();   // 平时不再有"关窗"时机,隐藏前顺手把几何落盘
            HidePanel();
        }
        catch { /* 增强面:隐藏失败就退回系统默认(窗口关闭退出) */ }
    }

    /// <summary>几何持久化(AppWindow 物理像素直接存,恢复时同源)。</summary>
    private void SaveGeometry()
    {
        try
        {
            var ui = _app.UiDict();
            ui["x"] = (double)AppWindow.Position.X;
            ui["y"] = (double)AppWindow.Position.Y;
            ui["width"] = (double)AppWindow.Size.Width;
            ui["height"] = (double)AppWindow.Size.Height;
            _app.TrySaveCfg();
        }
        catch { /* 关窗路径绝不因写盘失败崩溃 */ }
    }

    /// <summary>
    /// 真退出(托盘菜单"退出"/更新重启):绕过关窗进托盘拦截,走完整清理。
    /// 托盘菜单在 tray 线程上调本方法,而 Window.Close 只能在 UI 线程调——
    /// 跨线程调用抛 RPC_E_WRONG_THREAD 且被 ShowMenu 的 catch 吞掉,表现就是
    /// "托盘退出点了没反应"(1.4.1 实测踩坑);且 _forceClose 已置位不回滚,
    /// 下一次点 X 会被误放行。所以统一封送 UI 线程执行,失败回滚 _forceClose。
    /// </summary>
    public void ExitFromTray()
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ExitCore();
            return;
        }
        DispatcherQueue.TryEnqueue(() => ExitCore());
    }

    private void ExitCore()
    {
        if (_closed) return;
        _forceClose = true;
        try { Close(); }
        catch { _forceClose = false; }
    }

    /// <summary>高级设置:立即改主窗口尺寸并落盘(物理像素)。</summary>
    public void ApplyWindowSize(int width, int height)
    {
        try
        {
            var w = Math.Clamp(width, MinWidth, MaxWidth);
            var h = Math.Clamp(height, MinHeight, MaxHeight);
            AppWindow.Resize(new SizeInt32(w, h));
            _app.SetWindowSize(w, h);
            _app.TrySaveCfg();
        }
        catch { /* 增强面:改尺寸失败不影响主流程 */ }
    }

    /// <summary>恢复默认主窗口尺寸(420×560)。</summary>
    public void ResetWindowSize() => ApplyWindowSize(DefaultWidth, DefaultHeight);

    /// <summary>
    /// 置前:全局热键不自动给前台权,SetForegroundWindow 被系统拒绝时
    /// 借前台线程的输入队列再试一次(老坑,不试就"按了没反应")。
    /// </summary>
    private static void BringToFront(nint hwnd)
    {
        if (hwnd == nint.Zero) return;
        try
        {
            if (SetForegroundWindow(hwnd)) return;
            var fg = GetForegroundWindow();
            if (fg == nint.Zero) return;
            var fgThread = GetWindowThreadProcessId(fg, out _);
            var myThread = GetCurrentThreadId();
            if (fgThread == 0 || fgThread == myThread) return;
            if (!AttachThreadInput(myThread, fgThread, true)) return;
            try
            {
                ShowWindow(hwnd, 9); // SW_RESTORE
                SetForegroundWindow(hwnd);
            }
            finally { AttachThreadInput(myThread, fgThread, false); }
        }
        catch { /* 增强面:置前失败不影响唤醒 */ }
    }

    public void ToggleWidget() => DispatcherQueue.TryEnqueue(() =>
    {
        if (WidgetVisible) HideWidget();
        else ShowWidget();
    });

    /// <summary>数值容错:JSON 里 int/double 都收(Python 版写的是 int,老配置不能被丢弃)。</summary>
    private static double? AsNum(object? v)
    {
        if (v is null) return null;
        try
        {
            var d = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
            return double.IsNaN(d) || double.IsInfinity(d) ? null : d;
        }
        catch { return null; }
    }

    private void RestoreGeometry()
    {
        var ui = _app.UiDict();
        // 主尺寸 420×560;恢复时夹到允许范围(其它尺寸都自适应)
        var wv = AsNum(ui.TryGetValue("width", out var wRaw) ? wRaw : null);
        var w = wv is double dw && dw >= MinWidth
            ? (int)Math.Clamp(dw, MinWidth, MaxWidth) : DefaultWidth;
        var hv = AsNum(ui.TryGetValue("height", out var hRaw) ? hRaw : null);
        var h = hv is double dh && dh >= MinHeight
            ? (int)Math.Clamp(dh, MinHeight, MaxHeight) : DefaultHeight;
        AppWindow.Resize(new SizeInt32(w, h));
        var xv = AsNum(ui.TryGetValue("x", out var xRaw) ? xRaw : null);
        var yv = AsNum(ui.TryGetValue("y", out var yRaw) ? yRaw : null);
        if (xv is double dx && yv is double dy &&
            dx > -10000 && dy > -10000)
        {
            try { AppWindow.Move(new PointInt32((int)dx, (int)dy)); }
            catch { /* 副屏已拔等情况:留系统默认位 */ }
        }
    }

    private void OnClosed()
    {
        if (_closed) return;
        _closed = true;
        // 卸掉窗口子类(句柄即将失效)
        try
        {
            if (_subclassed)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hwnd != nint.Zero)
                    RemoveWindowSubclass(hwnd, SizeConstraintCallback, 1);
                _subclassed = false;
            }
        }
        catch { /* 静默 */ }
        // 几何持久化(AppWindow 物理像素直接存,恢复时同源)
        SaveGeometry();
        _widget?.Dispose();
        _tray?.Dispose();
        _hotkey?.Dispose();
        _app.Poller.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
