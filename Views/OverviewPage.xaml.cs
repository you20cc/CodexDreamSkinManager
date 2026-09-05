using CodexDreamSkinManager.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CodexDreamSkinManager.Views;

public sealed partial class OverviewPage : Page, IStatusPage
{
    private bool _paused;
    private bool _busy;

    public OverviewPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var snap = await AppServices.Status.ReadAsync();
        OverviewThemeNameText.Text = snap.ActiveThemeName ?? "未初始化";
        OverviewPortText.Text = $"Port {snap.Port}";
        _paused = snap.Paused;
        TogglePausedButton.Content = snap.Paused ? "继续显示" : "暂停皮肤";
        await SetPreviewAsync(snap.ActiveImagePath);
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => AppServices.PowerShell.RunScriptAsync(
            "start-dream-skin.ps1", new[] { "-Port", "9445", "-RestartExisting" }));
    }

    private void OnEditThemeClick(object sender, RoutedEventArgs e)
        => (App.MainWindow as MainWindow)?.NavigateTo("themes");

    private void OnOpenActionsClick(object sender, RoutedEventArgs e)
        => (App.MainWindow as MainWindow)?.NavigateTo("actions");

    private async void OnChooseBackgroundClick(object sender, RoutedEventArgs e)
    {
        var imagePath = await AppServices.Files.PickImageAsync(App.MainWindow!);
        if (string.IsNullOrEmpty(imagePath)) return;
        await RunAsync(() => AppServices.Themes.PickBackgroundAsync(imagePath));
    }

    private async void OnTogglePausedClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => AppServices.Themes.SetPausedAsync(!_paused));
    }

    private async void OnSaveThemeClick(object sender, RoutedEventArgs e)
    {
        var name = ThemeNameInput.Text.Trim();
        if (name.Length == 0)
        {
            OutputBox.Text = "请输入主题名称。";
            return;
        }
        await RunAsync(() => AppServices.Themes.SaveThemeAsync(name));
        ThemeNameInput.Text = "";
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
        StartButton.IsEnabled = !busy;
        EditThemeButton.IsEnabled = !busy;
        OpenActionsButton.IsEnabled = !busy;
        ChooseBackgroundButton.IsEnabled = !busy;
        TogglePausedButton.IsEnabled = !busy;
        SaveThemeButton.IsEnabled = !busy;
        ThemeNameInput.IsEnabled = !busy;
    }

    private async Task SetPreviewAsync(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            PreviewImage.Source = null;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        var bitmap = new BitmapImage();
        using (var stream = File.OpenRead(imagePath))
        {
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
        }
        PreviewImage.Source = bitmap;
    }

    private static string FormatOutput(CommandResult result)
    {
        var parts = new List<string> { result.Ok ? $"PASS exit={result.Code}" : $"FAIL exit={result.Code}" };
        if (!string.IsNullOrWhiteSpace(result.Stdout)) parts.Add(result.Stdout.Trim());
        if (!string.IsNullOrWhiteSpace(result.Stderr)) parts.Add(result.Stderr.Trim());
        return string.Join("\n\n", parts);
    }
}
