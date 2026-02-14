using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Prison Activity System - Allows prisoners to do activities that improve their stats
/// While imprisoned, characters can choose activities to pass the time and grow stronger
/// </summary>
public class PrisonActivitySystem
{
    private static PrisonActivitySystem? _fallbackInstance;
    public static PrisonActivitySystem Instance
    {
        get
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null) return ctx.PrisonActivity;
            return _fallbackInstance ??= new PrisonActivitySystem();
        }
    }

    private Random random = new();

    /// <summary>
    /// Prison activities that prisoners can perform
    /// </summary>
    public enum PrisonActivity
    {
        None,
        Pushups,         // Increases Strength
        Yoga,            // Increases Dexterity/Agility
        Reading,         // Increases Intelligence/Mana
        Meditation,      // Increases Wisdom, heals HP
        ShadowBoxing,    // Increases Attack/Defence
        Stretching,      // Increases Stamina/MaxHP
        Planning,        // Increases Charisma (planning escape/future)
        Praying          // Increases Chivalry (if good) or Darkness (if evil)
    }

    /// <summary>
    /// Activity descriptions for display
    /// </summary>
    public static readonly Dictionary<PrisonActivity, (string Name, string Description, string Effect)> ActivityInfo = new()
    {
        { PrisonActivity.Pushups, ("Pushups", "Build upper body strength", "+1-2 Strength") },
        { PrisonActivity.Yoga, ("Yoga", "Improve flexibility and balance", "+1-2 Dexterity/Agility") },
        { PrisonActivity.Reading, ("Reading", "Study whatever texts you can find", "+1-2 Intelligence, +5 Mana") },
        { PrisonActivity.Meditation, ("Meditation", "Clear your mind and find inner peace", "+1 Wisdom, Heal 10% HP") },
        { PrisonActivity.ShadowBoxing, ("Shadow Boxing", "Practice combat moves", "+1 Attack, +1 Defence") },
        { PrisonActivity.Stretching, ("Stretching", "Build endurance and vitality", "+1 Stamina, +5 MaxHP") },
        { PrisonActivity.Planning, ("Planning", "Strategize your future moves", "+1-2 Charisma") },
        { PrisonActivity.Praying, ("Praying", "Seek divine guidance", "+10-20 Chivalry or Darkness") }
    };

    /// <summary>
    /// Perform a prison activity for a character
    /// </summary>
    public async Task<string> PerformActivity(Character prisoner, PrisonActivity activity)
    {
        if (activity == PrisonActivity.None)
            return "You rest in your cell.";

        string result = "";

        switch (activity)
        {
            case PrisonActivity.Pushups:
                result = PerformPushups(prisoner);
                break;

            case PrisonActivity.Yoga:
                result = PerformYoga(prisoner);
                break;

            case PrisonActivity.Reading:
                result = PerformReading(prisoner);
                break;

            case PrisonActivity.Meditation:
                result = PerformMeditation(prisoner);
                break;

            case PrisonActivity.ShadowBoxing:
                result = PerformShadowBoxing(prisoner);
                break;

            case PrisonActivity.Stretching:
                result = PerformStretching(prisoner);
                break;

            case PrisonActivity.Planning:
                result = PerformPlanning(prisoner);
                break;

            case PrisonActivity.Praying:
                result = PerformPraying(prisoner);
                break;
        }

        await Task.CompletedTask;
        return result;
    }

    private string PerformPushups(Character prisoner)
    {
        int gain = random.Next(1, 3);
        prisoner.Strength += gain;

        return $"You do pushups until your arms burn. Strength +{gain}!";
    }

    private string PerformYoga(Character prisoner)
    {
        int dexGain = random.Next(1, 3);
        int agiGain = random.Next(0, 2);

        prisoner.Dexterity += dexGain;
        prisoner.Agility += agiGain;

        string result = $"You practice yoga poses. Dexterity +{dexGain}";
        if (agiGain > 0)
            result += $", Agility +{agiGain}";
        result += "!";

        return result;
    }

    private string PerformReading(Character prisoner)
    {
        int intGain = random.Next(1, 3);
        int manaGain = random.Next(3, 8);

        prisoner.Intelligence += intGain;
        prisoner.Mana = Math.Min(prisoner.Mana + manaGain, prisoner.MaxMana);
        prisoner.MaxMana += random.Next(0, 2);

        return $"You read whatever materials you can find. Intelligence +{intGain}, Mana +{manaGain}!";
    }

    private string PerformMeditation(Character prisoner)
    {
        int wisGain = 1;
        long healAmount = prisoner.MaxHP / 10;

        prisoner.Wisdom += wisGain;
        prisoner.HP = Math.Min(prisoner.HP + healAmount, prisoner.MaxHP);

        return $"You meditate peacefully. Wisdom +{wisGain}, HP +{healAmount}!";
    }

    private string PerformShadowBoxing(Character prisoner)
    {
        prisoner.WeapPow += 1;
        prisoner.Defence += 1;

        return "You practice fighting an imaginary opponent. Attack +1, Defence +1!";
    }

    private string PerformStretching(Character prisoner)
    {
        int stamGain = random.Next(1, 3);
        int hpGain = random.Next(3, 8);

        prisoner.Stamina += stamGain;
        prisoner.MaxHP += hpGain;
        prisoner.HP = Math.Min(prisoner.HP + hpGain, prisoner.MaxHP);

        return $"You stretch and build endurance. Stamina +{stamGain}, MaxHP +{hpGain}!";
    }

    private string PerformPlanning(Character prisoner)
    {
        int chaGain = random.Next(1, 3);
        prisoner.Charisma += chaGain;

        return $"You plan your future carefully. Charisma +{chaGain}!";
    }

    private string PerformPraying(Character prisoner)
    {
        int gain = random.Next(10, 21);

        // Prayer aligns with character's existing tendencies
        if (prisoner.Chivalry > prisoner.Darkness)
        {
            prisoner.Chivalry += gain;
            return $"You pray for guidance and redemption. Chivalry +{gain}!";
        }
        else
        {
            prisoner.Darkness += gain;
            return $"You pray to darker powers for strength. Darkness +{gain}!";
        }
    }

    /// <summary>
    /// Get list of available activities
    /// </summary>
    public List<PrisonActivity> GetAvailableActivities()
    {
        return new List<PrisonActivity>
        {
            PrisonActivity.Pushups,
            PrisonActivity.Yoga,
            PrisonActivity.Reading,
            PrisonActivity.Meditation,
            PrisonActivity.ShadowBoxing,
            PrisonActivity.Stretching,
            PrisonActivity.Planning,
            PrisonActivity.Praying
        };
    }

    /// <summary>
    /// NPC prisoners automatically do random activities
    /// </summary>
    public void ProcessNPCPrisonerActivity(NPC prisoner)
    {
        if (prisoner == null || !prisoner.IsAlive || prisoner.DaysInPrison <= 0)
            return;

        // 50% chance to do an activity each day
        if (random.NextDouble() > 0.5)
            return;

        var activities = GetAvailableActivities();
        var activity = activities[random.Next(activities.Count)];

        // Silently perform the activity (no output needed for NPCs)
        switch (activity)
        {
            case PrisonActivity.Pushups:
                prisoner.Strength += random.Next(1, 2);
                break;
            case PrisonActivity.Yoga:
                prisoner.Dexterity += random.Next(1, 2);
                break;
            case PrisonActivity.Reading:
                prisoner.Intelligence += random.Next(1, 2);
                break;
            case PrisonActivity.Meditation:
                prisoner.Wisdom += 1;
                prisoner.HP = Math.Min(prisoner.HP + prisoner.MaxHP / 10, prisoner.MaxHP);
                break;
            case PrisonActivity.ShadowBoxing:
                prisoner.WeapPow += 1;
                prisoner.Defence += 1;
                break;
            case PrisonActivity.Stretching:
                prisoner.MaxHP += random.Next(3, 6);
                break;
            case PrisonActivity.Planning:
                prisoner.Charisma += random.Next(1, 2);
                break;
            case PrisonActivity.Praying:
                if (prisoner.Chivalry > prisoner.Darkness)
                    prisoner.Chivalry += random.Next(5, 15);
                else
                    prisoner.Darkness += random.Next(5, 15);
                break;
        }

        GD.Print($"[Prison] {prisoner.Name} performed {activity} while imprisoned");
    }

    /// <summary>
    /// Process all NPC prisoners' daily activities
    /// Called by WorldSimulator during maintenance
    /// </summary>
    public void ProcessAllPrisonerActivities()
    {
        var prisoners = UsurperRemake.Systems.NPCSpawnSystem.Instance?.GetPrisoners();
        if (prisoners == null) return;

        foreach (var prisoner in prisoners)
        {
            ProcessNPCPrisonerActivity(prisoner);

            // Decrease prison days
            if (prisoner.DaysInPrison > 0)
            {
                prisoner.DaysInPrison--;
                if (prisoner.DaysInPrison <= 0)
                {
                    // Release prisoner
                    UsurperRemake.Systems.NPCSpawnSystem.Instance?.ReleaseNPC(prisoner);
                    NewsSystem.Instance?.Newsy(true, $"{prisoner.Name} has been released from prison.");
                }
            }
        }
    }
}
