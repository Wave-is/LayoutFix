using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public enum TextSelectionAvailability
{
    Unknown,
    None,
    Present
}

public readonly record struct TextSelectionReadResult(
    bool IsSupported,
    string? Text,
    bool IsSafeToModify = false)
{
    public static TextSelectionReadResult Unsupported => new(false, null, false);

    public static TextSelectionReadResult Captured(string? text) => new(true, text, false);

    public static TextSelectionReadResult Verified(string? text) => new(true, text, true);
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

    Task<TextSelectionReadResult> TryReadSelectedTextAsync(
        ActiveWindowContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(TextSelectionReadResult.Unsupported);
}
