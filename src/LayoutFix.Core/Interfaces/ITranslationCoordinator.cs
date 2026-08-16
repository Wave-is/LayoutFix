using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface ITranslationCoordinator : IDisposable
{
    ValueTask<bool> QueueTranslationAsync(
        TextSelection selection,
        string targetLanguage,
        string sourceLanguage = "auto",
        CancellationToken cancellationToken = default);
}
