namespace UsurperRemake.Systems;

/// <summary>
/// v1.1.15 Mental UI helpers. MentalSystem itself stays pure (no printing); this class carries
/// the small amount of player-facing text logic that sits on top of it: the empty-at-Stable tag
/// used in headers and at combat start, and the band-change announcement with its first-time
/// hint. Nothing here changes Mental; callers run MentalSystem.Change (or AddStrain) first and
/// pass the before value in.
/// </summary>
public static class MentalUi
{
    /// <summary>
    /// Band label and color for a compact tag (dungeon header, combat start): empty at Stable,
    /// same label and color as Character.GetMentalTier() otherwise. Deliberately has no
    /// online-mode check; Mental runs in both modes so the tag shows in both.
    /// </summary>
    public static (string label, string color) GetMentalTag(Character? player)
    {
        if (player == null) return ("", "");
        return MentalSystem.GetBand(player.Mental) == MentalBand.Stable ? ("", "") : player.GetMentalTier();
    }

    /// <summary>
    /// A recovery source's gain: when applied is above 0 prints one short line with the amount
    /// ("mental.gain") and then the band announcement. applied 0 (at the cap, or the source was
    /// already used today) prints nothing. Skips a null terminal or player and NPCs.
    /// </summary>
    public static void ReportGain(TerminalEmulator terminal, Character player, int mentalBefore, int applied)
    {
        if (terminal == null || player == null || player.IsNPC || applied <= 0) return;
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("mental.gain", applied));
        AnnounceMentalChange(terminal, player, mentalBefore);
    }

    /// <summary>
    /// Compares mentalBefore's band to the player's current Mental band and, if it changed, prints
    /// a band-change line (worse when Mental moved toward Broken, better when it moved toward
    /// Stable). On the first time Mental is below Stable (MentalHintShown false) also prints a
    /// one-time hint explaining Mental and how to recover, then sets MentalHintShown. Skips NPCs.
    /// This has no callers yet in this piece; later pieces call it after every Mental-changing
    /// event (dungeon strain, grief, drugs, recovery sources).
    /// </summary>
    public static void AnnounceMentalChange(TerminalEmulator terminal, Character player, int mentalBefore)
    {
        if (terminal == null || player == null || player.IsNPC) return;

        var bandBefore = MentalSystem.GetBand(mentalBefore);
        var bandAfter = MentalSystem.GetBand(player.Mental);

        if (bandAfter != bandBefore)
        {
            var (label, color) = player.GetMentalTier();
            terminal.SetColor(color);
            terminal.WriteLine((int)bandAfter > (int)bandBefore
                ? Loc.Get("mental.band_worse", label)
                : Loc.Get("mental.band_better", label));
        }

        if (!player.MentalHintShown && (int)bandAfter > (int)MentalBand.Stable)
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("mental.first_hint"));
            player.MentalHintShown = true;
        }
    }
}
