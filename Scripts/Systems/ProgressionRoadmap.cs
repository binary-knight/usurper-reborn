using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v0.65.4: read-only progression lookups over the existing ability/spell/story data. Powers the
    /// level-up unlock announcement, the [P] Path / /next roadmap screen, and any "what do I get at
    /// level N / what's next" surface. Pure reads -- no state mutation, no save fields.
    ///
    /// Design driver: the game already knew every class's unlock ladder but never showed it. Casters
    /// (a new spell every ~4 levels) felt great; martials with 12-15 level ability droughts felt like
    /// "a whole lot of nothing." Making the ladder visible converts every dead zone into a countdown.
    /// </summary>
    public static class ProgressionRoadmap
    {
        public readonly struct Unlock
        {
            public int Level { get; }
            public string Name { get; }
            public bool IsSpell { get; }
            public Unlock(int level, string name, bool isSpell) { Level = level; Name = name; IsSpell = isSpell; }
        }

        // Story gates: Old God boss floors + collectible seal floors.
        public static readonly int[] OldGodFloors = { 25, 40, 55, 70, 85, 95, 100 };
        public static readonly int[] SealFloors = { 15, 30, 45, 60, 80, 99 };

        /// <summary>Every ability + spell unlock for the character's class, sorted by required level.</summary>
        public static List<Unlock> GetAllUnlocks(Character c)
        {
            var list = new List<Unlock>();
            if (c == null) return list;

            foreach (var a in ClassAbilitySystem.GetClassAbilities(c.Class))
            {
                if (a == null || string.IsNullOrEmpty(a.Name)) continue;
                list.Add(new Unlock(a.LevelRequired, a.Name, false));
            }

            if (SpellSystem.HasSpells(c))
            {
                // Caster classes define up to 25 spell tiers; GetSpellInfo returns null past the last.
                for (int spellLevel = 1; spellLevel <= 25; spellLevel++)
                {
                    var info = SpellSystem.GetSpellInfo(c.Class, spellLevel);
                    if (info == null || string.IsNullOrEmpty(info.Name)) continue;
                    int req = SpellSystem.GetLevelRequired(c.Class, spellLevel);
                    if (req <= 0) continue;
                    list.Add(new Unlock(req, info.Name, true));
                }
            }

            return list.OrderBy(u => u.Level).ThenBy(u => u.IsSpell).ThenBy(u => u.Name).ToList();
        }

        /// <summary>Unlocks that become available exactly at the given level.</summary>
        public static List<Unlock> GetUnlocksAtLevel(Character c, int level)
            => GetAllUnlocks(c).Where(u => u.Level == level).ToList();

        /// <summary>The next `count` unlocks strictly above the character's current level.</summary>
        public static List<Unlock> GetNextUnlocks(Character c, int count)
            => GetAllUnlocks(c).Where(u => u.Level > (c?.Level ?? 1)).Take(count).ToList();

        /// <summary>Next story gate above the deepest dungeon floor reached, or null if all cleared.</summary>
        public static (int floor, bool isOldGod)? GetNextStoryGate(Character c)
        {
            int deepest = (int)(c?.Statistics?.DeepestDungeonLevel ?? 0);
            var gates = OldGodFloors.Select(f => (floor: f, isOldGod: true))
                .Concat(SealFloors.Select(f => (floor: f, isOldGod: false)))
                .Where(g => g.floor > deepest)
                .OrderBy(g => g.floor)
                .ToList();
            if (gates.Count == 0) return null;
            var g0 = gates[0];
            return (g0.floor, g0.isOldGod);
        }
    }
}
