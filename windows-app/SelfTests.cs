namespace AudioPuck;

internal static class SelfTests
{
    public static int Run(string configPath)
    {
        var failures = new List<string>();
        Check(failures, "config loads", () =>
        {
            var config = ConfigLoader.Load(configPath);
            if (config.Profiles.Count < 1) throw new Exception("Expected a profile");
            if (config.Profiles[0].Targets.Count < 1) throw new Exception("No targets");
            var spotify = config.Profiles.SelectMany(p => p.Targets)
                .FirstOrDefault(t => t.Id == "spotify");
            if (spotify is null || spotify.Type != TargetKind.App ||
                !spotify.Apps.Contains("spotify"))
                throw new Exception("Simple app target was not inferred");
            if (!config.Media.VolumeTargets.SequenceEqual(
                    new[] { "spotify", "chrome", "sonar_media" }))
                throw new Exception("Media volume targets were not loaded");
            if (!config.Media.SeekApps.SequenceEqual(
                    new[] { "firefox", "chrome", "msedge" }))
                throw new Exception("Media seek applications were not loaded");
            var brightness = config.Profiles.SelectMany(p => p.Targets)
                .FirstOrDefault(t => t.Type == TargetKind.Brightness);
            if (brightness is null)
                throw new Exception("Laptop brightness target was not loaded");
            if (Math.Abs(config.Battery.MinVoltage - 3.30) > 0.001 ||
                Math.Abs(config.Battery.MaxVoltage - 4.20) > 0.001 ||
                config.Battery.Display != "percent")
                throw new Exception("Battery voltage thresholds were not loaded");
            if (config.HostStatus.BatteryDisplay != "off" ||
                config.HostStatus.IdleDashboard ||
                !config.HostStatus.ShowClock ||
                config.HostStatus.IdleAfterSeconds != 20 ||
                config.HostStatus.LowBatteryPercent != 20 ||
                config.HostStatus.CriticalBatteryPercent != 10)
                throw new Exception("Host status defaults were not loaded");
            var sonarMedia = config.Profiles.SelectMany(p => p.Targets)
                .FirstOrDefault(t => t.Id == "sonar_media");
            if (sonarMedia?.Step != 1)
                throw new Exception("Per-target step override was not loaded");
            var gaming = config.Profiles.FirstOrDefault(p => p.Id == "gaming");
            if (gaming is null || gaming.Trigger != ProfileTrigger.Foreground ||
                !gaming.ActivateWhen.Contains("cs2"))
                throw new Exception("Automatic gaming profile was not loaded");
            if (gaming.Targets.FirstOrDefault()?.Type != TargetKind.Game)
                throw new Exception("Dynamic game target was not loaded first");
        });
        Check(failures, "process normalization", () =>
        {
            if (ConfigLoader.NormalizeProcessName(" Spotify.EXE ") != "spotify")
                throw new Exception("Normalization failed");
        });
        Check(failures, "sixteen target capacity", () =>
        {
            var source = File.ReadAllText(configPath);
            var additions = string.Join(Environment.NewLine,
                Enumerable.Range(1, 7).Select(index =>
                    $"extra_{index} = \"blue\""));
            var temporary = Path.Combine(Path.GetDirectoryName(
                    Path.GetFullPath(configPath))!,
                $"sounddial-targets-{Guid.NewGuid():N}.toml");
            File.WriteAllText(temporary, source.Replace("\n[steps]\n",
                "\n" + additions + Environment.NewLine + Environment.NewLine +
                "[steps]\n"));
            try
            {
                var loaded = ConfigLoader.Load(temporary);
                if (loaded.Profiles[0].Targets.Count != 16)
                    throw new Exception("A 16-target profile did not load");
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        });
        Check(failures, "host battery simulation", () =>
        {
            var status = HostPowerService.Read(new HostStatusConfig
            {
                TestBatteryPercent = 57,
                TestPluggedIn = true
            });
            if (!status.HasBattery || status.Percent != 57 ||
                !status.PluggedIn)
                throw new Exception("Simulated Windows battery was not returned");
        });
        Check(failures, "desktop battery override", () =>
        {
            var status = HostPowerService.Read(new HostStatusConfig
            {
                TestBatteryPercent = -1
            });
            if (status.HasBattery)
                throw new Exception("Desktop override still exposed a laptop battery");
        });
        Check(failures, "legacy laptop display migration", () =>
        {
            var source = File.ReadAllText(configPath)
                .Replace("battery_display = \"off\"",
                    "battery_display = \"badge\"");
            var temporary = Path.Combine(Path.GetDirectoryName(
                    Path.GetFullPath(configPath))!,
                $"sounddial-legacy-{Guid.NewGuid():N}.toml");
            File.WriteAllText(temporary, source);
            try
            {
                if (ConfigLoader.Load(temporary).HostStatus.BatteryDisplay !=
                    "icon")
                    throw new Exception("Legacy badge mode was not migrated");
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        });
        Check(failures, "host settings persistence", () =>
        {
            var temporary = Path.Combine(Path.GetDirectoryName(
                    Path.GetFullPath(configPath))!,
                $"sounddial-config-{Guid.NewGuid():N}.toml");
            File.Copy(configPath, temporary);
            try
            {
                var original = ConfigLoader.Load(temporary);
                original.HostStatus.BatteryDisplay = "icon";
                original.HostStatus.IdleDashboard = true;
                original.HostStatus.ShowClock = false;
                original.HostStatus.IdleAfterSeconds = 60;
                ConfigLoader.SaveHostDisplaySettings(temporary,
                    original.HostStatus);
                original.Battery.MinVoltage = 3.25;
                original.Battery.MaxVoltage = 4.15;
                original.Battery.Display = "icon";
                ConfigLoader.SaveBatterySettings(temporary, original.Battery);
                var reloaded = ConfigLoader.Load(temporary);
                var saved = reloaded.HostStatus;
                if (saved.BatteryDisplay != "icon" ||
                    !saved.IdleDashboard || saved.ShowClock ||
                    saved.IdleAfterSeconds != 60 ||
                    saved.LowBatteryPercent !=
                        original.HostStatus.LowBatteryPercent)
                    throw new Exception("Host settings were not preserved");
                if (Math.Abs(reloaded.Battery.MinVoltage - 3.25) > 0.001 ||
                    Math.Abs(reloaded.Battery.MaxVoltage - 4.15) > 0.001 ||
                    reloaded.Battery.Display != "icon")
                    throw new Exception("Battery settings were not preserved");
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        });
        Check(failures, "protocol encoding", () =>
        {
            const string source = "SONAR MEDIA / тест";
            if (PuckProtocol.Decode(PuckProtocol.Encode(source)) != source)
                throw new Exception("Round-trip failed");
            var track = PuckProtocol.Track(new MediaTrack("Song", "Artist", "test"), true);
            if (track != "TRACK\tSong\tArtist")
                throw new Exception("TRACK encoding failed");
            var deviceSettings = new PuckDeviceSettings(1, 5, false, 10,
                60, 120, 80, 1, true, true, false, 12, 34, 56,
                false, 2, true, 32);
            if (PuckProtocol.DeviceSettings(deviceSettings) !=
                "SET_DEVICE_CONFIG\t1\t5\t0\t10\t60\t120\t80\t1\t1\t1\t0\t12\t34\t56\t0\t2\t1\t32\t1\t5\t5\t1200")
                throw new Exception("Device settings encoding failed");
            if (PuckProtocol.BatteryCalibration(new BatteryConfig
                    { MinVoltage = 2.8, MaxVoltage = 4.15 }) !=
                "SET_BATTERY_RANGE\t2.80\t4.15\tPERCENT")
                throw new Exception("Battery calibration encoding failed");
            var host = PuckProtocol.HostStatus(new HostStatusConfig
                {
                    BatteryDisplay = "percent", IdleDashboard = true,
                    ShowClock = true, IdleAfterSeconds = 25
                }, new HostPowerStatus(true, 57, false),
                new DateTime(2026, 1, 1, 12, 34, 56));
            if (host != "HOST_STATUS\tPERCENT\t1\t57\t0\t45296\t1\t1\t25")
                throw new Exception("HOST_STATUS encoding failed");
            if (PuckProtocol.HostAlert("LAPTOP LOW 19%", true) !=
                "HOST_ALERT\tLAPTOP LOW 19%25")
                throw new Exception("HOST_ALERT encoding failed");
            var state = PuckProtocol.State(new TargetState("spotify", "SPOTIFY",
                    42, false, true, string.Empty, (29, 185, 84)),
                0, 0, ConnectionKind.Usb, true, 1).Split('\t');
            if (state.Length != 13 || state[11] != "%7E" || state[12] != "1")
                throw new Exception("STATE preview metadata encoding failed");
            var mediaState = PuckProtocol.MediaState(new TargetState("spotify",
                    "SPOTIFY", 42, true, true, string.Empty, (29, 185, 84)),
                0, 3, true, 1).Split('\t');
            if (mediaState.Length != 12 || mediaState[3] != "1" ||
                mediaState[10] != "3" || mediaState[11] != "1")
                throw new Exception("Muted MEDIA_STATE encoding failed");
        });
        Check(failures, "event parsing", () =>
        {
            var parsed = PuckProtocol.ParseEvent("ROTATE\t-3");
            if (parsed.Kind != PuckEventKind.Rotate || parsed.Value != -3)
                throw new Exception("ROTATE parsing failed");
            if (PuckProtocol.ParseEvent("TRIPLE").Kind !=
                PuckEventKind.TripleClick)
                throw new Exception("TRIPLE parsing failed");
            var mediaRotate = PuckProtocol.ParseEvent("MEDIA_ROTATE\t-2\t4");
            if (mediaRotate.Kind != PuckEventKind.MediaRotate ||
                mediaRotate.Value != -2 || mediaRotate.Step != 4)
                throw new Exception("MEDIA_ROTATE parsing failed");
            var linked = PuckProtocol.ParseEvent("MEDIA_LINK_TARGETS\t5");
            if (linked.Kind != PuckEventKind.LinkMediaTargets || linked.Value != 5)
                throw new Exception("MEDIA_LINK_TARGETS parsing failed");
            if (PuckProtocol.ParseEvent("MEDIA_MUTE").Kind !=
                PuckEventKind.MediaMute)
                throw new Exception("MEDIA_MUTE parsing failed");
            var seek=PuckProtocol.ParseEvent("MEDIA_SEEK\t-15");
            if(seek.Kind!=PuckEventKind.MediaSeek||seek.Value!=-15)
                throw new Exception("MEDIA_SEEK parsing failed");
            var hostSettings=PuckProtocol.ParseEvent(
                "HOST_CONFIG\tICON\t1\t0\t30");
            if(hostSettings.Kind!=PuckEventKind.HostSettings||
                hostSettings.HostSettings is not { BatteryDisplay: "icon",
                    IdleDashboard: true, ShowClock: false,
                    IdleAfterSeconds: 30 })
                throw new Exception("HOST_CONFIG parsing failed");
            var dialBattery=PuckProtocol.ParseEvent(
                "DIAL_BATTERY_CONFIG\t3.30\t4.20\tICON");
            if(dialBattery.Kind!=PuckEventKind.DialBatterySettings||
                dialBattery.DialBatterySettings is not { Display: "icon" }||
                Math.Abs(dialBattery.DialBatterySettings.MinVoltage-3.30)>0.001||
                Math.Abs(dialBattery.DialBatterySettings.MaxVoltage-4.20)>0.001)
                throw new Exception("DIAL_BATTERY_CONFIG parsing failed");
            var legacyHost=PuckProtocol.ParseEvent(
                "HOST_CONFIG\tBADGE\t1\t0\t30");
            if(legacyHost.HostSettings?.BatteryDisplay!="icon")
                throw new Exception("Legacy HOST_CONFIG migration failed");
            var settings = PuckProtocol.ParseEvent(
                "DEVICE_CONFIG\t2\t4\t1\t5\t30\t60\t48\t3\t0\t1\t1\t12\t34\t56\t0\t2\t1\t32\t192.168.0.190\t1\t15\t0\t1500");
            if (settings.Kind != PuckEventKind.DeviceSettings ||
                settings.DeviceSettings is not { LinkMode: 2, VolumeStep: 4,
                    ScreenTimeoutSeconds: 60, EncoderReversed: true,
                    LightMode: 3, SolidRed: 12, SolidGreen: 34, SolidBlue: 56,
                    AutomaticProfiles: false, RainbowSpeed: 2,
                    RainbowReverse: true, RainbowSpread: 32,
                    DeviceIp: "192.168.0.190", BatteryMonitor: true,
                    SeekStepSeconds: 15, SeekTimeoutSeconds: 0,
                    SeekHoldMs: 1500 })
                throw new Exception("DEVICE_CONFIG parsing failed");
        });
        Check(failures, "selection persistence format", () =>
        {
            var saved = new SavedSelectionState
            {
                MediaTargetId = "sonar_media",
                LinkedMediaTargetIds = new List<string>
                {
                    "spotify", "sonar_media"
                }
            };
            saved.TargetsByProfile["default"] = "firefox";
            var json = System.Text.Json.JsonSerializer.Serialize(saved);
            var restored = System.Text.Json.JsonSerializer
                .Deserialize<SavedSelectionState>(json);
            if (restored is null ||
                restored.TargetsByProfile["default"] != "firefox" ||
                restored.MediaTargetId != "sonar_media" ||
                !restored.LinkedMediaTargetIds.SequenceEqual(
                    new[] { "spotify", "sonar_media" }))
                throw new Exception("Saved target selection did not round-trip");
        });
        Check(failures, "target selection restore", () =>
        {
            var config = ConfigLoader.Load(configPath);
            using var first = new TargetController(config);
            var selectedTarget = Math.Min(2,
                first.CurrentProfile.Targets.Count - 1);
            first.SelectTarget(selectedTarget);
            first.SetLinkedMediaTargets(5);
            var saved = new SavedSelectionState();
            first.CaptureSelections(saved);

            using var restored = new TargetController(config);
            restored.RestoreSelections(saved);
            if (restored.TargetIndex != selectedTarget ||
                restored.MediaLinkedMask != 5)
                throw new Exception("Target or linked Media group was not restored");
        });

        if (failures.Count == 0)
        {
            Console.WriteLine("All SoundDial self-tests passed.");
            return 0;
        }
        foreach (var failure in failures) Console.Error.WriteLine(failure);
        return 1;
    }

    private static void Check(List<string> failures, string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception ex)
        {
            failures.Add($"FAIL: {name}: {ex.Message}");
        }
    }
}
