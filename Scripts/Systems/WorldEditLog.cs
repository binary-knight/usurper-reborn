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
    /// server, else the world sim lock holder) re-applies every unapplied edit and every edit of the last
    /// 24 hours after it loads or reloads the shared records and before it saves them, and marks an edit
    /// applied only after its own versioned write succeeds. So a stale or old-binary write that brings a
    /// deleted character's grudge, marriage or throne back is undone at the owner's next save.
    /// </summary>
    public static class WorldEditLog
    {
        public const string ForgetCharacter = "forget_character";
        public const string VacateThrone = "vacate_throne";
        public const int ReapplyHours = 24;
        public const int PruneAppliedDays = 7;

        public sealed class ForgetCharacterPayload
        {
            [JsonPropertyName("aliases")] public List<string> Aliases { get; set; } = new();
            [JsonPropertyName("character_key")] public string CharacterKey { get; set; } = "";
            [JsonPropertyName("deleted_at")] public string DeletedAt { get; set; } = "";   // round-trip local time, as memory times are
            [JsonPropertyName("untimed")] public bool Untimed { get; set; }                 // no other player used the name at the delete
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

        public static long AppendForgetCharacter(SqlSaveBackend sql, IEnumerable<string> aliases, string? characterKey, DateTime deletedAt, bool untimed) =>
            sql.AppendWorldEdit(ForgetCharacter, JsonSerializer.Serialize(new ForgetCharacterPayload
            {
                Aliases = aliases.Where(a => !string.IsNullOrWhiteSpace(a)).ToList(),
                CharacterKey = characterKey ?? "",
                DeletedAt = deletedAt.ToString("o", CultureInfo.InvariantCulture),
                Untimed = untimed
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
            bool later = sql.LaterCharacterUsesName(p.Aliases, edit.CreatedAt, p.CharacterKey);
            int n = 0;
            foreach (var a in p.Aliases)
            {
                n += PermadeathHelper.ForgetNpcGrudgesAgainst(a, cutOff, p.Untimed && !later);
                if (!later) n += PermadeathHelper.ClearNpcSpousesOf(a, endedMarriages);
            }
            return n;
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
            var stale = sql.GetUnappliedWorldEditsOlderThan(ReapplyHours);
            if (stale.Count > 0)
                DebugLogger.Instance.LogWarning("WORLD_EDITS",
                    $"{stale.Count} world edit(s) not applied after {ReapplyHours} h: " +
                    string.Join(", ", stale.Select(e => $"#{e.Id} {e.Kind} by {e.CreatedBy} at {e.CreatedAt}")));
            return stale.Count;
        }
    }
}
