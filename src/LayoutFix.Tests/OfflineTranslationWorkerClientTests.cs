using LayoutFix.Services;

namespace LayoutFix.Tests;

public sealed class OfflineTranslationWorkerClientTests
{
    [Fact]
    public void WorkerModelMatches_RequiresExactModelIdentity()
    {
        Assert.True(OfflineTranslationWorkerClient.WorkerModelMatches("light", "light"));
        Assert.True(OfflineTranslationWorkerClient.WorkerModelMatches("pro", "pro"));
        Assert.True(OfflineTranslationWorkerClient.WorkerModelMatches("alma", "alma"));

        Assert.False(OfflineTranslationWorkerClient.WorkerModelMatches(null, "light"));
        Assert.False(OfflineTranslationWorkerClient.WorkerModelMatches("light", "pro"));
        Assert.False(OfflineTranslationWorkerClient.WorkerModelMatches("pro", "alma"));
        Assert.False(OfflineTranslationWorkerClient.WorkerModelMatches("ALMA", "alma"));
    }
}
