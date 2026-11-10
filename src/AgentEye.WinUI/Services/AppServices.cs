using AgentEye.Core.Cache;
using AgentEye.Core.Config;
using AgentEye.Core.Hotkeys;
using AgentEye.Core.Providers;

namespace AgentEye.WinUI.Services;

/// <summary>
/// 应用级中心状态:配置仓库、轮询服务、暂停集合、主题。
/// 对齐 Python 版 main.build_actions 的装配面——UI 层只从这里拿能力,
/// 不直接摸 Core 细节。
/// </summary>
public sealed class AppServices
{
    public static AppServices Instance { get; private set; } = null!;

    public ConfigStore Store { get; }
    public CacheStore Cache { get; }
    public Dictionary<string, object?> Cfg { get; private set; }
    public HashSet<string> Paused { get; } = new();
    public List<ProviderResult> Results { get; private set; } = [];
    public LoadOutcome? LastOutcome { get; private set; }

    public PollerService Poller { get; }
    public NotifyService Notify { get; }
    public UpdateService Update { get; }

    /// <summary>每轮刷新完成后触发(UI 线程)。</summary>
    public event Action? ResultsChanged;
    /// <summary>配置保存成功后触发(设置页回显等)。</summary>
    public event Action? ConfigSaved;

    public AppServices()
    {
        Instance = this;
        Store = new ConfigStore();
        Cache = CacheStore.Default;
        try
        {
            LastOutcome = Store.LoadV2();
            Cfg = LastOutcome.Config;
        }
        catch (ConfigError)
        {
            // 配置不可用(读锁等):按空配置跑,禁止任何写盘,等用户处理
            Cfg = ConfigStore.Template();
        }
        // 代理模式启动注入(Core.Http 静态属性,零 UI 依赖)
        Http.Mode = Http.ParseMode(ProxyModeChoice);
        Poller = new PollerService(this);
        Notify = new NotifyService();
        Update = new UpdateService(this);
    }

    public bool TrySaveCfg()
    {
        try
        {
            Store.SaveV2(Cfg);
            ConfigSaved?.Invoke();
            return true;
        }
        catch (ConfigError)
        {
            // 写盘失败转 UI 提示,不让调用方以为"界面变了就存下来了"
            return false;
        }
    }

    public string? SaveCfgError()
    {
        try
        {
            Store.SaveV2(Cfg);
            ConfigSaved?.Invoke();
            return null;
        }
        catch (ConfigError e)
        {
            return e.Message;
        }
    }

    public void SetResults(List<ProviderResult> results)
    {
        Results = results;
        ResultsChanged?.Invoke();
    }

    public string ThemeChoice =>
        Cfg.TryGetValue("ui", out var u) && u is Dictionary<string, object?> ui &&
        ui.TryGetValue("theme", out var t) && t is string ts ? ts : "auto";

    public void SetThemeChoice(string choice)
    {
        var ui = UiDict();
        ui["theme"] = choice;
    }

    /// <summary>
    /// 代理模式(ui.proxy_mode):direct=直连优先,连不上才回退系统代理(缺省)/
    /// auto=跟随系统代理(代理优先)/ system=严格跟随不做兜底。ui 自由字典加键,
    /// schema_version 仍为 2,Python 版 merge 会保留未知键。
    /// </summary>
    public string ProxyModeChoice =>
        Cfg.TryGetValue("ui", out var u) && u is Dictionary<string, object?> ui &&
        ui.TryGetValue("proxy_mode", out var pm) && pm is string pms && pms.Length > 0
            ? pms : "direct";

    public void SetProxyMode(string mode)
    {
        UiDict()["proxy_mode"] = mode;
        Http.Mode = Http.ParseMode(mode);
        Http.ResetProxyState(); // 别让旧的「代理疑似不可用」记忆污染新模式
    }

    /// <summary>
    /// 全局快捷键组合文本(ui.hotkey)。
    /// 键不存在 → 默认 Ctrl+Shift+A;键存在但为空 → 用户主动"清除"(热键停用)。
    /// </summary>
    public string HotkeyText
    {
        get
        {
            var ui = UiDict();
            if (!ui.TryGetValue("hotkey", out var h)) return HotkeySpec.DefaultText;
            return h is string hs ? hs.Trim() : HotkeySpec.DefaultText;
        }
    }

    /// <summary>全局快捷键开关(ui.hotkey_enabled);缺省开启。</summary>
    public bool HotkeyEnabled
    {
        get
        {
            var ui = UiDict();
            if (ui.TryGetValue("hotkey_enabled", out var e) && e is bool b) return b;
            return true;
        }
    }

    /// <summary>组合是否可用:开关打开 + 文本非空 + 能解析。</summary>
    public bool HotkeyUsable =>
        HotkeyEnabled &&
        HotkeyText.Length > 0 &&
        HotkeySpec.TryParse(HotkeyText, out _, out _);

    public void SetHotkey(string text, bool enabled)
    {
        var ui = UiDict();
        ui["hotkey"] = text;
        ui["hotkey_enabled"] = enabled;
    }

    /// <summary>开机自启动开关的镜像值;真实状态以注册表 Run 键为准。</summary>
    public bool Autostart
    {
        get
        {
            var ui = UiDict();
            if (ui.TryGetValue("autostart", out var a) && a is bool b) return b;
            return false;
        }
    }

    public void SetAutostart(bool enabled) => UiDict()["autostart"] = enabled;

    /// <summary>
    /// 关主窗行为(ui.close_to_tray):true = 点 X 隐藏进托盘(默认,进程/托盘存活),
    /// false = 点 X 直接退出(1.3 及以前的行为)。托盘菜单"退出"与更新安装不受影响。
    /// </summary>
    public bool CloseToTray
    {
        get
        {
            var ui = UiDict();
            if (ui.TryGetValue("close_to_tray", out var c) && c is bool b) return b;
            return true;
        }
    }

    public void SetCloseToTray(bool enabled) => UiDict()["close_to_tray"] = enabled;

    /// <summary>界面语言(ui.language);1.3.0 起支持中英切换。</summary>
    public string Language
    {
        get
        {
            var ui = UiDict();
            if (ui.TryGetValue("language", out var l) && l is string ls && ls.Length > 0)
                return ls;
            return "zh-Hans";
        }
    }

    public void SetLanguage(string language) => UiDict()["language"] = language;

    /// <summary>主窗口初始尺寸(物理像素,ui.width/ui.height)。</summary>
    public int WindowWidth => IntOr(UiDict(), "width", 420);

    public int WindowHeight => IntOr(UiDict(), "height", 560);

    public void SetWindowSize(int width, int height)
    {
        var ui = UiDict();
        ui["width"] = (double)width;
        ui["height"] = (double)height;
    }

    private static int IntOr(Dictionary<string, object?> ui, string key, int fallback)
    {
        if (!ui.TryGetValue(key, out var v) || v is null) return fallback;
        try
        {
            var d = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
            if (double.IsNaN(d) || double.IsInfinity(d)) return fallback;
            return (int)Math.Round(d);
        }
        catch
        {
            return fallback;
        }
    }

    public Dictionary<string, object?> UiDict()
    {
        if (Cfg.TryGetValue("ui", out var u) && u is Dictionary<string, object?> ui)
            return ui;
        var fresh = (Dictionary<string, object?>)ConfigStore.Template()["ui"]!;
        Cfg["ui"] = fresh;
        return fresh;
    }

    public Dictionary<string, object?> AlertDict()
    {
        if (Cfg.TryGetValue("alert", out var a) && a is Dictionary<string, object?> ad)
            return ad;
        var fresh = (Dictionary<string, object?>)ConfigStore.Template()["alert"]!;
        Cfg["alert"] = fresh;
        return fresh;
    }

    public List<object?> Providers() => ConfigStore.GetList(Cfg, "providers");

    /// <summary>provider 名称必须唯一:返回重名条目(用于添加/编辑校验)。</summary>
    public Dictionary<string, object?>? FindProviderByName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return Providers().OfType<Dictionary<string, object?>>()
            .FirstOrDefault(p => p.TryGetValue("name", out var n) && n as string == name);
    }

    public static string PlainKey(Dictionary<string, object?> p) => ConfigStore.PlainKey(p);
}
