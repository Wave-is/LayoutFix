namespace LayoutFix.Core.Interfaces;

public interface IPopupService
{
    void ShowTranslationPopup(string text);
    void OfferCorrectedCopy(string text) => ShowTranslationPopup(text);
    void ShowStatus(string message, bool isError = false);
}
