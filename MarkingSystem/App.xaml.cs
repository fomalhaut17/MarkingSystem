using MarkingSystem.Services;
using System.Windows;

namespace MarkingSystem;

public partial class App : Application
{
    internal static AppSettings  Settings { get; private set; } = new();
    internal static AuthService  Auth     { get; private set; } = null!;

    private async void App_Startup(object sender, StartupEventArgs e)
    {
        Settings = AppSettings.Load();
        Auth     = new AuthService(Settings.Api.AuthBaseUrl);

        if (await Auth.TryAutoLoginAsync())
            new MainWindow(Auth, Settings).Show();
        else
            new LoginWindow(Auth).Show();
    }
}
