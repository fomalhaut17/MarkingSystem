using MarkingSystem.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MarkingSystem.ViewModels;

public enum SystemState { Idle, Ready, Operating, ResultReady }

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private string        _barcodeInput          = string.Empty;
    private string        _materialBarcodeInput  = string.Empty;
    private bool          _isOperating      = false;
    private bool          _isLotPublishMode = true;
    private SystemState   _state            = SystemState.Idle;
    private MaterialInfo? _currentMaterial;
    private string        _currentDateText  = string.Empty;
    private string        _statusMessage    = "Lot 바코드를 스캔하거나 입력하세요.";

    private readonly Timer _clockTimer;

    public MainViewModel()
    {
        LotEntries = [];

        LookupCommand               = new RelayCommand(ExecuteLookup,               () => !string.IsNullOrWhiteSpace(BarcodeInput));
        StartPublishCommand         = new RelayCommand(ExecuteStartPublish,         () => State == SystemState.Ready && IsLotPublishMode);
        StopPublishCommand          = new RelayCommand(ExecuteStopPublish,          () => State == SystemState.Operating);
        MarkDefectCommand           = new RelayCommand(ExecuteMarkDefect,           () => State == SystemState.Operating && LotEntries.Any(e => e.IsSelected));
        ProductionLotInquiryCommand = new RelayCommand(ExecuteProductionLotInquiry, () => State != SystemState.Idle);
        SaveResultCommand           = new RelayCommand(ExecuteSaveResult,           () => (State == SystemState.Operating || State == SystemState.ResultReady) && LotEntries.Any(e => e.Result != InspectionResult.Pending));
        LotInquiryCommand           = new RelayCommand(ExecuteLotInquiry,           () => State != SystemState.Idle);
        ToggleModeCommand           = new RelayCommand(ExecuteToggleMode,           () => State != SystemState.Operating);
        SelectAllCommand            = new RelayCommand(ExecuteSelectAll);
        DeselectAllCommand          = new RelayCommand(ExecuteDeselectAll);

        UpdateDateTime();
        _clockTimer = new Timer(_ => UpdateDateTime(), null, 1000, 1000);
    }

    // ── Properties ──────────────────────────────────────────────────────────

    public string BarcodeInput
    {
        get => _barcodeInput;
        set { SetProperty(ref _barcodeInput, value); }
    }

    public string MaterialBarcodeInput
    {
        get => _materialBarcodeInput;
        set { SetProperty(ref _materialBarcodeInput, value); }
    }

    public bool IsOperating
    {
        get => _isOperating;
        set { SetProperty(ref _isOperating, value); }
    }

    public bool IsLotPublishMode
    {
        get => _isLotPublishMode;
        set { SetProperty(ref _isLotPublishMode, value); OnPropertyChanged(nameof(ModeButtonText)); }
    }

    public string ModeButtonText => IsLotPublishMode ? "Lot 발행모드" : "Lot 조회모드";

    public SystemState State
    {
        get => _state;
        set { SetProperty(ref _state, value); }
    }

    public MaterialInfo? CurrentMaterial
    {
        get => _currentMaterial;
        set { SetProperty(ref _currentMaterial, value); }
    }

    public string CurrentDateText
    {
        get => _currentDateText;
        set { SetProperty(ref _currentDateText, value); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { SetProperty(ref _statusMessage, value); }
    }

    public ObservableCollection<LotEntry> LotEntries { get; }

    public int OkCount    => LotEntries.Count(e => e.Result == InspectionResult.OK);
    public int NgCount    => LotEntries.Count(e => e.Result == InspectionResult.NG);
    public int AdCount    => LotEntries.Count(e => e.Result == InspectionResult.AD);
    public int TotalCount => LotEntries.Count;

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand LookupCommand               { get; }
    public ICommand StartPublishCommand         { get; }
    public ICommand StopPublishCommand          { get; }
    public ICommand MarkDefectCommand           { get; }
    public ICommand ProductionLotInquiryCommand { get; }
    public ICommand SaveResultCommand           { get; }
    public ICommand LotInquiryCommand           { get; }
    public ICommand ToggleModeCommand           { get; }
    public ICommand SelectAllCommand            { get; }
    public ICommand DeselectAllCommand          { get; }

    // ── Events ───────────────────────────────────────────────────────────────

    public event EventHandler? ProductionLotInquiryRequested;
    public event EventHandler<string>? NotificationRequested;

    // ── Private Methods ──────────────────────────────────────────────────────

    private void UpdateDateTime()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            CurrentDateText = DateTime.Now.ToString("yyyy년 M월 d일"));
    }

    public void ExecuteLookupWithValue(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;
        BarcodeInput = barcode;
        ExecuteLookup();
    }

    public void ExecuteLookupByMaterial()
    {
        if (string.IsNullOrWhiteSpace(MaterialBarcodeInput)) return;
        LoadMockData(MaterialBarcodeInput.Trim());
        MaterialBarcodeInput = string.Empty;
    }

    private void ExecuteLookup()
    {
        if (string.IsNullOrWhiteSpace(BarcodeInput)) return;
        LoadMockData(BarcodeInput.Trim());
        BarcodeInput = string.Empty;
    }

    private void LoadMockData(string barcode)
    {
        CurrentMaterial = new MaterialInfo
        {
            MaterialBarcode     = barcode,
            ProductName         = "플라스틱 커버 어셈블리",
            ManufactureDate     = DateTime.Now.ToString("yyyy-MM-dd"),
            ContainerQty        = "100",
            LotCode             = "2026031401ABCDE23045678",
            LastIssuedSer       = "000000",
            ProductionEquipment = "사출기-01",
            ProductionMold      = "금형-A",
            LotProductionQty    = "500",
            WorkDateTime        = DateTime.Now,
        };

        LotEntries.Clear();
        for (int i = 1; i <= 13; i++)
        {
            LotEntries.Add(new LotEntry
            {
                Sequence        = i,
                MaterialBarcode = barcode,
                LotBarcode      = CurrentMaterial.LotCode + i.ToString("D6"),
                Result          = InspectionResult.Pending,
            });
        }

        RefreshCounts();
        State         = SystemState.Ready;
        StatusMessage = $"조회 완료: {barcode}  (총 {LotEntries.Count}개)";
    }

    private void ExecuteStartPublish()
    {
        IsOperating   = true;
        State         = SystemState.Operating;
        StatusMessage = "발행 중...  Lot 바코드를 스캔하세요.";
        SimulateSomeResults();
    }

    private void SimulateSomeResults()
    {
        var entries = LotEntries.Take(4).ToList();
        for (int i = 0; i < entries.Count; i++)
            entries[i].Result = (i == 2) ? InspectionResult.NG : InspectionResult.OK;

        if (CurrentMaterial != null)
            CurrentMaterial.LastIssuedSer = LotEntries.Take(4).Last().LotSer;

        RefreshCounts();
    }

    private void ExecuteStopPublish()
    {
        IsOperating   = false;
        State         = SystemState.ResultReady;
        StatusMessage = "발행 종료.  결과를 저장하거나 검토하세요.";
    }

    private void ExecuteMarkDefect()
    {
        foreach (var entry in LotEntries.Where(e => e.IsSelected).ToList())
        {
            entry.Result     = InspectionResult.NG;
            entry.IsSelected = false;
        }
        RefreshCounts();
        StatusMessage = "선택된 항목을 불량(NG) 처리했습니다.";
    }

    private void ExecuteProductionLotInquiry()
        => ProductionLotInquiryRequested?.Invoke(this, EventArgs.Empty);

    private void ExecuteSaveResult()
    {
        StatusMessage = $"결과 저장 완료  (OK: {OkCount}, NG: {NgCount})";
        State         = SystemState.Idle;
        IsOperating   = false;
    }

    private void ExecuteLotInquiry()
        => NotificationRequested?.Invoke(this, "Lot 조회 기능은 wizMES 연동 후 사용 가능합니다.");

    private void ExecuteToggleMode()
    {
        IsLotPublishMode = !IsLotPublishMode;
        StatusMessage    = IsLotPublishMode ? "Lot 발행 모드로 전환되었습니다." : "Lot 조회 모드로 전환되었습니다.";
    }

    private void ExecuteSelectAll()   { foreach (var e in LotEntries) e.IsSelected = true; }
    private void ExecuteDeselectAll() { foreach (var e in LotEntries) e.IsSelected = false; }

    public void RefreshCounts()
    {
        if (CurrentMaterial != null)
        {
            CurrentMaterial.OkCount = OkCount;
            CurrentMaterial.NgCount = NgCount;
            CurrentMaterial.AdCount = AdCount;
        }
        OnPropertyChanged(nameof(OkCount));
        OnPropertyChanged(nameof(NgCount));
        OnPropertyChanged(nameof(AdCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(CurrentMaterial));
    }

    public void Dispose() => _clockTimer.Dispose();
}
