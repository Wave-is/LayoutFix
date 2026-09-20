namespace LayoutFix.Core.Interfaces;

public interface ITranslatorWindowProvider
{
    void Prepare() { }

    void ShowTranslator(string initialText = "");
}
