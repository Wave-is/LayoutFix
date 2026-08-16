using System.Text;

namespace LayoutFix.Infrastructure.Hooks;

internal readonly record struct KeyboardTextObservation(string Text, bool IsDeadKey);

internal static class KeyboardTextDecoder
{
    public static KeyboardTextObservation Decode(int result, StringBuilder buffer)
    {
        if (result < 0)
            return new KeyboardTextObservation(string.Empty, IsDeadKey: true);
        if (result == 0 || buffer.Length == 0)
            return new KeyboardTextObservation(string.Empty, IsDeadKey: false);

        var length = Math.Min(result, buffer.Length);
        return new KeyboardTextObservation(
            buffer.ToString(0, length),
            IsDeadKey: false);
    }
}
