using Microsoft.UI.Xaml;

namespace TClient;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Real MainWindow bootstrap comes in Task 13.
        // For now just exit — this task only proves the project builds.
        Exit();
    }
}
