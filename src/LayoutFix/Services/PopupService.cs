using System.Drawing;
using LayoutFix.Core.Interfaces;
using LayoutFix.UI.Controls;

namespace LayoutFix.Services;

public class PopupService : IPopupService
{
    public void ShowTranslationPopup(string text)
    {
        var popup = new TranslationPopupForm(text, Color.FromArgb(240, 240, 240), Color.Black);
        popup.Show();
    }
}
