using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AudioPuck;

internal sealed class CoreAudioService
{
    private static readonly Guid EventContext =
        new("905A0AE7-7D04-4C0E-9A3B-37F40EBA1391");
    private readonly object _sessionCacheGate = new();
    private IReadOnlyList<AudioSessionSnapshot> _sessionCache =
        Array.Empty<AudioSessionSnapshot>();
    private long _sessionCacheTick;
    private const int SessionCacheMs = 120;

    public EndpointSnapshot GetDefaultEndpoint()
    {
        var enumerator = CreateEnumerator();
        IMmDevice? device = null;
        try
        {
            ThrowIfFailed(enumerator.GetDefaultAudioEndpoint(
                DataFlow.Render, Role.Multimedia, out device));
            return ReadEndpoint(device);
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    public IReadOnlyList<EndpointSnapshot> GetRenderEndpoints()
    {
        var result = new List<EndpointSnapshot>();
        var enumerator = CreateEnumerator();
        IMmDeviceCollection? collection = null;
        try
        {
            ThrowIfFailed(enumerator.EnumAudioEndpoints(DataFlow.Render,
                (uint)CoreAudioInterop.DeviceStateActive, out collection));
            ThrowIfFailed(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                ThrowIfFailed(collection.Item(i, out var device));
                try
                {
                    result.Add(ReadEndpoint(device));
                }
                finally
                {
                    Release(device);
                }
            }
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }
        return result;
    }

    public EndpointSnapshot? FindEndpoint(params string[] nameFragments)
    {
        var fragments = nameFragments.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).ToArray();
        return GetRenderEndpoints().FirstOrDefault(endpoint => fragments.Any(fragment =>
            endpoint.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
    }

    public AudioValue GetDefaultVolume() => GetEndpointVolume(GetDefaultEndpoint().Id);

    public AudioValue GetEndpointVolume(string endpointId)
    {
        return WithEndpointVolume(endpointId, volume =>
        {
            ThrowIfFailed(volume.GetMasterVolumeLevelScalar(out var scalar));
            ThrowIfFailed(volume.GetMute(out var muted));
            return new AudioValue(ToPercent(scalar), muted);
        });
    }

    public void AdjustDefaultVolume(int deltaPercent)
    {
        var endpoint = GetDefaultEndpoint();
        AdjustEndpointVolume(endpoint.Id, deltaPercent);
    }

    public void AdjustEndpointVolume(string endpointId, int deltaPercent)
    {
        WithEndpointVolume(endpointId, volume =>
        {
            ThrowIfFailed(volume.GetMasterVolumeLevelScalar(out var current));
            var changed = Math.Clamp(current + deltaPercent / 100.0f, 0.0f, 1.0f);
            var context = EventContext;
            ThrowIfFailed(volume.SetMasterVolumeLevelScalar(changed, ref context));
            return 0;
        });
    }

    public void ToggleDefaultMute()
    {
        var endpoint = GetDefaultEndpoint();
        ToggleEndpointMute(endpoint.Id);
    }

    public void ToggleEndpointMute(string endpointId)
    {
        WithEndpointVolume(endpointId, volume =>
        {
            ThrowIfFailed(volume.GetMute(out var muted));
            var context = EventContext;
            ThrowIfFailed(volume.SetMute(!muted, ref context));
            return 0;
        });
    }

    public IReadOnlyList<AudioSessionSnapshot> GetSessions()
    {
        lock (_sessionCacheGate)
        {
            var now = Environment.TickCount64;
            if (_sessionCacheTick != 0 && now - _sessionCacheTick < SessionCacheMs)
                return _sessionCache;

            var sessions = new List<AudioSessionSnapshot>();
            foreach (var endpoint in GetRenderEndpoints())
                sessions.AddRange(GetSessionsForEndpoint(endpoint));
            _sessionCache = sessions;
            _sessionCacheTick = now;
            return _sessionCache;
        }
    }

    public IReadOnlyList<AudioSessionSnapshot> FindSessions(IEnumerable<string> appNames)
    {
        var wanted = new HashSet<string>(appNames.Select(ConfigLoader.NormalizeProcessName),
            StringComparer.OrdinalIgnoreCase);
        return GetSessions().Where(session => wanted.Contains(session.ProcessName)).ToList();
    }

    public AudioValue? GetSessionVolume(IEnumerable<string> appNames)
    {
        var sessions = FindSessions(appNames);
        if (sessions.Count == 0) return null;
        var audible = sessions.Where(s => s.State != AudioSessionState.Expired).ToList();
        if (audible.Count == 0) audible = sessions.ToList();
        return new AudioValue(
            (int)Math.Round(audible.Average(s => s.Volume)),
            audible.All(s => s.Muted));
    }

    public AudioSessionSnapshot? GetPreferredSession(IEnumerable<string> appNames)
    {
        var sessions = FindSessions(appNames);
        if (sessions.Count == 0) return null;

        var foreground = GetForegroundProcessName();
        return sessions.FirstOrDefault(session =>
                   session.State == AudioSessionState.Active &&
                   session.ProcessName.Equals(foreground,
                       StringComparison.OrdinalIgnoreCase))
               ?? sessions.FirstOrDefault(session =>
                   session.State == AudioSessionState.Active)
               ?? sessions.FirstOrDefault(session =>
                   session.State != AudioSessionState.Expired)
               ?? sessions[0];
    }

    public void AdjustSessionVolume(IEnumerable<string> appNames, int deltaPercent)
    {
        var wanted = new HashSet<string>(appNames.Select(ConfigLoader.NormalizeProcessName),
            StringComparer.OrdinalIgnoreCase);
        VisitSessions((session, volume) =>
        {
            if (!wanted.Contains(session.ProcessName)) return;
            ThrowIfFailed(volume.GetMasterVolume(out var current));
            var changed = Math.Clamp(current + deltaPercent / 100.0f, 0.0f, 1.0f);
            var context = EventContext;
            ThrowIfFailed(volume.SetMasterVolume(changed, ref context));
        });
        InvalidateSessionCache();
    }

    public void ToggleSessionMute(IEnumerable<string> appNames)
    {
        var wanted = new HashSet<string>(appNames.Select(ConfigLoader.NormalizeProcessName),
            StringComparer.OrdinalIgnoreCase);
        var matching = GetSessions().Where(s => wanted.Contains(s.ProcessName)).ToList();
        var newMute = !matching.All(s => s.Muted);
        VisitSessions((session, volume) =>
        {
            if (!wanted.Contains(session.ProcessName)) return;
            var context = EventContext;
            ThrowIfFailed(volume.SetMute(newMute, ref context));
        });
        InvalidateSessionCache();
    }

    public string? GetForegroundProcessName()
    {
        var window = CoreAudioInterop.GetForegroundWindow();
        if (window == IntPtr.Zero) return null;
        CoreAudioInterop.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return null;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return ConfigLoader.NormalizeProcessName(process.ProcessName);
        }
        catch
        {
            return null;
        }
    }

    public AudioSessionSnapshot? GetActiveSession()
    {
        var foreground = GetForegroundProcessName();
        var sessions = GetSessions();
        var foregroundSession = sessions.FirstOrDefault(s =>
            s.State == AudioSessionState.Active &&
            s.ProcessName.Equals(foreground, StringComparison.OrdinalIgnoreCase));
        return foregroundSession ?? sessions.FirstOrDefault(s =>
            s.State == AudioSessionState.Active && !s.IsSystemSounds);
    }

    private IReadOnlyList<AudioSessionSnapshot> GetSessionsForEndpoint(
        EndpointSnapshot endpoint)
    {
        var result = new List<AudioSessionSnapshot>();
        var enumerator = CreateEnumerator();
        IMmDevice? device = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessionEnumerator = null;
        try
        {
            ThrowIfFailed(enumerator.GetDevice(endpoint.Id, out device));
            var iid = CoreAudioInterop.IidAudioSessionManager2;
            ThrowIfFailed(device.Activate(ref iid, ClsCtx.All, IntPtr.Zero,
                out var managerObject));
            manager = (IAudioSessionManager2)managerObject;
            ThrowIfFailed(manager.GetSessionEnumerator(out sessionEnumerator));
            ThrowIfFailed(sessionEnumerator.GetCount(out var count));
            for (var i = 0; i < count; i++)
            {
                IAudioSessionControl? control = null;
                try
                {
                    ThrowIfFailed(sessionEnumerator.GetSession(i, out control));
                    var control2 = (IAudioSessionControl2)control;
                    var volume = (ISimpleAudioVolume)control;
                    ThrowIfFailed(control2.GetState(out var state));
                    ThrowIfFailed(control2.GetProcessId(out var processId));
                    ThrowIfFailed(control2.GetDisplayName(out var displayName));
                    ThrowIfFailed(volume.GetMasterVolume(out var scalar));
                    ThrowIfFailed(volume.GetMute(out var muted));
                    // Some third-party virtual endpoints return S_OK from
                    // IsSystemSoundsSession for ordinary sessions. PID 0 is the
                    // documented, stable identifier for the Windows system
                    // sounds session and does not misclassify Spotify/Sonar.
                    var system = processId == 0;
                    var processName = system ? "system_sounds" : ProcessName(processId);
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = system ? "System Sounds" : processName;
                    }
                    result.Add(new AudioSessionSnapshot(endpoint.Id, endpoint.Name,
                        processId, processName, displayName, state,
                        ToPercent(scalar), muted, system));
                }
                catch (Exception ex)
                {
                    Log.Debug($"Skipping audio session {i} on {endpoint.Name}: {ex.Message}");
                }
                finally
                {
                    Release(control);
                }
            }
        }
        finally
        {
            Release(sessionEnumerator);
            Release(manager);
            Release(device);
            Release(enumerator);
        }
        return result;
    }

    private void VisitSessions(Action<AudioSessionSnapshot, ISimpleAudioVolume> visitor)
    {
        foreach (var endpoint in GetRenderEndpoints())
        {
            var enumerator = CreateEnumerator();
            IMmDevice? device = null;
            IAudioSessionManager2? manager = null;
            IAudioSessionEnumerator? sessions = null;
            try
            {
                ThrowIfFailed(enumerator.GetDevice(endpoint.Id, out device));
                var iid = CoreAudioInterop.IidAudioSessionManager2;
                ThrowIfFailed(device.Activate(ref iid, ClsCtx.All, IntPtr.Zero,
                    out var managerObject));
                manager = (IAudioSessionManager2)managerObject;
                ThrowIfFailed(manager.GetSessionEnumerator(out sessions));
                ThrowIfFailed(sessions.GetCount(out var count));
                for (var i = 0; i < count; i++)
                {
                    IAudioSessionControl? control = null;
                    try
                    {
                        ThrowIfFailed(sessions.GetSession(i, out control));
                        var control2 = (IAudioSessionControl2)control;
                        var volume = (ISimpleAudioVolume)control;
                        ThrowIfFailed(control2.GetState(out var state));
                        ThrowIfFailed(control2.GetProcessId(out var processId));
                        ThrowIfFailed(control2.GetDisplayName(out var displayName));
                        ThrowIfFailed(volume.GetMasterVolume(out var scalar));
                        ThrowIfFailed(volume.GetMute(out var muted));
                        var system = processId == 0;
                        var snapshot = new AudioSessionSnapshot(endpoint.Id,
                            endpoint.Name, processId,
                            system ? "system_sounds" : ProcessName(processId),
                            displayName, state, ToPercent(scalar), muted, system);
                        visitor(snapshot, volume);
                    }
                    finally
                    {
                        Release(control);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Session visit failed for {endpoint.Name}: {ex.Message}");
            }
            finally
            {
                Release(sessions);
                Release(manager);
                Release(device);
                Release(enumerator);
            }
        }
    }

    private static EndpointSnapshot ReadEndpoint(IMmDevice device)
    {
        ThrowIfFailed(device.GetId(out var id));
        IPropertyStore? store = null;
        var value = new PropVariant();
        try
        {
            ThrowIfFailed(device.OpenPropertyStore(CoreAudioInterop.StgmRead, out store));
            var key = CoreAudioInterop.PkeyDeviceFriendlyName;
            ThrowIfFailed(store.GetValue(ref key, out value));
            return new EndpointSnapshot(id, value.GetString());
        }
        finally
        {
            CoreAudioInterop.PropVariantClear(ref value);
            Release(store);
        }
    }

    private static IMmDeviceEnumerator CreateEnumerator() =>
        (IMmDeviceEnumerator)new MmDeviceEnumeratorComObject();

    private static string ProcessName(uint processId)
    {
        if (processId == 0) return "unknown";
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return ConfigLoader.NormalizeProcessName(process.ProcessName);
        }
        catch
        {
            return $"pid_{processId}";
        }
    }

    private static T WithEndpointVolume<T>(string endpointId,
        Func<IAudioEndpointVolume, T> action)
    {
        var enumerator = CreateEnumerator();
        IMmDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            ThrowIfFailed(enumerator.GetDevice(endpointId, out device));
            var iid = CoreAudioInterop.IidAudioEndpointVolume;
            ThrowIfFailed(device.Activate(ref iid, ClsCtx.All, IntPtr.Zero,
                out var volumeObject));
            volume = (IAudioEndpointVolume)volumeObject;
            return action(volume);
        }
        finally
        {
            Release(volume);
            Release(device);
            Release(enumerator);
        }
    }

    private static int ToPercent(float scalar) =>
        Math.Clamp((int)Math.Round(scalar * 100.0f), 0, 100);

    private void InvalidateSessionCache()
    {
        lock (_sessionCacheGate) _sessionCacheTick = 0;
    }

    internal static void ThrowIfFailed(int hresult)
    {
        if (hresult < 0) Marshal.ThrowExceptionForHR(hresult);
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            // FinalReleaseComObject can invalidate a shared RCW which another
            // interface cast or callback still uses. Release only the COM
            // reference acquired by this method.
            try { Marshal.ReleaseComObject(comObject); } catch { }
        }
    }
}

internal sealed record EndpointSnapshot(string Id, string Name);

internal sealed record AudioValue(int Volume, bool Muted);

internal sealed record AudioSessionSnapshot(
    string EndpointId,
    string EndpointName,
    uint ProcessId,
    string ProcessName,
    string DisplayName,
    AudioSessionState State,
    int Volume,
    bool Muted,
    bool IsSystemSounds);
