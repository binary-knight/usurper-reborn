using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Data;

/// <summary>
/// Beast Taming (v0.61.0). Wild creatures the player can encounter and tame in the
/// wilderness. Each beast belongs to a region, has a minimum player level for the
/// encounter to trigger, and offers either a passive bonus (out-of-combat effect)
/// or a combat-ready 5th party slot. Players accumulate beasts in a permanent
/// roster (cap 8) and switch between them at Home.
/// </summary>
public static class BeastData
{
    /// <summary>How many active pets the roster can hold.</summary>
    public const int MaxRosterSize = 8;

    /// <summary>Number of skill-check attempts the player gets to tame a beast per encounter.</summary>
    public const int TameAttempts = 3;

    /// <summary>Encounter probability per wilderness expedition (after the standard encounter resolves).</summary>
    public const int BeastEncounterChancePercent = 8;

    public enum BeastRole
    {
        Passive,  // Doesn't enter combat. Provides an out-of-combat bonus while active.
        Combat,   // Enters combat as a 5th party slot. Has HP/Atk/Def.
    }

    public class BeastDefinition
    {
        public string Id { get; init; } = "";                  // Stable key, also the per-player roster lookup key.
        public string Name { get; init; } = "";                // Display name ("Forest Hawk").
        public string Species { get; init; } = "";             // Flavor type ("Bird of Prey").
        public string RegionDirectionKey { get; init; } = "";  // Which WildernessRegion (N/E/S/W).
        public int MinPlayerLevel { get; init; }               // Player level required for this beast to spawn.
        public BeastRole Role { get; init; }
        public int TameDifficulty { get; init; }               // CHA + DEX vs this number; ~roll d20 + stat-mod >= diff.
        public string EncounterFlavor { get; init; } = "";     // Read when the beast is first sighted.
        public string TameSuccessFlavor { get; init; } = "";   // Read when the tame succeeds.
        public string PassiveDescription { get; init; } = "";  // One-line "what this beast does for you" summary.

        // Combat stats (only used when Role == Combat). Scaled by player level at runtime.
        public int CombatBaseHP { get; init; }
        public int CombatBaseAttack { get; init; }
        public int CombatBaseDefence { get; init; }
        public string CombatPassive { get; init; } = "";       // Short flavor of what the combat pet does (internal, not displayed).

        // Localized accessors. The English fields above are the source/fallback; these resolve
        // beast.{id}.* loc keys so the encounter/tame prose and passive summary render in the
        // session language. Name/Species stay canonical creature types (monster-name layer).
        private static string LocOr(string key, string fallback)
        {
            var v = UsurperRemake.Systems.Loc.Get(key);
            return v == key ? fallback : v;
        }
        public string LocEncounterFlavor() => LocOr($"beast.{Id}.encounter", EncounterFlavor);
        public string LocTameSuccessFlavor() => LocOr($"beast.{Id}.tame", TameSuccessFlavor);
        public string LocPassiveDescription() => LocOr($"beast.{Id}.passive", PassiveDescription);
    }

    public static readonly BeastDefinition[] Beasts = new[]
    {
        // ── Whispering Forest ──────────────────────────────────────────────
        new BeastDefinition
        {
            Id = "forest_hawk",
            Name = "Forest Hawk",
            Species = "Bird of Prey",
            RegionDirectionKey = "N",
            MinPlayerLevel = 10,
            Role = BeastRole.Passive,
            TameDifficulty = 14,
            EncounterFlavor =
                "A russet-feathered hawk circles low overhead, then drops to a branch above the path.\n" +
                "It watches you with bright, unafraid eyes.",
            TameSuccessFlavor =
                "You offer your gauntleted forearm. The hawk hops down, claws scoring the leather, and\n" +
                "fixes you with a steady, expectant look. You have a friend in high places now.",
            PassiveDescription = "+5% dungeon map reveal per floor visited"
        },
        new BeastDefinition
        {
            Id = "dire_wolf",
            Name = "Dire Wolf",
            Species = "Predator",
            RegionDirectionKey = "N",
            MinPlayerLevel = 30,
            Role = BeastRole.Combat,
            TameDifficulty = 19,
            EncounterFlavor =
                "A wolf the size of a small horse steps out of the brush, low-shouldered and silent.\n" +
                "Its grey muzzle is scarred. Old grey-coat fights have rendered judgment on it many times,\n" +
                "and it has always walked away.",
            TameSuccessFlavor =
                "You stand your ground. The wolf circles once, ears flat, then sits beside you.\n" +
                "It is not your pet. It is something more like a fellow who agreed not to bite you.",
            PassiveDescription = "Combat companion (5th party slot)",
            CombatBaseHP = 80,
            CombatBaseAttack = 18,
            CombatBaseDefence = 8,
            CombatPassive = "Bite -- flat damage, no special abilities."
        },

        // ── Iron Mountains ─────────────────────────────────────────────────
        new BeastDefinition
        {
            Id = "mountain_goat",
            Name = "Mountain Goat",
            Species = "Beast of Burden",
            RegionDirectionKey = "E",
            MinPlayerLevel = 15,
            Role = BeastRole.Passive,
            TameDifficulty = 12,
            EncounterFlavor =
                "A long-coated goat with corkscrew horns picks its way along a ledge you would never have spotted.\n" +
                "It pauses, gives you a long, unimpressed look, and chews.",
            TameSuccessFlavor =
                "You hold out a strip of dried bread. The goat considers it. Considers you.\n" +
                "Accepts the bread. Decides you are now its problem.",
            PassiveDescription = "-1 fatigue per dungeon descend"
        },
        new BeastDefinition
        {
            Id = "cave_spider",
            Name = "Cave Spider",
            Species = "Arachnid",
            RegionDirectionKey = "E",
            MinPlayerLevel = 35,
            Role = BeastRole.Passive,
            TameDifficulty = 17,
            EncounterFlavor =
                "Eight obsidian eyes regard you from a crevice between stones. Then the spider crawls forward,\n" +
                "the size of a large hound, and tilts its body slightly -- a question.",
            TameSuccessFlavor =
                "It rides on your shoulder. NPCs cross the street when they see you coming.\n" +
                "You catch yourself feeding it scraps and humming.",
            PassiveDescription = "+5% crit chance, NPCs react -10% (creepy)"
        },

        // ── Blackmire Swamp ────────────────────────────────────────────────
        new BeastDefinition
        {
            Id = "marsh_toad",
            Name = "Marsh Toad",
            Species = "Amphibian",
            RegionDirectionKey = "S",
            MinPlayerLevel = 12,
            Role = BeastRole.Passive,
            TameDifficulty = 13,
            EncounterFlavor =
                "A toad the size of a dinner plate sits on a lily-pad. Its eyes are the wrong color for\n" +
                "any toad you have ever seen -- pale gold and oddly intelligent.",
            TameSuccessFlavor =
                "You scoop it up. It does not flinch. It clings to your wrist with patient amphibian patience\n" +
                "and seems to know it is now the only toad that matters.",
            PassiveDescription = "+25% poison resist, free antidote 1/day"
        },
        new BeastDefinition
        {
            Id = "bog_wisp",
            Name = "Bog Wisp",
            Species = "Spirit-Light",
            RegionDirectionKey = "S",
            MinPlayerLevel = 40,
            Role = BeastRole.Passive,
            TameDifficulty = 18,
            EncounterFlavor =
                "A pale flame hangs in the air between two rotted stumps. It is not a torch and there is no source.\n" +
                "When you step toward it, it sways -- not away, but as if listening.",
                TameSuccessFlavor =
                "You whisper your name. The wisp settles into your shadow and lives there now,\n" +
                "a small private moon that follows wherever you go.",
            PassiveDescription = "+shadow damage, free Hide action 1/fight"
        },

        // ── Stormbreak Coast ───────────────────────────────────────────────
        new BeastDefinition
        {
            Id = "tidepool_sprite",
            Name = "Tidepool Sprite",
            Species = "Faerie",
            RegionDirectionKey = "W",
            MinPlayerLevel = 20,
            Role = BeastRole.Passive,
            TameDifficulty = 15,
            EncounterFlavor =
                "Something pearl-colored drifts in a rock pool. When you lean close, a small face the size of a thumbnail\n" +
                "lifts from the water and considers you with grave, ancient eyes.",
            TameSuccessFlavor =
                "You offer your cupped palm. The sprite climbs in, light as wet silk,\n" +
                "and begins to hum a song older than the coast itself.",
            PassiveDescription = "+5% out-of-combat mana regen"
        },
        new BeastDefinition
        {
            Id = "storm_eagle",
            Name = "Storm Eagle",
            Species = "Apex Predator",
            RegionDirectionKey = "W",
            MinPlayerLevel = 50,
            Role = BeastRole.Combat,
            TameDifficulty = 21,
            EncounterFlavor =
                "Lightning forks across the headland. When the flash fades, an eagle the size of a wagon stands on\n" +
                "the rocks, wings still smoking. It tilts its head. It has noticed you.",
            TameSuccessFlavor =
                "You do not flinch. The eagle takes one slow step forward and bows its head briefly,\n" +
                "the way old kings did when they were tired of being kings.",
            PassiveDescription = "Combat companion (5th party slot)",
            CombatBaseHP = 110,
            CombatBaseAttack = 30,
            CombatBaseDefence = 12,
            CombatPassive = "Lightning-themed strikes; occasional stun on hit."
        }
    };

    public static BeastDefinition? GetById(string id) =>
        Beasts.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Pick a single beast that's eligible to spawn for this player on this expedition.
    /// Eligibility: matches the region the player is exploring AND player meets MinPlayerLevel.
    /// Returns null if nothing qualifies. The wilderness encounter code rolls
    /// BeastEncounterChancePercent BEFORE calling this -- this method does not roll
    /// the spawn chance itself; it just picks WHICH beast spawns if the roll succeeded.
    /// </summary>
    public static BeastDefinition? PickEligibleBeast(string regionDirectionKey, int playerLevel, Random rng,
        IReadOnlyCollection<string>? alreadyOwnedIds = null)
    {
        var pool = Beasts
            .Where(b => string.Equals(b.RegionDirectionKey, regionDirectionKey, StringComparison.OrdinalIgnoreCase)
                        && playerLevel >= b.MinPlayerLevel)
            .Where(b => alreadyOwnedIds == null || !alreadyOwnedIds.Contains(b.Id))
            .ToList();
        if (pool.Count == 0) return null;
        return pool[rng.Next(pool.Count)];
    }
}

/// <summary>
/// A tamed beast in the player's permanent roster. Persists across sessions.
/// CombatBaseHP/Attack/Defence on the definition get scaled at runtime by player level.
/// </summary>
public class Pet
{
    public string Id { get; set; } = "";                  // Matches BeastDefinition.Id.
    public string Name { get; set; } = "";                // Player-given name (or default species name).
    public DateTime TamedAtUtc { get; set; } = DateTime.MinValue;
    public int Level { get; set; } = 1;                   // Pet's own level, grows slowly with use.
    public long Experience { get; set; } = 0;             // Toward next pet level.

    /// <summary>Convenience access to the BeastDefinition by Id.</summary>
    public BeastData.BeastDefinition? GetDefinition() => BeastData.GetById(Id);
}
