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
    private readonly string _logFilePath;
    private readonly Mutex? _crossProcessMutex;
    private readonly Channel<LogCommand> _queue;
    private readonly Task _writerTask;
    private bool _initialized;
    private int _acceptingWrites = 1;

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
            // Logging must never prevent application startup. The single-reader
            // queue still serializes writes from this process when the shared
            // kernel mutex is unavailable.
        }

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
        try
        {
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

                EnsureLogFileReady(Encoding.UTF8.GetByteCount(entries));
                File.AppendAllText(_logFilePath, entries);
            }
            finally
            {
                if (ownsMutex)
                    _crossProcessMutex!.ReleaseMutex();
            }
        }
        catch
        {
            // Ignore logging errors to prevent crash.
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
        try { _crossProcessMutex?.Dispose(); } catch { }
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
