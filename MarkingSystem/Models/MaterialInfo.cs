namespace MarkingSystem.Models;

public class MaterialInfo
{
    public string MaterialBarcode { get; set; } = string.Empty;
    public string ItemCode        { get; set; } = string.Empty;
    public string ProductName     { get; set; } = string.Empty;
    public string LotCode         { get; set; } = string.Empty;
    public int    IssuedQuantity  { get; set; }
    public int    OkCount         { get; set; }
    public int    NgCount         { get; set; }
    public int    AdCount         { get; set; }
    public string Worker          { get; set; } = string.Empty;
    public DateTime WorkDateTime  { get; set; } = DateTime.Now;
}
