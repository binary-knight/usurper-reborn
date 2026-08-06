using System.Collections.Generic;
using System.Threading;

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

/// <summary>
/// Semantic color ROLES. v1.0.2.
///
/// Player report: "Text color in general is just inconsistent as all get-out.
/// Please stop using darkgray for important readouts... use it for things that
/// are irrelevant to the flow of the game, like stat rolls." Correct, and the
/// cause is structural: call sites named a COLOR, so every author guessed, and
/// the same kind of message ended up in different colors all over the codebase.
///
/// A theme could never fix that. ColorTheme.Resolve maps a color name to
/// another color name, which is one level BELOW meaning -- it restyles a slot,
/// it cannot move a message into a different slot. Worse, ClassicDark collapses
/// white / gray / dark_gray / green all into dim_green, so under it an urgent
/// readout and an ignorable stat roll render identically.
///
/// Naming the ROLE instead fixes both problems at once: the call site declares
/// how important the line is, and each theme decides independently what that
/// importance looks like in its own palette. Every theme stays coherent for
/// free, including the monochrome ones.
///
/// Use these instead of literal color names for anything a player reads during
/// play. Literal names still work everywhere, so migration is incremental.
/// </summary>
public static class ColorRole
{
    /// <summary>You are in danger or just lost something: damage taken, death, a debuff landing.</summary>
    public const string Critical = "role_critical";

    /// <summary>Something went your way: healing, a resist, a buff gained, victory.</summary>
    public const string Success = "role_success";

    /// <summary>Something you interact with: hotkeys, prompts, menu choices.</summary>
    public const string Action = "role_action";

    /// <summary>Worth reading but not urgent: a buff expiring, a warning, a state change.</summary>
    public const string Notice = "role_notice";

    /// <summary>Ordinary body text and flavor.</summary>
    public const string Narration = "role_narration";

    /// <summary>
    /// Safe to skip entirely: stat rolls, damage-vs-defense breakdowns, chrome.
    /// This is the ONLY role that should ever be dim. If a player needs to act
    /// on it, it is not Derived.
    /// </summary>
    public const string Derived = "role_derived";

    /// <summary>
    /// An option that exists but cannot be chosen right now: an ability on
    /// cooldown, a purchase you cannot afford. Visually dim like Derived, but a
    /// separate name because the meaning differs -- Derived is information you
    /// may ignore, Disabled is a choice being withheld.
    /// </summary>
    public const string Disabled = "role_disabled";
}

public static class ColorTheme
{
    /// <summary>
    /// The currently active color theme. Set from player preferences on load.
    /// In MUD mode this is per-session via SessionContext (a shared reference object)
    /// to avoid cross-player bleed. AsyncLocal was previously used but has copy-on-write
    /// semantics for value types — theme changes in child async scopes didn't flow back
    /// to the parent, causing the theme to revert after leaving preferences.
    /// </summary>
    private static ColorThemeType _currentGlobal = ColorThemeType.Default;

    public static ColorThemeType Current
    {
        get
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            return ctx != null ? ctx.ColorTheme : _currentGlobal;
        }
        set
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null)
                ctx.ColorTheme = value;
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
    /// <summary>
    /// Role to concrete color, per theme. Each theme answers the same six
    /// questions in its own palette, which is what keeps every theme readable
    /// without any call site knowing which theme is active.
    ///
    /// The monochrome themes only have three brightness tiers, so some roles
    /// necessarily share one. The collapse is chosen deliberately: everything
    /// actionable stays loud and Derived alone drops to the dim tier, because
    /// "can I ignore this line" is the distinction that actually matters.
    /// </summary>
    private static readonly Dictionary<ColorThemeType, Dictionary<string, string>> RoleMaps = new()
    {
        [ColorThemeType.Default] = new()
        {
            [ColorRole.Critical]  = "bright_red",
            [ColorRole.Success]   = "bright_green",
            [ColorRole.Action]    = "bright_yellow",
            [ColorRole.Notice]    = "yellow",
            [ColorRole.Narration] = "white",
            [ColorRole.Derived]   = "dark_gray",
            [ColorRole.Disabled]  = "dark_gray",
        },
        // Faithful to the original's narrow palette, but the roles stay
        // distinct -- which plain ClassicDark could not manage, since it
        // collapses white/gray/dark_gray/green into a single dim_green.
        [ColorThemeType.ClassicDark] = new()
        {
            [ColorRole.Critical]  = "bright_red",
            [ColorRole.Success]   = "bright_green",
            [ColorRole.Action]    = "dark_magenta",
            [ColorRole.Notice]    = "yellow",
            [ColorRole.Narration] = "dim_green",
            [ColorRole.Derived]   = "dark_gray",
            [ColorRole.Disabled]  = "dark_gray",
        },
        [ColorThemeType.AmberRetro] = new()
        {
            [ColorRole.Critical]  = "bright_yellow",
            [ColorRole.Success]   = "bright_yellow",
            [ColorRole.Action]    = "bright_yellow",
            [ColorRole.Notice]    = "yellow",
            [ColorRole.Narration] = "yellow",
            [ColorRole.Derived]   = "dark_yellow",
            [ColorRole.Disabled]  = "dark_yellow",
        },
        [ColorThemeType.GreenPhosphor] = new()
        {
            [ColorRole.Critical]  = "bright_green",
            [ColorRole.Success]   = "bright_green",
            [ColorRole.Action]    = "bright_green",
            [ColorRole.Notice]    = "green",
            [ColorRole.Narration] = "green",
            [ColorRole.Derived]   = "dark_green",
            [ColorRole.Disabled]  = "dark_green",
        },
        // Even at maximum contrast Derived stays a step down, or the hierarchy
        // flattens and nothing stands out at all.
        [ColorThemeType.HighContrast] = new()
        {
            [ColorRole.Critical]  = "bright_red",
            [ColorRole.Success]   = "bright_green",
            [ColorRole.Action]    = "bright_yellow",
            [ColorRole.Notice]    = "bright_yellow",
            [ColorRole.Narration] = "bright_white",
            [ColorRole.Derived]   = "white",
            [ColorRole.Disabled]  = "white",
        },
    };

    /// <summary>True when the name is a semantic role rather than a literal color.</summary>
    public static bool IsRole(string color) =>
        !string.IsNullOrEmpty(color) && color.StartsWith("role_", System.StringComparison.Ordinal);

    public static string Resolve(string color)
    {
        if (string.IsNullOrEmpty(color)) return color;

        // Roles resolve straight to a final color for the active theme and are
        // deliberately NOT passed through the literal remap below -- that map
        // collapses several colors together, which is exactly what would undo
        // the distinction the role is there to guarantee.
        if (IsRole(color))
        {
            var roles = RoleMaps.TryGetValue(Current, out var m) ? m : RoleMaps[ColorThemeType.Default];
            if (roles.TryGetValue(color, out var byRole)) return byRole;
            if (RoleMaps[ColorThemeType.Default].TryGetValue(color, out var byDefault)) return byDefault;
            return "white"; // unknown role: legible beats invisible
        }

        if (Current == ColorThemeType.Default)
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
