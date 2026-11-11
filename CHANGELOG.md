# Changelog

## 2026-11-11 — 1.6.0:告警增强 / 行级刷新 / 自适应轮询 / 新 provider

### 新功能

- **恢复通知**:之前告警过 warning/critical 的 provider 回到正常会弹
  「额度已恢复」;行被删除 / 暂停只静默清状态,不算恢复,不轰炸
- **免打扰时段**:设置页告警卡片新增开关 + 起止时间(`alert.quiet_start` /
  `quiet_end`,`HH:mm`,支持跨午夜如 23:00–08:00);免打扰期内冷却状态照记、
  toast 一律不弹,结束后不补发
- **Key 失效告警**:连续 3 轮拉取失败弹「连续拉取失败」toast,与额度告警
  共用 60 分钟冷却;error 轮不清冷却记录,网络抖动恢复后老告警不会被重弹
- **行级「立即刷新」**:总览行右键菜单新增,只重拉这一行(独立单飞,不打断
  全局轮询),合并进当前面板并触发同规则告警评估
- **自适应轮询**(`ui.adaptive_poll`,默认关):设置页「高级」新卡片。开启后
  有紧张行时间隔减半(最快 30 秒)、整轮全正常放缓 ×1.5(上限 1 小时)、
  连续整轮失败指数退避(×2 起,封顶 ×32)
- **新增 SiliconFlow(硅基流动)适配器**:官方 `/v1/user/info`,显示可用余额
  (¥)与总资产;免费账号(全 0)灰显不误报紧张
- **新增 OpenRouter 适配器**:官方 `/api/v1/credits`,显示剩余额度($)
- **$ 绝对额阈值**:`alert.critical_amount_usd`(缺省 2.0,模板已带)——
  此前无百分比字段的 $ 行(OpenCode Go / 中转站美元计额)**永远不会告警**,
  现在低于该阈值会判 critical;带百分比的行维持原路径不受影响

### 修复

- **`alert.enable` 开关此前不生效**:设置页关掉告警后 toast 照弹——开关只
  控制了阈值行的显隐。现在它是真正的总闸门,关闭后整轮静默
- **托盘 tooltip**:错误 / 未配置 / 暂停行不再露出英文内部枚举名(改本地化
  等级名);无百分比字段的金额行不再误报「已用 0%」

### 说明

- config 只加了自由键(`alert.quiet_start/quiet_end/critical_amount_usd`、
  `ui.adaptive_poll`),`schema_version` 仍为 2,Python 版 merge 保留未知键
- 测试 206 条全过(新增免打扰 / 失败轮数 / 自适应间隔 / 新 provider 解析)

## 2026-11-10 — 1.5.4:安装器可选目录 / 新建文件夹 / 快捷方式

### 改进

- **每次安装都能选目录**:`DisableDirPage` 由 `auto` 改 `no`——以前只有首装
  显示目录页,升级直接沿用旧目录;现在升级也预填旧目录并允许修改
- **浏览对话框支持「新建文件夹」**:`AppendDefaultDirName=no`(Inno 的这个
  开关同时决定「新建文件夹」按钮是否出现);代价是选目录后不再自动拼
  `\AgentEye`,选哪装哪
- 选驱动器根目录(如 `E:\`)会被拦下并提示选择/新建子文件夹,避免把整套
  文件撒在根目录
- **开始菜单快捷方式也可选**:新增 `startmenuicon` 任务(默认勾选,记住上次
  选择);不勾时开始菜单整组不创建,卸载入口仍在系统设置「应用」
- 桌面快捷方式保持可选(`desktopicon`,默认勾选、记住上次选择)
- 静默升级(`/VERYSILENT`,应用内更新)不显示向导页,行为不变:沿用旧目录
- 文档同步:AGENTS.md 安装器契约、README / docs/RELEASE.md 安装说明

## 2026-11-10 — 1.5.3:单实例互斥 + 快捷键失败一律弹窗

### 修复

- **单实例互斥**:热键、托盘、轮询都是进程级的——第二个实例会去抢第一个的
  全局热键(1409「该组合已被其它程序占用」),还会多出一份托盘图标 /
  小组件 / 轮询。现在第二个实例启动时把已有实例的面板叫到前台,**自己直接
  退出**:不再抢热键、不再加图标、不再重复轮询
- 静默启动(开机自启 `--minimized`)撞上已有实例时**只保托盘不弹面板**,
  免得开机弹窗打扰
- **热键所有失败状态都弹窗**:以前只有文案以「注册失败」开头的才弹,
  「服务不可用 / 设置失败 / 无响应 / 超时 / 组合无效」全静默,用户完全
  看不到反馈。改成按 `HotkeyState` 枚举判定,不再靠 match 文案前缀
- 启动时热键就没生效(被占用 / 组合非法)会弹一次告知,并提示可在设置页换
  组合;同一个失败状态在设置页只弹一次,恢复了下次再犯才再弹
- **`RegisterHotKey` 的 P/Invoke 漏了 `SetLastError=true`**:`GetLastWin32Error()`
  拿到的是 CLR 内部垃圾值(实测 -130988),所以 1409「组合被占用」的友好
  文案从来没生效过——用户看到的是「注册失败(错误 -130988)」这种怪码
- 顺带修掉 1.5.3 首版引入的误报:页面初始化早于热键服务装配,首版把
  「还没装配」当成「服务不可用」,导致**每次启动都误弹「快捷键未生效」**;
  现在「未装配」= `Unknown` = 无错,只有真失败才弹窗

### 实现

- 新增 `Services/SingleInstance.cs`:命名互斥体 `Local\AgentEye.Singleton` +
  主实例隐藏消息窗口(类名前缀 `AgentEyeSingleton_`),第二实例枚举顶层
  窗口认人并发唤醒消息。自建 Win32 而不用 WinAppSDK 的
  `AppInstance.FindOrRegisterForKey`:unpackaged 下
  `RedirectActivationToAsync` 的 `Activated` 回调可靠性要实测
- 第二实例在**建窗口之前**就退出:托盘 / 小组件 / 热键 / 轮询 / 配置写入
  一个都不碰
- 互斥体或隐藏窗口建不起来 → 静默降级成多实例照常跑(增强面契约)

## 2026-11-10 — 1.5.2:默认直连 + 代理回退(死系统代理不再拖死面板)

### 修复

- **默认直连**(`ui.proxy_mode` 缺省 `direct`):平时完全不碰系统代理,
  代理软件退出后残留的死代理(如 Clash Verge 退出后的 127.0.0.1:9652)
  彻底不参与——面板照常出数值。这正是 1.5.1 实测翻车的场景
- 直连优先模式下直连也失败(如只有代理能通的海外中转站)时,自动换系统
  代理重试一次,静默完成,**不出任何横幅/通知**
- 失败文案按尝试顺序点名每条路:「直连失败:(原因);系统代理 X 无响应
  (原因)」,不再只给裸 .NET 消息

### 保留的旧行为(设置页「网络」分节可选)

- `auto` 跟随系统代理(代理优先):先走系统代理,失败才直连
- `system` 严格跟随:只走代理,不做任何兜底

### 实现

- `Http` 抽出 `ProxyMode`(direct/auto/system)+ `SendAsync` 统一出口按
  路线顺序尝试,Get/Post 共用;`DirectClient`(UseProxy=false)与系统代理
  `Client` 并存
- 刚失败过的路线记进程内(10 分钟窗口):窗口内把它挪到尝试顺序末尾,
  省掉每轮一次注定失败的尝试;该路走通即解除,过期自动复探
- 更新检查/安装包下载走同一套选路语义(`Http.FirstRoute`)
- 代理地址问 `HttpClient.DefaultProxy.GetProxy` 拿(env → WinINET → PAC
  全跟 .NET 走,不自己读注册表);.NET 8 的 DefaultProxy 是进程级快照,
  系统代理变更在 .NET 9 前不会被运行中的进程看见——选路因此不依赖
  「实时感知」,失败就换路线,恢复靠窗口过期复探

## 2026-11-07 — 1.5.1:网络错误带底层原因

### 改进

- **错误行明细带底层异常摘要**:provider 拉取 / 模型探测 / 中转站
  登录 / 更新检查的网络失败,文案从笼统的「网络异常」升级为如
  「网络错误: 由于目标计算机积极拒绝,无法连接。 (127.0.0.1:9652)」
  ——死代理、DNS 断网这类问题一眼定位(1.5.0 实测:代理软件退出
  但系统代理残留,三个 provider 同时报错,只能手动排查才知道是代理)
- 超时给专门短语「请求超时」;无摘要时回退笼统文案
- 实现:`Http.GetJsonAsync/PostJsonAsync` 返回元组扩一列摘要随返回值
  传递——AsyncLocal 写入不向上传播到 await 调用方、静态字段在并发
  拉取下会串线,均不可用(已注释在代码里)

## 2026-11-07 — 1.5.0:UI 改版 + 托盘退出修复

### 改版(界面)

- **总览行卡片化**:provider 行改为白卡(1px 描边 + 圆角 8),行与行靠
  卡片边界分隔(去掉分割线);28px 图标底板、名称旁**等级 pill**
  (正常/注意/紧张,淡等级底 + 等级色字)、明细支持两行折行、主值
  18px(余额类下方补单位行)、4px 圆角等级条
- **头部**:品牌标(18×18 托盘蓝底 "A",与 .ico 同款)+ **状态胶囊**
  (最紧张 provider 的等级色点 + provider 数,随刷新更新)
- **页脚胶囊化**:刷新时间与 provider 数各成小胶囊,作底部视觉锚点;
  空态加图标
- **设置页**:分节标题收紧为 16 号,8 处卡片行图标加圆角底板,
  卡片间距统一 8,下拉控件列宽统一
- **主题令牌**:卡片描边提对比(浅 #C8CDD4 / 深 #3D4148),新增
  AeSubtleFillBrush(图标/胶囊底板)与 AeAccentTintBrush;玻璃底
  保持通透不加灰
- **桌面小组件**:行字号 12 + 名称加粗、行内等级条 2px→3px、
  内边距加大;高度公式修正,**3 行不再裁切**(实测 260×113)

### 修复

- **托盘菜单「退出」无反应**(1.4.1 引入):退出路径未封送 UI 线程,
  托盘消息循环的退出也改为显式 `WM_QUIT`;实测 130–266ms 完整退出
- **深色配置启动主面板偶发渲染浅色**:逻辑主题正确但视觉残留,
  树加载完成后再应用一次主题(`Root_Loaded` 重应用),连续多次
  新鲜启动验证稳定
- **小组件 3 行裁切**:行高估算偏小(Segoe UI Variable 实际行高偏大),
  RowHeight 提到 26 并留裕量

## 2026-11-06 — 1.4.1:托盘图标修复 + 关窗进托盘 + 几何风图标

### 修复

- **托盘图标**(长期"托盘没有图标"的根因):`NOTIFYICONDATAW` 误用
  `Pack=2`,x64 下 8 字节 hWnd/hIcon 被压到 4 字节对齐(hWnd@4,原生
  布局在 @8),`NIM_ADD` 必然失败且被增强面静默吞掉。改回 Default
  pack(实测 Size=952 / hWnd@8 / hIcon@32,与原生 x64 布局逐字段吻合)
- **主题被滚轮切回去**:ComboBox 选中后保持焦点,滚轮会改它的
  SelectedIndex(设置页整页在 ScrollViewer 里,滚轮本意是滚页面)。
  主题/语言/类型/模型下拉提交选择后把焦点交还页面,滚轮恢复滚动;
  模型列表程序填充不再触发移焦点
- **开机自启自愈**:启动时注册表命令行与当前 exe 不一致(挪目录/
  换盘/重装)即静默重写;`ui.autostart` 镜像值以注册表为准回填

### 新增

- **关窗行为可配**:设置页「关闭主面板时」开关(`ui.close_to_tray`,
  缺省开 = 点 X 隐藏进托盘,进程/托盘/轮询继续;关 = 直接退出)。
  真退出走托盘菜单「退出」;更新安装经 `MainWindow.CloseAllowedByUpdate`
  放行 Restart Manager;语言切换重启改走 `ExitFromTray`(托盘不残留)
- **应用图标只留字母 A**:实心窄高 A、托盘蓝,去掉瞳孔与底板;
  整字随额度紧张度在托盘上换色(`tools/make_icon.py` 重写设计段)

## 2026-10-31 — 1.4.0:API Key 接入优化

### 新增

- 设置页「添加 Key」表单**测试按钮**:保存前先验证连通性,成功顺带
  拉取模型列表;测试期间按钮禁用,结果行以绿/红着色(联网请求,
  失败提示保留 HTTP 状态)
- **模型自动拉取**:Model 输入框升级为可编辑下拉,测试成功后自动
  填入该 provider 返回的模型列表(保留手打值,手打仍可用)
- **类型预设下拉**:新增「类型」行——自动识别 / DeepSeek / 智谱 GLM /
  MiniMax / OpenCode Go / 通用 OpenAI 兼容;选中即自动填官方端点,
  编辑已有 Key 时回显类型且不可改
- **识别结果可见**:自动识别模式下输入 URL / Key 即时预览判定结论
  (如「识别:智谱 GLM — URL 含 'bigmodel.cn' → 智谱 GLM」),
  不再是保存时才暗箱判定

### 说明

- 官方直连四类(DeepSeek / 智谱 / MiniMax / OpenCode Go)Base URL
  可留空(走内置官方端点);通用 OpenAI 兼容与中转站必须手填 URL
- 新增 `Core/Providers/ProviderTest`(测试编排,Core 层可单测,
  144 passed)

## 2026-10-31 — 1.3.0:中英双语界面

### 新增

- 界面语言可切:设置页「语言」行(简体中文 / English),切换后
  **重启生效**(弹窗确认,可一键重启);语言存 `ui.language`,
  默认 "zh-Hans"
- 全量文案查表 `Core/Localization/L.cs`:zh / en 两本内置字典,
  启动时按 config 一次定型,运行期不变;XAML 静态文本与代码侧
  文案(设置页弹窗、provider 明细、托盘菜单/悬浮提示、小组件、
  toast、更新流程、config 错误)统一走 `L.T/L.F`
- 新增 11 条本地化测试:语言映射、缺键回退键名、zh/en 字典键位
  对齐(141 passed)

### 说明

- 切换语言只翻"流向 UI"的文案;纯日志与内部异常仍是中文
- 解包(unpackaged)形态下 WinRT `PrimaryLanguageOverride` 不可用,
  resw/x:Uid 方案已证伪并移除——1.3.0 起以代码查表为唯一文案源,
  新增界面文案必须 zh / en 双键同时给

## 2026-10-31 — 1.2.3:告警关闭时收纳阈值行

### 修复

- 告警卡片的两条阈值行(剩余百分比 ≤ / 剩余额度 ≤)不再常驻:
  开关**关**时整层收纳(卡片只留开关行),**开**时展开;初态跟随
  `alert.enable`,进页不闪

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

- 自动更新通道:更新源为 GitHub Releases(`AetherGazer6244/AgentEye`)。
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
