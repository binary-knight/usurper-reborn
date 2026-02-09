using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Character Creation System - Complete Pascal USERHUNC.PAS implementation
/// Handles all aspects of new player creation with full Pascal compatibility
/// </summary>
public class CharacterCreationSystem
{
    private readonly TerminalEmulator terminal;
    private readonly Random random;
    
    public CharacterCreationSystem(TerminalEmulator terminal)
    {
        this.terminal = terminal;
        this.random = new Random();
    }
    
    /// <summary>
    /// Main character creation workflow (Pascal USERHUNC.PAS)
    /// </summary>
    public async Task<Character> CreateNewCharacter(string playerName)
    {
        // Reset all story progress for a fresh start (seals, artifacts, etc.)
        StoryProgressionSystem.Instance.FullReset();

        terminal.WriteLine("");
        terminal.WriteLine("--- CHARACTER CREATION ---", "bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("Welcome to the medieval world of Usurper...", "yellow");
        terminal.WriteLine("");

        // Create base character with Pascal defaults
        var character = CreateBaseCharacter(playerName);

        try
        {
            // Step 1: Choose character name (used for both Name1 and Name2)
            // Name may already be provided from save slot selection, or locked in BBS mode
            string characterName;
            if (DoorMode.IsInDoorMode)
            {
                characterName = DoorMode.GetPlayerName();
                terminal.WriteLine($"Your character will be named: {characterName}", "cyan");
                terminal.WriteLine("(Name is set by your BBS login)", "gray");
                terminal.WriteLine("");
                await Task.Delay(1500);
            }
            else if (!string.IsNullOrWhiteSpace(playerName))
            {
                // Name already provided (from save slot dialog), use it directly
                characterName = playerName;
                terminal.WriteLine($"Creating character: {characterName}", "cyan");
                terminal.WriteLine("");
            }
            else
            {
                characterName = await SelectCharacterName();
                if (string.IsNullOrEmpty(characterName))
                {
                    return null; // User aborted
                }
            }
            character.Name1 = characterName;
            character.Name2 = characterName;
            
            // Step 2: Select gender (Pascal gender selection)
            character.Sex = await SelectGender();
            
            // Step 3: Select race (Pascal race selection with help)
            character.Race = await SelectRace();
            
            // Step 4: Select class (Pascal class selection with validation)
            character.Class = await SelectClass(character.Race);

            // Step 5: Select difficulty mode
            character.Difficulty = await SelectDifficulty();
            DifficultySystem.CurrentDifficulty = character.Difficulty;

            // Step 6: Roll stats with re-roll option (up to 5 re-rolls)
            await RollCharacterStats(character);

            // Step 7: Generate physical appearance (Pascal appearance generation)
            GeneratePhysicalAppearance(character);

            // Step 8: Set starting equipment and configuration
            SetStartingConfiguration(character);

            // Step 9: Show character summary and confirm
            await ShowCharacterSummary(character);
            
            var confirm = await terminal.GetInputAsync("Create this character? (Y/N): ");
            if (confirm.ToUpper() != "Y")
            {
                terminal.WriteLine("Character creation aborted.", "red");
                return null;
            }
            
            terminal.WriteLine("");
            terminal.WriteLine("Character created successfully!", "green");
            terminal.WriteLine("Preparing to enter the realm...", "cyan");
            await Task.Delay(2000);
            
            return character;
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"Error during character creation: {ex.Message}", "red");
            return null;
        }
    }
    
    /// <summary>
    /// Create base character with Pascal default values (USERHUNC.PAS)
    /// </summary>
    private Character CreateBaseCharacter(string playerName)
    {
        var character = new Character
        {
            Name1 = playerName,
            Name2 = playerName, // Will be changed in alias selection
            AI = CharacterAI.Human,
            Allowed = true,
            Level = GameConfig.DefaultStartingLevel,
            Gold = GameConfig.DefaultStartingGold,
            BankGold = 0,
            Experience = GameConfig.DefaultStartingExperience,
            Fights = GameConfig.DefaultDungeonFights,
            Healing = GameConfig.DefaultStartingHealing,
            AgePlus = 0,
            DarkNr = GameConfig.DefaultDarkDeeds,
            ChivNr = GameConfig.DefaultGoodDeeds,
            Chivalry = 0,
            Darkness = 0,
            PFights = GameConfig.DefaultPlayerFights,
            King = false,
            Location = GameConfig.OfflineLocationDormitory,
            Team = "",
            TeamPW = "",
            BGuard = 0,
            CTurf = false,
            GnollP = 0,
            Mental = GameConfig.DefaultMentalHealth,
            Addict = 0,
            WeapPow = 0,
            ArmPow = 0,
            AutoHeal = false,
            Loyalty = GameConfig.DefaultLoyalty,
            Haunt = 0,
            Master = '1',
            TFights = GameConfig.DefaultTournamentFights,
            Thiefs = GameConfig.DefaultThiefAttempts,
            Brawls = GameConfig.DefaultBrawls,
            Assa = GameConfig.DefaultAssassinAttempts,
            Poison = 0,
            Trains = 2,
            Immortal = false,
            BattleCry = "",
            BGuardNr = 0,
            Casted = false,
            Punch = 0,
            Deleted = false,
            Quests = 0,
            God = "",
            RoyQuests = 0,
            Resurrections = 3, // Default resurrections
            PickPocketAttempts = 3,
            BankRobberyAttempts = 3,
            ID = GenerateUniqueID()
        };
        
        // Initialize arrays with Pascal defaults
        InitializeCharacterArrays(character);
        
        return character;
    }
    
    /// <summary>
    /// Initialize character arrays to Pascal defaults
    /// </summary>
    private void InitializeCharacterArrays(Character character)
    {
        // Initialize inventory (Pascal: global_maxitem)
        character.Item = new List<int>();
        character.ItemType = new List<ObjType>();
        for (int i = 0; i < GameConfig.MaxItem; i++)
        {
            character.Item.Add(0);
            character.ItemType.Add(ObjType.Head);
        }
        
        // Initialize phrases (Pascal: 6 phrases)
        character.Phrases = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            character.Phrases.Add("");
        }
        
        // Initialize description (Pascal: 4 lines)
        character.Description = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            character.Description.Add("");
        }
        
        // Initialize spells (Pascal: global_maxspells, 2 columns)
        character.Spell = new List<List<bool>>();
        for (int i = 0; i < GameConfig.MaxSpells; i++)
        {
            character.Spell.Add(new List<bool> { false, false });
        }
        // Starting spell (Pascal: player.spell[1, 1] := True)
        character.Spell[0][0] = true;
        
        // Initialize skills (Pascal: global_maxcombat)
        character.Skill = new List<int>();
        for (int i = 0; i < GameConfig.MaxCombat; i++)
        {
            character.Skill.Add(0);
        }
        
        // Initialize medals (Pascal: array[1..20])
        character.Medal = new List<bool>();
        for (int i = 0; i < 20; i++)
        {
            character.Medal.Add(false);
        }
        
        // Initialize equipment slots to empty (Pascal: 0 = no item)
        character.LHand = 0;
        character.RHand = 0;
        character.Head = 0;
        character.Body = 0;
        character.Arms = 0;
        character.LFinger = 0;
        character.RFinger = 0;
        character.Legs = 0;
        character.Feet = 0;
        character.Waist = 0;
        character.Neck = 0;
        character.Neck2 = 0;
        character.Face = 0;
        character.Shield = 0;
        character.Hands = 0;
        character.ABody = 0;
    }
    
    /// <summary>
    /// Select character name with Pascal validation (USERHUNC.PAS)
    /// </summary>
    private async Task<string> SelectCharacterName()
    {
        string name;
        bool validName = false;

        do
        {
            terminal.WriteLine("");
            terminal.WriteLine("Enter your character's name:", "cyan");
            terminal.WriteLine("This is the name you will be known by in the realm.");
            terminal.WriteLine("");

            name = await terminal.GetInputAsync("Character name: ");

            if (string.IsNullOrWhiteSpace(name))
            {
                terminal.WriteLine("You must enter a name!", "red");
                continue;
            }

            name = name.Trim();

            // Pascal validation: Check for forbidden names
            var upperName = name.ToUpper();
            if (GameConfig.ForbiddenNames.Contains(upperName))
            {
                terminal.WriteLine("I'm sorry, but that name is already being used.", "red");
                continue;
            }

            // TODO: Check against existing players and NPCs
            // For now, we'll skip this validation in the prototype

            terminal.WriteLine("");
            terminal.WriteLine($"{name} is what you want? (Y/N)", "yellow");
            var confirm = await terminal.GetInputAsync("");

            if (confirm.ToUpper() == "Y")
            {
                validName = true;
            }

        } while (!validName);

        return name;
    }
    
    /// <summary>
    /// Select character gender (Pascal USERHUNC.PAS gender selection)
    /// </summary>
    private async Task<CharacterSex> SelectGender()
    {
        while (true)
        {
            terminal.WriteLine("");
            terminal.WriteLine("Gender:", "cyan");
            terminal.WriteLine("(M)ale", "white");
            terminal.WriteLine("(F)emale", "white");
            
            var choice = await terminal.GetInputAsync("Choice: ");
            
            switch (choice.ToUpper())
            {
                case "M":
                    if (await ConfirmChoice("Play a Male character", false))
                        return CharacterSex.Male;
                    break;
                    
                case "F":
                    if (await ConfirmChoice("Play a Female character", false))
                        return CharacterSex.Female;
                    break;
                    
                default:
                    terminal.WriteLine("Please choose M or F.", "red");
                    break;
            }
        }
    }

    /// <summary>
    /// Select game difficulty mode
    /// </summary>
    private async Task<DifficultyMode> SelectDifficulty()
    {
        terminal.Clear();
        terminal.WriteLine("");
        terminal.WriteLine("╔════════════════════════════════════════════════════════════════╗", "bright_cyan");
        terminal.WriteLine("║                   SELECT DIFFICULTY MODE                       ║", "bright_cyan");
        terminal.WriteLine("╚════════════════════════════════════════════════════════════════╝", "bright_cyan");
        terminal.WriteLine("");

        while (true)
        {
            // Display difficulty options with descriptions
            terminal.WriteLine("(E)asy      - " + DifficultySystem.GetDescription(DifficultyMode.Easy), DifficultySystem.GetColor(DifficultyMode.Easy));
            terminal.WriteLine("");
            terminal.WriteLine("(N)ormal    - " + DifficultySystem.GetDescription(DifficultyMode.Normal), DifficultySystem.GetColor(DifficultyMode.Normal));
            terminal.WriteLine("");
            terminal.WriteLine("(H)ard      - " + DifficultySystem.GetDescription(DifficultyMode.Hard), DifficultySystem.GetColor(DifficultyMode.Hard));
            terminal.WriteLine("");
            terminal.WriteLine("(!)Nightmare- " + DifficultySystem.GetDescription(DifficultyMode.Nightmare), DifficultySystem.GetColor(DifficultyMode.Nightmare));
            terminal.WriteLine("");

            var choice = await terminal.GetInputAsync("Choose difficulty (E/N/H/!): ");

            switch (choice.ToUpper())
            {
                case "E":
                    terminal.WriteLine("");
                    terminal.WriteLine("Easy mode selected - enjoy a relaxed adventure!", DifficultySystem.GetColor(DifficultyMode.Easy));
                    await Task.Delay(1000);
                    return DifficultyMode.Easy;

                case "N":
                    terminal.WriteLine("");
                    terminal.WriteLine("Normal mode selected - the classic Usurper experience!", DifficultySystem.GetColor(DifficultyMode.Normal));
                    await Task.Delay(1000);
                    return DifficultyMode.Normal;

                case "H":
                    terminal.WriteLine("");
                    terminal.WriteLine("Hard mode selected - prepare for a challenge!", DifficultySystem.GetColor(DifficultyMode.Hard));
                    await Task.Delay(1000);
                    return DifficultyMode.Hard;

                case "!":
                    terminal.WriteLine("");
                    terminal.WriteLine("NIGHTMARE MODE - You dare to walk the path of pain?", DifficultySystem.GetColor(DifficultyMode.Nightmare));
                    var confirm = await terminal.GetInputAsync("Are you SURE? (Y/N): ");
                    if (confirm.ToUpper() == "Y")
                    {
                        terminal.WriteLine("Your fate is sealed. May the gods have mercy.", "bright_red");
                        await Task.Delay(1500);
                        return DifficultyMode.Nightmare;
                    }
                    terminal.WriteLine("A wise choice. Select another difficulty.", "yellow");
                    terminal.WriteLine("");
                    break;

                default:
                    terminal.WriteLine("Please choose E, N, H, or !.", "red");
                    terminal.WriteLine("");
                    break;
            }
        }
    }

    /// <summary>
    /// Select character race with help system (Pascal USERHUNC.PAS race selection)
    /// </summary>
    private async Task<CharacterRace> SelectRace()
    {
        string choice = "?";

        while (true)
        {
            if (choice == "?")
            {
                terminal.Clear();
                terminal.WriteLine("");
                terminal.WriteLine("Choose your Race:", "cyan");
                terminal.WriteLine("");

                // Show race menu with available classes
                DisplayRaceOption(0, "Human", CharacterRace.Human);
                DisplayRaceOption(1, "Hobbit", CharacterRace.Hobbit);
                DisplayRaceOption(2, "Elf", CharacterRace.Elf);
                DisplayRaceOption(3, "Half-elf", CharacterRace.HalfElf);
                DisplayRaceOption(4, "Dwarf", CharacterRace.Dwarf);
                DisplayRaceOption(5, "Troll", CharacterRace.Troll);
                DisplayRaceOption(6, "Orc", CharacterRace.Orc);
                DisplayRaceOption(7, "Gnome", CharacterRace.Gnome);
                DisplayRaceOption(8, "Gnoll", CharacterRace.Gnoll, "*poisonous bite");
                DisplayRaceOption(9, "Mutant", CharacterRace.Mutant);
                terminal.WriteLine("");
                terminal.WriteLine("(H) Help", "green");
                terminal.WriteLine("(A) Abort", "red");
                terminal.WriteLine("");
            }

            choice = await terminal.GetInputAsync("Your choice: ");

            // Handle help
            if (choice.ToUpper() == "H")
            {
                await ShowRaceHelp();
                choice = "?";
                continue;
            }

            // Handle abort
            if (choice.ToUpper() == "A")
            {
                if (await ConfirmChoice("Abort", false))
                {
                    throw new OperationCanceledException("Character creation aborted by user");
                }
                choice = "?";
                continue;
            }

            // Handle race selection
            if (int.TryParse(choice, out int raceChoice) && raceChoice >= 0 && raceChoice <= 9)
            {
                var race = (CharacterRace)raceChoice;
                var description = GameConfig.RaceDescriptions[race];

                terminal.WriteLine("");
                if (await ConfirmChoice($"Be {description}", false))
                {
                    return race;
                }

                choice = "?";
            }
            else
            {
                terminal.WriteLine("Invalid choice. Please select 0-9, H for help, or A to abort.", "red");
            }
        }
    }

    /// <summary>
    /// Display a race option with available classes
    /// </summary>
    private void DisplayRaceOption(int number, string raceName, CharacterRace race, string suffix = "")
    {
        // Get all classes
        var allClasses = new[] {
            CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Ranger,
            CharacterClass.Assassin, CharacterClass.Bard, CharacterClass.Jester,
            CharacterClass.Alchemist, CharacterClass.Magician, CharacterClass.Cleric,
            CharacterClass.Sage, CharacterClass.Barbarian
        };

        // Get restricted classes for this race
        CharacterClass[] restrictedClasses = GameConfig.InvalidCombinations.ContainsKey(race)
            ? GameConfig.InvalidCombinations[race]
            : Array.Empty<CharacterClass>();

        // Get available classes
        var availableClasses = allClasses.Where(c => !restrictedClasses.Contains(c)).ToList();

        // Build class abbreviation string
        string classAbbreviations = GetClassAbbreviations(availableClasses);

        // Format the display
        string suffixText = string.IsNullOrEmpty(suffix) ? "" : $" {suffix}";
        terminal.Write($"({number}) ", "white");
        terminal.Write($"{raceName,-10}", "white");
        terminal.Write($"{suffixText}", "yellow");

        // Show available classes in a muted color
        if (availableClasses.Count == allClasses.Length)
        {
            terminal.WriteLine($" [All classes]", "darkgray");
        }
        else
        {
            terminal.WriteLine($" [{classAbbreviations}]", "darkgray");
        }
    }

    /// <summary>
    /// Get abbreviated class names for display
    /// </summary>
    private string GetClassAbbreviations(List<CharacterClass> classes)
    {
        var abbreviations = new Dictionary<CharacterClass, string>
        {
            { CharacterClass.Warrior, "War" },
            { CharacterClass.Paladin, "Pal" },
            { CharacterClass.Ranger, "Ran" },
            { CharacterClass.Assassin, "Asn" },
            { CharacterClass.Bard, "Brd" },
            { CharacterClass.Jester, "Jst" },
            { CharacterClass.Alchemist, "Alc" },
            { CharacterClass.Magician, "Mag" },
            { CharacterClass.Cleric, "Clr" },
            { CharacterClass.Sage, "Sge" },
            { CharacterClass.Barbarian, "Bar" }
        };

        return string.Join("/", classes.Select(c => abbreviations[c]));
    }
    
    /// <summary>
    /// Select character class with race validation (Pascal USERHUNC.PAS class selection)
    /// </summary>
    private async Task<CharacterClass> SelectClass(CharacterRace race)
    {
        string choice = "?";

        // Menu choice to enum mapping (menu order doesn't match alphabetical enum order)
        var menuToClass = new Dictionary<int, CharacterClass>
        {
            { 0, CharacterClass.Warrior },
            { 1, CharacterClass.Paladin },
            { 2, CharacterClass.Ranger },
            { 3, CharacterClass.Assassin },
            { 4, CharacterClass.Bard },
            { 5, CharacterClass.Jester },
            { 6, CharacterClass.Alchemist },
            { 7, CharacterClass.Magician },
            { 8, CharacterClass.Cleric },
            { 9, CharacterClass.Sage },
            { 10, CharacterClass.Barbarian }
        };

        // Get restricted classes for this race (if any)
        CharacterClass[] restrictedClasses = GameConfig.InvalidCombinations.ContainsKey(race)
            ? GameConfig.InvalidCombinations[race]
            : Array.Empty<CharacterClass>();

        while (true)
        {
            if (choice == "?")
            {
                terminal.Clear();
                terminal.WriteLine("");
                terminal.WriteLine($"Choose your Class (as a {GameConfig.RaceNames[(int)race]}):", "cyan");
                terminal.WriteLine("");

                // Show class menu with restrictions marked
                DisplayClassOption(0, "Warrior", CharacterClass.Warrior, restrictedClasses);
                DisplayClassOption(1, "Paladin", CharacterClass.Paladin, restrictedClasses);
                DisplayClassOption(2, "Ranger", CharacterClass.Ranger, restrictedClasses);
                DisplayClassOption(3, "Assassin", CharacterClass.Assassin, restrictedClasses);
                DisplayClassOption(4, "Bard", CharacterClass.Bard, restrictedClasses);
                DisplayClassOption(5, "Jester", CharacterClass.Jester, restrictedClasses);
                DisplayClassOption(6, "Alchemist", CharacterClass.Alchemist, restrictedClasses);
                DisplayClassOption(7, "Magician", CharacterClass.Magician, restrictedClasses);
                DisplayClassOption(8, "Cleric", CharacterClass.Cleric, restrictedClasses);
                DisplayClassOption(9, "Sage", CharacterClass.Sage, restrictedClasses);
                DisplayClassOption(10, "Barbarian", CharacterClass.Barbarian, restrictedClasses);
                terminal.WriteLine("(H) Help", "green");
                terminal.WriteLine("(A) Abort", "red");
                terminal.WriteLine("");

                // Show restriction reason if this race has restrictions
                if (restrictedClasses.Length > 0 && GameConfig.RaceRestrictionReasons.ContainsKey(race))
                {
                    terminal.WriteLine($"Note: {GameConfig.RaceRestrictionReasons[race]}", "yellow");
                    terminal.WriteLine("");
                }
            }

            choice = await terminal.GetInputAsync("Your choice: ");

            // Handle help
            if (choice.ToUpper() == "H")
            {
                await ShowClassHelp();
                choice = "?";
                continue;
            }

            // Handle abort
            if (choice.ToUpper() == "A")
            {
                if (await ConfirmChoice("Abort", false))
                {
                    throw new OperationCanceledException("Character creation aborted by user");
                }
                choice = "?";
                continue;
            }

            // Handle class selection
            if (int.TryParse(choice, out int classChoice) && menuToClass.ContainsKey(classChoice))
            {
                var characterClass = menuToClass[classChoice];

                // Check invalid race/class combinations
                if (restrictedClasses.Contains(characterClass))
                {
                    terminal.WriteLine("");
                    var article1 = "aeiouAEIOU".Contains(characterClass.ToString()[0]) ? "an" : "a";
                    terminal.WriteLine($"Sorry, {GameConfig.RaceNames[(int)race]} cannot be {article1} {characterClass}!", "red");
                    if (GameConfig.RaceRestrictionReasons.ContainsKey(race))
                    {
                        terminal.WriteLine(GameConfig.RaceRestrictionReasons[race], "yellow");
                    }
                    await Task.Delay(2000);
                    choice = "?";
                    continue;
                }

                terminal.WriteLine("");
                var article2 = "aeiouAEIOU".Contains(characterClass.ToString()[0]) ? "an" : "a";
                if (await ConfirmChoice($"Be {article2} {characterClass}", false))
                {
                    return characterClass;
                }

                choice = "?";
            }
            else
            {
                terminal.WriteLine("Invalid choice. Please select 0-10, H for help, or A to abort.", "red");
            }
        }
    }

    /// <summary>
    /// Display a class option with restriction indicator
    /// </summary>
    private void DisplayClassOption(int number, string className, CharacterClass classType, CharacterClass[] restrictedClasses)
    {
        bool isRestricted = restrictedClasses.Contains(classType);
        string numberStr = number < 10 ? $"({number}) " : $"({number})";

        if (isRestricted)
        {
            terminal.WriteLine($"{numberStr} {className,-12} [UNAVAILABLE]", "darkgray");
        }
        else
        {
            terminal.WriteLine($"{numberStr} {className}", "white");
        }
    }
    
    /// <summary>
    /// Roll character stats with option to re-roll up to 5 times
    /// </summary>
    private async Task RollCharacterStats(Character character)
    {
        const int MAX_REROLLS = 5;
        int rerollsRemaining = MAX_REROLLS;

        while (true)
        {
            // Roll the stats
            RollStats(character);

            // Display the rolled stats
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("═══ STAT ROLL ═══", "bright_cyan");
            terminal.WriteLine("");
            terminal.WriteLine($"Class: {character.Class}", "yellow");
            terminal.WriteLine($"Race: {GameConfig.RaceNames[(int)character.Race]}", "yellow");
            terminal.WriteLine("");

            // Calculate total stat points for comparison
            long totalStats = character.Strength + character.Defence + character.Stamina +
                              character.Agility + character.Charisma + character.Dexterity +
                              character.Wisdom + character.Intelligence + character.Constitution;

            terminal.WriteLine("Your rolled attributes:", "cyan");
            terminal.WriteLine("");
            terminal.Write($"  Hit Points:    ");
            terminal.Write($"{character.HP,3}", GetStatColor(character.HP, 15, 25));
            terminal.WriteLine("  - Your life force", "gray");

            terminal.Write($"  Strength:      ");
            terminal.Write($"{character.Strength,3}", GetStatColor(character.Strength, 6, 12));
            terminal.WriteLine("  - Melee damage bonus", "gray");

            terminal.Write($"  Defence:       ");
            terminal.Write($"{character.Defence,3}", GetStatColor(character.Defence, 5, 10));
            terminal.WriteLine("  - Reduces damage taken", "gray");

            terminal.Write($"  Stamina:       ");
            terminal.Write($"{character.Stamina,3}", GetStatColor(character.Stamina, 5, 10));
            terminal.WriteLine("  - Combat ability pool", "gray");

            terminal.Write($"  Agility:       ");
            terminal.Write($"{character.Agility,3}", GetStatColor(character.Agility, 5, 10));
            terminal.WriteLine("  - Dodge chance, extra attacks", "gray");

            terminal.Write($"  Dexterity:     ");
            terminal.Write($"{character.Dexterity,3}", GetStatColor(character.Dexterity, 5, 10));
            terminal.WriteLine("  - Hit chance, critical hits", "gray");

            terminal.Write($"  Constitution:  ");
            terminal.Write($"{character.Constitution,3}", GetStatColor(character.Constitution, 5, 10));
            terminal.WriteLine("  - Bonus HP, poison resist", "gray");

            terminal.Write($"  Intelligence:  ");
            terminal.Write($"{character.Intelligence,3}", GetStatColor(character.Intelligence, 5, 10));
            terminal.WriteLine("  - Spell damage, mana pool", "gray");

            terminal.Write($"  Wisdom:        ");
            terminal.Write($"{character.Wisdom,3}", GetStatColor(character.Wisdom, 5, 10));
            terminal.WriteLine("  - Mana efficiency, magic resist", "gray");

            terminal.Write($"  Charisma:      ");
            terminal.Write($"{character.Charisma,3}", GetStatColor(character.Charisma, 5, 10));
            terminal.WriteLine("  - Shop prices, NPC reactions", "gray");

            if (character.MaxMana > 0)
            {
                terminal.Write($"  Mana:          ");
                terminal.Write($"{character.Mana,3}/{character.MaxMana}", "cyan");
                terminal.WriteLine("  - Spellcasting resource", "gray");
            }
            terminal.WriteLine("");
            terminal.WriteLine($"  Total Stats: {totalStats}", totalStats >= 70 ? "bright_green" : totalStats >= 55 ? "yellow" : "red");
            terminal.WriteLine("");

            if (rerollsRemaining > 0)
            {
                terminal.WriteLine($"Re-rolls remaining: {rerollsRemaining}", "yellow");
                terminal.WriteLine("");
                terminal.WriteLine("(A)ccept these stats", "green");
                terminal.WriteLine("(R)e-roll for new stats", "cyan");
                terminal.WriteLine("");

                var choice = await terminal.GetInputAsync("Your choice: ");

                if (choice.ToUpper() == "A")
                {
                    terminal.WriteLine("");
                    terminal.WriteLine("Stats accepted!", "bright_green");
                    await Task.Delay(1000);
                    break;
                }
                else if (choice.ToUpper() == "R")
                {
                    rerollsRemaining--;
                    if (rerollsRemaining == 0)
                    {
                        terminal.WriteLine("");
                        terminal.WriteLine("This is your final roll!", "bright_red");
                        await Task.Delay(1500);
                    }
                    else
                    {
                        terminal.WriteLine("");
                        terminal.WriteLine("Re-rolling stats...", "cyan");
                        await Task.Delay(800);
                    }
                    continue;
                }
                else
                {
                    terminal.WriteLine("Please choose (A)ccept or (R)e-roll.", "red");
                    await Task.Delay(1000);
                    continue;
                }
            }
            else
            {
                // No re-rolls remaining - must accept
                terminal.WriteLine("No re-rolls remaining - these are your final stats!", "bright_red");
                terminal.WriteLine("");
                await terminal.GetInputAsync("Press Enter to accept and continue...");
                break;
            }
        }

        // CRITICAL: Initialize base stats from the rolled values
        // This prevents RecalculateStats() from resetting stats to 0
        character.InitializeBaseStats();

        // Apply stat-derived bonuses (CON->HP, INT/WIS->Mana) so displayed values
        // are correct from the start. Without this, creation shows raw HP/Mana and the
        // first RecalculateStats() (e.g. on equipment purchase) causes an apparent jump.
        character.RecalculateStats();
        character.HP = character.MaxHP;
        character.Mana = character.MaxMana;
    }

    /// <summary>
    /// Get color based on stat value (for display)
    /// </summary>
    private string GetStatColor(long value, int mediumThreshold, int highThreshold)
    {
        if (value >= highThreshold) return "bright_green";
        if (value >= mediumThreshold) return "yellow";
        return "white";
    }

    /// <summary>
    /// Roll stats for a character based on their class and race
    /// Uses 3d6 style rolling with class modifiers
    /// </summary>
    private void RollStats(Character character)
    {
        // Get class base attributes (these are now modifiers, not fixed values)
        var classAttrib = GameConfig.ClassStartingAttributes[character.Class];
        var raceAttrib = GameConfig.RaceAttributes[character.Race];

        // Roll each stat using 3d6 base + class modifier + small random bonus
        // Class attributes act as bonuses to make classes feel distinct
        character.Strength = Roll3d6() + classAttrib.Strength + raceAttrib.StrengthBonus;
        // Defence starts low (no 3d6 roll) - gear and levels provide the bulk of defence
        character.Defence = classAttrib.Defence + raceAttrib.DefenceBonus;
        character.Stamina = Roll3d6() + classAttrib.Stamina + raceAttrib.StaminaBonus;
        character.Agility = Roll3d6() + classAttrib.Agility;
        character.Charisma = Roll3d6() + classAttrib.Charisma;
        character.Dexterity = Roll3d6() + classAttrib.Dexterity;
        character.Wisdom = Roll3d6() + classAttrib.Wisdom;
        character.Intelligence = Roll3d6() + classAttrib.Intelligence;
        character.Constitution = Roll3d6() + classAttrib.Constitution;

        // Store base values for equipment bonus tracking
        character.BaseStrength = character.Strength;
        character.BaseDexterity = character.Dexterity;
        character.BaseConstitution = character.Constitution;
        character.BaseIntelligence = character.Intelligence;
        character.BaseWisdom = character.Wisdom;
        character.BaseCharisma = character.Charisma;

        // HP is rolled differently - 2d6 + class HP bonus + race HP bonus + Constitution bonus
        int constitutionBonus = (int)(character.Constitution / 3); // Constitution adds to HP
        character.HP = Roll2d6() + (classAttrib.HP * 3) + raceAttrib.HPBonus + constitutionBonus;
        character.MaxHP = character.HP;

        // Mana for spellcasters - base from class + Intelligence bonus
        int intelligenceBonus = (int)(character.Intelligence / 4); // Intelligence adds to mana
        character.Mana = classAttrib.Mana + intelligenceBonus;
        character.MaxMana = classAttrib.MaxMana + intelligenceBonus;
    }

    /// <summary>
    /// Roll 3d6 (3 six-sided dice)
    /// </summary>
    private int Roll3d6()
    {
        return random.Next(1, 7) + random.Next(1, 7) + random.Next(1, 7);
    }

    /// <summary>
    /// Roll 2d6 (2 six-sided dice)
    /// </summary>
    private int Roll2d6()
    {
        return random.Next(1, 7) + random.Next(1, 7);
    }
    
    /// <summary>
    /// Generate physical appearance based on race (Pascal USERHUNC.PAS appearance generation)
    /// </summary>
    private void GeneratePhysicalAppearance(Character character)
    {
        var raceAttrib = GameConfig.RaceAttributes[character.Race];
        
        // Generate age (Pascal random range)
        character.Age = random.Next(raceAttrib.MinAge, raceAttrib.MaxAge + 1);
        
        // Generate height (Pascal random range)
        character.Height = random.Next(raceAttrib.MinHeight, raceAttrib.MaxHeight + 1);
        
        // Generate weight (Pascal random range)
        character.Weight = random.Next(raceAttrib.MinWeight, raceAttrib.MaxWeight + 1);
        
        // Generate eye color (Pascal: random(5) + 1)
        character.Eyes = random.Next(1, 6);
        
        // Generate skin color (Pascal race-specific or random for mutants)
        if (character.Race == CharacterRace.Mutant)
        {
            character.Skin = random.Next(1, 11); // Mutants have random skin (1-10)
        }
        else
        {
            character.Skin = raceAttrib.SkinColor;
        }
        
        // Generate hair color (Pascal race-specific or random for mutants)
        if (character.Race == CharacterRace.Mutant)
        {
            character.Hair = random.Next(1, 11); // Mutants have random hair (1-10)
        }
        else
        {
            // Select random hair color from race's possible colors
            if (raceAttrib.HairColors.Length > 0)
            {
                character.Hair = raceAttrib.HairColors[random.Next(raceAttrib.HairColors.Length)];
            }
            else
            {
                character.Hair = 1; // Default to black
            }
        }
    }
    
    /// <summary>
    /// Set starting configuration and status (Pascal USERHUNC.PAS defaults)
    /// </summary>
    private void SetStartingConfiguration(Character character)
    {
        // Set remaining Pascal defaults
        character.WellWish = false;
        character.MKills = 0;
        character.MDefeats = 0;
        character.PKills = 0;
        character.PDefeats = 0;
        character.Interest = 0;
        character.AliveBonus = 0;
        character.Expert = false;
        character.MaxTime = 60; // Default max time per session
        character.Ear = 1; // global_ear_all
        character.CastIn = ' ';
        character.Weapon = 0;
        character.Armor = 0;
        character.APow = 0;
        character.WPow = 0;
        character.DisRes = 0;
        character.AMember = false;
        character.BankGuard = false;
        character.BankWage = 0;
        character.WeapHag = 3;
        character.ArmHag = 3;
        character.RoyTaxPaid = 0;
        character.Wrestlings = 3;
        character.DrinksLeft = 3;
        character.DaysInPrison = 0;
        character.UmanBearTries = 0;
        character.Massage = 0;
        character.GymSessions = 3;
        character.GymOwner = 0;
        character.GymCard = 0;
        character.RoyQuestsToday = 0;
        character.KingVotePoll = 200;
        character.KingLastVote = 0;
        character.Married = false;
        character.Kids = 0;
        character.IntimacyActs = 5;
        character.Pregnancy = 0;
        character.FatherID = "";
        character.TaxRelief = false;
        character.MarriedTimes = 0;
        character.BardSongsLeft = 5;
        character.PrisonEscapes = 2;
        
        // Disease status (all false by default)
        character.Blind = false;
        character.Plague = false;
        character.Smallpox = false;
        character.Measles = false;
        character.Leprosy = false;
        character.Mercy = 0;
        
        // Set last on date to current (Pascal: packed_date)
        character.LastOn = DateTimeOffset.Now.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Show character summary before creation (Pascal display)
    /// </summary>
    private async Task ShowCharacterSummary(Character character)
    {
        terminal.Clear();
        terminal.WriteLine("");
        terminal.WriteLine("--- CHARACTER SUMMARY ---", "bright_green");
        terminal.WriteLine("");
        
        terminal.WriteLine($"Name: {character.Name2}", "cyan");
        terminal.WriteLine($"Race: {GameConfig.RaceNames[(int)character.Race]}", "yellow");
        terminal.WriteLine($"Class: {character.Class}", "yellow");
        terminal.WriteLine($"Sex: {(character.Sex == CharacterSex.Male ? "Male" : "Female")}", "white");
        terminal.WriteLine($"Age: {character.Age}", "white");
        terminal.WriteLine("");
        
        terminal.WriteLine("=== ATTRIBUTES ===", "green");
        terminal.WriteLine($"Hit Points: {character.HP}/{character.MaxHP}", "white");
        terminal.WriteLine($"Strength: {character.Strength}", "white");
        terminal.WriteLine($"Defence: {character.Defence}", "white");
        terminal.WriteLine($"Stamina: {character.Stamina}", "white");
        terminal.WriteLine($"Agility: {character.Agility}", "white");
        terminal.WriteLine($"Dexterity: {character.Dexterity}", "white");
        terminal.WriteLine($"Constitution: {character.Constitution}", "white");
        terminal.WriteLine($"Intelligence: {character.Intelligence}", "white");
        terminal.WriteLine($"Wisdom: {character.Wisdom}", "white");
        terminal.WriteLine($"Charisma: {character.Charisma}", "white");
        if (character.MaxMana > 0)
        {
            terminal.WriteLine($"Mana: {character.Mana}/{character.MaxMana}", "cyan");
        }
        terminal.WriteLine("");
        
        terminal.WriteLine("=== APPEARANCE ===", "green");
        terminal.WriteLine($"Height: {character.Height} cm", "white");
        terminal.WriteLine($"Weight: {character.Weight} kg", "white");
        terminal.WriteLine($"Eyes: {GameConfig.EyeColors[character.Eyes]}", "white");
        terminal.WriteLine($"Hair: {GameConfig.HairColors[character.Hair]}", "white");
        terminal.WriteLine($"Skin: {GameConfig.SkinColors[character.Skin]}", "white");
        terminal.WriteLine("");
        
        terminal.WriteLine("=== STARTING RESOURCES ===", "green");
        terminal.WriteLine($"Gold: {character.Gold}", "yellow");
        terminal.WriteLine($"Experience: {character.Experience}", "white");
        terminal.WriteLine($"Level: {character.Level}", "white");
        terminal.WriteLine($"Healing Potions: {character.Healing}", "white");
        terminal.WriteLine("");
        
        await terminal.GetInputAsync("Press Enter to continue...");
    }
    
    /// <summary>
    /// Show race help text (Pascal RACEHELP display)
    /// </summary>
    private async Task ShowRaceHelp()
    {
        terminal.Clear();
        terminal.WriteLine("");
        terminal.WriteLine("--- RACE INFORMATION ---", "bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(GameConfig.RaceHelpText, "white");
        await terminal.GetInputAsync("Press Enter to continue...");
    }
    
    /// <summary>
    /// Show class help text (Pascal class help)
    /// </summary>
    private async Task ShowClassHelp()
    {
        terminal.Clear();
        terminal.WriteLine("");
        terminal.WriteLine("--- CLASS INFORMATION ---", "bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(GameConfig.ClassHelpText, "white");
        await terminal.GetInputAsync("Press Enter to continue...");
    }
    
    /// <summary>
    /// Pascal confirm function implementation
    /// </summary>
    private async Task<bool> ConfirmChoice(string message, bool defaultYes)
    {
        var response = await terminal.GetInputAsync($"{message}? (Y/N): ");

        if (string.IsNullOrEmpty(response))
        {
            return defaultYes;
        }

        return response.ToUpper() == "Y";
    }
    
    /// <summary>
    /// Generate unique player ID (Pascal crypt(15))
    /// </summary>
    private string GenerateUniqueID()
    {
        return Guid.NewGuid().ToString("N")[..15];
    }
}
