using System.IO.Pipes;
using System.Text.Json;
using LayoutFix.Core.Services;
using LayoutFix.Infrastructure.Services;

namespace LayoutFix.TranslationWorker;

public static class OfflineTranslationWorkerHost
{
    public static async Task<int> RunAsync(string pipeName, string? modelType)
    {
        if (string.IsNullOrWhiteSpace(pipeName)) return 2;

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await pipe.ConnectAsync(startup.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            var settings = new SettingsService();
            using var logger = new FileLoggerService(settings);
            using var translator = new OfflineTranslationService(logger, settings, modelType);

            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) return 0;

                OfflineTranslationResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<OfflineTranslationRequest>(line)
                        ?? throw new InvalidDataException("Invalid worker request.");
                    var translation = await translator.TranslateAsync(
                        request.Text,
                        request.TargetLanguage,
                        request.SourceLanguage);
                    response = new OfflineTranslationResponse(true, translation, null);
                }
                catch (Exception exception)
                {
                    logger.LogError("Offline translation worker request failed", exception);
                    response = new OfflineTranslationResponse(
                        false,
                        null,
                        $"Offline translation failed ({exception.GetType().Name}).");
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
        }
        catch (OperationCanceledException)
        {
            return 3;
        }
        catch
        {
            return 1;
        }
    }
}
