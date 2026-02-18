using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Cultural Meme System — Ideas and trends that spread through NPC conversations.
/// Different from opinion propagation (which is about people); memes are about concepts.
/// They influence NPC activity weights, creating observable cultural shifts.
/// Inspired by Project Sid (Altera.AL, 2024) cultural transmission experiments.
/// </summary>
public class CulturalMemeSystem
{
    private static CulturalMemeSystem? _instance;
    public static CulturalMemeSystem? Instance => _instance;

    // Active memes in the world
    private List<CulturalMeme> _activeMemes = new();
    private const int MAX_ACTIVE_MEMES = 15;

    // Per-NPC meme awareness: npcName -> set of meme IDs they've heard
    private Dictionary<string, HashSet<string>> _npcMemeAwareness = new();

    // Per-location meme popularity: location -> (memeId -> strength 0-1)
    private Dictionary<string, Dictionary<string, float>> _locationMemeStrength = new();

    private readonly Random _random = new();
    private int _lastMemeGenerationTick = 0;

    // Social locations where memes spread
    private static readonly string[] SocialLocations = new[]
    {
        "Inn", "Main Street", "Temple", "Love Street", "Auction House",
        "Church", "Castle", "Armor Shop", "Weapon Shop"
    };

    public CulturalMemeSystem()
    {
        _instance = this;
    }

    // ========================================================================
    // Meme Templates — predefined memes that can be activated by world state
    // ========================================================================

    private static readonly CulturalMemeTemplate[] MemeTemplates = new[]
    {
        // Danger memes
        new CulturalMemeTemplate("dungeon_danger", "Dungeon Peril", "Tales of dungeon horrors spread",
            MemeCategory.Danger, new Dictionary<string, float> { ["dungeon"] = 0.6f, ["heal"] = 1.3f, ["train"] = 1.2f }),
        new CulturalMemeTemplate("bandit_fear", "Bandit Scare", "Fear of bandits grips the town",
            MemeCategory.Danger, new Dictionary<string, float> { ["dungeon"] = 0.7f, ["castle"] = 1.3f, ["train"] = 1.2f }),
        new CulturalMemeTemplate("plague_dread", "Plague Dread", "Whispers of sickness fill the air",
            MemeCategory.Danger, new Dictionary<string, float> { ["temple"] = 1.5f, ["heal"] = 1.4f, ["inn"] = 0.7f }),

        // Prosperity memes
        new CulturalMemeTemplate("gold_rush", "Gold Rush", "Everyone's chasing fortune",
            MemeCategory.Prosperity, new Dictionary<string, float> { ["shop"] = 1.4f, ["bank"] = 1.5f, ["marketplace"] = 1.3f, ["dungeon"] = 1.2f }),
        new CulturalMemeTemplate("merchant_bounty", "Merchant's Bounty", "The merchants are generous today",
            MemeCategory.Prosperity, new Dictionary<string, float> { ["shop"] = 1.5f, ["marketplace"] = 1.4f }),
        new CulturalMemeTemplate("crafting_craze", "Crafting Craze", "A passion for craftsmanship sweeps the town",
            MemeCategory.Prosperity, new Dictionary<string, float> { ["shop"] = 1.3f, ["train"] = 1.2f }),

        // Faith memes
        new CulturalMemeTemplate("divine_blessing", "Divine Blessing", "The gods smile upon the faithful",
            MemeCategory.Faith, new Dictionary<string, float> { ["temple"] = 1.8f, ["heal"] = 1.3f }),
        new CulturalMemeTemplate("spiritual_awakening", "Spiritual Awakening", "A wave of devotion sweeps the realm",
            MemeCategory.Faith, new Dictionary<string, float> { ["temple"] = 1.6f, ["church"] = 1.5f, ["dark_alley"] = 0.7f }),
        new CulturalMemeTemplate("holy_pilgrimage", "Holy Pilgrimage", "Pilgrims flock to the temple",
            MemeCategory.Faith, new Dictionary<string, float> { ["temple"] = 1.7f, ["move"] = 1.2f }),

        // Unrest memes
        new CulturalMemeTemplate("tax_outrage", "Tax Outrage", "Anger over the king's taxes grows",
            MemeCategory.Unrest, new Dictionary<string, float> { ["castle"] = 0.5f, ["dark_alley"] = 1.5f, ["inn"] = 1.3f }),
        new CulturalMemeTemplate("throne_doubt", "Throne Doubt", "Doubts about the ruler spread",
            MemeCategory.Unrest, new Dictionary<string, float> { ["castle"] = 1.4f, ["dark_alley"] = 1.3f }),
        new CulturalMemeTemplate("freedom_call", "Call to Freedom", "Voices cry out for liberty",
            MemeCategory.Unrest, new Dictionary<string, float> { ["dark_alley"] = 1.6f, ["castle"] = 0.6f }),

        // Social memes
        new CulturalMemeTemplate("festival_spirit", "Festival Spirit", "A festive mood fills the town",
            MemeCategory.Social, new Dictionary<string, float> { ["inn"] = 1.5f, ["love_street"] = 1.4f, ["marketplace"] = 1.3f }),
        new CulturalMemeTemplate("love_season", "Love Season", "Romance is in the air",
            MemeCategory.Social, new Dictionary<string, float> { ["love_street"] = 1.8f, ["inn"] = 1.3f }),
        new CulturalMemeTemplate("dance_craze", "Dance Craze", "A new dance has everyone moving",
            MemeCategory.Social, new Dictionary<string, float> { ["inn"] = 1.5f, ["love_street"] = 1.3f }),
        new CulturalMemeTemplate("storytelling_nights", "Storytelling Nights", "Tales are being told at the Inn",
            MemeCategory.Social, new Dictionary<string, float> { ["inn"] = 1.6f }),

        // War memes
        new CulturalMemeTemplate("battle_call", "Battle Call", "Warriors rally for combat",
            MemeCategory.War, new Dictionary<string, float> { ["dungeon"] = 1.4f, ["train"] = 1.5f, ["shop"] = 1.2f }),
        new CulturalMemeTemplate("arms_race", "Arms Race", "Everyone's gearing up",
            MemeCategory.War, new Dictionary<string, float> { ["shop"] = 1.5f, ["train"] = 1.3f, ["dungeon"] = 1.2f }),
        new CulturalMemeTemplate("hero_worship", "Hero Worship", "Admiration for warriors grows",
            MemeCategory.War, new Dictionary<string, float> { ["train"] = 1.4f, ["dungeon"] = 1.3f, ["inn"] = 1.2f }),

        // Mystery memes
        new CulturalMemeTemplate("ancient_prophecy", "Ancient Prophecy", "Whispers of an old prophecy resurface",
            MemeCategory.Mystery, new Dictionary<string, float> { ["dungeon"] = 1.3f, ["temple"] = 1.3f, ["move"] = 1.2f }),
        new CulturalMemeTemplate("dungeon_treasure", "Dungeon Treasure", "Rumors of a great treasure below",
            MemeCategory.Mystery, new Dictionary<string, float> { ["dungeon"] = 1.6f, ["shop"] = 1.2f }),
        new CulturalMemeTemplate("strange_omens", "Strange Omens", "Unusual signs appear across the land",
            MemeCategory.Mystery, new Dictionary<string, float> { ["temple"] = 1.3f, ["dungeon"] = 1.2f, ["move"] = 1.2f }),
    };

    // ========================================================================
    // Meme Generation
    // ========================================================================

    /// <summary>
    /// Generate new memes based on world state or random cultural emergence.
    /// </summary>
    public void GenerateNewMemes(List<NPC> npcs, int currentTick)
    {
        if (_activeMemes.Count >= MAX_ACTIVE_MEMES) return;
        if (_random.NextDouble() > 0.01) return; // 1% per tick
        if (currentTick - _lastMemeGenerationTick < 20) return; // Min 20 ticks between generations

        // Pick a random template that isn't already active
        var activeIds = _activeMemes.Select(m => m.Id).ToHashSet();
        var available = MemeTemplates.Where(t => !activeIds.Contains(t.Id)).ToArray();
        if (available.Length == 0) return;

        var template = available[_random.Next(available.Length)];

        // Pick an origin NPC and location
        var originNpc = npcs
            .Where(n => n.IsAlive && !n.IsDead && n.Brain?.Personality != null &&
                        SocialLocations.Contains(n.CurrentLocation))
            .OrderBy(_ => _random.Next())
            .FirstOrDefault();

        if (originNpc == null) return;

        string originName = originNpc.Name2 ?? originNpc.Name;
        string originLocation = originNpc.CurrentLocation;

        // Create the meme
        var meme = new CulturalMeme
        {
            Id = template.Id,
            Name = template.Name,
            Description = template.Description,
            Category = template.Category,
            GlobalStrength = 0.5f + (float)_random.NextDouble() * 0.3f, // 0.5-0.8 starting strength
            TickCreated = currentTick,
            OriginLocation = originLocation,
            OriginNPC = originName,
            SpreadCount = 1,
            ActivityModifiers = new Dictionary<string, float>(template.ActivityModifiers)
        };

        _activeMemes.Add(meme);
        _lastMemeGenerationTick = currentTick;

        // Origin NPC knows this meme
        EnsureMemeAwareness(originNpc.Name);
        _npcMemeAwareness[originNpc.Name].Add(meme.Id);

        // Boost location strength
        EnsureLocationStrength(originLocation);
        _locationMemeStrength[originLocation][meme.Id] = meme.GlobalStrength;

        NewsSystem.Instance?.Newsy($"A new idea is stirring in {originLocation}: \"{meme.Name}\" — {meme.Description}");

        UsurperRemake.Systems.DebugLogger.Instance?.LogInfo("SOCIAL",
            $"New cultural meme: \"{meme.Name}\" ({meme.Category}) originated at {originLocation} by {originName}");
    }

    // ========================================================================
    // Meme Spreading
    // ========================================================================

    /// <summary>
    /// NPCs spread memes to other NPCs at the same location.
    /// </summary>
    public void ProcessMemeSpreading(List<NPC> npcs, int currentTick)
    {
        if (_activeMemes.Count == 0) return;
        if (_random.NextDouble() > 0.05) return; // 5% per tick

        // Find a speaker who knows at least one meme
        var speakers = npcs.Where(n =>
            n.IsAlive && !n.IsDead &&
            n.Brain?.Personality != null &&
            SocialLocations.Contains(n.CurrentLocation) &&
            _npcMemeAwareness.TryGetValue(n.Name, out var memes) && memes.Count > 0).ToList();

        if (speakers.Count == 0) return;
        var speaker = speakers[_random.Next(speakers.Count)];

        // Pick a meme the speaker knows
        var speakerMemes = _npcMemeAwareness[speaker.Name];
        var activeMemeIds = _activeMemes.Select(m => m.Id).ToHashSet();
        var validMemes = speakerMemes.Where(id => activeMemeIds.Contains(id)).ToList();
        if (validMemes.Count == 0) return;
        string memeId = validMemes[_random.Next(validMemes.Count)];

        // Find a listener at the same location who doesn't know this meme
        var listeners = npcs.Where(n =>
            n != speaker && n.IsAlive && !n.IsDead &&
            n.Brain?.Personality != null &&
            n.CurrentLocation == speaker.CurrentLocation &&
            (!_npcMemeAwareness.TryGetValue(n.Name, out var lm) || !lm.Contains(memeId))).ToList();

        if (listeners.Count == 0) return;
        var listener = listeners[_random.Next(listeners.Count)];

        var meme = _activeMemes.FirstOrDefault(m => m.Id == memeId);
        if (meme == null) return;

        // Spread chance based on personality and meme strength
        float spreadChance = 0.3f;
        spreadChance += speaker.Brain.Personality.Sociability * 0.3f; // 0-0.3 bonus
        spreadChance += meme.GlobalStrength * 0.2f; // 0-0.2 bonus
        // Intelligent listeners are selective but also recognize good ideas
        spreadChance += (listener.Brain.Personality.Intelligence - 0.5f) * 0.1f;
        spreadChance = Math.Clamp(spreadChance, 0.1f, 0.8f);

        if (_random.NextDouble() > spreadChance) return;

        // Spread the meme!
        EnsureMemeAwareness(listener.Name);
        _npcMemeAwareness[listener.Name].Add(memeId);
        meme.SpreadCount++;

        // Boost location strength
        string location = speaker.CurrentLocation;
        EnsureLocationStrength(location);
        var locStrength = _locationMemeStrength[location];
        locStrength[memeId] = Math.Min(1f, locStrength.GetValueOrDefault(memeId, 0f) + 0.05f);

        // Occasional news
        if (meme.SpreadCount % 10 == 0 && _random.NextDouble() < 0.3)
        {
            NewsSystem.Instance?.Newsy($"The idea of \"{meme.Name}\" continues to spread through the realm");
        }

        UsurperRemake.Systems.DebugLogger.Instance?.LogDebug("SOCIAL",
            $"Meme spread: {speaker.Name2 ?? speaker.Name} shared \"{meme.Name}\" with {listener.Name2 ?? listener.Name} (total spreads: {meme.SpreadCount})");
    }

    // ========================================================================
    // Meme Decay
    // ========================================================================

    /// <summary>
    /// Memes decay over time. Called every tick.
    /// </summary>
    public void DecayMemes()
    {
        // Decay global strength
        foreach (var meme in _activeMemes)
        {
            meme.GlobalStrength *= 0.995f; // 0.5% decay per tick (~25% per hour)
        }

        // Decay location strengths faster
        foreach (var locDict in _locationMemeStrength.Values)
        {
            var keys = locDict.Keys.ToList();
            foreach (var key in keys)
            {
                locDict[key] *= 0.99f; // 1% decay per tick
                if (locDict[key] < 0.02f)
                    locDict.Remove(key);
            }
        }

        // Remove dead memes
        var deadMemes = _activeMemes.Where(m => m.GlobalStrength < 0.05f).ToList();
        foreach (var dead in deadMemes)
        {
            _activeMemes.Remove(dead);

            // Clean up awareness
            foreach (var awareness in _npcMemeAwareness.Values)
                awareness.Remove(dead.Id);

            // Clean up location strength
            foreach (var locDict in _locationMemeStrength.Values)
                locDict.Remove(dead.Id);

            if (_random.NextDouble() < 0.3)
                NewsSystem.Instance?.Newsy($"The idea of \"{dead.Name}\" has faded from popular interest");

            UsurperRemake.Systems.DebugLogger.Instance?.LogDebug("SOCIAL",
                $"Meme expired: \"{dead.Name}\" after {dead.SpreadCount} total spreads");
        }
    }

    /// <summary>
    /// Reinforce a meme by category (called when relevant world events happen).
    /// </summary>
    public void ReinforceMemeCategory(MemeCategory category, float boostAmount = 0.2f)
    {
        foreach (var meme in _activeMemes.Where(m => m.Category == category))
        {
            meme.GlobalStrength = Math.Min(1f, meme.GlobalStrength + boostAmount);
        }
    }

    // ========================================================================
    // Activity Weight Integration
    // ========================================================================

    /// <summary>
    /// Apply cultural meme influence to NPC activity weights.
    /// Called from WorldSimulator.ProcessNPCActivities().
    /// </summary>
    public void ApplyMemeWeights(List<(string action, double weight)> activities, NPC npc)
    {
        if (!_npcMemeAwareness.TryGetValue(npc.Name, out var knownMemes) || knownMemes.Count == 0)
            return;

        // Conformity factor: sociable NPCs follow trends more strongly
        float conformity = 0.5f + (npc.Brain?.Personality?.Sociability ?? 0.5f) * 0.5f; // 0.5-1.0

        foreach (var memeId in knownMemes)
        {
            var meme = _activeMemes.FirstOrDefault(m => m.Id == memeId);
            if (meme == null) continue;

            foreach (var (activityKey, modifier) in meme.ActivityModifiers)
            {
                for (int i = 0; i < activities.Count; i++)
                {
                    if (activities[i].action == activityKey)
                    {
                        // Interpolate between 1.0 (no effect) and the modifier based on conformity and meme strength
                        double effectiveModifier = 1.0 + (modifier - 1.0) * conformity * meme.GlobalStrength;
                        activities[i] = (activities[i].action, activities[i].weight * effectiveModifier);
                    }
                }
            }
        }
    }

    // ========================================================================
    // Queries
    // ========================================================================

    /// <summary>
    /// Get active memes and their strength at a specific location.
    /// </summary>
    public Dictionary<string, float> GetLocationMemes(string location)
    {
        if (_locationMemeStrength.TryGetValue(location, out var memes))
            return new Dictionary<string, float>(memes);
        return new Dictionary<string, float>();
    }

    /// <summary>
    /// Get all active memes for display/debugging.
    /// </summary>
    public List<CulturalMeme> GetActiveMemes() => new(_activeMemes);

    /// <summary>
    /// Get how many NPCs know about a specific meme.
    /// </summary>
    public int GetMemeAwarenessCount(string memeId)
    {
        return _npcMemeAwareness.Count(kv => kv.Value.Contains(memeId));
    }

    // ========================================================================
    // Serialization
    // ========================================================================

    public CulturalMemeSaveData ExportSaveData()
    {
        return new CulturalMemeSaveData
        {
            ActiveMemes = _activeMemes.Select(m => new CulturalMemeData
            {
                Id = m.Id,
                Name = m.Name,
                Description = m.Description,
                Category = (int)m.Category,
                GlobalStrength = m.GlobalStrength,
                TickCreated = m.TickCreated,
                OriginLocation = m.OriginLocation,
                OriginNPC = m.OriginNPC,
                SpreadCount = m.SpreadCount,
                ActivityModifiers = new Dictionary<string, float>(m.ActivityModifiers)
            }).ToList(),
            NpcMemeAwareness = _npcMemeAwareness.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.ToList()),
            LocationMemeStrength = _locationMemeStrength.ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, float>(kv.Value))
        };
    }

    public void RestoreFromSaveData(CulturalMemeSaveData? data)
    {
        _activeMemes.Clear();
        _npcMemeAwareness.Clear();
        _locationMemeStrength.Clear();

        if (data == null) return;

        foreach (var md in data.ActiveMemes ?? new List<CulturalMemeData>())
        {
            _activeMemes.Add(new CulturalMeme
            {
                Id = md.Id,
                Name = md.Name,
                Description = md.Description,
                Category = (MemeCategory)md.Category,
                GlobalStrength = md.GlobalStrength,
                TickCreated = md.TickCreated,
                OriginLocation = md.OriginLocation,
                OriginNPC = md.OriginNPC,
                SpreadCount = md.SpreadCount,
                ActivityModifiers = md.ActivityModifiers != null
                    ? new Dictionary<string, float>(md.ActivityModifiers)
                    : new Dictionary<string, float>()
            });
        }

        if (data.NpcMemeAwareness != null)
        {
            foreach (var kv in data.NpcMemeAwareness)
                _npcMemeAwareness[kv.Key] = new HashSet<string>(kv.Value ?? new List<string>());
        }

        if (data.LocationMemeStrength != null)
        {
            foreach (var kv in data.LocationMemeStrength)
                _locationMemeStrength[kv.Key] = new Dictionary<string, float>(kv.Value ?? new Dictionary<string, float>());
        }
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private void EnsureMemeAwareness(string npcName)
    {
        if (!_npcMemeAwareness.ContainsKey(npcName))
            _npcMemeAwareness[npcName] = new HashSet<string>();
    }

    private void EnsureLocationStrength(string location)
    {
        if (!_locationMemeStrength.ContainsKey(location))
            _locationMemeStrength[location] = new Dictionary<string, float>();
    }
}

// ========================================================================
// Data Models
// ========================================================================

public class CulturalMeme
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public MemeCategory Category { get; set; }
    public float GlobalStrength { get; set; }
    public int TickCreated { get; set; }
    public string OriginLocation { get; set; } = "";
    public string OriginNPC { get; set; } = "";
    public int SpreadCount { get; set; }
    public Dictionary<string, float> ActivityModifiers { get; set; } = new();
}

public enum MemeCategory
{
    Danger,
    Prosperity,
    Faith,
    Unrest,
    Social,
    War,
    Mystery
}

/// <summary>
/// Template for creating cultural memes. Immutable definition.
/// </summary>
public class CulturalMemeTemplate
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public MemeCategory Category { get; }
    public Dictionary<string, float> ActivityModifiers { get; }

    public CulturalMemeTemplate(string id, string name, string description,
        MemeCategory category, Dictionary<string, float> activityModifiers)
    {
        Id = id;
        Name = name;
        Description = description;
        Category = category;
        ActivityModifiers = activityModifiers;
    }
}

// ========================================================================
// Save Data Structures (will be added to SaveDataStructures.cs)
// ========================================================================

public class CulturalMemeSaveData
{
    public List<CulturalMemeData> ActiveMemes { get; set; } = new();
    public Dictionary<string, List<string>> NpcMemeAwareness { get; set; } = new();
    public Dictionary<string, Dictionary<string, float>> LocationMemeStrength { get; set; } = new();
}

public class CulturalMemeData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Category { get; set; }
    public float GlobalStrength { get; set; }
    public int TickCreated { get; set; }
    public string OriginLocation { get; set; } = "";
    public string OriginNPC { get; set; } = "";
    public int SpreadCount { get; set; }
    public Dictionary<string, float> ActivityModifiers { get; set; } = new();
}
