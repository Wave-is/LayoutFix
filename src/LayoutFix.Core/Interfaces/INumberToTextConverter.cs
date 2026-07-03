namespace LayoutFix.Core.Interfaces;

public interface INumberToTextConverter
{
    string Convert(long number, string cultureCode);
}
