using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace EdtimeWidget;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DisplayMode
{
    /// <summary>Only the notification-area icon, showing the state.</summary>
    Tray,

    /// <summary>A pill with state and time on the taskbar, left of the notification area.</summary>
    Taskbar,
}

/// <summary>Non-secret settings, stored as JSON in %APPDATA%\EdtimeWidget.</summary>
public sealed class AppSettings
{
    public int PollSeconds { get; set; } = 60;

    public DisplayMode Display { get; set; } = DisplayMode.Tray;

    /// <summary>Extra horizontal shift of the pill (pixels, positive = further left).</summary>
    public int OffsetX { get; set; }

    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdtimeWidget");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "EdtimeWidget";

    public static bool Autostart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}
