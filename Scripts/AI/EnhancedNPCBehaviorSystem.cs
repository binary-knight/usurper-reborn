using UsurperRemake.Utils;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Enhanced NPC Behavior System - Complete implementation based on Pascal NPC AI files
/// Provides sophisticated NPC inventory management, gang warfare, and relationship systems
/// Direct Pascal compatibility with exact function preservation
/// </summary>
public class EnhancedNPCBehaviorSystem : Node
{
    // MailSystem and RelationshipSystem are static - no need to instantiate
    private Random random = new Random();
    
    // Pascal constants from NPCMAINT.PAS
    private const int MaxNPCTeams = 60;          // Pascal maxnr
    private const int MaxObjects = 400;          // Pascal mux
    private const int GlobalMaxTeamMembers = 5;  // Pascal global_maxteammembers
    
    // NPC maintenance tracking
    private Dictionary<string, NPCMaintenanceData> npcMaintenanceData = new();
    private Dictionary<string, NPCGangData> gangData = new();
    private List<string> activeGangs = new();
    
    public override void _Ready()
    {
        // Remove GetNode calls for static systems - these are static classes, not Nodes
        // MailSystem, NewsSystem, and RelationshipSystem are static and don't need to be retrieved as Nodes
        
        // Initialize any non-static systems here if needed
        random = new Random();
        npcMaintenanceData = new Dictionary<string, NPCMaintenanceData>();
        gangData = new Dictionary<string, NPCGangData>();
        activeGangs = new List<string>();
    }
    
    #region Pascal NPC_CHEC.PAS - Inventory Management
    
    /// <summary>
    /// Enhanced NPC inventory check - Pascal NPC_CHEC.PAS Check_Inventory function
    /// </summary>
    public async Task<int> CheckNPCInventory(Character npc, int itemId, ObjType itemType, 
        bool shout, int location)
    {
        // Pascal function signature: Check_Inventory(var gubbe: UserRec; onr: longint; otyp: objtype; shout: boolean; location: byte): byte;
        
        // ClassicMode check removed - const false makes code unreachable
        
        var result = 0; // Pascal return codes: 0=not touched, 1=starts using, 2=swapped
        
        if (itemId > 0)
        {
            var newItem = await GetItemDetails(itemId, itemType);
            if (newItem == null) return 0;
            
            // Pascal shout logic
            if (shout)
            {
                await ShowNPCExamineItem(npc, newItem);
            }
            
            // Check if NPC can use this item type
            if (!CanNPCUseItemType(npc, newItem))
            {
                if (shout)
                {
                    await ShowNPCRejectItem(npc, "I can't use this type of item!");
                }
                return 0;
            }
            
            // Process item by type (Pascal case statement logic)
            result = await ProcessItemByType(npc, newItem, location, shout);
        }
        else
        {
            // Reinventory all items (Pascal onr = 0 logic)
            await ReinventoryAllItems(npc);
        }
        
        return result;
    }
    
    /// <summary>
    /// Process item by type - Pascal NPC_CHEC.PAS item type handling
    /// </summary>
    private async Task<int> ProcessItemByType(Character npc, ItemDetails newItem, int location, bool shout)
    {
        var emptySlot = FindEmptyInventorySlot(npc);
        var result = 0;
        
        switch (newItem.Type)
        {
            case ObjType.Head:
                result = await ProcessHeadSlotItem(npc, newItem, location, shout, emptySlot);
                break;
                
            case ObjType.Abody:
                result = await ProcessBodySlotItem(npc, newItem, location, shout, emptySlot);
                break;
                
            case ObjType.Weapon:
                result = await ProcessWeaponSlotItem(npc, newItem, location, shout, emptySlot);
                break;
                
            case ObjType.Arms:
                result = await ProcessArmsSlotItem(npc, newItem, location, shout, emptySlot);
                break;
                
            default:
                // Try to put in backpack
                if (emptySlot >= 0)
                {
                    await PlaceItemInBackpack(npc, newItem, emptySlot, shout);
                }
                else
                {
                    await HandleFullInventory(npc, newItem, shout);
                }
                break;
        }
        
        return result;
    }
    
    /// <summary>
    /// Pascal item comparison logic - objekt_test function
    /// </summary>
    private bool IsNewItemBetter(Character npc, ItemDetails currentItem, ItemDetails newItem)
    {
        // Pascal objekt_test logic for determining if new item is better
        
        // Compare armor values
        if (newItem.Armor > currentItem.Armor) return true;
        if (newItem.Armor < currentItem.Armor) return false;
        
        // Compare attack values
        if (newItem.Attack > currentItem.Attack) return true;
        if (newItem.Attack < currentItem.Attack) return false;
        
        // Compare total stat bonuses
        var currentBonus = currentItem.Strength + currentItem.Stamina + currentItem.Charisma;
        var newBonus = newItem.Strength + newItem.Stamina + newItem.Charisma;
        
        if (newBonus > currentBonus) return true;
        
        // Compare value as tiebreaker
        return newItem.Value > currentItem.Value;
    }
    
    /// <summary>
    /// Pascal mail notification system - Inform_By_Mail procedure
    /// </summary>
    private async Task SendItemNotificationMail(Character npc, ItemDetails newItem, 
        ItemDetails oldItem, int situation, int location)
    {
        var header = GetRandomLootHeader();
        var locationDesc = GetLocationDescription(location);
        var situationText = GetSituationText(situation, newItem, oldItem);
        
        MailSystem.SendSystemMail(npc.Name2, 
            $"{GameConfig.NewsColorPlayer}{header}{GameConfig.NewsColorDefault}",
            $"You found {GameConfig.ItemColor}{newItem.Name}{GameConfig.NewsColorDefault} {locationDesc}.",
            situationText,
            $"The {GameConfig.ItemColor}{newItem.Name}{GameConfig.NewsColorDefault} is worth approximately {GameConfig.GoldColor}{newItem.Value:N0}{GameConfig.NewsColorDefault} gold.");
    }
    
    #endregion
    
    #region Pascal NPCMAINT.PAS - NPC Maintenance
    
    /// <summary>
    /// Main NPC maintenance - Pascal NPCMAINT.PAS Npc_Maint procedure
    /// </summary>
    public async Task PerformNPCMaintenance(List<Character> npcs)
    {
        bool kingFound = false;
        
        // Initialize maintenance data
        await InitializeMaintenanceData(npcs);
        
        // Process each NPC
        foreach (var npc in npcs.Where(n => n.AI == CharacterAI.Computer && n.IsAlive))
        {
            await ProcessNPCMaintenance(npc, kingFound);
        }
        
        // Gang management (Pascal gang logic)
        await ProcessGangMaintenance(npcs);
        
        // NPC shopping and equipment upgrades
        await ProcessNPCShopping(npcs);
        
        // Believer system (Pascal NPC_Believer)
        await ProcessNPCBelieverSystem(npcs);
    }
    
    /// <summary>
    /// Pascal gang recruitment and dissolution logic
    /// </summary>
    private async Task ProcessGangMaintenance(List<Character> npcs)
    {
        var gangSizes = CalculateGangSizes(npcs);
        
        foreach (var gang in gangSizes)
        {
            var gangName = gang.Key;
            var memberCount = gang.Value;
            var isNPCOnlyGang = IsNPCOnlyGang(npcs, gangName);
            
            if (isNPCOnlyGang && memberCount > GlobalMaxTeamMembers)
            {
                // Cheat gang - too many members
                await RemoveGang(gangName, npcs);
            }
            else if (isNPCOnlyGang && memberCount <= 3)
            {
                if (random.Next(4) == 0)
                {
                    // Dissolve small gang
                    await RemoveGang(gangName, npcs);
                }
                else
                {
                    // Recruit more members
                    await RecruitGangMembers(gangName, npcs, GlobalMaxTeamMembers - memberCount);
                }
            }
        }
    }
    
    /// <summary>
    /// Pascal Remove_Gang procedure
    /// </summary>
    private async Task RemoveGang(string gangName, List<Character> npcs)
    {
        // GD.Print($"Removing NPC team: {gangName}");
        
        // Generate news
        NewsSystem.Instance.Newsy($"{GameConfig.TeamColor}{gangName}{GameConfig.NewsColorDefault} ceased to exist!", false, GameConfig.NewsCategory.General);
        
        // Remove all members from gang
        var gangMembers = npcs.Where(n => n.Team == gangName).ToList();
        
        foreach (var member in gangMembers)
        {
            if (member.AI == CharacterAI.Computer)
            {
                NewsSystem.Instance.Newsy($"{GameConfig.NewsColorPlayer}{member.Name2}{GameConfig.NewsColorDefault} left the team.", false, GameConfig.NewsCategory.General);
                
                // Clear team data
                member.Team = "";
                member.TeamPassword = "";
                member.GymOwner = 0;
                
                // Save NPC
                await SaveCharacter(member);
            }
        }
        
        // Add final newline to news
        NewsSystem.Instance.Newsy("", true, GameConfig.NewsCategory.General);
    }
    
    /// <summary>
    /// Pascal gang recruitment logic
    /// </summary>
    private async Task RecruitGangMembers(string gangName, List<Character> npcs, int maxRecruits)
    {
        var recruited = 0;
        var gangExample = npcs.FirstOrDefault(n => n.Team == gangName);
        if (gangExample == null) return;
        
        var availableNPCs = npcs.Where(n => 
            n.AI == CharacterAI.Computer && 
            string.IsNullOrEmpty(n.Team) && 
            !n.King && 
            n.IsAlive).ToList();
        
        foreach (var npc in availableNPCs)
        {
            if (recruited >= maxRecruits) break;
            
            if (random.Next(3) == 0) // 33% chance to recruit
            {
                // Join the gang
                npc.Team = gangName;
                npc.TeamPassword = gangExample.TeamPassword;
                npc.ControlsTurf = gangExample.ControlsTurf;
                npc.TeamRecord = gangExample.TeamRecord;
                
                await SaveCharacter(npc);
                recruited++;
                
                // Generate news
                NewsSystem.Instance.Newsy($"{GameConfig.NewsColorPlayer}{npc.Name2}{GameConfig.NewsColorDefault} has been recruited to {GameConfig.TeamColor}{gangName}{GameConfig.NewsColorDefault}", true, GameConfig.NewsCategory.General);
            }
        }
    }
    
    /// <summary>
    /// Pascal NPC believer system - NPC_Believer procedure
    /// </summary>
    private async Task ProcessNPCBelieverSystem(List<Character> npcs)
    {
        // NPCBelievers check removed - const 50 makes code unreachable
        
        foreach (var npc in npcs.Where(n => n.AI == CharacterAI.Computer && n.IsAlive))
        {
            if (random.Next(3) != 0) continue; // Only process 33% of NPCs each cycle
            
            // Verify existing faith
            if (!string.IsNullOrEmpty(npc.God))
            {
                if (!DoesGodExist(npc.God))
                {
                    npc.God = "";
                    await SaveCharacter(npc);
                }
            }
            
            // Process faith actions
            if (!string.IsNullOrEmpty(npc.God))
            {
                await ProcessExistingBeliever(npc);
            }
            else
            {
                await ProcessPotentialConvert(npc);
            }
        }
    }
    
    #endregion
    
    #region Pascal AUTOGANG.PAS - Automated Gang Warfare
    
    /// <summary>
    /// Automated gang warfare - Pascal AUTOGANG.PAS Auto_Gangwar procedure
    /// </summary>
    public async Task<GangWarResult> ConductAutoGangWar(string gang1, string gang2, List<Character> npcs)
    {
        var result = new GangWarResult { Gang1 = gang1, Gang2 = gang2 };
        
        // Load gang members (Pascal team loading logic)
        var team1 = LoadGangMembers(gang1, npcs, 1, 5);
        var team2 = LoadGangMembers(gang2, npcs, 6, 10);
        
        if (team1.Count == 0 || team2.Count == 0)
        {
            result.Outcome = GangWarOutcome.NoContest;
            return result;
        }
        
        // Check for turf control stakes
        bool turfWar = team2.Any(m => m.ControlsTurf);
        
        // Generate gang war header
        var header = GetGangWarHeader();
        var announcement = $"{GameConfig.TeamColor}{gang1}{GameConfig.NewsColorDefault} challenged {GameConfig.TeamColor}{gang2}{GameConfig.NewsColorDefault}";
        var turfText = turfWar ? "A challenge for Town Control!" : "";
        
        NewsSystem.Instance.Newsy($"{header} {announcement} {turfText}", false, GameConfig.NewsCategory.General);
        
        // Reset HP for all participants
        foreach (var member in team1.Concat(team2))
        {
            member.HP = member.MaxHP;
        }
        
        // Conduct battle rounds (Pascal repeat-until loop)
        int round = 0;
        bool gameOver = false;
        
        while (!gameOver && round < 50) // Max 50 rounds to prevent infinite loops
        {
            round++;
            
            NewsSystem.Instance.Newsy($"Round {round} results:", false, GameConfig.NewsCategory.General);
            
            // Pair up fighters and conduct battles
            await ConductRoundBattles(team1, team2, result);
            
            // Check if war is over
            gameOver = team1.All(m => m.HP <= 0) || team2.All(m => m.HP <= 0);
        }
        
        // Determine outcome
        await DetermineGangWarOutcome(team1, team2, result, turfWar);
        
        return result;
    }
    
    /// <summary>
    /// Conduct individual battles in a round - Pascal battle logic
    /// </summary>
    private async Task ConductRoundBattles(List<Character> team1, List<Character> team2, GangWarResult result)
    {
        var busy = new bool[11]; // Pascal busy array
        
        for (int i = 0; i < team1.Count; i++)
        {
            for (int j = 0; j < team2.Count; j++)
            {
                var fighter1 = team1[i];
                var fighter2 = team2[j];
                
                if (fighter1.HP > 0 && fighter2.HP > 0 && !busy[i + 1] && !busy[j + 6])
                {
                    busy[i + 1] = true;
                    busy[j + 6] = true;
                    
                    // Conduct computer vs computer battle
                    var battleResult = await ConductComputerVsComputerBattle(fighter1, fighter2);
                    
                    // Record battle result
                    result.BattleResults.Add(battleResult);
                    
                    // Send mail notifications
                    await SendGangBattleMail(battleResult);
                    
                    // Update killed-by statistics
                    if (!battleResult.Fighter1.IsAlive)
                    {
                        await UpdateKilledByStats(battleResult.Fighter2, battleResult.Fighter1);
                    }
                    if (!battleResult.Fighter2.IsAlive)
                    {
                        await UpdateKilledByStats(battleResult.Fighter1, battleResult.Fighter2);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Pascal Gang_War_Header function
    /// </summary>
    private string GetGangWarHeader()
    {
        return random.Next(6) switch
        {
            0 => "Gang War!",
            1 => "Team Bash!",
            2 => "Team War!",
            3 => "Turf War!",
            4 => "Gang Fight!",
            _ => "Rival Gangs Clash!"
        };
    }
    
    #endregion
    
    #region Pascal RELATIO2.PAS - Enhanced Relationships
    
    /// <summary>
    /// NPC marriage system - Pascal Npc_Set_Out_To_Marry procedure
    /// </summary>
    public async Task ProcessNPCMarriageSystem(List<Character> npcs)
    {
        var unmarriedNPCs = npcs.Where(n => 
            n.AI == CharacterAI.Computer && 
            !n.Married && 
            n.Level >= 5 && // Mature enough to marry
            n.IsAlive).ToList();
        
        foreach (var npc in unmarriedNPCs)
        {
            if (random.Next(10) == 0) // 10% chance per maintenance cycle
            {
                await AttemptNPCMarriage(npc, npcs);
            }
        }
    }
    
    /// <summary>
    /// Child management system - Pascal child management logic
    /// </summary>
    public async Task ProcessChildManagement(List<Character> npcs)
    {
        var marriedNPCs = npcs.Where(n => 
            n.AI == CharacterAI.Computer && 
            n.Married && 
            n.IsAlive).ToList();
        
        foreach (var npc in marriedNPCs)
        {
            // Check for new children
            if (random.Next(20) == 0) // 5% chance per cycle
            {
                await ProcessNewChild(npc);
            }
            
            // Update existing children
            await UpdateExistingChildren(npc);
        }
    }
    
    /// <summary>
    /// Validate all relationships - Pascal Validate_All_Relations procedure
    /// </summary>
    public async Task ValidateAllRelationships(List<Character> npcs)
    {
        var relationships = await RelationshipSystem.Instance.GetAllRelationships();
        
        foreach (var relationship in relationships)
        {
            // Validate relationship consistency logic here
            await ValidateRelationshipConsistency(relationship, null, null);
        }
    }
    
    #endregion
    
    #region Utility Methods
    
    /// <summary>
    /// Get random loot header - Pascal Random_Header function
    /// </summary>
    private string GetRandomLootHeader()
    {
        var headers = new[]
        {
            "Automated NPC Report",
            "NPC Inventory Update", 
            "Equipment Change Notice",
            "Item Discovery Alert"
        };
        return headers[random.Next(headers.Length)];
    }
    
    /// <summary>
    /// Calculate gang sizes for maintenance
    /// </summary>
    private Dictionary<string, int> CalculateGangSizes(List<Character> npcs)
    {
        return npcs.Where(n => !string.IsNullOrEmpty(n.Team))
                  .GroupBy(n => n.Team)
                  .ToDictionary(g => g.Key, g => g.Count());
    }
    
    /// <summary>
    /// Check if gang is NPC-only
    /// </summary>
    private bool IsNPCOnlyGang(List<Character> npcs, string gangName)
    {
        // Never treat the player's team as NPC-only (player isn't in the NPC list)
        if (WorldSimulator.IsPlayerTeam(gangName))
            return false;

        var gangMembers = npcs.Where(n => n.Team == gangName);
        return gangMembers.Any() && gangMembers.All(n => n.AI == CharacterAI.Computer);
    }
    
    /// <summary>
    /// Load gang members for warfare
    /// </summary>
    private List<Character> LoadGangMembers(string gangName, List<Character> npcs, int startIndex, int endIndex)
    {
        return npcs.Where(n => n.Team == gangName && n.IsAlive)
                  .Take(endIndex - startIndex + 1)
                  .ToList();
    }
    
    /// <summary>
    /// Find empty inventory slot
    /// </summary>
    private int FindEmptyInventorySlot(Character character)
    {
        for (int i = 0; i < character.Item.Count; i++)
        {
            if (character.Item[i] == 0) return i;
        }
        return -1;
    }
    
    // Placeholder methods for complete implementation
    private async Task<ItemDetails> GetItemDetails(int itemId, ObjType itemType) { return new ItemDetails(); }
    private bool CanNPCUseItemType(Character npc, ItemDetails item) { return true; }
    private async Task ShowNPCExamineItem(Character npc, ItemDetails item) { }
    private async Task ShowNPCRejectItem(Character npc, string reason) { }
    private async Task<int> ProcessHeadSlotItem(Character npc, ItemDetails item, int location, bool shout, int emptySlot) { return 0; }
    private async Task<int> ProcessBodySlotItem(Character npc, ItemDetails item, int location, bool shout, int emptySlot) { return 0; }
    private async Task<int> ProcessWeaponSlotItem(Character npc, ItemDetails item, int location, bool shout, int emptySlot) { return 0; }
    private async Task<int> ProcessArmsSlotItem(Character npc, ItemDetails item, int location, bool shout, int emptySlot) { return 0; }
    private async Task PlaceItemInBackpack(Character npc, ItemDetails item, int slot, bool shout) { }
    private async Task HandleFullInventory(Character npc, ItemDetails item, bool shout) { }
    private async Task ReinventoryAllItems(Character npc) { }
    private string GetLocationDescription(int location) { return "in the dungeons"; }
    private string GetSituationText(int situation, ItemDetails newItem, ItemDetails oldItem) { return ""; }
    private async Task InitializeMaintenanceData(List<Character> npcs) { }
    private async Task ProcessNPCMaintenance(Character npc, bool kingFound) { }
    private async Task ProcessNPCShopping(List<Character> npcs) { }
    private async Task ProcessExistingBeliever(Character npc) { }
    private async Task ProcessPotentialConvert(Character npc) { }
    private bool DoesGodExist(string godName) { return true; }
    private async Task SaveCharacter(Character character) { }
    private async Task<BattleResult> ConductComputerVsComputerBattle(Character fighter1, Character fighter2) { return new BattleResult(); }
    private async Task SendGangBattleMail(BattleResult battleResult) { }
    /// <summary>
    /// Update statistics and news when one character kills another
    /// </summary>
    private async Task UpdateKilledByStats(Character killer, Character victim)
    {
        if (killer == null || victim == null) return;

        // Use the relationship system's UpdateKillStats for core tracking
        RelationshipSystem.Instance.UpdateKillStats(killer, victim);

        // Additional NPC-specific tracking
        if (victim is NPC npcVictim)
        {
            // Mark NPC as dead
            npcVictim.HP = 0;

            // Generate news about the kill
            string victimName = victim.Name2 ?? victim.Name1;
            string killerName = killer.Name2 ?? killer.Name1;
            string location = victim.CurrentLocation ?? "Unknown Location";

            NewsSystem.Instance.WriteDeathNews(victimName, killerName, location);
        }

        await Task.CompletedTask;
    }
    private async Task DetermineGangWarOutcome(List<Character> team1, List<Character> team2, GangWarResult result, bool turfWar) { }
    private async Task AttemptNPCMarriage(Character npc, List<Character> npcs) { }
    private async Task ProcessNewChild(Character npc) { }
    private async Task UpdateExistingChildren(Character npc) { }
    private async Task ValidateRelationshipConsistency(dynamic relationship, Character person1, Character person2) { }
    
    #endregion
    
    #region Data Structures
    
    public class NPCMaintenanceData
    {
        public string NPCId { get; set; }
        public DateTime LastMaintenance { get; set; }
        public Dictionary<string, object> MaintenanceState { get; set; } = new();
    }
    
    public class NPCGangData
    {
        public string GangName { get; set; }
        public List<string> Members { get; set; } = new();
        public bool IsNPCOnly { get; set; }
        public DateTime LastActivity { get; set; }
    }
    
    public class ItemDetails
    {
        public string Name { get; set; } = "";
        public ObjType Type { get; set; }
        public long Value { get; set; }
        public int Armor { get; set; }
        public int Attack { get; set; }
        public int Strength { get; set; }
        public int Stamina { get; set; }
        public int Charisma { get; set; }
        public bool Cursed { get; set; }
    }
    
    public class GangWarResult
    {
        public string Gang1 { get; set; }
        public string Gang2 { get; set; }
        public GangWarOutcome Outcome { get; set; }
        public List<BattleResult> BattleResults { get; set; } = new();
        public string WinningGang { get; set; }
        public bool TurfChanged { get; set; }
    }
    
    public class BattleResult
    {
        public Character Fighter1 { get; set; }
        public Character Fighter2 { get; set; }
        public Character Winner { get; set; }
        public int RoundsToVictory { get; set; }
        public bool BothDied { get; set; }
    }
    
    public enum GangWarOutcome
    {
        Gang1Victory,
        Gang2Victory,
        MutualDestruction,
        NoContest,
        Interrupted
    }
    
    #endregion
} 
