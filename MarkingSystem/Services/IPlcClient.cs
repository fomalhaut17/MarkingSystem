namespace MarkingSystem.Services;

/// <summary>PLC 각인 처리 상태</summary>
public enum PlcStatus { Idle = 0, Marking = 1, DoneOk = 2, DoneNg = 3, Error = 99 }

/// <summary>
/// PLC 통신 추상화.
/// TCP  모드: XgtRawPlcClient  (FENET 프로토콜, Mock PLC / 이더넷 모듈 장착 PLC)
/// Serial 모드: CnetSerialPlcClient (Cnet 프로토콜, RS-232, LS XBC-DN64H 실 PLC)
/// appsettings.json의 Plc.Mode 값으로 PlcClientFactory가 구현체를 선택한다.
/// </summary>
public interface IPlcClient : IDisposable
{
    bool IsConnected { get; }

    Task<bool> ConnectAsync(CancellationToken ct = default);
    void Disconnect();

    /// <summary>Lot 바코드 기록 (%MW100 ~ %MW114, 30 bytes ASCII)</summary>
    Task<bool> WriteLotBarcodeAsync(string lotBarcode);

    /// <summary>발행 시작 (%MW116 = 1)</summary>
    Task<bool> WriteStartCommandAsync();

    /// <summary>발행 정지 (%MW116 = 2)</summary>
    Task<bool> WriteStopCommandAsync();

    /// <summary>명령 클리어 (%MW116 = 0)</summary>
    Task<bool> ClearCommandAsync();

    /// <summary>각인 상태 조회 (%MW117)</summary>
    Task<PlcStatus> ReadStatusAsync();
}
