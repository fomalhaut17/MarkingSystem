using System.IO;
using System.Net.Sockets;
using System.Text;

namespace MarkingSystem.Services;

/// <summary>LS XGT PLC 각인 처리 상태</summary>
public enum PlcStatus { Idle = 0, Marking = 1, DoneOk = 2, DoneNg = 3, Error = 99 }

/// <summary>
/// LS XGT FENET 프로토콜 TCP 클라이언트.
/// 연결 대상(Host/Port)만 변경하면 Mock PLC → 실 PLC 전환 가능.
/// </summary>
public sealed class PlcClient : IDisposable
{
    // ── XGT FENET 프로토콜 상수 ───────────────────────────────────────────────

    private static readonly byte[] CompanyIdBytes =
        [.. Encoding.ASCII.GetBytes("LSIS-XGT"), 0x00, 0x00]; // 10 bytes

    private const byte   SrcPc      = 0x33;   // 방향: Host → PLC
    private const byte   SrcPlc     = 0x11;   // 방향: PLC  → Host
    private const int    HeaderSize = 20;
    private const ushort CmdRead    = 0x0054;
    private const ushort CmdWrite   = 0x0058;
    private const ushort TypeWord   = 0x0002;

    // ── PLC 메모리 맵 (%MW = 메모리 워드, 2바이트 단위) ───────────────────────

    /// <summary>Lot 바코드 저장 영역: %MW100 ~ %MW114 (15 word = 30 bytes ASCII)</summary>
    private const string AddrLotBarcode      = "%MW100";
    private const int    LotBarcodeWordCount = 15;

    /// <summary>명령 레지스터: %MW116  (0=None, 1=Start, 2=Stop)</summary>
    private const string AddrCommand = "%MW116";

    /// <summary>상태 레지스터: %MW117  (0=Idle, 1=Marking, 2=DoneOK, 3=DoneNG, 9=Error)</summary>
    private const string AddrStatus = "%MW117";

    // ── 접속 정보 ─────────────────────────────────────────────────────────────

    // TODO: 실 PLC 전환 시 Host/Port를 설정 파일로 이동
    public const string DefaultHost = "127.0.0.1";
    public const int    DefaultPort = 2004;

    private readonly string _host;
    private readonly int    _port;

    private TcpClient?             _tcp;
    private NetworkStream?         _stream;
    private ushort                 _invokeId;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _tcp?.Connected == true && _stream != null;

    public PlcClient(string host = DefaultHost, int port = DefaultPort)
    {
        _host = host;
        _port = port;
    }

    // ── 연결 관리 ─────────────────────────────────────────────────────────────

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        Disconnect();
        try
        {
            _tcp = new TcpClient { ReceiveTimeout = 5_000, SendTimeout = 5_000 };
            await _tcp.ConnectAsync(_host, _port, ct);
            _stream = _tcp.GetStream();
            return true;
        }
        catch
        {
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        _stream?.Dispose(); _stream = null;
        _tcp?.Dispose();    _tcp    = null;
    }

    // ── 고수준 API ────────────────────────────────────────────────────────────

    /// <summary>Lot 바코드를 PLC에 기록한다 (%MW100 ~ %MW114, 30바이트 ASCII).</summary>
    public async Task<bool> WriteLotBarcodeAsync(string lotBarcode)
    {
        var data = new byte[LotBarcodeWordCount * 2];
        var src  = Encoding.ASCII.GetBytes(lotBarcode);
        Buffer.BlockCopy(src, 0, data, 0, Math.Min(src.Length, data.Length));
        return await WriteWordsAsync(AddrLotBarcode, LotBarcodeWordCount, data);
    }

    /// <summary>발행 시작 명령 전송 (%MW116 = 1).</summary>
    public Task<bool> WriteStartCommandAsync() => WriteSingleWordAsync(AddrCommand, 1);

    /// <summary>발행 정지 명령 전송 (%MW116 = 2).</summary>
    public Task<bool> WriteStopCommandAsync() => WriteSingleWordAsync(AddrCommand, 2);

    /// <summary>명령 레지스터 클리어 (%MW116 = 0).</summary>
    public Task<bool> ClearCommandAsync() => WriteSingleWordAsync(AddrCommand, 0);

    /// <summary>PLC 각인 상태를 읽는다 (%MW117).</summary>
    public async Task<PlcStatus> ReadStatusAsync()
    {
        var words = await ReadWordsAsync(AddrStatus, 1);
        if (words == null) return PlcStatus.Error;
        return words[0] switch
        {
            0 => PlcStatus.Idle,
            1 => PlcStatus.Marking,
            2 => PlcStatus.DoneOk,
            3 => PlcStatus.DoneNg,
            _ => PlcStatus.Error,
        };
    }

    // ── 저수준 Read ───────────────────────────────────────────────────────────

    private async Task<ushort[]?> ReadWordsAsync(string varName, ushort wordCount)
    {
        await _lock.WaitAsync();
        try
        {
            if (_stream == null) return null;

            var id      = _invokeId++;
            var appData = BuildReadAppData(varName, wordCount);
            await SendFrameAsync(id, appData);

            var resp = await RecvAppDataAsync();
            if (resp == null || resp.Length < 6) return null;

            // resp: [errCode(2LE)] [blockCnt(2LE)] [wordCnt(2LE)] [data...]
            if (BitConverter.ToUInt16(resp, 0) != 0) return null;

            var cnt    = BitConverter.ToUInt16(resp, 4);
            var result = new ushort[cnt];
            for (int i = 0; i < cnt; i++)
                result[i] = BitConverter.ToUInt16(resp, 6 + i * 2);
            return result;
        }
        finally { _lock.Release(); }
    }

    // ── 저수준 Write ──────────────────────────────────────────────────────────

    private async Task<bool> WriteSingleWordAsync(string varName, ushort value)
    {
        var data = new byte[2];
        data[0] = (byte)(value & 0xFF);
        data[1] = (byte)(value >> 8);
        return await WriteWordsAsync(varName, 1, data);
    }

    private async Task<bool> WriteWordsAsync(string varName, int wordCount, byte[] data)
    {
        await _lock.WaitAsync();
        try
        {
            if (_stream == null) return false;

            var id      = _invokeId++;
            var appData = BuildWriteAppData(varName, wordCount, data);
            await SendFrameAsync(id, appData);

            var resp = await RecvAppDataAsync();
            if (resp == null || resp.Length < 4) return false;

            return BitConverter.ToUInt16(resp, 0) == 0; // errCode == 0
        }
        finally { _lock.Release(); }
    }

    // ── 프레임 빌더 ───────────────────────────────────────────────────────────

    private static byte[] BuildHeader(ushort invokeId, int dataLen)
    {
        var h = new byte[HeaderSize];
        CompanyIdBytes.CopyTo(h, 0);                  // [0-9]   Company ID
        // [10-13] Reserved / PLC Info / CPU Info = 0x00
        h[14] = SrcPc;                                 // [14]    Source
        h[15] = (byte)(invokeId & 0xFF);               // [15-16] Invoke ID
        h[16] = (byte)(invokeId >> 8);
        h[17] = (byte)(dataLen & 0xFF);                // [17-18] Data Length
        h[18] = (byte)(dataLen >> 8);
        // [19]  FEnet Position = 0x00
        return h;
    }

    /// <summary>
    /// 읽기 개별 변수 요청 Application Data:
    /// [CMD(2)] [TYPE(2)] [블록수(2)] [이름길이(2)] [이름...] [데이터수(2)]
    /// </summary>
    private static byte[] BuildReadAppData(string varName, ushort wordCount)
    {
        var name   = Encoding.ASCII.GetBytes(varName);
        var buf    = new byte[6 + 2 + name.Length + 2];
        int i      = 0;
        buf[i++] = 0x54; buf[i++] = 0x00;                           // CMD
        buf[i++] = (byte)(TypeWord & 0xFF); buf[i++] = (byte)(TypeWord >> 8); // TYPE
        buf[i++] = 0x01; buf[i++] = 0x00;                           // Block count = 1
        buf[i++] = (byte)(name.Length & 0xFF); buf[i++] = (byte)(name.Length >> 8);
        name.CopyTo(buf, i); i += name.Length;
        buf[i++] = (byte)(wordCount & 0xFF); buf[i] = (byte)(wordCount >> 8);
        return buf;
    }

    /// <summary>
    /// 쓰기 개별 변수 요청 Application Data:
    /// [CMD(2)] [TYPE(2)] [블록수(2)] [이름길이(2)] [이름...] [데이터수(2)] [데이터...]
    /// </summary>
    private static byte[] BuildWriteAppData(string varName, int wordCount, byte[] data)
    {
        var name = Encoding.ASCII.GetBytes(varName);
        var buf  = new byte[6 + 2 + name.Length + 2 + data.Length];
        int i    = 0;
        buf[i++] = 0x58; buf[i++] = 0x00;
        buf[i++] = (byte)(TypeWord & 0xFF); buf[i++] = (byte)(TypeWord >> 8);
        buf[i++] = 0x01; buf[i++] = 0x00;
        buf[i++] = (byte)(name.Length & 0xFF); buf[i++] = (byte)(name.Length >> 8);
        name.CopyTo(buf, i); i += name.Length;
        buf[i++] = (byte)(wordCount & 0xFF); buf[i++] = (byte)(wordCount >> 8);
        data.CopyTo(buf, i);
        return buf;
    }

    // ── 송수신 ────────────────────────────────────────────────────────────────

    private async Task SendFrameAsync(ushort invokeId, byte[] appData)
    {
        var header = BuildHeader(invokeId, appData.Length);
        var frame  = new byte[header.Length + appData.Length];
        header.CopyTo(frame, 0);
        appData.CopyTo(frame, header.Length);
        await _stream!.WriteAsync(frame);
    }

    private async Task<byte[]?> RecvAppDataAsync()
    {
        if (_stream == null) return null;
        using var cts = new CancellationTokenSource(5_000);
        try
        {
            var header  = new byte[HeaderSize];
            await ReadExactAsync(header, cts.Token);

            var dataLen = BitConverter.ToUInt16(header, 17);
            if (dataLen == 0) return [];

            var data = new byte[dataLen];
            await ReadExactAsync(data, cts.Token);
            return data;
        }
        catch { return null; }
    }

    private async Task ReadExactAsync(byte[] buf, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buf.Length)
        {
            int n = await _stream!.ReadAsync(buf.AsMemory(offset, buf.Length - offset), ct);
            if (n == 0) throw new EndOfStreamException("PLC 연결이 끊어졌습니다.");
            offset += n;
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
        Disconnect();
    }
}
