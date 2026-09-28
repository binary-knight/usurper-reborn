namespace UsurperRemake.Systems;

/// <summary>
/// 1.2.0 Temple gods piece 3: the player-facing lines for Favor changes. FavorSystem stays pure
/// (no printing); callers change Favor first and pass what was applied.
/// </summary>
public static class FavorUi
{
    /// <summary>
    /// A Favor gain: when applied is above 0 prints one short line naming the god, the gain and the
    /// Favor now ("favor.gain"). applied 0 (no god, or the source's daily cap is spent) prints
    /// nothing. Skips a null terminal or player and NPCs.
    /// </summary>
    public static void ReportGain(TerminalEmulator? terminal, Character? player, int applied, GodSystem? gods = null)
    {
        if (terminal == null || player == null || player.IsNPC || applied <= 0) return;
        string god = GodRegistry.GetWorshippedGod(player, gods)?.Name ?? player.GodFavorGod;
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("favor.gain", god, applied, FavorSystem.GetFavor(player, gods)));
    }

    /// <summary>
    /// Temple gods piece 3: a Favor loss (a taboo). When applied is below 0 prints one short line
    /// naming the god, the loss and the Favor now ("favor.loss"). 0 or above prints nothing. Skips a
    /// null terminal or player and NPCs.
    /// </summary>
    public static void ReportLoss(TerminalEmulator? terminal, Character? player, int applied, GodSystem? gods = null)
    {
        if (terminal == null || player == null || player.IsNPC || applied >= 0) return;
        string god = GodRegistry.GetWorshippedGod(player, gods)?.Name ?? player.GodFavorGod;
        terminal.SetColor("dark_red");
        terminal.WriteLine(Loc.Get("favor.loss", god, -applied, FavorSystem.GetFavor(player, gods)));
    }

    /// <summary>A Favor change of either sign: ReportGain above 0, ReportLoss below 0, nothing at 0.</summary>
    public static void ReportChange(TerminalEmulator? terminal, Character? player, int applied, GodSystem? gods = null)
    {
        if (applied > 0) ReportGain(terminal, player, applied, gods);
        else if (applied < 0) ReportLoss(terminal, player, applied, gods);
    }
}
