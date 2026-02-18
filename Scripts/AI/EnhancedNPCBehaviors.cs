using UsurperRemake.Utils;
using UsurperRemake.Systems;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Enhanced NPC Behaviors - Phase 21 Implementation
/// Pascal-compatible NPC behaviors that extend the existing AI system
/// </summary>
public static class EnhancedNPCBehaviors
{
    private static Random random = new Random();
    
    /// <summary>
    /// Enhanced inventory management - Pascal NPC_CHEC.PAS Check_Inventory
    /// </summary>
    public static int CheckNPCInventory(NPC npc, int itemId = 0, bool shout = false)
    {
        // ClassicMode check removed - const false makes code unreachable
        
        var result = 0; // Pascal return codes: 0=not touched, 1=equipped, 2=swapped
        
        if (itemId > 0)
        {
            // NPC examines new item (Pascal shout logic)
            if (shout)
            {
                GD.Print($"{npc.Name2} looks at the new item.");
            }
            
            // Determine if NPC should use this item
            result = ProcessItemDecision(npc, itemId, shout);
        }
        else
        {
            // Reinventory all items (Pascal onr = 0 logic)
            ReinventoryAllItems(npc);
        }
        
        return result;
    }
    
    /// <summary>
    /// NPC shopping AI - Pascal NPCMAINT.PAS Npc_Buy function
    /// </summary>
    public static bool ProcessNPCShopping(NPC npc)
    {
        // Pascal shopping conditions
        if (npc.Gold < 100) return false;
        if (npc.HP < npc.MaxHP * 0.3f) return false; // Too injured to shop
        
        var shoppingGoals = DetermineShoppingNeeds(npc);
        if (shoppingGoals.Count == 0) return false;
        
        foreach (var goal in shoppingGoals)
        {
            if (AttemptPurchase(npc, goal))
            {
                RecordPurchaseInMemory(npc, goal);
                return true; // One purchase per shopping session
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Gang management - Pascal NPCMAINT.PAS gang logic
    /// </summary>
    public static void ProcessGangMaintenance(List<NPC> npcs)
    {
        var gangAnalysis = AnalyzeGangs(npcs);
        
        foreach (var gang in gangAnalysis)
        {
            if (gang.Value.IsNPCOnly && gang.Value.Size <= 3)
            {
                if (random.Next(4) == 0) // 25% chance to dissolve
                {
                    DissolveGang(gang.Key, npcs);
                }
                else
                {
                    RecruitGangMembers(gang.Key, npcs);
                }
            }
        }
    }
    
    /// <summary>
    /// NPC believer system - Pascal NPCMAINT.PAS NPC_Believer
    /// Conversion now requires social contact with an existing believer (proselytizing).
    /// </summary>
    public static void ProcessBelieverSystem(NPC npc)
    {
        // NPCBelievers check removed - const 50 makes code unreachable
        if (random.Next(3) != 0) return; // Only 33% processed per cycle

        if (!string.IsNullOrEmpty(npc.God))
        {
            // Existing believer actions (pray, offering, seek guidance, preach)
            ProcessBelieverActions(npc);

            // 2% chance this believer attempts to proselytize a nearby non-believer
            if (random.NextDouble() < 0.02)
            {
                AttemptProselytize(npc);
            }
        }
        // Non-believers do nothing on their own — they can only be converted
        // through social contact with an existing believer via AttemptProselytize.
    }
    
    /// <summary>
    /// Automated gang warfare - Pascal AUTOGANG.PAS Auto_Gangwar
    /// </summary>
    public static GangWarResult ConductAutoGangWar(string gang1, string gang2, List<NPC> npcs)
    {
        var team1 = LoadGangMembers(gang1, npcs);
        var team2 = LoadGangMembers(gang2, npcs);
        
        if (team1.Count == 0 || team2.Count == 0)
        {
            return new GangWarResult { Outcome = "No Contest" };
        }
        
        // Generate news header (Pascal Gang_War_Header)
        var header = GetGangWarHeader();
        GenerateGangWarNews(header, gang1, gang2);
        
        // Conduct battles
        var result = new GangWarResult { Gang1 = gang1, Gang2 = gang2 };
        ConductGangBattles(team1, team2, result);
        
        return result;
    }
    
    /// <summary>
    /// Enhanced relationship processing - Pascal RELATIO2.PAS
    /// </summary>
    public static void ProcessNPCRelationships(NPC npc, List<NPC> allNPCs)
    {
        // Marriage system
        if (!npc.Married && npc.Level >= 5)
        {
            if (random.Next(500) == 0) // 0.2% chance per cycle (~1 attempt per NPC per 4 hours)
            {
                AttemptNPCMarriage(npc, allNPCs);
            }
        }
        
        // Friendship development
        ProcessFriendshipDevelopment(npc, allNPCs);
        
        // Enemy relationship tracking
        ProcessEnemyRelationships(npc);
    }
    
    #region Private Helper Methods
    
    private static int ProcessItemDecision(NPC npc, int itemId, bool shout)
    {
        // Simplified item evaluation (Pascal objekt_test logic)
        var currentValue = GetCurrentEquipmentValue(npc);
        var newItemValue = EstimateItemValue(itemId);
        
        if (newItemValue > currentValue * 1.2f) // 20% better
        {
            if (shout)
            {
                // GD.Print($"{npc.Name2} starts to use the new item instead.");
            }
            
            // Send mail notification (Pascal Inform_By_Mail)
            SendItemNotificationMail(npc, itemId);
            
            return 2; // Swapped equipment
        }
        else if (newItemValue > currentValue * 1.1f) // 10% better
        {
            if (shout)
            {
                // GD.Print($"{npc.Name2} starts to use the new item.");
            }
            
            SendItemNotificationMail(npc, itemId);
            return 1; // Equipped new item
        }
        
        return 0; // No change
    }
    
    private static void ReinventoryAllItems(NPC npc)
    {
        // Pascal reinventory logic - check all items
        npc.Memory?.AddMemory("I reorganized my equipment", "inventory", DateTime.Now);
        
        // Simple optimization
        OptimizeNPCEquipment(npc);
    }
    
    private static List<ShoppingGoal> DetermineShoppingNeeds(NPC npc)
    {
        var goals = new List<ShoppingGoal>();
        
        // Pascal Ok_To_Buy logic based on class
        switch (npc.Class)
        {
            case CharacterClass.Warrior:
                if (npc.WeaponPower < npc.Level * 20)
                    goals.Add(new ShoppingGoal { Type = "weapon", Priority = 0.8f });
                if (npc.ArmorClass < npc.Level * 15)
                    goals.Add(new ShoppingGoal { Type = "armor", Priority = 0.7f });
                break;
                
            case CharacterClass.Magician:
                if (npc.Mana < npc.MaxMana * 0.7f)
                    goals.Add(new ShoppingGoal { Type = "mana_potion", Priority = 0.9f });
                goals.Add(new ShoppingGoal { Type = "magic_item", Priority = 0.6f });
                break;
                
            case CharacterClass.Paladin:
                if (npc.HP < npc.MaxHP * 0.8f)
                    goals.Add(new ShoppingGoal { Type = "healing_potion", Priority = 0.8f });
                break;
        }
        
        return goals.OrderByDescending(g => g.Priority).ToList();
    }
    
    private static bool AttemptPurchase(NPC npc, ShoppingGoal goal)
    {
        var cost = CalculateItemCost(goal.Type, npc.Level);
        
        if (npc.Gold >= cost)
        {
            npc.Gold -= cost;
            return true;
        }
        
        return false;
    }
    
    private static void RecordPurchaseInMemory(NPC npc, ShoppingGoal goal)
    {
        npc.Memory?.AddMemory($"I bought a {goal.Type}", "purchase", DateTime.Now);
    }
    
    private static Dictionary<string, GangInfo> AnalyzeGangs(List<NPC> npcs)
    {
        return npcs.Where(n => !string.IsNullOrEmpty(n.Team))
                  .GroupBy(n => n.Team)
                  .ToDictionary(g => g.Key, g => new GangInfo
                  {
                      Size = g.Count(),
                      IsNPCOnly = g.All(n => n.AI == CharacterAI.Computer)
                  });
    }
    
    private static void DissolveGang(string gangName, List<NPC> npcs)
    {
        // Pascal Remove_Gang procedure
        // GD.Print($"Removing NPC team: {gangName}");
        
        foreach (var member in npcs.Where(n => n.Team == gangName))
        {
            member.Team = "";
            member.ControlsTurf = false;
            member.TeamPassword = "";
            member.GymOwner = 0;
        }
        
        // Generate news
        NewsSystem.Instance.Newsy($"{GameConfig.TeamColor}{gangName}{GameConfig.NewsColorDefault} ceased to exist!", false, GameConfig.NewsCategory.General);
    }
    
    private static void RecruitGangMembers(string gangName, List<NPC> npcs)
    {
        var availableNPCs = npcs.Where(n => 
            string.IsNullOrEmpty(n.Team) && 
            !n.King && 
            n.IsAlive).Take(3).ToList();
        
        foreach (var candidate in availableNPCs)
        {
            if (random.Next(3) == 0) // 33% recruitment chance
            {
                candidate.Team = gangName;
                
                // Copy team settings from existing member
                var existingMember = npcs.FirstOrDefault(n => n.Team == gangName);
                if (existingMember != null)
                {
                    candidate.TeamPassword = existingMember.TeamPassword;
                    candidate.ControlsTurf = existingMember.ControlsTurf;
                }
                
                // Generate news
                NewsSystem.Instance.Newsy($"{GameConfig.NewsColorPlayer}{candidate.Name2}{GameConfig.NewsColorDefault} has been recruited to {GameConfig.TeamColor}{gangName}{GameConfig.NewsColorDefault}", true, GameConfig.NewsCategory.General);
            }
        }
    }
    
    /// <summary>
    /// A believing NPC attempts to proselytize a non-believer at the same location.
    /// Conversion chance is based on the speaker's sociability, the listener's personality,
    /// and the existing relationship between them.
    /// </summary>
    private static void AttemptProselytize(NPC believer)
    {
        if (believer.IsDead || !believer.IsAlive) return;

        // Find non-believer NPCs at the same location
        var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (allNPCs == null || allNPCs.Count == 0) return;

        var candidates = allNPCs.Where(n =>
            n != believer &&
            !n.IsDead && n.IsAlive &&
            string.IsNullOrEmpty(n.God) &&
            n.CurrentLocation == believer.CurrentLocation).ToList();

        if (candidates.Count == 0) return;

        // Pick a random non-believer at the same location
        var target = candidates[random.Next(candidates.Count)];

        // Calculate social-contact-based conversion chance
        var conversionChance = CalculateSocialConversionChance(believer, target);
        if (random.NextDouble() < conversionChance)
        {
            ConvertNPCToFaith(target, believer.God);

            // Generate news about the conversion
            NewsSystem.Instance?.Newsy(
                $"{GameConfig.NewsColorPlayer}{target.Name2}{GameConfig.NewsColorDefault} was converted to the faith of {believer.God} by {GameConfig.NewsColorPlayer}{believer.Name2}{GameConfig.NewsColorDefault}",
                true, GameConfig.NewsCategory.General);
        }
    }

    /// <summary>
    /// Calculate conversion chance based on social contact between a believing speaker
    /// and a non-believing listener. Factors: speaker sociability, listener mysticism,
    /// listener intelligence, and their existing relationship.
    /// </summary>
    private static double CalculateSocialConversionChance(NPC speaker, NPC listener)
    {
        // Base: 5%
        double chance = 0.05;

        // Speaker's Sociability bonus: multiply by (0.5 + Sociability)
        float speakerSociability = speaker.Personality?.Sociability ?? 0.5f;
        chance *= (0.5 + speakerSociability);

        // Listener's Mysticism > 0.5: +30% additive to base
        float listenerMysticism = listener.Personality?.Mysticism ?? 0.5f;
        if (listenerMysticism > 0.5f)
            chance += 0.30;

        // Listener's Intelligence > 0.7: -20% additive to base (skeptical)
        float listenerIntelligence = listener.Personality?.Intelligence ?? 0.5f;
        if (listenerIntelligence > 0.7f)
            chance -= 0.20;

        // Relationship modifier: check speaker's impression of listener (and vice versa)
        float impression = speaker.Brain?.Memory?.GetCharacterImpression(listener.Name2) ?? 0f;
        if (impression > 0f)
            chance += 0.20; // Positive relationship: +20%
        else if (impression < 0f)
            chance -= 0.30; // Negative relationship: -30%

        // Clamp to a reasonable range (never negative, never guaranteed)
        return Math.Clamp(chance, 0.0, 0.85);
    }

    private static void ConvertNPCToFaith(NPC npc, string deity)
    {
        npc.God = deity;

        npc.Memory?.AddMemory($"I found faith in {npc.God}", "faith", DateTime.Now);
        npc.EmotionalState?.AdjustMood("spiritual", 0.3f);

        // GD.Print($"[Faith] {npc.Name2} converted to {npc.God}");
    }
    
    private static void ProcessBelieverActions(NPC npc)
    {
        var actions = new[] { "pray", "make offering", "seek guidance", "preach" };
        var action = actions[random.Next(actions.Length)];
        
        npc.Memory?.AddMemory($"I {action} to {npc.God}", "faith", DateTime.Now);
        
        // Faith actions can affect mood and goals
        npc.EmotionalState?.AdjustMood("spiritual", 0.1f);
        
        if (random.Next(10) == 0) // 10% chance to add faith-based goal
        {
            npc.Goals?.AddGoal(new Goal($"Serve {npc.God}", GoalType.Social, 0.6f));
        }
    }
    
    private static List<NPC> LoadGangMembers(string gangName, List<NPC> npcs)
    {
        return npcs.Where(n => n.Team == gangName && n.IsAlive).ToList();
    }
    
    private static string GetGangWarHeader()
    {
        var headers = new[] { "Gang War!", "Team Bash!", "Team War!", "Turf War!", "Gang Fight!", "Rival Gangs Clash!" };
        return headers[random.Next(headers.Length)];
    }
    
    private static void GenerateGangWarNews(string header, string gang1, string gang2)
    {
        NewsSystem.Instance.Newsy($"{header} {GameConfig.TeamColor}{gang1}{GameConfig.NewsColorDefault} challenged {GameConfig.TeamColor}{gang2}{GameConfig.NewsColorDefault}", false, GameConfig.NewsCategory.General);
    }
    
    private static void ConductGangBattles(List<NPC> team1, List<NPC> team2, GangWarResult result)
    {
        // Pascal computer vs computer battles
        for (int i = 0; i < Math.Min(team1.Count, team2.Count); i++)
        {
            var fighter1 = team1[i];
            var fighter2 = team2[i];
            
            var battle = ConductSingleBattle(fighter1, fighter2);
            result.Battles.Add(battle);
        }
        
        // Determine overall winner
        var team1Wins = result.Battles.Count(b => b.Winner == 1);
        var team2Wins = result.Battles.Count(b => b.Winner == 2);
        
        result.Outcome = team1Wins > team2Wins ? $"{result.Gang1} Victory" : 
                        team2Wins > team1Wins ? $"{result.Gang2} Victory" : "Draw";
    }
    
    private static BattleResult ConductSingleBattle(NPC fighter1, NPC fighter2)
    {
        // Simplified battle logic
        var power1 = fighter1.Level + fighter1.WeaponPower + random.Next(20);
        var power2 = fighter2.Level + fighter2.WeaponPower + random.Next(20);
        
        var winner = power1 > power2 ? 1 : 2;
        var loser = winner == 1 ? fighter2 : fighter1;
        
        // Reduce loser HP
        loser.HP = Math.Max(1, loser.HP - random.Next(20, 50));
        
        return new BattleResult
        {
            Fighter1 = fighter1.Name2,
            Fighter2 = fighter2.Name2,
            Winner = winner,
            Rounds = random.Next(1, 5)
        };
    }
    
    // Additional helper methods
    private static int GetCurrentEquipmentValue(NPC npc) => (int)(npc.WeaponPower + npc.ArmorClass);
    private static int EstimateItemValue(int itemId) => (int)(itemId * 10L); // Placeholder
    private static void SendItemNotificationMail(NPC npc, int itemId) { /* Mail implementation */ }
    private static void OptimizeNPCEquipment(NPC npc) { /* Equipment optimization */ }
    private static int CalculateItemCost(string itemType, int level) => level * 50 + random.Next(25, 100);

    /// <summary>
    /// Attempt NPC-to-NPC marriage - finds compatible partner and marries them
    /// </summary>
    private static void AttemptNPCMarriage(NPC npc, List<NPC> candidates)
    {
        if (npc.IsDead || !npc.IsAlive) return;

        var profile = npc.Brain?.Personality;
        if (profile == null) return;

        // Already married - only polyamorous/open NPCs seek additional partners
        if (npc.Married || npc.IsMarried)
        {
            bool isPolyOrOpen = profile.RelationshipPref == RelationshipPreference.Polyamorous
                             || profile.RelationshipPref == RelationshipPreference.OpenRelationship;
            if (!isPolyOrOpen) return;
        }

        // Asexual NPCs don't seek marriage (but could still be approached)
        if (profile.Orientation == SexualOrientation.Asexual) return;

        // Get NPC's gender for attraction checks
        var npcGender = profile.Gender;

        // Find eligible partners - polyamorous/open NPCs can also match with married NPCs
        var eligiblePartners = candidates.Where(c =>
        {
            if (c.ID == npc.ID || c.IsDead || !c.IsAlive) return false;
            if (c.Level < 5 || c.Brain?.Personality == null) return false;
            if (Math.Abs(c.Level - npc.Level) > 10) return false;
            if (!IsCompatibleForMarriage(npc, c)) return false;
            // Can't marry the same person twice
            if (c.Name2 == npc.SpouseName) return false;

            // If candidate is already married, they must also be poly/open
            if (c.Married || c.IsMarried)
            {
                var cProfile = c.Brain?.Personality;
                bool cIsPolyOrOpen = cProfile?.RelationshipPref == RelationshipPreference.Polyamorous
                                  || cProfile?.RelationshipPref == RelationshipPreference.OpenRelationship;
                return cIsPolyOrOpen;
            }
            return true;
        }).ToList();

        if (eligiblePartners.Count == 0) return;

        // Score potential partners
        var scoredPartners = eligiblePartners
            .Select(p => new { Partner = p, Score = CalculateMarriageCompatibility(npc, p) })
            .Where(x => x.Score > 0.3f) // Minimum compatibility threshold
            .OrderByDescending(x => x.Score)
            .ToList();

        if (scoredPartners.Count == 0) return;

        // Pick from top candidates with some randomness
        var topCandidates = scoredPartners.Take(3).ToList();
        var chosen = topCandidates[random.Next(topCandidates.Count)];

        // Marriage success chance based on compatibility
        float marriageChance = chosen.Score * 0.5f; // Max 50% even with perfect compatibility
        if (random.NextDouble() > marriageChance) return;

        // Execute the marriage!
        ExecuteNPCMarriage(npc, chosen.Partner);
    }

    /// <summary>
    /// Check if two NPCs are compatible for marriage (mutual attraction)
    /// </summary>
    private static bool IsCompatibleForMarriage(NPC npc1, NPC npc2)
    {
        var profile1 = npc1.Brain?.Personality;
        var profile2 = npc2.Brain?.Personality;

        if (profile1 == null || profile2 == null) return false;

        // Both must be attracted to each other
        bool npc1AttractedTo2 = profile1.IsAttractedTo(profile2.Gender);
        bool npc2AttractedTo1 = profile2.IsAttractedTo(profile1.Gender);

        return npc1AttractedTo2 && npc2AttractedTo1;
    }

    /// <summary>
    /// Calculate marriage compatibility score between two NPCs
    /// </summary>
    private static float CalculateMarriageCompatibility(NPC npc1, NPC npc2)
    {
        var profile1 = npc1.Brain?.Personality;
        var profile2 = npc2.Brain?.Personality;

        if (profile1 == null || profile2 == null) return 0f;

        float score = 0.5f; // Base compatibility

        // Same class bonus
        if (npc1.Class == npc2.Class) score += 0.1f;

        // Similar alignment
        bool bothGood = npc1.Chivalry > npc1.Darkness && npc2.Chivalry > npc2.Darkness;
        bool bothEvil = npc1.Darkness > npc1.Chivalry && npc2.Darkness > npc2.Chivalry;
        if (bothGood || bothEvil) score += 0.15f;

        // Personality compatibility - opposites can attract but similar is safer
        float flirtDiff = Math.Abs(profile1.Flirtatiousness - profile2.Flirtatiousness);
        float commitDiff = Math.Abs(profile1.Commitment - profile2.Commitment);

        // Similar commitment levels are important for marriage stability
        if (commitDiff < 0.3f) score += 0.15f;
        else if (commitDiff > 0.6f) score -= 0.2f;

        // High flirtatiousness in both = potential drama, but more likely to marry
        if (profile1.Flirtatiousness > 0.6f && profile2.Flirtatiousness > 0.6f)
            score += 0.1f;

        // Relationship preference compatibility
        if (profile1.RelationshipPref == RelationshipPreference.Monogamous &&
            profile2.RelationshipPref == RelationshipPreference.Monogamous)
            score += 0.1f;

        // Same faction bonus
        if (npc1.NPCFaction.HasValue && npc1.NPCFaction == npc2.NPCFaction)
            score += 0.1f;

        // Opposite factions penalty (especially Faith vs Shadows)
        if (npc1.NPCFaction.HasValue && npc2.NPCFaction.HasValue)
        {
            var f1 = npc1.NPCFaction.Value;
            var f2 = npc2.NPCFaction.Value;
            if ((f1 == UsurperRemake.Systems.Faction.TheFaith && f2 == UsurperRemake.Systems.Faction.TheShadows) ||
                (f1 == UsurperRemake.Systems.Faction.TheShadows && f2 == UsurperRemake.Systems.Faction.TheFaith))
            {
                score -= 0.3f; // Romeo and Juliet situation - rare but possible
            }
        }

        return Math.Clamp(score, 0f, 1f);
    }

    /// <summary>
    /// Execute the marriage between two NPCs
    /// </summary>
    private static void ExecuteNPCMarriage(NPC npc1, NPC npc2)
    {
        // Defense-in-depth: if either is already married, they must be poly/open
        var p1 = npc1.Brain?.Personality;
        var p2 = npc2.Brain?.Personality;

        if (npc1.Married || npc1.IsMarried)
        {
            bool poly1 = p1?.RelationshipPref == RelationshipPreference.Polyamorous
                       || p1?.RelationshipPref == RelationshipPreference.OpenRelationship;
            if (!poly1) return;
        }
        if (npc2.Married || npc2.IsMarried)
        {
            bool poly2 = p2?.RelationshipPref == RelationshipPreference.Polyamorous
                       || p2?.RelationshipPref == RelationshipPreference.OpenRelationship;
            if (!poly2) return;
        }

        // Check if this is a polyamorous union (either party already married)
        bool isPoly = (npc1.Married || npc1.IsMarried) || (npc2.Married || npc2.IsMarried);

        // Set marriage flags
        npc1.Married = true;
        npc1.IsMarried = true;
        npc1.SpouseName = npc2.Name2;
        npc1.MarriedTimes++;

        npc2.Married = true;
        npc2.IsMarried = true;
        npc2.SpouseName = npc1.Name2;
        npc2.MarriedTimes++;

        // Store spouse IDs for tracking (using existing memory system)
        npc1.Brain?.Memory?.AddMemory($"I married {npc2.Name2}!", "marriage", DateTime.Now);
        npc2.Brain?.Memory?.AddMemory($"I married {npc1.Name2}!", "marriage", DateTime.Now);

        // Track the marriage in the NPC marriage registry
        NPCMarriageRegistry.Instance.RegisterMarriage(npc1.ID, npc2.ID, npc1.Name2, npc2.Name2);

        // Generate news - different messages for polyamorous vs traditional unions
        if (isPoly)
        {
            NewsSystem.Instance?.WriteNews(
                GameConfig.NewsCategory.Marriage,
                $"♥ {npc1.Name2} and {npc2.Name2} have entered a polyamorous union!"
            );
        }
        else
        {
            NewsSystem.Instance?.WriteNews(
                GameConfig.NewsCategory.Marriage,
                $"Wedding Bells! {npc1.Name2} and {npc2.Name2} have gotten married!"
            );
        }

        GD.Print($"[NPC Marriage] {npc1.Name2} married {npc2.Name2} (poly={isPoly})");
    }

    /// <summary>
    /// Process an affair attempt by the player on a married NPC
    /// Returns true if the affair progresses
    /// </summary>
    public static AffairResult ProcessAffairAttempt(NPC marriedNpc, Character player, float seductionSuccess)
    {
        var profile = marriedNpc.Brain?.Personality;
        if (profile == null) return new AffairResult { Success = false, Message = "They seem unresponsive." };

        var affair = NPCMarriageRegistry.Instance.GetOrCreateAffair(marriedNpc.ID, player.ID);

        // Calculate affair susceptibility
        float susceptibility = CalculateAffairSusceptibility(marriedNpc, profile, affair);

        // Apply seduction success
        float progressChance = susceptibility * seductionSuccess;

        if (random.NextDouble() < progressChance)
        {
            // Affair progresses!
            affair.AffairProgress = Math.Min(200, affair.AffairProgress + random.Next(5, 15));
            affair.LastInteraction = DateTime.Now;
            affair.SecretMeetings++;

            // Check for affair milestones
            if (affair.AffairProgress >= 100 && !affair.IsActive)
            {
                affair.IsActive = true;
                return new AffairResult
                {
                    Success = true,
                    Milestone = AffairMilestone.BecameLovers,
                    Message = $"{marriedNpc.Name2} looks at you with desire. \"I know this is wrong, but I can't resist you anymore...\""
                };
            }
            else if (affair.AffairProgress >= 75 && affair.SecretMeetings >= 3)
            {
                return new AffairResult
                {
                    Success = true,
                    Milestone = AffairMilestone.SecretRendezvous,
                    Message = $"{marriedNpc.Name2} whispers, \"Meet me tonight... alone. My spouse doesn't need to know.\""
                };
            }
            else if (affair.AffairProgress >= 50)
            {
                return new AffairResult
                {
                    Success = true,
                    Milestone = AffairMilestone.EmotionalConnection,
                    Message = $"{marriedNpc.Name2}'s eyes linger on you. \"I shouldn't feel this way about you...\""
                };
            }
            else
            {
                return new AffairResult
                {
                    Success = true,
                    Milestone = AffairMilestone.Flirting,
                    Message = $"{marriedNpc.Name2} blushes despite themselves. \"You're quite charming, aren't you?\""
                };
            }
        }
        else
        {
            // Failed attempt - might raise suspicion
            if (random.NextDouble() < 0.2f) // 20% chance spouse notices
            {
                int oldSuspicion = affair.SpouseSuspicion;
                affair.SpouseSuspicion = Math.Min(100, affair.SpouseSuspicion + random.Next(10, 25));

                // Record betrayal memory on spouse when suspicion crosses threshold
                if (oldSuspicion < GameConfig.MinSuspicionForConfrontation &&
                    affair.SpouseSuspicion >= GameConfig.MinSuspicionForConfrontation)
                {
                    string spouseName = RelationshipSystem.GetSpouseName(marriedNpc);
                    var spouse = !string.IsNullOrEmpty(spouseName)
                        ? UsurperRemake.Systems.NPCSpawnSystem.Instance?.GetNPCByName(spouseName)
                        : null;
                    spouse?.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.Betrayed,
                        Description = $"Suspects {player.Name2} is having an affair with {marriedNpc.Name2}",
                        InvolvedCharacter = player.Name2,
                        Importance = 0.9f,
                        EmotionalImpact = -0.8f
                    });
                }

                return new AffairResult
                {
                    Success = false,
                    SpouseNoticed = true,
                    Message = $"{marriedNpc.Name2} glances nervously toward where their spouse might be. \"We shouldn't...\""
                };
            }

            return new AffairResult
            {
                Success = false,
                Message = $"{marriedNpc.Name2} maintains their composure. \"I'm married, you know.\""
            };
        }
    }

    /// <summary>
    /// Calculate how susceptible a married NPC is to an affair
    /// </summary>
    private static float CalculateAffairSusceptibility(NPC npc, PersonalityProfile profile, AffairState affair)
    {
        float susceptibility = 0.1f; // Base very low - affairs are hard

        // High flirtatiousness increases susceptibility
        susceptibility += profile.Flirtatiousness * 0.2f;

        // Low commitment increases susceptibility
        susceptibility += (1f - profile.Commitment) * 0.25f;

        // Existing affair progress helps
        susceptibility += affair.AffairProgress * 0.003f; // Up to +0.3 at max progress

        // Polyamorous/open relationship preference
        if (profile.RelationshipPref == RelationshipPreference.OpenRelationship ||
            profile.RelationshipPref == RelationshipPreference.Polyamorous)
        {
            susceptibility += 0.2f;
        }

        // Casual preference
        if (profile.RelationshipPref == RelationshipPreference.CasualOnly)
        {
            susceptibility += 0.15f;
        }

        // Very high commitment = nearly impossible to seduce
        if (profile.Commitment > 0.85f)
        {
            susceptibility *= 0.2f; // Reduce to 20%
        }

        return Math.Clamp(susceptibility, 0.05f, 0.6f); // Max 60% even with perfect conditions
    }

    /// <summary>
    /// Check if a player's affair with a married NPC should cause the NPC to leave their spouse
    /// </summary>
    public static DivorceResult CheckAffairDivorce(NPC marriedNpc, Character player)
    {
        var affair = NPCMarriageRegistry.Instance.GetAffair(marriedNpc.ID, player.ID);
        if (affair == null || !affair.IsActive)
            return new DivorceResult { WillDivorce = false };

        var profile = marriedNpc.Brain?.Personality;
        if (profile == null)
            return new DivorceResult { WillDivorce = false };

        // Need significant affair progress and emotional investment
        if (affair.AffairProgress < 150)
            return new DivorceResult { WillDivorce = false };

        // Calculate divorce chance
        float divorceChance = 0f;

        // Low commitment = more likely to leave spouse
        divorceChance += (1f - profile.Commitment) * 0.3f;

        // High affair progress
        divorceChance += (affair.AffairProgress - 100) * 0.002f;

        // Player's charisma bonus
        float charismaBonus = (player.Charisma - 50) / 200f;
        divorceChance += Math.Max(0, charismaBonus);

        // Multiple secret meetings show real connection
        divorceChance += Math.Min(0.2f, affair.SecretMeetings * 0.02f);

        // High spouse suspicion might force the issue
        if (affair.SpouseSuspicion >= 80)
        {
            divorceChance += 0.2f; // "The secret is out anyway"
        }

        if (random.NextDouble() < divorceChance)
        {
            // They'll leave their spouse for the player!
            return new DivorceResult
            {
                WillDivorce = true,
                Reason = affair.SpouseSuspicion >= 80
                    ? $"{marriedNpc.Name2} says, \"{marriedNpc.SpouseName} found out about us... I've made my choice. I choose you.\""
                    : $"{marriedNpc.Name2} takes your hand. \"I can't live this lie anymore. I'm leaving {marriedNpc.SpouseName} for you.\""
            };
        }

        return new DivorceResult { WillDivorce = false };
    }

    /// <summary>
    /// Execute an NPC leaving their spouse for the player (or becoming player's lover)
    /// </summary>
    public static void ProcessAffairDivorce(NPC npc, Character player, bool becomeSpouse)
    {
        // Check if NPC is dead
        if (npc.IsDead || !npc.IsAlive)
        {
            GD.Print($"[Affair] Cannot process divorce for dead NPC {npc.Name2}");
            return;
        }

        string oldSpouseName = npc.SpouseName;
        string oldSpouseId = NPCMarriageRegistry.Instance.GetSpouseId(npc.ID);

        // Find the old spouse NPC
        var oldSpouse = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.ID == oldSpouseId);

        // Check if old spouse is dead - still process divorce but skip hostility
        bool oldSpouseIsDead = oldSpouse?.IsDead == true || oldSpouse?.IsAlive == false;

        // Divorce the NPC from their current spouse
        npc.Married = false;
        npc.IsMarried = false;
        npc.SpouseName = "";

        if (oldSpouse != null && !oldSpouseIsDead)
        {
            oldSpouse.Married = false;
            oldSpouse.IsMarried = false;
            oldSpouse.SpouseName = "";

            // Jilted spouse becomes hostile to player
            oldSpouse.Brain?.Memory?.AddMemory($"{player.Name} stole my spouse {npc.Name2}!", "betrayal", DateTime.Now);

            // Relationship with player tanks - set to hostile
            RelationshipSystem.UpdateRelationship(player, oldSpouse, -8, 1, false, true); // Force to hostile
        }

        // Clear the marriage registry
        NPCMarriageRegistry.Instance.EndMarriage(npc.ID);
        NPCMarriageRegistry.Instance.ClearAffair(npc.ID, player.ID);

        // Now handle the new relationship with the player
        if (becomeSpouse)
        {
            // Marry the player to the NPC
            npc.Married = true;
            npc.IsMarried = true;
            npc.SpouseName = player.Name;
            npc.MarriedTimes++;

            player.Married = true;
            player.IsMarried = true;
            player.SpouseName = npc.Name2;
            player.MarriedTimes++;

            // Update RomanceTracker
            RomanceTracker.Instance?.AddSpouse(npc.ID);

            // Generate wedding news (dramatic remarriage after affair)
            NewsSystem.Instance?.WriteNews(
                GameConfig.NewsCategory.Marriage,
                $"Scandal and Romance! {npc.Name2} left {oldSpouseName} and immediately married {player.Name}!"
            );

            GD.Print($"[Affair] {npc.Name2} left {oldSpouseName} and married {player.Name}!");
        }
        else
        {
            // Become lovers instead
            RomanceTracker.Instance?.AddLover(npc.ID, 75, false);

            // Generate drama news
            NewsSystem.Instance?.WriteNews(
                GameConfig.NewsCategory.Marriage,
                $"Scandal! {npc.Name2} has left {oldSpouseName} for the adventurer {player.Name}!"
            );

            GD.Print($"[Affair] {npc.Name2} left {oldSpouseName} for {player.Name} (lovers)!");
        }
    }

    /// <summary>
    /// Sociable NPCs develop friendships with compatible others over time.
    /// Called every world sim tick per NPC.
    /// </summary>
    private static void ProcessFriendshipDevelopment(NPC npc, List<NPC> others)
    {
        if (npc.IsDead || npc.Personality == null || npc.Brain == null) return;

        // Sociability drives how often NPCs seek friendship (1-5% per tick)
        float friendChance = 0.01f + npc.Personality.Sociability * 0.04f;
        if (random.NextDouble() > friendChance) return;

        // Pick a random living NPC that isn't self, isn't spouse
        var candidates = others.Where(o =>
            o != npc && !o.IsDead && o.Brain != null && o.Personality != null
            && o.Name != npc.SpouseName).ToList();
        if (candidates.Count == 0) return;

        var target = candidates[random.Next(candidates.Count)];

        // Compatibility based on personality similarity
        float compat = 0f;

        // Similar loyalty = bonding
        compat += 1f - Math.Abs(npc.Personality.Loyalty - target.Personality!.Loyalty);
        // Similar courage
        compat += 1f - Math.Abs(npc.Personality.Courage - target.Personality.Courage);
        // Both sociable = more likely
        compat += (npc.Personality.Sociability + target.Personality.Sociability) * 0.5f;
        // Same class = camaraderie
        if (npc.Class == target.Class) compat += 0.5f;
        // Opposite aggression = clash
        compat -= Math.Abs(npc.Personality.Aggression - target.Personality.Aggression) * 0.5f;

        compat /= 4f; // Normalize to roughly 0-1

        if (compat > 0.4f && random.NextDouble() < compat)
        {
            // Positive interaction — compliment, help, share drink
            var types = new[] { InteractionType.Complimented, InteractionType.Helped, InteractionType.SharedDrink };
            var interType = types[random.Next(types.Length)];
            npc.Brain.RecordInteraction(target, interType);
            target.Brain?.RecordInteraction(npc, interType);
        }
    }

    /// <summary>
    /// Aggressive or vengeful NPCs develop rivalries with incompatible others.
    /// Called every world sim tick per NPC.
    /// </summary>
    private static void ProcessEnemyRelationships(NPC npc)
    {
        if (npc.IsDead || npc.Personality == null || npc.Brain?.Memory == null) return;

        // Vengeful/aggressive NPCs hold grudges and pick fights (1-4% per tick)
        float rivalChance = 0.01f + (npc.Personality.Aggression + npc.Personality.Vengefulness) * 0.015f;
        if (random.NextDouble() > rivalChance) return;

        // Look at existing negative impressions and deepen them
        var impressions = npc.Brain.Memory.CharacterImpressions;
        var enemies = impressions.Where(kvp => kvp.Value < -0.1f).ToList();

        if (enemies.Count > 0)
        {
            // Vengeful NPCs brood on existing enemies — deepen the grudge
            var worst = enemies.OrderBy(e => e.Value).First();

            // Find the actual NPC to record a proper interaction
            var targetNpc = NPCSpawnSystem.Instance?.GetNPCByName(worst.Key);
            if (targetNpc != null && !targetNpc.IsDead && targetNpc.Brain != null)
            {
                npc.Brain.RecordInteraction(targetNpc, InteractionType.Threatened);
                // Target may retaliate if also aggressive
                if (targetNpc.Personality != null && targetNpc.Personality.Aggression > 0.4f)
                    targetNpc.Brain.RecordInteraction(npc, InteractionType.Insulted);
            }
        }
    }
    
    #endregion
    
    #region Data Classes
    
    public class ShoppingGoal
    {
        public string Type { get; set; }
        public float Priority { get; set; }
    }
    
    public class GangInfo
    {
        public int Size { get; set; }
        public bool IsNPCOnly { get; set; }
    }
    
    public class GangWarResult
    {
        public string Gang1 { get; set; }
        public string Gang2 { get; set; }
        public string Outcome { get; set; }
        public List<BattleResult> Battles { get; set; } = new();
    }
    
    public class BattleResult
    {
        public string Fighter1 { get; set; }
        public string Fighter2 { get; set; }
        public int Winner { get; set; } // 1 or 2
        public int Rounds { get; set; }
    }

    #endregion
}

// Affair and marriage data classes - outside the static class for accessibility

public class AffairResult
{
    public bool Success { get; set; }
    public AffairMilestone Milestone { get; set; } = AffairMilestone.None;
    public string Message { get; set; } = "";
    public bool SpouseNoticed { get; set; }
}

public class DivorceResult
{
    public bool WillDivorce { get; set; }
    public string Reason { get; set; } = "";
}

public enum AffairMilestone
{
    None,
    Flirting,           // Just started flirting
    EmotionalConnection, // Deep conversations, growing feelings
    SecretRendezvous,    // Secret meetings
    BecameLovers,        // Full affair
    LeftSpouse           // Divorced for the player
}

public class AffairState
{
    public string MarriedNpcId { get; set; } = "";
    public string SeducerId { get; set; } = ""; // Player ID
    public int AffairProgress { get; set; } = 0; // 0-200 scale
    public int SecretMeetings { get; set; } = 0;
    public int SpouseSuspicion { get; set; } = 0; // 0-100
    public bool IsActive { get; set; } = false; // Full affair status
    public DateTime LastInteraction { get; set; } = DateTime.Now;
}

/// <summary>
/// Singleton registry for tracking NPC-NPC marriages and player affairs
/// </summary>
public class NPCMarriageRegistry
{
    private static NPCMarriageRegistry? _instance;
    public static NPCMarriageRegistry Instance => _instance ??= new NPCMarriageRegistry();

    // NPC ID -> Spouse NPC ID
    private Dictionary<string, string> marriages = new();

    // Married NPC ID -> AffairState (player seducing them)
    private Dictionary<string, AffairState> affairs = new();

    public void RegisterMarriage(string npc1Id, string npc2Id, string npc1Name, string npc2Name)
    {
        marriages[npc1Id] = npc2Id;
        marriages[npc2Id] = npc1Id;

        GD.Print($"[MarriageRegistry] Registered marriage: {npc1Name} <-> {npc2Name}");
    }

    public void EndMarriage(string npcId)
    {
        if (marriages.TryGetValue(npcId, out var spouseId))
        {
            marriages.Remove(npcId);
            marriages.Remove(spouseId);
            GD.Print($"[MarriageRegistry] Ended marriage for {npcId}");
        }
    }

    public string? GetSpouseId(string npcId)
    {
        return marriages.TryGetValue(npcId, out var spouseId) ? spouseId : null;
    }

    public bool IsMarriedToNPC(string npcId)
    {
        return marriages.ContainsKey(npcId);
    }

    public AffairState GetOrCreateAffair(string marriedNpcId, string seducerId)
    {
        var key = $"{marriedNpcId}:{seducerId}";
        if (!affairs.TryGetValue(key, out var affair))
        {
            affair = new AffairState
            {
                MarriedNpcId = marriedNpcId,
                SeducerId = seducerId
            };
            affairs[key] = affair;
        }
        return affair;
    }

    public AffairState? GetAffair(string marriedNpcId, string seducerId)
    {
        var key = $"{marriedNpcId}:{seducerId}";
        return affairs.TryGetValue(key, out var affair) ? affair : null;
    }

    public void ClearAffair(string marriedNpcId, string seducerId)
    {
        var key = $"{marriedNpcId}:{seducerId}";
        affairs.Remove(key);
    }

    /// <summary>
    /// Get all current NPC-NPC marriages for saving
    /// </summary>
    public List<NPCMarriageData> GetAllMarriages()
    {
        var result = new List<NPCMarriageData>();
        var processed = new HashSet<string>();

        foreach (var kvp in marriages)
        {
            if (!processed.Contains(kvp.Key) && !processed.Contains(kvp.Value))
            {
                result.Add(new NPCMarriageData
                {
                    Npc1Id = kvp.Key,
                    Npc2Id = kvp.Value
                });
                processed.Add(kvp.Key);
                processed.Add(kvp.Value);
            }
        }

        return result;
    }

    /// <summary>
    /// Get all affairs for saving
    /// </summary>
    public List<AffairState> GetAllAffairs()
    {
        return affairs.Values.ToList();
    }

    /// <summary>
    /// Restore marriages from save data
    /// </summary>
    public void RestoreMarriages(List<NPCMarriageData>? data)
    {
        marriages.Clear();
        if (data == null) return;

        foreach (var marriage in data)
        {
            marriages[marriage.Npc1Id] = marriage.Npc2Id;
            marriages[marriage.Npc2Id] = marriage.Npc1Id;
        }
    }

    /// <summary>
    /// Restore affairs from save data
    /// </summary>
    public void RestoreAffairs(List<AffairState>? data)
    {
        affairs.Clear();
        if (data == null) return;

        foreach (var affair in data)
        {
            var key = $"{affair.MarriedNpcId}:{affair.SeducerId}";
            affairs[key] = affair;
        }
    }

    /// <summary>
    /// Reset for new game
    /// </summary>
    public void Reset()
    {
        marriages.Clear();
        affairs.Clear();
        GD.Print("[MarriageRegistry] Reset for new game");
    }
}

public class NPCMarriageData
{
    public string Npc1Id { get; set; } = "";
    public string Npc2Id { get; set; } = "";
} 
