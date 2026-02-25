using UsurperRemake.Utils;
using System.Collections.Generic;

/// <summary>
/// Sexual orientation - determines who the NPC is attracted to
/// </summary>
public enum SexualOrientation
{
    Straight,       // Attracted to opposite sex
    Gay,            // Attracted to same sex (male)
    Lesbian,        // Attracted to same sex (female)
    Bisexual,       // Attracted to both sexes
    Pansexual,      // Attracted regardless of gender
    Asexual,        // Little to no sexual attraction
    Demisexual      // Only attracted after emotional bond
}

/// <summary>
/// Gender identity - how the NPC identifies
/// </summary>
public enum GenderIdentity
{
    Male,
    Female,
    NonBinary,
    Genderfluid,
    TransMale,      // Assigned female, identifies male
    TransFemale     // Assigned male, identifies female
}

/// <summary>
/// Romance style - preferred dynamic in intimate situations
/// </summary>
public enum RomanceStyle
{
    Dominant,       // Prefers to lead/control
    Submissive,     // Prefers to follow/be led
    Switch,         // Comfortable either way
    Vanilla,        // Prefers traditional/gentle
    Adventurous     // Open to experimentation
}

/// <summary>
/// Relationship preference - what type of relationship structure they prefer
/// </summary>
public enum RelationshipPreference
{
    Monogamous,         // Traditional exclusive relationship
    OpenRelationship,   // Primary partner + others allowed
    Polyamorous,        // Multiple committed relationships
    CasualOnly,         // No commitment, just fun
    FriendsWithBenefits,// Physical without romance
    Undecided           // Open to various structures
}

public class PersonalityProfile
{
    // Core traits (0.0 to 1.0)
    public float Aggression { get; set; }      // Likelihood to start fights
    public float Greed { get; set; }           // Focus on wealth accumulation
    public float Courage { get; set; }         // Willingness to take risks
    public float Loyalty { get; set; }         // Stays with gangs/friends
    public float Vengefulness { get; set; }    // Seeks revenge for wrongs
    public float Impulsiveness { get; set; }   // Makes quick decisions
    public float Sociability { get; set; }     // Seeks interaction
    public float Ambition { get; set; }        // Desires power/status

    // Additional traits required by ported modules
    public float Trustworthiness { get; set; } // Reliability / honesty
    public float Caution { get; set; }         // Risk-avoidance / carefulness
    public float Intelligence { get; set; }    // Problem-solving aptitude (for magic shops)
    public float Mysticism { get; set; }       // Affinity for the arcane
    public float Patience { get; set; }        // Willingness to wait / endure delays

    // Romance personality traits (0.0 to 1.0)
    public float Romanticism { get; set; }     // How romantic vs. practical in relationships
    public float Sensuality { get; set; }      // Physical expressiveness and desire
    public float Jealousy { get; set; }        // Reaction intensity to partner with others
    public float Commitment { get; set; }      // Marriage-minded vs. casual preference
    public float Adventurousness { get; set; } // Willingness to try new things intimately
    public float Exhibitionism { get; set; }   // Enjoys being watched during intimacy
    public float Voyeurism { get; set; }       // Enjoys watching others
    public float Flirtatiousness { get; set; } // Likelihood to flirt and respond to flirting
    public float Passion { get; set; }         // Intensity of emotional/physical expression
    public float Tenderness { get; set; }      // Preference for gentle vs. rough intimacy

    // Sexual/romantic identity
    public SexualOrientation Orientation { get; set; } = SexualOrientation.Straight;
    public GenderIdentity Gender { get; set; } = GenderIdentity.Male;
    public RomanceStyle IntimateStyle { get; set; } = RomanceStyle.Switch;
    public RelationshipPreference RelationshipPref { get; set; } = RelationshipPreference.Undecided;

    // Behavioral modifiers
    public CombatStyle PreferredCombatStyle { get; set; }
    public List<string> Fears { get; set; } = new List<string>();
    public List<string> Desires { get; set; } = new List<string>();
    public string Archetype { get; set; } = "";
    
    public static PersonalityProfile GenerateRandom(string npcArchetype)
    {
        var profile = new PersonalityProfile();
        profile.Archetype = npcArchetype;
        
        switch (npcArchetype.ToLower())
        {
            case "thug":
            case "brawler":
                profile.Aggression = (float)Random.Shared.NextDouble() * 0.3f + 0.7f;  // 0.7-1.0
                profile.Courage = (float)Random.Shared.NextDouble() * 0.4f + 0.6f;     // 0.6-1.0
                profile.Greed = (float)Random.Shared.NextDouble() * 0.5f + 0.3f;       // 0.3-0.8
                profile.Loyalty = (float)Random.Shared.NextDouble() * 0.3f + 0.2f;     // 0.2-0.5
                profile.Vengefulness = (float)Random.Shared.NextDouble() * 0.3f + 0.6f; // 0.6-0.9
                profile.Impulsiveness = (float)Random.Shared.NextDouble() * 0.4f + 0.5f; // 0.5-0.9
                profile.Sociability = (float)Random.Shared.NextDouble() * 0.4f + 0.3f; // 0.3-0.7
                profile.Ambition = (float)Random.Shared.NextDouble() * 0.5f + 0.2f;    // 0.2-0.7
                profile.Intelligence = (float)Random.Shared.NextDouble() * 0.4f + 0.1f; // 0.1-0.5 (street smart, not book smart)
                profile.Patience = (float)Random.Shared.NextDouble() * 0.3f + 0.1f;    // 0.1-0.4 (short tempered)
                profile.Mysticism = (float)Random.Shared.NextDouble() * 0.2f;          // 0.0-0.2 (no interest in magic)
                profile.Trustworthiness = (float)Random.Shared.NextDouble() * 0.3f + 0.1f; // 0.1-0.4 (unreliable)
                profile.Caution = (float)Random.Shared.NextDouble() * 0.3f + 0.1f;     // 0.1-0.4 (reckless)
                profile.PreferredCombatStyle = CombatStyle.Aggressive;
                profile.Fears.Add("stronger_opponents");
                profile.Fears.Add("law_enforcement");
                profile.Desires.Add("dominance");
                profile.Desires.Add("respect");
                break;
                
            case "merchant":
            case "trader":
                profile.Aggression = (float)Random.Shared.NextDouble() * 0.3f;         // 0.0-0.3
                profile.Greed = (float)Random.Shared.NextDouble() * 0.3f + 0.7f;      // 0.7-1.0
                profile.Courage = (float)Random.Shared.NextDouble() * 0.4f + 0.2f;     // 0.2-0.6
                profile.Loyalty = (float)Random.Shared.NextDouble() * 0.4f + 0.4f;     // 0.4-0.8
                profile.Vengefulness = (float)Random.Shared.NextDouble() * 0.3f + 0.2f; // 0.2-0.5
                profile.Impulsiveness = (float)Random.Shared.NextDouble() * 0.3f + 0.1f; // 0.1-0.4
                profile.Sociability = (float)Random.Shared.NextDouble() * 0.3f + 0.6f; // 0.6-0.9
                profile.Ambition = (float)Random.Shared.NextDouble() * 0.3f + 0.5f;    // 0.5-0.8
                profile.Intelligence = (float)Random.Shared.NextDouble() * 0.3f + 0.5f; // 0.5-0.8 (shrewd business sense)
                profile.Patience = (float)Random.Shared.NextDouble() * 0.3f + 0.5f;    // 0.5-0.8 (patient negotiators)
                profile.Mysticism = (float)Random.Shared.NextDouble() * 0.3f;          // 0.0-0.3 (practical minded)
                profile.Trustworthiness = (float)Random.Shared.NextDouble() * 0.4f + 0.4f; // 0.4-0.8 (reputation matters)
                profile.Caution = (float)Random.Shared.NextDouble() * 0.3f + 0.5f;     // 0.5-0.8 (careful with money)
                profile.PreferredCombatStyle = CombatStyle.Defensive;
                profile.Fears.Add("poverty");
                profile.Fears.Add("violence");
                profile.Fears.Add("theft");
                profile.Desires.Add("wealth");
                profile.Desires.Add("security");
                profile.Desires.Add("reputation");
                break;
                
            case "noble":
            case "aristocrat":
                profile.Ambition = (float)Random.Shared.NextDouble() * 0.2f + 0.8f;    // 0.8-1.0
                profile.Vengefulness = (float)Random.Shared.NextDouble() * 0.4f + 0.5f;// 0.5-0.9
                profile.Loyalty = (float)Random.Shared.NextDouble() * 0.3f + 0.4f;     // 0.4-0.7
                profile.Courage = (float)Random.Shared.NextDouble() * 0.3f + 0.5f;     // 0.5-0.8
                profile.Sociability = (float)Random.Shared.NextDouble() * 0.3f + 0.4f; // 0.4-0.7
                profile.Greed = (float)Random.Shared.NextDouble() * 0.4f + 0.4f;       // 0.4-0.8
                profile.Aggression = (float)Random.Shared.NextDouble() * 0.4f + 0.3f;  // 0.3-0.7
                profile.Impulsiveness = (float)Random.Shared.NextDouble() * 0.3f + 0.2f; // 0.2-0.5
                profile.Intelligence = (float)Random.Shared.NextDouble() * 0.3f + 0.5f; // 0.5-0.8 (educated, politically savvy)
                profile.Patience = (float)Random.Shared.NextDouble() * 0.3f + 0.4f;    // 0.4-0.7 (plays the long game)
                profile.Mysticism = (float)Random.Shared.NextDouble() * 0.3f + 0.1f;   // 0.1-0.4 (some superstition)
                profile.Trustworthiness = (float)Random.Shared.NextDouble() * 0.4f + 0.3f; // 0.3-0.7 (varies by individual)
                profile.Caution = (float)Random.Shared.NextDouble() * 0.3f + 0.4f;     // 0.4-0.7 (calculated)
                profile.PreferredCombatStyle = CombatStyle.Tactical;
                profile.Fears.Add("disgrace");
                profile.Fears.Add("poverty");
                profile.Desires.Add("power");
                profile.Desires.Add("respect");
                profile.Desires.Add("influence");
                break;
                
            case "guard":
            case "soldier":
                profile.Loyalty = (float)Random.Shared.NextDouble() * 0.2f + 0.7f;     // 0.7-0.9
                profile.Courage = (float)Random.Shared.NextDouble() * 0.3f + 0.6f;     // 0.6-0.9
                profile.Aggression = (float)Random.Shared.NextDouble() * 0.3f + 0.4f;  // 0.4-0.7
                profile.Ambition = (float)Random.Shared.NextDouble() * 0.4f + 0.3f;    // 0.3-0.7
                profile.Vengefulness = (float)Random.Shared.NextDouble() * 0.3f + 0.4f; // 0.4-0.7
                profile.Impulsiveness = (float)Random.Shared.NextDouble() * 0.3f + 0.2f; // 0.2-0.5
                profile.Sociability = (float)Random.Shared.NextDouble() * 0.4f + 0.3f; // 0.3-0.7
                profile.Greed = (float)Random.Shared.NextDouble() * 0.4f + 0.2f;       // 0.2-0.6
                profile.Intelligence = (float)Random.Shared.NextDouble() * 0.4f + 0.3f; // 0.3-0.7 (tactical awareness)
                profile.Patience = (float)Random.Shared.NextDouble() * 0.3f + 0.5f;    // 0.5-0.8 (disciplined, waits for orders)
                profile.Mysticism = (float)Random.Shared.NextDouble() * 0.2f + 0.1f;   // 0.1-0.3 (superstitious but practical)
                profile.Trustworthiness = (float)Random.Shared.NextDouble() * 0.2f + 0.6f; // 0.6-0.8 (duty-bound, reliable)
                profile.Caution = (float)Random.Shared.NextDouble() * 0.3f + 0.4f;     // 0.4-0.7 (trained to assess threats)
                profile.PreferredCombatStyle = CombatStyle.Balanced;
                profile.Fears.Add("dereliction");
                profile.Fears.Add("dishonor");
                profile.Desires.Add("order");
                profile.Desires.Add("justice");
                profile.Desires.Add("duty");
                break;
                
            case "priest":
            case "cleric":
                profile.Loyalty = (float)Random.Shared.NextDouble() * 0.3f + 0.6f;     // 0.6-0.9
                profile.Courage = (float)Random.Shared.NextDouble() * 0.4f + 0.4f;     // 0.4-0.8
                profile.Aggression = (float)Random.Shared.NextDouble() * 0.2f;         // 0.0-0.2
                profile.Ambition = (float)Random.Shared.NextDouble() * 0.3f + 0.3f;    // 0.3-0.6
                profile.Vengefulness = (float)Random.Shared.NextDouble() * 0.2f;       // 0.0-0.2
                profile.Impulsiveness = (float)Random.Shared.NextDouble() * 0.3f + 0.1f; // 0.1-0.4
                profile.Sociability = (float)Random.Shared.NextDouble() * 0.3f + 0.6f; // 0.6-0.9
                profile.Greed = (float)Random.Shared.NextDouble() * 0.3f;              // 0.0-0.3
                profile.Intelligence = (float)Random.Shared.NextDouble() * 0.3f + 0.5f; // 0.5-0.8 (theological study)
                profile.Patience = (float)Random.Shared.NextDouble() * 0.2f + 0.7f;    // 0.7-0.9 (serene, meditative)
                profile.Mysticism = (float)Random.Shared.NextDouble() * 0.3f + 0.5f;   // 0.5-0.8 (deeply spiritual)
                profile.Trustworthiness = (float)Random.Shared.NextDouble() * 0.2f + 0.7f; // 0.7-0.9 (pillars of community)
                profile.Caution = (float)Random.Shared.NextDouble() * 0.3f + 0.5f;     // 0.5-0.8 (thoughtful, deliberate)
                profile.PreferredCombatStyle = CombatStyle.Defensive;
                profile.Fears.Add("sin");
                profile.Fears.Add("corruption");
                profile.Desires.Add("peace");
                profile.Desires.Add("salvation");
                profile.Desires.Add("knowledge");
                break;
                
            case "mystic":
            case "mage":
                profile.Ambition = (float)Random.Shared.NextDouble() * 0.3f + 0.6f;    // 0.6-0.9
                profile.Courage = (float)Random.Shared.NextDouble() * 0.5f + 0.3f;     // 0.3-0.8
                profile.Aggression = (float)Random.Shared.NextDouble() * 0.4f + 0.2f;  // 0.2-0.6
                profile.Loyalty = (float)Random.Shared.NextDouble() * 0.4f + 0.3f;     // 0.3-0.7
                profile.Vengefulness = (float)Random.Shared.NextDouble() * 0.4f + 0.4f; // 0.4-0.8
                profile.Impulsiveness = (float)Random.Shared.NextDouble() * 0.3f + 0.2f; // 0.2-0.5
                profile.Sociability = (float)Random.Shared.NextDouble() * 0.5f + 0.2f; // 0.2-0.7
                profile.Greed = (float)Random.Shared.NextDouble() * 0.4f + 0.3f;       // 0.3-0.7
                profile.Intelligence = (float)Random.Shared.NextDouble() * 0.2f + 0.8f; // 0.8-1.0 (brilliant scholars)
                profile.Patience = (float)Random.Shared.NextDouble() * 0.3f + 0.5f;    // 0.5-0.8 (years of study require patience)
                profile.Mysticism = (float)Random.Shared.NextDouble() * 0.2f + 0.8f;   // 0.8-1.0 (deeply attuned to the arcane)
                profile.Trustworthiness = (float)Random.Shared.NextDouble() * 0.5f + 0.2f; // 0.2-0.7 (varies — some are secretive)
                profile.Caution = (float)Random.Shared.NextDouble() * 0.4f + 0.4f;     // 0.4-0.8 (respects dangerous magic)
                profile.PreferredCombatStyle = CombatStyle.Tactical;
                profile.Fears.Add("ignorance");
                profile.Fears.Add("powerlessness");
                profile.Desires.Add("knowledge");
                profile.Desires.Add("power");
                profile.Desires.Add("secrets");
                break;
                
            case "craftsman":
            case "artisan":
                profile.Loyalty = (float)Random.Shared.NextDouble() * 0.3f + 0.5f;     // 0.5-0.8
                profile.Courage = (float)Random.Shared.NextDouble() * 0.4f + 0.4f;     // 0.4-0.8
                profile.Aggression = (float)Random.Shared.NextDouble() * 0.3f + 0.2f;  // 0.2-0.5
                profile.Ambition = (float)Random.Shared.NextDouble() * 0.4f + 0.3f;    // 0.3-0.7
                profile.Vengefulness = (float)Random.Shared.NextDouble() * 0.3f + 0.3f; // 0.3-0.6
                profile.Impulsiveness = (float)Random.Shared.NextDouble() * 0.3f + 0.2f; // 0.2-0.5
                profile.Sociability = (float)Random.Shared.NextDouble() * 0.4f + 0.4f; // 0.4-0.8
                profile.Greed = (float)Random.Shared.NextDouble() * 0.4f + 0.4f;       // 0.4-0.8
                profile.Intelligence = (float)Random.Shared.NextDouble() * 0.3f + 0.5f; // 0.5-0.8 (skilled problem solvers)
                profile.Patience = (float)Random.Shared.NextDouble() * 0.2f + 0.6f;    // 0.6-0.8 (meticulous work requires patience)
                profile.Mysticism = (float)Random.Shared.NextDouble() * 0.3f + 0.1f;   // 0.1-0.4 (some enchanting interest)
                profile.Trustworthiness = (float)Random.Shared.NextDouble() * 0.3f + 0.5f; // 0.5-0.8 (honest tradespeople)
                profile.Caution = (float)Random.Shared.NextDouble() * 0.3f + 0.4f;     // 0.4-0.7 (careful with their work)
                profile.PreferredCombatStyle = CombatStyle.Balanced;
                profile.Fears.Add("mediocrity");
                profile.Fears.Add("poverty");
                profile.Desires.Add("mastery");
                profile.Desires.Add("recognition");
                profile.Desires.Add("quality");
                break;
                
            default: // commoner
                // Balanced random stats with slight variation
                profile.Aggression = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;  // 0.2-0.8
                profile.Greed = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;       // 0.2-0.8
                profile.Courage = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;     // 0.2-0.8
                profile.Loyalty = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;     // 0.2-0.8
                profile.Vengefulness = (float)Random.Shared.NextDouble() * 0.6f + 0.2f; // 0.2-0.8
                profile.Impulsiveness = (float)Random.Shared.NextDouble() * 0.8f + 0.1f; // 0.1-0.9
                profile.Sociability = (float)Random.Shared.NextDouble() * 0.6f + 0.2f; // 0.2-0.8
                profile.Ambition = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;    // 0.2-0.8
                profile.Intelligence = (float)Random.Shared.NextDouble() * 0.6f + 0.2f; // 0.2-0.8 (wide range)
                profile.Patience = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;    // 0.2-0.8 (wide range)
                profile.Mysticism = (float)Random.Shared.NextDouble() * 0.4f + 0.1f;   // 0.1-0.5 (some folk superstition)
                profile.Trustworthiness = (float)Random.Shared.NextDouble() * 0.6f + 0.2f; // 0.2-0.8 (wide range)
                profile.Caution = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;     // 0.2-0.8 (wide range)
                profile.PreferredCombatStyle = (CombatStyle)Random.Shared.Next(0, 4);
                profile.Fears.Add("death");
                profile.Fears.Add("poverty");
                profile.Desires.Add("survival");
                profile.Desires.Add("security");
                break;
        }

        // Generate romance traits for all archetypes
        GenerateRomanceTraits(profile, npcArchetype);

        return profile;
    }

    /// <summary>
    /// Generate romance-related personality traits based on archetype
    /// </summary>
    private static void GenerateRomanceTraits(PersonalityProfile profile, string archetype)
    {
        // Base romance traits - everyone gets some variance
        profile.Romanticism = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;     // 0.2-0.8
        profile.Sensuality = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;      // 0.2-0.8
        profile.Jealousy = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;        // 0.2-0.8
        profile.Commitment = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;      // 0.2-0.8
        profile.Adventurousness = (float)Random.Shared.NextDouble() * 0.6f + 0.2f; // 0.2-0.8
        profile.Exhibitionism = (float)Random.Shared.NextDouble() * 0.4f;          // 0.0-0.4 (rarer)
        profile.Voyeurism = (float)Random.Shared.NextDouble() * 0.4f;              // 0.0-0.4 (rarer)
        profile.Flirtatiousness = (float)Random.Shared.NextDouble() * 0.6f + 0.2f; // 0.2-0.8
        profile.Passion = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;         // 0.2-0.8
        profile.Tenderness = (float)Random.Shared.NextDouble() * 0.6f + 0.2f;      // 0.2-0.8

        // Archetype-specific romance trait adjustments
        switch (archetype.ToLower())
        {
            case "thug":
            case "brawler":
                profile.Romanticism *= 0.5f;        // Less romantic
                profile.Passion += 0.2f;            // More passionate/intense
                profile.Tenderness *= 0.4f;         // Less tender
                profile.IntimateStyle = (float)Random.Shared.NextDouble() > 0.7f ? RomanceStyle.Dominant : RomanceStyle.Adventurous;
                profile.Jealousy += 0.2f;           // More possessive
                break;

            case "noble":
            case "aristocrat":
                profile.Romanticism += 0.2f;        // More romantic (courtly love)
                profile.Exhibitionism += 0.1f;      // Enjoys showing off
                profile.Commitment += 0.2f;         // Values marriage/alliances
                profile.IntimateStyle = (float)Random.Shared.NextDouble() > 0.5f ? RomanceStyle.Dominant : RomanceStyle.Vanilla;
                profile.RelationshipPref = (float)Random.Shared.NextDouble() > 0.7f ? RelationshipPreference.OpenRelationship : RelationshipPreference.Monogamous;
                break;

            case "merchant":
            case "trader":
                profile.Commitment += 0.1f;         // Practical about relationships
                profile.Romanticism *= 0.8f;        // Slightly less romantic
                profile.IntimateStyle = RomanceStyle.Vanilla;
                profile.RelationshipPref = RelationshipPreference.Monogamous;
                break;

            case "guard":
            case "soldier":
                profile.Commitment += 0.2f;         // Loyal in relationships too
                profile.Jealousy += 0.1f;           // Protective
                profile.Passion += 0.1f;
                profile.IntimateStyle = (float)Random.Shared.NextDouble() > 0.5f ? RomanceStyle.Dominant : RomanceStyle.Switch;
                break;

            case "priest":
            case "cleric":
                profile.Romanticism += 0.3f;        // Very romantic (spiritual love)
                profile.Sensuality *= 0.6f;         // More reserved
                profile.Tenderness += 0.3f;         // Very gentle
                profile.Commitment += 0.3f;         // Strong commitment values
                profile.IntimateStyle = RomanceStyle.Vanilla;
                profile.Adventurousness *= 0.5f;    // Less adventurous
                profile.RelationshipPref = RelationshipPreference.Monogamous;
                break;

            case "mystic":
            case "mage":
                profile.Adventurousness += 0.3f;    // Open to experimentation
                profile.Sensuality += 0.1f;
                profile.IntimateStyle = (float)Random.Shared.NextDouble() > 0.5f ? RomanceStyle.Switch : RomanceStyle.Adventurous;
                profile.RelationshipPref = (float)Random.Shared.NextDouble() > 0.6f ? RelationshipPreference.Polyamorous : RelationshipPreference.Undecided;
                break;

            case "craftsman":
            case "artisan":
                profile.Passion += 0.2f;            // Passionate about everything
                profile.Tenderness += 0.1f;
                profile.Sensuality += 0.1f;         // Appreciates physical beauty
                profile.IntimateStyle = RomanceStyle.Switch;
                break;
        }

        // Randomly assign orientation — heterosexual is the vast majority,
        // non-hetero relationships are rare flavor (~1-2% of the population)
        float orientationRoll = (float)Random.Shared.NextDouble();
        if (orientationRoll < 0.96f)
            profile.Orientation = SexualOrientation.Straight;
        else if (orientationRoll < 0.975f)
            profile.Orientation = profile.Gender == GenderIdentity.Female ? SexualOrientation.Lesbian : SexualOrientation.Gay;
        else if (orientationRoll < 0.99f)
            profile.Orientation = SexualOrientation.Bisexual;
        else
            profile.Orientation = SexualOrientation.Asexual;

        // Randomly assign relationship preference if not already set by archetype
        if (profile.RelationshipPref == RelationshipPreference.Undecided)
        {
            float relPrefRoll = (float)Random.Shared.NextDouble();
            if (relPrefRoll < 0.60f)
                profile.RelationshipPref = RelationshipPreference.Monogamous;
            else if (relPrefRoll < 0.75f)
                profile.RelationshipPref = RelationshipPreference.OpenRelationship;
            else if (relPrefRoll < 0.85f)
                profile.RelationshipPref = RelationshipPreference.Polyamorous;
            else if (relPrefRoll < 0.92f)
                profile.RelationshipPref = RelationshipPreference.CasualOnly;
            else
                profile.RelationshipPref = RelationshipPreference.FriendsWithBenefits;
        }

        // Clamp all values to 0-1 range
        profile.Romanticism = ClampFloat(profile.Romanticism, 0f, 1f);
        profile.Sensuality = ClampFloat(profile.Sensuality, 0f, 1f);
        profile.Jealousy = ClampFloat(profile.Jealousy, 0f, 1f);
        profile.Commitment = ClampFloat(profile.Commitment, 0f, 1f);
        profile.Adventurousness = ClampFloat(profile.Adventurousness, 0f, 1f);
        profile.Exhibitionism = ClampFloat(profile.Exhibitionism, 0f, 1f);
        profile.Voyeurism = ClampFloat(profile.Voyeurism, 0f, 1f);
        profile.Flirtatiousness = ClampFloat(profile.Flirtatiousness, 0f, 1f);
        profile.Passion = ClampFloat(profile.Passion, 0f, 1f);
        profile.Tenderness = ClampFloat(profile.Tenderness, 0f, 1f);
    }

    /// <summary>
    /// Clamp a float value between min and max
    /// </summary>
    private static float ClampFloat(float value, float min, float max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
    
    /// <summary>
    /// Generate personality profile for specific archetype - alias for GenerateRandom for API compatibility
    /// </summary>
    public static PersonalityProfile GenerateForArchetype(string archetype)
    {
        return GenerateRandom(archetype);
    }
    
    public float GetCompatibility(PersonalityProfile other)
    {
        // Calculate compatibility based on trait differences
        var aggressionDiff = Math.Abs(Aggression - other.Aggression);
        var loyaltyDiff = Math.Abs(Loyalty - other.Loyalty);
        var sociabilityDiff = Math.Abs(Sociability - other.Sociability);
        var ambitionDiff = Math.Abs(Ambition - other.Ambition);

        // Similar people are more compatible
        var traitCompatibility = 1.0f - (aggressionDiff + loyaltyDiff + sociabilityDiff + ambitionDiff) / 4.0f;

        // Special compatibility bonuses/penalties
        var archetypeBonus = GetArchetypeCompatibility(other.Archetype);

        return Math.Max(0.0f, Math.Min(1.0f, traitCompatibility + archetypeBonus));
    }

    /// <summary>
    /// Calculate romantic compatibility with another personality
    /// </summary>
    public float GetRomanticCompatibility(PersonalityProfile other)
    {
        float compatibility = 0f;

        // Similar romanticism levels = good match
        var romanticismMatch = 1f - Math.Abs(Romanticism - other.Romanticism);
        compatibility += romanticismMatch * 0.15f;

        // Complementary intimate styles work well
        float styleMatch = GetIntimateStyleCompatibility(other.IntimateStyle);
        compatibility += styleMatch * 0.15f;

        // Passion levels should be similar
        var passionMatch = 1f - Math.Abs(Passion - other.Passion);
        compatibility += passionMatch * 0.1f;

        // Tenderness preferences should align
        var tendernessMatch = 1f - Math.Abs(Tenderness - other.Tenderness);
        compatibility += tendernessMatch * 0.1f;

        // Commitment levels should be similar
        var commitmentMatch = 1f - Math.Abs(Commitment - other.Commitment);
        compatibility += commitmentMatch * 0.15f;

        // Adventurousness - either both adventurous or both vanilla
        var adventureMatch = 1f - Math.Abs(Adventurousness - other.Adventurousness);
        compatibility += adventureMatch * 0.1f;

        // Relationship structure compatibility
        float relPrefMatch = GetRelationshipPrefCompatibility(other.RelationshipPref);
        compatibility += relPrefMatch * 0.15f;

        // Base personality compatibility also matters
        compatibility += GetCompatibility(other) * 0.1f;

        return ClampFloat(compatibility, 0f, 1f);
    }

    /// <summary>
    /// Check if this personality is attracted to another based on orientation
    /// </summary>
    public bool IsAttractedTo(GenderIdentity otherGender)
    {
        return Orientation switch
        {
            SexualOrientation.Straight => (Gender == GenderIdentity.Male && IsFemalePresenting(otherGender)) ||
                                          (Gender == GenderIdentity.Female && IsMalePresenting(otherGender)),
            SexualOrientation.Gay => Gender == GenderIdentity.Male && IsMalePresenting(otherGender),
            SexualOrientation.Lesbian => Gender == GenderIdentity.Female && IsFemalePresenting(otherGender),
            SexualOrientation.Bisexual => true,
            SexualOrientation.Pansexual => true,
            SexualOrientation.Demisexual => true, // Attraction develops with bond, check relationship level separately
            SexualOrientation.Asexual => false,   // No sexual attraction
            _ => true
        };
    }

    private static bool IsMalePresenting(GenderIdentity gender)
    {
        return gender == GenderIdentity.Male || gender == GenderIdentity.TransMale;
    }

    private static bool IsFemalePresenting(GenderIdentity gender)
    {
        return gender == GenderIdentity.Female || gender == GenderIdentity.TransFemale;
    }

    /// <summary>
    /// Get compatibility score for intimate styles (0-1)
    /// </summary>
    private float GetIntimateStyleCompatibility(RomanceStyle otherStyle)
    {
        // Dominant + Submissive = perfect match
        if ((IntimateStyle == RomanceStyle.Dominant && otherStyle == RomanceStyle.Submissive) ||
            (IntimateStyle == RomanceStyle.Submissive && otherStyle == RomanceStyle.Dominant))
            return 1.0f;

        // Switch works well with anyone
        if (IntimateStyle == RomanceStyle.Switch || otherStyle == RomanceStyle.Switch)
            return 0.8f;

        // Two dominants or two submissives = less compatible
        if (IntimateStyle == otherStyle && (IntimateStyle == RomanceStyle.Dominant || IntimateStyle == RomanceStyle.Submissive))
            return 0.3f;

        // Vanilla + Vanilla = good
        if (IntimateStyle == RomanceStyle.Vanilla && otherStyle == RomanceStyle.Vanilla)
            return 0.9f;

        // Adventurous + Adventurous = great
        if (IntimateStyle == RomanceStyle.Adventurous && otherStyle == RomanceStyle.Adventurous)
            return 0.95f;

        // Vanilla + Adventurous = mild mismatch
        if ((IntimateStyle == RomanceStyle.Vanilla && otherStyle == RomanceStyle.Adventurous) ||
            (IntimateStyle == RomanceStyle.Adventurous && otherStyle == RomanceStyle.Vanilla))
            return 0.5f;

        return 0.6f; // Default moderate compatibility
    }

    /// <summary>
    /// Get compatibility for relationship structure preferences
    /// </summary>
    private float GetRelationshipPrefCompatibility(RelationshipPreference otherPref)
    {
        // Exact match
        if (RelationshipPref == otherPref)
            return 1.0f;

        // Monogamous with non-monogamous = conflict
        if (RelationshipPref == RelationshipPreference.Monogamous &&
            (otherPref == RelationshipPreference.Polyamorous || otherPref == RelationshipPreference.OpenRelationship))
            return 0.2f;

        // Open and Poly are semi-compatible
        if ((RelationshipPref == RelationshipPreference.OpenRelationship && otherPref == RelationshipPreference.Polyamorous) ||
            (RelationshipPref == RelationshipPreference.Polyamorous && otherPref == RelationshipPreference.OpenRelationship))
            return 0.7f;

        // Casual + FWB are compatible
        if ((RelationshipPref == RelationshipPreference.CasualOnly && otherPref == RelationshipPreference.FriendsWithBenefits) ||
            (RelationshipPref == RelationshipPreference.FriendsWithBenefits && otherPref == RelationshipPreference.CasualOnly))
            return 0.8f;

        // Undecided is flexible
        if (RelationshipPref == RelationshipPreference.Undecided || otherPref == RelationshipPreference.Undecided)
            return 0.7f;

        return 0.4f; // Default low compatibility for mismatches
    }

    /// <summary>
    /// Calculate how likely this personality is to respond positively to flirting
    /// Takes into account NPC personality, relationship level, and whether NPC is already committed
    /// </summary>
    /// <param name="currentRelationshipLevel">0=Soulmate, 50=Neutral, 100=Hated</param>
    /// <param name="isAttracted">Whether NPC is attracted to player's gender</param>
    /// <param name="npcIsMarried">Whether this NPC is already married</param>
    /// <param name="npcHasLover">Whether this NPC already has a lover</param>
    public float GetFlirtReceptiveness(float currentRelationshipLevel, bool isAttracted,
        bool npcIsMarried = false, bool npcHasLover = false)
    {
        // Not attracted = very unlikely (but not impossible for very high-flirt personalities)
        if (!isAttracted)
            return Flirtatiousness > 0.8f ? 0.05f : 0.02f;

        // NPC is married with high commitment = impossible
        if (npcIsMarried && Commitment > 0.7f)
            return 0.01f; // Virtually impossible - they're loyal

        // NPC is married but lower commitment = very hard
        if (npcIsMarried)
            return ClampFloat(0.05f + (1f - Commitment) * 0.1f, 0.01f, 0.15f);

        // NPC has a lover and is jealous/committed = very hard
        if (npcHasLover && (Jealousy > 0.6f || Commitment > 0.6f))
            return ClampFloat(0.08f + Flirtatiousness * 0.1f, 0.05f, 0.20f);

        // Base receptiveness from personality - LOWER baseline than before
        // Flirtatiousness is the main driver, other traits contribute less
        float receptiveness = Flirtatiousness * 0.35f + Sensuality * 0.15f;

        // High commitment people are harder to flirt with (they want serious relationships, not casual flirting)
        receptiveness -= Commitment * 0.15f;

        // Sociability helps slightly
        receptiveness += Sociability * 0.08f;

        // Relationship level matters more now
        // Need decent relationship (< 60) before flirting has good odds
        if (currentRelationshipLevel > 70)
            receptiveness *= 0.3f; // Strangers are very unreceptive
        else if (currentRelationshipLevel > 50)
            receptiveness *= 0.6f; // Acquaintances are somewhat unreceptive
        else if (currentRelationshipLevel > 30)
            receptiveness *= 0.85f; // Friends are more receptive
        // Good friends and better get no penalty

        // Shy/reserved people need even more relationship first
        if (Sociability < 0.3f && currentRelationshipLevel > 40)
            receptiveness *= 0.4f;
        else if (Sociability < 0.5f && currentRelationshipLevel > 50)
            receptiveness *= 0.6f;

        // Tenderness affects approachability
        if (Tenderness < 0.3f && currentRelationshipLevel > 40)
            receptiveness *= 0.7f; // Cold personalities are harder to approach

        return ClampFloat(receptiveness, 0.02f, 0.75f); // Cap at 75% even in best case
    }

    /// <summary>
    /// Get jealousy response intensity (0-1) when partner is with someone else
    /// </summary>
    public float GetJealousyResponse(bool hasConsentedToArrangement)
    {
        if (hasConsentedToArrangement)
        {
            // Even with consent, some jealousy may remain based on personality
            return Jealousy * 0.3f;
        }

        // No consent = full jealousy response
        return Jealousy;
    }
    
    private float GetArchetypeCompatibility(string otherArchetype)
    {
        // Define archetype relationships
        var compatibilityMatrix = new Dictionary<string, Dictionary<string, float>>
        {
            ["thug"] = new Dictionary<string, float>
            {
                ["thug"] = 0.2f,
                ["merchant"] = -0.3f,
                ["guard"] = -0.5f,
                ["priest"] = -0.4f,
                ["noble"] = -0.2f,
                ["commoner"] = 0.1f
            },
            ["merchant"] = new Dictionary<string, float>
            {
                ["merchant"] = 0.3f,
                ["thug"] = -0.3f,
                ["guard"] = 0.1f,
                ["priest"] = 0.1f,
                ["noble"] = 0.2f,
                ["craftsman"] = 0.3f
            },
            ["guard"] = new Dictionary<string, float>
            {
                ["guard"] = 0.4f,
                ["thug"] = -0.5f,
                ["priest"] = 0.3f,
                ["noble"] = 0.2f,
                ["merchant"] = 0.1f
            },
            ["priest"] = new Dictionary<string, float>
            {
                ["priest"] = 0.4f,
                ["thug"] = -0.4f,
                ["guard"] = 0.3f,
                ["noble"] = 0.1f,
                ["commoner"] = 0.2f
            }
        };
        
        if (compatibilityMatrix.ContainsKey(Archetype.ToLower()) &&
            compatibilityMatrix[Archetype.ToLower()].ContainsKey(otherArchetype.ToLower()))
        {
            return compatibilityMatrix[Archetype.ToLower()][otherArchetype.ToLower()];
        }
        
        return 0.0f;
    }
    
    public bool IsLikelyToJoinGang()
    {
        // Gang joining likelihood — most NPCs should be willing to team up
        // Only truly antisocial loners refuse (low sociability + low loyalty + low ambition)
        var gangScore = (Loyalty * 0.25f) + (Sociability * 0.25f) + (Ambition * 0.25f) + (Courage * 0.15f) + (Aggression * 0.10f);
        return gangScore > 0.35f;
    }
    
    public bool IsLikelyToBetray()
    {
        // Betrayal likelihood
        var betrayalScore = (1.0f - Loyalty) * 0.4f + Greed * 0.3f + Ambition * 0.2f + Impulsiveness * 0.1f;
        return betrayalScore > 0.7f;
    }
    
    public bool IsLikelyToSeekRevenge()
    {
        return Vengefulness > 0.6f && (Aggression > 0.4f || Ambition > 0.5f);
    }
    
    public float GetDecisionWeight(string actionType)
    {
        return actionType.ToLower() switch
        {
            "attack" => Aggression * 0.7f + Courage * 0.3f,
            "flee" => (1.0f - Courage) * 0.6f + (1.0f - Aggression) * 0.4f,
            "negotiate" => Sociability * 0.5f + (1.0f - Aggression) * 0.3f + Charisma * 0.2f,
            "steal" => Greed * 0.6f + (1.0f - Loyalty) * 0.4f,
            "help" => Loyalty * 0.4f + Sociability * 0.3f + (1.0f - Greed) * 0.3f,
            "betray" => IsLikelyToBetray() ? 0.8f : 0.1f,
            "revenge" => IsLikelyToSeekRevenge() ? 0.9f : 0.2f,
            "join_gang" => IsLikelyToJoinGang() ? 0.7f : 0.2f,
            "trade" => Greed * 0.4f + Sociability * 0.3f + (1.0f - Aggression) * 0.3f,
            "explore" => Courage * 0.5f + Ambition * 0.3f + (1.0f - Fear) * 0.2f,
            _ => 0.5f
        };
    }
    
    private float Charisma => Sociability; // Simplified for now
    private float Fear => Fears.Count * 0.1f; // Simple fear calculation
    
    public override string ToString()
    {
        return $"{Archetype}: Agg={Aggression:F2}, Greed={Greed:F2}, Courage={Courage:F2}, " +
               $"Loyalty={Loyalty:F2}, Vengeful={Vengefulness:F2}, Social={Sociability:F2}";
    }
}

public enum CombatStyle
{
    Aggressive,    // All-out attack
    Defensive,     // Focus on defense and counters
    Tactical,      // Strategic, uses abilities
    Balanced       // Adaptable approach
} 
