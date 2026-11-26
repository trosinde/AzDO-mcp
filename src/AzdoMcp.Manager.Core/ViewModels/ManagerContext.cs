using AzdoMcp.Core;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Discovery;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Manager.Core.Clients;
using AzdoMcp.Manager.Core.Ipc;
using AzdoMcp.Manager.Core.Mvvm;
using AzdoMcp.Manager.Core.Services;

namespace AzdoMcp.Manager.Core.ViewModels;

/// <summary>Everything the view models share. Built once by the app (or a test).</summary>
public sealed class ManagerContext
{
    public required AppPaths Paths { get; init; }

    public required ConnectionStore Connections { get; init; }

    public required PermissionStore Permissions { get; init; }

    public required AuditLog Audit { get; init; }

    public required ManagerHost Host { get; init; }

    public required ConnectionTester Tester { get; init; }

    public required ProjectDiscovery Discovery { get; init; }

    public required ClientSetupService Clients { get; init; }

    public required AutostartService Autostart { get; init; }

    public required ReminderScheduler Reminders { get; init; }

    public required ManagerEnvironment Environment { get; init; }

    public required IDialogs Dialogs { get; init; }

    public IUiDispatcher Dispatcher { get; init; } = new ImmediateDispatcher();

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Paths searched for a v1 config.json.</summary>
    public IReadOnlyList<string> LegacyCandidates { get; init; } = LegacyImporter.DefaultCandidates();

    /// <summary>Footer text after "v2.0.0 · " when no <see cref="Updates"/> service is set.</summary>
    public string UpdateStatus { get; set; } = "Updates: nicht konfiguriert";

    /// <summary>Velopack update status and apply; null in tests that don't need it.</summary>
    public UpdateService? Updates { get; init; }

    public DateOnly Today => DateOnly.FromDateTime(Time.GetLocalNow().DateTime);

    /// <summary>Page switch requested by a view model: key ("wizard", "rescan", "connections", …) and argument (connection id).</summary>
    public Action<string, string?> Navigate { get; set; } = (_, _) => { };

    /// <summary>Raised after connections or permissions were saved from any page.</summary>
    public event EventHandler? ConfigurationChanged;

    public void NotifyConfigurationChanged()
    {
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        _ = Host.ReloadAsync();
    }

    public ConnectionStatus StatusOf(Connection c) => ExpiryClassifier.Classify(c, Today, Connections.CanReadPat(c));

    public IReadOnlyList<Connection> SafeConnections()
    {
        try
        {
            return Connections.GetAll();
        }
        catch (ConfigurationException ex)
        {
            Dialogs.Error("Konfiguration", ex.Message);
            return [];
        }
    }
}

/// <summary>Pill for an Ampel status.</summary>
public sealed record Pill(string Text, StatusLevel? Level, string? Tooltip = null)
{
    /// <summary>"g", "y", "r" or "n" (neutral) – matches the CSS classes of the UI proposal.</summary>
    public string Tone => Level switch
    {
        StatusLevel.Ok => "g",
        StatusLevel.Warning => "y",
        StatusLevel.Error => "r",
        _ => "n",
    };
}

public static class RelativeTime
{
    public static string Since(DateTimeOffset? then, DateTimeOffset now)
    {
        if (then is null)
        {
            return "noch kein Aufruf";
        }

        var d = now - then.Value;
        return d.TotalSeconds < 60 ? "gerade eben"
            : d.TotalMinutes < 60 ? $"vor {(int)d.TotalMinutes} min"
            : d.TotalHours < 24 ? $"vor {(int)d.TotalHours} h"
            : $"vor {(int)d.TotalDays} Tagen";
    }
}

/// <summary>Lesen and Schreiben of one area as two pills.</summary>
public sealed record RightsRow(AccessArea Area, string Name, string Short, Pill Read, Pill Write);

/// <summary>Pills for check values: ja = green, nein = red, n/a and ? = grey with an explanation.</summary>
public static class RightPills
{
    public static Pill Cell(Check? check, string? label = null)
    {
        var level = check switch
        {
            Check.Yes => StatusLevel.Ok,
            Check.No => (StatusLevel?)StatusLevel.Error,
            _ => null,
        };
        var tooltip = label is null ? check.Explain() : check.Explain() is { } why ? $"{label}: {check.Display()} – {why}" : $"{label}: {check.Display()}";
        return new Pill(check.Display(), level, tooltip);
    }

    public static string Short(AccessArea area) => area switch
    {
        AccessArea.WorkItems => "WI",
        AccessArea.Code => "Code",
        AccessArea.Test => "Test",
        AccessArea.Pipelines => "Pipe",
        _ => "Iter",
    };

    public static RightsRow Row(AccessArea area, AreaAccess access) => new(area, area.Display(), Short(area),
        Cell(access.Read, $"{area.Display()} Lesen"), Cell(access.Write, $"{area.Display()} Schreiben"));

    public static IReadOnlyList<RightsRow> Rows(AreaRights? rights) =>
        Access.Areas.Select(a => Row(a, rights?[a] ?? AreaAccess.Unknown)).ToList();
}
