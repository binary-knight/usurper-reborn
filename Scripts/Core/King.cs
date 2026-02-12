using UsurperRemake.Utils;
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// King - Pascal-compatible royal court system
/// Based on KingRec from INIT.PAS and CASTLE.PAS
/// </summary>
public class King
{
    // Basic King Information (Pascal KingRec)
    public string Name { get; set; } = "";                    // King's name
    public CharacterAI AI { get; set; } = CharacterAI.Human;  // 'H' for human, 'N' for NPC
    public CharacterSex Sex { get; set; } = CharacterSex.Male; // King's gender

    // Royal Treasury (Pascal: treasury)
    public long Treasury { get; set; } = GameConfig.DefaultRoyalTreasury;

    // Tax System (Pascal: tax, taxalignment)
    public long TaxRate { get; set; } = 20;                    // Daily tax amount (default 20 gold per citizen)
    public GameConfig.TaxAlignment TaxAlignment { get; set; } = GameConfig.TaxAlignment.All;

    // City control tax share (percentage of sales that goes to city-controlling team)
    public int CityTaxPercent { get; set; } = 2;              // Default 2% of all sales

    // King's sales tax (percentage of every sale that goes to the royal treasury)
    public int KingTaxPercent { get; set; } = 5;              // Default 5% of all sales (0-25%)

    // Actual tax revenue collected today (reset each day in ProcessDailyActivities)
    public long DailyTaxRevenue { get; set; } = 0;

    // City controller tax revenue collected today (for dashboard tracking)
    public long DailyCityTaxRevenue { get; set; } = 0;

    // Royal Guard System (Pascal: guard, guardpay, guardai, guardsex arrays)
    // Max 5 NPC guards + 5 monster guards
    public const int MaxNPCGuards = 5;
    public const int MaxMonsterGuards = 5;
    public List<RoyalGuard> Guards { get; set; } = new();
    public List<MonsterGuard> MonsterGuards { get; set; } = new();
    
    // Royal Orders and Establishments (Pascal: various establishment controls)
    public Dictionary<string, bool> EstablishmentStatus { get; set; } = new();
    public string LastProclamation { get; set; } = "";
    public DateTime LastProclamationDate { get; set; } = DateTime.MinValue;
    
    // Royal Court State
    public bool IsActive { get; set; } = true;                 // Is there currently a king?
    public DateTime CoronationDate { get; set; } = DateTime.Now;
    public long TotalReign { get; set; } = 0;                  // Days as ruler
    
    // Prison System Integration
    public Dictionary<string, PrisonRecord> Prisoners { get; set; } = new();
    
    // Royal Orphanage (Pascal: royal orphanage system)
    public List<RoyalOrphan> Orphans { get; set; } = new();
    
    // Court Magic System
    public List<string> AvailableSpells { get; set; } = new();
    public long MagicBudget { get; set; } = 10000;

    // Defense Alert System - notify human guards when throne is challenged
    public PendingDefenseEvent? ActiveDefenseEvent { get; set; }

    // Political Systems
    public List<CourtMember> CourtMembers { get; set; } = new();
    public List<CourtIntrigue> ActivePlots { get; set; } = new();
    public List<RoyalHeir> Heirs { get; set; } = new();
    public RoyalSpouse? Spouse { get; set; }
    public string? DesignatedHeir { get; set; }

    public King()
    {
        InitializeDefaultEstablishments();
        InitializeDefaultSpells();
    }
    
    /// <summary>
    /// Initialize default establishment statuses
    /// </summary>
    private void InitializeDefaultEstablishments()
    {
        EstablishmentStatus["Inn"] = true;
        EstablishmentStatus["WeaponShop"] = true;
        EstablishmentStatus["ArmorShop"] = true;
        EstablishmentStatus["Bank"] = true;
        EstablishmentStatus["MagicShop"] = true;
        EstablishmentStatus["Healer"] = true;
        EstablishmentStatus["Marketplace"] = true;
        EstablishmentStatus["Church"] = true;
    }
    
    /// <summary>
    /// Initialize default court spells
    /// </summary>
    private void InitializeDefaultSpells()
    {
        AvailableSpells.Add("Royal Blessing");
        AvailableSpells.Add("Detect Evil");
        AvailableSpells.Add("Summon Guards");
        AvailableSpells.Add("Treasury Insight");
        AvailableSpells.Add("Divine Protection");
    }
    
    /// <summary>
    /// Get the royal title based on gender
    /// </summary>
    public string GetTitle()
    {
        return Sex == CharacterSex.Male ? "King" : "Queen";
    }
    
    /// <summary>
    /// Calculate daily expenses for the royal court
    /// </summary>
    public long CalculateDailyExpenses()
    {
        long expenses = 0;

        // NPC Guard salaries
        foreach (var guard in Guards)
        {
            expenses += guard.DailySalary;
        }

        // Monster guard feeding costs
        foreach (var monster in MonsterGuards)
        {
            expenses += monster.DailyFeedingCost;
        }

        // Orphan care costs
        expenses += Orphans.Count * GameConfig.OrphanCareCost;

        // Base court maintenance
        expenses += 1000; // Base daily cost

        return expenses;
    }
    
    /// <summary>
    /// Calculate daily income (taxes)
    /// </summary>
    public long CalculateDailyIncome()
    {
        // Base income from tax rate (TaxRate * number of active NPCs)
        int npcCount = UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs?.Count ?? 10;
        long baseIncome = TaxRate * Math.Max(10, npcCount);

        // Sales tax revenue (actual amount collected today via ProcessSaleTax)
        long salesTaxIncome = DailyTaxRevenue;

        return baseIncome + salesTaxIncome;
    }
    
    /// <summary>
    /// Process daily royal court activities
    /// </summary>
    public void ProcessDailyActivities()
    {
        var expenses = CalculateDailyExpenses();
        var income = CalculateDailyIncome();

        Treasury += income - expenses;

        // Reset daily tax revenue accumulators for the next day
        DailyTaxRevenue = 0;
        DailyCityTaxRevenue = 0;
        
        // Ensure treasury doesn't go negative
        if (Treasury < 0)
        {
            Treasury = 0;
        }
        
        TotalReign++;
        
        // Process prisoner time served
        var prisonersToRelease = new List<string>();
        foreach (var prisoner in Prisoners)
        {
            prisoner.Value.DaysServed++;
            if (prisoner.Value.DaysServed >= prisoner.Value.Sentence)
            {
                prisonersToRelease.Add(prisoner.Key);
            }
        }
        
        // Release prisoners who have served their time
        foreach (var prisonerId in prisonersToRelease)
        {
            Prisoners.Remove(prisonerId);
        }
    }
    
    /// <summary>
    /// Add an NPC guard to the royal guard
    /// </summary>
    public bool AddGuard(string guardName, CharacterAI ai, CharacterSex sex, long salary)
    {
        if (Guards.Count >= MaxNPCGuards)
            return false;

        if (Treasury < GameConfig.GuardRecruitmentCost)
            return false;

        var guard = new RoyalGuard
        {
            Name = guardName,
            AI = ai,
            Sex = sex,
            DailySalary = salary,
            RecruitmentDate = DateTime.Now
        };

        Guards.Add(guard);
        Treasury -= GameConfig.GuardRecruitmentCost;

        return true;
    }

    /// <summary>
    /// Remove an NPC guard from the royal guard
    /// </summary>
    public bool RemoveGuard(string guardName)
    {
        var guard = Guards.Find(g => g.Name == guardName);
        if (guard != null)
        {
            Guards.Remove(guard);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Add a monster guard - monsters cost more but are stronger
    /// </summary>
    public bool AddMonsterGuard(string monsterName, int level, long purchaseCost)
    {
        if (MonsterGuards.Count >= MaxMonsterGuards)
            return false;

        // Monster cost scales with level and count
        long actualCost = purchaseCost + (MonsterGuards.Count * 500);

        if (Treasury < actualCost)
            return false;

        var monster = new MonsterGuard
        {
            Name = monsterName,
            Level = level,
            HP = 200 + (level * 50),
            MaxHP = 200 + (level * 50),
            Strength = 30 + (level * 5),
            Defence = 20 + (level * 3),
            PurchaseCost = actualCost,
            DailyFeedingCost = 50 + (level * 10),
            AcquiredDate = DateTime.Now
        };

        MonsterGuards.Add(monster);
        Treasury -= actualCost;

        return true;
    }

    /// <summary>
    /// Remove a monster guard
    /// </summary>
    public bool RemoveMonsterGuard(string monsterName)
    {
        var monster = MonsterGuards.Find(m => m.Name == monsterName);
        if (monster != null)
        {
            MonsterGuards.Remove(monster);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Get total guard count (NPC + Monster)
    /// </summary>
    public int TotalGuardCount => Guards.Count + MonsterGuards.Count;
    
    /// <summary>
    /// Imprison a character
    /// </summary>
    public void ImprisonCharacter(string characterName, int sentence, string crime)
    {
        var prisonRecord = new PrisonRecord
        {
            CharacterName = characterName,
            Crime = crime,
            Sentence = sentence,
            DaysServed = 0,
            ImprisonmentDate = DateTime.Now
        };
        
        Prisoners[characterName] = prisonRecord;
    }
    
    /// <summary>
    /// Release a character from prison (pardon or bail)
    /// </summary>
    public bool ReleaseCharacter(string characterName)
    {
        return Prisoners.Remove(characterName);
    }
    
    /// <summary>
    /// Create a new king (abdication or succession)
    /// </summary>
    public static King CreateNewKing(string name, CharacterAI ai, CharacterSex sex)
    {
        var king = new King
        {
            Name = name,
            AI = ai,
            Sex = sex,
            Treasury = GameConfig.DefaultRoyalTreasury,
            TaxRate = 20,
            TaxAlignment = GameConfig.TaxAlignment.All,
            KingTaxPercent = 5,
            CityTaxPercent = 2,
            CoronationDate = DateTime.Now,
            TotalReign = 0
        };
        
        return king;
    }
}

/// <summary>
/// Royal Guard record
/// </summary>
public class RoyalGuard
{
    public string Name { get; set; } = "";
    public CharacterAI AI { get; set; } = CharacterAI.Human;
    public CharacterSex Sex { get; set; } = CharacterSex.Male;
    public long DailySalary { get; set; } = GameConfig.BaseGuardSalary;
    public DateTime RecruitmentDate { get; set; } = DateTime.Now;
    public int Loyalty { get; set; } = 100;           // 0-100, affects guard performance
    public bool IsActive { get; set; } = true;        // Can be temporarily deactivated
}

/// <summary>
/// Prison record for royal justice system
/// </summary>
public class PrisonRecord
{
    public string CharacterName { get; set; } = "";
    public string Crime { get; set; } = "";
    public int Sentence { get; set; } = 1;            // Days in prison
    public int DaysServed { get; set; } = 0;
    public DateTime ImprisonmentDate { get; set; } = DateTime.Now;
    public long BailAmount { get; set; } = 0;         // 0 = no bail allowed
}

/// <summary>
/// Royal orphan under crown protection
/// </summary>
public class RoyalOrphan
{
    public string Name { get; set; } = "";
    public int Age { get; set; } = 10;
    public CharacterSex Sex { get; set; } = CharacterSex.Male;
    public DateTime ArrivalDate { get; set; } = DateTime.Now;
    public string BackgroundStory { get; set; } = "";
    public int Happiness { get; set; } = 50;          // 0-100, affects kingdom morale
}

/// <summary>
/// Monster guard - fearsome creatures that protect the throne
/// Challengers must fight through monster guards before NPC guards
/// </summary>
public class MonsterGuard
{
    public string Name { get; set; } = "";
    public int Level { get; set; } = 1;
    public long HP { get; set; } = 200;
    public long MaxHP { get; set; } = 200;
    public long Strength { get; set; } = 30;
    public long Defence { get; set; } = 20;
    public long WeapPow { get; set; } = 20;           // Natural attack power
    public long ArmPow { get; set; } = 15;            // Natural armor
    public string MonsterType { get; set; } = "";     // Family type (Dragon, Undead, etc.)
    public long PurchaseCost { get; set; } = 1000;
    public long DailyFeedingCost { get; set; } = 50;
    public DateTime AcquiredDate { get; set; } = DateTime.Now;
    public bool IsAlive => HP > 0;
}

/// <summary>
/// Available monster types for purchase as guards
/// </summary>
public static class MonsterGuardTypes
{
    public static readonly (string Name, int Level, long Cost)[] AvailableMonsters = new[]
    {
        ("War Hound", 5, 2000L),
        ("Cave Troll", 10, 5000L),
        ("Giant Spider", 8, 3500L),
        ("Dire Wolf", 7, 3000L),
        ("Stone Golem", 15, 8000L),
        ("Hellhound", 12, 6000L),
        ("Manticore", 18, 12000L),
        ("Wyvern", 20, 15000L),
        ("Basilisk", 16, 10000L),
        ("Iron Golem", 25, 20000L)
    };
}

/// <summary>
/// Pending defense event - when the throne is challenged and human guards need to be summoned
/// </summary>
public class PendingDefenseEvent
{
    public string ChallengerName { get; set; } = "";
    public int ChallengerLevel { get; set; }
    public DateTime EventTime { get; set; } = DateTime.Now;
    public bool PlayerNotified { get; set; } = false;
    public bool PlayerResponded { get; set; } = false;
    public int TicksRemaining { get; set; } = 2;  // Combat delayed for 2 ticks
    public string ChallengerTeam { get; set; } = "";

    /// <summary>
    /// Check if event has expired (player didn't respond in time)
    /// </summary>
    public bool IsExpired => TicksRemaining <= 0;
}

/// <summary>
/// Court faction - political groups with different goals and influence
/// </summary>
public enum CourtFaction
{
    None,
    Loyalists,      // Support current king unconditionally
    Reformists,     // Want change through lawful means
    Militarists,    // Believe strength should rule
    Merchants,      // Economic interests above all
    Faithful        // Religious interests and traditions
}

/// <summary>
/// Court member - advisor, noble, or other influential court figure
/// </summary>
public class CourtMember
{
    public string Name { get; set; } = "";
    public CourtFaction Faction { get; set; } = CourtFaction.None;
    public int Influence { get; set; } = 50;         // 0-100, political power
    public int LoyaltyToKing { get; set; } = 50;     // 0-100, how loyal to current ruler
    public string Role { get; set; } = "Advisor";   // Advisor, Steward, Marshal, Spymaster, etc.
    public DateTime JoinedCourt { get; set; } = DateTime.Now;
    public bool IsPlotting { get; set; } = false;   // Involved in active intrigue
}

/// <summary>
/// Court intrigue - plots and schemes against the throne
/// </summary>
public class CourtIntrigue
{
    public string PlotType { get; set; } = "";       // Assassination, Coup, Scandal, Sabotage
    public List<string> Conspirators { get; set; } = new();
    public string Target { get; set; } = "";         // Usually the king
    public int Progress { get; set; } = 0;           // 0-100, triggers at 100
    public DateTime StartDate { get; set; } = DateTime.Now;
    public bool IsDiscovered { get; set; } = false;
    public string DiscoveredBy { get; set; } = "";
}

/// <summary>
/// Royal heir - potential successor to the throne
/// </summary>
public class RoyalHeir
{
    public string Name { get; set; } = "";
    public int Age { get; set; } = 0;
    public int ClaimStrength { get; set; } = 50;     // 0-100, legitimacy of claim
    public string ParentName { get; set; } = "";
    public CharacterSex Sex { get; set; } = CharacterSex.Male;
    public bool IsDesignated { get; set; } = false;  // Named as official heir
    public DateTime BirthDate { get; set; } = DateTime.Now;

    public bool IsAdult => Age >= 18;
}

/// <summary>
/// Royal spouse - political marriage partner
/// </summary>
public class RoyalSpouse
{
    public string Name { get; set; } = "";
    public CharacterSex Sex { get; set; } = CharacterSex.Female;
    public CourtFaction OriginalFaction { get; set; } = CourtFaction.None;
    public long Dowry { get; set; } = 0;
    public DateTime MarriageDate { get; set; } = DateTime.Now;
    public int Happiness { get; set; } = 50;         // 0-100, affects heir legitimacy
}
