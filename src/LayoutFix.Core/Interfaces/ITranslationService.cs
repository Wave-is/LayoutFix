using System.Threading.Tasks;
using System.Threading;

namespace LayoutFix.Core.Interfaces;

public interface ITranslationService
{
    Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        string sourceLanguage = "auto",
        CancellationToken cancellationToken = default);
}
