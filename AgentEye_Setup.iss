; 版本以 csproj 为唯一真源,build.ps1 经 /DAppVer 注入;直接编译本脚本时回退 1.0.0
#ifndef AppVer
  #define AppVer "1.0.0"
#endif
; 打包源目录:build.ps1 传入干净 staging(build\setup_stage),直接编译时回退 dist
#ifndef SrcDir
  #define SrcDir "dist"
#endif

[Setup]
AppName=AgentEye
AppVersion={#AppVer}
AppPublisher=AgentEye Project
; 显式 AppId:升级链依赖它稳定(隐式取 AppName,改名即断链)
AppId=AgentEye
DefaultDirName=E:\AgentEye
; no:每次安装都显示目录页(升级预填旧目录,用户可改);
; 静默升级(/VERYSILENT)不显示向导页,仍沿用旧目录
DisableDirPage=no
UsePreviousAppDir=yes
; no:浏览目录时不再自动拼 "\AgentEye"(选哪装哪),并让对话框出现
; 「新建文件夹」按钮——设 yes 时这个按钮不出现(见 Inno 文档)
AppendDefaultDirName=no
MinVersion=10.0.19041
DefaultGroupName=AgentEye
OutputDir=installer
OutputBaseFilename=AgentEye_Setup
; 安装器图标:与 exe 同一份 A 标(必须是 32bpp BMP 格式条目的 .ico,不能是 PNG 压缩)
SetupIconFile=src\AgentEye.WinUI\Assets\AgentEye.ico
Compression=lzma2/max
SolidCompression=yes
UninstallDisplayIcon={app}\AgentEye.exe
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
; 静默安装时自动关掉正在运行的 AgentEye(用 Restart Manager)并替换文件,装完自动重启
; 应用内「下载并安装」就走这条路:拉起本安装器 + /VERYSILENT
CloseApplications=yes
RestartApplications=yes
; 不设 AppMutex:它会让 Setup 弹窗等用户手动关闭,静默升级会卡住

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimplified.RootDirNotAllowed=请不要直接安装到驱动器根目录,请选择或新建一个子文件夹。
english.RootDirNotAllowed=Please do not install directly into a drive root. Choose or create a subfolder.

[Tasks]
; checkedonce:首装默认勾选,之后记住用户上次的选择(升级不反复打扰)
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "快捷方式："; Flags: checkedonce
Name: "startmenuicon"; Description: "创建开始菜单快捷方式"; GroupDescription: "快捷方式："; Flags: checkedonce
Name: "autostart"; Description: "开机自动启动 AgentEye"; GroupDescription: "启动选项："; Flags: unchecked

[Files]
; 发布产物(框架依赖:运行时用系统装的 .NET 8 Desktop Runtime + Windows App Runtime);
; 双保险:排除一切用户数据与更新缓存
Source: "{#SrcDir}\*"; DestDir: "{app}"; Excludes: "AgentEye_Setup.exe,config.json"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 开始菜单整组(启动/说明/卸载)跟随 startmenuicon 任务;不勾时卸载入口
; 仍在系统设置「应用」里(Uninstall 注册表由 Inno 自动写)
Name: "{group}\AgentEye"; Filename: "{app}\AgentEye.exe"; Tasks: startmenuicon
; 用 {userdesktop} 而非 {commondesktop}:后者是 C:\Users\Public\Desktop,
; 非管理员安装(PrivilegesRequired=lowest)写不进去,快捷方式会静默失败
Name: "{userdesktop}\AgentEye"; Filename: "{app}\AgentEye.exe"; Tasks: desktopicon
Name: "{group}\说明 (README)"; Filename: "{app}\README.md"; Tasks: startmenuicon
Name: "{group}\卸载 AgentEye"; Filename: "{uninstallexe}"; Tasks: startmenuicon

[Registry]
; 开机自启(HKCU,无需管理员);应用内不提供开关,要关就在任务管理器→启动里禁用
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AgentEye"; ValueData: """{app}\AgentEye.exe"" --minimized"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\AgentEye.exe"; Description: "启动 AgentEye"; Flags: postinstall nowait skipifsilent shellexec

[Code]
// AppendDefaultDirName=no 后不再自动拼 "\AgentEye":防止用户一路点进
// 驱动器根目录,把整套文件撒在 E:\ 根下。选根目录时拦一下,让用户
// 新建 / 选择子文件夹(浏览对话框里有「新建文件夹」按钮)。
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    if Length(WizardDirValue) = 3 then
    begin
      if (WizardDirValue[2] = ':') and (WizardDirValue[3] = '\') then
      begin
        MsgBox(ExpandConstant('{cm:RootDirNotAllowed}'), mbError, MB_OK);
        Result := False;
      end;
    end;
  end;
end;
