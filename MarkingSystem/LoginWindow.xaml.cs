using MarkingSystem.Services;
using System.Windows;
using System.Windows.Input;

namespace MarkingSystem;

public partial class LoginWindow : Window
{
    private readonly AuthService _auth;

    public LoginWindow(AuthService auth)
    {
        InitializeComponent();
        _auth = auth;
        Loaded += (_, _) => RestoreFormAndFocus();
    }

    private void RestoreFormAndFocus()
    {
        if (!string.IsNullOrEmpty(_auth.SavedLoginId))
        {
            TxtUsername.Text        = _auth.SavedLoginId;
            ChkRememberMe.IsChecked = true;
            PwdPassword.Focus();
        }
        else
        {
            TxtUsername.Focus();
        }
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = ExecuteLoginAsync();
    }

    private void LoginBtn_Click(object sender, RoutedEventArgs e)
        => _ = ExecuteLoginAsync();

    private async Task ExecuteLoginAsync()
    {
        const string loginCompany = "DEMO"; // 임시 고정, 차후 재사용 예정
        var loginId       = TxtUsername.Text.Trim();
        var loginPassword = PwdPassword.Password;

        if (string.IsNullOrEmpty(loginId) || string.IsNullOrEmpty(loginPassword))
        {
            ShowError("아이디, 비밀번호를 모두 입력하세요.");
            return;
        }

        TxtError.Visibility = Visibility.Collapsed;

        var rememberMe = ChkRememberMe.IsChecked == true;
        var error = await _auth.LoginAsync(loginCompany, loginId, loginPassword, rememberMe);
        if (error != null)
        {
            ShowError(error);
            PwdPassword.Clear();
            return;
        }

        var main = new MainWindow(_auth, App.Settings);
        main.Show();
        Close();
    }

    private void ShowError(string message)
    {
        TxtError.Text       = message;
        TxtError.Visibility = Visibility.Visible;
    }
}
