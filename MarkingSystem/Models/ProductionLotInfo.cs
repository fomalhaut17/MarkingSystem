namespace MarkingSystem.Models;

/// <summary>GET /production-lots/:lotCode 응답</summary>
public class ProductionLotInfo
{
    public string             LotCode             { get; set; } = string.Empty;
    public string             ProductName         { get; set; } = string.Empty;
    public string             ManufactureDate     { get; set; } = string.Empty;
    public string             ProductionEquipment { get; set; } = string.Empty;
    public string             ProductionMold      { get; set; } = string.Empty;
    public string             LotProductionQty    { get; set; } = string.Empty;
    public int                OkCount             { get; set; }
    public int                NgCount             { get; set; }
    public List<InjectionRow> InjectionConditions { get; set; } = [];
}
