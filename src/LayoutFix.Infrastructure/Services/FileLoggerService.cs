using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Infrastructure.Services;

public class FileLoggerService : ILoggerService, IDisposable
{
    private const long MaxLogFileBytes = 5L * 1024 * 1024;
    private static readonly Regex AbsoluteWindowsPathPattern = new(
        @"(?<![\p{L}\p{N}_])(?:[A-Za-z]:[\\/]|\\\\)[^""'\r\n)\]}|]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly ISettingsService? _settingsService;
    private readonly string _logFilePath;
    private readonly object _lock = new object();
    private readonly Mutex? _crossProcessMutex;
    private bool _initialized;
    private bool _disposed;

    public FileLoggerService() : this(null, null)
    {
    }

    public FileLoggerService(ISettingsService settingsService) : this(settingsService, null)
    {
    }

    public FileLoggerService(ISettingsService? settingsService, string? logFilePath)
    {
        _settingsService = settingsService;
        _logFilePath = Path.GetFullPath(logFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LayoutFix",
            "Logs",
            "layoutfix.log"));
        try
        {
            var pathHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(_logFilePath.ToUpperInvariant())));
            _crossProcessMutex = new Mutex(
                initiallyOwned: false,
                $"Local\\LayoutFix.Log.{pathHash}");
        }
        catch
        {
            // Logging must never prevent application startup. The instance lock
            // still provides a safe fallback if the kernel object is unavailable.
        }
    }

    public void LogInfo(string message) => WriteLog("INFO", message);
    public void LogWarning(string message) => WriteLog("WARN", message);
    public void LogError(string message, Exception? ex = null)
    {
        // Exception messages and stack traces can contain request payloads,
        // clipboard contents, document paths, or API credentials. Diagnostics
        // deliberately retain only stable technical identifiers.
        WriteLog(
            "ERROR",
            ex == null
                ? message
                : $"{message} | ExceptionType: {ex.GetType().FullName} | HResult: 0x{ex.HResult:X8}");
    }

    private void WriteLog(string level, string message)
    {
        if (_settingsService?.Current.LoggingEnabled != true)
            return;

        try
        {
            var safeMessage = AbsoluteWindowsPathPattern.Replace(
                message,
                "[absolute-path-redacted]");
            var entry =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {safeMessage}{Environment.NewLine}";
            lock (_lock)
            {
                if (_disposed)
                    return;

                var ownsMutex = false;
                try
                {
                    if (_crossProcessMutex is not null)
                    {
                        try
                        {
                            ownsMutex = _crossProcessMutex.WaitOne(TimeSpan.FromSeconds(2));
                        }
                        catch (AbandonedMutexException)
                        {
                            ownsMutex = true;
                        }

                        if (!ownsMutex)
                            return;
                    }

                    EnsureLogFileReady(Encoding.UTF8.GetByteCount(entry));
                    File.AppendAllText(_logFilePath, entry);
                }
                finally
                {
                    if (ownsMutex)
                        _crossProcessMutex!.ReleaseMutex();
                }
            }
        }
        catch
        {
            // Ignore logging errors to prevent crash
        }
    }

    private void EnsureLogFileReady(int pendingByteCount)
    {
        if (!_initialized)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
            _initialized = true;
        }

        if (File.Exists(_logFilePath) &&
            new FileInfo(_logFilePath).Length + pendingByteCount > MaxLogFileBytes)
            File.Move(_logFilePath, _logFilePath + ".bak", true);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            _crossProcessMutex?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
