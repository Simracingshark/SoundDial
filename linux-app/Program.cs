namespace AudioPuck;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var config = ResolveConfig(args);
        if (args.Contains("--self-test")) return SelfTests.Run(config);
        if (args.Contains("--diagnostics")) return Diagnostics(config);
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("This build is the SoundDial Linux host.");
            return 2;
        }
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        { eventArgs.Cancel = true; stop.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
        try
        {
            using var app = new LinuxContext(config);
            await app.RunAsync(stop.Token);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static string ResolveConfig(string[] args)
    {
        var index = Array.FindIndex(args, value => value == "--config");
        if (index >= 0 && index + 1 < args.Length) return Path.GetFullPath(args[index + 1]);
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "config.toml"),
            Path.Combine(Environment.CurrentDirectory, "config.toml"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "sounddial", "config.toml")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static int Diagnostics(string config)
    {
        Console.WriteLine("SoundDial Linux diagnostics");
        Console.WriteLine($"Config: {config} ({(File.Exists(config) ? "found" : "missing")})");
        Console.WriteLine($"wpctl: {(ProcessRunner.Exists("wpctl") ? "OK" : "MISSING")}");
        Console.WriteLine($"pactl: {(ProcessRunner.Exists("pactl") ? "OK" : "MISSING")}");
        Console.WriteLine($"playerctl: {(ProcessRunner.Exists("playerctl") ? "OK" : "MISSING")}");
        Console.WriteLine($"Serial ports: {string.Join(", ", NativeSerialPort.EnumeratePortNames())}");
        return 0;
    }
}
