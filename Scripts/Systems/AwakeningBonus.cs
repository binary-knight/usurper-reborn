using System;
using System.Collections.Generic;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v1.1.12: the boons of the awakening stages, cumulative, computed from the stage at run time and
    /// never stored on the character.
    ///   Stage 1  +3 Wisdom
    ///   Stage 2  +5% max mana
    ///   Stage 3  +5% combat XP
    ///   Stage 4  +5% max HP
    ///   Stage 5  +3% damage dealt
    ///   Stage 6  3% less damage taken
    ///   Stage 7  +5 more to each percentage above, and the title "the Awakened"
    /// Only a player has an awakening, stamped on their Character by their own session (AwakeningStage):
    /// companions, NPCs and characters loaded from other players' saves get nothing from it. Damage, defence and XP go through TeamHQBonus.Apply*, so they follow its
    /// ordering rule (after every other modifier, before any floor, cap or minimum).
    /// </summary>
    public static class AwakeningBonus
    {
        public const int Wisdom1 = 3;
        public const double Mana2 = 0.05;
        public const double XP3 = 0.05;
        public const double HP4 = 0.05;
        public const double Damage5 = 0.03;
        public const double Defense6 = 0.03;
        public const double Stage7Extra = 0.05;

        /// <summary>The awakening stage that applies to this character: its own stamped stage, else the
        /// session player's, else 0.</summary>
        public static int StageOf(Character? c)
        {
            if (c == null || c is NPC) return 0;
            // v1.1.12: the stamp travels with a grouped follower into the leader's session
            if (c.AwakeningStage >= 0) return c.AwakeningStage;
            var player = GameEngine.Instance?.CurrentPlayer;
            if (!ReferenceEquals(c, player)) return 0;
            return OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0;
        }

        private static double Pct(int stage, int from, double pct) =>
            stage < from ? 0.0 : pct + (stage >= OceanPhilosophySystem.MaxStage ? Stage7Extra : 0.0);

        public static int WisdomAt(int stage) => stage >= 1 ? Wisdom1 : 0;
        public static double ManaAt(int stage) => Pct(stage, 2, Mana2);
        public static double XPAt(int stage) => Pct(stage, 3, XP3);
        public static double HPAt(int stage) => Pct(stage, 4, HP4);
        public static double DamageAt(int stage) => Pct(stage, 5, Damage5);
        public static double DefenseAt(int stage) => Pct(stage, 6, Defense6);
        public static bool IsAwakened(int stage) => stage >= OceanPhilosophySystem.MaxStage;

        public static int Wisdom(Character c) => WisdomAt(StageOf(c));
        public static double DamageMultiplier(Character c) => 1.0 + DamageAt(StageOf(c));
        public static double DefenseFactor(Character c) => 1.0 - DefenseAt(StageOf(c));
        public static double XPMultiplier(Character c) => 1.0 + XPAt(StageOf(c));

        /// <summary>
        /// v1.1.12: the player was recalculated before the story systems were restored, so without the
        /// boons; recalculate with them and give back the saved HP and mana the earlier pass clamped.
        /// </summary>
        public static void RecalculateAfterRestore(Character? player, long savedHP, long savedMana)
        {
            if (player == null) return;
            Stamp(player);
            if (StageOf(player) == 0) return;
            player.RecalculateStats();
            if (savedHP > player.HP) player.HP = Math.Min(savedHP, player.MaxHP);
            if (savedMana > player.Mana) player.Mana = Math.Min(savedMana, player.MaxMana);
        }

        /// <summary>v1.1.12: give a player the session's stage as their own.</summary>
        public static void Stamp(Character? player, int? stage = null)
        {
            if (player == null || player is NPC) return;
            player.AwakeningStage = stage ?? OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0;
        }

        private static int P(double pct) => (int)Math.Round(pct * 100);

        /// <summary>The boon one stage adds, as a line of text.</summary>
        public static string GainedAt(int stage) => stage switch
        {
            1 => Loc.Get("ocean.boost.1", Wisdom1),
            2 => Loc.Get("ocean.boost.2", P(Mana2)),
            3 => Loc.Get("ocean.boost.3", P(XP3)),
            4 => Loc.Get("ocean.boost.4", P(HP4)),
            5 => Loc.Get("ocean.boost.5", P(Damage5)),
            6 => Loc.Get("ocean.boost.6", P(Defense6)),
            7 => Loc.Get("ocean.boost.7", P(Stage7Extra)),
            _ => ""
        };

        /// <summary>Every boon active at a stage, one line each, with the stage 7 extra folded in.</summary>
        public static List<string> ActiveAt(int stage)
        {
            var lines = new List<string>();
            if (stage >= 1) lines.Add(Loc.Get("ocean.boost.1", WisdomAt(stage)));
            if (stage >= 2) lines.Add(Loc.Get("ocean.boost.2", P(ManaAt(stage))));
            if (stage >= 3) lines.Add(Loc.Get("ocean.boost.3", P(XPAt(stage))));
            if (stage >= 4) lines.Add(Loc.Get("ocean.boost.4", P(HPAt(stage))));
            if (stage >= 5) lines.Add(Loc.Get("ocean.boost.5", P(DamageAt(stage))));
            if (stage >= 6) lines.Add(Loc.Get("ocean.boost.6", P(DefenseAt(stage))));
            if (IsAwakened(stage)) lines.Add(Loc.Get("ocean.title_awakened_line"));
            return lines;
        }
    }
}
