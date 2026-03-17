using MarkingSystem.Models;
using MarkingSystem.Services;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace MarkingSystem.ViewModels;

public enum SystemState { Idle, Ready, Operating, ResultReady }

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private string        _materialBarcodeInput = string.Empty;
    private bool          _isOperating          = false;
    private SystemState   _state                = SystemState.Idle;
    private int           _selectedTab          = 0;
    private MaterialInfo? _currentMaterial;
    private string        _currentDateText      = string.Empty;
    private string        _statusMessage        = "Lot 바코드를 스캔하거나 입력하세요.";

    private readonly Timer           _clockTimer;
    private readonly LocalDatabase   _db;
    private readonly WizMesApiClient _api;
    private readonly IPlcClient      _plc;
    private readonly AuthService     _auth;
    private CancellationTokenSource? _markingCts;

    // ── 발행 세션 상태 ────────────────────────────────────────────────────────
    private int _issueStartSer; // 이번 발행 시작 직전 Ser (다음 발행은 +1부터)
    private int _issueCount;    // 이번 발행할 총 개수
    private int _issuedSoFar;   // 현재까지 PLC에 전송 요청한 개수

    public MainViewModel(AuthService auth, AppSettings settings)
    {
        _auth = auth;
        _db   = new LocalDatabase();
        _api  = new WizMesApiClient(settings.Api.BaseUrl, auth);
        _plc  = PlcClientFactory.Create(settings.Plc);
        LotEntries = [];

        StartPublishCommand = new RelayCommand(ExecuteStartPublish, () => State == SystemState.Ready);
        StopPublishCommand  = new RelayCommand(ExecuteStopPublish,  () => State == SystemState.Operating);
        MarkDefectCommand   = new RelayCommand(ExecuteMarkDefect,   () => State == SystemState.Operating && LotEntries.Any(e => e.IsSelected));
        SaveResultCommand   = new RelayCommand(ExecuteSaveResult,   () => (State == SystemState.Operating || State == SystemState.ResultReady) && LotEntries.Any(e => e.Result != InspectionResult.Pending));
        SelectAllCommand    = new RelayCommand(ExecuteSelectAll);
        DeselectAllCommand  = new RelayCommand(ExecuteDeselectAll);
        LogoutCommand       = new RelayCommand(ExecuteLogout);

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
        set
        {
            SetProperty(ref _state, value);
            OnPropertyChanged(nameof(IsTabSwitchable));
        }
    }

    public int SelectedTab
    {
        get => _selectedTab;
        set { SetProperty(ref _selectedTab, value); }
    }

    /// <summary>발행 중(Operating)이 아닐 때 탭 전환 허용</summary>
    public bool IsTabSwitchable => State != SystemState.Operating;

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

    public ICommand StartPublishCommand { get; }
    public ICommand StopPublishCommand  { get; }
    public ICommand MarkDefectCommand   { get; }
    public ICommand SaveResultCommand   { get; }
    public ICommand SelectAllCommand    { get; }
    public ICommand DeselectAllCommand  { get; }
    public ICommand LogoutCommand       { get; }

    // ── Events ───────────────────────────────────────────────────────────────

    public event EventHandler<string>?  ShowErrorRequested;
    public event EventHandler?          LogoutRequested;

    // ── Private Methods ──────────────────────────────────────────────────────

    private void UpdateDateTime()
    {
        Application.Current?.Dispatcher.Invoke(() =>
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
        // 로컬 DB와 API 최종발행 Ser 중 큰 값 사용
        var localSer = _db.GetLastIssuedSer(material.LotCode);
        var lastSer  = string.Compare(localSer, material.LastIssuedSer, StringComparison.Ordinal) >= 0
                       ? localSer : material.LastIssuedSer;

        material.MaterialBarcode = barcode;
        material.LastIssuedSer   = lastSer;
        material.WorkDateTime    = DateTime.Now;
        CurrentMaterial          = material;

        // 발행 세션 초기화 (LotEntry는 발행 시 동적으로 추가)
        _issueStartSer = int.Parse(lastSer);
        _issueCount    = int.Parse(material.ContainerQty);
        _issuedSoFar   = 0;
        LotEntries.Clear();

        RefreshCounts();
        State         = SystemState.Ready;
        StatusMessage = $"조회 완료: {barcode}  (발행 예정 {_issueCount}개)";
    }

    private async void ExecuteStartPublish()
    {
        SelectedTab   = 0;
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

        StatusMessage = $"발행 중...  (총 {_issueCount}개)";

        var buffer = new ConcurrentQueue<(LotEntry e1, LotEntry? e2)>();

        var t1 = BarcodeRequestLoopAsync(buffer, ct);
        var t2 = ScannerResultLoopAsync(buffer, ct);
        await Task.WhenAll(t1, t2);

        _plc.Disconnect();

        if (!ct.IsCancellationRequested)
        {
            IsOperating   = false;
            State         = SystemState.ResultReady;
            StatusMessage = $"발행 완료.  OK: {OkCount}, NG: {NgCount}";
        }
    }

    /// <summary>
    /// Loop 1: PLC 발행 요청 감지 → Lot 바코드 2개 전송 → 버퍼에 추가.
    /// 모든 발행 완료 또는 취소 시 종료.
    /// </summary>
    private async Task BarcodeRequestLoopAsync(
        ConcurrentQueue<(LotEntry, LotEntry?)> buffer, CancellationToken ct)
    {
        while (_issuedSoFar < _issueCount && !ct.IsCancellationRequested)
        {
            // PLC가 발행 요청 메모리를 1로 세팅할 때까지 대기
            while (!ct.IsCancellationRequested)
            {
                if (await _plc.ReadBarcodeRequestAsync()) break;
                try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }
            }
            if (ct.IsCancellationRequested) return;

            // 이번 배치: 1 또는 2개 (마지막 홀수 케이스 대비)
            int batchSize = Math.Min(2, _issueCount - _issuedSoFar);

            var bc1 = _currentMaterial!.LotCode + (_issueStartSer + _issuedSoFar + 1).ToString("D6");
            var bc2 = _currentMaterial.LotCode  + (_issueStartSer + _issuedSoFar + 2).ToString("D6");

            // PLC에 전송 (batchSize=1이어도 #2 슬롯에 bc2 전송 — PLC가 1개만 처리)
            if (!await _plc.WriteLotBarcodesAsync(bc1, bc2)) break;
            if (!await _plc.ClearBarcodeRequestAsync()) break;

            // LotEntry 생성 및 그리드에 추가 (UI 스레드)
            var e1 = new LotEntry
            {
                Sequence        = _issuedSoFar + 1,
                MaterialBarcode = _currentMaterial.MaterialBarcode,
                LotBarcode      = bc1,
                Result          = InspectionResult.Pending,
            };
            LotEntry? e2 = batchSize == 2 ? new LotEntry
            {
                Sequence        = _issuedSoFar + 2,
                MaterialBarcode = _currentMaterial.MaterialBarcode,
                LotBarcode      = bc2,
                Result          = InspectionResult.Pending,
            } : null;

            Application.Current.Dispatcher.Invoke(() =>
            {
                LotEntries.Add(e1);
                if (e2 != null) LotEntries.Add(e2);
            });

            // 로컬 DB에 Pending으로 저장
            _db.InsertIssueLog(e1.MaterialBarcode, e1.LotBarcode);
            if (e2 != null) _db.InsertIssueLog(e2.MaterialBarcode, e2.LotBarcode);

            _issuedSoFar += batchSize;
            if (CurrentMaterial != null)
                CurrentMaterial.LastIssuedSer = (e2 ?? e1).LotSer;

            RefreshCounts();
            StatusMessage = $"발행 중...  {_issuedSoFar}/{_issueCount}  ({bc1})";

            // 스캐너 검사 루프에 전달
            buffer.Enqueue((e1, e2));
        }
    }

    /// <summary>
    /// Loop 2: 스캐너 바코드 읽기 → 버퍼와 비교 → OK/AD 판정.
    /// 버퍼가 비어 있고 취소 요청 시 종료.
    /// </summary>
    private async Task ScannerResultLoopAsync(
        ConcurrentQueue<(LotEntry, LotEntry?)> buffer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested || !buffer.IsEmpty)
        {
            // 버퍼에 항목이 생길 때까지 대기
            if (!buffer.TryPeek(out _))
            {
                if (ct.IsCancellationRequested) return;
                try { await Task.Delay(50, ct); } catch (OperationCanceledException) { return; }
                continue;
            }

            // 스캐너 바코드가 PLC 메모리에 기록될 때까지 폴링
            string? sc1 = null, sc2 = null;
            while (!ct.IsCancellationRequested)
            {
                (sc1, sc2) = await _plc.ReadScannedBarcodesAsync();
                if (sc1 != null && sc2 != null) break;
                try { await Task.Delay(100, ct); } catch (OperationCanceledException) { break; }
            }
            if (sc1 == null) return;

            if (!buffer.TryDequeue(out var pair)) return;
            var (e1, e2) = pair;

            // 비교 및 결과 반영
            var result1 = sc1 == e1.LotBarcode ? InspectionResult.OK : InspectionResult.AD;
            e1.Result    = result1;
            e1.IsSelected = result1 != InspectionResult.OK;
            _db.UpdateInspectionResult(e1.LotBarcode, result1);

            if (e2 != null)
            {
                var result2 = sc2 == e2.LotBarcode ? InspectionResult.OK : InspectionResult.AD;
                e2.Result    = result2;
                e2.IsSelected = result2 != InspectionResult.OK;
                _db.UpdateInspectionResult(e2.LotBarcode, result2);
            }

            await _plc.ClearScannedBarcodesAsync();
            RefreshCounts();
        }
    }

    private void ExecuteStopPublish()
    {
        _markingCts?.Cancel();

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

    private void ExecuteSaveResult()
    {
        if (CurrentMaterial != null)
            _db.SetResultSaved(CurrentMaterial.MaterialBarcode);

        StatusMessage = $"결과 저장 완료  (OK: {OkCount}, NG: {NgCount})";
        State         = SystemState.Idle;
        IsOperating   = false;
    }

    private void ExecuteSelectAll()   { foreach (var e in LotEntries) e.IsSelected = true; }
    private void ExecuteDeselectAll() { foreach (var e in LotEntries) e.IsSelected = false; }

    private void ExecuteLogout()
    {
        _auth.Logout();
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

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
