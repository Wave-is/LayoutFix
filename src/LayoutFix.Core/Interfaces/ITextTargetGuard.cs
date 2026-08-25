using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public enum TextSelectionAvailability
{
    Unknown,
    None,
    Present
}

public interface ITextTargetGuard
{
    Task<bool> CanModifyAsync(
        ActiveWindowContext context,
        CancellationToken cancellationToken = default);

    Task<TextSelectionAvailability> GetSelectionAvailabilityAsync(
        ActiveWindowContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(TextSelectionAvailability.Unknown);
}
