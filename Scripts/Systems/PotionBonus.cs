using System;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v1.1.11: the bonuses of a healing potion's owner (the character whose potion it is), applied after every
    /// other modifier of the heal and before the cap to missing HP: the Alchemist's Potion Mastery, then the
    /// team's Infirmary. Potion Mastery was applied only by the combat quick-heal, so every other potion (the
    /// item key, auto-heal, potions given to allies, town, Home, the dungeon, the world boss) missed it.
    /// </summary>
    public static class PotionBonus
    {
        public static long ApplyOwnerBonuses(Character owner, long heal)
        {
            if (owner.Class == CharacterClass.Alchemist)
                heal = (long)Math.Round(heal * (1.0 + GameConfig.AlchemistPotionMasteryBonus));
            return TeamHQBonus.ApplyPotionHeal(owner, heal);
        }
    }
}
