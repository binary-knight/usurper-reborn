using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v1.1.13: the world edits log (table world_edits). A process that deletes a character appends an
    /// idempotent edit, applies it and writes it under the records' versions. The owner process (the MUD
    /// server, else the world sim lock holder) re-applies every edit still in the log (v1.1.14: until the prune
    /// deletes it, PruneAppliedDays after it was applied) after it loads or reloads the shared records and before it saves them, and marks an edit
    /// applied only after its own versioned write succeeds. So a stale or old-binary write that brings a
    /// deleted character's grudge, marriage or throne back is undone at the owner's next save.
    /// </summary>
    public static class WorldEditLog
    {
        public const string ForgetCharacter = "forget_character";
        public const string VacateThrone = "vacate_throne";
        public const int PruneAppliedDays = 7;
        // v1.1.14: an edit is re-applied until the prune deletes it (was 24 h), so an old binary's write after a
        // day is still undone. The clock: SQLite's datetime('now') (UTC) against the edit's applied_at, also
        // written by datetime('now'); the set is the prune's complement (SqlSaveBackend.GetWorldEditsToApply)
        public const int ReapplyHours = PruneAppliedDays * 24;
        // v1.1.14: an edit no owner has applied after this long is still reported (ReportUnapplied)
        public const int UnappliedWarningHours = 24;

        public sealed class ForgetCharacterPayload
        {
            [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = new();
            [JsonPropertyName("character_key")] public string CharacterKey { get; set; } = "";
            [JsonPropertyName("deleted_at")] public string DeletedAt { get; set; } = "";   // round-trip local time, as memory times are
            [JsonPropertyName("untimed")] public bool Untimed { get; set; }                 // no other player used the name at the delete
            [JsonPropertyName("character_ids")] public List<string> CharacterIds { get; set; } = new();   // v1.1.13: registry marriages to end
        }

        public sealed class VacateThronePayload
        {
            [JsonPropertyName("king")] public string King { get; set; } = "";
            [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = new();
            [JsonPropertyName("character_key")] public string CharacterKey { get; set; } = "";
        }

        // --- the owner ---

        private static string? _lockOwnerId;

        /// <summary>Tests only: forces the answer of IsOwnerProcess.</summary>
        internal static bool? OwnerOverride { get; set; }

        /// <summary>The world sim of this process runs under this lock id (null: none).</summary>
        public static void NoteLockOwnerId(string? ownerId) => _lockOwnerId = ownerId;

        /// <summary>The name this process writes in created_by and applied_by.</summary>
        public static string ProcessLabel =>
            _lockOwnerId ?? (UsurperRemake.BBS.DoorMode.IsMudServerMode ? "mud" : $"pid_{Environment.ProcessId}");

        /// <summary>The MUD server process, else the process whose world sim holds the lock.</summary>
        public static bool IsOwnerProcess(SqlSaveBackend? sql)
        {
            if (OwnerOverride.HasValue) return OwnerOverride.Value;
            if (UsurperRemake.BBS.DoorMode.IsMudServerMode) return true;
            return sql != null && _lockOwnerId != null && sql.WorldSimLockOwner() == _lockOwnerId;
        }

        // --- appending ---

        public static long AppendForgetCharacter(SqlSaveBackend sql, IEnumerable<string> aliases, string? characterKey, DateTime deletedAt, bool untimed,
            IEnumerable<string>? characterIds = null) =>
            sql.AppendWorldEdit(ForgetCharacter, JsonSerializer.Serialize(new ForgetCharacterPayload
            {
                Aliases = aliases.Where(a => !string.IsNullOrWhiteSpace(a)).ToList(),
                CharacterKey = characterKey ?? "",
                DeletedAt = deletedAt.ToString("o", CultureInfo.InvariantCulture),
                Untimed = untimed,
                CharacterIds = characterIds?.Where(i => !string.IsNullOrWhiteSpace(i)).ToList() ?? new()
            }), ProcessLabel);

        public static long AppendVacateThrone(SqlSaveBackend sql, string king, IEnumerable<string> aliases, string? characterKey) =>
            sql.AppendWorldEdit(VacateThrone, JsonSerializer.Serialize(new VacateThronePayload
            {
                King = king,
                Aliases = aliases.Where(a => !string.IsNullOrWhiteSpace(a)).ToList(),
                CharacterKey = characterKey ?? ""
            }), ProcessLabel);

        // --- applying ---

        /// <summary>
        /// Apply the edits to this process's live world (NPC roster, marriage registry, king). Idempotent:
        /// a second pass changes nothing. Memories are cut off at the delete time, so a later same-name
        /// character's grudges stay; the untimed parts (Enemies, KnownCharacters, impressions, spouses, the
        /// throne) are skipped once a later character uses the name (SqlSaveBackend.LaterCharacterUsesName).
        /// Returns how much changed; endedMarriages collects the NPC ids divorced.
        /// </summary>
        public static int Apply(SqlSaveBackend sql, IEnumerable<WorldEdit> edits, ISet<string>? endedMarriages = null)
        {
            int changed = 0;
            foreach (var edit in edits)
            {
                try
                {
                    if (edit.Kind == ForgetCharacter)
                        changed += ApplyForgetCharacter(sql, edit, endedMarriages);
                    else if (edit.Kind == VacateThrone)
                        changed += ApplyVacateThrone(sql, edit);
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogWarning("WORLD_EDITS", $"Edit {edit.Id} ({edit.Kind}) not applied: {ex.Message}");
                }
            }
            return changed;
        }

        private static int ApplyForgetCharacter(SqlSaveBackend sql, WorldEdit edit, ISet<string>? endedMarriages)
        {
            var p = JsonSerializer.Deserialize<ForgetCharacterPayload>(edit.Payload);
            if (p == null || p.Aliases.Count == 0) return 0;
            var cutOff = ParseDeletedAt(p.DeletedAt);
            if (cutOff == null) return 0;
            // v1.1.13: a later character is one after the delete itself; a queued purge logs its edit later
            bool later = sql.LaterCharacterUsesName(p.Aliases, EarlierSqlTime(edit.CreatedAt, cutOff.Value), p.CharacterKey);
            bool untimed = p.Untimed && !later;
            int n = 0;
            foreach (var a in p.Aliases)
            {
                // v1.1.14: no later character uses the name, so every grudge against it is the deleted
                // character's: forgotten with no time cut-off (an old binary's re-stamped memory time included)
                n += PermadeathHelper.ForgetNpcGrudgesAgainst(a, untimed ? null : cutOff, untimed);
                if (untimed) n += PermadeathHelper.ClearNpcSpousesOf(a, endedMarriages);   // v1.1.13: a spouse name carries no time
            }
            // v1.1.13: the registry pairs by ID, so a marriage is ended even where the NPC's SpouseName is already empty
            n += PermadeathHelper.EndRegistryMarriagesOf(p.CharacterIds, endedMarriages);
            return n;
        }

        private static int ApplyVacateThrone(SqlSaveBackend sql, WorldEdit edit)
        {
            var p = JsonSerializer.Deserialize<VacateThronePayload>(edit.Payload);
            if (p == null || string.IsNullOrWhiteSpace(p.King)) return 0;
            if (sql.LaterCharacterUsesName(p.Aliases.Append(p.King), edit.CreatedAt, p.CharacterKey)) return 0;
            string? shown = p.Aliases.FirstOrDefault(a => !string.Equals(a, p.King, StringComparison.OrdinalIgnoreCase));
            return global::CastleLocation.VacateDeletedKingLocally(p.King, shown) ? 1 : 0;
        }

        /// <summary>v1.1.13: the earlier of the edit's created_at (SQL UTC text) and the delete time, as SQL UTC text.</summary>
        internal static string EarlierSqlTime(string createdAt, DateTime deletedAtLocal)
        {
            string deleted = deletedAtLocal.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(createdAt) || string.CompareOrdinal(deleted, createdAt) < 0 ? deleted : createdAt;
        }

        /// <summary>The delete time as a local time (memory times are DateTime.Now).</summary>
        internal static DateTime? ParseDeletedAt(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)) return null;
            return t.Kind == DateTimeKind.Utc ? t.ToLocalTime() : t;
        }

        /// <summary>
        /// Log, as a warning, every edit no owner has applied within the re-apply window. Such an edit is
        /// never pruned; it stays in the owner's set until an owner's write carries it. Returns the count.
        /// </summary>
        public static int ReportUnapplied(SqlSaveBackend sql)
        {
            var stale = sql.GetUnappliedWorldEditsOlderThan(UnappliedWarningHours);
            if (stale.Count > 0)
                DebugLogger.Instance.LogWarning("WORLD_EDITS",
                    $"{stale.Count} world edit(s) not applied after {UnappliedWarningHours} h: " +
                    string.Join(", ", stale.Select(e => $"#{e.Id} {e.Kind} by {e.CreatedBy} at {e.CreatedAt}")));
            return stale.Count;
        }
    }
}
