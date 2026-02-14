using System;

namespace UsurperRemake.Server;

/// <summary>
/// Wizard hierarchy levels for MUD administration.
/// Classic DIKU/Merc/ROM-inspired tier system.
/// Each tier inherits all powers of lower tiers.
/// </summary>
public enum WizardLevel
{
    /// <summary>Normal player. No special powers.</summary>
    Mortal = 0,

    /// <summary>Trusted helper. Can inspect, see wiznet, /where.</summary>
    Builder = 1,

    /// <summary>Cannot die. Teleport, godmode, heal, invisibility.</summary>
    Immortal = 2,

    /// <summary>Player manipulation: summon, snoop, force, set, slay, freeze, mute.</summary>
    Wizard = 3,

    /// <summary>Ban/unban, kick, promote (up to Wizard), broadcast.</summary>
    Archwizard = 4,

    /// <summary>Shutdown, reboot, full admin console, promote (up to Archwizard).</summary>
    God = 5,

    /// <summary>Supreme authority. ONLY "Rage". Hardcoded. Cannot be demoted.</summary>
    Implementor = 6
}

/// <summary>
/// Constants and utilities for the wizard system.
/// </summary>
public static class WizardConstants
{
    /// <summary>The username that is always auto-promoted to Implementor.</summary>
    public const string IMPLEMENTOR_USERNAME = "rage";

    /// <summary>Get the display title for a wizard level.</summary>
    public static string GetTitle(WizardLevel level) => level switch
    {
        WizardLevel.Builder => "Builder",
        WizardLevel.Immortal => "Immortal",
        WizardLevel.Wizard => "Wizard",
        WizardLevel.Archwizard => "Archwizard",
        WizardLevel.God => "God",
        WizardLevel.Implementor => "Implementor",
        _ => "Mortal"
    };

    /// <summary>Get the ANSI color name for a wizard level (used by TerminalEmulator).</summary>
    public static string GetColor(WizardLevel level) => level switch
    {
        WizardLevel.Builder => "cyan",
        WizardLevel.Immortal => "bright_cyan",
        WizardLevel.Wizard => "bright_yellow",
        WizardLevel.Archwizard => "bright_magenta",
        WizardLevel.God => "bright_red",
        WizardLevel.Implementor => "bright_white",
        _ => "white"
    };

    /// <summary>Get the ANSI escape code for a wizard level (used for raw ANSI output).</summary>
    public static string GetAnsiColor(WizardLevel level) => level switch
    {
        WizardLevel.Builder => "\u001b[36m",        // cyan
        WizardLevel.Immortal => "\u001b[1;36m",     // bright cyan
        WizardLevel.Wizard => "\u001b[1;33m",       // bright yellow
        WizardLevel.Archwizard => "\u001b[1;35m",   // bright magenta
        WizardLevel.God => "\u001b[1;31m",           // bright red
        WizardLevel.Implementor => "\u001b[1;37m",  // bright white
        _ => "\u001b[0;37m"                          // white
    };
}
