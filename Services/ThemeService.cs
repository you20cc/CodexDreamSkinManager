using CodexDreamSkinManager.Models;

namespace CodexDreamSkinManager.Services;

/// <summary>主题详情（编辑器加载用）。</summary>
public sealed record ThemeDetails(ThemeModel Theme, string ImagePath, bool IsActive);

/// <summary>
/// 主题 CRUD 与皮肤开关，对应 Electron 版 main.js 的 theme:* IPC handlers。
/// 通过 PowerShellRunner 执行 theme 命令（含热应用）。
/// </summary>
public sealed class ThemeService
{
    private static readonly ThemeHotApply DefaultHotApply = new(
        "Codex 未在 9445 皮肤端口运行；已保存，启动皮肤后生效。",
        "已热切换到当前 Codex。",
        "热切换未完成，主题已保存：");

    private readonly PowerShellRunner _runner;
    private readonly StatusReader _status;

    public ThemeService(PowerShellRunner runner, StatusReader status)
    {
        _runner = runner;
        _status = status;
    }

    public Task<ThemeDetails> GetThemeDetailsAsync(string? savedDirectory, CancellationToken ct = default)
        => Task.Run(() => GetThemeDetails(savedDirectory), ct);

    /// <summary>读取主题详情（theme.json + 图片路径）。savedDirectory 为 null 时读当前主题。</summary>
    private ThemeDetails GetThemeDetails(string? savedDirectory)
    {
        var activeDirectory = _status.ActiveThemeDirectory;
        var directory = savedDirectory ?? activeDirectory;

        if (!Directory.Exists(directory))
            throw new InvalidOperationException("主题目录不存在。");

        var themePath = Path.Combine(directory, "theme.json");
        if (!File.Exists(themePath))
            throw new InvalidOperationException("主题元数据缺失或无效。");

        var json = File.ReadAllText(themePath);
        var theme = ThemeSerializer.Deserialize(json) ?? new ThemeModel();
        if (string.IsNullOrWhiteSpace(theme.Image))
            throw new InvalidOperationException("主题元数据缺失或无效。");

        var imagePath = Path.GetFullPath(Path.Combine(directory, theme.Image));
        if (!File.Exists(imagePath))
            throw new InvalidOperationException("主题图片文件不存在。");

        var isActive = PathsEqual(directory, activeDirectory);
        return new ThemeDetails(theme, imagePath, isActive);
    }

    public Task<CommandResult> PickBackgroundAsync(string imagePath, CancellationToken ct = default)
        => _runner.RunThemeAsync(string.Join("\r\n", new[]
        {
            $"$null = Set-DreamSkinActiveTheme -ImagePath {PowerShellRunner.PsString(imagePath)} -Theme $null -StateRoot $StateRoot",
            "Set-DreamSkinPaused -Paused $false -StateRoot $StateRoot | Out-Null",
            $"Write-Host {PowerShellRunner.PsString("背景图已更新。")}",
        }), DefaultHotApply, ct: ct);

    public Task<CommandResult> SetPausedAsync(bool paused, CancellationToken ct = default)
    {
        var hot = new ThemeHotApply(
            "Codex 未在 9445 皮肤端口运行；状态已保存。",
            paused ? "当前 Codex 已立即移除皮肤。" : "当前 Codex 已立即恢复皮肤。",
            paused ? "热移除未完成，状态已保存：" : "热恢复未完成，状态已保存：");
        return _runner.RunThemeAsync(string.Join("\r\n", new[]
        {
            $"Set-DreamSkinPaused -Paused ${(paused ? "true" : "false")} -StateRoot $StateRoot | Out-Null",
            $"Write-Host {PowerShellRunner.PsString(paused ? "皮肤已暂停。" : "皮肤已继续显示。")}",
        }), hot, ct: ct);
    }

    public Task<CommandResult> SaveThemeAsync(string name, CancellationToken ct = default)
        => _runner.RunThemeAsync(string.Join("\r\n", new[]
        {
            $"$saved = Save-DreamSkinCurrentTheme -Name {PowerShellRunner.PsString(name)} -StateRoot $StateRoot",
            "Write-Host (\"已保存主题：\" + $saved.Theme.name)",
        }), null, ct: ct);

    public Task<CommandResult> UseThemeAsync(string directory, CancellationToken ct = default)
        => _runner.RunThemeAsync(string.Join("\r\n", new[]
        {
            $"$null = Use-DreamSkinSavedTheme -ThemeDirectory {PowerShellRunner.PsString(directory)} -StateRoot $StateRoot",
            "Set-DreamSkinPaused -Paused $false -StateRoot $StateRoot | Out-Null",
            $"Write-Host {PowerShellRunner.PsString("已切换主题。")}",
        }), DefaultHotApply, ct: ct);

    public Task<CommandResult> SaveEditedCopyAsync(string imagePath, ThemeModel theme, CancellationToken ct = default)
        => _runner.RunThemeAsync(
            BuildSaveThemeCopy(ThemeSerializer.Serialize(theme), imagePath, "已另存为主题："),
            null, ct: ct);

    public Task<CommandResult> SaveSelectedEditAsync(
        string sourceKind,
        string? savedDirectory,
        string imagePath,
        ThemeModel theme,
        CancellationToken ct = default)
    {
        var json = ThemeSerializer.Serialize(theme);
        if (sourceKind == "new")
        {
            return _runner.RunThemeAsync(
                BuildSaveThemeCopy(json, imagePath, "已保存自定义主题："),
                null, ct: ct);
        }

        var details = GetThemeDetails(savedDirectory);
        if (details.IsActive)
        {
            return _runner.RunThemeAsync(string.Join("\r\n", new[]
            {
                $"$theme = {PowerShellRunner.PsString(json)} | ConvertFrom-Json -ErrorAction Stop",
                $"$null = Set-DreamSkinActiveTheme -ImagePath {PowerShellRunner.PsString(imagePath)} -Theme $theme -StateRoot $StateRoot",
                "Set-DreamSkinPaused -Paused $false -StateRoot $StateRoot | Out-Null",
                $"Write-Host {PowerShellRunner.PsString("当前主题已保存。")}",
            }), DefaultHotApply, ct: ct);
        }

        // 保存到主题库中的既有主题（saved 且非 active）
        var themeDirectory = savedDirectory ?? throw new InvalidOperationException("主题目录无效。");
        return _runner.RunThemeAsync(BuildSaveSavedTheme(json, themeDirectory, imagePath), DefaultHotApply, ct: ct);
    }

    public void DeleteTheme(string directory)
    {
        var themesRoot = _status.ThemesRoot;
        if (!IsInside(directory, themesRoot))
            throw new InvalidOperationException("只能删除主题库中的主题。");
        if (!Directory.Exists(directory))
            throw new InvalidOperationException("主题目录不存在。");
        Directory.Delete(directory, recursive: true);
    }

    // ---- 脚本构建（对应 main.js 的 saveThemeCopyCommand / save-selected-edit 的 saved 分支） ----

    private static string BuildSaveThemeCopy(string themeJson, string imagePath, string message)
        => string.Join("\r\n", new[]
        {
            $"$theme = {PowerShellRunner.PsString(themeJson)} | ConvertFrom-Json -ErrorAction Stop",
            $"$imagePath = {PowerShellRunner.PsString(imagePath)}",
            "Assert-DreamSkinImageFile -Path $imagePath",
            "$id = (Get-Date).ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)",
            "$destination = Join-Path $paths.Saved $id",
            "Ensure-DreamSkinManagedDirectory -Path $destination -Root $paths.Root",
            "$extension = [System.IO.Path]::GetExtension($imagePath).ToLowerInvariant()",
            "$imageName = 'art' + $extension",
            "$target = Join-Path $destination $imageName",
            "Assert-DreamSkinNoReparseComponents -Path $target",
            "Copy-Item -LiteralPath $imagePath -Destination $target -Force",
            "Assert-DreamSkinImageFile -Path $target",
            "$theme | Add-Member -NotePropertyName id -NotePropertyValue $id -Force",
            "$theme | Add-Member -NotePropertyName image -NotePropertyValue $imageName -Force",
            "Write-DreamSkinTheme -ThemeDirectory $destination -Theme $theme",
            $"Write-Host ({PowerShellRunner.PsString(message)} + $theme.name)",
        });

    private static string BuildSaveSavedTheme(string themeJson, string themeDirectory, string imagePath)
        => string.Join("\r\n", new[]
        {
            $"$themeDirectory = {PowerShellRunner.PsString(themeDirectory)}",
            $"$theme = {PowerShellRunner.PsString(themeJson)} | ConvertFrom-Json -ErrorAction Stop",
            $"$loaded = Read-DreamSkinTheme -ThemeDirectory $themeDirectory",
            $"$imagePath = {PowerShellRunner.PsString(imagePath)}",
            "if ([System.IO.Path]::GetFullPath($imagePath) -ine [System.IO.Path]::GetFullPath($loaded.ImagePath)) {",
            "  Assert-DreamSkinImageFile -Path $imagePath",
            "  $extension = [System.IO.Path]::GetExtension($imagePath).ToLowerInvariant()",
            "  $imageName = New-DreamSkinThemeImageName -Extension $extension",
            "  $target = Join-Path $themeDirectory $imageName",
            "  Copy-Item -LiteralPath $imagePath -Destination $target -Force",
            "  Assert-DreamSkinImageFile -Path $target",
            "  $theme | Add-Member -NotePropertyName image -NotePropertyValue $imageName -Force",
            "} else {",
            "  $theme | Add-Member -NotePropertyName image -NotePropertyValue ([System.IO.Path]::GetFileName($loaded.ImagePath)) -Force",
            "}",
            "Write-DreamSkinTheme -ThemeDirectory $themeDirectory -Theme $theme",
            $"Write-Host {PowerShellRunner.PsString("所选主题已保存。")}",
            "$activeThemePath = Join-Path $paths.Active 'theme.json'",
            "$activeTheme = if (Test-Path -LiteralPath $activeThemePath) { (Read-DreamSkinUtf8File -Path $activeThemePath) | ConvertFrom-Json -ErrorAction Stop } else { $null }",
            "$shouldHotApply = [bool]($activeTheme -and $activeTheme.id -and $theme.id -and $activeTheme.id -eq $theme.id)",
            "if (-not $shouldHotApply) { return }",
            "$null = Use-DreamSkinSavedTheme -ThemeDirectory $themeDirectory -StateRoot $StateRoot",
            "Set-DreamSkinPaused -Paused $false -StateRoot $StateRoot | Out-Null",
            $"Write-Host {PowerShellRunner.PsString("已同步到当前主题。")}",
        });

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string candidate, string root)
    {
        var full = Path.GetFullPath(candidate);
        var fullRoot = Path.GetFullPath(root);
        return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
