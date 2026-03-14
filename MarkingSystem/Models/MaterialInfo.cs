namespace MarkingSystem.Models;

public class MaterialInfo
{
    public string MaterialBarcode { get; set; } = string.Empty;
    public string ProductName     { get; set; } = string.Empty;  // 품명
    public string ManufactureDate { get; set; } = string.Empty;  // 제조일자
    public string ContainerQty    { get; set; } = string.Empty;  // 용기 내 수량
    public string LotCode         { get; set; } = string.Empty;  // Lot 코드 (23자)
    public string LastIssuedSer   { get; set; } = string.Empty;  // 최종 발행 Ser.
    public int    OkCount         { get; set; }
    public int    NgCount         { get; set; }
    public int    AdCount         { get; set; }
    public DateTime WorkDateTime  { get; set; } = DateTime.Now;
}
