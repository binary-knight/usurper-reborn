using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        // Reset story progress for a fresh start — but preserve NG+ cycle data
        // (CreateNewGame already handles the NG+ vs fresh distinction)
        if (StoryProgressionSystem.Instance.CurrentCycle <= 1)
        {
            StoryProgressionSystem.Instance.FullReset();
        }

        terminal.WriteLine("");
        terminal.WriteLine("--- CHARACTER CREATION ---", "bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("Welcome to the medieval world of Usurper...", "yellow");
        terminal.WriteLine("");

        // Create base character with Pascal defaults
        var character = CreateBaseCharacter(playerName);

        try
        {
            // Step 1: Choose character name
            // Name1 = internal key (BBS username in door mode), Name2 = display name
            string characterName;
            if (DoorMode.IsInDoorMode)
            {
                // BBS username is the save key
                character.Name1 = DoorMode.GetPlayerName();

                // Let player choose a display name
                terminal.WriteLine($"BBS Login: {character.Name1}", "gray");
                terminal.WriteLine("");
                characterName = await SelectCharacterName();
                if (string.IsNullOrEmpty(characterName))
                {
                    return null; // User aborted
                }
                character.Name2 = characterName;
            }
            else if (SqlSaveBackend.IsAltCharacter(playerName))
            {
                // Alt character: Name1 = DB key (e.g. "rage__alt"), Name2 = player-chosen display name
                character.Name1 = playerName;
                terminal.WriteLine("  Choose a name for your alt character:", "bright_cyan");
                terminal.WriteLine("");
                characterName = await SelectCharacterName();
                if (string.IsNullOrEmpty(characterName))
                {
                    return null; // User aborted
                }
                character.Name2 = characterName;
            }
            else if (!string.IsNullOrWhiteSpace(playerName))
            {
                // Name1 = save key (account name in online mode, chosen name otherwise)
                character.Name1 = playerName;

                if (DoorMode.IsOnlineMode)
                {
                    // Online mode: account name is the save key, let player choose a display name
                    terminal.WriteLine($"Account: {playerName}", "gray");
                    terminal.WriteLine("");
                    terminal.WriteLine("Choose a character name (or press Enter to use your account name):", "bright_cyan");
                    terminal.WriteLine("");
                    characterName = await SelectCharacterName(allowEmpty: true);
                    if (string.IsNullOrWhiteSpace(characterName))
                        characterName = playerName; // Default to account name
                    character.Name2 = characterName;
                }
                else
                {
                    // Local/save slot: name already provided, use it directly
                    characterName = playerName;
                    terminal.WriteLine($"Creating character: {characterName}", "cyan");
                    terminal.WriteLine("");
                    character.Name2 = characterName;
                }
            }
            else
            {
                characterName = await SelectCharacterName();
                if (string.IsNullOrEmpty(characterName))
                {
                    return null; // User aborted
                }
                character.Name1 = characterName;
                character.Name2 = characterName;
            }
            
            // Step 2: Select gender (Pascal gender selection)
            character.Sex = await SelectGender();
            
            // Step 3: Select race (Pascal race selection with help + portrait preview)
            character.Race = await SelectRace(character.Name2, character.Sex);
            
            // Step 4: Select class (Pascal class selection with validation)
            character.Class = await SelectClass(character.Race);

            // Step 5: Select difficulty mode (skipped in online mode - server admin sets difficulty)
            if (DoorMode.IsOnlineMode)
            {
                character.Difficulty = DifficultyMode.Normal;
                DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
            }
            else
            {
                character.Difficulty = await SelectDifficulty();
                DifficultySystem.CurrentDifficulty = character.Difficulty;
            }

            // Step 6: Roll stats with re-roll option (up to 5 re-rolls)
            await RollCharacterStats(character);

            // Step 7: Generate physical appearance (Pascal appearance generation)
            GeneratePhysicalAppearance(character);

            // Step 8: Set starting equipment and configuration
            SetStartingConfiguration(character);

            // Step 9: Show character summary and confirm
            await ShowCharacterSummary(character);
            
            var confirm = await terminal.GetInputAsync("Create this character? (Y/n): ");
            if (!string.IsNullOrEmpty(confirm) && confirm.ToUpper() != "Y")
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
        catch (OperationCanceledException)
        {
            // User chose to abort — not an error
            return null;
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"Error during character creation: {ex.Message}", "red");
            DebugLogger.Instance?.LogError("CHARCREATE", $"{ex}");
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
    private async Task<string> SelectCharacterName(bool allowEmpty = false)
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
                if (allowEmpty)
                    return ""; // Caller handles the default
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

            // Check for duplicate display names in online mode
            // Allow reuse of the player's own account name (e.g., NG+ reroll keeping same name)
            if (DoorMode.IsOnlineMode)
            {
                var ownAccount = DoorMode.GetPlayerName()?.ToLowerInvariant();
                var existingNames = SaveSystem.Instance.GetAllPlayerNames();
                if (existingNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(n, ownAccount, StringComparison.OrdinalIgnoreCase)))
                {
                    terminal.WriteLine("That name is already taken! Choose another.", "red");
                    continue;
                }
            }

            terminal.WriteLine("");
            terminal.WriteLine($"{name} is what you want? (Y/n)", "yellow");
            var confirm = await terminal.GetInputAsync("");

            if (string.IsNullOrEmpty(confirm) || confirm.ToUpper() == "Y")
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
                    terminal.WriteLine("═══════════════════════════════════════════", "bright_red");
                    terminal.WriteLine("    NIGHTMARE MODE — PERMADEATH", DifficultySystem.GetColor(DifficultyMode.Nightmare));
                    terminal.WriteLine("═══════════════════════════════════════════", "bright_red");
                    terminal.WriteLine("");
                    terminal.WriteLine("Death is permanent. Your save will be", "red");
                    terminal.WriteLine("deleted if you die. No resurrections.", "red");
                    terminal.WriteLine("No mercy. No second chances.", "red");
                    terminal.WriteLine("");
                    var confirm = await terminal.GetInputAsync("Death is PERMANENT. Are you SURE? (y/N): ");
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
    private async Task<CharacterRace> SelectRace(string playerName, CharacterSex sex)
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
                DisplayRaceOption(5, "Troll", CharacterRace.Troll, "*regeneration");
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

            // Handle race selection — show full preview with portrait + stats
            if (int.TryParse(choice, out int raceChoice) && raceChoice >= 0 && raceChoice <= 9)
            {
                var race = (CharacterRace)raceChoice;

                if (await ShowRacePreview(race, playerName, sex))
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

    #region Race Preview Screen

    /// <summary>
    /// Show full-width race info card with stat bars, classes, and description.
    /// Returns true if player confirms the selection.
    /// </summary>
    private async Task<bool> ShowRacePreview(CharacterRace race, string playerName, CharacterSex sex)
    {
        // Screen reader mode: plain text, no boxes or bars
        if (GameConfig.ScreenReaderMode)
            return await ShowRacePreviewScreenReader(race);

        // Try side-by-side portrait layout if a portrait exists for this race
        var portrait = RacePortraits.GetCroppedPortrait(race, 38);
        if (portrait != null)
            return await ShowRacePreviewSideBySide(race, portrait);

        // Fallback: original card layout (no portrait)
        return await ShowRacePreviewCard(race);
    }

    /// <summary>
    /// Side-by-side race preview: ANSI art portrait (left) + stats panel (right).
    /// Fits in 80x25 (79 chars wide, 25 rows).
    /// </summary>
    private async Task<bool> ShowRacePreviewSideBySide(CharacterRace race, string[] portraitLines)
    {
        terminal.Clear();

        const int TOTAL_W = 79;   // total box width (matches standard location headers)
        const int LEFT_W = 38;    // portrait panel interior width
        const int RIGHT_W = 38;   // stats panel interior width  (1+38+1+38+1 = 79)
        const int CONTENT_ROWS = 18; // rows of portrait + stats content (18 fits 24-row BBS)

        var raceAttrib = GameConfig.RaceAttributes[race];
        string raceName = GameConfig.RaceNames[(int)race];

        // ── Row 1: Top border with race name ──
        string title = $" {raceName.ToUpper()} ";
        int leftPad = (TOTAL_W - 2 - title.Length) / 2;
        int rightPad = TOTAL_W - 2 - title.Length - leftPad;
        terminal.Write("╔", "gray");
        terminal.Write(new string('═', leftPad), "gray");
        terminal.Write(title, "bright_yellow");
        terminal.Write(new string('═', rightPad), "gray");
        terminal.WriteLine("╗", "gray");

        // ── Row 2: Split separator ──
        terminal.Write("╠", "gray");
        terminal.Write(new string('═', LEFT_W), "gray");
        terminal.Write("╦", "gray");
        terminal.Write(new string('═', RIGHT_W), "gray");
        terminal.WriteLine("╣", "gray");

        // ── Build stats panel lines (RIGHT_W chars each, with [color] tags) ──
        var statsLines = BuildStatsPanel(race, raceAttrib, RIGHT_W);

        // ── Rows 3-20: Side-by-side content ──
        for (int row = 0; row < CONTENT_ROWS; row++)
        {
            // Left border
            terminal.Write("║", "gray");

            // Portrait (raw ANSI)
            if (row < portraitLines.Length)
                terminal.WriteRawAnsi(portraitLines[row]);
            else
            {
                terminal.Write(new string(' ', LEFT_W));
                terminal.WriteRawAnsi("\x1b[0m");
            }

            // Middle divider
            terminal.Write("║", "gray");

            // Stats panel (uses [color] tags via WriteLine markup)
            if (row < statsLines.Count)
                WriteStatsPanelLine(statsLines[row], RIGHT_W);
            else
                terminal.Write(new string(' ', RIGHT_W));

            // Right border
            terminal.WriteLine("║", "gray");
        }

        // ── Row 21: Merge separator ──
        terminal.Write("╠", "gray");
        terminal.Write(new string('═', LEFT_W), "gray");
        terminal.Write("╩", "gray");
        terminal.Write(new string('═', RIGHT_W), "gray");
        terminal.WriteLine("╣", "gray");

        // ── Row 22: Confirm prompt ──
        var raceDesc = GameConfig.RaceDescriptions[race];
        string prompt = $" Be {raceDesc}? [Y]es [N]o";
        terminal.Write("║", "gray");
        terminal.Write(prompt.PadRight(TOTAL_W - 2), "white");
        terminal.WriteLine("║", "gray");

        // ── Row 23: Bottom border ──
        terminal.Write("╚", "gray");
        terminal.Write(new string('═', TOTAL_W - 2), "gray");
        terminal.WriteLine("╝", "gray");

        var response = await terminal.GetInputAsync("");

        return !string.IsNullOrEmpty(response) &&
               (response.ToUpper() == "Y" || response.ToUpper() == "YES");
    }

    /// <summary>
    /// Build the stats panel content lines for the right side of the portrait view.
    /// Each entry is either a plain string or a tuple of (text, color) write instructions.
    /// Returns a list of action delegates that write one stats line.
    /// </summary>
    private List<Action> BuildStatsPanel(CharacterRace race, RaceAttributes raceAttrib, int panelWidth)
    {
        var lines = new List<Action>();

        // Stat bars
        void AddStatBar(string label, int value, int maxValue)
        {
            lines.Add(() =>
            {
                const int barWidth = 12;
                int filled = (int)Math.Round((float)value / maxValue * barWidth);
                filled = Math.Clamp(filled, 1, barWidth);

                string lbl = $" {label,-9}";
                string fill = new string('\u2588', filled);
                string empty = new string('\u2591', barWidth - filled);
                string bonus = $" +{value}";

                terminal.Write(lbl, "cyan");
                terminal.Write(fill, "bright_green");
                terminal.Write(empty, "gray");
                terminal.Write(bonus, "white");

                int used = lbl.Length + barWidth + bonus.Length;
                if (used < panelWidth)
                    terminal.Write(new string(' ', panelWidth - used));
            });
        }

        void AddBlank()
        {
            lines.Add(() => terminal.Write(new string(' ', panelWidth)));
        }

        void AddSeparator()
        {
            lines.Add(() => terminal.Write(new string('─', panelWidth), "gray"));
        }

        void AddText(string text, string color = "white")
        {
            lines.Add(() =>
            {
                int visLen = Math.Min(text.Length, panelWidth - 1);
                terminal.Write(" " + text.Substring(0, visLen).PadRight(panelWidth - 1), color);
            });
        }

        // ── Stat bars ──
        AddStatBar("HP", raceAttrib.HPBonus, 17);
        AddStatBar("Strength", raceAttrib.StrengthBonus, 5);
        AddStatBar("Defence", raceAttrib.DefenceBonus, 5);
        AddStatBar("Stamina", raceAttrib.StaminaBonus, 5);

        // ── Separator ──
        AddSeparator();

        // ── Description ──
        string desc = GetRaceDescription(race);
        // Word-wrap description into panelWidth-2 chars (1 margin each side)
        var descWords = desc.Split(' ');
        var descLine = new StringBuilder();
        foreach (var word in descWords)
        {
            if (descLine.Length + word.Length + 1 > panelWidth - 2)
            {
                AddText(descLine.ToString(), "bright_yellow");
                descLine.Clear();
            }
            if (descLine.Length > 0) descLine.Append(' ');
            descLine.Append(word);
        }
        if (descLine.Length > 0) AddText(descLine.ToString(), "bright_yellow");

        // ── Separator ──
        AddSeparator();

        // ── Available classes ──
        var allClasses = new[] {
            CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Ranger,
            CharacterClass.Assassin, CharacterClass.Bard, CharacterClass.Jester,
            CharacterClass.Alchemist, CharacterClass.Magician, CharacterClass.Cleric,
            CharacterClass.Sage, CharacterClass.Barbarian
        };
        var restricted = GameConfig.InvalidCombinations.ContainsKey(race)
            ? GameConfig.InvalidCombinations[race]
            : Array.Empty<CharacterClass>();
        var available = allClasses.Where(c => !restricted.Contains(c)).ToList();

        if (available.Count == allClasses.Length)
        {
            AddText("Classes: All", "cyan");
        }
        else
        {
            AddText("Classes:", "cyan");
            // Word-wrap class list
            var classList = new StringBuilder();
            foreach (var cls in available)
            {
                string name = cls.ToString();
                if (classList.Length + name.Length + 2 > panelWidth - 2)
                {
                    AddText(classList.ToString(), "white");
                    classList.Clear();
                }
                if (classList.Length > 0) classList.Append(", ");
                classList.Append(name);
            }
            if (classList.Length > 0) AddText(classList.ToString(), "white");
        }

        // ── Restricted note (word-wrapped) ──
        if (restricted.Length > 0 && GameConfig.RaceRestrictionReasons.ContainsKey(race))
        {
            string reason = GameConfig.RaceRestrictionReasons[race];
            var reasonWords = reason.Split(' ');
            var reasonLine = new StringBuilder();
            foreach (var word in reasonWords)
            {
                if (reasonLine.Length + word.Length + 1 > panelWidth - 2)
                {
                    AddText(reasonLine.ToString(), "red");
                    reasonLine.Clear();
                }
                if (reasonLine.Length > 0) reasonLine.Append(' ');
                reasonLine.Append(word);
            }
            if (reasonLine.Length > 0) AddText(reasonLine.ToString(), "red");
        }

        // ── Separator ──
        AddSeparator();

        // ── Special trait ──
        string special = race switch
        {
            CharacterRace.Troll => "Regeneration",
            CharacterRace.Gnoll => "Poisonous Bite",
            _ => "None"
        };
        AddText($"Special: {special}", "cyan");
        if (race == CharacterRace.Troll) AddText("Heals HP each round", "gray");
        else if (race == CharacterRace.Gnoll) AddText("Chance to poison foes", "gray");

        // Pad remaining rows with blanks (18 = CONTENT_ROWS for BBS fit)
        while (lines.Count < 18)
            AddBlank();

        return lines;
    }

    /// <summary>
    /// Write a single stats panel line using the action delegate.
    /// </summary>
    private void WriteStatsPanelLine(Action writeAction, int panelWidth)
    {
        writeAction();
    }

    /// <summary>
    /// Original card layout (no portrait). Used when no ANSI art is available.
    /// </summary>
    private async Task<bool> ShowRacePreviewCard(CharacterRace race)
    {
        terminal.Clear();

        const int W = 76; // card width (centered in 80 cols, 2-char margin each side)
        string pad = new string(' ', (80 - W) / 2); // left padding to center

        var raceAttrib = GameConfig.RaceAttributes[race];
        string raceName = GameConfig.RaceNames[(int)race];

        // ── Top border with race name ──
        CardTopBorder(pad, W, raceName);

        CardBlank(pad, W);

        // ── Description ──
        string desc = GetRaceDescription(race);
        CardLine(pad, W, $"  [bright_yellow]\"{desc}\"");

        // ── Separator ──
        CardSeparator(pad, W);
        CardBlank(pad, W);

        // ── Stat bars ──
        CardStatBar(pad, W, "HP",       raceAttrib.HPBonus, 17);
        CardStatBar(pad, W, "Strength", raceAttrib.StrengthBonus, 5);
        CardStatBar(pad, W, "Defence",  raceAttrib.DefenceBonus, 5);
        CardStatBar(pad, W, "Stamina",  raceAttrib.StaminaBonus, 5);

        // ── Separator ──
        CardSeparator(pad, W);
        CardBlank(pad, W);

        // ── Available classes ──
        var allClasses = new[] {
            CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Ranger,
            CharacterClass.Assassin, CharacterClass.Bard, CharacterClass.Jester,
            CharacterClass.Alchemist, CharacterClass.Magician, CharacterClass.Cleric,
            CharacterClass.Sage, CharacterClass.Barbarian
        };
        var restricted = GameConfig.InvalidCombinations.ContainsKey(race)
            ? GameConfig.InvalidCombinations[race]
            : Array.Empty<CharacterClass>();
        var available = allClasses.Where(c => !restricted.Contains(c)).ToList();

        if (available.Count == allClasses.Length)
        {
            CardLine(pad, W, "  [cyan]Classes:  [white]All classes available");
        }
        else
        {
            var classNames = available.Select(c => c.ToString());
            string classList = string.Join(", ", classNames);
            if (("  Classes:  " + classList).Length <= W - 4)
            {
                CardLine(pad, W, $"  [cyan]Classes:  [white]{classList}");
            }
            else
            {
                CardLine(pad, W, $"  [cyan]Classes:");
                var row1 = string.Join(", ", available.Take(available.Count / 2 + 1).Select(c => c.ToString()));
                var row2 = string.Join(", ", available.Skip(available.Count / 2 + 1).Select(c => c.ToString()));
                CardLine(pad, W, $"  [white]{row1}");
                CardLine(pad, W, $"  [white]{row2}");
            }
        }

        // ── Restricted classes (if any) ──
        if (restricted.Length > 0 && GameConfig.RaceRestrictionReasons.ContainsKey(race))
        {
            CardLine(pad, W, $"  [red]{GameConfig.RaceRestrictionReasons[race]}");
        }

        CardBlank(pad, W);

        // ── Special trait ──
        string special = race switch
        {
            CharacterRace.Troll => "[yellow]Regeneration [gray]- Heals HP each combat round",
            CharacterRace.Gnoll => "[yellow]Poisonous Bite [gray]- Chance to poison enemies",
            _ => "[gray]None"
        };
        CardLine(pad, W, $"  [cyan]Special:  {special}");

        CardBlank(pad, W);

        // ── Bottom border ──
        CardBottomBorder(pad, W);

        // ── Confirm prompt ──
        terminal.WriteLine("");
        var raceDesc = GameConfig.RaceDescriptions[race];
        var response = await terminal.GetInputAsync($"{pad} Be {raceDesc}? (Y/N): ");

        return !string.IsNullOrEmpty(response) &&
               (response.ToUpper() == "Y" || response.ToUpper() == "YES");
    }

    /// <summary>
    /// Write a blank card line: ║ (spaces) ║
    /// </summary>
    private void CardBlank(string pad, int cardWidth)
    {
        terminal.Write(pad, "gray");
        terminal.Write("║", "gray");
        terminal.Write(new string(' ', cardWidth - 2));
        terminal.WriteLine("║", "gray");
    }

    /// <summary>
    /// Write a card line with colored content: ║ content (padded to width) ║
    /// </summary>
    private void CardLine(string pad, int cardWidth, string content)
    {
        // Count visible characters in content (strip [color] tags)
        int visibleLen = 0;
        int idx = 0;
        while (idx < content.Length)
        {
            if (content[idx] == '[')
            {
                int end = content.IndexOf(']', idx);
                if (end > idx) { idx = end + 1; continue; }
            }
            visibleLen++;
            idx++;
        }

        terminal.Write(pad, "gray");
        terminal.Write("║", "gray");
        UsurperRemake.UI.ANSIArt.DisplayColoredText(terminal, content);
        int remaining = cardWidth - 2 - visibleLen;
        if (remaining > 0) terminal.Write(new string(' ', remaining));
        terminal.WriteLine("║", "gray");
    }

    /// <summary>
    /// Write a stat bar line inside the card (single column, used by race card).
    /// Format: ║  Label    ████████████░░░░░░░░  +N       ║
    /// </summary>
    private void CardStatBar(string pad, int cardWidth, string label, int value, int maxValue)
    {
        const int barWidth = 24;
        int filled = (int)Math.Round((float)value / maxValue * barWidth);
        filled = Math.Clamp(filled, 1, barWidth);

        string filledBar = new string('█', filled);
        string emptyBar = new string('░', barWidth - filled);
        string bonus = $"+{value}";

        string labelPad = $"  {label,-10}";
        string bonusPad = $"  {bonus}";

        int contentLen = labelPad.Length + barWidth + bonusPad.Length;
        int trailing = cardWidth - 2 - contentLen;

        terminal.Write(pad, "gray");
        terminal.Write("║", "gray");
        terminal.Write(labelPad, "white");
        terminal.Write(filledBar, "bright_green");
        terminal.Write(emptyBar, "gray");
        terminal.Write(bonusPad, "white");
        if (trailing > 0) terminal.Write(new string(' ', trailing));
        terminal.WriteLine("║", "gray");
    }

    /// <summary>
    /// Write two stat bars side-by-side inside the card (used by class card).
    /// Format: ║  HP  ██████████░░░░  +4     AGI ██████░░░░░░░░  +3       ║
    /// </summary>
    private void CardStatBarPair(string pad, int cardWidth, string label1, int value1, string label2, int value2, int maxValue)
    {
        const int barWidth = 14;

        // Left column: "  LBL ██████████████░  +N"
        int filled1 = (int)Math.Round((float)value1 / maxValue * barWidth);
        filled1 = Math.Clamp(filled1, 1, barWidth);
        string fill1 = new string('█', filled1);
        string empty1 = new string('░', barWidth - filled1);

        // Right column: "  LBL ██████████████░  +N"
        int filled2 = (int)Math.Round((float)value2 / maxValue * barWidth);
        filled2 = Math.Clamp(filled2, 1, barWidth);
        string fill2 = new string('█', filled2);
        string empty2 = new string('░', barWidth - filled2);

        // Layout: 2 margin + 4 label + 14 bar + 2 space + 2 bonus = 24 per col, 5 gap
        // Total: 24 + 5 + 4 label + 14 bar + 2 space + 2 bonus = 51
        string lbl1 = $"  {label1,-4}";  // "  HP  " or "  STR "
        string bon1 = $"  +{value1}";
        string gap  = "     ";
        string lbl2 = $"{label2,-4}";    // "AGI " or "CHA "
        string bon2 = $"  +{value2}";

        int contentLen = lbl1.Length + barWidth + bon1.Length + gap.Length + lbl2.Length + barWidth + bon2.Length;
        int trailing = cardWidth - 2 - contentLen;

        terminal.Write(pad, "gray");
        terminal.Write("║", "gray");
        terminal.Write(lbl1, "white");
        terminal.Write(fill1, "bright_green");
        terminal.Write(empty1, "gray");
        terminal.Write(bon1, "white");
        terminal.Write(gap);
        terminal.Write(lbl2, "white");
        terminal.Write(fill2, "bright_green");
        terminal.Write(empty2, "gray");
        terminal.Write(bon2, "white");
        if (trailing > 0) terminal.Write(new string(' ', trailing));
        terminal.WriteLine("║", "gray");
    }

    /// <summary>Card top border: ╔═══ Title ══════╗</summary>
    private void CardTopBorder(string pad, int cardWidth, string title)
    {
        string topLabel = $"═══ {title} ";
        int topDashes = cardWidth - 2 - topLabel.Length;
        terminal.WriteLine("");
        terminal.Write(pad, "gray");
        terminal.Write("╔", "gray");
        terminal.Write(topLabel, "bright_yellow");
        terminal.Write(new string('═', Math.Max(0, topDashes)), "gray");
        terminal.WriteLine("╗", "gray");
    }

    /// <summary>Card separator: ╠──────────────╣</summary>
    private void CardSeparator(string pad, int cardWidth)
    {
        terminal.Write(pad, "gray");
        terminal.Write("╠", "gray");
        terminal.Write(new string('─', cardWidth - 2), "gray");
        terminal.WriteLine("╣", "gray");
    }

    /// <summary>Card bottom border: ╚══════════════╝</summary>
    private void CardBottomBorder(string pad, int cardWidth)
    {
        terminal.Write(pad, "gray");
        terminal.Write("╚", "gray");
        terminal.Write(new string('═', cardWidth - 2), "gray");
        terminal.WriteLine("╝", "gray");
    }

    private static string GetRaceDescription(CharacterRace race) => race switch
    {
        CharacterRace.Human => "Balanced in all areas. Can be any class.",
        CharacterRace.Hobbit => "Small but agile. Excellent rogues and rangers.",
        CharacterRace.Elf => "Graceful and magical. Excellent mages and clerics.",
        CharacterRace.HalfElf => "Versatile like humans. Can be any class.",
        CharacterRace.Dwarf => "Strong and tough. Great warriors, but distrust magic.",
        CharacterRace.Troll => "Massive brutes with natural regeneration.",
        CharacterRace.Orc => "Aggressive fighters with limited magic ability.",
        CharacterRace.Gnome => "Small and clever. Great mages, poor heavy fighters.",
        CharacterRace.Gnoll => "Pack hunters with a poisonous bite.",
        CharacterRace.Mutant => "Chaotic and unpredictable. Can be any class.",
        _ => "Unknown heritage."
    };

    private async Task<bool> ShowClassPreview(CharacterClass characterClass, CharacterRace race)
    {
        // Screen reader mode: plain text, no boxes or bars
        if (GameConfig.ScreenReaderMode)
            return await ShowClassPreviewScreenReader(characterClass);

        // Try side-by-side portrait layout if a portrait exists for this class
        var portrait = RacePortraits.GetCroppedClassPortrait(characterClass, 38);
        if (portrait != null)
            return await ShowClassPreviewSideBySide(characterClass, portrait);

        // Fallback: original card layout (no portrait)
        return await ShowClassPreviewCard(characterClass);
    }

    /// <summary>
    /// Side-by-side class preview: ANSI art portrait (left) + stats panel (right).
    /// Fits in 80x24 (79 chars wide, 23 rows + 1 input).
    /// </summary>
    private async Task<bool> ShowClassPreviewSideBySide(CharacterClass characterClass, string[] portraitLines)
    {
        terminal.Clear();

        const int TOTAL_W = 79;
        const int LEFT_W = 38;
        const int RIGHT_W = 38;
        const int CONTENT_ROWS = 18;

        string className = characterClass.ToString();

        // ── Row 1: Top border with class name ──
        string title = $" {className.ToUpper()} ";
        int leftPad = (TOTAL_W - 2 - title.Length) / 2;
        int rightPad = TOTAL_W - 2 - title.Length - leftPad;
        terminal.Write("╔", "gray");
        terminal.Write(new string('═', leftPad), "gray");
        terminal.Write(title, "bright_yellow");
        terminal.Write(new string('═', rightPad), "gray");
        terminal.WriteLine("╗", "gray");

        // ── Row 2: Split separator ──
        terminal.Write("╠", "gray");
        terminal.Write(new string('═', LEFT_W), "gray");
        terminal.Write("╦", "gray");
        terminal.Write(new string('═', RIGHT_W), "gray");
        terminal.WriteLine("╣", "gray");

        // ── Build stats panel ──
        var statsLines = BuildClassStatsPanel(characterClass, RIGHT_W);

        // ── Rows 3-20: Side-by-side content ──
        for (int row = 0; row < CONTENT_ROWS; row++)
        {
            terminal.Write("║", "gray");

            if (row < portraitLines.Length)
                terminal.WriteRawAnsi(portraitLines[row]);
            else
            {
                terminal.Write(new string(' ', LEFT_W));
                terminal.WriteRawAnsi("\x1b[0m");
            }

            terminal.Write("║", "gray");

            if (row < statsLines.Count)
                WriteStatsPanelLine(statsLines[row], RIGHT_W);
            else
                terminal.Write(new string(' ', RIGHT_W));

            terminal.WriteLine("║", "gray");
        }

        // ── Row 21: Merge separator ──
        terminal.Write("╠", "gray");
        terminal.Write(new string('═', LEFT_W), "gray");
        terminal.Write("╩", "gray");
        terminal.Write(new string('═', RIGHT_W), "gray");
        terminal.WriteLine("╣", "gray");

        // ── Row 22: Confirm prompt ──
        var article = "aeiouAEIOU".Contains(className[0]) ? "an" : "a";
        string prompt = $" Be {article} {className}? [Y]es [N]o";
        terminal.Write("║", "gray");
        terminal.Write(prompt.PadRight(TOTAL_W - 2), "white");
        terminal.WriteLine("║", "gray");

        // ── Row 23: Bottom border ──
        terminal.Write("╚", "gray");
        terminal.Write(new string('═', TOTAL_W - 2), "gray");
        terminal.WriteLine("╝", "gray");

        var response = await terminal.GetInputAsync("");

        return !string.IsNullOrEmpty(response) &&
               (response.ToUpper() == "Y" || response.ToUpper() == "YES");
    }

    /// <summary>
    /// Build class stats panel content for the right side of the portrait view.
    /// Shows category, stat bars (paired), mana, strengths, description.
    /// </summary>
    private List<Action> BuildClassStatsPanel(CharacterClass characterClass, int panelWidth)
    {
        var lines = new List<Action>();
        var attrs = GameConfig.ClassStartingAttributes[characterClass];

        void AddStatBarPair(string label1, int val1, string label2, int val2, int maxVal)
        {
            lines.Add(() =>
            {
                const int barW = 6;
                int filled1 = (int)Math.Round((float)val1 / maxVal * barW);
                filled1 = Math.Clamp(filled1, 0, barW);
                int filled2 = (int)Math.Round((float)val2 / maxVal * barW);
                filled2 = Math.Clamp(filled2, 0, barW);

                string lbl1 = $" {label1,-4}";
                string fill1 = new string('\u2588', filled1);
                string empty1 = new string('\u2591', barW - filled1);
                string val1Str = $"{val1,2}";

                string lbl2 = $" {label2,-4}";
                string fill2 = new string('\u2588', filled2);
                string empty2 = new string('\u2591', barW - filled2);
                string val2Str = $"{val2,2}";

                terminal.Write(lbl1, "cyan");
                terminal.Write(fill1, "bright_green");
                terminal.Write(empty1, "gray");
                terminal.Write(val1Str, "white");

                terminal.Write(lbl2, "cyan");
                terminal.Write(fill2, "bright_green");
                terminal.Write(empty2, "gray");
                terminal.Write(val2Str, "white");

                // Pad to panelWidth: lbl1(5) + barW(6) + val1(2) + lbl2(5) + barW(6) + val2(2) = 26
                int used = 5 + barW + 2 + 5 + barW + 2;
                if (used < panelWidth)
                    terminal.Write(new string(' ', panelWidth - used));
            });
        }

        void AddBlank()
        {
            lines.Add(() => terminal.Write(new string(' ', panelWidth)));
        }

        void AddSeparator()
        {
            lines.Add(() => terminal.Write(new string('─', panelWidth), "gray"));
        }

        void AddText(string text, string color = "white")
        {
            lines.Add(() =>
            {
                int visLen = Math.Min(text.Length, panelWidth - 1);
                terminal.Write(" " + text.Substring(0, visLen).PadRight(panelWidth - 1), color);
            });
        }

        // ── Category ──
        string category = characterClass switch
        {
            CharacterClass.Warrior or CharacterClass.Barbarian or CharacterClass.Paladin => "Melee Fighter",
            CharacterClass.Ranger or CharacterClass.Assassin or CharacterClass.Bard or CharacterClass.Jester => "Hybrid Class",
            CharacterClass.Magician or CharacterClass.Sage or CharacterClass.Cleric or CharacterClass.Alchemist => "Magic User",
            _ => "Adventurer"
        };
        AddText(category, "cyan");

        // ── Separator ──
        AddSeparator();

        // ── Stat bars (paired, 5 rows) ──
        AddStatBarPair("HP",  attrs.HP,           "AGI", attrs.Agility, 5);
        AddStatBarPair("STR", attrs.Strength,     "CHA", attrs.Charisma, 5);
        AddStatBarPair("DEF", attrs.Defence,      "DEX", attrs.Dexterity, 5);
        AddStatBarPair("STA", attrs.Stamina,      "WIS", attrs.Wisdom, 5);
        AddStatBarPair("CON", attrs.Constitution, "INT", attrs.Intelligence, 5);

        // ── Separator ──
        AddSeparator();

        // ── Mana ──
        string manaText;
        string manaColor;
        if (attrs.Mana > 0)
        {
            manaText = $"Mana: {attrs.Mana}";
            manaColor = "bright_green";
        }
        else if (GetClassManaPerLevel(characterClass) > 0)
        {
            manaText = $"Mana: +{GetClassManaPerLevel(characterClass)}/level";
            manaColor = "cyan";
        }
        else
        {
            manaText = "Mana: None";
            manaColor = "gray";
        }
        AddText(manaText, manaColor);

        // ── Strengths ──
        string strengths = GetClassStrengths(characterClass);
        var strengthWords = strengths.Split(' ');
        var strengthLine = new StringBuilder();
        foreach (var word in strengthWords)
        {
            if (strengthLine.Length + word.Length + 1 > panelWidth - 2)
            {
                AddText(strengthLine.ToString(), "white");
                strengthLine.Clear();
            }
            if (strengthLine.Length > 0) strengthLine.Append(' ');
            strengthLine.Append(word);
        }
        if (strengthLine.Length > 0) AddText(strengthLine.ToString(), "white");

        // ── Separator ──
        AddSeparator();

        // ── Description (word-wrapped) ──
        string desc = GetClassDescription(characterClass);
        var descWords = desc.Split(' ');
        var descLine = new StringBuilder();
        foreach (var word in descWords)
        {
            if (descLine.Length + word.Length + 1 > panelWidth - 2)
            {
                AddText(descLine.ToString(), "bright_yellow");
                descLine.Clear();
            }
            if (descLine.Length > 0) descLine.Append(' ');
            descLine.Append(word);
        }
        if (descLine.Length > 0) AddText(descLine.ToString(), "bright_yellow");

        // Pad remaining rows
        while (lines.Count < 18)
            AddBlank();

        return lines;
    }

    /// <summary>
    /// Original card layout for class preview (no portrait).
    /// </summary>
    private async Task<bool> ShowClassPreviewCard(CharacterClass characterClass)
    {
        terminal.Clear();

        const int W = 76;
        string pad = new string(' ', (80 - W) / 2);

        var attrs = GameConfig.ClassStartingAttributes[characterClass];
        string className = characterClass.ToString();

        // Determine class category
        string category = characterClass switch
        {
            CharacterClass.Warrior or CharacterClass.Barbarian or CharacterClass.Paladin => "Melee Fighter",
            CharacterClass.Ranger or CharacterClass.Assassin or CharacterClass.Bard or CharacterClass.Jester => "Hybrid Class",
            CharacterClass.Magician or CharacterClass.Sage or CharacterClass.Cleric or CharacterClass.Alchemist => "Magic User",
            _ => "Adventurer"
        };

        // ── Top border with class name ──
        CardTopBorder(pad, W, className);

        CardBlank(pad, W);

        // ── Category + Description ──
        CardLine(pad, W, $"  [cyan]{category}");
        string desc = GetClassDescription(characterClass);
        CardLine(pad, W, $"  [bright_yellow]\"{desc}\"");

        // ── Separator ──
        CardSeparator(pad, W);
        CardBlank(pad, W);

        // ── Two-column stat bars (5 rows instead of 10) ──
        CardStatBarPair(pad, W, "HP",  attrs.HP,           "AGI", attrs.Agility, 5);
        CardStatBarPair(pad, W, "STR", attrs.Strength,     "CHA", attrs.Charisma, 5);
        CardStatBarPair(pad, W, "DEF", attrs.Defence,      "DEX", attrs.Dexterity, 5);
        CardStatBarPair(pad, W, "STA", attrs.Stamina,      "WIS", attrs.Wisdom, 5);
        CardStatBarPair(pad, W, "CON", attrs.Constitution, "INT", attrs.Intelligence, 5);

        // ── Separator ──
        CardSeparator(pad, W);
        CardBlank(pad, W);

        // ── Mana + Strengths (compact) ──
        string manaCardText;
        if (attrs.Mana > 0)
            manaCardText = $"[bright_green]{attrs.Mana}";
        else if (GetClassManaPerLevel(characterClass) > 0)
            manaCardText = $"[cyan]+{GetClassManaPerLevel(characterClass)}/level";
        else
            manaCardText = "[gray]None";
        CardLine(pad, W, $"  [cyan]Mana: {manaCardText}");
        string strengths = GetClassStrengths(characterClass);
        CardLine(pad, W, $"  [cyan]Strengths:  [white]{strengths}");

        CardBlank(pad, W);

        // ── Bottom border ──
        CardBottomBorder(pad, W);

        // ── Confirm prompt ──
        terminal.WriteLine("");
        var article = "aeiouAEIOU".Contains(className[0]) ? "an" : "a";
        var response = await terminal.GetInputAsync($"{pad} Be {article} {className}? (Y/N): ");

        return !string.IsNullOrEmpty(response) &&
               (response.ToUpper() == "Y" || response.ToUpper() == "YES");
    }

    /// <summary>Screen reader race preview: plain text, no boxes or stat bars.</summary>
    private async Task<bool> ShowRacePreviewScreenReader(CharacterRace race)
    {
        terminal.Clear();

        var raceAttrib = GameConfig.RaceAttributes[race];
        string raceName = GameConfig.RaceNames[(int)race];
        string desc = GetRaceDescription(race);

        terminal.WriteLine("");
        terminal.WriteLine($"Race: {raceName}");
        terminal.WriteLine($"\"{desc}\"");
        terminal.WriteLine("");
        terminal.WriteLine($"Stats: HP +{raceAttrib.HPBonus}, Strength +{raceAttrib.StrengthBonus}, Defence +{raceAttrib.DefenceBonus}, Stamina +{raceAttrib.StaminaBonus}");
        terminal.WriteLine("");

        // Available classes
        var allClasses = new[] {
            CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Ranger,
            CharacterClass.Assassin, CharacterClass.Bard, CharacterClass.Jester,
            CharacterClass.Alchemist, CharacterClass.Magician, CharacterClass.Cleric,
            CharacterClass.Sage, CharacterClass.Barbarian
        };
        var restricted = GameConfig.InvalidCombinations.ContainsKey(race)
            ? GameConfig.InvalidCombinations[race]
            : Array.Empty<CharacterClass>();
        var available = allClasses.Where(c => !restricted.Contains(c)).ToList();

        if (available.Count == allClasses.Length)
        {
            terminal.WriteLine("Classes: All classes available");
        }
        else
        {
            terminal.WriteLine($"Classes: {string.Join(", ", available.Select(c => c.ToString()))}");
        }

        if (restricted.Length > 0 && GameConfig.RaceRestrictionReasons.ContainsKey(race))
        {
            terminal.WriteLine($"Restricted: {GameConfig.RaceRestrictionReasons[race]}");
        }

        terminal.WriteLine("");

        // Special trait
        string special = race switch
        {
            CharacterRace.Troll => "Regeneration - Heals HP each combat round",
            CharacterRace.Gnoll => "Poisonous Bite - Chance to poison enemies",
            _ => "None"
        };
        terminal.WriteLine($"Special: {special}");

        terminal.WriteLine("");
        var raceDesc = GameConfig.RaceDescriptions[race];
        var response = await terminal.GetInputAsync($"Be {raceDesc}? (Y/N): ");

        return !string.IsNullOrEmpty(response) &&
               (response.ToUpper() == "Y" || response.ToUpper() == "YES");
    }

    /// <summary>Screen reader class preview: plain text, no boxes or stat bars.</summary>
    private async Task<bool> ShowClassPreviewScreenReader(CharacterClass characterClass)
    {
        terminal.Clear();

        var attrs = GameConfig.ClassStartingAttributes[characterClass];
        string className = characterClass.ToString();

        string category = characterClass switch
        {
            CharacterClass.Warrior or CharacterClass.Barbarian or CharacterClass.Paladin => "Melee Fighter",
            CharacterClass.Ranger or CharacterClass.Assassin or CharacterClass.Bard or CharacterClass.Jester => "Hybrid Class",
            CharacterClass.Magician or CharacterClass.Sage or CharacterClass.Cleric or CharacterClass.Alchemist => "Magic User",
            _ => "Adventurer"
        };

        string desc = GetClassDescription(characterClass);

        terminal.WriteLine("");
        terminal.WriteLine($"Class: {className} ({category})");
        terminal.WriteLine($"\"{desc}\"");
        terminal.WriteLine("");
        terminal.WriteLine($"Stats: HP +{attrs.HP}, STR +{attrs.Strength}, DEF +{attrs.Defence}, STA +{attrs.Stamina}, AGI +{attrs.Agility}, CHA +{attrs.Charisma}, DEX +{attrs.Dexterity}, WIS +{attrs.Wisdom}, INT +{attrs.Intelligence}, CON +{attrs.Constitution}");
        terminal.WriteLine("");

        string manaText;
        if (attrs.Mana > 0)
            manaText = attrs.Mana.ToString();
        else if (GetClassManaPerLevel(characterClass) > 0)
            manaText = $"+{GetClassManaPerLevel(characterClass)}/level (grows with level)";
        else
            manaText = "None (physical class)";
        terminal.WriteLine($"Mana: {manaText}");
        string strengths = GetClassStrengths(characterClass);
        terminal.WriteLine($"Strengths: {strengths}");

        terminal.WriteLine("");
        var article = "aeiouAEIOU".Contains(className[0]) ? "an" : "a";
        var response = await terminal.GetInputAsync($"Be {article} {className}? (Y/N): ");

        return !string.IsNullOrEmpty(response) &&
               (response.ToUpper() == "Y" || response.ToUpper() == "YES");
    }

    private static string GetClassDescription(CharacterClass cls) => cls switch
    {
        CharacterClass.Warrior => "Strong fighters, masters of weapons. Balanced and reliable.",
        CharacterClass.Barbarian => "Savage fighters with incredible strength and endurance.",
        CharacterClass.Paladin => "Holy warriors of virtue. Strong in combat and spirit.",
        CharacterClass.Ranger => "Woodsmen and trackers. Balanced fighters with survival skills.",
        CharacterClass.Assassin => "Deadly killers, masters of stealth and critical strikes.",
        CharacterClass.Bard => "Musicians and storytellers. Social skills and light combat.",
        CharacterClass.Jester => "Entertainers and tricksters. Very agile and unpredictable.",
        CharacterClass.Magician => "Powerful spellcasters with devastating magic but frail bodies.",
        CharacterClass.Sage => "Scholars and wise magic users. The deepest mana reserves.",
        CharacterClass.Cleric => "Healers and holy magic users. Devoted to faith and wisdom.",
        CharacterClass.Alchemist => "Potion makers and researchers. Wisdom and charisma.",
        _ => "An adventurer of unknown calling."
    };

    private static string GetClassStrengths(CharacterClass cls) => cls switch
    {
        CharacterClass.Warrior => "High HP, STR, DEF, CON. Well-rounded melee.",
        CharacterClass.Barbarian => "Highest HP, STR, STA, CON. Raw power.",
        CharacterClass.Paladin => "High HP, STR, STA, CON. Tough and honorable.",
        CharacterClass.Ranger => "Good STA, DEX. Jack of all trades.",
        CharacterClass.Assassin => "Best DEX. High STR and AGI for ambushes.",
        CharacterClass.Bard => "Good CHA, DEX. Balanced across all stats.",
        CharacterClass.Jester => "Best AGI and DEX. Unpredictable in combat.",
        CharacterClass.Magician => "Best INT and CHA. Powerful offensive magic.",
        CharacterClass.Sage => "Best WIS and INT. Deepest mana pool (50).",
        CharacterClass.Cleric => "Good WIS and CHA. Healing magic and mana.",
        CharacterClass.Alchemist => "Best WIS and INT. High CHA for trading.",
        _ => "Unknown strengths."
    };

    /// <summary>
    /// Returns the mana gained per level for a class, matching LevelMasterLocation.ApplyClassBasedStatIncreases().
    /// Returns 0 for purely physical classes that never gain mana.
    /// </summary>
    private static int GetClassManaPerLevel(CharacterClass cls) => cls switch
    {
        CharacterClass.Magician => 15,
        CharacterClass.Cleric => 12,
        CharacterClass.Sage => 18,
        CharacterClass.Alchemist => 10,
        CharacterClass.Paladin => 5,
        CharacterClass.Bard => 5,
        _ => 0
    };

    #endregion

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

        // Check for unlocked NG+ prestige classes
        var unlockedPrestige = GetUnlockedPrestigeClasses();
        int prestigeStartIndex = 11;
        // Always map unlocked prestige classes to menu indices
        {
            int idx = prestigeStartIndex;
            foreach (var pc in unlockedPrestige)
            {
                menuToClass[idx] = pc;
                idx++;
            }
        }

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

                // Always show prestige classes — unlocked ones selectable, locked ones grayed out
                terminal.WriteLine("");
                terminal.WriteLine("  ═══ PRESTIGE CLASSES (NG+) ═══", "bright_magenta");
                var allPrestige = new[]
                {
                    (CharacterClass.Tidesworn, "Savior ending"),
                    (CharacterClass.Wavecaller, "Savior ending"),
                    (CharacterClass.Cyclebreaker, "Defiant ending"),
                    (CharacterClass.Abysswarden, "Usurper ending"),
                    (CharacterClass.Voidreaver, "Usurper ending")
                };
                int prestigeIdx = prestigeStartIndex;
                foreach (var (pc, unlockReq) in allPrestige)
                {
                    bool isUnlocked = unlockedPrestige.Contains(pc);
                    if (isUnlocked)
                    {
                        terminal.Write($"({prestigeIdx}) ", "bright_magenta");
                        terminal.Write($"{pc,-14}", "bright_white");
                        var desc = GameConfig.PrestigeClassDescriptions.TryGetValue(pc, out var d) ? d : "";
                        terminal.WriteLine($" {desc}", "magenta");
                        prestigeIdx++;
                    }
                    else
                    {
                        terminal.Write($"     ", "dark_gray");
                        terminal.Write($"{pc,-14}", "dark_gray");
                        terminal.WriteLine($" [Locked — requires {unlockReq}]", "dark_gray");
                    }
                }

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

                // Show class preview card with stats
                if (await ShowClassPreview(characterClass, race))
                {
                    return characterClass;
                }

                choice = "?";
            }
            else
            {
                int maxChoice = unlockedPrestige.Count > 0 ? prestigeStartIndex + unlockedPrestige.Count - 1 : 10;
                terminal.WriteLine($"Invalid choice. Please select 0-{maxChoice}, H for help, or A to abort.", "red");
            }
        }
    }

    /// <summary>
    /// Returns the set of NG+ prestige classes unlocked by the player's completed endings.
    /// Requires CurrentCycle >= 2 (at least one completed playthrough).
    /// </summary>
    private static List<CharacterClass> GetUnlockedPrestigeClasses()
    {
        var story = StoryProgressionSystem.Instance;
        var unlocked = new List<CharacterClass>();

        var endingsList = story?.CompletedEndings != null ? string.Join(",", story.CompletedEndings) : "null";
        Console.Error.WriteLine($"[Prestige] cycle={story?.CurrentCycle}, endings=[{endingsList}], count={story?.CompletedEndings?.Count}");
        if (story == null || story.CurrentCycle < 2 || story.CompletedEndings.Count == 0)
        {
            Console.Error.WriteLine($"[Prestige] BLOCKED: story null={story == null}, cycle<2={story?.CurrentCycle < 2}, endings empty={story?.CompletedEndings?.Count == 0}");
            return unlocked;
        }

        var endings = story.CompletedEndings;

        // True ending or Secret ending unlocks all prestige classes
        if (endings.Contains(EndingType.TrueEnding) || endings.Contains(EndingType.Secret))
        {
            unlocked.Add(CharacterClass.Tidesworn);
            unlocked.Add(CharacterClass.Wavecaller);
            unlocked.Add(CharacterClass.Cyclebreaker);
            unlocked.Add(CharacterClass.Abysswarden);
            unlocked.Add(CharacterClass.Voidreaver);
            return unlocked;
        }

        // Savior ending unlocks Holy and Good classes
        if (endings.Contains(EndingType.Savior))
        {
            unlocked.Add(CharacterClass.Tidesworn);
            unlocked.Add(CharacterClass.Wavecaller);
        }

        // Defiant ending unlocks Neutral class
        if (endings.Contains(EndingType.Defiant))
            unlocked.Add(CharacterClass.Cyclebreaker);

        // Usurper ending unlocks Dark and Evil classes
        if (endings.Contains(EndingType.Usurper))
        {
            unlocked.Add(CharacterClass.Abysswarden);
            unlocked.Add(CharacterClass.Voidreaver);
        }

        return unlocked;
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

            // Show effective mana including INT/WIS bonuses (matches what RecalculateStats will give)
            long effectiveMana = character.MaxMana;
            if (effectiveMana > 0)
            {
                effectiveMana += StatEffectsSystem.GetIntelligenceManaBonus(character.Intelligence, character.Level);
                effectiveMana += StatEffectsSystem.GetWisdomManaBonus(character.Wisdom);
            }
            if (effectiveMana > 0)
            {
                terminal.Write($"  Mana:          ");
                terminal.Write($"{effectiveMana,3}/{effectiveMana}", "cyan");
                terminal.WriteLine("  - Spellcasting resource", "gray");
            }
            else if (GetClassManaPerLevel(character.Class) > 0)
            {
                terminal.Write($"  Mana:          ");
                terminal.Write($"+{GetClassManaPerLevel(character.Class)}/level", "cyan");
                terminal.WriteLine("  - Grows with level-ups", "gray");
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
        var hint = defaultYes ? "Y/n" : "y/N";
        var response = await terminal.GetInputAsync($"{message}? ({hint}): ");

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
