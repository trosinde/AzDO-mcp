using System.Windows;
using System.Windows.Input;

namespace AzdoMcp.Manager.Tray;

public enum TrayCommand
{
    Open,
    Approvals,
    Restart,
    ToggleAutostart,
    LogFolder,
    Exit,
}

public partial class TrayMenuWindow : Window
{
    public TrayMenuWindow()
    {
        InitializeComponent();
    }

    public event EventHandler<TrayCommand>? CommandChosen;

    public event EventHandler<string>? ConnectionChosen;

    /// <summary>Shows the menu with its bottom-right corner near the cursor, inside the work area.</summary>
    public void ShowAtCursor()
    {
        var pos = System.Windows.Forms.Cursor.Position;
        var source = PresentationSource.FromVisual(Application.Current.MainWindow);
        var scale = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        var x = pos.X * scale;
        var y = pos.Y * scale;
        var area = SystemParameters.WorkArea;

        Show();
        UpdateLayout();
        Left = Math.Max(area.Left, Math.Min(x - ActualWidth + 10, area.Right - ActualWidth));
        Top = Math.Max(area.Top, Math.Min(y - ActualHeight + 10, area.Bottom - ActualHeight));
        Activate();
    }

    private void Choose(TrayCommand command)
    {
        Hide();
        CommandChosen?.Invoke(this, command);
    }

    private void OnDeactivated(object? sender, EventArgs e) => Hide();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    private void OnConnection(object sender, RoutedEventArgs e)
    {
        Hide();
        if (sender is FrameworkElement { Tag: string id })
        {
            ConnectionChosen?.Invoke(this, id);
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e) => Choose(TrayCommand.Open);

    private void OnApprovals(object sender, RoutedEventArgs e) => Choose(TrayCommand.Approvals);

    private void OnRestart(object sender, RoutedEventArgs e) => Choose(TrayCommand.Restart);

    private void OnAutostart(object sender, RoutedEventArgs e) => Choose(TrayCommand.ToggleAutostart);

    private void OnLogFolder(object sender, RoutedEventArgs e) => Choose(TrayCommand.LogFolder);

    private void OnExit(object sender, RoutedEventArgs e) => Choose(TrayCommand.Exit);
}
