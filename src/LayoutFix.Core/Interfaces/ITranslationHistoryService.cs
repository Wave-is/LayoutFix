using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LayoutFix.Core.Interfaces;

public class TranslationHistoryEntry
{
    public DateTime Timestamp { get; set; }
    public string SourceText { get; set; } = "";
    public string TranslatedText { get; set; } = "";
    public string SourceLang { get; set; } = "";
    public string TargetLang { get; set; } = "";
}

public interface ITranslationHistoryService
{
    Task AddEntryAsync(TranslationHistoryEntry entry);
    Task<List<TranslationHistoryEntry>> GetHistoryAsync();
    Task ClearHistoryAsync();
}
