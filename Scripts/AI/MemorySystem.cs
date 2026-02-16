using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class MemorySystem
{
    private const int MAX_MEMORIES = 100;
    private const int MEMORY_DECAY_DAYS = 7;
    
    private List<MemoryEvent> memories = new List<MemoryEvent>();
    private Dictionary<string, float> characterImpressions = new Dictionary<string, float>();

    // Public accessors for serialization
    public List<MemoryEvent> AllMemories => memories;
    public Dictionary<string, float> CharacterImpressions => characterImpressions;

    public void RecordEvent(MemoryEvent memoryEvent)
    {
        memoryEvent.Timestamp = DateTime.Now;
        memories.Add(memoryEvent);
        
        // Maintain memory limit
        if (memories.Count > MAX_MEMORIES)
        {
            // Remove least important old memories
            var toRemove = memories
                .Where(m => m.GetAge().TotalDays > MEMORY_DECAY_DAYS / 2)
                .OrderBy(m => m.Importance)
                .Take(memories.Count - MAX_MEMORIES)
                .ToList();
            
            foreach (var memory in toRemove)
            {
                memories.Remove(memory);
            }
        }
        
        // Update character impressions if this involves another character
        if (!string.IsNullOrEmpty(memoryEvent.InvolvedCharacter))
        {
            UpdateCharacterImpression(memoryEvent);
        }
        
        // Reduce console spam: only log memories of notable importance (>=0.5)
        if (memoryEvent.Importance >= 0.5f)
        {
            GD.Print($"[Memory] Recorded: {memoryEvent.Description}");
        }
    }
    
    private void UpdateCharacterImpression(MemoryEvent memoryEvent)
    {
        var characterId = memoryEvent.InvolvedCharacter;
        var impressionChange = CalculateImpressionChange(memoryEvent);
        
        if (!characterImpressions.ContainsKey(characterId))
        {
            characterImpressions[characterId] = 0.0f;
        }
        
        characterImpressions[characterId] += impressionChange;
        
        // Clamp to reasonable bounds
        characterImpressions[characterId] = Math.Max(-1.0f, Math.Min(1.0f, characterImpressions[characterId]));
    }
    
    private float CalculateImpressionChange(MemoryEvent memoryEvent)
    {
        return memoryEvent.Type switch
        {
            MemoryType.Attacked => -0.8f,
            MemoryType.Betrayed => -0.9f,
            MemoryType.Helped => 0.4f,
            MemoryType.SharedItem => 0.2f,
            MemoryType.SharedDrink => 0.1f,
            MemoryType.Defeated => -0.6f,
            MemoryType.Threatened => -0.4f,
            MemoryType.Defended => 0.6f,
            MemoryType.Traded => 0.1f,
            MemoryType.Insulted => -0.3f,
            MemoryType.Complimented => 0.2f,
            MemoryType.Saved => 0.8f,
            MemoryType.Abandoned => -0.5f,
            MemoryType.SocialInteraction => 0.3f,
            // Observation types
            MemoryType.SawDeath => -0.4f,
            MemoryType.SawBetrayat => -0.3f,
            MemoryType.SawCombat => -0.1f,
            // Personal events
            MemoryType.PersonalFailure => -0.3f,
            MemoryType.PersonalAchievement => 0.2f,
            // Economic events
            MemoryType.LostGold => -0.5f,
            MemoryType.GainedGold => 0.1f,
            // Social events
            MemoryType.MadeEnemy => -0.7f,
            MemoryType.MadeFriend => 0.5f,
            MemoryType.Murdered => -1.0f,
            _ => 0.0f
        };
    }
    
    public float GetCharacterImpression(string characterId)
    {
        return characterImpressions.GetValueOrDefault(characterId, 0.0f);
    }
    
    public List<MemoryEvent> GetMemoriesAboutCharacter(string characterId)
    {
        return memories
            .Where(m => m.InvolvedCharacter == characterId)
            .OrderByDescending(m => m.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Get recent memories involving a specific player character
    /// Used by NPCDialogueGenerator for memory-aware dialogue
    /// </summary>
    public List<MemoryEvent> GetRecentMemoriesWithPlayer(Player player, int maxDaysOld = 30, int maxResults = 5)
    {
        if (player == null) return new List<MemoryEvent>();

        var playerName = player.Name2 ?? player.Name1;
        if (string.IsNullOrEmpty(playerName)) return new List<MemoryEvent>();

        var cutoff = DateTime.Now.AddDays(-maxDaysOld);

        return memories
            .Where(m => m.InvolvedCharacter == playerName && m.Timestamp >= cutoff)
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.Timestamp)
            .Take(maxResults)
            .ToList();
    }
    
    public List<MemoryEvent> GetRecentEvents(int hours = 24)
    {
        var cutoff = DateTime.Now.AddHours(-hours);
        return memories
            .Where(m => m.Timestamp >= cutoff)
            .OrderByDescending(m => m.Timestamp)
            .ToList();
    }
    
    public List<MemoryEvent> GetMemoriesOfType(MemoryType type)
    {
        return memories
            .Where(m => m.Type == type)
            .OrderByDescending(m => m.Timestamp)
            .ToList();
    }
    
    public List<MemoryEvent> GetImportantMemories(float threshold = 0.5f)
    {
        return memories
            .Where(m => m.Importance >= threshold)
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.Timestamp)
            .ToList();
    }
    
    public bool HasMemoryOfEvent(MemoryType type, string? characterId = null, int hoursAgo = 24)
    {
        var cutoff = DateTime.Now.AddHours(-hoursAgo);
        
        return memories.Any(m => 
            m.Type == type && 
            m.Timestamp >= cutoff && 
            (characterId == null || m.InvolvedCharacter == characterId));
    }
    
    public void ForgetCharacter(string characterId)
    {
        memories.RemoveAll(m => m.InvolvedCharacter == characterId);
        characterImpressions.Remove(characterId);
    }
    
    public void DecayMemories()
    {
        var cutoff = DateTime.Now.AddDays(-MEMORY_DECAY_DAYS);
        var expiredMemories = memories.Where(m => m.Timestamp < cutoff).ToList();
        
        foreach (var memory in expiredMemories)
        {
            // Gradually reduce importance before complete removal
            memory.Importance *= 0.5f;
            
            if (memory.Importance < 0.1f)
            {
                memories.Remove(memory);
            }
        }
        
        // Gradually decay character impressions
        var charactersToUpdate = characterImpressions.Keys.ToList();
        foreach (var characterId in charactersToUpdate)
        {
            characterImpressions[characterId] *= 0.99f; // Slow decay
            
            // Remove very weak impressions
            if (Math.Abs(characterImpressions[characterId]) < 0.05f)
            {
                characterImpressions.Remove(characterId);
            }
        }
    }
    
    /// <summary>
    /// Convenience method for adding simple memories with description and type
    /// Used throughout the codebase for backward compatibility
    /// </summary>
    public void AddMemory(string description, string typeString, DateTime timestamp)
    {
        // Convert string type to MemoryType enum
        var memoryType = typeString.ToLower() switch
        {
            "combat" => MemoryType.SawCombat,
            "movement" => MemoryType.LocationChange,
            "inventory" => MemoryType.GainedItem,
            "equipment" => MemoryType.GainedItem,
            "purchase" => MemoryType.BoughtItem,
            "social" => MemoryType.ActivityChange,
            "gang" => MemoryType.JoinedGang,
            "faith" => MemoryType.PersonalAchievement,
            "romance" => MemoryType.ActivityChange,
            _ => MemoryType.Miscellaneous
        };
        
        var memoryEvent = new MemoryEvent
        {
            Type = memoryType,
            Description = description,
            Timestamp = timestamp,
            Importance = 0.5f
        };
        
        RecordEvent(memoryEvent);
    }
    
    public int GetMemoryCount()
    {
        return memories.Count;
    }
    
    public List<string> GetKnownCharacters()
    {
        return characterImpressions.Keys.ToList();
    }
    
    public string GetMemorySummary()
    {
        var summary = $"Memories: {memories.Count}/{MAX_MEMORIES}\n";
        summary += $"Known Characters: {characterImpressions.Count}\n";
        
        var recentCount = GetRecentEvents(24).Count;
        summary += $"Recent Events (24h): {recentCount}\n";
        
        var importantCount = GetImportantMemories(0.7f).Count;
        summary += $"Important Memories: {importantCount}";
        
        return summary;
    }

    // Stub for legacy compatibility: previously processed queued memories elsewhere
    public void ProcessNewMemories()
    {
        // In this simplified port we process memories immediately in RecordEvent,
        // so there is nothing to do here. Method left intentionally blank for
        // compatibility with older NPC update loops.
    }
}

public class MemoryEvent
{
    public MemoryType Type { get; set; }
    public string Description { get; set; }
    public DateTime Timestamp { get; set; }
    public string InvolvedCharacter { get; set; } // Character ID
    public string Location { get; set; }
    public float Importance { get; set; } = 0.5f; // 0.0 to 1.0
    public float EmotionalImpact { get; set; } = 0f; // -1.0 (negative) to 1.0 (positive)
    public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();
    
    public TimeSpan GetAge()
    {
        return DateTime.Now - Timestamp;
    }
    
    public bool IsRecent(int hours = 24)
    {
        return GetAge().TotalHours <= hours;
    }
    
    public override string ToString()
    {
        var ageText = GetAge().TotalHours < 24 
            ? $"{GetAge().TotalHours:F0}h ago"
            : $"{GetAge().TotalDays:F0}d ago";
            
        return $"[{ageText}] {Description} (Importance: {Importance:F2})";
    }
}

public enum MemoryType
{
    // Interaction types
    Attacked,
    Betrayed,
    Helped,
    SharedItem,
    SharedDrink,
    Defeated,
    Threatened,
    Defended,
    Traded,
    Insulted,
    Complimented,
    Saved,
    Abandoned,
    
    // Observation types
    SawCombat,
    SawDeath,
    SawBetrayat,
    WitnessedEvent,
    
    // Personal events
    PersonalAchievement,
    PersonalFailure,
    LevelUp,
    GainedItem,
    LostItem,
    
    // Location events
    LocationChange,
    ActivityChange,
    StateChange,
    
    // Social events
    JoinedGang,
    LeftGang,
    MadeEnemy,
    MadeFriend,
    Murdered,
    SocialInteraction,
    
    // Economic events
    BoughtItem,
    SoldItem,
    GainedGold,
    LostGold,
    
    // Quest/Goal events
    StartedQuest,
    CompletedQuest,
    FailedQuest,
    
    // Other
    Miscellaneous
} 
