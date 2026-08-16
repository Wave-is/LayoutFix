using LayoutFix.Core.Interfaces;
using LayoutFix.UI;
using System.Windows.Forms;

namespace LayoutFix.Services;

public class TranslatorWindowProvider : ITranslatorWindowProvider
{
    private readonly ITranslationService _translationService;
    private readonly IOfflineTranslationService _offlineService;
    private readonly ITranslationHistoryService _historyService;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;
    private TranslatorForm? _form;

    public TranslatorWindowProvider(
        ITranslationService translationService,
        IOfflineTranslationService offlineService,
        ITranslationHistoryService historyService,
        ILocalizationService localizationService,
        ISettingsService settingsService,
        ILoggerService logger)
    {
        _translationService = translationService;
        _offlineService = offlineService;
        _historyService = historyService;
        _localizationService = localizationService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public void ShowTranslator(string initialText = "")
    {
        if (_form == null || _form.IsDisposed)
        {
            _form = new TranslatorForm(
                _translationService,
                _offlineService,
                _historyService,
                _localizationService,
                _settingsService,
                _logger);
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
