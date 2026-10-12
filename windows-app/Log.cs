using System.Text;

namespace AudioPuck;

internal static class Log
{
    private static readonly object Gate = new();
    private static string _filePath = Path.Combine(AppContext.BaseDirectory,
        "SoundDial.log");
    private static LogLevel _minimum = LogLevel.Info;

    public static event Action<string>? LineWritten;

    public static void Initialize(string filePath, string minimum)
    {
        _minimum = Enum.TryParse<LogLevel>(minimum, true, out var parsed)
            ? parsed
            : LogLevel.Info;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _filePath = filePath;
        }
        catch (UnauthorizedAccessException)
        {
            // Portable/sandboxed launches may not be allowed to write to
            // LocalAppData. Keeping the log beside the executable is more
            // useful than refusing to start the controller.
            _filePath = Path.Combine(AppContext.BaseDirectory, "SoundDial.log");
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message, Exception? exception = null) =>
        Write(LogLevel.Error, exception is null
            ? message
            : $"{message}: {exception.GetType().Name}: {exception.Message}");

    private static void Write(LogLevel level, string message)
    {
        if (level < _minimum)
        {
            return;
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(_filePath, line + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
                // Logging must never take the controller down.
            }
        }

        LineWritten?.Invoke(line);
    }

    private enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }
}
