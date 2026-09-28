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
}
