using System;
using LayoutFix.Core.Models;
public class Test {
    public static void Main() {
        var combo = HotkeyCombo.Parse("Shift+Scroll");
        Console.WriteLine($"Shift: {combo.Shift}, Key: {combo.Key}, VirtualKey: {combo.VirtualKey}");
    }
}
