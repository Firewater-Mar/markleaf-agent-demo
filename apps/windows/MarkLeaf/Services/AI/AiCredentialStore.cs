using System.Runtime.InteropServices;
using System.Text;

namespace MarkLeaf.Services.AI;

/// <summary>
/// Stores the active API key with Windows DPAPI. The encrypted value is bound
/// to the current Windows account and is never written to a project or export.
/// </summary>
internal sealed class AiCredentialStore
{
    private const int CryptProtectUiForbidden = 0x1;
    private readonly string _path;

    public AiCredentialStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "ai-credential.bin");
    }

    public string Load()
    {
        try
        {
            if (!File.Exists(_path)) return string.Empty;
            return Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(_path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    public void Save(string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (File.Exists(_path)) File.Delete(_path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, Protect(Encoding.UTF8.GetBytes(value.Trim())));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // The session key remains usable even when secure persistence fails.
        }
    }

    private static byte[] Protect(byte[] data) => Transform(data, protect: true);
    private static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new DataBlob();
        var output = new DataBlob();
        try
        {
            input.Size = data.Length;
            input.Data = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, input.Data, data.Length);
            var success = protect
                ? CryptProtectData(ref input, "MarkLeaf Agent API Key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero) Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
