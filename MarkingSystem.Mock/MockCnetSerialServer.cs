using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkingSystem.Mock;

/// <summary>
/// LS XGT Cnet RS-232 Mock PLC 서버.
/// [MOCK] 실제 PLC가 아닙니다. 테스트 전용.
///
/// 가상 COM 포트 쌍(com0com) 필요:
///   앱(COM5) ←── null-modem ──→ Mock(COM6)
///
/// 프레임 구조 (ASCII, BCC 없음):
///   요청: ENQ + 국번(2) + 명령(R/W) + 타입(SB) + 변수길이(2hex) + 변수명 + 데이터수(2hex) [+ 데이터] + EOT
///   ACK : ACK + 국번(2) + 명령(R/W) + 타입(SB) [+ 블록수(2) + 바이트수(2) + 데이터] + ETX
///   NAK : NAK + 국번(2) + 명령(R/W) + 타입(SB) + 에러코드(4) + ETX
/// </summary>
public sealed class MockCnetSerialServer
{
    private readonly string _portName;
    private readonly int    _baudRate;
    private readonly string _stationNo;

    private const byte ENQ = 0x05;
    private const byte ACK = 0x06;
    private const byte NAK = 0x15;
    private const byte ETX = 0x03;
    private const byte EOT = 0x04;

    // ── 메모리 주소 (appsettings.json PlcMemorySettings 기본값과 일치) ──────────

    private const int MwLotBarcode1    = 100;  // %DW100 ~ %DW114
    private const int MwLotBarcode2    = 120;  // %DW120 ~ %DW134
    private const int MwBarcodeRequest = 140;  // %DW140
    private const int MwScanned1       = 150;  // %DW150 ~ %DW164
    private const int MwScanned2       = 170;  // %DW170 ~ %DW184
    private const int LotWordCount     = 15;

    private readonly Dictionary<int, ushort>  _memory = [];
    private CancellationTokenSource?          _engraveCts;

    public MockCnetSerialServer(string portName, int baudRate = 9600, string stationNo = "01")
    {
        _portName  = portName;
        _baudRate  = baudRate;
        _stationNo = stationNo.PadLeft(2, '0').ToUpper();
    }

    // ── 서버 루프 ─────────────────────────────────────────────────────────────

    public async Task RunAsync(CancellationToken ct)
    {
        using var port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout  = 500,   // 500ms 단위로 ct 확인
            WriteTimeout = 3_000,
        };

        try
        {
            port.Open();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [MOCK SERIAL] {_portName} 열기 실패: {ex.Message}");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"  [MOCK SERIAL] {_portName}  Cnet {_baudRate}bps  국번={_stationNo}");
        Console.ResetColor();

        // 연결 직후 500ms 후 발행 요청 세팅
        _ = TriggerBarcodeRequestAsync(500, ct);

        var rxBuf = new List<byte>();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int b;
                try   { b = port.ReadByte(); }
                catch (TimeoutException) { continue; }
                catch { break; }

                rxBuf.Add((byte)b);

                // EOT 도착 = 요청 프레임 완성
                if ((byte)b == EOT)
                {
                    var response = HandleRequest(rxBuf.ToArray());
                    rxBuf.Clear();

                    if (response != null)
                    {
                        try   { port.Write(response, 0, response.Length); }
                        catch { break; }
                    }
                }
            }
        }
        finally
        {
            _engraveCts?.Cancel();
            port.Close();
            Console.WriteLine($"  [MOCK SERIAL] {_portName} 닫힘");
        }

        await Task.CompletedTask;
    }

    // ── 요청 처리 ─────────────────────────────────────────────────────────────

    private byte[]? HandleRequest(byte[] frame)
    {
        // 최소: ENQ(1) + stationNo(2) + cmd(1) + type(2) + varNameLen(2) + varName(최소1) + wordCount(2) + EOT(1) = 12
        if (frame.Length < 12 || frame[0] != ENQ || frame[^1] != EOT)
            return null;

        try
        {
            // ENQ 와 EOT 를 제외한 본문
            var s = Encoding.ASCII.GetString(frame, 1, frame.Length - 2);

            var stn        = s[..2];
            var cmd        = s[2];        // 'R' or 'W'
            var typ        = s[3..5];     // "SB"
            int nameLen    = Convert.ToInt32(s[5..7], 16);
            var varName    = s[7..(7 + nameLen)];
            int ofs        = 7 + nameLen;
            int wordCount  = Convert.ToInt32(s[ofs..(ofs + 2)], 16);
            ofs += 2;

            var addr = ParseAddr(varName);

            if (cmd == 'R')
                return HandleRead(stn, typ, addr, wordCount);

            if (cmd == 'W')
                return HandleWrite(stn, typ, cmd, addr, wordCount, s[ofs..]);

            return BuildNak(stn, cmd, typ, "1234");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [MOCK SERIAL] 파싱 오류: {ex.Message}");
            return null;
        }
    }

    // ── READ (RSB) ────────────────────────────────────────────────────────────

    private byte[] HandleRead(string stn, string typ, int addr, int wordCount)
    {
        var dataSb = new StringBuilder(wordCount * 4);
        for (int i = 0; i < wordCount; i++)
            dataSb.Append(_memory.GetValueOrDefault(addr + i).ToString("X4"));

        // 바이트수 = word 수 × 2 (raw bytes)
        int byteCount = wordCount * 2;
        var body = $"{stn}R{typ}01{byteCount:X2}{dataSb}";

        var resp = new byte[1 + Encoding.ASCII.GetByteCount(body) + 1];
        resp[0] = ACK;
        Encoding.ASCII.GetBytes(body).CopyTo(resp, 1);
        resp[^1] = ETX;

        var preview = Enumerable.Range(0, Math.Min(wordCount, 4))
            .Select(i => _memory.GetValueOrDefault(addr + i));
        Console.WriteLine($"  [MOCK SERIAL] READ  %DW{addr}[{wordCount}] → [{string.Join(", ", preview)}{(wordCount > 4 ? "..." : "")}]");

        return resp;
    }

    // ── WRITE (WSB) ───────────────────────────────────────────────────────────

    private byte[] HandleWrite(string stn, string typ, char cmd, int addr, int wordCount, string dataStr)
    {
        ushort prevVal = _memory.GetValueOrDefault(addr);

        for (int i = 0; i < wordCount; i++)
            _memory[addr + i] = Convert.ToUInt16(dataStr.Substring(i * 4, 4), 16);

        // 상태 변화 감지
        if (addr == MwBarcodeRequest)
        {
            var newVal = _memory[MwBarcodeRequest];
            if (prevVal != 0 && newVal == 0)
                OnBarcodeRequestCleared();
        }
        else if (addr == MwScanned1 && IsClear(MwScanned1))
        {
            OnScannedBarcodesCleared();
        }

        if (wordCount <= 4)
        {
            var vals = Enumerable.Range(0, wordCount).Select(i => _memory.GetValueOrDefault(addr + i));
            Console.WriteLine($"  [MOCK SERIAL] WRITE %DW{addr}[{wordCount}] ← [{string.Join(", ", vals)}]");
        }
        else
        {
            Console.WriteLine($"  [MOCK SERIAL] WRITE %DW{addr}[{wordCount}] ← \"{ReadBarcode(addr)}\"");
        }

        var body = $"{stn}{cmd}{typ}";
        var resp = new byte[1 + Encoding.ASCII.GetByteCount(body) + 1];
        resp[0] = ACK;
        Encoding.ASCII.GetBytes(body).CopyTo(resp, 1);
        resp[^1] = ETX;
        return resp;
    }

    // ── 상태 머신 ─────────────────────────────────────────────────────────────

    private void OnBarcodeRequestCleared()
    {
        var bc1 = ReadBarcode(MwLotBarcode1);
        var bc2 = ReadBarcode(MwLotBarcode2);
        if (string.IsNullOrEmpty(bc1) || string.IsNullOrEmpty(bc2)) return;

        Console.WriteLine($"  [MOCK SERIAL] 바코드 수신: #1=\"{bc1}\", #2=\"{bc2}\"");

        _engraveCts?.Cancel();
        _engraveCts = new CancellationTokenSource();
        var tok = _engraveCts.Token;

        var delay = Random.Shared.Next(1000, 2000);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, tok);

                var sc1 = Random.Shared.NextDouble() > 0.1 ? bc1 : bc1[..^1] + "X";
                var sc2 = Random.Shared.NextDouble() > 0.1 ? bc2 : bc2[..^1] + "X";

                WriteBarcode(MwScanned1, sc1);
                WriteBarcode(MwScanned2, sc2);

                Console.WriteLine($"  [MOCK SERIAL] 스캐너 결과: #11=\"{sc1}\" {(sc1 == bc1 ? "OK ✓" : "NG ✗")},  " +
                                  $"#12=\"{sc2}\" {(sc2 == bc2 ? "OK ✓" : "NG ✗")}");
            }
            catch (OperationCanceledException) { }
        }, tok);
    }

    private void OnScannedBarcodesCleared()
        => _ = TriggerBarcodeRequestAsync(300, CancellationToken.None);

    private async Task TriggerBarcodeRequestAsync(int delayMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delayMs, ct);
            _memory[MwBarcodeRequest] = 1;
            Console.WriteLine("  [MOCK SERIAL] 발행 요청 세팅 (MW140=1)");
        }
        catch (OperationCanceledException) { }
    }

    // ── 메모리 헬퍼 ───────────────────────────────────────────────────────────

    private string ReadBarcode(int start)
    {
        var bytes = new byte[LotWordCount * 2];
        for (int i = 0; i < LotWordCount; i++)
        {
            var w = _memory.GetValueOrDefault(start + i);
            bytes[i * 2]     = (byte)(w & 0xFF);
            bytes[i * 2 + 1] = (byte)(w >> 8);
        }
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    private void WriteBarcode(int start, string barcode)
    {
        var src = Encoding.ASCII.GetBytes(barcode);
        for (int i = 0; i < LotWordCount; i++)
        {
            var lo = i * 2     < src.Length ? src[i * 2]     : (byte)0;
            var hi = i * 2 + 1 < src.Length ? src[i * 2 + 1] : (byte)0;
            _memory[start + i] = (ushort)(lo | (hi << 8));
        }
    }

    private bool IsClear(int start)
    {
        for (int i = 0; i < LotWordCount; i++)
            if (_memory.GetValueOrDefault(start + i) != 0) return false;
        return true;
    }

    private static byte[] BuildNak(string stn, char cmd, string typ, string errCode)
    {
        var body = $"{stn}{cmd}{typ}{errCode}";
        var resp = new byte[1 + Encoding.ASCII.GetByteCount(body) + 1];
        resp[0] = NAK;
        Encoding.ASCII.GetBytes(body).CopyTo(resp, 1);
        resp[^1] = ETX;
        return resp;
    }

    private static int ParseAddr(string varName)
    {
        var m = Regex.Match(varName, @"%[MD]W(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }
}
