using LayoutFix.Core.Interfaces;
using LayoutFix.UI;
using System.Diagnostics;
using System.Windows.Forms;

namespace LayoutFix.Services;

public class TranslatorWindowProvider : ITranslatorWindowProvider, IDisposable
{
    private readonly ITranslationService _translationService;
    private readonly IOfflineTranslationService _offlineService;
    private readonly ITranslationHistoryService _historyService;
    private readonly ILocalizationService _localizationService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;
    private readonly SynchronizationContext _uiContext;
    private readonly int _uiThreadId;
    private TranslatorForm? _form;
    private volatile bool _disposing;

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
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _uiThreadId = Environment.CurrentManagedThreadId;
    }

    public void Prepare()
    {
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            _uiContext.Post(
                static state => ((TranslatorWindowProvider)state!).Prepare(),
                this);
            return;
        }

        var started = Stopwatch.GetTimestamp();
        var form = EnsureForm();
        PrepareControlTree(form);
        form.PerformLayout();
        _logger.LogInfo(
            $"Translator window prepared. ElapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}.");
    }

    public void ShowTranslator(string initialText = "")
    {
        ObjectDisposedException.ThrowIf(_disposing, this);
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            _uiContext.Post(
                static state =>
                {
                    var request = ((TranslatorWindowProvider Provider, string InitialText))state!;
                    request.Provider.TryShowTranslatorOnUiThread(request.InitialText);
                },
                (this, initialText));
            _logger.LogInfo("Translator window request queued on the UI thread.");
            return;
        }

        ShowTranslatorOnUiThread(initialText);
    }

    private void TryShowTranslatorOnUiThread(string initialText)
    {
        try
        {
            ShowTranslatorOnUiThread(initialText);
        }
        catch (Exception exception)
        {
            _logger.LogError("Translator window could not be shown on the UI thread.", exception);
        }
    }

    private void ShowTranslatorOnUiThread(string initialText)
    {
        if (_disposing)
            return;

        var started = Stopwatch.GetTimestamp();
        var form = EnsureForm();
        var preparedAt = Stopwatch.GetTimestamp();

        form.Show();
        if (form.WindowState == FormWindowState.Minimized)
            form.WindowState = FormWindowState.Normal;

        form.Activate();

        if (!string.IsNullOrEmpty(initialText))
            form.SetSourceText(initialText);

        _logger.LogInfo(
            "Translator window shown. " +
            $"PreparationMs={Stopwatch.GetElapsedTime(started, preparedAt).TotalMilliseconds:F0}; " +
            $"TotalMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}.");
    }

    private TranslatorForm EnsureForm()
    {
        ObjectDisposedException.ThrowIf(_disposing, this);
        if (_form == null || _form.IsDisposed)
        {
            _form = new TranslatorForm(
                _translationService,
                _offlineService,
                _historyService,
                _localizationService,
                _settingsService,
                _logger);
            _form.FormClosing += Form_FormClosing;
            _form.FormClosed += Form_FormClosed;
        }

        return _form;
    }

    private static void PrepareControlTree(Control control)
    {
        control.CreateControl();
        _ = control.Handle;
        foreach (Control child in control.Controls)
            PrepareControlTree(child);
    }

    private void Form_FormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_disposing && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            if (sender is Form form)
                form.Hide();
        }
    }

    private void Form_FormClosed(object? sender, FormClosedEventArgs eventArgs)
    {
        if (ReferenceEquals(_form, sender))
            _form = null;
    }

    public void Dispose()
    {
        if (_disposing)
            return;

        _disposing = true;
        var form = _form;
        _form = null;
        if (form == null || form.IsDisposed)
            return;

        form.FormClosing -= Form_FormClosing;
        form.FormClosed -= Form_FormClosed;
        form.Close();
        form.Dispose();
    }
}
