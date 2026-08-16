using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface IDictionaryAnalyzer
{
    bool IsGibberish(string word, string currentLayout);
    bool TryGetCorrection(
        string word,
        string currentLayout,
        out LayoutCorrectionSuggestion suggestion);
}
