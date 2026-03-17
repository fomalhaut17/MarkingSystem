using System.IO;
using System.Net.Sockets;
using System.Text;

namespace MarkingSystem.Services;

/// <summary>
/// LS XGT FENET 프로토콜 직접 구현 클라이언트.
/// Mock PLC 서버(mock-plc/server.js)와 통신하는 개발용 구현체.
/// </summary>
public sealed class XgtRawPlcClient : IPlcClient
{
    // ── XGT FENET 프로토콜 상수 ───────────────────────────────────────────────

    private static readonly byte[] CompanyIdBytes =
        [.. Encoding.ASCII.GetBytes("LSIS-XGT"), 0x00, 0x00]; // 10 bytes

    private const byte   SrcPc      = 0x33;
    private const int    HeaderSize = 20;
    private const ushort TypeWord   = 0x0002;

    // ── PLC 메모리 주소 (appsettings.json에서 주입) ───────────────────────────

    private readonly string _addrLotBarcode1;
    private readonly string _addrLotBarcode2;
    private readonly int    _lotBarcodeWordCount;
    private readonly string _addrBarcodeRequest;
    private readonly string _addrScannedBarcode1;
    private readonly string _addrScannedBarcode2;

    // ── 접속 ──────────────────────────────────────────────────────────────────

    public const string DefaultHost = "127.0.0.1";
    public const int    DefaultPort = 2004;

    private readonly string        _host;
    private readonly int           _port;
    private TcpClient?             _tcp;
    private NetworkStream?         _stream;
    private ushort                 _invokeId;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _tcp?.Connected == true && _stream != null;

    public XgtRawPlcClient(string host, int port, PlcMemorySettings memory)
    {
        _host                = host;
        _port                = port;
        _addrLotBarcode1     = memory.LotBarcode1Addr;
        _addrLotBarcode2     = memory.LotBarcode2Addr;
        _lotBarcodeWordCount = memory.LotBarcodeWordCount;
        _addrBarcodeRequest  = memory.BarcodeRequestAddr;
        _addrScannedBarcode1 = memory.ScannedBarcode1Addr;
        _addrScannedBarcode2 = memory.ScannedBarcode2Addr;
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

    // ── IPlcClient 구현 ───────────────────────────────────────────────────────

    public async Task<bool> ReadBarcodeRequestAsync()
    {
        var words = await ReadWordsAsync(_addrBarcodeRequest, 1);
        return words?[0] == 1;
    }

    public async Task<bool> WriteLotBarcodesAsync(string barcode1, string barcode2)
    {
        return await WriteWordsAsync(_addrLotBarcode1, _lotBarcodeWordCount, BarcodeToBytes(barcode1))
            && await WriteWordsAsync(_addrLotBarcode2, _lotBarcodeWordCount, BarcodeToBytes(barcode2));
    }

    public Task<bool> ClearBarcodeRequestAsync() => WriteSingleWordAsync(_addrBarcodeRequest, 0);

    public async Task<(string? barcode1, string? barcode2)> ReadScannedBarcodesAsync()
    {
        var w1 = await ReadWordsAsync(_addrScannedBarcode1, _lotBarcodeWordCount);
        if (w1 == null) return (null, null);
        var bc1 = WordsToBarcode(w1);
        if (string.IsNullOrEmpty(bc1)) return (null, null);

        var w2 = await ReadWordsAsync(_addrScannedBarcode2, _lotBarcodeWordCount);
        if (w2 == null) return (null, null);
        var bc2 = WordsToBarcode(w2);
        if (string.IsNullOrEmpty(bc2)) return (null, null);

        return (bc1, bc2);
    }

    public async Task<bool> ClearScannedBarcodesAsync()
    {
        var zeros = new byte[_lotBarcodeWordCount * 2];
        return await WriteWordsAsync(_addrScannedBarcode1, _lotBarcodeWordCount, zeros)
            && await WriteWordsAsync(_addrScannedBarcode2, _lotBarcodeWordCount, zeros);
    }

    // ── 저수준 Read ───────────────────────────────────────────────────────────

    private async Task<ushort[]?> ReadWordsAsync(string varName, int wordCount)
    {
        await _lock.WaitAsync();
        try
        {
            if (_stream == null) return null;

            await SendFrameAsync(_invokeId++, BuildReadAppData(varName, (ushort)wordCount));

            var resp = await RecvAppDataAsync();
            if (resp == null || resp.Length < 6) return null;
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

    private Task<bool> WriteSingleWordAsync(string varName, ushort value)
    {
        var data = new byte[] { (byte)(value & 0xFF), (byte)(value >> 8) };
        return WriteWordsAsync(varName, 1, data);
    }

    private async Task<bool> WriteWordsAsync(string varName, int wordCount, byte[] data)
    {
        await _lock.WaitAsync();
        try
        {
            if (_stream == null) return false;

            await SendFrameAsync(_invokeId++, BuildWriteAppData(varName, wordCount, data));

            var resp = await RecvAppDataAsync();
            if (resp == null || resp.Length < 4) return false;
            return BitConverter.ToUInt16(resp, 0) == 0;
        }
        finally { _lock.Release(); }
    }

    // ── 바이트 변환 헬퍼 ──────────────────────────────────────────────────────

    private byte[] BarcodeToBytes(string barcode)
    {
        var data = new byte[_lotBarcodeWordCount * 2];
        var src  = Encoding.ASCII.GetBytes(barcode);
        Buffer.BlockCopy(src, 0, data, 0, Math.Min(src.Length, data.Length));
        return data;
    }

    private static string WordsToBarcode(ushort[] words)
    {
        var bytes = new byte[words.Length * 2];
        for (int i = 0; i < words.Length; i++)
        {
            bytes[i * 2]     = (byte)(words[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)(words[i] >> 8);
        }
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    // ── 프레임 빌더 ───────────────────────────────────────────────────────────

    private static byte[] BuildReadAppData(string varName, ushort wordCount)
    {
        var name = Encoding.ASCII.GetBytes(varName);
        var buf  = new byte[6 + 2 + name.Length + 2];
        int i    = 0;
        buf[i++] = 0x54; buf[i++] = 0x00;
        buf[i++] = (byte)(TypeWord & 0xFF); buf[i++] = (byte)(TypeWord >> 8);
        buf[i++] = 0x01; buf[i++] = 0x00;
        buf[i++] = (byte)(name.Length & 0xFF); buf[i++] = (byte)(name.Length >> 8);
        name.CopyTo(buf, i); i += name.Length;
        buf[i++] = (byte)(wordCount & 0xFF); buf[i] = (byte)(wordCount >> 8);
        return buf;
    }

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

    private static byte[] BuildHeader(ushort invokeId, int dataLen)
    {
        var h = new byte[HeaderSize];
        CompanyIdBytes.CopyTo(h, 0);
        h[14] = SrcPc;
        h[15] = (byte)(invokeId & 0xFF); h[16] = (byte)(invokeId >> 8);
        h[17] = (byte)(dataLen  & 0xFF); h[18] = (byte)(dataLen  >> 8);
        return h;
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
            var header = new byte[HeaderSize];
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
