using System.Globalization;
using System.Text;

namespace AudioPuck;

internal static class ConfigLoader
{
    public const int MaximumProfiles = 4;
    public const int MaximumTargetsPerProfile = 16;
    public const int MaximumLinkedMediaTargets = 8;

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("config.toml was not found", path);
        }

        var result = new AppConfig();
        string section = string.Empty;
        ProfileConfig? currentProfile = null;
        TargetConfig? currentTarget = null;

        var lineNumber = 0;
        foreach (var sourceLine in File.ReadAllLines(path, Encoding.UTF8))
        {
            lineNumber++;
            var line = RemoveComment(sourceLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[[", StringComparison.Ordinal) &&
                line.EndsWith("]]", StringComparison.Ordinal))
            {
                section = line[2..^2].Trim();
                var parts = section.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3 || parts[0] != "profile" ||
                    parts[2] != "targets")
                {
                    throw ParseError(lineNumber, "Unsupported array section", line);
                }

                currentProfile = GetOrCreateProfile(result, parts[1]);
                currentTarget = new TargetConfig();
                currentProfile.Targets.Add(currentTarget);
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) &&
                line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line[1..^1].Trim();
                currentTarget = null;
                currentProfile = null;
                if (section.StartsWith("profile.", StringComparison.Ordinal))
                {
                    currentProfile = GetOrCreateProfile(result,
                        section["profile.".Length..]);
                }
                else if (section == "mixer")
                {
                    currentProfile = GetOrCreateProfile(result, "default");
                    currentProfile.Name = "MIXER";
                }
                else if (section == "voicemeeter")
                {
                    currentProfile = GetOrCreateProfile(result, "voicemeeter");
                    currentProfile.Name = "VOICEMEETER";
                }
                continue;
            }

            var equals = FindUnquotedEquals(line);
            if (equals < 1)
            {
                throw ParseError(lineNumber, "Expected key = value", line);
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();

            try
            {
                if (currentTarget is not null)
                {
                    ApplyTarget(currentTarget, key, value);
                }
                else if (currentProfile is not null)
                {
                    ApplyProfile(currentProfile, key, value);
                }
                else if (section == "general")
                {
                    ApplyGeneral(result.General, key, value);
                }
                else if (section is "transport" or "connection")
                {
                    ApplyTransport(result.Transport, key, value);
                }
                else if (section == "media")
                {
                    ApplyMedia(result.Media, key, value);
                }
                else if (section == "battery")
                {
                    ApplyBattery(result.Battery, key, value);
                }
                else if (section == "host_status")
                {
                    ApplyHostStatus(result.HostStatus, key, value);
                }
                else if (section == "steps")
                {
                    result.StepOverrides[NormalizeId(key.Trim().Trim('"'))] =
                        Integer(value);
                }
                else
                {
                    throw new FormatException($"Unknown section [{section}]");
                }
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw ParseError(lineNumber, ex.Message, line);
            }
        }

        ApplyStepOverrides(result);
        Validate(result);
        return result;
    }

    public static void SaveHostDisplaySettings(string path,
        HostStatusConfig config)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
        var section = lines.FindIndex(line =>
            RemoveComment(line).Trim().Equals("[host_status]",
                StringComparison.OrdinalIgnoreCase));
        if (section < 0)
        {
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
            section = lines.Count;
            lines.Add("[host_status]");
        }

        var end = lines.Count;
        for (var i = section + 1; i < lines.Count; i++)
        {
            var trimmed = RemoveComment(lines[i]).Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                end = i;
                break;
            }
        }

        Upsert("battery_display", $"\"{config.BatteryDisplay}\"");
        Upsert("idle_dashboard", config.IdleDashboard ? "true" : "false");
        Upsert("show_clock", config.ShowClock ? "true" : "false");
        Upsert("idle_after_seconds", config.IdleAfterSeconds.ToString(
            CultureInfo.InvariantCulture));
        Upsert("low_battery_warning", config.LowBatteryWarning ? "true" : "false");
        Upsert("low_battery_percent", config.LowBatteryPercent.ToString(
            CultureInfo.InvariantCulture));
        Upsert("critical_battery_percent", config.CriticalBatteryPercent.ToString(
            CultureInfo.InvariantCulture));
        Upsert("repeat_warning_minutes", config.RepeatWarningMinutes.ToString(
            CultureInfo.InvariantCulture));

        var temporary = path + ".host-status.tmp";
        File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
        File.Move(temporary, path, true);

        void Upsert(string key, string value)
        {
            for (var i = section + 1; i < end; i++)
            {
                var clean = RemoveComment(lines[i]).TrimStart();
                if (!clean.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                var equals = FindUnquotedEquals(clean);
                if (equals >= 0 && clean[..equals].Trim().Equals(key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"{key} = {value}";
                    return;
                }
            }
            lines.Insert(end++, $"{key} = {value}");
        }
    }

    public static void SaveBatterySettings(string path, BatteryConfig config)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();
        var section = lines.FindIndex(line =>
            RemoveComment(line).Trim().Equals("[battery]",
                StringComparison.OrdinalIgnoreCase));
        if (section < 0)
        {
            if (lines.Count > 0 && lines[^1].Length > 0) lines.Add(string.Empty);
            section = lines.Count;
            lines.Add("[battery]");
        }

        var end = lines.Count;
        for (var i = section + 1; i < lines.Count; i++)
        {
            if (!RemoveComment(lines[i]).Trim().StartsWith("[",
                    StringComparison.Ordinal)) continue;
            end = i;
            break;
        }

        Upsert("min_voltage", config.MinVoltage.ToString("0.00",
            CultureInfo.InvariantCulture));
        Upsert("max_voltage", config.MaxVoltage.ToString("0.00",
            CultureInfo.InvariantCulture));
        Upsert("display", $"\"{config.Display}\"");

        var temporary = path + ".battery.tmp";
        File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
        File.Move(temporary, path, true);

        void Upsert(string key, string value)
        {
            for (var i = section + 1; i < end; i++)
            {
                var clean = RemoveComment(lines[i]).TrimStart();
                var equals = FindUnquotedEquals(clean);
                if (equals < 0 || !clean[..equals].Trim().Equals(key,
                        StringComparison.OrdinalIgnoreCase)) continue;
                lines[i] = $"{key} = {value}";
                return;
            }
            lines.Insert(end++, $"{key} = {value}");
        }
    }

    private static void ApplyStepOverrides(AppConfig config)
    {
        foreach (var (id, step) in config.StepOverrides)
        {
            var matches = config.Profiles.SelectMany(profile => profile.Targets)
                .Where(target => target.Id.Equals(id,
                    StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
                throw new FormatException($"Step target '{id}' does not exist");
            if (step is < 1 or > 25)
                throw new FormatException($"Step for '{id}' must be between 1 and 25");
            foreach (var target in matches) target.Step = step;
        }
    }

    private static ProfileConfig GetOrCreateProfile(AppConfig config, string id)
    {
        var normalized = NormalizeId(id);
        var profile = config.Profiles.FirstOrDefault(p =>
            p.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (profile is not null)
        {
            return profile;
        }

        profile = new ProfileConfig { Id = normalized, Name = normalized.ToUpperInvariant() };
        config.Profiles.Add(profile);
        return profile;
    }

    private static void ApplyGeneral(GeneralConfig config, string key, string value)
    {
        switch (key)
        {
            case "active_profile": config.ActiveProfile = String(value); break;
            case "poll_interval_ms": config.PollIntervalMs = Integer(value); break;
            case "log_level": config.LogLevel = String(value); break;
            case "start_minimized": config.StartMinimized = Boolean(value); break;
            case "transliterate_oled": config.TransliterateOled = Boolean(value); break;
            default: throw new FormatException($"Unknown general key '{key}'");
        }
    }

    private static void ApplyTransport(TransportConfig config, string key, string value)
    {
        switch (key)
        {
            case "serial_port":
            case "port": config.SerialPort = String(value); break;
            case "serial_baud": config.SerialBaud = Integer(value); break;
            case "wifi_enabled":
            case "wifi": config.WifiEnabled = Boolean(value); break;
            case "discovery_port": config.DiscoveryPort = Integer(value); break;
            case "tcp_port": config.TcpPort = Integer(value); break;
            case "pairing_token":
            case "token": config.PairingToken = String(value); break;
            default: throw new FormatException($"Unknown transport key '{key}'");
        }
    }

    private static void ApplyMedia(MediaConfig config, string key, string value)
    {
        switch (key)
        {
            case "volume_targets":
                config.VolumeTargets = StringArray(value)
                    .Select(NormalizeId).ToList();
                break;
            case "seek_apps":
                config.SeekApps = StringArray(value)
                    .Select(NormalizeProcessName)
                    .Where(app => app.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                break;
            default: throw new FormatException($"Unknown media key '{key}'");
        }
    }

    private static void ApplyBattery(BatteryConfig config, string key,
        string value)
    {
        switch (key)
        {
            case "min_voltage": config.MinVoltage = Number(value); break;
            case "max_voltage": config.MaxVoltage = Number(value); break;
            case "display": config.Display = String(value).Trim().ToLowerInvariant(); break;
            default: throw new FormatException($"Unknown battery key '{key}'");
        }
    }

    private static void ApplyHostStatus(HostStatusConfig config, string key,
        string value)
    {
        switch (key)
        {
            case "battery_display":
                config.BatteryDisplay = String(value).Trim().ToLowerInvariant() switch
                {
                    "badge" => "icon",
                    "idle" => "percent",
                    var mode => mode
                };
                break;
            case "idle_dashboard": config.IdleDashboard = Boolean(value); break;
            case "show_clock": config.ShowClock = Boolean(value); break;
            case "idle_after_seconds": config.IdleAfterSeconds = Integer(value); break;
            case "low_battery_warning": config.LowBatteryWarning = Boolean(value); break;
            case "low_battery_percent": config.LowBatteryPercent = Integer(value); break;
            case "critical_battery_percent": config.CriticalBatteryPercent = Integer(value); break;
            case "repeat_warning_minutes": config.RepeatWarningMinutes = Integer(value); break;
            case "test_battery_percent": config.TestBatteryPercent = Integer(value); break;
            case "test_plugged_in": config.TestPluggedIn = Boolean(value); break;
            default: throw new FormatException($"Unknown host_status key '{key}'");
        }
    }

    private static void ApplyProfile(ProfileConfig profile, string key, string value)
    {
        switch (key)
        {
            case "name": profile.Name = String(value); break;
            case "activate_when": profile.ActivateWhen = StringArray(value)
                    .Select(NormalizeProcessName).ToList(); break;
            case "auto_when": profile.ActivateWhen = StringArray(value)
                    .Select(NormalizeProcessName).ToList(); break;
            case "trigger": profile.Trigger = String(value).ToLowerInvariant() switch
            {
                "foreground" => ProfileTrigger.Foreground,
                "running" => ProfileTrigger.Running,
                _ => throw new FormatException(
                    "Profile trigger must be 'foreground' or 'running'")
            }; break;
            default: AddSimpleTarget(profile, key, value); break;
        }
    }

    private static void AddSimpleTarget(ProfileConfig profile, string key,
        string value)
    {
        var id = NormalizeId(key.Trim().Trim('"'));
        var target = new TargetConfig
        {
            Id = id,
            Color = String(value)
        };

        switch (id)
        {
            case "game":
                target.Type = TargetKind.Game;
                target.Label = "GAME";
                break;
            case "master":
                target.Type = TargetKind.Master;
                break;
            case "current_app":
            case "foreground":
                target.Type = TargetKind.Foreground;
                break;
            case "active_audio":
                target.Type = TargetKind.ActiveAudio;
                break;
            case "output":
                target.Type = TargetKind.Output;
                break;
            case "media":
                target.Type = TargetKind.Media;
                target.DoubleClickAction = "media_next";
                break;
            case "brightness":
                target.Type = TargetKind.Brightness;
                target.Label = "BRIGHTNESS";
                target.ClickAction = "none";
                break;
            case "vm_vaio":
                target.Type = TargetKind.Voicemeeter;
                target.Parameter = "Strip[3].Gain";
                target.MuteParameter = "Strip[3].Mute";
                break;
            case "vm_b1":
                target.Type = TargetKind.Voicemeeter;
                target.Parameter = "Bus[0].Gain";
                target.MuteParameter = "Bus[0].Mute";
                break;
            default:
                if (id.StartsWith("sonar_", StringComparison.Ordinal))
                {
                    target.Type = TargetKind.Sonar;
                    target.Sonar = id["sonar_".Length..];
                    if (id == "sonar_gaming") target.Label = "PC AUDIO";
                }
                else
                {
                    target.Type = TargetKind.App;
                    target.Apps = new() { NormalizeProcessName(id) };
                }
                break;
        }

        profile.Targets.Add(target);
    }

    private static void ApplyTarget(TargetConfig target, string key, string value)
    {
        switch (key)
        {
            case "id": target.Id = NormalizeId(String(value)); break;
            case "label": target.Label = String(value); break;
            case "type": target.Type = ParseTargetKind(String(value)); break;
            case "app": target.Apps = new() { NormalizeProcessName(String(value)) }; break;
            case "apps": target.Apps = StringArray(value)
                    .Select(NormalizeProcessName).ToList(); break;
            case "sonar": target.Sonar = String(value).Trim().ToLowerInvariant(); break;
            case "parameter": target.Parameter = String(value); break;
            case "mute_parameter": target.MuteParameter = String(value); break;
            case "fallback": target.Fallback = NormalizeId(String(value)); break;
            case "step": target.Step = Integer(value); break;
            case "step_db": target.StepDb = Number(value); break;
            case "min_db": target.MinDb = Number(value); break;
            case "max_db": target.MaxDb = Number(value); break;
            case "color": target.Color = String(value); break;
            case "click": target.ClickAction = String(value).Trim().ToLowerInvariant(); break;
            case "double_click": target.DoubleClickAction = String(value).Trim().ToLowerInvariant(); break;
            default: throw new FormatException($"Unknown target key '{key}'");
        }
    }

    private static TargetKind ParseTargetKind(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "master" => TargetKind.Master,
            "game" => TargetKind.Game,
            "app" => TargetKind.App,
            "sonar" => TargetKind.Sonar,
            "voicemeeter" => TargetKind.Voicemeeter,
            "foreground" => TargetKind.Foreground,
            "active_audio" => TargetKind.ActiveAudio,
            "output" => TargetKind.Output,
            "media" => TargetKind.Media,
            "brightness" => TargetKind.Brightness,
            _ => throw new FormatException($"Unknown target type '{value}'")
        };

    public static string NormalizeProcessName(string value)
    {
        var name = value.Trim().Trim('"').Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }
        return name.ToLowerInvariant();
    }

    private static string NormalizeId(string value)
    {
        var result = value.Trim().ToLowerInvariant().Replace(' ', '_');
        if (result.Length == 0 || result.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')))
        {
            throw new FormatException($"Invalid identifier '{value}'");
        }
        return result;
    }

    private static void Validate(AppConfig config)
    {
        if (config.Profiles.Count is < 1 or > MaximumProfiles)
        {
            throw new FormatException($"Expected 1-{MaximumProfiles} profiles, got {config.Profiles.Count}");
        }

        if (config.General.PollIntervalMs is < 100 or > 5000)
        {
            throw new FormatException("poll_interval_ms must be between 100 and 5000");
        }

        if (string.IsNullOrWhiteSpace(config.Transport.PairingToken))
        {
            throw new FormatException("pairing_token must not be empty");
        }

        if (config.Transport.DiscoveryPort is < 1024 or > 65535 ||
            config.Transport.TcpPort is < 1024 or > 65535 ||
            config.Transport.DiscoveryPort == config.Transport.TcpPort)
        {
            throw new FormatException("discovery_port and tcp_port must be distinct ports between 1024 and 65535");
        }

        if (config.Battery.MinVoltage is < 2.5 or > 3.9)
            throw new FormatException(
                "battery min_voltage must be between 2.5 and 3.9 V");
        if (config.Battery.MaxVoltage is < 3.6 or > 4.5)
            throw new FormatException(
                "battery max_voltage must be between 3.6 and 4.5 V");
        if (config.Battery.MaxVoltage - config.Battery.MinVoltage < 0.3)
            throw new FormatException(
                "battery voltage range must be at least 0.3 V");
        if (config.Battery.Display is not ("percent" or "icon" or "off"))
            throw new FormatException(
                "battery display must be 'percent', 'icon' or 'off'");

        if (config.HostStatus.BatteryDisplay is not ("off" or "icon" or "percent"))
            throw new FormatException(
                "host_status battery_display must be 'off', 'icon' or 'percent'");
        if (config.HostStatus.IdleAfterSeconds is < 5 or > 3600)
            throw new FormatException(
                "host_status idle_after_seconds must be between 5 and 3600");
        if (config.HostStatus.CriticalBatteryPercent is < 1 or > 50 ||
            config.HostStatus.LowBatteryPercent is < 2 or > 75 ||
            config.HostStatus.CriticalBatteryPercent >=
            config.HostStatus.LowBatteryPercent)
            throw new FormatException(
                "host_status battery thresholds must satisfy 1 <= critical < low <= 75");
        if (config.HostStatus.RepeatWarningMinutes is < 0 or > 1440)
            throw new FormatException(
                "host_status repeat_warning_minutes must be between 0 and 1440");
        if (config.HostStatus.TestBatteryPercent is < -2 or > 100)
            throw new FormatException(
                "host_status test_battery_percent must be -2, -1 or 0-100");

        foreach (var profile in config.Profiles)
        {
            if (profile.Targets.Count is < 1 or > MaximumTargetsPerProfile)
            {
                throw new FormatException($"Profile '{profile.Id}' must contain 1-{MaximumTargetsPerProfile} targets");
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in profile.Targets)
            {
                if (!ids.Add(target.Id))
                {
                    throw new FormatException($"Duplicate target id '{target.Id}' in profile '{profile.Id}'");
                }
                if (target.Type == TargetKind.App && target.Apps.Count == 0)
                {
                    throw new FormatException($"App target '{target.Id}' has no app/apps value");
                }
                if (target.Type == TargetKind.Sonar && string.IsNullOrWhiteSpace(target.Sonar))
                {
                    throw new FormatException($"Sonar target '{target.Id}' has no sonar channel");
                }
                if (target.Type == TargetKind.Voicemeeter && string.IsNullOrWhiteSpace(target.Parameter))
                {
                    throw new FormatException($"Voicemeeter target '{target.Id}' has no parameter");
                }
                if (target.Step != 0)
                    target.Step = Math.Clamp(target.Step, 1, 25);
            }

        }


        var availableTargets = config.Profiles.SelectMany(p => p.Targets)
            .Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (config.Media.VolumeTargets.Count > MaximumLinkedMediaTargets)
            throw new FormatException(
                $"media volume_targets supports at most {MaximumLinkedMediaTargets} linked targets");
        foreach (var target in config.Media.VolumeTargets)
        {
            if (!availableTargets.Contains(target))
                throw new FormatException($"Media volume target '{target}' does not exist");
        }

        if (!config.Profiles.Any(p => p.Id.Equals(config.General.ActiveProfile,
                StringComparison.OrdinalIgnoreCase)))
        {
            config.General.ActiveProfile = config.Profiles[0].Id;
        }
    }

    private static int FindUnquotedEquals(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) quoted = !quoted;
            if (line[i] == '=' && !quoted) return i;
        }
        return -1;
    }

    private static string RemoveComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) quoted = !quoted;
            if (line[i] == '#' && !quoted) return line[..i];
        }
        return line;
    }

    private static string String(string value)
    {
        value = value.Trim();
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            throw new FormatException("Expected a quoted string");
        }
        return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private static List<string> StringArray(string value)
    {
        value = value.Trim();
        if (!value.StartsWith('[') || !value.EndsWith(']'))
        {
            throw new FormatException("Expected a string array");
        }

        var content = value[1..^1].Trim();
        if (content.Length == 0) return new();
        var result = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        foreach (var ch in content)
        {
            if (ch == '"') quoted = !quoted;
            if (ch == ',' && !quoted)
            {
                result.Add(String(builder.ToString().Trim()));
                builder.Clear();
            }
            else
            {
                builder.Append(ch);
            }
        }
        if (quoted) throw new FormatException("Unterminated string in array");
        result.Add(String(builder.ToString().Trim()));
        return result;
    }

    private static bool Boolean(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => throw new FormatException("Expected true or false")
    };

    private static int Integer(string value) => int.Parse(value,
        NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static double Number(string value) => double.Parse(value,
        NumberStyles.Float, CultureInfo.InvariantCulture);

    private static FormatException ParseError(int line, string message, string content) =>
        new($"config.toml line {line}: {message}. Text: {content}");
}
