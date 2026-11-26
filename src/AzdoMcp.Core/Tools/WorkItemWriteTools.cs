using System.Text.Json.Nodes;
using AzdoMcp.Core.Azdo;
using static AzdoMcp.Core.Tools.WorkItemReadTools;

namespace AzdoMcp.Core.Tools;

internal static class WorkItemWriteTools
{
    public const long MaxAttachmentBytes = 60L * 1024 * 1024;

    public static IEnumerable<ToolDefinition> All()
    {
        yield return new("create_work_item", ToolGroup.WorkItemsWrite,
            "Legt ein Work Item an (Bug, Task, User Story, Feature, Epic oder jeden anderen Typ des Projekts).",
            Schema.Create().Str("type", "Work-Item-Typ, z. B. Task", required: true).Str("title", "Titel", required: true)
                .Str("description", "Beschreibung (HTML erlaubt)").Str("assignedTo", "Zugewiesen an")
                .Str("areaPath", "Bereichspfad").Str("iterationPath", "Iterationspfad").Str("tags", "Tags, mit ; getrennt")
                .Int("parentId", "ID des übergeordneten Work Items")
                .Obj("fields", "Weitere Felder als { \"Referenzname\": Wert }").Project().Build(),
            Create);

        yield return new("update_work_item", ToolGroup.WorkItemsWrite,
            "Ändert Felder eines Work Items, z. B. Status, Zuweisung oder Priorität.",
            Schema.Create().Int("id", "Work-Item-ID", required: true)
                .Obj("fields", "Zu setzende Felder als { \"Referenzname\": Wert }, z. B. { \"System.State\": \"Resolved\" }")
                .Str("title", "Neuer Titel").Str("state", "Neuer Status").Str("assignedTo", "Neue Zuweisung").Project().Build(),
            Update);

        yield return new("add_work_item_comment", ToolGroup.WorkItemsWrite,
            "Fügt einem Work Item einen Kommentar hinzu.",
            Schema.Create().Int("id", "Work-Item-ID", required: true).Str("text", "Kommentartext (HTML oder Markdown)", required: true).Project().Build(),
            AddComment);

        yield return new("update_work_item_comment", ToolGroup.WorkItemsWrite,
            "Ändert einen bestehenden Kommentar eines Work Items.",
            Schema.Create().Int("id", "Work-Item-ID", required: true).Int("commentId", "Kommentar-ID", required: true)
                .Str("text", "Neuer Kommentartext", required: true).Project().Build(),
            UpdateComment);

        yield return new("add_work_item_relation", ToolGroup.WorkItemsWrite,
            "Verknüpft zwei Work Items (parent, child, related, predecessor, successor, duplicate, …).",
            Schema.Create().Int("id", "Work-Item-ID", required: true).Int("targetId", "ID des Ziel-Work-Items", required: true)
                .Str("relationType", "parent, child, related, predecessor, successor, duplicate, duplicate-of, tests, tested-by oder Referenzname", required: true)
                .Str("comment", "Kommentar zur Verknüpfung").Project().Build(),
            AddRelation);

        yield return new("add_work_item_attachment", ToolGroup.WorkItemsWrite,
            "Hängt eine Datei an ein Work Item an (lokaler Pfad oder Base64-Inhalt, höchstens 60 MB).",
            Schema.Create().Int("id", "Work-Item-ID", required: true).Str("filePath", "Lokaler Dateipfad")
                .Str("fileName", "Dateiname (bei contentBase64 Pflicht)").Str("contentBase64", "Dateiinhalt als Base64")
                .Str("comment", "Kommentar zum Anhang").Project().Build(),
            AddAttachment);
    }

    private static async Task<string> Create(ToolInvocation t)
    {
        var type = t.Args.RequiredString("type");
        var ops = new JsonArray { Op("/fields/System.Title", t.Args.RequiredString("title")) };
        AddIf(ops, "System.Description", t.Args.String("description"));
        AddIf(ops, "System.AssignedTo", t.Args.String("assignedTo"));
        AddIf(ops, "System.AreaPath", t.Args.String("areaPath"));
        AddIf(ops, "System.IterationPath", t.Args.String("iterationPath"));
        AddIf(ops, "System.Tags", t.Args.String("tags"));
        AddFields(ops, t.Args.Object("fields"));
        if (t.Args.Int("parentId") is { } parent)
        {
            ops.Add(RelationOp(t.Azdo, Relations.Resolve("parent"), parent, null));
        }

        var created = await t.Azdo.JsonPatchAsync(HttpMethod.Post,
            $"{t.Azdo.ProjectUrl}/_apis/wit/workitems/${Uri.EscapeDataString(type)}?api-version={AzdoClient.ApiVersion}", ops, t.CancellationToken).ConfigureAwait(false);
        var id = created?["id"]?.GetValue<int>() ?? 0;
        t.WorkItemId = id;
        return Output(new JsonObject { ["id"] = id, ["type"] = type, ["url"] = WebUrl(t.Azdo, id), ["message"] = $"{type} #{id} angelegt." });
    }

    private static async Task<string> Update(ToolInvocation t)
    {
        var id = t.Args.RequiredInt("id");
        t.WorkItemId = id;
        var ops = new JsonArray();
        AddIf(ops, "System.Title", t.Args.String("title"));
        AddIf(ops, "System.State", t.Args.String("state"));
        AddIf(ops, "System.AssignedTo", t.Args.String("assignedTo"));
        AddFields(ops, t.Args.Object("fields"));
        if (ops.Count == 0)
        {
            throw new ToolArgumentException("Nichts zu ändern: „fields“, „title“, „state“ oder „assignedTo“ angeben.");
        }

        var updated = await t.Azdo.JsonPatchAsync(HttpMethod.Patch,
            $"{t.Azdo.ProjectUrl}/_apis/wit/workitems/{id}?api-version={AzdoClient.ApiVersion}", ops, t.CancellationToken).ConfigureAwait(false);
        return Output(new JsonObject { ["id"] = id, ["rev"] = updated?["rev"]?.DeepClone(), ["url"] = WebUrl(t.Azdo, id), ["message"] = $"#{id} geändert." });
    }

    private static async Task<string> AddComment(ToolInvocation t)
    {
        var id = t.Args.RequiredInt("id");
        t.WorkItemId = id;
        var res = await t.Azdo.PostJsonAsync($"{t.Azdo.ProjectUrl}/_apis/wit/workItems/{id}/comments?api-version={AzdoClient.CommentsApiVersion}",
            new { text = t.Args.RequiredString("text") }, t.CancellationToken).ConfigureAwait(false);
        return Output(new JsonObject { ["workItemId"] = id, ["commentId"] = res?["id"]?.DeepClone(), ["url"] = WebUrl(t.Azdo, id) });
    }

    private static async Task<string> UpdateComment(ToolInvocation t)
    {
        var id = t.Args.RequiredInt("id");
        t.WorkItemId = id;
        var commentId = t.Args.RequiredInt("commentId");
        var res = await t.Azdo.PatchJsonAsync($"{t.Azdo.ProjectUrl}/_apis/wit/workItems/{id}/comments/{commentId}?api-version={AzdoClient.CommentsApiVersion}",
            new { text = t.Args.RequiredString("text") }, t.CancellationToken).ConfigureAwait(false);
        return Output(new JsonObject { ["workItemId"] = id, ["commentId"] = res?["id"]?.DeepClone() ?? commentId, ["version"] = res?["version"]?.DeepClone(), ["url"] = WebUrl(t.Azdo, id) });
    }

    private static async Task<string> AddRelation(ToolInvocation t)
    {
        var id = t.Args.RequiredInt("id");
        t.WorkItemId = id;
        var target = t.Args.RequiredInt("targetId");
        var rel = Relations.Resolve(t.Args.RequiredString("relationType"));
        var ops = new JsonArray { RelationOp(t.Azdo, rel, target, t.Args.String("comment")) };
        await t.Azdo.JsonPatchAsync(HttpMethod.Patch, $"{t.Azdo.ProjectUrl}/_apis/wit/workitems/{id}?api-version={AzdoClient.ApiVersion}", ops, t.CancellationToken).ConfigureAwait(false);
        return Output(new JsonObject { ["id"] = id, ["targetId"] = target, ["relation"] = Relations.Friendly(rel), ["url"] = WebUrl(t.Azdo, id) });
    }

    private static async Task<string> AddAttachment(ToolInvocation t)
    {
        var id = t.Args.RequiredInt("id");
        t.WorkItemId = id;
        byte[] content;
        string fileName;
        if (t.Args.String("filePath") is { Length: > 0 } path)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new ToolArgumentException($"Datei nicht gefunden: {path}");
            }

            if (info.Length > MaxAttachmentBytes)
            {
                throw new ToolArgumentException($"Datei ist {info.Length / 1024 / 1024} MB groß – höchstens 60 MB erlaubt.");
            }

            content = await File.ReadAllBytesAsync(path, t.CancellationToken).ConfigureAwait(false);
            fileName = t.Args.String("fileName") ?? info.Name;
        }
        else if (t.Args.String("contentBase64") is { Length: > 0 } b64)
        {
            if ((long)b64.Length * 3 / 4 > MaxAttachmentBytes + 3)
            {
                throw new ToolArgumentException("Inhalt ist größer als 60 MB – höchstens 60 MB erlaubt.");
            }

            try
            {
                content = Convert.FromBase64String(b64);
            }
            catch (FormatException)
            {
                throw new ToolArgumentException("„contentBase64“ ist kein gültiges Base64.");
            }

            fileName = t.Args.RequiredString("fileName");
        }
        else
        {
            throw new ToolArgumentException("„filePath“ oder „contentBase64“ angeben.");
        }

        var upload = await t.Azdo.PostBytesAsync(
            $"{t.Azdo.ProjectUrl}/_apis/wit/attachments?fileName={Uri.EscapeDataString(fileName)}&api-version={AzdoClient.ApiVersion}", content, t.CancellationToken).ConfigureAwait(false);
        var url = upload?["url"]?.GetValue<string>() ?? throw new AzdoApiException(AzdoErrorKind.Other, null, "Upload lieferte keine URL.");

        var attributes = new JsonObject();
        if (t.Args.String("comment") is { Length: > 0 } c)
        {
            attributes["comment"] = c;
        }

        var ops = new JsonArray
        {
            new JsonObject
            {
                ["op"] = "add",
                ["path"] = "/relations/-",
                ["value"] = new JsonObject { ["rel"] = "AttachedFile", ["url"] = url, ["attributes"] = attributes },
            },
        };
        await t.Azdo.JsonPatchAsync(HttpMethod.Patch, $"{t.Azdo.ProjectUrl}/_apis/wit/workitems/{id}?api-version={AzdoClient.ApiVersion}", ops, t.CancellationToken).ConfigureAwait(false);
        return Output(new JsonObject { ["id"] = id, ["fileName"] = fileName, ["size"] = content.Length, ["url"] = WebUrl(t.Azdo, id) });
    }

    private static JsonObject Op(string path, JsonNode? value) => new() { ["op"] = "add", ["path"] = path, ["value"] = value };

    private static void AddIf(JsonArray ops, string field, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            ops.Add(Op("/fields/" + field, value));
        }
    }

    private static void AddFields(JsonArray ops, JsonObject? fields)
    {
        foreach (var (name, value) in fields ?? [])
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('/'))
            {
                throw new ToolArgumentException($"Ungültiger Feldname „{name}“.");
            }

            ops.Add(Op("/fields/" + name, value?.DeepClone()));
        }
    }

    private static JsonObject RelationOp(AzdoClient client, string rel, int target, string? comment)
    {
        var value = new JsonObject { ["rel"] = rel, ["url"] = $"{client.CollectionUrl}/_apis/wit/workItems/{target}" };
        if (!string.IsNullOrEmpty(comment))
        {
            value["attributes"] = new JsonObject { ["comment"] = comment };
        }

        return new JsonObject { ["op"] = "add", ["path"] = "/relations/-", ["value"] = value };
    }
}
