using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public readonly record struct DirectTextCaptureResult(
    bool IsApplicable,
    string? Text,
    string? AdapterId,
    bool AllowSelectionFallback,
    bool AllowTargetLayoutActivation)
{
    public static DirectTextCaptureResult NotApplicable =>
        new(false, null, null, false, false);

    public static DirectTextCaptureResult Rejected(string adapterId) =>
        new(true, null, adapterId, false, false);

    public static DirectTextCaptureResult SelectionMissing(string adapterId) =>
        new(true, null, adapterId, true, false);

    public static DirectTextCaptureResult Captured(
        string adapterId,
        string text,
        bool allowTargetLayoutActivation = true) =>
        new(true, text, adapterId, false, allowTargetLayoutActivation);
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
