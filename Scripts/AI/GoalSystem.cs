using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public partial class GoalSystem
{
    private List<Goal> goals = new List<Goal>();
    private PersonalityProfile personality;
    private const int MaxGoals = 30;

    // v0.64.0 Brain v2 Slice 12a: how often the LLM strategic-goal
    // generator runs per NPC. Reactive goals (heal/earn money/etc) keep
    // firing every tick from their concrete triggers; the LLM layer
    // refreshes long-arc trajectory periodically.
    private static readonly TimeSpan StrategicGoalRefreshInterval = TimeSpan.FromHours(6);

    // Shared stagger RNG for the post-restart "first refresh ever" path.
    // Without staggering, every NPC's LastLLMGoalRefreshUtc starts at
    // MinValue after a process restart (transient JsonIgnore field), so
    // the first eligibility tick would fire LLM calls for the entire
    // population simultaneously -- hundreds of HTTP requests, daily token
    // cap blown in a single burst, Anthropic rate-limit rejections.
    // Stagger spreads initial refreshes uniformly across the refresh
    // interval (~6 hours), so the population's daily LLM goal cost is
    // smooth instead of bursty.
    private static readonly Random _staggerRandom = new Random();

    // Public accessor for serialization
    public List<Goal> AllGoals => goals;

    public GoalSystem(PersonalityProfile profile)
    {
        personality = profile;
    }
    
    public void AddGoal(Goal goal)
    {
        // Skip if an active goal with the same name already exists
        if (goals.Any(g => g.Name == goal.Name && g.IsActive))
            return;

        // Hard cap: prune before adding
        if (goals.Count >= MaxGoals)
        {
            // Remove completed/inactive goals first
            goals.RemoveAll(g => g.IsCompleted || !g.IsActive);

            // If still over cap, remove lowest priority goals
            while (goals.Count >= MaxGoals)
            {
                var lowest = goals.OrderBy(g => g.GetEffectivePriority()).FirstOrDefault();
                if (lowest != null)
                    goals.Remove(lowest);
                else
                    break;
            }
        }

        goals.Add(goal);
    }
    
    public void RemoveGoal(string goalName)
    {
        goals.RemoveAll(g => g.Name == goalName);
    }
    
    public Goal? GetPriorityGoal()
    {
        return goals
            .Where(g => g.IsActive)
            .OrderByDescending(g => g.GetEffectivePriority())
            .FirstOrDefault();
    }
    
    public List<Goal> GetActiveGoals()
    {
        return goals.Where(g => g.IsActive).OrderByDescending(g => g.Priority).ToList();
    }
    
    public void UpdateGoals(NPC owner, WorldState world, MemorySystem memory, EmotionalState emotions)
    {
        // Prune completed/inactive goals to prevent unbounded list growth
        goals.RemoveAll(g => g.IsCompleted || !g.IsActive);

        // Decay goal priorities over time
        foreach (var goal in goals)
        {
            // Skip goals that are already completed or inactive
            if (goal.IsCompleted || !goal.IsActive) continue;

            goal.Priority *= 0.995f; // Slow decay

            // Check if goal should be completed or abandoned
            if (IsGoalCompleted(goal, owner, world))
            {
                goal.Complete();
                OnGoalCompleted(goal, owner, world);
            }
            else if (goal.Priority < 0.1f)
            {
                goal.IsActive = false;
                // GD.Print($"[Goals] {owner.Name} abandoned goal: {goal.Name}");
            }
        }
        
        // Add new goals based on current situation
        GenerateNewGoals(owner, world, memory, emotions);

        // v0.64.0 Brain v2 Slice 12a: opportunistic LLM strategic-goal
        // refresh. Cheap when LLM is unavailable (early-out in
        // TryRefreshStrategicGoals). Fire-and-forget when it does run --
        // any added goals land on the next tick via AddGoal's name dedup.
        TryRefreshStrategicGoals(owner);

        // Adjust priorities based on personality and emotions
        AdjustGoalPriorities(emotions);
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 12a: throttled LLM strategic-goal refresh.
    /// Gated on:
    ///   - LLM provider available (online + configured + has budget)
    ///   - Refresh interval has elapsed since last refresh for this NPC
    ///   - No refresh currently in flight for this NPC
    /// All checks are cheap. The actual LLM call runs on a background
    /// Task.Run so the calling tick doesn't block.
    ///
    /// Cohort note: Slice 12a initially gated on `npc.IsAIDriven` to match
    /// the rest of Brain v2 (cohort split between scorer-driven and legacy
    /// picker). But the cohort split was about picker comparability -- this
    /// layer is purely ADDITIVE (LLM goals augment the reactive goal stack
    /// alongside existing reactive triggers; both go into the same
    /// GetPriorityGoal lookup). All NPCs benefit from strategic goals
    /// regardless of which picker drives their verbs, and live audit
    /// surfaced that the cohort grew to zero across hundreds of NPCs,
    /// shutting this off in practice. Gate dropped.
    /// </summary>
    private void TryRefreshStrategicGoals(NPC owner)
    {
        if (owner == null) return;
        if (UsurperRemake.Systems.LLMProvider.Get() == null) return;
        if (owner.LLMGoalRefreshInFlight) return;

        var now = DateTime.UtcNow;

        // First-time stagger: initialize the per-NPC refresh anchor to a
        // uniformly random point within the past interval window. Effect:
        // (now - LastLLMGoalRefreshUtc) is uniformly distributed in
        // [0, interval], so the eligibility check below fires uniformly
        // across the next interval for the population instead of all at
        // once. Spreads ~244 NPCs over 6 hours = ~40 calls/hr at peak,
        // not 244 simultaneous calls. Skip kicking this tick; the next
        // tick checks the staggered anchor naturally.
        if (owner.LastLLMGoalRefreshUtc == DateTime.MinValue)
        {
            int minutesAgo = _staggerRandom.Next(0, (int)StrategicGoalRefreshInterval.TotalMinutes);
            owner.LastLLMGoalRefreshUtc = now - TimeSpan.FromMinutes(minutesAgo);
            return;
        }

        if (now - owner.LastLLMGoalRefreshUtc < StrategicGoalRefreshInterval)
            return;

        // Stamp the refresh time BEFORE kicking so a thundering herd of
        // concurrent ticks doesn't all fire LLM calls before the first
        // one returns. In-flight bool is the second guard for the rare
        // case where two ticks hit this line within the same microsecond.
        owner.LastLLMGoalRefreshUtc = now;
        owner.LLMGoalRefreshInFlight = true;

        _ = Task.Run(async () =>
        {
            try
            {
                var candidates = await UsurperRemake.Systems.LLMMoments.GenerateStrategicGoalsAsync(
                    owner, CancellationToken.None);

                if (candidates == null || candidates.Count == 0) return;

                foreach (var c in candidates)
                {
                    if (string.IsNullOrWhiteSpace(c.Name)) continue;
                    GoalType gt = ParseGoalTypeSafe(c.Type);
                    var g = new Goal(c.Name, gt, c.Priority);
                    if (!string.IsNullOrWhiteSpace(c.TargetCharacter))
                        g.TargetCharacter = c.TargetCharacter;
                    // Tag this goal as LLM-generated for future telemetry /
                    // potential per-cycle replacement. IsLLMGenerated is
                    // transient (JsonIgnore) so it resets on restart -- the
                    // goal itself persists via the normal Goal serialization.
                    g.IsLLMGenerated = true;
                    AddGoal(g); // name-dedup means re-running is idempotent
                }

                UsurperRemake.Systems.DebugLogger.Instance.LogInfo("GOALS",
                    $"LLM strategic refresh for {owner.Name2 ?? owner.Name}: added " +
                    $"{candidates.Count} candidate goal(s)");
            }
            catch (Exception ex)
            {
                UsurperRemake.Systems.DebugLogger.Instance.LogError("GOALS",
                    $"LLM strategic refresh failed for {owner.Name2 ?? owner.Name}: {ex.Message}");
            }
            finally
            {
                owner.LLMGoalRefreshInFlight = false;
            }
        });
    }

    /// <summary>
    /// Parse the LLM's free-form Type string into a GoalType enum, defaulting
    /// to Personal if the model emitted something unexpected. The system
    /// prompt restricts to {Personal, Social, Economic, Combat, Exploration};
    /// case-insensitive and tolerant of stray whitespace.
    /// </summary>
    private static GoalType ParseGoalTypeSafe(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return GoalType.Personal;
        return Enum.TryParse<GoalType>(s.Trim(), ignoreCase: true, out var t)
            ? t : GoalType.Personal;
    }
    
    private bool IsGoalCompleted(Goal goal, NPC owner, WorldState world)
    {
        // v0.64.0 Brain v2 Slice 3: extended completion detection. The Brain v2
        // scorer reads the top-priority goal each tick, so stale/unsatisfiable
        // goals must complete promptly otherwise the NPC keeps biasing toward
        // them after the underlying need is gone (NPC keeps shopping after their
        // gear is good, keeps pursuing a partner after they're married, etc.).
        return goal.Type switch
        {
            GoalType.Economic when goal.Name.Contains("Wealthy") => owner.Gold >= 10000,
            GoalType.Economic when goal.Name.Contains("Control") => owner.CTurf,
            GoalType.Economic when goal.Name.Contains("Earn Money") => owner.Gold >= 1000,
            GoalType.Social when goal.Name.Contains("Power") => owner.King,
            GoalType.Social when goal.Name.Contains("Ruler") => owner.King,
            GoalType.Personal when goal.Name.Contains("Strength") => owner.Level >= 20,
            GoalType.Personal when goal.Name.Contains("Elite Status") => owner.Level >= 30,
            GoalType.Social when goal.Name.Contains("Join") || goal.Name.Contains("Find Gang") => !string.IsNullOrEmpty(owner.GangId),
            // v0.64.0 Slice 3 additions:
            // Weapon / equipment goals satisfied when the gear-power baseline is met.
            GoalType.Economic when goal.Name.Contains("Weapon") || goal.Name.Contains("Equipment") => owner.BaseWeapPow >= owner.Level * 8,
            GoalType.Economic when goal.Name.Contains("Magic Item") => owner.BaseWeapPow >= owner.Level * 6,
            // Mana potion goal satisfied when stash is healthy.
            GoalType.Economic when goal.Name.Contains("Mana Potion") => owner.ManaPotions >= 5,
            // Health-recovery goals satisfied at near-full HP.
            GoalType.Personal when goal.Name.Contains("Heal") || goal.Name.Contains("Health") => owner.MaxHP > 0 && (double)owner.HP / owner.MaxHP >= 0.9,
            // Life-partner / make-friends goals satisfied by relationship state.
            GoalType.Social when goal.Name.Contains("Life Partner") || goal.Name.Contains("Partner") => owner.Married || owner.IsMarried,
            GoalType.Social when goal.Name.Contains("Friends") => owner.KnownCharacters?.Count >= 5,
            // Family revenge satisfied when the target is dead.
            GoalType.Combat when goal.Name.StartsWith("Avenge") && !string.IsNullOrEmpty(goal.TargetCharacter) => IsTargetCharacterDead(goal.TargetCharacter),
            GoalType.Social when goal.Name.StartsWith("Revenge") && !string.IsNullOrEmpty(goal.TargetCharacter) => IsTargetCharacterDead(goal.TargetCharacter),
            _ => false
        };
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 3: helper for revenge-goal completion. The target
    /// is dead if any active NPC matches the name and has IsDead=true, OR no
    /// matching NPC exists at all (target was a transient hostile, NPC long gone).
    /// </summary>
    private static bool IsTargetCharacterDead(string targetName)
    {
        if (string.IsNullOrEmpty(targetName)) return false;
        var pool = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (pool == null) return false;
        var match = pool.FirstOrDefault(n =>
            (n.Name2 ?? n.Name1 ?? "").Equals(targetName, StringComparison.OrdinalIgnoreCase));
        if (match == null) return true;  // no longer in the world -> consider settled
        return match.IsDead;
    }
    
    /// <summary>
    /// Generate visible consequences when an NPC achieves a goal - news events, emotional effects, gossip.
    /// </summary>
    private void OnGoalCompleted(Goal goal, NPC owner, WorldState world)
    {
        string npcName = owner.Name2 ?? owner.Name;

        if (goal.Name.Contains("Wealthy") || goal.Name.Contains("Earn Money"))
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Joy, 0.5f, 300);
            owner.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.3f, 240);
            if (owner.Gold >= 10000)
            {
                NewsSystem.Instance?.Newsy($"{npcName} has amassed a fortune and is now one of the wealthiest in town!");
                WorldSimulator.AddGossip($"{npcName} struck it rich");
            }
        }
        else if (goal.Name.Contains("Power") || goal.Name.Contains("Ruler"))
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.5f, 360);
            owner.EmotionalState?.AddEmotion(EmotionType.Pride, 0.5f, 360);
            // King news is already generated by CastleLocation, just add emotion + gossip
            WorldSimulator.AddGossip($"{npcName} has seized power");
        }
        else if (goal.Name.Contains("Strength") || goal.Name.Contains("Elite Status"))
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.5f, 300);
            // Level-up news is already generated elsewhere, just add emotion
        }
        else if (goal.Name.Contains("Control the City"))
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.5f, 300);
            owner.EmotionalState?.AddEmotion(EmotionType.Pride, 0.4f, 240);
            NewsSystem.Instance?.Newsy($"{npcName}'s team now controls the city and collects tax revenue from every sale!");
            WorldSimulator.AddGossip($"{npcName}'s gang took over the city");
        }
        else if (goal.Name.Contains("Join") || goal.Name.Contains("Find Gang"))
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Joy, 0.3f, 180);
            NewsSystem.Instance?.Newsy($"{npcName} has formed a powerful new alliance!");
            WorldSimulator.AddGossip($"{npcName} is gathering followers");
        }
        else if (goal.Name.Contains("Revenge") || goal.Name.StartsWith("Avenge"))
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.5f, 300);
            // Catharsis: anger subsides after revenge
            owner.EmotionalState?.ClearEmotion(EmotionType.Anger);
            string targetName = goal.TargetCharacter ?? "their enemy";
            // v0.64.0 Brain v2 Slice 3: family revenge gets weightier news / gossip.
            bool isFamilyVengeance = goal.Name.StartsWith("Avenge");
            if (isFamilyVengeance)
            {
                // v0.64.0 Brain v2 Slice 5: LLMMoments.PostAvengeNewsAsync handles
                // both the templated fallback (always works) AND the LLM-rendered
                // dramatic version (when configured + budget allows + online mode).
                // Fire-and-forget -- world-sim tick continues, news lands within
                // a few seconds. The previous inline NewsSystem.Newsy is now
                // inside PostAvengeNewsAsync's fallback path.
                _ = UsurperRemake.Systems.LLMMoments.PostAvengeNewsAsync(owner, targetName);
                WorldSimulator.AddGossip($"{npcName} took blood for blood from {targetName}");
            }
            else
            {
                NewsSystem.Instance?.Newsy($"{npcName} has finally settled the score with {targetName}.");
                WorldSimulator.AddGossip($"{npcName} got revenge on {targetName}");
            }
        }
        else if (goal.Name == "Mourn the Dead")
        {
            // v0.64.0 Slice 3: mourning runs its course. Sadness fades, hope returns.
            owner.EmotionalState?.AddEmotion(EmotionType.Peace, 0.4f, 240);
            owner.EmotionalState?.AddEmotion(EmotionType.Hope, 0.3f, 180);
            owner.EmotionalState?.ClearEmotion(EmotionType.Sadness);
        }
        else if (goal.Name == "Protect Family")
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Pride, 0.4f, 240);
            owner.EmotionalState?.AddEmotion(EmotionType.Joy, 0.3f, 180);
        }
        else if (goal.Name.Contains("Life Partner") || goal.Name.Contains("Partner"))
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Joy, 0.6f, 480);
            owner.EmotionalState?.AddEmotion(EmotionType.Peace, 0.4f, 360);
        }
        else if (goal.Name.Contains("Weapon") || goal.Name.Contains("Equipment"))
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Confidence, 0.4f, 240);
            owner.EmotionalState?.AddEmotion(EmotionType.Pride, 0.3f, 180);
        }
        else if (goal.Name.Contains("Friends"))
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Joy, 0.4f, 240);
            owner.EmotionalState?.ClearEmotion(EmotionType.Loneliness);
        }
        else if (goal.Name.Contains("Heal"))
        {
            owner.EmotionalState?.AddEmotion(EmotionType.Peace, 0.3f, 120);
        }

        owner.Brain?.Memory?.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.PersonalAchievement,
            Description = $"Achieved goal: {goal.Name}",
            Importance = 0.7f,
            Location = owner.CurrentLocation
        });

        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("GOALS", $"{npcName} completed goal: {goal.Name}");
    }

    private void GenerateNewGoals(NPC owner, WorldState world, MemorySystem memory, EmotionalState emotions)
    {
        // Generate revenge goals based on memories
        var attackMemories = memory.GetMemoriesOfType(MemoryType.Attacked)
            .Where(m => m.IsRecent(168)) // Within a week
            .ToList();

        foreach (var attackMemory in attackMemories.Take(2)) // Limit revenge goals
        {
            var revengeName = $"Revenge against {attackMemory.InvolvedCharacter}";
            if (!goals.Any(g => g.Name == revengeName))
            {
                var revengeGoal = new Goal(revengeName, GoalType.Social, personality.Vengefulness);
                revengeGoal.TargetCharacter = attackMemory.InvolvedCharacter;
                AddGoal(revengeGoal);
            }
        }

        // v0.64.0 Brain v2 Slice 3: family-event goal promotion. The v0.63.0
        // KilledMyParent / KilledMyFamily / LostFamilyMember / FamilyMemberBorn
        // memory types are written by WorldSimulator.RecordFamilyDeath /
        // RecordFamilyBirth into the affected NPCs' memory streams. Until
        // Slice 3, nothing read these except `FamilySystem.HasGrudgeAgainst`
        // for passive talk / recruit / attack-on-sight gates. The Brain v2
        // scorer reads `goals.GetPriorityGoal()` to bias action selection
        // toward verbs that advance the goal; family memories should produce
        // goals so an aggrieved NPC actually walks toward their kin's killer
        // instead of waiting for the killer to walk to them.

        // Family revenge -- killed parent / family member promotes a high-priority
        // Combat goal. Higher than ordinary Attacked-revenge because it's blood.
        // Capped to 2 active family-revenge goals so an NPC with multiple dead
        // relatives doesn't monopolize the goal slot.
        var familyKillMemories = memory.GetMemoriesOfType(MemoryType.KilledMyParent)
            .Concat(memory.GetMemoriesOfType(MemoryType.KilledMyFamily))
            .Where(m => m.IsRecent(720)) // within a month (family wounds run deeper)
            .Where(m => !string.IsNullOrEmpty(m.InvolvedCharacter))
            .OrderByDescending(m => m.Importance)
            .Take(2)
            .ToList();
        foreach (var killMem in familyKillMemories)
        {
            var name = $"Avenge {killMem.InvolvedCharacter}";
            if (!goals.Any(g => g.Name == name))
            {
                float pri = Math.Min(1.0f, personality.Vengefulness * 1.2f + 0.1f);
                var goal = new Goal(name, GoalType.Combat, pri)
                {
                    TargetCharacter = killMem.InvolvedCharacter,
                };
                AddGoal(goal);
            }
        }

        // Family birth -- new child / niece / nephew promotes a Social "Protect
        // Family" goal that lifts go_home / castle / settlement / inn weights via
        // the scorer's Social bucket. Uses Loyalty as the personality driver
        // (Loyal NPCs care more about kin).
        var birthMemories = memory.GetMemoriesOfType(MemoryType.FamilyMemberBorn)
            .Where(m => m.IsRecent(168))
            .ToList();
        if (birthMemories.Any() && !goals.Any(g => g.Name == "Protect Family"))
        {
            float pri = Math.Min(1.0f, personality.Loyalty * 0.8f + 0.2f);
            AddGoal(new Goal("Protect Family", GoalType.Social, pri));
        }

        // Family loss (non-killing) -- spouse / sibling / child died of old age
        // or NPC didn't witness the killer. Promotes a Personal "Mourn the Dead"
        // goal which biases toward temple (peaceful introspection) and inn
        // (gather with community). Decays naturally over ~3 days via the 0.995x
        // tick decay. Also injects Sadness so the emotional state matches.
        var lossMemories = memory.GetMemoriesOfType(MemoryType.LostFamilyMember)
            .Where(m => m.IsRecent(120)) // within 5 days
            .ToList();
        if (lossMemories.Any() && !goals.Any(g => g.Name == "Mourn the Dead"))
        {
            AddGoal(new Goal("Mourn the Dead", GoalType.Personal, 0.6f));
            owner.EmotionalState?.AddEmotion(EmotionType.Sadness, 0.6f, 4320); // 3 days
        }
        
        // Generate social goals based on loneliness
        if (personality.Sociability > 0.6f && owner.KnownCharacters.Count < 3)
        {
            if (!goals.Any(g => g.Name.Contains("Make Friends")))
            {
                AddGoal(new Goal("Make Friends", GoalType.Social, personality.Sociability * 0.8f));
            }
        }
        
        // Generate economic goals based on poverty
        if (owner.Gold < 100 && personality.Greed > 0.5f)
        {
            if (!goals.Any(g => g.Name.Contains("Earn Money")))
            {
                AddGoal(new Goal("Earn Money", GoalType.Economic, personality.Greed));
            }
        }
        
        // Generate survival goals based on health
        if (owner.CurrentHP < owner.MaxHP * 0.3f)
        {
            if (!goals.Any(g => g.Name.Contains("Heal")))
            {
                AddGoal(new Goal("Heal Wounds", GoalType.Personal, 0.9f));
            }
        }
        
        // Generate power goals for ambitious NPCs
        if (personality.Ambition > 0.8f && owner.Level >= 10)
        {
            if (!goals.Any(g => g.Name.Contains("Become Ruler")))
            {
                // Tax revenue makes the throne more attractive
                var king = CastleLocation.GetCurrentKing();
                float taxBonus = (king != null && king.KingTaxPercent > 10) ? 0.2f : 0f;
                float rulerPriority = Math.Min(1.0f, personality.Ambition + taxBonus);
                AddGoal(new Goal("Become Ruler", GoalType.Social, rulerPriority));
            }
        }

        // Generate city control goals for greedy NPCs with teams
        if (personality.Greed > 0.6f && !string.IsNullOrEmpty(owner.GangId) && !owner.CTurf)
        {
            if (!goals.Any(g => g.Name.Contains("Control the City")))
            {
                AddGoal(new Goal("Control the City", GoalType.Economic, personality.Greed * 0.7f));
            }
        }
    }
    
    private void AdjustGoalPriorities(EmotionalState emotions)
    {
        var activeEmotions = emotions.GetActiveEmotions();
        
        foreach (var goal in goals.Where(g => g.IsActive))
        {
            var emotionModifier = 1.0f;
            
            // Emotion-based goal priority adjustments
            foreach (var emotion in activeEmotions)
            {
                emotionModifier *= emotion.Key switch
                {
                    EmotionType.Anger when goal.Type == GoalType.Social && goal.Name.Contains("Revenge") => 1.5f,
                    EmotionType.Fear when goal.Type == GoalType.Personal => 1.3f,
                    EmotionType.Greed when goal.Type == GoalType.Economic => 1.4f,
                    EmotionType.Confidence when goal.Type == GoalType.Social && goal.Name.Contains("Power") => 1.2f,
                    EmotionType.Loneliness when goal.Type == GoalType.Social && goal.Name.Contains("Friends") => 1.6f,
                    _ => 1.0f
                };
            }
            
            goal.EmotionModifier = emotionModifier;
        }
    }
    
    public void OnLevelUp(int newLevel)
    {
        // Boost ambition-related goals on level up
        foreach (var goal in goals.Where(g => g.Type == GoalType.Personal || g.Type == GoalType.Social))
        {
            goal.Priority += 0.1f;
        }
        
        // Add new high-level goals
        if (newLevel >= 15 && personality.Ambition > 0.7f)
        {
            if (!goals.Any(g => g.Name.Contains("Elite Status")))
            {
                AddGoal(new Goal("Achieve Elite Status", GoalType.Social, 0.8f));
            }
        }
    }
    
    public string GetGoalsSummary()
    {
        var activeGoals = GetActiveGoals();
        if (!activeGoals.Any())
        {
            return "No active goals";
        }
        
        var summary = "Active Goals:\n";
        foreach (var goal in activeGoals.Take(3))
        {
            summary += $"  - {goal.Name} (Priority: {goal.GetEffectivePriority():F2})\n";
        }
        
        return summary.TrimEnd();
    }
}

public class Goal
{
    public string Name { get; set; }
    public GoalType Type { get; set; }
    public float Priority { get; set; }
    public float EmotionModifier { get; set; } = 1.0f;
    public bool IsActive { get; set; } = true;
    public bool IsCompleted { get; set; } = false;
    public DateTime CreatedTime { get; set; } = DateTime.Now;
    public string TargetCharacter { get; set; } = ""; // For revenge goals
    public string TargetLocation { get; set; } = ""; // For location-based goals
    public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

    // Progress tracking for serialization
    public float Progress { get; set; } = 0f;
    public float TargetValue { get; set; } = 1f;
    public float CurrentValue { get; set; } = 0f;

    // v0.64.0 Brain v2 Slice 12a: marker for LLM-generated strategic
    // goals. Transient (JsonIgnore) -- after a restart these revert to
    // false and become indistinguishable from reactive goals; they then
    // continue to live their normal lifecycle (complete / decay / get
    // pruned) and the next LLM refresh adds new strategic goals on top.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLLMGenerated { get; set; }
    
    public Goal(string name, GoalType type, float priority)
    {
        Name = name;
        Type = type;
        Priority = Math.Max(0.0f, Math.Min(1.0f, priority));
    }
    
    public float GetEffectivePriority()
    {
        var timeFactor = 1.0f;
        var age = DateTime.Now - CreatedTime;
        
        // Some goals become more urgent over time
        if (Type == GoalType.Personal)
        {
            timeFactor = 1.0f + (float)(age.TotalHours * 0.01f); // Gradually increase
        }
        
        return Priority * EmotionModifier * timeFactor;
    }
    
    public void Complete()
    {
        IsCompleted = true;
        IsActive = false;
    }
    
    public bool IsUrgent()
    {
        return GetEffectivePriority() > 0.8f;
    }
    
    public override string ToString()
    {
        var status = IsCompleted ? "[DONE]" : IsActive ? "[ACTIVE]" : "[INACTIVE]";
        return $"{status} {Name} ({Type}) - Priority: {GetEffectivePriority():F2}";
    }
}

public enum GoalType
{
    Personal,   // Self-improvement, survival, health
    Social,     // Relationships, reputation, power
    Economic,   // Wealth, trade, resources
    Combat,     // Fighting, revenge, dominance
    Exploration // Discovery, adventure, knowledge
} 
