namespace CodexDreamSkinManager.Models;

/// <summary>一个已保存的主题条目（主题库列表项）。</summary>
public sealed class SavedTheme
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Directory { get; set; } = "";

    /// <summary>是否当前正在应用的主题（用于列表标记）。</summary>
    public bool IsApplied { get; set; }

    public string AppliedText => IsApplied ? "已应用" : "应用";
}

/// <summary>完整运行状态快照（对应 Electron 版 getStatus）。</summary>
public sealed class StatusModel
{
    public int Port { get; set; } = 9445;
    public bool Running { get; set; }
    public bool Paused { get; set; }
    public bool EngineReady { get; set; }
    public string? ActiveThemeId { get; set; }
    public string? ActiveThemeName { get; set; }
    public string? ActiveImagePath { get; set; }
    public List<SavedTheme> SavedThemes { get; set; } = new();
    public string Log { get; set; } = "";
    public string ErrorLog { get; set; } = "";
    public string VerifyLog { get; set; } = "";
    public string StateRoot { get; set; } = "";

    public string RunningText => Running ? $"运行中（端口 {Port}）" : "未运行";
    public string PausedText => Paused ? "已暂停" : "显示中";
    public string EngineText => EngineReady ? "已安装" : "未安装";
}
