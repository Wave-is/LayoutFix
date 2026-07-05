using System.Threading.Tasks;

namespace LayoutFix.Core.Interfaces;

public interface IOfflineTranslationService
{
    Task<string> TranslateAsync(string text, string targetLanguageCode);
    bool IsModelAvailable();
}
