using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using JTC.Services;

namespace JTC.Helpers;

public static class ThemeHelper
{
    // Brand palette straight from the logo: pink→orange gradient, white text.
    private static readonly Color BrandTop    = Color.FromArgb(0xFF, 0xE5, 0x2E, 0x71);
    private static readonly Color BrandBottom = Color.FromArgb(0xFF, 0xFF, 0x8A, 0x00);
    private static readonly Color DarkSolid   = Color.FromArgb(0xFF, 0x21, 0x21, 0x21);
    private static readonly Color LightSolid  = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    private static LinearGradientBrush BrandGradient()
    {
        var g = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.5, 0),
            EndPoint   = new Windows.Foundation.Point(0.5, 1),
        };
        g.GradientStops.Add(new GradientStop { Color = BrandTop,    Offset = 0 });
        g.GradientStops.Add(new GradientStop { Color = BrandBottom, Offset = 1 });
        return g;
    }

    // ContentDialog's default template binds BOTH the outer BackgroundElement Border
    // AND the inner CommandSpace Grid to {TemplateBinding Background}. A gradient brush
    // paints per-element (RelativeToBoundingBox), so both regions render their own
    // independent 0..1 gradient — the button strip visibly "restarts" the pink at its
    // own top, creating the discontinuity we see in screenshots/img_10.png.
    //
    // No Resources override can decouple them (both go through the same Background
    // property). Instead, once the dialog loads and its template is materialized, we
    // walk the visual tree, find CommandSpace by x:Name, and clear its Background —
    // then the outer BackgroundElement's continuous gradient shows through.
    //
    // Also overrides AccentButton brushes so the primary "Сохранить" button reads
    // as brand-consistent instead of the stock lavender accent.
    public static void ApplyToDialog(ContentDialog dialog, AppTheme theme)
    {
        dialog.RequestedTheme = theme == AppTheme.Light ? ElementTheme.Light : ElementTheme.Dark;

        // Dialogs live on the XamlRoot popup surface, which does not inherit FontFamily
        // from RootGrid. The Application-level ContentControlThemeFontFamily override
        // usually reaches here through the ContentDialog's template, but templates in
        // some builds copy the default at compile-time; setting FontFamily directly
        // guarantees Ubuntu regardless.
        if (Application.Current.Resources["AppFontFamily"] is FontFamily appFont)
            dialog.FontFamily = appFont;

        if (theme != AppTheme.Brand)
            return;

        var transparent   = new SolidColorBrush(Color.FromArgb(0x00, 0, 0, 0));
        var white         = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        var brandPrimary  = new SolidColorBrush(BrandTop);                              // #E52E71
        var brandPrimaryH = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x4A, 0x8C)); // lighter on hover
        var brandPrimaryP = new SolidColorBrush(Color.FromArgb(0xFF, 0xB0, 0x1F, 0x55)); // darker on press

        dialog.Background = BrandGradient();
        dialog.Foreground = white;
        dialog.Resources["ContentDialogForeground"]  = white;
        dialog.Resources["ContentDialogBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

        // Primary button ("Сохранить" / "Удалить" / "Добавить") — brand pink, white text.
        dialog.Resources["AccentButtonBackground"]              = brandPrimary;
        dialog.Resources["AccentButtonBackgroundPointerOver"]   = brandPrimaryH;
        dialog.Resources["AccentButtonBackgroundPressed"]       = brandPrimaryP;
        dialog.Resources["AccentButtonForeground"]              = white;
        dialog.Resources["AccentButtonForegroundPointerOver"]   = white;
        dialog.Resources["AccentButtonForegroundPressed"]       = white;
        dialog.Resources["AccentButtonBorderBrush"]             = transparent;

        // Input surfaces — TextBox / NumberBox / ComboBox default backgrounds come from
        // the Dark theme's dark-grey surface. A translucent WHITE overlay tints
        // differently depending on WHERE the input sits on the gradient (pink vs
        // orange), so the three fields look mismatched. Using a translucent BLACK
        // overlay uniformly darkens whatever gradient color is underneath, so all
        // three fields read as one visual family. Border stays constant (no hover-
        // brightening) so all three behave identically across states.
        var inputBg      = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0));
        var inputBgHover = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));
        var inputBgFocus = new SolidColorBrush(Color.FromArgb(0x77, 0, 0, 0));
        var inputBorder  = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

        // TextBox + NumberBox (inner input) — TextControl* keys.
        dialog.Resources["TextControlBackground"]              = inputBg;
        dialog.Resources["TextControlBackgroundPointerOver"]   = inputBgHover;
        dialog.Resources["TextControlBackgroundFocused"]       = inputBgFocus;
        dialog.Resources["TextControlBackgroundDisabled"]      = inputBg;
        dialog.Resources["TextControlForeground"]              = white;
        dialog.Resources["TextControlForegroundPointerOver"]   = white;
        dialog.Resources["TextControlForegroundFocused"]       = white;
        dialog.Resources["TextControlForegroundDisabled"]      = white;
        dialog.Resources["TextControlBorderBrush"]             = inputBorder;
        dialog.Resources["TextControlBorderBrushPointerOver"]  = inputBorder;
        dialog.Resources["TextControlBorderBrushFocused"]      = inputBorder;
        dialog.Resources["TextControlBorderBrushDisabled"]     = inputBorder;

        // ComboBox — separate resource family from TextBox.
        dialog.Resources["ComboBoxBackground"]                 = inputBg;
        dialog.Resources["ComboBoxBackgroundPointerOver"]      = inputBgHover;
        dialog.Resources["ComboBoxBackgroundPressed"]          = inputBgFocus;
        dialog.Resources["ComboBoxBackgroundFocused"]          = inputBgFocus;
        dialog.Resources["ComboBoxBackgroundUnfocused"]        = inputBg;
        dialog.Resources["ComboBoxBackgroundDisabled"]         = inputBg;
        dialog.Resources["ComboBoxForeground"]                 = white;
        dialog.Resources["ComboBoxForegroundPointerOver"]      = white;
        dialog.Resources["ComboBoxForegroundPressed"]          = white;
        dialog.Resources["ComboBoxForegroundFocused"]          = white;
        dialog.Resources["ComboBoxForegroundDisabled"]         = white;
        dialog.Resources["ComboBoxBorderBrush"]                = inputBorder;
        dialog.Resources["ComboBoxBorderBrushPointerOver"]     = inputBorder;
        dialog.Resources["ComboBoxBorderBrushPressed"]         = inputBorder;
        dialog.Resources["ComboBoxBorderBrushFocused"]         = inputBorder;
        dialog.Resources["ComboBoxBorderBrushUnfocused"]       = inputBorder;
        dialog.Resources["ComboBoxBorderBrushDisabled"]        = inputBorder;

        // ComboBox open dropdown (Popup on XamlRoot's overlay surface). Same brand
        // gradient as the dialog so the flyout reads as an extension of the surface,
        // not a stray dark WinUI panel. Item backgrounds are subtle white overlays
        // over the gradient for hover / selected states.
        var itemHover    = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        var itemPressed  = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        var itemSelected = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

        dialog.Resources["ComboBoxDropDownBackground"]                   = BrandGradient();
        dialog.Resources["ComboBoxDropDownBackgroundPointerOver"]        = BrandGradient();
        dialog.Resources["ComboBoxDropDownBackgroundPointerPressed"]     = BrandGradient();
        dialog.Resources["ComboBoxDropDownForeground"]                   = white;
        dialog.Resources["ComboBoxDropDownBorderBrush"]                  = inputBorder;

        dialog.Resources["ComboBoxItemBackground"]                       = transparent;
        dialog.Resources["ComboBoxItemBackgroundPointerOver"]            = itemHover;
        dialog.Resources["ComboBoxItemBackgroundPressed"]                = itemPressed;
        dialog.Resources["ComboBoxItemBackgroundSelected"]               = itemSelected;
        dialog.Resources["ComboBoxItemBackgroundSelectedUnfocused"]      = itemSelected;
        dialog.Resources["ComboBoxItemBackgroundSelectedPointerOver"]    = itemHover;
        dialog.Resources["ComboBoxItemBackgroundSelectedPressed"]        = itemPressed;
        dialog.Resources["ComboBoxItemForeground"]                       = white;
        dialog.Resources["ComboBoxItemForegroundPointerOver"]            = white;
        dialog.Resources["ComboBoxItemForegroundPressed"]                = white;
        dialog.Resources["ComboBoxItemForegroundSelected"]               = white;
        dialog.Resources["ComboBoxItemForegroundSelectedUnfocused"]      = white;
        dialog.Resources["ComboBoxItemForegroundSelectedPointerOver"]    = white;
        dialog.Resources["ComboBoxItemForegroundSelectedPressed"]        = white;
        dialog.Resources["ComboBoxItemForegroundDisabled"]               = white;

        // Kill CommandSpace's background AFTER the template applies. Loaded fires once
        // per Show, so re-hook every ApplyToDialog call — safe: subscriptions leak with
        // the dialog itself, which is discarded after ShowAsync completes.
        dialog.Loaded += (_, _) =>
        {
            var cs = FindDescendantByName(dialog, "CommandSpace");
            if (cs is Panel csPanel) csPanel.Background = transparent;
        };
    }

    private static FrameworkElement? FindDescendantByName(DependencyObject root, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name)
                return fe;
            var deep = FindDescendantByName(child, name);
            if (deep is not null)
                return deep;
        }
        return null;
    }

    // Exposed so ContentDialogs (which live in the XamlRoot popup layer, not inside
    // RootGrid) can be themed explicitly — element-theme doesn't inherit across
    // that boundary. Kept as the AppTheme so dialogs can pick brand-specific overrides.
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Brand;

    public static void Apply(FrameworkElement root, AppTheme theme)
    {
        // Palette first — new TorrentViewModels created after this point will
        // read the right brushes on construction. Existing rows are refreshed
        // by the caller (MainWindow after settings-save).
        RowBrushes.Set(theme);

        CurrentTheme = theme;

        if (root is not Panel panel)
            return;

        switch (theme)
        {
            case AppTheme.Dark:
                panel.Background = new SolidColorBrush(DarkSolid);
                root.RequestedTheme = ElementTheme.Dark;
                break;
            case AppTheme.Light:
                panel.Background = new SolidColorBrush(LightSolid);
                root.RequestedTheme = ElementTheme.Light;
                break;
            default:
                panel.Background = BrandGradient();
                root.RequestedTheme = ElementTheme.Dark;
                break;
        }

        // Context-menu (MenuFlyout) brushes — the row right-click flyout renders on
        // XamlRoot's popup surface, so ThemeResource lookup from the MenuFlyoutPresenter
        // does NOT traverse back through RootGrid. Application.Current.Resources is at
        // the root of every lookup chain, so put overrides there.
        if (theme == AppTheme.Brand)
            ApplyBrandMenuFlyoutBrushes();
        else
            ClearBrandMenuFlyoutBrushes();
    }

    private static readonly string[] BrandMenuFlyoutKeys = new[]
    {
        "MenuFlyoutPresenterBackground",
        "MenuFlyoutPresenterBorderBrush",
        "MenuFlyoutItemForeground",
        "MenuFlyoutItemForegroundPointerOver",
        "MenuFlyoutItemForegroundPressed",
        "MenuFlyoutItemBackground",
        "MenuFlyoutItemBackgroundPointerOver",
        "MenuFlyoutItemBackgroundPressed",
        "MenuFlyoutSeparatorBackground",
    };

    private static void ApplyBrandMenuFlyoutBrushes()
    {
        var res = Application.Current.Resources;

        var white       = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        var transparent = new SolidColorBrush(Color.FromArgb(0x00, 0, 0, 0));
        var hover       = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        var pressed     = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        var border      = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        var separator   = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

        res["MenuFlyoutPresenterBackground"]       = BrandGradient();
        res["MenuFlyoutPresenterBorderBrush"]      = border;
        res["MenuFlyoutItemForeground"]            = white;
        res["MenuFlyoutItemForegroundPointerOver"] = white;
        res["MenuFlyoutItemForegroundPressed"]     = white;
        res["MenuFlyoutItemBackground"]            = transparent;
        res["MenuFlyoutItemBackgroundPointerOver"] = hover;
        res["MenuFlyoutItemBackgroundPressed"]     = pressed;
        res["MenuFlyoutSeparatorBackground"]       = separator;
    }

    private static void ClearBrandMenuFlyoutBrushes()
    {
        var res = Application.Current.Resources;
        foreach (var key in BrandMenuFlyoutKeys)
            res.Remove(key);
    }

    /// <summary>
    /// Applies caption-button (minimize / maximize / close) colors that match the
    /// active theme. Windows would otherwise dim inactive-state icons to grey, which
    /// reads as "out of style" on Brand and Dark themes (icons blend into pink/dark
    /// background) and inversely, active white-on-white icons vanish on Light theme.
    /// Same foreground for active + inactive keeps them consistently readable.
    /// </summary>
    public static void ApplyToTitleBar(Microsoft.UI.Windowing.AppWindowTitleBar titleBar, AppTheme theme)
    {
        var transparent = (Color?)Color.FromArgb(0, 0, 0, 0);

        if (theme == AppTheme.Light)
        {
            var dark      = Color.FromArgb(0xFF, 0x21, 0x21, 0x21);
            var hoverBg   = Color.FromArgb(0x15, 0x00, 0x00, 0x00);
            var pressedBg = Color.FromArgb(0x25, 0x00, 0x00, 0x00);

            titleBar.ButtonForegroundColor            = dark;
            titleBar.ButtonInactiveForegroundColor    = dark;
            titleBar.ButtonHoverForegroundColor       = dark;
            titleBar.ButtonPressedForegroundColor     = dark;
            titleBar.ButtonBackgroundColor            = transparent;
            titleBar.ButtonInactiveBackgroundColor    = transparent;
            titleBar.ButtonHoverBackgroundColor       = hoverBg;
            titleBar.ButtonPressedBackgroundColor     = pressedBg;
        }
        else
        {
            var white     = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            var hoverBg   = Color.FromArgb(0x25, 0xFF, 0xFF, 0xFF);
            var pressedBg = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);

            titleBar.ButtonForegroundColor            = white;
            titleBar.ButtonInactiveForegroundColor    = white;
            titleBar.ButtonHoverForegroundColor       = white;
            titleBar.ButtonPressedForegroundColor     = white;
            titleBar.ButtonBackgroundColor            = transparent;
            titleBar.ButtonInactiveBackgroundColor    = transparent;
            titleBar.ButtonHoverBackgroundColor       = hoverBg;
            titleBar.ButtonPressedBackgroundColor     = pressedBg;
        }
    }
}
