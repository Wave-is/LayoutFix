namespace LayoutFix.Services;

public enum AppUpdateStatus
{
    Idle,
    Checking,
    UpToDate,
    Downloading,
    ReadyToInstall,
    Installing,
    Failed
}

public sealed record AppUpdateState(
    AppUpdateStatus Status,
    string? Version = null,
    int ProgressPercent = 0,
    string? ReleasePageUrl = null);

public interface IAppUpdateService
{
    AppUpdateState State { get; }
    bool HasAttemptedInstall => false;
    event EventHandler<AppUpdateState>? StateChanged;

    void StartAutomaticCheck();
    Task CheckAndDownloadAsync(
        bool force = false,
        CancellationToken cancellationToken = default);
    bool TryStartInstaller();
}
