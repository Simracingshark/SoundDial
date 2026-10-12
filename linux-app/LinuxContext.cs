using System.Text.Json;

namespace AudioPuck;

internal sealed class LinuxContext : IDisposable
{
    private readonly object _gate = new();
    private readonly string _configPath;
    private readonly string _selectionPath;
    private readonly TransportHub _hub = new();
    private AppConfig _config;
    private TargetController _targets;
    private SavedSelectionState _saved;
    private MprisService _mpris;
    private SerialTransport? _serial;
    private NetworkTransport? _network;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _pollTask;
    private DateTime _configWriteTime;
    private string _lastState = string.Empty;
    private string _lastMediaState = string.Empty;
    private string _lastTimeline = string.Empty;
    private string _lastTrack = string.Empty;
    private DateTime _lastFullPush;
    private bool _disposed;

    public LinuxContext(string configPath)
    {
        _configPath = Path.GetFullPath(configPath);
        var stateDirectory = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "sounddial");
        Directory.CreateDirectory(stateDirectory);
        _selectionPath = Path.Combine(stateDirectory, "selection-state.json");
        _config = ConfigLoader.Load(_configPath);
        Log.Initialize(Path.Combine(stateDirectory, "sounddial.log"),
            _config.General.LogLevel);
        Log.LineWritten += Console.WriteLine;
        Log.Info($"SoundDial Linux starting with {_configPath}");
        _saved = LoadSelections();
        _targets = new TargetController(_config);
        _targets.RestoreSelections(_saved);
        _targets.SelectionChanged += SelectionChanged;
        _mpris = new MprisService(_config.Media.SeekApps);
        _hub.EventReceived += HandleEvent;
        _hub.ConnectionChanged += ConnectionChanged;
        _serial = new SerialTransport(_hub, _config.Transport);
        if (_config.Transport.WifiEnabled)
            _network = CreateNetwork(_config.Transport);
        _configWriteTime = File.GetLastWriteTimeUtc(_configPath);
        _pollTask = Task.Run(PollLoop);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(_stop.Cancel);
        try { await Task.Delay(Timeout.Infinite, _stop.Token); }
        catch (OperationCanceledException) { }
    }

    private async Task PollLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                lock (_gate)
                {
                    ReloadIfChanged();
                    _targets.EvaluateAutomaticProfile();
                    PushStates();
                    PushTimeline();
                    PushTrack();
                }
            }
            catch (Exception ex) { Log.Error("Polling error", ex); }
            try { await Task.Delay(Math.Max(100, _config.General.PollIntervalMs),
                _stop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void PushStates()
    {
        var state = _targets.ReadCurrentState();
        var line = PuckProtocol.State(state, _targets.ProfileIndex,
            _targets.TargetIndex, _hub.ActiveKind,
            _config.General.TransliterateOled, _targets.MainStepOverride);
        var force = DateTime.UtcNow - _lastFullPush > TimeSpan.FromSeconds(2);
        if (line != _lastState || force) { _hub.Send(line); _lastState = line; }

        var media = _targets.ReadMediaState();
        var mediaLine = PuckProtocol.MediaState(media,
            _targets.MediaTargetIndex, _targets.MediaLinkedMask,
            _config.General.TransliterateOled, _targets.MediaStepOverride);
        if (mediaLine != _lastMediaState || force)
        { _hub.Send(mediaLine); _lastMediaState = mediaLine; }
        if (force) _lastFullPush = DateTime.UtcNow;
    }

    private void PushTimeline()
    {
        var timeline = _mpris.GetTimelineAsync().GetAwaiter().GetResult();
        var line = PuckProtocol.MediaTimeline(timeline,
            _config.General.TransliterateOled);
        if (line == _lastTimeline) return;
        _lastTimeline = line; _hub.Send(line);
    }

    private void PushTrack()
    {
        var track = _mpris.ReadTrack();
        if (track is null) return;
        var identity = $"{track.Source}\n{track.Title}\n{track.Artist}";
        if (identity == _lastTrack) return;
        _lastTrack = identity;
        _hub.Send(PuckProtocol.Track(track, _config.General.TransliterateOled));
    }

    private void HandleEvent(PuckEvent value)
    {
        lock (_gate)
        {
            switch (value.Kind)
            {
                case PuckEventKind.Rotate: _targets.Adjust(value.Value, value.Step); break;
                case PuckEventKind.Click: _targets.Click(); break;
                case PuckEventKind.DoubleClick: _targets.DoubleClick(); break;
                case PuckEventKind.TripleClick: _targets.TripleClick(); break;
                case PuckEventKind.MediaRotate: _targets.AdjustMedia(value.Value, value.Step); break;
                case PuckEventKind.MediaSelect: _targets.SelectMediaTarget(value.Value); break;
                case PuckEventKind.MediaMute: _targets.ToggleMediaMute(); break;
                case PuckEventKind.MediaPlayPause: _targets.MediaPlayPause(); break;
                case PuckEventKind.MediaNext: _targets.MediaNext(); break;
                case PuckEventKind.MediaPrevious: _targets.MediaPrevious(); break;
                case PuckEventKind.MediaSeek:
                    var sought = _mpris.SeekAsync(value.Value).GetAwaiter().GetResult();
                    _lastTimeline = string.Empty;
                    _hub.Send(PuckProtocol.MediaTimeline(sought,
                        _config.General.TransliterateOled));
                    break;
                case PuckEventKind.RequestMediaTimeline: _lastTimeline = string.Empty; PushTimeline(); break;
                case PuckEventKind.SelectTarget: _targets.SelectTarget(value.Value); break;
                case PuckEventKind.LinkMediaTargets: _targets.SetLinkedMediaTargets(value.Value); break;
                case PuckEventKind.SelectProfile: _targets.SelectProfile(value.Value); break;
                case PuckEventKind.Hello:
                case PuckEventKind.RequestState: PushCatalog(); break;
                case PuckEventKind.DeviceSettings:
                    if (value.DeviceSettings is { } settings)
                        _targets.AutomaticProfilesEnabled = settings.AutomaticProfiles;
                    break;
            }
            _lastState = string.Empty; _lastMediaState = string.Empty;
            PushStates();
        }
    }

    private void PushCatalog()
    {
        _hub.Send(PuckProtocol.Profiles(_targets.GetProfileNames(),
            _config.General.TransliterateOled));
        _hub.Send(PuckProtocol.Targets(_targets.GetTargetNames(),
            _config.General.TransliterateOled));
        _hub.Send(PuckProtocol.MediaTargets(_targets.GetMediaTargetNames(),
            _config.General.TransliterateOled));
        _hub.Send(PuckProtocol.BatteryCalibration(_config.Battery));
        _lastState = _lastMediaState = _lastTimeline = string.Empty;
    }

    private void ConnectionChanged()
    {
        lock (_gate)
        {
            PushCatalog();
            if (_hub.ActiveKind != ConnectionKind.None) _hub.Send("GET_DEVICE_CONFIG");
        }
    }

    private void SelectionChanged()
    {
        SaveSelections(); PushCatalog();
    }

    private void ReloadIfChanged()
    {
        var write = File.GetLastWriteTimeUtc(_configPath);
        if (write == _configWriteTime) return;
        Thread.Sleep(120);
        var updated = ConfigLoader.Load(_configPath);
        var transportChanged = TransportChanged(_config.Transport, updated.Transport);
        _config = updated; _configWriteTime = write;
        _targets.ReplaceConfig(updated);
        _mpris.SetSeekApps(updated.Media.SeekApps);
        if (transportChanged)
        {
            _serial?.Dispose(); _network?.Dispose();
            _serial = new SerialTransport(_hub, updated.Transport);
            _network = updated.Transport.WifiEnabled ? CreateNetwork(updated.Transport) : null;
        }
        Log.Info("config.toml reloaded");
        _hub.Send(PuckProtocol.Message("CONFIG RELOADED", true));
        PushCatalog();
    }

    private NetworkTransport? CreateNetwork(TransportConfig config)
    {
        try { return new NetworkTransport(_hub, config); }
        catch (Exception ex) { Log.Error("WiFi transport could not start", ex); return null; }
    }

    private SavedSelectionState LoadSelections()
    {
        try
        {
            if (!File.Exists(_selectionPath)) return new();
            return JsonSerializer.Deserialize<SavedSelectionState>(
                File.ReadAllText(_selectionPath)) ?? new();
        }
        catch { return new(); }
    }

    private void SaveSelections()
    {
        try
        {
            _targets.CaptureSelections(_saved);
            File.WriteAllText(_selectionPath, JsonSerializer.Serialize(_saved,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log.Warn($"Cannot save selection: {ex.Message}"); }
    }

    private static bool TransportChanged(TransportConfig a, TransportConfig b) =>
        a.SerialPort != b.SerialPort || a.SerialBaud != b.SerialBaud ||
        a.WifiEnabled != b.WifiEnabled || a.DiscoveryPort != b.DiscoveryPort ||
        a.TcpPort != b.TcpPort || a.PairingToken != b.PairingToken;

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _stop.Cancel();
        try { _pollTask.Wait(1500); } catch { }
        SaveSelections(); _targets.SelectionChanged -= SelectionChanged;
        _hub.EventReceived -= HandleEvent; _hub.ConnectionChanged -= ConnectionChanged;
        _serial?.Dispose(); _network?.Dispose(); _hub.Dispose(); _targets.Dispose();
        Log.LineWritten -= Console.WriteLine; _stop.Dispose();
    }
}
