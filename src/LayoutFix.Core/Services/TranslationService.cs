using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Core.Services;

public sealed class TranslationService : ITranslationService, IDisposable
{
    private const int MaximumInputLength = 10_000;
    private static readonly Uri Endpoint = new(
        "https://translation.googleapis.com/language/translate/v2");
    private readonly HttpClient _httpClient;
    private readonly ITranslationCredentialStore _credentials;
    private readonly bool _ownsHttpClient;

    public TranslationService(ITranslationCredentialStore credentials)
        : this(CreateHttpClient(), credentials, ownsHttpClient: true)
    {
    }

    public TranslationService(HttpClient httpClient, ITranslationCredentialStore credentials)
        : this(httpClient, credentials, ownsHttpClient: false)
    {
    }

    private TranslationService(
        HttpClient httpClient,
        ITranslationCredentialStore credentials,
        bool ownsHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string sourceLanguage = "auto",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (text.Length > MaximumInputLength)
            throw new ArgumentException($"Translation input exceeds {MaximumInputLength} characters.", nameof(text));
        if (string.IsNullOrWhiteSpace(targetLanguage))
            throw new ArgumentException("Target language is required.", nameof(targetLanguage));
        var apiKey = _credentials.ReadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("A Google Cloud Translation API key is not configured.");

        var body = new Dictionary<string, string>
        {
            ["q"] = text,
            ["target"] = targetLanguage,
            ["format"] = "text"
        };
        if (!string.IsNullOrWhiteSpace(sourceLanguage) && sourceLanguage != "auto")
            body["source"] = sourceLanguage;

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(body)
        };
        // Google recommends the header over the query parameter so the secret is
        // not exposed in URLs, proxy logs, or exception diagnostics.
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("translations", out var translations) ||
            translations.ValueKind != JsonValueKind.Array ||
            translations.GetArrayLength() == 0 ||
            !translations[0].TryGetProperty("translatedText", out var translatedText) ||
            translatedText.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Translation provider returned an unexpected response.");
        }

        var translated = System.Net.WebUtility.HtmlDecode(translatedText.GetString() ?? string.Empty).Trim();
        return translated.Length > 0
            ? translated
            : throw new InvalidDataException("Translation provider returned an empty translation.");
    }

    private static HttpClient CreateHttpClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
