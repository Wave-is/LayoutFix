using System.Collections.Generic;
using System.Text;

namespace LayoutFix.Core.Services;

public class TransliterationService
{
    private static readonly Dictionary<string, string> RuToEn = new()
    {
        {"А", "A"}, {"Б", "B"}, {"В", "V"}, {"Г", "G"}, {"Д", "D"},
        {"Е", "E"}, {"Ё", "Yo"}, {"Ж", "Zh"}, {"З", "Z"}, {"И", "I"},
        {"Й", "J"}, {"К", "K"}, {"Л", "L"}, {"М", "M"}, {"Н", "N"},
        {"О", "O"}, {"П", "P"}, {"Р", "R"}, {"С", "S"}, {"Т", "T"},
        {"У", "U"}, {"Ф", "F"}, {"Х", "X"}, {"Ц", "C"}, {"Ч", "Ch"},
        {"Ш", "Sh"}, {"Щ", "Shh"}, {"Ъ", "``"}, {"Ы", "Y'"}, {"Ь", "`"},
        {"Э", "E'"}, {"Ю", "Yu"}, {"Я", "Ya"},
        {"а", "a"}, {"б", "b"}, {"в", "v"}, {"г", "g"}, {"д", "d"},
        {"е", "e"}, {"ё", "yo"}, {"ж", "zh"}, {"з", "z"}, {"и", "i"},
        {"й", "j"}, {"к", "k"}, {"л", "l"}, {"м", "m"}, {"н", "n"},
        {"о", "o"}, {"п", "p"}, {"р", "r"}, {"с", "s"}, {"т", "t"},
        {"у", "u"}, {"ф", "f"}, {"х", "x"}, {"ц", "c"}, {"ч", "ch"},
        {"ш", "sh"}, {"щ", "shh"}, {"ъ", "``"}, {"ы", "y'"}, {"ь", "`"},
        {"э", "e'"}, {"ю", "yu"}, {"я", "ya"}
    };
    
    public string Transliterate(string text)
    {
        bool hasCyrillic = false;
        foreach(var c in text) {
            if ((c >= 'А' && c <= 'я') || c == 'Ё' || c == 'ё') {
                hasCyrillic = true; break;
            }
        }

        if (hasCyrillic)
        {
            var sb = new StringBuilder();
            foreach (var c in text)
            {
                if (RuToEn.TryGetValue(c.ToString(), out var en)) sb.Append(en);
                else sb.Append(c);
            }
            return sb.ToString();
        }
        else
        {
            var enToRuList = new List<KeyValuePair<string, string>>();
            foreach (var kvp in RuToEn)
            {
                enToRuList.Add(new KeyValuePair<string, string>(kvp.Value, kvp.Key));
            }
            enToRuList.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

            var result = text;
            foreach (var kvp in enToRuList)
            {
                result = result.Replace(kvp.Key, kvp.Value);
            }
            return result;
        }
    }
}
