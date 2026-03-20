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
        terminal.WriteLine($"--- {Loc.Get("creation.header")} ---", "bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("creation.welcome"), "yellow");
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
                terminal.WriteLine($"{Loc.Get("character_creation.bbs_login")}: {character.Name1}", "gray");
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
                terminal.WriteLine($"  {Loc.Get("creation.choose_display_name")}", "bright_cyan");
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
                    terminal.WriteLine($"{Loc.Get("character_creation.account")}: {playerName}", "gray");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("creation.choose_display_online"), "bright_cyan");
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
                    terminal.WriteLine(Loc.Get("creation.creating", characterName), "cyan");
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

            // Step 2b: Select orientation
            character.Orientation = await SelectOrientation();

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
            
            var confirm = await terminal.GetInputAsync(Loc.Get("creation.confirm"));
            if (!string.IsNullOrEmpty(confirm) && confirm.ToUpper() != "Y")
            {
                terminal.WriteLine(Loc.Get("creation.aborted"), "red");
                return null;
            }

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("creation.created"), "green");
            terminal.WriteLine(Loc.Get("creation.entering"), "cyan");
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
            terminal.WriteLine(Loc.Get("creation.enter_name"), "cyan");
            terminal.WriteLine(Loc.Get("creation.name_known_as"));
            terminal.WriteLine("");

            name = await terminal.GetInputAsync(Loc.Get("creation.name_prompt"));

            if (string.IsNullOrWhiteSpace(name))
            {
                if (allowEmpty)
                    return ""; // Caller handles the default
                terminal.WriteLine(Loc.Get("creation.name_required"), "red");
                continue;
            }

            name = name.Trim();

            // Pascal validation: Check for forbidden names
            var upperName = name.ToUpper();
            if (GameConfig.ForbiddenNames.Contains(upperName))
            {
                terminal.WriteLine(Loc.Get("creation.name_forbidden"), "red");
                continue;
            }

            // Check for duplicate display names in online mode
            // Allow reuse of the player's own account name (e.g., NG+ reroll keeping same name)
            if (DoorMode.IsOnlineMode)
            {
                var ownAccount = DoorMode.GetPlayerName() ?? "";
                if (SaveSystem.Instance.IsDisplayNameTaken(name, ownAccount))
                {
                    terminal.WriteLine(Loc.Get("creation.name_taken"), "red");
                    continue;
                }
            }

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("creation.name_confirm", name), "yellow");
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
            terminal.WriteLine(Loc.Get("creation.gender"), "cyan");
            terminal.WriteLine(Loc.Get("creation.male"), "white");
            terminal.WriteLine(Loc.Get("creation.female"), "white");

            var choice = await terminal.GetInputAsync(Loc.Get("creation.gender_prompt"));
            
            switch (choice.ToUpper())
            {
                case "M":
                    if (await ConfirmChoice(Loc.Get("creation.gender_confirm_m"), false))
                        return CharacterSex.Male;
                    break;

                case "F":
                    if (await ConfirmChoice(Loc.Get("creation.gender_confirm_f"), false))
                        return CharacterSex.Female;
                    break;
                    
                default:
                    terminal.WriteLine(Loc.Get("creation.gender_invalid"), "red");
                    break;
            }
        }
    }

    /// <summary>
    /// Select character orientation (affects NPC romance availability)
    /// </summary>
    private async Task<SexualOrientation> SelectOrientation()
    {
        while (true)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("creation.orientation"), "cyan");
            terminal.WriteLine(Loc.Get("creation.orientation_1"), "white");
            terminal.WriteLine(Loc.Get("creation.orientation_2"), "white");
            terminal.WriteLine(Loc.Get("creation.orientation_3"), "white");
            terminal.WriteLine(Loc.Get("creation.orientation_4"), "white");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("creation.orientation_hint"));

            var choice = await terminal.GetInputAsync(Loc.Get("ui.your_choice"));

            switch (choice.Trim())
            {
                case "1":
                    return SexualOrientation.Straight;
                case "2":
                    return SexualOrientation.Gay; // Gay or Lesbian based on sex, but stored as Gay for simplicity
                case "3":
                    return SexualOrientation.Bisexual;
                case "4":
                    return SexualOrientation.Asexual;
                default:
                    terminal.WriteLine(Loc.Get("ui.invalid_choice"), "red");
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
        UIHelper.WriteBoxHeader(terminal, Loc.Get("creation.difficulty.header"), "bright_cyan", 64);
        terminal.WriteLine("");

        while (true)
        {
            // Display difficulty options with descriptions
            terminal.WriteLine($"(E){Loc.Get("character_creation.easy_label")}      - " + DifficultySystem.GetDescription(DifficultyMode.Easy), DifficultySystem.GetColor(DifficultyMode.Easy));
            terminal.WriteLine("");
            terminal.WriteLine($"(N){Loc.Get("character_creation.normal_label")}    - " + DifficultySystem.GetDescription(DifficultyMode.Normal), DifficultySystem.GetColor(DifficultyMode.Normal));
            terminal.WriteLine("");
            terminal.WriteLine($"(H){Loc.Get("character_creation.hard_label")}      - " + DifficultySystem.GetDescription(DifficultyMode.Hard), DifficultySystem.GetColor(DifficultyMode.Hard));
            terminal.WriteLine("");
            terminal.WriteLine($"(!){Loc.Get("character_creation.nightmare_label")}- " + DifficultySystem.GetDescription(DifficultyMode.Nightmare), DifficultySystem.GetColor(DifficultyMode.Nightmare));
            terminal.WriteLine("");

            var choice = await terminal.GetInputAsync(Loc.Get("creation.difficulty.prompt"));

            switch (choice.ToUpper())
            {
                case "E":
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("creation.difficulty.easy_selected"), DifficultySystem.GetColor(DifficultyMode.Easy));
                    await Task.Delay(1000);
                    return DifficultyMode.Easy;

                case "N":
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("creation.difficulty.normal_selected"), DifficultySystem.GetColor(DifficultyMode.Normal));
                    await Task.Delay(1000);
                    return DifficultyMode.Normal;

                case "H":
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("creation.difficulty.hard_selected"), DifficultySystem.GetColor(DifficultyMode.Hard));
                    await Task.Delay(1000);
                    return DifficultyMode.Hard;

                case "!":
                    terminal.WriteLine("");
                    if (!GameConfig.ScreenReaderMode)
                        terminal.WriteLine("═══════════════════════════════════════════", "bright_red");
                    terminal.WriteLine($"    {Loc.Get("creation.difficulty.nightmare_header")}", DifficultySystem.GetColor(DifficultyMode.Nightmare));
                    if (!GameConfig.ScreenReaderMode)
                        terminal.WriteLine("═══════════════════════════════════════════", "bright_red");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("creation.difficulty.nightmare_desc1"), "red");
                    terminal.WriteLine(Loc.Get("creation.difficulty.nightmare_desc2"), "red");
                    terminal.WriteLine(Loc.Get("creation.difficulty.nightmare_desc3"), "red");
                    terminal.WriteLine("");
                    var confirm = await terminal.GetInputAsync(Loc.Get("creation.difficulty.nightmare_confirm"));
                    if (confirm.ToUpper() == "Y")
                    {
                        terminal.WriteLine(Loc.Get("creation.difficulty.nightmare_sealed"), "bright_red");
                        await Task.Delay(1500);
                        return DifficultyMode.Nightmare;
                    }
                    terminal.WriteLine(Loc.Get("creation.difficulty.nightmare_wise"), "yellow");
                    terminal.WriteLine("");
                    break;

                default:
                    terminal.WriteLine(Loc.Get("creation.difficulty.invalid"), "red");
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
                terminal.WriteLine(Loc.Get("creation.choose_race"), "cyan");
                terminal.WriteLine("");

                // Show race menu with available classes
                DisplayRaceOption(0, Loc.Get("race.human"), CharacterRace.Human);
                DisplayRaceOption(1, Loc.Get("race.hobbit"), CharacterRace.Hobbit);
                DisplayRaceOption(2, Loc.Get("race.elf"), CharacterRace.Elf);
                DisplayRaceOption(3, Loc.Get("race.half_elf"), CharacterRace.HalfElf);
                DisplayRaceOption(4, Loc.Get("race.dwarf"), CharacterRace.Dwarf);
                DisplayRaceOption(5, Loc.Get("race.troll"), CharacterRace.Troll, $"*{Loc.Get("creation.preview.regen").ToLower()}");
                DisplayRaceOption(6, Loc.Get("race.orc"), CharacterRace.Orc);
                DisplayRaceOption(7, Loc.Get("race.gnome"), CharacterRace.Gnome);
                DisplayRaceOption(8, Loc.Get("race.gnoll"), CharacterRace.Gnoll, $"*{Loc.Get("creation.preview.poison_bite").ToLower()}");
                DisplayRaceOption(9, Loc.Get("race.mutant"), CharacterRace.Mutant);
                terminal.WriteLine("");
                terminal.WriteLine($"(H) {Loc.Get("creation.help")}", "green");
                terminal.WriteLine($"(A) {Loc.Get("creation.abort")}", "red");
                terminal.WriteLine("");
            }

            choice = await terminal.GetInputAsync(Loc.Get("ui.your_choice"));

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
                if (await ConfirmChoice(Loc.Get("charcreate.abort"), false))
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
                terminal.WriteLine(Loc.Get("creation.invalid_race"), "red");
            }
        }
    }

    /// <summary>
    /// Display a race option with available classes
    /// </summary>
    private void DisplayRaceOption(int number, string raceName, CharacterRace race, string suffix = "")
    {
        // Get all base classes (including race-locked classes)
        var allClasses = new[] {
            CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Ranger,
            CharacterClass.Assassin, CharacterClass.Bard, CharacterClass.Jester,
            CharacterClass.Alchemist, CharacterClass.Magician, CharacterClass.Cleric,
            CharacterClass.Sage, CharacterClass.Barbarian, CharacterClass.MysticShaman
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
            terminal.WriteLine($" [{Loc.Get("creation.all_classes")}]", "darkgray");
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
            { CharacterClass.Barbarian, "Bar" },
            { CharacterClass.MysticShaman, "Sha" }
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
        string prompt = $" {Loc.Get("creation.preview.be_race", raceDesc)}";
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
        AddStatBar(Loc.Get("status.hp"), raceAttrib.HPBonus, 17);
        AddStatBar(Loc.Get("status.str"), raceAttrib.StrengthBonus, 5);
        AddStatBar(Loc.Get("status.def"), raceAttrib.DefenceBonus, 5);
        AddStatBar(Loc.Get("status.sta"), raceAttrib.StaminaBonus, 5);

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
            CharacterClass.Sage, CharacterClass.Barbarian, CharacterClass.MysticShaman
        };
        var restricted = GameConfig.InvalidCombinations.ContainsKey(race)
            ? GameConfig.InvalidCombinations[race]
            : Array.Empty<CharacterClass>();
        var available = allClasses.Where(c => !restricted.Contains(c)).ToList();

        if (available.Count == allClasses.Length)
        {
            AddText($"{Loc.Get("creation.preview.classes")} {Loc.Get("creation.preview.classes_all")}", "cyan");
        }
        else
        {
            AddText(Loc.Get("creation.preview.classes"), "cyan");
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
            CharacterRace.Troll => Loc.Get("creation.preview.regen"),
            CharacterRace.Gnoll => Loc.Get("creation.preview.poison_bite"),
            _ => Loc.Get("creation.preview.none")
        };
        AddText($"{Loc.Get("creation.preview.special")} {special}", "cyan");
        if (race == CharacterRace.Troll) AddText(Loc.Get("creation.preview.regen_desc"), "gray");
        else if (race == CharacterRace.Gnoll) AddText(Loc.Get("creation.preview.poison_desc"), "gray");

        // ── Armor restriction for small races ──
        if (GameConfig.IsSmallRace(race))
            AddText(Loc.Get("creation.preview.small_race"), "red");

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
        CardStatBar(pad, W, Loc.Get("status.hp"),  raceAttrib.HPBonus, 17);
        CardStatBar(pad, W, Loc.Get("status.str"), raceAttrib.StrengthBonus, 5);
        CardStatBar(pad, W, Loc.Get("status.def"), raceAttrib.DefenceBonus, 5);
        CardStatBar(pad, W, Loc.Get("status.sta"), raceAttrib.StaminaBonus, 5);

        // ── Separator ──
        CardSeparator(pad, W);
        CardBlank(pad, W);

        // ── Available classes ──
        var allClasses = new[] {
            CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Ranger,
            CharacterClass.Assassin, CharacterClass.Bard, CharacterClass.Jester,
            CharacterClass.Alchemist, CharacterClass.Magician, CharacterClass.Cleric,
            CharacterClass.Sage, CharacterClass.Barbarian, CharacterClass.MysticShaman
        };
        var restricted = GameConfig.InvalidCombinations.ContainsKey(race)
            ? GameConfig.InvalidCombinations[race]
            : Array.Empty<CharacterClass>();
        var available = allClasses.Where(c => !restricted.Contains(c)).ToList();

        if (available.Count == allClasses.Length)
        {
            CardLine(pad, W, $"  [cyan]{Loc.Get("creation.preview.classes")}  [white]{Loc.Get("creation.preview.classes_all")}");
        }
        else
        {
            string GetClassDisplayName(CharacterClass c) => (int)c < GameConfig.ClassNames.Length ? GameConfig.ClassNames[(int)c] : c.ToString();
            var classNamesList = available.Select(GetClassDisplayName);
            string classList = string.Join(", ", classNamesList);
            if (($"  {Loc.Get("creation.preview.classes")}  " + classList).Length <= W - 4)
            {
                CardLine(pad, W, $"  [cyan]{Loc.Get("creation.preview.classes")}  [white]{classList}");
            }
            else
            {
                CardLine(pad, W, $"  [cyan]{Loc.Get("creation.preview.classes")}");
                var row1 = string.Join(", ", available.Take(available.Count / 2 + 1).Select(GetClassDisplayName));
                var row2 = string.Join(", ", available.Skip(available.Count / 2 + 1).Select(GetClassDisplayName));
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
            CharacterRace.Troll => $"[yellow]{Loc.Get("creation.preview.regen")} [gray]- {Loc.Get("creation.preview.regen_desc")}",
            CharacterRace.Gnoll => $"[yellow]{Loc.Get("creation.preview.poison_bite")} [gray]- {Loc.Get("creation.preview.poison_desc")}",
            _ => $"[gray]{Loc.Get("creation.preview.none")}"
        };
        CardLine(pad, W, $"  [cyan]{Loc.Get("creation.preview.special")}  {special}");

        // ── Armor restriction for small races ──
        if (GameConfig.IsSmallRace(race))
        {
            CardLine(pad, W, $"  [yellow]Size:     [red]{Loc.Get("creation.preview.small_race")}");
        }

        CardBlank(pad, W);

        // ── Bottom border ──
        CardBottomBorder(pad, W);

        // ── Confirm prompt ──
        terminal.WriteLine("");
        var raceDesc = GameConfig.RaceDescriptions[race];
        var response = await terminal.GetInputAsync($"{pad} {Loc.Get("creation.preview.be_race_yn", raceDesc)}");

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
        CharacterRace.Human => Loc.Get("character_creation.race_desc.human"),
        CharacterRace.Hobbit => Loc.Get("character_creation.race_desc.hobbit"),
        CharacterRace.Elf => Loc.Get("character_creation.race_desc.elf"),
        CharacterRace.HalfElf => Loc.Get("character_creation.race_desc.half_elf"),
        CharacterRace.Dwarf => Loc.Get("character_creation.race_desc.dwarf"),
        CharacterRace.Troll => Loc.Get("character_creation.race_desc.troll"),
        CharacterRace.Orc => Loc.Get("character_creation.race_desc.orc"),
        CharacterRace.Gnome => Loc.Get("character_creation.race_desc.gnome"),
        CharacterRace.Gnoll => Loc.Get("character_creation.race_desc.gnoll"),
        CharacterRace.Mutant => Loc.Get("character_creation.race_desc.mutant"),
        _ => Loc.Get("character_creation.race_desc.unknown")
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

        string className = (int)characterClass < GameConfig.ClassNames.Length
            ? GameConfig.ClassNames[(int)characterClass]
            : characterClass.ToString();

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
        string prompt = $" {Loc.Get("creation.preview.be_class", article, className)}";
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
            CharacterClass.Warrior or CharacterClass.Barbarian or CharacterClass.Paladin => Loc.Get("creation.preview.category.melee"),
            CharacterClass.Ranger or CharacterClass.Assassin or CharacterClass.Bard or CharacterClass.Jester or CharacterClass.MysticShaman => Loc.Get("creation.preview.category.hybrid"),
            CharacterClass.Magician or CharacterClass.Sage or CharacterClass.Cleric or CharacterClass.Alchemist => Loc.Get("creation.preview.category.magic"),
            _ => Loc.Get("creation.preview.category.adventurer")
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
            manaText = $"{Loc.Get("creation.preview.mana")} {attrs.Mana}";
            manaColor = "bright_green";
        }
        else if (GetClassManaPerLevel(characterClass) > 0)
        {
            manaText = $"{Loc.Get("creation.preview.mana")} {Loc.Get("creation.preview.mana_per_level", GetClassManaPerLevel(characterClass).ToString())}";
            manaColor = "cyan";
        }
        else
        {
            manaText = $"{Loc.Get("creation.preview.mana")} {Loc.Get("creation.preview.mana_none")}";
            manaColor = "gray";
        }
        AddText(manaText, manaColor);

        // ── Armor ──
        string armorInfo = GameConfig.GetMaxArmorWeight(characterClass) switch
        {
            ArmorWeightClass.Light => $"{Loc.Get("creation.preview.armor")} {Loc.Get("creation.preview.armor_light")}",
            ArmorWeightClass.Medium => $"{Loc.Get("creation.preview.armor")} {Loc.Get("creation.preview.armor_medium")}",
            _ => $"{Loc.Get("creation.preview.armor")} {Loc.Get("creation.preview.armor_all")}"
        };
        string armorColor = GameConfig.GetMaxArmorWeight(characterClass) switch
        {
            ArmorWeightClass.Light => "cyan",
            ArmorWeightClass.Medium => "yellow",
            _ => "bright_green"
        };
        AddText(armorInfo, armorColor);

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
        string className = (int)characterClass < GameConfig.ClassNames.Length
            ? GameConfig.ClassNames[(int)characterClass]
            : characterClass.ToString();

        // Determine class category
        string category = characterClass switch
        {
            CharacterClass.Warrior or CharacterClass.Barbarian or CharacterClass.Paladin => Loc.Get("creation.preview.category.melee"),
            CharacterClass.Ranger or CharacterClass.Assassin or CharacterClass.Bard or CharacterClass.Jester or CharacterClass.MysticShaman => Loc.Get("creation.preview.category.hybrid"),
            CharacterClass.Magician or CharacterClass.Sage or CharacterClass.Cleric or CharacterClass.Alchemist => Loc.Get("creation.preview.category.magic"),
            _ => Loc.Get("creation.preview.category.adventurer")
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
            manaCardText = $"[cyan]{Loc.Get("creation.preview.mana_per_level", GetClassManaPerLevel(characterClass).ToString())}";
        else
            manaCardText = $"[gray]{Loc.Get("creation.preview.mana_none")}";
        CardLine(pad, W, $"  [cyan]{Loc.Get("creation.preview.mana")} {manaCardText}");
        string strengths = GetClassStrengths(characterClass);
        CardLine(pad, W, $"  [cyan]{Loc.Get("creation.preview.strengths")}  [white]{strengths}");

        CardBlank(pad, W);

        // ── Bottom border ──
        CardBottomBorder(pad, W);

        // ── Confirm prompt ──
        terminal.WriteLine("");
        var article = "aeiouAEIOU".Contains(className[0]) ? "an" : "a";
        var response = await terminal.GetInputAsync($"{pad} {Loc.Get("creation.preview.be_class_yn", article, className)}");

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
        terminal.WriteLine($"{Loc.Get("status.race")}: {raceName}");
        terminal.WriteLine($"\"{desc}\"");
        terminal.WriteLine("");
        terminal.WriteLine($"{Loc.Get("creation.preview.stats")} {Loc.Get("status.hp")} +{raceAttrib.HPBonus}, {Loc.Get("status.str")} +{raceAttrib.StrengthBonus}, {Loc.Get("status.def")} +{raceAttrib.DefenceBonus}, {Loc.Get("status.sta")} +{raceAttrib.StaminaBonus}");
        terminal.WriteLine("");

        // Available classes
        var allClasses = new[] {
            CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Ranger,
            CharacterClass.Assassin, CharacterClass.Bard, CharacterClass.Jester,
            CharacterClass.Alchemist, CharacterClass.Magician, CharacterClass.Cleric,
            CharacterClass.Sage, CharacterClass.Barbarian, CharacterClass.MysticShaman
        };
        var restricted = GameConfig.InvalidCombinations.ContainsKey(race)
            ? GameConfig.InvalidCombinations[race]
            : Array.Empty<CharacterClass>();
        var available = allClasses.Where(c => !restricted.Contains(c)).ToList();

        if (available.Count == allClasses.Length)
        {
            terminal.WriteLine($"{Loc.Get("creation.preview.classes")} {Loc.Get("creation.preview.classes_all")}");
        }
        else
        {
            terminal.WriteLine($"{Loc.Get("creation.preview.classes")} {string.Join(", ", available.Select(c => (int)c < GameConfig.ClassNames.Length ? GameConfig.ClassNames[(int)c] : c.ToString()))}");
        }

        if (restricted.Length > 0 && GameConfig.RaceRestrictionReasons.ContainsKey(race))
        {
            terminal.WriteLine($"{Loc.Get("creation.preview.restricted")} {GameConfig.RaceRestrictionReasons[race]}");
        }

        terminal.WriteLine("");

        // Special trait
        string special = race switch
        {
            CharacterRace.Troll => $"{Loc.Get("creation.preview.regen")} - {Loc.Get("creation.preview.regen_desc")}",
            CharacterRace.Gnoll => $"{Loc.Get("creation.preview.poison_bite")} - {Loc.Get("creation.preview.poison_desc")}",
            _ => Loc.Get("creation.preview.none")
        };
        terminal.WriteLine($"{Loc.Get("creation.preview.special")} {special}");

        terminal.WriteLine("");
        var raceDesc = GameConfig.RaceDescriptions[race];
        var response = await terminal.GetInputAsync(Loc.Get("creation.preview.be_race_yn", raceDesc));

        return !string.IsNullOrEmpty(response) &&
               (response.ToUpper() == "Y" || response.ToUpper() == "YES");
    }

    /// <summary>Screen reader class preview: plain text, no boxes or stat bars.</summary>
    private async Task<bool> ShowClassPreviewScreenReader(CharacterClass characterClass)
    {
        terminal.Clear();

        var attrs = GameConfig.ClassStartingAttributes[characterClass];
        string className = (int)characterClass < GameConfig.ClassNames.Length
            ? GameConfig.ClassNames[(int)characterClass]
            : characterClass.ToString();

        string category = characterClass switch
        {
            CharacterClass.Warrior or CharacterClass.Barbarian or CharacterClass.Paladin => Loc.Get("creation.preview.category.melee"),
            CharacterClass.Ranger or CharacterClass.Assassin or CharacterClass.Bard or CharacterClass.Jester or CharacterClass.MysticShaman => Loc.Get("creation.preview.category.hybrid"),
            CharacterClass.Magician or CharacterClass.Sage or CharacterClass.Cleric or CharacterClass.Alchemist => Loc.Get("creation.preview.category.magic"),
            _ => Loc.Get("creation.preview.category.adventurer")
        };

        string desc = GetClassDescription(characterClass);

        terminal.WriteLine("");
        terminal.WriteLine($"{Loc.Get("status.class")}: {className} ({category})");
        terminal.WriteLine($"\"{desc}\"");
        terminal.WriteLine("");
        terminal.WriteLine($"{Loc.Get("creation.preview.stats")} {Loc.Get("status.hp")} +{attrs.HP}, {Loc.Get("status.str")} +{attrs.Strength}, {Loc.Get("status.def")} +{attrs.Defence}, {Loc.Get("status.sta")} +{attrs.Stamina}, {Loc.Get("status.agi")} +{attrs.Agility}, {Loc.Get("status.cha")} +{attrs.Charisma}, {Loc.Get("status.dex")} +{attrs.Dexterity}, {Loc.Get("status.wis")} +{attrs.Wisdom}, {Loc.Get("status.int")} +{attrs.Intelligence}, {Loc.Get("status.con")} +{attrs.Constitution}");
        terminal.WriteLine("");

        string manaText;
        if (attrs.Mana > 0)
            manaText = attrs.Mana.ToString();
        else if (GetClassManaPerLevel(characterClass) > 0)
            manaText = Loc.Get("creation.preview.mana_grows", GetClassManaPerLevel(characterClass).ToString());
        else
            manaText = Loc.Get("creation.preview.mana_none_physical");
        terminal.WriteLine($"{Loc.Get("creation.preview.mana")} {manaText}");
        string strengths = GetClassStrengths(characterClass);
        terminal.WriteLine($"{Loc.Get("creation.preview.strengths")} {strengths}");

        terminal.WriteLine("");
        var article = "aeiouAEIOU".Contains(className[0]) ? "an" : "a";
        var response = await terminal.GetInputAsync(Loc.Get("creation.preview.be_class_yn", article, className));

        return !string.IsNullOrEmpty(response) &&
               (response.ToUpper() == "Y" || response.ToUpper() == "YES");
    }

    private static string GetClassDescription(CharacterClass cls) => cls switch
    {
        CharacterClass.Warrior => Loc.Get("character_creation.class_desc.warrior"),
        CharacterClass.Barbarian => Loc.Get("character_creation.class_desc.barbarian"),
        CharacterClass.Paladin => Loc.Get("character_creation.class_desc.paladin"),
        CharacterClass.Ranger => Loc.Get("character_creation.class_desc.ranger"),
        CharacterClass.Assassin => Loc.Get("character_creation.class_desc.assassin"),
        CharacterClass.Bard => Loc.Get("character_creation.class_desc.bard"),
        CharacterClass.Jester => Loc.Get("character_creation.class_desc.jester"),
        CharacterClass.Magician => Loc.Get("character_creation.class_desc.magician"),
        CharacterClass.Sage => Loc.Get("character_creation.class_desc.sage"),
        CharacterClass.Cleric => Loc.Get("character_creation.class_desc.cleric"),
        CharacterClass.Alchemist => Loc.Get("character_creation.class_desc.alchemist"),
        CharacterClass.MysticShaman => Loc.Get("character_creation.class_desc.mystic_shaman"),
        _ => Loc.Get("character_creation.class_desc.unknown")
    };

    private static string GetClassStrengths(CharacterClass cls) => cls switch
    {
        CharacterClass.Warrior => Loc.Get("character_creation.class_str.warrior"),
        CharacterClass.Barbarian => Loc.Get("character_creation.class_str.barbarian"),
        CharacterClass.Paladin => Loc.Get("character_creation.class_str.paladin"),
        CharacterClass.Ranger => Loc.Get("character_creation.class_str.ranger"),
        CharacterClass.Assassin => Loc.Get("character_creation.class_str.assassin"),
        CharacterClass.Bard => Loc.Get("character_creation.class_str.bard"),
        CharacterClass.Jester => Loc.Get("character_creation.class_str.jester"),
        CharacterClass.Magician => Loc.Get("character_creation.class_str.magician"),
        CharacterClass.Sage => Loc.Get("character_creation.class_str.sage"),
        CharacterClass.Cleric => Loc.Get("character_creation.class_str.cleric"),
        CharacterClass.Alchemist => Loc.Get("character_creation.class_str.alchemist"),
        CharacterClass.MysticShaman => Loc.Get("character_creation.class_str.mystic_shaman"),
        _ => Loc.Get("character_creation.class_str.unknown")
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
        CharacterClass.Alchemist => 0,
        CharacterClass.Paladin => 0,
        CharacterClass.Bard => 0,
        CharacterClass.MysticShaman => 10,
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
            { 10, CharacterClass.Barbarian },
            { 11, CharacterClass.MysticShaman }
        };

        // Check for unlocked NG+ prestige classes
        var unlockedPrestige = GetUnlockedPrestigeClasses();
        int prestigeStartIndex = 12;
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
                terminal.WriteLine(Loc.Get("creation.choose_class", GameConfig.RaceNames[(int)race]), "cyan");
                terminal.WriteLine("");

                // Show class menu with restrictions marked
                DisplayClassOption(0, Loc.Get("class.warrior"), CharacterClass.Warrior, restrictedClasses);
                DisplayClassOption(1, Loc.Get("class.paladin"), CharacterClass.Paladin, restrictedClasses);
                DisplayClassOption(2, Loc.Get("class.ranger"), CharacterClass.Ranger, restrictedClasses);
                DisplayClassOption(3, Loc.Get("class.assassin"), CharacterClass.Assassin, restrictedClasses);
                DisplayClassOption(4, Loc.Get("class.bard"), CharacterClass.Bard, restrictedClasses);
                DisplayClassOption(5, Loc.Get("class.jester"), CharacterClass.Jester, restrictedClasses);
                DisplayClassOption(6, Loc.Get("class.alchemist"), CharacterClass.Alchemist, restrictedClasses);
                DisplayClassOption(7, Loc.Get("class.magician"), CharacterClass.Magician, restrictedClasses);
                DisplayClassOption(8, Loc.Get("class.cleric"), CharacterClass.Cleric, restrictedClasses);
                DisplayClassOption(9, Loc.Get("class.sage"), CharacterClass.Sage, restrictedClasses);
                DisplayClassOption(10, Loc.Get("class.barbarian"), CharacterClass.Barbarian, restrictedClasses);
                DisplayClassOption(11, Loc.Get("class.mystic_shaman"), CharacterClass.MysticShaman, restrictedClasses);

                // Always show prestige classes — unlocked ones selectable, locked ones grayed out
                terminal.WriteLine("");
                if (!GameConfig.ScreenReaderMode)
                    terminal.WriteLine($"  ═══ {Loc.Get("creation.prestige_header")} ═══", "bright_magenta");
                else
                    terminal.WriteLine($"  {Loc.Get("creation.prestige_header")}", "bright_magenta");
                var allPrestige = new[]
                {
                    (CharacterClass.Tidesworn, Loc.Get("charcreate.prestige_req_savior")),
                    (CharacterClass.Wavecaller, Loc.Get("charcreate.prestige_req_savior")),
                    (CharacterClass.Cyclebreaker, Loc.Get("charcreate.prestige_req_defiant")),
                    (CharacterClass.Abysswarden, Loc.Get("charcreate.prestige_req_usurper")),
                    (CharacterClass.Voidreaver, Loc.Get("charcreate.prestige_req_usurper"))
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
                        terminal.WriteLine($" {Loc.Get("creation.prestige_locked", unlockReq)}", "dark_gray");
                    }
                }

                terminal.WriteLine($"(H) {Loc.Get("creation.help")}", "green");
                terminal.WriteLine($"(A) {Loc.Get("creation.abort")}", "red");
                terminal.WriteLine("");

                // Show restriction reason if this race has restrictions
                if (restrictedClasses.Length > 0 && GameConfig.RaceRestrictionReasons.ContainsKey(race))
                {
                    terminal.WriteLine($"{Loc.Get("character_creation.note")}: {GameConfig.RaceRestrictionReasons[race]}", "yellow");
                    terminal.WriteLine("");
                }
            }

            choice = await terminal.GetInputAsync(Loc.Get("ui.your_choice"));

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
                if (await ConfirmChoice(Loc.Get("charcreate.abort"), false))
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
                    terminal.WriteLine(Loc.Get("creation.race_restricted", GameConfig.RaceNames[(int)race], article1, characterClass.ToString()), "red");
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
                terminal.WriteLine(Loc.Get("creation.invalid_class", maxChoice.ToString()), "red");
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
            terminal.WriteLine($"{numberStr} {className,-12} [{Loc.Get("character_creation.unavailable")}]", "darkgray");
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
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine($"═══ {Loc.Get("character_creation.stat_roll")} ═══", "bright_cyan");
            else
                terminal.WriteLine(Loc.Get("character_creation.stat_roll"), "bright_cyan");
            terminal.WriteLine("");
            terminal.WriteLine($"{Loc.Get("status.class")}: {character.ClassName}", "yellow");
            terminal.WriteLine($"{Loc.Get("status.race")}: {GameConfig.RaceNames[(int)character.Race]}", "yellow");
            terminal.WriteLine("");

            // Calculate total stat points for comparison
            long totalStats = character.Strength + character.Defence + character.Stamina +
                              character.Agility + character.Charisma + character.Dexterity +
                              character.Wisdom + character.Intelligence + character.Constitution;

            terminal.WriteLine(Loc.Get("character_creation.rolled_attributes"), "cyan");
            terminal.WriteLine("");
            terminal.Write($"  {Loc.Get("ui.stat_hp"),-15} ");
            terminal.Write($"{character.HP,3}", GetStatColor(character.HP, 15, 25));
            terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_hp")}", "gray");

            terminal.Write($"  {Loc.Get("ui.stat_strength"),-15} ");
            terminal.Write($"{character.Strength,3}", GetStatColor(character.Strength, 6, 12));
            terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_str")}", "gray");

            terminal.Write($"  {Loc.Get("ui.stat_defense"),-15} ");
            terminal.Write($"{character.Defence,3}", GetStatColor(character.Defence, 5, 10));
            terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_def")}", "gray");

            terminal.Write($"  {Loc.Get("ui.stat_stamina"),-15} ");
            terminal.Write($"{character.Stamina,3}", GetStatColor(character.Stamina, 5, 10));
            terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_sta")}", "gray");

            terminal.Write($"  {Loc.Get("ui.stat_agility"),-15} ");
            terminal.Write($"{character.Agility,3}", GetStatColor(character.Agility, 5, 10));
            terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_agi")}", "gray");

            terminal.Write($"  {Loc.Get("ui.stat_dexterity"),-15} ");
            terminal.Write($"{character.Dexterity,3}", GetStatColor(character.Dexterity, 5, 10));
            terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_dex")}", "gray");

            terminal.Write($"  {Loc.Get("ui.stat_constitution"),-15} ");
            terminal.Write($"{character.Constitution,3}", GetStatColor(character.Constitution, 5, 10));
            terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_con")}", "gray");

            terminal.Write($"  {Loc.Get("ui.stat_intelligence"),-15} ");
            terminal.Write($"{character.Intelligence,3}", GetStatColor(character.Intelligence, 5, 10));
            terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_int")}", "gray");

            terminal.Write($"  {Loc.Get("ui.stat_wisdom"),-15} ");
            terminal.Write($"{character.Wisdom,3}", GetStatColor(character.Wisdom, 5, 10));
            terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_wis")}", "gray");

            terminal.Write($"  {Loc.Get("ui.stat_charisma"),-15} ");
            terminal.Write($"{character.Charisma,3}", GetStatColor(character.Charisma, 5, 10));
            terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_cha")}", "gray");

            // Show effective mana including INT/WIS bonuses (matches what RecalculateStats will give)
            long effectiveMana = character.MaxMana;
            if (effectiveMana > 0)
            {
                effectiveMana += StatEffectsSystem.GetIntelligenceManaBonus(character.Intelligence, character.Level);
                effectiveMana += StatEffectsSystem.GetWisdomManaBonus(character.Wisdom);
            }
            if (effectiveMana > 0)
            {
                terminal.Write($"  {Loc.Get("ui.stat_mana"),-15} ");
                terminal.Write($"{effectiveMana,3}/{effectiveMana}", "cyan");
                terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_mana")}", "gray");
            }
            else if (GetClassManaPerLevel(character.Class) > 0)
            {
                terminal.Write($"  {Loc.Get("ui.stat_mana"),-15} ");
                terminal.Write(Loc.Get("charcreate.mana_per_level_label", GetClassManaPerLevel(character.Class).ToString()), "cyan");
                terminal.WriteLine($"  - {Loc.Get("character_creation.stat_desc_mana_grows")}", "gray");
            }
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("character_creation.total_stats")}: {totalStats}", totalStats >= 70 ? "bright_green" : totalStats >= 55 ? "yellow" : "red");
            terminal.WriteLine("");

            if (rerollsRemaining > 0)
            {
                terminal.WriteLine(Loc.Get("character_creation.rerolls_remaining", rerollsRemaining.ToString()), "yellow");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("character_creation.accept_stats"), "green");
                terminal.WriteLine(Loc.Get("character_creation.reroll_stats"), "cyan");
                terminal.WriteLine("");

                var choice = await terminal.GetInputAsync(Loc.Get("ui.your_choice"));

                if (choice.ToUpper() == "A")
                {
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("character_creation.stats_accepted"), "bright_green");
                    await Task.Delay(1000);
                    break;
                }
                else if (choice.ToUpper() == "R")
                {
                    rerollsRemaining--;
                    if (rerollsRemaining == 0)
                    {
                        terminal.WriteLine("");
                        terminal.WriteLine(Loc.Get("character_creation.final_roll"), "bright_red");
                        await Task.Delay(1500);
                    }
                    else
                    {
                        terminal.WriteLine("");
                        terminal.WriteLine(Loc.Get("character_creation.rerolling"), "cyan");
                        await Task.Delay(800);
                    }
                    continue;
                }
                else
                {
                    terminal.WriteLine(Loc.Get("character_creation.choose_accept_reroll"), "red");
                    await Task.Delay(1000);
                    continue;
                }
            }
            else
            {
                // No re-rolls remaining - must accept
                terminal.WriteLine(Loc.Get("character_creation.no_rerolls"), "bright_red");
                terminal.WriteLine("");
                await terminal.GetInputAsync(Loc.Get("character_creation.press_enter_accept"));
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

        // Mana for spellcasters only - base from class + Intelligence bonus
        if (classAttrib.MaxMana > 0)
        {
            int intelligenceBonus = (int)(character.Intelligence / 4); // Intelligence adds to mana
            character.Mana = classAttrib.Mana + intelligenceBonus;
            character.MaxMana = classAttrib.MaxMana + intelligenceBonus;
        }
        else
        {
            character.Mana = 0;
            character.MaxMana = 0;
        }
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
        
        // Give class-appropriate starting weapon
        GiveStartingWeapon(character);

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
    /// Give new characters a class-appropriate starting weapon so they can use their abilities from level 1.
    /// Magicians/Sages need a Staff for spells, Assassins need a Dagger for Backstab, etc.
    /// </summary>
    private void GiveStartingWeapon(Character character)
    {
        var (name, weaponType, handedness, power) = character.Class switch
        {
            CharacterClass.Magician => ("Wooden Staff", WeaponType.Staff, WeaponHandedness.TwoHanded, 3),
            CharacterClass.Sage => ("Wooden Staff", WeaponType.Staff, WeaponHandedness.TwoHanded, 3),
            CharacterClass.Assassin => ("Rusty Dagger", WeaponType.Dagger, WeaponHandedness.OneHanded, 4),
            CharacterClass.Ranger => ("Short Bow", WeaponType.Bow, WeaponHandedness.TwoHanded, 5),
            CharacterClass.Warrior => ("Dull Sword", WeaponType.Sword, WeaponHandedness.OneHanded, 5),
            CharacterClass.Paladin => ("Dull Sword", WeaponType.Sword, WeaponHandedness.OneHanded, 5),
            CharacterClass.Barbarian => ("Crude Axe", WeaponType.Axe, WeaponHandedness.OneHanded, 5),
            CharacterClass.Cleric => ("Wooden Staff", WeaponType.Staff, WeaponHandedness.TwoHanded, 3),
            CharacterClass.Bard => ("Old Lute", WeaponType.Instrument, WeaponHandedness.OneHanded, 4),
            CharacterClass.Jester => ("Rusty Dagger", WeaponType.Dagger, WeaponHandedness.OneHanded, 4),
            CharacterClass.Alchemist => ("Rusty Dagger", WeaponType.Dagger, WeaponHandedness.OneHanded, 4),
            CharacterClass.MysticShaman => ("Tribal Mace", WeaponType.Mace, WeaponHandedness.OneHanded, 5),
            _ => ("Dull Sword", WeaponType.Sword, WeaponHandedness.OneHanded, 5),
        };

        var weapon = new Equipment
        {
            Name = name,
            Description = $"A basic {name.ToLower()} for new adventurers.",
            Slot = EquipmentSlot.MainHand,
            WeaponType = weaponType,
            Handedness = handedness,
            WeaponPower = power,
            Value = 50,
            MinLevel = 1,
            Rarity = EquipmentRarity.Common,
        };

        EquipmentDatabase.RegisterDynamic(weapon);
        character.EquipItem(weapon, EquipmentSlot.MainHand, out _);
    }

    /// <summary>
    /// Show character summary before creation (Pascal display)
    /// </summary>
    private async Task ShowCharacterSummary(Character character)
    {
        terminal.Clear();
        terminal.WriteLine("");
        terminal.WriteLine($"--- {Loc.Get("character_creation.summary_header")} ---", "bright_green");
        terminal.WriteLine("");

        terminal.WriteLine($"{Loc.Get("ui.name_label")}: {character.Name2}", "cyan");
        terminal.WriteLine($"{Loc.Get("status.race")}: {GameConfig.RaceNames[(int)character.Race]}", "yellow");
        terminal.WriteLine($"{Loc.Get("status.class")}: {character.ClassName}", "yellow");
        terminal.WriteLine($"{Loc.Get("character_creation.sex")}: {(character.Sex == CharacterSex.Male ? Loc.Get("character_creation.male") : Loc.Get("character_creation.female"))}", "white");
        terminal.WriteLine($"{Loc.Get("character_creation.age")}: {character.Age}", "white");
        terminal.WriteLine("");

        terminal.WriteLine($"=== {Loc.Get("ui.attributes")} ===", "green");
        terminal.WriteLine($"{Loc.Get("ui.stat_hp")}: {character.HP}/{character.MaxHP}", "white");
        terminal.WriteLine($"{Loc.Get("ui.stat_strength")}: {character.Strength}", "white");
        terminal.WriteLine($"{Loc.Get("ui.stat_defense")}: {character.Defence}", "white");
        terminal.WriteLine($"{Loc.Get("ui.stat_stamina")}: {character.Stamina}", "white");
        terminal.WriteLine($"{Loc.Get("ui.stat_agility")}: {character.Agility}", "white");
        terminal.WriteLine($"{Loc.Get("ui.stat_dexterity")}: {character.Dexterity}", "white");
        terminal.WriteLine($"{Loc.Get("ui.stat_constitution")}: {character.Constitution}", "white");
        terminal.WriteLine($"{Loc.Get("ui.stat_intelligence")}: {character.Intelligence}", "white");
        terminal.WriteLine($"{Loc.Get("ui.stat_wisdom")}: {character.Wisdom}", "white");
        terminal.WriteLine($"{Loc.Get("ui.stat_charisma")}: {character.Charisma}", "white");
        if (character.MaxMana > 0)
        {
            terminal.WriteLine($"{Loc.Get("ui.stat_mana")}: {character.Mana}/{character.MaxMana}", "cyan");
        }
        terminal.WriteLine("");
        
        terminal.WriteLine($"=== {Loc.Get("character_creation.appearance")} ===", "green");
        terminal.WriteLine($"{Loc.Get("character_creation.height")}: {character.Height} cm", "white");
        terminal.WriteLine($"{Loc.Get("character_creation.weight")}: {character.Weight} kg", "white");
        terminal.WriteLine($"{Loc.Get("character_creation.eyes")}: {GameConfig.EyeColors[character.Eyes]}", "white");
        terminal.WriteLine($"{Loc.Get("character_creation.hair")}: {GameConfig.HairColors[character.Hair]}", "white");
        terminal.WriteLine($"{Loc.Get("character_creation.skin")}: {GameConfig.SkinColors[character.Skin]}", "white");
        terminal.WriteLine("");

        terminal.WriteLine($"=== {Loc.Get("character_creation.starting_resources")} ===", "green");
        terminal.WriteLine($"{Loc.Get("ui.gold")}: {character.Gold}", "yellow");
        terminal.WriteLine($"{Loc.Get("ui.experience")}: {character.Experience}", "white");
        terminal.WriteLine($"{Loc.Get("ui.level")}: {character.Level}", "white");
        terminal.WriteLine($"{Loc.Get("ui.healing_potions")}: {character.Healing}", "white");
        terminal.WriteLine("");
        
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Show race help text (Pascal RACEHELP display)
    /// </summary>
    private async Task ShowRaceHelp()
    {
        terminal.Clear();
        terminal.WriteLine("");
        terminal.WriteLine($"--- {Loc.Get("character_creation.race_info_header")} ---", "bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(GameConfig.RaceHelpText, "white");
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Show class help text (Pascal class help)
    /// </summary>
    private async Task ShowClassHelp()
    {
        terminal.Clear();
        terminal.WriteLine("");
        terminal.WriteLine($"--- {Loc.Get("character_creation.class_info_header")} ---", "bright_green");
        terminal.WriteLine("");
        terminal.WriteLine(GameConfig.ClassHelpText, "white");
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
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
