using System.Text;

namespace FLPresence.Core;

public enum LogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 }

/// <summary>Thread-safe rolling-ish file logger: %LOCALAPPDATA%\FLPresence\logs\flpresence.log</summary>
public sealed class FileLogger
{
    private readonly object _lock = new();
    private readonly string _path;
    private readonly LogLevel _min;
    private const long MaxBytes = 2 * 1024 * 1024;

    public string FilePath => _path;

    public FileLogger(LogLevel min = LogLevel.Info, string? path = null)
    {
        _min = min;
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FLPresence", "logs", "flpresence.log");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public void Debug(string msg) => Log(LogLevel.Debug, msg);
    public void Info(string msg) => Log(LogLevel.Info, msg);
    public void Warn(string msg) => Log(LogLevel.Warn, msg);
    public void Error(string msg) => Log(LogLevel.Error, msg);

    public void Error(string msg, Exception ex) =>
        Log(LogLevel.Error, $"{msg}: {ex.GetType().Name}: {ex.Message}");

    public void Log(LogLevel level, string msg)
    {
        if (level < _min) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {msg}";
        lock (_lock)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                    File.WriteAllText(_path, "", Encoding.UTF8); // ponytail: truncate instead of rotating files
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception) { /* logging must never throw */ }
        }
    }
}
