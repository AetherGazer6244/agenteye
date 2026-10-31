namespace AgentEye.Core.Localization;

/// <summary>
/// 代码侧界面文案查表。zh / en 两本内置字典,启动时按 ui.language 一次性定型
/// (重启生效,运行期不变)。Core 纯静态数据零 UI 依赖;测试默认 zh-Hans。
/// XAML 静态文本不走这里——那些用 Strings\{lang}\Resources.resw + x:Uid。
/// </summary>
public static class L
{
    /// <summary>当前语言:"zh-Hans"(默认)或 "en"。</summary>
    public static string Lang { get; private set; } = "zh-Hans";

    public static void Init(string lang) => Lang = lang is "en" or "en-US" ? "en" : "zh-Hans";

    /// <summary>取文案:先当前语言,缺键回退 zh,再缺回退键名自身(好排查)。</summary>
    public static string T(string key)
    {
        var dict = Lang == "en" ? En : Zh;
        if (dict.TryGetValue(key, out var v)) return v;
        return Zh.TryGetValue(key, out var z) ? z : key;
    }

    // 便于插值的格式串:fmt("{0} 已暂停", name) 按语言取模板再 string.Format
    public static string F(string key, params object?[] args) => string.Format(T(key), args);

    private static readonly Dictionary<string, string> Zh = new()
    {
        // —— 设置页:分节 ——
        ["sec.appearance"] = "外观",
        ["sec.hotkey"] = "快捷键",
        ["sec.alert"] = "告警",
        ["sec.api"] = "API 管理",
        ["sec.adv"] = "高级",
        ["sec.about"] = "关于软件",

        // —— 设置页:外观 ——
        ["theme.title"] = "外观模式",
        ["theme.subtitle"] = "更改软件的深浅色模式",
        ["theme.auto"] = "跟随系统",
        ["theme.light"] = "浅色",
        ["theme.dark"] = "深色",

        // —— 设置页:开机自启动 ——
        ["autostart.title"] = "开机自启动",
        ["autostart.subtitle"] = "开机自动在后台运行(静默进托盘,不弹主面板)",
        ["autostart.on"] = "已启用",
        ["autostart.off"] = "已关闭",

        // —— 设置页:语言 ——
        ["lang.title"] = "语言",
        ["lang.subtitle"] = "界面语言(切换后重启生效)",

        // —— 设置页:快捷键 ——
        ["hotkey.title"] = "唤醒快捷键",
        ["hotkey.subtitle"] = "设置全局唤醒和隐藏主面板的快捷键",
        ["hotkey.clear"] = "清除",

        // —— 设置页:告警 ——
        ["alert.title"] = "告警",
        ["alert.subtitle"] = "额度低于阈值时弹系统通知",
        ["alert.on"] = "告警开",
        ["alert.off"] = "告警关",
        ["alert.warnPct"] = "剩余百分比 ≤",
        ["alert.warnPctSuffix"] = "% 记为黄",
        ["alert.critAmount"] = "剩余额度 ≤",
        ["alert.critSuffix"] = "元 记为红",

        // —— 设置页:API 管理 ——
        ["api.keysLabel"] = "已接入的 Key",
        ["api.addKey"] = "添加 Key",
        ["api.edit"] = "编辑",
        ["api.delete"] = "删除",
        ["form.keyPlaceholder"] = "粘贴 key;留空则沿用已存 key",
        ["form.modelPlaceholder"] = "如 deepseek-chat;额度端点不可用时用真实对话探测",
        ["form.save"] = "保存",
        ["form.cancel"] = "取消",

        // —— 设置页:高级 ——
        ["adv.sizeTitle"] = "主窗口面板大小",
        ["adv.sizeSubtitle"] = "主面板初始尺寸(320×400 ~ 800×1200 像素)",
        ["adv.width"] = "宽",
        ["adv.height"] = "高",
        ["adv.resetSize"] = "恢复默认 420×560",

        // —— 设置页:关于软件 ——
        ["about.title"] = "关于软件",
        ["about.subtitle"] = "查看软件信息、作者与项目详情",
        ["about.details"] = "查看详情",

        // —— 设置页:更新 ——
        ["update.check"] = "检查更新",
        ["update.download"] = "下载并更新",
        ["update.ignore"] = "忽略此版本",

        // —— 主窗口 ——
        ["win.backTooltip"] = "返回总览",
        ["win.settingsTooltip"] = "设置",

        // —— 总览页 ——
        ["ov.title"] = "总览",
        ["ov.empty"] = "尚未配置 provider",
        ["ov.openSettings"] = "打开设置",
        ["ov.notRefreshed"] = "尚未刷新",

        // —— 语言切换 ——
        ["lang.saved"] = "语言设置已保存",
        ["lang.restart.msg"] = "重启应用后生效。要现在重启吗?",
        ["lang.restart.now"] = "立即重启",
        ["common.close"] = "好",
        ["common.error"] = "提示",
        ["app.restartFailed"] = "自动重启失败,请手动重启应用",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        // —— Settings: sections ——
        ["sec.appearance"] = "Appearance",
        ["sec.hotkey"] = "Hotkey",
        ["sec.alert"] = "Alerts",
        ["sec.api"] = "API keys",
        ["sec.adv"] = "Advanced",
        ["sec.about"] = "About",

        // —— Settings: appearance ——
        ["theme.title"] = "Theme",
        ["theme.subtitle"] = "Switch between light and dark mode",
        ["theme.auto"] = "Follow system",
        ["theme.light"] = "Light",
        ["theme.dark"] = "Dark",

        // —— Settings: startup ——
        ["autostart.title"] = "Run at startup",
        ["autostart.subtitle"] = "Start automatically in the background at login (silent, tray only)",
        ["autostart.on"] = "On",
        ["autostart.off"] = "Off",

        // —— Settings: language ——
        ["lang.title"] = "Language",
        ["lang.subtitle"] = "UI language (takes effect after restart)",

        // —— Settings: hotkey ——
        ["hotkey.title"] = "Wake-up hotkey",
        ["hotkey.subtitle"] = "Global hotkey to show or hide the main panel",
        ["hotkey.clear"] = "Clear",

        // —— Settings: alerts ——
        ["alert.title"] = "Alerts",
        ["alert.subtitle"] = "Notify when balance falls below a threshold",
        ["alert.on"] = "On",
        ["alert.off"] = "Off",
        ["alert.warnPct"] = "Remaining % ≤",
        ["alert.warnPctSuffix"] = "% → yellow",
        ["alert.critAmount"] = "Remaining balance ≤",
        ["alert.critSuffix"] = "CNY → red",

        // —— Settings: API keys ——
        ["api.keysLabel"] = "Configured keys",
        ["api.addKey"] = "Add key",
        ["api.edit"] = "Edit",
        ["api.delete"] = "Delete",
        ["form.keyPlaceholder"] = "Paste key; leave empty to keep the stored one",
        ["form.modelPlaceholder"] = "e.g. deepseek-chat; used as a live probe when the quota endpoint is unavailable",
        ["form.save"] = "Save",
        ["form.cancel"] = "Cancel",

        // —— Settings: advanced ——
        ["adv.sizeTitle"] = "Panel size",
        ["adv.sizeSubtitle"] = "Main panel initial size (320×400 ~ 800×1200 px)",
        ["adv.width"] = "Width",
        ["adv.height"] = "Height",
        ["adv.resetSize"] = "Reset to 420×560",

        // —— Settings: about ——
        ["about.title"] = "About",
        ["about.subtitle"] = "App info, author and project links",
        ["about.details"] = "Details",

        // —— Settings: updates ——
        ["update.check"] = "Check for updates",
        ["update.download"] = "Download & update",
        ["update.ignore"] = "Skip this version",

        // —— Main window ——
        ["win.backTooltip"] = "Back to overview",
        ["win.settingsTooltip"] = "Settings",

        // —— Overview ——
        ["ov.title"] = "Overview",
        ["ov.empty"] = "No providers configured",
        ["ov.openSettings"] = "Open settings",
        ["ov.notRefreshed"] = "Not refreshed yet",

        // —— language switch ——
        ["lang.saved"] = "Language preference saved",
        ["lang.restart.msg"] = "Takes effect after the app restarts. Restart now?",
        ["lang.restart.now"] = "Restart now",
        ["common.close"] = "OK",
        ["common.error"] = "Notice",
        ["app.restartFailed"] = "Automatic restart failed, please restart the app manually",
    };
}
