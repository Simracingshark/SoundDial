using System.Globalization;

namespace AudioPuck;

internal sealed class AppConfig
{
    public GeneralConfig General { get; } = new();
    public TransportConfig Transport { get; } = new();
    public MediaConfig Media { get; } = new();
    public BatteryConfig Battery { get; } = new();
    public HostStatusConfig HostStatus { get; } = new();
    public Dictionary<string, int> StepOverrides { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<ProfileConfig> Profiles { get; } = new();

    public ProfileConfig ActiveProfile =>
        Profiles.FirstOrDefault(p => p.Id.Equals(General.ActiveProfile,
            StringComparison.OrdinalIgnoreCase)) ?? Profiles[0];
}

internal sealed class GeneralConfig
{
    public string ActiveProfile { get; set; } = "default";
    // Volume commands are handled immediately; this timer only refreshes
    // confirmed state.  A 500 ms scan avoids hammering systems with many
    // Sonar/Voicemeeter/virtual endpoints while the puck still previews turns.
    public int PollIntervalMs { get; set; } = 500;
    public string LogLevel { get; set; } = "info";
    public bool StartMinimized { get; set; } = true;
    public bool TransliterateOled { get; set; } = true;
}

internal sealed class TransportConfig
{
    public string SerialPort { get; set; } = "auto";
    public int SerialBaud { get; set; } = 115200;
    public bool WifiEnabled { get; set; } = true;
    public int DiscoveryPort { get; set; } = 45830;
    public int TcpPort { get; set; } = 45831;
    public string PairingToken { get; set; } = "change-me";
}

internal sealed class MediaConfig
{
    public List<string> VolumeTargets { get; set; } =
        new() { "spotify", "chrome", "sonar_media" };
    public List<string> SeekApps { get; set; } =
        new() { "firefox", "chrome", "msedge" };
}

internal sealed class BatteryConfig
{
    public double MinVoltage { get; set; } = 3.30;
    public double MaxVoltage { get; set; } = 4.20;
    public string Display { get; set; } = "percent";
}

internal sealed class HostStatusConfig
{
    // Independent laptop battery style for Main, Media and Seek screens.
    // The optional idle dashboard is controlled separately below.
    public string BatteryDisplay { get; set; } = "off";
    public bool IdleDashboard { get; set; }
    public bool ShowClock { get; set; } = true;
    public int IdleAfterSeconds { get; set; } = 20;
    public bool LowBatteryWarning { get; set; }
    public int LowBatteryPercent { get; set; } = 20;
    public int CriticalBatteryPercent { get; set; } = 10;
    public int RepeatWarningMinutes { get; set; } = 30;
    // -1 reads the real Windows battery. 0-100 simulates a laptop battery so
    // the feature can be checked on a desktop PC.
    public int TestBatteryPercent { get; set; } = -2;
    public bool TestPluggedIn { get; set; }
}

internal sealed class SavedSelectionState
{
    public Dictionary<string, string> TargetsByProfile { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public string MediaTargetId { get; set; } = string.Empty;
    public List<string> LinkedMediaTargetIds { get; set; } = new();
}

internal sealed class ProfileConfig
{
    public required string Id { get; init; }
    public string Name { get; set; } = "PROFILE";
    public List<string> ActivateWhen { get; set; } = new();
    public ProfileTrigger Trigger { get; set; } = ProfileTrigger.Foreground;
    public List<TargetConfig> Targets { get; } = new();
}

internal enum ProfileTrigger
{
    Foreground,
    Running
}

internal enum TargetKind
{
    Master,
    Game,
    App,
    Sonar,
    Voicemeeter,
    Foreground,
    ActiveAudio,
    Output,
    Media,
    Brightness
}

internal sealed class TargetConfig
{
    public string Id { get; set; } = "target";
    public string Label { get; set; } = string.Empty;
    public TargetKind Type { get; set; } = TargetKind.Master;
    public List<string> Apps { get; set; } = new();
    public string Sonar { get; set; } = string.Empty;
    public string Parameter { get; set; } = string.Empty;
    public string MuteParameter { get; set; } = string.Empty;
    public string Fallback { get; set; } = string.Empty;
    // Zero means: use the global step selected on the controller.
    public int Step { get; set; }
    public double StepDb { get; set; } = 1.0;
    public double MinDb { get; set; } = -60.0;
    public double MaxDb { get; set; } = 12.0;
    public string Color { get; set; } = "#35A7FF";
    public string ClickAction { get; set; } = "auto";
    public string DoubleClickAction { get; set; } = "next_target";

    public string DisplayLabel => string.IsNullOrWhiteSpace(Label)
        ? Id.Replace('_', ' ').ToUpperInvariant()
        : Label;

    public (byte R, byte G, byte B) ParsedColor
    {
        get
        {
            var color = Color.Trim().ToLowerInvariant() switch
            {
                "blue" => "35A7FF",
                "green" => "1DB954",
                "orange" => "FF7139",
                "cyan" => "00A8FF",
                "purple" => "7657FF",
                "mint" => "39D98A",
                "yellow" => "FFB000",
                "pink" => "FF4D8D",
                "red" => "FF3030",
                "white" => "FFFFFF",
                _ => Color.Trim().TrimStart('#')
            };
            if (color.Length == 6 && int.TryParse(color,
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                    out var rgb))
            {
                return ((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }

            return (53, 167, 255);
        }
    }
}

internal sealed record TargetState(
    string Id,
    string Name,
    int Volume,
    bool Muted,
    bool Available,
    string Subtitle,
    (byte R, byte G, byte B) Color)
{
    public static TargetState Offline(TargetConfig target, string reason) =>
        new(target.Id, target.DisplayLabel, 0, false, false, reason,
            target.ParsedColor);
}

internal enum PuckEventKind
{
    Hello,
    Rotate,
    Click,
    DoubleClick,
    TripleClick,
    MediaRotate,
    MediaSelect,
    MediaMute,
    MediaPlayPause,
    MediaNext,
    MediaPrevious,
    MediaSeek,
    RequestMediaTimeline,
    SelectTarget,
    LinkMediaTargets,
    SelectProfile,
    RequestState,
    WifiSaved,
    DeviceSettings,
    HostSettings,
    DialBatterySettings,
    Unknown
}

internal sealed record HostSettingsUpdate(
    string BatteryDisplay,
    bool IdleDashboard,
    bool ShowClock,
    int IdleAfterSeconds);

internal sealed record DialBatterySettingsUpdate(
    double MinVoltage,
    double MaxVoltage,
    string Display);

internal sealed record PuckDeviceSettings(
    int LinkMode = 0,
    int VolumeStep = 4,
    bool TargetConfirm = true,
    int TrackPopupSeconds = 5,
    int MediaTimeoutSeconds = 30,
    int ScreenTimeoutSeconds = 30,
    int LedBrightness = 48,
    int LightMode = 0,
    bool OledInverted = false,
    bool EncoderReversed = false,
    bool Acceleration = true,
    int SolidRed = 0,
    int SolidGreen = 170,
    int SolidBlue = 255,
    bool AutomaticProfiles = true,
    int RainbowSpeed = 1,
    bool RainbowReverse = false,
    int RainbowSpread = 64,
    string DeviceIp = "",
    bool BatteryMonitor = true,
    int SeekStepSeconds = 5,
    int SeekTimeoutSeconds = 5,
    int SeekHoldMs = 1200);

internal sealed record PuckEvent(
    PuckEventKind Kind,
    int Value = 0,
    string Text = "",
    string DeviceId = "",
    int Step = 0,
    PuckDeviceSettings? DeviceSettings = null,
    HostSettingsUpdate? HostSettings = null,
    DialBatterySettingsUpdate? DialBatterySettings = null);

internal enum ConnectionKind
{
    None,
    Usb,
    Wifi
}
