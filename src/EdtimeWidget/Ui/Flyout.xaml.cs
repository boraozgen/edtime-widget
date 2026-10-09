using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using EdtimeWidget.Api;
using EdtimeWidget.State;

namespace EdtimeWidget.Ui;

public partial class Flyout : Window
{
    private enum StampAction { None, StartWork, StartBreak, EndBreak, EndWork, Login }

    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(4);

    private readonly StatusService _service;
    private StampAction _primary;
    private StampAction _secondary;
    private DateTime _confirmEndUntil;
    private Native.RECT? _anchor;

    public event Action? SettingsRequested;

    /// <summary>When the flyout was last hidden; used so a click on the tray icon that closed it doesn't reopen it.</summary>
    public DateTime LastHidden { get; private set; }

    public Flyout(StatusService service)
    {
        _service = service;
        InitializeComponent();
        Left = -10000;
        SizeChanged += (_, _) => PlaceAtAnchor();
    }

    internal void ShowAt(Native.RECT anchor)
    {
        _anchor = anchor;
        _confirmEndUntil = default;
        Render();
        Show();
        UpdateLayout();
        PlaceAtAnchor();
        Activate();
    }

    private void PlaceAtAnchor()
    {
        if (_anchor is not { } anchor || !IsVisible) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var scale = Native.GetDpiForWindow(hwnd) / 96.0;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var gap = (int)(12 * scale);

        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(anchor.Left, anchor.Top)).WorkingArea;
        var x = Math.Clamp(anchor.Left + anchor.Width / 2 - width / 2, area.Left + gap, Math.Max(area.Left + gap, area.Right - width - gap));
        var taskbarAtTop = anchor.Top < area.Top + area.Height / 2;
        var y = taskbarAtTop ? area.Top + gap : area.Bottom - height - gap;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE);
    }

    public void Render()
    {
        var s = _service.Current;
        var now = DateTimeOffset.Now;
        var confirming = DateTime.Now < _confirmEndUntil;

        _primary = StampAction.None;
        _secondary = StampAction.None;

        if (s is null)
        {
            Dot.Fill = new SolidColorBrush(_service.Error is null ? Theme.Off : Theme.Error);
            StateText.Text = _service.NeedsLogin ? "Nicht angemeldet" : _service.Error is null ? "Lade Status…" : "Status unbekannt";
            TimerText.Text = "–:––";
            DetailText.Text = string.Empty;
            if (_service.NeedsLogin) _primary = StampAction.Login;
        }
        else
        {
            Dot.Fill = new SolidColorBrush(Theme.ForState(s.State));
            var worked = s.WorkedAt(now);
            switch (s.State)
            {
                case WorkState.Working:
                    StateText.Text = "Arbeitszeit läuft";
                    TimerText.Text = Theme.FormatHm(worked);
                    DetailText.Text = (s.WorkStart is { } start ? $"Beginn {start.ToLocalTime():HH:mm}" : "Heute")
                        + $"  ·  Pausen {Theme.FormatHm(s.BreakTotal)}";
                    _primary = StampAction.StartBreak;
                    _secondary = StampAction.EndWork;
                    break;
                case WorkState.Break:
                case WorkState.SmokerBreak:
                    StateText.Text = s.State == WorkState.SmokerBreak ? "Raucherpause" : "In Pause";
                    TimerText.Text = Theme.FormatHm(s.CurrentBreakAt(now));
                    DetailText.Text = (s.BreakStart is { } bs ? $"Pause seit {bs.ToLocalTime():HH:mm}  ·  " : string.Empty)
                        + $"Arbeitszeit {Theme.FormatHm(worked)}";
                    _primary = StampAction.EndBreak;
                    break;
                default:
                    StateText.Text = "Nicht eingestempelt";
                    TimerText.Text = Theme.FormatHm(worked);
                    DetailText.Text = worked > TimeSpan.Zero ? "Heute gearbeitet" : string.Empty;
                    _primary = StampAction.StartWork;
                    break;
            }
        }

        ConfigureButton(PrimaryAction, _primary, confirming, primary: true);
        ConfigureButton(SecondaryAction, _secondary, confirming, primary: false);
        Actions.Visibility = _primary == StampAction.None ? Visibility.Collapsed : Visibility.Visible;

        ErrorText.Text = _service.Error ?? string.Empty;
        ErrorText.Visibility = _service.Error is null ? Visibility.Collapsed : Visibility.Visible;

        UpdatedText.Text = _service.Busy ? "Wird gesendet…"
            : _service.LastSuccess is { } t ? $"Stand {t:HH:mm:ss}" : string.Empty;
    }

    private void ConfigureButton(System.Windows.Controls.Button button, StampAction action, bool confirming, bool primary)
    {
        button.Visibility = action == StampAction.None ? Visibility.Collapsed : Visibility.Visible;
        button.IsEnabled = !_service.Busy;
        button.Content = action switch
        {
            StampAction.StartWork => "Arbeit beginnen",
            StampAction.StartBreak => "Pause beginnen",
            StampAction.EndBreak => "Pause beenden",
            StampAction.EndWork => confirming ? "Wirklich Feierabend? Nochmal klicken" : "Arbeit beenden",
            StampAction.Login => "Anmelden…",
            _ => string.Empty,
        };
        if (primary)
        {
            var color = action switch
            {
                StampAction.StartBreak => Theme.Break,
                StampAction.Login => Color.FromRgb(0x00, 0x67, 0xC0),
                _ => Theme.Working,
            };
            button.Background = new SolidColorBrush(color);
        }
        else if (action == StampAction.EndWork && confirming)
        {
            button.Background = new SolidColorBrush(Theme.Error);
            button.Foreground = Brushes.White;
        }
        else
        {
            button.SetResourceReference(BackgroundProperty, "ButtonBg");
            button.SetResourceReference(ForegroundProperty, "Fg");
        }
    }

    private Task Run(StampAction action)
    {
        switch (action)
        {
            case StampAction.StartWork: return _service.PerformAsync(c => c.StartWorkAsync());
            case StampAction.StartBreak: return _service.PerformAsync(c => c.StartBreakAsync());
            case StampAction.EndBreak: return _service.PerformAsync(c => c.EndBreakAsync());
            case StampAction.EndWork:
                if (DateTime.Now < _confirmEndUntil)
                {
                    _confirmEndUntil = default;
                    return _service.PerformAsync(c => c.EndWorkAsync());
                }
                _confirmEndUntil = DateTime.Now + ConfirmWindow;
                Render();
                return Task.CompletedTask;
            case StampAction.Login:
                OpenSettings();
                return Task.CompletedTask;
            default:
                return Task.CompletedTask;
        }
    }

    private async void PrimaryAction_Click(object sender, RoutedEventArgs e) => await Run(_primary);

    private async void SecondaryAction_Click(object sender, RoutedEventArgs e) => await Run(_secondary);

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _service.RefreshAsync();

    private void OpenWeb_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://app.edtime.de/stempelmedien/") { UseShellExecute = true });
        Hide();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        Hide();
        SettingsRequested?.Invoke();
    }

    private void Window_Deactivated(object sender, EventArgs e) => Hide();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Hide();
    }

    public new void Hide()
    {
        if (!IsVisible) return;
        LastHidden = DateTime.Now;
        base.Hide();
    }
}
