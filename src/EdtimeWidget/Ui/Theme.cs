using System;
using System.Windows;
using System.Windows.Media;
using EdtimeWidget.Api;
using Microsoft.Win32;

namespace EdtimeWidget.Ui;

/// <summary>Light/dark brushes following the Windows taskbar theme, plus the state colors.</summary>
public static class Theme
{
    public static readonly Color Working = Color.FromRgb(0x2E, 0xA0, 0x43);
    public static readonly Color Break = Color.FromRgb(0xE3, 0xA0, 0x08);
    public static readonly Color Off = Color.FromRgb(0x8B, 0x8B, 0x8B);
    public static readonly Color Error = Color.FromRgb(0xD1, 0x34, 0x38);

    public static Color ForState(WorkState? state) => state switch
    {
        WorkState.Working => Working,
        WorkState.Break or WorkState.SmokerBreak => Break,
        _ => Off,
    };

    public static void Apply()
    {
        var dark = !ReadLight("SystemUsesLightTheme");
        var r = Application.Current.Resources;
        r["PillBg"] = Brush(dark ? "#33FFFFFF" : "#14000000");
        r["PillHoverBg"] = Brush(dark ? "#4DFFFFFF" : "#26000000");
        r["SurfaceBg"] = Brush(dark ? "#FF2B2B2B" : "#FFF9F9F9");
        r["SurfaceBorder"] = Brush(dark ? "#FF3D3D3D" : "#FFE0E0E0");
        r["Fg"] = Brush(dark ? "#FFFFFFFF" : "#FF1B1B1B");
        r["SubFg"] = Brush(dark ? "#FFB5B5B5" : "#FF5F5F5F");
        r["ButtonBg"] = Brush(dark ? "#FF3A3A3A" : "#FFFFFFFF");
        r["ButtonHoverBg"] = Brush(dark ? "#FF454545" : "#FFF0F0F0");
        r["ButtonBorder"] = Brush(dark ? "#FF4A4A4A" : "#FFD0D0D0");
        r["InputBg"] = Brush(dark ? "#FF1F1F1F" : "#FFFFFFFF");
        r["ErrorFg"] = new SolidColorBrush(Error);
    }

    private static bool ReadLight(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue(name) is int v ? v != 0 : true;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    // edtime books at minute precision, so times are shown without seconds.
    public static string FormatHm(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}";

    // Segoe Fluent Icons / Segoe MDL2 Assets glyphs.
    public const string GlyphWork = "\uE821";
    public const string GlyphBreak = "\uE769";
    public const string GlyphOff = "\uE7E8";
    public const string GlyphWarning = "\uE7BA";
    public const string GlyphLogin = "\uE77B";
    public const string GlyphLoading = "\uE72C";
}
