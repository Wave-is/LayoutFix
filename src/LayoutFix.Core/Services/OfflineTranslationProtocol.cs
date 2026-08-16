namespace LayoutFix.Core.Services;

public sealed record OfflineTranslationRequest(
    string Text,
    string TargetLanguage,
    string SourceLanguage);

public sealed record OfflineTranslationResponse(
    bool Success,
    string? Translation,
    string? Error);
