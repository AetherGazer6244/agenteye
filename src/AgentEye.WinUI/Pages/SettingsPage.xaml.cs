using AgentEye.Core.Config;
using AgentEye.Core.Hotkeys;
using AgentEye.Core.Providers;
using AgentEye.Core.Update;
using AgentEye.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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

    private const string RepoUrl = "https://github.com/Cecilian-Elysian/AgentEye";

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private readonly AppServices _app;
    private readonly MainWindow _main;
    private Dictionary<string, object?>? _editing; // 非 null = 编辑模式
    private bool _loading;                          // 回显期间不触发保存
    private CancellationTokenSource? _sizeDebounce;

    private readonly List<ThemeOption> _themeOptions =
    [
        new("跟随系统", "auto"),
        new("浅色", "light"),
        new("深色", "dark"),
    ];

    // 1.2.1 只落库不切换;中英双语自 1.3.0 起生效
    private readonly List<LanguageOption> _languageOptions =
    [
        new("简体中文", "zh-Hans"),
    ];

    public SettingsPage(AppServices app, MainWindow main)
    {
        InitializeComponent();
        _app = app;
        _main = main;
        ThemeCombo.ItemsSource = _themeOptions;
        LanguageCombo.ItemsSource = _languageOptions;
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
        // 热键当前注册失败(组合被别的程序占用)时提示一次;
        // 启动阶段不弹——增强面静默降级,别在开机时打扰用户
        var status = _main.HotkeyStatus;
        if (status.StartsWith("注册失败", StringComparison.Ordinal))
            _ = ShowErrorAsync("快捷键未生效", status);
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
                CloseButtonText = "好",
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
            LanguageCombo.SelectedIndex = 0;

            HotkeyBox.Text = _app.HotkeyText;

            var alert = _app.AlertDict();
            AlertEnable.IsOn = alert.TryGetValue("enable", out var en) && en is true;
            SyncAlertThresholds();
            WarnPctBox.Value = alert.TryGetValue("warn_pct", out var w) ? Convert.ToDouble(w) : 30;
            CriticalYuanBox.Value = alert.TryGetValue("critical_amount_yuan", out var c)
                ? Convert.ToDouble(c) : 5.0;

            WidthBox.Value = _app.WindowWidth;
            HeightBox.Value = _app.WindowHeight;

            VersionText.Text = $"当前版本 v{UpdateService.CurrentVersion}" +
                               $"(更新源 github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo})";
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
            _ = ShowErrorAsync("无法设置开机自启动", error ?? "写入启动项失败");
        }
    }

    // ------------------------------------------------------------------
    // 语言(1.2.1 先落库,中英双语 1.3.0 起可切)
    // ------------------------------------------------------------------

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LanguageCombo.SelectedIndex < 0) return;
        var lang = _languageOptions[LanguageCombo.SelectedIndex].Value;
        _app.SetLanguage(lang);
        _app.TrySaveCfg();
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
            _ = ShowErrorAsync("组合无效", error ?? "请按字母/数字/功能键");
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
            _ = ShowErrorAsync("组合无效", $"{error}(例如 Ctrl+Shift+A)");
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
        // 注册失败(被别的程序占用)才提示;成功静默,界面不留状态小字
        var status = _main.HotkeyStatus;
        if (status.StartsWith("注册失败", StringComparison.Ordinal))
            _ = ShowErrorAsync("快捷键未生效", status);
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
        _app.TrySaveCfg();
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
        UpdateStatusText.Text = "正在检查更新…";
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
                UpdateStatusText.Text = $"正在下载并更新… {p}%";
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
        var input = new TextBox { PlaceholderText = $"输入 {name} 以确认" };
        var dialog = new ContentDialog
        {
            XamlRoot = host.XamlRoot,
            Title = $"删除 {name}",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "此操作会从配置里移除该 provider 与其 key。",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    input,
                },
            },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
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
        }
        finally
        {
            _loading = false;
        }
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

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var baseUrl = FBaseUrl.Text.Trim();
        var key = FKey.Text.Trim();
        var model = FModel.Text.Trim();

        if (baseUrl.Length == 0)
        {
            ShowFormError("Base URL 不能为空");
            return;
        }

        // 名称自动取 URL 域名,重名追加 -2/-3;编辑时保持原名不变
        // (改名会让暂停集合与托盘最紧张行的索引错位)
        var name = _editing is not null ? TryGetName(_editing)
            : UniqueName(NameFromUrl(baseUrl));

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
            // kind 只在新增时推断;已有条目改 kind 会破坏 extra 语义,走 config
            entry["kind"] = Detect.Run(key: key, hintUrl: baseUrl).Kind;
        }
        entry["name"] = name;
        entry["base_url"] = baseUrl;
        if (newKey.Length > 0)
            entry["key"] = newKey;
        SetOrRemove(entry, "model", model);
        entry.Remove("_key_from_env");

        var err = _app.SaveCfgError();
        if (err is not null)
        {
            // 写盘失败:把刚加的新条目回滚,别让内存态和磁盘态分叉
            if (_editing is null) ConfigStore.GetList(_app.Cfg, "providers").Remove(entry);
            ShowFormError($"保存失败:{err}");
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
}
