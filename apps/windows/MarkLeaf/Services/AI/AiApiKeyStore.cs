using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MarkLeaf.Services.AI;

public interface IAiApiKeyStore
{
    string? Load(string? providerId = null);
    void Save(string apiKey, string? providerId = null);
    void Delete(string? providerId = null);
}

public sealed class WindowsCredentialAiApiKeyStore : IAiApiKeyStore
{
    private const string TargetPrefix = "MarkLeaf/TypographyAI/";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public string? Load(string? providerId = null)
    {
        var targetName = Target(providerId);
        if (!CredRead(targetName, CredTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 1168) return null;
            throw new Win32Exception(error, "无法从 Windows 凭据管理器读取 API Key。");
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0
                ? null
                : Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
        }
        finally { CredFree(pointer); }
    }

    public void Save(string apiKey, string? providerId = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { Delete(providerId); return; }
        var bytes = System.Text.Encoding.Unicode.GetBytes(apiKey);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(providerId),
                CredentialBlobSize = checked((uint)bytes.Length),
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName,
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法将 API Key 保存到 Windows 凭据管理器。");
        }
        finally { Marshal.FreeCoTaskMem(blob); }
    }

    public void Delete(string? providerId = null)
    {
        if (CredDelete(Target(providerId), CredTypeGeneric, 0)) return;
        var error = Marshal.GetLastWin32Error();
        if (error != 1168) throw new Win32Exception(error, "无法从 Windows 凭据管理器删除 API Key。");
    }

    private static string Target(string? providerId)
    {
        var safe = string.IsNullOrWhiteSpace(providerId)
            ? "openai"
            : new string(providerId.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return TargetPrefix + (safe.Length == 0 ? "custom" : safe);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
