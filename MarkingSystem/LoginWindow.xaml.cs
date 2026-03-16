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
        if (!string.IsNullOrEmpty(_auth.SavedCompanyCode) || !string.IsNullOrEmpty(_auth.SavedUsername))
        {
            TxtCompanyCode.Text    = _auth.SavedCompanyCode ?? string.Empty;
            TxtUsername.Text       = _auth.SavedUsername    ?? string.Empty;
            ChkRememberMe.IsChecked = true;
            PwdPassword.Focus();
        }
        else
        {
            TxtCompanyCode.Focus();
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
        var companyCode = TxtCompanyCode.Text.Trim();
        var username    = TxtUsername.Text.Trim();
        var password    = PwdPassword.Password;

        if (string.IsNullOrEmpty(companyCode) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("업체코드, 아이디, 비밀번호를 모두 입력하세요.");
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

        var main = new MainWindow(_auth);
        main.Show();
        Close();
    }

    private void ShowError(string message)
    {
        TxtError.Text       = message;
        TxtError.Visibility = Visibility.Visible;
    }
}
