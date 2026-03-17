using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MarkingSystem.Models;

public enum InspectionResult
{
    NotIssued,  // 목록 생성 시 초기값. DB에 저장되지 않음. UI 표시: "-"
    Pending,    // PLC 전송 후 스캐너 결과 대기 중. DB에 저장됨. UI 표시: "대기"
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
        InspectionResult.Pending => "대기",
        InspectionResult.OK      => "OK",
        InspectionResult.NG      => "NG",
        InspectionResult.AD      => "AD",
        _                        => "-"   // NotIssued
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
