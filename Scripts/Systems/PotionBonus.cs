using System;
using System.Collections.Generic;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v1.1.11: the bonuses of a healing potion's owner (the character whose potion it is), applied after every
    /// other modifier of the heal and before the cap to missing HP: the Alchemist's Potion Mastery, then the
    /// team's Infirmary. Potion Mastery was applied only by the combat quick-heal, so every other potion (the
    /// item key, auto-heal, potions given to allies, town, Home, the dungeon, the world boss) missed it.
    /// 1.2.0: last, Solarius's boon while fighting undead or demons (the monsters parameter is the fight the
    /// owner is in now; null outside a monster fight, so the boon never applies there).
    /// </summary>
    public static class PotionBonus
    {
        /// <summary>
        /// 1.2.8: the base heal of one healing potion, before the difficulty multiplier, the owner bonuses and
        /// the cap to missing HP (each site keeps its own chain). The flat heal is 30 + 5 x Level plus a roll
        /// of rng.Next(10, rollMaxExclusive); the dungeon sites roll to 31, the others to 30, as before. With
        /// GameConfig.ScaledPotionsEnabled on the heal is at least ScaledPotionMaxHPFraction of the receiver's
        /// MaxHP: the character the potion heals, given when it is not c (a potion given to an ally). The roll
        /// is drawn once whether the switch is on or off, so the random sequence is the same either way.
        /// </summary>
        public static long BaseHeal(Character c, Random rng, int rollMaxExclusive = 30, Character? receiver = null)
        {
            long flat = 30 + c.Level * 5 + rng.Next(10, rollMaxExclusive);
            return GameConfig.ScaledPotionsEnabled ? Math.Max(flat, ScaledFloor(receiver ?? c)) : flat;
        }

        /// <summary>
        /// 1.2.8: the heal one potion is expected to give, for the "how many potions" and auto-heal estimates:
        /// the flat midpoint 50 + 5 x Level, or with ScaledPotionsEnabled at least ScaledPotionMaxHPFraction of
        /// the receiver's MaxHP (receiver as in BaseHeal). Draws no random number.
        /// </summary>
        public static long AverageHeal(Character c, Character? receiver = null)
        {
            long flat = 50 + c.Level * 5;
            return GameConfig.ScaledPotionsEnabled ? Math.Max(flat, ScaledFloor(receiver ?? c)) : flat;
        }

        private static long ScaledFloor(Character c) => (long)Math.Round(c.MaxHP * GameConfig.ScaledPotionMaxHPFraction);

        public static long ApplyOwnerBonuses(Character owner, long heal, IEnumerable<Monster>? monsters = null)
        {
            if (owner.Class == CharacterClass.Alchemist)
                heal = (long)Math.Round(heal * (1.0 + GameConfig.AlchemistPotionMasteryBonus));
            heal = TeamHQBonus.ApplyPotionHeal(owner, heal);
            return GodBoonSystem.HealAgainstUndead(owner, heal, monsters);
        }
    }
}
