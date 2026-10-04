using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UsurperRemake;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v0.57.19: emergency repair pass for bloated save files that won't load.
    ///
    /// Background: v0.57.18 added serialization-time caps for ~16 unbounded
    /// collections that were causing OOM-on-load failures. Those caps only fire
    /// on the *next save* though — pre-existing bloated saves on disk still hold
    /// the original data, and players whose entire backup chain (primary +
    /// backup + 3 autosaves + emergency) is all bloated had no way to bootstrap
    /// the heal cycle: the file couldn't be loaded, so it couldn't be re-saved.
    ///
    /// This module reads a bloated save via JsonDocument (memory cost ~2-3x
    /// file size, vs JsonSerializer.Deserialize which builds a full C# object
    /// graph at ~10x file size) and writes out a trimmed copy with the same
    /// bloated arrays clipped to the v0.57.18 caps. The trimmed file then loads
    /// normally and the regular save path takes over.
    ///
    /// The repair is conservative — it ONLY clips known-bloated arrays. Every
    /// other field is copied verbatim. The player loses some history (extra NPC
    /// memories, old prisoners, etc.) but recovers their character.
    /// </summary>
    public static class SaveFileRepair
    {
        // Map of dotted JSON paths to caps. Paths use [*] to mean "any array
        // index". Match logic does pattern matching against the live path during
        // traversal. Property names are camelCase (matches FileSaveBackend.jsonOptions).
        //
        // Caps mirror the serialization-time caps in SaveSystem.cs / RomanceTracker.cs /
        // CompanionSystem.cs / StrangerEncounterSystem.cs / VisualNovelDialogueSystem.cs /
        // EnhancedNPCBehaviors.cs and the v0.57.16 NPC memory cap. Selection
        // strategy is uniformly "take last N" for arrays — the proper sort
        // strategies (Influence desc for CourtMembers, Importance desc for
        // memories, etc.) require typed C# objects we can't materialize here.
        // Last-N is a safe fallback that recovers the file; subsequent normal
        // saves apply the proper sorts.
        private static readonly Dictionary<string, int> ArrayCaps = new()
        {
            // Per-NPC fields. NPCs is the dominant bloat surface (130+ entries × bloated children).
            { ".npcs[*].memories", 30 },                          // matches MemorySystem.MAX_MEMORIES (v0.57.16)
            { ".npcs[*].recentDialogueIds", GameConfig.MaxSerializedDialogueIdsPerNpc },
            { ".npcs[*].knownCharacters", GameConfig.MaxSerializedKnownCharactersPerNpc },
            { ".npcs[*].enemies", GameConfig.MaxSerializedEnemiesPerNpc },
            { ".npcs[*].inventory", GameConfig.MaxSerializedCompanionInventory }, // reasonable cap for NPC bags
            { ".npcs[*].marketInventory", 50 },                  // shop stock — long lists also bloat

            // RoyalCourt collections.
            { ".royalCourt.prisoners", GameConfig.MaxSerializedRoyalCourtPrisoners },
            { ".royalCourt.orphans", GameConfig.MaxSerializedRoyalCourtOrphans },
            { ".royalCourt.monarchHistory", GameConfig.MaxSerializedMonarchHistory },
            { ".royalCourt.courtMembers", GameConfig.MaxSerializedCourtMembers },
            { ".royalCourt.heirs", GameConfig.MaxSerializedHeirs },
            { ".royalCourt.monsterGuards", GameConfig.MaxSerializedMonsterGuards },

            // RomanceTracker (lives inside player.romanceData).
            { ".player.romanceData.encounterHistory", GameConfig.MaxSerializedEncounterHistory },
            { ".player.romanceData.conversationStates", GameConfig.MaxSerializedConversationStates },
            { ".player.romanceData.conversationStates[*].topicsDiscussed", GameConfig.MaxSerializedTopicsDiscussedPerConvo },

            // Companions (top-level on SaveGameData).
            { ".companions[*].inventory", GameConfig.MaxSerializedCompanionInventory },

            // StrangerEncounters.
            { ".strangerEncounters.usedDialogueIds", GameConfig.MaxSerializedStrangerDialogueIds },
            { ".strangerEncounters.recentGameEvents", GameConfig.MaxSerializedStrangerRecentEvents },

            // Affairs (top-level).
            { ".affairs", GameConfig.MaxSerializedAffairs },
        };

        // Object-as-dict caps. JSON objects with arbitrary string keys (dictionaries
        // serialized as objects). Caps applied by taking the first N enumerated
        // properties — JSON object key order is implementation-defined but stable
        // within a single read, so this is deterministic for a given input file.
        private static readonly Dictionary<string, int> DictCaps = new()
        {
            { ".npcs[*].relationships", GameConfig.MaxSerializedRelationshipsPerNpc },
        };

        public class RepairResult
        {
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
            public long OriginalSizeBytes { get; set; }
            public long RepairedSizeBytes { get; set; }
            public List<string> TrimmedFields { get; set; } = new();
        }

        /// <summary>
        /// Attempt to repair a bloated save file in place. Returns success/failure
        /// and a summary of what was trimmed. The original file is overwritten via
        /// atomic temp+rename — if the repair fails part-way, the original is
        /// untouched.
        /// </summary>
        public static RepairResult RepairInPlace(string filePath, JsonSerializerOptions writeOptions)
        {
            var result = new RepairResult();
            string tempPath = filePath + ".repair.tmp";

            try
            {
                if (!File.Exists(filePath))
                {
                    result.ErrorMessage = Loc.Get("save_repair.not_found", filePath);
                    return result;
                }

                var fi = new FileInfo(filePath);
                result.OriginalSizeBytes = fi.Length;

                DebugLogger.Instance.LogInfo("SAVE_REPAIR",
                    $"Beginning repair of '{filePath}' ({result.OriginalSizeBytes / 1024} KB)");

                // Read raw bytes — File.ReadAllBytes succeeds for files up to ~2 GB
                // even when the equivalent UTF-16 string would OOM. JsonDocument.Parse
                // operates on UTF-8 bytes directly, no string conversion.
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(filePath);
                }
                catch (OutOfMemoryException)
                {
                    result.ErrorMessage = Loc.Get("save_repair.too_large", result.OriginalSizeBytes / (1024 * 1024));
                    return result;
                }

                using var doc = JsonDocument.Parse(bytes);
                var stats = new RepairResult { Success = true };

                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: false))
                using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions
                {
                    Indented = writeOptions?.WriteIndented ?? true,
                    SkipValidation = false
                }))
                {
                    WriteTrimmedElement(writer, doc.RootElement, "", stats);
                    writer.Flush();
                }

                // bytes can be GC'd before atomic rename — release reference.
                bytes = Array.Empty<byte>();

                var ti = new FileInfo(tempPath);
                result.RepairedSizeBytes = ti.Length;
                result.TrimmedFields = stats.TrimmedFields;

                // Atomic rename — same pattern as FileSaveBackend.WriteGameData.
                File.Move(tempPath, filePath, overwrite: true);

                result.Success = true;
                DebugLogger.Instance.LogInfo("SAVE_REPAIR",
                    $"Repair complete: {result.OriginalSizeBytes / 1024} KB -> {result.RepairedSizeBytes / 1024} KB " +
                    $"({result.TrimmedFields.Count} field(s) trimmed)");
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
                DebugLogger.Instance.LogError("SAVE_REPAIR",
                    $"Repair failed for '{filePath}': {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

                // Clean up any partial temp file.
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return result;
            }
        }

        private static void WriteTrimmedElement(Utf8JsonWriter writer, JsonElement element, string path, RepairResult stats)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();

                    // Check if this object should be trimmed as a dictionary (object-as-dict).
                    int? dictCap = LookupCap(DictCaps, path);
                    int objIndex = 0;
                    int objTotal = 0;
                    if (dictCap.HasValue)
                    {
                        // Count first — JsonElement objects don't expose a Count property.
                        foreach (var _ in element.EnumerateObject()) objTotal++;
                    }

                    foreach (var prop in element.EnumerateObject())
                    {
                        if (dictCap.HasValue && objIndex >= dictCap.Value)
                        {
                            // Skip remaining entries; trim recorded once below.
                            objIndex++;
                            continue;
                        }
                        writer.WritePropertyName(prop.Name);
                        WriteTrimmedElement(writer, prop.Value, path + "." + prop.Name, stats);
                        objIndex++;
                    }

                    writer.WriteEndObject();

                    if (dictCap.HasValue && objTotal > dictCap.Value)
                    {
                        stats.TrimmedFields.Add($"{path} (dict {objTotal} -> {dictCap.Value})");
                    }
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();

                    int? arrCap = LookupCap(ArrayCaps, path);
                    int arrTotal = element.GetArrayLength();
                    int skipCount = (arrCap.HasValue && arrTotal > arrCap.Value) ? (arrTotal - arrCap.Value) : 0;

                    int idx = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        if (idx >= skipCount)
                        {
                            WriteTrimmedElement(writer, item, path + "[*]", stats);
                        }
                        idx++;
                    }

                    writer.WriteEndArray();

                    if (skipCount > 0)
                    {
                        stats.TrimmedFields.Add($"{path} (array {arrTotal} -> {arrCap})");
                    }
                    break;

                default:
                    // Strings, numbers, booleans, nulls — copy verbatim.
                    element.WriteTo(writer);
                    break;
            }
        }

        // Cap lookup is path-pattern-based. ArrayCaps and DictCaps use [*] as a
        // wildcard for array indices. The live path uses concrete [*] segments
        // too (we don't track actual indices), so straight dictionary lookup
        // works.
        private static int? LookupCap(Dictionary<string, int> caps, string path)
        {
            if (caps.TryGetValue(path, out int cap)) return cap;
            return null;
        }
    }
}
