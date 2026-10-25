using System.Linq;
using UsurperRemake.Systems;
using UsurperRemake.Utils;

/// <summary>
/// 1.4.0 cascade (design CS-140 v2, DOCS/PARTY_SURVIVABILITY_PLAN.md: "records the memory ... and the town
/// talks ... no new state"). When a monster finishes an ally, every other ally in scope in the fight, Downed
/// ones included, saw it. An NPC witness's live world twin records a SawDeath memory whose English description
/// names the fallen ally and whose InvolvedCharacter is empty: RecordEvent moves no impression for an empty
/// name, so the fall itself moves no impression, live or when the memory is replayed at a load.
/// When the player could have rescued the fallen ally and did not (CouldHaveHelped, the left-to-die test), each
/// NPC witness also records a SawCombat memory naming the player: its impression of the player drops by 0.1
/// (MemorySystem.ImpressionChangeFor), and a load replays it, so it lasts until newer memories push it out. A
/// SawCombat is no grudge memory: the street revenge needs a SawDeath, Defeated or Murdered naming the player.
/// Each companion witness loses the left-to-die loyalty step with a line in its history.
/// </summary>
public partial class CombatEngine
{
    /// <summary>Called once from RunFinish, after the fall is counted and before the death handlers.</summary>
    private void RecordFinishWitnesses(Character fallen, Monster finisher, CombatResult result)
    {
        if (fallen == null || result?.Teammates == null) return;
        var player = result.Player;
        bool blamed = player != null && CouldHaveHelped(fallen, player);
        string fallenName = string.IsNullOrEmpty(fallen.Name2) ? fallen.DisplayName : fallen.Name2;
        // stored English, as every memory text is (1.2.5)
        string memoryText = Loc.GetIn("en", "combat.memory_saw_ally_finished", fallenName, finisher?.Name ?? "");
        foreach (var witness in result.Teammates.ToList())
        {
            if (witness == null || ReferenceEquals(witness, fallen) || !IsDownable(witness)) continue;
            if (!string.IsNullOrEmpty(fallen.ID) && witness.ID == fallen.ID) continue;
            if (witness.IsCompanion && witness.CompanionId.HasValue)
            {
                if (blamed)
                    CompanionSystem.Instance.ModifyLoyalty(witness.CompanionId.Value, -GameConfig.AbandonCompanionLoyaltyPenalty,
                        Loc.GetIn("en", "companion.history_saw_ally_left", fallenName));   // stored English, shown through HistoryLabel
                continue;
            }
            // the party copy can be stale (DungeonLocation rebuilds it from the world NPC): write on the live twin,
            // the one the saves carry
            if (string.IsNullOrEmpty(witness.ID)) continue;
            var twin = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == witness.ID);
            var memory = twin?.Brain?.Memory ?? twin?.Memory;
            if (memory == null) continue;
            memory.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.SawDeath,
                Description = memoryText,
                InvolvedCharacter = "",   // named in the description only: no impression moves, now or on a replay at load
                Importance = 0.8f,
                EmotionalImpact = -0.6f,
                Location = player?.CurrentLocation ?? "",
            });
            if (blamed)
                memory.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.SawCombat,
                    Description = Loc.GetIn("en", "combat.memory_saw_ally_left", player!.Name2, fallenName),   // stored English
                    InvolvedCharacter = player.Name2,
                    Importance = 0.6f,
                    EmotionalImpact = -0.4f,
                    Location = player.CurrentLocation ?? "",
                });
        }
    }
}
