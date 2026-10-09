using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EdtimeWidget.Ui;

/// <summary>Notification-area icon: colored state dot, tooltip with the timer, and a context menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private System.Windows.Media.Color? _color;
    private Icon? _current;

    public TrayIcon(Action open, Action refresh, Action settings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => open());
        menu.Items.Add("Aktualisieren", null, (_, _) => refresh());
        menu.Items.Add("Einstellungen…", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => exit());

        _icon = new NotifyIcon { ContextMenuStrip = menu, Text = "edtime", Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) open();
        };
        Update("edtime", Theme.Off);
    }

    public void Update(string tooltip, System.Windows.Media.Color color)
    {
        // NotifyIcon.Text is limited to 127 characters.
        _icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (_color == color) return;
        _color = color;

        var previous = _current;
        _current = CreateDotIcon(Color.FromArgb(color.R, color.G, color.B));
        _icon.Icon = _current;
        if (previous is not null) DestroyIcon(previous);
    }

    private static Icon CreateDotIcon(Color color)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var inset = size / 8f;
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, inset, inset, size - 2 * inset, size - 2 * inset);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static void DestroyIcon(Icon icon)
    {
        DestroyIcon(icon.Handle);
        icon.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_current is not null) DestroyIcon(_current);
    }
}
