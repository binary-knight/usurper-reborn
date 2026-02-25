using System;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Systems;
using UsurperRemake.BBS;

namespace UsurperRemake.Locations;

/// <summary>
/// The Level Master – lets players advance in level when they have enough experience.
/// Features three alignment-based masters with hidden stat bonuses:
///  - Good Master: Grants bonus Wisdom and Charisma
///  - Neutral Master: Grants bonus Intelligence and Dexterity
///  - Evil Master: Grants bonus Strength and Constitution
///
/// Each class also receives stat bonuses appropriate to their role:
///  - Magic classes (Magician, Cleric, Sage, Alchemist): +Intelligence, +Wisdom, +Mana
///  - Warrior classes (Warrior, Barbarian, Paladin): +Strength, +Constitution, +HP
///  - Agile classes (Assassin, Ranger, Jester, Bard): +Dexterity, +Agility, +Stamina
/// </summary>
public class LevelMasterLocation : BaseLocation
{
    // The three alignment-based masters
    private static readonly MasterInfo GoodMaster = new(
        "Seraphina the Radiant",
        "A celestial figure surrounded by soft golden light",
        "bright_yellow",
        PlayerAlignment.Good
    );

    private static readonly MasterInfo NeutralMaster = new(
        "Zharkon the Grey",
        "A mysterious sage wrapped in grey robes, eyes glowing with arcane knowledge",
        "gray",
        PlayerAlignment.Neutral
    );

    private static readonly MasterInfo EvilMaster = new(
        "Malachar the Dark",
        "A shadowy figure emanating an aura of dark power",
        "bright_red",
        PlayerAlignment.Evil
    );

    private MasterInfo currentMaster;
    private PlayerAlignment playerAlignment;

    public LevelMasterLocation() : base(GameLocation.Master, "Level Master's Sanctum",
        "A mystical chamber where warriors seek to transcend their limits.")
    {
    }

    protected override void SetupLocation()
    {
        PossibleExits.Add(GameLocation.MainStreet);
    }

    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        // Determine player alignment before entering
        playerAlignment = DetermineAlignment(player);
        currentMaster = GetMasterForAlignment(playerAlignment);

        await base.EnterLocation(player, term);
    }

    /// <summary>
    /// Determines player alignment based on Chivalry/Darkness ratio
    /// </summary>
    private static PlayerAlignment DetermineAlignment(Character player)
    {
        long chivalry = player.Chivalry;
        long darkness = player.Darkness;

        // If both are zero or very close, default to neutral
        if (chivalry == 0 && darkness == 0)
            return PlayerAlignment.Neutral;

        // Calculate the ratio
        double total = chivalry + darkness;
        if (total == 0)
            return PlayerAlignment.Neutral;

        double chivalryRatio = chivalry / total;
        double darknessRatio = darkness / total;

        // If within 20% of each other, consider neutral
        if (Math.Abs(chivalryRatio - darknessRatio) < 0.2)
            return PlayerAlignment.Neutral;

        return chivalryRatio > darknessRatio ? PlayerAlignment.Good : PlayerAlignment.Evil;
    }

    /// <summary>
    /// Gets the appropriate master for the player's alignment
    /// </summary>
    private static MasterInfo GetMasterForAlignment(PlayerAlignment alignment)
    {
        return alignment switch
        {
            PlayerAlignment.Good => GoodMaster,
            PlayerAlignment.Evil => EvilMaster,
            _ => NeutralMaster
        };
    }

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();

        if (DoorMode.IsInDoorMode)
        {
            DisplayLocationBBS();
            return;
        }

        // Header - standardized format
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"LEVEL MASTER'S SANCTUM".PadLeft((77 + 22) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.SetColor(currentMaster.Color);
        terminal.WriteLine($"Master: {currentMaster.Name}");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(currentMaster.Description);
        terminal.WriteLine("");

        // Show alignment-specific greeting
        terminal.SetColor(currentMaster.Color);
        switch (playerAlignment)
        {
            case PlayerAlignment.Good:
                terminal.WriteLine($"\"Welcome, noble {currentPlayer.DisplayName}. Your light shines brightly.\"");
                break;
            case PlayerAlignment.Evil:
                terminal.WriteLine($"\"Ah, {currentPlayer.DisplayName}... The darkness within you grows stronger.\"");
                break;
            default:
                terminal.WriteLine($"\"Greetings, seeker of balance. What brings you here, {currentPlayer.DisplayName}?\"");
                break;
        }
        terminal.WriteLine("");

        ShowNPCsInLocation();

        // Show XP status
        DisplayExperienceStatus();

        terminal.SetColor("cyan");
        terminal.WriteLine("Services:");
        terminal.WriteLine("");

        // Row 1
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("evel Raise – advance if you have earned enough experience");

        // Row 2
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("bilities – view and learn combat abilities or spells");

        // Row 3
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine($"raining – improve your skills (Points: {currentPlayer.TrainingPoints})");

        // Row 4
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("rystal Ball – scry information about other characters");

        // Row 5
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("H");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("elp Team Member – assist a teammate in levelling");

        // Row 6
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("tatus – view your statistics");

        terminal.WriteLine("");

        // Navigation
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("eturn to Main Street");
        terminal.WriteLine("");

        ShowStatusLine();
    }

    /// <summary>
    /// Display the player's experience status
    /// </summary>
    private void DisplayExperienceStatus()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine($"Your Experience: {currentPlayer.Experience:N0}");

        if (currentPlayer.Level >= GameConfig.MaxLevel)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("You have reached the pinnacle of mortal power!");
        }
        else
        {
            long nextLevelXP = GetExperienceForLevel(currentPlayer.Level + 1);
            long xpNeeded = nextLevelXP - currentPlayer.Experience;

            if (xpNeeded <= 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"* You are ready to advance to level {currentPlayer.Level + 1}! *");
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine($"Experience needed for level {currentPlayer.Level + 1}: {xpNeeded:N0}");
            }
        }
        terminal.WriteLine("");
    }

    /// <summary>
    /// BBS compact display for 80x25 terminal
    /// </summary>
    private void DisplayLocationBBS()
    {
        ShowBBSHeader("LEVEL MASTER'S SANCTUM");
        // 1-line master + XP status
        terminal.SetColor(currentMaster.Color);
        terminal.Write($" {currentMaster.Name}");
        terminal.SetColor("gray");
        terminal.Write("  XP:");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Experience:N0}");
        if (currentPlayer.Level >= GameConfig.MaxLevel)
        {
            terminal.SetColor("bright_magenta");
            terminal.Write(" MAX LEVEL!");
        }
        else
        {
            long nextXP = GetExperienceForLevel(currentPlayer.Level + 1);
            long xpNeeded = nextXP - currentPlayer.Experience;
            if (xpNeeded <= 0)
            {
                terminal.SetColor("bright_green");
                terminal.Write(" *READY TO ADVANCE!*");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.Write($"  Need:");
                terminal.SetColor("white");
                terminal.Write($"{xpNeeded:N0}");
            }
        }
        terminal.Write($"  Train:");
        terminal.SetColor("yellow");
        terminal.Write($"{currentPlayer.TrainingPoints}pts");
        terminal.WriteLine("");
        ShowBBSNPCs();
        // Menu rows
        ShowBBSMenuRow(("L", "bright_yellow", "Level Raise"), ("A", "bright_yellow", "Abilities"), ("T", "bright_yellow", "Training"));
        ShowBBSMenuRow(("C", "bright_yellow", "Crystal Ball"), ("H", "bright_yellow", "Help Team"), ("R", "bright_yellow", "Return"));
        ShowBBSFooter();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        switch (choice.ToUpperInvariant())
        {
            case "L":
                await AttemptLevelRaise();
                return false;
            case "A":
                await ShowAbilitiesMenu();
                return false;
            case "T":
                await ShowTrainingMenu();
                return false;
            case "C":
                await UseCrystalBall();
                return false;
            case "H":
                await HelpTeamMember();
                return false;
            case "R":
            case "Q":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;
            default:
                return await base.ProcessChoice(choice);
        }
    }

    /// <summary>
    /// Show the abilities menu - spells for casters, combat abilities for others
    /// </summary>
    private async Task ShowAbilitiesMenu()
    {
        terminal.SetColor(currentMaster.Color);

        if (ClassAbilitySystem.IsSpellcaster(currentPlayer.Class))
        {
            terminal.WriteLine($"\"{currentPlayer.DisplayName}, let me teach you the arcane arts...\"");
            await Task.Delay(800);
            await SpellLearningSystem.ShowSpellLearningMenu(currentPlayer, terminal);
        }
        else
        {
            terminal.WriteLine($"\"Come, {currentPlayer.DisplayName}. Let me show you the way of the warrior...\"");
            await Task.Delay(800);
            await ClassAbilitySystem.ShowAbilityLearningMenu(currentPlayer, terminal);
        }
    }

    /// <summary>
    /// Show the training menu - D&D style proficiency training
    /// </summary>
    private async Task ShowTrainingMenu()
    {
        terminal.SetColor(currentMaster.Color);
        terminal.WriteLine($"\"Practice makes perfect, {currentPlayer.DisplayName}. Let us hone your skills...\"");
        await Task.Delay(800);
        await TrainingSystem.ShowTrainingMenu(currentPlayer, terminal);
    }

    #region Level Raise Logic

    private async Task AttemptLevelRaise()
    {
        int levelsRaised = 0;
        int startLevel = currentPlayer.Level;

        int totalTrainingPoints = 0;

        while (currentPlayer.Level < GameConfig.MaxLevel &&
               currentPlayer.Experience >= GetExperienceForLevel(currentPlayer.Level + 1))
        {
            // Raise one level
            int newLevel = currentPlayer.Level + 1;

            // Apply class-based stat increases
            ApplyClassBasedStatIncreases();

            // Apply hidden master bonuses
            ApplyMasterBonuses();

            // Award training points for the new level
            int trainingPoints = TrainingSystem.CalculateTrainingPointsPerLevel(currentPlayer);
            currentPlayer.TrainingPoints += trainingPoints;
            totalTrainingPoints += trainingPoints;

            currentPlayer.RaiseLevel(newLevel);
            levelsRaised++;
        }

        if (levelsRaised == 0)
        {
            long needed = GetExperienceForLevel(currentPlayer.Level + 1) - currentPlayer.Experience;
            terminal.SetColor("yellow");
            terminal.WriteLine($"You still need {needed:N0} experience to reach level {currentPlayer.Level + 1}.");
            await Task.Delay(2000);
        }
        else
        {
            // Full HP and Mana restore on level up
            currentPlayer.HP = currentPlayer.MaxHP;
            currentPlayer.Mana = currentPlayer.MaxMana;

            // Track statistics for each level gained
            for (int i = 0; i < levelsRaised; i++)
            {
                currentPlayer.Statistics.RecordLevelUp(startLevel + i + 1);
            }

            // Track telemetry for level up with full stats
            UsurperRemake.Systems.TelemetrySystem.Instance.TrackLevelUp(
                currentPlayer.Level,
                currentPlayer.Class.ToString(),
                (int)currentPlayer.Strength,
                (int)currentPlayer.Dexterity,
                (int)currentPlayer.Constitution,
                (int)currentPlayer.Intelligence,
                (int)currentPlayer.Wisdom,
                (int)currentPlayer.Charisma
            );

            // Update user properties in PostHog so level is current for dashboards
            UsurperRemake.Systems.TelemetrySystem.Instance.SetUserProperties(
                new System.Collections.Generic.Dictionary<string, object>
                {
                    ["level"] = currentPlayer.Level,
                    ["max_level_reached"] = currentPlayer.Level
                }
            );

            // Log level up
            UsurperRemake.Systems.DebugLogger.Instance.LogLevelUp(currentPlayer.Name, startLevel, currentPlayer.Level);

            // Online news: announce level ups at milestones
            if (UsurperRemake.Systems.OnlineStateManager.IsActive)
            {
                var displayName = currentPlayer.Name2 ?? currentPlayer.Name1;
                if (currentPlayer.Level % 5 == 0 || currentPlayer.Level <= 3)
                    _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                        $"{displayName} has reached level {currentPlayer.Level}!", "combat");
            }

            // Auto-add newly unlocked spells/abilities to empty quickbar slots
            GameEngine.QuickbarAddNewSkills(currentPlayer);

            // Display level up celebration with training points earned
            await DisplayLevelUpCelebration(levelsRaised, startLevel, totalTrainingPoints);

            // Auto-save after leveling up - major milestone
            await SaveSystem.Instance.AutoSave(currentPlayer);
        }
    }

    /// <summary>
    /// Displays an elaborate level up celebration message
    /// </summary>
    private async Task DisplayLevelUpCelebration(int levelsRaised, int startLevel, int trainingPointsEarned)
    {
        terminal.ClearScreen();

        // Display dramatic ANSI art for level up
        await UsurperRemake.UI.ANSIArt.DisplayArtAnimated(terminal, UsurperRemake.UI.ANSIArt.LevelUp, 30);
        terminal.WriteLine("");

        terminal.SetColor("bright_green");
        if (levelsRaised == 1)
        {
            terminal.WriteLine($"  You have advanced to level {currentPlayer.Level}!");
        }
        else
        {
            terminal.WriteLine($"  You have advanced {levelsRaised} levels! ({startLevel} -> {currentPlayer.Level})");
        }
        terminal.WriteLine("");

        // Check for milestone levels and give bonuses
        await CheckMilestoneBonuses(startLevel, currentPlayer.Level);

        terminal.SetColor(currentMaster.Color);
        switch (playerAlignment)
        {
            case PlayerAlignment.Good:
                terminal.WriteLine($"  {currentMaster.Name} places a gentle hand on your shoulder:");
                terminal.WriteLine("  \"The light within you grows ever stronger. Use it wisely.\"");
                break;
            case PlayerAlignment.Evil:
                terminal.WriteLine($"  {currentMaster.Name}'s eyes gleam with dark approval:");
                terminal.WriteLine("  \"Yes... embrace your power. The weak will tremble before you.\"");
                break;
            default:
                terminal.WriteLine($"  {currentMaster.Name} nods with quiet respect:");
                terminal.WriteLine("  \"Balance in all things. You walk the true path.\"");
                break;
        }
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("  Your body and mind surge with newfound power!");
        terminal.WriteLine("  Your health and mana have been fully restored.");
        terminal.WriteLine("");

        // Show training points earned
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"  +{trainingPointsEarned} Training Points earned!");
        terminal.WriteLine($"  Total Training Points: {currentPlayer.TrainingPoints}");
        terminal.SetColor("gray");
        terminal.WriteLine("  (Use (T)raining at the Level Master to improve your skills)");
        terminal.WriteLine("");

        await terminal.PressAnyKey("  Press Enter to continue...");
    }

    /// <summary>
    /// Check and award milestone bonuses for key levels
    /// </summary>
    private async Task CheckMilestoneBonuses(int startLevel, int endLevel)
    {
        // Milestone levels and their bonuses
        var milestones = new (int level, string title, string hint, long goldBonus, int potionBonus)[]
        {
            (5, "Adventurer", "You can now venture deeper into the dungeons!", 500, 3),
            (10, "Veteran", "The Seven Seals await you on floors 15, 30, 45, 60, 80, 99, and 100!", 1000, 5),
            (25, "Champion", "Monsters now fear your name!", 5000, 10),
            (50, "Hero", "You are ready to face the Old Gods!", 25000, 20),
            (75, "Legend", "Your power rivals the ancient heroes!", 75000, 30),
            (100, "GODSLAYER", "You have reached the pinnacle of mortal power!", 250000, 50)
        };

        foreach (var (level, title, hint, goldBonus, potionBonus) in milestones)
        {
            // Check if we crossed this milestone
            if (startLevel < level && endLevel >= level)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗");
                terminal.WriteLine($"║              * MILESTONE REACHED: Level {level,3} *                     ║");
                terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝");
                terminal.WriteLine("");

                terminal.SetColor("bright_white");
                terminal.WriteLine($"  You have earned the title: {title}!");
                terminal.WriteLine("");

                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {hint}");
                terminal.WriteLine("");

                // Award bonuses
                currentPlayer.Gold += goldBonus;
                currentPlayer.Healing = Math.Min(currentPlayer.MaxPotions, currentPlayer.Healing + potionBonus);

                terminal.SetColor("bright_green");
                terminal.WriteLine($"  BONUS: +{goldBonus:N0} Gold!");
                terminal.WriteLine($"  BONUS: +{potionBonus} Healing Potions!");
                terminal.WriteLine("");

                await Task.Delay(1000);
            }
        }
    }

    /// <summary>
    /// Apply class-based stat increases based on character class type
    /// </summary>
    private void ApplyClassBasedStatIncreases()
    {
        // Base stats that everyone gets
        currentPlayer.BaseMaxHP += 5;
        currentPlayer.BaseDefence += 1;
        currentPlayer.BaseStamina += 1;

        // Class-specific bonuses
        switch (currentPlayer.Class)
        {
            // Magic classes - focus on Intelligence, Wisdom, Mana
            case CharacterClass.Magician:
                currentPlayer.BaseIntelligence += 4;
                currentPlayer.BaseWisdom += 3;
                currentPlayer.BaseMaxMana += 15;
                currentPlayer.BaseMaxHP += 3;
                currentPlayer.BaseStrength += 1;
                currentPlayer.BaseConstitution += 1;
                break;

            case CharacterClass.Cleric:
                currentPlayer.BaseWisdom += 4;
                currentPlayer.BaseIntelligence += 2;
                currentPlayer.BaseMaxMana += 12;
                currentPlayer.BaseMaxHP += 6;
                currentPlayer.BaseStrength += 2;
                currentPlayer.BaseConstitution += 2;
                break;

            case CharacterClass.Sage:
                currentPlayer.BaseIntelligence += 5;
                currentPlayer.BaseWisdom += 4;
                currentPlayer.BaseMaxMana += 18;
                currentPlayer.BaseMaxHP += 2;
                currentPlayer.BaseStrength += 1;
                break;

            case CharacterClass.Alchemist:
                currentPlayer.BaseIntelligence += 3;
                currentPlayer.BaseWisdom += 2;
                currentPlayer.BaseDexterity += 2;
                currentPlayer.BaseMaxMana += 10;
                currentPlayer.BaseMaxHP += 5;
                currentPlayer.BaseConstitution += 2;
                break;

            // Warrior classes - focus on Strength, Constitution, HP
            case CharacterClass.Warrior:
                currentPlayer.BaseStrength += 4;
                currentPlayer.BaseConstitution += 3;
                currentPlayer.BaseMaxHP += 12;
                currentPlayer.BaseDexterity += 2;
                currentPlayer.BaseDefence += 2;
                break;

            case CharacterClass.Barbarian:
                currentPlayer.BaseStrength += 5;
                currentPlayer.BaseConstitution += 4;
                currentPlayer.BaseMaxHP += 15;
                currentPlayer.BaseStamina += 2;
                break;

            case CharacterClass.Paladin:
                currentPlayer.BaseStrength += 3;
                currentPlayer.BaseConstitution += 3;
                currentPlayer.BaseWisdom += 2;
                currentPlayer.BaseCharisma += 2;
                currentPlayer.BaseMaxHP += 10;
                currentPlayer.BaseMaxMana += 5;
                currentPlayer.BaseDefence += 1;
                break;

            // Agile classes - focus on Dexterity, Agility, Stamina
            case CharacterClass.Assassin:
                currentPlayer.BaseDexterity += 4;
                currentPlayer.BaseAgility += 3;
                currentPlayer.BaseStrength += 2;
                currentPlayer.BaseMaxHP += 6;
                currentPlayer.BaseStamina += 2;
                break;

            case CharacterClass.Ranger:
                currentPlayer.BaseDexterity += 3;
                currentPlayer.BaseAgility += 3;
                currentPlayer.BaseStrength += 2;
                currentPlayer.BaseWisdom += 1;
                currentPlayer.BaseMaxHP += 8;
                currentPlayer.BaseStamina += 2;
                break;

            case CharacterClass.Jester:
                currentPlayer.BaseDexterity += 3;
                currentPlayer.BaseAgility += 3;
                currentPlayer.BaseCharisma += 3;
                currentPlayer.BaseMaxHP += 5;
                currentPlayer.BaseStamina += 2;
                break;

            case CharacterClass.Bard:
                currentPlayer.BaseCharisma += 4;
                currentPlayer.BaseDexterity += 2;
                currentPlayer.BaseAgility += 2;
                currentPlayer.BaseIntelligence += 2;
                currentPlayer.BaseMaxHP += 5;
                currentPlayer.BaseMaxMana += 5;
                break;

            // NG+ Prestige Classes — strictly stronger, both spells AND abilities
            case CharacterClass.Tidesworn:
                currentPlayer.BaseStrength += 4;
                currentPlayer.BaseConstitution += 4;
                currentPlayer.BaseWisdom += 3;
                currentPlayer.BaseCharisma += 2;
                currentPlayer.BaseDefence += 2;
                currentPlayer.BaseMaxHP += 13;
                currentPlayer.BaseMaxMana += 8;
                break;

            case CharacterClass.Wavecaller:
                currentPlayer.BaseCharisma += 5;
                currentPlayer.BaseWisdom += 4;
                currentPlayer.BaseIntelligence += 3;
                currentPlayer.BaseConstitution += 2;
                currentPlayer.BaseAgility += 2;
                currentPlayer.BaseMaxHP += 7;
                currentPlayer.BaseMaxMana += 14;
                break;

            case CharacterClass.Cyclebreaker:
                currentPlayer.BaseStrength += 3;
                currentPlayer.BaseIntelligence += 3;
                currentPlayer.BaseWisdom += 3;
                currentPlayer.BaseDexterity += 3;
                currentPlayer.BaseConstitution += 3;
                currentPlayer.BaseAgility += 2;
                currentPlayer.BaseMaxHP += 9;
                currentPlayer.BaseMaxMana += 10;
                break;

            case CharacterClass.Abysswarden:
                currentPlayer.BaseDexterity += 5;
                currentPlayer.BaseStrength += 4;
                currentPlayer.BaseAgility += 4;
                currentPlayer.BaseIntelligence += 3;
                currentPlayer.BaseConstitution += 2;
                currentPlayer.BaseMaxHP += 8;
                currentPlayer.BaseMaxMana += 10;
                break;

            case CharacterClass.Voidreaver:
                currentPlayer.BaseStrength += 5;
                currentPlayer.BaseIntelligence += 5;
                currentPlayer.BaseDexterity += 4;
                currentPlayer.BaseAgility += 3;
                currentPlayer.BaseStamina += 2;
                currentPlayer.BaseMaxHP += 6;
                currentPlayer.BaseMaxMana += 12;
                break;

            default:
                // Fallback for any undefined class
                currentPlayer.BaseStrength += 2;
                currentPlayer.BaseConstitution += 2;
                currentPlayer.BaseMaxHP += 8;
                break;
        }

        // Recalculate all stats from base values
        currentPlayer.RecalculateStats();
    }

    /// <summary>
    /// Apply hidden master-specific bonuses based on alignment
    /// These are not shown to the player but provide small advantages
    /// </summary>
    private void ApplyMasterBonuses()
    {
        switch (playerAlignment)
        {
            case PlayerAlignment.Good:
                // Good master grants bonus Wisdom and Charisma
                currentPlayer.BaseWisdom += 1;
                currentPlayer.BaseCharisma += 1;
                // Small healing boost
                currentPlayer.BaseMaxHP += 2;
                break;

            case PlayerAlignment.Evil:
                // Evil master grants bonus Strength and Constitution
                currentPlayer.BaseStrength += 1;
                currentPlayer.BaseConstitution += 1;
                // Small damage boost
                currentPlayer.BaseMaxHP += 1;
                break;

            case PlayerAlignment.Neutral:
                // Neutral master grants bonus Intelligence and Dexterity
                currentPlayer.BaseIntelligence += 1;
                currentPlayer.BaseDexterity += 1;
                // Small mana boost
                currentPlayer.BaseMaxMana += 3;
                break;
        }

        // Recalculate after master bonuses
        currentPlayer.RecalculateStats();
    }

    /// <summary>
    /// Experience required to have <paramref name="level"/> (cumulative), compatible with NPC formula.
    /// </summary>
    private static long GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        long exp = 0;
        for (int i = 2; i <= level; i++)
        {
            // Softened curve: level^2.0 * 50 (rebalanced v0.41.4)
            exp += (long)(Math.Pow(i, 2.0) * 50);
        }
        return exp;
    }

    #endregion

    #region Crystal Ball and Team Help

    /// <summary>
    /// Crystal Ball - Scry information about other characters in the game
    /// </summary>
    private async Task UseCrystalBall()
    {
        // Get list of all characters (NPCs)
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs?.ToList();
        if (npcs == null || npcs.Count == 0)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine("The crystal ball shows only swirling mists... No souls to scry.");
            await terminal.PressAnyKey();
            return;
        }

        const int pageSize = 15;
        int currentPage = 0;
        int totalPages = (npcs.Count + pageSize - 1) / pageSize;

        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor(currentMaster.Color);
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                          THE CRYSTAL BALL                                   ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine($"\"{currentMaster.Name}\" gestures to a glowing crystal orb...");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine($"Who do you wish to scry? (Page {currentPage + 1} of {totalPages})");
            terminal.WriteLine("");

            // Show numbered list of NPCs for current page
            int startIndex = currentPage * pageSize;
            int endIndex = Math.Min(startIndex + pageSize, npcs.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var npc = npcs[i];
                string status = npc.IsDead ? " [DEAD]" : "";
                terminal.WriteLine($"{i + 1,3}. {npc.Name} - Level {npc.Level} {npc.Class}{status}");
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"Showing {startIndex + 1}-{endIndex} of {npcs.Count} souls");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            string navOptions = "";
            if (currentPage > 0) navOptions += "[P]rev  ";
            if (currentPage < totalPages - 1) navOptions += "[N]ext  ";
            terminal.WriteLine($"{navOptions}[#] Select by number  [Q]uit");
            terminal.WriteLine("");
            terminal.Write("Choice: ");
            terminal.SetColor("white");

            string input = await terminal.ReadLineAsync();
            input = input?.Trim().ToUpper() ?? "";

            if (input == "Q" || input == "0" || string.IsNullOrEmpty(input))
            {
                terminal.SetColor("gray");
                terminal.WriteLine("The mists close around the ball once more...");
                await terminal.PressAnyKey();
                return;
            }

            if (input == "N" && currentPage < totalPages - 1)
            {
                currentPage++;
                continue;
            }

            if (input == "P" && currentPage > 0)
            {
                currentPage--;
                continue;
            }

            // Try to parse as number
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= npcs.Count)
            {
                var targetNPC = npcs[choice - 1];
                await DisplayScryingResult(targetNPC);
                return;
            }

            terminal.SetColor("red");
            terminal.WriteLine("Invalid choice. Try again.");
            await Task.Delay(1000);
        }
    }

    /// <summary>
    /// Display detailed information about a scried character
    /// </summary>
    private async Task DisplayScryingResult(NPC target)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                        VISIONS IN THE CRYSTAL                               ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        await Task.Delay(500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"The mists part to reveal: {target.Name}");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  Class: {target.Class}");
        terminal.WriteLine($"  Level: {target.Level}");
        terminal.WriteLine($"  Status: {(target.IsAlive ? "Alive" : "Dead")}");
        terminal.WriteLine($"  Location: {target.CurrentLocation}");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("  Combat Stats:");
        terminal.SetColor("white");
        terminal.WriteLine($"    Strength: {target.Strength}   Defence: {target.Defence}");
        terminal.WriteLine($"    Agility: {target.Agility}    Dexterity: {target.Dexterity}");
        terminal.WriteLine($"    HP: {target.HP}/{target.MaxHP}  Mana: {target.Mana}/{target.MaxMana}");
        terminal.WriteLine("");

        terminal.SetColor("green");
        terminal.WriteLine("  Wealth & Status:");
        terminal.SetColor("white");
        terminal.WriteLine($"    Gold: {target.Gold:N0}");
        terminal.WriteLine($"    Team: {(string.IsNullOrEmpty(target.Team) ? "None" : target.Team)}");
        terminal.WriteLine($"    Alignment: {(target.Chivalry > target.Darkness ? "Good" : target.Darkness > target.Chivalry ? "Evil" : "Neutral")}");

        if (target.Brain != null)
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  Personality: {target.Brain.Personality}");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("The vision fades as the mists return...");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Help Team Member - Donate experience or gold to help a teammate level up
    /// </summary>
    private async Task HelpTeamMember()
    {
        terminal.ClearScreen();
        terminal.SetColor(currentMaster.Color);
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                        HELP ALLY                                            ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Find NPC teammates (only if player is on a team)
        var npcTeammates = new System.Collections.Generic.List<NPC>();
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            npcTeammates = NPCSpawnSystem.Instance?.ActiveNPCs?
                .Where(n => n.Team == currentPlayer.Team && n.IsAlive && n.Name != currentPlayer.Name2)
                .ToList() ?? new System.Collections.Generic.List<NPC>();
        }

        // Find active companions (always available if recruited)
        var companions = CompanionSystem.Instance?.GetActiveCompanions()
            .Where(c => !c.IsDead && c.Level < GameConfig.MaxLevel)
            .ToList() ?? new System.Collections.Generic.List<Companion>();

        // Build combined list of shareable allies
        var shareableAllies = new System.Collections.Generic.List<(string Name, int Level, long Experience, bool IsCompanion, object Data)>();

        foreach (var npc in npcTeammates)
        {
            shareableAllies.Add((npc.Name, npc.Level, npc.Experience, false, npc));
        }

        foreach (var companion in companions)
        {
            shareableAllies.Add((companion.Name, companion.Level, companion.Experience, true, companion));
        }

        if (shareableAllies.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"\"{currentMaster.Name}\" looks into the distance...");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("\"You have no allies to assist. Recruit companions or join a team first.\"");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{currentMaster.Name}\" nods approvingly...");
        terminal.WriteLine("");
        terminal.WriteLine("\"Helping your allies grow stronger is a noble pursuit.\"");
        terminal.WriteLine("\"You may share your wisdom (experience) to help them advance.\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("Select an ally to help:");
        terminal.WriteLine("");

        for (int i = 0; i < shareableAllies.Count; i++)
        {
            var ally = shareableAllies[i];
            long xpToNext = GetExperienceForLevel(ally.Level + 1) - ally.Experience;
            string allyType = ally.IsCompanion ? " [Companion]" : "";
            terminal.WriteLine($"{i + 1}. {ally.Name}{allyType} - Level {ally.Level} ({xpToNext:N0} XP to next level)");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Select ally (0 to cancel): ");
        terminal.SetColor("white");

        string input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > shareableAllies.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You decide to keep your wisdom for yourself...");
            await terminal.PressAnyKey();
            return;
        }

        var selectedAlly = shareableAllies[choice - 1];
        long xpNeeded = GetExperienceForLevel(selectedAlly.Level + 1) - selectedAlly.Experience;

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"{selectedAlly.Name} needs {xpNeeded:N0} experience to reach level {selectedAlly.Level + 1}.");
        terminal.WriteLine($"You have {currentPlayer.Experience:N0} experience.");
        terminal.WriteLine("");

        // Calculate max they can give (half their XP, but not more than they have)
        long maxGive = currentPlayer.Experience / 2;
        if (maxGive < 1)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You don't have enough experience to share!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write($"How much XP to share (max {maxGive:N0}): ");
        terminal.SetColor("white");

        string xpInput = await terminal.ReadLineAsync();
        if (!long.TryParse(xpInput, out long xpToGive) || xpToGive < 1 || xpToGive > maxGive)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Invalid amount. No experience shared.");
            await terminal.PressAnyKey();
            return;
        }

        // Transfer experience
        currentPlayer.Experience -= xpToGive;

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"You share {xpToGive:N0} experience with {selectedAlly.Name}!");

        // Handle leveling up based on ally type
        int levelsGained = 0;
        var random = new Random();

        if (selectedAlly.IsCompanion)
        {
            // Handle companion XP sharing
            var companion = (Companion)selectedAlly.Data;
            companion.Experience += xpToGive;

            // Calculate how many levels should be gained
            int levelsToGain = 0;
            long tempXP = companion.Experience;
            int tempLevel = companion.Level;
            while (tempXP >= GetExperienceForLevel(tempLevel + 1) && tempLevel < GameConfig.MaxLevel)
            {
                tempLevel++;
                levelsToGain++;
            }

            // Apply level-ups with proper stat gains through CompanionSystem
            if (levelsToGain > 0)
            {
                levelsGained = CompanionSystem.Instance?.LevelUpCompanion(companion.Id, levelsToGain) ?? 0;
            }

            if (levelsGained > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"{companion.Name} has grown to level {companion.Level}!");
                terminal.WriteLine($"HP: {companion.BaseStats.HP} | ATK: {companion.BaseStats.Attack} | DEF: {companion.BaseStats.Defense}");
                terminal.WriteLine("Their combat effectiveness has increased!");
            }
        }
        else
        {
            // Handle NPC teammate XP sharing
            var recipient = (NPC)selectedAlly.Data;
            recipient.Experience += xpToGive;
            int startLevel = recipient.Level;

            while (recipient.Experience >= GetExperienceForLevel(recipient.Level + 1) && recipient.Level < GameConfig.MaxLevel)
            {
                recipient.Level++;
                levelsGained++;

                // Update base stats on level up (matches WorldSimulator.NPCLevelUp behavior)
                recipient.BaseMaxHP += 10 + random.Next(5, 15);
                recipient.BaseStrength += random.Next(1, 3);
                recipient.BaseDefence += random.Next(1, 2);
            }

            if (levelsGained > 0)
            {
                // Recalculate all stats from base values
                recipient.RecalculateStats();

                // Restore HP/Mana to full on level up
                recipient.HP = recipient.MaxHP;
                recipient.Mana = recipient.MaxMana;

                terminal.SetColor("bright_yellow");
                if (levelsGained == 1)
                {
                    terminal.WriteLine($"{recipient.Name} has reached level {recipient.Level}!");
                }
                else
                {
                    terminal.WriteLine($"{recipient.Name} has gained {levelsGained} levels! ({startLevel} -> {recipient.Level})");
                }
                NewsSystem.Instance?.Newsy(true, $"{recipient.Name} advanced to level {recipient.Level} with help from {currentPlayer.Name2}!");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{currentMaster.Name}\" smiles warmly...");
        terminal.SetColor("white");
        terminal.WriteLine("\"True strength is found in lifting others.\"");

        await terminal.PressAnyKey();
    }

    #endregion
}

/// <summary>
/// Represents a level master's information
/// </summary>
public record MasterInfo(string Name, string Description, string Color, PlayerAlignment Alignment);

/// <summary>
/// Player alignment for determining which master to use
/// </summary>
public enum PlayerAlignment
{
    Good,
    Neutral,
    Evil
}
