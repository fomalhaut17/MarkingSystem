using MarkingSystem.Models;
using MarkingSystem.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MarkingSystem.Views;

public partial class LotInquiryDialog : Window
{
    private readonly ObservableCollection<LotEntry> _results = [];
    private readonly LocalDatabase _db = new();

    public LotInquiryDialog()
    {
        InitializeComponent();
        TxtDate.Text = DateTime.Now.ToString("yyyy년 M월 d일");
        ResultDataGrid.ItemsSource = _results;
    }

    private void TxtSearchInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ExecuteSearch();
    }

    private void SearchBtn_Click(object sender, RoutedEventArgs e)
        => ExecuteSearch();

    private void ExecuteSearch()
    {
        var keyword = TxtSearchInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword)) return;

        // TODO: wizMES 연동 후 실제 조회로 교체
        _results.Clear();
        for (int i = 1; i <= 5; i++)
        {
            _results.Add(new LotEntry
            {
                Sequence        = i,
                MaterialBarcode = keyword,
                LotBarcode      = "2026031401ABCDE23045678" + i.ToString("D6"),
                Result          = (InspectionResult)(i % 4),
            });
        }
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

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
