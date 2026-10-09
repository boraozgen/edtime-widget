using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Threading;
using EdtimeWidget.Api;

namespace EdtimeWidget.State;

/// <summary>Polls the server, ticks the timer locally every second and runs stamp actions. UI thread only.</summary>
public sealed class StatusService
{
    private readonly EdtimeClient _client;
    private readonly DispatcherTimer _poll;
    private readonly DispatcherTimer _tick;
    private bool _refreshing;

    public WorkStatus? Current { get; private set; }
    public string? Error { get; private set; }
    public bool NeedsLogin { get; private set; }
    public bool Busy { get; private set; }
    public DateTimeOffset? LastSuccess { get; private set; }

    /// <summary>Raised on every tick and after each status change.</summary>
    public event Action? Changed;

    public StatusService(EdtimeClient client, TimeSpan pollInterval)
    {
        _client = client;
        _poll = new DispatcherTimer { Interval = pollInterval };
        _poll.Tick += async (_, _) => await RefreshAsync();
        _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick += (_, _) => Changed?.Invoke();
    }

    public TimeSpan PollInterval
    {
        get => _poll.Interval;
        set => _poll.Interval = value;
    }

    public void Start()
    {
        _poll.Start();
        _tick.Start();
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            Current = await _client.GetStatusAsync();
            LastSuccess = DateTimeOffset.Now;
            Error = null;
            NeedsLogin = false;
        }
        catch (Exception e)
        {
            Fail(e);
        }
        finally
        {
            _refreshing = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Runs a stamp action once (never retried automatically), then re-reads the status.</summary>
    public async Task PerformAsync(Func<EdtimeClient, Task> action)
    {
        if (Busy) return;
        Busy = true;
        Changed?.Invoke();
        try
        {
            await action(_client);
            Error = null;
        }
        catch (Exception e)
        {
            Fail(e);
        }
        finally
        {
            Busy = false;
            var actionError = Error;
            await RefreshAsync();
            // Keep the action's error visible even when the follow-up refresh succeeds.
            if (actionError is not null && Error is null)
            {
                Error = actionError;
                Changed?.Invoke();
            }
        }
    }

    private void Fail(Exception e)
    {
        NeedsLogin = e is AuthException;
        Error = e switch
        {
            AuthException => e.Message,
            HttpRequestException or TaskCanceledException => "Keine Verbindung zu edtime.",
            InvalidOperationException => e.Message,
            _ => "Fehler: " + e.Message,
        };
    }
}
