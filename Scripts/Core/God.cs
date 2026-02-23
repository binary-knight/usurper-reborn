using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// God class - Complete Pascal-compatible implementation of GodRec from INITGODS.PAS
/// Represents an immortal character with divine powers and believers
/// Based on Pascal source: INITGODS.PAS, VARGODS.PAS, GODWORLD.PAS
/// </summary>
public class God
{
    // Core God Properties (Pascal GodRec fields)
    public string RealName { get; set; } = "";        // realname: s30 - real (user/bbs) name
    public string Name { get; set; } = "";             // Name: s30 - alias
    public string Id { get; set; } = "";               // id: s15 - unique ID tag
    public int RecordNumber { get; set; } = 1;         // recnr: SmallWord - rec # in file
    public int Age { get; set; } = 2;                  // age: SmallWord - age
    public int Sex { get; set; } = 1;                  // sex: byte - 1=male, 2=female
    public char AI { get; set; } = GameConfig.GodAIHuman; // ai: char - 'H'uman or 'C'omputer
    public int Level { get; set; } = 1;                // level: SmallWord - level
    public long Experience { get; set; } = 1;          // exp: longint - experience, power
    public int DeedsLeft { get; set; } = GameConfig.DefaultGodDeedsLeft; // deedsleft: SmallWord
    public bool Deleted { get; set; } = false;         // deleted: boolean
    public int Believers { get; set; } = 0;            // believers: SmallWord - # of worshippers
    public long Darkness { get; set; } = 0;            // darkness: longint - dark points
    public long Goodness { get; set; } = 0;            // goodness: longint - good points
    
    // Additional properties for C# implementation
    public DateTime LastActive { get; set; } = DateTime.Now;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public List<string> Disciples { get; set; } = new List<string>();
    public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    
    /// <summary>
    /// Default constructor - creates empty god
    /// </summary>
    public God()
    {
        // Initialize with default values
    }
    
    /// <summary>
    /// Constructor for new god based on Pascal Become_God procedure
    /// </summary>
    public God(string userName, string alias, string playerId, int playerSex, long playerDarkness, long playerGoodness)
    {
        RealName = userName;
        Name = alias;
        Id = playerId;
        RecordNumber = 1;
        Age = new Random().Next(GameConfig.MinGodAge, GameConfig.MaxGodAge + 1); // random(5) + 2
        Sex = playerSex;
        AI = GameConfig.GodAIHuman;
        Level = 1;
        Experience = 1;
        DeedsLeft = GameConfig.DefaultGodDeedsLeft;
        Deleted = false;
        Believers = 0;
        Darkness = playerDarkness;  // follows from player
        Goodness = playerGoodness;  // follows from player
        CreatedDate = DateTime.Now;
        LastActive = DateTime.Now;
    }
    
    /// <summary>
    /// Calculate god level based on experience (Pascal God_Level_Raise function)
    /// </summary>
    public int CalculateLevel()
    {
        if (Experience > GameConfig.GodLevel9Experience) return 9;
        if (Experience > GameConfig.GodLevel8Experience) return 8;
        if (Experience > GameConfig.GodLevel7Experience) return 7;
        if (Experience > GameConfig.GodLevel6Experience) return 6;
        if (Experience > GameConfig.GodLevel5Experience) return 5;
        if (Experience > GameConfig.GodLevel4Experience) return 4;
        if (Experience > GameConfig.GodLevel3Experience) return 3;
        if (Experience > GameConfig.GodLevel2Experience) return 2;
        return 1;
    }
    
    /// <summary>
    /// Get god title based on level (Pascal God_Title function)
    /// </summary>
    public string GetTitle()
    {
        int actualLevel = CalculateLevel();
        if (actualLevel >= 1 && actualLevel <= GameConfig.MaxGodLevel)
        {
            return GameConfig.GodTitles[Math.Clamp(actualLevel - 1, 0, GameConfig.GodTitles.Length - 1)];
        }
        return "Lesser Spirit";
    }
    
    /// <summary>
    /// Check if god is active (Pascal God_Active function)
    /// </summary>
    public bool IsActive()
    {
        return !Deleted && !string.IsNullOrEmpty(Name);
    }
    
    /// <summary>
    /// Increase god experience (Pascal IncGodExp procedure)
    /// </summary>
    public void IncreaseExperience(long amount)
    {
        if (amount > 0)
        {
            Experience += amount;
            Level = CalculateLevel(); // Update level based on new experience
        }
    }
    
    /// <summary>
    /// Calculate sacrifice power return (Pascal Sacrifice_Gold_Return function)
    /// </summary>
    public static long CalculateSacrificeReturn(long goldAmount)
    {
        if (goldAmount <= GameConfig.SacrificeGoldTier1Max) return 1;
        if (goldAmount <= GameConfig.SacrificeGoldTier2Max) return 2;
        if (goldAmount <= GameConfig.SacrificeGoldTier3Max) return 3;
        if (goldAmount <= GameConfig.SacrificeGoldTier4Max) return 4;
        if (goldAmount <= GameConfig.SacrificeGoldTier5Max) return 5;
        if (goldAmount <= GameConfig.SacrificeGoldTier6Max) return 6;
        if (goldAmount <= GameConfig.SacrificeGoldTier7Max) return 7;
        return 8; // Maximum power return
    }
    
    /// <summary>
    /// Add a believer to this god
    /// </summary>
    public void AddBeliever(string characterName)
    {
        if (!Disciples.Contains(characterName))
        {
            Disciples.Add(characterName);
            Believers = Disciples.Count;
        }
    }
    
    /// <summary>
    /// Remove a believer from this god
    /// </summary>
    public void RemoveBeliever(string characterName)
    {
        if (Disciples.Contains(characterName))
        {
            Disciples.Remove(characterName);
            Believers = Disciples.Count;
        }
    }
    
    /// <summary>
    /// Use a divine deed
    /// </summary>
    public bool UseDeed()
    {
        if (DeedsLeft > 0)
        {
            DeedsLeft--;
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Reset daily deeds (Pascal God_Maintenance procedure)
    /// </summary>
    public void ResetDailyDeeds()
    {
        DeedsLeft = GameConfig.DefaultGodDeedsLeft;
        // Give believer experience
        IncreaseExperience(Believers);
        LastActive = DateTime.Now;
    }
    
    /// <summary>
    /// Get formatted god status for display
    /// </summary>
    public string GetStatusDisplay()
    {
        return $"Name: {Name}\n" +
               $"Rank: {GetTitle()} (Level {Level})\n" +
               $"Believers: {Believers}\n" +
               $"Deeds Left: {DeedsLeft}\n" +
               $"Power: {Experience}\n" +
               $"Age: {Age}\n" +
               $"Active: {(IsActive() ? "Yes" : "No")}";
    }
    
    /// <summary>
    /// Create a copy of this god
    /// </summary>
    public God Clone()
    {
        var clone = new God
        {
            RealName = this.RealName,
            Name = this.Name,
            Id = this.Id,
            RecordNumber = this.RecordNumber,
            Age = this.Age,
            Sex = this.Sex,
            AI = this.AI,
            Level = this.Level,
            Experience = this.Experience,
            DeedsLeft = this.DeedsLeft,
            Deleted = this.Deleted,
            Believers = this.Believers,
            Darkness = this.Darkness,
            Goodness = this.Goodness,
            LastActive = this.LastActive,
            CreatedDate = this.CreatedDate,
            Disciples = new List<string>(this.Disciples),
            Properties = new Dictionary<string, object>(this.Properties)
        };
        return clone;
    }
    
    /// <summary>
    /// Validate god data
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(Name) && 
               !string.IsNullOrEmpty(RealName) && 
               !string.IsNullOrEmpty(Id) &&
               Level >= 1 && Level <= GameConfig.MaxGodLevel &&
               Experience >= 0 &&
               Age >= GameConfig.MinGodAge &&
               (Sex == 1 || Sex == 2) &&
               (AI == GameConfig.GodAIHuman || AI == GameConfig.GodAIComputer);
    }
    
    /// <summary>
    /// Convert to dictionary for serialization
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["RealName"] = RealName,
            ["Name"] = Name,
            ["Id"] = Id,
            ["RecordNumber"] = RecordNumber,
            ["Age"] = Age,
            ["Sex"] = Sex,
            ["AI"] = AI.ToString(),
            ["Level"] = Level,
            ["Experience"] = Experience,
            ["DeedsLeft"] = DeedsLeft,
            ["Deleted"] = Deleted,
            ["Believers"] = Believers,
            ["Darkness"] = Darkness,
            ["Goodness"] = Goodness,
            ["LastActive"] = LastActive.ToBinary(),
            ["CreatedDate"] = CreatedDate.ToBinary(),
            ["Disciples"] = Disciples.ToArray(),
            ["Properties"] = Properties
        };
    }
    
    /// <summary>
    /// Create god from dictionary
    /// </summary>
    public static God FromDictionary(Dictionary<string, object> dict)
    {
        var god = new God();
        
        if (dict.ContainsKey("RealName")) god.RealName = dict["RealName"].ToString() ?? "";
        if (dict.ContainsKey("Name")) god.Name = dict["Name"].ToString() ?? "";
        if (dict.ContainsKey("Id")) god.Id = dict["Id"].ToString() ?? "";
        if (dict.ContainsKey("RecordNumber")) god.RecordNumber = Convert.ToInt32(dict["RecordNumber"]);
        if (dict.ContainsKey("Age")) god.Age = Convert.ToInt32(dict["Age"]);
        if (dict.ContainsKey("Sex")) god.Sex = Convert.ToInt32(dict["Sex"]);
        if (dict.ContainsKey("AI")) god.AI = (dict["AI"].ToString() ?? "N")[0];
        if (dict.ContainsKey("Level")) god.Level = Convert.ToInt32(dict["Level"]);
        if (dict.ContainsKey("Experience")) god.Experience = Convert.ToInt64(dict["Experience"]);
        if (dict.ContainsKey("DeedsLeft")) god.DeedsLeft = Convert.ToInt32(dict["DeedsLeft"]);
        if (dict.ContainsKey("Deleted")) god.Deleted = Convert.ToBoolean(dict["Deleted"]);
        if (dict.ContainsKey("Believers")) god.Believers = Convert.ToInt32(dict["Believers"]);
        if (dict.ContainsKey("Darkness")) god.Darkness = Convert.ToInt64(dict["Darkness"]);
        if (dict.ContainsKey("Goodness")) god.Goodness = Convert.ToInt64(dict["Goodness"]);
        if (dict.ContainsKey("LastActive")) god.LastActive = DateTime.FromBinary(Convert.ToInt64(dict["LastActive"]));
        if (dict.ContainsKey("CreatedDate")) god.CreatedDate = DateTime.FromBinary(Convert.ToInt64(dict["CreatedDate"]));
        if (dict.ContainsKey("Disciples")) god.Disciples = ((object[])dict["Disciples"]).Select(x => x.ToString() ?? "").ToList();
        if (dict.ContainsKey("Properties")) god.Properties = (Dictionary<string, object>)dict["Properties"];
        
        return god;
    }
    
    /// <summary>
    /// Override ToString for debugging
    /// </summary>
    public override string ToString()
    {
        return $"God: {Name} ({GetTitle()}) - Level {Level}, {Believers} believers, {Experience} power";
    }
    
    /// <summary>
    /// Override Equals for comparison
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is God other)
        {
            return Name == other.Name && Id == other.Id;
        }
        return false;
    }
    
    /// <summary>
    /// Override GetHashCode
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Id);
    }
} 
