using System.Collections.Generic;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;
using Xunit;

namespace LayoutFix.Tests;

public class AutoDetectionTests
{
    private readonly Layout _en;
    private readonly Layout _ru;

    public AutoDetectionTests()
    {
        _en = new Layout { 
            Code = "en-US", 
            Keys = new Dictionary<string, string> { {"q","q"},{"w","w"},{"e","e"},{"r","r"},{"t","t"},{"y","y"},{"u","u"},{"i","i"},{"o","o"},{"p","p"},{"[","["},{"]","]"},{"a","a"},{"s","s"},{"d","d"},{"f","f"},{"g","g"},{"h","h"},{"j","j"},{"k","k"},{"l","l"},{";",";"},{"'","'"},{"z","z"},{"x","x"},{"c","c"},{"v","v"},{"b","b"},{"n","n"},{"m","m"},{",",","},{".","."},{"`","`"},{"/","/"},{"\\","\\"} }, 
            ShiftKeys = new Dictionary<string, string>{ {"q","Q"},{"w","W"},{"e","E"},{"r","R"},{"t","T"},{"y","Y"},{"u","U"},{"i","I"},{"o","O"},{"p","P"},{"[","{"},{"]","}"},{"a","A"},{"s","S"},{"d","D"},{"f","F"},{"g","G"},{"h","H"},{"j","J"},{"k","K"},{"l","L"},{";",":"},{"'","\""},{"z","Z"},{"x","X"},{"c","C"},{"v","V"},{"b","B"},{"n","N"},{"m","M"},{",","<"},{".",">"},{"`","~"},{"/","?"},{"\\","|"},{"1","!"},{"2","@"},{"3","#"},{"4","$"},{"5","%"},{"6","^"},{"7","&"},{"8","*"},{"9","("},{"0",")"},{"-","_"},{"=","+"} }
        };
        _ru = new Layout { 
            Code = "ru-RU", 
            Keys = new Dictionary<string, string> { {"q","й"},{"w","ц"},{"e","у"},{"r","к"},{"t","е"},{"y","н"},{"u","г"},{"i","ш"},{"o","щ"},{"p","з"},{"[","х"},{"]","ъ"},{"a","ф"},{"s","ы"},{"d","в"},{"f","а"},{"g","п"},{"h","р"},{"j","о"},{"k","л"},{"l","д"},{";","ж"},{"'","э"},{"z","я"},{"x","ч"},{"c","с"},{"v","м"},{"b","и"},{"n","т"},{"m","ь"},{",","б"},{".","ю"},{"`","ё"},{"/","."},{"\\","\\"} }, 
            ShiftKeys = new Dictionary<string, string>{ {"1","!"},{"2","\""},{"3","№"},{"4",";"},{"5","%"},{"6",":"},{"7","?"},{"8","*"},{"9","("},{"0",")"},{"-","_"},{"=","+"},{"`","Ё"},{"q","Й"},{"w","Ц"},{"e","У"},{"r","К"},{"t","Е"},{"y","Н"},{"u","Г"},{"i","Ш"},{"o","Щ"},{"p","З"},{"[","Х"},{"]","Ъ"},{"a","Ф"},{"s","Ы"},{"d","В"},{"f","А"},{"g","П"},{"h","Р"},{"j","О"},{"k","Л"},{"l","Д"},{";","Ж"},{"'","Э"},{"z","Я"},{"x","Ч"},{"c","С"},{"v","М"},{"b","И"},{"n","Т"},{"m","Ь"},{",","Б"},{".","Ю"},{"/",","} }
        };
    }

    [Fact]
    public void AutoDetect_Works_For_Clear_Cases()
    {
        var converter = new LayoutConverter();
        var (text, src, tgt) = converter.AutoConvert("ghbdtn", new[] { _en, _ru });
        Assert.Equal("привет", text);
        Assert.Equal("en-US", src?.Code);
        Assert.Equal("ru-RU", tgt?.Code);
    }

    [Fact]
    public void AutoDetect_Returns_Null_For_Ambiguous()
    {
        var converter = new LayoutConverter();
        var (text, src, tgt) = converter.AutoConvert("123", new[] { _en, _ru });
        Assert.Null(text);
    }

    [Fact]
    public void AutoDetect_Returns_Null_For_Mixed()
    {
        var converter = new LayoutConverter();
        var (text, src, tgt) = converter.AutoConvert("ghbdtn привет", new[] { _en, _ru });
        Assert.Null(text);
    }
}
