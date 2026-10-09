using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace EdtimeWidget.Ui;

/// <summary>
/// Small always-on-top window drawn over the taskbar, left of the notification area.
/// Windows 11 has no taskbar toolbar API, so this keeps itself placed and above the taskbar.
/// </summary>
public partial class TaskbarPill : Window
{
    private IntPtr _hwnd;

    public event Action? Clicked;

    /// <summary>Extra distance from the tray in physical pixels.</summary>
    public int OffsetX { get; set; }

    public TaskbarPill()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.AddExStyle(_hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            HookTaskbarActivation();
        };
        Closed += (_, _) => UnhookTaskbarActivation();
        SizeChanged += (_, _) => Reposition();
    }

    public void Update(string glyph, string time, Color color, string tooltip, bool dim)
    {
        Icon.Text = glyph;
        Icon.Foreground = new SolidColorBrush(color);
        Time.Text = time;
        Time.Visibility = time.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Icon.Margin = new Thickness(0, 0, time.Length == 0 ? 0 : 8, 0);
        Pill.ToolTip = tooltip;
        Pill.Opacity = dim ? 0.6 : 1.0;
        CenterInk(Icon, glyph);
        CenterInk(Time, "0:00");
    }

    /// <summary>
    /// Fonts place glyphs unevenly inside their line box (digits sit low, icons vary), so VerticalAlignment
    /// alone doesn't center what you see. Shift each TextBlock so its visible ink is centered on its box.
    /// </summary>
    private static void CenterInk(System.Windows.Controls.TextBlock block, string sample)
    {
        if (sample.Length == 0) return;
        var text = new FormattedText(sample, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize,
            Brushes.Black, 1.0);
        var ink = text.BuildGeometry(new Point()).Bounds;
        if (ink.IsEmpty) return;
        var offset = text.Height / 2 - (ink.Top + ink.Bottom) / 2;
        block.RenderTransform = new TranslateTransform(0, Math.Round(offset * 2) / 2);
    }

    /// <summary>Renders sample pills to a PNG (for checking layout without a taskbar): EdtimeWidget.exe --snapshot out.png</summary>
    internal static void Snapshot(string path)
    {
        const double scale = 4;
        var samples = new[]
        {
            (Theme.GlyphWork, "7:55", Theme.Working),
            (Theme.GlyphBreak, "0:12", Theme.Break),
            (Theme.GlyphOff, "8:02", Theme.Off),
            (Theme.GlyphLogin, "", Theme.Off),
        };
        var stack = new System.Windows.Controls.StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)) };
        foreach (var (glyph, time, color) in samples)
        {
            var pill = new TaskbarPill();
            pill.Update(glyph, time, color, string.Empty, dim: false);
            var border = pill.Pill;
            pill.Content = null;
            pill.Close();
            border.Margin = new Thickness(8);
            border.HorizontalAlignment = HorizontalAlignment.Left;
            var grid = new System.Windows.Controls.Grid();
            grid.Children.Add(border);
            // Red guide through the pill's vertical center.
            grid.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Height = 0.25, Fill = Brushes.Red, VerticalAlignment = VerticalAlignment.Center,
            });
            stack.Children.Add(grid);
        }
        stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        stack.Arrange(new Rect(stack.DesiredSize));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(stack.ActualWidth * scale), (int)(stack.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(stack);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Screen rectangle of the pill in physical pixels.</summary>
    internal Native.RECT? ScreenRect => _hwnd != IntPtr.Zero && Native.GetWindowRect(_hwnd, out var r) ? r : null;

    /// <summary>Places the pill on the taskbar and puts it back on top (the taskbar takes z-order when clicked).</summary>
    public void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;
        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !Native.GetWindowRect(taskbar, out var tb)) return;

        // Hide when the taskbar is auto-hidden (moved off screen) or a fullscreen app/presentation runs.
        var screen = System.Windows.Forms.Screen.FromHandle(taskbar).Bounds;
        var visibleHeight = Math.Min(tb.Bottom, screen.Bottom) - Math.Max(tb.Top, screen.Top);
        if (visibleHeight < tb.Height - 2 || Native.IsFullscreenAppActive())
        {
            if (IsVisible) Hide();
            return;
        }
        if (!IsVisible) Show();

        var notify = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var anchor = notify != IntPtr.Zero && Native.GetWindowRect(notify, out var tray) && tray.Width > 0
            ? tray.Left
            : tb.Right - tb.Width / 5;

        var scale = Native.GetDpiForWindow(_hwnd) / 96.0;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var x = anchor - width - (int)(8 * scale) - OffsetX;
        var y = tb.Top + (tb.Height - height) / 2;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Puts the pill back above the taskbar without moving it.</summary>
    public void BringToTop()
    {
        if (_hwnd != IntPtr.Zero && IsVisible)
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // Clicking the taskbar raises it above all other topmost windows, which hides the pill until the next
    // placement tick. Re-raise immediately when the foreground changes or explorer reorders its windows.
    private Native.WinEventProc? _winEventProc;
    private readonly List<IntPtr> _hooks = new();

    private void HookTaskbarActivation()
    {
        _winEventProc = (_, _, _, _, _, _, _) => BringToTop();
        _hooks.Add(Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT));

        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero && Native.GetWindowThreadProcessId(taskbar, out var explorerPid) != 0)
        {
            _hooks.Add(Native.SetWinEventHook(Native.EVENT_OBJECT_REORDER, Native.EVENT_OBJECT_REORDER,
                IntPtr.Zero, _winEventProc, explorerPid, 0, Native.WINEVENT_OUTOFCONTEXT));
        }
    }

    private void UnhookTaskbarActivation()
    {
        foreach (var hook in _hooks)
        {
            if (hook != IntPtr.Zero) Native.UnhookWinEvent(hook);
        }
        _hooks.Clear();
    }

    private void Pill_Click(object sender, MouseButtonEventArgs e) => Clicked?.Invoke();

    private void Pill_MouseEnter(object sender, MouseEventArgs e) => Pill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "PillHoverBg");

    private void Pill_MouseLeave(object sender, MouseEventArgs e) => Pill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "PillBg");
}
