using System;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.UI;

public class TranslatorForm : Form
{
    private readonly ITranslationService _translationService;
    private readonly IOfflineTranslationService _offlineService;
    private readonly ITranslationHistoryService _historyService;
    private readonly ILocalizationService _locService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggerService _logger;
    private readonly System.Windows.Forms.Timer _debounceTimer;
    private CancellationTokenSource? _translationCancellation;

    private Panel _pnlSourceHeader = null!;
    private Panel _pnlTargetHeader = null!;
    private ComboBox _cmbSourceLang = null!;
    private ComboBox _cmbTargetLang = null!;
    private Button _btnSwap = null!;
    private TextBox _txtSource = null!;
    private TextBox _txtTarget = null!;
    private Button _btnCopy = null!;
    private Button _btnCancel = null!;
    private Label _lblStatus = null!;
    private ListBox _lstHistory = null!;
    private bool _suppressAutoTranslate;

    private Color _bgColor;
    private Color _textColor;
    private Color _panelColor;
    private Color _accentColor = Color.FromArgb(26, 115, 232); // Google Blue
    private Color _borderColor = Color.FromArgb(218, 220, 224);

    public TranslatorForm(
        ITranslationService translationService,
        IOfflineTranslationService offlineService,
        ITranslationHistoryService historyService,
        ILocalizationService locService,
        ISettingsService settingsService,
        ILoggerService logger)
    {
        _translationService = translationService;
        _offlineService = offlineService;
        _historyService = historyService;
        _locService = locService;
        _settingsService = settingsService;
        _logger = logger;

        _debounceTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _debounceTimer.Tick += DebounceTimer_Tick;

        InitializeComponent();
        ApplyTheme();
        _ = LoadHistoryAsync();
        FormClosed += (_, _) =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Dispose();
            var cancellation = _translationCancellation;
            _translationCancellation = null;
            cancellation?.Cancel();
        };
    }
    
    private async Task LoadHistoryAsync()
    {
        try
        {
            var history = await _historyService.GetHistoryAsync();
            if (IsDisposed || Disposing) return;
            _lstHistory.Items.Clear();
            foreach (var entry in history)
            {
                _lstHistory.Items.Add(new HistoryItem { Entry = entry, DisplayText = $"[{entry.TargetLang}] {entry.SourceText} ➔ {entry.TranslatedText}" });
            }
        }
        catch (Exception) when (IsDisposed || Disposing)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError("Translation history could not be loaded", exception);
            _lblStatus.Text = _locService.GetString(
                "Translator_HistoryUnavailable",
                "History is temporarily unavailable.");
        }
    }
    
    private class HistoryItem { public TranslationHistoryEntry Entry {get;set;} = null!; public string DisplayText {get;set;} = null!; public override string ToString() => DisplayText; }
    private sealed record LanguageItem(string Code, string Name);

    private void InitializeComponent()
    {
        // See the matching comment in SettingsForm: this form mixes Dock-based
        // layout with several hand-placed pixel widths (language combos,
        // swap/copy buttons), so it needs the same explicit DPI baseline to
        // stay correct above 100% Windows scaling.
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.AutoScaleDimensions = new SizeF(96F, 96F);
        this.Text = _locService.GetString("Translator_Title", "LayoutFix Translator");
        this.Size = new Size(1000, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Font = new Font("Segoe UI", 11F);
        this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var targetLocales = new[] {
            new LanguageItem("en", "English"), new LanguageItem("zh-CN", "中文（普通话）"),
            new LanguageItem("hi", "हिन्दी"), new LanguageItem("es", "Español"),
            new LanguageItem("fr", "Français"), new LanguageItem("ar", "العربية"),
            new LanguageItem("bn", "বাংলা"), new LanguageItem("ru", "Русский"),
            new LanguageItem("pt", "Português"), new LanguageItem("ur", "اردو"),
            new LanguageItem("id", "Bahasa Indonesia"), new LanguageItem("de", "Deutsch"),
            new LanguageItem("ja", "日本語"), new LanguageItem("pcm", "Nigerian Pidgin"),
            new LanguageItem("mr", "मराठी"), new LanguageItem("te", "తెలుగు"),
            new LanguageItem("tr", "Türkçe"), new LanguageItem("ta", "தமிழ்"),
            new LanguageItem("yue", "粵語"), new LanguageItem("vi", "Tiếng Việt"),
            new LanguageItem("tl", "Filipino"), new LanguageItem("wuu", "吴语"),
            new LanguageItem("ko", "한국어"), new LanguageItem("fa", "فارسی"),
            new LanguageItem("ha", "Hausa"), new LanguageItem("arz", "العربية المصرية"),
            new LanguageItem("sw", "Kiswahili"), new LanguageItem("jv", "Basa Jawa"),
            new LanguageItem("it", "Italiano"), new LanguageItem("uk", "Українська")
        };
        
        var locales = new[]
        {
            new LanguageItem(
                "auto",
                _locService.GetString("Translator_DetectLanguage", "Detect language"))
        }.Concat(targetLocales).ToArray();

        var splitContainer = new SplitContainer 
        { 
            Dock = DockStyle.Fill, 
            Orientation = Orientation.Vertical, 
            SplitterDistance = 480,
            SplitterWidth = 10,
            Margin = new Padding(20)
        };
        splitContainer.Resize += (s, e) => {
            if (splitContainer.Width > 100) splitContainer.SplitterDistance = splitContainer.Width / 2;
        };

        // Source Side
        var pnlSourceContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 20, 10, 20) };
        _pnlSourceHeader = new Panel { Dock = DockStyle.Top, Height = 40 };
        _cmbSourceLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Location = new Point(0, 5), DisplayMember = "Name", FlatStyle = FlatStyle.Flat };
        _cmbSourceLang.Items.AddRange(locales.Cast<object>().ToArray());
        SelectLanguage(_cmbSourceLang, "auto");
        _pnlSourceHeader.Controls.Add(_cmbSourceLang);
        
        var pnlSourceBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _txtSource = new TextBox { Multiline = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, Margin = new Padding(10) };
        pnlSourceBody.Controls.Add(_txtSource);

        pnlSourceContainer.Controls.Add(pnlSourceBody);
        pnlSourceContainer.Controls.Add(_pnlSourceHeader);
        splitContainer.Panel1.Controls.Add(pnlSourceContainer);

        // Target Side
        var pnlTargetContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 20, 20, 20) };
        _pnlTargetHeader = new Panel { Dock = DockStyle.Top, Height = 40 };
        
        _btnSwap = new Button { Text = "⇄", Width = 40, Height = 30, Location = new Point(0, 5), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
        _btnSwap.FlatAppearance.BorderSize = 0;
        
        _cmbTargetLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Location = new Point(50, 5), DisplayMember = "Name", FlatStyle = FlatStyle.Flat };
        _cmbTargetLang.Items.AddRange(targetLocales.Cast<object>().ToArray());
        SelectLanguage(_cmbTargetLang, _settingsService.Current.TranslateLang1);
        
        _btnCopy = new Button { Text = _locService.GetString("Translator_Copy", "Copy"), FlatStyle = FlatStyle.Flat, Width = 120, Height = 30, Location = new Point(240, 5), Cursor = Cursors.Hand };
        _btnCopy.FlatAppearance.BorderSize = 0;

        _pnlTargetHeader.Controls.Add(_btnSwap);
        _pnlTargetHeader.Controls.Add(_cmbTargetLang);
        _pnlTargetHeader.Controls.Add(_btnCopy);

        var pnlTargetBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _txtTarget = new TextBox { Multiline = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Margin = new Padding(10) };
        pnlTargetBody.Controls.Add(_txtTarget);

        var pnlTargetBottom = new Panel { Dock = DockStyle.Bottom, Height = 34 };
        _lblStatus = new Label { Text = "", AutoSize = true, Location = new Point(0, 5), ForeColor = Color.Gray, Font = new Font("Segoe UI", 9F) };
        _btnCancel = new Button
        {
            Text = _locService.GetString("Translator_Cancel", "Cancel"),
            Dock = DockStyle.Right,
            Width = 100,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            Visible = false,
            Cursor = Cursors.Hand
        };
        _btnCancel.FlatAppearance.BorderSize = 0;
        _btnCancel.Click += (_, _) =>
        {
            _btnCancel.Enabled = false;
            _translationCancellation?.Cancel();
        };
        pnlTargetBottom.Controls.Add(_lblStatus);
        pnlTargetBottom.Controls.Add(_btnCancel);

        pnlTargetContainer.Controls.Add(pnlTargetBody);
        pnlTargetContainer.Controls.Add(pnlTargetBottom);
        pnlTargetContainer.Controls.Add(_pnlTargetHeader);
        splitContainer.Panel2.Controls.Add(pnlTargetContainer);

        var pnlHistory = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(20, 0, 20, 0) };
        var lblHistoryTitle = new Label { Text = _locService.GetString("Translator_HistoryCollapsed", "Translation history ▼"), AutoSize = true, Location = new Point(25, 10), ForeColor = Color.Gray, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Cursor = Cursors.Hand };
        pnlHistory.Controls.Add(lblHistoryTitle);
        _lstHistory = new ListBox { Dock = DockStyle.Bottom, Height = 120, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10F), Visible = false };
        
        lblHistoryTitle.Click += (s, e) => {
            _lstHistory.Visible = !_lstHistory.Visible;
            if (_lstHistory.Visible) {
                pnlHistory.Height = 160;
                lblHistoryTitle.Text = _locService.GetString(
                    "Translator_HistoryExpanded",
                    "Translation history ▲ (double-click to load)");
            } else {
                pnlHistory.Height = 40;
                lblHistoryTitle.Text = _locService.GetString(
                    "Translator_HistoryCollapsed",
                    "Translation history ▼");
            }
        };

        _lstHistory.DoubleClick += (s, e) => {
            if (_lstHistory.SelectedItem is HistoryItem item) {
                _suppressAutoTranslate = true;
                try
                {
                    CancelCurrentTranslation();
                    _txtSource.Text = item.Entry.SourceText;
                    SelectLanguage(_cmbTargetLang, item.Entry.TargetLang);
                    SelectLanguage(_cmbSourceLang, item.Entry.SourceLang == "" ? "auto" : item.Entry.SourceLang);
                    _txtTarget.Text = item.Entry.TranslatedText;
                    _lblStatus.Text = _locService.GetString(
                        "Translator_FromHistory",
                        "Loaded from history");
                }
                finally
                {
                    _suppressAutoTranslate = false;
                }
            }
        };
        pnlHistory.Controls.Add(_lstHistory);
        this.Controls.Add(pnlHistory);
        this.Controls.Add(splitContainer);

        // Events
        _txtSource.TextChanged += (s, e) => {
            if (_suppressAutoTranslate) return;
            CancelCurrentTranslation();
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };
        _cmbSourceLang.SelectedIndexChanged += (s, e) => {
            if (!_suppressAutoTranslate) _ = TranslateCurrentTextAsync();
        };
        _cmbTargetLang.SelectedIndexChanged += (s, e) => {
            if (!_suppressAutoTranslate) _ = TranslateCurrentTextAsync();
        };
        _btnSwap.Click += (s, e) => {
            string src = GetSelectedLanguage(_cmbSourceLang, "auto");
            string tgt = GetSelectedLanguage(_cmbTargetLang, "en");
            if (src != "auto") {
                _suppressAutoTranslate = true;
                try
                {
                    SelectLanguage(_cmbTargetLang, src);
                    SelectLanguage(_cmbSourceLang, tgt);
                    string temp = _txtSource.Text;
                    _txtSource.Text = _txtTarget.Text;
                    _txtTarget.Text = temp;
                }
                finally
                {
                    _suppressAutoTranslate = false;
                }
                _ = TranslateCurrentTextAsync();
            }
        };
        _btnCopy.Click += (s, e) => {
            if (!string.IsNullOrEmpty(_txtTarget.Text)) Clipboard.SetText(_txtTarget.Text);
        };
    }

    private void ApplyTheme()
    {
        bool isDark = _settingsService.Current.AppTheme == "Dark" || (_settingsService.Current.AppTheme == "Auto" && IsSystemDarkTheme());
        
        _bgColor = isDark ? Color.FromArgb(32, 33, 36) : Color.White;
        _panelColor = isDark ? Color.FromArgb(41, 42, 45) : Color.FromArgb(241, 243, 244);
        _textColor = isDark ? Color.White : Color.Black;
        _borderColor = isDark ? Color.FromArgb(95, 99, 104) : Color.FromArgb(218, 220, 224);

        this.BackColor = _bgColor;
        
        _cmbSourceLang.BackColor = _bgColor;
        _cmbSourceLang.ForeColor = _accentColor; // Google uses blue for active lang tab
        _cmbTargetLang.BackColor = _bgColor;
        _cmbTargetLang.ForeColor = _accentColor;
        
        _btnSwap.ForeColor = _borderColor;
        _btnCopy.ForeColor = _textColor;
        _btnCopy.BackColor = _bgColor;
        _btnCancel.ForeColor = _textColor;
        _btnCancel.BackColor = _bgColor;

        _txtSource.BackColor = _panelColor;
        _txtSource.ForeColor = _textColor;
        _txtTarget.BackColor = _panelColor;
        _txtTarget.ForeColor = _textColor;
        
        _lstHistory.BackColor = _panelColor;
        _lstHistory.ForeColor = _textColor;
        
        // Give panels a colored background to act as border/rounding simulator
        _txtSource.Parent!.BackColor = _panelColor;
        _txtTarget.Parent!.BackColor = _panelColor;
    }

    private bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val != null && val is int i) return i == 0;
        }
        catch { }
        return true;
    }

    public void SetSourceText(string text)
    {
        _suppressAutoTranslate = true;
        try
        {
            _debounceTimer.Stop();
            _txtSource.Text = text;
        }
        finally
        {
            _suppressAutoTranslate = false;
        }
        _ = TranslateCurrentTextAsync();
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _ = TranslateCurrentTextAsync();
    }

    private async Task TranslateCurrentTextAsync()
    {
        _debounceTimer.Stop();
        _translationCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _translationCancellation = cancellation;

        try
        {
            string text = _txtSource.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                _txtTarget.Text = "";
                _lblStatus.Text = "";
                return;
            }

            string targetLang = GetSelectedLanguage(_cmbTargetLang, "en");
            string sourceLang = GetSelectedLanguage(_cmbSourceLang, "auto");

            _lblStatus.Text = _locService.GetString("Translator_Translating", "Translating...");
            _btnCancel.Enabled = true;
            _btnCancel.Visible = true;

            string result = "";
            if (_settingsService.Current.UseOfflineTranslation)
            {
                if (!_offlineService.IsModelAvailable())
                    throw new FileNotFoundException("Offline translation model is not downloaded.");
                _lblStatus.Text = _locService.GetString(
                    "Translator_TranslatingOffline",
                    "Translating (offline)...");
                result = await _offlineService.TranslateAsync(text, targetLang, sourceLang, cancellation.Token);
            }
            else
            {
                if (!_settingsService.Current.OnlineTranslationEnabled)
                    throw new InvalidOperationException(
                        "Online translation is disabled. Enable it explicitly in LayoutFix settings.");
                _lblStatus.Text = _locService.GetString(
                    "Translator_TranslatingOnline",
                    "Translating (online)...");
                result = await _translationService.TranslateAsync(text, targetLang, sourceLang, cancellation.Token);
            }

            if (cancellation != _translationCancellation || IsDisposed || Disposing) return;
            
            _txtTarget.Text = result;
            _lblStatus.Text = _locService.GetString("Translator_Ready", "Ready");
            
            if (_settingsService.Current.TranslationHistoryEnabled)
            {
                var entry = new LayoutFix.Core.Interfaces.TranslationHistoryEntry { Timestamp = DateTime.Now, SourceText = text, TranslatedText = result, TargetLang = targetLang, SourceLang = sourceLang };
                try
                {
                    await _historyService.AddEntryAsync(entry);
                    await LoadHistoryAsync();
                }
                catch (Exception exception)
                {
                    // History is auxiliary: a storage failure must not replace
                    // a successful translation result with a translation error.
                    _logger.LogError("Translation history could not be saved", exception);
                    _lblStatus.Text = _locService.GetString(
                        "Translator_ReadyHistoryUnavailable",
                        "Ready (history unavailable).");
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (cancellation == _translationCancellation && !IsDisposed && !Disposing)
                _lblStatus.Text = _locService.GetString("Translator_Cancelled", "Cancelled");
        }
        catch (Exception ex)
        {
            if (cancellation != _translationCancellation || IsDisposed || Disposing) return;
            _txtTarget.Text =
                _locService.GetString("Translator_ErrorPrefix", "Error:") + " " + ex.Message;
            _lblStatus.Text = _locService.GetString("Translator_Error", "Error");
        }
        finally
        {
            if (cancellation == _translationCancellation)
            {
                _translationCancellation = null;
                if (!IsDisposed && !Disposing)
                    _btnCancel.Visible = false;
            }
            cancellation.Dispose();
        }
    }

    private void CancelCurrentTranslation()
    {
        _translationCancellation?.Cancel();
        if (!IsDisposed && !Disposing)
            _btnCancel.Enabled = false;
    }

    private static string GetSelectedLanguage(ComboBox comboBox, string fallback) =>
        comboBox.SelectedItem is LanguageItem item ? item.Code : fallback;

    private static void SelectLanguage(ComboBox comboBox, string code)
    {
        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is LanguageItem item &&
                string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }
}
