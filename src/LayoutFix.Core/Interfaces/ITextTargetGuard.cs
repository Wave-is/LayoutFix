using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface ITextTargetGuard
{
    Task<bool> CanModifyAsync(
        ActiveWindowContext context,
        CancellationToken cancellationToken = default);
}
