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
        if (!string.IsNullOrEmpty(_auth.SavedUsername))
        {
            TxtUsername.Text        = _auth.SavedUsername;
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
        const string companyCode = "DEMO"; // 임시 고정, 차후 재사용 예정
        var username = TxtUsername.Text.Trim();
        var password = PwdPassword.Password;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("아이디, 비밀번호를 모두 입력하세요.");
            return;
        }

        TxtError.Visibility = Visibility.Collapsed;

        var rememberMe = ChkRememberMe.IsChecked == true;
        var error = await _auth.LoginAsync(companyCode, username, password, rememberMe);
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
