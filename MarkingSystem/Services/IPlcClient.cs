namespace MarkingSystem.Services;

/// <summary>PLC 각인 처리 상태</summary>
public enum PlcStatus { Idle = 0, Marking = 1, DoneOk = 2, DoneNg = 3, Error = 99 }

/// <summary>
/// PLC 통신 추상화.
/// 개발: XgtRawPlcClient (직접 구현, Mock 서버와 통신)
/// 운영: HslPlcClient (HslCommunication, 실 LS XGT PLC와 통신)
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
