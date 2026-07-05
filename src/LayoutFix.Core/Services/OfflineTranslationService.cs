using System;
using System.IO;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Core.Services;

public class OfflineTranslationService : IOfflineTranslationService, IDisposable
{
    private readonly ILoggerService _logger;
    private readonly ISettingsService _settingsService;
    private LLamaWeights? _weights;
    private LLamaContext? _context;
    private InstructExecutor? _executor;
    private string _currentLoadedModel = "";

    public OfflineTranslationService(ILoggerService logger, ISettingsService settingsService)
    {
        _logger = logger;
        _settingsService = settingsService;
    }

    public string GetModelPath()
    {
        string fileName = "qwen2-0_5b-instruct-q4_k_m.gguf";
        if (_settingsService.Current.OfflineModelType == "pro") fileName = "gemma-2b-it-q4_k_m.gguf";
        if (_settingsService.Current.OfflineModelType == "alma") fileName = "alma-7b.Q4_K_M.gguf";
            
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LayoutFix", "Models", fileName);
    }

    public bool IsModelAvailable()
    {
        string path = GetModelPath();
        return File.Exists(path) && new FileInfo(path).Length > 1024 * 1024;
    }

    private void InitializeModel()
    {
        string path = GetModelPath();
        if (_weights != null && _currentLoadedModel == path) return;
        if (!IsModelAvailable()) throw new FileNotFoundException("Model file not found.");

        _logger.LogInfo($"Initializing Offline Translation Model ({path})...");
        
        // Unload previous
        _context?.Dispose();
        _weights?.Dispose();
        
        var parameters = new ModelParams(path)
        {
            ContextSize = 1024,
            GpuLayerCount = _settingsService.Current.OfflineModelType == "light" ? 0 : 99 // offload pro and alma to GPU, run light on CPU
        };

        _weights = LLamaWeights.LoadFromFile(parameters);
        _context = _weights.CreateContext(parameters);
        _executor = new InstructExecutor(_context, "Instruction: ", "Response: ");
        _currentLoadedModel = path;
        _logger.LogInfo("Model initialized successfully.");
    }

    public async Task<string> TranslateAsync(string text, string targetLanguageCode)
    {
        if (!IsModelAvailable()) return "Error: Model not downloaded.";

        InitializeModel();

        string targetLangName = targetLanguageCode switch
        {
            "ru" => "Russian",
            "uk" => "Ukrainian",
            "en" => "English",
            "es" => "Spanish",
            "fr" => "French",
            "de" => "German",
            _ => "English"
        };

        string prompt = $"Translate the following text to {targetLangName}. Output ONLY the translation without any introduction, markdown, or explanation.\n\nText to translate: {text}\n";

        var inferenceParams = new InferenceParams()
        {
            MaxTokens = 256,
            AntiPrompts = ["Instruction:"]
        };

        string result = "";
        
        await foreach (var textPart in _executor!.InferAsync(prompt, inferenceParams))
        {
            result += textPart;
        }

        return result.Trim();
    }

    public void Dispose()
    {
        _context?.Dispose();
        _weights?.Dispose();
    }
}
