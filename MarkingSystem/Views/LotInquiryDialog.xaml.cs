using MarkingSystem.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace MarkingSystem.Views;

public partial class LotInquiryDialog : Window
{
    private readonly ObservableCollection<LotEntry> _results = [];

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

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
