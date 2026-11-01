using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PCCleaner.Models;
using PCCleaner.Services.Native;

namespace PCCleaner.Services.Security;

/// <summary>Checks a file's embedded Authenticode signature (no revocation check, so it stays fast and offline).</summary>
internal static class Authenticode
{
    public static SignatureState Check(string path)
    {
        if (!File.Exists(path)) return SignatureState.Unknown;

        var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>(),
            pcwszFilePath = path,
        };

        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, filePtr, false);
            var data = new NativeMethods.WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_DATA>(),
                dwUIChoice = NativeMethods.WTD_UI_NONE,
                fdwRevocationChecks = NativeMethods.WTD_REVOKE_NONE,
                dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
                pFile = filePtr,
                dwStateAction = NativeMethods.WTD_STATEACTION_VERIFY,
                dwProvFlags = NativeMethods.WTD_REVOCATION_CHECK_NONE | NativeMethods.WTD_CACHE_ONLY_URL_RETRIEVAL,
            };

            var action = NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2;
            var result = NativeMethods.WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            data.dwStateAction = NativeMethods.WTD_STATEACTION_CLOSE;
            NativeMethods.WinVerifyTrust(IntPtr.Zero, ref action, ref data);

            // Any failure (no signature, tampered file, untrusted root) counts as unsigned.
            if (result != 0) return SignatureState.Unsigned;
            return IsMicrosoftSigner(path) ? SignatureState.SignedByMicrosoft : SignatureState.Signed;
        }
        finally
        {
            Marshal.DestroyStructure<NativeMethods.WINTRUST_FILE_INFO>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    private static bool IsMicrosoftSigner(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // No loader replacement exists for reading the signer of a signed file.
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return certificate.Subject.Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
