namespace AudioPuck;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var configPath = ResolveConfigPath(args);
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            return SelfTests.Run(configPath);
        if (args.Contains("--diagnostics", StringComparer.OrdinalIgnoreCase))
            return Diagnostics.Run(configPath);

        using var singleInstance = new Mutex(true, "Local\\SoundDial.PC.Singleton",
            out var created);
        if (!created)
        {
            MessageBox.Show("SoundDial is already running in the system tray.",
                "SoundDial", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 2;
        }

        ApplicationConfiguration.Initialize();
        try
        {
            Application.Run(new AudioPuckContext(configPath));
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Startup error", ex);
            try
            {
                File.WriteAllText(Path.Combine(
                    Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory,
                    "SoundDial-startup-error.txt"), ex.ToString());
            }
            catch { }
            MessageBox.Show(ex.ToString(), "SoundDial startup error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static string ResolveConfigPath(string[] args)
    {
        var option = Array.FindIndex(args, value =>
            value.Equals("--config", StringComparison.OrdinalIgnoreCase));
        if (option >= 0 && option + 1 < args.Length)
            return Path.GetFullPath(args[option + 1]);

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "config.toml"),
            Path.Combine(Environment.CurrentDirectory, "config.toml"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config.toml"))
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
