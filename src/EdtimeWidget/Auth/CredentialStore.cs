using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using EdtimeWidget.Api;

namespace EdtimeWidget.Auth;

/// <summary>Stores login and session token in the Windows Credential Manager (per user, DPAPI protected).</summary>
public static class CredentialStore
{
    private const string LoginTarget = "EdtimeWidget:login";
    private const string SessionTarget = "EdtimeWidget:session";

    public static (string User, string Password)? LoadLogin() =>
        Read(LoginTarget) is { } c ? (c.User, c.Secret) : null;

    public static void SaveLogin(string user, string password) => Write(LoginTarget, user, password);

    public static void DeleteLogin()
    {
        Delete(LoginTarget);
        Delete(SessionTarget);
    }

    public static Session? LoadSession()
    {
        if (Read(SessionTarget) is not { } c) return null;
        var parts = c.User.Split('|');
        if (parts.Length != 2 || !long.TryParse(parts[0], out var eid) || !long.TryParse(parts[1], out var exp)) return null;
        return new Session(c.Secret, eid, DateTimeOffset.FromUnixTimeSeconds(exp));
    }

    public static void SaveSession(Session? session)
    {
        if (session is null) Delete(SessionTarget);
        else Write(SessionTarget, $"{session.EmployeeId}|{session.Expires.ToUnixTimeSeconds()}", session.Token);
    }

    private static (string User, string Secret)? Read(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            var secret = cred.CredentialBlobSize > 0
                ? Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2)
                : string.Empty;
            return (cred.UserName ?? string.Empty, secret);
        }
        finally
        {
            CredFree(ptr);
        }
    }

    private static void Write(string target, string user, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = user,
                CredentialBlob = blobPtr,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
            };
            if (!CredWrite(ref cred, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    private static void Delete(string target) => CredDelete(target, CRED_TYPE_GENERIC, 0);

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
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
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
