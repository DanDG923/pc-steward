using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PCSteward.Desktop;

public sealed class MainWindow : Window
{
    public MainWindow()
    {
        Title = AppIdentity.DisplayName;
        Content = new TextBlock
        {
            Text = $"{AppIdentity.DisplayName} is ready.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
