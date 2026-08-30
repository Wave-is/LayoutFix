using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public readonly record struct DirectTextCaptureResult(
    bool IsApplicable,
    string? Text,
    string? AdapterId,
    bool AllowSelectionFallback)
{
    public static DirectTextCaptureResult NotApplicable => new(false, null, null, false);

    public static DirectTextCaptureResult Rejected(string adapterId) =>
        new(true, null, adapterId, false);

    public static DirectTextCaptureResult SelectionMissing(string adapterId) =>
        new(true, null, adapterId, true);

    public static DirectTextCaptureResult Captured(string adapterId, string text) =>
        new(true, text, adapterId, false);
}

public interface IDirectTextAdapter
{
    Task<DirectTextCaptureResult> TryCaptureAsync(
        ActiveWindowContext context,
        CancellationToken cancellationToken = default);

    Task<bool> TryReplaceAsync(
        string adapterId,
        ActiveWindowContext context,
        string expectedText,
        string replacement,
        CancellationToken cancellationToken = default);
}
