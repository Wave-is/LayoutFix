using LayoutFix.Core.Services;
using Xunit;

namespace LayoutFix.Tests;

public class ChangeCaseTests
{
    [Fact]
    public void ChangeCase_Cycles_Through_Forms()
    {
        var transformer = new TextTransformer();
        string step1 = transformer.ChangeCase("hello");
        Assert.Equal("HELLO", step1);
        string step2 = transformer.ChangeCase(step1);
        Assert.Equal("Hello", step2);
        string step3 = transformer.ChangeCase(step2);
        Assert.Equal("hELLO", step3);
        string step4 = transformer.ChangeCase(step3);
        Assert.Equal("hello", step4);
    }
}
