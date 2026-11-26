using AzdoMcp.Manager.Core.Services;
using AzdoMcp.Manager.Platform;
using Velopack;

namespace AzdoMcp.Manager;

public static class Program
{
    /// <summary>
    /// Velopack must run first: during install/update/uninstall it calls the exe with special
    /// arguments, runs the hook and exits before any window appears.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => Autostart().Enable())
            .OnBeforeUninstallFastCallback(_ => Autostart().Disable())
            .SetArgs(args)
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static AutostartService Autostart() =>
        new(new WindowsRunKey(), Path.Combine(AppContext.BaseDirectory, "AzdoMcp.Manager.exe"));
}
