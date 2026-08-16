using System.Security.Cryptography;

namespace LayoutFix.Core.Services;

/// <summary>
/// Immutable metadata for offline models distributed by LayoutFix. Keeping it
/// outside the UI makes the download URL, expected size, and integrity check a
/// single audited contract shared by the downloader and worker client.
/// </summary>
public sealed record OfflineModelDescriptor(
    string Id,
    string FileName,
    Uri DownloadUri,
    long FileSize,
    string Sha256);

public static class OfflineModelCatalog
{
    public static readonly OfflineModelDescriptor Light = new(
        "light",
        "qwen2-0_5b-instruct-q4_k_m.gguf",
        new Uri("https://huggingface.co/Qwen/Qwen2-0.5B-Instruct-GGUF/resolve/main/qwen2-0_5b-instruct-q4_k_m.gguf"),
        397_805_248,
        "f0a42bb979ca62b5e61f3bf924ab4b6a40aa091825ee7dcb4039949980ab81a8");

    public static readonly OfflineModelDescriptor Alma = new(
        "alma",
        "alma-7b.Q4_K_M.gguf",
        new Uri("https://huggingface.co/TheBloke/ALMA-7B-GGUF/resolve/main/alma-7b.Q4_K_M.gguf"),
        4_081_004_224,
        "1f951ffb983e070afce8b57c3b2def63ea03d543c5cafd4d7a6a6bceece3ced0");

    public static readonly OfflineModelDescriptor Pro = new(
        "pro",
        "qwen2.5-1_5b-instruct-q4_k_m.gguf",
        new Uri("https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF/resolve/main/qwen2.5-1.5b-instruct-q4_k_m.gguf"),
        1_117_320_736,
        "6a1a2eb6d15622bf3c96857206351ba97e1af16c30d7a74ee38970e434e9407e");

    public static OfflineModelDescriptor Get(string? modelType) => modelType switch
    {
        "pro" => Pro,
        "alma" => Alma,
        _ => Light
    };

    public static bool SupportsTargetLanguage(
        string? modelType,
        string? targetLanguageCode)
    {
        var code = targetLanguageCode?.Trim().ToLowerInvariant();
        return Get(modelType).Id switch
        {
            "light" => code is "en" or "es" or "fr" or "ru",
            "pro" => code is "en" or "es" or "fr" or "ru" or "uk",
            "alma" => code is "de" or "en" or "es" or "fr" or "ru" or "uk",
            _ => false
        };
    }

    public static bool IsInstalled(string path, OfflineModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length != descriptor.FileSize)
                return false;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> magic = stackalloc byte[4];
            return stream.Read(magic) == magic.Length && magic.SequenceEqual("GGUF"u8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsTrustedArtifact(string path, OfflineModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!IsInstalled(path, descriptor))
            return false;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan);
            var actualHash = SHA256.HashData(stream);
            var expectedHash = Convert.FromHexString(descriptor.Sha256);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }
}
