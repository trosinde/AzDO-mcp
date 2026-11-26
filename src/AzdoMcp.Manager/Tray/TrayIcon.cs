using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using AzdoMcp.Core.Connections;
using Forms = System.Windows.Forms;

namespace AzdoMcp.Manager.Tray;

/// <summary>Notification-area icon: app glyph plus an Ampel dot; left double-click opens, right-click shows the menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon = new();
    private Icon? _current;

    public TrayIcon()
    {
        _icon.Text = "AzDO MCP Manager";
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right || e.Button == Forms.MouseButtons.Left)
            {
                MenuRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        _icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        _icon.BalloonTipClicked += (_, _) => BalloonClicked?.Invoke(this, EventArgs.Empty);
        SetStatus(StatusLevel.Ok, "AzDO MCP Manager");
        _icon.Visible = true;
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? MenuRequested;

    public event EventHandler? BalloonClicked;

    public void SetStatus(StatusLevel level, string tooltip)
    {
        var old = _current;
        _current = CreateIcon(level, 32);
        _icon.Icon = _current;
        _icon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
        if (old is not null)
        {
            DestroyIcon(old.Handle);
            old.Dispose();
        }
    }

    public void ShowBalloon(string title, string text, bool warning) =>
        _icon.ShowBalloonTip(8000, title, text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

    /// <summary>The app glyph from the UI proposal (three squares and a plus), optionally with a status dot.</summary>
    public static Icon CreateIcon(StatusLevel? level, int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var s = size / 24f;
            using var pen = new Pen(Color.FromArgb(0x00, 0x5F, 0xB8), 2.4f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            void Square(float x, float y) => g.DrawPath(pen, Rounded(x * s, y * s, 7 * s, 7 * s, 1.5f * s));
            Square(3, 3);
            Square(14, 3);
            Square(3, 14);
            g.DrawLine(pen, 14 * s, 17.5f * s, 21 * s, 17.5f * s);
            g.DrawLine(pen, 17.5f * s, 14 * s, 17.5f * s, 21 * s);

            if (level is { } l)
            {
                var color = l switch
                {
                    StatusLevel.Ok => Color.FromArgb(0x10, 0x7C, 0x10),
                    StatusLevel.Warning => Color.FromArgb(0xE3, 0xB1, 0x00),
                    _ => Color.FromArgb(0xC4, 0x2B, 0x1C),
                };
                var d = size * 0.42f;
                using var ring = new SolidBrush(Color.White);
                using var dot = new SolidBrush(color);
                g.FillEllipse(ring, size - d - 0.5f, size - d - 0.5f, d + 0.5f, d + 0.5f);
                g.FillEllipse(dot, size - d + 1.5f, size - d + 1.5f, d - 3f, d - 3f);
            }
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    public static BitmapSource WindowIcon()
    {
        var icon = CreateIcon(null, 32);
        try
        {
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DestroyIcon(icon.Handle);
            icon.Dispose();
        }
    }

    private static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, 2 * r, 2 * r, 180, 90);
        p.AddArc(x + w - 2 * r, y, 2 * r, 2 * r, 270, 90);
        p.AddArc(x + w - 2 * r, y + h - 2 * r, 2 * r, 2 * r, 0, 90);
        p.AddArc(x, y + h - 2 * r, 2 * r, 2 * r, 90, 90);
        p.CloseFigure();
        return p;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_current is not null)
        {
            DestroyIcon(_current.Handle);
            _current.Dispose();
        }
    }
}
