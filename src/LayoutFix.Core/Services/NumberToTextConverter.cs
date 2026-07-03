using System;
using System.Text;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Core.Services;

public class NumberToTextConverter : INumberToTextConverter
{
    private static readonly string[] Ones = { "", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять" };
    private static readonly string[] OnesFemale = { "", "одна", "две", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять" };
    private static readonly string[] Teens = { "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать" };
    private static readonly string[] Tens = { "", "десять", "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят", "восемьдесят", "девяносто" };
    private static readonly string[] Hundreds = { "", "сто", "двести", "триста", "четыреста", "пятьсот", "шестьсот", "семьсот", "восемьсот", "девятьсот" };

    public string Convert(long number, string cultureCode)
    {
        if (cultureCode.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            return ConvertRu(number);
        
        return number.ToString();
    }

    private string ConvertRu(long number)
    {
        if (number == 0) return "ноль";
        if (number < 0) return "минус " + ConvertRu(-number);

        var sb = new StringBuilder();

        long billions = number / 1000000000;
        long millions = (number % 1000000000) / 1000000;
        long thousands = (number % 1000000) / 1000;
        long units = number % 1000;

        if (billions > 0)
        {
            sb.Append(ConvertChunk(billions, false));
            sb.Append(GetDeclension(billions, "миллиард", "миллиарда", "миллиардов")).Append(" ");
        }

        if (millions > 0)
        {
            sb.Append(ConvertChunk(millions, false));
            sb.Append(GetDeclension(millions, "миллион", "миллиона", "миллионов")).Append(" ");
        }

        if (thousands > 0)
        {
            sb.Append(ConvertChunk(thousands, true));
            sb.Append(GetDeclension(thousands, "тысяча", "тысячи", "тысяч")).Append(" ");
        }

        if (units > 0)
        {
            sb.Append(ConvertChunk(units, false));
        }

        return sb.ToString().Trim();
    }

    private string ConvertChunk(long number, bool isFemale)
    {
        var sb = new StringBuilder();
        int h = (int)(number / 100);
        int rem = (int)(number % 100);

        if (h > 0) sb.Append(Hundreds[h]).Append(" ");

        if (rem >= 10 && rem < 20)
        {
            sb.Append(Teens[rem - 10]).Append(" ");
        }
        else
        {
            int t = rem / 10;
            int u = rem % 10;

            if (t > 0) sb.Append(Tens[t]).Append(" ");
            if (u > 0)
            {
                sb.Append(isFemale ? OnesFemale[u] : Ones[u]).Append(" ");
            }
        }

        return sb.ToString();
    }

    private string GetDeclension(long number, string form1, string form2, string form5)
    {
        long n100 = number % 100;
        long n10 = number % 10;

        if (n100 >= 11 && n100 <= 19) return form5;
        if (n10 == 1) return form1;
        if (n10 >= 2 && n10 <= 4) return form2;
        return form5;
    }
}
