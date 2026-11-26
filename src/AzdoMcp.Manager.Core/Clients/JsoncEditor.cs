using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AzdoMcp.Manager.Core.Clients;

public sealed class ClientConfigException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Sets one property in a JSON/JSONC document by editing the text, so comments, order and
/// formatting of everything else stay as they are.
/// </summary>
public static class JsoncEditor
{
    public static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Parses for validation and reading; throws <see cref="ClientConfigException"/> on syntax errors.</summary>
    public static JsonNode? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(text, documentOptions: ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new ClientConfigException($"Datei ist kein gültiges JSON (Zeile {ex.LineNumber + 1}): {ex.Message}", ex);
        }
    }

    /// <summary>Sets root.path[0].path[1]... = value, creating objects on the way.</summary>
    public static string SetProperty(string text, IReadOnlyList<string> path, JsonNode value)
    {
        if (Parse(text) is not JsonObject)
        {
            throw new ClientConfigException("Die Datei enthält kein JSON-Objekt.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            text = "{\n}\n";
        }

        var scanner = new Scanner(text);
        var root = scanner.SkipToValue(0);
        return Set(text, root, path, 0, value, indentLevel: 1);
    }

    private static string Set(string text, int objectStart, IReadOnlyList<string> path, int depth, JsonNode value, int indentLevel)
    {
        var scanner = new Scanner(text);
        var members = scanner.ReadObjectMembers(objectStart, out var objectEnd);
        var key = path[depth];
        var member = members.FirstOrDefault(m => m.Name == key);
        var isLast = depth == path.Count - 1;
        var indent = DetectIndent(text, members, indentLevel);

        if (member is not null)
        {
            if (isLast)
            {
                var rendered = Render(value, indent);
                return text[..member.ValueStart] + rendered + text[member.ValueEnd..];
            }

            if (text[member.ValueStart] != '{')
            {
                // Replace a non-object with an object holding the rest of the path.
                return text[..member.ValueStart] + Render(Nest(path, depth + 1, value), indent) + text[member.ValueEnd..];
            }

            return Set(text, member.ValueStart, path, depth + 1, value, indentLevel + 1);
        }

        // Insert as first member right after '{'.
        var newValue = isLast ? value : Nest(path, depth + 1, value);
        var entry = $"{JsonSerializer.Serialize(key)}: {Render(newValue, indent)}";
        var closingIndent = indentLevel > 1 ? indent[..Math.Max(0, indent.Length - IndentUnit(text).Length)] : "";
        var insert = members.Count == 0
            ? $"\n{indent}{entry}\n{closingIndent}"
            : $"\n{indent}{entry},";
        var at = objectStart + 1;
        if (members.Count == 0)
        {
            // Drop whitespace between the braces so the result stays tidy.
            var innerEnd = objectEnd;
            var inner = text[at..innerEnd];
            if (string.IsNullOrWhiteSpace(inner))
            {
                return text[..at] + insert + text[innerEnd..];
            }
        }

        return text[..at] + insert + text[at..];
    }

    private static JsonNode Nest(IReadOnlyList<string> path, int from, JsonNode value)
    {
        var node = value;
        for (var i = path.Count - 1; i >= from; i--)
        {
            node = new JsonObject { [path[i]] = node };
        }

        return node;
    }

    private static string Render(JsonNode value, string indent)
    {
        var json = value.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var lines = json.Split('\n');
        var sb = new StringBuilder(lines[0].TrimEnd('\r'));
        for (var i = 1; i < lines.Length; i++)
        {
            sb.Append('\n').Append(indent).Append(lines[i].TrimEnd('\r'));
        }

        return sb.ToString();
    }

    private static string IndentUnit(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var ws = line.Length - line.TrimStart(' ', '\t').Length;
            if (ws > 0 && line.Trim().Length > 0)
            {
                return line[..ws];
            }
        }

        return "  ";
    }

    private static string DetectIndent(string text, List<Member> members, int level)
    {
        if (members.Count > 0)
        {
            var lineStart = text.LastIndexOf('\n', members[0].NameStart) + 1;
            var prefix = text[lineStart..members[0].NameStart];
            if (prefix.Trim().Length == 0)
            {
                return prefix;
            }
        }

        return string.Concat(Enumerable.Repeat(IndentUnit(text), level));
    }

    private sealed record Member(string Name, int NameStart, int ValueStart, int ValueEnd);

    /// <summary>Minimal JSONC scanner: knows strings, comments and nesting, nothing more.</summary>
    private sealed class Scanner(string text)
    {
        public int SkipToValue(int i)
        {
            i = SkipTrivia(i);
            if (i >= text.Length || text[i] != '{')
            {
                throw new ClientConfigException("Die Datei enthält kein JSON-Objekt.");
            }

            return i;
        }

        public List<Member> ReadObjectMembers(int openBrace, out int closeBrace)
        {
            var members = new List<Member>();
            var i = openBrace + 1;
            while (true)
            {
                i = SkipTrivia(i);
                if (i >= text.Length)
                {
                    throw new ClientConfigException("Unerwartetes Dateiende.");
                }

                if (text[i] == '}')
                {
                    closeBrace = i;
                    return members;
                }

                if (text[i] == ',')
                {
                    i++;
                    continue;
                }

                var nameStart = i;
                var nameEnd = SkipString(i);
                var name = JsonSerializer.Deserialize<string>(text[nameStart..nameEnd])!;
                i = SkipTrivia(nameEnd);
                if (text[i] != ':')
                {
                    throw new ClientConfigException("„:“ erwartet.");
                }

                var valueStart = SkipTrivia(i + 1);
                var valueEnd = SkipValue(valueStart);
                members.Add(new Member(name, nameStart, valueStart, valueEnd));
                i = valueEnd;
            }
        }

        private int SkipValue(int i)
        {
            var c = text[i];
            if (c == '"')
            {
                return SkipString(i);
            }

            if (c is '{' or '[')
            {
                var depth = 0;
                while (i < text.Length)
                {
                    var ch = text[i];
                    if (ch == '"')
                    {
                        i = SkipString(i);
                        continue;
                    }

                    if (ch == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
                    {
                        i = SkipTrivia(i);
                        continue;
                    }

                    if (ch is '{' or '[')
                    {
                        depth++;
                    }
                    else if (ch is '}' or ']')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return i + 1;
                        }
                    }

                    i++;
                }

                throw new ClientConfigException("Klammer nicht geschlossen.");
            }

            while (i < text.Length && text[i] is not (',' or '}' or ']') && !char.IsWhiteSpace(text[i]) && !(text[i] == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*'))
            {
                i++;
            }

            return i;
        }

        private int SkipString(int i)
        {
            if (text[i] != '"')
            {
                throw new ClientConfigException("Name in Anführungszeichen erwartet.");
            }

            i++;
            while (i < text.Length)
            {
                if (text[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (text[i] == '"')
                {
                    return i + 1;
                }

                i++;
            }

            throw new ClientConfigException("Zeichenkette nicht geschlossen.");
        }

        private int SkipTrivia(int i)
        {
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
                else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    var nl = text.IndexOf('\n', i);
                    i = nl < 0 ? text.Length : nl + 1;
                }
                else if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 2;
                }
                else
                {
                    break;
                }
            }

            return i;
        }
    }
}
