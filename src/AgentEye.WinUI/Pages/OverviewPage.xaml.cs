using AgentEye.Core.Localization;
using AgentEye.Core.Providers;
using AgentEye.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AgentEye.WinUI.Pages;

/// <summary>
/// 总览页:provider 行卡片化直排在玻璃上(白卡 + 1px 描边 + 圆角 8;
/// 28px 图标底板 + 名称/等级 pill/detail + 18px 主值 + 4px 等级条),
/// 行随刷新整批重建,对齐 Python 版 Panel 的重建语义。
/// </summary>
public sealed partial class OverviewPage : UserControl
{
    private readonly AppServices _app;
    private readonly MainWindow _main;

    public OverviewPage(AppServices app, MainWindow main)
    {
        InitializeComponent();
        _app = app;
        _main = main;
        // 静态文案回填(语言启动定型,重启生效)
        TxtOverviewTitle.Text = L.T("ov.title");
        TxtEmpty.Text = L.T("ov.empty");
        BtnOpenSettings.Content = L.T("ov.openSettings");
        FooterUpdated.Text = L.T("ov.notRefreshed");
        BtnProxyRetry.Content = L.T("ov.proxyRetry");
        BtnProxyRetry.Click += (_, _) => _ = _app.Poller.RefreshNowAsync();
        Loaded += (_, _) =>
        {
            _app.ResultsChanged += OnResultsChanged;
            OnResultsChanged();
        };
        Unloaded += (_, _) => _app.ResultsChanged -= OnResultsChanged;
        // 系统深浅切换(auto 模式)时重渲染代码画的行
        ActualThemeChanged += (_, _) => OnResultsChanged();
    }

    /// <summary>主题切换后的调色刷新(XAML ThemeResource 自动,这里刷代码画的行)。</summary>
    public void RefreshPalette() => OnResultsChanged();

    private void OnResultsChanged()
    {
        var rows = _app.Results;
        FooterUpdated.Text = _app.Poller.LastRefresh is { } t
            ? L.F("ov.lastRefresh", t.ToString("HH:mm:ss"))
            : L.T("ov.notRefreshed");
        FooterCount.Text = rows.Count == 0 ? "" : L.F("ov.count", rows.Count);
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 代理兜底横幅:Auto 模式判死系统代理并已改直连时提示(1.5.2)
        var proxy = Http.Status;
        var showBanner = proxy.Suspect && proxy.Proxy is not null;
        ProxyBanner.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
        if (showBanner) BannerText.Text = L.F("ov.proxyBanner", proxy.Proxy!.Authority);

        RowsHost.Children.Clear();
        foreach (var row in rows)
            RowsHost.Children.Add(BuildRow(row));
    }

    private Brush LevelBrush(string level) => AeRes.Brush(this, level switch
    {
        Levels.Ok => "AeOkBrush",
        Levels.Warn => "AeWarnBrush",
        Levels.Critical => "AeCriticalBrush",
        Levels.Error => "AeErrorBrush",
        Levels.Unconfigured => "AePausedBrush",
        Levels.Paused => "AePausedBrush",
        _ => "AePausedBrush",
    });

    private Brush TextBrush() => AeRes.Brush(this, "AeTextPrimaryBrush");
    private Brush DimBrush() => AeRes.Brush(this, "AeTextSecondaryBrush");

    /// <summary>等级色的淡版,当进度条轨道用(0% 剩余时也能看出等级)。</summary>
    private static Brush LevelTrack(Brush level) => level is SolidColorBrush s
        ? new SolidColorBrush(Windows.UI.Color.FromArgb(28, s.Color.R, s.Color.G, s.Color.B))
        : level;

    /// <summary>kind → Segoe Fluent Icons 字形,未知 kind 落其它。</summary>
    private static string KindGlyph(string kind) => kind switch
    {
        "deepseek" => "\uE99A",
        "zhipu" => "\uE943",
        "minimax" => "\uE945",
        "opencode_go" => "\uE756",
        "relay" => "\uE968",
        _ => "\uE774",
    };

    /// <summary>名称旁等级 pill 的配色与文案;暂停行显示"已暂停"。</summary>
    private (Brush Brush, string Text) LevelPillInfo(ProviderResult row)
    {
        if (row.Paused || row.Level == Levels.Paused)
            return (AeRes.Brush(this, "AePausedBrush"), L.T("ov.paused"));
        return row.Level switch
        {
            Levels.Ok => (AeRes.Brush(this, "AeOkBrush"), L.T("ov.level.ok")),
            Levels.Warn => (AeRes.Brush(this, "AeWarnBrush"), L.T("ov.level.warn")),
            Levels.Critical => (AeRes.Brush(this, "AeCriticalBrush"), L.T("ov.level.critical")),
            Levels.Error => (AeRes.Brush(this, "AeErrorBrush"), L.T("ov.error")),
            _ => (AeRes.Brush(this, "AePausedBrush"), L.T("ov.unconfigured")),
        };
    }

    /// <summary>等级 pill:淡等级底 + 等级色字(与进度条轨道同一套淡版算法)。</summary>
    private FrameworkElement BuildLevelPill(Brush brush, string text)
    {
        var tint = brush is SolidColorBrush s
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(30, s.Color.R, s.Color.G, s.Color.B))
            : brush;
        return new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            Background = tint,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 11, Foreground = brush },
        };
    }

    private FrameworkElement BuildRow(ProviderResult row)
    {
        // 卡片容器:白底 + 1px 描边 + 圆角 8,行与行靠卡片边界分隔(无分割线)
        var card = new Border
        {
            Background = AeRes.Brush(this, "AeCardBrush"),
            BorderBrush = AeRes.Brush(this, "AeCardStrokeBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
        };
        var stack = new StackPanel { Spacing = 8 };

        // 上半:28px 图标底板 + (名称行 / detail) + 主值
        var top = new Grid { ColumnSpacing = 10 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pausedBrush = AeRes.Brush(this, "AePausedBrush");
        var iconChip = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(6),
            Background = AeRes.Brush(this, "AeSubtleFillBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 14,
                Glyph = KindGlyph(row.Kind),
                Foreground = row.Paused ? pausedBrush : DimBrush(),
            },
        };
        Grid.SetColumn(iconChip, 0);
        top.Children.Add(iconChip);

        var mid = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

        // 名称 + 等级 pill(暂停时 pill 显示"已暂停",替代旧的行内"已暂停"小字)
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(new TextBlock
        {
            Text = row.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = TextBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            // 列宽约束 + 裁剪;不设 MaxWidth,窄窗口靠 Grid 自适应
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var (pillBrush, pillText) = LevelPillInfo(row);
        nameRow.Children.Add(BuildLevelPill(pillBrush, pillText));
        mid.Children.Add(nameRow);

        // detail:允许两行折行(长文案不再被单行截断),超出再裁
        var detail = row.Detail;
        if (row.Error is not null) detail = row.Error;
        mid.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = DimBrush(),
        });
        Grid.SetColumn(mid, 1);
        top.Children.Add(mid);

        // 右列:主值 18 SemiBold;余额类在下方补一行 11 Secondary 单位
        var valueStack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var mainValue = new TextBlock
        {
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = TextBrush(),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        string? unitLine = null;
        if (row.Level == Levels.Unconfigured)
        {
            // 未配置:降级成次级文案,不抢主值的视觉位
            mainValue.FontSize = 12;
            mainValue.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            mainValue.Foreground = DimBrush();
            mainValue.Text = L.T("ov.unconfigured");
        }
        else if (row.Error is not null)
        {
            mainValue.Text = "!";
            mainValue.FontSize = 16;
            mainValue.Foreground = LevelBrush(row.Level);
        }
        else if (row.Pct is not null)
            mainValue.Text = $"{Http.F0(row.Pct.Value)}%";
        else if (row.Remaining is not null)
        {
            mainValue.Text = Http.F2(row.Remaining.Value);
            unitLine = row.Unit;
        }
        else
            mainValue.Text = row.Level == Levels.Error ? L.T("ov.error") : "-";

        valueStack.Children.Add(mainValue);
        if (unitLine is not null)
            valueStack.Children.Add(new TextBlock
            {
                Text = unitLine,
                FontSize = 11,
                Foreground = DimBrush(),
                HorizontalAlignment = HorizontalAlignment.Right,
            });
        Grid.SetColumn(valueStack, 2);
        top.Children.Add(valueStack);

        stack.Children.Add(top);

        // 下半:4px 等级色条(收在卡片内,圆角 2;无值时空轨)
        var level = LevelBrush(row.Level);
        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Foreground = level,
            // 轨道也带等级色:剩余 0% 时填充为空,不然等级完全看不出来
            Background = LevelTrack(level),
        };
        if (row.Pct is not null) bar.Value = Math.Clamp(row.Pct.Value, 0, 100);
        stack.Children.Add(bar);

        card.Child = stack;

        // 行右键菜单(对齐 Python row_menu:暂停/编辑/删除)
        var menu = new MenuFlyout();
        var miPause = new MenuFlyoutItem
        { Text = _app.Paused.Contains(row.Name) ? L.T("ov.resume") : L.T("ov.pause") };
        miPause.Click += (_, _) => TogglePause(row.Name);
        menu.Items.Add(miPause);
        var miEdit = new MenuFlyoutItem { Text = L.T("api.edit") };
        miEdit.Click += (_, _) =>
        {
            _main.GoSettings();
            _main.Settings.EditProvider(row.Name);
        };
        menu.Items.Add(miEdit);
        var miDelete = new MenuFlyoutItem { Text = L.T("api.delete") };
        miDelete.Click += async (_, _) => await SettingsPage.ConfirmDeleteAsync(
            _app, this, row.Name);
        menu.Items.Add(miDelete);
        card.ContextFlyout = menu;

        return card;
    }

    private void TogglePause(string name)
    {
        if (!_app.Paused.Remove(name)) _app.Paused.Add(name);
        _ = _app.Poller.RefreshNowAsync();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
        => _main.GoSettings();
}
