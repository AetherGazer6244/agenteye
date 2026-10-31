# AgentEye 的工程契约(WinUI 3 版)。

## 项目定位

Windows 桌面多 Key AI 额度观察工具。UI 采用 Win11 Acrylic 紧凑毛玻璃面板
(单窗、无导航栏、图标头部),另有两个常驻面:无边框桌面小组件(不抢焦点)
与托盘图标(GDI 画 A 标数字牌:紧张度染色 + 最紧张 provider 的数字);
全局快捷键(默认 Ctrl+Shift+A)一键唤醒主面板。
**只支持 Windows 10 1809+ / Win11**,依赖 DPAPI、WinRT toast 与 WinAppSDK,
不承诺跨平台。Python/Tkinter 原版只存在于 `main` 与 `feat/glass-redesign`
的历史里,不再维护。

## 三层结构

- `src/AgentEye.Core`:net8.0 纯逻辑,**零 UI 依赖**——config v2 读写、
  DPAPI 加解密、6 种 provider 适配器、缓存、阈值判定、更新检查
  (`Update/`:版本比较、release 解析、下载校验、安装器拉起)、快捷键解析
  (`Hotkeys/HotkeySpec`)。只引 `System.Security.Cryptography.ProtectedData`。
- `src/AgentEye.WinUI`:WinUI 3 壳——MainWindow(去导航紧凑单窗)、
  Pages(总览/设置)、Widget(小组件)、Tray(托盘)、Services(AeRes /
  Poller / Notify / AppServices / HotkeyService / StartupService)、
  Styles(AeStyles.xaml)。
- `tests/AgentEye.Core.Tests`:xUnit,只测 Core(契约层),WinUI 层靠
  人工核对。

## 构建与测试

```text
dotnet build src\AgentEye.WinUI -c Debug -v q --nologo   # 0 error 才算过
dotnet test tests\AgentEye.Core.Tests -v q --nologo      # 130 passed
```

冒烟:启动 exe 7 秒未退出。改 XAML 后必须重构建确认 pass1 没有静默崩。

## 发布形态

- **框架依赖**(csproj 里 `SelfContained=false` + `WindowsAppSDKSelfContained=false`):
  产物 40 文件 / 37 MB(含 369 KB 图标资源),运行时用系统装的 .NET 8
  Desktop Runtime + Windows App Runtime 1.7。`SatelliteResourceLanguages=zh-Hans;en`
  砍掉 83 种语言卫星资源。`RuntimeIdentifier` 必须无条件设 win-x64,
  否则框架依赖发布直接失败。
- 要自包含版(换机即用,166 MB)得临时改回这两个属性——别长期并存两套。
- 一键构建 `build.ps1`(项目根,模式对齐 key_note):publish → 剪 pdb →
  `dist\`(便携目录)→ 体积报告 → Inno Setup 出 `installer\AgentEye_Setup.exe`
  (lzma2 约 7.9 MB)。`build\`、`dist\`、`installer\` 全部 gitignore;
  staging 是干净的,config 在 `~\.agenteye` 与安装目录无关,无需备份还原。
- PS1 含中文**必须带 UTF-8 BOM**(PS5.1 无 BOM 按 GBK 读,中文全串烧)。

## 安装器契约(AgentEye_Setup.iss)

- `AppId=AgentEye` 显式写死——升级链依赖它稳定,改名即断链。
- `DefaultDirName=E:\AgentEye` + `DisableDirPage=auto`(升级沿用旧目录)。
- `PrivilegesRequired=lowest`,HKCU 注册表,无需管理员。
- `CloseApplications=yes` + `RestartApplications=yes`:静默升级时安装器经
  Restart Manager 关掉运行中的应用、装完自动重启——**应用内更新靠这一对**,
  所以 UpdateService 拉起安装器后**不自己退出**(安装失败应用还活着)。
- [Run] 项带 `skipifsilent`:静默安装(更新场景)不弹"启动应用",
  重启交给 RestartApplications。


## 窗口尺寸契约

- 主尺寸 **420×560**(用户确认的紧凑面板),写在 `MainWindow.xaml.cs`
  的 `DefaultWidth/DefaultHeight`;物理像素,与 config 的
  `ui.width/ui.height` 同源(存的是物理像素,恢复时同源)。
- 允许范围 320×400 ~ 800×1200,靠 `WM_GETMINMAXINFO` 子类
  (`SetWindowSubclass`)约束;越界拖拽会被系统夹回。
- **其它尺寸必须自适应**:横向排列容易撑破的地方一律用 Grid
  (标签列 `*` + 控件列 `Auto`),不要用固定宽度的横排 StackPanel;
  文字加 `TextTrimming`,页脚/标题用两列 Grid 防重叠。
- 窄窗口下头部按钮区优先(标题裁剪),设置页靠 ScrollViewer 滚动。

## 主题资源契约

- 代码里取主题画刷**必须走 `Services/AeRes.Brush(el, key)` /
  `BrushOrNull`**;普通 `Application.Current.Resources.TryGetValue`
  不会下钻 ThemeDictionaries,拿到的永远是默认主题——禁止直连。
- `AeRes` 查主题子字典时**字符串键("Light"/"Dark")与枚举键
  (ElementTheme.Light)都要试**:XAML 里写的是 `x:Key="Light"` 字符串,
  `el.ActualTheme` 是枚举装箱值,两者不相等——只试一种会静默退化成
  灰色兜底(行文字/分割线/小组件底色全变灰,很显眼)。
- 新增主题色必须同时给 `App.xaml` 的 Light/Dark 两个子字典;主题无关色
  (等级色、`AeErrorBrush`)放根字典,可用 `{StaticResource}` 直引。
- 样式集中在 `Styles/AeStyles.xaml`;删样式前先全工程搜引用。
- 代码画的行(总览/小组件)在 `ActualThemeChanged` 里整批重渲染。

## XAML 契约

- 属性值若以 `{` 开头(如字面 JSON)必须写成 `{}{...}` 转义,否则
  XamlCompiler pass1 静默崩溃、无报错行号。子元素排在属性元素
  (如 `<Grid.RowDefinitions>`)**之前**会确定性复现 pass1 静默崩
  (3/3)——属性元素一律写在最前。另:pass1 偶发无差别静默崩
  (同内容重试即过,疑似增量状态/并行节点竞态),遇 MSB3073 无
  诊断输出先原样重跑一次、再清 `obj` 重跑,别急着改代码。
- 头部图标按钮是 32×32、透明底、圆角 6 的 `Button` + `FontIcon`;
  按钮区右侧留白 = `AppWindow.TitleBar.RightInset / RasterizationScale`
  (物理像素换算 DIP),别硬编码。
- 图标字形统一用 Segoe Fluent Icons 码位(E769/E768/E72C/E713/E72B、
  行图标 E99A/E943/E945/E756/E968/E774),不许内嵌图片。

## 图标资源契约

- 唯一图标源:`src\AgentEye.WinUI\Assets\AgentEye.ico`(A 标 + 瞳孔,
  托盘蓝 #285A96),16/20/24/32/40/48/64/128/256 九档、**32bpp BMP
  条目**(不能改成 PNG 压缩条目——Inno 的 `SetupIconFile` 吃不下)。
  由 `tools\make_icon.py` 生成(Pillow,dev-only,不在构建流程里);
  要改样式先改脚本再重生成,别手改 .ico。
- 同一份文件三处用:csproj `ApplicationIcon` 内嵌进 exe(资源管理器/
  任务栏/快捷方式/卸载项/toast)+ `Content CopyToOutputDirectory`
  随输出复制,运行时 `Services\AeAppIcon.cs` 按
  `AppContext.BaseDirectory\Assets\AgentEye.ico` 定位,用
  `AppWindow.SetIcon/SetTaskbarIcon/SetTitleBarIcon`,失败退 Win32
  `WM_SETICON`;全程 try/catch(图标是增强面,挂了不许崩主流程)。
- **托盘也用这份资源**:`Tray\TrayIconRenderer.cs` 读同一份 ico(纯
  Win32/GDI,不依赖 WinUI),把 A 标按额度紧张度染色(蓝→绿→橙→红,
  无数据走灰)+ 右下角白底数字牌;`Layout.Default` 换版式只改这一处。
  ico 读不到时自动退 `LegacyPlate`(彩色圆角底 + 大数字)。
- **GDI COLORREF 必须是 `0x00BBGGRR`**:高字节非 0 时 GDI 直接拒画,
  一个像素都不落——旧托盘代码就是栽在这(`0xFF000000 | ...`),
  托盘图标一直是空的,别再写错。
- 上面的「图标字形用 Segoe Fluent Icons」只约束**界面内按钮**,
  不含 app 级 .ico 资源。

## 中文文件

含中文的文件一律用编辑器工具(Read/Write/Edit)读写;PowerShell 5.1 的
`Get-/Set-Content` 按 GBK 解码 UTF-8,会把整个文件写坏。字节级复制
(Copy-Item / robocopy)安全。

## config 契约(与 Python 版同源,不得破坏)

- `~/.agenteye/config.json`,v2 schema(`providers[]` + `ui.*` +
  `alert.*`),`schema_version` 非 2 即按版本异常处理。
- API Key 内存态明文、磁盘态 `key_enc`(DPAPI CurrentUser + 标准
  base64);写盘前深拷贝加密,发现明文残留直接抛 `PlaintextKeyError`。
- 损坏隔离:JSON/UTF-8/根类型坏 → 先把原文件改名
  `config.json.corrupt-<时间戳>-<随机>` 再按模板启动,**绝不覆盖原文**。
- v1(5 个分立数组)自动迁移,原文件 DPAPI 加密备份 `.v1.bak`。
- 写盘必须原子(临时文件 + `File.Move` + flush-to-disk)。
- 环境变量覆盖解密后的 key(`*_API_KEY`),且不落盘。
- UI 只暴露 url/key/model;其余字段让用户手改 config。
- 小组件几何:`ui.widget_pos` 落盘取松手时的 `_lastPos`(事件坐标),
  不读 `AppWindow.Position`(回显滞后);松手位移 <4px 判点击。
- 全局快捷键:`ui.hotkey`(组合文本,默认 "Ctrl+Shift+A")+
  `ui.hotkey_enabled`(开关,默认 true);组合文本只在 `ui` 自由字典里加键,
  `schema_version` 仍是 2,Python 版 merge 会保留未知键,不受影响。
- 开机自启:`ui.autostart` 只是镜像值,**真实状态以注册表为准**;
  `ui.language`(默认 "zh-Hans")1.2.0 只落库不切换,中英双语 1.3.0 起生效。

## 设置页版式契约(卡片行模板)

- 每条功能 = 一张圆角卡片(`AeSettingsItemStyle`):左图标
  (`AeSettingsIconStyle`,Segoe Fluent Icons 字形)+ 标题
  (`AeSettingsItemTitleStyle`)+ 副说明(`AeSettingsItemSubtitleStyle`),
  右控件;分节用 `AeSectionHeaderStyle`。
- 行内布局一律 Grid 三列:`Auto`(图标)/ `*`(文字)/ `Auto`(控件);
  控件列放不下时先裁标题,不许用固定宽横排 StackPanel。
- 新样式只复用已有主题令牌(AeCardBrush / AeCardStrokeBrush /
  AeTextSecondaryBrush),要新颜色必须 Light/Dark 两边都加。

## 开机自启契约

- 写 HKCU `Software\Microsoft\Windows\CurrentVersion\Run` 的 "AgentEye" 项,
  值 = `"<exe 路径>" --minimized`——**必须与安装器 `AgentEye_Setup.iss` 里
  那条同键同格式**,否则两边互相看不见对方的开关。
- `--minimized` 由 `App.xaml.cs` 解析:建窗后 `AppWindow.Hide()`,静默进
  托盘(托盘/小组件照常);不带参数则正常显示主面板。
- 注册表读写失败只回滚 UI 开关 + 落状态文案,不许崩主流程。

## 全局快捷键契约

- 组合解析在 `Core/Hotkeys/HotkeySpec.cs`(纯逻辑,修饰键位与键码常量与
  Win32 同值,Core 不引 Win32);壳层 `Services/HotkeyService.cs` 只做
  `RegisterHotKey` 与消息转发。
- **`RegisterHotKey` 必须在窗口所属线程上调用**,否则直接失败(1408)——
  服务是独立线程 + 自建隐藏窗口(照托盘范式),所以注册/注销都经自定义
  消息 `WM_APP_APPLY` 交给热键线程执行,调用方等 `ManualResetEventSlim`。
- 至少要有一个修饰键(Ctrl/Alt/Shift/Win):裸键会劫持日常打字,解析层
  直接拒绝。
- 组合被别的程序占用(1409)只落 `HotkeyService.Status` 文案,设置页回显,
  应用照常运行——不许因此崩主流程(增强面契约)。
- 设置页「录制」:文本框既可手打(解析器大小写不敏感),也可**直接按组合键**
  录入——但只有"输入框内容仍是已保存的值"时按键才算录入,用户一旦手动改字
  就交回输入框(否则打 "+" 会被误判成组合)。**WinUI 的 `KeyRoutedEventArgs`
  不带修饰键**,修饰键状态走 Win32 `GetKeyState`;Esc 取消;非法组合当场
  提示并回滚,不写坏值。
- 唤醒语义:托盘左键 / 小组件点击 = 总是拉回前台(`MainWindow.WakeUp()`);
  **热键 = 唤醒/隐藏切换**(`MainWindow.TogglePanel()`):面板在前台就
  `AppWindow.Hide()` 藏回托盘,否则拉回前台。隐藏 ≠ 退出(托盘/轮询继续),
  关主窗 X 仍是退出。热键不给前台权,`SetForegroundWindow` 被拒时借前台线程
  (`AttachThreadInput`)再试。

## 增强面降级

托盘(`TrayIconService`,Shell_NotifyIconW + `TrayIconRenderer` 画的
A 标数字牌)、小组件(`WidgetWindow`,WS_EX_NOACTIVATE|TOOLWINDOW)与
全局快捷键(`HotkeyService`,RegisterHotKey)都是增强面:任何 Win32/WinRT
步骤失败只能静默降级,不许把主流程带崩。
`DesktopAcrylicBackdrop` 在老系统上可能抛异常,赋值处必须 try/catch。
系统「透明效果」关闭时(注册表 `EnableTransparency=0`)Windows 一律不渲染
Acrylic/Mica,窗口退化为不透明底色——这是系统策略,别去代码里绕。

## 更新机制

- 更新源是 GitHub Releases;检查逻辑在 `Core/Update/`,编排在
  `WinUI/Services/UpdateService.cs`。
- 状态存 `cache/update_state.json`(上次检查时间、忽略的版本)——
  **不要往 config 里塞更新的键**(config 与 Python 版同源,别破坏)。
- 版本号只认 csproj 的 `<Version>`;发布格式约定见 `docs/RELEASE.md`。
- 更新资产是 **`AgentEye_Setup.exe`(Inno 安装器)**:下载 → sha256 校验 →
  `/VERYSILENT` 拉起安装器,关进程/替换/重启全归它。旧的
  zip→robocopy 替换脚本方案已删(`ExtractZip`/`DetectPayloadRoot` 不复存在)。
- 安装目录不可写时下载前就拒绝(提示手动修复);排障看 Inno 的
  `%TEMP%` Setup Log,不再有 apply.ps1.log。
- `AGENTEYE_UPDATE_API` 可覆盖 API 基址(镜像/自建实例/本地联调)。
- 更新是增强面:任何一步失败只落状态文案,不许把主流程带崩。

## 退出码

`dotnet run` 场景不承诺退出码;Core 抛 `ConfigError` 族在壳层兜住后
弹错误 UI。进程因未处理异常退出即为 bug。
