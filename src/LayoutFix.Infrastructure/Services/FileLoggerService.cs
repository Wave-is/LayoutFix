using System;
using System.IO;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Infrastructure.Services;

public class FileLoggerService : ILoggerService
{
    private readonly string _logFilePath;
    private readonly object _lock = new object();

    public FileLoggerService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "LayoutFix");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _logFilePath = Path.Combine(dir, "layoutfix.log");
        
        // Rotate if too large (e.g., > 5MB)
        try
        {
            if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length > 5 * 1024 * 1024)
            {
                File.Move(_logFilePath, _logFilePath + ".bak", true);
            }
        }
        catch { }
    }

    public void LogInfo(string message) => WriteLog("INFO", message);
    public void LogWarning(string message) => WriteLog("WARN", message);
    public void LogError(string message, Exception? ex = null)
    {
        WriteLog("ERROR", ex == null ? message : $"{message} | Exception: {ex}");
    }

    private void WriteLog(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Ignore logging errors to prevent crash
        }
    }
}
