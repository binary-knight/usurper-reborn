using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public class NPCBrain
{
    private NPC owner;
    private PersonalityProfile personality;
    private MemorySystem memory;
    private GoalSystem goals;
    private RelationshipManager relationships;
    private EmotionalState emotions;
    
    private DateTime lastDecisionTime = DateTime.Now;
    private const int DECISION_COOLDOWN_MINUTES = 15;
    
    // Phase 21: Enhanced NPC behaviors
    private DateTime lastShoppingAttempt = DateTime.MinValue;
    private DateTime lastInventoryCheck = DateTime.MinValue;
    private Dictionary<string, DateTime> lastActivities = new();
    private Random random = new Random();
    
    public NPC Owner => owner;
    public PersonalityProfile Personality => personality;
    public MemorySystem Memory => memory;
    public GoalSystem Goals => goals;
    public RelationshipManager Relationships => relationships;
    public EmotionalState Emotions => emotions;
    
    public NPCBrain(NPC npc, PersonalityProfile profile)
    {
        owner = npc;
        personality = profile;
        memory = new MemorySystem();
        goals = new GoalSystem(personality);
        relationships = new RelationshipManager();
        emotions = new EmotionalState();
        
        InitializeGoals();
        InitializeEnhancedBehaviors(); // Phase 21
        // GD.Print($"[AI] Created enhanced brain for {npc.Name} ({profile.Archetype})");
    }
    
    /// <summary>
    /// Initialize enhanced behaviors from Pascal NPC systems
    /// </summary>
    private void InitializeEnhancedBehaviors()
    {
        // Initialize shopping preferences based on class
        InitializeShoppingBehavior();
        
        // Initialize gang behavior tendencies
        InitializeGangBehavior();
        
        // Initialize believer system tendencies
        InitializeBelieverBehavior();
        
        // Initialize relationship goals
        InitializeRelationshipBehavior();
    }
    
    /// <summary>
    /// Pascal NPCMAINT.PAS shopping behavior initialization
    /// </summary>
    private void InitializeShoppingBehavior()
    {
        switch (owner.Class)
        {
            case CharacterClass.Warrior:
                goals.AddGoal(new Goal("Maintain Good Equipment", GoalType.Economic, 0.7f));
                goals.AddGoal(new Goal("Find Better Weapons", GoalType.Economic, 0.8f));
                break;
                
            case CharacterClass.Magician:
                goals.AddGoal(new Goal("Stock Mana Potions", GoalType.Economic, 0.6f));
                goals.AddGoal(new Goal("Find Magic Items", GoalType.Economic, 0.7f));
                break;
                
            case CharacterClass.Paladin:
                goals.AddGoal(new Goal("Maintain Health", GoalType.Personal, 0.8f));
                goals.AddGoal(new Goal("Help Others", GoalType.Social, 0.7f));
                break;
        }
    }
    
    /// <summary>
    /// Pascal NPCMAINT.PAS gang behavior initialization
    /// </summary>
    private void InitializeGangBehavior()
    {
        if (personality.IsLikelyToJoinGang())
        {
            if (string.IsNullOrEmpty(owner.Team) && string.IsNullOrEmpty(owner.GangId))
            {
                if (!goals.AllGoals.Any(g => g.Name.Contains("Gang") || g.Name.Contains("Join")))
                    goals.AddGoal(new Goal("Find Gang to Join", GoalType.Social, 0.6f));
            }
            else
            {
                goals.AddGoal(new Goal("Support Gang", GoalType.Social, 0.8f));
                goals.AddGoal(new Goal("Defend Territory", GoalType.Social, 0.7f));
            }
        }
    }
    
    /// <summary>
    /// Pascal NPCMAINT.PAS believer system initialization
    /// </summary>
    private void InitializeBelieverBehavior()
    {
        if (string.IsNullOrEmpty(owner.God))
        {
            // Potential for conversion based on personality
            if (personality.Sociability > 0.6f || random.NextDouble() < 0.1)
            {
                goals.AddGoal(new Goal("Seek Spiritual Meaning", GoalType.Personal, 0.4f));
            }
        }
        else
        {
            goals.AddGoal(new Goal($"Serve {owner.God}", GoalType.Social, 0.6f));
            goals.AddGoal(new Goal("Spread Faith", GoalType.Social, 0.5f));
        }
    }
    
    /// <summary>
    /// Pascal RELATIO2.PAS relationship behavior initialization
    /// </summary>
    private void InitializeRelationshipBehavior()
    {
        if (owner.Married || owner.Level < 5)
        {
            return;
        }
        
        goals.AddGoal(new Goal("Find Life Partner", GoalType.Social, 0.5f));
        
        if (personality.Sociability > 0.6f)
        {
            goals.AddGoal(new Goal("Make Friends", GoalType.Social, 0.6f));
        }
    }
    
    public NPCAction DecideNextAction(WorldState world)
    {
        // Only make decisions periodically
        var timeSinceLastDecision = DateTime.Now - lastDecisionTime;
        if (timeSinceLastDecision.TotalMinutes < DECISION_COOLDOWN_MINUTES)
        {
            return new NPCAction { Type = ActionType.Continue };
        }
        
        lastDecisionTime = DateTime.Now;
        
        // Phase 21: Enhanced decision making with Pascal behaviors
        ProcessEnhancedBehaviors(world);
        
        // Update emotional state based on recent events
        emotions.Update(memory.GetRecentEvents());
        
        // Decay old memories
        memory.DecayMemories();
        
        // Re-evaluate goals based on current situation
        goals.UpdateGoals(owner, world, memory, emotions);
        
        // Get current priority goal
        var currentGoal = goals.GetPriorityGoal();
        if (currentGoal == null)
        {
            return new NPCAction { Type = ActionType.Idle };
        }
        
        // Generate possible actions based on current goal
        var possibleActions = GenerateActions(currentGoal, world);
        
        // Score each action based on personality and current state
        var bestAction = SelectBestAction(possibleActions);
        
        // Record the decision
        memory.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.PersonalAchievement,
            Description = $"Decided to {bestAction.Type} for goal: {currentGoal.Name}",
            Importance = 0.3f,
            Location = owner.CurrentLocation
        });
        
        return bestAction;
    }
    
    /// <summary>
    /// Phase 21: Enhanced behavior processing with Pascal compatibility
    /// </summary>
    private void ProcessEnhancedBehaviors(WorldState world)
    {
        // Pascal NPC_CHEC.PAS inventory management
        if (ShouldCheckInventory())
        {
            ProcessInventoryCheck();
        }
        
        // Pascal NPCMAINT.PAS shopping behavior
        if (ShouldAttemptShopping(world))
        {
            ProcessShoppingBehavior(world);
        }
        
        // Pascal NPCMAINT.PAS gang behavior
        if (ShouldProcessGangBehavior())
        {
            ProcessGangBehavior();
        }
        
        // Pascal NPCMAINT.PAS believer system
        if (ShouldProcessBelieverBehavior())
        {
            ProcessBelieverBehavior();
        }
        
        // Pascal RELATIO2.PAS relationship processing
        if (ShouldProcessRelationships())
        {
            ProcessRelationshipBehavior();
        }
    }
    
    /// <summary>
    /// Pascal NPC_CHEC.PAS inventory checking logic
    /// </summary>
    private bool ShouldCheckInventory()
    {
        var timeSinceLastCheck = DateTime.Now - lastInventoryCheck;
        return timeSinceLastCheck.TotalHours >= 2; // Check every 2 hours
    }
    
    private void ProcessInventoryCheck()
    {
        lastInventoryCheck = DateTime.Now;
        memory.AddMemory("I checked my equipment and inventory", "inventory", DateTime.Now);
        
        // Pascal Check_Inventory logic - evaluate current equipment
        var needsBetterWeapon = owner.WeaponPower < owner.Level * 15;
        var needsBetterArmor = owner.ArmorClass < owner.Level * 10;
        
        if (needsBetterWeapon)
        {
            goals.AddGoal(new Goal("Find Better Weapon", GoalType.Economic, 0.8f));
            memory.AddMemory("My weapon is getting outdated", "equipment", DateTime.Now);
        }
        
        if (needsBetterArmor)
        {
            goals.AddGoal(new Goal("Find Better Armor", GoalType.Economic, 0.7f));
            memory.AddMemory("My armor could be better", "equipment", DateTime.Now);
        }
    }
    
    /// <summary>
    /// Pascal NPCMAINT.PAS shopping behavior
    /// </summary>
    private bool ShouldAttemptShopping(WorldState world)
    {
        if (owner.Gold < 100) return false; // Pascal minimum gold requirement
        if (owner.HP < owner.MaxHP * 0.3f) return false; // Too injured
        
        var timeSinceLastShopping = DateTime.Now - lastShoppingAttempt;
        var shoppingFrequency = personality.Greed * 4; // Hours between attempts
        
        return timeSinceLastShopping.TotalHours >= shoppingFrequency;
    }
    
    private void ProcessShoppingBehavior(WorldState world)
    {
        lastShoppingAttempt = DateTime.Now;
        
        var shoppingLocations = new[] { "Main Street", "Weapon Shop", "Armor Shop", "Magic Shop" };
        if (!shoppingLocations.Contains(owner.CurrentLocation))
        {
            // Add goal to go shopping
            goals.AddGoal(new Goal("Go Shopping", GoalType.Economic, 0.6f));
            return;
        }
        
        // Determine what to buy based on Pascal Ok_To_Buy logic
        var shoppingGoals = DetermineShoppingGoals();
        foreach (var goal in shoppingGoals)
        {
            goals.AddGoal(goal);
            memory.AddMemory($"I need to buy {goal.Name}", "shopping", DateTime.Now);
        }
    }
    
    private List<Goal> DetermineShoppingGoals()
    {
        var shoppingGoals = new List<Goal>();
        
        // Pascal class-based shopping logic
        switch (owner.Class)
        {
            case CharacterClass.Warrior:
                if (owner.WeaponPower < owner.Level * 20)
                    shoppingGoals.Add(new Goal("Buy Better Weapon", GoalType.Economic, personality.Greed));
                if (owner.ArmorClass < owner.Level * 15)
                    shoppingGoals.Add(new Goal("Buy Better Armor", GoalType.Economic, personality.Greed * 0.8f));
                break;
                
            case CharacterClass.Magician:
                if (owner.Mana < owner.MaxMana * 0.7f)
                    shoppingGoals.Add(new Goal("Buy Mana Potions", GoalType.Economic, 0.8f));
                shoppingGoals.Add(new Goal("Buy Magic Items", GoalType.Economic, 0.6f));
                break;
                
            case CharacterClass.Paladin:
                if (owner.HP < owner.MaxHP * 0.8f)
                    shoppingGoals.Add(new Goal("Buy Healing Potions", GoalType.Economic, 0.7f));
                break;
        }
        
        return shoppingGoals;
    }
    
    /// <summary>
    /// Pascal NPCMAINT.PAS gang behavior processing
    /// </summary>
    private bool ShouldProcessGangBehavior()
    {
        return GetTimeSinceLastActivity("gang_behavior").TotalHours >= 6;
    }
    
    private void ProcessGangBehavior()
    {
        lastActivities["gang_behavior"] = DateTime.Now;

        if (string.IsNullOrEmpty(owner.Team) && string.IsNullOrEmpty(owner.GangId))
        {
            // Not in a gang - consider joining one
            if (personality.IsLikelyToJoinGang() && random.Next(10) == 0
                && !goals.AllGoals.Any(g => g.Name.Contains("Gang") || g.Name.Contains("Join")))
            {
                goals.AddGoal(new Goal("Find Gang to Join", GoalType.Social, 0.7f));
                memory.AddMemory("I should look for a gang to join", "social", DateTime.Now);
            }
        }
        else
        {
            // In a gang - process gang loyalty
            var loyaltyLevel = personality.Loyalty;
            
            if (loyaltyLevel > 0.7f)
            {
                goals.AddGoal(new Goal("Support Gang Activities", GoalType.Social, loyaltyLevel));
                memory.AddMemory($"I'm committed to {owner.Team}", "gang", DateTime.Now);
            }
            else if (loyaltyLevel < 0.3f && random.Next(20) == 0)
            {
                // Potential gang betrayal or leaving
                goals.AddGoal(new Goal("Consider Leaving Gang", GoalType.Social, 0.4f));
                memory.AddMemory($"I'm having doubts about {owner.Team}", "gang", DateTime.Now);
            }
        }
    }
    
    /// <summary>
    /// Pascal NPCMAINT.PAS believer system processing
    /// </summary>
    private bool ShouldProcessBelieverBehavior()
    {
        return random.Next(3) == 0; // Pascal 33% processing chance
    }
    
    private void ProcessBelieverBehavior()
    {
        if (string.IsNullOrEmpty(owner.God))
        {
            // Potential conversion based on personality and circumstances
            var conversionChance = CalculateConversionChance();
            if (random.NextDouble() < conversionChance)
            {
                ConvertToFaith();
            }
        }
        else
        {
            // Existing believer - perform faith actions
            ProcessFaithActions();
        }
    }
    
    private double CalculateConversionChance()
    {
        var baseChance = 0.02; // 2% base chance
        
        // Personality modifiers
        if (personality.Sociability > 0.7f) baseChance *= 1.5;
        if (emotions.GetCurrentMood() < 0.3f) baseChance *= 2; // Desperate times
        if (owner.HP < owner.MaxHP * 0.5f) baseChance *= 1.5; // Injured and vulnerable
        
        return Math.Min(baseChance, 0.1); // Cap at 10%
    }
    
    private void ConvertToFaith()
    {
        var availableGods = new[] { "Nosferatu", "Darkcloak", "Druid", "Seth Able" };
        owner.God = availableGods[random.Next(availableGods.Length)];
        
        memory.AddMemory($"I found faith in {owner.God}", "faith", DateTime.Now);
        emotions.AddEmotion(EmotionType.Hope, 0.7f, 300); // 5 hours of hope
        
        // Add faith-based goals
        goals.AddGoal(new Goal($"Serve {owner.God}", GoalType.Social, 0.7f));
        goals.AddGoal(new Goal("Live According to Faith", GoalType.Personal, 0.6f));
        
        // GD.Print($"[Faith] {owner.Name} converted to {owner.God}");
    }
    
    private void ProcessFaithActions()
    {
        var faithActions = new[] { "pray", "make offering", "seek guidance", "help others", "spread faith" };
        var action = faithActions[random.Next(faithActions.Length)];
        
        memory.AddMemory($"I {action} in service of {owner.God}", "faith", DateTime.Now);
        emotions.AddEmotion(EmotionType.Peace, 0.3f, 120); // 2 hours of peace
        
        // Faith actions can generate new goals
        if (action == "help others" && random.Next(5) == 0)
        {
            goals.AddGoal(new Goal("Help Someone in Need", GoalType.Social, 0.6f));
        }
    }
    
    /// <summary>
    /// Pascal RELATIO2.PAS relationship processing
    /// </summary>
    private bool ShouldProcessRelationships()
    {
        return GetTimeSinceLastActivity("relationships").TotalHours >= 12;
    }
    
    private void ProcessRelationshipBehavior()
    {
        lastActivities["relationships"] = DateTime.Now;
        
        // Marriage considerations
        if (!owner.Married && owner.Level >= 5)
        {
            if (random.Next(50) == 0) // 2% chance per processing cycle
            {
                goals.AddGoal(new Goal("Look for Marriage Partner", GoalType.Social, 0.6f));
                memory.AddMemory("I'm thinking about finding someone special", "romance", DateTime.Now);
            }
        }
        
        // Friendship development
        if (personality.Sociability > 0.6f && random.Next(10) == 0)
        {
            goals.AddGoal(new Goal("Strengthen Friendships", GoalType.Social, personality.Sociability));
            memory.AddMemory("I should spend time with friends", "social", DateTime.Now);
        }
        
        // Enemy relationship processing
        var enemies = memory.GetMemoriesOfType(MemoryType.Attacked)
            .Where(m => m.IsRecent(168)) // Within a week
            .ToList();
            
        if (enemies.Any() && personality.Vengefulness > 0.6f)
        {
            var recentEnemy = enemies.OrderByDescending(m => m.Importance).First();
            goals.AddGoal(new Goal($"Settle Score with {recentEnemy.InvolvedCharacter}", 
                GoalType.Social, personality.Vengefulness));
        }
    }
    
    private TimeSpan GetTimeSinceLastActivity(string activityType)
    {
        if (lastActivities.ContainsKey(activityType))
        {
            return DateTime.Now - lastActivities[activityType];
        }
        return TimeSpan.FromDays(1); // Force first-time processing
    }
    
    private void InitializeGoals()
    {
        // Add basic goals based on personality and archetype
        switch (personality.Archetype.ToLower())
        {
            case "thug":
                goals.AddGoal(new Goal("Dominate Others", GoalType.Social, 0.8f));
                goals.AddGoal(new Goal("Gain Strength", GoalType.Personal, 0.7f));
                goals.AddGoal(new Goal("Find Enemies", GoalType.Social, 0.6f));
                break;
                
            case "merchant":
                goals.AddGoal(new Goal("Accumulate Wealth", GoalType.Economic, 0.9f));
                goals.AddGoal(new Goal("Build Trade Network", GoalType.Social, 0.7f));
                goals.AddGoal(new Goal("Secure Trade Routes", GoalType.Economic, 0.6f));
                break;
                
            case "guard":
                goals.AddGoal(new Goal("Maintain Order", GoalType.Social, 0.8f));
                goals.AddGoal(new Goal("Protect Citizens", GoalType.Social, 0.7f));
                goals.AddGoal(new Goal("Improve Skills", GoalType.Personal, 0.5f));
                break;
                
            case "priest":
                goals.AddGoal(new Goal("Help Others", GoalType.Social, 0.8f));
                goals.AddGoal(new Goal("Spread Faith", GoalType.Social, 0.7f));
                goals.AddGoal(new Goal("Gain Wisdom", GoalType.Personal, 0.6f));
                break;
                
            case "noble":
                goals.AddGoal(new Goal("Gain Political Power", GoalType.Social, 0.9f));
                goals.AddGoal(new Goal("Increase Influence", GoalType.Social, 0.8f));
                goals.AddGoal(new Goal("Maintain Status", GoalType.Personal, 0.7f));
                break;
                
            default:
                goals.AddGoal(new Goal("Survive", GoalType.Personal, 0.6f));
                goals.AddGoal(new Goal("Improve Life", GoalType.Personal, 0.5f));
                break;
        }
        
        // Add personality-driven goals
        if (personality.Greed > 0.7f)
        {
            goals.AddGoal(new Goal("Become Wealthy", GoalType.Economic, personality.Greed));
        }
        
        if (personality.Ambition > 0.8f)
        {
            goals.AddGoal(new Goal("Gain Power", GoalType.Social, personality.Ambition));
        }
        
        if (personality.Vengefulness > 0.7f)
        {
            goals.AddGoal(new Goal("Seek Revenge", GoalType.Social, personality.Vengefulness));
        }
    }
    
    private List<NPCAction> GenerateActions(Goal goal, WorldState world)
    {
        var actions = new List<NPCAction>();
        
        switch (goal.Type)
        {
            case GoalType.Economic:
                actions.AddRange(GenerateEconomicActions(world));
                break;
                
            case GoalType.Social:
                actions.AddRange(GenerateSocialActions(world));
                break;
                
            case GoalType.Personal:
                actions.AddRange(GeneratePersonalActions(world));
                break;
                
            case GoalType.Combat:
                actions.AddRange(GenerateCombatActions(world));
                break;
        }
        
        // Always add basic actions
        actions.Add(new NPCAction { Type = ActionType.Idle, Priority = 0.1f });
        actions.Add(new NPCAction { Type = ActionType.Explore, Priority = 0.3f });
        
        return actions;
    }
    
    private List<NPCAction> GenerateEconomicActions(WorldState world)
    {
        var actions = new List<NPCAction>();
        
        if (personality.Greed > 0.5f)
        {
            actions.Add(new NPCAction 
            { 
                Type = ActionType.Trade, 
                Priority = personality.Greed * 0.8f,
                Target = FindTradePartner(world)
            });
            
            if (personality.GetDecisionWeight("steal") > 0.6f)
            {
                actions.Add(new NPCAction 
                { 
                    Type = ActionType.Steal, 
                    Priority = personality.GetDecisionWeight("steal"),
                    Target = FindStealTarget(world)
                });
            }
        }
        
        return actions;
    }
    
    private List<NPCAction> GenerateSocialActions(WorldState world)
    {
        var actions = new List<NPCAction>();
        
        if (personality.Sociability > 0.6f)
        {
            actions.Add(new NPCAction 
            { 
                Type = ActionType.Socialize, 
                Priority = personality.Sociability * 0.7f,
                Target = FindSocialTarget(world)
            });
        }
        
        if (personality.IsLikelyToJoinGang() && owner.GangId == null)
        {
            actions.Add(new NPCAction 
            { 
                Type = ActionType.JoinGang, 
                Priority = 0.8f,
                Target = FindGangToJoin(world)
            });
        }
        
        if (personality.IsLikelyToSeekRevenge())
        {
            var enemy = FindRevengTarget();
            if (enemy != null)
            {
                actions.Add(new NPCAction 
                { 
                    Type = ActionType.SeekRevenge, 
                    Priority = personality.Vengefulness,
                    Target = enemy
                });
            }
        }
        
        return actions;
    }
    
    private List<NPCAction> GeneratePersonalActions(WorldState world)
    {
        var actions = new List<NPCAction>();
        
        if (owner.CurrentHP < owner.MaxHP * 0.5f)
        {
            actions.Add(new NPCAction 
            { 
                Type = ActionType.Rest, 
                Priority = 0.9f 
            });
        }
        
        if (personality.Ambition > 0.7f)
        {
            actions.Add(new NPCAction 
            { 
                Type = ActionType.Train, 
                Priority = personality.Ambition * 0.6f 
            });
        }
        
        return actions;
    }
    
    private List<NPCAction> GenerateCombatActions(WorldState world)
    {
        var actions = new List<NPCAction>();
        
        if (personality.Aggression > 0.6f)
        {
            var target = FindCombatTarget(world);
            if (target != null)
            {
                actions.Add(new NPCAction 
                { 
                    Type = ActionType.Attack, 
                    Priority = personality.Aggression,
                    Target = target
                });
            }
        }
        
        return actions;
    }
    
    private NPCAction SelectBestAction(List<NPCAction> actions)
    {
        if (!actions.Any())
        {
            return new NPCAction { Type = ActionType.Idle };
        }
        
        // Modify priorities based on emotional state
        foreach (var action in actions)
        {
            action.Priority *= emotions.GetActionModifier(action.Type);
        }
        
        // Add randomness based on impulsiveness
        if (personality.Impulsiveness > 0.7f && actions.Count > 1)
        {
            // Sometimes pick a random action instead of the best one
            if (GD.Randf() < personality.Impulsiveness * 0.3f)
            {
                return actions[GD.RandRange(0, actions.Count - 1)];
            }
        }
        
        // Select the highest priority action
        return actions.OrderByDescending(a => a.Priority).First();
    }
    
    private string? FindTradePartner(WorldState world)
    {
        // Find NPCs in the same location who are merchants or friendly
        return world.GetNPCsInLocation(owner.CurrentLocation)
            .Where(npc => npc.Id != owner.Id && npc.Archetype == "merchant")
            .FirstOrDefault()?.Id;
    }
    
    private string? FindStealTarget(WorldState world)
    {
        // Find wealthy NPCs to steal from
        return world.GetNPCsInLocation(owner.CurrentLocation)
            .Where(npc => npc.Id != owner.Id && npc.Gold > 100 && !owner.IsAllyOf(npc.Id))
            .OrderByDescending(npc => npc.Gold)
            .FirstOrDefault()?.Id;
    }
    
    private string? FindSocialTarget(WorldState world)
    {
        // Find compatible NPCs to socialize with
        return world.GetNPCsInLocation(owner.CurrentLocation)
            .Where(npc => npc.Id != owner.Id && !owner.IsEnemyOf(npc.Id))
            .FirstOrDefault()?.Id;
    }
    
    private string? FindGangToJoin(WorldState world)
    {
        // Find gang leaders to approach
        return world.GetNPCsInLocation(owner.CurrentLocation)
            .Where(npc => npc.Id != owner.Id && npc.GangMembers.Any())
            .FirstOrDefault()?.Id;
    }
    
    private string? FindRevengTarget()
    {
        // Find enemies from memory
        return memory.GetMemoriesOfType(MemoryType.Attacked)
            .Where(m => m.InvolvedCharacter != null)
            .OrderByDescending(m => m.Importance)
            .FirstOrDefault()?.InvolvedCharacter;
    }
    
    private string? FindCombatTarget(WorldState world)
    {
        // Find enemies or potential targets based on personality
        var npcsHere = world.GetNPCsInLocation(owner.CurrentLocation);
        
        // Prioritize known enemies
        var enemy = npcsHere.FirstOrDefault(npc => owner.IsEnemyOf(npc.Id));
        if (enemy != null) return enemy.Id;
        
        // For aggressive NPCs, find weaker targets
        if (personality.Aggression > 0.8f)
        {
            return npcsHere
                .Where(npc => npc.Id != owner.Id && npc.Level < owner.Level)
                .FirstOrDefault()?.Id;
        }
        
        return null;
    }
    
    public void RecordInteraction(Character other, InteractionType type, Dictionary<string, object>? details = null)
    {
        // Record the interaction in memory
        var importance = CalculateInteractionImportance(type);
        var memoryType = MapInteractionToMemoryType(type);
        
        memory.RecordEvent(new MemoryEvent
        {
            Type = memoryType,
            Description = $"I {type.ToString().ToLower()} {other.Name}",
            InvolvedCharacter = other.Name,
            Importance = importance,
            Location = owner.CurrentLocation,
            Details = details ?? new Dictionary<string, object>()
        });
        
        // Update relationship
        relationships.UpdateRelationship(other.Name, type);
        
        // Trigger emotional response
        emotions.ProcessInteraction(type, other, importance);
        
        // Update goals based on interaction
        goals.ProcessInteractionFeedback(type, other, importance);
        
        // GD.Print($"[AI] {owner.Name} recorded {type} interaction with {other.Name}");
    }
    
    private MemoryType MapInteractionToMemoryType(InteractionType type)
    {
        return type switch
        {
            InteractionType.Attacked => MemoryType.Attacked,
            InteractionType.Helped => MemoryType.PersonalAchievement,
            InteractionType.Traded => MemoryType.PersonalAchievement,
            InteractionType.Betrayed => MemoryType.Attacked,
            _ => MemoryType.SocialInteraction
        };
    }
    
    private float CalculateInteractionImportance(InteractionType type)
    {
        return type switch
        {
            InteractionType.Attacked => 0.9f,
            InteractionType.Betrayed => 1.0f,
            InteractionType.Helped => 0.7f,
            InteractionType.Defended => 0.8f,
            InteractionType.Defeated => 0.8f,
            InteractionType.Threatened => 0.6f,
            InteractionType.Traded => 0.3f,
            InteractionType.SharedDrink => 0.2f,
            InteractionType.SharedItem => 0.4f,
            InteractionType.Complimented => 0.2f,
            InteractionType.Insulted => 0.4f,
            _ => 0.3f
        };
    }
    
    public void OnNPCLevelUp()
    {
        memory.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.PersonalAchievement,
            Description = $"Reached level {owner.Level}",
            Importance = 0.8f,
            Location = owner.CurrentLocation
        });
        
        // Level up might change goals
        goals.OnLevelUp(owner.Level);
        
        // Boost confidence emotion
        emotions.AddEmotion(EmotionType.Confidence, 0.7f, 300); // 5 hours
    }
    
    public string GetBrainSummary()
    {
        var summary = $"=== {owner.Name} Enhanced AI Brain ===\n";
        summary += $"Personality: {personality}\n";
        summary += $"Current Goal: {goals.GetPriorityGoal()?.Name ?? "None"}\n";
        summary += $"Active Emotions: {emotions.GetActiveEmotions().Count}\n";
        summary += $"Faith: {owner.God ?? "None"}\n";
        summary += $"Gang: {owner.Team ?? "None"}\n";
        summary += $"Married: {(owner.Married ? "Yes" : "No")}\n";
        summary += memory.GetMemorySummary();
        
        return summary;
    }

    /// <summary>
    /// Generate a greeting for a player based on personality and relationship
    /// </summary>
    public string GenerateGreeting(Character player, object relationship)
    {
        // Simple greeting based on relationship level
        if (relationship is int relValue)
        {
            return relValue switch
            {
                > 50 => "Hello, my friend!",
                > 0 => "Greetings.",
                > -50 => "What do you want?",
                _ => "Stay away from me!"
            };
        }

        return "Hello there.";
    }

    /// <summary>
    /// Get speech modifiers based on dominant personality traits
    /// Used by NPCDialogueGenerator for personality-driven dialogue
    /// </summary>
    public SpeechModifiers GetSpeechModifiers()
    {
        var modifiers = new SpeechModifiers();

        if (personality == null) return modifiers;

        // Determine dominant traits that affect speech
        if (personality.Aggression > 0.7f)
        {
            modifiers.Tone = "aggressive";
            modifiers.SentenceLength = "short";
            modifiers.Vocabulary = "threatening";
        }
        else if (personality.Aggression < 0.3f)
        {
            modifiers.Tone = "gentle";
            modifiers.SentenceLength = "normal";
            modifiers.Vocabulary = "polite";
        }

        if (personality.Intelligence > 0.7f)
        {
            modifiers.Vocabulary = "complex";
            modifiers.UsesPhilosophy = true;
        }
        else if (personality.Intelligence < 0.3f)
        {
            modifiers.Vocabulary = "simple";
            modifiers.SentenceLength = "short";
        }

        if (personality.Greed > 0.7f)
        {
            modifiers.MentionsMoney = true;
        }

        if (personality.Romanticism > 0.7f)
        {
            modifiers.Vocabulary = "flowery";
            modifiers.UsesCompliments = true;
        }

        if (personality.Sociability > 0.7f)
        {
            modifiers.Tone = "friendly";
            modifiers.UsesHumor = true;
        }

        if (personality.Loyalty > 0.7f)
        {
            modifiers.MentionsHonor = true;
        }

        if (personality.Courage > 0.7f)
        {
            modifiers.Tone = "confident";
        }
        else if (personality.Courage < 0.3f)
        {
            modifiers.Tone = "cautious";
        }

        if (personality.Trustworthiness < 0.3f)
        {
            modifiers.UsesDeflection = true;
        }

        return modifiers;
    }
}

/// <summary>
/// Container for speech modification parameters based on personality
/// </summary>
public class SpeechModifiers
{
    public string Tone { get; set; } = "neutral";
    public string SentenceLength { get; set; } = "normal";
    public string Vocabulary { get; set; } = "normal";
    public bool MentionsMoney { get; set; } = false;
    public bool UsesCompliments { get; set; } = false;
    public bool UsesHumor { get; set; } = false;
    public bool MentionsHonor { get; set; } = false;
    public bool UsesPhilosophy { get; set; } = false;
    public bool UsesDeflection { get; set; } = false;
}

// Supporting classes and enums
public class NPCAction
{
    public ActionType Type { get; set; }
    public float Priority { get; set; } = 0.5f;
    public string Target { get; set; } = ""; // Target character ID
    public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
}

public enum ActionType
{
    Idle,
    Continue,
    Explore,
    Trade,
    Socialize,
    Attack,
    Flee,
    Rest,
    Train,
    Steal,
    JoinGang,
    LeaveGang,
    SeekRevenge,
    Help,
    Betray,
    
    // Additional actions for testing compatibility
    SeekHealing,
    DoNothing,
    ExploreDungeon,
    LootThenFlee,
    SeekWealth,
    SeekCombat,
    FormGang,
    AccumulateWealth,
    CallForHelp,
    VisitTavern
}

public enum InteractionType
{
    Attacked,
    Betrayed,
    Helped,
    Defended,
    Traded,
    SharedDrink,
    SharedItem,
    Defeated,
    Threatened,
    Insulted,
    Complimented,
    Challenged,
    Intimidated
}

public partial class WorldState
{
    private List<NPC> npcs;
    
    // Properties for test compatibility
    public int CurrentHour { get; set; }
    public string CurrentLocation { get; set; } = "";
    public bool InCombat { get; set; }
    public Character[] NearbyCharacters { get; set; } = Array.Empty<Character>();
    
    public WorldState(List<NPC>? worldNPCs = null)
    {
        npcs = worldNPCs ?? new List<NPC>();
    }
    
    public List<NPC> GetNPCsInLocation(string location)
    {
        return npcs.Where(npc => npc.CurrentLocation == location && npc.IsAlive).ToList();
    }
    
    public NPC? GetNPCById(string id)
    {
        return npcs.FirstOrDefault(npc => npc.Id == id);
    }
} 
