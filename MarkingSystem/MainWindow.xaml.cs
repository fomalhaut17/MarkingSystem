using MarkingSystem.ViewModels;
using MarkingSystem.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MarkingSystem;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.ProductionLotInquiryRequested += OnProductionLotInquiryRequested;
        _vm.NotificationRequested         += OnNotificationRequested;

        StateChanged += MainWindow_StateChanged;
    }

    // ── Window chrome ────────────────────────────────────────────────────────

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _vm.Dispose();
        Close();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        // Update maximize icon: two overlapping squares when maximized
        if (MaxIcon is System.Windows.Shapes.Path icon)
        {
            icon.Data = System.Windows.Media.Geometry.Parse(
                WindowState == WindowState.Maximized
                    ? "M 0 2.5 L 7.5 2.5 L 7.5 10 L 0 10 Z M 2.5 0 L 10 0 L 10 7.5 L 2.5 7.5"
                    : "M 0 0 L 10 0 L 10 10 L 0 10 Z");
        }
    }

    // ── Barcode input ────────────────────────────────────────────────────────

    private void TxtBarcode_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _vm.ExecuteLookupWithValue(TxtBarcode.Text);
    }

    // ── DataGrid checkbox interactions ───────────────────────────────────────

    private void RowCheckBox_Click(object sender, RoutedEventArgs e)
    {
        // Prevent the DataGrid row selection from interfering
        e.Handled = true;
        _vm.RefreshCounts(); // Refresh MarkDefect CanExecute
    }

    private void HeaderCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            if (cb.IsChecked == true)
                _vm.SelectAllCommand.Execute(null);
            else
                _vm.DeselectAllCommand.Execute(null);
        }
    }

    private void LotDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RelayCommand.Invalidate();
    }

    // ── Dialog events ────────────────────────────────────────────────────────

    private void OnProductionLotInquiryRequested(object? sender, EventArgs e)
    {
        var dlg = new ProductionLotDialog(_vm.CurrentMaterial, _vm.LotEntries)
        {
            Owner = this
        };
        dlg.ShowDialog();
    }

    private void OnNotificationRequested(object? sender, string message)
    {
        MessageBox.Show(message, "안내", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
