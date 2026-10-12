using System.Diagnostics;

namespace AudioPuck;

internal sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;
}

internal static class ProcessRunner
{
    private static readonly Dictionary<string, bool> ExistsCache =
        new(StringComparer.Ordinal);
    private static readonly object ExistsGate = new();

    public static bool Exists(string name)
    {
        lock (ExistsGate)
            if (ExistsCache.TryGetValue(name, out var cached)) return cached;
        var result = Run("sh", new[] { "-lc", $"command -v {Shell(name)}" }, 1500);
        var exists = result.Success && result.Output.Trim().Length > 0;
        lock (ExistsGate) ExistsCache[name] = exists;
        return exists;
    }

    public static CommandResult Run(string file, IEnumerable<string> arguments,
        int timeoutMs = 2500)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(true); } catch { }
                return new(-1, string.Empty, $"{file} timed out");
            }
            Task.WaitAll(new Task[] { outputTask, errorTask }, 500);
            return new(process.ExitCode, outputTask.Result, errorTask.Result);
        }
        catch (Exception ex)
        {
            return new(-1, string.Empty, ex.Message);
        }
    }

    private static string Shell(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}
