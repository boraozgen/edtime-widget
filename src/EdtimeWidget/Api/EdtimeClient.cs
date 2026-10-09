using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EdtimeWidget.Api;

public sealed class AuthException : Exception
{
    public AuthException(string message) : base(message) { }
}

public sealed record Session(string Token, long EmployeeId, DateTimeOffset Expires);

/// <summary>Thin client for the (unofficial) edtime employee API. See docs/api-notes.md.</summary>
public sealed class EdtimeClient : IDisposable
{
    private const string BaseUrl = "https://app.edtime.de";
    private const string AppVersion = "4.25.0";

    private readonly HttpClient _http;
    private readonly Func<(string User, string Password)?> _credentials;
    private readonly Action<Session?> _sessionChanged;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private Session? _session;
    private long? _groupId;

    public EdtimeClient(Session? cached, Func<(string User, string Password)?> credentials, Action<Session?> sessionChanged)
    {
        _session = cached;
        _credentials = credentials;
        _sessionChanged = sessionChanged;
        _http = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.Add("X-App-Version", AppVersion);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Drops the current session, e.g. after the user changed credentials.</summary>
    public void ResetSession()
    {
        _session = null;
        _groupId = null;
        _sessionChanged(null);
    }

    public async Task<WorkStatus> GetStatusAsync(CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, "work/status", null, ct);
        var status = WorkStatus.Parse(doc.RootElement.GetProperty("data"), DateTimeOffset.Now);
        if (status.GroupId is long g) _groupId = g;
        return status;
    }

    public Task StartWorkAsync(CancellationToken ct = default) => StampAsync("work/start", ct);
    public Task EndWorkAsync(CancellationToken ct = default) => StampAsync("work/end", ct);
    public Task StartBreakAsync(CancellationToken ct = default) => StampAsync("pause/start", ct);
    public Task EndBreakAsync(CancellationToken ct = default) => StampAsync("pause/end", ct);

    private async Task StampAsync(string action, CancellationToken ct)
    {
        if (_groupId is null) await GetStatusAsync(ct);
        var body = new { groupId = _groupId, timeTypeId = (long?)null };
        (await SendAsync(HttpMethod.Post, action, body, ct)).Dispose();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string employeePath, object? body, CancellationToken ct)
    {
        var session = await EnsureSessionAsync(forceLogin: false, ct);
        var response = await SendOnceAsync(method, employeePath, body, session, ct);

        // A rejected token means the request was not executed, so retrying once after re-login is safe even for POSTs.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            session = await EnsureSessionAsync(forceLogin: true, ct);
            response = await SendOnceAsync(method, employeePath, body, session, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AuthException("edtime hat die Anmeldung abgelehnt.");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new InvalidOperationException("Status wurde zwischenzeitlich geändert.");
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
    }

    private Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string employeePath, object? body, Session session, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, $"/api/employees/{session.EmployeeId}/{employeePath}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return _http.SendAsync(request, ct);
    }

    private async Task<Session> EnsureSessionAsync(bool forceLogin, CancellationToken ct)
    {
        var stale = _session;
        if (!forceLogin && stale is not null && stale.Expires > DateTimeOffset.Now.AddMinutes(5)) return stale;

        await _loginLock.WaitAsync(ct);
        try
        {
            // Another caller may have logged in while we waited.
            if (_session is not null && !ReferenceEquals(_session, stale) && _session.Expires > DateTimeOffset.Now) return _session;

            var creds = _credentials() ?? throw new AuthException("Keine Zugangsdaten hinterlegt.");
            using var response = await _http.PostAsJsonAsync(
                "/api/v3/auth/stempelmedien/login?scope=UserAuthResponse",
                new { username = creds.User, password = creds.Password, endpointType = "smartphone" }, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new AuthException("Benutzername oder Passwort/PIN falsch.");
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            if (root.GetProperty("role").GetString() != "employee")
                throw new AuthException("Dieses Konto ist kein Mitarbeiterkonto.");
            _session = new Session(
                root.GetProperty("token").GetString()!,
                root.GetProperty("userAuth").GetProperty("employeeId").GetInt64(),
                DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("expires").GetInt64()));
            _sessionChanged(_session);
            return _session;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    public void Dispose() => _http.Dispose();
}
