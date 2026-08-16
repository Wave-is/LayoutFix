using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;

namespace LayoutFix.Services;

/// <summary>
/// Keeps native model code outside the tray/input process. A timeout,
/// cancellation, broken protocol, or native crash tears down only the worker.
/// </summary>
public sealed class OfflineTranslationWorkerClient : IOfflineTranslationService, IDisposable
{
    private const int MaximumInputLength = 3_000;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TranslationTimeout = TimeSpan.FromMinutes(2);

    private readonly ISettingsService _settings;
    private readonly ILoggerService _logger;
    private readonly string _applicationPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _processSync = new();
    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private string? _workerModelId;
    private bool _disposed;

    public OfflineTranslationWorkerClient(ISettingsService settings, ILoggerService logger)
        : this(settings, logger, Path.Combine(AppContext.BaseDirectory, "LayoutFix.exe"))
    {
    }

    internal OfflineTranslationWorkerClient(
        ISettingsService settings,
        ILoggerService logger,
        string applicationPath)
    {
        _settings = settings;
        _logger = logger;
        _applicationPath = Path.GetFullPath(applicationPath);
    }

    public bool IsModelAvailable()
    {
        var descriptor = OfflineModelCatalog.Get(_settings.Current.OfflineModelType);
        return IsModelAvailable(descriptor);
    }

    private static bool IsModelAvailable(OfflineModelDescriptor descriptor)
    {
        var path = OfflineModelLocator.GetModelPath(descriptor.Id);
        return OfflineModelCatalog.IsInstalled(path, descriptor);
    }

    public async Task<string> TranslateAsync(
        string text,
        string targetLanguageCode,
        string sourceLanguageCode = "auto",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (text.Length > MaximumInputLength)
            throw new ArgumentException($"Offline translation input exceeds {MaximumInputLength} characters.", nameof(text));
        if (string.IsNullOrWhiteSpace(targetLanguageCode))
            throw new ArgumentException("Target language is required.", nameof(targetLanguageCode));
        targetLanguageCode = targetLanguageCode.Trim().ToLowerInvariant();
        var model = OfflineModelCatalog.Get(_settings.Current.OfflineModelType);
        if (!OfflineModelCatalog.SupportsTargetLanguage(model.Id, targetLanguageCode))
            throw new NotSupportedException(
                $"Offline model '{model.Id}' has not passed the '{targetLanguageCode}' quality gate. " +
                "Choose a validated model or enable online translation.");
        if (!IsModelAvailable(model))
            throw new FileNotFoundException("Offline translation model is not downloaded.");

        await _gate.WaitAsync(cancellationToken);
        Process? worker = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var timeout = new CancellationTokenSource(TranslationTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);

            worker = await EnsureWorkerAsync(model.Id, linked.Token);
            var request = new OfflineTranslationRequest(
                text,
                targetLanguageCode,
                string.IsNullOrWhiteSpace(sourceLanguageCode) ? "auto" : sourceLanguageCode);
            var json = JsonSerializer.Serialize(request);
            await _writer!.WriteLineAsync(json.AsMemory(), linked.Token);
            await _writer.FlushAsync(linked.Token);

            var responseLine = await _reader!.ReadLineAsync(linked.Token);
            if (responseLine == null)
                throw new IOException("Offline translation worker exited without a response.");

            var response = JsonSerializer.Deserialize<OfflineTranslationResponse>(responseLine)
                ?? throw new InvalidDataException("Offline translation worker returned an invalid response.");
            if (!response.Success)
                throw new InvalidOperationException(response.Error ?? "Offline translation failed.");
            if (string.IsNullOrWhiteSpace(response.Translation))
                throw new InvalidDataException("Offline translation worker returned an empty translation.");

            return response.Translation;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StopWorker(worker);
            throw new TimeoutException($"Offline translation exceeded {TranslationTimeout.TotalSeconds:0} seconds.");
        }
        catch
        {
            StopWorker(worker);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Process> EnsureWorkerAsync(
        string modelId,
        CancellationToken cancellationToken)
    {
        lock (_processSync)
        {
            if (_process is { HasExited: false } &&
                _pipe?.IsConnected == true &&
                WorkerModelMatches(_workerModelId, modelId))
            {
                return _process;
            }
        }

        StopWorker();
        var pipeName = $"LayoutFix-Translation-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var executable = _applicationPath;
        if (!File.Exists(executable))
        {
            pipe.Dispose();
            throw new FileNotFoundException("LayoutFix translation worker executable was not found.", executable);
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        process.StartInfo.ArgumentList.Add("--translation-worker");
        process.StartInfo.ArgumentList.Add(pipeName);
        process.StartInfo.ArgumentList.Add(modelId);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Offline translation worker could not be started.");

            using var startup = new CancellationTokenSource(StartupTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                startup.Token);
            try
            {
                await pipe.WaitForConnectionAsync(linked.Token);
            }
            catch (OperationCanceledException) when (
                startup.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Offline translation worker did not connect within {StartupTimeout.TotalSeconds:0} seconds.");
            }
            var reader = new StreamReader(pipe, leaveOpen: true);
            var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            lock (_processSync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _process = process;
                _pipe = pipe;
                _reader = reader;
                _writer = writer;
                _workerModelId = modelId;
            }

            _logger.LogInfo("Offline translation worker started.");
            return process;
        }
        catch
        {
            TryTerminate(process);
            process.Dispose();
            pipe.Dispose();
            throw;
        }
    }

    private void StopWorker(Process? expectedProcess = null)
    {
        Process? process;
        lock (_processSync)
        {
            if (expectedProcess != null && !ReferenceEquals(expectedProcess, _process))
                return;

            process = _process;
            _process = null;
            _reader?.Dispose();
            _reader = null;
            _writer?.Dispose();
            _writer = null;
            _pipe?.Dispose();
            _pipe = null;
            _workerModelId = null;
        }

        if (process == null) return;
        TryTerminate(process);
        process.Dispose();
        _logger.LogInfo("Offline translation worker stopped.");
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2_000);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWorker();
    }

    internal static bool WorkerModelMatches(string? runningModelId, string requestedModelId) =>
        string.Equals(runningModelId, requestedModelId, StringComparison.Ordinal);

    internal (int? ProcessId, string? ModelId) GetWorkerStateForTesting()
    {
        lock (_processSync)
        {
            return (_process?.Id, _workerModelId);
        }
    }
}
