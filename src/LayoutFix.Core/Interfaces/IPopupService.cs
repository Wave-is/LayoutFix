namespace LayoutFix.Core.Interfaces;

public interface IPopupService
{
    void ShowTranslationPopup(string text);
    void ShowStatus(string message, bool isError = false);
}
