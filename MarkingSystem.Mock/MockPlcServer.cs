using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkingSystem.Mock;

/// <summary>
/// LS XGT FENET Mock PLC 서버 (TCP :47200).
/// [MOCK] 실제 PLC가 아닙니다. 테스트 전용.
/// </summary>
public sealed class MockPlcServer
{
    private readonly int _port;

    // ── 메모리 주소 (appsettings.json PlcMemorySettings 기본값과 일치) ──────────

    private const int MwLotBarcode1    = 100;  // %MW100 ~ %MW114  (PC → PLC)
    private const int MwLotBarcode2    = 120;  // %MW120 ~ %MW134  (PC → PLC)
    private const int MwBarcodeRequest = 140;  // %MW140            (PLC → PC)
    private const int MwScanned1       = 150;  // %MW150 ~ %MW164  (Scanner → PLC → PC)
    private const int MwScanned2       = 170;  // %MW170 ~ %MW184  (Scanner → PLC → PC)
    private const int LotWordCount     = 15;

    // ── XGT FENET 프로토콜 상수 ────────────────────────────────────────────────

    private const int    HeaderSize = 20;
    private const ushort CmdRead    = 0x0054;
    private const ushort CmdWrite   = 0x0058;

    public MockPlcServer(int port) => _port = port;

    // ── 서버 루프 ─────────────────────────────────────────────────────────────

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  [MOCK PLC]  TCP localhost:{_port}");
        Console.ResetColor();
        Console.WriteLine();

        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try   { client = await listener.AcceptTcpClientAsync(ct); }
            catch { break; }

            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }

        listener.Stop();
    }

    // ── 클라이언트 처리 ───────────────────────────────────────────────────────

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        Console.WriteLine($"  [MOCK PLC] 연결됨: {remote}");

        // 접속별 독립 메모리
        var state = new ConnectionState();

        // 연결 후 500ms → 첫 번째 발행 요청
        _ = TriggerBarcodeRequestAsync(state, 500, ct);

        using var stream = client.GetStream();
        var rxBuf = new List<byte>();
        var tmp   = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n;
                try   { n = await stream.ReadAsync(tmp, ct); }
                catch { break; }
                if (n == 0) break;

                rxBuf.AddRange(tmp[..n]);

                while (rxBuf.Count >= HeaderSize)
                {
                    if (!HasValidMagic(rxBuf))
                    {
                        Console.WriteLine("  [MOCK PLC] 잘못된 헤더, 버퍼 초기화");
                        rxBuf.Clear();
                        break;
                    }

                    var dataLen = (ushort)(rxBuf[17] | (rxBuf[18] << 8));
                    var total   = HeaderSize + dataLen;
                    if (rxBuf.Count < total) break;

                    var invokeId = (ushort)(rxBuf[15] | (rxBuf[16] << 8));
                    var appData  = rxBuf.GetRange(HeaderSize, dataLen).ToArray();
                    rxBuf.RemoveRange(0, total);

                    var respData = HandleAppData(appData, state, stream, ct);
                    var frame    = BuildFrame(invokeId, respData);
                    await stream.WriteAsync(frame, ct);
                }
            }
        }
        finally
        {
            state.CancelEngraving();
            client.Dispose();
            Console.WriteLine($"  [MOCK PLC] 연결 종료: {remote}");
        }
    }

    // ── App data 처리 (READ / WRITE) ──────────────────────────────────────────

    private byte[] HandleAppData(byte[] data, ConnectionState state,
        NetworkStream stream, CancellationToken ct)
    {
        if (data.Length < 6) return BuildError(0x0003);

        var cmd      = (ushort)(data[0] | (data[1] << 8));
        var blockCnt = (ushort)(data[4] | (data[5] << 8));
        int offset   = 6;

        if (cmd == CmdRead)
        {
            var parts = new List<byte>();
            parts.AddRange(ToLE16(0));        // error = 0
            parts.AddRange(ToLE16(blockCnt));

            for (int b = 0; b < blockCnt; b++)
            {
                var nameLen = (ushort)(data[offset] | (data[offset + 1] << 8)); offset += 2;
                var varName = Encoding.ASCII.GetString(data, offset, nameLen);  offset += nameLen;
                var wordCnt = (ushort)(data[offset] | (data[offset + 1] << 8)); offset += 2;

                var addr = ParseAddr(varName);
                parts.AddRange(ToLE16(wordCnt));
                lock (state.Lock)
                {
                    for (int i = 0; i < wordCnt; i++)
                        parts.AddRange(ToLE16(state.Memory.GetValueOrDefault(addr + i)));
                }

                var preview = Enumerable.Range(0, Math.Min((int)wordCnt, 4))
                    .Select(i => state.Memory.GetValueOrDefault(addr + i));
                Console.WriteLine($"  [MOCK PLC] READ  {varName}[{wordCnt}] → [{string.Join(", ", preview)}{(wordCnt > 4 ? "..." : "")}]");
            }
            return [.. parts];
        }

        if (cmd == CmdWrite)
        {
            for (int b = 0; b < blockCnt; b++)
            {
                var nameLen = (ushort)(data[offset] | (data[offset + 1] << 8)); offset += 2;
                var varName = Encoding.ASCII.GetString(data, offset, nameLen);  offset += nameLen;
                var wordCnt = (ushort)(data[offset] | (data[offset + 1] << 8)); offset += 2;

                var addr = ParseAddr(varName);
                ushort prevVal;

                lock (state.Lock)
                {
                    prevVal = state.Memory.GetValueOrDefault(addr);
                    for (int i = 0; i < wordCnt; i++)
                    {
                        state.Memory[addr + i] = (ushort)(data[offset] | (data[offset + 1] << 8));
                        offset += 2;
                    }
                }

                // 상태 변화 감지
                if (addr == MwBarcodeRequest)
                {
                    var newVal = state.Memory.GetValueOrDefault(MwBarcodeRequest);
                    if (prevVal != 0 && newVal == 0)
                        OnBarcodeRequestCleared(state, ct);
                }
                else if (addr == MwScanned1 && IsClear(state, MwScanned1))
                {
                    OnScannedBarcodesCleared(state, ct);
                }

                if (wordCnt <= 4)
                {
                    var vals = Enumerable.Range(0, wordCnt).Select(i => state.Memory.GetValueOrDefault(addr + i));
                    Console.WriteLine($"  [MOCK PLC] WRITE {varName}[{wordCnt}] ← [{string.Join(", ", vals)}]");
                }
                else
                {
                    Console.WriteLine($"  [MOCK PLC] WRITE {varName}[{wordCnt}] ← \"{ReadBarcode(state, addr)}\"");
                }
            }

            var resp = new List<byte>();
            resp.AddRange(ToLE16(0));
            resp.AddRange(ToLE16(blockCnt));
            return [.. resp];
        }

        Console.WriteLine($"  [MOCK PLC] 알 수 없는 명령: 0x{cmd:X4}");
        return BuildError(0x0003);
    }

    // ── 상태 머신 ────────────────────────────────────────────────────────────

    private void OnBarcodeRequestCleared(ConnectionState state, CancellationToken ct)
    {
        var bc1 = ReadBarcode(state, MwLotBarcode1);
        var bc2 = ReadBarcode(state, MwLotBarcode2);
        if (string.IsNullOrEmpty(bc1) || string.IsNullOrEmpty(bc2)) return;

        Console.WriteLine($"  [MOCK PLC] 바코드 수신: #1=\"{bc1}\", #2=\"{bc2}\"");

        var delay = Random.Shared.Next(1000, 2000);
        state.StartEngraving(async engraveCt =>
        {
            await Task.Delay(delay, engraveCt);

            // 90% 일치, 10% 불일치 (마지막 문자 변조)
            var sc1 = Random.Shared.NextDouble() > 0.1 ? bc1 : bc1[..^1] + "X";
            var sc2 = Random.Shared.NextDouble() > 0.1 ? bc2 : bc2[..^1] + "X";

            lock (state.Lock)
            {
                WriteBarcode(state, MwScanned1, sc1);
                WriteBarcode(state, MwScanned2, sc2);
            }

            Console.WriteLine($"  [MOCK PLC] 스캐너 결과: #11=\"{sc1}\" {(sc1 == bc1 ? "OK ✓" : "NG ✗")},  " +
                              $"#12=\"{sc2}\" {(sc2 == bc2 ? "OK ✓" : "NG ✗")}");
        }, ct);
    }

    private void OnScannedBarcodesCleared(ConnectionState state, CancellationToken ct)
        => _ = TriggerBarcodeRequestAsync(state, 300, ct);

    private static async Task TriggerBarcodeRequestAsync(ConnectionState state, int delayMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delayMs, ct);
            lock (state.Lock) state.Memory[MwBarcodeRequest] = 1;
            Console.WriteLine("  [MOCK PLC] 발행 요청 세팅 (MW140=1)");
        }
        catch (OperationCanceledException) { }
    }

    // ── 메모리 헬퍼 ───────────────────────────────────────────────────────────

    private static string ReadBarcode(ConnectionState state, int start)
    {
        var bytes = new byte[LotWordCount * 2];
        lock (state.Lock)
        {
            for (int i = 0; i < LotWordCount; i++)
            {
                var w = state.Memory.GetValueOrDefault(start + i);
                bytes[i * 2]     = (byte)(w & 0xFF);
                bytes[i * 2 + 1] = (byte)(w >> 8);
            }
        }
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    private static void WriteBarcode(ConnectionState state, int start, string barcode)
    {
        var src = Encoding.ASCII.GetBytes(barcode);
        for (int i = 0; i < LotWordCount; i++)
        {
            var lo = i * 2     < src.Length ? src[i * 2]     : (byte)0;
            var hi = i * 2 + 1 < src.Length ? src[i * 2 + 1] : (byte)0;
            state.Memory[start + i] = (ushort)(lo | (hi << 8));
        }
    }

    private static bool IsClear(ConnectionState state, int start)
    {
        lock (state.Lock)
        {
            for (int i = 0; i < LotWordCount; i++)
                if (state.Memory.GetValueOrDefault(start + i) != 0) return false;
        }
        return true;
    }

    // ── 프레임 헬퍼 ───────────────────────────────────────────────────────────

    private static bool HasValidMagic(List<byte> buf)
    {
        var magic = "LSIS-XGT"u8;
        for (int i = 0; i < 8; i++)
            if (buf[i] != magic[i]) return false;
        return true;
    }

    private static byte[] BuildFrame(ushort invokeId, byte[] appData)
    {
        var frame = new byte[HeaderSize + appData.Length];
        Encoding.ASCII.GetBytes("LSIS-XGT").CopyTo(frame, 0);
        frame[14] = 0x11;  // SrcPlc (응답)
        frame[15] = (byte)(invokeId      & 0xFF);
        frame[16] = (byte)(invokeId >> 8);
        frame[17] = (byte)(appData.Length      & 0xFF);
        frame[18] = (byte)(appData.Length >> 8);
        appData.CopyTo(frame, HeaderSize);
        return frame;
    }

    private static byte[] BuildError(ushort code) => [.. ToLE16(code), 0, 0];

    private static byte[] ToLE16(ushort v) => [(byte)(v & 0xFF), (byte)(v >> 8)];

    private static int ParseAddr(string varName)
    {
        var m = Regex.Match(varName, @"%MW(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    // ── 접속별 상태 ───────────────────────────────────────────────────────────

    private sealed class ConnectionState
    {
        public Dictionary<int, ushort>  Memory { get; } = [];
        public readonly object          Lock   = new();

        private CancellationTokenSource? _engraveCts;

        public void StartEngraving(Func<CancellationToken, Task> action, CancellationToken parentCt)
        {
            _engraveCts?.Cancel();
            _engraveCts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);
            var tok = _engraveCts.Token;
            _ = Task.Run(async () =>
            {
                try   { await action(tok); }
                catch (OperationCanceledException) { }
            }, tok);
        }

        public void CancelEngraving()
        {
            _engraveCts?.Cancel();
            _engraveCts = null;
        }
    }
}
