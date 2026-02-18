using System.Collections.Generic;

/// <summary>
/// Player-selectable color themes. Remaps game color names before ANSI/Console output.
/// All output flows through ColorTheme.Resolve() in the 3 color resolution points:
/// TerminalEmulator.GetAnsiColorCode(), TerminalEmulator.ColorNameToConsole(), BBSTerminalAdapter.GetAnsiColorCode()
/// </summary>
public enum ColorThemeType
{
    Default,       // Current colors (bright, modern)
    ClassicDark,   // Muted colors, original Usurper feel
    AmberRetro,    // Monochrome amber CRT
    GreenPhosphor, // Monochrome green CRT
    HighContrast   // Maximum brightness/readability
}

public static class ColorTheme
{
    /// <summary>
    /// The currently active color theme. Set from player preferences on load.
    /// In MUD mode this is per-session via ThreadStatic to avoid cross-player bleed.
    /// </summary>
    [ThreadStatic] private static ColorThemeType? _currentThread;
    private static ColorThemeType _currentGlobal = ColorThemeType.Default;

    public static ColorThemeType Current
    {
        get => _currentThread ?? _currentGlobal;
        set
        {
            if (UsurperRemake.Server.SessionContext.IsActive)
                _currentThread = value;
            else
                _currentGlobal = value;
        }
    }

    // Classic Dark — authentic 1993 Usurper palette derived from the original Pascal source.
    // The original used ~5 colors per screen:
    //   dim_green   (ANSI 2;32)  — body text, descriptions, menus (~60% of all text)
    //   dark_green  (ANSI 32)    — secondary text, slightly brighter than dim
    //   bright_green (ANSI 1;32) — titles, player/NPC names, location headers
    //   dark_magenta (ANSI 35)  — hotkeys, column headers, separator lines, UI chrome
    //   yellow (ANSI 33)        — warnings, notices, gold amounts
    //   dark_red (ANSI 31)      — danger, damage, death
    // dim_green uses SGR 2 (faint/dim attribute) for a darker green than standard ANSI 32.
    // Terminals that don't support SGR 2 gracefully fall back to normal green.
    private static readonly Dictionary<string, string> ClassicDarkMap = new()
    {
        // Body text → dim_green (darker than standard green via SGR 2 faint attribute)
        { "white", "dim_green" },
        { "green", "dim_green" },
        { "gray", "dim_green" },
        { "grey", "dim_green" },
        { "dark_gray", "dim_green" },

        // Emphasized text → bright_green (titles, names, highlights)
        { "bright_white", "bright_green" },

        // Menu hotkeys & UI chrome → dark_magenta (the original's prominent accent color)
        { "bright_yellow", "dark_magenta" },
        // yellow stays as-is (warnings/notices remain visible)
        // dark_yellow stays as-is

        // UI chrome → dark_magenta (headers, separators, hotkeys — very prominent in original)
        { "cyan", "dark_magenta" },
        { "bright_cyan", "dark_magenta" },
        { "dark_cyan", "dim_green" },

        // Blues → dim_green (unused in original, collapse to body text)
        { "blue", "dim_green" },
        { "bright_blue", "dim_green" },
        { "dark_blue", "dim_green" },

        // Danger → dark_red
        { "red", "dark_red" },
        { "bright_red", "dark_red" },
        // dark_red stays as-is

        // Accents → dark_magenta
        { "magenta", "dark_magenta" },
        { "bright_magenta", "dark_magenta" }
        // dark_magenta stays as-is

        // bright_green stays as-is (NOT remapped — names, titles)
        // dark_green stays as-is (used for secondary text that should be slightly brighter than dim)
    };

    // Amber Retro — monochrome amber phosphor CRT
    private static readonly Dictionary<string, string> AmberRetroMap = new()
    {
        { "bright_white", "bright_yellow" },
        { "white", "yellow" },
        { "bright_green", "yellow" },
        { "green", "dark_yellow" },
        { "bright_red", "bright_yellow" },
        { "red", "yellow" },
        { "bright_cyan", "bright_yellow" },
        { "cyan", "yellow" },
        { "bright_blue", "dark_yellow" },
        { "blue", "dark_yellow" },
        { "bright_magenta", "yellow" },
        { "magenta", "dark_yellow" },
        { "gray", "dark_yellow" },
        { "grey", "dark_yellow" },
        { "dark_gray", "dark_yellow" },
        { "dark_red", "dark_yellow" },
        { "dark_green", "dark_yellow" },
        { "dark_blue", "dark_yellow" },
        { "dark_cyan", "dark_yellow" },
        { "dark_magenta", "dark_yellow" }
    };

    // Green Phosphor — monochrome green CRT
    private static readonly Dictionary<string, string> GreenPhosphorMap = new()
    {
        { "bright_white", "bright_green" },
        { "white", "green" },
        { "bright_yellow", "bright_green" },
        { "yellow", "green" },
        { "bright_red", "bright_green" },
        { "red", "green" },
        { "bright_cyan", "bright_green" },
        { "cyan", "green" },
        { "bright_blue", "dark_green" },
        { "blue", "dark_green" },
        { "bright_magenta", "green" },
        { "magenta", "dark_green" },
        { "gray", "dark_green" },
        { "grey", "dark_green" },
        { "dark_gray", "dark_green" },
        { "dark_red", "dark_green" },
        { "dark_yellow", "dark_green" },
        { "dark_blue", "dark_green" },
        { "dark_cyan", "dark_green" },
        { "dark_magenta", "dark_green" }
    };

    // High Contrast — maximum brightness for readability
    private static readonly Dictionary<string, string> HighContrastMap = new()
    {
        { "white", "bright_white" },
        { "gray", "white" },
        { "grey", "white" },
        { "dark_gray", "white" },
        { "yellow", "bright_yellow" },
        { "green", "bright_green" },
        { "cyan", "bright_cyan" },
        { "red", "bright_red" },
        { "blue", "bright_blue" },
        { "magenta", "bright_magenta" },
        { "dark_yellow", "yellow" },
        { "dark_green", "green" },
        { "dark_cyan", "cyan" },
        { "dark_red", "red" },
        { "dark_blue", "blue" },
        { "dark_magenta", "magenta" }
    };

    /// <summary>
    /// Resolve a color name through the current theme's remapping.
    /// Returns the input unchanged if no theme is active or if the color has no mapping.
    /// </summary>
    public static string Resolve(string color)
    {
        if (Current == ColorThemeType.Default || string.IsNullOrEmpty(color))
            return color;

        var map = Current switch
        {
            ColorThemeType.ClassicDark => ClassicDarkMap,
            ColorThemeType.AmberRetro => AmberRetroMap,
            ColorThemeType.GreenPhosphor => GreenPhosphorMap,
            ColorThemeType.HighContrast => HighContrastMap,
            _ => null
        };

        if (map != null && map.TryGetValue(color.ToLower(), out var mapped))
            return mapped;

        return color;
    }

    /// <summary>Get the display name for a theme.</summary>
    public static string GetThemeName(ColorThemeType theme) => theme switch
    {
        ColorThemeType.Default => "Default",
        ColorThemeType.ClassicDark => "Classic Dark",
        ColorThemeType.AmberRetro => "Amber Retro",
        ColorThemeType.GreenPhosphor => "Green Phosphor",
        ColorThemeType.HighContrast => "High Contrast",
        _ => "Unknown"
    };

    /// <summary>Get a short description for a theme.</summary>
    public static string GetThemeDescription(ColorThemeType theme) => theme switch
    {
        ColorThemeType.Default => "Bright, modern colors",
        ColorThemeType.ClassicDark => "Muted colors, original Usurper feel",
        ColorThemeType.AmberRetro => "Monochrome amber CRT",
        ColorThemeType.GreenPhosphor => "Monochrome green CRT",
        ColorThemeType.HighContrast => "Maximum brightness for readability",
        _ => ""
    };

    /// <summary>Cycle to the next theme in order.</summary>
    public static ColorThemeType NextTheme(ColorThemeType current) => current switch
    {
        ColorThemeType.Default => ColorThemeType.ClassicDark,
        ColorThemeType.ClassicDark => ColorThemeType.AmberRetro,
        ColorThemeType.AmberRetro => ColorThemeType.GreenPhosphor,
        ColorThemeType.GreenPhosphor => ColorThemeType.HighContrast,
        ColorThemeType.HighContrast => ColorThemeType.Default,
        _ => ColorThemeType.Default
    };
}
