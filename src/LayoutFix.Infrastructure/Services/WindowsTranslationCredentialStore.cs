using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using LayoutFix.Core.Interfaces;

namespace LayoutFix.Infrastructure.Services;

/// <summary>
/// Stores the user-supplied Google Cloud key in Windows Credential Manager.
/// The secret never enters settings.json or application logs.
/// </summary>
public sealed class WindowsTranslationCredentialStore : ITranslationCredentialStore
{
    private const string TargetName = "LayoutFix/GoogleCloudTranslationApiKey";
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBytes = 512;
    private readonly object _sync = new();

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ReadApiKey());

    public string? ReadApiKey()
    {
        lock (_sync)
        {
            if (!CredRead(TargetName, CredentialTypeGeneric, 0, out var credentialPointer))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound) return null;
                throw new Win32Exception(error, "Could not read the translation API key from Windows Credential Manager.");
            }

            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
                if (credential.CredentialBlob == 0 || credential.CredentialBlobSize == 0)
                    return null;

                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                var value = Encoding.UTF8.GetString(bytes);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            finally
            {
                CredFree(credentialPointer);
            }
        }
    }

    public void SaveApiKey(string? apiKey)
    {
        lock (_sync)
        {
            var normalized = apiKey?.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                if (!CredDelete(TargetName, CredentialTypeGeneric, 0) &&
                    Marshal.GetLastWin32Error() != ErrorNotFound)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Could not remove the translation API key from Windows Credential Manager.");
                }
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(normalized);
            if (bytes.Length > MaximumCredentialBytes)
                throw new ArgumentException("The translation API key is too long.", nameof(apiKey));

            var blob = Marshal.AllocCoTaskMem(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new NativeCredential
                {
                    Type = CredentialTypeGeneric,
                    TargetName = TargetName,
                    CredentialBlobSize = (uint)bytes.Length,
                    CredentialBlob = blob,
                    Persist = CredentialPersistLocalMachine,
                    UserName = Environment.UserName
                };
                if (!CredWrite(ref credential, 0))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Could not save the translation API key in Windows Credential Manager.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(blob);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out nint credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint buffer);
}
