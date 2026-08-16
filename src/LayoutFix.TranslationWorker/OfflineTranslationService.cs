using System.Text;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace LayoutFix.TranslationWorker;

internal sealed class OfflineTranslationService : IOfflineTranslationService, IDisposable
{
    private const int MaximumInputLength = 3_000;
    private readonly ILoggerService _logger;
    private readonly string _modelType;
    private readonly SemaphoreSlim _translationLock = new(1, 1);
    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;
    private string _currentLoadedModel = "";
    private int _disposeState;

    public OfflineTranslationService(
        ILoggerService logger,
        ISettingsService settingsService,
        string? modelType = null)
    {
        _logger = logger;
        _modelType = OfflineModelCatalog.Get(modelType ?? settingsService.Current.OfflineModelType).Id;
    }

    public bool IsModelAvailable()
    {
        var descriptor = OfflineModelCatalog.Get(_modelType);
        var path = OfflineModelLocator.GetModelPath(descriptor.Id);
        return OfflineModelCatalog.IsInstalled(path, descriptor);
    }

    private void InitializeModel()
    {
        var descriptor = OfflineModelCatalog.Get(_modelType);
        var path = OfflineModelLocator.GetModelPath(descriptor.Id);
        if (_weights != null && _currentLoadedModel == path) return;
        if (!IsModelAvailable()) throw new FileNotFoundException("Model file not found.");
        if (!OfflineModelCatalog.IsTrustedArtifact(path, descriptor))
            throw new InvalidDataException(
                "Offline translation model failed its SHA-256 integrity check.");

        _logger.LogInfo($"Initializing offline translation model '{descriptor.Id}'.");
        var parameters = new ModelParams(path)
        {
            ContextSize = 1024,
            // CPU is deliberately the safe default. Unknown GPU drivers must not
            // be able to destabilize translation, even inside the worker process.
            GpuLayerCount = 0
        };

        LLamaWeights? newWeights = null;
        StatelessExecutor? newExecutor = null;
        try
        {
            newWeights = LLamaWeights.LoadFromFile(parameters);
            newExecutor = new StatelessExecutor(newWeights, parameters)
            {
                // Both Qwen models carry a valid embedded chat template. ALMA is
                // a completion-style translation model, so BuildPrompt supplies
                // its audited native format without asking LLamaSharp to guess.
                ApplyTemplate = _modelType != OfflineModelCatalog.Alma.Id,
                SystemMessage = GetSystemMessage(_modelType)
            };
        }
        catch
        {
            newWeights?.Dispose();
            throw;
        }

        var oldWeights = _weights;
        _weights = newWeights;
        _executor = newExecutor;
        _currentLoadedModel = path;
        oldWeights?.Dispose();
        _logger.LogInfo("Offline translation model initialized successfully.");
    }

    public async Task<string> TranslateAsync(
        string text,
        string targetLanguageCode,
        string sourceLanguageCode = "auto",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (text.Length > MaximumInputLength)
            throw new ArgumentException($"Offline translation input exceeds {MaximumInputLength} characters.", nameof(text));
        if (string.IsNullOrWhiteSpace(targetLanguageCode))
            throw new ArgumentException("Target language is required.", nameof(targetLanguageCode));
        targetLanguageCode = targetLanguageCode.Trim().ToLowerInvariant();
        if (!OfflineModelCatalog.SupportsTargetLanguage(_modelType, targetLanguageCode))
            throw new NotSupportedException(
                $"Offline model '{_modelType}' has not passed the '{targetLanguageCode}' quality gate.");
        if (!IsModelAvailable()) throw new FileNotFoundException("Offline translation model is not downloaded.");

        await _translationLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
            await Task.Run(InitializeModel, cancellationToken);

            var targetLanguageName = targetLanguageCode switch
            {
                "ru" => "Russian",
                "uk" => "Ukrainian",
                "en" => "English",
                "es" => "Spanish",
                "fr" => "French",
                "de" => "German",
                _ => targetLanguageCode
            };
            var sourceLanguageName = GetLanguageName(sourceLanguageCode);
            var promptProtection = OfflineTranslationPromptProtector.Protect(text);
            var prompt = BuildPrompt(
                _modelType,
                promptProtection.ProtectedText,
                sourceLanguageCode,
                sourceLanguageName,
                targetLanguageCode,
                targetLanguageName);
            var inferenceParameters = new InferenceParams
            {
                MaxTokens = 256,
                AntiPrompts = ["<|im_end|>", "<end_of_turn>"],
                SamplingPipeline = new GreedySamplingPipeline()
            };

            var rawResult = await InferAsync(prompt, inferenceParameters, cancellationToken);
            if (promptProtection.TryRestore(rawResult, out var restoredResult) &&
                OfflineTranslationResultGuard.TryAccept(
                text,
                targetLanguageCode,
                restoredResult,
                out var translated))
            {
                return translated;
            }

            var correctionPrompt = BuildPrompt(
                _modelType,
                promptProtection.ProtectedText,
                sourceLanguageCode,
                sourceLanguageName,
                targetLanguageCode,
                targetLanguageName,
                isCorrectionAttempt: true);
            rawResult = await InferAsync(
                correctionPrompt,
                inferenceParameters,
                cancellationToken);
            if (promptProtection.TryRestore(rawResult, out restoredResult) &&
                OfflineTranslationResultGuard.TryAccept(
                text,
                targetLanguageCode,
                restoredResult,
                out translated))
            {
                return translated;
            }

            throw new InvalidDataException(
                "Offline model returned an unsafe or implausible translation.");
        }
        finally
        {
            _translationLock.Release();
        }
    }

    private async Task<string> InferAsync(
        string prompt,
        InferenceParams inferenceParameters,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder();
        await foreach (var textPart in _executor!.InferAsync(prompt, inferenceParameters, cancellationToken))
            result.Append(textPart);

        return result.ToString();
    }

    private static string GetLanguageName(string languageCode) => languageCode switch
    {
        "ru" => "Russian",
        "uk" => "Ukrainian",
        "en" => "English",
        "es" => "Spanish",
        "fr" => "French",
        "de" => "German",
        "auto" => "automatically detected source language",
        _ => languageCode
    };

    internal static string BuildPrompt(
        string modelType,
        string text,
        string sourceLanguageCode,
        string sourceLanguageName,
        string targetLanguageCode,
        string targetLanguageName,
        bool isCorrectionAttempt = false)
    {
        if (modelType == OfflineModelCatalog.Alma.Id)
        {
            var almaSource = sourceLanguageCode == "auto"
                ? "Detected source language"
                : sourceLanguageName;
            var correction = isCorrectionAttempt
                ? $"Return only standard {targetLanguageName}. The previous attempt used the wrong " +
                  $"language or unsafe text. Preserve every paragraph break, list/Markdown marker, " +
                  "technical token, number, date, time, percentage, inline-code span, fenced " +
                  "code block with all of its content, " +
                  "Markdown table shape, link destination, reference id, " +
                  "and identifier. Person and place names are identity labels, not text to " +
                  "localize: transliterate them phonetically into the target script and never " +
                  "substitute a culturally similar name (Alice becomes Алиса, never Алла; " +
                  "Bob becomes Боб, never Борис); " +
                  $"translate every sentence and clause without omission. " +
                  $"{GetScriptConstraint(targetLanguageCode)}\n"
                : string.Empty;
            return $"Translate this from {almaSource} to {targetLanguageName}:\n" +
                   correction +
                   $"{almaSource}: {text}\n" +
                   $"{targetLanguageName}:";
        }

        var source = sourceLanguageCode == "auto"
            ? "Detect the source language."
            : $"Translate from {sourceLanguageName} ({sourceLanguageCode}).";
        var scriptConstraint = GetScriptConstraint(targetLanguageCode);
        var isLightModel = modelType == OfflineModelCatalog.Light.Id;
        var correctionConstraint = isCorrectionAttempt
            ? "This is a correction attempt after a rejected answer. Check every translated word " +
              $"and output only standard {targetLanguageName}; mixed-language text is invalid. " +
              (isLightModel
                  ? "Preserve every paragraph break, technical token, number, date, time, and " +
                    "percentage.\n"
                  : "Preserve every paragraph break, list/Markdown marker, technical token, " +
                    "number, date, time, percentage, inline-code span, fenced code block with " +
                    "all of its content, Markdown table " +
                    "shape, link destination, reference id, and identifier.\n")
            : string.Empty;
        var preservationConstraint = isLightModel
            ? "Preserve filenames, paths, URLs, keyboard shortcuts, placeholders, code " +
              "identifiers, numbers, dates, times, percentages, paragraph breaks, and list " +
              "structure exactly. Copy every " +
              "{LF_PROTECTED_0000}-style placeholder exactly once and unchanged.\n"
            : "Preserve filenames, paths, URLs, keyboard shortcuts, placeholders, code " +
              "identifiers, numbers, dates, times, percentages, inline code, paragraph breaks, " +
              "list bullets, numbering, " +
              "indentation, strong/strikethrough markers, fenced code blocks with all of their " +
              "content, Markdown table shape, link destinations, and reference ids exactly. " +
              "Copy every {LF_PROTECTED_0000}-style placeholder exactly once and unchanged. " +
              "Person and place names are identity labels, not text to localize: transliterate " +
              "them phonetically into the target script and never substitute a culturally " +
              "similar name (Alice becomes Алиса, never Алла; Bob becomes Боб, never Борис).\n";
        var request = $"{source}\n" +
                      $"Translate into {targetLanguageName} ({targetLanguageCode}).\n" +
                      "Return only the translated text.\n" +
                      preservationConstraint +
                      "Translate every sentence and clause without omitting or summarizing content.\n" +
                      correctionConstraint +
                      scriptConstraint +
                      "Translate the natural-language prose; do not copy the entire source text.\n\n" +
                      $"{sourceLanguageName} source:\n{text}\n\n" +
                      $"{targetLanguageName} translation:";

        return request;
    }

    private static string GetSystemMessage(string modelType)
    {
        const string prefix =
            "You are a translation engine. Translate the user's text to the requested " +
            "target language. Return only the translated text, without labels, notes, " +
            "markdown, alternatives, or explanations. ";
        return modelType == OfflineModelCatalog.Light.Id
            ? prefix +
              "Preserve filenames, paths, URLs, keyboard shortcuts, placeholders, code " +
              "identifiers, numbers, dates, times, percentages, paragraph breaks, and list " +
              "structure exactly. Translate every " +
              "sentence and clause without omitting or summarizing content."
            : prefix +
              "Preserve filenames, paths, URLs, keyboard shortcuts, placeholders, code " +
              "identifiers, numbers, dates, times, percentages, inline code, paragraph breaks, " +
              "list bullets, numbering, indentation, " +
              "strong/strikethrough markers, fenced code blocks with all of their content, " +
              "Markdown table shape, link destinations, and reference ids exactly. Preserve " +
              "person and place names phonetically into the target script without substituting " +
              "a culturally similar name (Alice becomes Алиса, never Алла; Bob becomes " +
              "Боб, never Борис). Translate every " +
              "sentence and clause without " +
              "omitting or summarizing content.";
    }

    private static string GetScriptConstraint(string targetLanguageCode) =>
        targetLanguageCode switch
        {
            "ru" =>
                "The answer must use standard Russian vocabulary and Cyrillic letters, " +
                "not Ukrainian and with no English words.\n",
            "uk" =>
                "The answer must use standard Ukrainian vocabulary and Cyrillic letters, " +
                "not Russian and with no English words. Every translated word must be Ukrainian; " +
                "a mixed Russian/Ukrainian answer is invalid. Use Ukrainian forms such as \"дякую\", " +
                "\"допомога\" or \"допомогу\"; never use Russian \"спасибо\" or \"помощь\". " +
                "Preserve imperative force and negation: translate \"Do not close the window\" " +
                "as \"Не закривайте вікно\", never as a statement about cancellation. Preserve " +
                "temporal clauses: \"before the process finishes\" means \"до завершення процесу\".\n",
            "en" or "es" or "fr" or "de" => "The answer must use the Latin alphabet.\n",
            _ => string.Empty
        };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;

        if (!_translationLock.Wait(TimeSpan.FromSeconds(2)))
        {
            _logger.LogWarning("Offline model was still busy during shutdown; the worker process will reclaim native resources.");
            return;
        }

        try
        {
            _executor = null;
            _weights?.Dispose();
            _weights = null;
        }
        finally
        {
            _translationLock.Release();
            _translationLock.Dispose();
        }
    }
}
