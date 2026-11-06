using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;

namespace AgentEye.WinUI.Services;

/// <summary>
/// 应用图标统一入口(字母 A,托盘蓝)。
///
/// 资源:Assets\AgentEye.ico —— 同一份文件既被 csproj 的 ApplicationIcon 内嵌进 exe,
/// 也随输出复制到 exe 同级 Assets 目录,供运行时按绝对路径喂给窗口 API。
///
/// 生效顺序(任一步失败静默跳过,增强面契约:图标挂了不许把主流程带崩):
///   1) AppWindow.SetIcon / SetTaskbarIcon / SetTitleBarIcon(WinAppSDK 1.7 起可用)
///   2) Win32 WM_SETICON(老系统上 AppWindow 那套可能不可用时的兜底)
///
/// 托盘图标不在这里管:托盘是 GDI 现场画数字徽章(配色硬编码是设计决定)。
/// </summary>
public static class AeAppIcon
{
    private const uint WM_SETICON = 0x0080;
    private const nint ICON_SMALL = 0, ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0000_0010;
    private const uint LR_DEFAULTCOLOR = 0x0000_0020;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadImageW(nint hInst, string name, uint type,
        int cx, int cy, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>图标文件绝对路径;文件不在(发布形态变了)时为 null,所有设置都跳过。</summary>
    public static string? IcoPath { get; } = ResolvePath();

    private static string? ResolvePath()
    {
        try
        {
            var p = Path.Combine(AppContext.BaseDirectory, "Assets", "AgentEye.ico");
            return File.Exists(p) ? Path.GetFullPath(p) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把应用图标挂到窗口上(任务栏 + 标题栏)。任何异常都吞掉。</summary>
    public static void Apply(AppWindow? appWindow, nint hwnd)
    {
        var path = IcoPath;
        if (path is null) return;

        // 1) WinAppSDK 1.7 的原生接口
        try { appWindow?.SetIcon(path); } catch { }
        try { appWindow?.SetTaskbarIcon(path); } catch { }
        try { appWindow?.SetTitleBarIcon(path); } catch { }

        // 2) Win32 兜底
        if (hwnd == nint.Zero) return;
        TrySetWin32(hwnd, path, 16, ICON_SMALL);
        TrySetWin32(hwnd, path, 32, ICON_BIG);
    }

    private static void TrySetWin32(nint hwnd, string path, int size, nint which)
    {
        try
        {
            var hIcon = LoadImageW(nint.Zero, path, IMAGE_ICON, size, size,
                LR_LOADFROMFILE | LR_DEFAULTCOLOR);
            if (hIcon == nint.Zero) return;
            // WM_SETICON 不转移所有权:HICON 得活到窗口销毁,故意不 DestroyIcon
            SendMessageW(hwnd, WM_SETICON, which, hIcon);
        }
        catch
        {
            // 增强面:失败就算了
        }
    }
}
