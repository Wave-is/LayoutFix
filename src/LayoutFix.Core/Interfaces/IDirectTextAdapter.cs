using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public readonly record struct DirectTextCaptureResult(
    bool IsApplicable,
    string? Text,
    string? AdapterId)
{
    public static DirectTextCaptureResult NotApplicable => new(false, null, null);

    public static DirectTextCaptureResult Rejected(string adapterId) =>
        new(true, null, adapterId);

    public static DirectTextCaptureResult Captured(string adapterId, string text) =>
        new(true, text, adapterId);
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
