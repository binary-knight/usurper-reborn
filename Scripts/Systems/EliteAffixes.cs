using System;
using System.Collections.Generic;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.8 difficulty pass: what five of the six elite affixes do in a fight (DESIGN item 3, rows F6 to F10).
    /// Every rule here reads Monster.IsElite and Monster.EliteAffixes, which MonsterGenerator.PromoteElite fills
    /// only while the elite switch of GameConfig is on, so with the switch off no monster has an affix and
    /// none of this runs. The combat engine calls these and writes the lines; this class holds the numbers
    /// and the choices and draws one random number, the Venomous chance. The sixth affix is not here: it
    /// belongs to the player's to-hit roll.
    /// </summary>
    public static class EliteAffixes
    {
        public const string Frenzied = "frenzied";
        public const string Venomous = "venomous";
        public const string Commanding = "commanding";
        public const string Mending = "mending";
        public const string Swift = "swift";

        /// <summary>Frenzied: below this share of MaxHP its Strength rises by FrenzyStrengthMultiplier, once.</summary>
        public const int FrenzyBelowHpPercent = 50;
        public const double FrenzyStrengthMultiplier = 1.25;

        /// <summary>Venomous: the chance that a basic hit that did damage poisons, and for how many rounds.</summary>
        public const int VenomChancePercent = 35;
        public const int VenomRounds = 5;

        /// <summary>Commanding: its living allies deal this much more damage while it lives.</summary>
        public const double CommandDamageMultiplier = 1.10;

        /// <summary>Mending: it heals an ally below MendBelowHpPercent of MaxHP for MendHealPercent of that
        /// ally's MaxHP, and then not again for MendEveryRounds rounds.</summary>
        public const int MendBelowHpPercent = 40;
        public const int MendHealPercent = 15;
        public const int MendEveryRounds = 3;

        /// <summary>True when the monster is an elite that carries the affix.</summary>
        public static bool Has(Monster? m, string affix) => m != null && m.IsElite && m.EliteAffixes.Contains(affix);

        /// <summary>
        /// Frenzied: true the first time the elite is seen below FrenzyBelowHpPercent of MaxHP; its Strength
        /// is raised here and the flag set, so every later call is false.
        /// </summary>
        public static bool TryFrenzy(Monster m)
        {
            if (!Has(m, Frenzied) || m.EliteFrenzyUsed || !m.IsAlive) return false;
            if (m.HP * 100 >= m.MaxHP * FrenzyBelowHpPercent) return false;
            m.EliteFrenzyUsed = true;
            m.Strength = (long)(m.Strength * FrenzyStrengthMultiplier);
            return true;
        }

        /// <summary>
        /// Venomous: true when this basic hit poisons. No draw unless the attacker is a Venomous elite and
        /// the target is not poisoned already; then one rng.Next(100).
        /// </summary>
        public static bool VenomLands(Monster attacker, Character target, Random rng)
        {
            if (!Has(attacker, Venomous)) return false;
            if (target.HasStatus(StatusEffect.Poisoned)) return false;
            return rng.Next(100) < VenomChancePercent;
        }

        /// <summary>True while a living Commanding elite other than the attacker is in the group.</summary>
        public static bool IsCommanded(Monster attacker, IReadOnlyList<Monster>? group)
        {
            if (group == null) return false;
            for (int i = 0; i < group.Count; i++)
            {
                var m = group[i];
                if (!ReferenceEquals(m, attacker) && m.IsAlive && Has(m, Commanding)) return true;
            }
            return false;
        }

        /// <summary>Commanding: the damage an ally of a living Commanding elite deals (rounded to the nearest
        /// point); the same number for everyone else, the elite itself included.</summary>
        public static long CommandedDamage(Monster attacker, IReadOnlyList<Monster>? group, long damage)
        {
            if (damage <= 0 || !IsCommanded(attacker, group)) return damage;
            return (long)Math.Round(damage * CommandDamageMultiplier, MidpointRounding.AwayFromZero);
        }

        /// <summary>True when the elite has a living ally in the group.</summary>
        public static bool HasLivingAlly(Monster elite, IReadOnlyList<Monster>? group)
        {
            if (group == null) return false;
            for (int i = 0; i < group.Count; i++)
                if (!ReferenceEquals(group[i], elite) && group[i].IsAlive) return true;
            return false;
        }

        /// <summary>
        /// Mending: the ally the elite heals this round, or null. It must be the elite's turn to mend
        /// (round at or past Monster.EliteMendReadyRound); the ally is living, not the elite, below
        /// MendBelowHpPercent of MaxHP, and the one with the smallest share of its MaxHP left (the first
        /// in the group on a tie). No random number.
        /// </summary>
        public static Monster? MendTarget(Monster elite, IReadOnlyList<Monster>? group, int round)
        {
            if (!Has(elite, Mending) || !elite.IsAlive || group == null || round < elite.EliteMendReadyRound) return null;
            Monster? best = null;
            for (int i = 0; i < group.Count; i++)
            {
                var m = group[i];
                if (ReferenceEquals(m, elite) || !m.IsAlive || m.MaxHP <= 0) continue;
                if (m.HP * 100 >= m.MaxHP * MendBelowHpPercent) continue;
                if (best == null || m.HP * best.MaxHP < best.HP * m.MaxHP) best = m;
            }
            return best;
        }

        /// <summary>Mending: heals the ally for MendHealPercent of its MaxHP (never past MaxHP), sets the
        /// elite's next mend MendEveryRounds rounds on, and returns the HP restored.</summary>
        public static long Mend(Monster elite, Monster ally, int round)
        {
            long amount = Math.Max(1, ally.MaxHP * MendHealPercent / 100);
            long before = ally.HP;
            ally.HP = Math.Min(ally.MaxHP, ally.HP + amount);
            elite.EliteMendReadyRound = round + MendEveryRounds;
            return ally.HP - before;
        }
    }
}
