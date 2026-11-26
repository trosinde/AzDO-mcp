namespace AzdoMcp.Core;

/// <summary>Tool groups as shown on the "Tools &amp; Rechte" page.</summary>
public enum ToolGroup
{
    WorkItemsRead,
    WorkItemsWrite,
    Git,
    Test,
    Planning,
    System,
    Pipelines,
}

public static class ToolGroupNames
{
    public static string Display(this ToolGroup group) => group switch
    {
        ToolGroup.WorkItemsRead => "Work Items lesen",
        ToolGroup.WorkItemsWrite => "Work Items schreiben",
        ToolGroup.Git => "Git",
        ToolGroup.Pipelines => "Pipelines",
        ToolGroup.Test => "Test",
        ToolGroup.Planning => "Planung",
        ToolGroup.System => "System",
        _ => group.ToString(),
    };
}
