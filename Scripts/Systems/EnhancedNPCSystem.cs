using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Enhanced NPC System - Phase 21 Implementation
/// Builds upon existing NPC AI with Pascal-compatible behaviors
/// </summary>
public class EnhancedNPCSystem
{
    private NewsSystem newsSystem;
    private Random random = new Random();
    
    public EnhancedNPCSystem()
    {
        newsSystem = NewsSystem.Instance;
    }
    
    /// <summary>
    /// Enhanced NPC inventory management - Pascal NPC_CHEC.PAS
    /// </summary>
    public int CheckNPCInventory(NPC npc, int itemId, bool shout = false)
    {
        // ClassicMode check removed - const false makes code unreachable
        
        var result = 0; // 0=not touched, 1=equipped, 2=swapped
        
        if (itemId > 0)
        {
            // NPC examines new item
            if (shout)
            {
            }
            
            // Check if NPC should equip this item
            result = ProcessNPCItemDecision(npc, itemId, shout);
        }
        
        return result;
    }
    
    /// <summary>
    /// NPC shopping AI - Pascal NPCMAINT.PAS shopping logic
    /// </summary>
    public bool ProcessNPCShopping(NPC npc)
    {
        if (npc.Gold < 100) return false;
        
        var shoppingGoals = DetermineShoppingNeeds(npc);
        if (shoppingGoals.Count == 0) return false;
        
        foreach (var goal in shoppingGoals)
        {
            if (AttemptNPCPurchase(npc, goal))
            {
                RecordNPCPurchase(npc, goal);
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Gang maintenance system - Pascal NPCMAINT.PAS gang logic
    /// </summary>
    public void ProcessGangMaintenance(List<NPC> npcs)
    {
        var gangData = AnalyzeGangs(npcs);
        
        foreach (var gang in gangData)
        {
            if (gang.Value.IsNPCOnly && gang.Value.MemberCount <= 3)
            {
                if (random.Next(4) == 0)
                {
                    DissolveNPCGang(gang.Key, npcs);
                }
                else
                {
                    RecruitToGang(gang.Key, npcs);
                }
            }
        }
    }
    
    /// <summary>
    /// NPC believer system - Pascal NPCMAINT.PAS NPC_Believer
    /// </summary>
    public void ProcessNPCBelieverSystem(NPC npc)
    {
        // NPCBelievers check removed - const 50 makes code unreachable
        
        if (random.Next(3) != 0) return; // Only 33% processed per cycle
        
        if (string.IsNullOrEmpty(npc.God))
        {
            // Potential conversion
            if (random.Next(10) == 0)
            {
                AttemptNPCConversion(npc);
            }
        }
        else
        {
            // Process existing believer
            ProcessExistingNPCBeliever(npc);
        }
    }
    
    /// <summary>
    /// Automated gang warfare - Pascal AUTOGANG.PAS
    /// </summary>
    public void InitiateAutoGangWar(string gang1, string gang2, List<NPC> npcs)
    {
        var team1 = npcs.Where(n => n.Team == gang1 && n.IsAlive).ToList();
        var team2 = npcs.Where(n => n.Team == gang2 && n.IsAlive).ToList();
        
        if (team1.Count == 0 || team2.Count == 0) return;
        
        var header = GetGangWarHeader();
        newsSystem.Newsy($"{header}: {GameConfig.TeamColor}{gang1}{GameConfig.NewsColorDefault} challenged {GameConfig.TeamColor}{gang2}{GameConfig.NewsColorDefault}",
            false, GameConfig.NewsCategory.General);
        
        ConductGangBattles(team1, team2);
    }
    
    private int ProcessNPCItemDecision(NPC npc, int itemId, bool shout)
    {
        // Simplified item evaluation
        var currentWeaponValue = npc.WeaponPower;
        var newItemValue = itemId * 10; // Placeholder
        
        if (newItemValue > currentWeaponValue * 1.2f)
        {
            if (shout)
            {
                // GD.Print($"{npc.Name2} starts to use the new item.");
            }
            
            // Send mail notification
            SendItemNotificationMail(npc, "new item", "Dungeon");
            
            return 2; // Swapped equipment
        }
        
        return 0; // No change
    }
    
    private List<string> DetermineShoppingNeeds(NPC npc)
    {
        var needs = new List<string>();
        
        // Pascal Ok_To_Buy logic
        if (npc.WeaponPower < npc.Level * 20)
            needs.Add("weapon");
            
        if (npc.ArmorClass < npc.Level * 15)
            needs.Add("armor");
            
        if (npc.HP < npc.MaxHP * 0.7f)
            needs.Add("healing");
            
        return needs;
    }
    
    private bool AttemptNPCPurchase(NPC npc, string itemType)
    {
        var cost = CalculateItemCost(itemType, npc.Level);
        
        if (npc.Gold >= cost)
        {
            npc.Gold -= cost;
            return true;
        }
        
        return false;
    }
    
    private void RecordNPCPurchase(NPC npc, string itemType)
    {
        npc.Memory?.AddMemory($"I bought a {itemType}", "purchase", DateTime.Now);
    }
    
    private Dictionary<string, GangInfo> AnalyzeGangs(List<NPC> npcs)
    {
        return npcs.Where(n => !string.IsNullOrEmpty(n.Team))
                  .GroupBy(n => n.Team)
                  .ToDictionary(g => g.Key, g => new GangInfo
                  {
                      MemberCount = g.Count(),
                      IsNPCOnly = g.All(n => n.AI == CharacterAI.Computer)
                  });
    }
    
    private void DissolveNPCGang(string gangName, List<NPC> npcs)
    {
        newsSystem.Newsy($"{GameConfig.TeamColor}{gangName}{GameConfig.NewsColorDefault} ceased to exist!",
            false, GameConfig.NewsCategory.General);
        
        foreach (var member in npcs.Where(n => n.Team == gangName))
        {
            member.Team = "";
            member.ControlsTurf = false;
            member.TeamPassword = "";
        }
    }
    
    private void RecruitToGang(string gangName, List<NPC> npcs)
    {
        var candidates = npcs.Where(n => 
            string.IsNullOrEmpty(n.Team) && 
            !n.King && 
            n.IsAlive).Take(2).ToList();
        
        foreach (var candidate in candidates)
        {
            if (random.Next(3) == 0)
            {
                candidate.Team = gangName;
                newsSystem.Newsy($"Gang Recruit: {GameConfig.NewsColorPlayer}{candidate.Name2}{GameConfig.NewsColorDefault} has been recruited to {GameConfig.TeamColor}{gangName}{GameConfig.NewsColorDefault}", true, GameConfig.NewsCategory.General);
            }
        }
    }
    
    private void AttemptNPCConversion(NPC npc)
    {
        var gods = new[] { "Nosferatu", "Darkcloak", "Druid" };
        if (gods.Length > 0)
        {
            npc.God = gods[random.Next(gods.Length)];
            // GD.Print($"[Faith] {npc.Name2} converted to {npc.God}");
        }
    }
    
    private void ProcessExistingNPCBeliever(NPC npc)
    {
        // Pascal believer actions
        var actions = new[] { "pray", "sacrifice", "preach" };
        var action = actions[random.Next(actions.Length)];
        
        npc.Memory?.AddMemory($"I performed {action} for {npc.God}", "faith", DateTime.Now);
    }
    
    private string GetGangWarHeader()
    {
        var headers = new[] { "Gang War!", "Team Bash!", "Team War!", "Turf War!", "Gang Fight!" };
        return headers[random.Next(headers.Length)];
    }
    
    private void ConductGangBattles(List<NPC> team1, List<NPC> team2)
    {
        for (int i = 0; i < Math.Min(team1.Count, team2.Count); i++)
        {
            var fighter1 = team1[i];
            var fighter2 = team2[i];
            
            // Simplified battle
            var winner = (fighter1.Level + random.Next(20)) > (fighter2.Level + random.Next(20)) 
                ? fighter1 : fighter2;
            var loser = winner == fighter1 ? fighter2 : fighter1;
            
            newsSystem.Newsy($"{winner.Name2} defeated {loser.Name2}", false, GameConfig.NewsCategory.General);
            
            loser.HP = 1; // Near death
        }
    }
    
    private void SendItemNotificationMail(NPC npc, string itemName, string location)
    {
        var headers = new[] { "Gold and Glory!", "Loot!", "Looting Party!", "New Stuff!" };
        var header = headers[random.Next(headers.Length)];
        
        MailSystem.SendMail(
            npc.Name2,
            header,
            $"You found {itemName} in {location}. You started to use it immediately."
        );
    }
    
    private int CalculateItemCost(string itemType, int level)
    {
        return itemType switch
        {
            "weapon" => level * 100 + random.Next(50, 200),
            "armor" => level * 80 + random.Next(40, 160),
            "healing" => level * 20 + random.Next(10, 40),
            _ => 100
        };
    }
    
    private class GangInfo
    {
        public int MemberCount { get; set; }
        public bool IsNPCOnly { get; set; }
    }
} 
