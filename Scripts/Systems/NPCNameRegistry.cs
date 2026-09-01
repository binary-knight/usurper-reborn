using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems;

/// <summary>
/// v1.0.4: every display name ever given to an NPC, child, or royal orphan in this
/// world. A name is reserved for good: a permadead NPC's name is never handed to an
/// immigrant or a graduating child, even after PrunePermanentlyDeadNPCs has dropped
/// the corpse from the roster. Seeded from the roster on every restore and persisted
/// (WorldStateData.UsedNPCNames in single-player, world_state "npc_names" online) so
/// the reservation survives restarts. Process-wide: in the MUD server the world sim
/// and every player session share it.
/// </summary>
public static class NPCNameRegistry
{
    private static readonly object _lock = new();
    private static readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsTaken(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        lock (_lock) return _names.Contains(name.Trim());
    }

    /// <summary>Reserve a name. Returns false if it was already reserved.</summary>
    public static bool Reserve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        lock (_lock) return _names.Add(name.Trim());
    }

    /// <summary>Merge-only import; never removes anything, so load order is irrelevant.</summary>
    public static void ReserveAll(IEnumerable<string>? names)
    {
        if (names == null) return;
        lock (_lock)
        {
            foreach (var name in names)
                if (!string.IsNullOrWhiteSpace(name)) _names.Add(name.Trim());
        }
    }

    public static List<string> Export()
    {
        lock (_lock) return _names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static int Count
    {
        get { lock (_lock) return _names.Count; }
    }

    /// <summary>New game or sysop world reset only. Never call from a roster rebuild.</summary>
    public static void Reset()
    {
        lock (_lock) _names.Clear();
    }
}
