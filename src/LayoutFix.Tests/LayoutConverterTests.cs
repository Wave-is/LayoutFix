using System;
using System.Collections.Generic;
using LayoutFix.Core.Models;
using LayoutFix.Core.Services;
using Xunit;

namespace LayoutFix.Tests;

public class LayoutConverterTests
{
    private Layout _enLayout;
    private Layout _ruLayout;

    public LayoutConverterTests()
    {
        _enLayout = new Layout
        {
            Code = "en-US",
            Keys = new Dictionary<string, string>
            {
                { "q", "q" }, { "w", "w" }, { "e", "e" }, { "r", "r" },
                { "a", "a" }, { "s", "s" }, { "d", "d" }, { "f", "f" }
            },
            ShiftKeys = new Dictionary<string, string>
            {
                { "q", "Q" }, { "w", "W" }, { "e", "E" }, { "r", "R" },
                { "a", "A" }, { "s", "S" }, { "d", "D" }, { "f", "F" }
            }
        };

        _ruLayout = new Layout
        {
            Code = "ru-RU",
            Keys = new Dictionary<string, string>
            {
                { "q", "й" }, { "w", "ц" }, { "e", "у" }, { "r", "к" },
                { "a", "ф" }, { "s", "ы" }, { "d", "в" }, { "f", "а" }
            },
            ShiftKeys = new Dictionary<string, string>
            {
                { "q", "Й" }, { "w", "Ц" }, { "e", "У" }, { "r", "К" },
                { "a", "Ф" }, { "s", "Ы" }, { "d", "В" }, { "f", "А" }
            }
        };
    }

    [Fact]
    public void ConvertTo_EnToRu_CorrectlyConverts()
    {
        var converter = new LayoutConverter();
        string result = converter.ConvertTo("qwerty", _ruLayout, _enLayout);
        
        // Since we only mocked q,w,e,r in ruLayout keys, 't' and 'y' will remain 't' and 'y'
        Assert.Equal("йцукty", result);
    }

    [Fact]
    public void ConvertTo_RuToEn_CorrectlyConverts()
    {
        var converter = new LayoutConverter();
        string result = converter.ConvertTo("йцук", _enLayout, _ruLayout);
        Assert.Equal("qwer", result);
    }

    [Fact]
    public void ConvertTo_WithUpperCase_PreservesCase()
    {
        var converter = new LayoutConverter();
        string result = converter.ConvertTo("QweR", _ruLayout, _enLayout);
        Assert.Equal("ЙцуК", result);
    }
}
