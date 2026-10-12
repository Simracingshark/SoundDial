using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AudioPuck;

internal enum AudioSessionState { Inactive, Active, Expired }
internal sealed record EndpointSnapshot(string Id, string Name);
internal sealed record AudioValue(int Volume, bool Muted);
internal sealed record AudioSessionSnapshot(string EndpointId, string EndpointName,
    uint ProcessId, string ProcessName, string DisplayName,
    AudioSessionState State, int Volume, bool Muted, bool IsSystemSounds);

/// <summary>PipeWire/PulseAudio implementation used by the shared mixer logic.</summary>
internal sealed class CoreAudioService
{
    private readonly object _cacheGate = new();
    private IReadOnlyList<AudioSessionSnapshot> _cachedSessions =
        Array.Empty<AudioSessionSnapshot>();
    private long _cacheTick;

    public EndpointSnapshot GetDefaultEndpoint()
    {
        var inspect = ProcessRunner.Run("wpctl", new[] { "inspect", "@DEFAULT_AUDIO_SINK@" });
        var id = Regex.Match(inspect.Output, @"(?m)^id\s+(\d+)").Groups[1].Value;
        var name = Regex.Match(inspect.Output,
            "node\\.description\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
        if (id.Length == 0) id = "@DEFAULT_AUDIO_SINK@";
        if (name.Length == 0) name = "DEFAULT OUTPUT";
        return new(id, name);
    }

    public IReadOnlyList<EndpointSnapshot> GetRenderEndpoints()
    {
        var status = ProcessRunner.Run("wpctl", new[] { "status", "-n" });
        var result = new List<EndpointSnapshot>();
        var inSinks = false;
        foreach (var raw in status.Output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Contains("Sinks:", StringComparison.OrdinalIgnoreCase))
            { inSinks = true; continue; }
            if (inSinks && (line.Contains("Sources:", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Filters:", StringComparison.OrdinalIgnoreCase))) break;
            if (!inSinks) continue;
            var match = Regex.Match(line, @"^[^0-9]*(\d+)\.\s+(.+?)(?:\s+\[vol:|$)");
            if (match.Success) result.Add(new(match.Groups[1].Value,
                match.Groups[2].Value.Trim()));
        }
        if (result.Count == 0) result.Add(GetDefaultEndpoint());
        return result;
    }

    public EndpointSnapshot? FindEndpoint(params string[] fragments) =>
        GetRenderEndpoints().FirstOrDefault(endpoint => fragments.Any(fragment =>
            endpoint.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

    public AudioValue GetDefaultVolume() => GetEndpointVolume("@DEFAULT_AUDIO_SINK@");

    public AudioValue GetEndpointVolume(string endpointId)
    {
        var result = ProcessRunner.Run("wpctl", new[] { "get-volume", endpointId });
        if (!result.Success) throw new IOException(result.Error.Trim());
        var match = Regex.Match(result.Output, @"Volume:\s*([0-9.]+)");
        var scalar = match.Success
            ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return new((int)Math.Clamp(Math.Round(scalar * 100), 0, 100),
            result.Output.Contains("MUTED", StringComparison.OrdinalIgnoreCase));
    }

    public void AdjustDefaultVolume(int deltaPercent) =>
        AdjustEndpointVolume("@DEFAULT_AUDIO_SINK@", deltaPercent);

    public void AdjustEndpointVolume(string endpointId, int deltaPercent)
    {
        var current = GetEndpointVolume(endpointId).Volume;
        var changed = Math.Clamp(current + deltaPercent, 0, 100);
        RunRequired("wpctl", "set-volume", endpointId, $"{changed}%");
    }

    public void ToggleDefaultMute() => ToggleEndpointMute("@DEFAULT_AUDIO_SINK@");
    public void ToggleEndpointMute(string endpointId) =>
        RunRequired("wpctl", "set-mute", endpointId, "toggle");

    public IReadOnlyList<AudioSessionSnapshot> GetSessions()
    {
        lock (_cacheGate)
        {
            if (Environment.TickCount64 - _cacheTick < 180)
                return _cachedSessions;
        }
        var result = ProcessRunner.Run("pactl",
            new[] { "-f", "json", "list", "sink-inputs" }, 3500);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return Array.Empty<AudioSessionSnapshot>();
        try
        {
            using var json = JsonDocument.Parse(result.Output);
            var sessions = new List<AudioSessionSnapshot>();
            foreach (var item in json.RootElement.EnumerateArray())
            {
                var index = item.TryGetProperty("index", out var indexNode)
                    ? indexNode.GetUInt32() : 0;
                var props = item.TryGetProperty("properties", out var propertyNode)
                    ? propertyNode : default;
                var binary = Property(props, "application.process.binary");
                var appName = Property(props, "application.name");
                var mediaName = Property(props, "media.name");
                var pidText = Property(props, "application.process.id");
                _ = uint.TryParse(pidText, out var pid);
                var process = ConfigLoader.NormalizeProcessName(
                    binary.Length > 0 ? binary : appName);
                if (process.Length == 0) process = $"stream_{index}";
                var volume = ReadVolume(item);
                var muted = item.TryGetProperty("mute", out var muteNode) &&
                            muteNode.ValueKind == JsonValueKind.True;
                var corked = item.TryGetProperty("corked", out var corkNode) &&
                             corkNode.ValueKind == JsonValueKind.True;
                sessions.Add(new(index.ToString(CultureInfo.InvariantCulture),
                    "PIPEWIRE", pid, process,
                    appName.Length > 0 ? appName : mediaName,
                    corked ? AudioSessionState.Inactive : AudioSessionState.Active,
                    volume, muted, false));
            }
            lock (_cacheGate)
            {
                _cachedSessions = sessions;
                _cacheTick = Environment.TickCount64;
            }
            return sessions;
        }
        catch (Exception ex)
        {
            Log.Debug($"Cannot parse pactl streams: {ex.Message}");
            return Array.Empty<AudioSessionSnapshot>();
        }
    }

    public IReadOnlyList<AudioSessionSnapshot> FindSessions(IEnumerable<string> appNames)
    {
        var wanted = new HashSet<string>(appNames.Select(ConfigLoader.NormalizeProcessName),
            StringComparer.OrdinalIgnoreCase);
        return GetSessions().Where(session => wanted.Contains(session.ProcessName) ||
            wanted.Any(name => session.ProcessName.Contains(name,
                StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public AudioValue? GetSessionVolume(IEnumerable<string> appNames)
    {
        var sessions = FindSessions(appNames);
        return sessions.Count == 0 ? null : new(
            (int)Math.Round(sessions.Average(s => s.Volume)),
            sessions.All(s => s.Muted));
    }

    public AudioSessionSnapshot? GetPreferredSession(IEnumerable<string> names)
    {
        var sessions = FindSessions(names);
        var foreground = GetForegroundProcessName();
        return sessions.FirstOrDefault(s => s.State == AudioSessionState.Active &&
            s.ProcessName.Equals(foreground, StringComparison.OrdinalIgnoreCase)) ??
            sessions.FirstOrDefault(s => s.State == AudioSessionState.Active) ??
            sessions.FirstOrDefault();
    }

    public AudioSessionSnapshot? GetActiveSession() =>
        GetSessions().FirstOrDefault(s => s.State == AudioSessionState.Active);

    public void AdjustSessionVolume(IEnumerable<string> appNames, int deltaPercent)
    {
        foreach (var session in FindSessions(appNames))
        {
            var changed = Math.Clamp(session.Volume + deltaPercent, 0, 100);
            RunRequired("pactl", "set-sink-input-volume", session.EndpointId,
                $"{changed}%");
        }
        InvalidateSessions();
    }

    public void ToggleSessionMute(IEnumerable<string> appNames)
    {
        foreach (var session in FindSessions(appNames))
            RunRequired("pactl", "set-sink-input-mute", session.EndpointId, "toggle");
        InvalidateSessions();
    }

    public string? GetForegroundProcessName()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
                "wayland", StringComparison.OrdinalIgnoreCase) &&
            ProcessRunner.Exists("xdotool"))
        {
            var result = ProcessRunner.Run("sh", new[] { "-lc",
                "xdotool getactivewindow getwindowpid 2>/dev/null" });
            if (int.TryParse(result.Output.Trim(), out var pid))
            {
                try { return ConfigLoader.NormalizeProcessName(
                    Process.GetProcessById(pid).ProcessName); } catch { }
            }
        }
        if (ProcessRunner.Exists("hyprctl"))
        {
            var result = ProcessRunner.Run("hyprctl", new[] { "activewindow", "-j" });
            try
            {
                using var json = JsonDocument.Parse(result.Output);
                if (json.RootElement.TryGetProperty("class", out var cls))
                    return ConfigLoader.NormalizeProcessName(cls.GetString() ?? string.Empty);
            }
            catch { }
        }
        return GetActiveSession()?.ProcessName;
    }

    private static string Property(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object ||
            !properties.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty :
            value.ToString();
    }

    private static int ReadVolume(JsonElement item)
    {
        if (!item.TryGetProperty("volume", out var channels) ||
            channels.ValueKind != JsonValueKind.Object) return 0;
        foreach (var channel in channels.EnumerateObject())
        {
            if (!channel.Value.TryGetProperty("value_percent", out var percent)) continue;
            var text = percent.GetString() ?? percent.ToString();
            var match = Regex.Match(text, @"(\d+)");
            if (match.Success) return Math.Clamp(int.Parse(match.Groups[1].Value), 0, 100);
        }
        return 0;
    }

    private static void RunRequired(string file, params string[] arguments)
    {
        var result = ProcessRunner.Run(file, arguments);
        if (!result.Success) throw new IOException(
            string.IsNullOrWhiteSpace(result.Error) ? $"{file} failed" : result.Error.Trim());
    }

    private void InvalidateSessions()
    {
        lock (_cacheGate) _cacheTick = 0;
    }
}

internal sealed class DefaultAudioDeviceSwitcher
{
    public bool TrySetDefault(string endpointId) =>
        ProcessRunner.Run("wpctl", new[] { "set-default", endpointId }).Success;
}

internal sealed class VoicemeeterService : IDisposable
{
    public void Poll() { }
    public bool TryGet(string parameter, out float value) { value = 0; return false; }
    public bool TrySet(string parameter, float value) => false;
    public void Dispose() { }
}

internal static class WindowsActions
{
    public static void MediaPlayPause() => MprisService.Current?.Command("play-pause");
    public static void MediaNext() => MprisService.Current?.Command("next");
    public static void MediaPrevious() => MprisService.Current?.Command("previous");
}
