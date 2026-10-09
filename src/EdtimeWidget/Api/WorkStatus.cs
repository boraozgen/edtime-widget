using System;
using System.Globalization;
using System.Text.Json;

namespace EdtimeWidget.Api;

public enum WorkState
{
    Off = 0,
    Working = 1,
    Break = 2,
    SmokerBreak = 3,
}

/// <summary>Snapshot of <c>GET /api/employees/{id}/work/status</c>.</summary>
public sealed record WorkStatus(
    WorkState State,
    TimeSpan WorkedAtFetch,
    DateTimeOffset? WorkStart,
    DateTimeOffset? BreakStart,
    TimeSpan BreakTotal,
    long? GroupId,
    DateTimeOffset FetchedAt)
{
    public bool IsOnBreak => State is WorkState.Break or WorkState.SmokerBreak;

    /// <summary>Net work time today, ticking forward locally while working.</summary>
    public TimeSpan WorkedAt(DateTimeOffset now) =>
        State == WorkState.Working ? WorkedAtFetch + Clamp(now - FetchedAt) : WorkedAtFetch;

    /// <summary>Duration of the running break, or zero when not on a break.</summary>
    public TimeSpan CurrentBreakAt(DateTimeOffset now) =>
        IsOnBreak && BreakStart is { } start ? Clamp(now - start) : TimeSpan.Zero;

    private static TimeSpan Clamp(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

    public static WorkStatus Parse(JsonElement data, DateTimeOffset fetchedAt)
    {
        var state = data.TryGetProperty("state", out var s) && s.TryGetInt32(out var si) && Enum.IsDefined(typeof(WorkState), si)
            ? (WorkState)si
            : WorkState.Off;

        DateTimeOffset? workStart = null;
        if (data.TryGetProperty("workinghour", out var wh) && wh.ValueKind == JsonValueKind.Object)
            workStart = ParseDate(wh, "start");

        // The running break is the pause entry without an end.
        DateTimeOffset? breakStart = null;
        if (data.TryGetProperty("pause", out var pauses) && pauses.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in pauses.EnumerateArray())
            {
                if (ParseDate(p, "end") is null && ParseDate(p, "start") is { } start) breakStart = start;
            }
        }
        if (breakStart is null && state is WorkState.Break or WorkState.SmokerBreak)
            breakStart = ParseDate(data, "breakstart");

        long? groupId = data.TryGetProperty("activeGroup", out var g) && g.ValueKind == JsonValueKind.Object
            && g.TryGetProperty("id", out var gid) && gid.TryGetInt64(out var gl) ? gl : null;

        return new WorkStatus(
            state,
            TimeSpan.FromSeconds(ReadNumber(data, "total")),
            workStart,
            breakStart,
            TimeSpan.FromSeconds(ReadNumber(data, "pausetotal")),
            groupId,
            fetchedAt);
    }

    private static double ReadNumber(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    /// <summary>Parses PHP DateTime JSON: <c>{"date":"2026-10-08 08:44:00.000000","timezone":"+02:00"}</c>.</summary>
    private static DateTimeOffset? ParseDate(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var d) || d.ValueKind != JsonValueKind.Object) return null;
        if (!d.TryGetProperty("date", out var dateEl) || dateEl.GetString() is not { } date) return null;
        if (!DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return null;

        var zone = d.TryGetProperty("timezone", out var tz) ? tz.GetString() : null;
        if (zone is not null && (zone.StartsWith('+') || zone.StartsWith('-'))
            && TimeSpan.TryParse(zone.TrimStart('+'), CultureInfo.InvariantCulture, out var offset))
        {
            return new DateTimeOffset(local, zone.StartsWith('-') ? -offset.Duration() : offset);
        }
        if (zone is not null)
        {
            try
            {
                var info = TimeZoneInfo.FindSystemTimeZoneById(zone);
                return new DateTimeOffset(local, info.GetUtcOffset(local));
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
