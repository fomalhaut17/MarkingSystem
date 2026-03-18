namespace MarkingSystem.Models;

/// <summary>GET /lots 응답 항목 — Lot 조회 화면 DataGrid 한 행</summary>
public class LotItem
{
    public string MaterialBarcode  { get; set; } = string.Empty;
    public string LotBarcode       { get; set; } = string.Empty;
    public string InspectionResult { get; set; } = string.Empty;
}
