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
    public static PlayerAlignment DetermineAlignment(Character player)
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

    protected override string GetMudPromptName() => "Level Master";

    private void DisplayLocationSR()
    {
        terminal.ClearScreen();

        terminal.WriteLine(Loc.Get("level_master.header"), "bright_cyan");
        terminal.WriteLine(Loc.Get("level_master.master_label", currentMaster.Name), currentMaster.Color);
        terminal.WriteLine("");

        // XP status
        terminal.WriteLine(Loc.Get("level_master.your_xp", $"{currentPlayer.Experience:N0}"), "cyan");
        if (currentPlayer.Level >= GameConfig.MaxLevel)
        {
            terminal.WriteLine(Loc.Get("level_master.max_level"), "bright_magenta");
        }
        else
        {
            long nextLevelXP = GetExperienceForLevel(currentPlayer.Level + 1);
            long xpNeeded = nextLevelXP - currentPlayer.Experience;
            if (xpNeeded <= 0)
                terminal.WriteLine(Loc.Get("level_master.ready_advance", (currentPlayer.Level + 1).ToString()), "bright_green");
            else
                terminal.WriteLine(Loc.Get("level_master.need_xp", $"{xpNeeded:N0}", (currentPlayer.Level + 1).ToString()), "white");
        }
        terminal.WriteLine(Loc.Get("level_master.training_points", currentPlayer.TrainingPoints.ToString()), "bright_magenta");
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("level_master.services"), "cyan");
        WriteSRMenuOption("L", Loc.Get("level_master.level_raise"));
        WriteSRMenuOption("A", $"{Loc.Get("level_master.abilities")} - {Loc.Get("level_master.abilities_desc")}");
        WriteSRMenuOption("T", $"{Loc.Get("level_master.training")} - {Loc.Get("level_master.training_desc", currentPlayer.TrainingPoints.ToString())}");
        WriteSRMenuOption("C", $"{Loc.Get("level_master.crystal_ball")} - {Loc.Get("level_master.crystal_desc")}");
        WriteSRMenuOption("H", $"{Loc.Get("level_master.help_team")} - {Loc.Get("level_master.help_desc")}");
        WriteSRMenuOption("S", Loc.Get("level_master.sr_status_desc"));
        terminal.WriteLine("");
        WriteSRMenuOption("R", Loc.Get("shop.return"));
        terminal.WriteLine("");

        ShowStatusLine();
    }

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();

        if (IsScreenReader)
        {
            DisplayLocationSR();
            return;
        }

        if (IsBBSSession)
        {
            DisplayLocationBBS();
            return;
        }

        // Header - standardized format
        WriteBoxHeader(Loc.Get("level_master.header"), "bright_cyan");
        terminal.WriteLine("");
        terminal.SetColor(currentMaster.Color);
        terminal.WriteLine(Loc.Get("level_master.master_label", currentMaster.Name));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(currentMaster.Description);
        terminal.WriteLine("");

        // Show alignment-specific greeting
        terminal.SetColor(currentMaster.Color);
        switch (playerAlignment)
        {
            case PlayerAlignment.Good:
                terminal.WriteLine(Loc.Get("level_master.greet_good", currentPlayer.DisplayName));
                break;
            case PlayerAlignment.Evil:
                terminal.WriteLine(Loc.Get("level_master.greet_evil", currentPlayer.DisplayName));
                break;
            default:
                terminal.WriteLine(Loc.Get("level_master.greet_neutral", currentPlayer.DisplayName));
                break;
        }
        terminal.WriteLine("");

        ShowNPCsInLocation();

        // Show XP status
        DisplayExperienceStatus();

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("level_master.services"));
        terminal.WriteLine("");

        // Row 1 - Level Raise
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.menu_level_raise"));

        // Row 2 - Abilities
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.menu_abilities", Loc.Get("level_master.abilities_desc")));

        // Row 3 - Training
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.menu_training", Loc.Get("level_master.training_desc", currentPlayer.TrainingPoints.ToString())));

        // Row 4 - Crystal Ball
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.menu_crystal", Loc.Get("level_master.crystal_desc")));

        // Row 5 - Help Team
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("H");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.menu_help_team", Loc.Get("level_master.help_desc")));

        // Row 6 - Status
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.menu_status"));

        terminal.WriteLine("");

        // Navigation
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.menu_return"));
        terminal.WriteLine("");

        ShowStatusLine();
    }

    /// <summary>
    /// Display the player's experience status
    /// </summary>
    private void DisplayExperienceStatus()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("level_master.your_xp", $"{currentPlayer.Experience:N0}"));

        if (currentPlayer.Level >= GameConfig.MaxLevel)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("level_master.max_level"));
        }
        else
        {
            long nextLevelXP = GetExperienceForLevel(currentPlayer.Level + 1);
            long xpNeeded = nextLevelXP - currentPlayer.Experience;

            if (xpNeeded <= 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"* {Loc.Get("level_master.ready_advance", (currentPlayer.Level + 1).ToString())} *");
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("level_master.xp_needed", (currentPlayer.Level + 1).ToString(), $"{xpNeeded:N0}"));
            }
        }
        terminal.WriteLine("");
    }

    /// <summary>
    /// BBS compact display for 80x25 terminal
    /// </summary>
    private void DisplayLocationBBS()
    {
        ShowBBSHeader(Loc.Get("level_master.header"));
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
            terminal.Write(Loc.Get("level_master.bbs_max_level"));
        }
        else
        {
            long nextXP = GetExperienceForLevel(currentPlayer.Level + 1);
            long xpNeeded = nextXP - currentPlayer.Experience;
            if (xpNeeded <= 0)
            {
                terminal.SetColor("bright_green");
                terminal.Write(Loc.Get("level_master.bbs_ready"));
            }
            else
            {
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("level_master.bbs_need"));
                terminal.SetColor("white");
                terminal.Write($"{xpNeeded:N0}");
            }
        }
        terminal.Write(Loc.Get("level_master.bbs_train"));
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("level_master.bbs_pts", currentPlayer.TrainingPoints));
        terminal.WriteLine("");
        ShowBBSNPCs();
        // Menu rows
        ShowBBSMenuRow(("L", "bright_yellow", Loc.Get("level_master.bbs_level_raise")), ("A", "bright_yellow", Loc.Get("level_master.bbs_abilities")), ("T", "bright_yellow", Loc.Get("level_master.bbs_training")));
        ShowBBSMenuRow(("C", "bright_yellow", Loc.Get("level_master.bbs_crystal_ball")), ("H", "bright_yellow", Loc.Get("level_master.bbs_help_team")), ("R", "bright_yellow", Loc.Get("level_master.bbs_return")));
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

        if (ClassAbilitySystem.IsSpellcaster(currentPlayer.Class) || SpellSystem.HasSpells(currentPlayer))
        {
            // Prestige hybrid — both abilities and spells
            terminal.WriteLine(Loc.Get("level_master.hybrid_intro", currentPlayer.DisplayName));
            terminal.WriteLine("");
            if (IsScreenReader)
            {
                WriteSRMenuOption("A", Loc.Get("level_master.combat_abilities"));
                WriteSRMenuOption("S", Loc.Get("level_master.spell_library"));
            }
            else
            {
                terminal.WriteLine(Loc.Get("level_master.visual_combat_abilities"), "bright_yellow");
                terminal.WriteLine(Loc.Get("level_master.visual_spell_library"), "bright_cyan");
            }
            terminal.WriteLine("");
            var choice = await GetChoice();
            if (choice.Equals("S", StringComparison.OrdinalIgnoreCase))
            {
                await SpellLearningSystem.ShowSpellLearningMenu(currentPlayer, terminal);
            }
            else
            {
                await ClassAbilitySystem.ShowAbilityLearningMenu(currentPlayer, terminal);
            }
        }
        else
        {
            // Pure ability user — ability menu only
            terminal.WriteLine(Loc.Get("level_master.ability_intro", currentPlayer.DisplayName, currentPlayer.Class.ToString().ToLower()));
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
        terminal.WriteLine(Loc.Get("level_master.training_intro", currentPlayer.DisplayName));
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
            terminal.WriteLine(Loc.Get("level_master.need_xp_remaining", $"{needed:N0}", currentPlayer.Level + 1));
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

            // Log level up
            UsurperRemake.Systems.DebugLogger.Instance.LogLevelUp(currentPlayer.Name, startLevel, currentPlayer.Level);

            // Online news: announce level ups at milestones
            if (UsurperRemake.Systems.OnlineStateManager.IsActive)
            {
                var displayName = currentPlayer.Name2 ?? currentPlayer.Name1;
                if (currentPlayer.Level % 5 == 0 || currentPlayer.Level <= 3)
                    _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                        Loc.Get("level_master.reached_level_news", displayName, currentPlayer.Level), "combat");
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
        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("level_master.level_up_title"));
            terminal.WriteLine("");
        }
        else
        {
            await UsurperRemake.UI.ANSIArt.DisplayArtAnimated(terminal, UsurperRemake.UI.ANSIArt.LevelUp, 30);
            terminal.WriteLine("");
        }

        terminal.SetColor("bright_green");
        if (levelsRaised == 1)
        {
            terminal.WriteLine(Loc.Get("level_master.advanced_to", currentPlayer.Level));
        }
        else
        {
            terminal.WriteLine(Loc.Get("level_master.advanced_multi", levelsRaised, startLevel, currentPlayer.Level));
        }
        terminal.WriteLine("");

        // Check for milestone levels and give bonuses
        await CheckMilestoneBonuses(startLevel, currentPlayer.Level);

        terminal.SetColor(currentMaster.Color);
        switch (playerAlignment)
        {
            case PlayerAlignment.Good:
                terminal.WriteLine(Loc.Get("level_master.celeb_good_touch", currentMaster.Name));
                terminal.WriteLine(Loc.Get("level_master.celeb_good_quote"));
                break;
            case PlayerAlignment.Evil:
                terminal.WriteLine(Loc.Get("level_master.celeb_evil_eyes", currentMaster.Name));
                terminal.WriteLine(Loc.Get("level_master.celeb_evil_quote"));
                break;
            default:
                terminal.WriteLine(Loc.Get("level_master.celeb_neutral_nod", currentMaster.Name));
                terminal.WriteLine(Loc.Get("level_master.celeb_neutral_quote"));
                break;
        }
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("level_master.power_surge"));
        terminal.WriteLine(Loc.Get("level_master.hp_mana_restored"));
        terminal.WriteLine("");

        // Show training points earned
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("level_master.training_earned", trainingPointsEarned));
        terminal.WriteLine(Loc.Get("level_master.training_total", currentPlayer.TrainingPoints));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("level_master.training_hint"));
        terminal.WriteLine("");

        await terminal.PressAnyKey("  Press Enter to continue...");
    }

    /// <summary>
    /// Check and award milestone bonuses for key levels
    /// </summary>
    private async Task CheckMilestoneBonuses(int startLevel, int endLevel)
    {
        // Milestone levels and their bonuses
        var milestones = new (int level, string titleKey, string hintKey, long goldBonus, int potionBonus)[]
        {
            (5, "level_master.milestone_adventurer", "level_master.milestone_adventurer_hint", 500, 3),
            (10, "level_master.milestone_veteran", "level_master.milestone_veteran_hint", 1000, 5),
            (25, "level_master.milestone_champion", "level_master.milestone_champion_hint", 5000, 10),
            (50, "level_master.milestone_hero", "level_master.milestone_hero_hint", 25000, 20),
            (75, "level_master.milestone_legend", "level_master.milestone_legend_hint", 75000, 30),
            (100, "level_master.milestone_godslayer", "level_master.milestone_godslayer_hint", 250000, 50)
        };

        foreach (var (level, titleKey, hintKey, goldBonus, potionBonus) in milestones)
        {
            // Check if we crossed this milestone
            if (startLevel < level && endLevel >= level)
            {
                terminal.WriteLine("");
                WriteBoxHeader(Loc.Get("level_master.milestone", level), "bright_yellow");
                terminal.WriteLine("");

                terminal.SetColor("bright_white");
                terminal.WriteLine(Loc.Get("level_master.milestone_title_earned", Loc.Get(titleKey)));
                terminal.WriteLine("");

                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {Loc.Get(hintKey)}");
                terminal.WriteLine("");

                // Award bonuses
                currentPlayer.Gold += goldBonus;
                currentPlayer.Healing = Math.Min(currentPlayer.MaxPotions, currentPlayer.Healing + potionBonus);

                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("level_master.milestone_gold", $"{goldBonus:N0}"));
                terminal.WriteLine(Loc.Get("level_master.milestone_potions", potionBonus));
                terminal.WriteLine("");

                await Task.Delay(1000);
            }
        }
    }

    /// <summary>
    /// Apply class-based stat increases based on character class type.
    /// Instance wrapper for the static method.
    /// </summary>
    private void ApplyClassBasedStatIncreases()
    {
        ApplyClassStatIncreases(currentPlayer);
    }

    /// <summary>
    /// Apply class-based stat increases for a level-up. Public static so it can be
    /// called from auto-level-up (BaseLocation) and grouped combat (CombatEngine).
    /// </summary>
    public static void ApplyClassStatIncreases(Character player)
    {
        // Base stats that everyone gets
        player.BaseMaxHP += 5;
        player.BaseDefence += 1;
        player.BaseStamina += 1;

        // Class-specific bonuses
        switch (player.Class)
        {
            // Magic classes - focus on Intelligence, Wisdom, Mana
            case CharacterClass.Magician:
                player.BaseIntelligence += 4;
                player.BaseWisdom += 3;
                player.BaseMaxMana += 15;
                player.BaseMaxHP += 6;
                player.BaseStrength += 1;
                player.BaseDefence += 1;
                player.BaseConstitution += 2;
                break;

            case CharacterClass.Cleric:
                player.BaseWisdom += 4;
                player.BaseIntelligence += 2;
                player.BaseMaxMana += 12;
                player.BaseMaxHP += 6;
                player.BaseStrength += 2;
                player.BaseConstitution += 2;
                break;

            case CharacterClass.Sage:
                player.BaseIntelligence += 5;
                player.BaseWisdom += 4;
                player.BaseMaxMana += 18;
                player.BaseMaxHP += 5;
                player.BaseStrength += 1;
                player.BaseDefence += 1;
                player.BaseConstitution += 1;
                break;

            case CharacterClass.Alchemist:
                player.BaseIntelligence += 4;
                player.BaseWisdom += 2;
                player.BaseDexterity += 2;
                player.BaseMaxHP += 5;
                player.BaseConstitution += 2;
                player.BaseStamina += 2;
                break;

            // Warrior classes - focus on Strength, Constitution, HP
            case CharacterClass.Warrior:
                player.BaseStrength += 3;     // v0.47.5: was 4, reduced to curb one-shot damage
                player.BaseConstitution += 3;
                player.BaseMaxHP += 12;
                player.BaseDexterity += 2;
                player.BaseDefence += 2;
                break;

            case CharacterClass.Barbarian:
                player.BaseStrength += 4;     // v0.47.5: was 5, reduced to curb one-shot damage
                player.BaseConstitution += 4;
                player.BaseMaxHP += 12;       // v0.50.5: was 15, glass cannon needs to feel glass
                player.BaseStamina += 2;
                break;

            case CharacterClass.Paladin:
                player.BaseStrength += 3;
                player.BaseConstitution += 3;
                player.BaseWisdom += 2;
                player.BaseCharisma += 2;
                player.BaseMaxHP += 10;
                player.BaseDefence += 1;
                break;

            // Agile classes - focus on Dexterity, Agility, Stamina
            case CharacterClass.Assassin:
                player.BaseDexterity += 4;
                player.BaseAgility += 3;
                player.BaseStrength += 2;
                player.BaseMaxHP += 6;
                player.BaseStamina += 2;
                break;

            case CharacterClass.Ranger:
                player.BaseDexterity += 3;
                player.BaseAgility += 3;
                player.BaseStrength += 2;
                player.BaseWisdom += 1;
                player.BaseMaxHP += 8;
                player.BaseStamina += 2;
                break;

            case CharacterClass.Jester:
                player.BaseDexterity += 3;
                player.BaseAgility += 3;
                player.BaseCharisma += 3;
                player.BaseMaxHP += 5;
                player.BaseStamina += 2;
                break;

            case CharacterClass.Bard:
                player.BaseCharisma += 4;
                player.BaseDexterity += 2;
                player.BaseAgility += 2;
                player.BaseIntelligence += 2;
                player.BaseMaxHP += 5;
                player.BaseStamina += 2;
                break;

            // NG+ Prestige Classes — strictly stronger, both spells AND abilities
            case CharacterClass.Tidesworn:
                player.BaseStrength += 4;
                player.BaseConstitution += 4;
                player.BaseWisdom += 3;
                player.BaseCharisma += 2;
                player.BaseDefence += 2;
                player.BaseMaxHP += 13;
                player.BaseMaxMana += 8;
                break;

            case CharacterClass.Wavecaller:
                player.BaseCharisma += 5;
                player.BaseWisdom += 4;
                player.BaseIntelligence += 3;
                player.BaseConstitution += 2;
                player.BaseAgility += 2;
                player.BaseMaxHP += 7;
                player.BaseMaxMana += 14;
                break;

            case CharacterClass.Cyclebreaker:
                player.BaseStrength += 3;
                player.BaseIntelligence += 3;
                player.BaseWisdom += 3;
                player.BaseDexterity += 3;
                player.BaseConstitution += 3;
                player.BaseAgility += 2;
                player.BaseMaxHP += 9;
                player.BaseMaxMana += 10;
                break;

            case CharacterClass.Abysswarden:
                player.BaseDexterity += 5;
                player.BaseStrength += 4;
                player.BaseAgility += 4;
                player.BaseIntelligence += 3;
                player.BaseConstitution += 2;
                player.BaseMaxHP += 8;
                player.BaseMaxMana += 10;
                break;

            case CharacterClass.Voidreaver:
                player.BaseStrength += 5;
                player.BaseIntelligence += 5;
                player.BaseDexterity += 4;
                player.BaseAgility += 3;
                player.BaseStamina += 2;
                player.BaseMaxHP += 6;
                player.BaseMaxMana += 12;
                break;

            case CharacterClass.MysticShaman:
                player.BaseStrength += 3;
                player.BaseDefence += 2;
                player.BaseIntelligence += 3;
                player.BaseConstitution += 2;
                player.BaseStamina += 2;
                player.BaseAgility += 1;
                player.BaseMaxMana += 8;
                break;

            default:
                // Fallback for any undefined class
                player.BaseStrength += 2;
                player.BaseConstitution += 2;
                player.BaseMaxHP += 8;
                break;
        }

        // Recalculate all stats from base values
        player.RecalculateStats();
    }

    /// <summary>
    /// Ensures an NPC/companion Character has class-appropriate stats for their level.
    /// Computes the expected minimum stat values from level-up scaling and applies
    /// Math.Max(current, expected) — never a nerf, only a buff for under-scaled characters.
    /// Safe to call multiple times (idempotent).
    /// </summary>
    public static void EnsureClassStatsForLevel(Character npc)
    {
        if (npc == null || npc.Level <= 1) return;

        int levelsGained = npc.Level - 1; // number of level-up applications

        // Compute expected cumulative gains from pure level scaling
        // (baseline starting value assumed to be 10 for secondary stats)
        long expStr = 0, expDex = 0, expAgi = 0, expInt = 0, expWis = 0;
        long expCha = 0, expCon = 0, expSta = 0, expDef = 0, expHP = 0, expMana = 0;

        // Everyone gets +1 DEF, +1 STA per level
        expDef += levelsGained;
        expSta += levelsGained;
        expHP  += levelsGained * 5;

        switch (npc.Class)
        {
            case CharacterClass.Magician:
                expInt  += levelsGained * 4;
                expWis  += levelsGained * 3;
                expMana += levelsGained * 15;
                expHP   += levelsGained * 6;
                expStr  += levelsGained;
                expDef  += levelsGained;
                expCon  += levelsGained * 2;
                break;
            case CharacterClass.Cleric:
                expWis  += levelsGained * 4;
                expInt  += levelsGained * 2;
                expMana += levelsGained * 12;
                expHP   += levelsGained * 6;
                expStr  += levelsGained * 2;
                expCon  += levelsGained * 2;
                break;
            case CharacterClass.Sage:
                expInt  += levelsGained * 5;
                expWis  += levelsGained * 4;
                expMana += levelsGained * 18;
                expHP   += levelsGained * 5;
                expStr  += levelsGained;
                expDef  += levelsGained;
                expCon  += levelsGained;
                break;
            case CharacterClass.Alchemist:
                expInt  += levelsGained * 4;
                expWis  += levelsGained * 2;
                expDex  += levelsGained * 2;
                expHP   += levelsGained * 5;
                expCon  += levelsGained * 2;
                expSta  += levelsGained * 2;
                break;
            case CharacterClass.Warrior:
                expStr  += levelsGained * 3;
                expCon  += levelsGained * 3;
                expHP   += levelsGained * 12;
                expDex  += levelsGained * 2;
                expDef  += levelsGained * 2;
                break;
            case CharacterClass.Barbarian:
                expStr  += levelsGained * 4;
                expCon  += levelsGained * 4;
                expHP   += levelsGained * 12;
                expSta  += levelsGained * 2;
                break;
            case CharacterClass.Paladin:
                expStr  += levelsGained * 3;
                expCon  += levelsGained * 3;
                expWis  += levelsGained * 2;
                expCha  += levelsGained * 2;
                expHP   += levelsGained * 10;
                expDef  += levelsGained;
                break;
            case CharacterClass.Assassin:
                expDex  += levelsGained * 4;
                expAgi  += levelsGained * 3;
                expStr  += levelsGained * 2;
                expHP   += levelsGained * 6;
                expSta  += levelsGained * 2;
                break;
            case CharacterClass.Ranger:
                expDex  += levelsGained * 3;
                expAgi  += levelsGained * 3;
                expStr  += levelsGained * 2;
                expWis  += levelsGained;
                expHP   += levelsGained * 8;
                expSta  += levelsGained * 2;
                break;
            case CharacterClass.Jester:
                expDex  += levelsGained * 3;
                expAgi  += levelsGained * 3;
                expCha  += levelsGained * 3;
                expHP   += levelsGained * 5;
                expSta  += levelsGained * 2;
                break;
            case CharacterClass.Bard:
                expCha  += levelsGained * 4;
                expDex  += levelsGained * 2;
                expAgi  += levelsGained * 2;
                expInt  += levelsGained * 2;
                expHP   += levelsGained * 5;
                expSta  += levelsGained * 2;
                break;
            case CharacterClass.Tidesworn:
                expStr  += levelsGained * 4;
                expCon  += levelsGained * 4;
                expWis  += levelsGained * 3;
                expCha  += levelsGained * 2;
                expDef  += levelsGained * 2;
                expHP   += levelsGained * 13;
                expMana += levelsGained * 8;
                break;
            case CharacterClass.Wavecaller:
                expCha  += levelsGained * 5;
                expWis  += levelsGained * 4;
                expInt  += levelsGained * 3;
                expCon  += levelsGained * 2;
                expAgi  += levelsGained * 2;
                expHP   += levelsGained * 7;
                expMana += levelsGained * 14;
                break;
            case CharacterClass.Cyclebreaker:
                expStr  += levelsGained * 3;
                expInt  += levelsGained * 3;
                expWis  += levelsGained * 3;
                expDex  += levelsGained * 3;
                expCon  += levelsGained * 3;
                expAgi  += levelsGained * 2;
                expHP   += levelsGained * 9;
                expMana += levelsGained * 10;
                break;
            case CharacterClass.Abysswarden:
                expDex  += levelsGained * 5;
                expStr  += levelsGained * 4;
                expAgi  += levelsGained * 4;
                expInt  += levelsGained * 3;
                expCon  += levelsGained * 2;
                expHP   += levelsGained * 8;
                expMana += levelsGained * 10;
                break;
            case CharacterClass.Voidreaver:
                expStr  += levelsGained * 5;
                expInt  += levelsGained * 5;
                expDex  += levelsGained * 4;
                expAgi  += levelsGained * 3;
                expSta  += levelsGained * 2;
                expHP   += levelsGained * 6;
                expMana += levelsGained * 12;
                break;
            case CharacterClass.MysticShaman:
                expStr  += levelsGained * 3;
                expDef  += levelsGained * 2;
                expInt  += levelsGained * 3;
                expCon  += levelsGained * 2;
                expSta  += levelsGained * 2;
                expAgi  += levelsGained;
                expMana += levelsGained * 8;
                break;
            default:
                expStr  += levelsGained * 2;
                expCon  += levelsGained * 2;
                expHP   += levelsGained * 8;
                break;
        }

        // Apply only if current is below expected floor (10 = assumed starting baseline)
        const long statFloor = 10;
        if (expStr  > 0) npc.BaseStrength     = Math.Max(npc.BaseStrength,     statFloor + expStr);
        if (expDex  > 0) npc.BaseDexterity    = Math.Max(npc.BaseDexterity,    statFloor + expDex);
        if (expAgi  > 0) npc.BaseAgility      = Math.Max(npc.BaseAgility,      statFloor + expAgi);
        if (expInt  > 0) npc.BaseIntelligence = Math.Max(npc.BaseIntelligence, statFloor + expInt);
        if (expWis  > 0) npc.BaseWisdom       = Math.Max(npc.BaseWisdom,       statFloor + expWis);
        if (expCha  > 0) npc.BaseCharisma     = Math.Max(npc.BaseCharisma,     statFloor + expCha);
        if (expCon  > 0) npc.BaseConstitution = Math.Max(npc.BaseConstitution, statFloor + expCon);
        if (expSta  > 0) npc.BaseStamina      = Math.Max(npc.BaseStamina,      statFloor + expSta);
        if (expDef  > 0) npc.BaseDefence      = Math.Max(npc.BaseDefence,            expDef);  // DEF starts at 0
        if (expHP   > 0) npc.BaseMaxHP        = Math.Max(npc.BaseMaxHP,         100  + expHP); // baseline HP ~100
        if (expMana > 0) npc.BaseMaxMana      = Math.Max(npc.BaseMaxMana,             expMana);

        npc.RecalculateStats();
    }

    /// <summary>
    /// Apply hidden master-specific bonuses based on alignment.
    /// Instance wrapper for the static method.
    /// </summary>
    private void ApplyMasterBonuses()
    {
        ApplyAlignmentBonuses(currentPlayer);
    }

    /// <summary>
    /// Apply alignment-based bonus stats on level-up. Public static so it can be
    /// called from auto-level-up and grouped combat.
    /// </summary>
    public static void ApplyAlignmentBonuses(Character player)
    {
        var alignment = DetermineAlignment(player);

        switch (alignment)
        {
            case PlayerAlignment.Good:
                // Good master grants bonus Wisdom and Charisma
                player.BaseWisdom += 1;
                player.BaseCharisma += 1;
                // Small healing boost
                player.BaseMaxHP += 2;
                break;

            case PlayerAlignment.Evil:
                // Evil master grants bonus Strength and Constitution
                player.BaseStrength += 1;
                player.BaseConstitution += 1;
                // Small damage boost
                player.BaseMaxHP += 1;
                break;

            case PlayerAlignment.Neutral:
                // Neutral master grants bonus Intelligence and Dexterity
                player.BaseIntelligence += 1;
                player.BaseDexterity += 1;
                // Small mana boost
                player.BaseMaxMana += 3;
                break;
        }

        // Recalculate after alignment bonuses
        player.RecalculateStats();
    }

    /// <summary>
    /// Experience required to have <paramref name="level"/> (cumulative), compatible with NPC formula.
    /// </summary>
    public static long GetExperienceForLevel(int level)
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

    /// <summary>
    /// Checks if a character has enough XP to level up, and if so, applies all
    /// level-up effects (class stats, alignment bonuses, training points, quickbar).
    /// Called from BaseLocation.LocationLoop() to catch ALL XP sources, and from
    /// CombatEngine for grouped combat. Returns number of levels gained (0 = none).
    /// </summary>
    public static int CheckAutoLevelUp(Character player)
    {
        int levelsGained = 0;
        int startLevel = player.Level;

        while (player.Level < GameConfig.MaxLevel &&
               player.Experience >= GetExperienceForLevel(player.Level + 1))
        {
            int newLevel = player.Level + 1;

            // Apply proper class-based stat increases
            ApplyClassStatIncreases(player);

            // Apply alignment-based bonus stats
            ApplyAlignmentBonuses(player);

            // Award training points for the new level
            int trainingPoints = TrainingSystem.CalculateTrainingPointsPerLevel(player);
            player.TrainingPoints += trainingPoints;

            player.RaiseLevel(newLevel);
            levelsGained++;
        }

        if (levelsGained > 0)
        {
            // Full HP and Mana restore on level up
            player.HP = player.MaxHP;
            player.Mana = player.MaxMana;

            // Track statistics for each level gained
            for (int i = 0; i < levelsGained; i++)
            {
                int lvl = startLevel + i + 1;
                player.Statistics.RecordLevelUp(lvl);

                // Fame milestone every 10 levels
                if (lvl % 10 == 0)
                    player.Fame += 5;
            }

            // Log level up
            UsurperRemake.Systems.DebugLogger.Instance.LogLevelUp(player.Name, startLevel, player.Level);

            // Online news: announce level ups at milestones
            if (UsurperRemake.Systems.OnlineStateManager.IsActive)
            {
                var displayName = player.Name2 ?? player.Name1;
                if (player.Level % 5 == 0 || player.Level <= 3)
                    _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                        $"{displayName} has reached level {player.Level}!", "combat");
            }

            // Auto-add newly unlocked spells/abilities to empty quickbar slots
            GameEngine.QuickbarAddNewSkills(player);
        }

        return levelsGained;
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
            terminal.WriteLine(Loc.Get("level_master.crystal_empty"));
            await terminal.PressAnyKey();
            return;
        }

        const int pageSize = 15;
        int currentPage = 0;
        int totalPages = (npcs.Count + pageSize - 1) / pageSize;

        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("level_master.crystal_ball"), currentMaster.Color);
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("level_master.crystal_gesture", currentMaster.Name));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("level_master.crystal_who_scry", currentPage + 1, totalPages));
            terminal.WriteLine("");

            // Show numbered list of NPCs for current page
            int startIndex = currentPage * pageSize;
            int endIndex = Math.Min(startIndex + pageSize, npcs.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var npc = npcs[i];
                string status = npc.IsDead ? Loc.Get("level_master.crystal_dead_tag") : "";
                terminal.WriteLine(Loc.Get("level_master.crystal_npc_entry", i + 1, npc.Name, npc.Level, npc.Class, status));
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("level_master.crystal_showing", startIndex + 1, endIndex, npcs.Count));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            string navOptions = "";
            if (currentPage > 0) navOptions += "[P]rev  ";
            if (currentPage < totalPages - 1) navOptions += "[N]ext  ";
            terminal.WriteLine(Loc.Get("level_master.crystal_nav", navOptions));
            terminal.WriteLine("");
            terminal.Write(Loc.Get("ui.choice"));
            terminal.SetColor("white");

            string input = await terminal.ReadLineAsync();
            input = input?.Trim().ToUpper() ?? "";

            if (input == "Q" || input == "0" || string.IsNullOrEmpty(input))
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("level_master.crystal_close"));
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
            terminal.WriteLine(Loc.Get("level_master.crystal_invalid"));
            await Task.Delay(1000);
        }
    }

    /// <summary>
    /// Display detailed information about a scried character
    /// </summary>
    private async Task DisplayScryingResult(NPC target)
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("level_master.visions"), "bright_magenta");
        terminal.WriteLine("");

        await Task.Delay(500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("level_master.crystal_reveal", target.Name));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.crystal_class", target.Class));
        terminal.WriteLine(Loc.Get("level_master.crystal_level", target.Level));
        terminal.WriteLine(Loc.Get("level_master.crystal_status", target.IsAlive ? Loc.Get("level_master.crystal_alive") : Loc.Get("level_master.crystal_dead")));
        terminal.WriteLine(Loc.Get("level_master.crystal_location", target.CurrentLocation));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("level_master.crystal_combat_stats"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.crystal_str_def", target.Strength, target.Defence));
        terminal.WriteLine(Loc.Get("level_master.crystal_agi_dex", target.Agility, target.Dexterity));
        terminal.WriteLine(Loc.Get("level_master.crystal_hp_mana", target.HP, target.MaxHP, target.Mana, target.MaxMana));
        terminal.WriteLine("");

        terminal.SetColor("green");
        terminal.WriteLine($"  {Loc.Get("ui.wealth_status")}");
        terminal.SetColor("white");
        terminal.WriteLine($"    {Loc.Get("ui.gold")}: {target.Gold:N0}");
        terminal.WriteLine($"    {Loc.Get("ui.team")}: {(string.IsNullOrEmpty(target.Team) ? Loc.Get("ui.none") : target.Team)}");
        terminal.WriteLine($"    {Loc.Get("ui.alignment")}: {(target.Chivalry > target.Darkness ? Loc.Get("ui.good") : target.Darkness > target.Chivalry ? Loc.Get("ui.evil") : Loc.Get("ui.neutral"))}");

        if (target.Brain != null)
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("level_master.crystal_personality", target.Brain.Personality));
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("level_master.crystal_fade"));
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Help Team Member - Donate experience or gold to help a teammate level up
    /// </summary>
    private async Task HelpTeamMember()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("level_master.help_ally"), currentMaster.Color);
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

        // Find player's spouse (if married to an NPC)
        var spouseName = RelationshipSystem.GetSpouseName(currentPlayer);
        NPC? spouseNpc = null;
        if (!string.IsNullOrEmpty(spouseName))
        {
            spouseNpc = NPCSpawnSystem.Instance?.GetNPCByName(spouseName);
            if (spouseNpc != null && (spouseNpc.IsDead || spouseNpc.Level >= GameConfig.MaxLevel))
                spouseNpc = null;
        }

        // Build combined list of shareable allies
        var shareableAllies = new System.Collections.Generic.List<(string Name, int Level, long Experience, bool IsCompanion, object Data)>();

        // Add spouse first (most personal relationship)
        if (spouseNpc != null)
        {
            shareableAllies.Add((spouseNpc.Name, spouseNpc.Level, spouseNpc.Experience, false, spouseNpc));
        }

        foreach (var npc in npcTeammates)
        {
            // Skip spouse if already added (could also be on the same team)
            if (spouseNpc != null && npc.Name == spouseNpc.Name) continue;
            shareableAllies.Add((npc.Name, npc.Level, npc.Experience, false, npc));
        }

        foreach (var companion in companions)
        {
            shareableAllies.Add((companion.Name, companion.Level, companion.Experience, true, companion));
        }

        if (shareableAllies.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("level_master.help_master_looks", currentMaster.Name));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("level_master.help_no_allies"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("level_master.help_nods", currentMaster.Name));
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("level_master.help_noble_pursuit"));
        terminal.WriteLine(Loc.Get("level_master.help_share_wisdom"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.help_select_ally"));
        terminal.WriteLine("");

        for (int i = 0; i < shareableAllies.Count; i++)
        {
            var ally = shareableAllies[i];
            long xpToNext = GetExperienceForLevel(ally.Level + 1) - ally.Experience;
            string allyType = ally.IsCompanion ? Loc.Get("level_master.help_companion_tag")
                : (spouseNpc != null && ally.Name == spouseNpc.Name) ? Loc.Get("level_master.help_spouse_tag") : "";
            terminal.WriteLine(Loc.Get("level_master.help_ally_entry", i + 1, ally.Name, allyType, ally.Level, $"{xpToNext:N0}"));
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("level_master.help_select_prompt"));
        terminal.SetColor("white");

        string input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > shareableAllies.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("level_master.help_keep_wisdom"));
            await terminal.PressAnyKey();
            return;
        }

        var selectedAlly = shareableAllies[choice - 1];
        long xpNeeded = GetExperienceForLevel(selectedAlly.Level + 1) - selectedAlly.Experience;

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("level_master.help_needs_xp", selectedAlly.Name, $"{xpNeeded:N0}", selectedAlly.Level + 1));
        terminal.WriteLine(Loc.Get("level_master.help_you_have_xp", $"{currentPlayer.Experience:N0}"));
        terminal.WriteLine("");

        // Calculate max they can give (half their XP, but not more than they have)
        long maxGive = currentPlayer.Experience / 2;
        if (maxGive < 1)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("level_master.help_not_enough_xp"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("level_master.help_how_much", $"{maxGive:N0}"));
        terminal.SetColor("white");

        string xpInput = await terminal.ReadLineAsync();
        if (!long.TryParse(xpInput, out long xpToGive) || xpToGive < 1 || xpToGive > maxGive)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("level_master.help_invalid_amount"));
            await terminal.PressAnyKey();
            return;
        }

        // Transfer experience
        currentPlayer.Experience -= xpToGive;

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("level_master.help_shared", $"{xpToGive:N0}", selectedAlly.Name));

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

            // Snapshot companion stats before level-ups
            int beforeCHP = companion.BaseStats.HP;
            int beforeCAtk = companion.BaseStats.Attack;
            int beforeCDef = companion.BaseStats.Defense;
            int beforeCSpd = companion.BaseStats.Speed;
            int beforeCMag = companion.BaseStats.MagicPower;
            int beforeCHeal = companion.BaseStats.HealingPower;
            int beforeCCon = companion.Constitution;
            int beforeCDex = companion.Dexterity, beforeCAgi = companion.Agility;
            int beforeCInt = companion.Intelligence, beforeCWis = companion.Wisdom, beforeCCha = companion.Charisma;

            // Apply level-ups with proper stat gains through CompanionSystem
            if (levelsToGain > 0)
            {
                levelsGained = CompanionSystem.Instance?.LevelUpCompanion(companion.Id, levelsToGain) ?? 0;
            }

            if (levelsGained > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("level_master.help_companion_grown", companion.Name, companion.Level));

                // Show stat diffs
                var csc = new System.Collections.Generic.List<string>();
                int dAtk = companion.BaseStats.Attack - beforeCAtk;
                int dDef = companion.BaseStats.Defense - beforeCDef;
                int dSpd = companion.BaseStats.Speed - beforeCSpd;
                int dMag = companion.BaseStats.MagicPower - beforeCMag;
                int dHeal = companion.BaseStats.HealingPower - beforeCHeal;
                int dHP = companion.BaseStats.HP - beforeCHP;
                int dCon = companion.Constitution - beforeCCon;
                int dDex = companion.Dexterity - beforeCDex;
                int dAgi = companion.Agility - beforeCAgi;
                int dInt = companion.Intelligence - beforeCInt;
                int dWis = companion.Wisdom - beforeCWis;
                int dCha = companion.Charisma - beforeCCha;
                if (dAtk > 0) csc.Add($"ATK +{dAtk}");
                if (dDef > 0) csc.Add($"DEF +{dDef}");
                if (dSpd > 0) csc.Add($"SPD +{dSpd}");
                if (dMag > 0) csc.Add($"MAG +{dMag}");
                if (dHeal > 0) csc.Add($"HEAL +{dHeal}");
                if (dCon > 0) csc.Add($"CON +{dCon}");
                if (dDex > 0) csc.Add($"DEX +{dDex}");
                if (dAgi > 0) csc.Add($"AGI +{dAgi}");
                if (dInt > 0) csc.Add($"INT +{dInt}");
                if (dWis > 0) csc.Add($"WIS +{dWis}");
                if (dCha > 0) csc.Add($"CHA +{dCha}");
                if (dHP > 0) csc.Add($"HP +{dHP}");
                terminal.SetColor("bright_green");
                if (csc.Count > 0)
                    terminal.WriteLine("  " + string.Join("  ", csc));

                terminal.WriteLine(Loc.Get("level_master.help_effectiveness"));
            }
        }
        else
        {
            // Handle NPC teammate XP sharing
            var recipient = (NPC)selectedAlly.Data;
            recipient.Experience += xpToGive;
            int startLevel = recipient.Level;

            // Snapshot stats before any level-ups
            long beforeHP = recipient.BaseMaxHP;
            long beforeStr = recipient.BaseStrength;
            long beforeDef = recipient.BaseDefence;
            long beforeDex = recipient.BaseDexterity;
            long beforeAgi = recipient.BaseAgility;
            long beforeInt = recipient.BaseIntelligence;
            long beforeWis = recipient.BaseWisdom;
            long beforeCha = recipient.BaseCharisma;
            long beforeSta = recipient.BaseStamina;
            long beforeMana = recipient.BaseMaxMana;

            while (recipient.Experience >= GetExperienceForLevel(recipient.Level + 1) && recipient.Level < GameConfig.MaxLevel)
            {
                recipient.Level++;
                levelsGained++;

                // Use class-appropriate stat gains (same as auto-level-up for players)
                ApplyClassStatIncreases(recipient);
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
                    terminal.WriteLine(Loc.Get("level_master.help_npc_level", recipient.Name, recipient.Level));
                else
                    terminal.WriteLine(Loc.Get("level_master.help_npc_multi", recipient.Name, levelsGained, startLevel, recipient.Level));

                // Show stat changes
                var statChanges = new System.Collections.Generic.List<string>();
                long dHP = recipient.BaseMaxHP - beforeHP;
                long dStr = recipient.BaseStrength - beforeStr;
                long dDef = recipient.BaseDefence - beforeDef;
                long dDex = recipient.BaseDexterity - beforeDex;
                long dAgi = recipient.BaseAgility - beforeAgi;
                long dInt = recipient.BaseIntelligence - beforeInt;
                long dWis = recipient.BaseWisdom - beforeWis;
                long dCha = recipient.BaseCharisma - beforeCha;
                long dSta = recipient.BaseStamina - beforeSta;
                long dMana = recipient.BaseMaxMana - beforeMana;
                if (dStr > 0) statChanges.Add($"STR +{dStr}");
                if (dDef > 0) statChanges.Add($"DEF +{dDef}");
                if (dDex > 0) statChanges.Add($"DEX +{dDex}");
                if (dAgi > 0) statChanges.Add($"AGI +{dAgi}");
                if (dInt > 0) statChanges.Add($"INT +{dInt}");
                if (dWis > 0) statChanges.Add($"WIS +{dWis}");
                if (dCha > 0) statChanges.Add($"CHA +{dCha}");
                if (dHP > 0) statChanges.Add($"HP +{dHP}");
                if (dSta > 0) statChanges.Add($"STA +{dSta}");
                if (dMana > 0) statChanges.Add($"MP +{dMana}");
                if (statChanges.Count > 0)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("  " + string.Join("  ", statChanges));
                }

                NewsSystem.Instance?.Newsy(true, Loc.Get("level_master.advanced_level_news", recipient.Name, recipient.Level, currentPlayer.Name2));
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("level_master.help_smiles", currentMaster.Name));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("level_master.help_true_strength"));

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
