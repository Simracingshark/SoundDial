using Windows.Media.Control;
using System.Diagnostics;

namespace AudioPuck;

internal sealed record MediaTrack(string Title, string Artist, string Source);
internal sealed record MediaTimeline(bool Seekable, TimeSpan Position,
    TimeSpan Duration, string Source);

/// <summary>
/// Watches Windows' global media session and reports only real item changes.
/// Spotify, browsers and most modern players publish title/artist here.
/// Failure is non-fatal: volume control continues to work without popups.
/// </summary>
internal sealed class MediaSessionService : IDisposable
{
    private readonly object _gate = new();
    private List<string> _seekApps;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private GlobalSystemMediaTransportControlsSession? _seekSession;
    private string _seekSessionLabel = string.Empty;
    private readonly Dictionary<string, string> _opaqueSourceAliases =
        new(StringComparer.OrdinalIgnoreCase);
    private string _lastIdentity = string.Empty;
    private string _lastSeekSource = string.Empty;
    private bool _baselineEstablished;
    private System.Threading.Timer? _fallbackTimer;
    private System.Threading.Timer? _sessionPollTimer;
    private int _polling;
    private readonly CoreAudioService _audio = new();
    private bool _disposed;

    public event Action<MediaTrack>? TrackChanged;
    public event Action<MediaTimeline>? TimelineChanged;

    public MediaSessionService(IEnumerable<string> seekApps)
    {
        _seekApps = NormalizeSeekApps(seekApps);
    }

    public void SetSeekApps(IEnumerable<string> seekApps)
    {
        lock (_gate) _seekApps = NormalizeSeekApps(seekApps);
        RefreshSelectedSession();
    }

    public MediaTimeline GetTimeline()
    {
        GlobalSystemMediaTransportControlsSession? session;
        string selectedLabel;
        lock (_gate)
        {
            session = _seekSession;
            selectedLabel = _seekSessionLabel;
        }
        if (session is null)
            return new(false, TimeSpan.Zero, TimeSpan.Zero, "NO MEDIA");
        try
        {
            var timeline = session.GetTimelineProperties();
            var playback = session.GetPlaybackInfo();
            // Chromium normally fills StartTime/EndTime. Spotify commonly
            // reports the useful track bounds through MinSeekTime/MaxSeekTime
            // instead, even though seeking itself already works.
            var hasSeekBounds=timeline.MaxSeekTime>timeline.MinSeekTime;
            var start = hasSeekBounds?timeline.MinSeekTime:timeline.StartTime;
            var end = hasSeekBounds?timeline.MaxSeekTime:timeline.EndTime;
            var duration = end > start ? end - start : TimeSpan.Zero;
            var position = timeline.Position - start;
            // GSMTC reports the last confirmed position, not a continuously
            // ticking clock.  Advance it locally while playback is running so
            // the puck follows a film even when the encoder is untouched.
            if (playback?.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                var elapsed = DateTimeOffset.Now - timeline.LastUpdatedTime;
                if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromDays(1))
                    position += elapsed;
            }
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && position > duration) position = duration;
            // Spotify and a few other players allow absolute positioning but
            // do not publish an end time. Keep relative seeking available;
            // the UI will display the known position with an unknown duration.
            var seekable = playback?.Controls.IsPlaybackPositionEnabled == true;
            return new(seekable, position, duration,
                selectedLabel.Length > 0
                    ? selectedLabel.ToUpperInvariant()
                    : FriendlySource(session.SourceAppUserModelId));
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read media timeline: {ex.Message}");
            return new(false, TimeSpan.Zero, TimeSpan.Zero, "NO SEEK");
        }
    }

    public async Task<MediaTimeline> SeekAsync(int deltaSeconds)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate) session = _seekSession;
        if (session is null) return GetTimeline();
        try
        {
            var current = GetTimeline();
            if (!current.Seekable) return current;
            var target = current.Position + TimeSpan.FromSeconds(deltaSeconds);
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (current.Duration > TimeSpan.Zero && target > current.Duration)
                target = current.Duration;
            var raw = session.GetTimelineProperties();
            var start=raw.MaxSeekTime>raw.MinSeekTime?raw.MinSeekTime:raw.StartTime;
            var accepted = await session.TryChangePlaybackPositionAsync(
                (start + target).Ticks);
            if (!accepted)
                Log.Debug($"Seek was rejected by {session.SourceAppUserModelId}");
            return GetTimeline();
        }
        catch (Exception ex)
        {
            Log.Debug($"Media seek failed: {ex.Message}");
            return GetTimeline();
        }
    }

    private static string FriendlySource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "MEDIA";
        var value = source.Split('!')[0].Split('.').LastOrDefault() ?? source;
        return value.Replace("exe", "", StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ').Trim().ToUpperInvariant();
    }

    public void Start() => _ = Task.Run(InitializeAsync);

    private async Task InitializeAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager
                .RequestAsync();
            if (_disposed) return;
            lock (_gate) _manager = manager;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            AttachSession(manager.GetCurrentSession());
            UpdateSeekSession(manager);
            _sessionPollTimer = new System.Threading.Timer(_ => PollSessions(),
                null, 1000, 1000);
        }
        catch (Exception ex)
        {
            Log.Warn($"Windows media-session metadata is unavailable: {ex.Message}");
            StartFallback();
        }
    }

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        AttachSession(sender.GetCurrentSession());
        UpdateSeekSession(sender);
    }

    private void RefreshSelectedSession()
    {
        GlobalSystemMediaTransportControlsSessionManager? manager;
        lock (_gate) manager = _manager;
        if (manager is not null) UpdateSeekSession(manager);
    }

    private void UpdateSeekSession(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var selection = SelectSession(manager);
        var selected = selection?.Session;
        bool changed;
        string source;
        string allowed;
        lock (_gate)
        {
            var previous = _seekSession;
            _seekSession = selected;
            _seekSessionLabel = selection?.Label ?? string.Empty;
            changed = previous is null != selected is null;
            source = selected?.SourceAppUserModelId ?? "NONE";
            allowed = string.Join(", ", _seekApps);
            if (source == _lastSeekSource && !changed) return;
            _lastSeekSource = source;
        }
        Log.Info($"Seek session selected: {source}; allowed: {allowed}");
        TimelineChanged?.Invoke(GetTimeline());
    }

    private sealed record SeekCandidate(
        GlobalSystemMediaTransportControlsSession Session,
        string Label, bool Inferred, int Score, DateTimeOffset Updated);

    private SeekCandidate? SelectSession(
        GlobalSystemMediaTransportControlsSessionManager manager)
    {
        List<string> allowed;
        lock (_gate) allowed = new List<string>(_seekApps);
        if (allowed.Count == 0) return null;

        var allSessions = manager.GetSessions()
            .Where(IsSessionAlive)
            .ToList();
        string? foreground = null;
        string? activeAudio = null;
        try
        {
            foreground = _audio.GetForegroundProcessName();
            activeAudio = _audio.GetActiveSession()?.ProcessName;
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not resolve browser process for seek: {ex.Message}");
        }

        var processApp = allowed.FirstOrDefault(app =>
            app.Equals(foreground, StringComparison.OrdinalIgnoreCase) ||
            app.Equals(activeAudio, StringComparison.OrdinalIgnoreCase));

        Dictionary<string, string> aliases;
        lock (_gate) aliases = new(_opaqueSourceAliases,
            StringComparer.OrdinalIgnoreCase);

        var sessions = allSessions.Select(session =>
        {
            var source = session.SourceAppUserModelId ?? string.Empty;
            var direct = allowed.FirstOrDefault(app => MatchesApp(source, app));
            if (direct is not null)
                return (Session: session, Label: direct, Inferred: false,
                    Included: true);
            if (aliases.TryGetValue(source, out var alias) &&
                allowed.Contains(alias, StringComparer.OrdinalIgnoreCase))
                return (Session: session, Label: alias, Inferred: true,
                    Included: true);
            var canInfer = processApp is not null &&
                !IsRecognizablyBlocked(source, allowed);
            return (Session: session, Label: processApp ?? string.Empty,
                Inferred: canInfer, Included: canInfer);
        }).Where(candidate => candidate.Included).ToList();

        // Some Win32 browser builds publish an opaque AUMID which contains
        // neither "firefox" nor "chrome". In that case validate the browser
        // through its real foreground/Core Audio process, then use the GSMTC
        // session exactly as the original working implementation did.
        if (sessions.Count == 0)
        {
            return null;
        }

        var currentSource = manager.GetCurrentSession()?.SourceAppUserModelId;
        var candidates = sessions.Select(candidate =>
        {
            var session = candidate.Session;
            var score = 0;
            var updated = DateTimeOffset.MinValue;
            try
            {
                var playback = session.GetPlaybackInfo();
                if (playback?.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    score += 10000;
                if (playback?.Controls.IsPlaybackPositionEnabled == true)
                    score += 3000;

                var timeline = session.GetTimelineProperties();
                if (timeline.MaxSeekTime > timeline.MinSeekTime ||
                    timeline.EndTime > timeline.StartTime)
                    score += 2000;
                updated = timeline.LastUpdatedTime;
            }
            catch { }
            if (string.Equals(session.SourceAppUserModelId, currentSource,
                    StringComparison.OrdinalIgnoreCase))
                score += 500;
            if (candidate.Label.Equals(foreground,
                    StringComparison.OrdinalIgnoreCase) ||
                candidate.Label.Equals(activeAudio,
                    StringComparison.OrdinalIgnoreCase))
                score += 7000;
            var priority = allowed.FindIndex(app => app.Equals(candidate.Label,
                StringComparison.OrdinalIgnoreCase));
            if (priority >= 0) score += (allowed.Count - priority) * 10;
            return new SeekCandidate(session, candidate.Label,
                candidate.Inferred, score, updated);
        });

        var selected = candidates.OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Updated)
            .First();
        if (selected.Inferred &&
            !string.IsNullOrWhiteSpace(selected.Session.SourceAppUserModelId))
        {
            lock (_gate)
                _opaqueSourceAliases[selected.Session.SourceAppUserModelId] =
                    selected.Label;
            Log.Debug($"Mapped opaque session " +
                $"{selected.Session.SourceAppUserModelId} to {selected.Label}; " +
                $"foreground={foreground ?? "NONE"}, audio={activeAudio ?? "NONE"}");
        }
        return selected;
    }

    private static bool IsAllowed(string? source, IReadOnlyList<string> allowed)
    {
        return allowed.Any(app => MatchesApp(source, app));
    }

    private static bool IsSessionAlive(
        GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo()?.PlaybackStatus !=
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
        }
        catch { return false; }
    }

    private static bool MatchesApp(string? source, string app)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        var normalized = source.ToLowerInvariant();
        if (normalized.Contains(app, StringComparison.OrdinalIgnoreCase))
            return true;
        return app switch
        {
            "firefox" => normalized.Contains("mozilla"),
            "chrome" => normalized.Contains("chromium"),
            "msedge" or "edge" => normalized.Contains("microsoftedge"),
            _ => false
        };
    }

    private static bool IsRecognizablyBlocked(string? source,
        IReadOnlyList<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        var known = new[] { "spotify", "firefox", "chrome", "msedge" };
        var identified = known.FirstOrDefault(app => MatchesApp(source, app));
        return identified is not null &&
               !allowed.Any(app => app.Equals(identified,
                   StringComparison.OrdinalIgnoreCase) ||
                   identified == "msedge" && app.Equals("edge",
                       StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> NormalizeSeekApps(IEnumerable<string> apps) =>
        apps.Select(app => Path.GetFileNameWithoutExtension(app.Trim()))
            .Where(app => app.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void AttachSession(GlobalSystemMediaTransportControlsSession? session)
    {
        GlobalSystemMediaTransportControlsSession? previous;
        lock (_gate)
        {
            if (_disposed) return;
            previous = _session;
            _session = session;
        }
        if (previous is not null)
            previous.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        if (session is null) return;
        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _ = RefreshAsync(session);
    }

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => _ = RefreshAsync(sender);

    private void PollSessions()
    {
        if (_disposed || Interlocked.Exchange(ref _polling, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                GlobalSystemMediaTransportControlsSessionManager? manager;
                GlobalSystemMediaTransportControlsSession? attached;
                lock (_gate)
                {
                    manager = _manager;
                    attached = _session;
                }
                if (manager is null) return;
                UpdateSeekSession(manager);
                var current = manager.GetCurrentSession();
                if (current is null)
                {
                    PollWindowTitle();
                    return;
                }
                if (!ReferenceEquals(current, attached)) AttachSession(current);
                else await RefreshAsync(current);
            }
            catch (Exception ex)
            {
                Log.Debug($"Media-session poll failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        });
    }

    private async Task RefreshAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            if (_disposed || properties is null) return;
            var title = (properties.Title ?? string.Empty).Trim();
            var artist = (properties.Artist ?? string.Empty).Trim();
            if (title.Length == 0) return;
            var source = session.SourceAppUserModelId ?? string.Empty;
            var identity = $"{source}\n{title}\n{artist}";
            lock (_gate)
            {
                if (identity == _lastIdentity) return;
                _lastIdentity = identity;
                if (!_baselineEstablished)
                {
                    _baselineEstablished = true;
                    return;
                }
            }
            TrackChanged?.Invoke(new MediaTrack(title, artist, source));
        }
        catch (Exception ex)
        {
            Log.Debug($"Could not read media metadata: {ex.Message}");
        }
    }

    private void StartFallback()
    {
        if (_disposed || _fallbackTimer is not null) return;
        _fallbackTimer = new System.Threading.Timer(_ => PollWindowTitle(),
            null, 1000, 1000);
    }

    private void PollWindowTitle()
    {
        if (_disposed) return;
        try
        {
            var active = _audio.GetActiveSession();
            if (active is null || active.ProcessName.StartsWith("pid_",
                    StringComparison.OrdinalIgnoreCase)) return;
            var title = Process.GetProcessesByName(active.ProcessName)
                .Select(process =>
                {
                    try { return process.MainWindowTitle.Trim(); }
                    catch { return string.Empty; }
                    finally { process.Dispose(); }
                })
                .Where(value => value.Length > 0)
                .OrderByDescending(value => value.Length)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(title)) return;
            title = CleanWindowTitle(title, active.ProcessName);
            if (title.Length == 0) return;
            var identity = $"{active.ProcessName}\n{title}";
            lock (_gate)
            {
                if (identity == _lastIdentity) return;
                _lastIdentity = identity;
                if (!_baselineEstablished)
                {
                    _baselineEstablished = true;
                    return;
                }
            }
            TrackChanged?.Invoke(new MediaTrack(title,
                active.ProcessName.ToUpperInvariant(), active.ProcessName));
        }
        catch (Exception ex)
        {
            Log.Debug($"Media-title fallback failed: {ex.Message}");
        }
    }

    private static string CleanWindowTitle(string title, string process)
    {
        var suffixes = new[]
        {
            " - Google Chrome", " — Mozilla Firefox", " - Mozilla Firefox",
            " - Microsoft Edge"
        };
        foreach (var suffix in suffixes)
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                title = title[..^suffix.Length];
        if (title.Equals(process, StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Spotify Premium", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return title.Trim();
    }

    public void Dispose()
    {
        _disposed = true;
        _fallbackTimer?.Dispose();
        _fallbackTimer = null;
        _sessionPollTimer?.Dispose();
        _sessionPollTimer = null;
        lock (_gate)
        {
            if (_manager is not null)
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            if (_session is not null)
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _manager = null;
            _session = null;
            _seekSession = null;
            _seekSessionLabel = string.Empty;
            _opaqueSourceAliases.Clear();
        }
    }
}
