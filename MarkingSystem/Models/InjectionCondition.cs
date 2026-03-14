namespace MarkingSystem.Models;

public class InjectionConditionRow
{
    public string Category      { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string Unit          { get; set; } = string.Empty;
    public string? Nozzle       { get; set; }
    public string? H1           { get; set; }
    public string? H2           { get; set; }
    public string? H3           { get; set; }
    public string? H4           { get; set; }
    public string? H5           { get; set; }
}

public static class InjectionConditionFactory
{
    public static List<InjectionConditionRow> CreateSampleData() =>
    [
        new() { Category = "온도",  ParameterName = "노즐 온도",       Unit = "°C",    Nozzle = "285", H1 = "-",   H2 = "-",   H3 = "-",   H4 = "-",   H5 = "-"   },
        new() { Category = "온도",  ParameterName = "배럴 온도",       Unit = "°C",    Nozzle = "-",   H1 = "280", H2 = "275", H3 = "270", H4 = "265", H5 = "255" },
        new() { Category = "압력",  ParameterName = "사출 압력",       Unit = "bar",   Nozzle = "-",   H1 = "155", H2 = "150", H3 = "145", H4 = "140", H5 = "135" },
        new() { Category = "속도",  ParameterName = "사출 속도",       Unit = "mm/s",  Nozzle = "-",   H1 = "85",  H2 = "80",  H3 = "75",  H4 = "70",  H5 = "65"  },
        new() { Category = "핫러너",ParameterName = "핫러너 온도 (M1)",Unit = "°C",    Nozzle = "290", H1 = "288", H2 = "285", H3 = "-",   H4 = "-",   H5 = "-"   },
        new() { Category = "핫러너",ParameterName = "핫러너 온도 (M2)",Unit = "°C",    Nozzle = "290", H1 = "286", H2 = "284", H3 = "-",   H4 = "-",   H5 = "-"   },
        new() { Category = "시간",  ParameterName = "냉각 시간",       Unit = "sec",   Nozzle = "-",   H1 = "18",  H2 = "-",   H3 = "-",   H4 = "-",   H5 = "-"   },
        new() { Category = "시간",  ParameterName = "사이클 타임",     Unit = "sec",   Nozzle = "-",   H1 = "42",  H2 = "-",   H3 = "-",   H4 = "-",   H5 = "-"   },
    ];
}
