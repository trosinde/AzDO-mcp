using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace AzdoMcp.Core.Connections;

public interface ISecretProtector
{
    byte[] Protect(byte[] plain);

    /// <exception cref="CryptographicException">The blob cannot be decrypted (other user, damaged).</exception>
    byte[] Unprotect(byte[] blob);
}

/// <summary>Windows DPAPI, CurrentUser scope: only the same Windows account can decrypt.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "AzdoMcp.v2.PAT"u8.ToArray();

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] blob) => ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
}

public static class SecretProtectors
{
    /// <summary>The production protector. Only Windows is supported.</summary>
    public static ISecretProtector CreateDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiSecretProtector();
        }

        throw new PlatformNotSupportedException("PAT-Speicherung benötigt Windows (DPAPI).");
    }
}

public sealed class SecretUnreadableException(string connection, Exception? inner = null)
    : Exception($"PAT für {connection} nicht lesbar – bitte ersetzen", inner)
{
    public const string ShortText = "PAT nicht lesbar – bitte ersetzen";
}
