using System.Text.Json;
using CodexDreamSkinManager.Services;
using CodexDreamSkinManager.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace CodexDreamSkinManager;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _statusTimer;
    private readonly string _settingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexDreamSkinManager", "settings.json");
    private bool _darkMode = true;
    private bool _resizingPane;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Codex Dream Skin Manager";

        // 自定义标题栏
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        if (AppWindow is { } appWindow)
        {
            CenterWindow(appWindow);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
            if (File.Exists(iconPath)) appWindow.SetIcon(iconPath);
        }

        // 导航栏宽度可拖拽调整（handledEventsToo 确保子元素不拦截）
        NavView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnNavViewPointerPressed), true);
        NavView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnNavViewPointerMoved), true);
        NavView.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnNavViewPointerReleased), true);

        // 恢复上次的导航栏宽度和主题模式
        var settings = LoadSettings();
        if (settings.PaneWidth >= 220 && settings.PaneWidth <= 600) NavView.OpenPaneLength = settings.PaneWidth;
        _darkMode = settings.DarkMode;

        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(OverviewPage));

        ApplyTheme();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += async (_, _) => await RefreshPaneStatusAsync();
        _statusTimer.Start();

        _ = RefreshPaneStatusAsync();
    }

    /// <summary>切换导航（会同步左侧高亮并导航 Frame），供页面内跳转调用。</summary>
    public void NavigateTo(string tag)
    {
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem nvi && string.Equals(nvi.Tag as string, tag, StringComparison.Ordinal))
            {
                NavView.SelectedItem = nvi;
                return;
            }
        }
    }

    private static void CenterWindow(AppWindow appWindow)
    {
        var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var width = 1280;
        var height = 860;
        var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            Type pageType = tag switch
            {
                "themes" => typeof(ThemesPage),
                "actions" => typeof(ActionsPage),
                _ => typeof(OverviewPage),
            };
            ContentFrame.Navigate(pageType);
            if (ContentFrame.Content is IStatusPage page) _ = page.RefreshAsync();
        }
    }

    private void OnNavViewDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
    {
        // 折叠后（只留图标）隐藏左下角的状态与按钮，避免被压缩
        PaneFooterContent.Visibility = NavView.PaneDisplayMode == NavigationViewPaneDisplayMode.Left
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnThemeButtonClick(object sender, RoutedEventArgs e)
    {
        _darkMode = !_darkMode;
        ApplyTheme();
        SaveSettings((int)NavView.OpenPaneLength, _darkMode);
    }

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = _darkMode ? ElementTheme.Dark : ElementTheme.Light;

        var background = _darkMode
            ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
            : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        var foreground = _darkMode ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;

        RootGrid.Background = new SolidColorBrush(background);
        ThemeIcon.Glyph = _darkMode ? "\uE708" : "\uE706";

        if (AppWindow is { } appWindow)
        {
            appWindow.TitleBar.BackgroundColor = background;
            appWindow.TitleBar.ForegroundColor = foreground;
            appWindow.TitleBar.ButtonBackgroundColor = background;
            appWindow.TitleBar.ButtonForegroundColor = foreground;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = background;
            appWindow.TitleBar.ButtonInactiveForegroundColor = foreground;
        }
    }

    // ---- 导航栏宽度拖拽调整 + 持久化 ----

    private void OnNavViewPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(NavView);
        if (Math.Abs(point.Position.X - NavView.OpenPaneLength) < 10)
        {
            _resizingPane = true;
            NavView.CapturePointer(e.Pointer);
        }
    }

    private void OnNavViewPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_resizingPane) return;
        var point = e.GetCurrentPoint(NavView);
        var width = point.Position.X;
        if (width > 220 && width < 600)
        {
            NavView.OpenPaneLength = width;
        }
    }

    private void OnNavViewPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_resizingPane)
        {
            _resizingPane = false;
            NavView.ReleasePointerCapture(e.Pointer);
            SaveSettings((int)NavView.OpenPaneLength, _darkMode);
        }
    }

    private GuiSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFile)) return new GuiSettings();
            return JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(_settingsFile)) ?? new GuiSettings();
        }
        catch { return new GuiSettings(); }
    }

    private void SaveSettings(int paneWidth, bool darkMode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
            File.WriteAllText(_settingsFile, JsonSerializer.Serialize(new GuiSettings { PaneWidth = paneWidth, DarkMode = darkMode }));
        }
        catch { /* 忽略 */ }
    }

    private sealed class GuiSettings
    {
        public int PaneWidth { get; set; }
        public bool DarkMode { get; set; } = true;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await RefreshPaneStatusAsync();
        if (ContentFrame.Content is IStatusPage page) await page.RefreshAsync();
    }

    private void OnOpenThemesFolderClick(object sender, RoutedEventArgs e)
        => AppServices.Files.OpenFolder(AppServices.Status.ThemesRoot);

    private void OnOpenStateFolderClick(object sender, RoutedEventArgs e)
        => AppServices.Files.OpenFolder(AppServices.Status.StateRoot);

    private async Task RefreshPaneStatusAsync()
    {
        var snap = await AppServices.Status.ReadAsync();
        RunningStateText.Text = $"运行状态：{snap.RunningText}";
        PausedStateText.Text = $"皮肤显示：{snap.PausedText}";
        ThemeNameText.Text = $"当前主题：{snap.ActiveThemeName ?? "未初始化"}";
        EngineStateText.Text = $"运行时：{snap.EngineText}";
    }
}

/// <summary>页面统一刷新接口，供主窗口轮询/刷新调用。</summary>
public interface IStatusPage
{
    Task RefreshAsync();
}
