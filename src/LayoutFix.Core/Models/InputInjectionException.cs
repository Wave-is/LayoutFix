namespace LayoutFix.Core.Models;

public enum InputInjectionOperation
{
    KeyCombination,
    Backspace,
    Text
}

public sealed class InputInjectionException : Exception
{
    public InputInjectionException(
        InputInjectionOperation operation,
        int requestedUnitCount,
        int affectedUnitCount,
        int requestedEventCount,
        int acceptedEventCount,
        Exception? innerException = null)
        : base(
            $"Input injection accepted {acceptedEventCount} of {requestedEventCount} events.",
            innerException)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requestedUnitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(affectedUnitCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(affectedUnitCount, requestedUnitCount);
        ArgumentOutOfRangeException.ThrowIfNegative(requestedEventCount);
        ArgumentOutOfRangeException.ThrowIfNegative(acceptedEventCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(acceptedEventCount, requestedEventCount);

        Operation = operation;
        RequestedUnitCount = requestedUnitCount;
        AffectedUnitCount = affectedUnitCount;
        RequestedEventCount = requestedEventCount;
        AcceptedEventCount = acceptedEventCount;
    }

    public InputInjectionOperation Operation { get; }
    public int RequestedUnitCount { get; }
    public int AffectedUnitCount { get; }
    public int RequestedEventCount { get; }
    public int AcceptedEventCount { get; }
}
