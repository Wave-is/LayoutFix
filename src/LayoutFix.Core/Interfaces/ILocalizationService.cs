namespace LayoutFix.Core.Interfaces;

public interface ILocalizationService
{
    string GetString(string key, string defaultValue);
}
