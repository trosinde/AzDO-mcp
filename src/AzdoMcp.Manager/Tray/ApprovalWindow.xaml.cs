using System.Windows;
using System.Windows.Threading;
using AzdoMcp.Core.Ipc;

namespace AzdoMcp.Manager.Tray;

/// <summary>
/// Approval notification near the tray (own window instead of a Windows toast: toasts with
/// buttons need a COM activator registration, see design.md). Stacks when several are open.
/// </summary>
public partial class ApprovalWindow : Window
{
    private static readonly List<ApprovalWindow> Open = [];

    private readonly DispatcherTimer _timer;

    public ApprovalWindow(ApprovalRequest request)
    {
        InitializeComponent();
        Request = request;
        TitleText.Text = request.Title;
        SummaryText.Text = request.Summary;
        AppIcon.Source = TrayIcon.WindowIcon();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateRemaining();
        UpdateRemaining();
        Loaded += (_, _) => Place();
        Closed += (_, _) =>
        {
            _timer.Stop();
            Open.Remove(this);
            Restack();
        };
    }

    public ApprovalRequest Request { get; }

    /// <summary>approved: true/false; null = Details.</summary>
    public event EventHandler<bool?>? Answered;

    public void ShowStacked()
    {
        Open.Add(this);
        Show();
        _timer.Start();
    }

    public static void CloseFor(string requestId)
    {
        foreach (var w in Open.Where(w => w.Request.RequestId == requestId).ToList())
        {
            w.Close();
        }
    }

    public static void CloseAll()
    {
        foreach (var w in Open.ToList())
        {
            w.Close();
        }
    }

    private void UpdateRemaining()
    {
        var left = Request.ExpiresAt - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            Close();
            return;
        }

        Remaining.Text = $"noch {(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    private void Place()
    {
        var area = SystemParameters.WorkArea;
        var index = Open.IndexOf(this);
        var offset = Open.Take(Math.Max(0, index)).Sum(w => w.ActualHeight);
        Left = area.Right - ActualWidth - 14;
        Top = area.Bottom - ActualHeight - 14 - offset;
    }

    private static void Restack()
    {
        foreach (var w in Open)
        {
            w.Place();
        }
    }

    private void OnApprove(object sender, RoutedEventArgs e) => Finish(true);

    private void OnDeny(object sender, RoutedEventArgs e) => Finish(false);

    private void OnDetails(object sender, RoutedEventArgs e) => Answered?.Invoke(this, null);

    private void Finish(bool approved)
    {
        Answered?.Invoke(this, approved);
        Close();
    }
}
