using System;
using System.Collections.Generic;
using System.Linq;
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

        public static string FragmentTitle(WaveFragment f) => Loc.Get($"ocean.fragment.{f}.title");
        public static string FragmentText(WaveFragment f) => Loc.Get($"ocean.fragment.{f}.text");
        public static string MomentLabel(AwakeningMoment m) => Loc.Get($"ocean.moment.{m}");

        /// <summary>
        /// v1.1.12: the Ocean Journal section of [P] Progress: the stage and its name, the points toward
        /// the next stage, the fragments found, the moments lived and the active boons.
        /// </summary>
        public static void WriteJournalSummary(TerminalEmulator term)
        {
            var ocean = OceanPhilosophySystem.Instance;
            int stage = ocean.AwakeningLevel;
            term.SetColor("bright_cyan");
            term.WriteLine($"  {Loc.Get("ocean.journal_stage", stage, OceanPhilosophySystem.MaxStage, StageName(stage))}");
            term.SetColor("white");
            if (stage >= OceanPhilosophySystem.MaxStage)
                term.WriteLine($"  {Loc.Get("ocean.journal_awake")}");
            else if (stage == OceanPhilosophySystem.MaxStage - 1 && ocean.Points >= ocean.PointsForNextStage)
                term.WriteLine($"  {Loc.Get("ocean.journal_points_last", ocean.Points)}");
            else
                term.WriteLine($"  {Loc.Get("ocean.journal_points", ocean.Points, ocean.PointsForNextStage)}");
            term.SetColor("gray");
            term.WriteLine($"  {Loc.Get("ocean.journal_fragments", ocean.CollectedFragments.Count, OceanPhilosophySystem.FragmentData.Count)}");
            term.WriteLine($"  {Loc.Get("ocean.journal_moments", ocean.ExperiencedMoments.Count)}");

            var boons = AwakeningBonus.ActiveAt(stage);
            if (boons.Count > 0)
            {
                term.SetColor("bright_green");
                term.WriteLine($"  {Loc.Get("ocean.journal_boons", string.Join(", ", boons))}");
            }
            term.SetColor("yellow");
            term.WriteLine($"  {Loc.Get("ocean.journal_open_key")}");
            term.SetColor("white");
        }

        /// <summary>v1.1.12: the Journal's pages: each fragment found with its lore, then the moments lived.</summary>
        public static async Task ShowJournal(TerminalEmulator term)
        {
            var ocean = OceanPhilosophySystem.Instance;
            term.ClearScreen();
            term.WriteLine("");
            UIHelper.WriteBoxHeader(term, Loc.Get("ocean.journal_header"), "bright_cyan", 64);
            term.WriteLine("");

            term.WriteLine($"  {Loc.Get("ocean.journal_fragments", ocean.CollectedFragments.Count, OceanPhilosophySystem.FragmentData.Count)}", "bright_cyan");
            term.WriteLine("");
            foreach (var f in ocean.CollectedFragments.OrderBy(f => OceanPhilosophySystem.FragmentData[f].RequiredAwakening).ThenBy(f => (int)f))
            {
                term.WriteLine($"  [{FragmentTitle(f)}]", "bright_white");
                foreach (var line in Wrap(FragmentText(f), 72))
                    term.WriteLine($"    {line}", "cyan");
                term.WriteLine("");
            }
            int hidden = OceanPhilosophySystem.FragmentData.Count - ocean.CollectedFragments.Count;
            if (hidden > 0)
            {
                term.WriteLine($"  {Loc.Get("ocean.journal_hidden", hidden)}", "dark_gray");
                term.WriteLine("");
            }

            term.WriteLine($"  {Loc.Get("ocean.journal_moments", ocean.ExperiencedMoments.Count)}", "bright_magenta");
            foreach (var m in ocean.ExperiencedMoments.OrderBy(m => (int)m))
                term.WriteLine($"    - {MomentLabel(m)}", "magenta");
            if (ocean.ExperiencedMoments.Count == 0)
                term.WriteLine($"    {Loc.Get("ocean.journal_no_moments")}", "dark_gray");
            term.WriteLine("");

            await term.PressAnyKey();
        }

        private static List<string> Wrap(string text, int width)
        {
            var lines = new List<string>();
            string current = "";
            foreach (var word in text.Split(' '))
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else current = current.Length == 0 ? word : current + " " + word;
            }
            if (current.Length > 0) lines.Add(current);
            return lines;
        }

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
