using Windows.UI;
using JTC.Services;

namespace JTC.Helpers;

// Row-background palette. Two axes:
//   1. Plashka surface (Bg / Fill / Fg) is THEME-scoped — Light + Brand share opaque white
//      plashkas with dark text, Dark uses opaque #212121 plashkas with white text.
//   2. Status is STATE-scoped and theme-INDEPENDENT — a 4 px left-edge stripe painted in a
//      saturated Material A400 hue so it reads equally on white and dark plashkas.
// Progress renders as a two-band LinearGradientBrush (Fill on the left, Bg on the right,
// with a hard seam at Progress %). Bg and Fill are pre-composited (opaque colors, not
// alpha overlays) so the gradient interpolates cleanly between two solids.
public static class RowBrushes
{
    public sealed record Palette(Color Bg, Color Fill, Color BgSelected, Color FillSelected, Color Fg);

    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
    private static Color Hex(uint rgb) =>
        Color.FromArgb(0xFF, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    // Status stripe hues, shared across all themes. Material A400 for the four "live" states,
    // Blue Grey 400 for Idle (calmer, reads as "not active" without competing for attention).
    public static readonly Color StatusIdle        = Hex(0x90A4AE);
    public static readonly Color StatusDownloading = Hex(0xFF9100);
    public static readonly Color StatusSeeding     = Hex(0x00E676);
    public static readonly Color StatusHashing     = Hex(0x2979FF);
    public static readonly Color StatusError       = Hex(0xFF1744);

    // Light: opaque white plashka on the #f2f2f2 window background, dark text. Fill is
    // pre-composited: #ffffff × 0.94 + #000000 × 0.06 = #f0f0f0 (normal); selected drops the
    // plashka to #ececec, fill = #ececec × 0.90 + #000000 × 0.10 = #d4d4d4.
    private static readonly Palette LightPalette = new(
        Bg:           C(0xFF, 0xFF, 0xFF, 0xFF),
        Fill:         C(0xFF, 0xF0, 0xF0, 0xF0),
        BgSelected:   C(0xFF, 0xEC, 0xEC, 0xEC),
        FillSelected: C(0xFF, 0xD4, 0xD4, 0xD4),
        Fg:           C(0xFF, 0x21, 0x21, 0x21));

    // Brand uses the same white plashka as Light — it sits on the pink→orange gradient so
    // the plashka fully covers the brand color inside its rectangle (only the 3 px inter-row
    // margin lets the gradient peek through).
    private static readonly Palette BrandPalette = LightPalette;

    // Dark: plashka sits on top of the #212121 window with a faint white lift so the row
    // rectangle registers even when the state stripe alone wouldn't reveal it. Each step in
    // the ladder is a small % of white composited over #212121:
    //   Bg           ≈ 4 %  → #2A2A2A (barely noticeable, the "лёгкое еле заметное" background)
    //   Fill         ≈ 8 %  → #363636 (progress seam vs. Bg is visible)
    //   BgSelected   ≈ 8 %  → #363636 (selected plashka lifts one step)
    //   FillSelected ≈ 14 % → #474747 (selected progress lifts one more)
    private static readonly Palette DarkPalette = new(
        Bg:           C(0xFF, 0x2A, 0x2A, 0x2A),
        Fill:         C(0xFF, 0x36, 0x36, 0x36),
        BgSelected:   C(0xFF, 0x36, 0x36, 0x36),
        FillSelected: C(0xFF, 0x47, 0x47, 0x47),
        Fg:           C(0xFF, 0xFF, 0xFF, 0xFF));

    public static Palette Current { get; private set; } = BrandPalette;

    public static void Set(AppTheme theme) =>
        Current = theme switch
        {
            AppTheme.Dark  => DarkPalette,
            AppTheme.Light => LightPalette,
            _              => BrandPalette,
        };
}
