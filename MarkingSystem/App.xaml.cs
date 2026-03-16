using MarkingSystem.Services;
using System.Windows;

namespace MarkingSystem;

public partial class App : Application
{
    // TODO: 실서버 전환 시 URL을 설정 파일로 이동
    internal const string AuthBaseUrl = "http://localhost:3000/auth";

    internal static readonly AuthService Auth = new(AuthBaseUrl);

    private async void App_Startup(object sender, StartupEventArgs e)
    {
        if (await Auth.TryAutoLoginAsync())
        {
            new MainWindow(Auth).Show();
        }
        else
        {
            new LoginWindow(Auth).Show();
        }
    }
}
