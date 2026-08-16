using LayoutFix.Core.Services;
using System.Security.Cryptography;

namespace LayoutFix.Tests;

public class OfflineModelCatalogTests
{
    [Fact]
    public void ApprovedModelsHavePinnedUniqueHttpsMetadata()
    {
        OfflineModelDescriptor[] models =
        [
            OfflineModelCatalog.Light,
            OfflineModelCatalog.Pro,
            OfflineModelCatalog.Alma
        ];

        Assert.Equal(models.Length, models.Select(model => model.Id).Distinct().Count());
        Assert.Equal(models.Length, models.Select(model => model.FileName).Distinct().Count());
        foreach (var model in models)
        {
            Assert.Equal(Uri.UriSchemeHttps, model.DownloadUri.Scheme);
            Assert.True(model.FileSize > 0);
            Assert.Matches("^[0-9a-f]{64}$", model.Sha256);
        }
    }

    [Fact]
    public void ProModelIsApprovedApacheLicensedQwen25Artifact()
    {
        var model = OfflineModelCatalog.Pro;

        Assert.Equal("qwen2.5-1_5b-instruct-q4_k_m.gguf", model.FileName);
        Assert.Equal(
            "https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf",
            model.DownloadUri.AbsoluteUri);
        Assert.Equal(1_117_320_736, model.FileSize);
        Assert.Equal(
            "6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e",
            model.Sha256);
    }

    [Theory]
    [InlineData("light", "ru", true)]
    [InlineData("light", "uk", false)]
    [InlineData("light", "de", false)]
    [InlineData("pro", "uk", true)]
    [InlineData("pro", "de", false)]
    [InlineData("alma", "de", true)]
    [InlineData("alma", "it", false)]
    [InlineData("pro", " de ", false)]
    [InlineData("alma", " DE ", true)]
    public void TargetLanguageSupportMatchesRealQualityGates(
        string modelType,
        string targetLanguageCode,
        bool expected)
    {
        Assert.Equal(
            expected,
            OfflineModelCatalog.SupportsTargetLanguage(modelType, targetLanguageCode));
    }

    [Fact]
    public void TrustedArtifactVerificationRejectsSameSizeGgufWithModifiedPayload()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"LayoutFix.ModelIntegrityTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "model.gguf");
        var payload = new byte[1024 * 1024 + 1];
        "GGUF"u8.CopyTo(payload);
        payload[^1] = 42;
        File.WriteAllBytes(path, payload);
        var descriptor = new OfflineModelDescriptor(
            "test",
            "model.gguf",
            new Uri("https://models.invalid/model.gguf"),
            payload.LongLength,
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant());

        try
        {
            Assert.True(OfflineModelCatalog.IsInstalled(path, descriptor));
            Assert.True(OfflineModelCatalog.IsTrustedArtifact(path, descriptor));

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Position = payload.Length / 2;
                stream.WriteByte(1);
            }

            Assert.True(OfflineModelCatalog.IsInstalled(path, descriptor));
            Assert.False(OfflineModelCatalog.IsTrustedArtifact(path, descriptor));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
