using System.Text.Json;
using System.Text.RegularExpressions;
using CodexDreamSkinManager.Models;

namespace CodexDreamSkinManager.Services;

/// <summary>主题对象的规范化与 JSON 序列化（对应 Electron 版 normalizeTheme）。</summary>
public static partial class ThemeSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static readonly Regex AccentPattern = AccentRegex();

    public static string Serialize(ThemeModel theme)
        => JsonSerializer.Serialize(Normalize(theme), Options);

    public static ThemeModel? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<ThemeModel>(json, Options); }
        catch { return null; }
    }

    /// <summary>规范化主题（sanitize 输入），对齐 Electron normalizeTheme。</summary>
    public static ThemeModel Normalize(ThemeModel value)
    {
        var theme = value ?? new ThemeModel();
        var art = theme.Art ?? new ThemeArt();

        return new ThemeModel
        {
            Id = Text(theme.Id, "custom", 80),
            Name = Text(theme.Name, "自定义主题", 80),
            Appearance = Choice(theme.Appearance, ["auto", "light", "dark"], "auto"),
            Art = new ThemeArt
            {
                FocusX = Unit(art.FocusX),
                FocusY = Unit(art.FocusY),
                SafeArea = Choice(art.SafeArea, ["auto", "left", "center", "right", "none"], "auto"),
                TaskMode = Choice(art.TaskMode, ["auto", "ambient", "banner", "off"], "auto"),
                TaskOpacity = Unit(art.TaskOpacity),
                HomeOpacity = Unit(art.HomeOpacity),
            },
            BrandSubtitle = OptionalText(theme.BrandSubtitle, 120),
            Tagline = OptionalText(theme.Tagline, 180),
            ProjectPrefix = OptionalText(theme.ProjectPrefix, 120),
            ProjectLabel = OptionalText(theme.ProjectLabel, 120),
            StatusText = OptionalText(theme.StatusText, 120),
            Quote = OptionalText(theme.Quote, 120),
            Accent = SafeAccent(theme.Accent),
            Image = theme.Image,
        };
    }

    private static string Text(string? candidate, string fallback, int maxLength = 120)
    {
        var resolved = Clean(candidate);
        return (resolved.Length > 0 ? resolved : fallback).Truncate(maxLength);
    }

    private static string? OptionalText(string? candidate, int maxLength = 120)
    {
        var resolved = Clean(candidate);
        return resolved.Length > 0 ? resolved.Truncate(maxLength) : null;
    }

    private static string Clean(string? value)
        => string.IsNullOrEmpty(value) ? "" : Regex.Replace(value, "[\u0000-\u001f]", "").Trim();

    private static string Choice(string? candidate, IReadOnlyList<string> allowed, string fallback)
        => candidate is not null && allowed.Contains(candidate) ? candidate : fallback;

    private static double? Unit(double? candidate)
        => candidate is null ? null : Math.Clamp(candidate.Value, 0, 1);

    private static string? SafeAccent(string? accent)
    {
        var trimmed = Clean(accent);
        return trimmed.Length > 0 && AccentPattern.IsMatch(trimmed) ? trimmed : null;
    }

    [GeneratedRegex(@"^(?:#[\da-fA-F]{3,8}|(?:rgb|hsl|oklch|oklab)\([^;{}]{1,96}\))$")]
    private static partial Regex AccentRegex();
}

internal static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
