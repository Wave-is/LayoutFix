using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace LayoutFix.Infrastructure.Services;

internal enum ProcessIntegrityLevel
{
    Unknown,
    Low,
    Medium,
    High,
    System,
    Protected
}

internal enum TargetInputAccess
{
    Unavailable,
    Allowed,
    HigherIntegrity
}

internal enum DpiAwarenessLevel
{
    Unknown,
    Unaware,
    System,
    PerMonitor,
    PerMonitorV2,
    UnawareGdiScaled
}

internal sealed record DiagnosticsCompatibilitySnapshot(
    bool? Elevated,
    bool? RemoteSession,
    ProcessIntegrityLevel ProcessIntegrity,
    DpiAwarenessLevel DpiAwareness,
    int MonitorCount,
    uint SystemDpi,
    IReadOnlyList<int> MonitorScaleFactors);

internal static class WindowsCompatibilityProbe
{
    private const int TokenIntegrityLevelInformationClass = 25;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const uint SecurityMandatoryLowRid = 0x1000;
    private const uint SecurityMandatoryMediumRid = 0x2000;
    private const uint SecurityMandatoryHighRid = 0x3000;
    private const uint SecurityMandatorySystemRid = 0x4000;
    private const uint SecurityMandatoryProtectedProcessRid = 0x5000;

    private static readonly IntPtr DpiAwarenessContextUnaware = new(-1);
    private static readonly IntPtr DpiAwarenessContextSystemAware = new(-2);
    private static readonly IntPtr DpiAwarenessContextPerMonitorAware = new(-3);
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);
    private static readonly IntPtr DpiAwarenessContextUnawareGdiScaled = new(-5);
    private static readonly Lazy<uint?> CurrentProcessIntegrityRid = new(
        CaptureCurrentProcessIntegrityRid,
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static DiagnosticsCompatibilitySnapshot Capture()
    {
        var monitorCount = TryGetMonitorCount();
        return new DiagnosticsCompatibilitySnapshot(
            IsElevated(),
            TryGetRemoteSession(),
            GetProcessIntegrityLevel(),
            GetDpiAwareness(),
            monitorCount,
            GetSystemDpi(),
            GetMonitorScaleFactors(monitorCount));
    }

    private static bool? IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return null;
        }
    }

    private static bool? TryGetRemoteSession()
    {
        try
        {
            return SystemInformation.TerminalServerSession;
        }
        catch
        {
            return null;
        }
    }

    private static int TryGetMonitorCount()
    {
        try
        {
            return Math.Max(0, SystemInformation.MonitorCount);
        }
        catch
        {
            return 0;
        }
    }

    private static ProcessIntegrityLevel GetProcessIntegrityLevel() =>
        ClassifyIntegrityLevel(CurrentProcessIntegrityRid.Value);

    private static uint? CaptureCurrentProcessIntegrityRid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return GetIntegrityRid(identity.AccessToken);
        }
        catch
        {
            return null;
        }
    }

    internal static bool CanSendInputToProcess(uint processId)
        => GetTargetInputAccess(processId) == TargetInputAccess.Allowed;

    internal static TargetInputAccess GetTargetInputAccess(uint processId)
    {
        if (processId == 0)
            return TargetInputAccess.Unavailable;

        try
        {
            using var process = OpenProcess(
                ProcessQueryLimitedInformation,
                inheritHandle: false,
                processId);
            if (process.IsInvalid ||
                !OpenProcessToken(process, TokenQuery, out var targetToken))
            {
                return TargetInputAccess.Unavailable;
            }

            using (targetToken)
                return ClassifyTargetInputAccess(
                    CurrentProcessIntegrityRid.Value,
                    GetIntegrityRid(targetToken));
        }
        catch
        {
            return TargetInputAccess.Unavailable;
        }
    }

    internal static bool IsTargetIntegrityAllowed(uint? currentRid, uint? targetRid) =>
        ClassifyTargetInputAccess(currentRid, targetRid) == TargetInputAccess.Allowed;

    internal static TargetInputAccess ClassifyTargetInputAccess(
        uint? currentRid,
        uint? targetRid)
    {
        if (!currentRid.HasValue || !targetRid.HasValue)
            return TargetInputAccess.Unavailable;

        return targetRid.Value <= currentRid.Value
            ? TargetInputAccess.Allowed
            : TargetInputAccess.HigherIntegrity;
    }

    private static uint? GetIntegrityRid(SafeAccessTokenHandle token)
    {
        _ = GetTokenInformation(
            token.DangerousGetHandle(),
            TokenIntegrityLevelInformationClass,
            IntPtr.Zero,
            0,
            out var requiredLength);
        if (requiredLength <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal(requiredLength);
        try
        {
            if (!GetTokenInformation(
                    token.DangerousGetHandle(),
                    TokenIntegrityLevelInformationClass,
                    buffer,
                    requiredLength,
                    out _))
            {
                return null;
            }

            var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            if (label.Label.Sid == IntPtr.Zero)
                return null;

            var countPointer = GetSidSubAuthorityCount(label.Label.Sid);
            if (countPointer == IntPtr.Zero)
                return null;

            var count = Marshal.ReadByte(countPointer);
            if (count == 0)
                return null;

            var ridPointer = GetSidSubAuthority(label.Label.Sid, (uint)(count - 1));
            return ridPointer == IntPtr.Zero
                ? null
                : unchecked((uint)Marshal.ReadInt32(ridPointer));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ProcessIntegrityLevel ClassifyIntegrityLevel(uint? rid) => rid switch
    {
        null or < SecurityMandatoryLowRid => ProcessIntegrityLevel.Unknown,
        < SecurityMandatoryMediumRid => ProcessIntegrityLevel.Low,
        < SecurityMandatoryHighRid => ProcessIntegrityLevel.Medium,
        < SecurityMandatorySystemRid => ProcessIntegrityLevel.High,
        < SecurityMandatoryProtectedProcessRid => ProcessIntegrityLevel.System,
        _ => ProcessIntegrityLevel.Protected
    };

    private static DpiAwarenessLevel GetDpiAwareness()
    {
        try
        {
            var context = GetThreadDpiAwarenessContext();
            if (context == IntPtr.Zero)
                return DpiAwarenessLevel.Unknown;

            if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextPerMonitorAwareV2))
                return DpiAwarenessLevel.PerMonitorV2;
            if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextPerMonitorAware))
                return DpiAwarenessLevel.PerMonitor;
            if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextSystemAware))
                return DpiAwarenessLevel.System;
            if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextUnawareGdiScaled))
                return DpiAwarenessLevel.UnawareGdiScaled;
            if (AreDpiAwarenessContextsEqual(context, DpiAwarenessContextUnaware))
                return DpiAwarenessLevel.Unaware;

            return DpiAwarenessLevel.Unknown;
        }
        catch
        {
            return DpiAwarenessLevel.Unknown;
        }
    }

    private static uint GetSystemDpi()
    {
        try
        {
            return GetDpiForSystem();
        }
        catch
        {
            return 0;
        }
    }

    private static IReadOnlyList<int> GetMonitorScaleFactors(int expectedMonitorCount)
    {
        if (expectedMonitorCount <= 0)
            return Array.Empty<int>();

        try
        {
            var values = new List<int>(expectedMonitorCount);
            MonitorEnumProc callback = (
                IntPtr monitor,
                IntPtr deviceContext,
                ref NativeRect monitorRect,
                IntPtr data) =>
            {
                if (GetScaleFactorForMonitor(monitor, out var scale) == 0 &&
                    scale is >= 100 and <= 500)
                {
                    values.Add(scale);
                }
                return true;
            };

            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                return Array.Empty<int>();

            return values;
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public SidAndAttributes Label;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool MonitorEnumProc(
        IntPtr monitor,
        IntPtr deviceContext,
        ref NativeRect monitorRect,
        IntPtr data);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        SafeProcessHandle processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthorityIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRect,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("shcore.dll")]
    private static extern int GetScaleFactorForMonitor(
        IntPtr monitor,
        out int scale);
}
