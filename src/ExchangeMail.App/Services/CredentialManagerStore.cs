using System.Runtime.InteropServices;
using System.Text;
using ExchangeMail.ViewModels;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace ExchangeMail.App.Services;

/// <summary>
/// Stores the password in Windows Credential Manager ("Windows Kimlik Bilgileri" → "Genel Kimlik Bilgileri",
/// entries named Postaci:&lt;user&gt;). Uses the Win32 Cred* API, which works for unpackaged apps
/// (the WinRT PasswordVault does not reliably persist without package identity).
/// </summary>
public sealed class CredentialManagerStore : ICredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    public string? GetPassword(string userName)
    {
        var password = Read(Target(userName));
        if (password is not null) return password;

        // Entry saved before the app was renamed: move it to the new name.
        password = Read(Target(userName, AppInfo.LegacyId));
        if (password is null) return null;
        try
        {
            SavePassword(userName, password);
            CredDeleteW(Target(userName, AppInfo.LegacyId), CredTypeGeneric, 0);
        }
        catch (InvalidOperationException)
        {
            // keep using the old entry
        }
        return password;
    }

    private static string? Read(string target)
    {
        if (!CredReadW(target, CredTypeGeneric, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return null;
            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void SavePassword(string userName, string password)
    {
        var blob = Encoding.Unicode.GetBytes(password);
        var blobPointer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(userName),
                Comment = AppInfo.Name,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredPersistLocalMachine,
                UserName = userName,
            };
            if (!CredWriteW(ref credential, 0))
                throw new InvalidOperationException($"Parola Kimlik Bilgisi Yöneticisi'ne yazılamadı (hata {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Marshal.FreeHGlobal(blobPointer);
        }
    }

    public void RemovePassword(string userName)
    {
        CredDeleteW(Target(userName), CredTypeGeneric, 0);
        CredDeleteW(Target(userName, AppInfo.LegacyId), CredTypeGeneric, 0);
    }

    private static string Target(string userName, string prefix = AppInfo.Id) => prefix + ":" + userName.ToLowerInvariant();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
