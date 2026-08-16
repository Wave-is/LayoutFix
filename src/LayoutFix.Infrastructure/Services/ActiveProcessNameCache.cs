namespace LayoutFix.Infrastructure.Services;

/// <summary>
/// Keeps the foreground process lookup off the repeated low-level hotkey path.
/// A foreground top-level window and PID remain stable while the user works in
/// one application; switching either identity refreshes the value.
/// </summary>
internal sealed class ActiveProcessNameCache
{
    private readonly object _missGate = new();
    private Entry? _entry;

    public string GetOrResolve(
        IntPtr window,
        uint processId,
        Func<uint, string> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (window == IntPtr.Zero || processId == 0)
            return string.Empty;

        var cached = Volatile.Read(ref _entry);
        if (cached is not null && cached.Matches(window, processId))
            return cached.ProcessName;

        lock (_missGate)
        {
            cached = Volatile.Read(ref _entry);
            if (cached is not null && cached.Matches(window, processId))
                return cached.ProcessName;

            var processName = resolver(processId);
            if (!string.IsNullOrWhiteSpace(processName))
                Volatile.Write(ref _entry, new Entry(window, processId, processName));
            return processName;
        }
    }

    private sealed record Entry(
        IntPtr Window,
        uint ProcessId,
        string ProcessName)
    {
        public bool Matches(IntPtr window, uint processId) =>
            Window == window && ProcessId == processId;
    }
}
