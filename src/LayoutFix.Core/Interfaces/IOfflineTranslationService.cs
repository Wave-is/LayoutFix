using System.Threading.Tasks;
using System.Threading;

namespace LayoutFix.Core.Interfaces;

public interface IOfflineTranslationService
{
    Task<string> TranslateAsync(
        string text,
        string targetLanguageCode,
        string sourceLanguageCode = "auto",
        CancellationToken cancellationToken = default);
    bool IsModelAvailable();
}
