using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.Locations;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public class WorldSimulator
{
    // Singleton instance for easy access
    private static WorldSimulator? _instance;
    public static WorldSimulator? Instance => _instance;

    private bool isRunning = false;
    private Random random = new Random();

    /// <summary>
    /// Multiplier for NPC XP gains. Default 1.0 for normal mode.
    /// Set to less than 1.0 for 24/7 world sim mode to slow NPC progression.
    /// </summary>
    public static float NpcXpMultiplier { get; set; } = 1.0f;

    // Get the active NPC list from NPCSpawnSystem instead of caching
    // This ensures we always use the current list even after save/load
    private List<NPC> npcs => UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>();

    private const float SIMULATION_INTERVAL = 30.0f; // seconds between simulation steps (reduced for faster respawns)
    private const int MAX_TEAM_SIZE = 5; // Maximum members per team (from Pascal)
    private const int NPC_RESPAWN_TICKS = 20; // Respawn dead NPCs after 20 simulation ticks (~10 min)

    // Track dead NPCs for respawn
    private Dictionary<string, int> deadNPCRespawnTimers = new();

    /// <summary>
    /// Set by WorldSimService when a player's team controls city turf but the player is offline.
    /// The world sim can't see the player, so this flag prevents NPC teams from overriding player turf.
    /// </summary>
    public string? PlayerTurfTeam { get; set; } = null;

    // Gossip system - pool of recent events NPCs can spread as rumors
    private class GossipItem
    {
        public string Text { get; set; } = "";
        public int TimesShared { get; set; }
        public int MaxShares { get; set; } // 2-3 shares before it's old news
    }
    private static readonly List<GossipItem> _gossipPool = new();
    private const int MaxGossipPoolSize = 20;

    // Emotional cascade rate limiting - tick counter per NPC
    private static readonly Dictionary<string, int> _lastCascadeTick = new();
    private static int _currentTick = 0;

    public WorldSimulator()
    {
        _instance = this;
    }

    /// <summary>
    /// Add a gossip item to the pool. Sociable NPCs will spread it via news later.
    /// </summary>
    public static void AddGossip(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // Avoid duplicate gossip
        if (_gossipPool.Any(g => g.Text == text)) return;
        _gossipPool.Add(new GossipItem { Text = text, TimesShared = 0, MaxShares = 2 + new Random().Next(2) });
        while (_gossipPool.Count > MaxGossipPoolSize)
            _gossipPool.RemoveAt(0);
    }

    // Team name generators for NPC-formed teams - Ocean/Manwe themed for lore
    private static readonly string[] TeamNamePrefixes = new[]
    {
        "The Tidal", "The Azure", "The Storm", "The Deep", "The Salt",
        "The Wave", "The Coral", "The Tempest", "The Abyssal", "The Pearl",
        "The Manwe", "The Seafoam", "The Riptide", "The Trident", "The Nautical",
        "The Leviathan", "The Kraken", "The Siren", "The Maritime", "The Oceanic"
    };

    private static readonly string[] TeamNameSuffixes = new[]
    {
        "Tide", "Current", "Mariners", "Sailors", "Corsairs",
        "Navigators", "Voyagers", "Depths", "Wanderers", "Brotherhood",
        "Covenant", "Order", "Guild", "Company", "Alliance",
        "Conclave", "Fellowship", "Syndicate", "Circle", "Legion"
    };

    // Location names that match actual game locations
    private static readonly string[] GameLocations = new[]
    {
        "Main Street", "Dungeon", "Weapon Shop", "Armor Shop", "Magic Shop",
        "Healer", "Inn", "Temple", "Church", "Market", "Castle", "Love Street", "Bank"
    };
    
    public void StartSimulation(List<NPC>? worldNPCs = null)
    {
        // Note: The worldNPCs parameter is ignored - we always use NPCSpawnSystem.Instance.ActiveNPCs
        // This ensures the simulator sees the correct NPCs even after save/load
        isRunning = true;

        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLD", $"WorldSimulator starting - NPCs available: {npcs?.Count ?? 0}");

        // Start a background task to periodically run simulation steps. This works even when
        // running head-less outside the Godot scene tree.
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLD", "Background simulation task started");
            while (isRunning)
            {
                try
                {
                    SimulateStep();
                }
                catch (Exception ex)
                {
                    UsurperRemake.Systems.DebugLogger.Instance.LogError("WORLD", $"Simulation error: {ex.Message}\n{ex.StackTrace}");
                }
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(SIMULATION_INTERVAL));
            }
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLD", "Background simulation task stopped");
        });
    }
    
    public void StopSimulation()
    {
        isRunning = false;
        // GD.Print("[WorldSim] Simulation stopped");
    }

    /// <summary>
    /// Mark the simulator as active without starting the internal background loop.
    /// Used by WorldSimService which drives SimulateStep() externally.
    /// </summary>
    public void SetActive(bool active)
    {
        isRunning = active;
    }
    
    public void SimulateStep()
    {
        if (!isRunning || npcs == null) return;

        var aliveCount = npcs.Count(n => n.IsAlive && !n.IsDead);
        var deadCount = npcs.Count(n => !n.IsAlive || n.IsDead);
        UsurperRemake.Systems.DebugLogger.Instance.LogDebug("WORLD", $"SimulateStep: {aliveCount} alive, {deadCount} dead, {deadNPCRespawnTimers.Count} in respawn queue");

        // Handle NPC respawns
        ProcessNPCRespawns();

        // Process child aging (children age and eventually become adult NPCs)
        FamilySystem.Instance?.ProcessDailyAging();

        // Process NPC aging and natural death
        ProcessNPCAging();

        // Process NPC pregnancies and births (including affairs)
        ProcessNPCPregnancies();

        // Process NPC divorces
        ProcessNPCDivorces();

        var worldState = new WorldState(npcs);

        // Process each NPC's AI
        foreach (var npc in npcs.Where(n => n.IsAlive && n.Brain != null))
        {
            try
            {
                var action = npc.Brain.DecideNextAction(worldState);
                ExecuteNPCAction(npc, action, worldState);

                // NPCs have a chance to do additional activities each tick
                ProcessNPCActivities(npc, worldState);

                // Process NPC relationships (marriages, friendships, enemies)
                EnhancedNPCBehaviors.ProcessNPCRelationships(npc, npcs);
            }
            catch (Exception ex)
            {
                UsurperRemake.Systems.DebugLogger.Instance.LogError("WORLD", $"Error processing NPC {npc.Name}: {ex.Message}");
            }
        }

        // Track dead NPCs for respawn (check both HP <= 0 and IsDead flag)
        // Skip age-dead NPCs - their soul has moved on, no respawn
        foreach (var npc in npcs.Where(n => (!n.IsAlive || n.IsDead) && !n.IsAgedDeath))
        {
            if (!deadNPCRespawnTimers.ContainsKey(npc.Name))
            {
                deadNPCRespawnTimers[npc.Name] = NPC_RESPAWN_TICKS;
                UsurperRemake.Systems.DebugLogger.Instance.LogDebug("NPC", $"{npc.Name} added to respawn queue ({NPC_RESPAWN_TICKS} ticks)");
            }
        }

        // Update emotional states from recent memories (generates emotions from events)
        foreach (var npc in npcs.Where(n => n.IsAlive && !n.IsDead && n.EmotionalState != null))
        {
            var recentMems = npc.Brain?.Memory?.AllMemories?.Where(m => m.IsRecent(2)).ToList()
                ?? new List<MemoryEvent>();
            npc.EmotionalState.Update(recentMems);
        }

        // Process emotional cascades - strong emotions spread to nearby NPCs
        _currentTick++;
        foreach (var npc in npcs.Where(n => n.IsAlive && !n.IsDead && n.EmotionalState != null))
        {
            ProcessEmotionalCascades(npc);
        }

        // Process gossip spreading
        ProcessGossip();

        // Process world events
        ProcessWorldEvents();

        // Update relationships and social dynamics
        UpdateSocialDynamics();
    }

    /// <summary>
    /// Queue an NPC for respawn immediately (call when NPC dies in combat)
    /// </summary>
    public void QueueNPCForRespawn(string npcName, int ticks = -1)
    {
        if (string.IsNullOrEmpty(npcName)) return;

        int respawnTicks = ticks > 0 ? ticks : NPC_RESPAWN_TICKS;

        if (!deadNPCRespawnTimers.ContainsKey(npcName))
        {
            deadNPCRespawnTimers[npcName] = respawnTicks;
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("NPC", $"Queued {npcName} for respawn ({respawnTicks} ticks = ~{respawnTicks * SIMULATION_INTERVAL / 60:F1} min)");
        }
    }

    /// <summary>
    /// Clear the respawn queue - call when loading a different save
    /// </summary>
    public void ClearRespawnQueue()
    {
        if (deadNPCRespawnTimers.Count > 0)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogDebug("NPC", $"Clearing respawn queue ({deadNPCRespawnTimers.Count} entries)");
            deadNPCRespawnTimers.Clear();
        }
    }

    /// <summary>
    /// Force immediate processing of dead NPCs - call after loading a save
    /// This ensures dead NPCs start their respawn timers immediately and
    /// respawn NPCs that have been dead for a while (based on save data)
    /// </summary>
    public void ProcessDeadNPCsOnLoad()
    {
        // Clear any stale entries from a previous save
        ClearRespawnQueue();

        if (npcs == null || npcs.Count == 0)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC", "ProcessDeadNPCsOnLoad: No NPCs found!");
            return;
        }

        var deadCount = npcs.Count(n => !n.IsAlive || n.IsDead);
        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("NPC", $"ProcessDeadNPCsOnLoad: Found {deadCount} dead NPCs out of {npcs.Count} total");

        // Find all dead NPCs and add them to the respawn queue
        foreach (var npc in npcs.Where(n => !n.IsAlive || n.IsDead))
        {
            if (!deadNPCRespawnTimers.ContainsKey(npc.Name))
            {
                // NPCs from saves respawn faster - just 2 ticks (~2 min) instead of 10
                deadNPCRespawnTimers[npc.Name] = 2;
                UsurperRemake.Systems.DebugLogger.Instance.LogDebug("NPC", $"Queued {npc.Name} for fast respawn (2 ticks)");
            }
        }

        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("NPC", $"Respawn queue now has {deadNPCRespawnTimers.Count} NPCs");
    }

    /// <summary>
    /// Process NPC respawns - resurrect dead NPCs after a cooldown period
    /// </summary>
    private void ProcessNPCRespawns()
    {
        if (npcs == null) return;

        var toRespawn = new List<string>();

        foreach (var kvp in deadNPCRespawnTimers.ToList())
        {
            deadNPCRespawnTimers[kvp.Key] = kvp.Value - 1;

            if (deadNPCRespawnTimers[kvp.Key] <= 0)
            {
                toRespawn.Add(kvp.Key);
            }
        }

        foreach (var npcName in toRespawn)
        {
            var npc = npcs.FirstOrDefault(n => n.Name == npcName);
            if (npc != null)
            {
                // Age death is permanent - the soul has moved on, never respawn
                if (npc.IsAgedDeath)
                {
                    deadNPCRespawnTimers.Remove(npcName);
                    continue;
                }

                // Respawn the NPC - clear permanent death flag
                npc.IsDead = false;

                // CRITICAL FIX: Ensure base stats are valid before RecalculateStats
                // If base stats are 0 (from old saves or corruption), RecalculateStats
                // will zero out all stats. Fix them based on level and class.
                ValidateAndFixBaseStats(npc);

                // Recalculate stats to fix any corrupted MaxHP values
                npc.RecalculateStats();

                // Ensure MaxHP is at least a reasonable minimum based on level
                long minHP = 20 + (npc.Level * 10);
                if (npc.MaxHP < minHP)
                {
                    npc.BaseMaxHP = minHP;
                    npc.MaxHP = minHP;
                    UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC", $"{npc.Name} had invalid MaxHP, reset to {minHP}");
                }

                // Restore full HP
                npc.HP = npc.MaxHP;
                npc.UpdateLocation("Main Street");

                // Lose some gold and XP as death penalty
                npc.Gold = Math.Max(0, npc.Gold / 2);

                NewsSystem.Instance.Newsy(true, $"{npc.Name} has returned from the realm of the dead!");
                UsurperRemake.Systems.DebugLogger.Instance.LogInfo("NPC", $"RESPAWNED: {npc.Name} (HP restored to {npc.HP}, MaxHP={npc.MaxHP}, IsDead={npc.IsDead})");
            }
            else
            {
                UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC", $"Could not find NPC to respawn: {npcName}");
            }

            deadNPCRespawnTimers.Remove(npcName);
        }
    }

    /// <summary>
    /// Validate and fix NPC base stats if they are invalid (0 or negative).
    /// This is critical for saves from older versions where base stats weren't saved,
    /// or for corrupted NPCs. Without valid base stats, RecalculateStats() will
    /// zero out all stats causing STR: 0, DEF: -18 type issues.
    /// </summary>
    private void ValidateAndFixBaseStats(NPC npc)
    {
        bool needsFix = false;
        int level = npc.Level > 0 ? npc.Level : 1;

        // Check if base stats are invalid (0 or negative)
        if (npc.BaseStrength <= 0)
        {
            // Calculate reasonable base strength for level and class
            npc.BaseStrength = 10 + (level * 5);
            if (npc.Class == CharacterClass.Warrior || npc.Class == CharacterClass.Barbarian)
                npc.BaseStrength += level * 2;
            needsFix = true;
        }

        if (npc.BaseDefence <= 0)
        {
            npc.BaseDefence = 10 + (level * 3);
            needsFix = true;
        }

        if (npc.BaseAgility <= 0)
        {
            npc.BaseAgility = 10 + (level * 3);
            needsFix = true;
        }

        if (npc.BaseDexterity <= 0)
        {
            npc.BaseDexterity = 10 + (level * 2);
            if (npc.Class == CharacterClass.Assassin)
                npc.BaseDexterity += level * 3;
            needsFix = true;
        }

        if (npc.BaseStamina <= 0)
        {
            npc.BaseStamina = 10 + (level * 4);
            needsFix = true;
        }

        if (npc.BaseConstitution <= 0)
        {
            npc.BaseConstitution = 10 + (level * 2);
            needsFix = true;
        }

        if (npc.BaseIntelligence <= 0)
        {
            npc.BaseIntelligence = 10 + (level * 2);
            if (npc.Class == CharacterClass.Magician)
                npc.BaseIntelligence += level * 3;
            needsFix = true;
        }

        if (npc.BaseWisdom <= 0)
        {
            npc.BaseWisdom = 10 + (level * 2);
            if (npc.Class == CharacterClass.Cleric || npc.Class == CharacterClass.Paladin)
                npc.BaseWisdom += level * 2;
            needsFix = true;
        }

        if (npc.BaseCharisma <= 0)
        {
            npc.BaseCharisma = 10;
            needsFix = true;
        }

        if (npc.BaseMaxHP <= 0)
        {
            // Calculate based on class
            npc.BaseMaxHP = npc.Class switch
            {
                CharacterClass.Warrior or CharacterClass.Barbarian => 100 + (level * 50),
                CharacterClass.Magician => 50 + (level * 25),
                CharacterClass.Cleric or CharacterClass.Paladin => 80 + (level * 40),
                CharacterClass.Assassin => 70 + (level * 35),
                CharacterClass.Sage => 90 + (level * 45),
                _ => 80 + (level * 40)
            };
            needsFix = true;
        }

        if (npc.BaseMaxMana <= 0 && (npc.Class == CharacterClass.Magician ||
            npc.Class == CharacterClass.Cleric || npc.Class == CharacterClass.Paladin ||
            npc.Class == CharacterClass.Sage))
        {
            npc.BaseMaxMana = npc.Class switch
            {
                CharacterClass.Magician => 50 + (level * 30),
                CharacterClass.Cleric or CharacterClass.Paladin => 40 + (level * 20),
                CharacterClass.Sage => 30 + (level * 15),
                _ => 0
            };
            needsFix = true;
        }

        if (needsFix)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC",
                $"Fixed corrupted base stats for {npc.Name} (Level {level} {npc.Class}): " +
                $"STR={npc.BaseStrength}, DEF={npc.BaseDefence}, AGI={npc.BaseAgility}");
        }
    }

    /// <summary>
    /// Process NPC aging - update ages from BirthDate and trigger natural death
    /// when an NPC exceeds their race's maximum lifespan.
    /// Age-related death is permanent - the soul has moved on.
    /// </summary>
    private void ProcessNPCAging()
    {
        foreach (var npc in npcs.Where(n => n.IsAlive && !n.IsDead && !n.IsAgedDeath).ToList())
        {
            // Skip story NPCs - they're needed for narrative quests
            if (npc.IsStoryNPC) continue;

            // Skip NPCs without birth dates (shouldn't happen, but safety)
            if (npc.BirthDate <= DateTime.MinValue) continue;

            // Calculate current age from birth date (using accelerated lifecycle rate)
            int previousAge = npc.Age;
            int currentAge = (int)((DateTime.Now - npc.BirthDate).TotalHours / GameConfig.NpcLifecycleHoursPerYear);
            npc.Age = currentAge;

            // Birthday announcement when age increments
            if (currentAge > previousAge && previousAge > 0)
            {
                NewsSystem.Instance?.WriteBirthdayNews(npc.Name2, currentAge, npc.Race.ToString());
            }

            // Check for natural death
            if (GameConfig.RaceLifespan.TryGetValue(npc.Race, out int maxAge))
            {
                if (currentAge >= maxAge)
                {
                    // Natural death - permanent, no respawn
                    npc.IsDead = true;
                    npc.IsAgedDeath = true;
                    npc.HP = 0;

                    // Remove from respawn queue if somehow queued
                    deadNPCRespawnTimers.Remove(npc.Name);

                    // Handle marriage - widow the spouse
                    if (npc.Married || npc.IsMarried)
                    {
                        HandleSpouseBereavement(npc);
                    }

                    NewsSystem.Instance?.Newsy(
                        $"⚱ {npc.Name2} has passed away peacefully at the age of {currentAge}. The soul moves on...");

                    UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                        $"{npc.Name2} died of old age at {currentAge} (max {maxAge} for {npc.Race})");
                }
            }
        }
    }

    /// <summary>
    /// Handle a spouse's bereavement when their partner dies of old age.
    /// Clears marriage state and adds a memory of the loss.
    /// </summary>
    private void HandleSpouseBereavement(NPC deceased)
    {
        var spouse = npcs.FirstOrDefault(n =>
            n.Name2 == deceased.SpouseName && (n.Married || n.IsMarried) && !n.IsDead);
        if (spouse != null)
        {
            spouse.Married = false;
            spouse.IsMarried = false;
            spouse.SpouseName = "";
            spouse.Brain?.Memory?.AddMemory(
                $"My beloved {deceased.Name2} has passed away...", "bereavement", DateTime.Now);

            // Clear the marriage in the registry
            NPCMarriageRegistry.Instance.EndMarriage(deceased.ID);

            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                $"{spouse.Name2} is now widowed after {deceased.Name2}'s passing");
        }
    }

    /// <summary>
    /// Process NPC pregnancies - handle births for pregnant NPCs and
    /// give married female NPCs a chance to become pregnant each tick.
    /// </summary>
    // Track which NPC is the father of a current pregnancy (for affairs where father != spouse)
    private readonly Dictionary<string, string> _pregnancyFathers = new();

    private void ProcessNPCPregnancies()
    {
        // Process existing pregnancies - check for births
        foreach (var npc in npcs.Where(n => n.IsAlive && !n.IsDead && n.PregnancyDueDate.HasValue).ToList())
        {
            if (DateTime.Now >= npc.PregnancyDueDate.Value)
            {
                // Baby is due! Find the father (could be affair partner, not spouse)
                string fatherName = npc.SpouseName;
                if (_pregnancyFathers.TryGetValue(npc.Name2 ?? npc.Name, out var affairFather))
                {
                    fatherName = affairFather;
                    _pregnancyFathers.Remove(npc.Name2 ?? npc.Name);
                }

                // Try to find the father - first alive, then even dead (for the child record)
                var father = npcs.FirstOrDefault(n =>
                    n.Name2 == fatherName && n.IsAlive && !n.IsDead);

                // If father is dead/missing, still create the child (use any NPC with that name)
                if (father == null)
                {
                    father = npcs.FirstOrDefault(n => n.Name2 == fatherName);
                }

                if (father != null)
                {
                    FamilySystem.Instance?.CreateNPCChild(npc, father);
                    UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                        $"Child born to {npc.Name2} and {father.Name2}!");
                    Console.Error.WriteLine($"[WORLDSIM] Birth: {npc.Name2} and {father.Name2} had a child!");
                }
                else
                {
                    // Father completely gone from the game - create child with mother only
                    UsurperRemake.Systems.DebugLogger.Instance.LogWarning("LIFECYCLE",
                        $"Birth: father '{fatherName}' not found for {npc.Name2}'s pregnancy. Creating child anyway.");
                    Console.Error.WriteLine($"[WORLDSIM] Birth: father '{fatherName}' not found, creating child with mother only");
                    FamilySystem.Instance?.CreateNPCChild(npc, npc); // Use mother as both parents
                }
                npc.PregnancyDueDate = null;
            }
        }

        // Calculate dynamic pregnancy rate based on population
        int aliveCount = npcs.Count(n => n.IsAlive && !n.IsDead);
        int childCount = FamilySystem.Instance?.AllChildren.Count(c => !c.Deleted) ?? 0;
        int totalPop = aliveCount + childCount;

        // Dynamic rate: higher when underpopulated, lower when overpopulated
        int pregnancyDenominator = totalPop < 40 ? 33   // ~3% if underpopulated
                                 : totalPop > 80 ? 200  // ~0.5% if overpopulated
                                 : 100;                  // ~1% normal

        // Eligible females (married or not) may become pregnant
        var eligibleFemales = npcs.Where(n =>
            n.IsAlive && !n.IsDead &&
            n.Sex == CharacterSex.Female &&
            !n.PregnancyDueDate.HasValue &&
            n.Age >= 18 && n.Age <= 45).ToList();

        foreach (var npc in eligibleFemales)
        {
            if (random.Next(pregnancyDenominator) > 0) continue;

            // Try to find a partner for this pregnancy
            NPC? father = null;
            bool isAffair = false;

            // If married, spouse is the default partner
            if (npc.Married || npc.IsMarried)
            {
                father = npcs.FirstOrDefault(n =>
                    n.Name2 == npc.SpouseName && n.IsAlive && !n.IsDead);

                // Flirtatious NPCs may conceive with someone other than their spouse
                var profile = npc.Brain?.Personality;
                if (profile != null && profile.Flirtatiousness > 0.6f && random.Next(100) < 15)
                {
                    // 15% chance for flirtatious married NPCs to have an affair pregnancy
                    var affairPartner = FindAffairPartner(npc);
                    if (affairPartner != null)
                    {
                        father = affairPartner;
                        isAffair = true;
                    }
                }
            }
            else
            {
                // Unmarried NPCs with high flirtatiousness can have casual pregnancies
                var profile = npc.Brain?.Personality;
                if (profile != null && profile.Flirtatiousness > 0.5f)
                {
                    father = FindAffairPartner(npc);
                    if (father != null) isAffair = true;
                }
            }

            if (father == null) continue;

            // Check couple doesn't have too many children already (max 4)
            var existingChildren = FamilySystem.Instance?.GetChildrenOfCouple(npc, father);
            if (existingChildren != null && existingChildren.Count >= 4) continue;

            // Pregnancy! Due in ~9 game months ≈ 7 hours at accelerated rate
            npc.PregnancyDueDate = DateTime.Now.AddHours(7);

            if (isAffair)
            {
                // Track the affair father so the child gets the right parentage
                _pregnancyFathers[npc.Name2 ?? npc.Name] = father.Name2;

                NewsSystem.Instance?.WriteAffairNews(npc.Name2, father.Name2);
            }
            else
            {
                NewsSystem.Instance?.Newsy(
                    $"♥ {npc.Name2} and {father.Name2} are expecting a child!");
            }

            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                $"{npc.Name2} is pregnant by {father.Name2} (affair={isAffair})! Due: {npc.PregnancyDueDate.Value:HH:mm}");
        }
    }

    /// <summary>
    /// Find a compatible affair partner for an NPC. Must be attracted to each other,
    /// alive, and of appropriate age.
    /// </summary>
    private NPC? FindAffairPartner(NPC npc)
    {
        var profile = npc.Brain?.Personality;
        if (profile == null) return null;

        var candidates = npcs.Where(c =>
            c.ID != npc.ID &&
            c.IsAlive && !c.IsDead &&
            c.Sex != npc.Sex && // Opposite sex for pregnancy
            c.Name2 != npc.SpouseName && // Not the current spouse
            c.Age >= 18 &&
            c.Brain?.Personality != null &&
            profile.IsAttractedTo(c.Brain.Personality.Gender) &&
            c.Brain.Personality.IsAttractedTo(profile.Gender)
        ).ToList();

        if (candidates.Count == 0) return null;

        // Prefer flirtatious partners
        var flirty = candidates.Where(c =>
            c.Brain?.Personality?.Flirtatiousness > 0.4f).ToList();

        if (flirty.Count > 0)
            return flirty[random.Next(flirty.Count)];

        return candidates[random.Next(candidates.Count)];
    }

    /// <summary>
    /// Process NPC divorces. Married couples may split based on personality mismatch,
    /// low commitment, or high flirtatiousness. Divorce frees both NPCs to remarry.
    /// </summary>
    private void ProcessNPCDivorces()
    {
        // Only process each couple once (by checking the female or alphabetically first partner)
        var marriedNPCs = npcs.Where(n =>
            n.IsAlive && !n.IsDead &&
            (n.Married || n.IsMarried) &&
            !string.IsNullOrEmpty(n.SpouseName))
            .ToList();

        // Track already-processed couples to avoid double-divorce
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var npc in marriedNPCs)
        {
            if (processed.Contains(npc.Name2 ?? npc.Name)) continue;

            var spouse = npcs.FirstOrDefault(n =>
                n.Name2 == npc.SpouseName && n.IsAlive && !n.IsDead);
            if (spouse == null) continue;

            processed.Add(npc.Name2 ?? npc.Name);
            processed.Add(spouse.Name2 ?? spouse.Name);

            // Base divorce chance: 0.3% per tick (~1 per 5 hours of sim time)
            float divorceChance = 0.003f;

            var profile1 = npc.Brain?.Personality;
            var profile2 = spouse.Brain?.Personality;

            if (profile1 != null && profile2 != null)
            {
                // Low commitment increases divorce chance
                if (profile1.Commitment < 0.3f) divorceChance += 0.005f;
                if (profile2.Commitment < 0.3f) divorceChance += 0.005f;

                // High flirtatiousness increases divorce chance
                if (profile1.Flirtatiousness > 0.7f) divorceChance += 0.003f;
                if (profile2.Flirtatiousness > 0.7f) divorceChance += 0.003f;

                // Alignment mismatch increases divorce chance
                bool oneGood = npc.Chivalry > npc.Darkness;
                bool otherGood = spouse.Chivalry > spouse.Darkness;
                if (oneGood != otherGood) divorceChance += 0.004f;

                // High commitment in BOTH reduces divorce chance significantly
                if (profile1.Commitment > 0.7f && profile2.Commitment > 0.7f)
                    divorceChance *= 0.2f;
            }

            if (random.NextDouble() >= divorceChance) continue;

            // Divorce!
            npc.Married = false;
            npc.IsMarried = false;
            npc.SpouseName = "";

            spouse.Married = false;
            spouse.IsMarried = false;
            spouse.SpouseName = "";

            // Clear pregnancy if pregnant by this spouse
            if (npc.PregnancyDueDate.HasValue) npc.PregnancyDueDate = null;
            if (spouse.PregnancyDueDate.HasValue) spouse.PregnancyDueDate = null;

            // End marriage in registry
            NPCMarriageRegistry.Instance.EndMarriage(npc.ID);

            // Add memories
            npc.Brain?.Memory?.AddMemory(
                $"I divorced {spouse.Name2}.", "divorce", DateTime.Now);
            spouse.Brain?.Memory?.AddMemory(
                $"I divorced {npc.Name2}.", "divorce", DateTime.Now);

            NewsSystem.Instance?.WriteDivorceNews(npc.Name2, spouse.Name2);

            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIFECYCLE",
                $"{npc.Name2} and {spouse.Name2} have divorced!");
        }
    }

    /// <summary>
    /// Process additional NPC activities like dungeon runs, shopping, training
    /// </summary>
    private void ProcessNPCActivities(NPC npc, WorldState world)
    {
        // 15% chance per tick to do something interesting
        if (random.NextDouble() > 0.15) return;

        // Weight activities based on NPC state
        var activities = new List<(string action, double weight)>();

        // Dungeon exploration - if HP is decent and level appropriate
        if (npc.HP > npc.MaxHP * 0.4 && npc.Level >= 1)
        {
            activities.Add(("dungeon", 0.30));
        }

        // Shopping - if has gold
        if (npc.Gold > 100)
        {
            activities.Add(("shop", 0.20));
        }

        // Training at gym
        if (npc.Gold > 50)
        {
            activities.Add(("train", 0.15));
        }

        // Visit level master if eligible
        long expForNextLevel = GetExperienceForLevel(npc.Level + 1);
        if (npc.Experience >= expForNextLevel && npc.Level < 100)
        {
            activities.Add(("levelup", 0.30));
        }

        // Heal if wounded
        if (npc.HP < npc.MaxHP * 0.5)
        {
            activities.Add(("heal", 0.35));
        }

        // Socialize/move around
        activities.Add(("move", 0.10));

        // Romance/Love Street - based on personality
        if (npc.Gold > 500 && npc.Brain?.Personality != null)
        {
            float romanceWeight = 0.05f; // Base chance
            // Higher romanticism = more likely to seek romance
            romanceWeight += npc.Brain.Personality.Romanticism * 0.10f;
            // Lower commitment = more likely to visit pleasure houses
            romanceWeight += (1f - npc.Brain.Personality.Commitment) * 0.05f;
            activities.Add(("love_street", romanceWeight));
        }

        // Temple worship - based on personality (faith/piety influences this)
        if (npc.Brain?.Personality != null)
        {
            float templeWeight = 0.05f; // Base chance
            // More lawful/good aligned characters visit temple more
            if (npc.Chivalry > npc.Darkness)
                templeWeight += 0.08f;
            // Lower aggression = more peaceful/spiritual
            templeWeight += (1f - npc.Brain.Personality.Aggression) * 0.05f;
            // Higher wisdom = more spiritual
            if (npc.Wisdom > 50)
                templeWeight += 0.05f;
            activities.Add(("temple", templeWeight));
        }

        // Bank visit - more likely if has gold to deposit or needs gold
        if (npc.Gold > 1000 || (npc.BankGold > 0 && npc.Gold < 100))
        {
            float bankWeight = 0.1f; // Base chance
            // Higher greed = more likely to visit bank
            if (npc.Brain?.Personality != null)
                bankWeight += npc.Brain.Personality.Greed * 0.08f;
            // More gold = more reason to deposit
            if (npc.Gold > 5000)
                bankWeight += 0.1f;
            activities.Add(("bank", bankWeight));
        }

        // Spouse going home - if this NPC is married to the player
        var romanceTracker = RomanceTracker.Instance;
        bool isPlayerSpouse = romanceTracker?.Spouses?.Any(s => s.NPCId == npc.ID) == true;
        bool isPlayerLover = romanceTracker?.CurrentLovers?.Any(l => l.NPCId == npc.ID) == true;

        if (isPlayerSpouse || isPlayerLover)
        {
            // Spouses and lovers have a higher chance to go "home" to be with the player
            float homeWeight = isPlayerSpouse ? 0.35f : 0.20f; // Spouses more likely
            // Higher romanticism = more likely to want to be home with partner
            if (npc.Brain?.Personality != null)
            {
                homeWeight += npc.Brain.Personality.Romanticism * 0.15f;
                homeWeight += npc.Brain.Personality.Commitment * 0.10f;
            }
            activities.Add(("go_home", homeWeight));
        }

        // Marketplace visit - if has items to sell or gold to buy
        if (npc.Gold > 200 || npc.MarketInventory.Count > 0)
        {
            float marketWeight = 0.12f; // Base 12% weight
            // Higher greed = more likely to trade
            if (npc.Brain?.Personality != null)
                marketWeight += npc.Brain.Personality.Greed * 0.06f;
            // More inventory = more reason to visit
            if (npc.MarketInventory.Count > 2)
                marketWeight += 0.05f;
            activities.Add(("marketplace", marketWeight));
        }

        // Castle visit - chance to apply for royal guard or seek audience
        // More likely for higher level, honorable NPCs
        if (npc.Level >= 5 && npc.Chivalry > npc.Darkness)
        {
            float castleWeight = 0.05f; // Base 5% weight
            // Higher chivalry = more likely to serve the crown
            castleWeight += Math.Min(0.10f, npc.Chivalry / 1000f);
            // Higher level = more qualified for guard duty
            if (npc.Level >= 10)
                castleWeight += 0.05f;
            activities.Add(("castle", castleWeight));
        }

        // Dark Alley - shady NPCs, thieves, assassins, high darkness characters
        if (npc.Brain?.Personality != null)
        {
            float alleyWeight = 0.03f; // Low base
            // High darkness alignment = drawn to the shadows
            if (npc.Darkness > npc.Chivalry)
                alleyWeight += 0.10f;
            // Assassins and thieves love the alley
            if (npc.Class == CharacterClass.Assassin || npc.Class == CharacterClass.Ranger)
                alleyWeight += 0.08f;
            // Higher greed = seeking black market deals
            alleyWeight += npc.Brain.Personality.Greed * 0.06f;
            // Higher aggression = more comfortable in rough areas
            alleyWeight += npc.Brain.Personality.Aggression * 0.04f;
            if (alleyWeight > 0.05f) // Only add if they'd actually go
                activities.Add(("dark_alley", alleyWeight));
        }

        // Inn - rest, socialize, drink, gossip
        {
            float innWeight = 0.08f; // Moderate base
            // Wounded NPCs rest at the inn
            if (npc.HP < npc.MaxHP * 0.7)
                innWeight += 0.08f;
            // Sociable NPCs like the inn
            if (npc.Brain?.Personality != null)
                innWeight += npc.Brain.Personality.Sociability * 0.06f;
            // Evening/night - everyone heads to the inn
            int hour = DateTime.Now.Hour;
            if (hour >= 18 || hour < 6)
                innWeight += 0.08f;
            activities.Add(("inn", innWeight));
        }

        // Team activities
        if (string.IsNullOrEmpty(npc.Team))
        {
            // Not in a team - consider joining or forming one
            // Always offer the option; personality check happens inside formation methods
            float teamWeight = 0.25f;
            if (npc.Brain?.Personality?.IsLikelyToJoinGang() == true)
                teamWeight = 0.35f; // Gang-oriented NPCs try harder
            activities.Add(("team_recruit", teamWeight));
        }
        else
        {
            // In a team - team activities
            if (npc.HP > npc.MaxHP * 0.6)
            {
                activities.Add(("team_dungeon", 0.20)); // Team dungeon run
            }
            activities.Add(("team_recruit", 0.15)); // Recruit more members
        }

        if (activities.Count == 0) return;

        // Apply personality-driven, time-of-day, and memory-based weight modifiers
        ApplyPersonalityWeights(activities, npc);
        ApplyTimeOfDayWeights(activities);
        ApplyMemoryWeights(activities, npc);

        // Weighted random selection
        double totalWeight = activities.Sum(a => a.weight);
        double roll = random.NextDouble() * totalWeight;
        double cumulative = 0;

        string selectedAction = "move";
        foreach (var (action, weight) in activities)
        {
            cumulative += weight;
            if (roll <= cumulative)
            {
                selectedAction = action;
                break;
            }
        }

        switch (selectedAction)
        {
            case "dungeon":
                NPCExploreDungeon(npc);
                npc.CurrentActivity = "exploring the dungeon depths";
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.5f, 90);
                npc.EmotionalState?.AddEmotion(EmotionType.Fear, 0.3f, 60);
                break;
            case "shop":
                NPCGoShopping(npc);
                npc.CurrentActivity = npc.CurrentLocation == "Weapon Shop"
                    ? "examining a blade on the rack"
                    : "browsing the armor on display";
                npc.EmotionalState?.AddEmotion(EmotionType.Greed, 0.3f, 60);
                break;
            case "train":
                NPCTrainAtGym(npc);
                npc.CurrentActivity = "training with the practice dummies";
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.4f, 90);
                npc.EmotionalState?.AddEmotion(EmotionType.Pride, 0.3f, 60);
                break;
            case "levelup":
                NPCVisitMaster(npc);
                npc.CurrentActivity = "consulting with the Level Master";
                npc.EmotionalState?.AddEmotion(EmotionType.Pride, 0.6f, 120);
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.5f, 120);
                npc.EmotionalState?.AddEmotion(EmotionType.Hope, 0.4f, 90);
                break;
            case "heal":
                NPCVisitHealer(npc);
                npc.CurrentActivity = "browsing the healing potions";
                npc.EmotionalState?.AddEmotion(EmotionType.Hope, 0.4f, 60);
                npc.EmotionalState?.AddEmotion(EmotionType.Peace, 0.3f, 90);
                break;
            case "move":
                MoveNPCToRandomLocation(npc);
                npc.CurrentActivity = "passing through";
                break;
            case "team_recruit":
                NPCTeamRecruitment(npc);
                npc.CurrentActivity = "looking for recruits";
                npc.EmotionalState?.AddEmotion(EmotionType.Hope, 0.3f, 60);
                break;
            case "team_dungeon":
                NPCTeamDungeonRun(npc);
                npc.CurrentActivity = "rallying the team for a dungeon run";
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.5f, 90);
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.4f, 90);
                break;
            case "love_street":
                NPCVisitLoveStreet(npc);
                npc.CurrentActivity = "enjoying the evening company";
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.5f, 120);
                npc.EmotionalState?.AddEmotion(EmotionType.Peace, 0.3f, 90);
                break;
            case "temple":
                NPCVisitTemple(npc);
                npc.CurrentActivity = "praying quietly";
                npc.EmotionalState?.AddEmotion(EmotionType.Peace, 0.5f, 120);
                npc.EmotionalState?.AddEmotion(EmotionType.Hope, 0.4f, 90);
                break;
            case "bank":
                NPCVisitBank(npc);
                npc.CurrentActivity = "counting coins at the counter";
                npc.EmotionalState?.AddEmotion(EmotionType.Greed, 0.3f, 60);
                break;
            case "marketplace":
                NPCVisitMarketplace(npc);
                npc.CurrentActivity = "haggling with a merchant";
                npc.EmotionalState?.AddEmotion(EmotionType.Greed, 0.4f, 60);
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.2f, 60);
                break;
            case "castle":
                NPCVisitCastle(npc);
                npc.CurrentActivity = "attending to court business";
                npc.EmotionalState?.AddEmotion(EmotionType.Pride, 0.4f, 90);
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.3f, 60);
                break;
            case "go_home":
                NPCGoHome(npc);
                npc.CurrentActivity = "heading home for the day";
                npc.EmotionalState?.AddEmotion(EmotionType.Peace, 0.4f, 120);
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.3f, 90);
                break;
            case "dark_alley":
                NPCVisitDarkAlley(npc);
                npc.CurrentActivity = "lurking in the shadows";
                npc.EmotionalState?.AddEmotion(EmotionType.Greed, 0.4f, 90);
                npc.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.3f, 60);
                break;
            case "inn":
                NPCVisitInn(npc);
                npc.CurrentActivity = "having a drink at the bar";
                npc.EmotionalState?.AddEmotion(EmotionType.Joy, 0.3f, 60);
                npc.EmotionalState?.AddEmotion(EmotionType.Peace, 0.3f, 90);
                break;
        }
    }

    /// <summary>
    /// Multiply an activity weight in the list. No-op if activity not present.
    /// </summary>
    private static void MultiplyWeight(List<(string action, double weight)> activities, string action, double multiplier)
    {
        for (int i = 0; i < activities.Count; i++)
        {
            if (activities[i].action == action)
            {
                activities[i] = (action, activities[i].weight * multiplier);
                return;
            }
        }
    }

    /// <summary>
    /// Apply personality-driven weight modifiers so NPCs behave according to their traits.
    /// Aggressive NPCs dungeon more, scholarly NPCs train more, sociable NPCs socialize more, etc.
    /// </summary>
    private static void ApplyPersonalityWeights(List<(string action, double weight)> activities, NPC npc)
    {
        var p = npc.Brain?.Personality;
        if (p == null) return;

        if (p.Aggression > 0.6f)
        {
            MultiplyWeight(activities, "dungeon", 1.5);
            MultiplyWeight(activities, "team_dungeon", 1.4);
            MultiplyWeight(activities, "train", 1.3);
            MultiplyWeight(activities, "dark_alley", 1.4);
            MultiplyWeight(activities, "shop", 0.7);
        }
        if (p.Sociability > 0.6f)
        {
            MultiplyWeight(activities, "love_street", 1.5);
            MultiplyWeight(activities, "inn", 1.5);
            MultiplyWeight(activities, "move", 1.4); // wander and meet people
            MultiplyWeight(activities, "temple", 0.8);
        }
        if (p.Greed > 0.6f)
        {
            MultiplyWeight(activities, "shop", 1.5);
            MultiplyWeight(activities, "bank", 1.5);
            MultiplyWeight(activities, "marketplace", 1.4);
            MultiplyWeight(activities, "dark_alley", 1.3);
            MultiplyWeight(activities, "dungeon", 1.2);
        }
        if (p.Intelligence > 0.6f)
        {
            MultiplyWeight(activities, "train", 1.4);
            MultiplyWeight(activities, "shop", 1.2);
            MultiplyWeight(activities, "dungeon", 0.8);
        }
        if (p.Mysticism > 0.6f)
        {
            MultiplyWeight(activities, "temple", 1.5);
            MultiplyWeight(activities, "train", 1.2);
            MultiplyWeight(activities, "shop", 0.8);
        }
        if (p.Caution > 0.6f)
        {
            MultiplyWeight(activities, "heal", 1.5);
            MultiplyWeight(activities, "bank", 1.3);
            MultiplyWeight(activities, "dungeon", 0.6);
            MultiplyWeight(activities, "team_dungeon", 0.7);
        }
        if (p.Courage > 0.7f)
        {
            MultiplyWeight(activities, "dungeon", 1.4);
            MultiplyWeight(activities, "team_dungeon", 1.3);
            MultiplyWeight(activities, "castle", 1.3);
        }
        if (p.Patience < 0.3f)
        {
            MultiplyWeight(activities, "move", 1.3); // restless, always moving
            MultiplyWeight(activities, "dungeon", 1.3);
            MultiplyWeight(activities, "temple", 0.5);
        }
        // Sociable and ambitious NPCs are more likely to form/join teams
        if (p.Sociability > 0.5f)
        {
            MultiplyWeight(activities, "team_recruit", 1.4);
        }
        if (p.Ambition > 0.6f)
        {
            MultiplyWeight(activities, "team_recruit", 1.3);
        }
        if (p.Loyalty > 0.6f)
        {
            MultiplyWeight(activities, "team_recruit", 1.2);
        }
    }

    /// <summary>
    /// Apply time-of-day weight modifiers. NPCs train in the morning, shop in the afternoon,
    /// socialize in the evening, and go home at night.
    /// </summary>
    private static void ApplyTimeOfDayWeights(List<(string action, double weight)> activities)
    {
        int hour = DateTime.Now.Hour;

        if (hour >= 6 && hour < 12) // Morning
        {
            MultiplyWeight(activities, "train", 1.3);
            MultiplyWeight(activities, "shop", 1.2);
            MultiplyWeight(activities, "temple", 1.3);
        }
        else if (hour >= 12 && hour < 18) // Afternoon
        {
            MultiplyWeight(activities, "dungeon", 1.3);
            MultiplyWeight(activities, "team_dungeon", 1.3);
            MultiplyWeight(activities, "shop", 1.2);
            MultiplyWeight(activities, "marketplace", 1.2);
        }
        else if (hour >= 18 && hour < 23) // Evening
        {
            MultiplyWeight(activities, "move", 1.5); // socializing
            MultiplyWeight(activities, "love_street", 1.4);
            MultiplyWeight(activities, "inn", 1.5); // evening drinks
            MultiplyWeight(activities, "dark_alley", 1.6); // shady business after dark
            MultiplyWeight(activities, "go_home", 1.3);
        }
        else // Night (23-5)
        {
            MultiplyWeight(activities, "go_home", 2.0);
            MultiplyWeight(activities, "inn", 1.3); // late-night drinkers
            MultiplyWeight(activities, "dark_alley", 1.8); // dark alley thrives at night
            MultiplyWeight(activities, "dungeon", 0.5);
            MultiplyWeight(activities, "team_dungeon", 0.5);
            MultiplyWeight(activities, "shop", 0.3);
            MultiplyWeight(activities, "marketplace", 0.3);
            MultiplyWeight(activities, "heal", 0.5);
        }
    }

    /// <summary>
    /// Apply memory-driven weight modifiers. NPCs who were recently attacked seek healing,
    /// those who were betrayed stay home, those who traded return to shops.
    /// </summary>
    private static void ApplyMemoryWeights(List<(string action, double weight)> activities, NPC npc)
    {
        var memories = npc.Brain?.Memory?.GetRecentEvents(48); // last 48 hours
        if (memories == null || memories.Count == 0) return;

        // Check the 5 most recent memories for behavioral influence
        foreach (var mem in memories.Take(5))
        {
            switch (mem.Type)
            {
                case MemoryType.Attacked:
                case MemoryType.Defeated:
                    MultiplyWeight(activities, "heal", 1.3);
                    MultiplyWeight(activities, "dungeon", 0.7);
                    MultiplyWeight(activities, "train", 1.2);
                    break;
                case MemoryType.Betrayed:
                    MultiplyWeight(activities, "go_home", 1.3);
                    MultiplyWeight(activities, "move", 0.7);
                    MultiplyWeight(activities, "dungeon", 0.8);
                    break;
                case MemoryType.Helped:
                case MemoryType.Saved:
                case MemoryType.Defended:
                    MultiplyWeight(activities, "dungeon", 1.1);
                    MultiplyWeight(activities, "team_dungeon", 1.1);
                    break;
                case MemoryType.Traded:
                case MemoryType.BoughtItem:
                case MemoryType.SoldItem:
                    MultiplyWeight(activities, "shop", 1.2);
                    MultiplyWeight(activities, "marketplace", 1.2);
                    break;
                case MemoryType.SawDeath:
                    MultiplyWeight(activities, "temple", 1.3);
                    MultiplyWeight(activities, "dungeon", 0.7);
                    break;
            }
        }
    }

    /// <summary>
    /// NPC attempts to form or join a team, or recruit members
    /// </summary>
    private void NPCTeamRecruitment(NPC npc)
    {
        if (string.IsNullOrEmpty(npc.Team))
        {
            // Try to join an existing team or form a new one
            NPCTryJoinOrFormTeam(npc);
        }
        else
        {
            // Already in a team - try to recruit others
            NPCTryRecruitForTeam(npc);
        }
    }

    /// <summary>
    /// NPC tries to join an existing team or form a new one
    /// </summary>
    private void NPCTryJoinOrFormTeam(NPC npc)
    {
        // Exclude the player's team - NPCs shouldn't autonomously join it
        var player = GameEngine.Instance?.CurrentPlayer as Player;
        string? playerTeam = (!string.IsNullOrEmpty(player?.Team)) ? player.Team : null;

        // Look for existing NPC teams at this location to join
        var teamsAtLocation = npcs
            .Where(n => n.IsAlive && !string.IsNullOrEmpty(n.Team) && n.CurrentLocation == npc.CurrentLocation &&
                        !(playerTeam != null && n.Team.Equals(playerTeam, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(n => n.Team)
            .Where(g => g.Count() < MAX_TEAM_SIZE)
            .ToList();

        if (teamsAtLocation.Any() && random.NextDouble() < 0.75)
        {
            // Try to join an existing team
            var teamGroup = teamsAtLocation[random.Next(teamsAtLocation.Count)];
            var teamLeader = teamGroup.FirstOrDefault();
            if (teamLeader != null)
            {
                // Check compatibility with team leader
                var compatibility = npc.Brain?.Personality?.GetCompatibility(teamLeader.Brain?.Personality) ?? 0.5f;
                if (compatibility > 0.3f)
                {
                    // Join the team!
                    npc.Team = teamLeader.Team;
                    npc.TeamPW = teamLeader.TeamPW;
                    npc.CTurf = teamLeader.CTurf;

                    NewsSystem.Instance.Newsy(true, $"{npc.Name} joined the team '{npc.Team}'!");
                    // GD.Print($"[WorldSim] {npc.Name} joined team '{npc.Team}'");
                    return;
                }
            }
        }

        // Form a new team if we have compatible NPCs nearby (or anywhere if no locals found)
        var nearbyUnteamed = npcs
            .Where(n => n.IsAlive && !n.IsDead &&
                   string.IsNullOrEmpty(n.Team) &&
                   n.CurrentLocation == npc.CurrentLocation &&
                   n.Id != npc.Id)
            .ToList();

        // Fall back to any unaffiliated NPC in the realm if nobody at this location
        if (nearbyUnteamed.Count == 0)
        {
            nearbyUnteamed = npcs
                .Where(n => n.IsAlive && !n.IsDead &&
                       string.IsNullOrEmpty(n.Team) &&
                       n.Id != npc.Id &&
                       n.Brain?.Personality?.IsLikelyToJoinGang() == true)
                .ToList();
        }

        if (nearbyUnteamed.Count >= 1 && random.NextDouble() < 0.5)
        {
            // Form a new team
            var teamName = GenerateTeamName();
            var teamPassword = Guid.NewGuid().ToString().Substring(0, 8);

            npc.Team = teamName;
            npc.TeamPW = teamPassword;
            npc.CTurf = false;
            npc.TeamRec = 0;

            // Add first recruit - try a few candidates for compatibility
            NPC? bestRecruit = null;
            float bestCompat = 0f;
            int tries = Math.Min(nearbyUnteamed.Count, 5);
            for (int t = 0; t < tries; t++)
            {
                var candidate = nearbyUnteamed[random.Next(nearbyUnteamed.Count)];
                var compat = npc.Brain?.Personality?.GetCompatibility(candidate.Brain?.Personality) ?? 0.5f;
                if (compat > bestCompat) { bestCompat = compat; bestRecruit = candidate; }
            }

            if (bestRecruit != null && bestCompat > 0.25f)
            {
                bestRecruit.Team = teamName;
                bestRecruit.TeamPW = teamPassword;
                bestRecruit.CTurf = false;

                NewsSystem.Instance.Newsy(true, $"{npc.Name} formed a new team called '{teamName}' with {bestRecruit.Name}!");
                // GD.Print($"[WorldSim] {npc.Name} formed team '{teamName}' with {bestRecruit.Name}");
            }
            else
            {
                // No compatible recruit found - undo the team assignment
                npc.Team = "";
                npc.TeamPW = "";
            }
        }
    }

    /// <summary>
    /// NPC tries to recruit others into their team
    /// </summary>
    private void NPCTryRecruitForTeam(NPC npc)
    {
        if (string.IsNullOrEmpty(npc.Team)) return;

        // Don't let NPCs autonomously recruit into the player's team
        var player = GameEngine.Instance?.CurrentPlayer as Player;
        if (player != null && !string.IsNullOrEmpty(player.Team) &&
            npc.Team.Equals(player.Team, StringComparison.OrdinalIgnoreCase))
            return;

        // Check current team size
        var teamSize = npcs.Count(n => n.Team == npc.Team && n.IsAlive);
        if (teamSize >= MAX_TEAM_SIZE) return;

        // Find candidates at this location
        var candidates = npcs
            .Where(n => n.IsAlive &&
                   string.IsNullOrEmpty(n.Team) &&
                   n.CurrentLocation == npc.CurrentLocation &&
                   n.Id != npc.Id)
            .ToList();

        if (candidates.Count == 0) return;

        var candidate = candidates[random.Next(candidates.Count)];
        var compatibility = npc.Brain?.Personality?.GetCompatibility(candidate.Brain?.Personality) ?? 0.5f;

        // Base recruitment chance on compatibility, charisma, and candidate's gang-joining tendency
        float recruitChance = compatibility * 0.5f;
        recruitChance += (npc.Charisma / 100f) * 0.2f;
        if (candidate.Brain?.Personality?.IsLikelyToJoinGang() == true)
        {
            recruitChance += 0.2f;
        }

        if (random.NextDouble() < recruitChance)
        {
            candidate.Team = npc.Team;
            candidate.TeamPW = npc.TeamPW;
            candidate.CTurf = npc.CTurf;

            if (random.NextDouble() < 0.3) // 30% chance to announce
            {
                NewsSystem.Instance.Newsy(true, $"{npc.Name} recruited {candidate.Name} into '{npc.Team}'!");
            }
            // GD.Print($"[WorldSim] {npc.Name} recruited {candidate.Name} into team '{npc.Team}'");
        }
    }

    /// <summary>
    /// Generate a random team name
    /// </summary>
    private string GenerateTeamName()
    {
        var prefix = TeamNamePrefixes[random.Next(TeamNamePrefixes.Length)];
        var suffix = TeamNameSuffixes[random.Next(TeamNameSuffixes.Length)];
        return $"{prefix} {suffix}";
    }

    /// <summary>
    /// NPC does a team dungeon run with teammates
    /// </summary>
    private void NPCTeamDungeonRun(NPC npc)
    {
        if (string.IsNullOrEmpty(npc.Team)) return;

        // Get all alive team members
        var teamMembers = npcs
            .Where(n => n.Team == npc.Team && n.IsAlive && n.HP > n.MaxHP * 0.5)
            .ToList();

        if (teamMembers.Count < 2)
        {
            // Not enough healthy teammates, do solo dungeon run
            NPCExploreDungeon(npc);
            return;
        }

        // Move team to dungeon
        foreach (var member in teamMembers)
        {
            member.UpdateLocation("Dungeon");
        }

        // Determine dungeon level based on average team level
        int avgLevel = (int)teamMembers.Average(m => m.Level);
        int dungeonLevel = Math.Max(1, avgLevel + random.Next(-2, 4));
        dungeonLevel = Math.Min(dungeonLevel, 100);

        // Generate monster group (teams fight groups of monsters)
        int monsterCount = Math.Min(teamMembers.Count, random.Next(2, 5));
        var monsters = new List<Monster>();
        for (int i = 0; i < monsterCount; i++)
        {
            monsters.Add(MonsterGenerator.GenerateMonster(dungeonLevel));
        }

        // Team combat simulation
        bool teamWon = SimulateTeamVsMonsterCombat(teamMembers, monsters, out long totalExp, out long totalGold);

        if (teamWon)
        {
            // Distribute rewards evenly among surviving team members
            var survivors = teamMembers.Where(m => m.IsAlive).ToList();
            if (survivors.Count > 0)
            {
                long expShare = totalExp / survivors.Count;
                long goldShare = totalGold / survivors.Count;

                // Bonus for team play, throttled by NpcXpMultiplier for world sim mode
                expShare = (long)(expShare * 1.15 * NpcXpMultiplier);

                foreach (var member in survivors)
                {
                    member.GainExperience(expShare);
                    member.GainGold(goldShare);
                }

                // Generate news for notable victories
                if (random.NextDouble() < 0.15 || monsters.Any(m => m.IsBoss))
                {
                    NewsSystem.Instance.Newsy(true, $"Team '{npc.Team}' conquered dungeon level {dungeonLevel}, defeating {monsterCount} monsters!");
                }

                // GD.Print($"[WorldSim] Team '{npc.Team}' won! {survivors.Count} survivors shared {totalExp} XP and {totalGold} gold");
            }
        }
        else
        {
            // Team lost - check for deaths
            var dead = teamMembers.Where(m => !m.IsAlive).ToList();
            if (dead.Any())
            {
                var killerName = monsters.FirstOrDefault()?.Name ?? "dungeon monsters";
                foreach (var deadMember in dead)
                {
                    deadMember.IsDead = true;
                    QueueNPCForRespawn(deadMember.Name);
                    NewsSystem.Instance.WriteDeathNews(deadMember.Name, killerName, "the Dungeon");
                }
                GD.Print($"[WorldSim] Team '{npc.Team}' was defeated! {dead.Count} members died");
            }

            // Survivors flee
            foreach (var survivor in teamMembers.Where(m => m.IsAlive))
            {
                survivor.UpdateLocation("Main Street");
            }
        }
    }

    /// <summary>
    /// Simulate team vs monster group combat
    /// </summary>
    private bool SimulateTeamVsMonsterCombat(List<NPC> team, List<Monster> monsters, out long totalExp, out long totalGold)
    {
        totalExp = 0;
        totalGold = 0;
        int rounds = 0;
        const int maxRounds = 40;

        while (team.Any(m => m.IsAlive) && monsters.Any(m => m.IsAlive) && rounds < maxRounds)
        {
            rounds++;

            // Team attacks monsters
            foreach (var member in team.Where(m => m.IsAlive))
            {
                var target = monsters.Where(m => m.IsAlive).OrderBy(_ => random.Next()).FirstOrDefault();
                if (target == null) break;

                // Attack calculation with team coordination bonus
                long damage = Math.Max(1, member.Strength + member.WeapPow - target.Defence);
                damage += random.Next(1, (int)Math.Max(2, member.WeapPow / 3));
                damage = (long)(damage * 1.1); // 10% team coordination bonus

                target.HP -= damage;

                if (!target.IsAlive)
                {
                    totalExp += target.GetExperienceReward();
                    totalGold += target.GetGoldReward();
                }
            }

            // Monsters attack team
            foreach (var monster in monsters.Where(m => m.IsAlive))
            {
                var target = team.Where(m => m.IsAlive).OrderBy(_ => random.Next()).FirstOrDefault();
                if (target == null) break;

                // Monster attack - slightly reduced against teams (they help each other)
                long damage = Math.Max(1, monster.Strength + monster.WeapPow - target.Defence - target.ArmPow);
                damage += random.Next(1, (int)Math.Max(2, monster.WeapPow / 3));
                damage = (long)(damage * 0.85); // 15% damage reduction due to team support

                target.TakeDamage(damage);
            }
        }

        return team.Any(m => m.IsAlive) && !monsters.Any(m => m.IsAlive);
    }

    /// <summary>
    /// NPC explores the dungeon and fights monsters
    /// </summary>
    private void NPCExploreDungeon(NPC npc)
    {
        npc.UpdateLocation("Dungeon");

        // Determine dungeon level based on NPC level - sometimes they push too deep
        int dungeonLevel = Math.Max(1, npc.Level + random.Next(-3, 6));
        // Ambitious/courageous NPCs push even deeper
        if (npc.Brain?.Personality != null &&
            (npc.Brain.Personality.Courage > 0.7f || npc.Brain.Personality.Ambition > 0.7f))
        {
            dungeonLevel += random.Next(0, 5);
        }
        dungeonLevel = Math.Min(dungeonLevel, 100);

        // Generate a monster
        var monster = MonsterGenerator.GenerateMonster(dungeonLevel);

        // Simulate combat
        int rounds = 0;
        bool npcWon = false;

        while (npc.IsAlive && monster.IsAlive && rounds < 50)
        {
            rounds++;

            // NPC attacks
            long npcDamage = Math.Max(1, npc.Strength + npc.WeapPow - monster.Defence);
            npcDamage += random.Next(1, (int)Math.Max(1, npc.WeapPow / 2));
            monster.HP -= npcDamage;

            if (!monster.IsAlive)
            {
                npcWon = true;
                break;
            }

            // Monster attacks
            long monsterDamage = Math.Max(1, monster.Strength + monster.WeapPow - npc.Defence);
            monsterDamage += random.Next(1, (int)Math.Max(1, monster.WeapPow / 2));
            npc.TakeDamage(monsterDamage);
        }

        if (npcWon)
        {
            // NPC wins - gain XP and gold (XP throttled by NpcXpMultiplier for world sim mode)
            long expGain = (long)(monster.GetExperienceReward() * NpcXpMultiplier);
            long goldGain = monster.GetGoldReward();

            npc.GainExperience(expGain);
            npc.GainGold(goldGain);

            // 20% chance to find loot if has inventory space
            if (random.NextDouble() < 0.20 && npc.MarketInventory.Count < npc.MaxMarketInventory)
            {
                var loot = NPCItemGenerator.GenerateDungeonLoot(npc, dungeonLevel);
                npc.MarketInventory.Add(loot);
                // GD.Print($"[WorldSim] {npc.Name} found {loot.Name} in the dungeon!");
            }

            // Generate news for notable victories
            if (monster.IsBoss || monster.Level >= npc.Level + 5 || random.NextDouble() < 0.1)
            {
                string newsMsg = monster.IsBoss
                    ? $"{npc.Name} defeated the mighty {monster.Name} in the dungeon depths!"
                    : $"{npc.Name} slew a {monster.Name} (Lv{monster.Level}) and earned {goldGain} gold.";
                NewsSystem.Instance.Newsy(true, newsMsg);
            }

            // GD.Print($"[WorldSim] {npc.Name} defeated {monster.Name}, gained {expGain} XP and {goldGain} gold");
        }
        else if (!npc.IsAlive)
        {
            // NPC died - mark as dead and queue for respawn
            npc.IsDead = true;
            QueueNPCForRespawn(npc.Name);
            NewsSystem.Instance.WriteDeathNews(npc.Name, monster.Name, "the Dungeon");
            GD.Print($"[WorldSim] {npc.Name} was slain by {monster.Name} in the dungeon!");
        }
        else
        {
            // Fled or timeout
            npc.UpdateLocation("Main Street");
            // GD.Print($"[WorldSim] {npc.Name} fled from {monster.Name}");
        }
    }

    /// <summary>
    /// NPC goes shopping for equipment using the modern equipment system
    /// </summary>
    private void NPCGoShopping(NPC npc)
    {
        EquipmentDatabase.Initialize();

        // Initialize EquippedItems if needed
        if (npc.EquippedItems == null)
            npc.EquippedItems = new Dictionary<EquipmentSlot, int>();

        bool boughtSomething = false;
        string itemBought = "";

        // Determine how much gold the NPC is willing to spend (30-70% of their gold)
        long spendingBudget = (long)(npc.Gold * (0.3 + random.NextDouble() * 0.4));

        // Try to upgrade weapon (50% of the time)
        if (random.NextDouble() < 0.5)
        {
            npc.UpdateLocation("Weapon Shop");

            // Get current weapon power
            int currentWeaponPower = 0;
            if (npc.EquippedItems.TryGetValue(EquipmentSlot.MainHand, out int weaponId))
            {
                var currentWeapon = EquipmentDatabase.GetById(weaponId);
                if (currentWeapon != null)
                    currentWeaponPower = currentWeapon.WeaponPower;
            }

            // Find a better weapon within budget
            var betterWeapon = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded)
                .Concat(EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.TwoHanded))
                .Where(w => w.Value <= spendingBudget && w.WeaponPower > currentWeaponPower)
                .OrderByDescending(w => w.WeaponPower)
                .FirstOrDefault();

            if (betterWeapon != null)
            {
                var (_, _, weaponTotalWithTax) = CityControlSystem.CalculateTaxedPrice(betterWeapon.Value);
                if (npc.Gold < weaponTotalWithTax) { betterWeapon = null; }
            }

            if (betterWeapon != null)
            {
                var (_, _, weaponTotal) = CityControlSystem.CalculateTaxedPrice(betterWeapon.Value);
                npc.SpendGold((int)weaponTotal);
                CityControlSystem.Instance.ProcessSaleTax(betterWeapon.Value);
                npc.EquippedItems[EquipmentSlot.MainHand] = betterWeapon.Id;

                // If new weapon is two-handed, remove off-hand item
                if (betterWeapon.Handedness == WeaponHandedness.TwoHanded)
                {
                    npc.EquippedItems.Remove(EquipmentSlot.OffHand);
                }

                boughtSomething = true;
                itemBought = betterWeapon.Name;
                npc.RecalculateStats();
            }
        }
        else
        {
            // Try to upgrade armor
            npc.UpdateLocation("Armor Shop");

            // Pick a random armor slot to try to upgrade
            var armorSlots = new[]
            {
                EquipmentSlot.Body, EquipmentSlot.Head, EquipmentSlot.Hands,
                EquipmentSlot.Feet, EquipmentSlot.Legs, EquipmentSlot.Arms
            };
            var targetSlot = armorSlots[random.Next(armorSlots.Length)];

            // Get current armor in that slot
            int currentArmorAC = 0;
            if (npc.EquippedItems.TryGetValue(targetSlot, out int armorId))
            {
                var currentArmor = EquipmentDatabase.GetById(armorId);
                if (currentArmor != null)
                    currentArmorAC = currentArmor.ArmorClass;
            }

            // Find better armor for that slot within budget
            var betterArmor = EquipmentDatabase.GetBySlot(targetSlot)
                .Where(a => a.Value <= spendingBudget && a.ArmorClass > currentArmorAC)
                .OrderByDescending(a => a.ArmorClass)
                .FirstOrDefault();

            if (betterArmor != null)
            {
                var (_, _, armorTotalWithTax) = CityControlSystem.CalculateTaxedPrice(betterArmor.Value);
                if (npc.Gold < armorTotalWithTax) betterArmor = null;
            }

            if (betterArmor != null)
            {
                var (_, _, armorTotal) = CityControlSystem.CalculateTaxedPrice(betterArmor.Value);
                npc.SpendGold((int)armorTotal);
                CityControlSystem.Instance.ProcessSaleTax(betterArmor.Value);
                npc.EquippedItems[targetSlot] = betterArmor.Id;
                boughtSomething = true;
                itemBought = betterArmor.Name;
                npc.RecalculateStats();
            }
        }

        if (boughtSomething && random.NextDouble() < 0.15)
        {
            NewsSystem.Instance.Newsy(true, $"{npc.Name} purchased {itemBought} from the shop.");
        }

        // GD.Print($"[WorldSim] {npc.Name} went shopping" + (boughtSomething ? $" and bought {itemBought}" : " but couldn't afford anything"));
    }

    /// <summary>
    /// NPC trains to improve stats
    /// </summary>
    private void NPCTrainAtGym(NPC npc)
    {
        npc.UpdateLocation("Gym");

        int trainingCost = npc.Level * 10 + 50;
        var (_, _, trainingTotalWithTax) = CityControlSystem.CalculateTaxedPrice(trainingCost);
        if (npc.Gold >= trainingTotalWithTax)
        {
            npc.SpendGold((int)trainingTotalWithTax);
            CityControlSystem.Instance.ProcessSaleTax(trainingCost);

            // Random stat increase - update BOTH current and base stats
            int statChoice = random.Next(4);
            string statName = "";
            switch (statChoice)
            {
                case 0:
                    npc.BaseStrength++;
                    statName = "Strength";
                    break;
                case 1:
                    npc.BaseDefence++;
                    statName = "Defence";
                    break;
                case 2:
                    npc.BaseAgility++;
                    statName = "Agility";
                    break;
                case 3:
                    npc.BaseMaxHP += 5;
                    statName = "Vitality";
                    break;
            }

            // Recalculate all stats from base values
            npc.RecalculateStats();

            // Restore HP if vitality was trained
            if (statChoice == 3)
            {
                npc.HP = Math.Min(npc.HP + 5, npc.MaxHP);
            }

            // GD.Print($"[WorldSim] {npc.Name} trained at the Gym and gained {statName}");

            // Occasionally newsworthy
            if (random.NextDouble() < 0.05)
            {
                NewsSystem.Instance.Newsy(true, $"{npc.Name} has been training hard at the Gym!");
            }
        }
    }

    /// <summary>
    /// NPC visits their master to level up
    /// </summary>
    private void NPCVisitMaster(NPC npc)
    {
        npc.UpdateLocation("Level Master");
        long expNeeded = GetExperienceForLevel(npc.Level + 1);
        if (npc.Experience >= expNeeded && npc.Level < 100)
        {
            npc.Level++;

            // Update base stats on level up (before equipment bonuses)
            npc.BaseMaxHP += 10 + random.Next(5, 15);
            npc.BaseStrength += random.Next(1, 3);
            npc.BaseDefence += random.Next(1, 2);

            // Recalculate all stats with equipment bonuses
            npc.RecalculateStats();

            // Restore HP to full on level up
            npc.HP = npc.MaxHP;

            // This is always newsworthy!
            NewsSystem.Instance?.WriteNPCLevelUpNews(npc.Name, npc.Level, npc.Class.ToString(), npc.Race.ToString());
        }
    }

    /// <summary>
    /// NPC visits the healer
    /// </summary>
    private void NPCVisitHealer(NPC npc)
    {
        npc.UpdateLocation("Healer");

        long healCost = (npc.MaxHP - npc.HP) * 2;
        var (_, _, healTotalWithTax) = CityControlSystem.CalculateTaxedPrice(healCost);
        if (npc.Gold >= healTotalWithTax && healCost > 0)
        {
            npc.SpendGold(healTotalWithTax);
            CityControlSystem.Instance.ProcessSaleTax(healCost);
            npc.HP = npc.MaxHP;
        }
        else if (npc.HP < npc.MaxHP)
        {
            // Partial heal - spend half of gold, accounting for tax
            long canAfford = npc.Gold / 2;
            // Work backwards from what they can afford to base cost
            var king = CastleLocation.GetCurrentKing();
            int totalTaxPercent = (king?.KingTaxPercent ?? 0) + (king?.CityTaxPercent ?? 0);
            long baseCostFromAffordable = totalTaxPercent > 0 ? (canAfford * 100) / (100 + totalTaxPercent) : canAfford;
            long hpToHeal = baseCostFromAffordable / 2;
            if (hpToHeal > 0)
            {
                var (_, _, partialHealTotal) = CityControlSystem.CalculateTaxedPrice(baseCostFromAffordable);
                npc.SpendGold(Math.Min(partialHealTotal, npc.Gold));
                CityControlSystem.Instance.ProcessSaleTax(baseCostFromAffordable);
                npc.HP = Math.Min(npc.HP + hpToHeal, npc.MaxHP);
            }
        }
    }

    /// <summary>
    /// Calculate XP needed for a level (matches player formula)
    /// </summary>
    private static long GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        long exp = 0;
        for (int i = 2; i <= level; i++)
        {
            exp += (long)(Math.Pow(i, 1.8) * 50);
        }
        return exp;
    }
    
    private void ExecuteNPCAction(NPC npc, NPCAction action, WorldState world)
    {
        switch (action.Type)
        {
            case ActionType.Idle:
                // NPC does nothing this turn
                break;
                
            case ActionType.Explore:
                MoveNPCToRandomLocation(npc);
                break;
                
            case ActionType.Trade:
                ExecuteTrade(npc, action.Target, world);
                break;
                
            case ActionType.Socialize:
                ExecuteSocialize(npc, action.Target, world);
                break;
                
            case ActionType.Attack:
                ExecuteAttack(npc, action.Target, world);
                break;
                
            case ActionType.Rest:
                ExecuteRest(npc);
                break;
                
            case ActionType.Train:
                ExecuteTrain(npc);
                break;
                
            case ActionType.JoinGang:
                ExecuteJoinGang(npc, action.Target, world);
                break;
                
            case ActionType.SeekRevenge:
                ExecuteSeekRevenge(npc, action.Target, world);
                break;
        }
    }
    
    private void MoveNPCToRandomLocation(NPC npc)
    {
        var newLocation = GameLocations[random.Next(GameLocations.Length)];

        if (newLocation != npc.CurrentLocation)
        {
            npc.UpdateLocation(newLocation);
            // GD.Print($"[WorldSim] {npc.Name} moved to {newLocation}");
        }
    }

    /// <summary>
    /// NPC spouse/lover goes home to be with the player
    /// </summary>
    private void NPCGoHome(NPC npc)
    {
        npc.UpdateLocation("Home");
        // GD.Print($"[WorldSim] {npc.Name} went home to be with their partner");

        // Small chance to generate sweet news about it
        if (random.NextDouble() < 0.15)
        {
            var romance = RomanceTracker.Instance;
            bool isSpouse = romance?.Spouses?.Any(s => s.NPCId == npc.ID) == true;

            string[] homeMessages = isSpouse ? new[]
            {
                $"{npc.Name} is spending quality time at home.",
                $"{npc.Name} prepared a warm meal at home.",
                $"{npc.Name} is waiting faithfully at home."
            } : new[]
            {
                $"{npc.Name} stopped by home for a visit.",
                $"{npc.Name} is relaxing at home.",
                $"{npc.Name} came home looking for company."
            };

            NewsSystem.Instance?.Newsy(false, homeMessages[random.Next(homeMessages.Length)]);
        }
    }

    /// <summary>
    /// NPC visits Love Street for romance or paid companionship
    /// </summary>
    private void NPCVisitLoveStreet(NPC npc)
    {
        npc.UpdateLocation("Love Street");
        // GD.Print($"[WorldSim] {npc.Name} visits Love Street");

        var personality = npc.Brain?.Personality;
        if (personality == null) return;

        // Check if NPC has a spouse - might cause jealousy drama
        var spouse = RomanceTracker.Instance?.Spouses?
            .FirstOrDefault(s => s.NPCId == npc.ID);

        // Decide what to do: socialize, seek romance, or use paid services
        float roll = (float)random.NextDouble();

        if (roll < 0.4f)
        {
            // Just socializing/looking for dates
            // GD.Print($"[WorldSim] {npc.Name} is looking for romance at Love Street");
            // Could meet other NPCs here for relationship building
        }
        else if (roll < 0.7f && npc.Gold >= 500)
        {
            // Use paid services
            int cost = random.Next(500, 5000);
            cost = Math.Min(cost, (int)npc.Gold);
            npc.SpendGold(cost);

            // Disease risk based on how cheap they went
            float diseaseChance = cost < 2000 ? 0.25f : cost < 5000 ? 0.15f : 0.05f;
            bool gotDisease = random.NextDouble() < diseaseChance;

            if (gotDisease)
            {
                // NPC contracted a disease
                npc.HP = Math.Max(1, npc.HP / 2);
                NewsSystem.Instance.Newsy(true,
                    $"{npc.Name} caught a nasty disease at Love Street!");
                // GD.Print($"[WorldSim] {npc.Name} contracted a disease at Love Street!");
            }
            else
            {
                // GD.Print($"[WorldSim] {npc.Name} spent {cost} gold at Love Street");
            }

            // Generate news occasionally
            if (random.NextDouble() < 0.3f)
            {
                NewsSystem.Instance.Newsy(true,
                    $"{npc.Name} was seen at Love Street last night.");
            }
        }
        else
        {
            // Looking for actual romance with other NPCs present
            var otherNPCs = npcs
                .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Love Street")
                .ToList();

            if (otherNPCs.Any())
            {
                var target = otherNPCs[random.Next(otherNPCs.Count)];
                bool isAttracted = personality.IsAttractedTo(
                    target.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male);

                if (isAttracted)
                {
                    // Improve relationship
                    RelationshipSystem.UpdateRelationship(npc, target, 1, 2, true);
                    // GD.Print($"[WorldSim] {npc.Name} and {target.Name} hit it off at Love Street");
                }
            }
        }
    }

    /// <summary>
    /// NPC visits the Temple to worship, sacrifice, or seek divine guidance
    /// </summary>
    private void NPCVisitTemple(NPC npc)
    {
        npc.UpdateLocation("Temple");
        // GD.Print($"[WorldSim] {npc.Name} visits the Temple");

        var personality = npc.Brain?.Personality;
        float roll = (float)random.NextDouble();

        // Decide what to do at the temple
        if (roll < 0.4f)
        {
            // Prayer and worship
            // GD.Print($"[WorldSim] {npc.Name} prays at the Temple");

            // Small chance of receiving a blessing
            if (random.NextDouble() < 0.1)
            {
                // NPC receives minor blessing
                int statBoost = random.Next(1, 3);
                switch (random.Next(3))
                {
                    case 0:
                        npc.Strength += statBoost;
                        break;
                    case 1:
                        npc.Wisdom += statBoost;
                        break;
                    case 2:
                        npc.HP = Math.Min(npc.HP + npc.MaxHP / 4, npc.MaxHP);
                        break;
                }
                NewsSystem.Instance.Newsy(false, $"{npc.Name} received a divine blessing at the Temple.");
                // GD.Print($"[WorldSim] {npc.Name} received a divine blessing!");
            }
        }
        else if (roll < 0.6f && npc.Gold >= 500)
        {
            // Gold sacrifice to gain favor
            int sacrifice = random.Next(500, Math.Min(5000, (int)npc.Gold));
            npc.SpendGold(sacrifice);

            // Boost chivalry slightly
            npc.Chivalry += sacrifice / 500;

            // GD.Print($"[WorldSim] {npc.Name} sacrificed {sacrifice} gold at the Temple");

            // Occasional news for large sacrifices
            if (sacrifice >= 2000 && random.NextDouble() < 0.3)
            {
                NewsSystem.Instance.Newsy(false, $"{npc.Name} made a generous offering at the Temple.");
            }
        }
        else if (roll < 0.75f && personality != null && personality.Aggression > 0.6f && npc.Darkness > npc.Chivalry)
        {
            // Evil NPCs might desecrate an altar
            // GD.Print($"[WorldSim] {npc.Name} desecrates an altar at the Temple!");

            // Gain darkness, risk divine retribution
            npc.Darkness += random.Next(10, 25);

            // 30% chance of divine punishment
            if (random.NextDouble() < 0.3)
            {
                int damage = random.Next(20, 50 + npc.Level);
                npc.HP = Math.Max(1, npc.HP - damage);
                NewsSystem.Instance.Newsy(true, $"{npc.Name} was struck by divine wrath for desecrating an altar!");
                // GD.Print($"[WorldSim] {npc.Name} was struck by divine wrath!");
            }
            else
            {
                NewsSystem.Instance.Newsy(true, $"{npc.Name} desecrated an altar at the Temple!");
            }
        }
        else
        {
            // Meditation and contemplation - slight HP recovery
            npc.HP = Math.Min(npc.HP + npc.MaxHP / 10, npc.MaxHP);
            // GD.Print($"[WorldSim] {npc.Name} meditates peacefully at the Temple");

            // Meet other NPCs at temple for relationship building
            var otherNPCs = npcs
                .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Temple")
                .ToList();

            if (otherNPCs.Any() && random.NextDouble() < 0.3)
            {
                var other = otherNPCs[random.Next(otherNPCs.Count)];
                // Spiritual bond - improve relationship
                RelationshipSystem.UpdateRelationship(npc, other, 1, 1, false);
                // GD.Print($"[WorldSim] {npc.Name} and {other.Name} share a moment of spiritual connection");
            }
        }
    }

    /// <summary>
    /// NPC visits the Bank to deposit, withdraw, or apply for guard duty
    /// </summary>
    private void NPCVisitBank(NPC npc)
    {
        npc.UpdateLocation("Bank");
        // GD.Print($"[WorldSim] {npc.Name} visits the Bank");

        var personality = npc.Brain?.Personality;
        float roll = (float)random.NextDouble();

        // Decide what to do at the bank
        if (npc.Gold > 1000 && roll < 0.5f)
        {
            // Deposit gold (deposit 50-80% of gold on hand)
            double depositPercent = 0.5 + (random.NextDouble() * 0.3);
            long depositAmount = (long)(npc.Gold * depositPercent);

            npc.SpendGold(depositAmount);
            npc.BankGold += depositAmount;

            // GD.Print($"[WorldSim] {npc.Name} deposited {depositAmount} gold at the Bank");

            // Occasional news for large deposits
            if (depositAmount >= 10000 && random.NextDouble() < 0.2)
            {
                NewsSystem.Instance.Newsy(false, $"{npc.Name} made a substantial deposit at the Ironvault Bank.");
            }
        }
        else if (npc.BankGold > 0 && npc.Gold < 100 && roll < 0.7f)
        {
            // Withdraw gold when low on cash
            long withdrawAmount = Math.Min(npc.BankGold, 500 + (npc.Level * 50));
            npc.BankGold -= withdrawAmount;
            npc.GainGold(withdrawAmount);

            // GD.Print($"[WorldSim] {npc.Name} withdrew {withdrawAmount} gold from the Bank");
        }
        else if (!npc.BankGuard && npc.Level >= 5 && npc.Darkness <= 100 && roll < 0.85f)
        {
            // Apply for guard duty (if eligible)
            if (random.NextDouble() < 0.3) // 30% chance to actually apply
            {
                npc.BankGuard = true;
                npc.BankWage = 1000 + (npc.Level * 50);

                NewsSystem.Instance.Newsy(true, $"{npc.Name} has been hired as a guard at the Ironvault Bank!");
                // GD.Print($"[WorldSim] {npc.Name} became a bank guard (wage: {npc.BankWage}/day)");
            }
        }
        else if (npc.BankGuard && random.NextDouble() < 0.05f)
        {
            // Small chance to resign from guard duty
            npc.BankGuard = false;
            npc.BankWage = 0;
            // GD.Print($"[WorldSim] {npc.Name} resigned from bank guard duty");
        }
        else
        {
            // Just checking account or socializing at the bank
            // GD.Print($"[WorldSim] {npc.Name} checked their account at the Bank");

            // Might meet other NPCs at the bank
            var otherNPCs = npcs
                .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Bank")
                .ToList();

            if (otherNPCs.Any() && random.NextDouble() < 0.2)
            {
                var other = otherNPCs[random.Next(otherNPCs.Count)];
                // Business acquaintance - small relationship boost
                RelationshipSystem.UpdateRelationship(npc, other, 1, 0, false);
                // GD.Print($"[WorldSim] {npc.Name} and {other.Name} chatted at the Bank");
            }
        }
    }

    /// <summary>
    /// NPC visits the Marketplace to list items or browse/buy
    /// </summary>
    private void NPCVisitMarketplace(NPC npc)
    {
        npc.UpdateLocation("Market");
        // GD.Print($"[WorldSim] {npc.Name} visits the Marketplace");

        // 50% chance to list an item if has inventory
        if (npc.MarketInventory.Count > 0 && random.NextDouble() < 0.5)
        {
            var item = npc.MarketInventory[random.Next(npc.MarketInventory.Count)];
            MarketplaceSystem.Instance.NPCListItem(npc, item);
            npc.MarketInventory.Remove(item);
        }

        // 50% chance to browse and potentially buy
        if (npc.Gold > 500 && random.NextDouble() < 0.5)
        {
            MarketplaceSystem.Instance.NPCBrowseAndBuy(npc);
        }

        // Meet other NPCs at marketplace for relationship building
        var otherNPCs = npcs
            .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Market")
            .ToList();

        if (otherNPCs.Any() && random.NextDouble() < 0.2)
        {
            var other = otherNPCs[random.Next(otherNPCs.Count)];
            RelationshipSystem.UpdateRelationship(npc, other, 1, 0, false);
            // GD.Print($"[WorldSim] {npc.Name} and {other.Name} haggled together at the Marketplace");
        }
    }

    /// <summary>
    /// NPC visits the Castle to apply for royal guard or seek audience with the king
    /// </summary>
    private void NPCVisitCastle(NPC npc)
    {
        npc.UpdateLocation("Castle");

        // Get current king
        var king = CastleLocation.GetCurrentKing();
        if (king == null || !king.IsActive)
        {
            // No king - just wander the castle grounds
            return;
        }

        // Check if NPC is already a royal guard
        if (king.Guards.Any(g => g.Name == npc.Name))
        {
            // Already a guard - just doing their duty
            return;
        }

        // Check if NPC is already a bank guard (can't serve both)
        if (npc.BankGuard)
        {
            return;
        }

        // Check eligibility: Level 5+, more chivalry than darkness, not on a team
        if (npc.Level >= 5 &&
            npc.Chivalry > npc.Darkness &&
            string.IsNullOrEmpty(npc.Team) &&
            king.Guards.Count < GameConfig.MaxRoyalGuards)
        {
            // Chance to apply for royal guard based on personality and stats
            float applyChance = 0.15f; // Base 15% chance

            // Higher chivalry = more likely to want to serve
            applyChance += Math.Min(0.20f, npc.Chivalry / 500f);

            // Higher level = more confident to apply
            if (npc.Level >= 10) applyChance += 0.10f;
            if (npc.Level >= 20) applyChance += 0.10f;

            // Personality factors
            if (npc.Brain?.Personality != null)
            {
                applyChance += npc.Brain.Personality.Aggression * 0.05f; // Warriors like guard duty
                applyChance -= npc.Brain.Personality.Greed * 0.10f; // Greedy prefer bank guard (better pay)
            }

            if (random.NextDouble() < applyChance)
            {
                // NPC applies and is accepted!
                long salary = GameConfig.BaseGuardSalary + (npc.Level * 20);

                var guard = new RoyalGuard
                {
                    Name = npc.Name,
                    AI = CharacterAI.Computer,
                    Sex = npc.Sex,
                    DailySalary = salary,
                    RecruitmentDate = DateTime.Now,
                    Loyalty = 80 + random.Next(21), // 80-100 loyalty
                    IsActive = true
                };

                king.Guards.Add(guard);

                // News announcement
                NewsSystem.Instance?.Newsy(true, $"{npc.Name} has joined the Royal Guard!");

                // Chivalry boost for service
                npc.Chivalry += 5;
            }
        }
        else if (npc.Level >= 3 && random.NextDouble() < 0.10)
        {
            // Lower level NPCs might just seek audience or donate
            // Small chance to donate to royal purse
            if (npc.Gold > 500 && npc.Chivalry > 50)
            {
                long donation = Math.Min(npc.Gold / 10, 200 + npc.Level * 10);
                npc.SpendGold(donation);
                king.Treasury += donation;
                npc.Chivalry += (int)Math.Min(5, donation / 50);
            }
        }

        // Meet other NPCs at the castle
        var otherNPCs = npcs
            .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Castle")
            .ToList();

        if (otherNPCs.Any() && random.NextDouble() < 0.15)
        {
            var other = otherNPCs[random.Next(otherNPCs.Count)];
            RelationshipSystem.UpdateRelationship(npc, other, 1, 0, false);
        }
    }

    /// <summary>
    /// NPC visits the Dark Alley for shady dealings
    /// </summary>
    private void NPCVisitDarkAlley(NPC npc)
    {
        npc.UpdateLocation("Dark Alley");

        // Pickpocket attempt - high greed + high dex NPCs may steal
        if (npc.Brain?.Personality != null && npc.Brain.Personality.Greed > 0.6f && random.NextDouble() < 0.15)
        {
            var victims = npcs
                .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Dark Alley" && n.Gold > 100)
                .ToList();

            if (victims.Any())
            {
                var victim = victims[random.Next(victims.Count)];
                long stolen = Math.Min(victim.Gold / 10, 50 + npc.Level * 5);
                if (stolen > 0)
                {
                    victim.SpendGold(stolen);
                    npc.Gold += stolen;
                    npc.Darkness += 2;
                    NewsSystem.Instance?.Newsy(false, $"{npc.Name} pickpocketed {stolen} gold from {victim.Name} in the Dark Alley!");
                    RelationshipSystem.UpdateRelationship(victim, npc, -5, 0, false);
                }
            }
        }

        // Fence stolen goods - sell inventory items at shady prices
        if (npc.MarketInventory.Count > 0 && random.NextDouble() < 0.20)
        {
            var item = npc.MarketInventory[random.Next(npc.MarketInventory.Count)];
            long fencePrice = Math.Max(10, item.Value / 3); // Fence pays 33%
            npc.Gold += fencePrice;
            npc.MarketInventory.Remove(item);
            npc.Darkness += 1;
        }

        // Meet other shady NPCs
        var otherNPCs = npcs
            .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Dark Alley")
            .ToList();

        if (otherNPCs.Any() && random.NextDouble() < 0.20)
        {
            var other = otherNPCs[random.Next(otherNPCs.Count)];
            // Shady companionship - small friendship boost
            RelationshipSystem.UpdateRelationship(npc, other, 1, 0, false);
        }
    }

    /// <summary>
    /// NPC visits the Inn for rest, drinks, and socializing
    /// </summary>
    private void NPCVisitInn(NPC npc)
    {
        npc.UpdateLocation("Inn");

        // Heal slightly from resting at the inn
        if (npc.HP < npc.MaxHP)
        {
            int healing = (int)Math.Max(5, npc.MaxHP / 20);
            npc.HP = Math.Min(npc.HP + healing, npc.MaxHP);
        }

        // Pay for drinks
        if (npc.Gold > 20)
        {
            long drinkCost = 5 + random.Next(15);
            npc.SpendGold(drinkCost);
        }

        // Socialize - meet other NPCs at the inn
        var otherNPCs = npcs
            .Where(n => n.IsAlive && n.ID != npc.ID && n.CurrentLocation == "Inn")
            .ToList();

        if (otherNPCs.Any() && random.NextDouble() < 0.25)
        {
            var other = otherNPCs[random.Next(otherNPCs.Count)];
            RelationshipSystem.UpdateRelationship(npc, other, 2, 0, false);

            // Gossip at the inn (small chance)
            if (random.NextDouble() < 0.10)
            {
                NewsSystem.Instance?.Newsy(false, $"{npc.Name} and {other.Name} shared drinks and gossip at the Inn.");
            }
        }
    }

    private void ExecuteTrade(NPC npc, string targetId, WorldState world)
    {
        if (string.IsNullOrEmpty(targetId)) return;
        
        var target = world.GetNPCById(targetId);
        if (target == null || target.CurrentLocation != npc.CurrentLocation) return;
        
        // Simple trade simulation
        var tradeAmount = GD.RandRange(10, 100);
        if (npc.CanAfford(tradeAmount) && target.CanAfford(tradeAmount))
        {
            // Exchange some gold (simplified)
            npc.SpendGold(tradeAmount / 2);
            target.GainGold(tradeAmount / 2);
            
            // Record the interaction
            npc.Brain?.RecordInteraction(target, InteractionType.Traded);
            target.Brain?.RecordInteraction(npc, InteractionType.Traded);
            
            // GD.Print($"[WorldSim] {npc.Name} traded with {target.Name}");
        }
    }
    
    private void ExecuteSocialize(NPC npc, string targetId, WorldState world)
    {
        if (string.IsNullOrEmpty(targetId)) return;
        
        var target = world.GetNPCById(targetId);
        if (target == null || target.CurrentLocation != npc.CurrentLocation) return;
        
        // Check compatibility for relationship building
        var compatibility = npc.Brain.Personality.GetCompatibility(target.Brain?.Personality);
        
        if (compatibility > 0.6f)
        {
            // Positive interaction
            npc.Brain?.RecordInteraction(target, InteractionType.Complimented);
            target.Brain?.RecordInteraction(npc, InteractionType.Complimented);
            
            // Chance to become friends or allies
            if (GD.Randf() < compatibility * 0.5f)
            {
                npc.AddRelationship(target.Id, 0);
                target.AddRelationship(npc.Id, 0);
                // GD.Print($"[WorldSim] {npc.Name} and {target.Name} became friends");
            }
        }
        else if (compatibility < 0.3f)
        {
            // Negative interaction
            npc.Brain?.RecordInteraction(target, InteractionType.Insulted);
            target.Brain?.RecordInteraction(npc, InteractionType.Insulted);
            
            // GD.Print($"[WorldSim] {npc.Name} had a negative interaction with {target.Name}");
        }
    }
    
    private void ExecuteAttack(NPC npc, string targetId, WorldState world)
    {
        if (string.IsNullOrEmpty(targetId)) return;

        var target = world.GetNPCById(targetId);
        if (target == null || target.CurrentLocation != npc.CurrentLocation || !target.IsAlive) return;

        // Multi-round combat simulation (like a real fight, not a single punch)
        int rounds = 0;
        const int maxRounds = 30;
        long totalDamageToTarget = 0;
        long totalDamageToAttacker = 0;

        while (npc.IsAlive && target.IsAlive && rounds < maxRounds)
        {
            rounds++;

            // Attacker strikes
            long attackDamage = Math.Max(1, npc.Strength + npc.WeapPow - target.Defence);
            attackDamage += random.Next(1, (int)Math.Max(2, npc.WeapPow / 3));
            target.TakeDamage(attackDamage);
            totalDamageToTarget += attackDamage;

            if (!target.IsAlive) break;

            // Defender strikes back
            long defenderDamage = Math.Max(1, target.Strength + target.WeapPow - npc.Defence);
            defenderDamage += random.Next(1, (int)Math.Max(2, target.WeapPow / 3));
            npc.TakeDamage(defenderDamage);
            totalDamageToAttacker += defenderDamage;
        }

        // Record the attack
        npc.Brain?.RecordInteraction(target, InteractionType.Attacked);
        target.Brain?.RecordInteraction(npc, InteractionType.Attacked);

        // Update relationships - make them enemies
        npc.AddRelationship(target.Id, 0);
        target.AddRelationship(npc.Id, 0);

        if (!target.IsAlive)
        {
            target.IsDead = true;
            target.SetState(NPCState.Dead);
            QueueNPCForRespawn(target.Name);
            npc.Brain?.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.SawDeath,
                InvolvedCharacter = target.Id,
                Description = $"Killed {target.Name} in combat",
                Importance = 0.9f,
                Location = npc.CurrentLocation
            });

            // Generate news about the killing
            NewsSystem.Instance.WriteDeathNews(target.Name, npc.Name, npc.CurrentLocation ?? "unknown");

            // Victor gains gold from the fallen
            long stolenGold = Math.Max(0, target.Gold / 4);
            if (stolenGold > 0)
            {
                npc.GainGold(stolenGold);
                target.Gold -= stolenGold;
            }

            GD.Print($"[WorldSim] {npc.Name} killed {target.Name} in combat ({rounds} rounds)!");
        }
        else if (!npc.IsAlive)
        {
            // Attacker died instead!
            npc.IsDead = true;
            npc.SetState(NPCState.Dead);
            QueueNPCForRespawn(npc.Name);
            target.Brain?.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.SawDeath,
                InvolvedCharacter = npc.Id,
                Description = $"Killed {npc.Name} in self-defense",
                Importance = 0.9f,
                Location = target.CurrentLocation
            });

            NewsSystem.Instance.WriteDeathNews(npc.Name, target.Name, target.CurrentLocation ?? "unknown");
            GD.Print($"[WorldSim] {npc.Name} was killed by {target.Name} in combat ({rounds} rounds)!");
        }
    }
    
    private void ExecuteRest(NPC npc)
    {
        var healAmount = npc.MaxHP / 4;
        npc.Heal(healAmount);
        npc.ChangeActivity(Activity.Resting, "Recovering health");
        
        // GD.Print($"[WorldSim] {npc.Name} rested and healed {healAmount} HP");
    }
    
    private void ExecuteTrain(NPC npc)
    {
        // Small chance to gain experience from training
        if (GD.Randf() < 0.3f)
        {
            var expGain = (long)(GD.RandRange(10, 30) * NpcXpMultiplier);
            npc.GainExperience(expGain);
            // GD.Print($"[WorldSim] {npc.Name} trained and gained {expGain} experience");
        }
        
        npc.ChangeActivity(Activity.Working, "Training and improving skills");
    }
    
    private void ExecuteJoinGang(NPC npc, string targetId, WorldState world)
    {
        if (string.IsNullOrEmpty(targetId) || npc.GangId != null) return;
        
        var gangLeader = world.GetNPCById(targetId);
        if (gangLeader == null || gangLeader.CurrentLocation != npc.CurrentLocation) return;
        
        // Check if gang leader accepts the NPC
        var compatibility = npc.Brain.Personality.GetCompatibility(gangLeader.Brain?.Personality);
        
        if (compatibility > 0.5f && gangLeader.GangMembers.Count < 6) // Max gang size
        {
            npc.GangId = gangLeader.Id;
            gangLeader.GangMembers.Add(npc.Id);
            
            npc.AddRelationship(gangLeader.Id, 0);
            gangLeader.AddRelationship(npc.Id, 0);
            
            npc.Brain?.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.JoinedGang,
                InvolvedCharacter = gangLeader.Id,
                Description = $"Joined {gangLeader.Name}'s gang",
                Importance = 0.8f,
                Location = npc.CurrentLocation
            });
            
            // GD.Print($"[WorldSim] {npc.Name} joined {gangLeader.Name}'s gang");
        }
    }
    
    private void ExecuteSeekRevenge(NPC npc, string targetId, WorldState world)
    {
        if (string.IsNullOrEmpty(targetId)) return;
        
        var target = world.GetNPCById(targetId);
        if (target != null && target.CurrentLocation == npc.CurrentLocation)
        {
            // If target is found, attack them
            ExecuteAttack(npc, targetId, world);
        }
        else
        {
            // Move around looking for the target
            MoveNPCToRandomLocation(npc);
            npc.ChangeActivity(Activity.Hunting, $"Seeking revenge against {target?.Name ?? "enemy"}");
        }
    }
    
    /// <summary>
    /// When an NPC has a strong dominant emotion, it affects nearby NPCs at the same location.
    /// Anger spreads fear, fear spreads panic, joy is infectious, sadness resonates with the empathetic.
    /// Rate-limited to once per 5 ticks per NPC to prevent runaway cascades.
    /// </summary>
    private void ProcessEmotionalCascades(NPC npc)
    {
        // Rate limit: only cascade every 5 ticks per NPC
        string npcId = npc.Id ?? npc.Name;
        if (_lastCascadeTick.TryGetValue(npcId, out int lastTick) && (_currentTick - lastTick) < 5)
            return;

        var dominant = npc.EmotionalState.GetDominantEmotion();
        if (dominant == null) return;

        float intensity = npc.EmotionalState.GetEmotionIntensity(dominant.Value);
        if (intensity < 0.7f) return; // Only strong emotions cascade

        // Find other NPCs at the same location
        var nearbyNPCs = npcs.Where(n =>
            n != npc && n.IsAlive && !n.IsDead &&
            n.EmotionalState != null &&
            n.CurrentLocation == npc.CurrentLocation).ToList();

        if (nearbyNPCs.Count == 0) return;

        _lastCascadeTick[npcId] = _currentTick;
        string npcName = npc.Name2 ?? npc.Name;

        foreach (var nearby in nearbyNPCs)
        {
            switch (dominant.Value)
            {
                case EmotionType.Anger:
                    // Aggressive NPCs catch anger, others become fearful
                    if (nearby.Brain?.Personality?.Aggression > 0.5f)
                        nearby.EmotionalState.AddEmotion(EmotionType.Anger, 0.1f, 60);
                    else
                        nearby.EmotionalState.AddEmotion(EmotionType.Fear, 0.2f, 90);
                    break;

                case EmotionType.Fear:
                    // Panic spreads
                    nearby.EmotionalState.AddEmotion(EmotionType.Fear, 0.15f, 60);
                    break;

                case EmotionType.Joy:
                    // Infectious happiness
                    nearby.EmotionalState.AddEmotion(EmotionType.Joy, 0.1f, 120);
                    break;

                case EmotionType.Sadness:
                    // Empathetic NPCs share sadness
                    if (nearby.Brain?.Personality != null &&
                        (nearby.Brain.Personality.Mysticism > 0.5f || nearby.Brain.Personality.Sociability > 0.6f))
                    {
                        nearby.EmotionalState.AddEmotion(EmotionType.Sadness, 0.1f, 90);
                    }
                    break;
            }
        }

        // Generate gossip for particularly dramatic cascades
        if (nearbyNPCs.Count >= 3)
        {
            string emotionWord = dominant.Value switch
            {
                EmotionType.Anger => "rage",
                EmotionType.Fear => "panic",
                EmotionType.Joy => "celebration",
                EmotionType.Sadness => "grief",
                _ => null
            };
            if (emotionWord != null)
                AddGossip($"A wave of {emotionWord} swept through the {npc.CurrentLocation} — started by {npcName}");
        }
    }

    /// <summary>
    /// Sociable NPCs spread gossip from the pool to the news feed.
    /// </summary>
    private void ProcessGossip()
    {
        if (_gossipPool.Count == 0) return;
        if (random.NextDouble() > 0.10) return; // 10% chance per tick

        // Find a sociable NPC at a social location
        var socialLocations = new[] { "Inn", "Love Street", "Main Street", "Market", "Temple" };
        var gossiper = npcs
            .Where(n => n.IsAlive && !n.IsDead &&
                        n.Brain?.Personality?.Sociability > 0.4f &&
                        socialLocations.Contains(n.CurrentLocation))
            .OrderBy(_ => random.Next())
            .FirstOrDefault();

        if (gossiper == null) return;

        // Pick a random gossip to share
        var gossip = _gossipPool[random.Next(_gossipPool.Count)];
        string gossiperName = gossiper.Name2 ?? gossiper.Name;

        NewsSystem.Instance?.Newsy($"{gossiperName} is telling anyone who'll listen: \"{gossip.Text}\"");

        gossip.TimesShared++;
        if (gossip.TimesShared >= gossip.MaxShares)
            _gossipPool.Remove(gossip);
    }

    private void ProcessWorldEvents()
    {
        // Random world events that can affect NPCs
        if (GD.Randf() < 0.05f) // 5% chance per simulation step
        {
            GenerateRandomEvent();
        }
    }
    
    private void GenerateRandomEvent()
    {
        var events = new[]
        {
            "A merchant caravan arrives in town",
            "Strange noises are heard from the dungeon",
            "The king makes a royal decree",
            "A festival begins in the town square",
            "Bandits are spotted near the roads",
            "A mysterious stranger appears",
            "The weather turns stormy",
            "A new shop opens in the market"
        };
        
        var randomEvent = events[GD.RandRange(0, events.Length - 1)];
        // GD.Print($"[WorldSim] World Event: {randomEvent}");
        
        // Record event in all NPC memories with low importance
        foreach (var npc in npcs.Where(n => n.IsAlive && n.Brain != null))
        {
            npc.Brain.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.WitnessedEvent,
                Description = $"Witnessed: {randomEvent}",
                Importance = 0.2f,
                Location = npc.CurrentLocation
            });
        }
    }
    
    private void UpdateSocialDynamics()
    {
        // Check for gang betrayals
        CheckGangBetrayals();

        // Check for new gang formations
        CheckGangFormations();

        // Process rival relationships
        ProcessRivalries();

        // Process team dynamics
        ProcessTeamDynamics();

        // Process throne and city challenges
        ProcessChallenges();

        // Process prisoner activities
        ProcessPrisonerActivities();

        // Process royal court politics
        ProcessRoyalCourtPolitics();
    }

    /// <summary>
    /// Process royal court political activities - guard recruitment, court intrigue
    /// </summary>
    private void ProcessRoyalCourtPolitics()
    {
        try
        {
            var king = CastleLocation.GetCurrentKing();
            if (king == null || !king.IsActive) return;

            // NPC guard recruitment (10% chance per tick if there are openings)
            if (king.Guards.Count < King.MaxNPCGuards && GD.Randf() < 0.10f)
            {
                ProcessNPCGuardRecruitment(king);
            }

            // Court intrigue processing (5% chance per tick)
            if (GD.Randf() < 0.05f)
            {
                ProcessCourtIntrigue(king);
            }

            // Plot progression (all active plots advance)
            foreach (var plot in king.ActivePlots.ToList())
            {
                AdvancePlot(king, plot);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WorldSim] Error processing court politics: {ex.Message}");
        }
    }

    /// <summary>
    /// NPCs may apply to become royal guards if positions are available
    /// </summary>
    private void ProcessNPCGuardRecruitment(King king)
    {
        // Find NPCs who might want to become guards:
        // - Not already a guard
        // - Not in prison
        // - Not on a team (guards serve the crown, not teams)
        // - Not already the King
        // - Level 5+ (minimum competency)
        // - High loyalty/lawfulness personality
        var candidates = npcs?
            .Where(n => n.IsAlive &&
                   n.Level >= 5 &&
                   n.DaysInPrison <= 0 &&
                   string.IsNullOrEmpty(n.Team) &&
                   !n.King &&
                   !n.IsStoryNPC &&
                   !king.Guards.Any(g => g.Name == n.Name) &&
                   (n.Brain?.Personality?.Trustworthiness > 0.5f ||
                    n.Brain?.Personality?.Loyalty > 0.6f))
            .OrderByDescending(n => n.Level)
            .Take(3)
            .ToList();

        if (candidates == null || candidates.Count == 0) return;

        // Pick a random candidate
        var applicant = candidates[GD.RandRange(0, candidates.Count - 1)];

        // Check if treasury can afford the recruitment cost
        if (king.Treasury < GameConfig.GuardRecruitmentCost)
        {
            // GD.Print($"[WorldSim] {applicant.Name} wanted to join guards but treasury is low");
            return;
        }

        // Add the NPC as a guard
        var guard = new RoyalGuard
        {
            Name = applicant.Name,
            AI = CharacterAI.Computer,
            Sex = applicant.Sex,
            DailySalary = GameConfig.BaseGuardSalary,
            RecruitmentDate = DateTime.Now,
            Loyalty = 70 + GD.RandRange(0, 30)  // New recruits have 70-100 loyalty
        };

        king.Guards.Add(guard);
        king.Treasury -= GameConfig.GuardRecruitmentCost;

        NewsSystem.Instance?.Newsy(false, $"{applicant.Name} has joined the Royal Guard!");
        // GD.Print($"[WorldSim] {applicant.Name} recruited as Royal Guard");
    }

    /// <summary>
    /// Process court intrigue - unhappy court members may start plots
    /// </summary>
    private void ProcessCourtIntrigue(King king)
    {
        // Initialize court if empty
        if (king.CourtMembers.Count == 0)
        {
            InitializeCourtMembers(king);
        }

        // Check for new plots starting
        var unhappyMembers = king.CourtMembers
            .Where(c => c.LoyaltyToKing < 40 && !c.IsPlotting)
            .ToList();

        if (unhappyMembers.Count >= 2 && king.ActivePlots.Count < 3)
        {
            // Start a new plot
            var conspirators = unhappyMembers.Take(GD.RandRange(2, Math.Min(4, unhappyMembers.Count))).ToList();

            string plotType = GD.RandRange(0, 3) switch
            {
                0 => "Assassination",
                1 => "Coup",
                2 => "Scandal",
                _ => "Sabotage"
            };

            var plot = new CourtIntrigue
            {
                PlotType = plotType,
                Conspirators = conspirators.Select(c => c.Name).ToList(),
                Target = king.Name,
                Progress = 10 + GD.RandRange(0, 20),
                StartDate = DateTime.Now
            };

            king.ActivePlots.Add(plot);
            foreach (var conspirator in conspirators)
            {
                conspirator.IsPlotting = true;
            }

            // GD.Print($"[WorldSim] New {plotType} plot started by {string.Join(", ", plot.Conspirators)}");
        }
    }

    /// <summary>
    /// Initialize court members for a new king
    /// </summary>
    private void InitializeCourtMembers(King king)
    {
        // Create default court positions
        var roles = new[] { "Royal Advisor", "Court Steward", "Marshal", "Spymaster", "Treasurer" };
        var factions = Enum.GetValues<CourtFaction>().Where(f => f != CourtFaction.None).ToArray();

        foreach (var role in roles)
        {
            var member = new CourtMember
            {
                Name = GenerateCourtMemberName(),
                Role = role,
                Faction = factions[GD.RandRange(0, factions.Length - 1)],
                Influence = 40 + GD.RandRange(0, 40),
                LoyaltyToKing = 50 + GD.RandRange(0, 40),
                JoinedCourt = DateTime.Now
            };
            king.CourtMembers.Add(member);
        }
    }

    /// <summary>
    /// Generate a random court member name
    /// </summary>
    private string GenerateCourtMemberName()
    {
        var firstNames = new[] { "Lord", "Lady", "Sir", "Baron", "Countess", "Duke", "Duchess" };
        var lastNames = new[] { "Blackwood", "Ashford", "Ironside", "Goldstein", "Silverhart",
                                "Ravencroft", "Thornwood", "Nightingale", "Stormwind", "Darkhaven" };

        return $"{firstNames[GD.RandRange(0, firstNames.Length - 1)]} {lastNames[GD.RandRange(0, lastNames.Length - 1)]}";
    }

    /// <summary>
    /// Advance a plot toward completion
    /// </summary>
    private void AdvancePlot(King king, CourtIntrigue plot)
    {
        if (plot.IsDiscovered) return;

        // Plots advance 5-15% per tick
        plot.Progress += GD.RandRange(5, 15);

        // Chance of discovery (higher for larger conspiracies)
        float discoveryChance = 0.02f + (plot.Conspirators.Count * 0.01f);
        if (GD.Randf() < discoveryChance)
        {
            plot.IsDiscovered = true;
            plot.DiscoveredBy = "Royal Spymaster";

            // Conspirators go to prison
            foreach (var conspirator in plot.Conspirators)
            {
                var member = king.CourtMembers.FirstOrDefault(m => m.Name == conspirator);
                if (member != null)
                {
                    member.IsPlotting = false;
                    king.CourtMembers.Remove(member);
                }
            }

            NewsSystem.Instance?.Newsy(true,
                $"A {plot.PlotType.ToLower()} plot against {king.GetTitle()} {king.Name} was discovered!");

            king.ActivePlots.Remove(plot);
            return;
        }

        // Plot triggers at 100%
        if (plot.Progress >= 100)
        {
            ExecutePlot(king, plot);
        }
    }

    /// <summary>
    /// Execute a completed plot
    /// </summary>
    private void ExecutePlot(King king, CourtIntrigue plot)
    {
        switch (plot.PlotType)
        {
            case "Assassination":
                // King "survives" but is weakened
                king.Treasury /= 2;
                NewsSystem.Instance?.Newsy(true,
                    $"ASSASSINATION ATTEMPT! {king.GetTitle()} {king.Name} narrowly survived an assassination plot!");
                break;

            case "Coup":
                // Treasury stolen, guards desert
                king.Treasury = Math.Max(0, king.Treasury - 10000);
                var deserters = king.Guards.Where(g => g.Loyalty < 50).ToList();
                foreach (var guard in deserters)
                {
                    king.Guards.Remove(guard);
                }
                NewsSystem.Instance?.Newsy(true,
                    $"COUP ATTEMPT! {deserters.Count} guards joined the conspiracy against {king.GetTitle()} {king.Name}!");
                break;

            case "Scandal":
                // King's reputation damaged - harder to collect taxes
                king.TaxRate = Math.Max(0, king.TaxRate - 10);
                NewsSystem.Instance?.Newsy(true,
                    $"SCANDAL! Shocking revelations about {king.GetTitle()} {king.Name} rock the kingdom!");
                break;

            case "Sabotage":
                // Treasury damaged
                king.Treasury = Math.Max(0, king.Treasury - 5000);
                king.MagicBudget = Math.Max(0, king.MagicBudget - 2000);
                NewsSystem.Instance?.Newsy(true,
                    $"SABOTAGE! The royal treasury has been plundered!");
                break;
        }

        // Clear conspirators' plotting status
        foreach (var conspirator in plot.Conspirators)
        {
            var member = king.CourtMembers.FirstOrDefault(m => m.Name == conspirator);
            if (member != null)
            {
                member.IsPlotting = false;
            }
        }

        king.ActivePlots.Remove(plot);
    }

    /// <summary>
    /// Process throne and city control challenges
    /// </summary>
    private void ProcessChallenges()
    {
        try
        {
            ChallengeSystem.Instance.ProcessMaintenanceChallenges();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WorldSim] Error processing challenges: {ex.Message}");
        }
    }

    /// <summary>
    /// Process prisoner stat-building activities
    /// </summary>
    private void ProcessPrisonerActivities()
    {
        try
        {
            PrisonActivitySystem.Instance.ProcessAllPrisonerActivities();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[WorldSim] Error processing prisoner activities: {ex.Message}");
        }
    }

    /// <summary>
    /// Process team-related dynamics: betrayals, team wars, turf control
    /// </summary>
    private void ProcessTeamDynamics()
    {
        // Check for team betrayals (members leaving)
        CheckTeamBetrayals();

        // Check for team vs team conflicts
        CheckTeamWars();

        // Update turf control
        UpdateTurfControl();
    }

    /// <summary>
    /// Check for NPCs leaving their teams
    /// </summary>
    private void CheckTeamBetrayals()
    {
        var teamMembers = npcs.Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive).ToList();

        // Get the player's team name so we can protect player team members
        var player = GameEngine.Instance?.CurrentPlayer as Player;
        string? playerTeam = (!string.IsNullOrEmpty(player?.Team)) ? player.Team : null;

        // Dissolve any 1-member teams (cleanup for teams that lost members to death/departure)
        var soloTeams = teamMembers
            .GroupBy(n => n.Team, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToList();
        foreach (var soloGroup in soloTeams)
        {
            var solo = soloGroup.First();
            // Don't dissolve the player's team
            if (playerTeam != null && solo.Team.Equals(playerTeam, StringComparison.OrdinalIgnoreCase))
                continue;
            var oldTeam = solo.Team;
            solo.Team = "";
            solo.TeamPW = "";
            solo.CTurf = false;
            solo.TeamRec = 0;
            NewsSystem.Instance.Newsy(true, $"The team '{oldTeam}' has disbanded as {solo.DisplayName} went solo!");
        }

        // Re-fetch after cleanup
        teamMembers = npcs.Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive).ToList();

        foreach (var member in teamMembers)
        {
            // Never remove NPCs from the player's team via world simulation
            // Player must manually sack them from Team Corner
            if (playerTeam != null && member.Team.Equals(playerTeam, StringComparison.OrdinalIgnoreCase))
                continue;

            // Low loyalty or betrayal-prone personality
            bool likelyToLeave = member.Brain?.Personality?.IsLikelyToBetray() == true ||
                                 member.Loyalty < 30;

            if (likelyToLeave && random.NextDouble() < 0.01) // 1% chance per tick
            {
                string oldTeam = member.Team;

                // Leave the team
                member.Team = "";
                member.TeamPW = "";
                member.CTurf = false;
                member.TeamRec = 0;

                NewsSystem.Instance.Newsy(true, $"{member.Name} abandoned '{oldTeam}'!");
                // GD.Print($"[WorldSim] {member.Name} left team '{oldTeam}'");

                // Notify player if this was their teammate (shouldn't reach here for player teams
                // due to the continue above, but kept as safety net)
                if (player != null && playerTeam != null &&
                    playerTeam.Equals(oldTeam, StringComparison.OrdinalIgnoreCase))
                {
                    GameEngine.AddNotification($"{member.DisplayName} has abandoned your team!");
                }

                // Check if team is now empty or solo
                var remainingMembers = npcs.Count(n => n.Team == oldTeam && n.IsAlive);
                if (remainingMembers == 0)
                {
                    NewsSystem.Instance.Newsy(true, $"The team '{oldTeam}' has been disbanded!");
                }
                else if (remainingMembers == 1)
                {
                    // Dissolve single-member teams — can't be a team of one
                    var soloMember = npcs.FirstOrDefault(n => n.Team == oldTeam && n.IsAlive);
                    if (soloMember != null)
                    {
                        soloMember.Team = "";
                        soloMember.TeamPW = "";
                        soloMember.CTurf = false;
                        soloMember.TeamRec = 0;
                        NewsSystem.Instance.Newsy(true, $"The team '{oldTeam}' has disbanded as {soloMember.DisplayName} went solo!");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Check for team vs team conflicts
    /// </summary>
    private void CheckTeamWars()
    {
        // Get all active teams
        var teams = npcs
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive)
            .GroupBy(n => n.Team)
            .Where(g => g.Count() >= 2) // Only teams with 2+ members
            .ToList();

        if (teams.Count < 2) return;

        // Small chance for team war
        if (random.NextDouble() > 0.02) return; // 2% chance per tick

        // Pick two random teams at the same location
        var team1Group = teams[random.Next(teams.Count)];
        var team1Location = team1Group.First().CurrentLocation;

        var teamsAtSameLocation = teams
            .Where(t => t.Key != team1Group.Key && t.Any(n => n.CurrentLocation == team1Location))
            .ToList();

        if (!teamsAtSameLocation.Any()) return;

        var team2Group = teamsAtSameLocation[random.Next(teamsAtSameLocation.Count)];

        // Team war!
        var team1 = team1Group.Where(n => n.CurrentLocation == team1Location && n.IsAlive).ToList();
        var team2 = team2Group.Where(n => n.CurrentLocation == team1Location && n.IsAlive).ToList();

        if (team1.Count == 0 || team2.Count == 0) return;

        string team1Name = team1Group.Key;
        string team2Name = team2Group.Key;

        NewsSystem.Instance.Newsy(true, $"Team War! '{team1Name}' clashes with '{team2Name}' at {team1Location}!");
        // GD.Print($"[WorldSim] Team war between '{team1Name}' and '{team2Name}'");

        // Simulate team battle
        bool team1Won = SimulateTeamVsTeamCombat(team1, team2);

        if (team1Won)
        {
            NewsSystem.Instance.Newsy(true, $"'{team1Name}' emerged victorious against '{team2Name}'!");
        }
        else
        {
            NewsSystem.Instance.Newsy(true, $"'{team2Name}' emerged victorious against '{team1Name}'!");
        }
    }

    /// <summary>
    /// Simulate team vs team combat
    /// </summary>
    private bool SimulateTeamVsTeamCombat(List<NPC> team1, List<NPC> team2)
    {
        int rounds = 0;
        const int maxRounds = 30;

        while (team1.Any(m => m.IsAlive) && team2.Any(m => m.IsAlive) && rounds < maxRounds)
        {
            rounds++;

            // Team 1 attacks team 2
            foreach (var attacker in team1.Where(m => m.IsAlive))
            {
                var target = team2.Where(m => m.IsAlive).OrderBy(_ => random.Next()).FirstOrDefault();
                if (target == null) break;

                long damage = Math.Max(1, attacker.Strength + attacker.WeapPow - target.Defence - target.ArmPow);
                damage += random.Next(1, (int)Math.Max(2, attacker.WeapPow / 4));
                target.TakeDamage(damage);

                if (!target.IsAlive)
                {
                    target.IsDead = true;
                    QueueNPCForRespawn(target.Name);
                    NewsSystem.Instance.WriteDeathNews(target.Name, attacker.Name, target.CurrentLocation ?? "battle");
                }
            }

            // Team 2 attacks team 1
            foreach (var attacker in team2.Where(m => m.IsAlive))
            {
                var target = team1.Where(m => m.IsAlive).OrderBy(_ => random.Next()).FirstOrDefault();
                if (target == null) break;

                long damage = Math.Max(1, attacker.Strength + attacker.WeapPow - target.Defence - target.ArmPow);
                damage += random.Next(1, (int)Math.Max(2, attacker.WeapPow / 4));
                target.TakeDamage(damage);

                if (!target.IsAlive)
                {
                    target.IsDead = true;
                    QueueNPCForRespawn(target.Name);
                    NewsSystem.Instance.WriteDeathNews(target.Name, attacker.Name, target.CurrentLocation ?? "battle");
                }
            }
        }

        // Determine winner by survivors
        int team1Alive = team1.Count(m => m.IsAlive);
        int team2Alive = team2.Count(m => m.IsAlive);

        if (team1Alive > team2Alive) return true;
        if (team2Alive > team1Alive) return false;

        // Tiebreaker: total remaining HP
        long team1HP = team1.Where(m => m.IsAlive).Sum(m => m.HP);
        long team2HP = team2.Where(m => m.IsAlive).Sum(m => m.HP);

        return team1HP >= team2HP;
    }

    /// <summary>
    /// Update turf control - strongest team can claim turf
    /// </summary>
    private void UpdateTurfControl()
    {
        // Get current turf controller (NPC-side)
        var currentController = npcs.FirstOrDefault(n => n.CTurf && !string.IsNullOrEmpty(n.Team));

        // Check if a player's team controls turf (set by WorldSimService from economy data).
        // The player may have a team with no NPC members — the world sim can't see the player
        // but should respect their city control until another team actively takes it.
        bool playerTeamControlsTurf = !string.IsNullOrEmpty(PlayerTurfTeam);

        // Get all NPC teams with their power
        var teams = npcs
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive)
            .GroupBy(n => n.Team)
            .Select(g => new
            {
                TeamName = g.Key,
                Power = g.Sum(m => m.Level + m.Strength + m.Defence),
                Members = g.ToList(),
                HasTurf = g.Any(m => m.CTurf)
            })
            .OrderByDescending(t => t.Power)
            .ToList();

        if (teams.Count == 0) return;

        var strongestTeam = teams.First();

        // If no one controls turf (and no player team holds it), small chance for strongest team to claim
        if (currentController == null && !playerTeamControlsTurf &&
            strongestTeam.Power > 100 && random.NextDouble() < 0.02)
        {
            foreach (var member in strongestTeam.Members)
            {
                member.CTurf = true;
                member.TeamRec = 0;
            }

            NewsSystem.Instance.Newsy(true, $"'{strongestTeam.TeamName}' has taken control of the town!");
            // GD.Print($"[WorldSim] Team '{strongestTeam.TeamName}' took control of turf");
        }
    }
    
    private void CheckGangBetrayals()
    {
        var gangMembers = npcs.Where(npc => !string.IsNullOrEmpty(npc.GangId)).ToList();
        
        foreach (var member in gangMembers)
        {
            if (member.Brain?.Personality?.IsLikelyToBetray() == true && GD.Randf() < 0.02f) // 2% chance
            {
                var gangLeader = npcs.FirstOrDefault(npc => npc.Id == member.GangId);
                if (gangLeader != null)
                {
                    // Betray the gang
                    member.GangId = null;
                    gangLeader.GangMembers.Remove(member.Id);
                    
                    member.AddRelationship(gangLeader.Id, 0);
                    gangLeader.AddRelationship(member.Id, 0);
                    
                    member.Brain?.RecordInteraction(gangLeader, InteractionType.Betrayed);
                    gangLeader.Brain?.RecordInteraction(member, InteractionType.Betrayed);
                    
                    // GD.Print($"[WorldSim] {member.Name} betrayed {gangLeader.Name}'s gang!");
                }
            }
        }
    }
    
    private void CheckGangFormations()
    {
        var potentialLeaders = npcs.Where(npc => 
            npc.IsAlive && 
            string.IsNullOrEmpty(npc.GangId) && 
            npc.GangMembers.Count == 0 &&
            npc.Brain?.Personality?.IsLikelyToJoinGang() == true &&
            npc.Brain.Personality.Ambition > 0.7f).ToList();
        
        foreach (var leader in potentialLeaders)
        {
            if (GD.Randf() < 0.01f) // 1% chance to form new gang
            {
                // Look for potential gang members in the same location
                var sameLocation = npcs.Where(npc => 
                    npc.CurrentLocation == leader.CurrentLocation &&
                    npc.Id != leader.Id &&
                    string.IsNullOrEmpty(npc.GangId) &&
                    npc.Brain?.Personality?.IsLikelyToJoinGang() == true).ToList();
                
                if (sameLocation.Count >= 2)
                {
                    var newMember = sameLocation[GD.RandRange(0, sameLocation.Count - 1)];
                    newMember.GangId = leader.Id;
                    leader.GangMembers.Add(newMember.Id);
                    
                    // GD.Print($"[WorldSim] {leader.Name} formed a new gang with {newMember.Name}");
                }
            }
        }
    }
    
    private void ProcessRivalries()
    {
        // Check for escalating conflicts between enemies
        var enemies = npcs.Where(npc => npc.IsAlive && !npc.IsDead && npc.Enemies.Count > 0).ToList();

        foreach (var npc in enemies)
        {
            foreach (var enemyId in npc.Enemies.ToList()) // ToList to avoid modification during iteration
            {
                var enemy = npcs.FirstOrDefault(n => n.Id == enemyId);
                if (enemy?.IsAlive == true && !enemy.IsDead && GD.Randf() < 0.12f) // 12% chance
                {
                    // Escalate the rivalry - only if at same location
                    if (npc.CurrentLocation == enemy.CurrentLocation)
                    {
                        // Pick conflict type based on instigator's personality
                        var personality = npc.Brain?.Personality;
                        var world = new WorldState(npcs);

                        if (personality != null && personality.Greed > 0.5f && personality.Intelligence > 0.5f)
                        {
                            ExecuteTheft(npc, enemy);
                        }
                        else if (personality != null && personality.Courage > 0.6f && personality.Aggression < 0.6f)
                        {
                            ExecuteChallenge(npc, enemy);
                        }
                        else
                        {
                            // Default: brawl (existing combat)
                            ExecuteAttack(npc, enemyId, world);
                            AddGossip($"{npc.Name2 ?? npc.Name} got into a brawl with {enemy.Name2 ?? enemy.Name} at the {npc.CurrentLocation}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Cunning/greedy NPC steals gold from a rival.
    /// </summary>
    private void ExecuteTheft(NPC thief, NPC victim)
    {
        if (victim.Gold <= 0) return;

        // Steal 5-15% of victim's gold
        long stolenAmount = Math.Max(1, (long)(victim.Gold * (0.05 + random.NextDouble() * 0.10)));
        victim.Gold -= stolenAmount;
        thief.Gold += stolenAmount;

        // Record memories
        thief.Brain?.Memory?.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.GainedGold,
            InvolvedCharacter = victim.Id,
            Description = $"Stole {stolenAmount} gold from {victim.Name2 ?? victim.Name}",
            Importance = 0.7f,
            Location = thief.CurrentLocation
        });
        victim.Brain?.Memory?.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.LostGold,
            InvolvedCharacter = thief.Id,
            Description = $"{thief.Name2 ?? thief.Name} stole {stolenAmount} gold from me",
            Importance = 0.8f,
            Location = victim.CurrentLocation
        });

        // Victim becomes angry
        victim.EmotionalState?.AddEmotion(EmotionType.Anger, 0.6f, 180);

        // Generate news
        string thiefName = thief.Name2 ?? thief.Name;
        string victimName = victim.Name2 ?? victim.Name;
        NewsSystem.Instance?.Newsy($"{thiefName} was caught pickpocketing {victimName} at the {thief.CurrentLocation}! {stolenAmount} gold went missing.");
        AddGossip($"{thiefName} stole from {victimName}");

        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLD", $"{thiefName} stole {stolenAmount}g from {victimName}");
    }

    /// <summary>
    /// Noble/proud NPC publicly challenges a rival. Winner gains confidence, loser loses face.
    /// </summary>
    private void ExecuteChallenge(NPC challenger, NPC target)
    {
        string challengerName = challenger.Name2 ?? challenger.Name;
        string targetName = target.Name2 ?? target.Name;

        // Compare overall combat power with some randomness
        long challengerPower = challenger.GetAttackPower() + challenger.Level + random.Next(1, 20);
        long targetPower = target.GetAttackPower() + target.Level + random.Next(1, 20);

        bool challengerWins = challengerPower > targetPower;
        var winner = challengerWins ? challenger : target;
        var loser = challengerWins ? target : challenger;
        string winnerName = winner.Name2 ?? winner.Name;
        string loserName = loser.Name2 ?? loser.Name;

        // Winner gains confidence, loser gains sadness
        winner.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.5f, 300);
        winner.EmotionalState?.AddEmotion(EmotionType.Pride, 0.4f, 240);
        loser.EmotionalState?.AddEmotion(EmotionType.Sadness, 0.4f, 180);

        // Record memories
        winner.Brain?.Memory?.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.PersonalAchievement,
            InvolvedCharacter = loser.Id,
            Description = $"Won a public challenge against {loserName}",
            Importance = 0.7f,
            Location = challenger.CurrentLocation
        });
        loser.Brain?.Memory?.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.PersonalFailure,
            InvolvedCharacter = winner.Id,
            Description = $"Lost a public challenge to {winnerName}",
            Importance = 0.6f,
            Location = challenger.CurrentLocation
        });

        // Generate news
        NewsSystem.Instance?.Newsy($"{challengerName} publicly challenged {targetName} at the {challenger.CurrentLocation}! {winnerName} emerged victorious.");
        AddGossip($"{winnerName} bested {loserName} in a public challenge");

        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("WORLD", $"Challenge: {challengerName} vs {targetName} - {winnerName} won");
    }
    
    public string GetSimulationStatus()
    {
        if (!isRunning) return "Simulation stopped";

        var aliveNPCs = npcs.Count(npc => npc.IsAlive);
        var gangs = npcs.Where(npc => npc.GangMembers.Count > 0).Count();
        var teams = npcs.Where(n => !string.IsNullOrEmpty(n.Team)).Select(n => n.Team).Distinct().Count();
        var turfController = npcs.FirstOrDefault(n => n.CTurf && !string.IsNullOrEmpty(n.Team))?.Team ?? "None";
        var totalRelationships = npcs.Sum(npc => npc.KnownCharacters.Count);

        return $"Active NPCs: {aliveNPCs}, Teams: {teams}, Turf: {turfController}, Gangs: {gangs}, Relationships: {totalRelationships}";
    }

    /// <summary>
    /// Get list of all active teams with their members
    /// </summary>
    public List<TeamInfo> GetActiveTeams()
    {
        if (npcs == null) return new List<TeamInfo>();

        return npcs
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive)
            .GroupBy(n => n.Team)
            .Select(g => new TeamInfo
            {
                TeamName = g.Key,
                MemberCount = g.Count(),
                TotalPower = g.Sum(m => m.Level + m.Strength + m.Defence),
                AverageLevel = (int)g.Average(m => m.Level),
                ControlsTurf = g.Any(m => m.CTurf),
                Members = g.Select(m => m.Name).ToList()
            })
            .OrderByDescending(t => t.TotalPower)
            .ToList();
    }

    /// <summary>
    /// Get teammates for a player's team
    /// </summary>
    public List<NPC> GetPlayerTeammates(string teamName)
    {
        if (npcs == null || string.IsNullOrEmpty(teamName))
            return new List<NPC>();

        return npcs.Where(n => n.Team == teamName && n.IsAlive).ToList();
    }
}

/// <summary>
/// Team information for display and queries
/// </summary>
public class TeamInfo
{
    public string TeamName { get; set; } = "";
    public int MemberCount { get; set; }
    public long TotalPower { get; set; }
    public int AverageLevel { get; set; }
    public bool ControlsTurf { get; set; }
    public List<string> Members { get; set; } = new();
} 
