using UsurperRemake.Utils;
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
        EstablishmentStatus["AuctionHouse"] = true;
        EstablishmentStatus["Church"] = true;
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
        expenses += GameConfig.BaseCourtMaintenance;

        return expenses;
    }
    
    /// <summary>
    /// Calculate daily income (taxes)
    /// </summary>
    public long CalculateDailyIncome()
    {
        // Base income from tax rate (TaxRate * number of taxable NPCs)
        var activeNPCs = UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs;
        int npcCount;
        if (activeNPCs != null && TaxAlignment != GameConfig.TaxAlignment.All)
        {
            // Filter NPCs by tax alignment
            var alignSys = UsurperRemake.Systems.AlignmentSystem.Instance;
            npcCount = 0;
            foreach (var npc in activeNPCs)
            {
                var align = alignSys.GetAlignment(npc);
                // v0.57.0 — Balanced NPCs count as both Good-tax and Evil-tax targets (they walk
                // both paths) and also fit the Neutral bucket since they're not aligned either way.
                bool taxable = TaxAlignment switch
                {
                    GameConfig.TaxAlignment.Good => align == UsurperRemake.Systems.AlignmentSystem.AlignmentType.Good || align == UsurperRemake.Systems.AlignmentSystem.AlignmentType.Holy || align == UsurperRemake.Systems.AlignmentSystem.AlignmentType.Balanced,
                    GameConfig.TaxAlignment.Evil => align == UsurperRemake.Systems.AlignmentSystem.AlignmentType.Evil || align == UsurperRemake.Systems.AlignmentSystem.AlignmentType.Dark || align == UsurperRemake.Systems.AlignmentSystem.AlignmentType.Balanced,
                    GameConfig.TaxAlignment.Neutral => align == UsurperRemake.Systems.AlignmentSystem.AlignmentType.Neutral || align == UsurperRemake.Systems.AlignmentSystem.AlignmentType.Balanced,
                    _ => true
                };
                if (taxable) npcCount++;
            }
        }
        else
        {
            npcCount = Math.Max(GameConfig.PermadeathPopulationFloor, activeNPCs?.Count ?? GameConfig.PermadeathPopulationFloor);
        }
        long baseIncome = (long)(TaxRate * Math.Max(1, npcCount) * GameConfig.KingTaxIncomeMultiplier);

        // Sales tax revenue (actual amount collected today via ProcessSaleTax)
        long salesTaxIncome = DailyTaxRevenue;

        return baseIncome + salesTaxIncome;
    }
    
    /// <summary>
    /// v1.1.13: the day's royal court activities on the stored court (income less expenses into the
    /// treasury, the reign's day, prisoners' time served and release, the magic budget's top-up), as one
    /// guarded court change. Sales tax is not in that income: each sale already paid it into the stored
    /// treasury (CityControlSystem.AddSalesTaxAsync), and the day's takings (DailyTaxRevenue) are only
    /// reported, then start the next day at zero. Returns false (nothing done) when the court kept changing or has no king.
    /// </summary>
    public static async System.Threading.Tasks.Task<bool> ProcessDailyActivitiesAsync(UsurperRemake.Systems.SqlSaveBackend? sql,
        Func<UsurperRemake.Systems.RoyalCourtSaveData, bool>? alsoToday = null, Func<System.Threading.Tasks.Task>? beforeWrite = null)
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null) return false;
        var released = new List<string>();
        bool done = await UsurperRemake.Systems.OnlineStateManager.ApplyCourtChangeAsync(sql, court =>
        {
            released = ApplyDailyActivities(court);
            return alsoToday == null || alsoToday(court);
        }, beforeWrite);
        if (!done) return false;

        // Reset daily tax revenue accumulators for the next day
        var now = CastleLocation.GetCurrentKing();
        if (now != null)
        {
            now.DailyTaxRevenue = 0;
            now.DailyCityTaxRevenue = 0;
        }
        // Also clear a released NPC's prison state
        foreach (var prisonerId in released)
        {
            var npc = UsurperRemake.Systems.NPCSpawnSystem.Instance?.GetNPCByName(prisonerId, includeDead: true);
            if (npc != null)
            {
                npc.DaysInPrison = 0;
                npc.CurrentLocation = "MainStreet";
            }
        }
        return true;
    }

    /// <summary>v1.1.13: the day's court activities applied to a court record; returns the prisoners released.</summary>
    internal static List<string> ApplyDailyActivities(UsurperRemake.Systems.RoyalCourtSaveData court)
    {
        // v1.1.13: citizen tax only; the day's sales tax went into the treasury sale by sale
        var income = DailyIncomeOf(court, 0);
        var expenses = DailyExpensesOf(court);

        // Ensure treasury doesn't go negative
        court.Treasury = Math.Max(0, court.Treasury + income - expenses);

        court.TotalReign++;

        // Process prisoner time served; release prisoners who have served their time
        var released = new List<string>();
        foreach (var prisoner in court.Prisoners)
        {
            prisoner.DaysServed++;
            if (prisoner.DaysServed >= prisoner.Sentence) released.Add(prisoner.CharacterName);
        }
        court.Prisoners.RemoveAll(p => released.Contains(p.CharacterName));

        // Replenish magic budget from treasury (up to cap)
        long magicReplenish = Math.Min(GameConfig.DailyMagicReplenishment, court.Treasury);
        magicReplenish = Math.Min(magicReplenish, GameConfig.MaxMagicBudget - court.MagicBudget);
        if (magicReplenish > 0)
        {
            court.MagicBudget += magicReplenish;
            court.Treasury -= magicReplenish;
        }
        return released;
    }

    /// <summary>v1.1.13: CalculateDailyExpenses for a court record.</summary>
    internal static long DailyExpensesOf(UsurperRemake.Systems.RoyalCourtSaveData court)
    {
        long expenses = 0;
        foreach (var guard in court.Guards) expenses += guard.DailySalary;
        foreach (var monster in court.MonsterGuards) expenses += monster.DailyFeedingCost;
        expenses += court.Orphans.Count * GameConfig.OrphanCareCost;
        expenses += GameConfig.BaseCourtMaintenance;
        return expenses;
    }

    /// <summary>v1.1.13: CalculateDailyIncome for a court record (its tax rate and alignment).</summary>
    internal static long DailyIncomeOf(UsurperRemake.Systems.RoyalCourtSaveData court, long salesTaxIncome)
    {
        var probe = new King { TaxRate = court.TaxRate, TaxAlignment = (GameConfig.TaxAlignment)court.TaxAlignment, DailyTaxRevenue = salesTaxIncome };
        return probe.CalculateDailyIncome();
    }

    /// <summary>
    /// Add an NPC guard to a king being set up (v1.1.13: no cost; a reigning court hires through the
    /// court-record form below, which pays from the stored treasury in the same change)
    /// </summary>
    public bool AddGuard(string guardName, CharacterAI ai, CharacterSex sex, long salary)
    {
        if (Guards.Count >= MaxNPCGuards)
            return false;

        Guards.Add(new RoyalGuard
        {
            Name = guardName,
            AI = ai,
            Sex = sex,
            DailySalary = salary,
            RecruitmentDate = DateTime.Now
        });
        return true;
    }

    /// <summary>v1.1.13: hire a guard on a court record: the guard joins and the recruitment cost leaves the treasury together.</summary>
    internal static bool AddGuard(UsurperRemake.Systems.RoyalCourtSaveData court, string guardName, CharacterAI ai, CharacterSex sex, long salary)
    {
        if (court.Guards.Count >= MaxNPCGuards || court.Treasury < GameConfig.GuardRecruitmentCost)
            return false;
        court.Guards.Add(new UsurperRemake.Systems.RoyalGuardSaveData { Name = guardName, AI = (int)ai, Sex = (int)sex, DailySalary = salary, Loyalty = 100, IsActive = true });
        court.Treasury -= GameConfig.GuardRecruitmentCost;
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
    /// Add a monster guard - monsters cost more but are stronger. v1.1.13: on a court record, the monster
    /// joins and its cost leaves the treasury together.
    /// </summary>
    internal static bool AddMonsterGuard(UsurperRemake.Systems.RoyalCourtSaveData court, string monsterName, int level, long purchaseCost)
    {
        if (court.MonsterGuards.Count >= MaxMonsterGuards)
            return false;

        // Monster cost scales with level and count
        long actualCost = purchaseCost + (court.MonsterGuards.Count * 500);

        if (court.Treasury < actualCost)
            return false;

        // Stats scale quadratically at higher levels to make endgame guards formidable
        long guardHP = 200 + (level * 50) + (level > 30 ? (level - 30) * 80 : 0);
        long guardStr = 30 + (level * 5) + (level > 30 ? (level - 30) * 4 : 0);
        long guardDef = 20 + (level * 3) + (level > 30 ? (level - 30) * 3 : 0);
        int guardWeapPow = 15 + level * 3;
        int guardArmPow = 10 + level * 2;

        court.MonsterGuards.Add(new UsurperRemake.Systems.MonsterGuardSaveData
        {
            Name = monsterName,
            Level = level,
            HP = guardHP,
            MaxHP = guardHP,
            Strength = guardStr,
            Defence = guardDef,
            WeapPow = guardWeapPow,
            ArmPow = guardArmPow,
            PurchaseCost = actualCost,
            DailyFeedingCost = 50 + (level * 10) + (level > 30 ? (level - 30) * 20 : 0)
        });
        court.Treasury -= actualCost;

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
    public static King CreateNewKing(string name, CharacterAI ai, CharacterSex sex, List<RoyalOrphan>? inheritedOrphans = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = "Unknown Ruler";

        // Pick up any orphaned children that were flagged while no king existed
        var orphans = inheritedOrphans ?? new List<RoyalOrphan>();
        var king = new King
        {
            Name = name,
            AI = ai,
            Sex = sex,
            Treasury = GameConfig.DefaultRoyalTreasury,
            TaxRate = GameConfig.DefaultTaxRateNew,
            TaxAlignment = GameConfig.TaxAlignment.All,
            KingTaxPercent = 5,
            CityTaxPercent = 2,
            CoronationDate = DateTime.Now,
            TotalReign = 0,
            Orphans = orphans.Concat(WorldSimulator.OrphanedChildrenToPickUp(orphans)).ToList()
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
/// Royal orphan under crown protection.
/// Real orphans (IsRealOrphan=true) come from FamilySystem when both parents die.
/// Generated orphans (IsRealOrphan=false) are manually adopted via the orphanage UI.
/// </summary>
public class RoyalOrphan
{
    public string Name { get; set; } = "";
    public int Age { get; set; } = 10;
    public CharacterSex Sex { get; set; } = CharacterSex.Male;
    public DateTime ArrivalDate { get; set; } = DateTime.Now;
    public string BackgroundStory { get; set; } = "";
    public int Happiness { get; set; } = 50;          // 0-100, affects kingdom morale

    // Parent tracking (populated for real orphans)
    public string? MotherName { get; set; }
    public string? FatherName { get; set; }
    public string? MotherID { get; set; }
    public string? FatherID { get; set; }
    public CharacterRace Race { get; set; } = CharacterRace.Human;
    public DateTime BirthDate { get; set; } = DateTime.Now;
    public int Soul { get; set; } = 0;                // From Child.Soul — affects class at 18
    public bool IsRealOrphan { get; set; } = false;   // true = from FamilySystem, false = manually adopted

    /// <summary>
    /// Computed age from BirthDate using NPC lifecycle rate (same as NPC aging).
    /// Only meaningful for real orphans — generated orphans use static Age field.
    /// </summary>
    public int ComputedAge => (int)((DateTime.Now - BirthDate).TotalHours / GameConfig.NpcLifecycleHoursPerYear);
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
        ("Iron Golem", 25, 20000L),
        // High-tier guards — endgame throne defense
        ("Elder Drake", 40, 100000L),
        ("Abyssal Fiend", 55, 250000L),
        ("Storm Titan", 70, 500000L),
        ("Void Wyrm", 85, 1000000L),
        ("Champion of Maelketh", 100, 5000000L)
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
