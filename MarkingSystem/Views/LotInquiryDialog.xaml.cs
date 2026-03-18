using MarkingSystem.Models;
using MarkingSystem.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MarkingSystem.Views;

public partial class LotInquiryView : UserControl
{
    private readonly ObservableCollection<LotEntry> _results = [];
    private readonly LocalDatabase                  _db  = new();
    private readonly WizMesApiClient                _api = new(App.Settings.Api.BaseUrl, App.Auth);

    public LotInquiryView()
    {
        InitializeComponent();
        ResultDataGrid.ItemsSource = _results;
    }

    private async void TxtSearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await ExecuteSearch();
    }

    private async void SearchBtn_Click(object sender, RoutedEventArgs e)
        => await ExecuteSearch();

    private async Task ExecuteSearch()
    {
        var keyword = TxtSearchInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword)) return;

        // Lot 코드는 23자 이상 (품번 13 + ':' + Build Site 4 + 년도 2 + J-Date 3)
        // 물류 바코드는 14자 고정 — 길이로 파라미터 구분
        List<LotItem> items;
        if (keyword.Length >= 23)
            items = await _api.GetLotsAsync(null, keyword);
        else
            items = await _api.GetLotsAsync(keyword, null);

        _results.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            _results.Add(new LotEntry
            {
                Sequence        = i + 1,
                MaterialBarcode = items[i].MaterialBarcode,
                LotBarcode      = items[i].LotBarcode,
                Result          = ParseResult(items[i].InspectionResult),
            });
        }
    }

    private static InspectionResult ParseResult(string s) => s.ToUpperInvariant() switch
    {
        "OK" => InspectionResult.OK,
        "NG" => InspectionResult.NG,
        "AD" => InspectionResult.AD,
        _    => InspectionResult.Pending,
    };

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

    private void HeaderCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var cb = (CheckBox)sender;
        bool check = cb.IsChecked == true;
        foreach (var entry in _results)
            entry.IsSelected = check;
    }

    private void MarkDefectBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = _results.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0) return;

        if (selected.Any(r => r.Result == InspectionResult.OK))
        {
            DialogWindow.ShowError(Window.GetWindow(this),
                "양품으로 판정된 항목이 포함되어 있습니다.\n계속하면 해당 항목도 불량 처리됩니다.", "경고");
        }

        if (!DialogWindow.ShowConfirm(Window.GetWindow(this),
                "선택된 항목을 불량(NG) 처리하시겠습니까?", "불량 처리 확인")) return;

        foreach (var entry in selected)
        {
            entry.Result     = InspectionResult.NG;
            entry.IsSelected = false;
            _db.UpdateInspectionResult(entry.MaterialBarcode, entry.LotBarcode, InspectionResult.NG);
        }
    }
}
