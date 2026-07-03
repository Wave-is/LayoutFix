namespace LayoutFix.Core.Interfaces;

public interface IDictionaryAnalyzer
{
    bool IsGibberish(string word, string currentLayout);
}
