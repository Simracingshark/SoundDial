using System.Text;

namespace AudioPuck;

internal static class PuckProtocol
{
    public const int Version = 1;

    public static PuckEvent ParseEvent(string line)
    {
        var parts = line.Trim().Split('\t');
        if (parts.Length == 0) return new(PuckEventKind.Unknown);
        switch (parts[0].ToUpperInvariant())
        {
            case "HELLO":
                return new(PuckEventKind.Hello,
                    Text: parts.Length > 1 ? Decode(parts[1]) : string.Empty,
                    DeviceId: parts.Length > 2 ? Decode(parts[2]) : string.Empty);
            case "ROTATE":
                return new(PuckEventKind.Rotate,
                    parts.Length > 1 && int.TryParse(parts[1], out var delta)
                        ? Math.Clamp(delta, -20, 20) : 0,
                    Step: parts.Length > 2 && int.TryParse(parts[2], out var step)
                        ? Math.Clamp(step, 1, 10) : 0);
            case "CLICK": return new(PuckEventKind.Click);
            case "DOUBLE": return new(PuckEventKind.DoubleClick);
            case "TRIPLE": return new(PuckEventKind.TripleClick);
            case "MEDIA_ROTATE":
                return new(PuckEventKind.MediaRotate,
                    parts.Length > 1 && int.TryParse(parts[1], out var mediaDelta)
                        ? Math.Clamp(mediaDelta, -20, 20) : 0,
                    Step: parts.Length > 2 && int.TryParse(parts[2], out var mediaStep)
                        ? Math.Clamp(mediaStep, 1, 10) : 0);
            case "MEDIA_SELECT":
                return new(PuckEventKind.MediaSelect,
                    parts.Length > 1 && int.TryParse(parts[1], out var mediaTarget)
                        ? mediaTarget : -1);
            case "MEDIA_MUTE": return new(PuckEventKind.MediaMute);
            case "MEDIA_PLAY": return new(PuckEventKind.MediaPlayPause);
            case "MEDIA_NEXT": return new(PuckEventKind.MediaNext);
            case "MEDIA_PREV": return new(PuckEventKind.MediaPrevious);
            case "MEDIA_SEEK":
                return new(PuckEventKind.MediaSeek,
                    parts.Length > 1 && int.TryParse(parts[1], out var seekDelta)
                        ? Math.Clamp(seekDelta, -120, 120) : 0);
            case "MEDIA_TIMELINE_REQUEST":
                return new(PuckEventKind.RequestMediaTimeline);
            case "TARGET":
                return new(PuckEventKind.SelectTarget,
                    parts.Length > 1 && int.TryParse(parts[1], out var target)
                        ? target : -1);
            case "MEDIA_LINK_TARGETS":
                return new(PuckEventKind.LinkMediaTargets,
                    parts.Length > 1 && int.TryParse(parts[1], out var mask)
                        ? mask : 0);
            case "PROFILE":
                return new(PuckEventKind.SelectProfile,
                    parts.Length > 1 && int.TryParse(parts[1], out var profile)
                        ? profile : -1);
            case "GET_STATE": return new(PuckEventKind.RequestState);
            case "WIFI_SAVED": return new(PuckEventKind.WifiSaved);
            case "HOST_CONFIG":
                if (parts.Length < 5) return new(PuckEventKind.Unknown, Text: line);
                var hostMode = parts[1].ToLowerInvariant();
                hostMode = hostMode switch
                {
                    "badge" => "icon",
                    "idle" => "percent",
                    _ => hostMode
                };
                if (hostMode is not ("off" or "icon" or "percent"))
                    return new(PuckEventKind.Unknown, Text: line);
                return new(PuckEventKind.HostSettings, HostSettings:
                    new HostSettingsUpdate(hostMode, parts[2] == "1",
                        parts[3] == "1", Number(parts[4], 5, 3600, 20)));
            case "DIAL_BATTERY_CONFIG":
                if (parts.Length < 4 ||
                    !double.TryParse(parts[1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var batteryMin) ||
                    !double.TryParse(parts[2],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var batteryMax))
                    return new(PuckEventKind.Unknown, Text: line);
                var dialMode = parts[3].ToLowerInvariant();
                if (batteryMin is < 2.5 or > 3.9 ||
                    batteryMax is < 3.6 or > 4.5 ||
                    batteryMax - batteryMin < 0.3 ||
                    dialMode is not ("off" or "icon" or "percent"))
                    return new(PuckEventKind.Unknown, Text: line);
                return new(PuckEventKind.DialBatterySettings,
                    DialBatterySettings: new DialBatterySettingsUpdate(
                        batteryMin, batteryMax, dialMode));
            case "DEVICE_CONFIG":
                if (parts.Length < 12) return new(PuckEventKind.Unknown, Text: line);
                return new(PuckEventKind.DeviceSettings, DeviceSettings:
                    new PuckDeviceSettings(
                        Number(parts[1], 0, 3, 0),
                        Number(parts[2], 1, 10, 4),
                        parts[3] == "1",
                        Number(parts[4], 0, 10, 5),
                        Number(parts[5], 0, 3600, 30),
                        Number(parts[6], 0, 3600, 30),
                        Number(parts[7], 0, 160, 48),
                        Number(parts[8], 0, 3, 0),
                        parts[9] == "1",
                        parts[10] == "1",
                        parts[11] == "1",
                        parts.Length > 12 ? Number(parts[12], 0, 255, 0) : 0,
                        parts.Length > 13 ? Number(parts[13], 0, 255, 170) : 170,
                        parts.Length > 14 ? Number(parts[14], 0, 255, 255) : 255,
                        parts.Length <= 15 || parts[15] == "1",
                        parts.Length > 16 ? Number(parts[16], 0, 2, 1) : 1,
                        parts.Length > 17 && parts[17] == "1",
                        parts.Length > 18 ? Number(parts[18], 16, 96, 64) : 64,
                        parts.Length > 19 ? Decode(parts[19]) : string.Empty,
                        parts.Length <= 20 || parts[20] == "1",
                        parts.Length > 21 ? Number(parts[21], 5, 30, 5) : 5,
                        parts.Length > 22 ? Number(parts[22], 0, 10, 5) : 5,
                        parts.Length > 23 ? Number(parts[23], 800, 2000, 1200) : 1200));
            default: return new(PuckEventKind.Unknown, Text: line);
        }
    }

    public static string DeviceSettings(PuckDeviceSettings value) =>
        string.Join('\t', "SET_DEVICE_CONFIG", value.LinkMode,
            value.VolumeStep, value.TargetConfirm ? 1 : 0,
            value.TrackPopupSeconds, value.MediaTimeoutSeconds,
            value.ScreenTimeoutSeconds, value.LedBrightness,
            value.LightMode, value.OledInverted ? 1 : 0,
            value.EncoderReversed ? 1 : 0, value.Acceleration ? 1 : 0,
            value.SolidRed, value.SolidGreen, value.SolidBlue,
            value.AutomaticProfiles ? 1 : 0, value.RainbowSpeed,
            value.RainbowReverse ? 1 : 0, value.RainbowSpread,
            value.BatteryMonitor ? 1 : 0, value.SeekStepSeconds,
            value.SeekTimeoutSeconds, value.SeekHoldMs);

    public static string BatteryCalibration(BatteryConfig value) =>
        string.Join('\t', "SET_BATTERY_RANGE",
            value.MinVoltage.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            value.MaxVoltage.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            value.Display.ToUpperInvariant());

    public static string HostStatus(HostStatusConfig config,
        HostPowerStatus status, DateTime localTime) => string.Join('\t',
            "HOST_STATUS",
            config.BatteryDisplay.ToUpperInvariant(),
            status.HasBattery ? 1 : 0,
            Math.Clamp(status.Percent, 0, 100),
            status.PluggedIn ? 1 : 0,
            (int)localTime.TimeOfDay.TotalSeconds,
            config.IdleDashboard ? 1 : 0,
            config.ShowClock ? 1 : 0,
            config.IdleAfterSeconds);

    public static string HostAlert(string text, bool transliterate) =>
        "HOST_ALERT\t" + Encode(PrepareText(text, transliterate, 28));

    public static string MediaTimeline(MediaTimeline timeline,
        bool transliterate) => string.Join('\t', "MEDIA_TIMELINE",
            timeline.Seekable ? 1 : 0,
            Math.Max(0, (long)timeline.Position.TotalSeconds),
            Math.Max(0, (long)timeline.Duration.TotalSeconds),
            Encode(PrepareText(timeline.Source, transliterate, 24)));

    private static int Number(string value, int minimum, int maximum,
        int fallback) => int.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;

    public static string State(TargetState state, int profileIndex,
        int targetIndex, ConnectionKind connection, bool transliterate,
        int stepOverride = 0)
    {
        var name = PrepareText(state.Name, transliterate, 24);
        // The firmware trims incoming lines. A truly empty final field would
        // therefore disappear together with its trailing tab and invalidate
        // the entire STATE packet. "~" is an explicit empty-text sentinel.
        var subtitle = PrepareText(state.Subtitle, transliterate, 24);
        if (subtitle.Length == 0) subtitle = "~";
        return string.Join('\t',
            "STATE",
            profileIndex,
            targetIndex,
            state.Volume,
            state.Muted ? 1 : 0,
            state.Available ? 1 : 0,
            connection.ToString().ToUpperInvariant(),
            state.Color.R,
            state.Color.G,
            state.Color.B,
            Encode(name),
            Encode(subtitle),
            Math.Clamp(stepOverride, 0, 10));
    }

    public static string Targets(IEnumerable<string> names, bool transliterate) =>
        "TARGETS\t" + string.Join('|', names.Select(name =>
            Encode(PrepareText(name, transliterate, 24))));

    public static string MediaTargets(IEnumerable<string> names,
        bool transliterate) =>
        "MEDIA_TARGETS\t" + string.Join('|', names.Select(name =>
            Encode(PrepareText(name, transliterate, 24))));

    public static string MediaState(TargetState state, int targetIndex,
        int linkedMask, bool transliterate, int stepOverride = 0)
    {
        var name = PrepareText(state.Name, transliterate, 24);
        var subtitle = PrepareText(state.Subtitle, transliterate, 24);
        if (subtitle.Length == 0) subtitle = "~";
        return string.Join('\t', "MEDIA_STATE", targetIndex, state.Volume,
            state.Muted ? 1 : 0, state.Available ? 1 : 0,
            state.Color.R, state.Color.G, state.Color.B,
            Encode(name), Encode(subtitle), linkedMask,
            Math.Clamp(stepOverride, 0, 10));
    }

    public static string Profiles(IEnumerable<string> names, bool transliterate) =>
        "PROFILES\t" + string.Join('|', names.Select(name =>
            Encode(PrepareText(name, transliterate, 24))));

    public static string Message(string text, bool transliterate) =>
        "MESSAGE\t" + Encode(PrepareText(text, transliterate, 28));

    public static string Track(MediaTrack track, bool transliterate) =>
        string.Join('\t', "TRACK",
            Encode(PrepareText(track.Title, transliterate, 64)),
            Encode(PrepareText(track.Artist, transliterate, 48)));

    public static string Encode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var builder = new StringBuilder(bytes.Length);
        foreach (var valueByte in bytes)
        {
            if ((valueByte >= (byte)'A' && valueByte <= (byte)'Z') ||
                (valueByte >= (byte)'a' && valueByte <= (byte)'z') ||
                (valueByte >= (byte)'0' && valueByte <= (byte)'9') ||
                valueByte is (byte)'-' or (byte)'_' or (byte)' ' or (byte)'.')
            {
                builder.Append((char)valueByte);
            }
            else
            {
                builder.Append('%').Append(valueByte.ToString("X2"));
            }
        }
        return builder.ToString();
    }

    public static string Decode(string value)
    {
        using var bytes = new MemoryStream();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '%' && i + 2 < value.Length &&
                byte.TryParse(value.AsSpan(i + 1, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var decoded))
            {
                bytes.WriteByte(decoded);
                i += 2;
            }
            else
            {
                foreach (var b in Encoding.UTF8.GetBytes(new[] { value[i] }))
                    bytes.WriteByte(b);
            }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public static string PrepareText(string value, bool transliterate,
        int maximumLength)
    {
        var prepared = transliterate ? Transliterate(value) : value;
        prepared = new string(prepared.Where(ch => ch >= 32 && ch != '\t' && ch != '|')
            .ToArray()).Trim();
        return prepared.Length <= maximumLength
            ? prepared
            : prepared[..Math.Max(1, maximumLength - 1)] + "~";
    }

    private static string Transliterate(string value)
    {
        var map = new Dictionary<char, string>
        {
            ['А']="A",['Б']="B",['В']="V",['Г']="G",['Д']="D",['Е']="E",['Ё']="E",
            ['Ж']="ZH",['З']="Z",['И']="I",['Й']="Y",['К']="K",['Л']="L",['М']="M",
            ['Н']="N",['О']="O",['П']="P",['Р']="R",['С']="S",['Т']="T",['У']="U",
            ['Ф']="F",['Х']="KH",['Ц']="TS",['Ч']="CH",['Ш']="SH",['Щ']="SCH",
            ['Ъ']="",['Ы']="Y",['Ь']="",['Э']="E",['Ю']="YU",['Я']="YA",
            ['а']="a",['б']="b",['в']="v",['г']="g",['д']="d",['е']="e",['ё']="e",
            ['ж']="zh",['з']="z",['и']="i",['й']="y",['к']="k",['л']="l",['м']="m",
            ['н']="n",['о']="o",['п']="p",['р']="r",['с']="s",['т']="t",['у']="u",
            ['ф']="f",['х']="kh",['ц']="ts",['ч']="ch",['ш']="sh",['щ']="sch",
            ['ъ']="",['ы']="y",['ь']="",['э']="e",['ю']="yu",['я']="ya"
        };
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
            builder.Append(map.TryGetValue(ch, out var replacement) ? replacement : ch);
        return builder.ToString();
    }
}
