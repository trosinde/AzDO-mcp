using System.Collections.ObjectModel;
using System.Windows.Input;
using AzdoMcp.Core.Connections;
using AzdoMcp.Manager.Core.Clients;
using AzdoMcp.Manager.Core.Mvvm;

namespace AzdoMcp.Manager.Core.ViewModels;

public sealed record ClientAction(string Text, ICommand Command, bool IsPrimary = false);

public sealed class ClientCard : ObservableObject
{
    private Pill _status = new("offen", null);

    public ClientCard(ClientKind kind, string name, string snippet)
    {
        Kind = kind;
        Name = name;
        Snippet = snippet;
    }

    public ClientKind Kind { get; }

    public string Name { get; }

    public string Snippet { get; }

    public Pill Status { get => _status; set => Set(ref _status, value); }

    private string? _warning;

    /// <summary>Hint when the config is set up but not usable (e.g. OpenCode "servers" wrapper).</summary>
    public string? Warning { get => _warning; set => Set(ref _warning, value); }

    public string Prompt { get; init; } = "";

    public string PromptTarget { get; init; } = "";

    public string TestQuestion { get; init; } = "";

    public RelayCommand? CopyPromptCommand { get; init; }

    public ObservableCollection<ClientAction> Actions { get; } = [];
}

/// <summary>Page "MCP-Clients einbinden".</summary>
public sealed class ClientsViewModel : ObservableObject
{
    public const string Intro = "PATs bleiben im Benutzerprofil, in keiner Client-Konfiguration.";
    public const string PathNote = "Pfad bleibt nach Updates gleich. Beim Eintragen wird eine Sicherung der Client-Konfiguration angelegt.";

    private readonly ManagerContext _ctx;

    public ClientsViewModel(ManagerContext ctx)
    {
        _ctx = ctx;
        var setup = ctx.Clients;
        var exe = setup.ExePath.Replace("\\", "\\\\");

        var claudeCode = new ClientCard(ClientKind.ClaudeCode, "Claude Code", setup.ClaudeCodeCommand);
        claudeCode.Actions.Add(new("Befehl kopieren", new RelayCommand(() => _ctx.Dialogs.CopyText(setup.ClaudeCodeCommand))));
        claudeCode.Actions.Add(new("Ausführen", new AsyncCommand(RunClaudeCodeAsync, onError: Fail)));

        var desktop = new ClientCard(ClientKind.ClaudeDesktop, "Claude Desktop",
            $"\"azure-devops\": {{ \"command\": \"{exe}\", \"args\": [\"--stdio\"] }}");
        desktop.Actions.Add(new("In claude_desktop_config.json eintragen", new RelayCommand(() => Write(() => setup.WriteClaudeDesktop())), true));

        var openCode = new ClientCard(ClientKind.OpenCode, "OpenCode",
            $"\"azure-devops\": {{ \"type\": \"local\", \"command\": [\"{exe}\", \"--stdio\"] }}");
        openCode.Actions.Add(new($"In {Path.GetFileName(setup.OpenCodeConfig)} eintragen", new RelayCommand(() => Write(() => setup.WriteOpenCode()))));

        var vscode = new ClientCard(ClientKind.VsCode, "VS Code / GitHub Copilot",
            $"\"servers\": {{ \"azure-devops\": {{ \"type\": \"stdio\", \"command\": \"{exe}\", \"args\": [\"--stdio\"] }} }}");
        vscode.Actions.Add(new("Benutzer-mcp.json", new RelayCommand(() => Write(() => setup.WriteVsCode()))));
        vscode.Actions.Add(new("Workspace .vscode/mcp.json …", new RelayCommand(WriteWorkspace)));

        string? kurzname = null;
        try
        {
            kurzname = ctx.Connections.FindProject(null)?.Alias;
        }
        catch (Exception ex) when (ex is ConfigurationException or IOException)
        {
        }

        ClientCard WithPrompt(ClientCard card) => new(card.Kind, card.Name, card.Snippet)
        {
            Prompt = setup.Prompt(card.Kind),
            PromptTarget = ClientSetupService.PromptTarget(card.Kind),
            TestQuestion = ClientSetupService.TestQuestion(kurzname),
            CopyPromptCommand = new RelayCommand(() => _ctx.Dialogs.CopyText(setup.Prompt(card.Kind))),
        };

        Cards = [.. new[] { claudeCode, desktop, openCode, vscode }.Select(c =>
        {
            var copy = WithPrompt(c);
            foreach (var a in c.Actions)
            {
                copy.Actions.Add(a);
            }

            return copy;
        })];
        CopyPathCommand = new RelayCommand(() => _ctx.Dialogs.CopyText(setup.ExePath));
        Refresh();
    }

    public IReadOnlyList<ClientCard> Cards { get; }

    public string IntroText => Intro;

    public const string PortablePathNote = "Portable: Pfad ändert sich, wenn der Ordner verschoben wird – dann neu eintragen. Beim Eintragen wird eine Sicherung der Client-Konfiguration angelegt.";

    public string PathNoteText => _ctx.Environment.IsInstalled ? PathNote : PortablePathNote;

    public string ExeDisplayPath => _ctx.Environment.ServerExeDisplayPath;

    public string ServerCommand => "azdo-mcp.exe --stdio";

    public RelayCommand CopyPathCommand { get; }

    public void Refresh()
    {
        foreach (var state in _ctx.Clients.DetectAll())
        {
            var card = Cards.First(c => c.Kind == state.Kind);
            card.Status = state.Problem is not null
                ? new Pill("Datei fehlerhaft", StatusLevel.Error)
                : state.Warning is not null ? new Pill("prüfen", StatusLevel.Warning)
                : state.IsConfigured ? new Pill("eingerichtet", StatusLevel.Ok) : new Pill("offen", null);
            card.Warning = state.Problem ?? state.Warning;
        }
    }

    private void Write(Func<WriteResult> write)
    {
        try
        {
            var result = write();
            _ctx.Dialogs.Info("Eingetragen", result.BackupPath is null
                ? $"Eingetragen in\n{result.Path}\n\nClient neu starten, damit er den Server lädt."
                : $"Eingetragen in\n{result.Path}\n\nSicherung: {Path.GetFileName(result.BackupPath)}\nClient neu starten, damit er den Server lädt.");
        }
        catch (ClientConfigException ex)
        {
            _ctx.Dialogs.Error("Nicht eingetragen", $"Die Datei wurde nicht verändert.\n{ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ctx.Dialogs.Error("Nicht eingetragen", ex.Message);
        }

        Refresh();
    }

    private void WriteWorkspace()
    {
        var folder = _ctx.Dialogs.PickFolder("Workspace-Ordner wählen");
        if (folder is not null)
        {
            Write(() => _ctx.Clients.WriteVsCode(folder));
        }
    }

    private async Task RunClaudeCodeAsync()
    {
        var (ok, output) = await _ctx.Clients.RunClaudeCodeAsync();
        if (ok)
        {
            _ctx.Dialogs.Info("Claude Code", string.IsNullOrWhiteSpace(output) ? "Eingetragen." : output);
        }
        else
        {
            _ctx.Dialogs.Error("Claude Code", output);
        }

        Refresh();
    }

    private void Fail(Exception ex) => _ctx.Dialogs.Error("Fehler", ex.Message);
}
