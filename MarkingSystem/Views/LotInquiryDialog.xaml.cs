using MarkingSystem.Models;
using MarkingSystem.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
            MessageBox.Show("양품으로 판정된 항목이 포함되어 있습니다.\n계속하면 해당 항목도 불량 처리됩니다.",
                            "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var confirm = MessageBox.Show("선택된 항목을 불량(NG) 처리하시겠습니까?",
                                      "불량 처리 확인",
                                      MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        foreach (var entry in selected)
        {
            entry.Result     = InspectionResult.NG;
            entry.IsSelected = false;
            _db.UpdateInspectionResult(entry.LotBarcode, InspectionResult.NG);
        }
    }
}
