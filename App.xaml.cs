using CodexDreamSkinManager.Services;
using Microsoft.UI.Xaml;

namespace CodexDreamSkinManager;

public partial class App : Application
{
    private Window? _window;

    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Initialize();
        _window = new MainWindow();
        MainWindow = _window;
        _window.Activate();
    }
}
