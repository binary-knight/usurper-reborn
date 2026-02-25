using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class RelationshipManager
{
    private Dictionary<string, Relationship> relationships = new Dictionary<string, Relationship>();
    
    public void SetRelationship(string characterId, RelationshipType type)
    {
        if (!relationships.ContainsKey(characterId))
        {
            relationships[characterId] = new Relationship(characterId);
        }
        
        var oldType = relationships[characterId].Type;
        relationships[characterId].Type = type;
        relationships[characterId].LastUpdated = DateTime.Now;
        
        if (oldType != type)
        {
        }
    }
    
    public Relationship? GetRelationship(string characterId)
    {
        return relationships.GetValueOrDefault(characterId);
    }
    
    public void UpdateRelationship(string characterId, MemoryEvent memoryEvent)
    {
        if (!relationships.ContainsKey(characterId))
        {
            relationships[characterId] = new Relationship(characterId);
        }
        
        var relationship = relationships[characterId];
        var impactValue = CalculateRelationshipImpact(memoryEvent);
        
        relationship.AddInteraction(memoryEvent, impactValue);
        
        // Update relationship type based on cumulative value
        var newType = DetermineRelationshipType(relationship.GetTotalValue());
        if (newType != relationship.Type)
        {
            SetRelationship(characterId, newType);
        }
    }
    
    private float CalculateRelationshipImpact(MemoryEvent memoryEvent)
    {
        return memoryEvent.Type switch
        {
            MemoryType.Attacked => -0.8f,
            MemoryType.Betrayed => -1.0f,
            MemoryType.Helped => 0.6f,
            MemoryType.Defended => 0.8f,
            MemoryType.SharedDrink => 0.2f,
            MemoryType.SharedItem => 0.3f,
            MemoryType.Traded => 0.1f,
            MemoryType.Complimented => 0.2f,
            MemoryType.Insulted => -0.3f,
            MemoryType.Threatened => -0.5f,
            MemoryType.Saved => 1.0f,
            MemoryType.Abandoned => -0.6f,
            _ => 0.0f
        };
    }
    
    private RelationshipType DetermineRelationshipType(float totalValue)
    {
        return totalValue switch
        {
            >= 2.0f => RelationshipType.Brother,
            >= 1.2f => RelationshipType.CloseFriend,
            >= 0.5f => RelationshipType.Friend,
            >= 0.1f => RelationshipType.Acquaintance,
            >= -0.1f => RelationshipType.Neutral,
            >= -0.5f => RelationshipType.Dislike,
            >= -1.0f => RelationshipType.Enemy,
            >= -2.0f => RelationshipType.Nemesis,
            _ => RelationshipType.Feared
        };
    }
    
    public List<string> GetAllies()
    {
        return relationships
            .Where(kvp => IsPositiveRelationship(kvp.Value.Type))
            .Select(kvp => kvp.Key)
            .ToList();
    }
    
    public List<string> GetEnemies()
    {
        return relationships
            .Where(kvp => IsNegativeRelationship(kvp.Value.Type))
            .Select(kvp => kvp.Key)
            .ToList();
    }
    
    public List<string> GetNeutrals()
    {
        return relationships
            .Where(kvp => IsNeutralRelationship(kvp.Value.Type))
            .Select(kvp => kvp.Key)
            .ToList();
    }
    
    private bool IsPositiveRelationship(RelationshipType type)
    {
        return type == RelationshipType.Brother ||
               type == RelationshipType.CloseFriend ||
               type == RelationshipType.Friend;
    }
    
    private bool IsNegativeRelationship(RelationshipType type)
    {
        return type == RelationshipType.Enemy ||
               type == RelationshipType.Nemesis ||
               type == RelationshipType.Feared;
    }
    
    private bool IsNeutralRelationship(RelationshipType type)
    {
        return type == RelationshipType.Neutral ||
               type == RelationshipType.Acquaintance ||
               type == RelationshipType.Stranger ||
               type == RelationshipType.Dislike;
    }
    
    public void DecayRelationships()
    {
        var toRemove = new List<string>();
        
        foreach (var kvp in relationships.ToList())
        {
            var relationship = kvp.Value;
            var timeSinceUpdate = DateTime.Now - relationship.LastUpdated;
            
            // Decay relationships over time
            if (timeSinceUpdate.TotalDays > 30) // After 30 days
            {
                relationship.DecayValue(0.1f);
                
                // Remove very weak relationships
                if (Math.Abs(relationship.GetTotalValue()) < 0.1f)
                {
                    toRemove.Add(kvp.Key);
                }
            }
        }
        
        foreach (var characterId in toRemove)
        {
            relationships.Remove(characterId);
        }
    }
    
    public string GetRelationshipSummary()
    {
        if (relationships.Count == 0)
        {
            return "No known relationships";
        }
        
        var allies = GetAllies().Count;
        var enemies = GetEnemies().Count;
        var neutrals = GetNeutrals().Count;
        
        return $"Relationships: {allies} allies, {enemies} enemies, {neutrals} others";
    }
    
    public List<Relationship> GetStrongestRelationships(int count = 5)
    {
        return relationships.Values
            .OrderByDescending(r => Math.Abs(r.GetTotalValue()))
            .Take(count)
            .ToList();
    }

    // Legacy compatibility helper – no-op for now.
    public void UpdateRelationships()
    {
        // In the original Pascal code this performed periodic decay/maintenance.
        // For the port we call DecayRelationships() elsewhere, so keep this as a stub.
    }
}

public class Relationship
{
    public string CharacterId { get; set; }
    public RelationshipType Type { get; set; } = RelationshipType.Stranger;
    public DateTime FirstMet { get; set; } = DateTime.Now;
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public List<RelationshipEvent> InteractionHistory { get; set; } = new List<RelationshipEvent>();
    
    public Relationship(string characterId)
    {
        CharacterId = characterId;
    }
    
    public void AddInteraction(MemoryEvent memoryEvent, float impactValue)
    {
        var relationshipEvent = new RelationshipEvent
        {
            MemoryType = memoryEvent.Type,
            Impact = impactValue,
            Timestamp = memoryEvent.Timestamp,
            Description = memoryEvent.Description
        };
        
        InteractionHistory.Add(relationshipEvent);
        LastUpdated = DateTime.Now;
        
        // Keep only recent interactions (last 50)
        if (InteractionHistory.Count > 50)
        {
            InteractionHistory = InteractionHistory
                .OrderByDescending(e => e.Timestamp)
                .Take(50)
                .ToList();
        }
    }
    
    public float GetTotalValue()
    {
        var cutoffDate = DateTime.Now.AddDays(-30); // Only consider recent interactions
        
        return InteractionHistory
            .Where(e => e.Timestamp >= cutoffDate)
            .Sum(e => e.Impact);
    }
    
    public void DecayValue(float decayAmount)
    {
        // Reduce the impact of all interactions slightly
        foreach (var interaction in InteractionHistory)
        {
            interaction.Impact *= (1.0f - decayAmount);
        }
    }
    
    public RelationshipType GetRelationshipType()
    {
        return Type;
    }
    
    public bool IsPositive()
    {
        return GetTotalValue() > 0.1f;
    }
    
    public bool IsNegative()
    {
        return GetTotalValue() < -0.1f;
    }
    
    public TimeSpan GetRelationshipAge()
    {
        return DateTime.Now - FirstMet;
    }
    
    public override string ToString()
    {
        var value = GetTotalValue();
        var age = GetRelationshipAge();
        return $"{CharacterId}: {Type} (Value: {value:F2}, Age: {age.Days}d)";
    }
}

public class RelationshipEvent
{
    public MemoryType MemoryType { get; set; }
    public float Impact { get; set; }
    public DateTime Timestamp { get; set; }
    public string Description { get; set; } = "";

    public bool IsRecent(int days = 7)
    {
        return DateTime.Now.Subtract(Timestamp).TotalDays <= days;
    }
}

public enum RelationshipType
{
    Stranger,      // Never met or minimal interaction
    Acquaintance,  // Known but not well
    Neutral,       // Neutral relationship
    Dislike,       // Mild negative feelings
    Friend,        // Positive relationship
    CloseFriend,   // Strong positive relationship
    Brother,       // Extremely close ally
    Enemy,         // Active hostility
    Nemesis,       // Deep hatred and rivalry
    Feared         // One fears the other significantly
} 
