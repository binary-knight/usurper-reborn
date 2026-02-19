using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

// THIS FILE BUNDLES LIGHT-WEIGHT COMPATIBILITY SHIMS ONLY – they do **not** implement
// full gameplay behaviour.  They merely unblock the build so that more meaningful
// refactoring work can continue.

#region NPC helpers
// Removed duplicate NPCActionType enum – already defined in NPC.cs

public static class NPCExtensions
{
    public static bool IsAllyOf(this NPC npc, string otherId) => false;
    public static bool IsEnemyOf(this NPC npc, string otherId) => false;
}
#endregion

#region Memory stubs
// Removed duplicate MemoryType, MemoryEvent, and conflicting MemorySystem method stubs – original implementations exist.

public partial class MemorySystem
{
    public MemorySystem() { }
    public MemorySystem(NPC owner) { }
}
#endregion

#region Relationship manager stubs
public partial class RelationshipManager
{
    public RelationshipManager() { }
    public RelationshipManager(NPC owner) { }
    public void UpdateRelationship(string otherId, InteractionType type) { }
    public int GetRelationshipWith(string otherId) => 0;
}
#endregion

#region EmotionalState & GoalSystem extras
public static class EmotionalStateCompat
{
    public static void AddEmotion(this EmotionalState state, EmotionType type, float intensity)
        => state.AddEmotion(type, intensity, 120); // Default 2 hours
}

// Removed duplicate EmotionalState.ProcessInteraction stub – real implementation now lives in EmotionalState.cs

public partial class GoalSystem
{
    public GoalSystem() { personality = null!; }
    public GoalSystem(NPC owner, PersonalityProfile profile) { personality = profile; }
    public void ProcessInteractionFeedback(InteractionType type, Character other, float importance) { }
}
#endregion

#region Character creation helpers
public static class CharacterCompat
{
    public static bool LocationReadOnlyWorkaround(this NPC npc) => true;
}
#endregion

#region Terminal / UI shims
public partial class TerminalEmulator
{
    // Parameterless menu helper returns first option
    public async Task<int> GetMenuChoice() => 0;

    // Removed duplicate WriteLine(ConsoleColor) overload – implemented in TerminalEmulator.cs
}
#endregion

#region ConfigManager overload without key
public static partial class ConfigManager
{
    public static T GetConfig<T>() => default!;

    public class GameSettings
    {
        public int StartingGold { get; set; } = 2000;
        public int StartingTurns { get; set; } = 325;
        public bool DeathPenalty { get; set; } = false;
        public float DeathPenaltyXP { get; set; } = 0.1f;
        public bool PermaDeath { get; set; } = false;
    }

    public static GameSettings GetConfig() => new GameSettings();
}
#endregion

#region NewsSystem helper overloads expecting (header, ansi, cat)
// Removed duplicate NewsSystem.Newsy overloads – similar signatures already present.
#endregion

#region MailSystem quest helpers
// Quest completion stub exists in LegacyCompat
#endregion

#region TeamSystem helpers
public partial class TeamSystem
{
    // Removed obsolete void overloads – async Task versions below provide compatibility.

    // Legacy call initiating an auto-selected gang war for the player's team
    public async Task InitiateGangWar(Character teamLeader)
    {
        // Placeholder – real logic will pair the leader's team against a random enemy team
        await Task.CompletedTask;
    }

    // Result type used by ClaimTown/AbandonTownControl stubs below
    public class TownActionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public async Task<TownActionResult> ClaimTown(Character teamLeader)
    {
        // Placeholder – automatically fail to avoid breaking flow
        await Task.CompletedTask;
        return new TownActionResult { Success = false, Message = "(stub) Town claiming not implemented." };
    }

    public async Task<TownActionResult> AbandonTownControl(Character teamLeader)
    {
        await Task.CompletedTask;
        return new TownActionResult { Success = true, Message = "(stub) Town control abandoned." };
    }
}
#endregion

#region QuestSystem helpers
public partial class QuestSystem
{
    public async Task ShowAvailableQuests(Character player)
    {
        // Placeholder – simply list count of available quests.
        await Task.CompletedTask;
    }
}
#endregion

#region WorldState extra info
public partial class WorldState
{
    public int TimeOfDay { get; set; } = 12;
    public List<string> PlayersInArea { get; set; } = new();
    public List<string> NPCsInArea { get; set; } = new();
    public float DangerLevel { get; set; } = 0.1f;
}
#endregion

#region Location exceptions
public class LocationChangeException : Exception
{
    public string Destination { get; }
    public string NewLocation => Destination;
    public LocationChangeException(string destination) : base($"Change to {destination}") => Destination = destination;
}
#endregion

#region Player helper stubs
public class PlayerSaveData { }
public class PlayerLevelUpTracker { }

public static class CharacterDataManager
{
    public class ClassData { public List<string> SpecialAbilities { get; set; } = new(); }
    public static ClassData? GetClassData(CharacterClass cls) => new ClassData();
}
#endregion

// Quest stub helpers - REMOVED: Now fully implemented in Quest.cs
// Properties OfferedTo, Forced, Penalty, PenaltyType and method CanPlayerClaim moved to Quest.cs

#region CombatEngine helper
public partial class CombatEngine
{
    /// <summary>
    /// Legacy wrapper used by BankLocation – launches a simplified multi-monster fight and always
    /// returns a success result so that gameplay can proceed while the full engine is under
    /// construction.
    /// </summary>
    public CombatResult StartCombat(Character player, List<Monster> enemies, string context, bool allowRetreat = true)
    {
        // Very light-weight simulation: player always wins for now.
        return new CombatResult
        {
            Player = player,
            Monster = enemies.FirstOrDefault(),
            Teammates = new List<Character>(),
            Outcome = CombatOutcome.Victory,
            CombatLog = new List<string> { $"(stub) {context}: automatic victory over {enemies.Count} foes" },
            ExperienceGained = 0,
            GoldGained = 0
        };
    }
}
#endregion 