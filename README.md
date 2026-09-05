# Codex Dream Skin Manager — WinUI 3 版

把 Electron GUI（`windows/gui/`）用 C# + WinUI 3 完整重写，功能对齐原 GUI。引擎（`windows/scripts/` 的 PowerShell + `windows/assets/`）不动，本 GUI 换成 C# 直接调 `powershell.exe` 跑脚本 + 读状态文件。

## 功能（对齐 Electron）

- **概览**：当前主题、启动皮肤、背景预览、快速主题（选择背景图 / 暂停皮肤 / 保存主题）
- **主题管理**：主题库（拖拽排序 + 应用 / 编辑 / 新建 / 当前）+ 主题编辑器（名称 / 副标题 / 标语 / 项目前缀 / 项目标签 / 状态文案 / 引用文案 / 外观 / 安全区 / 任务页 / 任务页背景 / 主页背景 / 强调色 / 焦点 X·Y，含更换背景 / 保存 / 另存 / 删除）
- **操作与日志**：安装更新 / 启动皮肤 / 重启并应用 / 验证 / 打开托盘 / 关闭 Codex / 恢复官方外观，输出框 + 最近日志
- **状态**：运行状态 / 皮肤显示 / 当前主题 / 运行时（左侧栏，5 秒自动刷新）
- **图标**：沿用 Electron 的 icon.ico（exe + 窗口图标）

## 目录

```
windows/winui/
├── CodexDreamSkinManager.csproj
├── app.manifest
├── App.xaml / App.xaml.cs           # 应用入口 + 服务初始化 + converter
├── MainWindow.xaml / MainWindow.xaml.cs   # NavigationView 三页导航 + 状态栏 + 深色切换
├── Assets/icon.ico · icon.png       # 图标
├── Converters/InverseBoolConverter.cs
├── Models/ThemeModel.cs · StatusModel.cs
├── Services/
│   ├── AppServices.cs               # 服务容器
│   ├── PowerShellRunner.cs          # 等价 runPowerShell + psInvokeFile + theme/common/hotApply
│   ├── StatusReader.cs              # 读 state.json / active-theme / 主题库 / 日志
│   ├── ThemeSerializer.cs           # 等价 normalizeTheme
│   ├── ThemeService.cs              # 等价 theme:* IPC handlers（含热应用）
│   └── FileDialogService.cs         # 图片选择 + 打开文件夹
└── Views/
    ├── OverviewPage.xaml/.cs
    ├── ThemesPage.xaml/.cs
    └── ActionsPage.xaml/.cs
```

## 编译环境

- VS 2026 + 「.NET 桌面开发」工作负载
- .NET 8 SDK（`TargetFramework` 为 `net8.0-windows10.0.19041.0`）
- NuGet 首次打开自动恢复 `Microsoft.WindowsAppSDK 1.6` 与 `Microsoft.Windows.SDK.BuildTools`

## 编译 / 运行

1. VS 打开 `windows/winui/CodexDreamSkinManager.csproj`
2. 配置选 **x64 + Release**
3. 生成，输出在 `bin/x64/Release/net8.0-windows10.0.19041.0/win-x64/`，直接运行 `CodexDreamSkinManager.exe`

启用了 `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`，不需要单独装 Windows App SDK runtime。`scripts/`、`assets/`、`VERSION`、`Assets/` 会自动复制到输出目录。

## 如果虚拟机只有 .NET 10 SDK

1. csproj 把 `<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>` 改成 `net10.0-windows10.0.19041.0`
2. 用 NuGet 管理器把 `Microsoft.WindowsAppSDK` 升到支持 .NET 10 的版本

## 与 Electron 的对应

| Electron | WinUI (C#) |
|---|---|
| main.js `runPowerShell` / `psInvokeFile` / `themeCommand` / `hotApplyThemeLines` | `PowerShellRunner` |
| `getStatus()` | `StatusReader` |
| `normalizeTheme()` | `ThemeSerializer` |
| `theme:*` IPC handlers | `ThemeService` |
| `dialog.showOpenDialog` / `shell.openPath` | `FileDialogService` |
| 3 个视图 + 导航 | NavigationView + 3 个 Page |
| localStorage 主题排序 / 深色 | `gui-theme-order.json` / `RootGrid.RequestedTheme` |
| electron-builder `extraResources` | csproj `<Content>` 复制 |
| `DREAM_PORT = 9445` | 各处写死 9445 |
