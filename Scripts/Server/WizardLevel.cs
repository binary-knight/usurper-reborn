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
    /// <summary>
    /// The account that is auto-promoted to Implementor on login. Defaults to
    /// the canonical server's owner; self-hosters override it with the
    /// USURPER_IMPLEMENTOR env var so their deploy's superuser is an account
    /// THEY control.
    ///
    /// v0.65.14 (security audit F1): auto-promotion is self-escalating and
    /// Implementor can never be demoted, so whoever holds this name owns the
    /// server. On a fresh deploy the name was unregistered, meaning anyone who
    /// registered it first became superuser. Registration now REFUSES this
    /// name (see SqlSaveBackend.IsReservedUsername), so the account can only
    /// come into existence through the operator's own provisioning.
    /// </summary>
    public static string ImplementorUsername
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("USURPER_IMPLEMENTOR");
            return string.IsNullOrWhiteSpace(env) ? DefaultImplementorUsername : env.Trim().ToLowerInvariant();
        }
    }

    /// <summary>Fallback when USURPER_IMPLEMENTOR is not configured.</summary>
    public const string DefaultImplementorUsername = "rage";

    /// <summary>Back-compat alias for the pre-v0.65.14 constant name.</summary>
    public static string IMPLEMENTOR_USERNAME => ImplementorUsername;

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
