using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using AzdoMcp.Core;
using AzdoMcp.Core.Audit;
using AzdoMcp.Core.Azdo;
using AzdoMcp.Core.Connections;
using AzdoMcp.Core.Ipc;
using AzdoMcp.Core.Permissions;
using AzdoMcp.Manager.Core.Clients;
using AzdoMcp.Manager.Core.Ipc;
using AzdoMcp.Manager.Core.Services;
using AzdoMcp.Manager.Core.ViewModels;
using AzdoMcp.Manager.Platform;
using AzdoMcp.Manager.Tray;

namespace AzdoMcp.Manager;

public partial class App : Application
{
    private const string MutexName = @"Local\AzdoMcp-Manager";

    private Mutex? _single;
    private ManagerHost? _host;
    private TrayIcon? _tray;
    private TrayMenuWindow? _menu;
    private MainWindow? _window;
    private MainViewModel? _vm;
    private ManagerContext? _ctx;
    private DispatcherTimer? _timer;
    private DateTime _lastReminderCheck = DateTime.MinValue;

    public static string Version =>
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "2.0.0";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: a second start asks the running one to show its window.
        // Scoped to the pipe so a test/demo instance with its own AZDOMCP_PIPE can run beside the real one.
        _single = new Mutex(true, MutexName + "-" + PipeNames.ForCurrentUser(), out var first);
        if (!first)
        {
            SignalRunningInstance();
            Shutdown();
            return;
        }

        var de = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.DefaultThreadCurrentCulture = de;
        CultureInfo.DefaultThreadCurrentUICulture = de;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(de.IetfLanguageTag)));
        DispatcherUnhandledException += OnUnhandled;

        var paths = AppPaths.Default();
        paths.EnsureCreated();
        var audit = new AuditLog(paths);
        audit.Cleanup();

        _host = new ManagerHost(new NamedPipeServerTransport(PipeNames.ForCurrentUser()));
        var env = ManagerEnvironment.FromSystem(Version);
        var host = _host;
        var updates = Updater.Create(paths, async () =>
        {
            var left = await host.RequestServersExitAsync(TimeSpan.FromSeconds(5));
            if (left > 0)
            {
                throw new InvalidOperationException($"{left} MCP-Server laufen noch – bitte die Clients schliessen und erneut versuchen.");
            }
        });
        _ctx = new ManagerContext
        {
            Paths = paths,
            Connections = new ConnectionStore(paths, SecretProtectors.CreateDefault()),
            Permissions = new PermissionStore(paths),
            Audit = audit,
            Host = _host,
            Tester = new ConnectionTester(new AzdoClientFactory()),
            Discovery = new AzdoMcp.Core.Discovery.ProjectDiscovery(new AzdoClientFactory()),
            Clients = new ClientSetupService(env),
            Autostart = new AutostartService(new WindowsRunKey(), env.ManagerExePath),
            Reminders = new ReminderScheduler(paths),
            Environment = env,
            Dialogs = new WpfDialogs(),
            Dispatcher = new WpfDispatcher(Dispatcher),
            Updates = updates,
        };

        _vm = new MainViewModel(_ctx);
        _window = new MainWindow { DataContext = _vm, Icon = TrayIcon.WindowIcon() };
        MainWindow = _window;

        _tray = new TrayIcon();
        _tray.OpenRequested += (_, _) => ShowMain();
        _tray.MenuRequested += (_, _) => ShowMenu();
        _tray.BalloonClicked += (_, _) => ShowMain("connections");
        _menu = new TrayMenuWindow { DataContext = _vm };
        _menu.CommandChosen += (_, c) => OnTrayCommand(c);
        _menu.ConnectionChosen += (_, id) => ShowMain("connections", id);

        _host.ApprovalRequested += (_, r) => Dispatcher.BeginInvoke(() => ShowApproval(r));
        _host.ApprovalClosed += (_, id) => Dispatcher.BeginInvoke(() => ApprovalWindow.CloseFor(id));
        _host.ShowRequested += (_, _) => Dispatcher.BeginInvoke(() => ShowMain());
        _host.Start();

        _vm.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName is nameof(MainViewModel.OverallLevel) or nameof(MainViewModel.TrayStatus) or nameof(MainViewModel.PendingCount))
            {
                UpdateTrayIcon();
            }
        };
        UpdateTrayIcon();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var ticks = 0;
        _timer.Tick += (_, _) =>
        {
            _vm.Log.Tick();
            if (++ticks % 2 == 0)
            {
                _vm.RefreshLive();
            }

            if (DateTime.Now - _lastReminderCheck > TimeSpan.FromHours(1))
            {
                CheckReminders();
            }
        };
        _timer.Start();

        if (UpdateSettings.Load(paths).CheckOnStart)
        {
            _ = updates.CheckAsync();
        }

        if (!e.Args.Contains("--minimized"))
        {
            ShowMain();
        }
    }

    private void ShowMain(string? page = null, string? connectionId = null)
    {
        if (_window is null || _vm is null)
        {
            return;
        }

        if (page is not null)
        {
            _vm.Navigate(page, connectionId);
        }

        _window.ShowAndActivate();
    }

    private void ShowMenu()
    {
        _vm?.RefreshTray();
        _menu?.ShowAtCursor();
    }

    private void OnTrayCommand(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.Open:
                ShowMain();
                break;
            case TrayCommand.Approvals:
                ShowMain("log");
                break;
            case TrayCommand.Restart:
                _ = _host?.ReloadAsync();
                _tray?.ShowBalloon("AzDO MCP Manager", "Laufende Server lesen Verbindungen und Rechte neu ein.", false);
                break;
            case TrayCommand.ToggleAutostart:
                _vm?.SetAutostart(!_vm.Autostart);
                break;
            case TrayCommand.LogFolder:
                _ctx?.Dialogs.OpenFolder(_ctx.Paths.LogsDirectory);
                break;
            case TrayCommand.Exit:
                ExitApp();
                break;
        }
    }

    private void ShowApproval(ApprovalRequest request)
    {
        _vm?.RefreshTray();
        var w = new ApprovalWindow(request);
        w.Answered += async (_, approved) =>
        {
            if (approved is null)
            {
                _vm?.ShowApproval(request.RequestId);
                _window?.ShowAndActivate();
                return;
            }

            if (_host is not null)
            {
                await _host.RespondAsync(request.RequestId, approved.Value);
            }

            _vm?.RefreshLive();
        };
        w.ShowStacked();
    }

    private void UpdateTrayIcon()
    {
        if (_vm is null || _tray is null)
        {
            return;
        }

        var tip = _vm.PendingCount > 0 ? $"AzDO MCP Manager – {_vm.PendingMenuText}" : $"AzDO MCP Manager – {_vm.TrayStatus}";
        _tray.SetStatus(_vm.OverallLevel, tip);
    }

    private void CheckReminders()
    {
        _lastReminderCheck = DateTime.Now;
        if (_ctx is null || _tray is null)
        {
            return;
        }

        var due = _ctx.Reminders.Due(_ctx.SafeConnections());
        if (due.Count == 0)
        {
            return;
        }

        _tray.ShowBalloon("PAT erneuern", string.Join("\n", due.Select(d => d.Text)), warning: true);
        _ctx.Reminders.MarkShown(due.Select(d => d.Connection));
    }

    private void ExitApp()
    {
        _timer?.Stop();
        ApprovalWindow.CloseAll();
        _menu?.Close();
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }

        _tray?.Dispose();
        _tray = null;
        if (_host is not null)
        {
            // Pending approvals end with "Manager beendet" on the server side.
            _host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }

    private static void SignalRunningInstance()
    {
        try
        {
            var stream = new NamedPipeClientTransport(PipeNames.ForCurrentUser()).ConnectAsync(TimeSpan.FromSeconds(2), CancellationToken.None).GetAwaiter().GetResult();
            if (stream is not null)
            {
                using (stream)
                {
                    new IpcWriter(stream).SendAsync(new IpcMessage { Type = MessageTypes.Show }).GetAwaiter().GetResult();
                }
            }
        }
        catch (System.IO.IOException)
        {
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        _ctx?.Dialogs.Error("Unerwarteter Fehler", e.Exception.Message);
    }
}
