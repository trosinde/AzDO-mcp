using System.Text.Json;
using AzdoMcp.Core;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Infrastructure;

namespace AzdoMcp.Manager.Core.Services;

/// <summary>UI interactions the view models need; WPF implements them, tests fake them.</summary>
public interface IDialogs
{
    bool Confirm(string title, string message);

    /// <summary>Returns true = save, false = discard, null = cancel.</summary>
    bool? AskSaveChanges(string message);

    void Info(string title, string message);

    void Error(string title, string message);

    string? SaveFile(string title, string defaultName, string filter);

    string? PickFolder(string title);

    void OpenUrl(string url);

    void OpenFolder(string path);

    void CopyText(string text);
}

/// <summary>Where things are on this machine.</summary>
public sealed class ManagerEnvironment
{
    public required string LocalAppData { get; init; }

    public required string AppData { get; init; }

    public required string UserProfile { get; init; }

    /// <summary>Folder of the running Manager (contains azdo-mcp.exe).</summary>
    public required string AppDirectory { get; init; }

    public string Version { get; init; } = "2.0.0";

    public static ManagerEnvironment FromSystem(string version) => new()
    {
        LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        AppDirectory = AppContext.BaseDirectory,
        Version = version,
    };

    /// <summary>%LOCALAPPDATA%\AzdoMcp\current – the Velopack install folder that survives updates.</summary>
    public string InstalledDirectory => Path.Combine(LocalAppData, "AzdoMcp", "current");

    public bool IsInstalled =>
        Path.GetFullPath(AppDirectory).TrimEnd('\\', '/').Equals(Path.GetFullPath(InstalledDirectory).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>The path written into client configs: the stable installed path, or the portable folder.</summary>
    public string ServerExePath => IsInstalled
        ? Path.Combine(InstalledDirectory, "azdo-mcp.exe")
        : Path.Combine(AppDirectory, "azdo-mcp.exe");

    /// <summary>Path for display, with %LOCALAPPDATA% instead of the user folder.</summary>
    public string ServerExeDisplayPath => ServerExePath.StartsWith(LocalAppData, StringComparison.OrdinalIgnoreCase)
        ? "%LOCALAPPDATA%" + ServerExePath[LocalAppData.Length..]
        : ServerExePath;

    public string ManagerExePath => Path.Combine(AppDirectory, "AzdoMcp.Manager.exe");
}

/// <summary>HKCU\...\Run abstraction.</summary>
public interface IRunKey
{
    string? Get(string name);

    void Set(string name, string value);

    void Delete(string name);
}

/// <summary>"Autostart bei Anmeldung" – per-user Run key, no admin rights.</summary>
public sealed class AutostartService(IRunKey runKey, string managerExePath)
{
    public const string ValueName = "AzdoMcpManager";

    public string Command => $"\"{managerExePath}\" --minimized";

    public bool IsEnabled => runKey.Get(ValueName) is { } v && v.Contains(managerExePath, StringComparison.OrdinalIgnoreCase);

    public void Enable() => runKey.Set(ValueName, Command);

    public void Disable() => runKey.Delete(ValueName);

    public void Set(bool enabled)
    {
        if (enabled)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }
}

/// <summary>Daily PAT reminders; remembers per connection the last day a balloon was shown.</summary>
public sealed class ReminderScheduler(AppPaths paths, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private string FilePath => Path.Combine(paths.Root, "reminders.json");

    public sealed record Reminder(Connection Connection, string Text);

    /// <summary>Connections that expire within their lead time (or expired) and were not reminded today.</summary>
    public IReadOnlyList<Reminder> Due(IEnumerable<Connection> connections)
    {
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var shown = Load();
        var result = new List<Reminder>();
        foreach (var c in connections)
        {
            var days = ExpiryClassifier.DaysLeft(c, today);
            if (days is null || days > c.ReminderDays)
            {
                continue;
            }

            if (shown.TryGetValue(c.Id, out var last) && last == today)
            {
                continue;
            }

            var text = days switch
            {
                < 0 => $"PAT für {c.Name} ist abgelaufen",
                0 => $"PAT für {c.Name} läuft heute ab",
                1 => $"PAT für {c.Name} läuft in 1 Tag ab",
                _ => $"PAT für {c.Name} läuft in {days} Tagen ab",
            };
            result.Add(new Reminder(c, text));
        }

        return result;
    }

    public void MarkShown(IEnumerable<Connection> connections)
    {
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var shown = Load();
        foreach (var c in connections)
        {
            shown[c.Id] = today;
        }

        paths.EnsureCreated();
        FileUtil.WriteAllTextAtomic(FilePath, JsonSerializer.Serialize(shown, Json.File));
    }

    private Dictionary<string, DateOnly> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, DateOnly>>(File.ReadAllText(FilePath), Json.File) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
