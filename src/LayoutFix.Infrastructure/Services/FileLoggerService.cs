using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Infrastructure.Services;

public class FileLoggerService : ILoggerService, IDisposable
{
    private const long MaxLogFileBytes = 5L * 1024 * 1024;
    private static readonly Regex AbsoluteWindowsPathPattern = new(
        @"(?<![\p{L}\p{N}_])(?:[A-Za-z]:[\\/]|\\\\)[^""'\r\n)\]}|]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly ISettingsService? _settingsService;
    private readonly string? _fixedLogFilePath;
    private readonly string _defaultLogFilePath;
    private readonly Channel<LogCommand> _queue;
    private readonly Task _writerTask;
    private Task<bool>? _pendingDestinationWrite;
    private string? _pendingDestinationPath;
    private string? _retryDestinationPath;
    private string? _fallbackNoticeDestinationPath;
    private DateTime _customRetryAfterUtc;
    private int _acceptingWrites = 1;

    public FileLoggerService() : this(null, null)
    {
    }

    public FileLoggerService(ISettingsService settingsService) : this(settingsService, null)
    {
    }

    public FileLoggerService(ISettingsService? settingsService, string? logFilePath)
        : this(settingsService, logFilePath, DiagnosticLogPath.DefaultFilePath)
    {
    }

    internal FileLoggerService(
        ISettingsService? settingsService,
        string? logFilePath,
        string defaultLogFilePath)
    {
        _settingsService = settingsService;
        _fixedLogFilePath = string.IsNullOrWhiteSpace(logFilePath)
            ? null
            : Path.GetFullPath(logFilePath);
        _defaultLogFilePath = Path.GetFullPath(defaultLogFilePath);

        _queue = Channel.CreateUnbounded<LogCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _writerTask = Task.Run(ProcessQueueAsync);
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
            if (Volatile.Read(ref _acceptingWrites) != 0)
                _queue.Writer.TryWrite(LogCommand.Write(entry));
        }
        catch
        {
            // Ignore logging errors to prevent crash
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync())
            {
                if (!_queue.Reader.TryRead(out var command))
                    continue;

                var batch = new StringBuilder();
                TaskCompletionSource? flush = null;
                AddCommand(command, batch, ref flush);

                // A manual correction emits a compact burst of diagnostic lines.
                // Coalesce that burst off the hotkey path so diagnostics do not
                // translate into a separate disk open and mutex hand-off per line.
                if (flush == null && batch.Length > 0)
                    await Task.Delay(2);

                while (flush == null && _queue.Reader.TryRead(out command))
                    AddCommand(command, batch, ref flush);

                if (batch.Length > 0)
                    WriteBatch(batch.ToString());
                flush?.TrySetResult();
            }
        }
        catch
        {
            // Logging must never crash the application.
        }
        finally
        {
            while (_queue.Reader.TryRead(out var pending))
                pending.FlushCompletion?.TrySetResult();
        }
    }

    private static void AddCommand(
        LogCommand command,
        StringBuilder batch,
        ref TaskCompletionSource? flush)
    {
        if (command.Entry != null)
            batch.Append(command.Entry);
        else
            flush = command.FlushCompletion;
    }

    private void WriteBatch(string entries)
    {
        var destinationPath = ResolveDestinationPath();
        if (_fixedLogFilePath != null ||
            string.Equals(
                destinationPath,
                _defaultLogFilePath,
                StringComparison.OrdinalIgnoreCase))
        {
            TryWriteFile(destinationPath, entries);
            return;
        }

        AbandonPendingDestinationIfChanged(destinationPath);
        CompletePendingDestinationWrite();
        if (_pendingDestinationWrite == null && CanRetryCustomDestination(destinationPath))
        {
            var destinationWrite = Task.Run(() => TryWriteFile(destinationPath, entries));
            if (destinationWrite.Wait(TimeSpan.FromSeconds(2)))
            {
                if (GetWriteResult(destinationWrite))
                {
                    ResetDestinationFailure(destinationPath);
                    return;
                }

                DelayDestinationRetry(destinationPath);
            }
            else
            {
                // Network filesystem calls do not provide reliable cancellation.
                // Keep at most one unresolved write and send later batches to the
                // private local fallback so diagnostics never block a hotkey or
                // application shutdown.
                _pendingDestinationWrite = destinationWrite;
                _pendingDestinationPath = destinationPath;
            }
        }

        TryWriteFile(
            _defaultLogFilePath,
            AddFallbackNotice(destinationPath, entries));
    }

    private string ResolveDestinationPath()
    {
        if (_fixedLogFilePath != null)
            return _fixedLogFilePath;

        try
        {
            return _settingsService == null
                ? _defaultLogFilePath
                : ResolveSettingsDestination(_settingsService.Current);
        }
        catch
        {
            return _defaultLogFilePath;
        }
    }

    private string ResolveSettingsDestination(LayoutFix.Core.Models.AppSettings settings) =>
        DiagnosticLogPath.TryNormalizeDirectory(
            settings.DiagnosticLogDirectory,
            out var directory) &&
        !string.IsNullOrEmpty(directory)
            ? Path.Combine(directory, DiagnosticLogPath.FileName)
            : _defaultLogFilePath;

    private void CompletePendingDestinationWrite()
    {
        if (_pendingDestinationWrite?.IsCompleted != true)
            return;

        var completedPath = _pendingDestinationPath;
        if (GetWriteResult(_pendingDestinationWrite) && completedPath != null)
            ResetDestinationFailure(completedPath);
        else if (completedPath != null)
            DelayDestinationRetry(completedPath);

        _pendingDestinationWrite = null;
        _pendingDestinationPath = null;
    }

    private void AbandonPendingDestinationIfChanged(string destinationPath)
    {
        if (_pendingDestinationWrite == null ||
            _pendingDestinationWrite.IsCompleted ||
            string.Equals(
                _pendingDestinationPath,
                destinationPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // A permanently stalled network share must not pin logging to the local
        // fallback after the user selects a different destination. The old task
        // owns only immutable path/entry data and can safely finish on its own.
        _pendingDestinationWrite = null;
        _pendingDestinationPath = null;
    }

    private bool CanRetryCustomDestination(string destinationPath) =>
        !string.Equals(_retryDestinationPath, destinationPath, StringComparison.OrdinalIgnoreCase) ||
        DateTime.UtcNow >= _customRetryAfterUtc;

    private void DelayDestinationRetry(string destinationPath)
    {
        _retryDestinationPath = destinationPath;
        _customRetryAfterUtc = DateTime.UtcNow.AddSeconds(30);
    }

    private void ResetDestinationFailure(string destinationPath)
    {
        if (string.Equals(
                _retryDestinationPath,
                destinationPath,
                StringComparison.OrdinalIgnoreCase))
        {
            _retryDestinationPath = null;
            _customRetryAfterUtc = default;
        }

        if (string.Equals(
                _fallbackNoticeDestinationPath,
                destinationPath,
                StringComparison.OrdinalIgnoreCase))
        {
            _fallbackNoticeDestinationPath = null;
        }
    }

    private string AddFallbackNotice(string destinationPath, string entries)
    {
        if (string.Equals(
                _fallbackNoticeDestinationPath,
                destinationPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return entries;
        }

        _fallbackNoticeDestinationPath = destinationPath;
        return $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [WARN] " +
               "Configured diagnostic log directory is unavailable; using private local fallback." +
               Environment.NewLine + entries;
    }

    private static bool GetWriteResult(Task<bool> writeTask)
    {
        try
        {
            return writeTask.GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
    }

    private static bool TryWriteFile(string logFilePath, string entries)
    {
        Mutex? crossProcessMutex = null;
        var ownsMutex = false;
        try
        {
            try
            {
                var pathHash = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(logFilePath.ToUpperInvariant())));
                crossProcessMutex = new Mutex(
                    initiallyOwned: false,
                    $"Local\\LayoutFix.Log.{pathHash}");
                try
                {
                    ownsMutex = crossProcessMutex.WaitOne(TimeSpan.FromSeconds(2));
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                    return false;
            }
            catch
            {
                // The process-local single-reader queue still serializes writes
                // if the shared kernel mutex cannot be created.
            }

            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
            var pendingByteCount = Encoding.UTF8.GetByteCount(entries);
            if (File.Exists(logFilePath) &&
                new FileInfo(logFilePath).Length + pendingByteCount > MaxLogFileBytes)
            {
                File.Move(logFilePath, logFilePath + ".bak", true);
            }

            File.AppendAllText(logFilePath, entries);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (ownsMutex)
            {
                try { crossProcessMutex!.ReleaseMutex(); } catch { }
            }
            crossProcessMutex?.Dispose();
        }
    }

    public void Flush()
    {
        if (Volatile.Read(ref _acceptingWrites) == 0)
        {
            try { _writerTask.GetAwaiter().GetResult(); } catch { }
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (_queue.Writer.TryWrite(LogCommand.Flush(completion)))
        {
            try
            {
                completion.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                // A broken log destination must not stall or fail application shutdown.
            }
            return;
        }

        try { _writerTask.GetAwaiter().GetResult(); } catch { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _acceptingWrites, 0) == 0)
            return;

        _queue.Writer.TryComplete();
        try { _writerTask.GetAwaiter().GetResult(); } catch { }
        GC.SuppressFinalize(this);
    }

    private readonly record struct LogCommand(
        string? Entry,
        TaskCompletionSource? FlushCompletion)
    {
        public static LogCommand Write(string entry) => new(entry, null);
        public static LogCommand Flush(TaskCompletionSource completion) => new(null, completion);
    }
}
