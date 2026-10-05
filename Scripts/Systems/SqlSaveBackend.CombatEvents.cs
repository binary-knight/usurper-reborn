using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.6: the values of one fight's accumulator (CombatResult.Tally) as the combat_events row stores
    /// them. Plain values only, copied on the combat thread.
    /// </summary>
    public sealed record CombatRowTally(
        int FloorActual, int Difficulty, int PartySize, int EncounterSize, int FirstActor,
        long DmgToPlayerBasic, long DmgToPlayerAbility, long DmgToPlayerSpell, long DmgToPlayerDot,
        long DmgToTeam, long DmgByPlayer, long DmgByTeam, long HealPlayer,
        int PotionsUsed, int AbilitiesUsed, int SpellsUsed, int TeammatesLost, long? PlayerHpEnd)
    {
        /// <summary>The tally's values, or null when the fight was not entered (the new columns stay NULL).</summary>
        public static CombatRowTally? From(CombatTally? t) => t == null || !t.Started ? null : new CombatRowTally(
            t.FloorActual, t.Difficulty, t.PartySize, t.EncounterSize, t.FirstActor,
            t.DmgToPlayerBasic, t.DmgToPlayerAbility, t.DmgToPlayerSpell, t.DmgToPlayerDot,
            t.DmgToTeam, t.DmgByPlayer, t.DmgByTeam, t.HealPlayer,
            t.PotionsUsed, t.AbilitiesUsed, t.SpellsUsed, t.TeammatesLost, t.Ended ? t.PlayerHpEnd : null);
    }

    /// <summary>
    /// 1.2.6: every value of one combat_events row, built on the combat thread before the insert runs in
    /// the background, so the insert never reads a live Character, Monster or CombatResult.
    /// </summary>
    public sealed record CombatEventRow(
        string PlayerName, int PlayerLevel, string PlayerClass,
        long PlayerMaxHP, long PlayerSTR, long PlayerDEX, long PlayerWeapPow, long PlayerArmPow,
        string? MonsterName, int MonsterLevel, long MonsterMaxHP, long MonsterSTR, long MonsterDEF,
        bool IsBoss, string Outcome, int Rounds,
        long DamageDealt, long DamageTaken, long XpGained, long GoldGained,
        int DungeonFloor, int MonsterCount, bool HasTeammates,
        CombatRowTally? Tally);

    public partial class SqlSaveBackend
    {
        /// <summary>1.2.6: the accumulator columns of combat_events, in insert order, with their values.
        /// INTEGER with no default: NULL on rows written before 1.2.6 and on a fight that was not entered.</summary>
        internal static readonly (string Column, Func<CombatRowTally, object?> Value)[] CombatTallyColumns =
        {
            ("floor_actual", t => t.FloorActual),
            ("difficulty", t => t.Difficulty),
            ("party_size", t => t.PartySize),
            ("encounter_size", t => t.EncounterSize),
            ("first_actor", t => t.FirstActor),
            ("dmg_to_player_basic", t => t.DmgToPlayerBasic),
            ("dmg_to_player_ability", t => t.DmgToPlayerAbility),
            ("dmg_to_player_spell", t => t.DmgToPlayerSpell),
            ("dmg_to_player_dot", t => t.DmgToPlayerDot),
            ("dmg_to_team", t => t.DmgToTeam),
            ("dmg_by_player", t => t.DmgByPlayer),
            ("dmg_by_team", t => t.DmgByTeam),
            ("heal_player", t => t.HealPlayer),
            ("potions_used", t => t.PotionsUsed),
            ("abilities_used", t => t.AbilitiesUsed),
            ("spells_used", t => t.SpellsUsed),
            ("teammates_lost", t => t.TeammatesLost),
            ("player_hp_end", t => t.PlayerHpEnd),
        };

        /// <summary>1.2.6: the accumulator columns on a database made by an older release, then the indexes
        /// the retention and the per-floor reads use. Idempotent: an existing column throws and is skipped.</summary>
        private static void MigrateCombatEventColumns(SqliteConnection connection)
        {
            foreach (var (column, _) in CombatTallyColumns)
            {
                try
                {
                    using var migCmd = connection.CreateCommand();
                    migCmd.CommandText = $"ALTER TABLE combat_events ADD COLUMN {column} INTEGER;";
                    migCmd.ExecuteNonQuery();
                }
                catch { /* Column already exists - expected */ }
            }
            try
            {
                using var idxCmd = connection.CreateCommand();
                idxCmd.CommandText =
                    "CREATE INDEX IF NOT EXISTS idx_ce_created ON combat_events(created_at);" +
                    "CREATE INDEX IF NOT EXISTS idx_ce_floor ON combat_events(floor_actual, created_at);";
                idxCmd.ExecuteNonQuery();
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"combat_events indexes not ensured: {ex.Message}"); }
        }

        /// <summary>1.2.6 retention (PruneCombatEvents, daily at the world-sim reset): non-deaths 30 days or
        /// the newest 15000 rows, deaths 90 days and never counted against the row cap.</summary>
        internal const int CombatEventKeepDays = 30;
        internal const int CombatEventMaxRows = 15000;
        internal const int CombatDeathKeepDays = 90;

        /// <summary>1.2.6: how long the background insert waits on a locked database before it gives up
        /// (telemetry: a lost row is better than a queue of blocked threads).</summary>
        internal int CombatRowTimeoutSeconds { get; set; } = 5;

        /// <summary>1.2.6: a failed insert is logged at most once in this window; the failures in between
        /// are counted and reported with the next logged one.</summary>
        internal static readonly TimeSpan CombatRowErrorLogWindow = TimeSpan.FromMinutes(10);

        private readonly object _combatRowErrorLock = new();
        private DateTime _combatRowLastErrorLog = DateTime.MinValue;
        private int _combatRowUnlogged;

        /// <summary>Failed combat row inserts since start.</summary>
        internal int CombatRowFailures { get; private set; }
        /// <summary>Error lines written for failed combat row inserts since start.</summary>
        internal int CombatRowErrorLogs { get; private set; }

        // --- Combat Events (Balance Dashboard) ---

        /// <summary>
        /// Insert one combat_events row. 1.2.6: takes a snapshot built on the combat thread and is run off it
        /// (CombatEngine.LogCombatEventToDb); a failure is caught and logged, never thrown.
        /// </summary>
        public void LogCombatEvent(CombatEventRow row)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandTimeout = CombatRowTimeoutSeconds;
                string tallyColumns = string.Concat(CombatTallyColumns.Select(c => ", " + c.Column));
                string tallyParams = string.Concat(CombatTallyColumns.Select(c => ", @" + c.Column));
                cmd.CommandText = $@"
                    INSERT INTO combat_events (
                        player_name, player_level, player_class,
                        player_max_hp, player_str, player_dex, player_weap_pow, player_arm_pow,
                        monster_name, monster_level, monster_max_hp, monster_str, monster_def,
                        is_boss, outcome, rounds, damage_dealt, damage_taken,
                        xp_gained, gold_gained, dungeon_floor, monster_count, has_teammates{tallyColumns}
                    ) VALUES (
                        @pName, @pLevel, @pClass,
                        @pMaxHP, @pSTR, @pDEX, @pWeapPow, @pArmPow,
                        @mName, @mLevel, @mMaxHP, @mSTR, @mDEF,
                        @isBoss, @outcome, @rounds, @dmgDealt, @dmgTaken,
                        @xpGained, @goldGained, @floor, @mCount, @hasTeam{tallyParams}
                    );
                ";
                cmd.Parameters.AddWithValue("@pName", row.PlayerName);
                cmd.Parameters.AddWithValue("@pLevel", row.PlayerLevel);
                cmd.Parameters.AddWithValue("@pClass", row.PlayerClass);
                cmd.Parameters.AddWithValue("@pMaxHP", row.PlayerMaxHP);
                cmd.Parameters.AddWithValue("@pSTR", row.PlayerSTR);
                cmd.Parameters.AddWithValue("@pDEX", row.PlayerDEX);
                cmd.Parameters.AddWithValue("@pWeapPow", row.PlayerWeapPow);
                cmd.Parameters.AddWithValue("@pArmPow", row.PlayerArmPow);
                cmd.Parameters.AddWithValue("@mName", (object?)row.MonsterName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@mLevel", row.MonsterLevel);
                cmd.Parameters.AddWithValue("@mMaxHP", row.MonsterMaxHP);
                cmd.Parameters.AddWithValue("@mSTR", row.MonsterSTR);
                cmd.Parameters.AddWithValue("@mDEF", row.MonsterDEF);
                cmd.Parameters.AddWithValue("@isBoss", row.IsBoss ? 1 : 0);
                cmd.Parameters.AddWithValue("@outcome", row.Outcome);
                cmd.Parameters.AddWithValue("@rounds", row.Rounds);
                cmd.Parameters.AddWithValue("@dmgDealt", row.DamageDealt);
                cmd.Parameters.AddWithValue("@dmgTaken", row.DamageTaken);
                cmd.Parameters.AddWithValue("@xpGained", row.XpGained);
                cmd.Parameters.AddWithValue("@goldGained", row.GoldGained);
                cmd.Parameters.AddWithValue("@floor", row.DungeonFloor);
                cmd.Parameters.AddWithValue("@mCount", row.MonsterCount);
                cmd.Parameters.AddWithValue("@hasTeam", row.HasTeammates ? 1 : 0);
                foreach (var (column, value) in CombatTallyColumns)
                    cmd.Parameters.AddWithValue("@" + column, (row.Tally == null ? null : value(row.Tally)) ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                CombatRowFailed(ex);
            }
        }

        private void CombatRowFailed(Exception ex)
        {
            lock (_combatRowErrorLock)
            {
                CombatRowFailures++;
                var now = DateTime.UtcNow;
                if (now - _combatRowLastErrorLog < CombatRowErrorLogWindow)
                {
                    _combatRowUnlogged++;
                    return;
                }
                string more = _combatRowUnlogged > 0 ? $" ({_combatRowUnlogged} more failed since the last report)" : "";
                _combatRowLastErrorLog = now;
                _combatRowUnlogged = 0;
                CombatRowErrorLogs++;
                DebugLogger.Instance.LogError("SQL", $"Failed to log combat event: {ex.Message}{more}; further failures are reported at most every {CombatRowErrorLogWindow.TotalMinutes:0} minutes");
            }
        }
    }
}
