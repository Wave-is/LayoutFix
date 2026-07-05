using System;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
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
    private readonly System.Windows.Forms.Timer _debounceTimer;

    private Panel _pnlSourceHeader = null!;
    private Panel _pnlTargetHeader = null!;
    private ComboBox _cmbSourceLang = null!;
    private ComboBox _cmbTargetLang = null!;
    private Button _btnSwap = null!;
    private TextBox _txtSource = null!;
    private TextBox _txtTarget = null!;
    private Button _btnCopy = null!;
    private Label _lblStatus = null!;
    private ListBox _lstHistory = null!;

    private Color _bgColor;
    private Color _textColor;
    private Color _panelColor;
    private Color _accentColor = Color.FromArgb(26, 115, 232); // Google Blue
    private Color _borderColor = Color.FromArgb(218, 220, 224);

    public TranslatorForm(ITranslationService translationService, IOfflineTranslationService offlineService, ITranslationHistoryService historyService, ILocalizationService locService, ISettingsService settingsService)
    {
        _translationService = translationService;
        _offlineService = offlineService;
        _historyService = historyService;
        _locService = locService;
        _settingsService = settingsService;

        _debounceTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _debounceTimer.Tick += DebounceTimer_Tick;

        InitializeComponent();
        ApplyTheme();
        LoadHistoryAsync();
    }
    
    private async void LoadHistoryAsync()
    {
        var history = await _historyService.GetHistoryAsync();
        _lstHistory.Items.Clear();
        foreach (var entry in history)
        {
            _lstHistory.Items.Add(new HistoryItem { Entry = entry, DisplayText = $"[{entry.TargetLang}] {entry.SourceText} ➔ {entry.TranslatedText}" });
        }
    }
    
    private class HistoryItem { public TranslationHistoryEntry Entry {get;set;} = null!; public string DisplayText {get;set;} = null!; public override string ToString() => DisplayText; }

    private void InitializeComponent()
    {
        this.Text = "LayoutFix Translator";
        this.Size = new Size(1000, 600);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.Font = new Font("Segoe UI", 11F);
        this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var targetLocales = new[] {
            new { Code="en", Name="English" }, new { Code="zh-CN", Name="Китайский (мандарин)" }, 
            new { Code="hi", Name="Хинди" }, new { Code="es", Name="Испанский" },
            new { Code="fr", Name="Французский" }, new { Code="ar", Name="Арабский" },
            new { Code="bn", Name="Бенгальский" }, new { Code="ru", Name="Русский" },
            new { Code="pt", Name="Португальский" }, new { Code="ur", Name="Урду" },
            new { Code="id", Name="Индонезийский" }, new { Code="de", Name="Немецкий" },
            new { Code="ja", Name="Японский" }, new { Code="pcm", Name="Нигерийский пиджин" },
            new { Code="mr", Name="Маратхи" }, new { Code="te", Name="Телугу" },
            new { Code="tr", Name="Турецкий" }, new { Code="ta", Name="Тамильский" },
            new { Code="yue", Name="Юэ (кантонский)" }, new { Code="vi", Name="Вьетнамский" },
            new { Code="tl", Name="Тагальский" }, new { Code="wuu", Name="Ву (шанхайский)" },
            new { Code="ko", Name="Корейский" }, new { Code="fa", Name="Персидский (фарси)" },
            new { Code="ha", Name="Хауса" }, new { Code="arz", Name="Египетский арабский" },
            new { Code="sw", Name="Суахили" }, new { Code="jv", Name="Яванский" },
            new { Code="it", Name="Итальянский" }, new { Code="uk", Name="Украинский" }
        };
        
        var locales = new[] { new { Code="auto", Name="Определить язык" } }.Concat(targetLocales).ToArray();

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
        _cmbSourceLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Location = new Point(0, 5), DataSource = locales, DisplayMember = "Name", ValueMember = "Code", FlatStyle = FlatStyle.Flat };
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
        
        _cmbTargetLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Location = new Point(50, 5), DataSource = targetLocales, DisplayMember = "Name", ValueMember = "Code", FlatStyle = FlatStyle.Flat };
        _cmbTargetLang.SelectedValue = _settingsService.Current.TranslateLang1; // English default
        
        _btnCopy = new Button { Text = "Копировать", FlatStyle = FlatStyle.Flat, Width = 120, Height = 30, Location = new Point(240, 5), Cursor = Cursors.Hand };
        _btnCopy.FlatAppearance.BorderSize = 0;

        _pnlTargetHeader.Controls.Add(_btnSwap);
        _pnlTargetHeader.Controls.Add(_cmbTargetLang);
        _pnlTargetHeader.Controls.Add(_btnCopy);

        var pnlTargetBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1) };
        _txtTarget = new TextBox { Multiline = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, ReadOnly = true, Margin = new Padding(10) };
        pnlTargetBody.Controls.Add(_txtTarget);

        var pnlTargetBottom = new Panel { Dock = DockStyle.Bottom, Height = 30 };
        _lblStatus = new Label { Text = "", AutoSize = true, Location = new Point(0, 5), ForeColor = Color.Gray, Font = new Font("Segoe UI", 9F) };
        pnlTargetBottom.Controls.Add(_lblStatus);

        pnlTargetContainer.Controls.Add(pnlTargetBody);
        pnlTargetContainer.Controls.Add(pnlTargetBottom);
        pnlTargetContainer.Controls.Add(_pnlTargetHeader);
        splitContainer.Panel2.Controls.Add(pnlTargetContainer);

        var pnlHistory = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(20, 0, 20, 0) };
        var lblHistoryTitle = new Label { Text = "История переводов ▼", AutoSize = true, Location = new Point(25, 10), ForeColor = Color.Gray, Font = new Font("Segoe UI", 11F, FontStyle.Bold), Cursor = Cursors.Hand };
        pnlHistory.Controls.Add(lblHistoryTitle);
        _lstHistory = new ListBox { Dock = DockStyle.Bottom, Height = 120, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10F), Visible = false };
        
        lblHistoryTitle.Click += (s, e) => {
            _lstHistory.Visible = !_lstHistory.Visible;
            if (_lstHistory.Visible) {
                pnlHistory.Height = 160;
                lblHistoryTitle.Text = "История переводов ▲ (двойной клик для загрузки)";
            } else {
                pnlHistory.Height = 40;
                lblHistoryTitle.Text = "История переводов ▼";
            }
        };

        _lstHistory.DoubleClick += (s, e) => {
            if (_lstHistory.SelectedItem is HistoryItem item) {
                _txtSource.Text = item.Entry.SourceText;
                _cmbTargetLang.SelectedValue = item.Entry.TargetLang;
                _cmbSourceLang.SelectedValue = item.Entry.SourceLang == "" ? "auto" : item.Entry.SourceLang;
                _txtTarget.Text = item.Entry.TranslatedText;
            }
        };
        pnlHistory.Controls.Add(_lstHistory);
        this.Controls.Add(pnlHistory);
        this.Controls.Add(splitContainer);

        // Events
        _txtSource.TextChanged += (s, e) => {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        };
        _cmbSourceLang.SelectedIndexChanged += (s, e) => TranslateCurrentText();
        _cmbTargetLang.SelectedIndexChanged += (s, e) => TranslateCurrentText();
        _btnSwap.Click += (s, e) => {
            string src = _cmbSourceLang.SelectedValue?.ToString() ?? "auto";
            string tgt = _cmbTargetLang.SelectedValue?.ToString() ?? "en";
            if (src != "auto") {
                _cmbTargetLang.SelectedValue = src;
                _cmbSourceLang.SelectedValue = tgt;
                string temp = _txtSource.Text;
                _txtSource.Text = _txtTarget.Text;
                _txtTarget.Text = temp;
                TranslateCurrentText();
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
        _txtSource.Text = text;
        TranslateCurrentText();
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        TranslateCurrentText();
    }

    private async void TranslateCurrentText()
    {
        string text = _txtSource.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            _txtTarget.Text = "";
            return;
        }

        string targetLang = _cmbTargetLang.SelectedValue?.ToString() ?? "en";
        string sourceLang = _cmbSourceLang.SelectedValue?.ToString() ?? "auto";
        
        _lblStatus.Text = "Перевод...";
        
        try
        {
            string result = "";
            if (_settingsService.Current.UseOfflineTranslation && _offlineService.IsModelAvailable())
            {
                _lblStatus.Text = "Перевод (Offline)...";
                result = await _offlineService.TranslateAsync(text, targetLang);
            }
            else
            {
                _lblStatus.Text = "Перевод (Online)...";
                result = await _translationService.TranslateAsync(text, targetLang);
            }
            
            _txtTarget.Text = result;
            _lblStatus.Text = "Готово";
            
            var entry = new LayoutFix.Core.Interfaces.TranslationHistoryEntry { Timestamp = DateTime.Now, SourceText = text, TranslatedText = result, TargetLang = targetLang, SourceLang = sourceLang };
            await _historyService.AddEntryAsync(entry);
            LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            _txtTarget.Text = "Ошибка: " + ex.Message;
            _lblStatus.Text = "Ошибка";
        }
    }
}
