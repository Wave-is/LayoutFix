using System;
using System.Linq;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Core.Services;

public class TextTransformer : ITextTransformer
{
    public string ChangeCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        if (text.All(c => char.IsLower(c) || !char.IsLetter(c)))
        {
            return text.ToUpper();
        }
        else if (text.All(c => char.IsUpper(c) || !char.IsLetter(c)))
        {
            return ToTitleCase(text);
        }
        else if (IsTitleCase(text))
        {
            return ToInverseCase(text);
        }
        else
        {
            return text.ToLower(); // This handles Inverse -> Lower and random mixed case -> lower
        }
    }

    private string ToTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return char.ToUpper(text[0]) + text.Substring(1).ToLower();
    }

    private bool IsTitleCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (!char.IsUpper(text[0])) return false;
        return text.Skip(1).All(c => char.IsLower(c) || !char.IsLetter(c));
    }

    private string ToInverseCase(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return char.ToLower(text[0]) + text.Substring(1).ToUpper();
    }
}
