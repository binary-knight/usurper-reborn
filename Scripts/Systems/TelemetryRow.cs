using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.7: where a copy of the game runs, for the upload batch (DESIGN.md section 1). Never in a row.
    /// </summary>
    public enum TelemetrySource
    {
        Single = 1,
        Steam = 2,
        BbsDoor = 3,
        Server = 4,
    }

    /// <summary>
    /// 1.2.7: the opt-in combat row: integers only, no name and no free text. Built on the combat thread
    /// from the same values as the combat_events row (CombatEngine.BuildCombatEventRow).
    /// </summary>
    public sealed class TelemetryRow
    {
        /// <summary>The largest integer JSON carries exactly (2^53 minus 1).</summary>
        internal const long MaxSafe = 9007199254740991L;

        /// <summary>Every key of a row, in order, with its bounds (DESIGN.md section 1). Values outside
        /// refuse the whole row; a large value with no stated bound is 0 to 2^53 minus 1.</summary>
        internal static readonly (string Key, long Min, long Max)[] Columns =
        {
            ("outcome", 0, 2),
            ("player_class", 0, 16),
            ("monster_family", 0, 15),
            ("is_boss", 0, 1),
            ("has_teammates", 0, 1),
            ("player_level", 1, 100),
            ("player_max_hp", 0, MaxSafe),
            ("player_str", 0, MaxSafe),
            ("player_dex", 0, MaxSafe),
            ("player_weap_pow", 0, MaxSafe),
            ("player_arm_pow", 0, MaxSafe),
            ("monster_level", 0, 200),
            ("monster_max_hp", 0, MaxSafe),
            ("monster_str", 0, MaxSafe),
            ("monster_def", 0, MaxSafe),
            ("rounds", 0, 10000),
            ("damage_dealt", 0, MaxSafe),
            ("damage_taken", 0, MaxSafe),
            ("xp_gained", 0, MaxSafe),
            ("gold_gained", 0, MaxSafe),
            ("monster_count", 0, 50),
            ("floor_actual", 0, 100),
            ("difficulty", 0, 3),
            ("party_size", 0, 8),
            ("encounter_size", 0, 50),
            ("first_actor", 0, 1),
            ("dmg_to_player_basic", 0, MaxSafe),
            ("dmg_to_player_ability", 0, MaxSafe),
            ("dmg_to_player_spell", 0, MaxSafe),
            ("dmg_to_player_dot", 0, MaxSafe),
            ("dmg_to_team", 0, MaxSafe),
            ("dmg_by_player", 0, MaxSafe),
            ("dmg_by_team", 0, MaxSafe),
            ("heal_player", 0, MaxSafe),
            ("potions_used", 0, 10000),
            ("abilities_used", 0, 10000),
            ("spells_used", 0, 10000),
            ("teammates_lost", 0, 10000),
            ("player_hp_end", 0, MaxSafe),
        };

        /// <summary>Columns that real play can push past a bound, held at that bound in the row so the fight
        /// is not dropped (T1a2 audit): cursed gear can make Strength or Dexterity negative, and the enraged
        /// duelist copies the player's Strength; the enraged duelist's level grows past 200 with encounters;
        /// summoners can push the kills of one fight past 50; a fight has no round limit, and the use counts
        /// grow with rounds. A value at the bound reads "that or more" (or "that or less"). player_level is not
        /// here: only the admin set level command takes it past 100, and such a row is dropped.</summary>
        internal static readonly string[] Saturating =
        {
            "player_str", "player_dex", "monster_str", "monster_level", "monster_count",
            "rounds", "potions_used", "abilities_used", "spells_used",
        };

        /// <summary>The 15 built in monster families (MonsterFamilies.cs), numbered 1 to 15 in this order.
        /// Any other family name (modded, Summoned, Divine, OldGod, wilderness) is 0.</summary>
        internal static readonly string[] Families =
        {
            "Goblinoid", "Undead", "Orcish", "Draconic", "Demonic", "Giant", "Beast", "Elemental",
            "Aberration", "Insectoid", "Construct", "Fey", "Aquatic", "Celestial", "Shadow",
        };

        /// <summary>The row's values, in the order of <see cref="Columns"/>.</summary>
        internal IReadOnlyList<long> Values { get; }

        private TelemetryRow(long[] values) { Values = values; }

        /// <summary>Column index by key.</summary>
        private static readonly Dictionary<string, int> ColumnIndex =
            Columns.Select((c, i) => (c.Key, i)).ToDictionary(x => x.Key, x => x.i, StringComparer.Ordinal);

        internal long this[string key] => Values[ColumnIndex[key]];

        internal static int OutcomeNumber(string outcome) => outcome switch
        {
            "victory" => 0,
            "fled" => 1,
            "death" => 2,
            _ => -1,
        };

        internal static int FamilyNumber(string? familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return 0;
            int i = Array.IndexOf(Families, familyName);
            return i < 0 ? 0 : i + 1;
        }

        /// <summary>
        /// The row of one fight, from the combat_events row's values, the player's class and the monster's
        /// family. Null when the fight was not entered or its end never arrived (rows hold integers only,
        /// so there is no null and no stand-in value).
        /// </summary>
        internal static TelemetryRow? From(CombatEventRow row, CharacterClass playerClass, string? monsterFamily)
        {
            var t = row.Tally;
            if (t == null || t.PlayerHpEnd == null) return null;
            var values = new long[]
            {
                OutcomeNumber(row.Outcome),
                (int)playerClass,
                FamilyNumber(monsterFamily),
                row.IsBoss ? 1 : 0,
                row.HasTeammates ? 1 : 0,
                row.PlayerLevel,
                row.PlayerMaxHP,
                row.PlayerSTR,
                row.PlayerDEX,
                row.PlayerWeapPow,
                row.PlayerArmPow,
                row.MonsterLevel,
                row.MonsterMaxHP,
                row.MonsterSTR,
                row.MonsterDEF,
                row.Rounds,
                row.DamageDealt,
                row.DamageTaken,
                row.XpGained,
                row.GoldGained,
                row.MonsterCount,
                t.FloorActual,
                t.Difficulty,
                t.PartySize,
                t.EncounterSize,
                t.FirstActor,
                t.DmgToPlayerBasic,
                t.DmgToPlayerAbility,
                t.DmgToPlayerSpell,
                t.DmgToPlayerDot,
                t.DmgToTeam,
                t.DmgByPlayer,
                t.DmgByTeam,
                t.HealPlayer,
                t.PotionsUsed,
                t.AbilitiesUsed,
                t.SpellsUsed,
                t.TeammatesLost,
                Math.Max(0, t.PlayerHpEnd.Value),    // a killing blow can leave HP below 0 (life drain); the fight's own value is unchanged
            };
            foreach (var key in Saturating)
            {
                int i = ColumnIndex[key];
                values[i] = Math.Clamp(values[i], Columns[i].Min, Columns[i].Max);
            }
            return new TelemetryRow(values);
        }

        /// <summary>The row as a JSON object, keys in column order.</summary>
        internal JsonObject ToJson()
        {
            var o = new JsonObject();
            for (int i = 0; i < Columns.Length; i++) o[Columns[i].Key] = Values[i];
            return o;
        }

        /// <summary>A row read back from JSON; null unless <see cref="IsValid(JsonElement)"/> holds.</summary>
        internal static TelemetryRow? FromJson(JsonElement e)
        {
            var values = Read(e);
            return values == null ? null : new TelemetryRow(values);
        }

        /// <summary>
        /// The bounds check the server applies (DESIGN.md sections 1 and 4): an object with exactly the
        /// column keys, each value a JSON integer written plainly (no fraction, exponent or sign) inside its
        /// bounds. Anything else refuses the row.
        /// </summary>
        internal static bool IsValid(JsonElement e) => Read(e) != null;

        /// <summary>The values of a row that passes the check, in column order; null otherwise.</summary>
        private static long[]? Read(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Object) return null;
            var values = new long[Columns.Length];
            var seen = new bool[Columns.Length];
            int count = 0;
            foreach (var p in e.EnumerateObject())
            {
                if (!ColumnIndex.TryGetValue(p.Name, out int i) || seen[i]) return null;
                if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt64(out long v)) return null;
                if (p.Value.GetRawText()[0] == '-') return null;    // "-0": the server takes plain integers only
                if (v < Columns[i].Min || v > Columns[i].Max) return null;
                seen[i] = true;
                values[i] = v;
                count++;
            }
            return count == Columns.Length ? values : null;
        }

        /// <summary>The same check on a built row (through its JSON, so there is one rule).</summary>
        internal bool IsValid()
        {
            using var doc = JsonDocument.Parse(ToJson().ToJsonString());
            return IsValid(doc.RootElement);
        }
    }
}
