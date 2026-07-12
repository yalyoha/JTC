using Microsoft.UI.Xaml.Media;
using Windows.UI;
using JTC.Services;

namespace JTC.Helpers;

// Per-theme row-background palette. Each theme picks its own tints for the four
// row states (waiting/downloading/seeding/error) with a normal and "selected"
// variant. Kept semi-transparent so they blend into the theme background.
public static class RowBrushes
{
    public sealed record Palette(
        SolidColorBrush Seeding, SolidColorBrush SeedingSelected,
        SolidColorBrush Downloading, SolidColorBrush DownloadingSelected,
        SolidColorBrush Idle, SolidColorBrush IdleSelected,
        SolidColorBrush Error, SolidColorBrush ErrorSelected);

    private static SolidColorBrush B(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    // Brand: designed against the pink→orange gradient. White highlights read
    // as "brightening" over the warm background.
    private static readonly Palette BrandPalette = new(
        Seeding:             B(0x30, 0xFF, 0xFF, 0xFF),
        SeedingSelected:     B(0x60, 0xFF, 0xFF, 0xFF),
        Downloading:         B(0x40, 0xFF, 0xD5, 0x80),
        DownloadingSelected: B(0x75, 0xFF, 0xD5, 0x80),
        Idle:                B(0x35, 0x30, 0x20, 0x40),
        IdleSelected:        B(0x70, 0x30, 0x20, 0x40),
        Error:               B(0x50, 0xB0, 0x20, 0x20),
        ErrorSelected:       B(0x80, 0xB0, 0x20, 0x20));

    // Dark: solid #212121 — white idle brightens the row, green matches the
    // progress-bar color so seeding stays readable.
    private static readonly Palette DarkPalette = new(
        Seeding:             B(0x28, 0x4C, 0xD9, 0x64),
        SeedingSelected:     B(0x55, 0x4C, 0xD9, 0x64),
        Downloading:         B(0x40, 0xFF, 0xD5, 0x80),
        DownloadingSelected: B(0x75, 0xFF, 0xD5, 0x80),
        Idle:                B(0x20, 0xFF, 0xFF, 0xFF),
        IdleSelected:        B(0x45, 0xFF, 0xFF, 0xFF),
        Error:               B(0x50, 0xB0, 0x20, 0x20),
        ErrorSelected:       B(0x80, 0xB0, 0x20, 0x20));

    // Light: solid #FFFFFF — invert to darker tints so states are visible.
    private static readonly Palette LightPalette = new(
        Seeding:             B(0x38, 0x4C, 0xD9, 0x64),
        SeedingSelected:     B(0x68, 0x4C, 0xD9, 0x64),
        Downloading:         B(0x40, 0xE5, 0x8A, 0x00),
        DownloadingSelected: B(0x70, 0xE5, 0x8A, 0x00),
        Idle:                B(0x10, 0x00, 0x00, 0x00),
        IdleSelected:        B(0x25, 0x00, 0x00, 0x00),
        Error:               B(0x40, 0xE0, 0x20, 0x20),
        ErrorSelected:       B(0x70, 0xE0, 0x20, 0x20));

    public static Palette Current { get; private set; } = BrandPalette;

    public static void Set(AppTheme theme) =>
        Current = theme switch
        {
            AppTheme.Dark  => DarkPalette,
            AppTheme.Light => LightPalette,
            _              => BrandPalette,
        };
}
