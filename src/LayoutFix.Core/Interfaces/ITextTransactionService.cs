using System.Threading;
using System.Threading.Tasks;
using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface ITextTransactionService
{
    Task<TextSelection?> CaptureAsync(
        bool allowPreviousWordFallback,
        CancellationToken cancellationToken = default);

    Task<bool> ReplaceAsync(
        TextSelection selection,
        string replacement,
        CancellationToken cancellationToken = default);

    Task CancelFallbackSelectionAsync(
        TextSelection selection,
        CancellationToken cancellationToken = default);
}
