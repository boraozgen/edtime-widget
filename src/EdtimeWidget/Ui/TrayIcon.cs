using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EdtimeWidget.Ui;

/// <summary>Notification-area icon: state glyph in the state color, tooltip with the state, and a context menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private (string Glyph, System.Windows.Media.Color Color)? _shown;
    private Icon? _current;

    /// <summary><paramref name="open"/> receives the cursor position (physical pixels) the icon was clicked at.</summary>
    public TrayIcon(Action<Point> open, Action refresh, Action settings, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => open(Cursor.Position));
        menu.Items.Add("Aktualisieren", null, (_, _) => refresh());
        menu.Items.Add("Einstellungen…", null, (_, _) => settings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => exit());

        _icon = new NotifyIcon { ContextMenuStrip = menu, Text = "edtime", Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) open(Cursor.Position);
        };
        Update(Theme.GlyphLoading, Theme.Off, "edtime");
    }

    public void Update(string glyph, System.Windows.Media.Color color, string tooltip)
    {
        // NotifyIcon.Text is limited to 127 characters.
        _icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (_shown == (glyph, color)) return;
        _shown = (glyph, color);

        var previous = _current;
        _current = CreateGlyphIcon(glyph, Color.FromArgb(color.R, color.G, color.B));
        _icon.Icon = _current;
        if (previous is not null) DestroyIcon(previous);
    }

    private static Icon CreateGlyphIcon(string glyph, Color color)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var family = new FontFamily(IconFont);
            // Laid out at a point, not in a rectangle: a line taller than the rectangle is dropped entirely.
            using var path = new GraphicsPath();
            path.AddString(glyph, family, (int)FontStyle.Regular, size, PointF.Empty, StringFormat.GenericTypographic);

            // The icon fonts' strokes are hairline at tray size; outlining the glyph thickens them.
            var stroke = size / 20f;

            // Glyphs carry padding inside their em box; scale the ink to fill the icon, leaving room for the outline.
            var ink = path.GetBounds();
            if (ink.Width <= 0 || ink.Height <= 0) return Icon.FromHandle(bitmap.GetHicon());
            var room = size - stroke - 1;
            var scale = Math.Min(room / ink.Width, room / ink.Height);
            using var fit = new Matrix();
            fit.Translate(size / 2f, size / 2f);
            fit.Scale(scale, scale);
            fit.Translate(-(ink.Left + ink.Width / 2), -(ink.Top + ink.Height / 2));
            path.Transform(fit);

            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, stroke) { LineJoin = LineJoin.Round };
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static readonly string IconFont = FindIconFont();

    private static string FindIconFont()
    {
        using var fonts = new InstalledFontCollection();
        foreach (var family in fonts.Families)
        {
            if (family.Name == "Segoe Fluent Icons") return family.Name;
        }
        return "Segoe MDL2 Assets";
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
