using LayoutFix.Core.Models;

namespace LayoutFix.Core.Interfaces;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
    AppSettings Current { get; }
}
