namespace LayoutFix.Core.Services;

public static class OfflineModelLocator
{
    public static string GetModelPath(string? modelType)
    {
        var descriptor = OfflineModelCatalog.Get(modelType);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LayoutFix",
            "Models",
            descriptor.FileName);
    }
}
