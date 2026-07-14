using Windows.UI;
using JTC.Services;

namespace JTC.Helpers;

// Per-theme row-background palette. Each theme picks its own colors for the five
// row states (idle/downloading/seeding/hashing/error). Each state carries four
// tints: bg + fill for the non-selected row, bgSelected + fillSelected for the
// selected row. The row VM builds a LinearGradientBrush at runtime — bg is the
// right (empty) portion, fill is the left (progressed) portion — so the row
// itself renders the progress bar as its own background.
public static class RowBrushes
{
    public sealed record StateColors(Color Bg, Color Fill, Color BgSelected, Color FillSelected);

    public sealed record Palette(
        StateColors Seeding,
        StateColors Downloading,
        StateColors Idle,
        StateColors Hashing,
        StateColors Error);

    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    // Brand: mockup from LAV-Server jtc-design. Alpha ramps 0x4D (0.30) → 0x99 (0.60) → 0xE6 (0.90)
    // encode "empty vs filled" and "unselected vs selected". Seeding and Idle keep Fill == Bg
    // because those states don't have partial progress to show (Seeding is 100 %, Idle is 0 %).
    private static readonly Palette BrandPalette = new(
        Seeding:     new(Bg: C(0x4D, 0x5F, 0xAD, 0x56), Fill: C(0x4D, 0x5F, 0xAD, 0x56),
                         BgSelected: C(0x99, 0x5F, 0xAD, 0x56), FillSelected: C(0x99, 0x5F, 0xAD, 0x56)),
        Downloading: new(Bg: C(0x4D, 0xB0, 0x20, 0x20), Fill: C(0x99, 0xB0, 0x20, 0x20),
                         BgSelected: C(0x99, 0xB0, 0x20, 0x20), FillSelected: C(0xE6, 0xB0, 0x20, 0x20)),
        Idle:        new(Bg: C(0x4D, 0x30, 0x20, 0x40), Fill: C(0x4D, 0x30, 0x20, 0x40),
                         BgSelected: C(0x99, 0x30, 0x20, 0x40), FillSelected: C(0x99, 0x30, 0x20, 0x40)),
        Hashing:     new(Bg: C(0x4D, 0x5A, 0xA0, 0xFF), Fill: C(0x99, 0x5A, 0xA0, 0xFF),
                         BgSelected: C(0x99, 0x5A, 0xA0, 0xFF), FillSelected: C(0xE6, 0x5A, 0xA0, 0xFF)),
        Error:       new(Bg: C(0x4D, 0x8C, 0x3C, 0x28), Fill: C(0x99, 0x8C, 0x3C, 0x28),
                         BgSelected: C(0x99, 0x8C, 0x3C, 0x28), FillSelected: C(0xE6, 0x8C, 0x3C, 0x28)));

    // Dark: solid #212121 background. Kept from the pre-mockup palette — Fill == Bg because
    // the old design showed a single flat tint per state; running the new gradient logic with
    // matching stops produces the same visual (uniform tinted row) with no code branch.
    private static readonly Palette DarkPalette = new(
        Seeding:     new(Bg: C(0x28, 0x4C, 0xD9, 0x64), Fill: C(0x28, 0x4C, 0xD9, 0x64),
                         BgSelected: C(0x55, 0x4C, 0xD9, 0x64), FillSelected: C(0x55, 0x4C, 0xD9, 0x64)),
        Downloading: new(Bg: C(0x40, 0xFF, 0xD5, 0x80), Fill: C(0x40, 0xFF, 0xD5, 0x80),
                         BgSelected: C(0x75, 0xFF, 0xD5, 0x80), FillSelected: C(0x75, 0xFF, 0xD5, 0x80)),
        Idle:        new(Bg: C(0x20, 0xFF, 0xFF, 0xFF), Fill: C(0x20, 0xFF, 0xFF, 0xFF),
                         BgSelected: C(0x45, 0xFF, 0xFF, 0xFF), FillSelected: C(0x45, 0xFF, 0xFF, 0xFF)),
        Hashing:     new(Bg: C(0x20, 0xFF, 0xFF, 0xFF), Fill: C(0x20, 0xFF, 0xFF, 0xFF),
                         BgSelected: C(0x45, 0xFF, 0xFF, 0xFF), FillSelected: C(0x45, 0xFF, 0xFF, 0xFF)),
        Error:       new(Bg: C(0x50, 0xB0, 0x20, 0x20), Fill: C(0x50, 0xB0, 0x20, 0x20),
                         BgSelected: C(0x80, 0xB0, 0x20, 0x20), FillSelected: C(0x80, 0xB0, 0x20, 0x20)));

    // Light: solid #FFFFFF background, darker tints so states register visually.
    private static readonly Palette LightPalette = new(
        Seeding:     new(Bg: C(0x38, 0x4C, 0xD9, 0x64), Fill: C(0x38, 0x4C, 0xD9, 0x64),
                         BgSelected: C(0x68, 0x4C, 0xD9, 0x64), FillSelected: C(0x68, 0x4C, 0xD9, 0x64)),
        Downloading: new(Bg: C(0x40, 0xE5, 0x8A, 0x00), Fill: C(0x40, 0xE5, 0x8A, 0x00),
                         BgSelected: C(0x70, 0xE5, 0x8A, 0x00), FillSelected: C(0x70, 0xE5, 0x8A, 0x00)),
        Idle:        new(Bg: C(0x10, 0x00, 0x00, 0x00), Fill: C(0x10, 0x00, 0x00, 0x00),
                         BgSelected: C(0x25, 0x00, 0x00, 0x00), FillSelected: C(0x25, 0x00, 0x00, 0x00)),
        Hashing:     new(Bg: C(0x10, 0x00, 0x00, 0x00), Fill: C(0x10, 0x00, 0x00, 0x00),
                         BgSelected: C(0x25, 0x00, 0x00, 0x00), FillSelected: C(0x25, 0x00, 0x00, 0x00)),
        Error:       new(Bg: C(0x40, 0xE0, 0x20, 0x20), Fill: C(0x40, 0xE0, 0x20, 0x20),
                         BgSelected: C(0x70, 0xE0, 0x20, 0x20), FillSelected: C(0x70, 0xE0, 0x20, 0x20)));

    public static Palette Current { get; private set; } = BrandPalette;

    public static void Set(AppTheme theme) =>
        Current = theme switch
        {
            AppTheme.Dark  => DarkPalette,
            AppTheme.Light => LightPalette,
            _              => BrandPalette,
        };
}
