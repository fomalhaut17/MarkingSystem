using MarkingSystem.Models;
using MarkingSystem.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MarkingSystem.Views;

public partial class ProductionLotView : UserControl
{
    private readonly WizMesApiClient _api = new(App.Settings.Api.BaseUrl, App.Auth);

    // (groupName, rowName) → 사출 조건 표 grid row 번호
    private static readonly Dictionary<(string, string), int> GridRowMap = new()
    {
        { ("성형조건1차", "온도(°C)"),       1 },
        { ("성형조건1차", "압력(Kg/cm2)"),   3 },
        { ("성형조건1차", "속도(%)"),        4 },
        { ("계량조건",    "압력(Kg/cm2)"),   6 },
        { ("계량조건",    "속도(%)"),        7 },
        { ("계량조건",    "위치(mm)"),       8 },
        { ("보압",        "압력(Kg/cm2)"),   9 },
        { ("보압",        "속도(%)"),        10 },
        { ("보압",        "시간(초)"),       11 },
        { ("HOT RUNNER",  "1-4"),           12 },
        { ("HOT RUNNER",  "5-8"),           13 },
        { ("냉각시간",    ""),              14 },
        { ("금형보호",    ""),              15 },
        { ("가스위치",    ""),              16 },
    };

    public ProductionLotView()
    {
        InitializeComponent();
    }

    private async void LotSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        var lotCode = TxtSearchBarcode.Text.Trim();
        if (string.IsNullOrWhiteSpace(lotCode)) return;

        var info = await _api.GetProductionLotAsync(lotCode);
        if (info == null)
        {
            DialogWindow.ShowError(Window.GetWindow(this), "해당 Lot 코드를 찾을 수 없습니다.", "조회 결과 없음");
            return;
        }

        TxtLotBarcode.Text          = info.LotCode;
        TxtProductName.Text         = info.ProductName;
        TxtManufactureDate.Text     = info.ManufactureDate;
        TxtProductionEquipment.Text = info.ProductionEquipment;
        TxtProductionMold.Text      = info.ProductionMold;
        TxtLotProductionQty.Text    = info.LotProductionQty;
        TxtOkCount.Text             = info.OkCount.ToString();
        TxtNgCount.Text             = info.NgCount.ToString();

        PopulateInjGrid(info.InjectionConditions);
    }

    private void PopulateInjGrid(List<InjectionRow> rows)
    {
        // (row, col) → Border 맵 구성
        var cellMap = new Dictionary<(int, int), Border>();
        foreach (UIElement child in InjGrid.Children)
        {
            if (child is Border b)
                cellMap[(Grid.GetRow(b), Grid.GetColumn(b))] = b;
        }

        // 기존 데이터 셀 초기화 (col 2~11)
        foreach (var kv in cellMap)
        {
            if (kv.Key.Item2 >= 2 && kv.Value.Child is TextBlock tb)
                tb.Text = string.Empty;
        }

        // H 인덱스(1~5) → Set/Act 컬럼 번호
        static int SetCol(int h) => 2 + (h - 1) * 2;
        static int ActCol(int h) => 3 + (h - 1) * 2;

        void SetText(int gridRow, int col, string text, bool isSet)
        {
            if (!cellMap.TryGetValue((gridRow, col), out var border)) return;
            if (border.Child is TextBlock existing)
            {
                existing.Text = text;
            }
            else
            {
                border.Child = new TextBlock
                {
                    Text                = text,
                    FontSize            = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                    TextAlignment       = TextAlignment.Center,
                    Foreground          = isSet ? Brushes.White : Brushes.Black,
                };
            }
        }

        foreach (var ir in rows)
        {
            if (!GridRowMap.TryGetValue((ir.GroupName, ir.RowName), out int gridRow)) continue;
            var hs = new[] { ir.H1, ir.H2, ir.H3, ir.H4, ir.H5 };
            for (int h = 1; h <= 5; h++)
            {
                SetText(gridRow, SetCol(h), hs[h - 1].Set, isSet: true);
                SetText(gridRow, ActCol(h), hs[h - 1].Act, isSet: false);
            }
        }
    }
}
