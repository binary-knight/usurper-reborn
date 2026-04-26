using UsurperRemake.Utils;
using System;
using System.Collections.Generic;

/// <summary>
/// Child system based on Pascal CHILDREN.PAS and ChildRec structure
/// Manages child characters, adoption, custody, and family dynamics
/// Maintains perfect Pascal compatibility with original child mechanics
/// </summary>
public class Child
{
    // Basic child information
    public string Name { get; set; } = "";              // child's name
    public string Mother { get; set; } = "";            // mother's name
    public string Father { get; set; } = "";            // father's name
    public string MotherID { get; set; } = "";          // mother's unique ID
    public string FatherID { get; set; } = "";          // father's unique ID
    public string OriginalMother { get; set; } = "";    // biological mother
    public string OriginalFather { get; set; } = "";    // biological father
    public CharacterSex Sex { get; set; }               // child's sex
    public int Age { get; set; }                        // age in years
    public DateTime BirthDate { get; set; }             // birth date
    public bool Named { get; set; }                     // has been named
    public bool Deleted { get; set; }                   // is record deleted
    
    // Child status and location
    public int Location { get; set; }                   // where child lives
    public int Health { get; set; }                     // health status
    public int Soul { get; set; }                       // soul/behavior rating
    public bool MotherAccess { get; set; } = true;      // mother has access
    public bool FatherAccess { get; set; } = true;      // father has access
    
    // Special circumstances
    public bool Kidnapped { get; set; }                 // is kidnapped
    public string KidnapperName { get; set; } = "";     // kidnapper name
    public string KidnapperID { get; set; } = "";       // kidnapper ID
    public long RansomDemanded { get; set; }            // ransom amount
    public string CursedByGod { get; set; } = "";       // which god cursed child
    public string CursedByGodID { get; set; } = "";     // god's unique ID
    public int Royal { get; set; }                      // royal blood (0=no, 1=half, 2=full)
    
    // Parenting interaction tracking.
    // LastParentingDay is kept for backward compatibility with saves prior to
    // v0.57.7 but is no longer used for the cooldown check — it relied on
    // DailySystemManager.Instance.CurrentDay, a process-wide singleton that
    // gets clobbered across concurrent player sessions in MUD mode. Once
    // LastParentingDay caught up to the singleton's value, it stayed >= for
    // every subsequent session until a real-world daily reset ticked, which
    // sometimes never happened — so players reported permanent cooldowns on
    // "spend time with child" (Lumina: "can't spend time with any of them").
    // v0.57.7: switched to wall-clock via LastParentingTime.
    public int LastParentingDay { get; set; }           // legacy; unused post-v0.57.7
    public DateTime LastParentingTime { get; set; }     // UTC time of last parent interaction (cooldown)

    // System tracking
    public int RecordNumber { get; set; }               // file position
    public DateTime LastUpdated { get; set; }           // last update time
    
    /// <summary>
    /// Initialize new child record
    /// Pascal equivalent: New_ChildRecord procedure
    /// </summary>
    public Child()
    {
        Name = "";
        Mother = "";
        Father = "";
        MotherID = "";
        FatherID = "";
        OriginalMother = "";
        OriginalFather = "";
        Sex = CharacterSex.Male;
        Age = 0;
        BirthDate = DateTime.Now;
        Named = false;
        Deleted = false;
        Location = GameConfig.ChildLocationHome;
        Health = GameConfig.ChildHealthNormal;
        Soul = 0;
        LastParentingDay = 0;
        LastParentingTime = DateTime.MinValue;
        MotherAccess = true;
        FatherAccess = true;
        Kidnapped = false;
        KidnapperName = "";
        KidnapperID = "";
        RansomDemanded = 0;
        CursedByGod = "";
        CursedByGodID = "";
        Royal = 0;
        RecordNumber = 0;
        LastUpdated = DateTime.Now;
    }
    
    /// <summary>
    /// Create child from two parents
    /// Pascal equivalent: Create_Child procedure
    /// </summary>
    public static Child CreateChild(Character mother, Character father, bool bastard = false)
    {
        // Inherit parent alignment tendency (scaled to +-100 range)
        int parentInfluence = (int)Math.Clamp((mother.Chivalry - mother.Darkness + father.Chivalry - father.Darkness) / 2L, -100, 100);
        parentInfluence = Math.Clamp(parentInfluence, -100, 100);
        int initialSoul = parentInfluence + Random.Shared.Next(-20, 21);

        var child = new Child
        {
            Mother = mother.Name,
            Father = father.Name,
            MotherID = mother.ID,
            FatherID = father.ID,
            OriginalMother = mother.Name,
            OriginalFather = father.Name,
            Sex = (CharacterSex)Random.Shared.Next(1, 3),
            Age = 0,
            BirthDate = DateTime.Now,
            Named = false,
            Location = GameConfig.ChildLocationHome,
            Health = GameConfig.ChildHealthNormal,
            Soul = Math.Clamp(initialSoul, -500, 500)
        };

        // Inherit some traits from parents
        if (mother.King || father.King)
        {
            child.Royal = mother.King && father.King ? 2 : 1;
        }

        // Bastard children start with soul penalty
        if (bastard)
        {
            child.Soul -= 50;
            child.Soul = Math.Max(-500, child.Soul);
        }

        return child;
    }
    
    /// <summary>
    /// Check if child is adopted
    /// Pascal equivalent: Child_Adopted function
    /// </summary>
    public bool IsAdopted()
    {
        return !string.IsNullOrEmpty(Mother) && Mother != OriginalMother &&
               !string.IsNullOrEmpty(Father) && Father != OriginalFather;
    }
    
    /// <summary>
    /// Get child's location description
    /// Pascal equivalent: Child_Location_String function
    /// </summary>
    public string GetLocationDescription()
    {
        return Location switch
        {
            GameConfig.ChildLocationHome => "home",
            GameConfig.ChildLocationOrphanage => "Royal Orphanage",
            GameConfig.ChildLocationKidnapped => "Kidnapped",
            _ => "unknown location"
        };
    }
    
    /// <summary>
    /// Get child's health description
    /// Pascal equivalent: Child_Health_String function
    /// </summary>
    public string GetHealthDescription()
    {
        return Health switch
        {
            GameConfig.ChildHealthNormal => "normal",
            GameConfig.ChildHealthPoisoned => "poisoned!",
            GameConfig.ChildHealthCursed => "cursed!",
            GameConfig.ChildHealthDepressed => "*depressed*",
            _ => "unknown condition"
        };
    }
    
    /// <summary>
    /// Get child's soul/behavior description
    /// Pascal equivalent: Child_Soul_String function
    /// </summary>
    public string GetSoulDescription()
    {
        return Soul switch
        {
            <= -250 => "evil",
            <= -100 => "naughty",
            <= -25 => "mischievous",
            <= 24 => "normal",
            <= 100 => "well-behaved",
            <= 250 => "virtuous",
            _ => "angel-heart"
        };
    }
    
    /// <summary>
    /// Get child's status marks (bastard, royal, etc.)
    /// Pascal equivalent: Child_Marks function
    /// </summary>
    public string GetStatusMarks()
    {
        var marks = new List<string>();
        
        if (IsAdopted())
        {
            marks.Add("Adopted");
        }
        
        if (Royal == 2)
        {
            marks.Add("Full Royal Blood");
        }
        else if (Royal == 1)
        {
            marks.Add("Half Royal Blood");
        }
        
        if (Kidnapped)
        {
            marks.Add("Kidnapped");
        }
        
        if (!string.IsNullOrEmpty(CursedByGod))
        {
            marks.Add($"Cursed by {CursedByGod}");
        }
        
        if (Health == GameConfig.ChildHealthDepressed)
        {
            marks.Add("Depressed from Divorce");
        }
        
        return marks.Count > 0 ? string.Join(", ", marks) : "";
    }
    
    /// <summary>
    /// Generate newborn child name
    /// Pascal equivalent: Give_Me_Newborn_Childname function
    /// </summary>
    public string GenerateNewbornName()
    {
        if (Named) return Name;

        string[] maleNames = { "Alexander", "Benjamin", "Christopher", "Daniel", "Edward", "Frederick", "Gabriel", "Henry", "Isaac", "James" };
        string[] femaleNames = { "Alice", "Beatrice", "Catherine", "Diana", "Elizabeth", "Florence", "Grace", "Helena", "Isabella", "Jane" };

        if (Sex == CharacterSex.Male)
        {
            Name = maleNames[Random.Shared.Next(maleNames.Length)];
        }
        else
        {
            Name = femaleNames[Random.Shared.Next(femaleNames.Length)];
        }
        
        Named = true;
        return Name;
    }
    
    /// <summary>
    /// Check if character is parent of this child
    /// Pascal equivalent: My_Child function
    /// </summary>
    public bool IsParent(Character character)
    {
        return (FatherID == character.ID && Father == character.Name) ||
               (MotherID == character.ID && Mother == character.Name);
    }
    
    /// <summary>
    /// Improve child's soul/behavior
    /// Pascal equivalent: Better_Child_Soul procedure
    /// </summary>
    public void ImproveSoul(int change)
    {
        Soul += change;
        if (Soul > 500) Soul = 500; // Maximum soul value
        LastUpdated = DateTime.Now;
    }
    
    /// <summary>
    /// Worsen child's soul/behavior
    /// Pascal equivalent: Worsen_Child_Soul procedure
    /// </summary>
    public void WorsenSoul(int change)
    {
        Soul -= change;
        if (Soul < -500) Soul = -500; // Minimum soul value
        LastUpdated = DateTime.Now;
    }
    
    /// <summary>
    /// Age up the child (called daily)
    /// </summary>
    public void ProcessDailyAging()
    {
        var daysSinceBirth = (DateTime.Now - BirthDate).Days;
        var newAge = daysSinceBirth / GameConfig.ChildAgeUpDays;
        
        if (newAge > Age)
        {
            Age = newAge;
            LastUpdated = DateTime.Now;
            
            // Random soul changes as child grows
            if (Random.Shared.Next(10) == 0) // 10% chance
            {
                var soulChange = Random.Shared.Next(-5, 6); // -5 to +5
                Soul += soulChange;
                Soul = Math.Max(-500, Math.Min(500, Soul)); // Clamp to valid range
            }
        }
    }
    
    /// <summary>
    /// Handle child custody after divorce
    /// </summary>
    public void HandleDivorceCustody(Character losingParent, Character keepingParent)
    {
        // Losing parent loses access
        if (losingParent.Sex == CharacterSex.Male)
        {
            FatherAccess = false;
        }
        else
        {
            MotherAccess = false;
        }
        
        // Child becomes depressed from divorce
        Health = GameConfig.ChildHealthDepressed;
        WorsenSoul(100); // Divorce trauma affects behavior
        
        LastUpdated = DateTime.Now;
    }
    
    /// <summary>
    /// Full child information display
    /// Pascal equivalent: Child_View procedure
    /// </summary>
    public string GetFullDescription()
    {
        var description = $"{Name} (born {BirthDate:yyyy-MM-dd})\n";
        description += $"Age: {Age} year{(Age != 1 ? "s" : "")}\n";
        description += $"Sex: {(Sex == CharacterSex.Male ? "male" : "female")}\n";
        description += $"Location: {GetLocationDescription()}\n";
        description += $"Health: {GetHealthDescription()}\n";
        description += $"Soul: {GetSoulDescription()}\n";
        
        var marks = GetStatusMarks();
        if (!string.IsNullOrEmpty(marks))
        {
            description += $"Status: {marks}\n";
        }
        
        if (!string.IsNullOrEmpty(Mother))
        {
            description += $"Mother: {Mother}\n";
        }
        
        if (!string.IsNullOrEmpty(Father))
        {
            description += $"Father: {Father}\n";
        }
        
        return description;
    }
}

/// <summary>
/// Bonuses provided by having children
/// Children under 18 provide stat bonuses to their parents
/// </summary>
public class ChildBonuses
{
    public int ChildCount { get; set; }
    public float XPMultiplier { get; set; } = 1.0f;
    public int BonusMaxHP { get; set; }
    public int BonusStrength { get; set; }
    public int BonusCharisma { get; set; }
    public int DailyGoldBonus { get; set; }

    /// <summary>
    /// Get a summary of all bonuses for display
    /// </summary>
    public string GetBonusSummary()
    {
        if (ChildCount == 0)
            return "No children - no family bonuses.";

        var summary = $"Family Bonuses ({ChildCount} child{(ChildCount != 1 ? "ren" : "")}):\n";
        if (BonusMaxHP > 0) summary += $"  +{BonusMaxHP} Max HP\n";
        if (BonusStrength > 0) summary += $"  +{BonusStrength} Strength\n";
        if (BonusCharisma > 0) summary += $"  +{BonusCharisma} Charisma\n";
        if (XPMultiplier > 1.0f) summary += $"  +{(XPMultiplier - 1) * 100:F0}% XP Bonus\n";
        if (DailyGoldBonus > 0) summary += $"  +{DailyGoldBonus} Gold/day\n";
        return summary;
    }
}
