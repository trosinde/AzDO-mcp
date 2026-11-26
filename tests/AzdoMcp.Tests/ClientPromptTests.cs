using AzdoMcp.Core.Connections;
using AzdoMcp.Manager.Core.Services;
using AzdoMcp.Manager.Core.Clients;
using AzdoMcp.Manager.Core.ViewModels;

namespace AzdoMcp.Tests;

public class ClientPromptTests
{
    private static ClientSetupService Setup(TempDir dir) => new(new ManagerEnvironment
    {
        LocalAppData = Path.Combine(dir.Path, "Local"),
        AppData = Path.Combine(dir.Path, "Roaming"),
        UserProfile = dir.Path,
        AppDirectory = Path.Combine(dir.Path, "Local", "AzdoMcp", "current"),
    });

    private static readonly ClientKind[] All = [ClientKind.ClaudeCode, ClientKind.ClaudeDesktop, ClientKind.OpenCode, ClientKind.VsCode];

    [Fact]
    public void Claude_code_prompt_runs_add_and_checks_list()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        var p = s.Prompt(ClientKind.ClaudeCode);
        Assert.Contains(s.ClaudeCodeCommand, p);
        Assert.Contains("claude mcp list", p);
        Assert.Equal("In Claude Code einfügen", ClientSetupService.PromptTarget(ClientKind.ClaudeCode));
    }

    [Fact]
    public void Opencode_prompt_names_entry_and_fixes_wrapper()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        var p = s.Prompt(ClientKind.OpenCode);
        Assert.Contains(s.OpenCodeConfig, p);
        Assert.Contains("\"azure-devops\": " + s.OpenCodeEntry().ToJsonString(), p);
        Assert.Contains("\"type\":\"local\"", p);
        Assert.Contains("Zwischenschlüssel „servers“", p);
        Assert.Contains("Sicherungskopie", p);
    }

    [Fact]
    public void Desktop_prompt_is_for_an_assistant_with_file_access()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        Assert.Equal("für einen Assistenten mit Dateizugriff (z. B. Claude Code)", ClientSetupService.PromptTarget(ClientKind.ClaudeDesktop));
        var p = s.Prompt(ClientKind.ClaudeDesktop);
        Assert.Contains("claude_desktop_config.json", p);
        Assert.Contains("„mcpServers“", p);
    }

    [Fact]
    public void Vscode_prompt_uses_user_mcp_json()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        var p = s.Prompt(ClientKind.VsCode);
        Assert.Contains(s.VsCodeUserConfig, p);
        Assert.Contains("\"type\":\"stdio\"", p);
    }

    [Fact]
    public async Task Prompts_contain_no_secrets_urls_or_project_names()
    {
        await using var m = new ManagerHarness();
        m.H.Store.Save(new Connection
        {
            Name = "tfs",
            CollectionUrl = Fake.Collection,
            Projects = [new ProjectEntry { Name = "Demo Project One", Alias = "SHOP", IsDefault = true }],
        }, Fake.Pat);
        var vm = new ClientsViewModel(m.Ctx);
        foreach (var card in vm.Cards)
        {
            Assert.DoesNotContain(Fake.Pat, card.Prompt);
            Assert.DoesNotContain("azdo.example.local", card.Prompt);
            Assert.DoesNotContain("Demo Project One", card.Prompt);
            Assert.Contains("Tokens", card.Prompt);
        }
    }

    [Fact]
    public async Task Test_question_with_and_without_projects()
    {
        await using (var empty = new ManagerHarness())
        {
            Assert.Equal("Testfrage nach dem Neustart: „Welche Azure-DevOps-Projekte kennst du?“", new ClientsViewModel(empty.Ctx).Cards[0].TestQuestion);
        }

        await using var m = new ManagerHarness("SHOP");
        Assert.Contains("„Zeig die offenen Bugs in SHOP“", new ClientsViewModel(m.Ctx).Cards[0].TestQuestion);
    }

    [Fact]
    public async Task Copy_prompt_puts_it_on_the_clipboard()
    {
        await using var m = new ManagerHarness();
        var vm = new ClientsViewModel(m.Ctx);
        var card = vm.Cards.Single(c => c.Kind == ClientKind.OpenCode);
        card.CopyPromptCommand!.Execute(null);
        Assert.Equal(card.Prompt, m.Dialogs.Clipboard);
    }

    [Fact]
    public async Task Opencode_wrapper_is_flagged()
    {
        await using var m = new ManagerHarness();
        var path = m.Ctx.Clients.OpenCodeConfig;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            { "mcp": { "azure-devops": { "type": "local", "command": ["azdo-mcp.exe", "--stdio"] }, "servers": { "other": { "type": "local" } } } }
            """);
        var state = m.Ctx.Clients.Detect(ClientKind.OpenCode);
        Assert.Equal(ClientSetupService.OpenCodeWrapperWarning, state.Warning);
        var card = new ClientsViewModel(m.Ctx).Cards.Single(c => c.Kind == ClientKind.OpenCode);
        Assert.Equal(("prüfen", "y"), (card.Status.Text, card.Status.Tone));
        Assert.Equal(ClientSetupService.OpenCodeWrapperWarning, card.Warning);

        File.WriteAllText(path, """{ "mcp": { "azure-devops": { "type": "local", "command": ["azdo-mcp.exe", "--stdio"] } } }""");
        Assert.Null(m.Ctx.Clients.Detect(ClientKind.OpenCode).Warning);
    }

    [Fact]
    public void Every_client_has_a_prompt()
    {
        using var dir = new TempDir();
        var s = Setup(dir);
        Assert.All(All, k => Assert.Contains("azure-devops", s.Prompt(k)));
    }
}
