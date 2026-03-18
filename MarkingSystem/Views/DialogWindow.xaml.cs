using System.Windows;

namespace MarkingSystem.Views;

public partial class DialogWindow : Window
{
    public bool Result { get; private set; } = false;

    private DialogWindow() => InitializeComponent();

    /// <summary>확인 버튼 하나짜리 알림창.</summary>
    public static void ShowError(Window owner, string message, string title = "오류")
    {
        var dlg = new DialogWindow { Owner = owner, Title = title };
        dlg.TxtMessage.Text    = message;
        dlg.BtnCancel.Visibility = Visibility.Collapsed;
        dlg.ShowDialog();
    }

    /// <summary>예/아니오 확인창. 예 선택 시 true 반환.</summary>
    public static bool ShowConfirm(Window owner, string message, string title = "확인")
    {
        var dlg = new DialogWindow { Owner = owner, Title = title };
        dlg.TxtMessage.Text  = message;
        dlg.BtnOk.Content    = "예";
        dlg.ShowDialog();
        return dlg.Result;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Result = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }
}
