using System.Collections.ObjectModel;
using System.Text.Json;
using CodexDreamSkinManager.Models;
using CodexDreamSkinManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CodexDreamSkinManager.Views;

public sealed partial class ThemesPage : Page, IStatusPage
{
    private readonly ObservableCollection<SavedTheme> _savedThemes = new();
    private readonly ObservableCollection<SavedTheme> _hiddenThemes = new();
    private readonly string _orderFile;
    private readonly string _hiddenFile;

    private string _editorSourceKind = "active";
    private string? _editorSavedDirectory;
    private string? _editorImagePath;
    private string _editorThemeId = "custom";
    private bool _busy;
    private bool _suppressSave;

    public ThemesPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        ThemesGrid.ItemsSource = _savedThemes;
        HiddenThemesGrid.ItemsSource = _hiddenThemes;
        _savedThemes.CollectionChanged += (_, _) => { if (!_suppressSave) SaveOrder(); };
        _orderFile = Path.Combine(AppServices.Status.StateRoot, "gui-theme-order.json");
        _hiddenFile = Path.Combine(AppServices.Status.StateRoot, "gui-hidden-themes.json");
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var snap = await AppServices.Status.ReadAsync();
        var hidden = LoadHiddenSet();

        _suppressSave = true;
        try
        {
            _savedThemes.Clear();
            _hiddenThemes.Clear();
            foreach (var theme in ApplyOrder(snap.SavedThemes))
            {
                theme.IsApplied = theme.Id.Length > 0 && theme.Id == snap.ActiveThemeId;
                // 重置缩略图，让重新加载
                theme.Thumbnail = null;
                if (hidden.Contains(theme.Directory))
                {
                    _hiddenThemes.Add(theme);
                }
                else
                {
                    _savedThemes.Add(theme);
                }
            }
        }
        finally
        {
            _suppressSave = false;
        }
        await LoadThumbnailsAsync(_savedThemes);
        await LoadThumbnailsAsync(_hiddenThemes);
    }

    // ---- 缩略图 ----

    private static async Task LoadThumbnailsAsync(IEnumerable<SavedTheme> themes)
    {
        foreach (var theme in themes)
        {
            if (theme.Thumbnail is not null) continue;
            var imagePath = TryGetThemeImage(theme.Directory);
            if (imagePath is null) continue;
            try
            {
                var bitmap = new BitmapImage();
                using (var stream = File.OpenRead(imagePath))
                {
                    await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                }
                theme.Thumbnail = bitmap;
            }
            catch { /* 跳过损坏图片 */ }
        }
    }

    private static string? TryGetThemeImage(string directory)
    {
        try
        {
            var themePath = Path.Combine(directory, "theme.json");
            if (!File.Exists(themePath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(themePath));
            if (!doc.RootElement.TryGetProperty("image", out var img)) return null;
            var name = img.GetString();
            if (string.IsNullOrEmpty(name)) return null;
            var full = Path.GetFullPath(Path.Combine(directory, name));
            return File.Exists(full) ? full : null;
        }
        catch { return null; }
    }

    // ---- 视图切换（带动画）：主题库 ⇄ 编辑 / 隐藏列表 ----

    private FrameworkElement? _currentView;

    private void ShowView(FrameworkElement target)
    {
        FrameworkElement[] views = { LibraryView, EditorView, HiddenView };
        foreach (var view in views)
        {
            if (ReferenceEquals(view, target)) continue;
            if (view.Visibility == Visibility.Visible)
            {
                // 退出动画：淡出
                AnimateOpacity(view, 1, 0, 120, () =>
                {
                    view.Visibility = Visibility.Collapsed;
                    // 进入目标视图：淡入 + 轻微上移
                    target.Opacity = 0;
                    if (target.RenderTransform is TranslateTransform translate)
                    {
                        var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(160) };
                        var slideIn = new DoubleAnimation { From = 16, To = 0, Duration = TimeSpan.FromMilliseconds(160) };
                        Storyboard.SetTarget(fadeIn, target);
                        Storyboard.SetTargetProperty(fadeIn, "Opacity");
                        Storyboard.SetTarget(slideIn, translate);
                        Storyboard.SetTargetProperty(slideIn, "Y");
                        var board = new Storyboard();
                        board.Children.Add(fadeIn);
                        board.Children.Add(slideIn);
                        target.Visibility = Visibility.Visible;
                        board.Begin();
                        _currentView = target;
                    }
                    else
                    {
                        var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(160) };
                        Storyboard.SetTarget(fadeIn, target);
                        Storyboard.SetTargetProperty(fadeIn, "Opacity");
                        var board = new Storyboard();
                        board.Children.Add(fadeIn);
                        target.Visibility = Visibility.Visible;
                        board.Begin();
                        _currentView = target;
                    }
                });
                return;
            }
        }

        // 没有可见视图（首次）：直接淡入
        target.Opacity = 0;
        var firstFade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(160) };
        Storyboard.SetTarget(firstFade, target);
        Storyboard.SetTargetProperty(firstFade, "Opacity");
        var firstBoard = new Storyboard();
        firstBoard.Children.Add(firstFade);
        target.Visibility = Visibility.Visible;
        firstBoard.Begin();
        _currentView = target;
    }

    private static void AnimateOpacity(FrameworkElement element, double from, double to, double ms, Action? completed = null)
    {
        var fade = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms) };
        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var board = new Storyboard();
        board.Children.Add(fade);
        if (completed is not null) board.Completed += (_, _) => completed();
        board.Begin();
    }

    private void ShowLibrary() => ShowView(LibraryView);
    private void ShowEditor() => ShowView(EditorView);
    private void ShowHidden() => ShowView(HiddenView);

    // ---- 主题卡片大小切换 ----

    private int _cardWidth = 240;
    private int _cardHeight = 140;

    private void OnSmallSizeClick(object sender, RoutedEventArgs e)
    {
        _cardWidth = 180; _cardHeight = 100;
        ApplyCardSize();
    }

    private void OnLargeSizeClick(object sender, RoutedEventArgs e)
    {
        _cardWidth = 240; _cardHeight = 140;
        ApplyCardSize();
    }

    private void ApplyCardSize()
    {
        // 刷新 GridView 重新应用 ItemTemplate
        ThemesGrid.ItemsSource = null;
        ThemesGrid.ItemsSource = _savedThemes;
        HiddenThemesGrid.ItemsSource = null;
        HiddenThemesGrid.ItemsSource = _hiddenThemes;
    }

    public int CardWidth => _cardWidth;
    public int CardHeight => _cardHeight;

    // ---- 隐藏主题持久化 ----

    private HashSet<string> LoadHiddenSet()
    {
        try
        {
            if (!File.Exists(_hiddenFile)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_hiddenFile));
            return list is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }
        catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    private void SaveHiddenSet(IEnumerable<string> directories)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_hiddenFile)!);
            File.WriteAllText(_hiddenFile, JsonSerializer.Serialize(directories.ToList()));
        }
        catch { /* 忽略 */ }
    }

    // ---- 编辑器加载 ----

    private async Task LoadEditorAsync(string sourceKind, string? savedDirectory)
    {
        try
        {
            if (sourceKind == "new")
            {
                // 新建：以当前主题图片为底，套默认主题文案
                var active = await AppServices.Themes.GetThemeDetailsAsync(null);
                _editorSourceKind = "new";
                _editorSavedDirectory = null;
                _editorImagePath = active.ImagePath;
                _editorThemeId = "custom";
                FillEditor(DefaultCustomTheme(), false, "新建自定义主题");
                ShowEditor();
                return;
            }

            var details = await AppServices.Themes.GetThemeDetailsAsync(savedDirectory);
            _editorSourceKind = sourceKind;
            _editorSavedDirectory = savedDirectory;
            _editorImagePath = details.ImagePath;
            _editorThemeId = details.Theme.Id;
            FillEditor(details.Theme, details.IsActive, details.IsActive ? "当前主题" : details.Theme.Name);
            ShowEditor();
        }
        catch (Exception ex)
        {
            OutputBox.Text = ex.Message;
        }
    }

    private void FillEditor(ThemeModel theme, bool isActive, string label)
    {
        var art = theme.Art ?? new ThemeArt();
        EditorSourceText.Text = label;
        EditName.Text = theme.Name;
        EditBrandSubtitle.Text = theme.BrandSubtitle ?? "";
        EditTagline.Text = theme.Tagline ?? "";
        EditProjectPrefix.Text = theme.ProjectPrefix ?? "";
        EditProjectLabel.Text = theme.ProjectLabel ?? "";
        EditStatusText.Text = theme.StatusText ?? "";
        EditQuote.Text = theme.Quote ?? "";
        SetComboIndex(EditAppearance, theme.Appearance);
        SetComboIndex(EditSafeArea, art.SafeArea ?? "auto");
        SetComboIndex(EditTaskMode, art.TaskMode ?? "auto");
        EditTaskOpacity.Value = Clamp01(art.TaskOpacity ?? DefaultTaskOpacity(theme));
        EditHomeOpacity.Value = Clamp01(art.HomeOpacity ?? DefaultHomeOpacity(theme));
        EditAccent.Text = theme.Accent ?? "";
        EditFocusX.Value = Clamp01(art.FocusX ?? 0.5);
        EditFocusY.Value = Clamp01(art.FocusY ?? 0.5);
        UpdateRangeLabels();
        _ = SetPreviewAsync(_editorImagePath);
        UpdateDeleteButton();
    }

    private void UpdateDeleteButton()
        => DeleteButton.IsEnabled = !_busy && _editorSourceKind == "saved";

    // ---- 表单收集 ----

    private ThemeModel CollectEditorTheme()
    {
        return new ThemeModel
        {
            Id = _editorSourceKind == "new" ? "custom" : _editorThemeId,
            Name = EditName.Text.Trim().Length > 0 ? EditName.Text.Trim() : "自定义主题",
            Appearance = GetComboValue(EditAppearance),
            Art = new ThemeArt
            {
                FocusX = EditFocusX.Value,
                FocusY = EditFocusY.Value,
                SafeArea = GetComboValue(EditSafeArea),
                TaskMode = GetComboValue(EditTaskMode),
                TaskOpacity = EditTaskOpacity.Value,
                HomeOpacity = EditHomeOpacity.Value,
            },
            BrandSubtitle = Optional(EditBrandSubtitle.Text),
            Tagline = Optional(EditTagline.Text),
            ProjectPrefix = Optional(EditProjectPrefix.Text),
            ProjectLabel = Optional(EditProjectLabel.Text),
            StatusText = Optional(EditStatusText.Text),
            Quote = Optional(EditQuote.Text),
            Accent = Optional(EditAccent.Text),
        };
    }

    // ---- 事件 ----

    private void OnThemeItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SavedTheme theme)
            _ = LoadEditorAsync("saved", theme.Directory);
    }

    /// <summary>供外部调用：切换到主题管理页并直接打开当前主题的编辑器。</summary>
    public async Task LoadCurrentThemeIntoEditor()
    {
        await RefreshAsync();
        await LoadEditorAsync("active", null);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
        => ShowLibrary();

    private void OnHiddenClick(object sender, RoutedEventArgs e)
        => ShowHidden();

    private async void OnHideThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string directory }) return;
        await HideThemeAsync(directory);
    }

    private async void OnHideEditorClick(object sender, RoutedEventArgs e)
    {
        if (_editorSourceKind == "saved" && _editorSavedDirectory is not null)
        {
            await HideThemeAsync(_editorSavedDirectory);
        }
        else
        {
            OutputBox.Text = "只能隐藏主题库中已保存的主题。";
        }
    }

    private async Task HideThemeAsync(string directory)
    {
        var hidden = LoadHiddenSet();
        hidden.Add(directory);
        SaveHiddenSet(hidden);
        await RefreshAsync();
        ShowLibrary();
    }

    private async void OnUnhideThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string directory }) return;
        var hidden = LoadHiddenSet();
        hidden.Remove(directory);
        SaveHiddenSet(hidden);
        await RefreshAsync();
        // 如果隐藏列表已空，回主题库
        if (_hiddenThemes.Count == 0) ShowLibrary(); else ShowHidden();
    }

    private async void OnNewClick(object sender, RoutedEventArgs e)
        => await LoadEditorAsync("new", null);

    private async void OnEditActiveClick(object sender, RoutedEventArgs e)
        => await LoadEditorAsync("active", null);

    private async void OnApplyThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string directory }) return;
        await RunAsync(() => AppServices.Themes.UseThemeAsync(directory));
    }

    private async void OnDeleteThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string directory }) return;
        var name = _savedThemes.Concat(_hiddenThemes)
            .FirstOrDefault(t => t.Directory == directory)?.Name ?? "该主题";
        var dialog = new ContentDialog
        {
            Title = "确认删除",
            Content = $"确定删除主题“{name}”？此操作不能撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            AppServices.Themes.DeleteTheme(directory);
            var hidden = LoadHiddenSet();
            hidden.Remove(directory);
            SaveHiddenSet(hidden);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            OutputBox.Text = ex.Message;
        }
    }

    private async void OnChooseImageClick(object sender, RoutedEventArgs e)
    {
        var imagePath = await AppServices.Files.PickImageAsync(App.MainWindow!);
        if (string.IsNullOrEmpty(imagePath)) return;
        _editorImagePath = imagePath;
        await SetPreviewAsync(imagePath);
        OutputBox.Text = "背景已载入编辑器。";
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_editorImagePath is null)
        {
            OutputBox.Text = "请先选择背景图。";
            return;
        }
        var theme = CollectEditorTheme();
        await RunAsync(() => AppServices.Themes.SaveSelectedEditAsync(
            _editorSourceKind, _editorSavedDirectory, _editorImagePath, theme));
    }

    private async void OnSaveCopyClick(object sender, RoutedEventArgs e)
    {
        if (_editorImagePath is null)
        {
            OutputBox.Text = "请先选择背景图。";
            return;
        }
        var theme = CollectEditorTheme();
        await RunAsync(() => AppServices.Themes.SaveEditedCopyAsync(_editorImagePath, theme));
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_editorSourceKind != "saved" || _editorSavedDirectory is null)
        {
            OutputBox.Text = "只能删除主题库中已保存的主题。";
            return;
        }
        var name = EditName.Text.Trim().Length > 0 ? EditName.Text.Trim() : "当前选中主题";
        var dialog = new ContentDialog
        {
            Title = "确认删除",
            Content = $"确定删除主题“{name}”？此操作不能撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            AppServices.Themes.DeleteTheme(_editorSavedDirectory);
            OutputBox.Text = "主题已删除。";
            var hidden = LoadHiddenSet();
            hidden.Remove(_editorSavedDirectory);
            SaveHiddenSet(hidden);
            await RefreshAsync();
            ShowLibrary();
        }
        catch (Exception ex)
        {
            OutputBox.Text = ex.Message;
        }
    }

    private void OnRangeValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => UpdateRangeLabels();

    // ---- 辅助 ----

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
        NewButton.IsEnabled = !busy;
        EditActiveButton.IsEnabled = !busy;
        ChooseImageButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
        SaveCopyButton.IsEnabled = !busy;
        UpdateDeleteButton();
    }

    private void UpdateRangeLabels()
    {
        var baseTask = EditTaskOpacity.Value;
        var taskMode = GetComboValue(EditTaskMode);
        var effectiveTask = taskMode == "banner" ? Math.Min(0.95, baseTask * 1.8) : baseTask;
        TaskOpacityValue.Text = taskMode == "off" ? "关闭" : $"{Math.Round(effectiveTask * 100)}%";
        HomeOpacityValue.Text = $"{Math.Round(EditHomeOpacity.Value * 100)}%";
        FocusXValue.Text = EditFocusX.Value.ToString("0.00");
        FocusYValue.Text = EditFocusY.Value.ToString("0.00");
    }

    private async Task SetPreviewAsync(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            EditorPreview.Source = null;
            EditorPreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }
        EditorPreviewPlaceholder.Visibility = Visibility.Collapsed;
        var bitmap = new BitmapImage();
        using (var stream = File.OpenRead(imagePath))
        {
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
        }
        EditorPreview.Source = bitmap;
    }

    private static void SetComboIndex(ComboBox combo, string value)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item &&
                string.Equals(item.Content as string, value, StringComparison.Ordinal))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static string GetComboValue(ComboBox combo)
        => (combo.SelectedItem as ComboBoxItem)?.Content as string ?? "auto";

    private static string? Optional(string raw)
    {
        var value = raw.Trim();
        return value.Length > 0 ? value : null;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static double DefaultTaskOpacity(ThemeModel theme)
        => theme.Appearance == "dark" ? 0.22 : 0.18;

    private static double DefaultHomeOpacity(ThemeModel theme)
        => theme.Appearance == "dark" ? 0.91 : 0.93;

    private static ThemeModel DefaultCustomTheme() => new()
    {
        Id = "custom",
        Name = "自定义主题",
        BrandSubtitle = "CODEX DREAM SKIN",
        Tagline = "把今天的工作台调成想要的样子。",
        ProjectPrefix = "选择项目 · ",
        ProjectLabel = "◉  选择项目",
        StatusText = "CUSTOM THEME ONLINE",
        Quote = "MAKE SOMETHING YOURS",
        Appearance = "auto",
        Art = new ThemeArt
        {
            FocusX = 0.5,
            FocusY = 0.5,
            SafeArea = "auto",
            TaskMode = "auto",
            TaskOpacity = 0.18,
            HomeOpacity = 0.93,
        },
    };

    private static string FormatOutput(CommandResult result)
    {
        var parts = new List<string> { result.Ok ? $"PASS exit={result.Code}" : $"FAIL exit={result.Code}" };
        if (!string.IsNullOrWhiteSpace(result.Stdout)) parts.Add(result.Stdout.Trim());
        if (!string.IsNullOrWhiteSpace(result.Stderr)) parts.Add(result.Stderr.Trim());
        return string.Join("\n\n", parts);
    }

    // ---- 排序持久化（对应 Electron 的 localStorage dreamSkinThemeOrder） ----

    private List<SavedTheme> ApplyOrder(List<SavedTheme> themes)
    {
        var order = LoadOrder();
        if (order is null || order.Count == 0) return themes;
        var map = themes.ToDictionary(t => t.Directory, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<SavedTheme>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in order)
        {
            if (map.TryGetValue(dir, out var theme))
            {
                ordered.Add(theme);
                seen.Add(dir);
            }
        }
        foreach (var theme in themes)
        {
            if (!seen.Contains(theme.Directory)) ordered.Add(theme);
        }
        return ordered;
    }

    private List<string>? LoadOrder()
    {
        try
        {
            if (!File.Exists(_orderFile)) return null;
            var json = File.ReadAllText(_orderFile);
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch { return null; }
    }

    private void SaveOrder()
    {
        try
        {
            var order = _savedThemes.Select(t => t.Directory).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_orderFile)!);
            File.WriteAllText(_orderFile, JsonSerializer.Serialize(order));
        }
        catch { /* 忽略 */ }
    }
}
