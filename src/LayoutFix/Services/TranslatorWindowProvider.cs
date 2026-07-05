using LayoutFix.Core.Interfaces;
using LayoutFix.UI;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace LayoutFix.Services;

public class TranslatorWindowProvider : ITranslatorWindowProvider
{
    private TranslatorForm? _form;

    public void ShowTranslator(string initialText = "")
    {
        if (_form == null || _form.IsDisposed)
        {
            var translationService = AppHost.Services!.GetRequiredService<ITranslationService>();
            var offlineService = AppHost.Services!.GetRequiredService<IOfflineTranslationService>();
            var locService = AppHost.Services!.GetRequiredService<ILocalizationService>();
            var settingsService = AppHost.Services!.GetRequiredService<ISettingsService>();
            var historyService = AppHost.Services!.GetRequiredService<ITranslationHistoryService>();
            _form = new TranslatorForm(translationService, offlineService, historyService, locService, settingsService);
            _form.FormClosed += (s, e) => _form = null;
        }

        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized)
            _form.WindowState = FormWindowState.Normal;
        
        _form.Activate();

        if (!string.IsNullOrEmpty(initialText))
        {
            _form.SetSourceText(initialText);
        }
    }
}
