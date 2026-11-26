using AzdoMcp.Core.Audit;

namespace AzdoMcp.Tests;

public class AuditLogTests
{
    private static AuditRecord Rec(string tool, string conn = "SHOP", DateTimeOffset? at = null, int pid = 1) => new()
    {
        Time = at ?? DateTimeOffset.UtcNow,
        Client = "Claude Code",
        Tool = tool,
        Connection = conn,
        DurationMs = 200,
        Outcome = AuditOutcome.Ok,
        Result = "OK",
        ProcessId = pid,
    };

    [Fact]
    public void Records_round_trip_with_all_columns()
    {
        using var dir = new TempDir();
        var log = new AuditLog(dir.AppPaths);
        log.Append(Rec("get_work_item"));
        var r = log.Read(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1)).Single();
        Assert.Equal(("Claude Code", "get_work_item", "SHOP", 200L, "OK"), (r.Client, r.Tool, r.Connection, r.DurationMs, r.Result));
        Assert.Single(Directory.EnumerateFiles(dir.AppPaths.LogsDirectory, "audit-*.jsonl"));
    }

    [Fact]
    public async Task Concurrent_writers_do_not_interleave()
    {
        using var dir = new TempDir();
        var a = new AuditLog(dir.AppPaths);
        var b = new AuditLog(dir.AppPaths);
        await Task.WhenAll(
            Task.Run(() => Parallel.For(0, 200, i => a.Append(Rec("tool_a_" + i, pid: 1)))),
            Task.Run(() => Parallel.For(0, 200, i => b.Append(Rec("tool_b_" + i, pid: 2)))));

        var file = Directory.EnumerateFiles(dir.AppPaths.LogsDirectory).Single();
        var lines = File.ReadAllLines(file);
        Assert.Equal(400, lines.Length);
        Assert.Equal(400, a.Read(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1)).Count);
    }

    [Fact]
    public void Files_older_than_30_days_are_deleted()
    {
        using var dir = new TempDir();
        var log = new AuditLog(dir.AppPaths);
        Directory.CreateDirectory(dir.AppPaths.LogsDirectory);
        var today = DateOnly.FromDateTime(DateTime.Now);
        File.WriteAllText(log.FileFor(today.AddDays(-31)), "");
        File.WriteAllText(log.FileFor(today.AddDays(-30)), "");
        File.WriteAllText(log.FileFor(today), "");

        Assert.Equal(1, log.Cleanup());
        Assert.False(File.Exists(log.FileFor(today.AddDays(-31))));
        Assert.True(File.Exists(log.FileFor(today.AddDays(-30))));
    }

    [Fact]
    public void Csv_export_contains_only_given_records()
    {
        using var dir = new TempDir();
        var log = new AuditLog(dir.AppPaths);
        log.Append(Rec("get_work_item", "SHOP"));
        log.Append(Rec("get_commits", "OPS"));
        var shop = log.Read(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1)).Where(r => r.Connection == "SHOP");
        var csv = Path.Combine(dir.Path, "export.csv");
        AuditLog.ExportCsv(shop, csv);

        var lines = File.ReadAllLines(csv);
        Assert.Equal("Zeit;Client;Tool;Verbindung;Dauer (s);Ergebnis", lines[0].TrimStart('﻿'));
        Assert.Equal(2, lines.Length);
        Assert.Contains(";get_work_item;SHOP;0,2;OK", lines[1]);
    }

    [Fact]
    public void Csv_neutralises_formulas()
    {
        using var dir = new TempDir();
        var csv = Path.Combine(dir.Path, "x.csv");
        AuditLog.ExportCsv([Rec("=cmd()")], csv);
        Assert.Contains(";'=cmd();", File.ReadAllText(csv));
    }
}
