# Changelog

## 2026-10-31 — 1.2.2:删除暂停轮询 / 立即刷新入口

### 修复

- 主窗口头部删掉「暂停轮询 / 立即刷新」两颗按钮,头部只剩
  返回(E72B)/ 设置(E713)
- 托盘右键菜单同步删掉「暂停轮询 / 立即刷新」两项:
  **打开主面板 / 切换小组件 / — / 设置 / 退出**
- `PollerService` 的全局暂停机制(`ToggleGlobalPause` / `IsGloballyPaused`)
  随两个入口一并移除(零入口死代码)

### 说明

- 行级「暂停此 provider」(总览行右键,`Paused` 集合)不受影响
- 轮询本身照常:`PeriodicTimer` 循环 + 首启刷一轮 + 设置保存后重刷;
  想停轮询把 `refresh_interval_sec` 调大即可

## 2026-10-30 — 1.2.1:设置页改版 + 全局快捷键 + 开机自启动

### 新增

- **全局快捷键(唤醒 / 隐藏主面板)**,默认 `Ctrl+Shift+A`
  - `Core\Hotkeys\HotkeySpec.cs`:纯逻辑快捷键解析器("Ctrl+Shift+A" ⇄ 修饰键
    位 + 虚拟键码),修饰键/键码常量与 Win32 同值;校验"至少一个修饰键",
    免得裸键劫持日常打字
  - `WinUI\Services\HotkeyService.cs`:`RegisterHotKey` + 自建隐藏窗口(独立
    线程 + 自己的消息泵,照托盘范式),`MOD_NOREPEAT` 按住不连发;改键后即时
    重注册,被占用时只落状态文案
  - 设置页「唤醒快捷键」行:文本框既可手打(`ctrl+shift+a`,大小写不敏感)
    也可直接按组合键录入,「清除」= 停用热键;非法组合当场提示并回滚
  - 语义 = **唤醒 + 隐藏切换**:面板在前台时按热键藏回托盘,否则拉到前台;
    托盘左键 / 小组件点击仍是"拉回来"
  - `MainWindow.WakeUp()`:最小化先还原再置前;热键不自动给前台权,
    `SetForegroundWindow` 被系统拒绝时借前台线程(`AttachThreadInput`)再试
  - config 新增 `ui.hotkey`(默认 "Ctrl+Shift+A")与 `ui.hotkey_enabled`
    (默认 true),仍在 v2 schema 内,Python 版同源不受影响
- **开机自启动**:`WinUI\Services\StartupService.cs` 读写 HKCU
  `Software\Microsoft\Windows\CurrentVersion\Run` 的 "AgentEye" 项(与安装器
  同键同格式);并实现 `--minimized` 启动参数 → 开机静默进托盘不弹主面板
  (安装器早就在传这个参数,应用一直没接)
- **高级设置:主窗口面板大小**:宽/高数字框(320×400 ~ 800×1200)即时
  resize 并落盘 `ui.width/ui.height`,另有「恢复默认 420×560」
- **关于软件**分节:「查看详情」用系统浏览器打开项目主页
- config 新增 `ui.autostart`(镜像值,真实状态以注册表为准)与
  `ui.language`(默认 "zh-Hans")

### 变更

- **设置页整页改版**:按 Windows 11 设置的"卡片行"模板重排——图标 + 标题 +
  副说明 | 右侧控件;分节标题(外观 / 快捷键 / 告警 / API 管理 / 高级 /
  关于软件)。外观模式从单选按钮组换成下拉框(跟随系统 / 浅色 / 深色)
- 新增卡片行样式(`Styles\AeStyles.xaml`):`AeSectionHeaderStyle` /
  `AeSettingsItemStyle` / `AeSettingsItemTitleStyle` /
  `AeSettingsItemSubtitleStyle` / `AeSettingsIconStyle`
- 「语言」行先落库不切换:中英双语自 **1.3.0** 起提供(1.2.1 只有简体中文)

### 修复

- 设置页去掉常驻状态小字(热键「已生效…」、自启「已登记:…」一律不要):
  错误一律走弹窗(`ContentDialog`)——热键组合无效、热键被占用(1409)、
  自启写注册表失败;成功保持静默,界面不再堆状态行
- `GetModuleHandleW` 声明在了 user32(应为 kernel32):热键窗口线程直接抛
  `EntryPointNotFoundException`,热键服务整个起不来(静默降级,主流程无感)

### 说明

- `RegisterHotKey` 必须在**窗口所属线程**上调用,否则直接失败(1408);
  注册/注销都经自定义消息交给热键线程执行
- 组合被别的程序占用(1409)时设置页弹窗提示,应用照常运行
- 热键仅在本进程运行期间生效,不负责拉起未运行的程序
- 隐藏主面板 ≠ 退出:托盘、小组件、轮询继续跑;关主窗 X 仍是退出
- 测试基线 105 → 130

## 2026-10-28 — 1.1.1:README 重写 + 仓库截图

### 说明

- README 重排:新增「系统要求 / 下载安装 / 配置」三节(配置字段表、kind
  取值、损坏自愈、v1 迁移),功能描述补全(6 种 provider 适配器、DPAPI
  密钥安全、告警阈值与冷却细节、自动更新);删除三处个人机注记
- 新增 `docs\screenshots\`:主面板 + 桌面小组件实际截图(标题下方嵌入)
- 标题加静态徽章(版本 / 平台 / 运行时,shields.io,无 CI 依赖)
- 版本 1.1.0 → 1.1.1,无代码改动

## 2026-10-28 — 应用图标:A 标 + 瞳孔(全量接入)

### 新增

- 唯一图标源 `src\AgentEye.WinUI\Assets\AgentEye.ico`:几何 A 标 + 瞳孔
  (托盘蓝 #285A96 / 亮蓝瞳孔),16~256 九档 **32bpp BMP 条目**;
  由 `tools\make_icon.py` 生成(Pillow,dev-only,不在构建流程里),
  ≤24px 走"光学尺寸"变体(腿更粗)防糊
- csproj `ApplicationIcon` 内嵌进 exe:资源管理器 / 任务栏 / 快捷方式 /
  卸载项 / toast 图标全部统一
- 运行时 `Services\AeAppIcon.cs`:`AppWindow.SetIcon/SetTaskbarIcon/
  SetTitleBarIcon`(WinAppSDK 1.7),失败退 Win32 `WM_SETICON`;
  主窗口与桌面小组件各挂一次
- **托盘换成同一份 A 标**:新增 `Tray\TrayIconRenderer.cs`(纯 Win32/GDI,
  不依赖 WinUI)——A 标按额度紧张度染色(蓝→绿→橙→红,无数据走灰)+
  右下角白底数字牌(深色数字 + 状态色描边);ico 读不到时自动退旧样式
  (彩色圆角底 + 大数字)
- **顺带修掉旧托盘的老 bug**:旧代码用 `0xFF000000 | ...` 拼 COLORREF,
  高字节非 0 时 GDI 直接拒画(一个像素都不落)——**托盘图标一直是空的**;
  新代码统一 `0x00BBGGRR`,并修了 `CreateFontW` 少 `CharSet.Unicode`
  导致字体名不生效的问题
- Inno `SetupIconFile`:安装器图标换成同一份 A 标
- 界面内图标字形仍走 Segoe Fluent Icons,标题栏圆点不动(契约不变)

### 说明

- 托盘数字仍保留(数字牌),tooltip 语义不变;想只要图标不要数字牌改
  `TrayIconRenderer.Default` 一个常量即可
- 产物 40 文件 / 37 MB(多 369 KB 图标),安装器 7.9 MB;测试基线 105/105

## 2026-10-22 — dist/installer 形态对齐 key_note + 更新改静默安装(feat/winui-acrylic)

### 变更

- 一键构建 `build.ps1`:publish → 剪 pdb → **`dist\`**(便携目录)→ 体积报告 →
  Inno Setup 出 **`installer\AgentEye_Setup.exe`**(lzma2 约 7.6 MB),
  `build\`/`dist\`/`installer\` 全部 gitignore;删掉旧的 `release\` 约定
- 新增 `AgentEye_Setup.iss`:AppId=AgentEye 显式写死、装 `E:\AgentEye`、
  `PrivilegesRequired=lowest`、桌面快捷方式 + 可选开机自启(HKCU)、
  `CloseApplications=yes` + `RestartApplications=yes`(静默升级自动关旧进程、装完自动重启)
- **应用内更新改静默安装**:GitHub Release 资产从 zip 换成 `AgentEye_Setup.exe`;
  下载 → sha256 校验 → `/VERYSILENT` 拉起安装器;应用不再自己退出,
  关进程/替换/重启全归安装器(安装失败应用还活着)
- Core:`UpdateDownloader` 删掉 `ExtractZip`/`DetectPayloadRoot`;
  `UpdateApplier` 的 robocopy 脚本生成换成安装器命令行;
  `UpdateChecker` 资产匹配 `*.zip` → `*.exe`(精确名 `AgentEye_Setup.exe` 优先)
- 静默安装端到端验收:装进 `E:\AgentEye`(1.0.0.0、卸载注册表、冒烟通过);
  测试基线 105/105

## 2026-10-22 — 发布瘦身:框架依赖 + 裁语言包(feat/winui-acrylic)

### 变更

- 发布形态改为**框架依赖**(`SelfContained=false` + `WindowsAppSDKSelfContained=false`):
  产物从 491 文件 / 166 MB 降到 **41 文件 / 36 MB**,83 种语言卫星资源
  (`SatelliteResourceLanguages=zh-Hans;en`)一并砍掉
- `RuntimeIdentifier` 改为无条件 `win-x64`(框架依赖发布必须带 RID)
- 用户端前置依赖:.NET 8 Desktop Runtime + Windows App Runtime 1.7
  (本机均已安装);`E:\AgentEye` 已用瘦版替换,旧自包含版备份在 `E:\AgentEye.full`

## 2026-10-22 — 多尺寸自适应 + 安装到 E 盘(feat/winui-acrylic)

### 新增

- 安装到 `E:\AgentEye\`(Release 自包含发布,491 文件 / 166 MB),
  桌面与开始菜单各建一个快捷方式;配置仍在 `~\.agenteye\`,重装不丢
- 窗口尺寸约束:`WM_GETMINMAXINFO` 子类(`SetWindowSubclass`),
  允许范围 320×400 ~ 800×1200,主尺寸仍是 420×560
- 设置页、标题栏、页脚改为自适应栅格(标签列 `*`、控件列 `Auto`),
  窄窗口不再出现控件被挤没/文字被裁

### 变更

- 更新区按钮策略:发现新版时隐藏「检查更新」,只留「下载并更新 /
  忽略此版本」,窄窗口也放得下
- 几何恢复的宽度夹取放宽到 [320, 800](原 [360, 520]),其它尺寸可持久化

## 2026-10-22 — GitHub 自动更新(feat/winui-acrylic)

### 新增

- 自动更新通道:更新源为 GitHub Releases(`Cecilian-Elysian/AgentEye`)。
  - 启动时静默检查一次(同一自然日只查一次,状态存 `cache/update_state.json`)
  - 设置页「关于」区:当前版本 / 检查更新 / 下载并更新 / 忽略此版本
  - 下载 → sha256 校验(GitHub 资产 `digest`)→ 解包 → 退出应用 →
    PowerShell 辅助脚本 `robocopy /MIR` 整目录替换 → 自动拉起新版本
  - 安装目录不可写时降级为提示手动替换(不做 UAC 提权)
- Core 新增 `Update/`(版本比较、release 解析、下载解包、替换脚本生成),
  配套 53 个 xUnit 用例,测试总数 56 → 109
- `docs/RELEASE.md`:发布契约(tag 命名、资产名、zip 内容、校验、发布步骤)

### 变更

- 应用版本号写入 csproj(`<Version>2.0.0</Version>`),与 release tag 比对
- `AGENTEYE_UPDATE_API` 环境变量可覆盖 API 基址(镜像/自建实例/本地联调)

## 2026-10-21 — WinUI 3 紧凑毛玻璃面板(feat/winui-acrylic)

本分支起 AgentEye 转为 WinUI 3 实现,Python/Tkinter 原版只保留在
`main` 与 `feat/glass-redesign` 的历史里。

### 新增

- 三层工程:`src/AgentEye.Core`(纯逻辑,零 UI 依赖)、
  `src/AgentEye.WinUI`(壳)、`tests/AgentEye.Core.Tests`(xUnit,56 例)。
- 主面板:Win11 Acrylic(`DesktopAcrylicBackdrop`)+ `AeGlassTintBrush`
  着色罩(浅 `#8CF2F2F2` / 深 `#8C000000`),紧凑单窗 420×560、
  宽度夹 [360,520]。
- 头部图标按钮组(32×32 圆角 6):返回 E72B / 暂停 E769↔E768 /
  立即刷新 E72C / 设置 E713;右侧留白按
  `TitleBar.RightInset / RasterizationScale` 动态换算。
- 总览行扁平化直排玻璃:36px kind 图标 chip(deepseek E99A / zhipu E943 /
  minimax E945 / opencode_go E756 / relay E968 / 其它 E774)+
  3px 等级色细条 + 1px 分割线 + 页脚(上次刷新 / provider 数)。
- 小组件:260×64 起步、高度随行数自适应(修旧版 3 行必裁切),
  Acrylic 背板(老系统实底降级),行内 2px 等级细条。
- config 模板默认几何 420×560(原 360×360)。

### 变更

- 主窗口去 `NavigationView`:总览/设置靠可见性切换;`GoSettings()`
  保留公开入口(托盘在用),托盘来的调用统一封送 UI 线程。
- 设置页去卡片:分组透明直排,适配紧凑窗宽。
- `AeStyles.xaml` 删除零引用的 AeKpi*/AeCardStyle/AeCardCornerRadius。

### 修复

- 托盘暂停态与主面板暂停按钮字形联动(`SyncPauseGlyph` 封送)。

### 兼容性

- `~/.agenteye/config.json` v2 schema、DPAPI `key_enc`、损坏隔离、
  v1 自动迁移与 Python 版完全同源,可直接替换使用。
