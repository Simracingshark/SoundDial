namespace AudioPuck;

internal sealed class TargetController : IDisposable
{
    private readonly CoreAudioService _audio = new();
    private readonly VoicemeeterService _voicemeeter = new();
    private readonly DefaultAudioDeviceSwitcher _deviceSwitcher = new();
    private readonly LaptopBrightnessService _brightness = new();
    private AppConfig _config;
    private int _profileIndex;
    private int _targetIndex;
    private List<TargetConfig> _mediaTargets = new();
    private int _mediaTargetIndex;
    private DateTime _manualProfileUntil;
    private DateTime _lastRunningScan;
    private HashSet<string> _runningProcesses = new(
        StringComparer.OrdinalIgnoreCase);
    private int _automaticCandidate = -1;
    private DateTime _automaticCandidateSince;
    private bool _automaticProfileActive;
    private string _automaticProcessName = string.Empty;
    private string _automaticCandidateProcess = string.Empty;
    private bool _automaticProfilesEnabled = true;
    private readonly HashSet<int> _linkedMediaTargetIndices = new();
    private SavedSelectionState? _savedSelections;

    public bool AutomaticProfilesEnabled
    {
        get => _automaticProfilesEnabled;
        set
        {
            if (_automaticProfilesEnabled == value) return;
            _automaticProfilesEnabled = value;
            _automaticCandidate = -1;
            if (value || !_automaticProfileActive) return;
            var defaultIndex = Math.Max(0, _config.Profiles.FindIndex(profile =>
                profile.Id.Equals(_config.General.ActiveProfile,
                    StringComparison.OrdinalIgnoreCase)));
            _automaticProfileActive = false;
            _automaticProcessName = string.Empty;
            SelectProfile(defaultIndex, manual: false);
        }
    }

    public TargetController(AppConfig config)
    {
        RemoveUnavailableBrightnessTargets(config);
        _config = config;
        _profileIndex = Math.Max(0, config.Profiles.FindIndex(p =>
            p.Id.Equals(config.General.ActiveProfile,
                StringComparison.OrdinalIgnoreCase)));
        RefreshMediaTargets();
    }

    public ProfileConfig CurrentProfile => _config.Profiles[_profileIndex];
    public TargetConfig CurrentTarget => CurrentProfile.Targets[_targetIndex];
    public int ProfileIndex => _profileIndex;
    public int TargetIndex => _targetIndex;
    public int MainStepOverride => Math.Clamp(CurrentTarget.Step, 0, 10);
    public int MediaStepOverride => _linkedMediaTargetIndices.Count >= 2 ||
        _mediaTargets.Count == 0
            ? 0
            : Math.Clamp(_mediaTargets[_mediaTargetIndex].Step, 0, 10);
    public IReadOnlyList<ProfileConfig> Profiles => _config.Profiles;

    public event Action? SelectionChanged;

    public void RestoreSelections(SavedSelectionState saved)
    {
        _savedSelections = saved;
        if (saved.TargetsByProfile.TryGetValue(CurrentProfile.Id,
                out var targetId))
        {
            var restoredTarget = CurrentProfile.Targets.FindIndex(target =>
                target.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
            if (restoredTarget >= 0) _targetIndex = restoredTarget;
        }

        var restoredMedia = _mediaTargets.FindIndex(target =>
            target.Id.Equals(saved.MediaTargetId,
                StringComparison.OrdinalIgnoreCase));
        if (restoredMedia >= 0) _mediaTargetIndex = restoredMedia;

        _linkedMediaTargetIndices.Clear();
        foreach (var id in saved.LinkedMediaTargetIds)
        {
            var index = _mediaTargets.FindIndex(target =>
                target.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _linkedMediaTargetIndices.Add(index);
        }
        if (_linkedMediaTargetIndices.Count >= 2)
            _mediaTargetIndex = _linkedMediaTargetIndices.Min();
        else
            _linkedMediaTargetIndices.Clear();
    }

    public void CaptureSelections(SavedSelectionState saved)
    {
        saved.TargetsByProfile[CurrentProfile.Id] = CurrentTarget.Id;
        saved.MediaTargetId = _mediaTargets.Count == 0
            ? string.Empty
            : _mediaTargets[_mediaTargetIndex].Id;
        saved.LinkedMediaTargetIds = _linkedMediaTargetIndices
            .OrderBy(index => index)
            .Select(index => _mediaTargets[index].Id)
            .ToList();
    }

    public void ReplaceConfig(AppConfig config)
    {
        var oldProfile = CurrentProfile.Id;
        var oldTarget = CurrentTarget.Id;
        var linkedMediaIds = _linkedMediaTargetIndices
            .OrderBy(index => index)
            .Select(index => _mediaTargets[index].Id)
            .ToList();
        var oldMediaTargetId = _mediaTargets.Count == 0
            ? string.Empty
            : _mediaTargets[_mediaTargetIndex].Id;
        RemoveUnavailableBrightnessTargets(config);
        _config = config;
        _profileIndex = Math.Max(0, config.Profiles.FindIndex(p =>
            p.Id.Equals(oldProfile, StringComparison.OrdinalIgnoreCase)));
        _targetIndex = Math.Max(0, CurrentProfile.Targets.FindIndex(t =>
            t.Id.Equals(oldTarget, StringComparison.OrdinalIgnoreCase)));
        _targetIndex = Math.Min(_targetIndex, CurrentProfile.Targets.Count - 1);
        RefreshMediaTargets();
        var restoredMedia = _mediaTargets.FindIndex(target =>
            target.Id.Equals(oldMediaTargetId,
                StringComparison.OrdinalIgnoreCase));
        if (restoredMedia >= 0) _mediaTargetIndex = restoredMedia;
        _linkedMediaTargetIndices.Clear();
        foreach (var id in linkedMediaIds)
        {
            var index = _mediaTargets.FindIndex(target =>
                target.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) _linkedMediaTargetIndices.Add(index);
        }
        if (_linkedMediaTargetIndices.Count >= 2)
            _mediaTargetIndex = _linkedMediaTargetIndices.Min();
        else
            _linkedMediaTargetIndices.Clear();
        SelectionChanged?.Invoke();
    }

    public TargetState ReadCurrentState()
    {
        try
        {
            _voicemeeter.Poll();
            return ReadState(CurrentTarget, new HashSet<string>(
                StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Error($"Cannot read target '{CurrentTarget.Id}'", ex);
            return TargetState.Offline(CurrentTarget, "AUDIO ERROR");
        }
    }

    public void Adjust(int detents, int deviceStep = 0)
    {
        if (detents == 0) return;
        var target = CurrentTarget;
        try
        {
            AdjustTarget(target, detents, deviceStep, new HashSet<string>(
                StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Error($"Cannot adjust target '{target.Id}'", ex);
        }
    }

    public void Click()
    {
        ExecuteAction(CurrentTarget.ClickAction, isDouble: false);
    }

    public void DoubleClick()
    {
        ExecuteAction(CurrentTarget.DoubleClickAction, isDouble: true);
    }

    public void TripleClick()
    {
        WindowsActions.MediaPrevious();
    }

    public IReadOnlyList<string> GetMediaTargetNames() =>
        _mediaTargets.Select(t => t.DisplayLabel).ToList();

    public int MediaTargetIndex => _mediaTargetIndex;
    public int MediaLinkedMask => _linkedMediaTargetIndices.Aggregate(0,
        (mask, index) => mask | (1 << index));

    public TargetState ReadMediaState()
    {
        if (_mediaTargets.Count == 0)
            return TargetState.Offline(new TargetConfig { Id = "media" },
                "NO MEDIA VOLUME");
        if (_linkedMediaTargetIndices.Count >= 2)
            return ReadLinkedMediaState();
        return ReadState(_mediaTargets[_mediaTargetIndex], new HashSet<string>(
            StringComparer.OrdinalIgnoreCase));
    }

    public void SelectMediaTarget(int index)
    {
        if (index < 0 || index >= _mediaTargets.Count) return;
        _linkedMediaTargetIndices.Clear();
        _mediaTargetIndex = index;
        SelectionChanged?.Invoke();
    }

    public void AdjustMedia(int detents, int deviceStep)
    {
        if (_mediaTargets.Count == 0 || detents == 0) return;
        if (_linkedMediaTargetIndices.Count >= 2)
        {
            AdjustLinkedMediaTargets(detents, deviceStep);
            return;
        }
        var target = _mediaTargets[_mediaTargetIndex];
        AdjustTarget(target, detents, deviceStep, new HashSet<string>(
            StringComparer.OrdinalIgnoreCase));
    }

    public void MediaPlayPause() => WindowsActions.MediaPlayPause();
    public void MediaNext() => WindowsActions.MediaNext();
    public void MediaPrevious() => WindowsActions.MediaPrevious();

    public void ToggleMediaMute()
    {
        if (_mediaTargets.Count == 0) return;
        if (_linkedMediaTargetIndices.Count >= 2)
        {
            ToggleLinkedMediaMute();
            return;
        }
        ToggleMute(_mediaTargets[_mediaTargetIndex]);
    }

    public void NextTarget()
    {
        _targetIndex = (_targetIndex + 1) % CurrentProfile.Targets.Count;
        SelectionChanged?.Invoke();
    }

    public void SelectTarget(int index)
    {
        if (index < 0 || index >= CurrentProfile.Targets.Count) return;
        _targetIndex = index;
        SelectionChanged?.Invoke();
    }

    public void SetLinkedMediaTargets(int mask)
    {
        _linkedMediaTargetIndices.Clear();
        for (var index = 0; index < _mediaTargets.Count; index++)
            if ((mask & (1 << index)) != 0)
                _linkedMediaTargetIndices.Add(index);

        if (_linkedMediaTargetIndices.Count < 2)
        {
            _linkedMediaTargetIndices.Clear();
            SelectionChanged?.Invoke();
            return;
        }

        _mediaTargetIndex = _linkedMediaTargetIndices.Min();
        SelectionChanged?.Invoke();
    }

    public void SelectProfile(int index, bool manual = true)
    {
        if (index < 0 || index >= _config.Profiles.Count) return;
        if (_savedSelections is not null)
            _savedSelections.TargetsByProfile[CurrentProfile.Id] =
                CurrentTarget.Id;
        _profileIndex = index;
        _targetIndex = 0;
        if (_savedSelections is not null &&
            _savedSelections.TargetsByProfile.TryGetValue(CurrentProfile.Id,
                out var savedTargetId))
        {
            var restoredTarget = CurrentProfile.Targets.FindIndex(target =>
                target.Id.Equals(savedTargetId,
                    StringComparison.OrdinalIgnoreCase));
            if (restoredTarget >= 0) _targetIndex = restoredTarget;
        }
        if (manual) _manualProfileUntil = DateTime.UtcNow.AddMinutes(30);
        SelectionChanged?.Invoke();
    }

    public void EvaluateAutomaticProfile()
    {
        if (!AutomaticProfilesEnabled || DateTime.UtcNow < _manualProfileUntil)
        {
            _automaticCandidate = -1;
            return;
        }

        var foreground = _audio.GetForegroundProcessName();
        if (DateTime.UtcNow - _lastRunningScan >= TimeSpan.FromSeconds(1))
        {
            _lastRunningScan = DateTime.UtcNow;
            _runningProcesses = System.Diagnostics.Process.GetProcesses()
                .Select(process =>
                {
                    try { return ConfigLoader.NormalizeProcessName(process.ProcessName); }
                    catch { return string.Empty; }
                    finally { process.Dispose(); }
                })
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var match = -1;
        var matchedProcess = string.Empty;
        for (var index = 0; index < _config.Profiles.Count; index++)
        {
            var profile = _config.Profiles[index];
            if (profile.ActivateWhen.Count == 0) continue;
            string? process;
            if (profile.Trigger == ProfileTrigger.Running)
            {
                process = profile.ActivateWhen.FirstOrDefault(name =>
                              name.Equals(foreground,
                                  StringComparison.OrdinalIgnoreCase) &&
                              _runningProcesses.Contains(name)) ??
                          profile.ActivateWhen.FirstOrDefault(name =>
                              _runningProcesses.Contains(name));
            }
            else
            {
                process = profile.ActivateWhen.FirstOrDefault(name =>
                    name.Equals(foreground, StringComparison.OrdinalIgnoreCase));
            }
            if (process is null) continue;
            match = index;
            matchedProcess = process;
            break;
        }
        var defaultIndex = Math.Max(0, _config.Profiles.FindIndex(profile =>
            profile.Id.Equals(_config.General.ActiveProfile,
                StringComparison.OrdinalIgnoreCase)));
        var desired = match >= 0 ? match : defaultIndex;

        if (desired == _profileIndex)
        {
            _automaticCandidate = -1;
            _automaticProfileActive = match >= 0;
            _automaticProcessName = match >= 0 ? matchedProcess : string.Empty;
            return;
        }
        if (!_automaticProfileActive && match < 0) return;

        if (_automaticCandidate != desired)
        {
            _automaticCandidate = desired;
            _automaticCandidateSince = DateTime.UtcNow;
            _automaticCandidateProcess = matchedProcess;
            return;
        }
        _automaticCandidateProcess = matchedProcess;
        if (DateTime.UtcNow - _automaticCandidateSince < TimeSpan.FromSeconds(1))
            return;

        SelectProfile(desired, manual: false);
        _automaticProfileActive = match >= 0;
        _automaticProcessName = match >= 0
            ? _automaticCandidateProcess
            : string.Empty;
        _automaticCandidate = -1;
        Log.Info(match >= 0
            ? $"Automatically selected profile '{CurrentProfile.Id}'"
            : $"Automatically returned to profile '{CurrentProfile.Id}'");
    }

    public IReadOnlyList<string> GetTargetNames() =>
        CurrentProfile.Targets.Select(TargetPickerName).ToList();

    public IReadOnlyList<string> GetProfileNames() =>
        _config.Profiles.Select(p => p.Name).ToList();

    private TargetState ReadState(TargetConfig target, HashSet<string> visited)
    {
        if (!visited.Add(target.Id))
            return TargetState.Offline(target, "FALLBACK LOOP");

        switch (target.Type)
        {
            case TargetKind.Game:
            {
                if (string.IsNullOrWhiteSpace(_automaticProcessName))
                    return TargetState.Offline(target, "NO ACTIVE GAME");
                var value = _audio.GetSessionVolume(
                    new[] { _automaticProcessName });
                return value is null
                    ? TargetState.Offline(target,
                        _automaticProcessName.ToUpperInvariant())
                    : State(target, _automaticProcessName.ToUpperInvariant(),
                        value, true, string.Empty);
            }
            case TargetKind.Master:
            case TargetKind.Media:
            {
                var value = _audio.GetDefaultVolume();
                return State(target, target.DisplayLabel, value, true,
                    target.Type == TargetKind.Media ? "MEDIA KEYS" : "WINDOWS");
            }
            case TargetKind.App:
            {
                var value = _audio.GetSessionVolume(target.Apps);
                if (value is not null)
                {
                    var session = _audio.GetPreferredSession(target.Apps);
                    var sessionName = FriendlySessionName(session,
                        target.DisplayLabel);
                    return State(target, sessionName, value, true,
                        string.Empty);
                }
                return ReadFallback(target, visited, "NOT RUNNING");
            }
            case TargetKind.Foreground:
            {
                var process = _audio.GetForegroundProcessName();
                if (string.IsNullOrWhiteSpace(process))
                    return TargetState.Offline(target, "NO FOREGROUND APP");
                var value = _audio.GetSessionVolume(new[] { process });
                return value is null
                    ? TargetState.Offline(target, process.ToUpperInvariant())
                    : State(target, process.ToUpperInvariant(), value, true,
                        "FOREGROUND");
            }
            case TargetKind.ActiveAudio:
            {
                var session = _audio.GetActiveSession();
                return session is null
                    ? TargetState.Offline(target, "NO ACTIVE AUDIO")
                    : State(target, session.ProcessName.ToUpperInvariant(),
                        new AudioValue(session.Volume, session.Muted), true,
                        "ACTIVE AUDIO");
            }
            case TargetKind.Sonar:
            {
                var endpoint = FindSonarEndpoint(target.Sonar);
                return endpoint is null
                    ? TargetState.Offline(target, "SONAR OFFLINE")
                    : State(target, target.DisplayLabel,
                        _audio.GetEndpointVolume(endpoint.Id), true,
                        endpoint.Name);
            }
            case TargetKind.Voicemeeter:
            {
                if (!_voicemeeter.TryGet(target.Parameter, out var gain))
                    return TargetState.Offline(target, "VOICEMEETER OFFLINE");
                var muted = !string.IsNullOrWhiteSpace(target.MuteParameter) &&
                            _voicemeeter.TryGet(target.MuteParameter, out var mute) &&
                            mute >= 0.5f;
                var percent = DbToPercent(gain, target.MinDb, target.MaxDb);
                return new TargetState(target.Id, target.DisplayLabel, percent,
                    muted, true, $"{gain:0.0} dB", target.ParsedColor);
            }
            case TargetKind.Output:
            {
                var endpoint = _audio.GetDefaultEndpoint();
                var value = _audio.GetEndpointVolume(endpoint.Id);
                return State(target, endpoint.Name, value, true, "OUTPUT DEVICE");
            }
            case TargetKind.Brightness:
            {
                var value = _brightness.Read();
                return value is null
                    ? TargetState.Offline(target, "LAPTOP DISPLAY ONLY")
                    : new TargetState(target.Id, target.DisplayLabel, value.Value,
                        false, true, "BUILT-IN DISPLAY", target.ParsedColor);
            }
            default:
                return TargetState.Offline(target, "UNSUPPORTED");
        }
    }

    private TargetState ReadLinkedMediaState()
    {
        var states = _linkedMediaTargetIndices.OrderBy(index => index)
            .Select(index => ReadState(_mediaTargets[index],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
            .Where(state => state.Available)
            .ToList();
        if (states.Count == 0)
            return TargetState.Offline(CurrentTarget, "LINK OFFLINE");

        var name = string.Join(" + ", states.Select(state => state.Name));
        var levels = string.Join("  ", states.Select(state => $"{state.Volume}%"));
        return new TargetState("linked", name,
            (int)Math.Round(states.Average(state => state.Volume)),
            states.All(state => state.Muted), true, levels,
            states[0].Color);
    }

    private void AdjustLinkedMediaTargets(int detents, int deviceStep)
    {
        var members = _linkedMediaTargetIndices.OrderBy(index => index)
            .Select(index => (Target: _mediaTargets[index],
                State: ReadState(_mediaTargets[index],
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase))))
            .Where(item => item.State.Available)
            .ToList();
        if (members.Count == 0) return;

        var requested = detents * (deviceStep > 0 ? deviceStep : 4);
        var lowest = members.Min(item => item.State.Volume);
        var highest = members.Max(item => item.State.Volume);
        var delta = Math.Clamp(requested, -lowest, 100 - highest);
        if (delta == 0) return;

        foreach (var member in members)
            AdjustTargetByPercent(member.Target, delta,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private void ToggleLinkedMediaMute()
    {
        var members = _linkedMediaTargetIndices.OrderBy(index => index)
            .Select(index => (Target: _mediaTargets[index],
                State: ReadState(_mediaTargets[index],
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase))))
            .Where(item => item.State.Available)
            .ToList();
        if (members.Count == 0) return;

        // If at least one member is audible, mute every member. If all are
        // already muted, restore every member together.
        var mute = !members.All(item => item.State.Muted);
        foreach (var member in members)
            if (member.State.Muted != mute) ToggleMute(member.Target);
    }

    private void AdjustTargetByPercent(TargetConfig target, int delta,
        HashSet<string> visited)
    {
        if (!visited.Add(target.Id)) return;
        switch (target.Type)
        {
            case TargetKind.Game:
                if (!string.IsNullOrWhiteSpace(_automaticProcessName))
                    _audio.AdjustSessionVolume(new[] { _automaticProcessName },
                        delta);
                break;
            case TargetKind.Master:
            case TargetKind.Media:
            case TargetKind.Output:
                _audio.AdjustDefaultVolume(delta);
                break;
            case TargetKind.App:
                if (_audio.GetSessionVolume(target.Apps) is not null)
                    _audio.AdjustSessionVolume(target.Apps, delta);
                else if (!string.IsNullOrWhiteSpace(target.Fallback))
                {
                    var fallback = CurrentProfile.Targets.FirstOrDefault(item =>
                        item.Id.Equals(target.Fallback,
                            StringComparison.OrdinalIgnoreCase));
                    if (fallback is not null)
                        AdjustTargetByPercent(fallback, delta, visited);
                }
                break;
            case TargetKind.Foreground:
                var process = _audio.GetForegroundProcessName();
                if (process is not null)
                    _audio.AdjustSessionVolume(new[] { process }, delta);
                break;
            case TargetKind.ActiveAudio:
                var session = _audio.GetActiveSession();
                if (session is not null)
                    _audio.AdjustSessionVolume(new[] { session.ProcessName },
                        delta);
                break;
            case TargetKind.Sonar:
                var endpoint = FindSonarEndpoint(target.Sonar);
                if (endpoint is not null)
                    _audio.AdjustEndpointVolume(endpoint.Id, delta);
                break;
            case TargetKind.Voicemeeter:
                if (_voicemeeter.TryGet(target.Parameter, out var gain))
                    _voicemeeter.TrySet(target.Parameter, (float)Math.Clamp(
                        gain + delta * (target.MaxDb - target.MinDb) / 100.0,
                        target.MinDb, target.MaxDb));
                break;
            case TargetKind.Brightness:
                _brightness.Adjust(delta);
                break;
        }
    }

    private void AdjustTarget(TargetConfig target, int detents, int deviceStep,
        HashSet<string> visited)
    {
        if (!visited.Add(target.Id)) return;
        // A target-specific config value wins. Otherwise use the step chosen
        // in the SoundDial settings, with 4% as a defensive fallback.
        var effectiveStep = target.Step > 0 ? target.Step :
            deviceStep > 0 ? deviceStep : 4;
        var delta = detents * effectiveStep;
        switch (target.Type)
        {
            case TargetKind.Game:
                if (!string.IsNullOrWhiteSpace(_automaticProcessName))
                    _audio.AdjustSessionVolume(
                        new[] { _automaticProcessName }, delta);
                break;
            case TargetKind.Master:
            case TargetKind.Media:
            case TargetKind.Output:
                _audio.AdjustDefaultVolume(delta);
                break;
            case TargetKind.App:
                if (_audio.GetSessionVolume(target.Apps) is not null)
                    _audio.AdjustSessionVolume(target.Apps, delta);
                else AdjustFallback(target, detents, deviceStep, visited);
                break;
            case TargetKind.Foreground:
            {
                var process = _audio.GetForegroundProcessName();
                if (process is not null)
                    _audio.AdjustSessionVolume(new[] { process }, delta);
                break;
            }
            case TargetKind.ActiveAudio:
            {
                var session = _audio.GetActiveSession();
                if (session is not null)
                    _audio.AdjustSessionVolume(new[] { session.ProcessName }, delta);
                break;
            }
            case TargetKind.Sonar:
            {
                var endpoint = FindSonarEndpoint(target.Sonar);
                if (endpoint is not null) _audio.AdjustEndpointVolume(endpoint.Id, delta);
                break;
            }
            case TargetKind.Voicemeeter:
                if (_voicemeeter.TryGet(target.Parameter, out var gain))
                    _voicemeeter.TrySet(target.Parameter, (float)Math.Clamp(
                        gain + detents * target.StepDb, target.MinDb, target.MaxDb));
                break;
            case TargetKind.Brightness:
                _brightness.Adjust(delta);
                break;
        }
    }

    private void ToggleMute(TargetConfig target)
    {
        switch (target.Type)
        {
            case TargetKind.Game:
                if (!string.IsNullOrWhiteSpace(_automaticProcessName))
                    _audio.ToggleSessionMute(
                        new[] { _automaticProcessName });
                break;
            case TargetKind.Master:
            case TargetKind.Media:
            case TargetKind.Output:
                _audio.ToggleDefaultMute();
                break;
            case TargetKind.App:
                _audio.ToggleSessionMute(target.Apps);
                break;
            case TargetKind.Foreground:
            {
                var process = _audio.GetForegroundProcessName();
                if (process is not null) _audio.ToggleSessionMute(new[] { process });
                break;
            }
            case TargetKind.ActiveAudio:
            {
                var session = _audio.GetActiveSession();
                if (session is not null)
                    _audio.ToggleSessionMute(new[] { session.ProcessName });
                break;
            }
            case TargetKind.Sonar:
            {
                var endpoint = FindSonarEndpoint(target.Sonar);
                if (endpoint is not null) _audio.ToggleEndpointMute(endpoint.Id);
                break;
            }
            case TargetKind.Voicemeeter:
                if (!string.IsNullOrWhiteSpace(target.MuteParameter) &&
                    _voicemeeter.TryGet(target.MuteParameter, out var mute))
                    _voicemeeter.TrySet(target.MuteParameter, mute >= 0.5f ? 0 : 1);
                break;
        }
    }

    private void ExecuteAction(string action, bool isDouble)
    {
        if (action == "auto")
            action = CurrentTarget.Type == TargetKind.Media ? "media_play_pause" : "mute";
        switch (action)
        {
            case "mute": ToggleMute(CurrentTarget); break;
            case "next_target": NextTarget(); break;
            case "media_play_pause": WindowsActions.MediaPlayPause(); break;
            case "media_next": WindowsActions.MediaNext(); break;
            case "media_previous": WindowsActions.MediaPrevious(); break;
            case "next_output": CycleOutput(); break;
            case "none": break;
            default:
                Log.Warn($"Unknown {(isDouble ? "double-click" : "click")} action '{action}'");
                break;
        }
    }

    private void CycleOutput()
    {
        var endpoints = _audio.GetRenderEndpoints();
        if (endpoints.Count < 2) return;
        var current = _audio.GetDefaultEndpoint();
        var index = endpoints.ToList().FindIndex(e => e.Id == current.Id);
        var next = endpoints[(index + 1 + endpoints.Count) % endpoints.Count];
        if (_deviceSwitcher.TrySetDefault(next.Id))
            Log.Info($"Default audio output changed to '{next.Name}'");
    }

    private EndpointSnapshot? FindSonarEndpoint(string channel)
    {
        var normalized = channel.Trim().ToLowerInvariant();
        var display = normalized switch
        {
            // SteelSeries exposes this endpoint as "Gaming", not "Game".
            "game" => "Gaming",
            "gaming" => "Gaming",
            "chat" => "Chat",
            "media" => "Media",
            "aux" => "Aux",
            _ => channel
        };
        return _audio.FindEndpoint($"SteelSeries Sonar - {display}",
            $"Sonar - {display}", $"Sonar {display}");
    }

    private TargetState ReadFallback(TargetConfig source, HashSet<string> visited,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(source.Fallback))
            return TargetState.Offline(source, reason);
        var fallback = CurrentProfile.Targets.FirstOrDefault(t =>
            t.Id.Equals(source.Fallback, StringComparison.OrdinalIgnoreCase));
        if (fallback is null) return TargetState.Offline(source, reason);
        var state = ReadState(fallback, visited);
        return state with
        {
            Id = source.Id,
            Name = source.DisplayLabel,
            Subtitle = state.Available ? $"VIA {fallback.DisplayLabel}" : reason,
            Color = source.ParsedColor
        };
    }

    private void AdjustFallback(TargetConfig source, int detents, int deviceStep,
        HashSet<string> visited)
    {
        if (string.IsNullOrWhiteSpace(source.Fallback)) return;
        var fallback = CurrentProfile.Targets.FirstOrDefault(t =>
            t.Id.Equals(source.Fallback, StringComparison.OrdinalIgnoreCase));
        if (fallback is not null) AdjustTarget(fallback, detents, deviceStep, visited);
    }

    private void RefreshMediaTargets()
    {
        var previous = _mediaTargets.Count > 0 &&
                       _mediaTargetIndex < _mediaTargets.Count
            ? _mediaTargets[_mediaTargetIndex].Id
            : string.Empty;
        _mediaTargets = _config.Media.VolumeTargets.Select(id =>
                _config.Profiles.SelectMany(profile => profile.Targets)
                    .First(target => target.Id.Equals(id,
                        StringComparison.OrdinalIgnoreCase)))
            .ToList();
        _mediaTargetIndex = Math.Max(0, _mediaTargets.FindIndex(target =>
            target.Id.Equals(previous, StringComparison.OrdinalIgnoreCase)));
    }

    private void RemoveUnavailableBrightnessTargets(AppConfig config)
    {
        if (_brightness.IsAvailable) return;

        var removedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var emptyProfiles = new List<ProfileConfig>();
        foreach (var profile in config.Profiles)
        {
            foreach (var target in profile.Targets
                         .Where(target => target.Type == TargetKind.Brightness))
                removedIds.Add(target.Id);
            profile.Targets.RemoveAll(target =>
                target.Type == TargetKind.Brightness);
            if (profile.Targets.Count == 0)
                emptyProfiles.Add(profile);
        }

        if (removedIds.Count == 0) return;
        foreach (var profile in emptyProfiles)
            if (config.Profiles.Count > 1)
                config.Profiles.Remove(profile);
        if (config.Profiles.Count == 1 && config.Profiles[0].Targets.Count == 0)
            config.Profiles[0].Targets.Add(new TargetConfig
            {
                Id = "master", Label = "MASTER",
                Type = TargetKind.Master, Color = "blue"
            });
        config.Media.VolumeTargets.RemoveAll(id => removedIds.Contains(id));
        Log.Info("Laptop brightness target hidden: no controllable built-in display found");
    }

    private static string FriendlySessionName(AudioSessionSnapshot? session,
        string fallback)
    {
        if (session is null) return fallback;

        var displayName = session.DisplayName?.Trim() ?? string.Empty;
        // Packaged apps can expose an unresolved Windows resource reference
        // instead of a human-readable name. In that case the process name is
        // considerably more useful on the puck.
        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.StartsWith("@{", StringComparison.Ordinal) ||
            displayName.Contains("ms-resource:",
                StringComparison.OrdinalIgnoreCase))
        {
            displayName = session.ProcessName;
        }

        return displayName.Replace('_', ' ').ToUpperInvariant();
    }

    private string TargetPickerName(TargetConfig target)
    {
        if (target.Type == TargetKind.Game &&
            !string.IsNullOrWhiteSpace(_automaticProcessName))
            return _automaticProcessName.Replace('_', ' ').ToUpperInvariant();
        if (target.Type != TargetKind.App) return target.DisplayLabel;
        return FriendlySessionName(_audio.GetPreferredSession(target.Apps),
            target.DisplayLabel);
    }

    private static TargetState State(TargetConfig target, string name,
        AudioValue value, bool available, string subtitle) =>
        new(target.Id, name, value.Volume, value.Muted, available, subtitle,
            target.ParsedColor);

    private static int DbToPercent(double db, double minDb, double maxDb)
    {
        if (maxDb <= minDb) return 0;
        return Math.Clamp((int)Math.Round((db - minDb) * 100.0 / (maxDb - minDb)),
            0, 100);
    }

    public void Dispose()
    {
        _brightness.Dispose();
        _voicemeeter.Dispose();
    }
}
