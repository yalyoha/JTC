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
/// Row-background ("плашка") style used inside the Colored theme. Dark and Light themes
/// always use their own palette; this only differentiates whether the gradient window
/// hosts white rows with dark text OR dark rows with white text.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlashkaStyle
{
    White,
    Dark,
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

/// <summary>
/// Baked-in default hex values for the plashka + status colour groups. Kept as public
/// constants so the settings dialog can offer a "reset to default" gesture and so
/// SettingsStore.Load can migrate legacy (pre-v0.5) rows that never wrote these fields.
/// </summary>
public static class DefaultColors
{
    // Plashka bg/fg — defaults for the "White plashka" look. Dark-plashka defaults are
    // derived at load time (see SettingsStore) when ColoredPlashkaStyle == Dark.
    public const string WhitePlashkaBgHex = "#FFFFFFFF";
    public const string WhitePlashkaFgHex = "#FF212121";
    public const string DarkPlashkaBgHex  = "#FF2A2A2A";
    public const string DarkPlashkaFgHex  = "#FFFFFFFF";

    // Status stripe hues — Material A400 across the board so they read on any bg.
    public const string StatusIdleHex        = "#FF90A4AE";
    public const string StatusDownloadingHex = "#FFFF9100";
    public const string StatusSeedingHex     = "#FF00E676";
    public const string StatusHashingHex     = "#FF2979FF";
    public const string StatusErrorHex       = "#FFFF1744";
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

    // For AppTheme.Colored: which row-background palette to use over the gradient.
    // Legacy hint from v0.4.8; still used by SettingsStore.Load to derive PlashkaBgHex /
    // PlashkaFgHex when those newer fields are absent (pre-v0.5 settings.json).
    public PlashkaStyle ColoredPlashkaStyle { get; init; } = PlashkaStyle.White;

    // For AppTheme.Colored: plashka (row) background + foreground text colours. v0.5+
    // makes these user-picked (see DefaultColors for the two ship-defaults). Null on
    // pre-v0.5 records → SettingsStore.Load fills them in from ColoredPlashkaStyle.
    public string? PlashkaBgHex { get; init; }
    public string? PlashkaFgHex { get; init; }

    // Status-stripe colours. Applied globally regardless of theme — a torrent's state
    // is orthogonal to the window backdrop. Null on pre-v0.5 records → defaults kick
    // in at load time. The row's "progress bar" gradient fill is a 17%-opacity
    // composite of the row's current status colour over the plashka bg, so changing
    // e.g. StatusDownloading tints every downloading row's fill accordingly.
    public string? StatusIdleHex        { get; init; }
    public string? StatusDownloadingHex { get; init; }
    public string? StatusSeedingHex     { get; init; }
    public string? StatusHashingHex     { get; init; }
    public string? StatusErrorHex       { get; init; }

    // User-saved color-preset library. Built-in presets live in BuiltInColorPresets and
    // are prepended in the settings dialog — they are not persisted here to avoid drift
    // if we ever tweak the defaults in a future release.
    public List<ColorPreset> CustomPresets { get; init; } = new();
}
