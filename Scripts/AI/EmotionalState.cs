using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class EmotionalState
{
    private Dictionary<EmotionType, Emotion> activeEmotions = new Dictionary<EmotionType, Emotion>();
    private const int MAX_EMOTIONS = 12;
    
    public void AddEmotion(EmotionType type, float intensity, int durationMinutes)
    {
        intensity = Math.Max(0.0f, Math.Min(1.0f, intensity));
        
        if (activeEmotions.ContainsKey(type))
        {
            // Combine with existing emotion
            var existing = activeEmotions[type];
            existing.Intensity = Math.Min(1.0f, existing.Intensity + intensity * 0.5f);
            existing.Duration = Math.Max(existing.Duration, durationMinutes);
            existing.StartTime = DateTime.Now; // Reset timer
        }
        else
        {
            // Add new emotion
            activeEmotions[type] = new Emotion
            {
                Type = type,
                Intensity = intensity,
                Duration = durationMinutes,
                StartTime = DateTime.Now
            };
        }
        
        // Remove weakest emotion if we have too many
        if (activeEmotions.Count > MAX_EMOTIONS)
        {
            var weakest = activeEmotions.Values.OrderBy(e => e.Intensity).First();
            activeEmotions.Remove(weakest.Type);
        }
        
        GD.Print($"[Emotions] Added {type} emotion (Intensity: {intensity:F2}, Duration: {durationMinutes}m)");
    }
    
    public void Update(List<MemoryEvent> recentEvents)
    {
        // Remove expired emotions
        var expiredEmotions = activeEmotions
            .Where(kvp => kvp.Value.IsExpired())
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var emotionType in expiredEmotions)
        {
            activeEmotions.Remove(emotionType);
        }
        
        // Generate emotions based on recent events
        GenerateEmotionsFromEvents(recentEvents);
        
        // Gradually decay all emotions
        foreach (var emotion in activeEmotions.Values)
        {
            emotion.Intensity *= 0.99f; // Very slow decay
        }
    }
    
    private void GenerateEmotionsFromEvents(List<MemoryEvent> recentEvents)
    {
        var recentImportantEvents = recentEvents
            .Where(e => e.IsRecent(2) && e.Importance > 0.5f) // Last 2 hours, important events
            .ToList();
            
        foreach (var memoryEvent in recentImportantEvents)
        {
            var emotionInfo = GetEmotionFromEvent(memoryEvent);
            if (emotionInfo.HasValue)
            {
                AddEmotion(emotionInfo.Value.Type, emotionInfo.Value.Intensity, emotionInfo.Value.Duration);
            }
        }
    }
    
    private (EmotionType Type, float Intensity, int Duration)? GetEmotionFromEvent(MemoryEvent memoryEvent)
    {
        return memoryEvent.Type switch
        {
            MemoryType.Attacked => (EmotionType.Anger, 0.8f, 120), // 2 hours of anger
            MemoryType.Betrayed => (EmotionType.Anger, 1.0f, 300), // 5 hours of intense anger
            MemoryType.Helped => (EmotionType.Gratitude, 0.6f, 180), // 3 hours of gratitude
            MemoryType.Defended => (EmotionType.Gratitude, 0.8f, 240), // 4 hours of strong gratitude
            MemoryType.Saved => (EmotionType.Gratitude, 1.0f, 360), // 6 hours of intense gratitude
            MemoryType.Threatened => (EmotionType.Fear, 0.6f, 90), // 1.5 hours of fear
            MemoryType.PersonalAchievement => (EmotionType.Confidence, 0.7f, 300), // 5 hours of confidence
            MemoryType.PersonalFailure => (EmotionType.Sadness, 0.5f, 180), // 3 hours of sadness
            MemoryType.GainedGold => (EmotionType.Greed, 0.4f, 60), // 1 hour of increased greed
            MemoryType.LostGold => (EmotionType.Anger, 0.5f, 120), // 2 hours of anger
            MemoryType.SawDeath => (EmotionType.Fear, 0.7f, 240), // 4 hours of fear
            MemoryType.MadeFriend => (EmotionType.Joy, 0.6f, 180), // 3 hours of joy
            MemoryType.MadeEnemy => (EmotionType.Anger, 0.5f, 150), // 2.5 hours of anger
            _ => null
        };
    }
    
    public float GetActionModifier(ActionType actionType)
    {
        var modifier = 1.0f;
        
        foreach (var emotion in activeEmotions.Values)
        {
            modifier *= emotion.Type switch
            {
                EmotionType.Anger => actionType switch
                {
                    ActionType.Attack => 1.5f + emotion.Intensity * 0.5f,
                    ActionType.Socialize => 0.5f - emotion.Intensity * 0.3f,
                    ActionType.Trade => 0.8f - emotion.Intensity * 0.2f,
                    _ => 1.0f
                },
                EmotionType.Fear => actionType switch
                {
                    ActionType.Attack => 0.3f - emotion.Intensity * 0.2f,
                    ActionType.Flee => 1.8f + emotion.Intensity * 0.7f,
                    ActionType.Rest => 1.3f + emotion.Intensity * 0.3f,
                    ActionType.Explore => 0.4f - emotion.Intensity * 0.3f,
                    _ => 1.0f
                },
                EmotionType.Confidence => actionType switch
                {
                    ActionType.Attack => 1.3f + emotion.Intensity * 0.2f,
                    ActionType.Socialize => 1.2f + emotion.Intensity * 0.3f,
                    ActionType.Train => 1.4f + emotion.Intensity * 0.3f,
                    ActionType.Explore => 1.2f + emotion.Intensity * 0.2f,
                    _ => 1.0f
                },
                EmotionType.Sadness => actionType switch
                {
                    ActionType.Rest => 1.5f + emotion.Intensity * 0.4f,
                    ActionType.Socialize => 0.6f - emotion.Intensity * 0.4f,
                    ActionType.Attack => 0.7f - emotion.Intensity * 0.3f,
                    _ => 1.0f
                },
                EmotionType.Greed => actionType switch
                {
                    ActionType.Trade => 1.4f + emotion.Intensity * 0.4f,
                    ActionType.Steal => 1.6f + emotion.Intensity * 0.5f,
                    ActionType.Help => 0.5f - emotion.Intensity * 0.3f,
                    _ => 1.0f
                },
                EmotionType.Joy => actionType switch
                {
                    ActionType.Socialize => 1.3f + emotion.Intensity * 0.3f,
                    ActionType.Help => 1.2f + emotion.Intensity * 0.2f,
                    ActionType.Attack => 0.8f - emotion.Intensity * 0.2f,
                    _ => 1.0f
                },
                EmotionType.Gratitude => actionType switch
                {
                    ActionType.Help => 1.5f + emotion.Intensity * 0.4f,
                    ActionType.Socialize => 1.2f + emotion.Intensity * 0.2f,
                    ActionType.Attack => 0.6f - emotion.Intensity * 0.3f,
                    _ => 1.0f
                },
                EmotionType.Loneliness => actionType switch
                {
                    ActionType.Socialize => 1.6f + emotion.Intensity * 0.5f,
                    ActionType.JoinGang => 1.4f + emotion.Intensity * 0.4f,
                    ActionType.Rest => 0.8f - emotion.Intensity * 0.2f,
                    _ => 1.0f
                },
                _ => 1.0f
            };
        }
        
        return Math.Max(0.1f, Math.Min(3.0f, modifier)); // Clamp between 0.1 and 3.0
    }
    
    public Dictionary<EmotionType, Emotion> GetActiveEmotions()
    {
        return activeEmotions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }
    
    public bool HasEmotion(EmotionType type)
    {
        return activeEmotions.ContainsKey(type) && !activeEmotions[type].IsExpired();
    }
    
    public float GetEmotionIntensity(EmotionType type)
    {
        return HasEmotion(type) ? activeEmotions[type].GetCurrentIntensity() : 0.0f;
    }
    
    public EmotionType? GetDominantEmotion()
    {
        var strongest = activeEmotions.Values
            .Where(e => !e.IsExpired())
            .OrderByDescending(e => e.Intensity)
            .FirstOrDefault();
            
        return strongest?.Type;
    }
    
    /// <summary>
    /// Adjust mood based on an emotion type and intensity
    /// Used by NPCBrain for mood management
    /// </summary>
    public void AdjustMood(EmotionType emotionType, float intensity)
    {
        AddEmotion(emotionType, intensity);
    }
    
    /// <summary>
    /// Adjust mood based on emotion name and intensity
    /// Overload for string-based emotion types
    /// </summary>
    public void AdjustMood(string emotionName, float intensity)
    {
        if (Enum.TryParse<EmotionType>(emotionName, true, out var emotionType))
        {
            AddEmotion(emotionType, intensity);
        }
    }
    
    /// <summary>
    /// Get a numeric mood score where 0.0 is very negative and 1.0 is very positive.
    /// This provides a simple aggregate suitable for quick comparisons (e.g. <c>&lt; 0.3f</c>)
    /// used by higher-level AI code. The calculation is intentionally lightweight – we
    /// average intensities of emotions, counting traditionally "positive" emotions as
    /// positive contributions and "negative" emotions as negative contributions.
    /// </summary>
    public float GetCurrentMood()
    {
        if (activeEmotions.Count == 0) return 0.5f; // Neutral baseline

        float total = 0f;
        foreach (var kvp in activeEmotions)
        {
            var sign = kvp.Key switch
            {
                EmotionType.Joy or EmotionType.Confidence or EmotionType.Gratitude or EmotionType.Hope or EmotionType.Peace => 1f,
                EmotionType.Anger or EmotionType.Fear or EmotionType.Sadness or EmotionType.Greed or EmotionType.Loneliness or EmotionType.Envy => -1f,
                _ => 0f
            };

            total += kvp.Value.Intensity * sign;
        }

        // Map from [-1,1] range to [0,1]
        var normalized = (total / activeEmotions.Count + 1f) / 2f;
        return Math.Clamp(normalized, 0f, 1f);
    }
    
    /// <summary>
    /// Handle interaction feedback from the NPC brain to optionally modify emotions.
    /// For now this is a lightweight stub that boosts or reduces certain emotions
    /// based on the interaction type – primarily to satisfy compiler references.
    /// </summary>
    public void ProcessInteraction(InteractionType type, Character other, float importance)
    {
        // Very rough heuristic – expand later if needed
        switch (type)
        {
            case InteractionType.Attacked:
            case InteractionType.Betrayed:
            case InteractionType.Insulted:
            case InteractionType.Threatened:
                AddEmotion(EmotionType.Anger, Math.Min(1f, importance), 120);
                break;
            case InteractionType.Helped:
            case InteractionType.Defended:
            case InteractionType.Complimented:
                AddEmotion(EmotionType.Gratitude, Math.Min(1f, importance), 120);
                break;
            case InteractionType.SharedDrink:
            case InteractionType.SharedItem:
            case InteractionType.Traded:
                AddEmotion(EmotionType.Joy, Math.Min(0.5f, importance), 60);
                break;
            default:
                // No specific emotional reaction handled
                break;
        }
    }
    
    public string GetEmotionalSummary()
    {
        var activeEmotionsList = activeEmotions.Values
            .Where(e => !e.IsExpired())
            .OrderByDescending(e => e.Intensity)
            .Take(3)
            .ToList();
            
        if (!activeEmotionsList.Any())
        {
            return "Calm and composed";
        }
        
        var summary = "Feeling: ";
        summary += string.Join(", ", activeEmotionsList.Select(e => 
            $"{e.Type} ({e.Intensity * 100:F0}%)"));
            
        return summary;
    }
    
    public bool IsEmotionallyStable()
    {
        var totalIntensity = activeEmotions.Values.Sum(e => e.Intensity);
        return totalIntensity < 1.5f; // Arbitrary threshold for "stable"
    }
    
    public void ClearEmotion(EmotionType type)
    {
        activeEmotions.Remove(type);
    }
    
    public void ClearAllEmotions()
    {
        activeEmotions.Clear();
    }
}

public class Emotion
{
    public EmotionType Type { get; set; }
    public float Intensity { get; set; } // 0.0 to 1.0
    public int Duration { get; set; } // in minutes
    public DateTime StartTime { get; set; }
    
    public bool IsExpired()
    {
        return DateTime.Now.Subtract(StartTime).TotalMinutes >= Duration;
    }
    
    public int RemainingMinutes()
    {
        var elapsed = DateTime.Now.Subtract(StartTime).TotalMinutes;
        return Math.Max(0, Duration - (int)elapsed);
    }
    
    public float GetCurrentIntensity()
    {
        if (IsExpired()) return 0.0f;
        
        // Emotions fade over time
        var elapsed = DateTime.Now.Subtract(StartTime).TotalMinutes;
        var fadeRatio = 1.0f - (float)(elapsed / Duration);
        
        return Intensity * fadeRatio;
    }
    
    public override string ToString()
    {
        return $"{Type}: {GetCurrentIntensity() * 100:F0}% ({RemainingMinutes()}m left)";
    }
}

public enum EmotionType
{
    Anger,       // Increases aggression, reduces cooperation
    Fear,        // Increases flight response, reduces risk-taking
    Joy,         // Increases sociability, reduces aggression
    Sadness,     // Reduces activity, increases rest seeking
    Confidence,  // Increases risk-taking and goal pursuit
    Greed,       // Increases economic focus, reduces generosity
    Gratitude,   // Increases helping behavior toward specific character
    Loneliness,  // Increases social seeking behavior
    Envy,        // Increases competitive behavior
    Pride,       // Increases ambitious behavior, reduces cooperation
    Hope,        // Increases perseverance, reduces despair
    Peace        // Reduces aggression, increases cooperation
} 
