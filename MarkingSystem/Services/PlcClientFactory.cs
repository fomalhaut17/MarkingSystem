namespace MarkingSystem.Services;

public static class PlcClientFactory
{
    /// <summary>
    /// PlcSettings.Mode 값에 따라 구현체를 선택하여 반환한다.
    /// "Serial" → CnetSerialPlcClient (RS-232, 실 PLC)
    /// 그 외    → XgtRawPlcClient    (TCP, Mock PLC / 기본값)
    /// </summary>
    public static IPlcClient Create(PlcSettings settings) =>
        settings.Mode.Equals("Serial", StringComparison.OrdinalIgnoreCase)
            ? new CnetSerialPlcClient(
                settings.Serial.PortName,
                settings.Serial.BaudRate,
                settings.Serial.StationNo)
            : new XgtRawPlcClient(
                settings.Tcp.Host,
                settings.Tcp.Port);
}
