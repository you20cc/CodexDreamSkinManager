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
        _paused = snap.Paused;
        await SetPreviewAsync(snap.ActiveImagePath);
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => AppServices.PowerShell.RunScriptAsync(
            "start-dream-skin.ps1", new[] { "-Port", "9445", "-RestartExisting" }));
    }

    private void OnEditThemeClick(object sender, RoutedEventArgs e)
        => (App.MainWindow as MainWindow)?.NavigateToThemeEditor();

    private void OnOpenActionsClick(object sender, RoutedEventArgs e)
        => (App.MainWindow as MainWindow)?.NavigateTo("actions");

    private async Task RunAsync(Func<Task<CommandResult>> action)
    {
        SetBusy(true);
        try
        {
            await action();
            await RefreshAsync();
        }
        catch { /* 忽略 */ }
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

}
