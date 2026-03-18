using MarkingSystem.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace MarkingSystem.Services;

/// <summary>
/// 로컬 SQLite DB 접근 서비스.
/// DB 파일: %APPDATA%\ManntekMarkingSystem\marking.db
/// </summary>
public sealed class LocalDatabase
{
    private readonly string _connectionString;

    public LocalDatabase(bool isTestMode = false)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ManntekMarkingSystem");
        Directory.CreateDirectory(folder);
        var dbFile = isTestMode ? "marking_test.db" : "marking.db";
        _connectionString = $"Data Source={Path.Combine(folder, dbFile)}";
        Initialize();
    }

    // ── 초기화 ───────────────────────────────────────────────────────────────

    private void Initialize()
    {
        using var conn = OpenConnection();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL";
            cmd.ExecuteScalar();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS issue_log (
                    id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    material_barcode  TEXT    NOT NULL,
                    lot_code          TEXT    NOT NULL,
                    lot_ser           TEXT    NOT NULL,
                    inspection_result TEXT    NOT NULL DEFAULT 'Pending',
                    is_result_saved   INTEGER NOT NULL DEFAULT 0,
                    created_at        TEXT    NOT NULL,
                    updated_at        TEXT    NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_material_barcode
                    ON issue_log (material_barcode);
                CREATE INDEX IF NOT EXISTS idx_lot_code
                    ON issue_log (lot_code);
                DROP INDEX IF EXISTS uq_lot_code_ser;
                CREATE UNIQUE INDEX IF NOT EXISTS uq_material_lot_ser
                    ON issue_log (material_barcode, lot_code, lot_ser);
                """;
            cmd.ExecuteNonQuery();
        }
    }

    // ── 쓰기 ─────────────────────────────────────────────────────────────────

    /// <summary>각인 번호를 PLC에 전송할 때 호출. Pending 상태로 INSERT.</summary>
    public void InsertIssueLog(string materialBarcode, string lotBarcode)
    {
        var now = Now();
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO issue_log
                (material_barcode, lot_code, lot_ser,
                 inspection_result, is_result_saved, created_at, updated_at)
            VALUES ($mb, $lc, $ls, 'Pending', 0, $now, $now)
            """;
        cmd.Parameters.AddWithValue("$mb",  materialBarcode);
        cmd.Parameters.AddWithValue("$lc",  ParseLotCode(lotBarcode));
        cmd.Parameters.AddWithValue("$ls",  ParseLotSer(lotBarcode));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>스캐너 검사 결과(OK/AD) 또는 불량 처리(NG) 시 호출.</summary>
    public void UpdateInspectionResult(string materialBarcode, string lotBarcode, InspectionResult result)
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE issue_log
               SET inspection_result = $result, updated_at = $now
             WHERE material_barcode = $mb AND lot_code = $lc AND lot_ser = $ls AND is_result_saved = 0
            """;
        cmd.Parameters.AddWithValue("$result", result.ToString());
        cmd.Parameters.AddWithValue("$now",    Now());
        cmd.Parameters.AddWithValue("$mb",     materialBarcode);
        cmd.Parameters.AddWithValue("$lc",     ParseLotCode(lotBarcode));
        cmd.Parameters.AddWithValue("$ls",     ParseLotSer(lotBarcode));
        cmd.ExecuteNonQuery();
    }

    /// <summary>결과 저장 버튼 클릭 시 호출. 해당 물류 바코드의 미확정 레코드를 일괄 확정.</summary>
    public void SetResultSaved(string materialBarcode)
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE issue_log
               SET is_result_saved = 1, updated_at = $now
             WHERE material_barcode = $mb AND is_result_saved = 0
            """;
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$mb",  materialBarcode);
        cmd.ExecuteNonQuery();
    }

    // ── 읽기 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 미저장(is_result_saved=0) 발행 결과가 하나라도 존재하면 true.
    /// materialBarcode를 지정하면 해당 물류 바코드로 범위를 한정.
    /// </summary>
    public bool HasUnsavedResults(string? materialBarcode = null)
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        if (materialBarcode == null)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM issue_log WHERE is_result_saved = 0";
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM issue_log WHERE is_result_saved = 0 AND material_barcode = $mb";
            cmd.Parameters.AddWithValue("$mb", materialBarcode);
        }
        return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    /// <summary>
    /// 특정 물류 바코드의 모든 발행 이력을 반환 (저장 여부 무관).
    /// 반환: lot_barcode (lot_code+lot_ser) → InspectionResult 매핑. 없으면 빈 딕셔너리.
    /// </summary>
    public Dictionary<string, InspectionResult> GetLotEntriesByMaterial(string materialBarcode)
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT lot_code || lot_ser, inspection_result
              FROM issue_log
             WHERE material_barcode = $mb
             ORDER BY lot_ser
            """;
        cmd.Parameters.AddWithValue("$mb", materialBarcode);

        var result = new Dictionary<string, InspectionResult>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var lotBarcode = reader.GetString(0);
            if (Enum.TryParse<InspectionResult>(reader.GetString(1), out var r))
                result[lotBarcode] = r;
        }
        return result;
    }

    /// <summary>
    /// 특정 LotCode의 로컬 최종발행 Ser 반환.
    /// 기록 없으면 "000000" 반환.
    /// </summary>
    public string GetLastIssuedSer(string lotCode)
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(lot_ser) FROM issue_log WHERE lot_code = $lc";
        cmd.Parameters.AddWithValue("$lc", lotCode);
        return cmd.ExecuteScalar() as string ?? "000000";
    }

    // ── 테스트 유틸 ───────────────────────────────────────────────────────────

    /// <summary>테스트 초기화: issue_log 전체 삭제.</summary>
    public void ResetForTest()
    {
        using var conn = OpenConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM issue_log";
        cmd.ExecuteNonQuery();
    }

    // ── 내부 유틸 ─────────────────────────────────────────────────────────────

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static string Now()                  => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
    private static string ParseLotCode(string s) => s.Length >= 23 ? s[..23] : s;
    private static string ParseLotSer(string s)  => s.Length == 29 ? s[23..] : string.Empty;
}
