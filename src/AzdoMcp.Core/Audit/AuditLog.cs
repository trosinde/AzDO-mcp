using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AzdoMcp.Core.Infrastructure;

namespace AzdoMcp.Core.Audit;

[JsonConverter(typeof(JsonStringEnumConverter<AuditOutcome>))]
public enum AuditOutcome
{
    Ok,
    Error,
    Denied,
    Pending,
    Blocked,
}

/// <summary>
/// One tool call. Holds metadata only: never PATs, field values, comment texts or file contents.
/// </summary>
public sealed record AuditRecord
{
    public DateTimeOffset Time { get; init; }

    public string Client { get; init; } = "";

    public string Tool { get; init; } = "";

    public string? Connection { get; init; }

    public long DurationMs { get; init; }

    public AuditOutcome Outcome { get; init; }

    /// <summary>Text for the "Ergebnis" column, e.g. "OK", "403 Scope fehlt", "abgelehnt".</summary>
    public string Result { get; init; } = "";

    public bool IsWrite { get; init; }

    public int? WorkItemId { get; init; }

    /// <summary>Links pending / final records of one approval request.</summary>
    public string? RequestId { get; init; }

    public int ProcessId { get; init; }
}

/// <summary>Per-day JSONL files under logs\, safe for concurrent writers.</summary>
public sealed class AuditLog
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private const string Prefix = "audit-";
    private const string Suffix = ".jsonl";

    private readonly AppPaths _paths;
    private readonly TimeProvider _time;

    public AuditLog(AppPaths paths, TimeProvider? time = null)
    {
        _paths = paths;
        _time = time ?? TimeProvider.System;
    }

    private string MutexName => $@"Local\AzdoMcp-audit-{_paths.RootHash}";

    public string FileFor(DateOnly day) =>
        Path.Combine(_paths.LogsDirectory, $"{Prefix}{day:yyyyMMdd}{Suffix}");

    public void Append(AuditRecord record)
    {
        Directory.CreateDirectory(_paths.LogsDirectory);
        var line = JsonSerializer.Serialize(record, Json.Compact) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        var file = FileFor(DateOnly.FromDateTime(record.Time.ToLocalTime().DateTime));

        FileUtil.WithMutex(MutexName, () =>
        {
            using var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            fs.Write(bytes);
        });
    }

    /// <summary>Records with Time in [from, to], oldest first.</summary>
    public IReadOnlyList<AuditRecord> Read(DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<AuditRecord>();
        if (!Directory.Exists(_paths.LogsDirectory))
        {
            return result;
        }

        var firstDay = DateOnly.FromDateTime(from.ToLocalTime().DateTime);
        var lastDay = DateOnly.FromDateTime(to.ToLocalTime().DateTime);
        foreach (var (day, file) in LogFiles().Where(f => f.Day >= firstDay && f.Day <= lastDay).OrderBy(f => f.Day))
        {
            foreach (var line in ReadLinesShared(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                AuditRecord? r;
                try
                {
                    r = JsonSerializer.Deserialize<AuditRecord>(line, Json.Compact);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (r is not null && r.Time >= from && r.Time <= to)
                {
                    result.Add(r);
                }
            }
        }

        return result.OrderBy(r => r.Time).ToList();
    }

    /// <summary>Deletes day files older than the retention period. Returns the number deleted.</summary>
    public int Cleanup()
    {
        if (!Directory.Exists(_paths.LogsDirectory))
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var oldest = today.AddDays(-(int)Retention.TotalDays);
        var deleted = 0;
        foreach (var (day, file) in LogFiles())
        {
            if (day < oldest)
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (IOException)
                {
                }
            }
        }

        return deleted;
    }

    /// <summary>Writes records as CSV (semicolon, UTF-8 with BOM for Excel) with the visible protocol columns.</summary>
    public static void ExportCsv(IEnumerable<AuditRecord> records, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Zeit;Client;Tool;Verbindung;Dauer (s);Ergebnis");
        foreach (var r in records)
        {
            sb.Append(Csv(r.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(';')
              .Append(Csv(r.Client)).Append(';')
              .Append(Csv(r.Tool)).Append(';')
              .Append(Csv(r.Connection ?? "")).Append(';')
              .Append((r.DurationMs / 1000.0).ToString("0.0", CultureInfo.GetCultureInfo("de-DE"))).Append(';')
              .Append(Csv(r.Result)).AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Csv(string value)
    {
        // Neutralise spreadsheet formulas and quote when needed.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private IEnumerable<(DateOnly Day, string File)> LogFiles()
    {
        foreach (var file in Directory.EnumerateFiles(_paths.LogsDirectory, Prefix + "*" + Suffix))
        {
            var name = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
            if (DateOnly.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                yield return (day, file);
            }
        }
    }

    private static IEnumerable<string> ReadLinesShared(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
