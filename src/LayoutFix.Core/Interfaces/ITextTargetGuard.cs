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
    string? Text)
{
    public static TextSelectionReadResult Unsupported => new(false, null);

    public static TextSelectionReadResult Captured(string? text) => new(true, text);
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
