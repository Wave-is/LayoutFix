using System.Net;
using System.Net.Http;
using System.Text.Json;
using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;

namespace LayoutFix.Tests;

public class TranslationServiceTests
{
    [Fact]
    public async Task Translate_UsesOfficialGoogleCloudContractAndKeepsKeyOutOfUrl()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.OK,
            "{\"data\":{\"translations\":[{\"translatedText\":\"Hello &amp; welcome\"}]}}");
        var service = new TranslationService(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) },
            new StaticCredentialStore("secret-api-key"));

        var result = await service.TranslateAsync("Привет мир", "en", "ru");

        Assert.Equal("Hello & welcome", result);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://translation.googleapis.com/language/translate/v2", handler.RequestUri?.ToString());
        Assert.Equal("secret-api-key", handler.ApiKeyHeader);
        Assert.DoesNotContain("secret-api-key", handler.RequestUri?.ToString(), StringComparison.Ordinal);
        using var body = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal("Привет мир", body.RootElement.GetProperty("q").GetString());
        Assert.Equal("en", body.RootElement.GetProperty("target").GetString());
        Assert.Equal("ru", body.RootElement.GetProperty("source").GetString());
        Assert.Equal("text", body.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Translate_ThrowsForProviderFailureInsteadOfReturningErrorAsText()
    {
        var service = new TranslationService(new HttpClient(
            new RecordingHandler(HttpStatusCode.ServiceUnavailable, "unavailable")),
            new StaticCredentialStore("key"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.TranslateAsync("hello", "uk"));
    }

    [Fact]
    public async Task Translate_RejectsOversizedInputBeforeNetworkCall()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var service = new TranslationService(new HttpClient(handler), new StaticCredentialStore("key"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.TranslateAsync(new string('a', 10_001), "en"));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Translate_RequiresCredentialBeforeNetworkCall()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var service = new TranslationService(new HttpClient(handler), new StaticCredentialStore(null));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TranslateAsync("hello", "uk"));

        Assert.Equal(0, handler.CallCount);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? ApiKeyHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            ApiKeyHeader = request.Headers.TryGetValues("x-goog-api-key", out var values)
                ? values.SingleOrDefault()
                : null;
            RequestBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody)
            };
        }
    }

    private sealed class StaticCredentialStore(string? apiKey) : ITranslationCredentialStore
    {
        public bool HasApiKey => !string.IsNullOrWhiteSpace(apiKey);
        public string? ReadApiKey() => apiKey;
        public void SaveApiKey(string? value) => throw new NotSupportedException();
    }
}
