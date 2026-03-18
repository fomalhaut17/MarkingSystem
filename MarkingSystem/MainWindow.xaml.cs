using MarkingSystem.Services;
using MarkingSystem.ViewModels;
using MarkingSystem.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MarkingSystem;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AuthService   _auth;

    public MainWindow(AuthService auth, AppSettings settings)
    {
        InitializeComponent();

        _auth = auth;
        _vm   = new MainViewModel(auth, settings);
        DataContext = _vm;

        _vm.ShowErrorRequested  += OnShowErrorRequested;
        _vm.LogoutRequested     += OnLogoutRequested;
        _vm.TestDataReset       += (_, _) => LotInquiryViewControl.Clear();
        _vm.ConfirmRequested     = (msg, title)
            => DialogWindow.ShowConfirm(this, msg, title);
        _auth.LogoutRequested   += OnLogoutRequested;
    }

    // ── Barcode input ────────────────────────────────────────────────────────

    private void TxtMaterialBarcode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _vm.ExecuteLookupByMaterial();
    }

    // ── DataGrid checkbox ────────────────────────────────────────────────────

    private void RowCheckBox_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        RelayCommand.Invalidate();
    }

    private void HeaderCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            if (cb.IsChecked == true) _vm.SelectAllCommand.Execute(null);
            else                      _vm.DeselectAllCommand.Execute(null);
        }
    }

    private void LotDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RelayCommand.Invalidate();

    // ── DataGrid cell copy ───────────────────────────────────────────────────

    private void DataGrid_CopyCell(object sender, ExecutedRoutedEventArgs e)
    {
        var dg   = (DataGrid)sender;
        var info = dg.CurrentCell;
        if (info.Item == null || info.Column == null) { e.Handled = true; return; }

        var text = GetCellText(info);
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
        e.Handled = true;
    }

    private static string GetCellText(DataGridCellInfo info)
    {
        if (info.Column is DataGridTextColumn tc &&
            tc.Binding is System.Windows.Data.Binding b)
        {
            var prop = info.Item.GetType().GetProperty(b.Path.Path);
            return prop?.GetValue(info.Item)?.ToString() ?? string.Empty;
        }

        // TemplateColumn: 렌더링된 셀에서 TextBlock을 탐색
        var content = info.Column.GetCellContent(info.Item);
        return FindTextBlock(content)?.Text ?? string.Empty;
    }

    private static TextBlock? FindTextBlock(DependencyObject? parent)
    {
        if (parent == null) return null;
        if (parent is TextBlock tb) return tb;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var result = FindTextBlock(VisualTreeHelper.GetChild(parent, i));
            if (result != null) return result;
        }
        return null;
    }

    // ── Error dialog ─────────────────────────────────────────────────────────

    private void OnShowErrorRequested(object? sender, string message)
        => DialogWindow.ShowError(this, message);

    // ── Logout ───────────────────────────────────────────────────────────────

    private void OnLogoutRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _auth.Logout();
            new LoginWindow(_auth).Show();
            Close();
        });
    }
}
