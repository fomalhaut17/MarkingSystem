namespace MarkingSystem.Services;

/// <summary>
/// PLC 통신 추상화.
/// TCP    모드: XgtRawPlcClient      (FENET 프로토콜, Mock PLC)
/// Serial 모드: CnetSerialPlcClient  (Cnet 프로토콜, RS-232, LS XBC-DN64H 실 PLC)
/// appsettings.json의 Plc.Mode 값으로 PlcClientFactory가 구현체를 선택한다.
///
/// 각인 흐름:
///   1. [PLC→PC] ReadBarcodeRequestAsync() = true  (PLC가 바코드 요청)
///   2. [PC→PLC] WriteLotBarcodesAsync(bc1, bc2)   (Lot #1/#2 전송)
///   3. [PC→PLC] ClearBarcodeRequestAsync()        (요청 메모리 클리어)
///   4. [Scanner→PLC→PC] ReadScannedBarcodesAsync() 폴링 (스캐너 검사 결과)
///   5. [PC→PLC] ClearScannedBarcodesAsync()        (스캐너 메모리 클리어)
///   위 흐름을 모든 Lot 바코드가 처리될 때까지 반복.
/// </summary>
public interface IPlcClient : IDisposable
{
    bool IsConnected { get; }

    Task<bool> ConnectAsync(CancellationToken ct = default);
    void Disconnect();

    /// <summary>
    /// 발행 요청 메모리 확인. PLC가 1로 세팅하면 true 반환.
    /// (appsettings BarcodeRequestAddr)
    /// </summary>
    Task<bool> ReadBarcodeRequestAsync();

    /// <summary>
    /// Lot 바코드 2개를 PLC에 전송.
    /// barcode1: 낮은 Ser (Lot #1), barcode2: 높은 Ser (Lot #2)
    /// (appsettings LotBarcode1Addr / LotBarcode2Addr)
    /// </summary>
    Task<bool> WriteLotBarcodesAsync(string barcode1, string barcode2);

    /// <summary>
    /// 발행 요청 메모리를 0으로 클리어.
    /// (appsettings BarcodeRequestAddr)
    /// </summary>
    Task<bool> ClearBarcodeRequestAsync();

    /// <summary>
    /// 스캐너가 읽어 PLC에 기록한 바코드 2개 조회.
    /// 아직 스캐너가 읽지 않은 경우 (null, null) 반환.
    /// (appsettings ScannedBarcode1Addr / ScannedBarcode2Addr)
    /// </summary>
    Task<(string? barcode1, string? barcode2)> ReadScannedBarcodesAsync();

    /// <summary>
    /// 스캐너 바코드 메모리를 전체 0으로 클리어.
    /// (appsettings ScannedBarcode1Addr / ScannedBarcode2Addr)
    /// </summary>
    Task<bool> ClearScannedBarcodesAsync();
}
