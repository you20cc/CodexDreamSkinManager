using System.Diagnostics;
using System.Text;

namespace CodexDreamSkinManager.Services;

/// <summary>一次 PowerShell 调用的结果。</summary>
public sealed record CommandResult(bool Ok, int Code, string Stdout, string Stderr)
{
    public string Summary => string.IsNullOrWhiteSpace(Stdout) ? Stderr : Stdout;
}

/// <summary>热应用主题时的提示文案。</summary>
public sealed record ThemeHotApply(string UnavailableMessage, string SuccessMessage, string FailPrefix);

/// <summary>
/// 等价于 Electron 版 main.js 里的 runPowerShell / psInvokeFile / psPrelude /
/// commonCommand / themeCommand / hotApplyThemeLines。
/// </summary>
public sealed class PowerShellRunner
{
    private const int DefaultTimeoutMs = 5 * 60 * 1000;

    private readonly string _windowsRoot;   // exe 目录（其下有 scripts/、assets/）
    private readonly string _scriptsRoot;
    private readonly string _stateRoot;
    private readonly int _port;

    public PowerShellRunner(string windowsRoot, string stateRoot, int port = 9445)
    {
        _windowsRoot = windowsRoot;
        _scriptsRoot = Path.Combine(windowsRoot, "scripts");
        _stateRoot = stateRoot;
        _port = port;
    }

    public string ScriptPath(string name) => Path.Combine(_scriptsRoot, name);

    /// <summary>调一个 .ps1 文件（install / start / restore / verify / tray）。</summary>
    public Task<CommandResult> RunScriptAsync(
        string scriptName,
        IReadOnlyList<string> args,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken ct = default)
    {
        var script = string.Join("\r\n", BuildPrelude().Concat(new[]
        {
            $"& {PsString(Path.Combine(_scriptsRoot, scriptName))} {string.Join(" ", args.Select(FormatArg))}",
            "if ($global:LASTEXITCODE -ne $null -and $global:LASTEXITCODE -ne 0) { exit $global:LASTEXITCODE }",
        }));
        return RunAsync(script, timeoutMs, ct);
    }

    /// <summary>common 命令：前导 + dot-source common-windows.ps1 + body。</summary>
    public Task<CommandResult> RunCommonAsync(
        string body,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken ct = default)
    {
        var script = string.Join("\r\n", BuildPrelude()
            .Append($". {PsString(Path.Combine(_scriptsRoot, "common-windows.ps1"))}")
            .Append(body));
        return RunAsync(script, timeoutMs, ct);
    }

    /// <summary>theme 命令：前导 + dot-source common/theme + 初始化 + body（+ 可选热应用）。</summary>
    public Task<CommandResult> RunThemeAsync(
        string body,
        ThemeHotApply? hotApply = null,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken ct = default)
    {
        var lines = BuildPrelude()
            .Append($". {PsString(Path.Combine(_scriptsRoot, "common-windows.ps1"))}")
            .Append($". {PsString(Path.Combine(_scriptsRoot, "theme-windows.ps1"))}")
            .Append($"$StateRoot = {PsString(_stateRoot)}")
            .Append($"$SkillRoot = {PsString(_windowsRoot)}")
            .Append("$paths = Initialize-DreamSkinThemeStore -SkillRoot $SkillRoot -StateRoot $StateRoot")
            .Append(body);
        if (hotApply is not null)
        {
            lines = lines.Concat(BuildHotApply(hotApply));
        }
        return RunAsync(string.Join("\r\n", lines), timeoutMs, ct);
    }

    /// <summary>启动脚本但不等待退出（对应 tray 这种常驻进程）。</summary>
    public void RunDetached(string scriptName, IReadOnlyList<string> args)
    {
        var script = string.Join("\r\n", BuildPrelude().Concat(new[]
        {
            $"& {PsString(Path.Combine(_scriptsRoot, scriptName))} {string.Join(" ", args.Select(FormatArg))}",
            "if ($global:LASTEXITCODE -ne $null -and $global:LASTEXITCODE -ne 0) { exit $global:LASTEXITCODE }",
        }));
        var psi = BuildStartInfo(script);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        Process.Start(psi);
    }

    // ---- 底层 ----

    private async Task<CommandResult> RunAsync(string script, int timeoutMs, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        using var process = Process.Start(BuildStartInfo(script));
        if (process is null)
        {
            return new CommandResult(false, -1, "", "无法启动 powershell.exe");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new CommandResult(false, -1, "", $"超时（{timeoutMs / 1000}s）。Codex 可能已打开，刷新状态或运行验证确认。");
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new CommandResult(process.ExitCode == 0, process.ExitCode, Clean(stdout), Clean(stderr));
    }

    private ProcessStartInfo BuildStartInfo(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");

        return new ProcessStartInfo
        {
            FileName = powershell,
            Arguments = $"-NoLogo -NoProfile -ExecutionPolicy RemoteSigned -EncodedCommand {encoded}",
            WorkingDirectory = _windowsRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
    }

    private static void TryKill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
    }

    private static string Clean(string raw) => string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();

    // ---- 脚本构建辅助 ----

    private static IEnumerable<string> BuildPrelude()
    {
        return new[]
        {
            "$ErrorActionPreference = 'Stop'",
            "$ProgressPreference = 'SilentlyContinue'",
            "$InformationPreference = 'SilentlyContinue'",
            "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)",
            "$OutputEncoding = [Console]::OutputEncoding",
        };
    }

    private List<string> BuildHotApply(ThemeHotApply hot)
    {
        var injector = Path.Combine(_scriptsRoot, "injector.mjs");
        return new List<string>
        {
            "try {",
            "  $hotCodex = Get-DreamSkinCodexInstall",
            $"  $hotIdentity = Get-DreamSkinVerifiedCdpIdentity -Port {_port} -Codex $hotCodex",
            $"  if ($null -eq $hotIdentity) {{ Write-Host {PsString(hot.UnavailableMessage)}; return }}",
            $"  $hotInjector = {PsString(injector)}",
            "  if (-not (Test-Path -LiteralPath $hotInjector)) { throw 'Dream Skin injector is missing.' }",
            "  $hotNode = Get-DreamSkinNodeRuntime",
            $"  $hotArgs = @($hotInjector, '--once', '--port', '{_port}', '--browser-id', $hotIdentity.BrowserId, '--theme-dir', $paths.Active, '--timeout-ms', '8000')",
            "  $hotResult = Invoke-DreamSkinNative -FilePath $hotNode.Path -ArgumentList $hotArgs",
            "  if ($hotResult.ExitCode -ne 0) {",
            $"    Write-Host ({PsString(hot.FailPrefix)} + (($hotResult.Output | Select-Object -Last 12) -join \"`n\"))",
            "    return",
            "  }",
            $"  Write-Host {PsString(hot.SuccessMessage)}",
            "} catch {",
            $"  Write-Host ({PsString(hot.FailPrefix)} + $_.Exception.Message)",
            "}",
        };
    }

    public static string PsString(string value) => "'" + value.Replace("'", "''") + "'";

    private static string FormatArg(string arg)
    {
        if (int.TryParse(arg, out _)) return arg;
        if (arg.StartsWith('-')) return arg;
        return PsString(arg);
    }
}
