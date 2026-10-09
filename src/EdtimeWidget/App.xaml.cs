using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using EdtimeWidget.Api;
using EdtimeWidget.Auth;
using EdtimeWidget.State;
using EdtimeWidget.Ui;
using Microsoft.Win32;

namespace EdtimeWidget;

public partial class App : Application
{
    private Mutex? _mutex;
    private AppSettings _settings = null!;
    private EdtimeClient _client = null!;
    private StatusService _service = null!;
    private Flyout _flyout = null!;
    private TrayIcon _tray = null!;
    private TaskbarPill? _pill;
    private DispatcherTimer? _placement;
    private bool _settingsOpen;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["--snapshot", var snapshotPath])
        {
            Theme.Apply();
            TaskbarPill.Snapshot(snapshotPath);
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, @"Local\EdtimeWidget", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        Theme.Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _settings = AppSettings.Load();
        _client = new EdtimeClient(CredentialStore.LoadSession(), CredentialStore.LoadLogin, CredentialStore.SaveSession);
        _service = new StatusService(_client, TimeSpan.FromSeconds(_settings.PollSeconds));

        _flyout = new Flyout(_service);
        _flyout.SettingsRequested += OpenSettings;

        _tray = new TrayIcon(OpenFromTray, () => _ = _service.RefreshAsync(), OpenSettings, Shutdown);
        ApplyDisplayMode();

        _service.Changed += Render;
        Render();

        if (CredentialStore.LoadLogin() is null) OpenSettings();
        _service.Start();
    }

    /// <summary>Shows or removes the pill next to the tray; the tray icon itself stays in both modes.</summary>
    private void ApplyDisplayMode()
    {
        if (_settings.Display == DisplayMode.Taskbar)
        {
            if (_pill is null)
            {
                _pill = new TaskbarPill();
                _pill.Clicked += () => ToggleFlyout(_pill.ScreenRect);
                _pill.Show();

                // Keeps the pill placed and above the taskbar, which takes the top z-order whenever it is clicked.
                _placement = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
                _placement.Tick += (_, _) => _pill?.Reposition();
                _placement.Start();
            }
            _pill.OffsetX = _settings.OffsetX;
            _pill.Reposition();
        }
        else if (_pill is not null)
        {
            _placement?.Stop();
            _placement = null;
            _pill.Close();
            _pill = null;
        }
    }

    private void Render()
    {
        var s = _service.Current;
        var now = DateTimeOffset.Now;
        string glyph, label, time;

        if (s is null)
        {
            (glyph, label) = _service.NeedsLogin ? (Theme.GlyphLogin, "Nicht angemeldet")
                : _service.Error is not null ? (Theme.GlyphWarning, "Status unbekannt")
                : (Theme.GlyphLoading, "Lade Status…");
            time = string.Empty;
        }
        else
        {
            var worked = s.WorkedAt(now);
            (glyph, label, time) = s.State switch
            {
                WorkState.Working => (Theme.GlyphWork, "Arbeit", Theme.FormatHm(worked)),
                WorkState.Break => (Theme.GlyphBreak, "Pause", Theme.FormatHm(s.CurrentBreakAt(now))),
                WorkState.SmokerBreak => (Theme.GlyphBreak, "Raucherpause", Theme.FormatHm(s.CurrentBreakAt(now))),
                _ => (Theme.GlyphOff, worked > TimeSpan.Zero ? "Feierabend" : "Nicht eingestempelt",
                      worked > TimeSpan.Zero ? Theme.FormatHm(worked) : string.Empty),
            };
        }

        // The tray-only mode shows just the state; times appear only alongside the pill.
        if (_pill is null) time = string.Empty;

        var tooltip = time.Length > 0 ? $"edtime – {label} {time}" : $"edtime – {label}";
        if (_service.Error is not null) tooltip += $"\n{_service.Error}";
        var color = s is null && _service.Error is not null ? Theme.Error : Theme.ForState(s?.State);
        _pill?.Update(glyph, time, color, tooltip, dim: _service.Error is not null);
        _tray.Update(glyph, color, tooltip);
        if (_flyout.IsVisible) _flyout.Render();
    }

    private void OpenFromTray(System.Drawing.Point click) =>
        ToggleFlyout(new Native.RECT { Left = click.X, Top = click.Y, Right = click.X, Bottom = click.Y });

    private void ToggleFlyout(Native.RECT? anchor)
    {
        if (_flyout.IsVisible)
        {
            _flyout.Hide();
            return;
        }
        // Clicking the pill or tray icon deactivates (and hides) the flyout first; don't reopen it on that same click.
        if ((DateTime.Now - _flyout.LastHidden).TotalMilliseconds < 300) return;
        if (anchor is { } rect) _flyout.ShowAt(rect);
        _ = _service.RefreshAsync();
    }

    private void OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        try
        {
            var window = new SettingsWindow(_settings);
            if (window.ShowDialog() != true) return;

            _service.PollInterval = TimeSpan.FromSeconds(_settings.PollSeconds);
            ApplyDisplayMode();
            Render();
            if (window.LoginChanged) _client.ResetSession();
            _ = _service.RefreshAsync();
        }
        finally
        {
            _settingsOpen = false;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) Dispatcher.Invoke(Theme.Apply);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Dispatcher.Invoke(() => _pill?.Reposition());

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _tray?.Dispose();
        _client?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
