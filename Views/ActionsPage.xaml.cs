using CodexDreamSkinManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CodexDreamSkinManager.Views;

public sealed partial class ActionsPage : Page, IStatusPage
{
    private bool _busy;

    public ActionsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var snap = await AppServices.Status.ReadAsync();
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(snap.ErrorLog)) parts.Add($"[injector-error]\n{snap.ErrorLog.Trim()}");
        if (!string.IsNullOrWhiteSpace(snap.VerifyLog)) parts.Add($"[verify]\n{snap.VerifyLog.Trim()}");
        if (!string.IsNullOrWhiteSpace(snap.Log)) parts.Add($"[injector]\n{snap.Log.Trim()}");
        LogsBox.Text = parts.Count > 0 ? string.Join("\n\n", parts) : "";
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
        => await RunAsync(() => AppServices.PowerShell.RunScriptAsync("install-dream-skin.ps1", new[] { "-Port", "9445" }));

    private async void OnStartClick(object sender, RoutedEventArgs e)
        => await RunAsync(() => AppServices.PowerShell.RunScriptAsync("start-dream-skin.ps1", new[] { "-Port", "9445", "-RestartExisting" }));

    private async void OnRestartClick(object sender, RoutedEventArgs e)
        => await RunAsync(RestartAsync);

    private async void OnVerifyClick(object sender, RoutedEventArgs e)
    {
        var screenshot = Path.Combine(Path.GetTempPath(), "codex-dream-skin-gui-verify.png");
        await RunAsync(() => AppServices.PowerShell.RunScriptAsync("verify-dream-skin.ps1", new[] { "-Port", "9445", "-ScreenshotPath", screenshot }));
    }

    private void OnTrayClick(object sender, RoutedEventArgs e)
    {
        AppServices.PowerShell.RunDetached("tray-dream-skin.ps1", new[] { "-Port", "9445" });
        OutputBox.Text = "已启动托盘进程。\n" + OutputBox.Text;
    }

    private async void OnCloseCodexClick(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("这会关闭当前官方 Codex 窗口，未发送的输入可能丢失。继续？")) return;
        await RunAsync(() => AppServices.PowerShell.RunCommonAsync(CloseCodexBody()));
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAsync("这会恢复官方外观，并在需要时关闭/重启 Codex。继续？")) return;
        await RunAsync(() => AppServices.PowerShell.RunScriptAsync("restore-dream-skin.ps1", new[] { "-Port", "9445", "-RestoreBaseTheme", "-ForceRestart" }));
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => OutputBox.Text = "";

    private async Task<CommandResult> RestartAsync()
    {
        var closed = await AppServices.PowerShell.RunCommonAsync(CloseCodexBody(returnWhenIdle: false));
        if (!closed.Ok) return closed;
        return await AppServices.PowerShell.RunScriptAsync("start-dream-skin.ps1", new[] { "-Port", "9445" });
    }

    private async Task RunAsync(Func<Task<CommandResult>> action)
    {
        SetBusy(true);
        try
        {
            var result = await action();
            OutputBox.Text = FormatOutput(result);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            OutputBox.Text = "异常：" + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallButton.IsEnabled = !busy;
        StartButton.IsEnabled = !busy;
        RestartButton.IsEnabled = !busy;
        VerifyButton.IsEnabled = !busy;
        TrayButton.IsEnabled = !busy;
        CloseCodexButton.IsEnabled = !busy;
        RestoreButton.IsEnabled = !busy;
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "确认",
            Content = message,
            PrimaryButtonText = "继续",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static string FormatOutput(CommandResult result)
    {
        var parts = new List<string> { result.Ok ? $"PASS exit={result.Code}" : $"FAIL exit={result.Code}" };
        if (!string.IsNullOrWhiteSpace(result.Stdout)) parts.Add(result.Stdout.Trim());
        if (!string.IsNullOrWhiteSpace(result.Stderr)) parts.Add(result.Stderr.Trim());
        return string.Join("\n\n", parts);
    }

    /// <summary>等价 main.js 的 fastCloseCodexLines。</summary>
    private static string CloseCodexBody(bool returnWhenIdle = true)
    {
        var lines = new List<string>
        {
            "$codex = Get-DreamSkinCodexInstall",
            "$targetPids = New-Object 'System.Collections.Generic.HashSet[int]'",
            "$processes = @(Get-DreamSkinCodexProcesses -Codex $codex)",
            "foreach ($process in $processes) { [void]$targetPids.Add([int]$process.ProcessId) }",
            "$listeners = @(Get-DreamSkinPortListeners -Port 9445)",
            "foreach ($listener in $listeners) {",
            "  $owner = Get-CimInstance Win32_Process -Filter \"ProcessId = $([int]$listener.OwningProcess)\" -ErrorAction SilentlyContinue",
            "  $ownerPath = if ($owner) { Get-DreamSkinProcessExecutablePath -ProcessInfo $owner } else { $null }",
            "  if ($ownerPath -and (Test-DreamSkinPathEqual -Left $ownerPath -Right $codex.Executable)) {",
            "    [void]$targetPids.Add([int]$listener.OwningProcess)",
            "  }",
            "}",
            "if ($targetPids.Count -eq 0) {",
            "  if ($listeners.Count -gt 0) {",
            "    Write-Host ('Codex is not running, but port 9445 is still occupied by PID(s): ' + (($listeners | ForEach-Object { $_.OwningProcess }) -join ', '))",
            "  } else {",
            "    Write-Host 'Codex is not running and port 9445 is free.'",
            "  }",
            returnWhenIdle ? "  return" : "  $null = $null",
            "}",
            "foreach ($pidValue in @($targetPids)) {",
            "  try { [void](Get-Process -Id $pidValue -ErrorAction Stop).CloseMainWindow() } catch {}",
            "}",
            "Start-Sleep -Milliseconds 800",
            "foreach ($pidValue in @($targetPids)) {",
            "  $owner = Get-CimInstance Win32_Process -Filter \"ProcessId = $pidValue\" -ErrorAction SilentlyContinue",
            "  $ownerPath = if ($owner) { Get-DreamSkinProcessExecutablePath -ProcessInfo $owner } else { $null }",
            "  if ($ownerPath -and (Test-DreamSkinPathEqual -Left $ownerPath -Right $codex.Executable)) {",
            "    Stop-Process -Id $pidValue -Force -ErrorAction SilentlyContinue",
            "  }",
            "}",
            "if (-not (Wait-DreamSkinPortAvailable -Port 9445 -TimeoutSeconds 3)) {",
            "  $remaining = @(Get-DreamSkinPortListeners -Port 9445)",
            "  throw ('Port 9445 is still occupied by PID(s): ' + (($remaining | ForEach-Object { $_.OwningProcess }) -join ', '))",
            "}",
            "Write-Host ('Closed Codex process count: ' + $targetPids.Count + '; port 9445 is free.')",
        };
        return string.Join("\r\n", lines);
    }
}
