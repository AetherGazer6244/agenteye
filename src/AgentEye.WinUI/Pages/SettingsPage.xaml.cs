using AgentEye.Core.Config;
using AgentEye.Core.Hotkeys;
using AgentEye.Core.Localization;
using AgentEye.Core.Providers;
using AgentEye.Core.Update;
using AgentEye.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.System;

namespace AgentEye.WinUI.Pages;

/// <summary>
/// 设置页:外观 / 快捷键 / 告警 / API 管理 / 高级 / 关于软件。
/// 版式按 Windows 11 设置的"卡片行"模板:图标 + 标题 + 副标题 | 右侧控件。
///
/// 契约:
/// - 快捷键文本框既可手打("ctrl+shift+a",解析器大小写不敏感),也可直接按
///   组合键录入;只有"输入框还是原样"时按键才算录入,用户一旦改字就交回输入框。
/// - 组合非法当场提示并回滚,不让坏值落盘;占用(1409)只落状态文案。
/// - provider 表单只暴露 url / key / model;名称自动取 URL 域名,kind 由 key/URL
///   形态推断(新增时),其余高级字段走 config 文件。provider 名称唯一;
///   删除要输入名称确认;key 留空则沿用已存。
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    // Win32 修饰键虚拟键码(取修饰键状态用)
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12,
                      VkLWin = 0x5B, VkRWin = 0x5C;

    private const string RepoUrl = "https://github.com/AetherGazer6244/AgentEye";

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private readonly AppServices _app;
    private readonly MainWindow _main;
    private Dictionary<string, object?>? _editing; // 非 null = 编辑模式
    private bool _loading;                          // 回显期间不触发保存
    private bool _testing;                          // 「测试」进行中,防重入
    private bool _hotkeyWarned;                     // 当前失败状态已弹过窗(不重复弹)
    private CancellationTokenSource? _sizeDebounce;

    private readonly List<ThemeOption> _themeOptions =
    [
        new(L.T("theme.auto"), "auto"),
        new(L.T("theme.light"), "light"),
        new(L.T("theme.dark"), "dark"),
    ];

    // 语言项用各自语言的自称(简体中文 / English),不需翻译
    private readonly List<LanguageOption> _languageOptions =
    [
        new("简体中文", "zh-Hans"),
        new("English", "en"),
    ];

    // 空串 = 自动识别(保存时走 Detect);具体 kind 由保存时定死
    private readonly List<KindOption> _kindOptions =
    [
        new(L.T("form.kindAuto"), ""),
        new(L.T("kind.deepseek"), "deepseek"),
        new(L.T("kind.zhipu"), "zhipu"),
        new(L.T("kind.minimax"), "minimax"),
        new(L.T("kind.opencode_go"), "opencode_go"),
        new(L.T("kind.generic_openai"), "generic_openai"),
    ];

    // 代理模式(1.5.2):direct=直连优先(缺省)/ auto=代理优先 / system=严格跟随
    private readonly List<ProxyModeOption> _proxyModeOptions =
    [
        new(L.T("net.proxyMode.direct"), "direct"),
        new(L.T("net.proxyMode.auto"), "auto"),
        new(L.T("net.proxyMode.system"), "system"),
    ];

    /// <summary>有官方默认端点的 kind(base_url 可留空,拉取层有 fallback)。</summary>
    private static readonly string[] OfficialKinds = ["deepseek", "zhipu", "minimax", "opencode_go"];

    /// <summary>静态文案回填(语言启动定型,重启生效;unpackaged 无 resw 可用)。</summary>
    private void ApplyStrings()
    {
        SecAppearance.Text = L.T("sec.appearance");
        TxtThemeTitle.Text = L.T("theme.title");
        TxtThemeSub.Text = L.T("theme.subtitle");
        TxtAutostartTitle.Text = L.T("autostart.title");
        TxtAutostartSub.Text = L.T("autostart.subtitle");
        AutostartSwitch.OnContent = L.T("autostart.on");
        AutostartSwitch.OffContent = L.T("autostart.off");
        TxtCloseTrayTitle.Text = L.T("close.tray.title");
        TxtCloseTraySub.Text = L.T("close.tray.subtitle");
        CloseTraySwitch.OnContent = L.T("close.tray.on");
        CloseTraySwitch.OffContent = L.T("close.tray.off");
        TxtLangTitle.Text = L.T("lang.title");
        TxtLangSub.Text = L.T("lang.subtitle");
        SecNetwork.Text = L.T("sec.network");
        TxtProxyTitle.Text = L.T("net.proxyModeTitle");
        TxtProxySub.Text = L.T("net.proxyModeSub");
        SecHotkey.Text = L.T("sec.hotkey");
        TxtHotkeyTitle.Text = L.T("hotkey.title");
        TxtHotkeySub.Text = L.T("hotkey.subtitle");
        BtnClearHotkey.Content = L.T("hotkey.clear");
        SecAlert.Text = L.T("sec.alert");
        TxtAlertTitle.Text = L.T("alert.title");
        TxtAlertSub.Text = L.T("alert.subtitle");
        AlertEnable.OnContent = L.T("alert.on");
        AlertEnable.OffContent = L.T("alert.off");
        TxtWarnPct.Text = L.T("alert.warnPct");
        TxtWarnPctSuffix.Text = L.T("alert.warnPctSuffix");
        TxtCritAmount.Text = L.T("alert.critAmount");
        TxtCritSuffix.Text = L.T("alert.critSuffix");
        TxtQuietTitle.Text = L.T("alert.quiet.title");
        TxtQuietSub.Text = L.T("alert.quiet.subtitle");
        QuietSwitch.OnContent = L.T("alert.quiet.on");
        QuietSwitch.OffContent = L.T("alert.quiet.off");
        TxtQuietStart.Text = L.T("alert.quiet.startLabel");
        TxtQuietEnd.Text = L.T("alert.quiet.endLabel");
        SecApi.Text = L.T("sec.api");
        TxtKeysLabel.Text = L.T("api.keysLabel");
        BtnAddKey.Content = L.T("api.addKey");
        BtnEdit.Content = L.T("api.edit");
        BtnDelete.Content = L.T("api.delete");
        FKey.PlaceholderText = L.T("form.keyPlaceholder");
        FModel.PlaceholderText = L.T("form.modelPlaceholder");
        TxtKindLabel.Text = L.T("form.kindLabel");
        FBaseUrl.PlaceholderText = L.T("form.urlPlaceholder");
        BtnTest.Content = L.T("form.test");
        BtnSave.Content = L.T("form.save");
        BtnCancel.Content = L.T("form.cancel");
        SecAdv.Text = L.T("sec.adv");
        TxtSizeTitle.Text = L.T("adv.sizeTitle");
        TxtSizeSub.Text = L.T("adv.sizeSubtitle");
        TxtWidthLabel.Text = L.T("adv.width");
        TxtHeightLabel.Text = L.T("adv.height");
        BtnResetSize.Content = L.T("adv.resetSize");
        SecAbout.Text = L.T("sec.about");
        TxtAboutTitle.Text = L.T("about.title");
        TxtAboutSub.Text = L.T("about.subtitle");
        BtnAboutDetails.Content = L.T("about.details");
        BtnCheckUpdate.Content = L.T("update.check");
        BtnDownloadUpdate.Content = L.T("update.download");
        BtnIgnoreUpdate.Content = L.T("update.ignore");
    }

    public SettingsPage(AppServices app, MainWindow main)
    {
        InitializeComponent();
        // 页面可持有焦点:ComboBox 选中后把焦点交回页面,否则带焦点的下拉
        // 会被滚轮改选项(主题选完一滚就切回去的元凶)
        IsTabStop = true;
        ApplyStrings();
        _app = app;
        _main = main;
        ThemeCombo.ItemsSource = _themeOptions;
        LanguageCombo.ItemsSource = _languageOptions;
        FKind.ItemsSource = _kindOptions;
        ProxyModeCombo.ItemsSource = _proxyModeOptions;
        LoadGeneral();
        ReloadList();
        app.ConfigSaved += ReloadList;
        Unloaded += (_, _) => app.ConfigSaved -= ReloadList;
        app.Update.StateChanged += OnUpdateState;
        Unloaded += (_, _) => app.Update.StateChanged -= OnUpdateState;
    }

    /// <summary>每次切到设置页:表单收回列表态,通用区回显最新 cfg。</summary>
    public void OnShown()
    {
        ShowListPane();
        LoadGeneral();
        ReloadList();
        // 热键没生效(被占用/组合非法/服务异常)时提示一次;
        // 同一个失败状态只弹一次,恢复了下次再犯才再弹
        if (_main.HotkeyOk)
        {
            _hotkeyWarned = false;
            return;
        }
        if (_hotkeyWarned) return;
        _hotkeyWarned = true;
        _ = ShowErrorAsync(L.T("hotkey.notEffective"), _main.HotkeyStatus);
    }

    /// <summary>错误提示走弹窗(设置页不保留常驻状态小字)。</summary>
    private async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = L.T("common.close"),
            };
            await dialog.ShowAsync();
        }
        catch
        {
            // 弹不出来(无 XamlRoot 等):静默,不崩主流程
        }
    }

    // ------------------------------------------------------------------
    // 通用区回显(外观 / 快捷键 / 告警 / 高级 / 关于)
    // ------------------------------------------------------------------

    private void LoadGeneral()
    {
        _loading = true;
        try
        {
            ThemeCombo.SelectedIndex = Math.Max(0, _themeOptions.FindIndex(o => o.Value == _app.ThemeChoice));
            AutostartSwitch.IsOn = StartupService.IsEnabled();
            CloseTraySwitch.IsOn = _app.CloseToTray;
            LanguageCombo.SelectedIndex = Math.Max(0,
                _languageOptions.FindIndex(o => o.Value == _app.Language));
            ProxyModeCombo.SelectedIndex = Math.Max(0,
                _proxyModeOptions.FindIndex(o => o.Value == _app.ProxyModeChoice));

            HotkeyBox.Text = _app.HotkeyText;

            var alert = _app.AlertDict();
            AlertEnable.IsOn = alert.TryGetValue("enable", out var en) && en is true;
            SyncAlertThresholds();
            WarnPctBox.Value = alert.TryGetValue("warn_pct", out var w) ? Convert.ToDouble(w) : 30;
            CriticalYuanBox.Value = alert.TryGetValue("critical_amount_yuan", out var c)
                ? Convert.ToDouble(c) : 5.0;
            // 免打扰(1.6.0):两个键都能解析才算开;缺省 23:00–08:00
            QuietSwitch.IsOn = QuietConfigured(alert);
            QuietStartPicker.Time = QuietTimeOr(alert, "quiet_start", fallbackMinutes: 23 * 60);
            QuietEndPicker.Time = QuietTimeOr(alert, "quiet_end", fallbackMinutes: 8 * 60);
            SyncQuietTimes();

            WidthBox.Value = _app.WindowWidth;
            HeightBox.Value = _app.WindowHeight;

            VersionText.Text = L.F("update.versionLine",
                UpdateService.CurrentVersion, UpdateChecker.Owner, UpdateChecker.Repo);
        }
        finally
        {
            _loading = false;
        }
    }

    // ------------------------------------------------------------------
    // 外观模式
    // ------------------------------------------------------------------

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ThemeCombo.SelectedIndex < 0) return;
        var choice = _themeOptions[ThemeCombo.SelectedIndex].Value;
        _app.SetThemeChoice(choice);
        _main.ApplyTheme(choice);
        _app.TrySaveCfg();
        _main.RefreshOverviewPalette();
        BlurFocus(ThemeCombo);
    }

    // ------------------------------------------------------------------
    // 开机自启动(HKCU Run 键,与应用安装器同一条)
    // ------------------------------------------------------------------

    private void AutostartSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var want = AutostartSwitch.IsOn;
        if (StartupService.SetEnabled(want, out var error))
        {
            _app.SetAutostart(want);
            _app.TrySaveCfg();
        }
        else
        {
            // 写不进去就回滚开关,别让界面与注册表不一致
            _loading = true;
            AutostartSwitch.IsOn = !want;
            _loading = false;
            _ = ShowErrorAsync(L.T("autostart.errorTitle"),
                error ?? L.T("autostart.errorDefault"));
        }
    }

    // ------------------------------------------------------------------
    // 关窗行为(ui.close_to_tray):开 = X 隐藏进托盘(默认),关 = X 直接退出
    // ------------------------------------------------------------------

    private void CloseTraySwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _app.SetCloseToTray(CloseTraySwitch.IsOn);
        _app.TrySaveCfg();
    }

    // ------------------------------------------------------------------
    // 代理模式(ui.proxy_mode):Direct=直连优先(默认)/ Auto=代理优先 / System=严格跟随
    // ------------------------------------------------------------------

    private void ProxyModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (ProxyModeCombo.SelectedIndex < 0) return;
        var mode = _proxyModeOptions[ProxyModeCombo.SelectedIndex].Value;
        if (mode == _app.ProxyModeChoice) return;
        _app.SetProxyMode(mode);
        _app.TrySaveCfg();
        BlurFocus(ProxyModeCombo);
        // 立即按新模式拉一轮:兜底开没开,下一秒面板上就能看出来
        _ = _app.Poller.RefreshNowAsync();
    }

    /// <summary>
    /// 把焦点从下拉框挪回页面:WinUI 里带焦点的 ComboBox 会被滚轮改选项,
    /// 选完主题一滚轮就"切回去"就是它。焦点交还页面后,滚轮恢复滚动页面。
    /// </summary>
    private void BlurFocus(ComboBox combo)
    {
        try
        {
            combo.IsDropDownOpen = false;
            Focus(FocusState.Programmatic);
        }
        catch { /* 焦点挪不动也无碍:只是滚轮可能还会改下拉 */ }
    }

    // ------------------------------------------------------------------
    // 语言(1.3.0 起中英可切;重启生效——整套文案在启动时一次定型)
    // ------------------------------------------------------------------

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LanguageCombo.SelectedIndex < 0) return;
        var lang = _languageOptions[LanguageCombo.SelectedIndex].Value;
        if (lang == _app.Language) return;
        _app.SetLanguage(lang);
        _app.TrySaveCfg();
        BlurFocus(LanguageCombo);
        _ = ShowRestartDialogAsync();
    }

    /// <summary>语言切换提示:重启生效,可立即重启(增强面:失败静默回提示)。</summary>
    private async Task ShowRestartDialogAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = L.T("lang.saved"),
                Content = new TextBlock
                {
                    Text = L.T("lang.restart.msg"),
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = L.T("lang.restart.now"),
                CloseButtonText = L.T("common.close"),
                DefaultButton = ContentDialogButton.Primary,
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary) RestartApp();
        }
        catch
        {
            // 弹不出来(无 XamlRoot 等):设置已保存,用户下次重启自然生效
        }
    }

    private void RestartApp()
    {
        try
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "AgentEye.exe");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            // 真退出走 ExitFromTray:关闭拦截放行 + 完整清理(托盘图标不残留)
            _main.ExitFromTray();
        }
        catch
        {
            _ = ShowErrorAsync(L.T("common.error"), L.T("app.restartFailed"));
        }
    }

    // ------------------------------------------------------------------
    // 唤醒快捷键(文本框:手打 / 按键录入 / 清除)
    // ------------------------------------------------------------------

    private void HotkeyBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_loading) return;
        if (e.Key == VirtualKey.Enter)
        {
            CommitHotkeyText();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.Escape)
        {
            HotkeyBox.Text = _app.HotkeyText;
            e.Handled = true;
            return;
        }
        // 只按了修饰键:等用户按主键,不提示
        if (e.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows)
            return;
        // 输入框已被手动改过 → 这是在打字,别抢
        if (!HotkeyBoxUntouched) return;

        var mods = 0;
        if (IsDown(VkControl)) mods |= HotkeySpec.ModControl;
        if (IsDown(VkMenu)) mods |= HotkeySpec.ModAlt;
        if (IsDown(VkShift)) mods |= HotkeySpec.ModShift;
        if (IsDown(VkLWin) || IsDown(VkRWin)) mods |= HotkeySpec.ModWin;
        if (mods == 0) return; // 普通字符,交给输入框

        e.Handled = true;
        if (!HotkeySpec.TryFromVirtualKey((int)e.Key, mods, out var spec, out var error) ||
            spec is null)
        {
            _ = ShowErrorAsync(L.T("hotkey.invalidCombo"),
                error ?? L.T("hotkey.invalidDefault"));
            return;
        }
        ApplyHotkey(spec.ToText(), true);
    }

    private void HotkeyBox_KeyUp(object sender, KeyRoutedEventArgs e) { }

    /// <summary>失焦提交(手打的文本);内容没变就不动。</summary>
    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (HotkeyBoxUntouched) return;
        CommitHotkeyText();
    }

    /// <summary>输入框是否还停留在已保存的值(没被用户改过)。</summary>
    private bool HotkeyBoxUntouched =>
        string.Equals(HotkeyBox.Text.Trim(), _app.HotkeyText, StringComparison.OrdinalIgnoreCase);

    /// <summary>提交手打文本:非法当场弹窗提示并回滚,不写坏值。</summary>
    private void CommitHotkeyText()
    {
        var text = HotkeyBox.Text.Trim();
        if (text.Length == 0)
        {
            ApplyHotkey("", false);
            return;
        }
        if (!HotkeySpec.TryParse(text, out var spec, out var error) || spec is null)
        {
            HotkeyBox.Text = _app.HotkeyText;
            _ = ShowErrorAsync(L.T("hotkey.invalidCombo"),
                L.F("hotkey.invalidExample", error));
            return;
        }
        ApplyHotkey(spec.ToText(), true);
    }

    private void ApplyHotkey(string text, bool enabled)
    {
        _app.SetHotkey(text, enabled);
        _app.TrySaveCfg();
        _main.ApplyHotkey();
        HotkeyBox.Text = _app.HotkeyText;
        // 失败一律走弹窗(被占用/组合非法/服务异常都算,不再只认"注册失败"开头);
        // 成功静默,界面不留状态小字
        if (_main.HotkeyOk)
        {
            _hotkeyWarned = false;
            return;
        }
        _hotkeyWarned = true;
        _ = ShowErrorAsync(L.T("hotkey.notEffective"), _main.HotkeyStatus);
    }

    private void BtnClearHotkey_Click(object sender, RoutedEventArgs e)
        => ApplyHotkey("", false);

    // ------------------------------------------------------------------
    // 高级:主窗口面板大小
    // ------------------------------------------------------------------

    private async void SizeBox_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        if (double.IsNaN(WidthBox.Value) || double.IsNaN(HeightBox.Value)) return;
        // 防抖:连续敲数字时别一路 resize
        _sizeDebounce?.Cancel();
        _sizeDebounce = new CancellationTokenSource();
        var token = _sizeDebounce.Token;
        try { await Task.Delay(350, token); }
        catch { return; }
        if (token.IsCancellationRequested) return;
        var w = (int)Math.Round(WidthBox.Value);
        var h = (int)Math.Round(HeightBox.Value);
        _main.ApplyWindowSize(w, h);
    }

    private void BtnResetSize_Click(object sender, RoutedEventArgs e)
    {
        _main.ResetWindowSize();
        _loading = true;
        try
        {
            WidthBox.Value = _app.WindowWidth;
            HeightBox.Value = _app.WindowHeight;
        }
        finally { _loading = false; }
    }

    // ------------------------------------------------------------------
    // 关于软件
    // ------------------------------------------------------------------

    private async void BtnAboutDetails_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri(RepoUrl));
        }
        catch { /* 增强面:打不开浏览器就算了 */ }
    }

    // ------------------------------------------------------------------
    // 告警
    // ------------------------------------------------------------------

    private void AlertToggle_Changed(object sender, RoutedEventArgs e)
    {
        SyncAlertThresholds();
        if (_loading) return;
        ApplyAlert();
    }

    /// <summary>阈值行随开关收纳/展开(初态跟 cfg,切换即时跟随)。</summary>
    private void SyncAlertThresholds()
        => AlertThresholds.Visibility = AlertEnable.IsOn
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void AlertNum_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        ApplyAlert();
    }

    private void ApplyAlert()
    {
        var alert = _app.AlertDict();
        alert["enable"] = AlertEnable.IsOn;
        alert["warn_pct"] = (int)Math.Clamp(WarnPctBox.Value, 0, 100);
        alert["critical_amount_yuan"] = Math.Max(0, CriticalYuanBox.Value);
        // 免打扰(1.6.0):开 = 落 "HH:mm";关 = 置空串(与模板缺省一致)
        if (QuietSwitch.IsOn)
        {
            alert["quiet_start"] = QuietText(QuietStartPicker.Time);
            alert["quiet_end"] = QuietText(QuietEndPicker.Time);
        }
        else
        {
            alert["quiet_start"] = "";
            alert["quiet_end"] = "";
        }
        _app.TrySaveCfg();
    }

    // ------------------------------------------------------------------
    // 告警免打扰时段(1.6.0)
    // ------------------------------------------------------------------

    /// <summary>两个 quiet 键都能解析出时间才视为「已配置」。</summary>
    private static bool QuietConfigured(Dictionary<string, object?> alert) =>
        AgentEye.Core.Alerts.QuietHours.ParseMinutes(alert.TryGetValue("quiet_start", out var s) ? s as string : null)
            is not null &&
        AgentEye.Core.Alerts.QuietHours.ParseMinutes(alert.TryGetValue("quiet_end", out var e) ? e as string : null)
            is not null;

    /// <summary>quiet 键 → TimeSpan;没配给缺省值(开始 23:00 / 结束 08:00)。</summary>
    private static TimeSpan QuietTimeOr(Dictionary<string, object?> alert, string key, int fallbackMinutes) =>
        TimeSpan.FromMinutes(AgentEye.Core.Alerts.QuietHours.ParseMinutes(
            alert.TryGetValue(key, out var v) ? v as string : null) ?? fallbackMinutes);

    private static string QuietText(TimeSpan time) =>
        $"{(int)time.TotalHours:D2}:{time.Minutes:D2}";

    private void SyncQuietTimes() =>
        QuietTimes.Visibility = QuietSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;

    private void QuietSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        SyncQuietTimes();
        if (_loading) return;
        ApplyAlert();
    }

    private void QuietTime_Changed(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (_loading) return;
        if (QuietSwitch.IsOn) ApplyAlert();
    }

    // ------------------------------------------------------------------
    // 关于 / 更新
    // ------------------------------------------------------------------

    private void OnUpdateState(UpdateState state)
    {
        // 检查/下载都可能从后台线程回调,统一回 UI 线程刷新
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateStatusText.Text = state.Message;
            BtnCheckUpdate.IsEnabled = !state.Busy;
            // 有新版时只留「下载并更新 / 忽略此版本」,窄窗口也放得下
            BtnCheckUpdate.Visibility = state.HasUpdate
                ? Visibility.Collapsed : Visibility.Visible;
            UpdateProgress.Visibility = state.Busy && state.Progress > 0
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateProgress.Value = state.Progress;
            BtnDownloadUpdate.Visibility = state.HasUpdate ? Visibility.Visible : Visibility.Collapsed;
            BtnIgnoreUpdate.Visibility = state.HasUpdate ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatusText.Text = L.T("update.checking");
        await _app.Update.CheckAsync(silent: false);
    }

    private async void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnDownloadUpdate.IsEnabled = false;
        var progress = new Progress<int>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateProgress.Visibility = Visibility.Visible;
                UpdateProgress.Value = p;
                UpdateStatusText.Text = L.F("update.downloading", p);
            });
        });
        var error = await _app.Update.DownloadAndApplyAsync(progress);
        if (error is not null)
        {
            UpdateStatusText.Text = error;
            BtnDownloadUpdate.IsEnabled = true;
        }
    }

    private async void BtnIgnoreUpdate_Click(object sender, RoutedEventArgs e)
    {
        _app.Update.IgnoreCurrentVersion();
        await _app.Update.CheckAsync(silent: true);
    }

    // ------------------------------------------------------------------
    // API 管理:列表
    // ------------------------------------------------------------------

    private void ReloadList()
    {
        var list = new List<string>();
        foreach (var p in _app.Providers().OfType<Dictionary<string, object?>>())
        {
            var name = p.TryGetValue("name", out var n) ? n : "?";
            var kind = p.TryGetValue("kind", out var k) ? k : "?";
            list.Add($"{name}    [{kind}]");
        }
        var selected = ProviderList.SelectedIndex;
        ProviderList.ItemsSource = list;
        if (selected >= 0 && selected < list.Count)
            ProviderList.SelectedIndex = selected;
    }

    private Dictionary<string, object?>? SelectedProvider()
    {
        var idx = ProviderList.SelectedIndex;
        if (idx < 0) return null;
        var arr = _app.Providers().OfType<Dictionary<string, object?>>().ToList();
        return idx < arr.Count ? arr[idx] : null;
    }

    private async void ProviderList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var p = SelectedProvider();
        if (p is not null) EditProvider(TryGetName(p));
    }

    private async void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        var p = SelectedProvider();
        if (p is null) return;
        EditProvider(TryGetName(p));
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        var p = SelectedProvider();
        if (p is null) return;
        await ConfirmDeleteAsync(_app, this, TryGetName(p));
    }

    private static string TryGetName(Dictionary<string, object?> p) =>
        p.TryGetValue("name", out var n) && n is string ns ? ns : "";

    /// <summary>危险删除:输入 provider 名称确认(对齐 Python confirm_delete)。</summary>
    public static async Task ConfirmDeleteAsync(
        AppServices app, FrameworkElement host, string name)
    {
        var input = new TextBox { PlaceholderText = L.F("delete.confirmInput", name) };
        var dialog = new ContentDialog
        {
            XamlRoot = host.XamlRoot,
            Title = L.F("delete.title", name),
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = L.T("delete.body"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    input,
                },
            },
            PrimaryButtonText = L.T("api.delete"),
            CloseButtonText = L.T("form.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;
        if (input.Text.Trim() != name) return;
        var providers = ConfigStore.GetList(app.Cfg, "providers");
        var target = providers.OfType<Dictionary<string, object?>>()
            .FirstOrDefault(p => TryGetName(p) == name);
        if (target is null) return;
        providers.Remove(target);
        app.Paused.Remove(name);
        if (!app.TrySaveCfg())
            return; // 写盘失败:列表不动,让用户看到配置没存下来
        try
        {
            var baseUrl = target.TryGetValue("base_url", out var b) && b is string bs ? bs : "";
            app.Cache.RemoveProviderEntries(baseUrl, PlainKeyOf(target));
            app.Cache.LogProviderDeleted(name, baseUrl);
        }
        catch { /* 缓存清理失败不拦删除主流程 */ }
        app.SetResults(app.Results.Where(r => r.Name != name).ToList());
        _ = app.Poller.RefreshNowAsync();
    }

    private static string PlainKeyOf(Dictionary<string, object?> p) => ConfigStore.PlainKey(p);

    // ------------------------------------------------------------------
    // API 管理:表单(添加 / 编辑)
    // ------------------------------------------------------------------

    public void OpenAddForm()
    {
        _editing = null;
        ClearForm();
        _loading = true;
        try
        {
            FKind.IsEnabled = true;
            FKind.SelectedIndex = 0;
        }
        finally
        {
            _loading = false;
        }
        FormDetect.Visibility = Visibility.Collapsed;
        ShowFormPane();
    }

    public void EditProvider(string name)
    {
        var p = _app.FindProviderByName(name);
        if (p is null) return;
        _editing = p;
        LoadForm(p);
        ShowFormPane();
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e) => OpenAddForm();

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => ShowListPane();

    private void ShowFormPane()
    {
        ListPane.Visibility = Visibility.Collapsed;
        FormPane.Visibility = Visibility.Visible;
        FormError.Visibility = Visibility.Collapsed;
        FormResult.Visibility = Visibility.Collapsed;
    }

    private void ShowListPane()
    {
        _editing = null;
        FormPane.Visibility = Visibility.Collapsed;
        ListPane.Visibility = Visibility.Visible;
        ReloadList();
    }

    private void ClearForm()
    {
        FBaseUrl.Text = "";
        FKey.Text = "";
        FModel.Text = "";
        FModel.ItemsSource = null;
    }

    private void LoadForm(Dictionary<string, object?> p)
    {
        _loading = true;
        try
        {
            ClearForm();
            FBaseUrl.Text = p.TryGetValue("base_url", out var b) && b is string bs ? bs : "";
            // key 留空 = 沿用已存(明文/密文都行,保存时解好放内存)
            FKey.Text = AppServices.PlainKey(p);
            FModel.Text = p.TryGetValue("model", out var m) && m is string ms ? ms : "";
            // 编辑态 kind 定死(改 kind 会破坏 extra 语义):下拉禁用只回显
            var kind = p.TryGetValue("kind", out var k) && k is string ks ? ks : "";
            FKind.IsEnabled = false;
            FKind.SelectedIndex = Math.Max(0,
                _kindOptions.FindIndex(o => o.Value == kind));
        }
        finally
        {
            _loading = false;
        }
        FormDetect.Visibility = Visibility.Collapsed;
        FormResult.Visibility = Visibility.Collapsed;
    }

    private void ShowFormError(string? msg)
    {
        if (msg is null)
        {
            FormError.Visibility = Visibility.Collapsed;
            return;
        }
        FormError.Text = msg;
        FormError.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------
    // 类型下拉 + 识别预览(纯本地正则,无网络)
    // ------------------------------------------------------------------

    private string SelectedKind =>
        FKind.SelectedIndex >= 0 ? _kindOptions[FKind.SelectedIndex].Value : "";

    private string KindLabel(string kind) => L.T("kind." + kind);

    private void FKind_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        // 选官方类型时把默认端点填进 URL:仅在"空 or 还是别的官方默认"时,
        // 用户手敲的自定义 URL 不动
        var kind = SelectedKind;
        if (OfficialKinds.Contains(kind))
        {
            var def = ConfigStore.DefaultBaseUrl(kind);
            var cur = FBaseUrl.Text.Trim();
            if (def.Length > 0 &&
                (cur.Length == 0 || OfficialKinds.Any(k => ConfigStore.DefaultBaseUrl(k) == cur)))
                FBaseUrl.Text = def;
        }
        UpdateDetectPreview();
        BlurFocus(FKind);
    }

    private void FBaseUrl_LostFocus(object sender, RoutedEventArgs e) => UpdateDetectPreview();

    private void FKey_LostFocus(object sender, RoutedEventArgs e) => UpdateDetectPreview();

    /// <summary>自动识别态且填了内容时,实时展示 Detect 结论(JWT 撞形等提示不再丢失)。</summary>
    private void UpdateDetectPreview()
    {
        if (_editing is not null || SelectedKind.Length > 0)
        {
            FormDetect.Visibility = Visibility.Collapsed;
            return;
        }
        var baseUrl = FBaseUrl.Text.Trim();
        var key = FKey.Text.Trim();
        if (baseUrl.Length == 0 && key.Length == 0)
        {
            FormDetect.Visibility = Visibility.Collapsed;
            return;
        }
        var d = Detect.Run(key: key, hintUrl: baseUrl);
        FormDetect.Text = L.F("form.detect", KindLabel(d.Kind), d.Notes);
        FormDetect.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------
    // 测试 Key:表单当前值跑一次真实拉取(不落盘),顺带填充模型下拉
    // ------------------------------------------------------------------

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        if (_testing) return;
        var baseUrl = FBaseUrl.Text.Trim();
        var key = FKey.Text.Trim();
        var model = FModel.Text.Trim();
        var kind = SelectedKind.Length > 0 ? SelectedKind
            : Detect.Run(key: key, hintUrl: baseUrl).Kind;
        if (baseUrl.Length == 0 && !OfficialKinds.Contains(kind))
        {
            ShowFormError(L.T("form.errEmptyUrl"));
            return;
        }

        _testing = true;
        BtnTest.IsEnabled = false;
        BtnSave.IsEnabled = false;
        ShowFormResult(null, L.T("form.testing"));
        try
        {
            var outcome = await ProviderTest.RunAsync(kind, baseUrl, key, model);
            var line = L.F(outcome.Ok ? "form.testOk" : "form.testFail", outcome.Message);
            if (outcome.Models.Count > 0)
            {
                FillModelCombo(outcome.Models);
                line += " · " + L.F("form.modelsLoaded", outcome.Models.Count);
            }
            ShowFormResult(outcome.Ok, line);
        }
        finally
        {
            _testing = false;
            BtnTest.IsEnabled = true;
            BtnSave.IsEnabled = true;
        }
    }

    /// <summary>结果行:null=中性(进行中),true=绿,false=红(等级色均主题无关,直引安全)。</summary>
    private void ShowFormResult(bool? ok, string msg)
    {
        FormResult.Foreground = ok is { } flagged
            ? AeRes.Brush(this, flagged ? "AeOkBrush" : "AeErrorBrush")
            : AeRes.Brush(this, "AeTextSecondaryBrush");
        FormResult.Text = msg;
        FormResult.Visibility = msg.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>模型下拉填充:保留用户已输入/已选的值。</summary>
    private void FillModelCombo(IReadOnlyList<string> models)
    {
        var current = FModel.Text;
        // 填充是程序动作,不算用户选择:别触发 BlurFocus 抢走正在输入的焦点
        _loading = true;
        try { FModel.ItemsSource = models; }
        finally { _loading = false; }
        FModel.Text = current;
    }

    private void FModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        BlurFocus(FModel);
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var baseUrl = FBaseUrl.Text.Trim();
        var key = FKey.Text.Trim();
        var model = FModel.Text.Trim();

        // 新增:下拉选了具体 kind 就不跑 Detect;编辑:kind 保持不变
        var kind = _editing is not null
            ? _editing.TryGetValue("kind", out var k0) && k0 is string ks0 ? ks0 : ""
            : SelectedKind.Length > 0 ? SelectedKind
            : Detect.Run(key: key, hintUrl: baseUrl).Kind;

        // 官方 kind 拉取层对空 URL 有 fallback,允许留空;通用兼容必须给
        if (baseUrl.Length == 0 && !OfficialKinds.Contains(kind))
        {
            ShowFormError(L.T("form.errEmptyUrl"));
            return;
        }

        // 名称自动取 URL 域名(留空时用类型名),重名追加 -2/-3;编辑时保持原名不变
        // (改名会让暂停集合与托盘最紧张行的索引错位)
        var name = _editing is not null ? TryGetName(_editing)
            : UniqueName(baseUrl.Length > 0 ? NameFromUrl(baseUrl) : KindLabel(kind));

        Dictionary<string, object?> entry;
        if (_editing is not null)
        {
            entry = _editing;
        }
        else
        {
            entry = new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid().ToString("N")[..12],
                ["extra"] = new Dictionary<string, object?>(),
            };
            ConfigStore.GetList(_app.Cfg, "providers").Add(entry);
        }

        // key 处理:留空则沿用内存里已有的;env 覆盖在 ApplyEnvV2。
        var existingKey = AppServices.PlainKey(entry);
        var newKey = key.Length > 0 ? key
            : existingKey.Length > 0 ? existingKey
            : "";
        if (_editing is null)
        {
            // kind 只在新增时定死;已有条目改 kind 会破坏 extra 语义,走 config
            entry["kind"] = kind;
        }
        entry["name"] = name;
        SetOrRemove(entry, "base_url", baseUrl);
        if (newKey.Length > 0)
            entry["key"] = newKey;
        SetOrRemove(entry, "model", model);
        entry.Remove("_key_from_env");

        var err = _app.SaveCfgError();
        if (err is not null)
        {
            // 写盘失败:把刚加的新条目回滚,别让内存态和磁盘态分叉
            if (_editing is null) ConfigStore.GetList(_app.Cfg, "providers").Remove(entry);
            ShowFormError(L.F("form.errSave", err));
            return;
        }
        ShowListPane();
        _ = _app.Poller.RefreshNowAsync();
        await Task.CompletedTask;
    }

    /// <summary>URL → 展示名:优先域名,畸形输入退化为最后一段。</summary>
    private static string NameFromUrl(string baseUrl)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) && u.Host.Length > 0)
            return u.Host;
        var tail = baseUrl.Trim().TrimEnd('/').Split('/').LastOrDefault() ?? "";
        return tail.Length > 0 ? tail : "provider";
    }

    /// <summary>域名撞车时追加 -2/-3,保证 provider 名称唯一。</summary>
    private string UniqueName(string baseName)
    {
        var candidate = baseName;
        for (var n = 2; _app.FindProviderByName(candidate) is not null; n++)
            candidate = $"{baseName}-{n}";
        return candidate;
    }

    private static void SetOrRemove(Dictionary<string, object?> d, string key, string value)
    {
        if (value.Length > 0) d[key] = value;
        else d.Remove(key);
    }

    /// <summary>修饰键状态:WinUI 的 KeyRoutedEventArgs 不带修饰键,走 Win32。</summary>
    private static bool IsDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    private sealed record ThemeOption(string Text, string Value);

    private sealed record LanguageOption(string Text, string Value);

    private sealed record KindOption(string Text, string Value);

    private sealed record ProxyModeOption(string Text, string Value);
}
