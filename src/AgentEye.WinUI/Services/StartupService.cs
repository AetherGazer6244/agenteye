using System.Diagnostics;
using Microsoft.Win32;

namespace AgentEye.WinUI.Services;

/// <summary>
/// 开机自启动:写 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 的
/// "AgentEye" 值(与安装器 AgentEye_Setup.iss 里那条同键同格式,互相识别)。
///
/// - HKCU 写入:当前用户即可,不需要管理员(与安装器 PrivilegesRequired=lowest 一致)
/// - 命令行带 --minimized:开机静默进托盘,不弹主面板(应用侧在 App.xaml.cs 解析)
/// - 增强面:注册表读写失败只返回 false + 原因,不抛异常、不带崩主流程
/// </summary>
public static class StartupService
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AgentEye";

    /// <summary>开机自启是否已开启(注册表里存在该项即算开启)。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>注册表里当前登记的命令行(空 = 未登记)。</summary>
    public static string? CurrentCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>开启/关闭开机自启;失败返回 false 并给出原因。</summary>
    public static bool SetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunSubKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunSubKey, writable: true);
            if (key is null)
            {
                error = "无法打开启动项注册表";
                return false;
            }
            if (enabled)
            {
                key.SetValue(ValueName, CommandLine(), RegistryValueKind.String);
            }
            else
            {
                if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException)
        {
            error = "写入启动项被拒绝(权限不足?)";
            return false;
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            error = $"写入启动项失败:{e.Message}";
            return false;
        }
    }

    /// <summary>登记的命令行:&quot;exe 路径&quot; --minimized。</summary>
    public static string CommandLine() => $"\"{ExecutablePath()}\" --minimized";

    /// <summary>当前进程的可执行文件路径(开发态指向 bin 目录)。</summary>
    public static string ExecutablePath()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path)) return path;
        }
        catch { /* 兜底走 MainModule */ }
        try
        {
            using var proc = Process.GetCurrentProcess();
            return proc.MainModule?.FileName ?? "AgentEye.exe";
        }
        catch
        {
            return "AgentEye.exe";
        }
    }

    /// <summary>命令行里是否带了 --minimized(开机静默进托盘)。</summary>
    public static bool HasMinimizedFlag()
    {
        try
        {
            return Environment.GetCommandLineArgs()
                .Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}
