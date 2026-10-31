using AgentEye.WinUI.Services;
using Microsoft.UI.Xaml;

namespace AgentEye;

/// <summary>
/// 应用入口:负责窗口生命周期与全局服务装配(W 阶段逐步接入)。
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
        // 开机自启带 --minimized:静默进托盘,不弹主面板
        if (StartupService.HasMinimizedFlag() && _window is MainWindow main)
            main.HidePanel();
    }
}
