using System;
using System.Collections.Generic;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.8 difficulty pass: how an ordinary monster chooses. Choose picks the special ability of a turn
    /// from the monster's own list, leaving out the picks that would be wasted; TraitBonus is the extra
    /// target weight of the Predator and Skirmisher families. The combat engine calls both only while
    /// GameConfig.MonsterTacticsEnabled is on and the fight is not an Old God's (CombatEngine
    /// MonsterTacticsActive); the chance to use an ability at all (30 + Level / 5) is the engine's and is
    /// unchanged. No text of its own: every ability line already exists (mability.*).
    /// </summary>
    public static class MonsterTactics
    {
        /// <summary>A self-heal is used only below this share of MaxHP.</summary>
        public const int SelfHealBelowHpPercent = 40;

        /// <summary>Tier-1 signature abilities are used from this monster level (levels 1 and 2 stay as soft as before).</summary>
        public const int SignatureMinLevel = 3;

        /// <summary>A Predator adds PredatorBonus to a target at PredatorHpPercent of MaxHP or less.</summary>
        public const int PredatorHpPercent = 35;
        public const int PredatorBonus = 60;

        /// <summary>A Skirmisher adds SkirmisherBonus to a caster or healer.</summary>
        public const int SkirmisherBonus = 50;

        internal static readonly HashSet<MonsterAbilities.AbilityType> SelfHeals = new()
        {
            MonsterAbilities.AbilityType.Heal, MonsterAbilities.AbilityType.Regeneration, MonsterAbilities.AbilityType.Phylactery,
            MonsterAbilities.AbilityType.SelfRepair, MonsterAbilities.AbilityType.Cocoon, MonsterAbilities.AbilityType.Rebirth,
            MonsterAbilities.AbilityType.Sanctuary,
        };

        internal static readonly HashSet<MonsterAbilities.AbilityType> EvasionBuffs = new()
        {
            MonsterAbilities.AbilityType.Invisibility, MonsterAbilities.AbilityType.Flight, MonsterAbilities.AbilityType.Incorporeal,
            MonsterAbilities.AbilityType.PhaseShift, MonsterAbilities.AbilityType.TreeMeld, MonsterAbilities.AbilityType.Teleport,
            MonsterAbilities.AbilityType.InkCloud,
        };

        /// <summary>
        /// The status each ability sets (AbilityResult.InflictStatus in MonsterAbilities.ExecuteAbility); a test
        /// holds this table to the handlers. Spellcasting and RealityBreak roll their status, so they are not
        /// here and are never left out for a status.
        /// </summary>
        internal static readonly Dictionary<MonsterAbilities.AbilityType, StatusEffect> StatusOf = new()
        {
            [MonsterAbilities.AbilityType.CrushingBlow] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.VenomousBite] = StatusEffect.Poisoned,
            [MonsterAbilities.AbilityType.BleedingWound] = StatusEffect.Bleeding,
            [MonsterAbilities.AbilityType.FireBreath] = StatusEffect.Burning,
            [MonsterAbilities.AbilityType.FrostBreath] = StatusEffect.Frozen,
            [MonsterAbilities.AbilityType.PoisonCloud] = StatusEffect.Poisoned,
            [MonsterAbilities.AbilityType.PetrifyingGaze] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.HorrifyingScream] = StatusEffect.Feared,
            [MonsterAbilities.AbilityType.BlindingFlash] = StatusEffect.Blinded,
            [MonsterAbilities.AbilityType.Curse] = StatusEffect.Cursed,
            [MonsterAbilities.AbilityType.Silence] = StatusEffect.Silenced,
            [MonsterAbilities.AbilityType.Enfeeble] = StatusEffect.Weakened,
            [MonsterAbilities.AbilityType.Paralyze] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.Warcry] = StatusEffect.Feared,
            [MonsterAbilities.AbilityType.DragonFear] = StatusEffect.Feared,
            [MonsterAbilities.AbilityType.Hellfire] = StatusEffect.Burning,
            [MonsterAbilities.AbilityType.Corruption] = StatusEffect.Cursed,
            [MonsterAbilities.AbilityType.Dominate] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.Boulder] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.Lightning] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.Earthquake] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.Bite] = StatusEffect.Bleeding,
            [MonsterAbilities.AbilityType.Lycanthropy] = StatusEffect.Cursed,
            [MonsterAbilities.AbilityType.Howl] = StatusEffect.Feared,
            [MonsterAbilities.AbilityType.Burn] = StatusEffect.Burning,
            [MonsterAbilities.AbilityType.Immolate] = StatusEffect.Burning,
            [MonsterAbilities.AbilityType.Inferno] = StatusEffect.Burning,
            [MonsterAbilities.AbilityType.Corrosion] = StatusEffect.Weakened,
            [MonsterAbilities.AbilityType.Engulf] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.Madness] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.WebTrap] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.Poison] = StatusEffect.Poisoned,
            [MonsterAbilities.AbilityType.DeadlyVenom] = StatusEffect.Poisoned,
            [MonsterAbilities.AbilityType.PoisonGas] = StatusEffect.Poisoned,
            [MonsterAbilities.AbilityType.Sleep] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.Charm] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.RootEntangle] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.TentacleGrab] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.InkCloud] = StatusEffect.Blinded,
            [MonsterAbilities.AbilityType.Whirlpool] = StatusEffect.Stunned,
            [MonsterAbilities.AbilityType.Purify] = StatusEffect.Weakened,
            [MonsterAbilities.AbilityType.StrengthDrain] = StatusEffect.Weakened,
            [MonsterAbilities.AbilityType.Terror] = StatusEffect.Feared,
            [MonsterAbilities.AbilityType.Nightmare] = StatusEffect.Feared,
        };

        private static readonly HashSet<string> PredatorFamilies = new(StringComparer.OrdinalIgnoreCase) { "Beast", "Insectoid", "Shadow", "Undead" };
        /// <summary>1.3.3 party survival: a predator family (Beast, Insectoid, Shadow, Undead) closes on a
        /// Downed ally. Read from the family alone, not through the tactics switch.</summary>
        internal static bool IsPredatorFamily(string? family) =>
            !string.IsNullOrEmpty(family) && PredatorFamilies.Contains(family);

        private static readonly HashSet<string> SkirmisherFamilies = new(StringComparer.OrdinalIgnoreCase) { "Goblinoid", "Fey", "Demonic" };

        /// <summary>True when the monster has anything Choose could pick from, at any level.</summary>
        public static bool HasAbilities(Monster m) =>
            (m.SpecialAbilities != null && m.SpecialAbilities.Count > 0) || (m.SignatureAbilities != null && m.SignatureAbilities.Count > 0);

        /// <summary>
        /// The ability the monster uses this turn against this target, or null for its normal attack. The
        /// candidates are its SpecialAbilities and, from level SignatureMinLevel, its SignatureAbilities. A
        /// candidate that would be wasted (Wasted) is left out and the pick is uniform over the rest: one
        /// rng.Next when anything is left, none otherwise. round and allies are not read by any rule yet.
        /// </summary>
        public static string? Choose(Monster m, Character target, int round, IReadOnlyList<Monster>? allies, Random rng)
        {
            var useful = new List<string>();
            if (m.SpecialAbilities != null)
                foreach (var name in m.SpecialAbilities)
                    if (!Wasted(name, m, target)) useful.Add(name);
            if (m.SignatureAbilities != null && m.Level >= SignatureMinLevel)
                foreach (var name in m.SignatureAbilities)
                    if (!Wasted(name, m, target)) useful.Add(name);
            if (useful.Count == 0) return null;
            return useful[rng.Next(useful.Count)];
        }

        /// <summary>
        /// True when using the ability now would do nothing new: a self-heal at SelfHealBelowHpPercent of MaxHP
        /// or more, a status the target already carries, an evasion buff while one is active, or a one-shot
        /// buff already spent (Rally and Rage share HasEnraged, Stoneskin uses HasHardenedArmor). A name that
        /// is not an AbilityType is never wasted here; the engine treats it as it did before.
        /// </summary>
        public static bool Wasted(string abilityName, Monster m, Character target)
        {
            if (!Enum.TryParse<MonsterAbilities.AbilityType>(abilityName, true, out var ability)) return false;
            if (SelfHeals.Contains(ability) && m.HP * 100 >= m.MaxHP * SelfHealBelowHpPercent) return true;
            if (StatusOf.TryGetValue(ability, out var status) && target != null && target.HasStatus(status)) return true;
            if (EvasionBuffs.Contains(ability) && m.EvasionRounds > 0) return true;
            return ability switch
            {
                MonsterAbilities.AbilityType.Rally or MonsterAbilities.AbilityType.Rage => m.HasEnraged,
                MonsterAbilities.AbilityType.Stoneskin => m.HasHardenedArmor,
                _ => false,
            };
        }

        /// <summary>
        /// Extra target weight by family trait, added to the engine's weight of each candidate (the player, a
        /// companion, a grouped player alike). Predator families (Beast, Insectoid, Shadow, Undead) add
        /// PredatorBonus to a target at PredatorHpPercent of MaxHP or less; Skirmisher families (Goblinoid,
        /// Fey, Demonic) add SkirmisherBonus to a Magician, Sage, Cleric, Alchemist, Wavecaller or
        /// Abysswarden. Every other family adds 0.
        /// </summary>
        public static int TraitBonus(Monster m, Character target)
        {
            string family = m.FamilyName ?? "";
            if (PredatorFamilies.Contains(family))
                return target.HP * 100 <= target.MaxHP * PredatorHpPercent ? PredatorBonus : 0;
            if (SkirmisherFamilies.Contains(family))
                return IsCasterOrHealer(target.Class) ? SkirmisherBonus : 0;
            return 0;
        }

        private static bool IsCasterOrHealer(CharacterClass c) =>
            c is CharacterClass.Magician or CharacterClass.Sage or CharacterClass.Cleric or CharacterClass.Alchemist
                or CharacterClass.Wavecaller or CharacterClass.Abysswarden;
    }
}
