using System;
using System.Threading.Tasks;
using UsurperRemake.UI;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v1.1.12: the AWAKENING screen shown when the stage rises. The rise is queued on the session's
    /// OceanPhilosophySystem and shown at the next safe point, the top of a location's loop, so never
    /// mid-fight or mid-dialogue; a fight returns to that loop when it ends.
    /// </summary>
    public static class AwakeningScreens
    {
        /// <summary>The stage name, the same labels as the status screen.</summary>
        public static string StageName(int stage) => stage switch
        {
            1 => Loc.Get("base.awakening_stirring"),
            2 => Loc.Get("base.awakening_aware"),
            3 => Loc.Get("base.awakening_seeking"),
            4 => Loc.Get("base.awakening_illuminated"),
            5 => Loc.Get("base.awakening_transcendent"),
            6 => Loc.Get("base.awakening_enlightened"),
            7 => Loc.Get("base.awakening_awakened"),
            _ => Loc.Get("base.awakening_dormant")
        };

        public const int LoreLinesPerStage = 4;

        /// <summary>Show the pending announcement, once. Returns false when nothing was pending.</summary>
        public static async Task<bool> ShowPending(TerminalEmulator term, Character? player)
        {
            var pending = OceanPhilosophySystem.Instance?.TakePendingAnnouncement();
            if (pending == null || term == null) return false;
            var (from, to) = pending.Value;

            term.ClearScreen();
            term.WriteLine("");
            UIHelper.WriteBoxHeader(term, Loc.Get("ocean.awakening_header"), "bright_cyan", 64);
            term.WriteLine("");
            term.WriteLine($"  {Loc.Get("ocean.awakening_stage", to, OceanPhilosophySystem.MaxStage, StageName(to))}", "bright_white");
            term.WriteLine("");

            for (int i = 1; i <= LoreLinesPerStage; i++)
                term.WriteLine($"  {Loc.Get($"ocean.stage.{to}.lore.{i}")}", "cyan");
            term.WriteLine("");

            for (int stage = Math.Max(1, from + 1); stage <= to; stage++)
            {
                term.WriteLine($"  {Loc.Get("ocean.awakening_gained", AwakeningBonus.GainedAt(stage))}", "bright_green");
                term.WriteLine($"  {Loc.Get("ocean.awakening_opens", Loc.Get($"ocean.stage.{stage}.opens"))}", "yellow");
            }
            term.WriteLine("");

            await term.PressAnyKey();

            if (player != null)
                HintSystem.Instance.TryShowHint(HintSystem.HINT_AWAKENING, term, player.HintsShown);
            return true;
        }
    }
}
