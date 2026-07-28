using Microsoft.UI.Xaml;

namespace PCSteward.Desktop;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow window = new();
        window.Activate();
    }
}
