using MarkingSystem.ViewModels;
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

        _vm.ShowErrorRequested += OnShowErrorRequested;
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

    // ── Error dialog ─────────────────────────────────────────────────────────

    private void OnShowErrorRequested(object? sender, string message)
        => MessageBox.Show(message, "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
}
