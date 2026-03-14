using MarkingSystem.Models;
using System.Windows;

namespace MarkingSystem.Views;

public partial class ProductionLotDialog : Window
{
    public ProductionLotDialog(MaterialInfo? info, IEnumerable<LotEntry> entries)
    {
        InitializeComponent();

        if (info != null)
        {
            TxtMaterialBarcode.Text = info.MaterialBarcode;
            TxtItemCode.Text        = info.ItemCode;
            TxtProductName.Text     = info.ProductName;
            TxtWorkDate.Text        = info.WorkDateTime.ToString("yyyy-MM-dd  HH:mm");
        }

        var lotList = entries.ToList();
        LotGrid.ItemsSource = lotList;

        int ok    = lotList.Count(e => e.Result == InspectionResult.OK);
        int ng    = lotList.Count(e => e.Result == InspectionResult.NG);
        int ad    = lotList.Count(e => e.Result == InspectionResult.AD);
        int total = lotList.Count;

        TxtOkCount.Text    = $"양품 (OK)  {ok}";
        TxtNgCount.Text    = $"불량 (NG)  {ng}";
        TxtAdCount.Text    = $"대기 (AD)  {ad}";
        TxtTotalCount.Text = $"합계  {total}";

        InjGrid.ItemsSource = InjectionConditionFactory.CreateSampleData();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
