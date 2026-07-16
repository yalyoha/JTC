using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JTC.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    // Gradient theme with user-controlled top+bottom colors. Legacy JSON values "Brand"
    // and "Brand2" from earlier builds are migrated to Colored in SettingsStore.Load,
    // preserving their original palette via ColoredTopHex/ColoredBottomHex.
    Colored,
    Dark,
    Light,
}

/// <summary>
/// A named pair of gradient colors (top + bottom) that the user can save from the settings
/// dialog and re-apply later. Stored as hex strings (#AARRGGBB) so JSON is human-readable.
/// </summary>
public sealed record ColorPreset
{
    public string Name { get; init; } = "";
    public string TopHex { get; init; } = "";
    public string BottomHex { get; init; } = "";
}

/// <summary>
/// Built-in color presets that always appear at the top of the preset list in the settings
/// dialog. Legacy "Фирменная 1" (pink → orange) and "Фирменная 2" (blue → lime).
/// </summary>
public static class BuiltInColorPresets
{
    public const string PinkOrangeName = "Розово-оранжевая";
    public const string BlueLimeName   = "Сине-зелёная";

    public const string PinkTopHex     = "#FFE52E71";
    public const string PinkBottomHex  = "#FFFF8A00";
    public const string BlueTopHex     = "#FF324166";
    public const string BlueBottomHex  = "#FF7AB317";

    public static readonly IReadOnlyList<ColorPreset> All = new[]
    {
        new ColorPreset { Name = PinkOrangeName, TopHex = PinkTopHex, BottomHex = PinkBottomHex },
        new ColorPreset { Name = BlueLimeName,   TopHex = BlueTopHex, BottomHex = BlueBottomHex },
    };
}

public sealed record AppSettings
{
    public string? LastDownloadDir { get; init; }
    public int MaxSimultaneousDownloads { get; init; } = 3;
    public AppTheme Theme { get; init; } = AppTheme.Colored;

    // For AppTheme.Colored: current gradient endpoint colors (hex #AARRGGBB). Null on
    // fresh installs → ThemeHelper falls back to the first built-in preset.
    public string? ColoredTopHex { get; init; }
    public string? ColoredBottomHex { get; init; }

    // User-saved color-preset library. Built-in presets live in BuiltInColorPresets and
    // are prepended in the settings dialog — they are not persisted here to avoid drift
    // if we ever tweak the defaults in a future release.
    public List<ColorPreset> CustomPresets { get; init; } = new();
}
