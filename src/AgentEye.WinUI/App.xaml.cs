using AgentEye.Core.Config;
using AgentEye.Core.Localization;
using AgentEye.WinUI.Services;
using Microsoft.UI.Xaml;

namespace AgentEye;

/// <summary>
/// 应用入口:负责窗口生命周期与全局服务装配(W 阶段逐步接入)。
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private SingleInstance? _single;

    public App()
    {
        // 语言在装配期一次定型(重启生效)。unpackaged 下 MRT 的
        // PrimaryLanguageOverride 不可用(unpackaged 抛 InvalidOperationException),
        // 所以双语不走 resw/x:Uid——XAML 文本全部由代码回填 L.T。
        try
        {
            var outcome = new ConfigStore().LoadV2();
            var lang = outcome.Config.TryGetValue("ui", out var u) &&
                       u is Dictionary<string, object?> ui &&
                       ui.TryGetValue("language", out var l) && l is string ls && ls.Length > 0
                ? ls : "zh-Hans";
            L.Init(lang);
        }
        catch
        {
            // 配置读不了按默认语言
            L.Init("zh-Hans");
        }
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 单实例互斥(1.5.3):已有实例在跑就把它的面板叫到前台,自己退出——
        // 不建窗口/托盘/小组件/热键/轮询,免得抢全局热键(1409 占用报错)
        // + 双份轮询。互斥建不起来时降级成多实例照常跑(增强面契约)。
        var minimized = StartupService.HasMinimizedFlag();
        _single = SingleInstance.TryAcquire(minimized, showPanel =>
        {
            if (_window is MainWindow m) m.ActivateFromSecondInstance(showPanel);
        });
        if (_single is { IsPrimary: false })
        {
            _single.Dispose();
            _single = null;
            Environment.Exit(0); // 第二实例:消息已发给主实例,直接退
            return;
        }

        _window = new MainWindow();
        _window.Activate();
        // 开机自启带 --minimized:静默进托盘,不弹主面板
        if (minimized && _window is MainWindow main)
            main.HidePanel();
    }
}
