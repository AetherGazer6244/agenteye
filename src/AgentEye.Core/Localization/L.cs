namespace AgentEye.Core.Localization;

/// <summary>
/// 代码侧界面文案查表。zh / en 两本内置字典,启动时按 ui.language 一次性定型
/// (重启生效,运行期不变)。Core 纯静态数据零 UI 依赖;测试默认 zh-Hans。
/// XAML 静态文本同样在代码里用 L.T 回填(x:Name),不走 resw/x:Uid
/// (unpackaged 下 PrimaryLanguageOverride 不可用,已证伪)。
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
        ["form.kindLabel"] = "类型",
        ["form.kindAuto"] = "自动识别",
        ["form.urlPlaceholder"] = "官方端点可留空;中转站填完整 URL",
        ["form.detect"] = "识别:{0} — {1}",
        ["form.keyPlaceholder"] = "粘贴 key;留空则沿用已存 key",
        ["form.modelPlaceholder"] = "如 deepseek-chat;额度端点不可用时用真实对话探测",
        ["form.save"] = "保存",
        ["form.cancel"] = "取消",

        // —— provider 类型名 ——
        ["kind.deepseek"] = "DeepSeek",
        ["kind.zhipu"] = "智谱 GLM",
        ["kind.minimax"] = "MiniMax",
        ["kind.opencode_go"] = "OpenCode Go",
        ["kind.generic_openai"] = "通用 OpenAI 兼容",

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
        ["win.titleSettings"] = "AgentEye — 设置",

        // —— 总览页 ——
        ["ov.title"] = "总览",
        ["ov.empty"] = "尚未配置 provider",
        ["ov.openSettings"] = "打开设置",
        ["ov.notRefreshed"] = "尚未刷新",
        ["ov.lastRefresh"] = "上次刷新 {0}",
        ["ov.count"] = "{0} 个 provider",
        ["ov.paused"] = "已暂停",
        ["ov.unconfigured"] = "未配置",
        ["ov.error"] = "错误",
        ["ov.resume"] = "恢复轮询",
        ["ov.pause"] = "暂停此 provider",

        // —— 热键状态(设置页回显/弹窗) ——
        ["hotkey.svcDown"] = "热键服务不可用",
        ["hotkey.applyFailed"] = "热键设置失败",
        ["hotkey.regFailPrefix"] = "注册失败",
        ["hotkey.notEffective"] = "快捷键未生效",
        ["hotkey.invalidCombo"] = "组合无效",
        ["hotkey.invalidDefault"] = "请按字母/数字/功能键",
        ["hotkey.invalidExample"] = "{0}(例如 Ctrl+Shift+A)",
        ["hotkey.stDisabled"] = "未启用",
        ["hotkey.stSvcClosed"] = "热键服务已关闭",
        ["hotkey.stNoResponse"] = "热键服务无响应",
        ["hotkey.stTimeout"] = "热键设置超时",
        ["hotkey.stOff"] = "已关闭",
        ["hotkey.stInvalid"] = "组合无效:{0}",
        ["hotkey.stInUse"] = "注册失败(该组合已被其它程序占用)",
        ["hotkey.stRegError"] = "注册失败(错误 {0})",
        ["hotkey.stActive"] = "已生效:{0}",

        // —— 告警通知 ——
        ["notify.titleOne"] = "{0} 额度告警",
        ["notify.titleMany"] = "{0} 个 provider 额度告警",
        ["notify.body"] = "剩 {0}% · {1}",

        // —— 设置页弹窗/错误 ——
        ["autostart.errorTitle"] = "无法设置开机自启动",
        ["autostart.errorDefault"] = "写入启动项失败",
        ["startup.errOpen"] = "无法打开启动项注册表",
        ["startup.errDenied"] = "写入启动项被拒绝(权限不足?)",
        ["startup.errWrite"] = "写入启动项失败:{0}",
        ["form.errEmptyUrl"] = "Base URL 不能为空",
        ["form.errSave"] = "保存失败:{0}",

        // —— 删除确认 ——
        ["delete.title"] = "删除 {0}",
        ["delete.confirmInput"] = "输入 {0} 以确认",
        ["delete.body"] = "此操作会从配置里移除该 provider 与其 key。",

        // —— 更新 ——
        ["update.checking"] = "正在检查更新…",
        ["update.downloading"] = "正在下载并更新… {0}%",
        ["update.versionLine"] = "当前版本 v{0}(更新源 github.com/{1}/{2})",
        ["update.checkFailed"] = "检查更新失败:{0}",
        ["update.ignoredSuffix"] = "{0}(已忽略此版本)",
        ["update.toastTitle"] = "发现新版本",
        ["update.noneFound"] = "尚未检查到新版本",
        ["update.noAsset"] = "release 里没有可用的 AgentEye_Setup.exe 安装包",
        ["update.dirReadOnly"] = "安装目录不可写({0}),请重新运行安装器手动修复",
        ["update.downloadingAsset"] = "正在下载 {0}…",
        ["update.badChecksum"] = "安装包校验失败(sha256 不匹配),已放弃更新",
        ["update.launching"] = "正在启动安装器,应用将被关闭并自动重启…",
        ["update.cancelled"] = "已取消更新",
        ["update.failed"] = "更新失败:{0}",

        // —— 托盘 ——
        ["tray.open"] = "打开主面板",
        ["tray.widget"] = "切换小组件",
        ["tray.settings"] = "设置",
        ["tray.exit"] = "退出",
        ["tray.tooltipEmpty"] = "AgentEye — 没有已接入的 provider",
        ["tray.tooltipWorst"] = "AgentEye — {0} 已用 {1}%",
        ["tray.tooltipLevel"] = "AgentEye — {0}({1})",

        // —— 小组件 ——
        ["widget.empty"] = "未接入 provider",

        // —— Core:ConfigStore ——
        ["cfg.plaintextPrefix"] = "拒绝写盘:以下 provider 的 key 未能加密(DPAPI 不可用?)——",
        ["cfg.providersNotList"] = "配置里 providers 不是列表(实际 {0}),已置空",
        ["cfg.providersBadEntries"] = "配置里 providers 有 {0} 个非对象条目,已丢弃(provider 必须是 {{...}} 对象)",
        ["cfg.migrateDpapi"] = "DPAPI 不可用,已中止 v1 迁移:备份会写出明文密钥",
        ["cfg.migrateBackupEnc"] = "v1 备份加密失败,已中止迁移:不愿写出明文密钥",
        ["cfg.migrateBackupWrite"] = "无法写入 v1 备份({0}),已中止迁移以免密钥丢失",
        ["cfg.noBackup"] = "没有备份文件:{0}",
        ["cfg.notOurBackup"] = "{0} 不是本程序写的加密备份",
        ["cfg.backupDecrypt"] = "{0} 解密失败:DPAPI 绑定当前用户,换账户/换机器无法恢复",
        ["cfg.backupNotJson"] = "{0} 解密后的内容不是合法 JSON",
        ["cfg.saveLocked"] = "配置保存失败(文件被占用?):{0}",
        ["cfg.readLocked"] = "无法读取配置文件 {0}({1}: {2})。请关闭正在占用它的程序(记事本/编辑器/另一个 AgentEye 实例)后重试;为避免覆盖你的配置,本次不做任何写入。",
        ["cfg.badUtf8"] = "字节流不是合法 UTF-8({0})",
        ["cfg.badJson"] = "根节点不是对象或 JSON 解析失败",
        ["cfg.versionTooNew"] = "配置版本 schema_version={0} 高于本程序支持的 {1}。请升级 AgentEye,或手动改回 2。",
        ["cfg.versionBadType"] = "schema_version 类型异常: {0}",
        ["cfg.renameFail"] = "(重命名失败,本次不会覆盖原文件)",

        // —— Core:HotkeySpec ——
        ["hk.empty"] = "快捷键为空",
        ["hk.unknownKey"] = "无法识别的按键:{0}",
        ["hk.multiMain"] = "只能有一个主键(如 A / F2 / Space)",
        ["hk.noMain"] = "缺少主键(如 A / F2 / Space)",
        ["hk.noMod"] = "至少需要一个修饰键(Ctrl / Alt / Shift / Win)",
        ["hk.invalidKey"] = "无效按键",
        ["hk.modOnly"] = "请按字母/数字/功能键,不能只按修饰键",
        ["hk.unsupported"] = "暂不支持的按键(0x{0})",

        // —— Core:provider 通用 ——
        ["pr.noKey"] = "未配置 api_key",
        ["pr.netErr"] = "网络错误: 网络异常",
        ["pr.keyInvalid"] = "key 无效 (HTTP {0})",
        ["pr.notJson"] = "响应非 JSON",
        ["pr.apiError"] = "接口报错: {0}",
        ["pr.structUnknown"] = "响应结构未识别",
        ["pr.detailGranted"] = "赠金 {0}{1} · 充值 {2}{3}",
        ["pr.detailRemainUsd"] = "剩余 ${0} · 已用 ${1}",
        ["pr.reset"] = " · 重置 {0}",

        // —— Core:DeepSeek ——
        ["ds.noBalanceInfos"] = "响应无 balance_infos",
        ["ds.noBalance"] = " · 余额不可用",

        // —— Core:GenericOpenAI ——
        ["gen.noBaseUrl"] = "缺少 base_url",
        ["gen.noKey"] = "缺少 key",
        ["gen.labelNet"] = "{0}: 网络错误",
        ["gen.labelNotJson"] = "{0}: 非JSON",
        ["gen.labelStruct"] = "{0}: 结构未识别",
        ["gen.probeNet"] = "模型探测: 网络错误",
        ["gen.probeOk"] = "key+model 可用 (模型探测通过)",
        ["gen.probe404"] = "模型探测: 端点或模型不存在 (HTTP 404)",
        ["gen.probeHttp"] = "模型探测: HTTP {0}",
        ["gen.noModels"] = "该 provider 无 /v1/models 端点",
        ["gen.detailUsedTotal"] = "已用 ${0} · 总额 ${1}",
        ["gen.detailUsedLimit"] = "已用 ${0} · 额度 ${1}",

        // —— Core:MiniMax ——
        ["mm.noQuota"] = "套餐未启用或无可用额度项",
        ["mm.noPct"] = "无百分比字段",
        ["mm.timesLeft"] = " {0}/{1} 次",
        ["mm.weekly"] = " · 周 {0}%",
        ["mm.moreModels"] = "…共{0}个模型",
        ["mm.disabled"] = "({0}项未启用)",

        // —— Core:OpenCodeGo ——
        ["oc.5h"] = "5h",
        ["oc.week"] = "周",
        ["oc.month"] = "月",
        ["oc.seg"] = "{0} 剩≈${1}/${2}",
        ["oc.noWindows"] = "响应无窗口数据",
        ["oc.resetDone"] = "已重置",

        // —— Core:Detect ——
        ["det.minimaxCp"] = "key 以 'sk-cp-' 开头 → MiniMax Coding Plan",
        ["det.urlDeepseek"] = "URL 含 'deepseek.com' → DeepSeek 官方",
        ["det.urlZhipu"] = "URL 含 'bigmodel.cn' → 智谱 GLM",
        ["det.urlMinimax"] = "URL 含 'minimaxi.com' → MiniMax",
        ["det.urlMinimaxCn"] = "URL 含 'minimax.cn' → MiniMax",
        ["det.urlOc"] = "URL 含 'opencode.ai' → OpenCode Go",
        ["det.urlV1"] = "URL 含 '/v1' → 通用 OpenAI 兼容",
        ["det.fallbackGeneric"] = "未识别 URL,按通用 OpenAI 兼容处理",
        ["det.needMore"] = "未提供 key 与 URL,需要更多信息",
        ["det.jwtAmbiguous"] = "key 是 JWT 形态,可能是 OpenCode Go,也可能是 MiniMax 的 token_plan key;填 URL 提示可纠正,新增后请核对",
        ["det.skNeedsUrl"] = "key 以 'sk-' 开头,疑似 OpenAI 兼容,需要 base_url",
        ["det.unknownKeyShape"] = "无法识别 key 形态 (长度 {0}),按通用处理,请提供 base_url",

        // —— Core:ProviderRegistry ——
        ["reg.paused"] = "已暂停轮询",
        ["reg.skippedRound"] = "已退出,本轮未拉取",
        ["reg.keyDecrypt"] = "key_enc 解密失败,请在设置里重新填写 key",
        ["reg.noKey"] = "未配置 key",
        ["reg.unknownKind"] = "未知 provider kind: {0}",
        ["reg.noResult"] = "拉取任务未返回结果",

        // —— Core:Relay ——
        ["rl.net"] = "网络异常",
        ["rl.noToken"] = "响应无 access_token",
        ["rl.noCfg"] = "未配置 base_url/token",
        ["rl.loginFailed"] = "登录失败: {0}",
        ["rl.pathNet"] = "{0}: 网络异常",
        ["rl.keyInvalidFlag"] = "key 无效 (isValid=false)",
        ["rl.unitFallback"] = "额度",
        ["rl.today"] = "今日 {0}{1}",
        ["rl.reqs"] = "{0} 次",
        ["rl.paidTotal"] = "累计实付 {0}{1}",
        ["rl.sumRemainUsd"] = "剩余 ${0}",
        ["rl.sumRemainRaw"] = "剩余 {0}{1}",
        ["rl.keyCount"] = " · {0} 个 key",
        ["rl.sumTotalUsd"] = " · 总量 ${0}",
        ["rl.sumTotalRaw"] = " · 总量 {0}",

        // —— Core:Zhipu ——
        ["zp.week"] = "周",
        ["zp.noWindows"] = "无 token 额度窗口",
        ["zp.seg"] = "{0} 剩{1}%",
        ["zp.segReset"] = "(重置{0})",
        ["zp.plan"] = " · {0}套餐",

        // —— Core:UpdateChecker ——
        ["upd.net"] = "连不上 GitHub,检查更新失败(网络问题)",
        ["upd.noReleases"] = "仓库还没有发布版本,暂时无法检查更新",
        ["upd.ratelimit"] = "GitHub 接口限流(未登录每小时 60 次),稍后再试",
        ["upd.http"] = "GitHub 返回 HTTP {0}",
        ["upd.badFormat"] = "GitHub 返回的内容不是预期的 release 格式",
        ["upd.verCompare"] = "版本号无法比较(当前 {0},远端 {1})",
        ["upd.upToDate"] = "已是最新版本 v{0}",
        ["upd.noAsset"] = "发现新版本 v{0},但 release 里没有 {1} 安装包",
        ["upd.found"] = "发现新版本 v{0}(当前 v{1})",

        // —— Core:UpdateDownloader ——
        ["dl.http"] = "下载失败 HTTP {0}",
        ["dl.incomplete"] = "下载不完整({0}/{1} 字节)",

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
        ["form.kindLabel"] = "Type",
        ["form.kindAuto"] = "Auto-detect",
        ["form.urlPlaceholder"] = "Leave empty for official endpoints; full URL for relays",
        ["form.detect"] = "Detected: {0} — {1}",
        ["form.keyPlaceholder"] = "Paste key; leave empty to keep the stored one",
        ["form.modelPlaceholder"] = "e.g. deepseek-chat; used as a live probe when the quota endpoint is unavailable",
        ["form.save"] = "Save",
        ["form.cancel"] = "Cancel",

        // —— provider kind names ——
        ["kind.deepseek"] = "DeepSeek",
        ["kind.zhipu"] = "Zhipu GLM",
        ["kind.minimax"] = "MiniMax",
        ["kind.opencode_go"] = "OpenCode Go",
        ["kind.generic_openai"] = "Generic OpenAI-compatible",

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
        ["win.titleSettings"] = "AgentEye — Settings",

        // —— Overview ——
        ["ov.title"] = "Overview",
        ["ov.empty"] = "No providers configured",
        ["ov.openSettings"] = "Open settings",
        ["ov.notRefreshed"] = "Not refreshed yet",
        ["ov.lastRefresh"] = "Last refresh {0}",
        ["ov.count"] = "{0} providers",
        ["ov.paused"] = "Paused",
        ["ov.unconfigured"] = "Not configured",
        ["ov.error"] = "Error",
        ["ov.resume"] = "Resume polling",
        ["ov.pause"] = "Pause this provider",

        // —— hotkey status (settings echo / dialog) ——
        ["hotkey.svcDown"] = "Hotkey service unavailable",
        ["hotkey.applyFailed"] = "Failed to apply hotkey",
        ["hotkey.regFailPrefix"] = "Registration failed",
        ["hotkey.notEffective"] = "Hotkey not active",
        ["hotkey.invalidCombo"] = "Invalid shortcut",
        ["hotkey.invalidDefault"] = "Press a letter, number or function key",
        ["hotkey.invalidExample"] = "{0} (e.g. Ctrl+Shift+A)",
        ["hotkey.stDisabled"] = "Not set",
        ["hotkey.stSvcClosed"] = "Hotkey service is closed",
        ["hotkey.stNoResponse"] = "Hotkey service not responding",
        ["hotkey.stTimeout"] = "Hotkey apply timed out",
        ["hotkey.stOff"] = "Off",
        ["hotkey.stInvalid"] = "Invalid shortcut: {0}",
        ["hotkey.stInUse"] = "Registration failed (shortcut already in use by another app)",
        ["hotkey.stRegError"] = "Registration failed (error {0})",
        ["hotkey.stActive"] = "Active: {0}",

        // —— alert toast ——
        ["notify.titleOne"] = "{0} balance alert",
        ["notify.titleMany"] = "{0} providers low on balance",
        ["notify.body"] = "{0}% left · {1}",

        // —— settings dialogs / errors ——
        ["autostart.errorTitle"] = "Can't change the startup setting",
        ["autostart.errorDefault"] = "Failed to write the startup entry",
        ["startup.errOpen"] = "Can't open the startup registry key",
        ["startup.errDenied"] = "Write to the startup entry was denied (insufficient permissions?)",
        ["startup.errWrite"] = "Failed to write the startup entry: {0}",
        ["form.errEmptyUrl"] = "Base URL can't be empty",
        ["form.errSave"] = "Save failed: {0}",

        // —— delete confirmation ——
        ["delete.title"] = "Delete {0}",
        ["delete.confirmInput"] = "Type {0} to confirm",
        ["delete.body"] = "This removes the provider and its key from the configuration.",

        // —— updates ——
        ["update.checking"] = "Checking for updates…",
        ["update.downloading"] = "Downloading update… {0}%",
        ["update.versionLine"] = "Version v{0} (source github.com/{1}/{2})",
        ["update.checkFailed"] = "Update check failed: {0}",
        ["update.ignoredSuffix"] = "{0} (version ignored)",
        ["update.toastTitle"] = "New version available",
        ["update.noneFound"] = "No new version found yet",
        ["update.noAsset"] = "No usable AgentEye_Setup.exe asset in the release",
        ["update.dirReadOnly"] = "Install directory is not writable ({0}); run the installer again to repair",
        ["update.downloadingAsset"] = "Downloading {0}…",
        ["update.badChecksum"] = "Installer checksum mismatch (sha256); update aborted",
        ["update.launching"] = "Launching the installer; the app will close and restart…",
        ["update.cancelled"] = "Update cancelled",
        ["update.failed"] = "Update failed: {0}",

        // —— tray ——
        ["tray.open"] = "Open main panel",
        ["tray.widget"] = "Toggle widget",
        ["tray.settings"] = "Settings",
        ["tray.exit"] = "Exit",
        ["tray.tooltipEmpty"] = "AgentEye — no providers configured",
        ["tray.tooltipWorst"] = "AgentEye — {0} at {1}% used",
        ["tray.tooltipLevel"] = "AgentEye — {0} ({1})",

        // —— widget ——
        ["widget.empty"] = "No providers",

        // —— Core: ConfigStore ——
        ["cfg.plaintextPrefix"] = "Refusing to save: keys of these providers could not be encrypted (DPAPI unavailable?) — ",
        ["cfg.providersNotList"] = "providers in the config is not a list (got {0}); reset to empty",
        ["cfg.providersBadEntries"] = "{0} non-object entries in providers were dropped (each provider must be a {{...}} object)",
        ["cfg.migrateDpapi"] = "DPAPI unavailable; v1 migration aborted (the backup would contain plaintext keys)",
        ["cfg.migrateBackupEnc"] = "Failed to encrypt the v1 backup; migration aborted (plaintext keys are never written)",
        ["cfg.migrateBackupWrite"] = "Can't write the v1 backup ({0}); migration aborted to avoid losing keys",
        ["cfg.noBackup"] = "Backup file not found: {0}",
        ["cfg.notOurBackup"] = "{0} is not an encrypted backup written by this app",
        ["cfg.backupDecrypt"] = "Failed to decrypt {0}: DPAPI is bound to the current user and can't be restored on another account or machine",
        ["cfg.backupNotJson"] = "Decrypted content of {0} is not valid JSON",
        ["cfg.saveLocked"] = "Failed to save the config (file in use?): {0}",
        ["cfg.readLocked"] = "Can't read the config file {0} ({1}: {2}). Close any program using it (Notepad / editor / another AgentEye instance) and retry; nothing will be written this time to avoid overwriting your config.",
        ["cfg.badUtf8"] = "File bytes are not valid UTF-8 ({0})",
        ["cfg.badJson"] = "Root is not an object, or JSON parsing failed",
        ["cfg.versionTooNew"] = "Config schema_version={0} is newer than this build supports ({1}). Upgrade AgentEye, or set it back to 2 manually.",
        ["cfg.versionBadType"] = "schema_version has an unexpected type: {0}",
        ["cfg.renameFail"] = " (rename failed; the original file won't be overwritten this time)",

        // —— Core: HotkeySpec ——
        ["hk.empty"] = "Hotkey is empty",
        ["hk.unknownKey"] = "Unrecognized key: {0}",
        ["hk.multiMain"] = "Only one main key is allowed (e.g. A / F2 / Space)",
        ["hk.noMain"] = "Missing main key (e.g. A / F2 / Space)",
        ["hk.noMod"] = "At least one modifier key is required (Ctrl / Alt / Shift / Win)",
        ["hk.invalidKey"] = "Invalid key",
        ["hk.modOnly"] = "Press a letter, number or function key; modifiers alone aren't allowed",
        ["hk.unsupported"] = "Unsupported key (0x{0})",

        // —— Core: providers, common ——
        ["pr.noKey"] = "api_key not configured",
        ["pr.netErr"] = "Network error",
        ["pr.keyInvalid"] = "Invalid key (HTTP {0})",
        ["pr.notJson"] = "Response is not JSON",
        ["pr.apiError"] = "API error: {0}",
        ["pr.structUnknown"] = "Unrecognized response structure",
        ["pr.detailGranted"] = "Granted {0}{1} · Topped up {2}{3}",
        ["pr.detailRemainUsd"] = "Remaining ${0} · Used ${1}",
        ["pr.reset"] = " · resets {0}",

        // —— Core: DeepSeek ——
        ["ds.noBalanceInfos"] = "Response has no balance_infos",
        ["ds.noBalance"] = " · balance unavailable",

        // —— Core: GenericOpenAI ——
        ["gen.noBaseUrl"] = "base_url missing",
        ["gen.noKey"] = "key missing",
        ["gen.labelNet"] = "{0}: network error",
        ["gen.labelNotJson"] = "{0}: not JSON",
        ["gen.labelStruct"] = "{0}: unrecognized structure",
        ["gen.probeNet"] = "Model probe: network error",
        ["gen.probeOk"] = "key+model work (model probe passed)",
        ["gen.probe404"] = "Model probe: endpoint or model not found (HTTP 404)",
        ["gen.probeHttp"] = "Model probe: HTTP {0}",
        ["gen.noModels"] = "This provider has no /v1/models endpoint",
        ["gen.detailUsedTotal"] = "Used ${0} · Total ${1}",
        ["gen.detailUsedLimit"] = "Used ${0} · Limit ${1}",

        // —— Core: MiniMax ——
        ["mm.noQuota"] = "Plan disabled or no usable quota items",
        ["mm.noPct"] = "No percentage fields",
        ["mm.timesLeft"] = " {0}/{1} left",
        ["mm.weekly"] = " · weekly {0}%",
        ["mm.moreModels"] = "…{0} models in total",
        ["mm.disabled"] = " ({0} disabled)",

        // —— Core: OpenCodeGo ——
        ["oc.5h"] = "5h",
        ["oc.week"] = "wk",
        ["oc.month"] = "mo",
        ["oc.seg"] = "{0} ≈${1}/${2} left",
        ["oc.noWindows"] = "Response has no window data",
        ["oc.resetDone"] = "Reset",

        // —— Core: Detect ——
        ["det.minimaxCp"] = "Key starts with 'sk-cp-' → MiniMax Coding Plan",
        ["det.urlDeepseek"] = "URL contains 'deepseek.com' → DeepSeek official",
        ["det.urlZhipu"] = "URL contains 'bigmodel.cn' → Zhipu GLM",
        ["det.urlMinimax"] = "URL contains 'minimaxi.com' → MiniMax",
        ["det.urlMinimaxCn"] = "URL contains 'minimax.cn' → MiniMax",
        ["det.urlOc"] = "URL contains 'opencode.ai' → OpenCode Go",
        ["det.urlV1"] = "URL contains '/v1' → generic OpenAI-compatible",
        ["det.fallbackGeneric"] = "URL not recognized; treating as generic OpenAI-compatible",
        ["det.needMore"] = "Neither key nor URL provided; more info needed",
        ["det.jwtAmbiguous"] = "Key looks like a JWT: could be OpenCode Go, or a MiniMax token_plan key. Providing the URL hint disambiguates; double-check after adding",
        ["det.skNeedsUrl"] = "Key starts with 'sk-' (likely OpenAI-compatible); base_url is required",
        ["det.unknownKeyShape"] = "Unrecognized key shape (length {0}); treated as generic — provide base_url",

        // —— Core: ProviderRegistry ——
        ["reg.paused"] = "Polling paused",
        ["reg.skippedRound"] = "Shutting down; fetch skipped",
        ["reg.keyDecrypt"] = "Failed to decrypt key_enc; please re-enter the key in Settings",
        ["reg.noKey"] = "key not configured",
        ["reg.unknownKind"] = "Unknown provider kind: {0}",
        ["reg.noResult"] = "Fetch task returned no result",

        // —— Core: Relay ——
        ["rl.net"] = "Network error",
        ["rl.noToken"] = "Response has no access_token",
        ["rl.noCfg"] = "base_url/token not configured",
        ["rl.loginFailed"] = "Login failed: {0}",
        ["rl.pathNet"] = "{0}: network error",
        ["rl.keyInvalidFlag"] = "Invalid key (isValid=false)",
        ["rl.unitFallback"] = "credit",
        ["rl.today"] = "Today {0}{1}",
        ["rl.reqs"] = "{0} calls",
        ["rl.paidTotal"] = "Total paid {0}{1}",
        ["rl.sumRemainUsd"] = "Remaining ${0}",
        ["rl.sumRemainRaw"] = "Remaining {0}{1}",
        ["rl.keyCount"] = " · {0} keys",
        ["rl.sumTotalUsd"] = " · total ${0}",
        ["rl.sumTotalRaw"] = " · total {0}",

        // —— Core: Zhipu ——
        ["zp.week"] = "wk",
        ["zp.noWindows"] = "No token quota windows",
        ["zp.seg"] = "{0} {1}% left",
        ["zp.segReset"] = " (resets {0})",
        ["zp.plan"] = " · {0} plan",

        // —— Core: UpdateChecker ——
        ["upd.net"] = "Can't reach GitHub; update check failed (network issue)",
        ["upd.noReleases"] = "The repository has no releases yet; nothing to check",
        ["upd.ratelimit"] = "GitHub API rate-limited (60/hour unauthenticated); try again later",
        ["upd.http"] = "GitHub returned HTTP {0}",
        ["upd.badFormat"] = "GitHub response is not the expected release format",
        ["upd.verCompare"] = "Versions can't be compared (current {0}, remote {1})",
        ["upd.upToDate"] = "Already up to date (v{0})",
        ["upd.noAsset"] = "New version v{0} found, but the release has no {1} installer",
        ["upd.found"] = "New version v{0} found (current v{1})",

        // —— Core: UpdateDownloader ——
        ["dl.http"] = "Download failed (HTTP {0})",
        ["dl.incomplete"] = "Download incomplete ({0}/{1} bytes)",

        // —— language switch ——
        ["lang.saved"] = "Language preference saved",
        ["lang.restart.msg"] = "Takes effect after the app restarts. Restart now?",
        ["lang.restart.now"] = "Restart now",
        ["common.close"] = "OK",
        ["common.error"] = "Notice",
        ["app.restartFailed"] = "Automatic restart failed, please restart the app manually",
    };
}
