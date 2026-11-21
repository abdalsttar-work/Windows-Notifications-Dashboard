using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace NotifyHub.Services;

public class WindowsCredentialManagerService : ISecureStorageService
{
    private const string TargetPrefix = "NotifyHub_";
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree([In] IntPtr cred);

    public void SaveSecret(string key, string secret)
    {
        string safeKey = SanitizeKey(key);
        string target = TargetPrefix + safeKey;
        try
        {
            byte[] credentialBlob = Encoding.Unicode.GetBytes(secret);
            GCHandle handle = GCHandle.Alloc(credentialBlob, GCHandleType.Pinned);
            try
            {
                var cred = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = target,
                    CredentialBlobSize = credentialBlob.Length,
                    CredentialBlob = handle.AddrOfPinnedObject(),
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = Environment.UserName,
                    Comment = "NotifyHub Secure Credential"
                };

                if (CredWrite(ref cred, 0)) return;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(credentialBlob);
                handle.Free();
            }
        }
        catch
        {
            // Fallback to Windows DPAPI encrypted user store
        }

        SaveDpapiSecret(safeKey, secret);
    }

    public string? GetSecret(string key)
    {
        string safeKey = SanitizeKey(key);
        string target = TargetPrefix + safeKey;
        try
        {
            if (CredRead(target, CRED_TYPE_GENERIC, 0, out IntPtr credPtr))
            {
                try
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                    if (cred.CredentialBlob != IntPtr.Zero && cred.CredentialBlobSize > 0)
                    {
                        byte[] blob = new byte[cred.CredentialBlobSize];
                        try
                        {
                            Marshal.Copy(cred.CredentialBlob, blob, 0, cred.CredentialBlobSize);
                            return Encoding.Unicode.GetString(blob);
                        }
                        finally
                        {
                            CryptographicOperations.ZeroMemory(blob);
                        }
                    }
                }
                finally
                {
                    CredFree(credPtr);
                }
            }
        }
        catch
        {
            // Fallback to Windows DPAPI
        }

        return GetDpapiSecret(safeKey);
    }

    public void DeleteSecret(string key)
    {
        string safeKey = SanitizeKey(key);
        try { CredDelete(TargetPrefix + safeKey, CRED_TYPE_GENERIC, 0); } catch { }
        DeleteDpapiSecret(safeKey);
    }

    private static string SanitizeKey(string key)
    {
        string name = Path.GetFileName(key ?? "default");
        return string.IsNullOrWhiteSpace(name) ? "default" : name;
    }

    private static string GetDpapiFilePath(string safeKey)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(appData, "NotifyHub", ".secure");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"{safeKey}.dat");
    }

    private static void SaveDpapiSecret(string safeKey, string secret)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        try
        {
            byte[] encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(GetDpapiFilePath(safeKey), encrypted);
        }
        catch { }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string? GetDpapiSecret(string safeKey)
    {
        try
        {
            string path = GetDpapiFilePath(safeKey);
            if (!File.Exists(path)) return null;

            byte[] encrypted = File.ReadAllBytes(path);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.UTF8.GetString(decrypted);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decrypted);
            }
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteDpapiSecret(string safeKey)
    {
        try
        {
            string path = GetDpapiFilePath(safeKey);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
