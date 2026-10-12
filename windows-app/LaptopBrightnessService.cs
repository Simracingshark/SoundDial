using System.Management;

namespace AudioPuck;

/// <summary>
/// Controls the physical backlight exposed by a laptop's built-in panel.
/// External monitors normally do not expose these WMI classes and therefore
/// do not make the brightness target appear in SoundDial.
/// </summary>
internal sealed class LaptopBrightnessService : IDisposable
{
    private readonly object _gate = new();
    private bool _probed;
    private bool _available;
    private int? _cached;
    private DateTime _cacheTime;

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                if (!_probed)
                {
                    _probed = true;
                    _available = ReadCore() is not null && HasSetter();
                }
                return _available;
            }
        }
    }

    public int? Read()
    {
        lock (_gate)
        {
            if (!IsAvailable) return null;
            if (_cached is not null &&
                DateTime.UtcNow - _cacheTime < TimeSpan.FromSeconds(1))
                return _cached;
            return ReadCore();
        }
    }

    public void Adjust(int delta)
    {
        lock (_gate)
        {
            if (!IsAvailable || delta == 0) return;
            var current = ReadCore();
            if (current is null) return;
            var next = Math.Clamp(current.Value + delta, 0, 100);
            if (next == current.Value) return;
            if (SetCore(next))
            {
                _cached = next;
                _cacheTime = DateTime.UtcNow;
            }
        }
    }

    private int? ReadCore()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var value = Math.Clamp(Convert.ToInt32(
                        item["CurrentBrightness"]), 0, 100);
                    _cached = value;
                    _cacheTime = DateTime.UtcNow;
                    return value;
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or
                                   UnauthorizedAccessException or
                                   PlatformNotSupportedException)
        {
            Log.Warn($"Laptop brightness is unavailable: {ex.Message}");
        }
        return null;
    }

    private static bool HasSetter()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            using var results = searcher.Get();
            return results.Count > 0;
        }
        catch (Exception ex) when (ex is ManagementException or
                                   UnauthorizedAccessException or
                                   PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool SetCore(int percent)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                using (var arguments = item.GetMethodParameters(
                           "WmiSetBrightness"))
                {
                    arguments["Timeout"] = 0u;
                    arguments["Brightness"] = (byte)percent;
                    using var output = item.InvokeMethod("WmiSetBrightness",
                        arguments, null);
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or
                                   UnauthorizedAccessException or
                                   PlatformNotSupportedException)
        {
            Log.Warn($"Cannot change laptop brightness: {ex.Message}");
        }
        return false;
    }

    public void Dispose()
    {
    }
}
