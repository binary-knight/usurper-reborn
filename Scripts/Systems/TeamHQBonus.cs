using System;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v1.1.11: the Team HQ upgrade bonuses a player gets from their team (online only).
    ///   Armory     +5% of the damage the player deals, every way they deal it
    ///   Barracks   +5% defence: the damage the player takes is divided by 1 + 5% per level
    ///   Training   +5% of combat XP
    ///   Infirmary  +10% of what a healing potion restores
    /// Order, the same at every site: the bonus is applied after every other modifier of that damage,
    /// XP or healing, and before any floor, cap or minimum that follows, so it composes with the Old God
    /// dialogue factor and the rest the same way everywhere.
    ///
    /// The levels live on the Character at run time only, never in the save. They are the levels of the
    /// team they were read for (HQLevelsTeam): a player who leaves or changes team gets nothing from them
    /// until they are read again, which happens at login, when a fight starts, on the status screen and
    /// after an upgrade.
    /// </summary>
    public static class TeamHQBonus
    {
        public const double ArmoryPerLevel = 0.05;
        public const double BarracksPerLevel = 0.05;
        public const double TrainingPerLevel = 0.05;
        public const double InfirmaryPerLevel = 0.10;

        private static bool Current(Character c) => !string.IsNullOrEmpty(c.Team) && c.Team == c.HQLevelsTeam;

        public static int Armory(Character c) => Current(c) ? c.HQArmoryLevel : 0;
        public static int Barracks(Character c) => Current(c) ? c.HQBarracksLevel : 0;
        public static int Training(Character c) => Current(c) ? c.HQTrainingLevel : 0;
        public static int Infirmary(Character c) => Current(c) ? c.HQInfirmaryLevel : 0;

        public static double AttackMultiplier(Character c) => 1.0 + Armory(c) * ArmoryPerLevel;
        public static double DefenseMultiplier(Character c) => 1.0 + Barracks(c) * BarracksPerLevel;
        public static double XPMultiplier(Character c) => 1.0 + Training(c) * TrainingPerLevel;
        public static double PotionHealMultiplier(Character c) => 1.0 + Infirmary(c) * InfirmaryPerLevel;

        /// <summary>Damage dealt by the player, after every other modifier.</summary>
        public static long ApplyAttack(Character c, long damage) => Armory(c) > 0 ? (long)Math.Round(damage * AttackMultiplier(c)) : damage;
        /// <summary>Damage taken by the player, after every other modifier and before any floor.</summary>
        public static long ApplyDefense(Character c, long damage) => Barracks(c) > 0 ? (long)Math.Round(damage / DefenseMultiplier(c)) : damage;
        public static long ApplyXP(Character c, long xp) => Training(c) > 0 ? (long)Math.Round(xp * XPMultiplier(c)) : xp;
        public static long ApplyPotionHeal(Character c, long heal) => Infirmary(c) > 0 ? (long)Math.Round(heal * PotionHealMultiplier(c)) : heal;

        /// <summary>
        /// Reads the levels of the character's team. A character with no team, an NPC, or a game without
        /// the online database gets none. A PvP defender loaded from a save gets its own team's levels.
        /// </summary>
        public static void RefreshLevels(Character c, SqlSaveBackend? backend = null)
        {
            backend ??= SaveSystem.Instance?.Backend as SqlSaveBackend;
            if (c is NPC || string.IsNullOrEmpty(c.Team) || backend == null)
            {
                c.HQArmoryLevel = c.HQBarracksLevel = c.HQTrainingLevel = c.HQInfirmaryLevel = 0;
                c.HQLevelsTeam = "";
                return;
            }
            var levels = backend.GetTeamUpgradeLevels(c.Team);
            int Level(string type) => levels.TryGetValue(type, out int l) ? l : 0;
            c.HQArmoryLevel = Level("armory");
            c.HQBarracksLevel = Level("barracks");
            c.HQTrainingLevel = Level("training");
            c.HQInfirmaryLevel = Level("infirmary");
            c.HQLevelsTeam = c.Team;
        }
    }
}
