using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MarkingSystem.Models;

public enum InspectionResult
{
    Pending,
    OK,
    NG,
    AD  // Await Decision (대기결정)
}

public class LotEntry : INotifyPropertyChanged
{
    private bool _isSelected;
    private InspectionResult _result = InspectionResult.Pending;

    public int Sequence { get; set; }
    public string MaterialBarcode { get; set; } = string.Empty;
    public string LotBarcode { get; set; } = string.Empty;  // 29 chars

    public string LotCode => LotBarcode.Length >= 23 ? LotBarcode[..23] : LotBarcode;
    public string LotSer  => LotBarcode.Length == 29 ? LotBarcode[23..] : string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public InspectionResult Result
    {
        get => _result;
        set
        {
            _result = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ResultText));
        }
    }

    public string ResultText => Result switch
    {
        InspectionResult.OK => "OK",
        InspectionResult.NG => "NG",
        InspectionResult.AD => "AD",
        _ => "-"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
