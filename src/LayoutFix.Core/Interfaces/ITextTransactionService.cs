using System.Threading;
using System.Threading.Tasks;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface ITextTransactionService
{
    Task<TextSelection?> CaptureAsync(
        bool allowPreviousWordFallback,
        CancellationToken cancellationToken = default);

    // Reads a selection only when the focused control exposes it directly.
    // This path never sends Ctrl+C and never touches the clipboard, so optional
    // UI such as the translator can open immediately when accessibility data is
    // unavailable instead of waiting through the manual-correction retry budget.
    Task<TextSelection?> CaptureDirectAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<TextSelection?>(null);

    Task<bool> ReplaceAsync(
        TextSelection selection,
        string replacement,
        CancellationToken cancellationToken = default);

    Task CancelFallbackSelectionAsync(
        TextSelection selection,
        CancellationToken cancellationToken = default);
}
