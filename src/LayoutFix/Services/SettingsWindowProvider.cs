using LayoutFix.Core.Interfaces;
using LayoutFix.Core.Services;
using LayoutFix.UI;

namespace LayoutFix.Services;

public sealed class SettingsWindowProvider(
    ISettingsService settingsService,
    IAutoStartService autoStartService,
    ILocalizationService localizationService,
    ILoggerService logger,
    ModelDownloadService modelDownloadService,
    ITranslationHistoryService translationHistoryService,
    ITranslationCredentialStore translationCredentials)
{
    private SettingsForm? _form;

    public void Show()
    {
        if (_form == null || _form.IsDisposed)
        {
            _form = new SettingsForm(
                settingsService,
                autoStartService,
                localizationService,
                logger,
                modelDownloadService,
                translationHistoryService,
                translationCredentials);
            _form.FormClosed += (_, _) => _form = null;
        }

        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized)
            _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }
}
