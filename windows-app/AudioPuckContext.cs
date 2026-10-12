using System.Diagnostics;
using System.Text.Json;

namespace AudioPuck;

internal sealed class AudioPuckContext : ApplicationContext
{
    private readonly string _configPath;
    private readonly string _selectionPath;
    private readonly object _gate = new();
    private readonly Icon _appIcon;
    private readonly NotifyIcon _tray;
    private readonly System.Threading.Timer _pollTimer;
    private readonly System.Threading.Timer _reloadTimer;
    private readonly System.Threading.Timer _rotationTimer;
    private readonly FileSystemWatcher _watcher;
    private AppConfig _config;
    private TargetController _targets;
    private readonly SavedSelectionState _savedSelection;
    private readonly TransportHub _hub = new();
    private readonly MediaSessionService _mediaSessions;
    private SerialTransport? _serial;
    private NetworkTransport? _network;
    private string _lastState = string.Empty;
    private string _lastMediaState = string.Empty;
    private string _lastHostStatus = string.Empty;
    private DateTime _lastFullPush;
    private DateTime _lastHostStatusPush;
    private DateTime _lastHostWarning;
    private int _hostWarningLevel;
    private DeviceSettingsForm? _settingsForm;
    private int _settingsRequestVersion;
    private bool _deviceSettingsUiPending;
    private DateTime? _configErrorSince;
    private bool _configErrorShown;
    private bool _disposed;
    private int _pendingMainDetents;
    private int _pendingMediaDetents;
    private int _pendingMainStep;
    private int _pendingMediaStep;
    private int _rotationTimerArmed;
    private int _polling;

    public AudioPuckContext(string configPath)
    {
        _configPath = configPath;
        var dataDirectory = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "SoundDial");
        Directory.CreateDirectory(dataDirectory);
        _selectionPath = Path.Combine(dataDirectory, "selection-state.json");
        Log.Initialize(Path.Combine(dataDirectory, "SoundDial.log"), "info");
        _config = ConfigLoader.Load(configPath);
        _mediaSessions = new MediaSessionService(_config.Media.SeekApps);
        Log.Initialize(Path.Combine(dataDirectory, "SoundDial.log"),
            _config.General.LogLevel);
        Log.Info($"SoundDial starting with config {configPath}");

        _savedSelection = LoadSelections(_selectionPath);
        _targets = new TargetController(_config);
        _targets.RestoreSelections(_savedSelection);
        _targets.SelectionChanged += HandleSelectionChanged;
        _hub.EventReceived += HandlePuckEvent;
        _hub.ConnectionChanged += HandleConnectionChanged;
        _mediaSessions.TrackChanged += track =>
            _hub.Send(PuckProtocol.Track(track,
                _config.General.TransliterateOled));
        _mediaSessions.TimelineChanged += PushMediaTimeline;
        _mediaSessions.Start();

        _appIcon = LoadApplicationIcon();
        _tray = CreateTrayIcon();
        _serial = new SerialTransport(_hub, _config.Transport);
        if (_config.Transport.WifiEnabled)
        {
            try { _network = new NetworkTransport(_hub, _config.Transport); }
            catch (Exception ex)
            {
                Log.Error("WiFi transport could not start", ex);
                ShowBalloon("WiFi transport", ex.Message, ToolTipIcon.Warning);
            }
        }

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(configPath)!,
            Path.GetFileName(configPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                           NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => ScheduleReload();
        _watcher.Created += (_, _) => ScheduleReload();
        _watcher.Renamed += (_, _) => ScheduleReload();

        _reloadTimer = new System.Threading.Timer(_ => ReloadConfig(false), null,
            Timeout.Infinite, Timeout.Infinite);
        _rotationTimer = new System.Threading.Timer(ProcessPendingRotations,
            null, Timeout.Infinite, Timeout.Infinite);
        _pollTimer = new System.Threading.Timer(_ => Poll(), null, 100,
            _config.General.PollIntervalMs);
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Device settings", null, (_, _) => OpenDeviceSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open config.toml", null, (_, _) => OpenConfig());
        menu.Items.Add("Reload config", null, (_, _) => ReloadConfig(true));
        menu.Items.Add("Diagnostics", null, (_, _) => ShowDiagnostics());
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => RequestExit());

        var tray = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "SoundDial — starting",
            Visible = true,
            ContextMenuStrip = menu
        };
        tray.DoubleClick += (_, _) => OpenConfig();
        return tray;
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
                return Icon.ExtractAssociatedIcon(executable) ??
                       (Icon)SystemIcons.Application.Clone();
        }
        catch { }
        return (Icon)SystemIcons.Application.Clone();
    }

    private void Poll()
    {
        // System.Threading.Timer can invoke this callback again while the
        // previous Core Audio scan is still running.  Do not let slow or
        // broken third-party audio endpoints build an unbounded queue of
        // callbacks and starve every other SoundDial command.
        if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0) return;
        try
        {
            lock (_gate)
            {
                try
                {
                    _targets.EvaluateAutomaticProfile();
                    var state = _targets.ReadCurrentState();
                    var line = PuckProtocol.State(state, _targets.ProfileIndex,
                        _targets.TargetIndex, _hub.ActiveKind,
                        _config.General.TransliterateOled,
                        _targets.MainStepOverride);
                    if (line != _lastState ||
                        DateTime.UtcNow - _lastFullPush > TimeSpan.FromSeconds(2))
                    {
                        _hub.Send(line);
                        _lastState = line;
                        _lastFullPush = DateTime.UtcNow;
                        UpdateTray(state);
                    }

                    var media = _targets.ReadMediaState();
                    var mediaLine = PuckProtocol.MediaState(media,
                        _targets.MediaTargetIndex,
                        _targets.MediaLinkedMask,
                        _config.General.TransliterateOled,
                        _targets.MediaStepOverride);
                    if (mediaLine != _lastMediaState ||
                        DateTime.UtcNow - _lastFullPush > TimeSpan.FromSeconds(2))
                    {
                        _hub.Send(mediaLine);
                        _lastMediaState = mediaLine;
                    }

                    PushHostStatus();
                }
                catch (Exception ex)
                {
                    Log.Error("Polling error", ex);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private void HandlePuckEvent(PuckEvent puckEvent)
    {
        // Encoder events can arrive much faster than Windows can enumerate
        // every Core Audio session. Queue and merge them before touching COM;
        // otherwise hundreds of synchronous scans build up and Windows volume
        // keeps changing long after the knob has stopped.
        if (puckEvent.Kind == PuckEventKind.Rotate)
        {
            QueueRotation(media: false, puckEvent.Value, puckEvent.Step);
            return;
        }
        if (puckEvent.Kind == PuckEventKind.MediaRotate)
        {
            QueueRotation(media: true, puckEvent.Value, puckEvent.Step);
            return;
        }
        // Timeline requests and seeking do not touch TargetController state.
        // Keep them outside the Core Audio gate so a slow endpoint scan cannot
        // freeze film controls together with the volume mixer.
        if (puckEvent.Kind == PuckEventKind.RequestMediaTimeline)
        {
            PushMediaTimeline(_mediaSessions.GetTimeline());
            return;
        }
        if (puckEvent.Kind == PuckEventKind.MediaSeek)
        {
            _ = SeekAndPushAsync(puckEvent.Value);
            return;
        }

        lock (_gate)
        {
            var refreshMainImmediately = false;
            var refreshMediaImmediately = false;
            switch (puckEvent.Kind)
            {
                case PuckEventKind.Click:
                    _targets.Click();
                    break;
                case PuckEventKind.DoubleClick:
                    _targets.DoubleClick();
                    break;
                case PuckEventKind.TripleClick:
                    _targets.TripleClick();
                    break;
                case PuckEventKind.MediaSelect:
                    _targets.SelectMediaTarget(puckEvent.Value);
                    break;
                case PuckEventKind.MediaMute:
                    _targets.ToggleMediaMute();
                    refreshMediaImmediately = true;
                    break;
                case PuckEventKind.MediaPlayPause:
                    _targets.MediaPlayPause();
                    break;
                case PuckEventKind.MediaNext:
                    _targets.MediaNext();
                    break;
                case PuckEventKind.MediaPrevious:
                    _targets.MediaPrevious();
                    break;
                case PuckEventKind.SelectTarget:
                    _targets.SelectTarget(puckEvent.Value);
                    break;
                case PuckEventKind.LinkMediaTargets:
                    _targets.SetLinkedMediaTargets(puckEvent.Value);
                    break;
                case PuckEventKind.SelectProfile:
                    _targets.SelectProfile(puckEvent.Value);
                    break;
                case PuckEventKind.Hello:
                case PuckEventKind.RequestState:
                    PushCatalogAndState();
                    break;
                case PuckEventKind.DeviceSettings:
                    if (puckEvent.DeviceSettings is { } deviceSettings)
                    {
                        Interlocked.Increment(ref _settingsRequestVersion);
                        _targets.AutomaticProfilesEnabled =
                            deviceSettings.AutomaticProfiles;
                        if (_deviceSettingsUiPending)
                        {
                            _deviceSettingsUiPending = false;
                            _settingsForm?.ApplySettings(deviceSettings);
                        }
                    }
                    break;
                case PuckEventKind.HostSettings:
                    if (puckEvent.HostSettings is { } hostSettings)
                    {
                        _config.HostStatus.BatteryDisplay =
                            hostSettings.BatteryDisplay;
                        _config.HostStatus.IdleDashboard =
                            hostSettings.IdleDashboard;
                        _config.HostStatus.ShowClock = hostSettings.ShowClock;
                        _config.HostStatus.IdleAfterSeconds =
                            hostSettings.IdleAfterSeconds;
                        try
                        {
                            ConfigLoader.SaveHostDisplaySettings(_configPath,
                                _config.HostStatus);
                            _lastHostStatus = string.Empty;
                            PushHostStatus(true);
                            Log.Info("Host display settings saved from SoundDial");
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Host display settings could not be saved", ex);
                            ShowBalloon("Host settings", ex.Message,
                                ToolTipIcon.Warning);
                        }
                    }
                    break;
                case PuckEventKind.DialBatterySettings:
                    if (puckEvent.DialBatterySettings is { } dialBattery)
                    {
                        _config.Battery.MinVoltage = dialBattery.MinVoltage;
                        _config.Battery.MaxVoltage = dialBattery.MaxVoltage;
                        _config.Battery.Display = dialBattery.Display;
                        try
                        {
                            ConfigLoader.SaveBatterySettings(_configPath,
                                _config.Battery);
                            _hub.Send(PuckProtocol.BatteryCalibration(
                                _config.Battery));
                            Log.Info("Dial battery settings saved from SoundDial");
                        }
                        catch (Exception ex)
                        {
                            Log.Error("Dial battery settings could not be saved", ex);
                            ShowBalloon("Dial battery settings", ex.Message,
                                ToolTipIcon.Warning);
                        }
                    }
                    break;
            }
            _lastState = string.Empty;
            _lastMediaState = string.Empty;
            if (refreshMainImmediately) PushMainStateImmediately();
            if (refreshMediaImmediately) PushMediaStateImmediately();
        }
    }

    private void QueueRotation(bool media, int detents, int step)
    {
        if (detents == 0 || _disposed) return;
        if (media)
        {
            Interlocked.Add(ref _pendingMediaDetents, detents);
            Volatile.Write(ref _pendingMediaStep, step);
        }
        else
        {
            Interlocked.Add(ref _pendingMainDetents, detents);
            Volatile.Write(ref _pendingMainStep, step);
        }

        if (Interlocked.Exchange(ref _rotationTimerArmed, 1) == 0)
            ArmRotationTimer();
    }

    private void ProcessPendingRotations(object? _)
    {
        if (_disposed) return;
        var main = Interlocked.Exchange(ref _pendingMainDetents, 0);
        var media = Interlocked.Exchange(ref _pendingMediaDetents, 0);
        lock (_gate)
        {
            if (main != 0)
            {
                _targets.Adjust(main, Volatile.Read(ref _pendingMainStep));
                _lastState = string.Empty;
            }
            if (media != 0)
            {
                _targets.AdjustMedia(media,
                    Volatile.Read(ref _pendingMediaStep));
                _lastMediaState = string.Empty;
            }
        }

        Interlocked.Exchange(ref _rotationTimerArmed, 0);
        if ((Volatile.Read(ref _pendingMainDetents) != 0 ||
             Volatile.Read(ref _pendingMediaDetents) != 0) &&
            Interlocked.Exchange(ref _rotationTimerArmed, 1) == 0)
            ArmRotationTimer();
    }

    private void ArmRotationTimer()
    {
        try { _rotationTimer.Change(12, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    private async Task SeekAndPushAsync(int seconds)
    {
        var timeline=await _mediaSessions.SeekAsync(seconds);
        if(!_disposed)PushMediaTimeline(timeline);
    }

    private void PushMediaTimeline(MediaTimeline timeline) =>
        _hub.Send(PuckProtocol.MediaTimeline(timeline,
            _config.General.TransliterateOled));

    private void PushHostStatus(bool force = false)
    {
        var power = HostPowerService.Read(_config.HostStatus);
        var signature = string.Join('|',
            _config.HostStatus.BatteryDisplay,
            power.HasBattery, power.Percent, power.PluggedIn,
            _config.HostStatus.IdleDashboard,
            _config.HostStatus.ShowClock,
            _config.HostStatus.IdleAfterSeconds);
        if (force || signature != _lastHostStatus ||
            DateTime.UtcNow - _lastHostStatusPush >= TimeSpan.FromSeconds(15))
        {
            var line = PuckProtocol.HostStatus(_config.HostStatus, power,
                DateTime.Now);
            _hub.Send(line);
            _lastHostStatus = signature;
            _lastHostStatusPush = DateTime.UtcNow;
        }

        EvaluateHostBatteryWarning(power);
    }

    private void EvaluateHostBatteryWarning(HostPowerStatus power)
    {
        var settings = _config.HostStatus;
        if (!settings.LowBatteryWarning || !power.HasBattery ||
            power.PluggedIn || power.Percent > settings.LowBatteryPercent)
        {
            _hostWarningLevel = 0;
            return;
        }

        var level = power.Percent <= settings.CriticalBatteryPercent ? 2 : 1;
        var repeatDue = settings.RepeatWarningMinutes > 0 &&
            DateTime.UtcNow - _lastHostWarning >=
            TimeSpan.FromMinutes(settings.RepeatWarningMinutes);
        if (level <= _hostWarningLevel && !repeatDue) return;

        _hostWarningLevel = level;
        _lastHostWarning = DateTime.UtcNow;
        var text = level == 2
            ? $"LAPTOP CRITICAL {power.Percent}%"
            : $"LAPTOP LOW {power.Percent}%";
        _hub.Send(PuckProtocol.HostAlert(text,
            _config.General.TransliterateOled));
        ShowBalloon("SoundDial battery warning", text,
            level == 2 ? ToolTipIcon.Error : ToolTipIcon.Warning);
    }

    private void PushMainStateImmediately()
    {
        var state = _targets.ReadCurrentState();
        var line = PuckProtocol.State(state, _targets.ProfileIndex,
            _targets.TargetIndex, _hub.ActiveKind,
            _config.General.TransliterateOled,
            _targets.MainStepOverride);
        _hub.Send(line);
        _lastState = line;
        UpdateTray(state);
    }

    private void PushMediaStateImmediately()
    {
        var media = _targets.ReadMediaState();
        var line = PuckProtocol.MediaState(media,
            _targets.MediaTargetIndex,
            _targets.MediaLinkedMask,
            _config.General.TransliterateOled,
            _targets.MediaStepOverride);
        _hub.Send(line);
        _lastMediaState = line;
    }

    private void PushCatalogAndState()
    {
        lock (_gate)
        {
            _hub.Send(PuckProtocol.BatteryCalibration(_config.Battery));
            _hub.Send(PuckProtocol.Profiles(_targets.GetProfileNames(),
                _config.General.TransliterateOled));
            _hub.Send(PuckProtocol.Targets(_targets.GetTargetNames(),
                _config.General.TransliterateOled));
            _hub.Send(PuckProtocol.MediaTargets(_targets.GetMediaTargetNames(),
                _config.General.TransliterateOled));
            PushHostStatus(force: true);
            _lastState = string.Empty;
            _lastMediaState = string.Empty;
        }
    }

    private void HandleSelectionChanged()
    {
        SaveSelections();
        PushCatalogAndState();
    }

    private static SavedSelectionState LoadSelections(string path)
    {
        try
        {
            if (!File.Exists(path)) return new SavedSelectionState();
            var loaded = JsonSerializer.Deserialize<SavedSelectionState>(
                             File.ReadAllText(path)) ??
                         new SavedSelectionState();
            loaded.TargetsByProfile = new Dictionary<string, string>(
                loaded.TargetsByProfile ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
            loaded.MediaTargetId ??= string.Empty;
            loaded.LinkedMediaTargetIds ??= new List<string>();
            return loaded;
        }
        catch (Exception ex)
        {
            Log.Warn($"Saved selection state could not be read: {ex.Message}");
            return new SavedSelectionState();
        }
    }

    private void SaveSelections()
    {
        try
        {
            _targets.CaptureSelections(_savedSelection);
            var temporary = _selectionPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(
                _savedSelection, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            File.Move(temporary, _selectionPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Selection state could not be saved: {ex.Message}");
        }
    }

    private void HandleConnectionChanged()
    {
        if (_disposed) return;
        PushCatalogAndState();
        if (_hub.ActiveKind != ConnectionKind.None)
            _hub.Send("GET_DEVICE_CONFIG");
    }

    private void ScheduleReload()
    {
        if (_disposed) return;
        _configErrorSince = null;
        _configErrorShown = false;
        // Editors with auto-save may briefly write an incomplete TOML line.
        // Wait until typing settles before attempting to parse it.
        _reloadTimer.Change(1200, Timeout.Infinite);
    }

    private void ReloadConfig(bool userRequested)
    {
        if (_disposed) return;
        lock (_gate)
        {
            try
            {
                var updated = ConfigLoader.Load(_configPath);
                var transportChanged = TransportChanged(_config.Transport,
                    updated.Transport);
                _config = updated;
                _configErrorSince = null;
                _configErrorShown = false;
                _mediaSessions.SetSeekApps(updated.Media.SeekApps);
                _targets.ReplaceConfig(updated);
                _lastHostStatus = string.Empty;
                _pollTimer.Change(100, updated.General.PollIntervalMs);
                if (transportChanged)
                {
                    _serial?.Dispose();
                    _network?.Dispose();
                    _serial = new SerialTransport(_hub, updated.Transport);
                    _network = updated.Transport.WifiEnabled
                        ? new NetworkTransport(_hub, updated.Transport)
                        : null;
                }
                Log.Info("config.toml reloaded successfully");
                _hub.Send(PuckProtocol.Message("CONFIG RELOADED",
                    updated.General.TransliterateOled));
                PushCatalogAndState();
            }
            catch (Exception ex)
            {
                Log.Warn($"Config reload postponed: {ex.Message}");
                if (userRequested)
                {
                    ShowBalloon("config.toml error", ex.Message,
                        ToolTipIcon.Error);
                    return;
                }

                _configErrorSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - _configErrorSince <
                    TimeSpan.FromSeconds(8))
                {
                    _reloadTimer.Change(1500, Timeout.Infinite);
                    return;
                }
                if (!_configErrorShown)
                {
                    _configErrorShown = true;
                    ShowBalloon("config.toml is still incomplete",
                        "The last working settings are still active. " +
                        ex.Message, ToolTipIcon.Warning);
                }
            }
        }
    }

    private static bool TransportChanged(TransportConfig a, TransportConfig b) =>
        a.SerialPort != b.SerialPort || a.SerialBaud != b.SerialBaud ||
        a.WifiEnabled != b.WifiEnabled || a.DiscoveryPort != b.DiscoveryPort ||
        a.TcpPort != b.TcpPort || a.PairingToken != b.PairingToken;

    private void UpdateTray(TargetState state)
    {
        var connection = _hub.ActiveKind switch
        {
            ConnectionKind.Usb => "USB",
            ConnectionKind.Wifi => "WiFi",
            _ => "offline"
        };
        var text = $"SoundDial [{connection}] {state.Name}: " +
                   (state.Available ? $"{state.Volume}%" : state.Subtitle);
        _tray.Text = text.Length <= 63 ? text : text[..63];
    }

    private void OpenConfig() => OpenPath(_configPath);

    private void OpenDeviceSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Show();
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new DeviceSettingsForm(
            settings =>
            {
                _deviceSettingsUiPending = true;
                _hub.Send(PuckProtocol.DeviceSettings(settings));
            },
            (battery, host) =>
            {
                try
                {
                    _config.Battery.MinVoltage = battery.MinVoltage;
                    _config.Battery.MaxVoltage = battery.MaxVoltage;
                    _config.Battery.Display = battery.Display;
                    _config.HostStatus.BatteryDisplay = host.BatteryDisplay;
                    _config.HostStatus.IdleDashboard = host.IdleDashboard;
                    _config.HostStatus.ShowClock = host.ShowClock;
                    _config.HostStatus.IdleAfterSeconds = host.IdleAfterSeconds;
                    _config.HostStatus.LowBatteryWarning = host.LowBatteryWarning;
                    _config.HostStatus.LowBatteryPercent = host.LowBatteryPercent;
                    _config.HostStatus.CriticalBatteryPercent =
                        host.CriticalBatteryPercent;
                    _config.HostStatus.RepeatWarningMinutes =
                        host.RepeatWarningMinutes;
                    ConfigLoader.SaveBatterySettings(_configPath, _config.Battery);
                    ConfigLoader.SaveHostDisplaySettings(_configPath,
                        _config.HostStatus);
                    _hub.Send(PuckProtocol.BatteryCalibration(_config.Battery));
                    _lastHostStatus = string.Empty;
                    PushHostStatus(force: true);
                }
                catch (Exception ex)
                {
                    Log.Error($"Cannot save companion settings: {ex}");
                    ShowBalloon("SoundDial settings",
                        $"Could not save config.toml: {ex.Message}",
                        ToolTipIcon.Error);
                }
            },
            RequestDeviceSettings,
            (Icon)_appIcon.Clone(), _config.Battery, _config.HostStatus);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    private void RequestDeviceSettings()
    {
        if (_disposed) return;
        _deviceSettingsUiPending = true;
        var requestVersion = Interlocked.Increment(ref _settingsRequestVersion);
        _hub.Send("GET_DEVICE_CONFIG");
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            if (!_disposed && requestVersion == Volatile.Read(ref _settingsRequestVersion))
                _settingsForm?.ShowNoResponse(_hub.ActiveKind != ConnectionKind.None);
        });
    }

    private static void OpenLog()
    {
        var path = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "SoundDial",
            "SoundDial.log");
        OpenPath(path);
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "SoundDial", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ShowDiagnostics()
    {
        string report;
        lock (_gate)
        {
            report = Diagnostics.BuildReport(_configPath, _hub.Description,
                _targets.CurrentProfile.Id, _targets.CurrentTarget.Id);
        }
        MessageBox.Show(report, "SoundDial diagnostics",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        try { _tray.ShowBalloonTip(5000, title, text, icon); } catch { }
    }

    private void RequestExit()
    {
        if (_disposed) return;
        // Remove the shell icon before shutting transports down. Their worker
        // threads can need a moment to leave, but the tray entry must vanish
        // immediately and must never become a stale ghost icon.
        _tray.Visible = false;
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        if (_disposed) return;
        _disposed = true;
        _tray.Visible = false;
        _watcher.Dispose();
        _pollTimer.Dispose();
        _reloadTimer.Dispose();
        _rotationTimer.Dispose();
        SaveSelections();
        _hub.EventReceived -= HandlePuckEvent;
        _hub.ConnectionChanged -= HandleConnectionChanged;
        _serial?.Dispose();
        _network?.Dispose();
        _mediaSessions.Dispose();
        _hub.Dispose();
        _targets.SelectionChanged -= HandleSelectionChanged;
        _targets.Dispose();
        if (_settingsForm is { IsDisposed: false }) _settingsForm.Dispose();
        _tray.Dispose();
        _appIcon.Dispose();
        Log.Info("SoundDial stopped");
        base.ExitThreadCore();
    }
}
