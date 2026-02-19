using UsurperRemake.Utils;
using UsurperRemake.Systems;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Complete relationship system based on Pascal RELATION.PAS and RELATIO2.PAS
/// Manages social relationships, marriages, divorces, and family dynamics
/// Maintains perfect Pascal compatibility with original game mechanics
/// </summary>
public partial class RelationshipSystem
{
    // Simple singleton instance for global access
    public static RelationshipSystem Instance { get; } = new RelationshipSystem();

    // ThreadStatic so each MUD session has its own relationship data (prevents cross-player Clear)
    [ThreadStatic] private static Dictionary<string, Dictionary<string, RelationshipRecord>>? _relationshipsThread;
    private static Dictionary<string, Dictionary<string, RelationshipRecord>> _relationships
    {
        get => _relationshipsThread ??= new();
        set => _relationshipsThread = value;
    }

    private static Random _random = new Random();

    // Daily relationship gain tracking (v0.26) - prevents relationship flooding
    // Key format: "{character1}_{character2}_{date}" -> steps gained today
    [ThreadStatic] private static Dictionary<string, int>? _dailyRelationshipGainsThread;
    private static Dictionary<string, int> _dailyRelationshipGains
    {
        get => _dailyRelationshipGainsThread ??= new();
        set => _dailyRelationshipGainsThread = value;
    }
    [ThreadStatic] private static DateTime? _lastDailyResetThread;
    private static DateTime _lastDailyReset
    {
        get => _lastDailyResetThread ?? DateTime.MinValue;
        set => _lastDailyResetThread = value;
    }

    /// <summary>
    /// Relationship record structure based on Pascal RelationRec
    /// Tracks bidirectional relationships between characters
    /// </summary>
    public class RelationshipRecord
    {
        public string Name1 { get; set; } = "";         // player 1 name
        public string Name2 { get; set; } = "";         // player 2 name
        public CharacterAI AI1 { get; set; }            // player 1 AI type
        public CharacterAI AI2 { get; set; }            // player 2 AI type
        public CharacterRace Race1 { get; set; }        // player 1 race
        public CharacterRace Race2 { get; set; }        // player 2 race
        public int Relation1 { get; set; }              // pl1's relation to pl2
        public int Relation2 { get; set; }              // pl2's relation to pl1
        public string IdTag1 { get; set; } = "";        // player 1 unique ID
        public string IdTag2 { get; set; } = "";        // player 2 unique ID
        public int RecordNumber1 { get; set; }          // pl1 record number
        public int RecordNumber2 { get; set; }          // pl2 record number
        public int FileType1 { get; set; }              // pl1 file type (1=player, 2=npc)
        public int FileType2 { get; set; }              // pl2 file type (1=player, 2=npc)
        public bool Deleted { get; set; }               // is record deleted
        public int RecordNumber { get; set; }           // position in file
        public bool BannedMarry { get; set; }           // banned from marriage by King
        public int MarriedTimes { get; set; }           // times married
        public int MarriedDays { get; set; }            // days married
        public int Kids { get; set; }                   // children produced
        public int KilledBy1 { get; set; }              // name2 killed by name1 count
        public int KilledBy2 { get; set; }              // name1 killed by name2 count
        public DateTime CreatedDate { get; set; }       // when relationship started (real time)
        public DateTime LastUpdated { get; set; }       // last update time
        public int CreatedOnGameDay { get; set; }       // in-game day when relationship started (v0.26)
    }
    
    /// <summary>
    /// Get relationship status between two characters
    /// Pascal equivalent: Social_Relation function
    /// </summary>
    public static int GetRelationshipStatus(Character character1, Character character2)
    {
        var key1 = GetRelationshipKey(character1.Name, character2.Name);
        var key2 = GetRelationshipKey(character2.Name, character1.Name);

        // Check (char1, char2) ordering — Relation1 is char1's feeling toward char2
        if (_relationships.ContainsKey(key1) && _relationships[key1].ContainsKey(key2))
        {
            return _relationships[key1][key2].Relation1;
        }

        // Check reverse ordering — if record was created as (char2, char1),
        // then Relation2 is char1's feeling toward char2
        if (_relationships.ContainsKey(key2) && _relationships[key2].ContainsKey(key1))
        {
            return _relationships[key2][key1].Relation2;
        }

        // Default relationship is normal
        return GameConfig.RelationNormal;
    }
    
    /// <summary>
    /// Update relationship between two characters
    /// Pascal equivalent: Update_Relation procedure
    /// v0.26: Added daily relationship gain cap to prevent relationship flooding
    /// </summary>
    public static void UpdateRelationship(Character character1, Character character2, int direction, int steps = 1, bool _unused = false, bool overrideMaxFeeling = false)
    {
        var relation = GetOrCreateRelationship(character1, character2);

        // Determine which side of the record represents character1's feeling
        // If record was created as (char1, char2), use Relation1
        // If record was created as (char2, char1), use Relation2
        bool isReversed = relation.Name1 != character1.Name;

        // Track old relation to detect new friendships
        int oldRelation = isReversed ? relation.Relation2 : relation.Relation1;

        // v0.26: Enforce daily relationship gain cap for positive relationship changes
        int effectiveSteps = steps;
        if (direction > 0)
        {
            effectiveSteps = EnforceDailyRelationshipCap(character1.Name, character2.Name, steps);
            if (effectiveSteps <= 0)
            {
                // Daily cap reached, no more gains today
                return;
            }
        }

        for (int i = 0; i < effectiveSteps; i++)
        {
            if (isReversed)
            {
                if (direction > 0)
                    relation.Relation2 = IncreaseRelation(relation.Relation2, overrideMaxFeeling);
                else if (direction < 0)
                    relation.Relation2 = DecreaseRelation(relation.Relation2);
            }
            else
            {
                if (direction > 0)
                    relation.Relation1 = IncreaseRelation(relation.Relation1, overrideMaxFeeling);
                else if (direction < 0)
                    relation.Relation1 = DecreaseRelation(relation.Relation1);
            }
        }

        // Track new friendship for achievements (when relation improves to Friendship level or better)
        // Friendship level is 40 or lower (lower = better relationship)
        int newRelation = isReversed ? relation.Relation2 : relation.Relation1;
        if (oldRelation > GameConfig.RelationFriendship && newRelation <= GameConfig.RelationFriendship)
        {
            // A new friendship was formed - track for achievements
            character1.Statistics?.RecordFriendMade();
        }

        relation.LastUpdated = DateTime.Now;
        SaveRelationship(relation);

        // Send relationship change notification
        SendRelationshipChangeNotification(character1, character2, newRelation);
    }

    /// <summary>
    /// Enforce daily relationship gain cap (v0.26)
    /// Returns the number of steps that can be applied without exceeding the cap
    /// </summary>
    private static int EnforceDailyRelationshipCap(string name1, string name2, int requestedSteps)
    {
        // Reset daily tracking if it's a new day
        if (_lastDailyReset.Date != DateTime.Now.Date)
        {
            _dailyRelationshipGains.Clear();
            _lastDailyReset = DateTime.Now;
        }

        string key = $"{name1}_{name2}_{DateTime.Now:yyyyMMdd}";
        int alreadyGained = _dailyRelationshipGains.GetValueOrDefault(key, 0);
        int remaining = GameConfig.MaxDailyRelationshipGain - alreadyGained;

        if (remaining <= 0) return 0;

        int actualSteps = Math.Min(requestedSteps, remaining);
        _dailyRelationshipGains[key] = alreadyGained + actualSteps;

        return actualSteps;
    }
    
    /// <summary>
    /// Convenience overload accepting the compatibility enum
    /// </summary>
    public static void UpdateRelationship(Character character1, Character character2, UsurperRemake.RelationshipType relationChange, int steps = 1, bool _unused = false, bool overrideMaxFeeling = false)
        => UpdateRelationship(character1, character2, (int)relationChange, steps, _unused, overrideMaxFeeling);
    
    /// <summary>
    /// Check if two characters are married
    /// Pascal equivalent: Are_They_Married function
    /// </summary>
    public static bool AreMarried(Character character1, Character character2)
    {
        var relation = GetRelationship(character1, character2);
        return relation != null && 
               relation.Relation1 == GameConfig.RelationMarried && 
               relation.Relation2 == GameConfig.RelationMarried;
    }
    
    /// <summary>
    /// Get spouse name for a character
    /// Pascal equivalent: Is_Player_Married function
    /// </summary>
    public static string GetSpouseName(Character character)
    {
        foreach (var relationGroup in _relationships.Values)
        {
            foreach (var relation in relationGroup.Values)
            {
                if (relation.Deleted) continue;

                if (relation.Name1 == character.Name &&
                    relation.Relation1 == GameConfig.RelationMarried &&
                    relation.Relation2 == GameConfig.RelationMarried)
                {
                    // Check if spouse is dead before returning
                    var spouse = NPCSpawnSystem.Instance?.GetNPCByName(relation.Name2);
                    if (spouse != null && spouse.IsDead)
                        continue; // Skip dead spouse
                    return relation.Name2;
                }

                if (relation.Name2 == character.Name &&
                    relation.Relation1 == GameConfig.RelationMarried &&
                    relation.Relation2 == GameConfig.RelationMarried)
                {
                    // Check if spouse is dead before returning
                    var spouse = NPCSpawnSystem.Instance?.GetNPCByName(relation.Name1);
                    if (spouse != null && spouse.IsDead)
                        continue; // Skip dead spouse
                    return relation.Name1;
                }
            }
        }

        return "";
    }
    
    /// <summary>
    /// Perform marriage ceremony between two characters
    /// Pascal equivalent: marry_routine from LOVERS.PAS
    /// v0.26: Added minimum relationship duration and NPC proposal acceptance
    /// </summary>
    public static bool PerformMarriage(Character character1, Character character2, out string message)
    {
        message = "";

        // Check if either character is permanently dead (IsDead is on NPC/Player, not base Character)
        if (character1 is NPC deadCheck1 && deadCheck1.IsDead)
        {
            message = $"{character1.Name} has passed away and cannot marry.";
            return false;
        }
        if (character2 is NPC deadCheck2 && deadCheck2.IsDead)
        {
            message = $"{character2.Name} has passed away and cannot marry.";
            return false;
        }

        // Check marriage prerequisites
        if (character1.Age < GameConfig.MinimumAgeToMarry || character2.Age < GameConfig.MinimumAgeToMarry)
        {
            message = "Both parties must be at least 18 years old to marry!";
            return false;
        }

        if (GetSpouseName(character1) != "" || GetSpouseName(character2) != "")
        {
            message = "One or both parties are already married!";
            return false;
        }

        if (character1.IntimacyActs < 1)
        {
            message = "You have no intimacy acts left today!";
            return false;
        }

        var relation = GetOrCreateRelationship(character1, character2);

        // Both must be in love to marry
        if (relation.Relation1 != GameConfig.RelationLove || relation.Relation2 != GameConfig.RelationLove)
        {
            message = "You both need to be in love with each other to marry!";
            return false;
        }

        // Check if marriage is banned
        if (relation.BannedMarry)
        {
            message = "Marriage between these characters has been banned by the King!";
            return false;
        }

        // v0.26: Check minimum relationship duration (7 in-game days)
        int currentGameDay = 1;
        try { currentGameDay = StoryProgressionSystem.Instance.CurrentGameDay; }
        catch { /* StoryProgressionSystem not initialized */ }

        int daysSinceRelationshipStart = currentGameDay - relation.CreatedOnGameDay;
        if (daysSinceRelationshipStart < GameConfig.MinDaysBeforeMarriage)
        {
            int daysRemaining = GameConfig.MinDaysBeforeMarriage - daysSinceRelationshipStart;
            message = $"Your relationship is too new! Wait {daysRemaining} more day{(daysRemaining > 1 ? "s" : "")} before proposing.";
            return false;
        }

        // v0.26: NPC proposal acceptance check (if character2 is NPC)
        if (character2 is NPC npcPartner)
        {
            int acceptanceChance = CalculateProposalAcceptance(character1, npcPartner);
            int roll = _random.Next(100);
            if (roll >= acceptanceChance)
            {
                message = GetProposalRejectionMessage(npcPartner, acceptanceChance);
                return false;
            }
        }

        // Perform marriage ceremony
        relation.Relation1 = GameConfig.RelationMarried;
        relation.Relation2 = GameConfig.RelationMarried;
        relation.MarriedDays = 0;
        relation.MarriedTimes++;
        
        // Update character marriage status
        character1.Married = true;
        character1.IsMarried = true;
        character1.SpouseName = character2.Name;
        character1.MarriedTimes++;
        character1.IntimacyActs--;
        
        character2.Married = true;
        character2.IsMarried = true;
        character2.SpouseName = character1.Name;
        character2.MarriedTimes++;
        
        SaveRelationship(relation);

        // Online news: marriage
        if (OnlineStateManager.IsActive)
        {
            _ = OnlineStateManager.Instance!.AddNews(
                $"{character1.Name} and {character2.Name} have been wed! Congratulations!", "romance");
        }

        // Generate wedding announcement
        var ceremonyMessage = GameConfig.WeddingCeremonyMessages[_random.Next(GameConfig.WeddingCeremonyMessages.Length)];
        
        message = $"Wedding Ceremony Complete!\n" +
                 $"{character1.Name} and {character2.Name} are now married!\n" +
                 $"{ceremonyMessage}";
        
        // Handle different-sex vs same-sex marriages
        if (character1.Sex != character2.Sex)
        {
            message += "\nCongratulations! (go home and make babies)";
        }
        else
        {
            message += "\nCongratulations! (go home and adopt babies)";
        }

        // Generate marriage news for the realm
        NewsSystem.Instance?.WriteMarriageNews(character1.Name, character2.Name, "Church");

        // Sync with RomanceTracker for NPC spouses
        if (character2 is NPC npc)
        {
            RomanceTracker.Instance?.AddSpouse(npc.ID);
        }
        else if (character1 is NPC npc1)
        {
            RomanceTracker.Instance?.AddSpouse(npc1.ID);
        }

        // Sync with NPCMarriageRegistry for NPC-NPC marriages
        if (character1 is NPC npc1Marriage && character2 is NPC npc2Marriage)
        {
            NPCMarriageRegistry.Instance.RegisterMarriage(npc1Marriage.ID, npc2Marriage.ID, npc1Marriage.Name2, npc2Marriage.Name2);
        }

        // Log the marriage event
        DebugLogger.Instance.LogMarriage(character1.Name, character2.Name);

        return true;
    }
    
    /// <summary>
    /// Process divorce between married characters
    /// Pascal equivalent: divorce procedure from LOVERS.PAS
    /// </summary>
    public static bool ProcessDivorce(Character character1, Character character2, out string message)
    {
        message = "";
        
        if (!AreMarried(character1, character2))
        {
            message = "You are not married to this person!";
            return false;
        }
        
        var relation = GetRelationship(character1, character2);
        if (relation == null)
        {
            message = "No relationship record found!";
            return false;
        }
        
        // Generate divorce message based on marriage duration
        string durationMessage;
        if (relation.MarriedDays < 1)
        {
            durationMessage = "Their marriage lasted only a couple of hours!";
        }
        else if (relation.MarriedDays < 30)
        {
            durationMessage = $"Their marriage lasted only {relation.MarriedDays} days.";
        }
        else
        {
            durationMessage = $"Their marriage lasted {relation.MarriedDays} days.";
        }
        
        // Update relationship status
        relation.Relation1 = GameConfig.RelationNormal;
        relation.Relation2 = GameConfig.RelationHate; // Divorced partner becomes hateful
        relation.MarriedDays = 0;
        
        // Update character marriage status
        character1.Married = false;
        character1.IsMarried = false;
        character1.SpouseName = "";
        
        character2.Married = false;
        character2.IsMarried = false;
        character2.SpouseName = "";
        
        SaveRelationship(relation);
        
        // Handle child custody (children go to character2 - the spouse)
        HandleChildCustodyAfterDivorce(character1, character2);
        
        message = $"Divorce Finalized!\n" +
                 $"{character1.Name} divorced {character2.Name}!\n" +
                 $"{durationMessage}\n" +
                 $"You have lost custody of your children!";

        // Generate divorce news for the realm
        NewsSystem.Instance?.WriteDivorceNews(character1.Name, character2.Name);

        return true;
    }
    
    /// <summary>
    /// Calculate experience gained from romantic interaction
    /// Pascal equivalent: Sex_Experience function
    /// </summary>
    public static long CalculateRomanticExperience(Character character1, Character character2, int experienceType)
    {
        long baseExperience = character1.Level * 110 + character2.Level * 90;
        
        return experienceType switch
        {
            0 => baseExperience / 2, // Kiss
            1 => baseExperience,     // Dinner
            2 => baseExperience / 3, // Hold hands
            3 => baseExperience * 2, // Intimate
            _ => baseExperience
        };
    }
    
    /// <summary>
    /// List all married couples
    /// Pascal equivalent: List_Married_Couples procedure
    /// </summary>
    public static List<string> GetMarriedCouples()
    {
        var couples = new List<string>();
        
        foreach (var relationGroup in _relationships.Values)
        {
            foreach (var relation in relationGroup.Values)
            {
                if (relation.Deleted) continue;
                
                if (relation.Relation1 == GameConfig.RelationMarried &&
                    relation.Relation2 == GameConfig.RelationMarried)
                {
                    string duration = relation.MarriedDays == 1 ? "day" : "days";
                    couples.Add($"{relation.Name1} and {relation.Name2} have been married for {relation.MarriedDays} {duration}.");
                }
            }
        }
        
        return couples;
    }
    
    /// <summary>
    /// Generate relationship description string
    /// Pascal equivalent: Relation_String function
    /// </summary>
    public static string GetRelationshipDescription(int relationValue, bool useYou = false)
    {
        return relationValue switch
        {
            GameConfig.RelationMarried => useYou ? "You are married!" : "They are married",
            GameConfig.RelationLove => useYou ? "You are in love!" : "They are in love",
            GameConfig.RelationPassion => useYou ? "You have passionate feelings!" : "They have passionate feelings",
            GameConfig.RelationFriendship => useYou ? "You consider them a friend." : "They are friends",
            GameConfig.RelationTrust => useYou ? "You trust them." : "They trust each other",
            GameConfig.RelationRespect => useYou ? "You respect them." : "They respect each other",
            GameConfig.RelationNormal => useYou ? "You feel neutral towards them." : "They are neutral",
            GameConfig.RelationSuspicious => useYou ? "You are suspicious of them." : "They are suspicious",
            GameConfig.RelationAnger => useYou ? "You feel anger towards them!" : "They are angry",
            GameConfig.RelationEnemy => useYou ? "You consider them an enemy!" : "They are enemies",
            GameConfig.RelationHate => useYou ? "You HATE them!" : "They hate each other",
            _ => useYou ? "You feel indifferent." : "No relationship"
        };
    }
    
    /// <summary>
    /// Daily relationship maintenance
    /// Pascal equivalent: Relation_Maintenance procedure
    /// </summary>
    public static void DailyMaintenance()
    {
        foreach (var relationGroup in _relationships.Values)
        {
            foreach (var relation in relationGroup.Values)
            {
                if (relation.Deleted) continue;
                
                // Increment married days
                if (relation.Relation1 == GameConfig.RelationMarried &&
                    relation.Relation2 == GameConfig.RelationMarried)
                {
                    relation.MarriedDays++;
                    
                    // Random chance of divorce (5% chance - 1 in 20)
                    if (_random.Next(20) == 0)
                    {
                        ProcessAutomaticDivorce(relation);
                    }
                }
                
                relation.LastUpdated = DateTime.Now;
                SaveRelationship(relation);
            }
        }
    }
    
    #region Private Helper Methods
    
    private static string GetRelationshipKey(string name1, string name2)
    {
        return $"{name1}_{name2}";
    }
    
    internal static RelationshipRecord GetOrCreateRelationship(Character character1, Character character2)
    {
        var key1 = GetRelationshipKey(character1.Name, character2.Name);
        var key2 = GetRelationshipKey(character2.Name, character1.Name);

        // Check if record exists in (char1, char2) ordering
        if (_relationships.ContainsKey(key1) && _relationships[key1].ContainsKey(key2))
        {
            return _relationships[key1][key2];
        }

        // Check reverse ordering — record may have been created as (char2, char1)
        if (_relationships.ContainsKey(key2) && _relationships[key2].ContainsKey(key1))
        {
            return _relationships[key2][key1];
        }

        // No existing record — create new one
        if (!_relationships.ContainsKey(key1))
            _relationships[key1] = new Dictionary<string, RelationshipRecord>();

        // Get current in-game day for tracking relationship duration
        int currentGameDay = 1;
        try { currentGameDay = StoryProgressionSystem.Instance.CurrentGameDay; }
        catch { /* StoryProgressionSystem not initialized */ }

        var newRelation = new RelationshipRecord
        {
            Name1 = character1.Name,
            Name2 = character2.Name,
            AI1 = character1.AI,
            AI2 = character2.AI,
            Race1 = character1.Race,
            Race2 = character2.Race,
            Relation1 = GameConfig.RelationNormal,
            Relation2 = GameConfig.RelationNormal,
            IdTag1 = character1.ID,
            IdTag2 = character2.ID,
            CreatedDate = DateTime.Now,
            LastUpdated = DateTime.Now,
            CreatedOnGameDay = currentGameDay  // v0.26: Track in-game day
        };

        _relationships[key1][key2] = newRelation;
        return _relationships[key1][key2];
    }
    
    private static RelationshipRecord GetRelationship(Character character1, Character character2)
    {
        var key1 = GetRelationshipKey(character1.Name, character2.Name);
        var key2 = GetRelationshipKey(character2.Name, character1.Name);

        if (_relationships.ContainsKey(key1) && _relationships[key1].ContainsKey(key2))
        {
            return _relationships[key1][key2];
        }

        // Check reverse ordering — record may have been created as (char2, char1)
        if (_relationships.ContainsKey(key2) && _relationships[key2].ContainsKey(key1))
        {
            return _relationships[key2][key1];
        }

        return null;
    }

    /// <summary>
    /// Get the relationship level from character1 towards character2
    /// Returns RelationNormal (70) if no relationship exists
    /// Lower numbers = better relationship (10 = married, 20 = love, 70 = normal, 110 = hate)
    /// </summary>
    public static int GetRelationshipLevel(Character character1, Character character2)
    {
        var relation = GetRelationship(character1, character2);
        if (relation == null)
            return GameConfig.RelationNormal;

        // Return character1's feeling towards character2
        if (relation.Name1 == character1.Name)
            return relation.Relation1;
        else
            return relation.Relation2;
    }
    
    private static int IncreaseRelation(int currentRelation, bool overrideMaxFeeling)
    {
        return currentRelation switch
        {
            GameConfig.RelationMarried => GameConfig.RelationMarried, // no change
            GameConfig.RelationLove => GameConfig.RelationLove, // no change
            GameConfig.RelationPassion => overrideMaxFeeling ? GameConfig.RelationLove : GameConfig.RelationPassion,
            GameConfig.RelationFriendship => overrideMaxFeeling ? GameConfig.RelationPassion : GameConfig.RelationFriendship,
            GameConfig.RelationTrust => GameConfig.RelationFriendship,
            GameConfig.RelationRespect => GameConfig.RelationTrust,
            GameConfig.RelationNormal => GameConfig.RelationRespect,
            GameConfig.RelationSuspicious => GameConfig.RelationNormal,
            GameConfig.RelationAnger => GameConfig.RelationSuspicious,
            GameConfig.RelationEnemy => GameConfig.RelationAnger,
            GameConfig.RelationHate => GameConfig.RelationEnemy,
            _ => currentRelation
        };
    }
    
    private static int DecreaseRelation(int currentRelation)
    {
        return currentRelation switch
        {
            GameConfig.RelationMarried => GameConfig.RelationMarried, // no change
            GameConfig.RelationLove => GameConfig.RelationPassion,
            GameConfig.RelationPassion => GameConfig.RelationFriendship,
            GameConfig.RelationFriendship => GameConfig.RelationTrust,
            GameConfig.RelationTrust => GameConfig.RelationRespect,
            GameConfig.RelationRespect => GameConfig.RelationNormal,
            GameConfig.RelationNormal => GameConfig.RelationSuspicious,
            GameConfig.RelationSuspicious => GameConfig.RelationAnger,
            GameConfig.RelationAnger => GameConfig.RelationEnemy,
            GameConfig.RelationEnemy => GameConfig.RelationHate,
            GameConfig.RelationHate => GameConfig.RelationHate, // no change
            _ => currentRelation
        };
    }
    
    internal static void SaveRelationship(RelationshipRecord relation)
    {
        // In a full implementation, this would save to a file
        // For now, we keep it in memory
        relation.LastUpdated = DateTime.Now;
    }
    
    private static void SendRelationshipChangeNotification(Character character1, Character character2, int newRelation)
    {
        // In a full implementation, this would send mail notifications
        // For now, we just log the change
        GD.Print($"Relationship changed: {character1.Name} -> {character2.Name}: {GetRelationshipDescription(newRelation)}");
        DebugLogger.Instance.LogRelationshipChange(character1.Name, character2.Name, 0, newRelation, "interaction");
    }
    
    private static void HandleChildCustodyAfterDivorce(Character parent1, Character parent2)
    {
        // Parent1 keeps custody of children, parent2 loses access
        // This follows the original Pascal behavior
        int totalChildren = parent1.Kids + parent2.Kids;

        if (totalChildren > 0)
        {
            // Parent1 (initiator of divorce) keeps the children
            parent1.Kids = totalChildren;
            parent2.Kids = 0;

            // Generate news about the custody arrangement
            NewsSystem.Instance?.Newsy(true, $"{parent1.Name} was awarded custody of {totalChildren} child{(totalChildren > 1 ? "ren" : "")} in the divorce from {parent2.Name}.");
        }

        GD.Print($"Child custody handled: {parent1.Name} has custody of {totalChildren} children after divorce from {parent2.Name}");
    }
    
    private static void ProcessAutomaticDivorce(RelationshipRecord relation)
    {
        // Automatic divorce processing for NPCs
        relation.Relation1 = GameConfig.RelationNormal;
        relation.Relation2 = GameConfig.RelationHate;
        relation.MarriedDays = 0;

        GD.Print($"Automatic divorce: {relation.Name1} and {relation.Name2} divorced after {relation.MarriedDays} days");
    }

    /// <summary>
    /// Calculate NPC proposal acceptance chance based on personality (v0.26)
    /// Base 50% + personality modifiers + charisma modifier
    /// </summary>
    private static int CalculateProposalAcceptance(Character proposer, NPC npc)
    {
        int acceptance = GameConfig.BaseProposalAcceptance; // 50%

        // Personality modifiers from NPC brain
        if (npc.Brain?.Personality != null)
        {
            var personality = npc.Brain.Personality;

            // Romanticism increases acceptance (+0-20%)
            acceptance += (int)(personality.Romanticism * 20);

            // Commitment increases acceptance (+0-15%)
            acceptance += (int)(personality.Commitment * 15);

            // Low commitment (independence) decreases acceptance (-0-10%)
            acceptance -= (int)((1f - personality.Commitment) * 10);

            // Low romanticism (cynicism) decreases acceptance (-0-8%)
            acceptance -= (int)((1f - personality.Romanticism) * 8);
        }

        // Proposer's charisma modifier (+0-15% based on Charisma)
        int charismaBonus = Math.Min(15, (int)(proposer.Charisma / 4));
        acceptance += charismaBonus;

        // Relationship depth bonus (longer relationships = higher acceptance)
        var relation = GetRelationship(proposer, npc);
        if (relation != null)
        {
            // Use in-game days for consistency
            int currentGameDay = 1;
            try { currentGameDay = StoryProgressionSystem.Instance.CurrentGameDay; }
            catch { /* StoryProgressionSystem not initialized */ }

            int daysTogether = currentGameDay - relation.CreatedOnGameDay;
            int loyaltyBonus = Math.Min(15, daysTogether / 2); // +1% per 2 in-game days, max +15%
            acceptance += loyaltyBonus;
        }

        // Clamp to 20-95% (never guaranteed, never impossible if in love)
        return Math.Clamp(acceptance, 20, 95);
    }

    /// <summary>
    /// Get personalized rejection message based on NPC personality (v0.26)
    /// </summary>
    private static string GetProposalRejectionMessage(NPC npc, int acceptanceChance)
    {
        string name = npc.Name2 ?? npc.Name ?? "They";
        string pronoun = npc.Sex == CharacterSex.Female ? "she" : "he";

        // Low acceptance = strong rejection
        if (acceptanceChance < 30)
        {
            return $"{name} looks uncomfortable and steps back.\n" +
                   $"\"I... I'm sorry, but I'm not ready for that kind of commitment.\"\n" +
                   $"{pronoun.Substring(0, 1).ToUpper() + pronoun.Substring(1)} needs more time.";
        }
        // Medium acceptance = hesitant rejection
        else if (acceptanceChance < 50)
        {
            return $"{name} takes your hands gently but {pronoun} eyes are uncertain.\n" +
                   $"\"I care about you deeply, but... not yet. Let's give it more time.\"\n" +
                   $"Perhaps try again when your bond is stronger.";
        }
        // High acceptance = close call rejection
        else
        {
            return $"{name} hesitates, clearly tempted.\n" +
                   $"\"Ask me again soon... I just need a little more time.\"\n" +
                   $"You sense {pronoun}'s close to saying yes.";
        }
    }

    #region Serialization

    /// <summary>
    /// Export all relationships for saving
    /// </summary>
    public static List<UsurperRemake.Systems.RelationshipSaveData> ExportAllRelationships()
    {
        var result = new List<UsurperRemake.Systems.RelationshipSaveData>();

        foreach (var outerPair in _relationships)
        {
            foreach (var innerPair in outerPair.Value)
            {
                var relation = innerPair.Value;
                // Only save non-trivial relationships (not just "Normal" both ways)
                if (relation.Relation1 != GameConfig.RelationNormal ||
                    relation.Relation2 != GameConfig.RelationNormal ||
                    relation.MarriedDays > 0)
                {
                    result.Add(new UsurperRemake.Systems.RelationshipSaveData
                    {
                        Name1 = relation.Name1,
                        Name2 = relation.Name2,
                        IdTag1 = relation.IdTag1,  // Critical for identity tracking
                        IdTag2 = relation.IdTag2,  // Critical for identity tracking
                        Relation1 = relation.Relation1,
                        Relation2 = relation.Relation2,
                        MarriedDays = relation.MarriedDays,
                        Deleted = relation.Deleted,
                        LastUpdated = relation.LastUpdated,
                        CreatedOnGameDay = relation.CreatedOnGameDay  // In-game day tracking (v0.26)
                    });
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Import relationships from save data
    /// </summary>
    public static void ImportAllRelationships(List<UsurperRemake.Systems.RelationshipSaveData> savedRelationships)
    {
        if (savedRelationships == null) return;

        // Clear existing relationships
        _relationships.Clear();

        foreach (var saved in savedRelationships)
        {
            var key1 = GetRelationshipKey(saved.Name1, saved.Name2);
            var key2 = GetRelationshipKey(saved.Name2, saved.Name1);

            if (!_relationships.ContainsKey(key1))
                _relationships[key1] = new Dictionary<string, RelationshipRecord>();

            _relationships[key1][key2] = new RelationshipRecord
            {
                Name1 = saved.Name1,
                Name2 = saved.Name2,
                IdTag1 = saved.IdTag1 ?? "",  // Restore identity tags (critical for tracking)
                IdTag2 = saved.IdTag2 ?? "",  // Restore identity tags (critical for tracking)
                Relation1 = saved.Relation1,
                Relation2 = saved.Relation2,
                MarriedDays = saved.MarriedDays,
                Deleted = saved.Deleted,
                LastUpdated = saved.LastUpdated,
                CreatedOnGameDay = saved.CreatedOnGameDay  // Restore in-game day tracking (v0.26)
            };
        }

        GD.Print($"[RelationshipSystem] Imported {savedRelationships.Count} relationships");
    }

    #endregion

    #endregion
}
