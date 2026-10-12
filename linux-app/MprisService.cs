using System.Globalization;

namespace AudioPuck;

internal sealed record MediaTrack(string Title, string Artist, string Source);
internal sealed record MediaTimeline(bool Seekable, TimeSpan Position,
    TimeSpan Duration, string Source);

/// <summary>MPRIS adapter. playerctl is a small front-end for D-Bus/MPRIS.</summary>
internal sealed class MprisService
{
    private readonly object _gate = new();
    private List<string> _seekApps;
    private readonly bool _available;
    private string? _cachedGeneralPlayer;
    private string? _cachedSeekPlayer;
    private long _playerCacheTick;
    public static MprisService? Current { get; private set; }

    public MprisService(IEnumerable<string> seekApps)
    {
        _seekApps = Normalize(seekApps);
        _available = ProcessRunner.Exists("playerctl");
        Current = this;
    }

    public bool Available => _available;

    public void SetSeekApps(IEnumerable<string> apps)
    {
        lock (_gate)
        {
            _seekApps = Normalize(apps);
            _cachedSeekPlayer = null;
            _playerCacheTick = 0;
        }
    }

    public void Command(string command)
    {
        var player = SelectPlayer(requireAllowed: false);
        if (player is null) return;
        var result = ProcessRunner.Run("playerctl", new[] { "--player", player, command });
        if (!result.Success) Log.Debug($"MPRIS {command} failed: {result.Error.Trim()}");
    }

    public MediaTrack? ReadTrack()
    {
        var player = SelectPlayer(requireAllowed: false);
        if (player is null) return null;
        var metadata = Metadata(player,
            "{{xesam:title}}\\n{{xesam:artist}}").Split('\n');
        var title = metadata.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
        if (title.Length == 0) return null;
        var artist = metadata.ElementAtOrDefault(1)?.Trim() ?? string.Empty;
        return new(title, artist, Friendly(player));
    }

    public Task<MediaTimeline> GetTimelineAsync() =>
        Task.FromResult(ReadTimeline());

    public Task<MediaTimeline> SeekAsync(int deltaSeconds)
    {
        var player = SelectPlayer(requireAllowed: true);
        if (player is null) return Task.FromResult(NoSeek());
        var suffix = deltaSeconds >= 0 ? "+" : "-";
        var amount = Math.Abs(deltaSeconds).ToString(CultureInfo.InvariantCulture) + suffix;
        var result = ProcessRunner.Run("playerctl",
            new[] { "--player", player, "position", amount });
        if (!result.Success)
            Log.Debug($"MPRIS seek failed for {player}: {result.Error.Trim()}");
        return Task.FromResult(ReadTimeline(player));
    }

    private MediaTimeline ReadTimeline()
    {
        var player = SelectPlayer(requireAllowed: true);
        return player is null ? NoSeek() : ReadTimeline(player);
    }

    private MediaTimeline ReadTimeline(string player)
    {
        var positionText = ProcessRunner.Run("playerctl",
            new[] { "--player", player, "position" }).Output.Trim();
        var lengthText = Metadata(player, "{{mpris:length}}");
        _ = double.TryParse(positionText, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var positionSeconds);
        _ = long.TryParse(lengthText, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var durationMicros);
        return new(true, TimeSpan.FromSeconds(Math.Max(0, positionSeconds)),
            TimeSpan.FromMilliseconds(Math.Max(0, durationMicros) / 1000.0),
            Friendly(player));
    }

    private string? SelectPlayer(bool requireAllowed)
    {
        if (!Available) return null;
        lock (_gate)
        {
            if (Environment.TickCount64 - _playerCacheTick < 900)
                return requireAllowed ? _cachedSeekPlayer : _cachedGeneralPlayer;
        }
        var list = ProcessRunner.Run("playerctl", new[] { "-l" });
        if (!list.Success) return null;
        var players = list.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        List<string> allowed;
        lock (_gate) allowed = new(_seekApps);
        var candidates = requireAllowed
            ? players.Where(player => Matches(player, allowed)).ToArray()
            : players;
        var selected = candidates.FirstOrDefault(player =>
                   Status(player).Equals("Playing", StringComparison.OrdinalIgnoreCase)) ??
               candidates.FirstOrDefault(player =>
                   Status(player).Equals("Paused", StringComparison.OrdinalIgnoreCase)) ??
               candidates.FirstOrDefault();
        lock (_gate)
        {
            if (requireAllowed) _cachedSeekPlayer = selected;
            else _cachedGeneralPlayer = selected;
            // Only mark a complete cache generation after the general player
            // has also been selected; otherwise a first seek lookup could
            // temporarily hide the general player.
            if (!requireAllowed) _playerCacheTick = Environment.TickCount64;
        }
        return selected;
    }

    private static bool Matches(string player, IEnumerable<string> allowed)
    {
        var normalized = player.ToLowerInvariant();
        return allowed.Any(app => normalized.Contains(app) ||
            (app == "chrome" && (normalized.Contains("chromium") ||
                                  normalized.Contains("brave"))) ||
            (app == "msedge" && normalized.Contains("edge")));
    }

    private static string Status(string player) => ProcessRunner.Run("playerctl",
        new[] { "--player", player, "status" }).Output.Trim();

    private static string Metadata(string player, string format) =>
        ProcessRunner.Run("playerctl",
            new[] { "--player", player, "metadata", "--format", format }).Output.Trim();

    private static string Friendly(string player)
    {
        var name = player.Split('.')[0];
        return name.Replace('-', ' ').Replace('_', ' ').ToUpperInvariant();
    }

    private static List<string> Normalize(IEnumerable<string> values) => values
        .Select(ConfigLoader.NormalizeProcessName).Where(value => value.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static MediaTimeline NoSeek() =>
        new(false, TimeSpan.Zero, TimeSpan.Zero, "NO SEEK");
}
