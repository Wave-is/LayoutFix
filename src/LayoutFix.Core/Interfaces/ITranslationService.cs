using System.Threading.Tasks;

namespace LayoutFix.Core.Interfaces;

public interface ITranslationService
{
    Task<string> TranslateAsync(string text, string targetLanguage);
}
