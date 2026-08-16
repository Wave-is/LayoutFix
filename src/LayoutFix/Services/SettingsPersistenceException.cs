namespace LayoutFix.Services;

internal enum SettingsPersistenceStage
{
    SettingsFile,
    AutoStartRegistry
}

internal sealed class SettingsPersistenceException : Exception
{
    public SettingsPersistenceException(
        SettingsPersistenceStage stage,
        Exception innerException)
        : base("A settings persistence stage failed.", innerException)
    {
        Stage = stage;
    }

    public SettingsPersistenceStage Stage { get; }
    public string DiagnosticCode => Stage switch
    {
        SettingsPersistenceStage.SettingsFile => "LF-ST-001",
        SettingsPersistenceStage.AutoStartRegistry => "LF-ST-002",
        _ => "LF-ST-003"
    };

    public string SafeLogMessage =>
        $"DiagnosticCode: {DiagnosticCode} | Action: settings-save | " +
        $"Stage: {GetSafeStageName(Stage)} | Outcome: failed";

    private static string GetSafeStageName(SettingsPersistenceStage stage) => stage switch
    {
        SettingsPersistenceStage.SettingsFile => "settings-file",
        SettingsPersistenceStage.AutoStartRegistry => "autostart-registry",
        _ => "unknown"
    };
}
