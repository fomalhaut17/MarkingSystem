namespace MarkingSystem.Models;

// 설정값(Set) / 실제값(Actual) 쌍
public class InjCell
{
    public string Set { get; set; } = "";
    public string Act { get; set; } = "";
}

// 사출 조건 한 행 (그룹명 + 행명 + H1~H5)
public class InjectionRow
{
    public string  GroupName { get; set; } = "";
    public string  RowName   { get; set; } = "";
    public InjCell H1        { get; set; } = new();
    public InjCell H2        { get; set; } = new();
    public InjCell H3        { get; set; } = new();
    public InjCell H4        { get; set; } = new();
    public InjCell H5        { get; set; } = new();
}
