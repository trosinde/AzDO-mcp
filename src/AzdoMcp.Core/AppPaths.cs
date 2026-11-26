namespace AzdoMcp.Core;

/// <summary>
/// Locations of the per-user data. Deliberately separate from the program folder
/// (%LOCALAPPDATA%\AzdoMcp\ is the installer root and is removed on uninstall).
/// </summary>
public sealed class AppPaths
{
    public const string DataFolderName = "AzdoMcpData";

    public AppPaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    /// <summary>%LOCALAPPDATA%\AzdoMcpData, overridable with AZDOMCP_DATA (tests, portable use).</summary>
    public static AppPaths Default()
    {
        var overridden = Environment.GetEnvironmentVariable("AZDOMCP_DATA");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return new AppPaths(overridden);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(Path.Combine(local, DataFolderName));
    }

    public string Root { get; }

    public string ConnectionsFile => Path.Combine(Root, "connections.json");

    public string SecretsDirectory => Path.Combine(Root, "secrets");

    public string LogsDirectory => Path.Combine(Root, "logs");

    public string SecretFile(string connectionId) => Path.Combine(SecretsDirectory, connectionId + ".bin");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SecretsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>Short stable hash of the root, used to scope named mutexes per data root.</summary>
    internal string RootHash
    {
        get
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Root.ToUpperInvariant()));
            return Convert.ToHexString(bytes, 0, 8);
        }
    }
}
