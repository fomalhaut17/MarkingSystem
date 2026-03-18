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
    private readonly MarkingSettings _markingSettings;
    private CancellationTokenSource? _markingCts;

    private int  _inFlightCount        = 0;     // 전송 후 스캐너 확인 대기 중인 배치 수
    private bool _isStoppingInProgress = false; // 발행 종료 대기 중 (중복 클릭 방지)

    // ── 발행 세션 상태 ────────────────────────────────────────────────────────
    // LotEntries는 ApplyMaterial 시 전체 목록(NotIssued)으로 미리 구성됨
    // 발행 루프는 NotIssued 항목을 순서대로 PLC에 전송함

    public MainViewModel(AuthService auth, AppSettings settings)
    {
        _auth            = auth;
        _db              = new LocalDatabase();
        _api             = new WizMesApiClient(settings.Api.BaseUrl, auth);
        _plc             = PlcClientFactory.Create(settings.Plc);
        _markingSettings = settings.Marking;
        IsTestMode       = settings.AppMode is "local" or "dev";
        LotEntries = [];

        StartPublishCommand    = new RelayCommand(ExecuteStartPublish, () => State == SystemState.Ready || State == SystemState.ResultReady);
        StopPublishCommand     = new RelayCommand(ExecuteStopPublish,  () => State == SystemState.Operating && !_isStoppingInProgress);
        MarkDefectCommand      = new RelayCommand(ExecuteMarkDefect,   () => State == SystemState.ResultReady);
        SaveResultCommand      = new RelayCommand(ExecuteSaveResult,   () => State == SystemState.ResultReady);
        SelectAllCommand       = new RelayCommand(ExecuteSelectAll);
        DeselectAllCommand     = new RelayCommand(ExecuteDeselectAll);
        LogoutCommand          = new RelayCommand(ExecuteLogout);
        ResetTestDataCommand   = new RelayCommand(ExecuteResetTestData, () => IsTestMode && CurrentMaterial != null && State != SystemState.Operating);

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
            OnPropertyChanged(nameof(IsStartOperating));
            OnPropertyChanged(nameof(IsStopOperating));
            RelayCommand.Invalidate();
        }
    }

    public int SelectedTab
    {
        get => _selectedTab;
        set { SetProperty(ref _selectedTab, value); }
    }

    /// <summary>발행 중(Operating)이 아닐 때 탭 전환 허용</summary>
    public bool IsTabSwitchable => State != SystemState.Operating;

    /// <summary>발행 시작 버튼 동작 상태 (파랑): 발행 진행 중</summary>
    public bool IsStartOperating => State == SystemState.Operating;

    /// <summary>발행 종료 버튼 동작 상태 (파랑): 발행 종료 후 결과 대기 중</summary>
    public bool IsStopOperating => State == SystemState.ResultReady;

    public MaterialInfo? CurrentMaterial
    {
        get => _currentMaterial;
        set { SetProperty(ref _currentMaterial, value); RelayCommand.Invalidate(); }
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

    public bool     IsTestMode           { get; }
    public ICommand StartPublishCommand  { get; }
    public ICommand StopPublishCommand   { get; }
    public ICommand MarkDefectCommand    { get; }
    public ICommand SaveResultCommand    { get; }
    public ICommand SelectAllCommand     { get; }
    public ICommand DeselectAllCommand   { get; }
    public ICommand LogoutCommand        { get; }
    public ICommand ResetTestDataCommand { get; }

    // ── Events ───────────────────────────────────────────────────────────────

    public event EventHandler<string>?  ShowErrorRequested;
    public event EventHandler?          LogoutRequested;
    /// <summary>확인창 요청. message, title → true=Yes.</summary>
    public Func<string, string, bool>?  ConfirmRequested { get; set; }

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

        if (CurrentMaterial != null && _db.HasUnsavedResults(CurrentMaterial.MaterialBarcode))
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
        material.MaterialBarcode = barcode;
        material.WorkDateTime    = DateTime.Now;

        // 발행 이력이 있으면 기존 항목 복원 (저장 여부 무관, 중복 발행 방지)
        var existing = _db.GetLotEntriesByMaterial(barcode);
        if (existing.Count > 0)
        {
            LotEntries.Clear();
            int seq = 1;
            foreach (var kv in existing)
            {
                LotEntries.Add(new LotEntry
                {
                    Sequence        = seq++,
                    MaterialBarcode = barcode,
                    LotBarcode      = kv.Key,
                    Result          = kv.Value,
                });
            }
            material.LastIssuedSer = LotEntries[^1].LotSer;
            CurrentMaterial        = material;
            RefreshCounts();

            bool hasUnsaved = _db.HasUnsavedResults(barcode);
            // 미저장 항목이 있으면 결과 저장 가능 상태, 전부 저장됐으면 재발행 불가
            State         = hasUnsaved ? SystemState.ResultReady : SystemState.Idle;
            StatusMessage = hasUnsaved
                ? $"발행 이력 복원: {barcode}  ({existing.Count}개, 저장 필요)"
                : $"이미 처리가 완료된 물류 바코드입니다.  ({barcode})";
            return;
        }

        // 로컬 DB와 API 최종발행 Ser 중 큰 값 사용
        var localSer = _db.GetLastIssuedSer(material.LotCode);
        var lastSer  = string.Compare(localSer, material.LastIssuedSer, StringComparison.Ordinal) >= 0
                       ? localSer : material.LastIssuedSer;

        material.LastIssuedSer = lastSer;
        CurrentMaterial        = material;

        // 전체 목록 구성: lastSer+1 ~ lastSer+containerQty (NotIssued)
        int totalCount = int.Parse(material.ContainerQty);
        int lastSerInt = int.Parse(lastSer);
        LotEntries.Clear();
        for (int i = 1; i <= totalCount; i++)
        {
            LotEntries.Add(new LotEntry
            {
                Sequence        = i,
                MaterialBarcode = barcode,
                LotBarcode      = material.LotCode + (lastSerInt + i).ToString("D6"),
                Result          = InspectionResult.NotIssued,
            });
        }

        RefreshCounts();
        State         = SystemState.Ready;
        StatusMessage = $"조회 완료: {barcode}  (발행 예정 {totalCount}개)";
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

        StatusMessage = $"발행 중...  (총 {LotEntries.Count}개)";

        var buffer = new ConcurrentQueue<(LotEntry e1, LotEntry? e2)>();
        using var loopDoneCts = new CancellationTokenSource();

        var t1 = BarcodeRequestLoopAsync(buffer, loopDoneCts, ct);
        var t2 = ScannerResultLoopAsync(buffer, loopDoneCts.Token, ct);
        try
        {
            await Task.WhenAll(t1, t2);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowErrorRequested?.Invoke(this, $"발행 중 오류가 발생했습니다.\n{ex.Message}");
        }

        _plc.Disconnect();

        if (!ct.IsCancellationRequested)
        {
            IsOperating   = false;
            State         = SystemState.ResultReady;
            StatusMessage = $"발행 완료.  OK: {OkCount}, NG: {NgCount}";
        }
    }

    /// <summary>
    /// Loop 1: PLC 발행 요청 감지 → NotIssued 항목 2개 찾아 Pending으로 전환 → 버퍼에 추가.
    /// NotIssued 항목이 없거나 취소 시 종료.
    /// </summary>
    private async Task BarcodeRequestLoopAsync(
        ConcurrentQueue<(LotEntry, LotEntry?)> buffer,
        CancellationTokenSource loopDoneCts,
        CancellationToken ct)
    {
        try
        {
        while (!ct.IsCancellationRequested)
        {
            // 다음에 발행할 NotIssued 항목 탐색 (UI 스레드에서 접근)
            LotEntry? e1 = null, e2 = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var pending = LotEntries.Where(e => e.Result == InspectionResult.NotIssued).Take(2).ToList();
                if (pending.Count >= 1) e1 = pending[0];
                if (pending.Count >= 2) e2 = pending[1];
            });

            if (e1 == null) break; // 모든 항목 발행 완료

            // PLC가 발행 요청 메모리를 1로 세팅할 때까지 대기
            while (!ct.IsCancellationRequested)
            {
                if (await _plc.ReadBarcodeRequestAsync()) break;
                try { await Task.Delay(100, ct); } catch (OperationCanceledException) { return; }
            }
            if (ct.IsCancellationRequested) return;

            // PLC에 전송 (e2=null이어도 bc2 슬롯 전송 — PLC가 1개만 처리)
            Interlocked.Increment(ref _inFlightCount);
            if (!await _plc.WriteLotBarcodesAsync(e1.LotBarcode, e2?.LotBarcode ?? e1.LotBarcode))
            {
                Interlocked.Decrement(ref _inFlightCount);
                break;
            }
            if (!await _plc.ClearBarcodeRequestAsync())
            {
                Interlocked.Decrement(ref _inFlightCount);
                break;
            }

            // NotIssued → Pending 전환 및 DB 삽입
            Application.Current.Dispatcher.Invoke(() =>
            {
                e1!.Result = InspectionResult.Pending;
                if (e2 != null) e2.Result = InspectionResult.Pending;
            });

            _db.InsertIssueLog(e1.MaterialBarcode, e1.LotBarcode);
            if (e2 != null) _db.InsertIssueLog(e2.MaterialBarcode, e2.LotBarcode);

            if (CurrentMaterial != null)
                CurrentMaterial.LastIssuedSer = (e2 ?? e1).LotSer;

            var issuedCount = LotEntries.Count(e => e.Result != InspectionResult.NotIssued);
            RefreshCounts();
            StatusMessage = $"발행 중...  {issuedCount}/{LotEntries.Count}  ({e1.LotBarcode})";

            // 스캐너 검사 루프에 전달
            buffer.Enqueue((e1, e2));
        }
        }
        finally
        {
            loopDoneCts.Cancel(); // ScannerResultLoop에게 더 이상 항목이 없음을 알림
        }
    }

    /// <summary>
    /// Loop 2: 스캐너 바코드 읽기 → 버퍼와 비교 → OK/AD 판정.
    /// 버퍼가 비어 있고 취소 요청 시 종료.
    /// </summary>
    private async Task ScannerResultLoopAsync(
        ConcurrentQueue<(LotEntry, LotEntry?)> buffer,
        CancellationToken loopDone,   // BarcodeRequestLoop 종료 시 시그널
        CancellationToken ct)
    {
        while (!loopDone.IsCancellationRequested || !buffer.IsEmpty)
        {
            // 버퍼에 항목이 생길 때까지 대기
            if (!buffer.TryPeek(out _))
            {
                if (loopDone.IsCancellationRequested) return; // 더 이상 항목이 오지 않음
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
            _db.UpdateInspectionResult(e1.MaterialBarcode, e1.LotBarcode, result1);

            if (e2 != null)
            {
                var result2 = sc2 == e2.LotBarcode ? InspectionResult.OK : InspectionResult.AD;
                e2.Result    = result2;
                e2.IsSelected = result2 != InspectionResult.OK;
                _db.UpdateInspectionResult(e2.MaterialBarcode, e2.LotBarcode, result2);
            }

            await _plc.ClearScannedBarcodesAsync();
            Interlocked.Decrement(ref _inFlightCount);
            RefreshCounts();
        }
    }

    private async void ExecuteStopPublish()
    {
        _isStoppingInProgress = true;
        RelayCommand.Invalidate();

        if (_inFlightCount > 0)
        {
            StatusMessage = "전송 완료 대기 중...";
            var deadline = DateTime.UtcNow.AddMilliseconds(_markingSettings.StopWaitTimeoutMs);
            while (_inFlightCount > 0 && DateTime.UtcNow < deadline)
                await Task.Delay(100);

            if (_inFlightCount > 0)
                ShowErrorRequested?.Invoke(this,
                    $"타임아웃({_markingSettings.StopWaitTimeoutMs / 1000}초)이 발생했습니다.\n" +
                    "일부 항목이 Pending 상태로 남을 수 있습니다.");
        }

        _markingCts?.Cancel();
        _isStoppingInProgress = false;
        IsOperating           = false;
        State                 = SystemState.ResultReady;
        StatusMessage         = "발행 정지.  결과를 저장하거나 검토하세요.";
    }

    private void ExecuteMarkDefect()
    {
        var selected = LotEntries.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0) return;

        bool hasOk = selected.Any(e => e.Result == InspectionResult.OK);
        string msg = hasOk
            ? "양품으로 판정된 항목이 포함되어 있습니다.\n불량 처리하시겠습니까?"
            : "선택된 항목을 불량(NG) 처리하시겠습니까?";

        if (ConfirmRequested?.Invoke(msg, "불량 처리 확인") != true) return;

        foreach (var entry in selected)
        {
            entry.Result     = InspectionResult.NG;
            entry.IsSelected = false;
            _db.UpdateInspectionResult(entry.MaterialBarcode, entry.LotBarcode, InspectionResult.NG);
        }
        RefreshCounts();
        StatusMessage = "선택된 항목을 불량(NG) 처리했습니다.";
    }

    private async void ExecuteSaveResult()
    {
        if (CurrentMaterial == null) return;

        var entries = LotEntries
            .Where(e => e.Result == InspectionResult.OK ||
                        e.Result == InspectionResult.NG ||
                        e.Result == InspectionResult.AD)
            .Select(e => new IssueResultDto(e.LotBarcode, e.Result.ToString()))
            .ToList();

        if (entries.Count > 0)
            await _api.PostIssueResultsAsync(CurrentMaterial.MaterialBarcode, entries);

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

    private async void ExecuteResetTestData()
    {
        if (CurrentMaterial == null) return;

        var barcode = CurrentMaterial.MaterialBarcode;
        var msg     = $"[{barcode}] 의 발행 이력을 삭제합니다.\n계속하시겠습니까?";
        if (ConfirmRequested?.Invoke(msg, "테스트 초기화") != true) return;

        await _api.ResetTestDataAsync(barcode);
        _db.ResetForTest(barcode);

        CurrentMaterial = null;
        LotEntries.Clear();
        MaterialBarcodeInput = string.Empty;
        State         = SystemState.Idle;
        IsOperating   = false;
        StatusMessage = $"테스트 초기화 완료  ({barcode})";
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
