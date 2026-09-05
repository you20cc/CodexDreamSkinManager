using System.Text.Json;
using CodexDreamSkinManager.Models;

namespace CodexDreamSkinManager.Services;

/// <summary>读取 %LOCALAPPDATA%\CodexDreamSkin 下的状态、主题与日志。</summary>
public sealed class StatusReader
{
    private const int LogTailBytes = 24 * 1024;
    private const int Port = 9445;
    private readonly string _stateRoot;

    public StatusReader()
    {
        _stateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexDreamSkin");
    }

    public string StateRoot => _stateRoot;
    public string ThemesRoot => Path.Combine(_stateRoot, "themes");
    public string ActiveThemeDirectory => Path.Combine(_stateRoot, "active-theme");

    public Task<StatusModel> ReadAsync(CancellationToken ct = default)
        => Task.Run(Read, ct);

    private StatusModel Read()
    {
        var state = ReadState();
        var activeTheme = ReadActiveTheme(out var activeImagePath);
        var running = state is { Port: Port, InjectorPid: not null and > 0 };
        var paused = File.Exists(Path.Combine(_stateRoot, "paused"));
        var engineReady = File.Exists(Path.Combine(_stateRoot, "engine", "scripts", "start-dream-skin.ps1"));

        return new StatusModel
        {
            Port = Port,
            Running = running,
            Paused = paused,
            EngineReady = engineReady,
            ActiveThemeId = activeTheme?.Id,
            ActiveThemeName = activeTheme?.Name,
            ActiveImagePath = activeImagePath,
            SavedThemes = ReadSavedThemes(),
            Log = TailFile(Path.Combine(_stateRoot, "injector.log")),
            ErrorLog = TailFile(Path.Combine(_stateRoot, "injector-error.log")),
            VerifyLog = TailFile(Path.Combine(_stateRoot, "verify.log")),
            StateRoot = _stateRoot,
        };
    }

    private StateInfo? ReadState()
    {
        var path = Path.Combine(_stateRoot, "state.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return new StateInfo(
                root.TryGetProperty("port", out var p) && p.TryGetInt32(out var port) ? port : 0,
                root.TryGetProperty("injectorPid", out var pid) && pid.TryGetInt32(out var pidVal) ? pidVal : null,
                root.TryGetProperty("codexVersion", out var v) ? v.GetString() : null);
        }
        catch { return null; }
    }

    private ThemeModel? ReadActiveTheme(out string? imagePath)
    {
        imagePath = null;
        var directory = ActiveThemeDirectory;
        var path = Path.Combine(directory, "theme.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var theme = new ThemeModel
            {
                Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? "custom" : "custom",
                Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "自定义主题" : "自定义主题",
            };
            if (root.TryGetProperty("image", out var img) && img.GetString() is { Length: > 0 } imageName)
            {
                var full = Path.GetFullPath(Path.Combine(directory, imageName));
                if (File.Exists(full)) imagePath = full;
            }
            return theme;
        }
        catch { return null; }
    }

    private List<SavedTheme> ReadSavedThemes()
    {
        var result = new List<SavedTheme>();
        if (!Directory.Exists(ThemesRoot)) return result;
        foreach (var directory in Directory.EnumerateDirectories(ThemesRoot))
        {
            var themePath = Path.Combine(directory, "theme.json");
            if (!File.Exists(themePath)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(themePath));
                var root = doc.RootElement;
                result.Add(new SavedTheme
                {
                    Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Directory = directory,
                });
            }
            catch { /* 跳过损坏的主题 */ }
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return result;
    }

    private static string TailFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var length = Math.Min(bytes.Length, LogTailBytes);
            var buffer = new byte[length];
            Array.Copy(bytes, bytes.Length - length, buffer, 0, length);
            return System.Text.Encoding.UTF8.GetString(buffer);
        }
        catch { return string.Empty; }
    }

    private sealed record StateInfo(int Port, int? InjectorPid, string? CodexVersion);
}
