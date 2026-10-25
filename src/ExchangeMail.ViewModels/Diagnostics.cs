namespace ExchangeMail.ViewModels;

/// <summary>
/// A small local log of errors that the app otherwise survives quietly (background refresh, notifications, streaming)
/// and of crashes. Only error types, messages and stack traces are written, never mail content; the file stays on this
/// computer (%LOCALAPPDATA%\Postaci\postaci.log) until the user chooses to share it.
/// </summary>
public static class Diagnostics
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    /// <summary>Folder of the log; tests point it elsewhere.</summary>
    public static string LogDirectory { get; set; } = AppInfo.DataDirectory;

    public static string LogPath => Path.Combine(LogDirectory, "postaci.log");

    public static void Log(string context, Exception ex) => Write($"{context}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    public static void Log(string message) => Write(message);

    private static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var path = LogPath;
                // Keep one older file, so the log can't grow without bound.
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {text}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the reason something fails.
        }
    }
}
