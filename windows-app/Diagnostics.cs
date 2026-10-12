using System.Text;

namespace AudioPuck;

internal static class Diagnostics
{
    public static int Run(string configPath)
    {
        try
        {
            var report = BuildReport(configPath, "diagnostic mode", "-", "-");
            Console.WriteLine(report);
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory,
                "SoundDial-diagnostics.txt"), report);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    public static string BuildReport(string configPath, string connection,
        string profile, string target)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SoundDial diagnostics");
        builder.AppendLine($"Time: {DateTime.Now:O}");
        builder.AppendLine($"Config: {configPath}");
        builder.AppendLine($"Connection: {connection}");
        builder.AppendLine($"Selection: {profile} / {target}");
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine($"Runtime: {Environment.Version}");

        var config = ConfigLoader.Load(configPath);
        builder.AppendLine($"Profiles: {config.Profiles.Count}");
        foreach (var item in config.Profiles)
            builder.AppendLine($"  {item.Id}: {item.Targets.Count} targets");

        var audio = new CoreAudioService();
        builder.AppendLine("Render endpoints:");
        foreach (var endpoint in audio.GetRenderEndpoints())
            builder.AppendLine($"  {endpoint.Name} [{endpoint.Id}]");

        builder.AppendLine("Audio sessions:");
        foreach (var session in audio.GetSessions())
            builder.AppendLine($"  {session.ProcessName} | {session.DisplayName} | " +
                               $"{session.Volume}% | mute={session.Muted} | " +
                               $"{session.EndpointName}");
        return builder.ToString();
    }
}
