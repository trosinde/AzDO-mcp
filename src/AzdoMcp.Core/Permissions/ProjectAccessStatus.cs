using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Tools;

namespace AzdoMcp.Core.Permissions;

public enum AccessStatusKind
{
    Direct,
    WithApproval,
    ReadOnly,
    NoAccess,
    NotVisible,
}

public enum ReadOnlyReason
{
    Pat,
    ProjectRight,
    WriteSwitch,
    ToolsBlocked,
}

/// <summary>
/// What the server does in a project, with the reason when it only reads. One place for the
/// Übersicht, the wizard, "Tools &amp; Rechte", list_projects and the discover command.
/// </summary>
public sealed record ProjectAccessStatus(AccessStatusKind Kind, ReadOnlyReason? Reason, bool WriteUntested, IReadOnlyList<string> Derivation)
{
    public string Text
    {
        get
        {
            var text = Kind switch
            {
                AccessStatusKind.NotVisible => "nicht mehr sichtbar",
                AccessStatusKind.NoAccess => "kein Zugriff",
                AccessStatusKind.Direct => "schreibt direkt",
                AccessStatusKind.WithApproval => "schreibt mit Freigabe",
                _ => "nur Lesen · " + ReasonText(Reason),
            };
            return WriteUntested && Kind is AccessStatusKind.Direct or AccessStatusKind.WithApproval
                ? text + " · Schreibrecht nicht geprüft"
                : text;
        }
    }

    public string Tooltip => string.Join(Environment.NewLine, Derivation);

    public static string ReasonText(ReadOnlyReason? reason) => reason switch
    {
        ReadOnlyReason.Pat => "PAT",
        ReadOnlyReason.ProjectRight => "Projektrecht",
        ReadOnlyReason.WriteSwitch => "Schreibschalter aus",
        _ => "Schreib-Tools gesperrt",
    };

    /// <summary>Why the Schreibschalter cannot be switched on, or null when it can.</summary>
    public static string? WriteLockHint(AreaRights? patScope, AreaRights? projectRights) =>
        patScope?[AccessArea.WorkItems].Write == Check.No ? "PAT ohne Schreibrecht"
        : projectRights?[AccessArea.WorkItems].Write == Check.No ? "kein Projektrecht zum Bearbeiten"
        : null;

    public const string UntestedHint = "Schreibrecht nicht prüfbar – Azure DevOps entscheidet beim Schreiben";

    public static ProjectAccessStatus For(ProjectRef p, PermissionSettings settings)
    {
        var effective = p.Effective;
        var wi = effective[AccessArea.WorkItems];
        var pat = p.Connection.PatScope?[AccessArea.WorkItems] ?? AreaAccess.Unknown;
        var proj = p.Project.ProjectRights?[AccessArea.WorkItems] ?? AreaAccess.Unknown;

        var writeModes = ToolCatalog.All.Where(t => t.IsWrite).Select(t => PermissionEvaluator.Resolve(settings, t, p.Connection.Id, p.PermissionKey).Mode).ToList();
        var bestMode = writeModes.Contains(PermissionMode.Allowed) ? PermissionMode.Allowed
            : writeModes.Contains(PermissionMode.Ask) ? PermissionMode.Ask
            : PermissionMode.Blocked;

        var derivation = new List<string>
        {
            $"Work Items Schreiben: PAT {pat.Write.Display()} · Projektrecht {proj.Write.Display()} → {wi.Write.Display()}",
            $"Schreibschalter {(p.Project.AllowWrite ? "an" : "aus")} · Modus Schreib-Tools: {ModeText(bestMode)}",
        };

        AccessStatusKind kind;
        ReadOnlyReason? reason = null;
        if (!p.Project.Visible)
        {
            kind = AccessStatusKind.NotVisible;
        }
        else if (Access.Areas.All(a => effective[a].Read == Check.No))
        {
            kind = AccessStatusKind.NoAccess;
        }
        else if (wi.Write == Check.No || wi.Read == Check.No)
        {
            kind = AccessStatusKind.ReadOnly;
            reason = pat.Write == Check.No || pat.Read == Check.No ? ReadOnlyReason.Pat : ReadOnlyReason.ProjectRight;
        }
        else if (!p.Project.AllowWrite)
        {
            kind = AccessStatusKind.ReadOnly;
            reason = ReadOnlyReason.WriteSwitch;
        }
        else
        {
            kind = bestMode switch
            {
                PermissionMode.Allowed => AccessStatusKind.Direct,
                PermissionMode.Ask => AccessStatusKind.WithApproval,
                _ => AccessStatusKind.ReadOnly,
            };
            reason = kind == AccessStatusKind.ReadOnly ? ReadOnlyReason.ToolsBlocked : null;
        }

        return new ProjectAccessStatus(kind, reason, wi.Write is null or Check.NotTestable, derivation);
    }

    private static string ModeText(PermissionMode m) => m switch
    {
        PermissionMode.Allowed => "Erlaubt",
        PermissionMode.Ask => "Nachfragen",
        _ => "Gesperrt",
    };
}
