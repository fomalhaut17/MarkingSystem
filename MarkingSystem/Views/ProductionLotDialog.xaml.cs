using MarkingSystem.Models;
using System.Windows;

namespace MarkingSystem.Views;

public partial class ProductionLotDialog : Window
{
    public ProductionLotDialog(MaterialInfo? info = null)
    {
        InitializeComponent();

        TxtDate.Text = DateTime.Now.ToString("yyyy년 M월 d일");

        if (info != null)
        {
            TxtLotBarcode.Text          = info.LotCode;
            TxtProductName.Text         = info.ProductName;
            TxtManufactureDate.Text     = info.ManufactureDate;
            TxtProductionEquipment.Text = info.ProductionEquipment;
            TxtProductionMold.Text      = info.ProductionMold;
            TxtLotProductionQty.Text    = info.LotProductionQty;
        }
    }

    private void LotSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        // TODO: wizMES 연동 후 구현
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
}
