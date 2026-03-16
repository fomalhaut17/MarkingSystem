using System.IO.Ports;
using System.Text;

namespace MarkingSystem.Services;

/// <summary>
/// LS XGT Cnet 프로토콜 구현 클라이언트 (RS-232 시리얼).
/// LS XBC-DN64H 실 PLC의 내장 RS-232 포트에 직접 연결한다.
///
/// 참조 문서: 사용설명서_XGB Cnet_국문_V2.3_20251124.pdf §7 XGT 전용 프로토콜
///
/// 프레임 구조 (ASCII 기반):
///   요청: ENQ + 국번(2) + 명령(1) + 타입(2) + 변수길이(2) + 변수명 + 데이터수(2) [+ 데이터] + EOT
///   ACK : ACK + 국번(2) + 명령(1) + 타입(2) [+ 블록수(2) + 바이트수(2) + 데이터] + ETX
///   NAK : NAK + 국번(2) + 명령(1) + 타입(2) + 에러코드(4) + ETX
///
/// 대문자 명령어(W/R) 사용 → BCC 생략.
/// 데이터 바이트 순서: 각 Word의 상위 바이트(High)를 먼저 전송 (big-endian in frame).
/// PLC 메모리 저장 순서는 little-endian이므로, 변환 시 raw[i*2]=Low, raw[i*2+1]=High.
/// </summary>
public sealed class CnetSerialPlcClient : IPlcClient
{
    // ── Cnet 제어 코드 ────────────────────────────────────────────────────────
    private const byte ENQ = 0x05;
    private const byte ACK = 0x06;
    private const byte ETX = 0x03;
    private const byte EOT = 0x04;

    // ── PLC 메모리 맵 ─────────────────────────────────────────────────────────
    private const string AddrLotBarcode      = "%MW100";
    private const int    LotBarcodeWordCount = 15;
    private const string AddrCommand         = "%MW116";
    private const string AddrStatus          = "%MW117";

    // ── 직렬 포트 ─────────────────────────────────────────────────────────────
    private readonly string        _portName;
    private readonly int           _baudRate;
    private readonly string        _stationNo; // 2자리 ASCII hex (예: "01")
    private SerialPort?            _port;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _port?.IsOpen == true;

    public CnetSerialPlcClient(string portName, int baudRate, string stationNo)
    {
        _portName  = portName;
        _baudRate  = baudRate;
        _stationNo = stationNo.PadLeft(2, '0').ToUpper();
    }

    // ── 연결 관리 ─────────────────────────────────────────────────────────────

    public Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        Disconnect();
        try
        {
            _port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout  = 3_000,
                WriteTimeout = 3_000,
            };
            _port.Open();
            return Task.FromResult(true);
        }
        catch
        {
            Disconnect();
            return Task.FromResult(false);
        }
    }

    public void Disconnect()
    {
        _port?.Close();
        _port?.Dispose();
        _port = null;
    }

    // ── 고수준 API ────────────────────────────────────────────────────────────

    public async Task<bool> WriteLotBarcodeAsync(string lotBarcode)
    {
        var raw = new byte[LotBarcodeWordCount * 2];
        var src = Encoding.ASCII.GetBytes(lotBarcode);
        Buffer.BlockCopy(src, 0, raw, 0, Math.Min(src.Length, raw.Length));
        return await WriteWordsAsync(AddrLotBarcode, LotBarcodeWordCount, raw);
    }

    public Task<bool> WriteStartCommandAsync() => WriteSingleWordAsync(AddrCommand, 1);
    public Task<bool> WriteStopCommandAsync()  => WriteSingleWordAsync(AddrCommand, 2);
    public Task<bool> ClearCommandAsync()      => WriteSingleWordAsync(AddrCommand, 0);

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

    // ── 저수준 Write ──────────────────────────────────────────────────────────

    private Task<bool> WriteSingleWordAsync(string varName, ushort value)
    {
        var raw = new byte[] { (byte)(value & 0xFF), (byte)(value >> 8) };
        return WriteWordsAsync(varName, 1, raw);
    }

    private async Task<bool> WriteWordsAsync(string varName, int wordCount, byte[] rawBytes)
    {
        await _lock.WaitAsync();
        try
        {
            if (_port == null) return false;

            // 데이터 hex 문자열 조립: 각 Word = 상위 바이트 먼저 (big-endian in frame)
            var dataSb = new StringBuilder(wordCount * 4);
            for (int i = 0; i < wordCount; i++)
            {
                byte lo = rawBytes[i * 2];
                byte hi = rawBytes[i * 2 + 1];
                dataSb.Append(hi.ToString("X2"));
                dataSb.Append(lo.ToString("X2"));
            }

            var frame = BuildRequestFrame("WSB", varName, wordCount, dataSb.ToString());
            var (ok, _) = await ExchangeAsync(frame);
            return ok;
        }
        finally { _lock.Release(); }
    }

    // ── 저수준 Read ───────────────────────────────────────────────────────────

    private async Task<ushort[]?> ReadWordsAsync(string varName, int wordCount)
    {
        await _lock.WaitAsync();
        try
        {
            if (_port == null) return null;

            var frame = BuildRequestFrame("RSB", varName, wordCount, null);
            var (ok, body) = await ExchangeAsync(frame);
            if (!ok || body == null) return null;

            // ACK 응답 body: 국번(2) + "R" + "SB" + 블록수(2) + 바이트수(2) + 데이터
            // prefix = 2 + 1 + 2 + 2 + 2 = 9 chars
            const int prefixLen = 9;
            if (body.Length < prefixLen + wordCount * 4) return null;

            var dataStr = body.Substring(prefixLen);
            var result  = new ushort[wordCount];
            for (int i = 0; i < wordCount; i++)
            {
                // 프레임 내 big-endian: 상위 바이트 먼저 → ushort 값 그대로 사용
                result[i] = Convert.ToUInt16(dataStr.Substring(i * 4, 4), 16);
            }
            return result;
        }
        finally { _lock.Release(); }
    }

    // ── 프레임 빌더 ───────────────────────────────────────────────────────────

    /// <summary>
    /// Cnet 요청 프레임을 조립한다. BCC 없음(대문자 명령어).
    /// cmdType: "WSB" 또는 "RSB"
    /// </summary>
    private byte[] BuildRequestFrame(string cmdType, string varName, int wordCount, string? data)
    {
        // ENQ + 국번 + 명령(W/R) + 타입(SB) + 변수길이(2 hex) + 변수명 + 데이터수(2 hex) [+ 데이터] + EOT
        var sb = new StringBuilder();
        sb.Append((char)ENQ);
        sb.Append(_stationNo);
        sb.Append(cmdType[0]);              // 'W' 또는 'R'
        sb.Append(cmdType.Substring(1));    // "SB"
        sb.Append(varName.Length.ToString("X2"));
        sb.Append(varName);
        sb.Append(wordCount.ToString("X2"));
        if (data != null) sb.Append(data);
        sb.Append((char)EOT);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    // ── 송수신 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 프레임을 전송하고 ETX까지 응답을 수신한다.
    /// 반환: (ACK 여부, ETX 이전 본문 문자열)
    /// </summary>
    private Task<(bool ok, string? body)> ExchangeAsync(byte[] requestFrame)
    {
        return Task.Run(() =>
        {
            try
            {
                _port!.DiscardInBuffer();
                _port.Write(requestFrame, 0, requestFrame.Length);

                // 첫 바이트: ACK(0x06) 또는 NAK(0x15)
                int first = _port.ReadByte();

                // ACK/NAK 이후 ETX까지 본문 수집
                var sb = new StringBuilder();
                int b;
                while ((b = _port.ReadByte()) != ETX)
                    sb.Append((char)b);

                return (first == ACK, sb.ToString());
            }
            catch
            {
                return (false, (string?)null);
            }
        });
    }

    public void Dispose()
    {
        _lock.Dispose();
        Disconnect();
    }
}
