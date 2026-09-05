namespace CodexDreamSkinManager.Services;

/// <summary>应用级服务容器（在 App 启动时初始化一次）。</summary>
public static class AppServices
{
    public static PowerShellRunner PowerShell { get; private set; } = null!;
    public static StatusReader Status { get; private set; } = null!;
    public static ThemeService Themes { get; private set; } = null!;
    public static FileDialogService Files { get; private set; } = null!;

    public static void Initialize()
    {
        var windowsRoot = AppContext.BaseDirectory;
        var stateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexDreamSkin");

        Status = new StatusReader();
        PowerShell = new PowerShellRunner(windowsRoot, stateRoot);
        Themes = new ThemeService(PowerShell, Status);
        Files = new FileDialogService();
    }
}
