using MarkingSystem.Models;
using MarkingSystem.Services;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;

namespace MarkingSystem.ViewModels;

public enum SystemState { Idle, Ready, Operating, ResultReady }

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private string        _materialBarcodeInput  = string.Empty;
    private bool          _isOperating      = false;
    private SystemState   _state            = SystemState.Idle;
    private MaterialInfo? _currentMaterial;
    private string        _currentDateText  = string.Empty;
    private string        _statusMessage    = "Lot 바코드를 스캔하거나 입력하세요.";

    private readonly Timer           _clockTimer;
    private readonly LocalDatabase   _db;
    private readonly WizMesApiClient _api;
    private readonly IPlcClient      _plc;
    private CancellationTokenSource? _markingCts;

    // TODO: 실서버 전환 시 URL/IP를 설정 파일로 이동
    private const string ApiBaseUrl = "http://localhost:3000/api/marking";
    private const string PlcHost    = XgtRawPlcClient.DefaultHost;
    private const int    PlcPort    = XgtRawPlcClient.DefaultPort;

    /// <summary>
    /// PLC 구현체 선택.
    /// 개발: XgtRawPlcClient (mock-plc/server.js 대상)
    /// 운영: HslPlcClient    (실 LS XGT PLC 대상, PlcHost를 실 IP로 변경)
    /// </summary>
    private static IPlcClient CreatePlcClient() =>
        new XgtRawPlcClient(PlcHost, PlcPort);
        // new HslPlcClient(PlcHost);  ← 실 PLC 전환 시 이 줄로 교체

    public MainViewModel()
    {
        _db  = new LocalDatabase();
        _api = new WizMesApiClient(ApiBaseUrl);
        _plc = CreatePlcClient();
        LotEntries = [];

        StartPublishCommand         = new RelayCommand(ExecuteStartPublish,         () => State == SystemState.Ready);
        StopPublishCommand          = new RelayCommand(ExecuteStopPublish,          () => State == SystemState.Operating);
        MarkDefectCommand           = new RelayCommand(ExecuteMarkDefect,           () => State == SystemState.Operating && LotEntries.Any(e => e.IsSelected));
        ProductionLotInquiryCommand = new RelayCommand(ExecuteProductionLotInquiry, () => State != SystemState.Idle);
        SaveResultCommand           = new RelayCommand(ExecuteSaveResult,           () => (State == SystemState.Operating || State == SystemState.ResultReady) && LotEntries.Any(e => e.Result != InspectionResult.Pending));
        LotInquiryCommand           = new RelayCommand(ExecuteLotInquiry,           () => State != SystemState.Idle);
        SelectAllCommand            = new RelayCommand(ExecuteSelectAll);
        DeselectAllCommand          = new RelayCommand(ExecuteDeselectAll);

        UpdateDateTime();
        _clockTimer = new Timer(_ => UpdateDateTime(), null, 1000, 1000);
    }

    // ── Properties ──────────────────────────────────────────────────────────

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

    public ICommand StartPublishCommand         { get; }
    public ICommand StopPublishCommand          { get; }
    public ICommand MarkDefectCommand           { get; }
    public ICommand ProductionLotInquiryCommand { get; }
    public ICommand SaveResultCommand           { get; }
    public ICommand LotInquiryCommand           { get; }
    public ICommand SelectAllCommand            { get; }
    public ICommand DeselectAllCommand          { get; }

    // ── Events ───────────────────────────────────────────────────────────────

    public event EventHandler? ProductionLotInquiryRequested;
    public event EventHandler? LotInquiryRequested;
    public event EventHandler<string>? ShowErrorRequested;

    // ── Private Methods ──────────────────────────────────────────────────────

    private void UpdateDateTime()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            CurrentDateText = DateTime.Now.ToString("yyyy년 M월 d일"));
    }

    public async void ExecuteLookupByMaterial()
    {
        if (string.IsNullOrWhiteSpace(MaterialBarcodeInput)) return;

        var barcode = MaterialBarcodeInput.Trim();

        if (CurrentMaterial != null && _db.HasUnsavedResults())
        {
            ShowErrorRequested?.Invoke(this,
                "미저장 발행 결과가 있습니다.\n결과를 저장한 후 다음 물류 바코드를 조회하세요.");
            return;
        }

        MaterialBarcodeInput = string.Empty;
        StatusMessage = $"조회 중...  ({barcode})";

        try
        {
            var material = await _api.GetMaterialAsync(barcode);
            if (material == null)
            {
                StatusMessage = $"조회 실패: 물류 바코드를 찾을 수 없습니다.  ({barcode})";
                return;
            }

            ApplyMaterial(barcode, material);
        }
        catch (Exception ex)
        {
            StatusMessage = $"API 오류: {ex.Message}";
        }
    }

    private void ApplyMaterial(string barcode, MaterialInfo material)
    {
        // 로컬 DB 조회 후 큰 값 사용 (§4.3.3, §5.3)
        var localSer = _db.GetLastIssuedSer(material.LotCode);
        var lastSer  = string.Compare(localSer, material.LastIssuedSer, StringComparison.Ordinal) >= 0
                       ? localSer : material.LastIssuedSer;

        material.MaterialBarcode = barcode;
        material.LastIssuedSer   = lastSer;
        material.WorkDateTime    = DateTime.Now;
        CurrentMaterial          = material;

        LotEntries.Clear();
        for (int i = 1; i <= int.Parse(material.ContainerQty); i++)
        {
            LotEntries.Add(new LotEntry
            {
                Sequence        = i,
                MaterialBarcode = barcode,
                LotBarcode      = material.LotCode + i.ToString("D6"),
                Result          = InspectionResult.Pending,
            });
        }

        RefreshCounts();
        State         = SystemState.Ready;
        StatusMessage = $"조회 완료: {barcode}  (총 {LotEntries.Count}개)";
    }

    private async void ExecuteStartPublish()
    {
        IsOperating   = true;
        State         = SystemState.Operating;
        StatusMessage = "PLC 연결 중...";

        _markingCts?.Dispose();
        _markingCts = new CancellationTokenSource();

        await RunMarkingSessionAsync(_markingCts.Token);
    }

    private async Task RunMarkingSessionAsync(CancellationToken ct)
    {
        if (!await _plc.ConnectAsync(ct))
        {
            StatusMessage = "PLC 연결 실패.  Mock PLC 서버가 실행 중인지 확인하세요.";
            State         = SystemState.Ready;
            IsOperating   = false;
            return;
        }

        StatusMessage = $"발행 중...  (총 {LotEntries.Count}개)";

        foreach (var entry in LotEntries)
        {
            if (ct.IsCancellationRequested) break;

            // 1. Lot 바코드 → PLC 전송
            if (!await _plc.WriteLotBarcodeAsync(entry.LotBarcode)) break;

            // 2. 발행 시작 명령
            if (!await _plc.WriteStartCommandAsync()) break;

            // 3. 완료 대기 (최대 10초, 100ms 간격 폴링)
            var status = PlcStatus.Idle;
            for (int i = 0; i < 100; i++)
            {
                if (ct.IsCancellationRequested) break;
                await Task.Delay(100, ct).ConfigureAwait(true);
                status = await _plc.ReadStatusAsync();
                if (status is PlcStatus.DoneOk or PlcStatus.DoneNg or PlcStatus.Error) break;
            }

            if (ct.IsCancellationRequested) break;

            // 4. 결과 반영
            var result = status switch
            {
                PlcStatus.DoneOk => InspectionResult.OK,
                PlcStatus.DoneNg => InspectionResult.NG,
                _                => InspectionResult.AD,
            };

            entry.Result = result;
            _db.InsertIssueLog(entry.MaterialBarcode, entry.LotBarcode);
            _db.UpdateInspectionResult(entry.LotBarcode, result);

            if (CurrentMaterial != null)
                CurrentMaterial.LastIssuedSer = entry.LotSer;

            RefreshCounts();
            StatusMessage = $"발행 중...  {entry.Sequence}/{LotEntries.Count}  ({entry.LotBarcode})";

            // 5. 명령 레지스터 클리어
            await _plc.ClearCommandAsync();
        }

        _plc.Disconnect();

        if (!ct.IsCancellationRequested)
        {
            IsOperating   = false;
            State         = SystemState.ResultReady;
            StatusMessage = $"발행 완료.  OK: {OkCount}, NG: {NgCount}";
        }
    }

    private void ExecuteStopPublish()
    {
        _markingCts?.Cancel();
        _plc.WriteStopCommandAsync();   // fire-and-forget (best effort)
        _plc.Disconnect();

        IsOperating   = false;
        State         = SystemState.ResultReady;
        StatusMessage = "발행 정지.  결과를 저장하거나 검토하세요.";
    }

    private void ExecuteMarkDefect()
    {
        foreach (var entry in LotEntries.Where(e => e.IsSelected).ToList())
        {
            entry.Result     = InspectionResult.NG;
            entry.IsSelected = false;
            _db.UpdateInspectionResult(entry.LotBarcode, InspectionResult.NG);
        }
        RefreshCounts();
        StatusMessage = "선택된 항목을 불량(NG) 처리했습니다.";
    }

    private void ExecuteProductionLotInquiry()
        => ProductionLotInquiryRequested?.Invoke(this, EventArgs.Empty);

    private void ExecuteSaveResult()
    {
        if (CurrentMaterial != null)
            _db.SetResultSaved(CurrentMaterial.MaterialBarcode);

        StatusMessage = $"결과 저장 완료  (OK: {OkCount}, NG: {NgCount})";
        State         = SystemState.Idle;
        IsOperating   = false;
    }

    private void ExecuteLotInquiry()
        => LotInquiryRequested?.Invoke(this, EventArgs.Empty);

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

    public void Dispose()
    {
        _markingCts?.Dispose();
        _clockTimer.Dispose();
        _plc.Dispose();
    }
}
