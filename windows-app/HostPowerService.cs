namespace AudioPuck;

internal readonly record struct HostPowerStatus(
    bool HasBattery, int Percent, bool PluggedIn);

internal static class HostPowerService
{
    public static HostPowerStatus Read(HostStatusConfig config)
    {
        // -1 is an explicit desktop/no-laptop override.  It is useful on PCs
        // where a UPS or another HID battery can otherwise look like a laptop
        // battery to Windows.  -2 asks Windows for the real built-in status.
        if (config.TestBatteryPercent == -1)
            return new(false, 0, false);
        if (config.TestBatteryPercent >= 0)
            return new(true, config.TestBatteryPercent,
                config.TestPluggedIn);

        var status = SystemInformation.PowerStatus;
        var charge = status.BatteryChargeStatus;
        var hasBattery = !charge.HasFlag(BatteryChargeStatus.NoSystemBattery) &&
                         status.BatteryLifePercent >= 0;
        if (!hasBattery) return new(false, 0,
            status.PowerLineStatus == PowerLineStatus.Online);

        var percent = (int)Math.Round(status.BatteryLifePercent * 100,
            MidpointRounding.AwayFromZero);
        return new(true, Math.Clamp(percent, 0, 100),
            status.PowerLineStatus == PowerLineStatus.Online ||
            charge.HasFlag(BatteryChargeStatus.Charging));
    }
}
