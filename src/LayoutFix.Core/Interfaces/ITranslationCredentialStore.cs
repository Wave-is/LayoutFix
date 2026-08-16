namespace LayoutFix.Core.Interfaces;

public interface ITranslationCredentialStore
{
    bool HasApiKey { get; }
    string? ReadApiKey();
    void SaveApiKey(string? apiKey);
}
