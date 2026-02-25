using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Secret Developer Menu - allows editing character stats, equipment, and game state for testing
/// Protected by passcode "CHEATER"
/// Access by typing "DEV" on Main Street
/// </summary>
public class DevMenuLocation : BaseLocation
{
    private const string PASSCODE = "CHEATER";
    private const string ONLINE_PASSCODE = "CHEATER-ONLINE";
    private bool _authenticated = false;

    public DevMenuLocation() : base(
        GameLocation.NoWhere, // Hidden location
        "Developer Menu",
        "A secret place where the fabric of reality can be bent to your will..."
    ) { }

    protected override void SetupLocation()
    {
        PossibleExits = new List<GameLocation> { GameLocation.MainStreet };
        LocationActions = new List<string>();
    }

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                       * DEVELOPER MENU *                                    ║");
        terminal.WriteLine("║                    For Testing Purposes Only                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        if (!_authenticated)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  This area is restricted. Enter passcode to continue.");
            terminal.WriteLine("");
            return;
        }

        ShowDevMenu();
    }

    private void ShowDevMenu()
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  ═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    DEVELOPER CHEAT MENU");
        terminal.WriteLine("  ═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  CHARACTER STATS:");
        terminal.SetColor("cyan");
        terminal.WriteLine("    [1] Edit Primary Stats (Str, Dex, Con, Int, Wis, Cha)");
        terminal.WriteLine("    [2] Edit Secondary Stats (Sta, Agi, Thievery)");
        terminal.WriteLine("    [3] Edit Combat Stats (HP, Mana, Defence, WeapPow, ArmPow)");
        terminal.WriteLine("    [4] Edit Resources (Gold, Bank, XP, Level, Fights)");
        terminal.WriteLine("    [5] Edit Alignment (Chivalry, Darkness)");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  CHARACTER IDENTITY:");
        terminal.SetColor("cyan");
        terminal.WriteLine("    [6] Edit Class & Race");
        terminal.WriteLine("    [7] Edit Appearance (Sex, Age, Height, Weight, Colors)");
        terminal.WriteLine("    [8] Edit Phrases & Battle Cry");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  STATUS & CONDITIONS:");
        terminal.SetColor("cyan");
        terminal.WriteLine("    [9] Toggle Diseases (Blind, Plague, Leprosy, etc.)");
        terminal.WriteLine("    [A] Toggle Status Effects");
        terminal.WriteLine("    [B] Edit Drug/Addiction Status");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  GAME PROGRESS:");
        terminal.SetColor("cyan");
        terminal.WriteLine("    [C] Edit Story Progress & Seals");
        terminal.WriteLine("    [D] Edit Artifacts & God Status");
        terminal.WriteLine("    [E] Edit Kill/Death Statistics");
        terminal.WriteLine("    [F] Edit Social Status (King, Team, Marriage)");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  INVENTORY & ITEMS:");
        terminal.SetColor("cyan");
        terminal.WriteLine("    [I] Spawn Items to Inventory");
        terminal.WriteLine("    [J] Clear Inventory");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  WORLD CONTROLS:");
        terminal.SetColor("cyan");
        terminal.WriteLine("    [N] NPC Controls (spawn, modify, teleport)");
        terminal.WriteLine("    [W] World Simulation Controls");
        terminal.WriteLine("    [T] Time Controls (advance days)");
        terminal.WriteLine("    [S] Narrative Systems Debug (Dreams, Factions, Stranger, NPCs)");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  QUICK CHEATS:");
        terminal.SetColor("yellow");
        terminal.WriteLine("    [G] GOD MODE (Max everything)");
        terminal.WriteLine("    [H] Full Heal + Cure All");
        terminal.WriteLine("    [M] Max Gold (10,000,000)");
        terminal.WriteLine("    [L] Level Up (to next level)");
        terminal.WriteLine("    [X] Max Level (100)");
        terminal.WriteLine("    [Z] Reset Character to Level 1");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  STEAM (when running via Steam):");
        terminal.SetColor("cyan");
        terminal.WriteLine("    [R] Reset Steam Stats & Achievements");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("    [Q] Return to Main Street");
        terminal.WriteLine("");

        if (currentPlayer.DevMenuUsed)
        {
            terminal.SetColor("dark_red");
            terminal.WriteLine("  [!] Steam achievements are PERMANENTLY DISABLED for this save.");
            terminal.WriteLine("");
        }

        ShowCurrentStats();
    }

    private void ShowCurrentStats()
    {
        terminal.SetColor("dark_cyan");
        terminal.WriteLine("  ═══════════════════════════════════════════════════════════════");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {currentPlayer.DisplayName} | Lvl {currentPlayer.Level} {currentPlayer.Race} {currentPlayer.Class} | HP {currentPlayer.HP}/{currentPlayer.MaxHP} | Mana {currentPlayer.Mana}/{currentPlayer.MaxMana}");
        terminal.WriteLine($"  Str:{currentPlayer.Strength} Dex:{currentPlayer.Dexterity} Con:{currentPlayer.Constitution} Int:{currentPlayer.Intelligence} Wis:{currentPlayer.Wisdom} Cha:{currentPlayer.Charisma}");
        terminal.WriteLine($"  Sta:{currentPlayer.Stamina} Agi:{currentPlayer.Agility} | WeapPow:{currentPlayer.WeapPow} ArmPow:{currentPlayer.ArmPow} Def:{currentPlayer.Defence}");
        terminal.WriteLine($"  Gold:{currentPlayer.Gold:N0} Bank:{currentPlayer.BankGold:N0} | XP:{currentPlayer.Experience:N0}");
        terminal.WriteLine($"  Chivalry:{currentPlayer.Chivalry} Darkness:{currentPlayer.Darkness} | Fights:{currentPlayer.Fights} PFights:{currentPlayer.PFights}");

        // Show active conditions
        var conditions = new List<string>();
        if (currentPlayer.Blind) conditions.Add("BLIND");
        if (currentPlayer.Plague) conditions.Add("PLAGUE");
        if (currentPlayer.Smallpox) conditions.Add("SMALLPOX");
        if (currentPlayer.Measles) conditions.Add("MEASLES");
        if (currentPlayer.Leprosy) conditions.Add("LEPROSY");
        if (currentPlayer.LoversBane) conditions.Add("STD");
        if (currentPlayer.Poison > 0) conditions.Add($"POISON({currentPlayer.Poison})");
        if (currentPlayer.OnDrugs) conditions.Add($"DRUG({currentPlayer.ActiveDrug})");
        if (currentPlayer.IsAddicted) conditions.Add($"ADDICT({currentPlayer.Addict})");
        if (currentPlayer.King) conditions.Add("KING");
        if (currentPlayer.Married) conditions.Add("MARRIED");

        if (conditions.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  Status: {string.Join(", ", conditions)}");
        }
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        if (!_authenticated)
        {
            return await HandleAuthentication(choice);
        }

        var upperChoice = choice.ToUpper().Trim();

        switch (upperChoice)
        {
            case "1": await EditPrimaryStats(); return false;
            case "2": await EditSecondaryStats(); return false;
            case "3": await EditCombatStats(); return false;
            case "4": await EditResources(); return false;
            case "5": await EditAlignment(); return false;
            case "6": await EditClassAndRace(); return false;
            case "7": await EditAppearance(); return false;
            case "8": await EditPhrases(); return false;
            case "9": await ToggleDiseases(); return false;
            case "A": await ToggleStatusEffects(); return false;
            case "B": await EditDrugStatus(); return false;
            case "C": await EditStoryProgress(); return false;
            case "D": await EditArtifactsAndGods(); return false;
            case "E": await EditKillStatistics(); return false;
            case "F": await EditSocialStatus(); return false;
            case "I": await SpawnItems(); return false;
            case "J": await ClearInventory(); return false;
            case "N": await NPCControls(); return false;
            case "W": await WorldControls(); return false;
            case "T": await TimeControls(); return false;
            case "S": await NarrativeDebug(); return false;
            case "G": await GodMode(); return false;
            case "H": await FullHeal(); return false;
            case "M": await MaxGold(); return false;
            case "L": await LevelUp(); return false;
            case "X": await MaxLevel(); return false;
            case "Z": await ResetCharacter(); return false;
            case "R": await ResetSteamStats(); return false;
            case "Q":
                terminal.WriteLine("Returning to Main Street...", "cyan");
                await Task.Delay(500);
                throw new LocationExitException(GameLocation.MainStreet);
            default:
                terminal.WriteLine("Invalid option.", "red");
                await Task.Delay(500);
                return false;
        }
    }

    private async Task<bool> HandleAuthentication(string input)
    {
        var requiredPasscode = UsurperRemake.BBS.DoorMode.IsOnlineMode ? ONLINE_PASSCODE : PASSCODE;
        if (input.ToUpper() == requiredPasscode)
        {
            // If Steam is available and flag not already set, warn the user
            if (SteamIntegration.IsAvailable && !currentPlayer.DevMenuUsed)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                terminal.WriteLine("║                         !! STEAM WARNING !!                                 ║");
                terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
                terminal.WriteLine("║  Using the Developer Menu will PERMANENTLY disable Steam achievements       ║");
                terminal.WriteLine("║  for this save file. This cannot be undone - the only way to re-enable      ║");
                terminal.WriteLine("║  achievements is to delete this save and start a new character.             ║");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                string confirm = await terminal.GetInput("Are you sure you want to proceed? (Y/N): ");
                if (confirm.Trim().ToUpper() != "Y")
                {
                    terminal.WriteLine("Dev menu access cancelled. Returning to Main Street...", "cyan");
                    await Task.Delay(1000);
                    throw new LocationExitException(GameLocation.MainStreet);
                }

                currentPlayer.DevMenuUsed = true;
                terminal.WriteLine("");
                terminal.WriteLine("Steam achievements have been disabled for this save.", "red");
                await Task.Delay(1000);
            }
            else if (!currentPlayer.DevMenuUsed)
            {
                // Non-Steam build: still set the flag silently in case save is later used with Steam
                currentPlayer.DevMenuUsed = true;
            }

            _authenticated = true;
            terminal.WriteLine("Access granted.", "green");
            await Task.Delay(500);
            return false;
        }
        else if (input.ToUpper() == "Q" || string.IsNullOrWhiteSpace(input))
        {
            terminal.WriteLine("Access denied. Returning to Main Street...", "red");
            await Task.Delay(1000);
            throw new LocationExitException(GameLocation.MainStreet);
        }
        else
        {
            terminal.WriteLine("Incorrect passcode.", "red");
            await Task.Delay(500);
            return false;
        }
    }

    #region Primary Stats

    private async Task EditPrimaryStats()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    EDIT PRIMARY STATS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  [1] Strength:     {currentPlayer.Strength} (Base: {currentPlayer.BaseStrength})");
        terminal.WriteLine($"  [2] Dexterity:    {currentPlayer.Dexterity} (Base: {currentPlayer.BaseDexterity})");
        terminal.WriteLine($"  [3] Constitution: {currentPlayer.Constitution} (Base: {currentPlayer.BaseConstitution})");
        terminal.WriteLine($"  [4] Intelligence: {currentPlayer.Intelligence} (Base: {currentPlayer.BaseIntelligence})");
        terminal.WriteLine($"  [5] Wisdom:       {currentPlayer.Wisdom} (Base: {currentPlayer.BaseWisdom})");
        terminal.WriteLine($"  [6] Charisma:     {currentPlayer.Charisma} (Base: {currentPlayer.BaseCharisma})");
        terminal.WriteLine($"  [7] SET ALL to value");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var statChoice = await terminal.GetInput("\nWhich stat? ");
        if (statChoice == "0") return;

        if (statChoice == "7")
        {
            var valueInput = await terminal.GetInput("Set ALL primary stats to: ");
            if (long.TryParse(valueInput, out long newValue))
            {
                currentPlayer.Strength = currentPlayer.BaseStrength = newValue;
                currentPlayer.Dexterity = currentPlayer.BaseDexterity = newValue;
                currentPlayer.Constitution = currentPlayer.BaseConstitution = newValue;
                currentPlayer.Intelligence = currentPlayer.BaseIntelligence = newValue;
                currentPlayer.Wisdom = currentPlayer.BaseWisdom = newValue;
                currentPlayer.Charisma = currentPlayer.BaseCharisma = newValue;
                terminal.WriteLine($"All primary stats set to {newValue}", "green");
            }
        }
        else
        {
            var valueInput = await terminal.GetInput("Enter new value: ");
            if (!long.TryParse(valueInput, out long newValue))
            {
                terminal.WriteLine("Invalid number!", "red");
                await Task.Delay(1000);
                return;
            }

            switch (statChoice)
            {
                case "1":
                    currentPlayer.Strength = currentPlayer.BaseStrength = newValue;
                    terminal.WriteLine($"Strength set to {newValue}", "green");
                    break;
                case "2":
                    currentPlayer.Dexterity = currentPlayer.BaseDexterity = newValue;
                    terminal.WriteLine($"Dexterity set to {newValue}", "green");
                    break;
                case "3":
                    currentPlayer.Constitution = currentPlayer.BaseConstitution = newValue;
                    terminal.WriteLine($"Constitution set to {newValue}", "green");
                    break;
                case "4":
                    currentPlayer.Intelligence = currentPlayer.BaseIntelligence = newValue;
                    terminal.WriteLine($"Intelligence set to {newValue}", "green");
                    break;
                case "5":
                    currentPlayer.Wisdom = currentPlayer.BaseWisdom = newValue;
                    terminal.WriteLine($"Wisdom set to {newValue}", "green");
                    break;
                case "6":
                    currentPlayer.Charisma = currentPlayer.BaseCharisma = newValue;
                    terminal.WriteLine($"Charisma set to {newValue}", "green");
                    break;
            }
        }

        await Task.Delay(1000);
    }

    #endregion

    #region Secondary Stats

    private async Task EditSecondaryStats()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                   EDIT SECONDARY STATS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  [1] Stamina:      {currentPlayer.Stamina} (Base: {currentPlayer.BaseStamina})");
        terminal.WriteLine($"  [2] Agility:      {currentPlayer.Agility} (Base: {currentPlayer.BaseAgility})");
        terminal.WriteLine($"  [3] Thievery:     {currentPlayer.Thievery}");
        terminal.WriteLine($"  [4] Loyalty:      {currentPlayer.Loyalty}%");
        terminal.WriteLine($"  [5] Mental:       {currentPlayer.Mental}");
        terminal.WriteLine($"  [6] Fame:         {currentPlayer.Fame}");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var statChoice = await terminal.GetInput("\nWhich stat? ");
        if (statChoice == "0") return;

        var valueInput = await terminal.GetInput("Enter new value: ");
        if (!long.TryParse(valueInput, out long newValue))
        {
            terminal.WriteLine("Invalid number!", "red");
            await Task.Delay(1000);
            return;
        }

        switch (statChoice)
        {
            case "1":
                currentPlayer.Stamina = currentPlayer.BaseStamina = newValue;
                terminal.WriteLine($"Stamina set to {newValue}", "green");
                break;
            case "2":
                currentPlayer.Agility = currentPlayer.BaseAgility = newValue;
                terminal.WriteLine($"Agility set to {newValue}", "green");
                break;
            case "3":
                currentPlayer.Thievery = newValue;
                terminal.WriteLine($"Thievery set to {newValue}", "green");
                break;
            case "4":
                currentPlayer.Loyalty = (int)Math.Clamp(newValue, 0, 100);
                terminal.WriteLine($"Loyalty set to {currentPlayer.Loyalty}%", "green");
                break;
            case "5":
                currentPlayer.Mental = (int)newValue;
                terminal.WriteLine($"Mental set to {newValue}", "green");
                break;
            case "6":
                currentPlayer.Fame = (int)newValue;
                terminal.WriteLine($"Fame set to {newValue}", "green");
                break;
        }

        await Task.Delay(1000);
    }

    #endregion

    #region Combat Stats

    private async Task EditCombatStats()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                     EDIT COMBAT STATS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  [1] Current HP:     {currentPlayer.HP}");
        terminal.WriteLine($"  [2] Max HP:         {currentPlayer.MaxHP} (Base: {currentPlayer.BaseMaxHP})");
        terminal.WriteLine($"  [3] Current Mana:   {currentPlayer.Mana}");
        terminal.WriteLine($"  [4] Max Mana:       {currentPlayer.MaxMana} (Base: {currentPlayer.BaseMaxMana})");
        terminal.WriteLine($"  [5] Defence:        {currentPlayer.Defence} (Base: {currentPlayer.BaseDefence})");
        terminal.WriteLine($"  [6] Weapon Power:   {currentPlayer.WeapPow}");
        terminal.WriteLine($"  [7] Armor Power:    {currentPlayer.ArmPow}");
        terminal.WriteLine($"  [8] Punch:          {currentPlayer.Punch}");
        terminal.WriteLine($"  [9] Absorb:         {currentPlayer.Absorb}");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var statChoice = await terminal.GetInput("\nWhich stat? ");
        if (statChoice == "0") return;

        var valueInput = await terminal.GetInput("Enter new value: ");
        if (!long.TryParse(valueInput, out long newValue))
        {
            terminal.WriteLine("Invalid number!", "red");
            await Task.Delay(1000);
            return;
        }

        switch (statChoice)
        {
            case "1":
                currentPlayer.HP = Math.Min(newValue, currentPlayer.MaxHP);
                terminal.WriteLine($"HP set to {currentPlayer.HP}", "green");
                break;
            case "2":
                currentPlayer.MaxHP = currentPlayer.BaseMaxHP = newValue;
                terminal.WriteLine($"Max HP set to {newValue}", "green");
                break;
            case "3":
                currentPlayer.Mana = Math.Min(newValue, currentPlayer.MaxMana);
                terminal.WriteLine($"Mana set to {currentPlayer.Mana}", "green");
                break;
            case "4":
                currentPlayer.MaxMana = currentPlayer.BaseMaxMana = newValue;
                terminal.WriteLine($"Max Mana set to {newValue}", "green");
                break;
            case "5":
                currentPlayer.Defence = currentPlayer.BaseDefence = newValue;
                terminal.WriteLine($"Defence set to {newValue}", "green");
                break;
            case "6":
                currentPlayer.WeapPow = newValue;
                terminal.WriteLine($"Weapon Power set to {newValue}", "green");
                break;
            case "7":
                currentPlayer.ArmPow = newValue;
                terminal.WriteLine($"Armor Power set to {newValue}", "green");
                break;
            case "8":
                currentPlayer.Punch = newValue;
                terminal.WriteLine($"Punch set to {newValue}", "green");
                break;
            case "9":
                currentPlayer.Absorb = newValue;
                terminal.WriteLine($"Absorb set to {newValue}", "green");
                break;
        }

        await Task.Delay(1000);
    }

    #endregion

    #region Resources

    private async Task EditResources()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                      EDIT RESOURCES");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  [1] Gold:             {currentPlayer.Gold:N0}");
        terminal.WriteLine($"  [2] Bank Gold:        {currentPlayer.BankGold:N0}");
        terminal.WriteLine($"  [3] Experience:       {currentPlayer.Experience:N0}");
        terminal.WriteLine($"  [4] Level:            {currentPlayer.Level}");
        terminal.WriteLine($"  [5] Healing Potions:  {currentPlayer.Healing} (Max: {currentPlayer.MaxPotions})");
        terminal.WriteLine($"  [6] Dungeon Fights:   {currentPlayer.Fights}");
        terminal.WriteLine($"  [7] Player Fights:    {currentPlayer.PFights}");
        terminal.WriteLine($"  [8] Team Fights:      {currentPlayer.TFights}");
        terminal.WriteLine($"  [9] Training Points:  {currentPlayer.TrainingPoints}");
        terminal.WriteLine($"  [A] Trains Left:      {currentPlayer.Trains}");
        terminal.WriteLine($"  [B] Thievery Attempts:{currentPlayer.Thiefs}");
        terminal.WriteLine($"  [C] Brawls Left:      {currentPlayer.Brawls}");
        terminal.WriteLine($"  [D] Assassinations:   {currentPlayer.Assa}");
        terminal.WriteLine($"  [E] Quests Completed: {currentPlayer.Quests}");
        terminal.WriteLine($"  [F] Turn Count:       {currentPlayer.TurnCount}");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var statChoice = await terminal.GetInput("\nWhich resource? ");
        if (statChoice == "0") return;

        var valueInput = await terminal.GetInput("Enter new value: ");
        if (!long.TryParse(valueInput, out long newValue))
        {
            terminal.WriteLine("Invalid number!", "red");
            await Task.Delay(1000);
            return;
        }

        switch (statChoice.ToUpper())
        {
            case "1":
                currentPlayer.Gold = newValue;
                terminal.WriteLine($"Gold set to {newValue:N0}", "green");
                break;
            case "2":
                currentPlayer.BankGold = newValue;
                terminal.WriteLine($"Bank Gold set to {newValue:N0}", "green");
                break;
            case "3":
                currentPlayer.Experience = newValue;
                terminal.WriteLine($"Experience set to {newValue:N0}", "green");
                break;
            case "4":
                currentPlayer.Level = (int)Math.Clamp(newValue, 1, GameConfig.MaxLevel);
                terminal.WriteLine($"Level set to {currentPlayer.Level}", "green");
                break;
            case "5":
                currentPlayer.Healing = newValue;
                terminal.WriteLine($"Healing Potions set to {newValue}", "green");
                break;
            case "6":
                currentPlayer.Fights = (int)newValue;
                terminal.WriteLine($"Dungeon Fights set to {newValue}", "green");
                break;
            case "7":
                currentPlayer.PFights = (int)newValue;
                terminal.WriteLine($"Player Fights set to {newValue}", "green");
                break;
            case "8":
                currentPlayer.TFights = (int)newValue;
                terminal.WriteLine($"Team Fights set to {newValue}", "green");
                break;
            case "9":
                currentPlayer.TrainingPoints = (int)newValue;
                terminal.WriteLine($"Training Points set to {newValue}", "green");
                break;
            case "A":
                currentPlayer.Trains = (int)newValue;
                terminal.WriteLine($"Trains set to {newValue}", "green");
                break;
            case "B":
                currentPlayer.Thiefs = (int)newValue;
                terminal.WriteLine($"Thievery Attempts set to {newValue}", "green");
                break;
            case "C":
                currentPlayer.Brawls = (int)newValue;
                terminal.WriteLine($"Brawls set to {newValue}", "green");
                break;
            case "D":
                currentPlayer.Assa = (int)newValue;
                terminal.WriteLine($"Assassinations set to {newValue}", "green");
                break;
            case "E":
                currentPlayer.Quests = (int)newValue;
                terminal.WriteLine($"Quests Completed set to {newValue}", "green");
                break;
            case "F":
                currentPlayer.TurnCount = (int)newValue;
                terminal.WriteLine($"Turn Count set to {newValue}", "green");
                break;
        }

        await Task.Delay(1000);
    }

    #endregion

    #region Alignment

    private async Task EditAlignment()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                       EDIT ALIGNMENT");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        int netAlignment = (int)(currentPlayer.Chivalry - currentPlayer.Darkness);
        string alignmentStr = netAlignment > 50 ? "Good" : netAlignment < -50 ? "Evil" : "Neutral";

        terminal.SetColor("white");
        terminal.WriteLine($"  Current Alignment: {alignmentStr} (Net: {netAlignment:+#;-#;0})");
        terminal.WriteLine($"  [1] Chivalry (Good): {currentPlayer.Chivalry}");
        terminal.WriteLine($"  [2] Darkness (Evil): {currentPlayer.Darkness}");
        terminal.WriteLine($"  [3] Good Deeds Left: {currentPlayer.ChivNr}");
        terminal.WriteLine($"  [4] Dark Deeds Left: {currentPlayer.DarkNr}");
        terminal.WriteLine("");
        terminal.WriteLine("  PRESETS:");
        terminal.WriteLine("  [5] Pure Good (Chivalry 1000, Darkness 0)");
        terminal.WriteLine("  [6] Pure Evil (Chivalry 0, Darkness 1000)");
        terminal.WriteLine("  [7] Perfect Neutral (Both 500)");
        terminal.WriteLine("  [8] Chaotic (Both 1000)");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");

        switch (choice)
        {
            case "0": return;
            case "1":
                var chivInput = await terminal.GetInput("Enter new Chivalry value: ");
                if (long.TryParse(chivInput, out long chivValue))
                {
                    currentPlayer.Chivalry = chivValue;
                    terminal.WriteLine($"Chivalry set to {chivValue}", "green");
                }
                break;
            case "2":
                var darkInput = await terminal.GetInput("Enter new Darkness value: ");
                if (long.TryParse(darkInput, out long darkValue))
                {
                    currentPlayer.Darkness = darkValue;
                    terminal.WriteLine($"Darkness set to {darkValue}", "green");
                }
                break;
            case "3":
                var chivNrInput = await terminal.GetInput("Enter Good Deeds left: ");
                if (int.TryParse(chivNrInput, out int chivNr))
                {
                    currentPlayer.ChivNr = chivNr;
                    terminal.WriteLine($"Good Deeds set to {chivNr}", "green");
                }
                break;
            case "4":
                var darkNrInput = await terminal.GetInput("Enter Dark Deeds left: ");
                if (int.TryParse(darkNrInput, out int darkNr))
                {
                    currentPlayer.DarkNr = darkNr;
                    terminal.WriteLine($"Dark Deeds set to {darkNr}", "green");
                }
                break;
            case "5":
                currentPlayer.Chivalry = 1000;
                currentPlayer.Darkness = 0;
                terminal.WriteLine("Set to Pure Good!", "bright_yellow");
                break;
            case "6":
                currentPlayer.Chivalry = 0;
                currentPlayer.Darkness = 1000;
                terminal.WriteLine("Set to Pure Evil!", "red");
                break;
            case "7":
                currentPlayer.Chivalry = 500;
                currentPlayer.Darkness = 500;
                terminal.WriteLine("Set to Perfect Neutral.", "gray");
                break;
            case "8":
                currentPlayer.Chivalry = 1000;
                currentPlayer.Darkness = 1000;
                terminal.WriteLine("Set to Chaotic!", "bright_magenta");
                break;
        }

        await Task.Delay(1000);
    }

    #endregion

    #region Class and Race

    private async Task EditClassAndRace()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    EDIT CLASS & RACE");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  Current Class: {currentPlayer.Class}");
        terminal.WriteLine($"  Current Race:  {currentPlayer.Race}");
        terminal.WriteLine("");
        terminal.WriteLine("  [1] Change Class");
        terminal.WriteLine("  [2] Change Race");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");

        if (choice == "1")
        {
            terminal.WriteLine("\n  Available Classes:");
            var classes = Enum.GetValues<CharacterClass>();
            for (int i = 0; i < classes.Length; i++)
            {
                terminal.WriteLine($"    [{i}] {classes[i]}");
            }

            var classInput = await terminal.GetInput("\nSelect class number: ");
            if (int.TryParse(classInput, out int classIndex) && classIndex >= 0 && classIndex < classes.Length)
            {
                currentPlayer.Class = classes[classIndex];
                terminal.WriteLine($"Class changed to {currentPlayer.Class}", "green");
            }
        }
        else if (choice == "2")
        {
            terminal.WriteLine("\n  Available Races:");
            var races = Enum.GetValues<CharacterRace>();
            for (int i = 0; i < races.Length; i++)
            {
                terminal.WriteLine($"    [{i}] {races[i]}");
            }

            var raceInput = await terminal.GetInput("\nSelect race number: ");
            if (int.TryParse(raceInput, out int raceIndex) && raceIndex >= 0 && raceIndex < races.Length)
            {
                currentPlayer.Race = races[raceIndex];
                terminal.WriteLine($"Race changed to {currentPlayer.Race}", "green");
            }
        }

        await Task.Delay(1000);
    }

    #endregion

    #region Appearance

    private async Task EditAppearance()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                      EDIT APPEARANCE");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  [1] Sex:         {currentPlayer.Sex}");
        terminal.WriteLine($"  [2] Age:         {currentPlayer.Age}");
        terminal.WriteLine($"  [3] Height:      {currentPlayer.Height}");
        terminal.WriteLine($"  [4] Weight:      {currentPlayer.Weight}");
        terminal.WriteLine($"  [5] Eye Color:   {currentPlayer.Eyes}");
        terminal.WriteLine($"  [6] Hair Color:  {currentPlayer.Hair}");
        terminal.WriteLine($"  [7] Skin Color:  {currentPlayer.Skin}");
        terminal.WriteLine($"  [8] Name:        {currentPlayer.Name2}");
        terminal.WriteLine($"  [9] Real Name:   {currentPlayer.Name1}");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");
        if (choice == "0") return;

        switch (choice)
        {
            case "1":
                currentPlayer.Sex = currentPlayer.Sex == CharacterSex.Male ? CharacterSex.Female : CharacterSex.Male;
                terminal.WriteLine($"Sex changed to {currentPlayer.Sex}", "green");
                break;
            case "2":
                var ageInput = await terminal.GetInput("Enter age: ");
                if (int.TryParse(ageInput, out int age))
                {
                    currentPlayer.Age = age;
                    terminal.WriteLine($"Age set to {age}", "green");
                }
                break;
            case "3":
                var heightInput = await terminal.GetInput("Enter height: ");
                if (int.TryParse(heightInput, out int height))
                {
                    currentPlayer.Height = height;
                    terminal.WriteLine($"Height set to {height}", "green");
                }
                break;
            case "4":
                var weightInput = await terminal.GetInput("Enter weight: ");
                if (int.TryParse(weightInput, out int weight))
                {
                    currentPlayer.Weight = weight;
                    terminal.WriteLine($"Weight set to {weight}", "green");
                }
                break;
            case "5":
                var eyeInput = await terminal.GetInput("Enter eye color (0-9): ");
                if (int.TryParse(eyeInput, out int eyes))
                {
                    currentPlayer.Eyes = eyes;
                    terminal.WriteLine($"Eye color set to {eyes}", "green");
                }
                break;
            case "6":
                var hairInput = await terminal.GetInput("Enter hair color (0-9): ");
                if (int.TryParse(hairInput, out int hair))
                {
                    currentPlayer.Hair = hair;
                    terminal.WriteLine($"Hair color set to {hair}", "green");
                }
                break;
            case "7":
                var skinInput = await terminal.GetInput("Enter skin color (0-9): ");
                if (int.TryParse(skinInput, out int skin))
                {
                    currentPlayer.Skin = skin;
                    terminal.WriteLine($"Skin color set to {skin}", "green");
                }
                break;
            case "8":
                var nameInput = await terminal.GetInput("Enter new name: ");
                if (!string.IsNullOrWhiteSpace(nameInput))
                {
                    currentPlayer.Name2 = nameInput;
                    terminal.WriteLine($"Name changed to {nameInput}", "green");
                }
                break;
            case "9":
                var realNameInput = await terminal.GetInput("Enter new real name: ");
                if (!string.IsNullOrWhiteSpace(realNameInput))
                {
                    currentPlayer.Name1 = realNameInput;
                    terminal.WriteLine($"Real name changed to {realNameInput}", "green");
                }
                break;
        }

        await Task.Delay(1000);
    }

    #endregion

    #region Phrases

    private async Task EditPhrases()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                   EDIT PHRASES & BATTLE CRY");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  [1] When being attacked:  {GetPhrase(0)}");
        terminal.WriteLine($"  [2] When you win:         {GetPhrase(1)}");
        terminal.WriteLine($"  [3] When you lose:        {GetPhrase(2)}");
        terminal.WriteLine($"  [4] When begging mercy:   {GetPhrase(3)}");
        terminal.WriteLine($"  [5] When sparing opponent:{GetPhrase(4)}");
        terminal.WriteLine($"  [6] When killing opponent:{GetPhrase(5)}");
        terminal.WriteLine($"  [7] Battle Cry:           {currentPlayer.BattleCry}");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");
        if (choice == "0") return;

        if (int.TryParse(choice, out int phraseIndex) && phraseIndex >= 1 && phraseIndex <= 6)
        {
            var newPhrase = await terminal.GetInput($"Enter new phrase: ");
            if (currentPlayer.Phrases != null && currentPlayer.Phrases.Count >= phraseIndex)
            {
                currentPlayer.Phrases[phraseIndex - 1] = newPhrase;
                terminal.WriteLine("Phrase updated!", "green");
            }
        }
        else if (choice == "7")
        {
            var newCry = await terminal.GetInput("Enter new battle cry: ");
            currentPlayer.BattleCry = newCry;
            terminal.WriteLine("Battle cry updated!", "green");
        }

        await Task.Delay(1000);
    }

    private string GetPhrase(int index)
    {
        if (currentPlayer.Phrases == null || currentPlayer.Phrases.Count <= index)
            return "(not set)";
        return string.IsNullOrEmpty(currentPlayer.Phrases[index]) ? "(not set)" : currentPlayer.Phrases[index];
    }

    #endregion

    #region Diseases

    private async Task ToggleDiseases()
    {
        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            terminal.WriteLine("                     TOGGLE DISEASES");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine($"  [1] Blind:      {(currentPlayer.Blind ? "[ON]" : "[OFF]")}");
            terminal.WriteLine($"  [2] Plague:     {(currentPlayer.Plague ? "[ON]" : "[OFF]")}");
            terminal.WriteLine($"  [3] Smallpox:   {(currentPlayer.Smallpox ? "[ON]" : "[OFF]")}");
            terminal.WriteLine($"  [4] Measles:    {(currentPlayer.Measles ? "[ON]" : "[OFF]")}");
            terminal.WriteLine($"  [5] Leprosy:    {(currentPlayer.Leprosy ? "[ON]" : "[OFF]")}");
            terminal.WriteLine($"  [6] Lover's Bane (STD): {(currentPlayer.LoversBane ? "[ON]" : "[OFF]")}");
            terminal.WriteLine($"  [7] Poison Level: {currentPlayer.Poison}");
            terminal.WriteLine("");
            terminal.WriteLine("  [C] Cure All Diseases");
            terminal.WriteLine("  [I] Infect All Diseases");
            terminal.WriteLine("");
            terminal.WriteLine("  [0] Back");

            var choice = await terminal.GetInput("\nChoice: ");

            switch (choice.ToUpper())
            {
                case "0": return;
                case "1": currentPlayer.Blind = !currentPlayer.Blind; break;
                case "2": currentPlayer.Plague = !currentPlayer.Plague; break;
                case "3": currentPlayer.Smallpox = !currentPlayer.Smallpox; break;
                case "4": currentPlayer.Measles = !currentPlayer.Measles; break;
                case "5": currentPlayer.Leprosy = !currentPlayer.Leprosy; break;
                case "6": currentPlayer.LoversBane = !currentPlayer.LoversBane; break;
                case "7":
                    var poisonInput = await terminal.GetInput("Enter poison level (0-100): ");
                    if (int.TryParse(poisonInput, out int poison))
                        currentPlayer.Poison = Math.Clamp(poison, 0, 100);
                    break;
                case "C":
                    currentPlayer.Blind = false;
                    currentPlayer.Plague = false;
                    currentPlayer.Smallpox = false;
                    currentPlayer.Measles = false;
                    currentPlayer.Leprosy = false;
                    currentPlayer.LoversBane = false;
                    currentPlayer.Poison = 0;
                    terminal.WriteLine("All diseases cured!", "bright_green");
                    await Task.Delay(1000);
                    break;
                case "I":
                    currentPlayer.Blind = true;
                    currentPlayer.Plague = true;
                    currentPlayer.Smallpox = true;
                    currentPlayer.Measles = true;
                    currentPlayer.Leprosy = true;
                    currentPlayer.LoversBane = true;
                    currentPlayer.Poison = 100;
                    terminal.WriteLine("All diseases inflicted!", "red");
                    await Task.Delay(1000);
                    break;
            }
        }
    }

    #endregion

    #region Status Effects

    private async Task ToggleStatusEffects()
    {
        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            terminal.WriteLine("                   TOGGLE STATUS EFFECTS");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            var statuses = Enum.GetValues<StatusEffect>().Where(s => s != StatusEffect.None).ToArray();

            terminal.SetColor("white");
            terminal.WriteLine("  Current Active Status Effects:");
            if (currentPlayer.ActiveStatuses.Count == 0)
            {
                terminal.WriteLine("    (none)", "gray");
            }
            else
            {
                foreach (var kvp in currentPlayer.ActiveStatuses)
                {
                    terminal.WriteLine($"    {kvp.Key}: {kvp.Value} rounds", "yellow");
                }
            }

            terminal.WriteLine("");
            terminal.WriteLine("  Available effects to add (enter number):");

            for (int i = 0; i < Math.Min(20, statuses.Length); i++)
            {
                string active = currentPlayer.HasStatus(statuses[i]) ? " [ACTIVE]" : "";
                terminal.WriteLine($"    [{i + 1}] {statuses[i]}{active}");
            }

            terminal.WriteLine("");
            terminal.WriteLine("  [C] Clear All Status Effects");
            terminal.WriteLine("  [0] Back");

            var choice = await terminal.GetInput("\nChoice: ");

            if (choice.ToUpper() == "0") return;
            if (choice.ToUpper() == "C")
            {
                currentPlayer.ClearAllStatuses();
                terminal.WriteLine("All status effects cleared!", "green");
                await Task.Delay(1000);
                continue;
            }

            if (int.TryParse(choice, out int statusIndex) && statusIndex >= 1 && statusIndex <= statuses.Length)
            {
                var status = statuses[statusIndex - 1];
                if (currentPlayer.HasStatus(status))
                {
                    currentPlayer.RemoveStatus(status);
                    terminal.WriteLine($"Removed {status}", "yellow");
                }
                else
                {
                    var durationInput = await terminal.GetInput($"Duration for {status} (rounds): ");
                    if (int.TryParse(durationInput, out int duration) && duration > 0)
                    {
                        currentPlayer.ApplyStatus(status, duration);
                        terminal.WriteLine($"Applied {status} for {duration} rounds", "green");
                    }
                }
                await Task.Delay(1000);
            }
        }
    }

    #endregion

    #region Drug Status

    private async Task EditDrugStatus()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    EDIT DRUG STATUS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  Active Drug:       {currentPlayer.ActiveDrug}");
        terminal.WriteLine($"  Drug Effect Days:  {currentPlayer.DrugEffectDays}");
        terminal.WriteLine($"  Steroid Days:      {currentPlayer.SteroidDays}");
        terminal.WriteLine($"  Addiction Level:   {currentPlayer.Addict}% {(currentPlayer.IsAddicted ? "(ADDICTED)" : "")}");
        terminal.WriteLine("");
        terminal.WriteLine("  [1] Set Active Drug");
        terminal.WriteLine("  [2] Set Drug Effect Days");
        terminal.WriteLine("  [3] Set Steroid Days");
        terminal.WriteLine("  [4] Set Addiction Level");
        terminal.WriteLine("  [5] Clear All Drug Effects");
        terminal.WriteLine("  [6] Max Addiction (100%)");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");

        switch (choice)
        {
            case "0": return;
            case "1":
                terminal.WriteLine("\n  Available Drugs:");
                var drugs = Enum.GetValues<DrugType>();
                for (int i = 0; i < drugs.Length; i++)
                {
                    terminal.WriteLine($"    [{i}] {drugs[i]}");
                }
                var drugInput = await terminal.GetInput("\nSelect drug: ");
                if (int.TryParse(drugInput, out int drugIndex) && drugIndex >= 0 && drugIndex < drugs.Length)
                {
                    currentPlayer.ActiveDrug = drugs[drugIndex];
                    if (currentPlayer.DrugEffectDays == 0) currentPlayer.DrugEffectDays = 3;
                    terminal.WriteLine($"Active drug set to {drugs[drugIndex]}", "green");
                }
                break;
            case "2":
                var daysInput = await terminal.GetInput("Enter drug effect days: ");
                if (int.TryParse(daysInput, out int days))
                {
                    currentPlayer.DrugEffectDays = days;
                    terminal.WriteLine($"Drug effect days set to {days}", "green");
                }
                break;
            case "3":
                var steroidInput = await terminal.GetInput("Enter steroid days: ");
                if (int.TryParse(steroidInput, out int steroidDays))
                {
                    currentPlayer.SteroidDays = steroidDays;
                    terminal.WriteLine($"Steroid days set to {steroidDays}", "green");
                }
                break;
            case "4":
                var addictInput = await terminal.GetInput("Enter addiction level (0-100): ");
                if (int.TryParse(addictInput, out int addict))
                {
                    currentPlayer.Addict = Math.Clamp(addict, 0, 100);
                    terminal.WriteLine($"Addiction set to {currentPlayer.Addict}%", "green");
                }
                break;
            case "5":
                currentPlayer.ActiveDrug = DrugType.None;
                currentPlayer.DrugEffectDays = 0;
                currentPlayer.SteroidDays = 0;
                currentPlayer.Addict = 0;
                terminal.WriteLine("All drug effects cleared!", "green");
                break;
            case "6":
                currentPlayer.Addict = 100;
                terminal.WriteLine("Addiction maxed to 100%!", "red");
                break;
        }

        await Task.Delay(1000);
    }

    #endregion

    #region Story Progress

    private async Task EditStoryProgress()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    EDIT STORY PROGRESS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        var story = StoryProgressionSystem.Instance;

        terminal.SetColor("white");
        terminal.WriteLine($"  Current Chapter: {story.CurrentChapter}");
        terminal.WriteLine($"  Current Act:     {story.CurrentAct}");
        terminal.WriteLine("");
        terminal.WriteLine("  Collected Seals:");
        foreach (var seal in Enum.GetValues<SealType>())
        {
            bool collected = story.CollectedSeals.Contains(seal);
            terminal.WriteLine($"    {seal}: {(collected ? "[COLLECTED]" : "[NOT FOUND]")}");
        }
        terminal.WriteLine("");
        terminal.WriteLine("  [1] Set Chapter");
        terminal.WriteLine("  [2] Unlock All Seals");
        terminal.WriteLine("  [3] Clear All Seals");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");

        switch (choice)
        {
            case "0": return;
            case "1":
                terminal.WriteLine("\n  Available Chapters:");
                var chapters = Enum.GetValues<StoryChapter>();
                for (int i = 0; i < chapters.Length; i++)
                {
                    terminal.WriteLine($"    [{i}] {chapters[i]}");
                }
                var chapterInput = await terminal.GetInput("\nSelect chapter: ");
                if (int.TryParse(chapterInput, out int chapterIndex) && chapterIndex >= 0 && chapterIndex < chapters.Length)
                {
                    story.AdvanceChapter(chapters[chapterIndex]);
                    terminal.WriteLine($"Chapter set to {chapters[chapterIndex]}", "green");
                }
                break;
            case "2":
                foreach (var seal in Enum.GetValues<SealType>())
                {
                    story.CollectSeal(seal);
                }
                story.SetStoryFlag("all_seals_collected", true);
                story.SetStoryFlag("true_ending_possible", true);
                terminal.WriteLine("All seals unlocked!", "bright_magenta");
                break;
            case "3":
                story.CollectedSeals.Clear();
                story.SetStoryFlag("all_seals_collected", false);
                terminal.WriteLine("All seals cleared!", "yellow");
                break;
        }

        await Task.Delay(1500);
    }

    #endregion

    #region Artifacts and Gods

    private async Task EditArtifactsAndGods()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                 EDIT ARTIFACTS & GOD STATUS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        var story = StoryProgressionSystem.Instance;

        terminal.SetColor("white");
        terminal.WriteLine("  Collected Artifacts:");
        foreach (var artifact in Enum.GetValues<ArtifactType>())
        {
            bool collected = story.CollectedArtifacts.Contains(artifact);
            terminal.WriteLine($"    {artifact}: {(collected ? "[COLLECTED]" : "[NOT FOUND]")}");
        }
        terminal.WriteLine("");
        terminal.WriteLine("  Old God Status:");
        foreach (var god in Enum.GetValues<OldGodType>())
        {
            if (story.OldGodStates.TryGetValue(god, out var state))
            {
                terminal.WriteLine($"    {god}: {state.Status}");
            }
        }
        terminal.WriteLine("");
        terminal.WriteLine("  [1] Unlock All Artifacts");
        terminal.WriteLine("  [2] Clear All Artifacts");
        terminal.WriteLine("  [3] Defeat All Gods");
        terminal.WriteLine("  [4] Save All Gods");
        terminal.WriteLine("  [5] Reset All Gods to Corrupted");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");

        switch (choice)
        {
            case "0": return;
            case "1":
                foreach (var artifact in Enum.GetValues<ArtifactType>())
                {
                    story.CollectArtifact(artifact);
                }
                terminal.WriteLine("All artifacts collected!", "bright_cyan");
                break;
            case "2":
                story.CollectedArtifacts.Clear();
                terminal.WriteLine("All artifacts cleared!", "yellow");
                break;
            case "3":
                foreach (var god in Enum.GetValues<OldGodType>())
                {
                    if (god != OldGodType.Manwe)
                        story.UpdateGodState(god, GodStatus.Defeated);
                }
                terminal.WriteLine("All Old Gods defeated!", "red");
                break;
            case "4":
                foreach (var god in Enum.GetValues<OldGodType>())
                {
                    if (god != OldGodType.Manwe)
                        story.UpdateGodState(god, GodStatus.Saved);
                }
                terminal.WriteLine("All Old Gods saved!", "bright_yellow");
                break;
            case "5":
                foreach (var god in Enum.GetValues<OldGodType>())
                {
                    story.UpdateGodState(god, GodStatus.Corrupted);
                }
                terminal.WriteLine("All Old Gods reset to Corrupted.", "gray");
                break;
        }

        await Task.Delay(1500);
    }

    #endregion

    #region Kill Statistics

    private async Task EditKillStatistics()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                   EDIT KILL STATISTICS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  [1] Monster Kills:   {currentPlayer.MKills}");
        terminal.WriteLine($"  [2] Monster Defeats: {currentPlayer.MDefeats}");
        terminal.WriteLine($"  [3] Player Kills:    {currentPlayer.PKills}");
        terminal.WriteLine($"  [4] Player Defeats:  {currentPlayer.PDefeats}");
        terminal.WriteLine($"  [5] Resurrections:   {currentPlayer.Resurrections} (Used: {currentPlayer.ResurrectionsUsed})");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");
        if (choice == "0") return;

        var valueInput = await terminal.GetInput("Enter new value: ");
        if (!long.TryParse(valueInput, out long newValue))
        {
            terminal.WriteLine("Invalid number!", "red");
            await Task.Delay(1000);
            return;
        }

        switch (choice)
        {
            case "1":
                currentPlayer.MKills = newValue;
                terminal.WriteLine($"Monster Kills set to {newValue}", "green");
                break;
            case "2":
                currentPlayer.MDefeats = newValue;
                terminal.WriteLine($"Monster Defeats set to {newValue}", "green");
                break;
            case "3":
                currentPlayer.PKills = newValue;
                terminal.WriteLine($"Player Kills set to {newValue}", "green");
                break;
            case "4":
                currentPlayer.PDefeats = newValue;
                terminal.WriteLine($"Player Defeats set to {newValue}", "green");
                break;
            case "5":
                currentPlayer.Resurrections = (int)newValue;
                terminal.WriteLine($"Resurrections set to {newValue}", "green");
                break;
        }

        await Task.Delay(1000);
    }

    #endregion

    #region Social Status

    private async Task EditSocialStatus()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    EDIT SOCIAL STATUS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  [1] King/Ruler:       {(currentPlayer.King ? "[YES]" : "[NO]")}");
        terminal.WriteLine($"  [2] Days in Power:    {currentPlayer.DaysInPower}");
        terminal.WriteLine($"  [3] Team Name:        {currentPlayer.Team}");
        terminal.WriteLine($"  [4] Team Controls Turf: {(currentPlayer.CTurf ? "[YES]" : "[NO]")}");
        terminal.WriteLine($"  [5] Married:          {(currentPlayer.Married ? "[YES]" : "[NO]")}");
        terminal.WriteLine($"  [6] Spouse Name:      {currentPlayer.SpouseName}");
        terminal.WriteLine($"  [7] Kids:             {currentPlayer.Kids}");
        terminal.WriteLine($"  [8] Pregnancy Days:   {currentPlayer.Pregnancy}");
        terminal.WriteLine($"  [9] Married Times:    {currentPlayer.MarriedTimes}");
        terminal.WriteLine($"  [A] Wanted Level:     {currentPlayer.WantedLvl}");
        terminal.WriteLine($"  [B] Prison Days:      {currentPlayer.DaysInPrison}");
        terminal.WriteLine($"  [C] Immortal:         {(currentPlayer.Immortal ? "[YES]" : "[NO]")}");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");

        switch (choice.ToUpper())
        {
            case "0": return;
            case "1":
                currentPlayer.King = !currentPlayer.King;
                terminal.WriteLine($"King status: {(currentPlayer.King ? "YES" : "NO")}", "green");
                break;
            case "2":
                var daysInput = await terminal.GetInput("Enter days in power: ");
                if (int.TryParse(daysInput, out int days))
                {
                    currentPlayer.DaysInPower = days;
                    terminal.WriteLine($"Days in power set to {days}", "green");
                }
                break;
            case "3":
                var teamInput = await terminal.GetInput("Enter team name: ");
                currentPlayer.Team = teamInput;
                terminal.WriteLine($"Team set to {teamInput}", "green");
                break;
            case "4":
                currentPlayer.CTurf = !currentPlayer.CTurf;
                terminal.WriteLine($"Controls turf: {(currentPlayer.CTurf ? "YES" : "NO")}", "green");
                break;
            case "5":
                currentPlayer.Married = !currentPlayer.Married;
                currentPlayer.IsMarried = currentPlayer.Married;
                terminal.WriteLine($"Married: {(currentPlayer.Married ? "YES" : "NO")}", "green");
                break;
            case "6":
                var spouseInput = await terminal.GetInput("Enter spouse name: ");
                currentPlayer.SpouseName = spouseInput;
                terminal.WriteLine($"Spouse set to {spouseInput}", "green");
                break;
            case "7":
                var kidsInput = await terminal.GetInput("Enter number of kids: ");
                if (int.TryParse(kidsInput, out int kids))
                {
                    currentPlayer.Kids = kids;
                    terminal.WriteLine($"Kids set to {kids}", "green");
                }
                break;
            case "8":
                var pregInput = await terminal.GetInput("Enter pregnancy days (0 = not pregnant): ");
                if (byte.TryParse(pregInput, out byte preg))
                {
                    currentPlayer.Pregnancy = preg;
                    terminal.WriteLine($"Pregnancy days set to {preg}", "green");
                }
                break;
            case "9":
                var marriedTimesInput = await terminal.GetInput("Enter times married: ");
                if (int.TryParse(marriedTimesInput, out int marriedTimes))
                {
                    currentPlayer.MarriedTimes = marriedTimes;
                    terminal.WriteLine($"Married times set to {marriedTimes}", "green");
                }
                break;
            case "A":
                var wantedInput = await terminal.GetInput("Enter wanted level: ");
                if (int.TryParse(wantedInput, out int wanted))
                {
                    currentPlayer.WantedLvl = wanted;
                    terminal.WriteLine($"Wanted level set to {wanted}", "green");
                }
                break;
            case "B":
                var prisonInput = await terminal.GetInput("Enter prison days: ");
                if (byte.TryParse(prisonInput, out byte prison))
                {
                    currentPlayer.DaysInPrison = prison;
                    terminal.WriteLine($"Prison days set to {prison}", "green");
                }
                break;
            case "C":
                currentPlayer.Immortal = !currentPlayer.Immortal;
                terminal.WriteLine($"Immortal: {(currentPlayer.Immortal ? "YES" : "NO")}", "green");
                break;
        }

        await Task.Delay(1000);
    }

    #endregion

    #region Inventory

    private async Task SpawnItems()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                      SPAWN ITEMS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  [1] Spawn Weapon (specify power)");
        terminal.WriteLine("  [2] Spawn Armor (specify power)");
        terminal.WriteLine("  [3] Spawn Healing Potions");
        terminal.WriteLine("  [4] Spawn Random Dungeon Loot");
        terminal.WriteLine("  [5] View Current Inventory");
        terminal.WriteLine("  [6] Spawn Unidentified Item");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");

        switch (choice)
        {
            case "0": return;
            case "1":
                var weapPowerInput = await terminal.GetInput("Enter weapon power: ");
                var weapNameInput = await terminal.GetInput("Enter weapon name: ");
                if (int.TryParse(weapPowerInput, out int weapPower))
                {
                    var weapon = new Item
                    {
                        Name = string.IsNullOrWhiteSpace(weapNameInput) ? $"Dev Weapon +{weapPower}" : weapNameInput,
                        Type = ObjType.Weapon,
                        Attack = weapPower,
                        Value = weapPower * 50
                    };
                    currentPlayer.Inventory.Add(weapon);
                    terminal.WriteLine($"Spawned: {weapon.Name}", "green");
                }
                break;
            case "2":
                var armPowerInput = await terminal.GetInput("Enter armor power: ");
                var armNameInput = await terminal.GetInput("Enter armor name: ");
                if (int.TryParse(armPowerInput, out int armPower))
                {
                    var armor = new Item
                    {
                        Name = string.IsNullOrWhiteSpace(armNameInput) ? $"Dev Armor +{armPower}" : armNameInput,
                        Type = ObjType.Body,
                        Defence = armPower,
                        Value = armPower * 50
                    };
                    currentPlayer.Inventory.Add(armor);
                    terminal.WriteLine($"Spawned: {armor.Name}", "green");
                }
                break;
            case "3":
                var potionInput = await terminal.GetInput("How many healing potions? ");
                if (long.TryParse(potionInput, out long potions))
                {
                    currentPlayer.Healing += potions;
                    terminal.WriteLine($"Added {potions} healing potions (Total: {currentPlayer.Healing})", "green");
                }
                break;
            case "4":
                var levelInput = await terminal.GetInput("Dungeon level for loot (1-100): ");
                if (int.TryParse(levelInput, out int level))
                {
                    level = Math.Clamp(level, 1, 100);
                    // Generate some random loot
                    for (int i = 0; i < 5; i++)
                    {
                        var loot = NPCItemGenerator.GenerateWeapon(currentPlayer.Class, level);
                        currentPlayer.Inventory.Add(new Item
                        {
                            Name = loot.Name,
                            Type = loot.Type == global::ObjType.Weapon ? ObjType.Weapon : ObjType.Body,
                            Attack = loot.Attack,
                            Value = loot.Value
                        });
                        terminal.WriteLine($"  Spawned: {loot.Name}", "cyan");
                    }
                }
                break;
            case "5":
                terminal.WriteLine("\n  Current Inventory:", "white");
                if (currentPlayer.Inventory.Count == 0)
                {
                    terminal.WriteLine("    (empty)", "gray");
                }
                else
                {
                    foreach (var item in currentPlayer.Inventory)
                    {
                        terminal.WriteLine($"    {item.Name} ({item.Type}, Value: {item.Value})");
                    }
                }
                await terminal.PressAnyKey();
                return;
            case "6":
                var unidLevelInput = await terminal.GetInput("Dungeon level for loot (1-100): ");
                if (int.TryParse(unidLevelInput, out int unidLevel))
                {
                    unidLevel = Math.Clamp(unidLevel, 1, 100);
                    // Generate loot with boosted rarity to ensure unidentified chance
                    // Use MiniBoss loot for better rarity (Rare+ items become unidentified)
                    var unidItem = LootGenerator.GenerateMiniBossLoot(unidLevel, currentPlayer.Class);
                    unidItem.IsIdentified = false;
                    currentPlayer.Inventory.Add(unidItem);
                    var mysteryName = LootGenerator.GetUnidentifiedName(unidItem);
                    terminal.SetColor("magenta");
                    terminal.WriteLine($"  Spawned: {mysteryName}");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  (Actual: {unidItem.Name}, {unidItem.Type}, Value: {unidItem.Value})");
                }
                break;
        }

        await Task.Delay(1500);
    }

    private async Task ClearInventory()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                     CLEAR INVENTORY");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.WriteLine($"  You have {currentPlayer.Inventory.Count} items in your inventory.");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput("Clear all items? (Y/N): ");
        if (confirm.ToUpper() == "Y")
        {
            currentPlayer.Inventory.Clear();
            terminal.WriteLine("Inventory cleared!", "green");
        }
        else
        {
            terminal.WriteLine("Cancelled.", "gray");
        }

        await Task.Delay(1000);
    }

    #endregion

    #region NPC Controls

    private async Task NPCControls()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                       NPC CONTROLS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
        int npcCount = npcs?.Count ?? 0;

        terminal.SetColor("white");
        terminal.WriteLine($"  Active NPCs: {npcCount}");
        terminal.WriteLine("");
        terminal.WriteLine("  [1] List All NPCs");
        terminal.WriteLine("  [2] Modify NPC Stats");
        terminal.WriteLine("  [3] Kill an NPC");
        terminal.WriteLine("  [4] Resurrect All Dead NPCs");
        terminal.WriteLine("  [5] Teleport NPC to Location");
        terminal.WriteLine("  [6] Force NPC Level Up");
        terminal.WriteLine("  [7] Respawn All NPCs");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");

        switch (choice)
        {
            case "0": return;
            case "1":
                terminal.ClearScreen();
                terminal.WriteLine("  ACTIVE NPCs:", "bright_cyan");
                terminal.WriteLine("");
                if (npcs != null)
                {
                    int idx = 1;
                    foreach (var npc in npcs.Take(30))
                    {
                        string status = npc.IsAlive ? "ALIVE" : "DEAD";
                        terminal.WriteLine($"  [{idx}] {npc.Name} - Lvl {npc.Level} {npc.Class} @ {npc.CurrentLocation} ({status})");
                        idx++;
                    }
                    if (npcs.Count > 30)
                    {
                        terminal.WriteLine($"  ... and {npcs.Count - 30} more", "gray");
                    }
                }
                await terminal.PressAnyKey();
                break;
            case "2":
                await ModifyNPCStats();
                break;
            case "3":
                var killInput = await terminal.GetInput("Enter NPC name to kill: ");
                var npcToKill = npcs?.FirstOrDefault(n => n.Name.Contains(killInput, StringComparison.OrdinalIgnoreCase));
                if (npcToKill != null)
                {
                    npcToKill.HP = 0;
                    terminal.WriteLine($"{npcToKill.Name} has been killed!", "red");
                }
                else
                {
                    terminal.WriteLine("NPC not found.", "yellow");
                }
                break;
            case "4":
                if (npcs != null)
                {
                    int resurrected = 0;
                    foreach (var npc in npcs.Where(n => !n.IsAlive))
                    {
                        npc.HP = npc.MaxHP;
                        resurrected++;
                    }
                    terminal.WriteLine($"Resurrected {resurrected} NPCs!", "green");
                }
                break;
            case "5":
                var teleportInput = await terminal.GetInput("Enter NPC name: ");
                var npcToTeleport = npcs?.FirstOrDefault(n => n.Name.Contains(teleportInput, StringComparison.OrdinalIgnoreCase));
                if (npcToTeleport != null)
                {
                    var locInput = await terminal.GetInput("Enter location name: ");
                    npcToTeleport.UpdateLocation(locInput);
                    terminal.WriteLine($"{npcToTeleport.Name} teleported to {locInput}!", "green");
                }
                break;
            case "6":
                var levelInput = await terminal.GetInput("Enter NPC name: ");
                var npcToLevel = npcs?.FirstOrDefault(n => n.Name.Contains(levelInput, StringComparison.OrdinalIgnoreCase));
                if (npcToLevel != null)
                {
                    var newLevelInput = await terminal.GetInput($"Current level: {npcToLevel.Level}. New level: ");
                    if (int.TryParse(newLevelInput, out int newLevel))
                    {
                        npcToLevel.Level = Math.Clamp(newLevel, 1, 100);
                        npcToLevel.MaxHP = 50 + npcToLevel.Level * 20;
                        npcToLevel.HP = npcToLevel.MaxHP;
                        terminal.WriteLine($"{npcToLevel.Name} is now level {npcToLevel.Level}!", "green");
                    }
                }
                break;
            case "7":
                // Re-initialize NPCs by calling the initialization method
                if (NPCSpawnSystem.Instance != null)
                {
                    terminal.WriteLine("Reinitializing NPCs...", "yellow");
                    _ = NPCSpawnSystem.Instance.InitializeClassicNPCs();
                    terminal.WriteLine("All NPCs reinitialized!", "green");
                }
                break;
        }

        await Task.Delay(1500);
    }

    private async Task ModifyNPCStats()
    {
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
        var npcNameInput = await terminal.GetInput("Enter NPC name to modify: ");
        var npc = npcs?.FirstOrDefault(n => n.Name.Contains(npcNameInput, StringComparison.OrdinalIgnoreCase));

        if (npc == null)
        {
            terminal.WriteLine("NPC not found.", "yellow");
            await Task.Delay(1000);
            return;
        }

        terminal.ClearScreen();
        terminal.WriteLine($"  Modifying: {npc.Name}", "bright_cyan");
        terminal.WriteLine($"  Level: {npc.Level} | HP: {npc.HP}/{npc.MaxHP} | Gold: {npc.Gold}");
        terminal.WriteLine($"  WeapPow: {npc.WeapPow} | ArmPow: {npc.ArmPow}");
        terminal.WriteLine("");
        terminal.WriteLine("  [1] Set Level");
        terminal.WriteLine("  [2] Set HP");
        terminal.WriteLine("  [3] Set Gold");
        terminal.WriteLine("  [4] Set Weapon Power");
        terminal.WriteLine("  [5] Set Armor Power");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Choice: ");
        var valueInput = await terminal.GetInput("New value: ");

        if (!int.TryParse(valueInput, out int value)) return;

        switch (choice)
        {
            case "1":
                npc.Level = Math.Clamp(value, 1, 100);
                break;
            case "2":
                npc.HP = value;
                break;
            case "3":
                npc.Gold = value;
                break;
            case "4":
                npc.WeapPow = value;
                break;
            case "5":
                npc.ArmPow = value;
                break;
        }

        terminal.WriteLine("NPC modified!", "green");
        await Task.Delay(1000);
    }

    #endregion

    #region World Controls

    private async Task WorldControls()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                      WORLD CONTROLS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  [1] Trigger World Simulation Tick");
        terminal.WriteLine("  [2] Clear All News");
        terminal.WriteLine("  [3] Generate Random News");
        terminal.WriteLine("  [4] View Auction House Stats");
        terminal.WriteLine("  [5] Clear Auction House");
        terminal.WriteLine("  [6] Force Save Game");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");

        switch (choice)
        {
            case "0": return;
            case "1":
                // WorldSimulator is managed by GameEngine, just show message
                terminal.WriteLine("World simulation runs automatically every 60 seconds.", "yellow");
                terminal.WriteLine("NPCs will perform activities on next tick.", "green");
                break;
            case "2":
                // NewsSystem doesn't have ClearAllNews - just inform user
                terminal.WriteLine("News is stored in files and resets daily.", "yellow");
                break;
            case "3":
                NewsSystem.Instance?.Newsy(false, "A mysterious stranger was seen wandering the streets.");
                NewsSystem.Instance?.Newsy(false, "The tavern is offering free drinks tonight!");
                NewsSystem.Instance?.Newsy(true, "BREAKING: Dragons spotted near the dungeon entrance!");
                terminal.WriteLine("Random news generated!", "green");
                break;
            case "4":
                var stats = MarketplaceSystem.Instance.GetStatistics();
                terminal.WriteLine($"\n  Auction House Statistics:", "white");
                terminal.WriteLine($"  Total Listings: {stats.TotalListings}");
                terminal.WriteLine($"  NPC Listings: {stats.NPCListings}");
                terminal.WriteLine($"  Player Listings: {stats.PlayerListings}");
                terminal.WriteLine($"  Total Value: {stats.TotalValue:N0} gold");
                await terminal.PressAnyKey();
                return;
            case "5":
                MarketplaceSystem.Instance.ClearAllListings();
                terminal.WriteLine("Auction House cleared!", "green");
                break;
            case "6":
                await SaveSystem.Instance.SaveGame(currentPlayer.Name, currentPlayer);
                terminal.WriteLine("Game saved!", "green");
                break;
        }

        await Task.Delay(1500);
    }

    #endregion

    #region Time Controls

    private async Task TimeControls()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                       TIME CONTROLS");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  Current Turn Count: {currentPlayer.TurnCount}");
        terminal.WriteLine($"  Days Played: ~{currentPlayer.TurnCount / 100}");
        terminal.WriteLine("");
        terminal.WriteLine("  [1] Advance 1 Day (100 turns)");
        terminal.WriteLine("  [2] Advance 7 Days (700 turns)");
        terminal.WriteLine("  [3] Advance 30 Days (3000 turns)");
        terminal.WriteLine("  [4] Set Turn Count");
        terminal.WriteLine("  [5] Reset Turn Count to 0");
        terminal.WriteLine("");
        terminal.WriteLine("  [0] Back");

        var choice = await terminal.GetInput("\nChoice: ");

        switch (choice)
        {
            case "0": return;
            case "1":
                currentPlayer.TurnCount += 100;
                terminal.WriteLine("Advanced 1 day (100 turns)!", "green");
                terminal.WriteLine("NPC activities will occur on next world simulation tick.", "gray");
                break;
            case "2":
                currentPlayer.TurnCount += 700;
                terminal.WriteLine("Advanced 7 days (700 turns)!", "green");
                terminal.WriteLine("NPC activities will occur on next world simulation tick.", "gray");
                break;
            case "3":
                currentPlayer.TurnCount += 3000;
                terminal.WriteLine("Advanced 30 days (3000 turns)!", "green");
                terminal.WriteLine("NPC activities will occur on next world simulation tick.", "gray");
                break;
            case "4":
                var turnInput = await terminal.GetInput("Set turn count to: ");
                if (int.TryParse(turnInput, out int turns))
                {
                    currentPlayer.TurnCount = Math.Max(0, turns);
                    terminal.WriteLine($"Turn count set to {currentPlayer.TurnCount}", "green");
                }
                break;
            case "5":
                currentPlayer.TurnCount = 0;
                terminal.WriteLine("Turn count reset to 0!", "green");
                break;
        }

        await Task.Delay(1500);
    }

    #endregion

    #region Quick Cheats

    private async Task GodMode()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                       * GOD MODE *");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  This will set ALL stats to maximum values!");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput("Are you sure? (Y/N): ");
        if (confirm.ToUpper() != "Y")
        {
            terminal.WriteLine("Cancelled.", "gray");
            await Task.Delay(1000);
            return;
        }

        // Max all stats
        currentPlayer.Level = GameConfig.MaxLevel;
        currentPlayer.Experience = 999999999;

        currentPlayer.Strength = currentPlayer.BaseStrength = 500;
        currentPlayer.Dexterity = currentPlayer.BaseDexterity = 500;
        currentPlayer.Constitution = currentPlayer.BaseConstitution = 500;
        currentPlayer.Intelligence = currentPlayer.BaseIntelligence = 500;
        currentPlayer.Wisdom = currentPlayer.BaseWisdom = 500;
        currentPlayer.Charisma = currentPlayer.BaseCharisma = 500;
        currentPlayer.Stamina = currentPlayer.BaseStamina = 500;
        currentPlayer.Agility = currentPlayer.BaseAgility = 500;

        currentPlayer.MaxHP = currentPlayer.BaseMaxHP = 9999;
        currentPlayer.HP = 9999;
        currentPlayer.MaxMana = currentPlayer.BaseMaxMana = 9999;
        currentPlayer.Mana = 9999;

        currentPlayer.Defence = currentPlayer.BaseDefence = 300;
        currentPlayer.WeapPow = 300;
        currentPlayer.ArmPow = 250;

        currentPlayer.Gold = 10000000;
        currentPlayer.BankGold = 100000000;
        currentPlayer.Healing = 99;

        currentPlayer.Fights = 999;
        currentPlayer.PFights = 99;
        currentPlayer.TFights = 99;

        // Cure everything
        currentPlayer.Blind = false;
        currentPlayer.Plague = false;
        currentPlayer.Smallpox = false;
        currentPlayer.Measles = false;
        currentPlayer.Leprosy = false;
        currentPlayer.LoversBane = false;
        currentPlayer.Poison = 0;
        currentPlayer.ActiveDrug = DrugType.None;
        currentPlayer.Addict = 0;

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("");
        terminal.WriteLine("  ╔═══════════════════════════════════════════════════════╗");
        terminal.WriteLine("  ║          YOU HAVE ASCENDED TO GODHOOD!               ║");
        terminal.WriteLine("  ╚═══════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("  All stats maximized. You are now unstoppable.");

        await Task.Delay(2000);
    }

    private async Task FullHeal()
    {
        currentPlayer.HP = currentPlayer.MaxHP;
        currentPlayer.Mana = currentPlayer.MaxMana;

        // Cure diseases
        currentPlayer.Blind = false;
        currentPlayer.Plague = false;
        currentPlayer.Smallpox = false;
        currentPlayer.Measles = false;
        currentPlayer.Leprosy = false;
        currentPlayer.LoversBane = false;
        currentPlayer.Poison = 0;

        // Clear negative status effects
        currentPlayer.ClearAllStatuses();

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("  * Fully healed and cured of all ailments! *");
        terminal.WriteLine($"  HP: {currentPlayer.HP}/{currentPlayer.MaxHP}");
        terminal.WriteLine($"  Mana: {currentPlayer.Mana}/{currentPlayer.MaxMana}");

        await Task.Delay(1500);
    }

    private async Task MaxGold()
    {
        currentPlayer.Gold = 10000000;
        currentPlayer.BankGold += 10000000;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine("  * 10,000,000 gold added! *");
        terminal.WriteLine($"  Gold in hand: {currentPlayer.Gold:N0}");
        terminal.WriteLine($"  Gold in bank: {currentPlayer.BankGold:N0}");

        await Task.Delay(1500);
    }

    private async Task LevelUp()
    {
        if (currentPlayer.Level >= GameConfig.MaxLevel)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  Already at maximum level!");
            await Task.Delay(1000);
            return;
        }

        int oldLevel = currentPlayer.Level;
        currentPlayer.Level++;

        // Give level-up bonuses
        int hpGain = 10 + (int)(currentPlayer.Constitution / 5);
        int manaGain = 5 + (int)(currentPlayer.Intelligence / 5);

        currentPlayer.MaxHP += hpGain;
        currentPlayer.BaseMaxHP += hpGain;
        currentPlayer.HP = currentPlayer.MaxHP;

        currentPlayer.MaxMana += manaGain;
        currentPlayer.BaseMaxMana += manaGain;
        currentPlayer.Mana = currentPlayer.MaxMana;

        // Stat gains
        currentPlayer.Strength += 2;
        currentPlayer.BaseStrength += 2;
        currentPlayer.Defence += 1;
        currentPlayer.BaseDefence += 1;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine($"  * LEVEL UP! {oldLevel} -> {currentPlayer.Level} *");
        terminal.WriteLine($"  HP: +{hpGain} -> {currentPlayer.MaxHP}");
        terminal.WriteLine($"  Mana: +{manaGain} -> {currentPlayer.MaxMana}");
        terminal.WriteLine($"  Strength: +2");
        terminal.WriteLine($"  Defence: +1");

        await Task.Delay(1500);
    }

    private async Task MaxLevel()
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine("  Raising to maximum level...");

        int levelsGained = GameConfig.MaxLevel - currentPlayer.Level;

        currentPlayer.Level = GameConfig.MaxLevel;

        // Calculate cumulative bonuses
        int totalHpGain = levelsGained * (10 + (int)(currentPlayer.Constitution / 5));
        int totalManaGain = levelsGained * (5 + (int)(currentPlayer.Intelligence / 5));

        currentPlayer.MaxHP += totalHpGain;
        currentPlayer.BaseMaxHP += totalHpGain;
        currentPlayer.HP = currentPlayer.MaxHP;

        currentPlayer.MaxMana += totalManaGain;
        currentPlayer.BaseMaxMana += totalManaGain;
        currentPlayer.Mana = currentPlayer.MaxMana;

        currentPlayer.Strength += levelsGained * 2;
        currentPlayer.BaseStrength += levelsGained * 2;
        currentPlayer.Defence += levelsGained;
        currentPlayer.BaseDefence += levelsGained;

        // Set experience to a high value
        currentPlayer.Experience = 999999999;

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("");
        terminal.WriteLine($"  * MAXIMUM LEVEL ACHIEVED: {currentPlayer.Level} *");
        terminal.WriteLine($"  HP: {currentPlayer.MaxHP:N0}");
        terminal.WriteLine($"  Mana: {currentPlayer.MaxMana:N0}");
        terminal.WriteLine($"  Strength: {currentPlayer.Strength}");
        terminal.WriteLine($"  Defence: {currentPlayer.Defence}");

        await Task.Delay(2000);
    }

    private async Task ResetCharacter()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    RESET CHARACTER");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  WARNING: This will reset your character to level 1!");
        terminal.WriteLine("  All stats will be reset to starting values.");
        terminal.WriteLine("  Gold and items will be preserved.");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput("Are you SURE? (Type YES to confirm): ");
        if (confirm.ToUpper() != "YES")
        {
            terminal.WriteLine("Cancelled.", "gray");
            await Task.Delay(1000);
            return;
        }

        currentPlayer.Level = 1;
        currentPlayer.Experience = 0;

        currentPlayer.Strength = currentPlayer.BaseStrength = 10;
        currentPlayer.Dexterity = currentPlayer.BaseDexterity = 10;
        currentPlayer.Constitution = currentPlayer.BaseConstitution = 10;
        currentPlayer.Intelligence = currentPlayer.BaseIntelligence = 10;
        currentPlayer.Wisdom = currentPlayer.BaseWisdom = 10;
        currentPlayer.Charisma = currentPlayer.BaseCharisma = 10;
        currentPlayer.Stamina = currentPlayer.BaseStamina = 10;
        currentPlayer.Agility = currentPlayer.BaseAgility = 10;

        currentPlayer.MaxHP = currentPlayer.BaseMaxHP = 50;
        currentPlayer.HP = 50;
        currentPlayer.MaxMana = currentPlayer.BaseMaxMana = 20;
        currentPlayer.Mana = 20;

        currentPlayer.Defence = currentPlayer.BaseDefence = 5;
        currentPlayer.WeapPow = 5;
        currentPlayer.ArmPow = 3;

        terminal.WriteLine("");
        terminal.WriteLine("  Character reset to level 1!", "yellow");

        await Task.Delay(2000);
    }

    private async Task ResetSteamStats()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                    STEAM STATS RESET (DEBUG)                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        if (!SteamIntegration.IsAvailable)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  Steam is not available.");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  Make sure you're running the game through Steam.");
            terminal.WriteLine("  (The Steam client must be running and this must be a Steam build.)");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine("  WARNING: This will reset all YOUR Steam stats and achievements!");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("  This affects ONLY your Steam account (the currently logged-in user).");
        terminal.WriteLine("  Other players' stats are not affected - Steam's security prevents that.");
        terminal.WriteLine("");
        terminal.WriteLine("  This includes:");
        terminal.SetColor("cyan");
        terminal.WriteLine("    - All 14 tracked Steam stats (monsters killed, gold earned, etc.)");
        terminal.WriteLine("    - All 47 Steam achievements linked to those stats");
        terminal.WriteLine("    - LOCAL achievements on current character (prevents re-sync)");
        terminal.WriteLine("    - LOCAL statistics on current character (prevents stat-trigger re-sync)");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        var confirm = await terminal.GetInput("  Type 'RESET' to confirm: ");

        if (confirm.Trim().ToUpper() == "RESET")
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("  Resetting Steam stats and achievements...");

            bool success = SteamIntegration.ResetAllStats(resetAchievements: true);

            if (success)
            {
                // Also clear LOCAL achievements to prevent SyncUnlockedToSteam from re-granting them
                if (currentPlayer?.Achievements != null)
                {
                    int localCount = currentPlayer.Achievements.UnlockedAchievements.Count;
                    currentPlayer.Achievements.UnlockedAchievements.Clear();
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  Cleared {localCount} local achievements to prevent re-sync.");
                }

                // Also reset LOCAL statistics to prevent stat-to-achievement auto-triggers
                if (currentPlayer?.Statistics != null)
                {
                    currentPlayer.Statistics.ResetAllStats();
                    terminal.SetColor("cyan");
                    terminal.WriteLine("  Reset local statistics to prevent stat-based achievement triggers.");
                }

                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine("  All Steam stats and achievements have been reset!");
                terminal.WriteLine("  Local achievements AND statistics cleared.");
                terminal.WriteLine("  Stat syncing DISABLED until game restart.");
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("  TO COMPLETE THE RESET:");
                terminal.WriteLine("  1. SAVE your game now (to persist zeroed stats to save file)");
                terminal.WriteLine("  2. Wait 5 seconds for Steam to sync the reset");
                terminal.WriteLine("  3. Quit the game");
                terminal.WriteLine("  4. Restart and load your save");
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("  NOTE: If achievements still return, try disabling Steam Cloud");
                terminal.WriteLine("        for this game in Steam settings while testing.");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine("  Failed to reset Steam stats. Check debug.log for details.");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine("  Reset cancelled.");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    #endregion

    #region Narrative Systems Debug

    private async Task NarrativeDebug()
    {
        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                    NARRATIVE SYSTEMS DEBUG                                   ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  VIEW STATE:");
            terminal.SetColor("cyan");
            terminal.WriteLine("    [1] View All Narrative System States");
            terminal.WriteLine("    [2] View Faction Standing Details");
            terminal.WriteLine("    [3] View Dream History");
            terminal.WriteLine("    [4] View Town NPC Story Progress");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  STRANGER/NOCTURA SYSTEM:");
            terminal.SetColor("cyan");
            terminal.WriteLine("    [5] Force Stranger Encounter (next dungeon visit)");
            terminal.WriteLine("    [6] Reset Stranger Encounters");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  FACTION SYSTEM:");
            terminal.SetColor("cyan");
            terminal.WriteLine("    [7] Join a Faction (bypass requirements)");
            terminal.WriteLine("    [8] Leave Current Faction");
            terminal.WriteLine("    [9] Adjust Faction Standing");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  DREAM SYSTEM:");
            terminal.SetColor("cyan");
            terminal.WriteLine("    [A] Queue Specific Dream");
            terminal.WriteLine("    [B] Force Dream on Next Rest");
            terminal.WriteLine("    [C] Clear Dream Queue");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  TOWN NPC STORIES:");
            terminal.SetColor("cyan");
            terminal.WriteLine("    [D] Advance NPC Story Stage");
            terminal.WriteLine("    [E] Reset NPC Story");
            terminal.WriteLine("    [F] Trigger Random NPC Event");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  CYCLE DIALOGUE:");
            terminal.SetColor("cyan");
            terminal.WriteLine("    [G] Set Story Cycle (1-4)");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("    [Q] Return to Dev Menu");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("  Choice: ");
            var upperChoice = choice.ToUpper().Trim();

            switch (upperChoice)
            {
                case "1": await ViewAllNarrativeStates(); break;
                case "2": await ViewFactionDetails(); break;
                case "3": await ViewDreamHistory(); break;
                case "4": await ViewTownNPCStories(); break;
                case "5": await ForceStrangerEncounter(); break;
                case "6": await ResetStrangerEncounters(); break;
                case "7": await ForceJoinFaction(); break;
                case "8": await LeaveFaction(); break;
                case "9": await AdjustFactionStanding(); break;
                case "A": await QueueSpecificDream(); break;
                case "B": await ForceDreamOnNextRest(); break;
                case "C": await ClearDreamQueue(); break;
                case "D": await AdvanceNPCStoryStage(); break;
                case "E": await ResetNPCStory(); break;
                case "F": await TriggerRandomNPCEvent(); break;
                case "G": await SetStoryCycle(); break;
                case "Q": return;
                default:
                    terminal.WriteLine("  Invalid option.", "red");
                    await Task.Delay(500);
                    break;
            }
        }
    }

    private async Task ViewAllNarrativeStates()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════ NARRATIVE SYSTEMS STATE ═══════════════════════");
        terminal.WriteLine("");

        // Stranger Encounter System
        terminal.SetColor("yellow");
        terminal.WriteLine("  ▶ STRANGER/NOCTURA SYSTEM:");
        terminal.SetColor("white");
        try
        {
            var strangerData = StrangerEncounterSystem.Instance.Serialize();
            terminal.WriteLine($"    Encounters Had: {strangerData.EncountersHad}");
            terminal.WriteLine($"    Player Suspects: {strangerData.PlayerSuspectsStranger}");
            terminal.WriteLine($"    Player Knows Truth: {strangerData.PlayerKnowsTruth}");
            terminal.WriteLine($"    Actions Since Last: {strangerData.ActionsSinceLastEncounter}");
            terminal.WriteLine($"    Disguises Seen: {strangerData.EncounteredDisguises?.Count ?? 0}");
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"    Error: {ex.Message}", "red");
        }
        terminal.WriteLine("");

        // Faction System
        terminal.SetColor("yellow");
        terminal.WriteLine("  ▶ FACTION SYSTEM:");
        terminal.SetColor("white");
        try
        {
            var factionData = FactionSystem.Instance.Serialize();
            var currentFactionName = factionData.PlayerFaction >= 0 ? ((Faction)factionData.PlayerFaction).ToString() : "None";
            terminal.WriteLine($"    Current Faction: {currentFactionName}");
            terminal.WriteLine($"    Rank: {factionData.FactionRank}");
            terminal.WriteLine($"    Reputation: {factionData.FactionReputation}");
            terminal.WriteLine($"    Completed Quests: {factionData.CompletedFactionQuests?.Count ?? 0}");
            terminal.WriteLine($"    Has Betrayed: {factionData.HasBetrayedFaction}");
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"    Error: {ex.Message}", "red");
        }
        terminal.WriteLine("");

        // Dream System
        terminal.SetColor("yellow");
        terminal.WriteLine("  ▶ DREAM SYSTEM:");
        terminal.SetColor("white");
        try
        {
            var experiencedDreams = DreamSystem.Instance.ExperiencedDreams;
            var dreamData = DreamSystem.Instance.Serialize();
            terminal.WriteLine($"    Dreams Experienced: {experiencedDreams.Count}");
            terminal.WriteLine($"    Total Dreams Available: {DreamSystem.Dreams.Count}");
            terminal.WriteLine($"    Rests Since Last Dream: {dreamData.RestsSinceLastDream}");
            if (experiencedDreams.Count > 0)
            {
                terminal.WriteLine($"    Recent: {string.Join(", ", experiencedDreams.TakeLast(3))}");
            }
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"    Error: {ex.Message}", "red");
        }
        terminal.WriteLine("");

        // Town NPC Story System
        terminal.SetColor("yellow");
        terminal.WriteLine("  ▶ TOWN NPC STORY SYSTEM:");
        terminal.SetColor("white");
        try
        {
            var npcStates = TownNPCStorySystem.Instance.NPCStates;
            var withProgress = npcStates.Count(s => s.Value.CurrentStage > 0);
            terminal.WriteLine($"    NPCs with Progress: {withProgress} / {npcStates.Count}");
            foreach (var kvp in npcStates.Where(s => s.Value.CurrentStage > 0).Take(5))
            {
                var npcData = TownNPCStorySystem.MemorableNPCs.GetValueOrDefault(kvp.Key);
                var name = npcData?.Name ?? kvp.Key;
                terminal.WriteLine($"      - {name}: Stage {kvp.Value.CurrentStage}");
            }
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"    Error: {ex.Message}", "red");
        }
        terminal.WriteLine("");

        // Cycle Dialogue System
        terminal.SetColor("yellow");
        terminal.WriteLine("  ▶ CYCLE/STORY PROGRESSION:");
        terminal.SetColor("white");
        try
        {
            var storySystem = StoryProgressionSystem.Instance;
            var cycleTitle = CycleDialogueSystem.Instance.GetCycleTitle();
            terminal.WriteLine($"    Current Cycle: {storySystem.CurrentCycle}");
            terminal.WriteLine($"    Cycle Title: {(string.IsNullOrEmpty(cycleTitle) ? "First Journey" : cycleTitle)}");
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"    Error: {ex.Message}", "red");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        await terminal.PressAnyKey("Press any key to continue...");
    }

    private async Task ViewFactionDetails()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════ FACTION DETAILS ═══════════════════════");
        terminal.WriteLine("");

        try
        {
            var factionSystem = FactionSystem.Instance;
            var factionData = factionSystem.Serialize();

            terminal.SetColor("yellow");
            var currentFactionName = factionData.PlayerFaction >= 0 ? ((Faction)factionData.PlayerFaction).ToString() : "None";
            terminal.WriteLine($"  Current Faction: {currentFactionName}");
            terminal.WriteLine($"  Rank: {factionData.FactionRank}");
            terminal.WriteLine($"  Reputation: {factionData.FactionReputation}");
            terminal.WriteLine("");

            // Show all factions and requirements
            terminal.SetColor("white");
            terminal.WriteLine("  Available Factions:");
            terminal.WriteLine("");

            foreach (Faction faction in Enum.GetValues(typeof(Faction)))
            {
                var (canJoin, reason) = factionSystem.CanJoinFaction(faction, currentPlayer);
                var statusColor = canJoin ? "green" : "red";
                var statusText = canJoin ? "✓ Eligible" : $"✗ {reason}";

                terminal.SetColor("cyan");
                terminal.Write($"    {faction}: ");
                terminal.SetColor(statusColor);
                terminal.WriteLine(statusText);
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("  Standing with each faction:");
            foreach (var kvp in factionData.FactionStanding)
            {
                var faction = (Faction)kvp.Key;
                terminal.WriteLine($"    {faction}: {kvp.Value}");
            }
            terminal.WriteLine($"  Completed Quests: {factionData.CompletedFactionQuests?.Count ?? 0}");

            if (factionData.CompletedFactionQuests?.Count > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine("  Completed Quest IDs:");
                foreach (var quest in factionData.CompletedFactionQuests.Take(10))
                {
                    terminal.WriteLine($"    - {quest}");
                }
            }
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error loading faction data: {ex.Message}", "red");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey("Press any key to continue...");
    }

    private async Task ViewDreamHistory()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════ DREAM HISTORY ═══════════════════════");
        terminal.WriteLine("");

        try
        {
            var dreamData = DreamSystem.Instance.Serialize();
            var experiencedDreams = DreamSystem.Instance.ExperiencedDreams;

            terminal.SetColor("yellow");
            terminal.WriteLine($"  Total Dreams Experienced: {experiencedDreams.Count}");
            terminal.WriteLine($"  Rests Since Last Dream: {dreamData.RestsSinceLastDream}");
            terminal.WriteLine($"  Total Dreams Available: {DreamSystem.Dreams.Count}");
            terminal.WriteLine("");

            if (experiencedDreams.Count > 0)
            {
                terminal.SetColor("white");
                terminal.WriteLine("  Dreams Experienced:");
                foreach (var dreamId in experiencedDreams.Take(20))
                {
                    var dream = DreamSystem.Dreams.FirstOrDefault(d => d.Id == dreamId);
                    var title = dream?.Title ?? dreamId;
                    terminal.WriteLine($"    - {title} ({dreamId})");
                }
                if (experiencedDreams.Count > 20)
                {
                    terminal.WriteLine($"    ... and {experiencedDreams.Count - 20} more");
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  No dreams experienced yet.");
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  Remaining Dreams: {DreamSystem.Dreams.Count - experiencedDreams.Count}");
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error loading dream data: {ex.Message}", "red");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey("Press any key to continue...");
    }

    private async Task ViewTownNPCStories()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════ TOWN NPC STORIES ═══════════════════════");
        terminal.WriteLine("");

        try
        {
            var npcStates = TownNPCStorySystem.Instance.NPCStates;
            var npcRelationships = TownNPCStorySystem.Instance.NPCRelationship;

            terminal.SetColor("yellow");
            terminal.WriteLine($"  Total Memorable NPCs: {TownNPCStorySystem.MemorableNPCs.Count}");
            terminal.WriteLine($"  NPCs with Progress: {npcStates.Count(s => s.Value.CurrentStage > 0)}");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  NPC Story Progress:");
            foreach (var kvp in npcStates)
            {
                var npcId = kvp.Key;
                var state = kvp.Value;
                var npcData = TownNPCStorySystem.MemorableNPCs.GetValueOrDefault(npcId);

                if (npcData == null) continue;

                var maxStages = npcData.StoryStages?.Length ?? 0;
                var relationship = npcRelationships.GetValueOrDefault(npcId, 0);

                terminal.SetColor("cyan");
                terminal.WriteLine($"    {npcData.Name} ({npcData.Title}):");
                terminal.SetColor("white");
                terminal.WriteLine($"      Location: {npcData.Location}");
                terminal.WriteLine($"      Stage: {state.CurrentStage} / {maxStages}");
                terminal.WriteLine($"      Completed Stages: {string.Join(", ", state.CompletedStages)}");
                terminal.WriteLine($"      Relationship: {relationship}");
                if (state.ChoicesMade.Count > 0)
                {
                    terminal.WriteLine($"      Choices Made: {string.Join(", ", state.ChoicesMade.Select(c => $"Stage{c.Key}:{c.Value}"))}");
                }
                terminal.WriteLine("");
            }
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error loading NPC story data: {ex.Message}", "red");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey("Press any key to continue...");
    }

    private async Task ForceStrangerEncounter()
    {
        terminal.WriteLine("");
        terminal.WriteLine("  Forcing stranger encounter on next dungeon visit...", "yellow");

        try
        {
            // Force encounter by setting actions counter high (min 20 required)
            var currentData = StrangerEncounterSystem.Instance.Serialize();
            currentData.ActionsSinceLastEncounter = 100;
            StrangerEncounterSystem.Instance.Deserialize(currentData);
            terminal.WriteLine("  ✓ Stranger encounter probability maximized!", "green");
            terminal.WriteLine("  (Next dungeon visit will likely trigger an encounter)", "gray");
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error: {ex.Message}", "red");
        }

        await Task.Delay(1500);
    }

    private async Task ResetStrangerEncounters()
    {
        terminal.WriteLine("");
        terminal.WriteLine("  Resetting all stranger encounter data...", "yellow");

        try
        {
            StrangerEncounterSystem.Instance.Deserialize(new StrangerEncounterData());
            terminal.WriteLine("  ✓ Stranger encounter data reset!", "green");
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error: {ex.Message}", "red");
        }

        await Task.Delay(1500);
    }

    private async Task ForceJoinFaction()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════ FORCE JOIN FACTION ═══════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  Available Factions:");
        var factions = Enum.GetValues(typeof(Faction)).Cast<Faction>().ToList();

        for (int i = 0; i < factions.Count; i++)
        {
            terminal.WriteLine($"    [{i + 1}] {factions[i]}");
        }
        terminal.WriteLine("    [Q] Cancel");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("  Choose faction: ");

        if (choice.ToUpper() == "Q") return;

        if (int.TryParse(choice, out int index) && index >= 1 && index <= factions.Count)
        {
            var faction = factions[index - 1];
            try
            {
                // Force join by directly manipulating save state
                var data = FactionSystem.Instance.Serialize();
                data.PlayerFaction = (int)faction;
                data.FactionRank = 1;
                data.FactionReputation = 100;
                FactionSystem.Instance.Deserialize(data);
                terminal.WriteLine($"  ✓ Force joined {faction}!", "green");
            }
            catch (Exception ex)
            {
                terminal.WriteLine($"  Error: {ex.Message}", "red");
            }
        }
        else
        {
            terminal.WriteLine("  Invalid choice.", "red");
        }

        await Task.Delay(1500);
    }

    private async Task LeaveFaction()
    {
        terminal.WriteLine("");

        try
        {
            var factionData = FactionSystem.Instance.Serialize();
            if (factionData.PlayerFaction < 0)
            {
                terminal.WriteLine("  You are not in any faction.", "yellow");
            }
            else
            {
                var oldFaction = (Faction)factionData.PlayerFaction;
                FactionSystem.Instance.LeaveFaction();
                terminal.WriteLine($"  ✓ Left {oldFaction}!", "green");
            }
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error: {ex.Message}", "red");
        }

        await Task.Delay(1500);
    }

    private async Task AdjustFactionStanding()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════ ADJUST FACTION STANDING ═══════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        var factionData = FactionSystem.Instance.Serialize();
        terminal.WriteLine("  Current standings:");
        foreach (var kvp in factionData.FactionStanding)
        {
            var faction = (Faction)kvp.Key;
            terminal.WriteLine($"    {faction}: {kvp.Value}");
        }
        terminal.WriteLine("");

        terminal.WriteLine("  Choose faction to modify:");
        var factions = Enum.GetValues(typeof(Faction)).Cast<Faction>().ToList();
        for (int i = 0; i < factions.Count; i++)
        {
            terminal.WriteLine($"    [{i + 1}] {factions[i]}");
        }
        terminal.WriteLine("    [Q] Cancel");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("  Choose faction: ");

        if (choice.ToUpper() == "Q") return;

        if (int.TryParse(choice, out int index) && index >= 1 && index <= factions.Count)
        {
            var faction = factions[index - 1];
            var input = await terminal.GetInput($"  Enter new standing for {faction} (-1000 to 1000): ");

            if (int.TryParse(input, out int newStanding))
            {
                newStanding = Math.Clamp(newStanding, -1000, 1000);
                try
                {
                    FactionSystem.Instance.ModifyReputation(faction, newStanding - (factionData.FactionStanding.GetValueOrDefault((int)faction, 0)));
                    terminal.WriteLine($"  ✓ {faction} standing set to {newStanding}!", "green");
                }
                catch (Exception ex)
                {
                    terminal.WriteLine($"  Error: {ex.Message}", "red");
                }
            }
            else
            {
                terminal.WriteLine("  Invalid value.", "red");
            }
        }
        else
        {
            terminal.WriteLine("  Invalid choice.", "red");
        }

        await Task.Delay(1500);
    }

    private async Task QueueSpecificDream()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════ EXPERIENCE DREAM ═══════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  Available Dreams:");
        terminal.SetColor("cyan");

        // Get all dream IDs from static Dreams list
        var dreamIds = DreamSystem.Dreams.Select(d => d.Id).ToList();
        var experienced = DreamSystem.Instance.ExperiencedDreams;

        for (int i = 0; i < Math.Min(dreamIds.Count, 20); i++) // Show first 20
        {
            var dreamId = dreamIds[i];
            var status = experienced.Contains(dreamId) ? " (experienced)" : "";
            terminal.WriteLine($"    [{i + 1}] {dreamId}{status}");
        }
        if (dreamIds.Count > 20)
        {
            terminal.WriteLine($"    ... and {dreamIds.Count - 20} more");
        }
        terminal.WriteLine("    [Q] Cancel");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("  Choose dream to mark as experienced: ");

        if (choice.ToUpper() == "Q") return;

        if (int.TryParse(choice, out int index) && index >= 1 && index <= dreamIds.Count)
        {
            var dreamId = dreamIds[index - 1];
            try
            {
                DreamSystem.Instance.ExperienceDream(dreamId);
                terminal.WriteLine($"  ✓ Marked dream as experienced: {dreamId}", "green");
            }
            catch (Exception ex)
            {
                terminal.WriteLine($"  Error: {ex.Message}", "red");
            }
        }
        else
        {
            terminal.WriteLine("  Invalid choice.", "red");
        }

        await Task.Delay(1500);
    }

    private async Task ForceDreamOnNextRest()
    {
        terminal.WriteLine("");
        terminal.WriteLine("  Forcing dream on next rest...", "yellow");

        try
        {
            // Force dream by setting rests counter high (need at least 2 rests normally)
            var dreamData = DreamSystem.Instance.Serialize();
            dreamData.RestsSinceLastDream = 10;
            DreamSystem.Instance.Deserialize(dreamData);
            terminal.WriteLine("  ✓ Dream probability maximized for next rest!", "green");
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error: {ex.Message}", "red");
        }

        await Task.Delay(1500);
    }

    private async Task ClearDreamQueue()
    {
        terminal.WriteLine("");
        terminal.WriteLine("  Clearing experienced dreams...", "yellow");

        try
        {
            // Reset dream history
            var dreamData = new DreamSaveData
            {
                ExperiencedDreams = new List<string>(),
                RestsSinceLastDream = 0
            };
            DreamSystem.Instance.Deserialize(dreamData);
            terminal.WriteLine("  ✓ Dream history cleared! All dreams available again.", "green");
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error: {ex.Message}", "red");
        }

        await Task.Delay(1500);
    }

    private async Task AdvanceNPCStoryStage()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════ ADVANCE NPC STORY ═══════════════════════");
        terminal.WriteLine("");

        try
        {
            var npcStates = TownNPCStorySystem.Instance.NPCStates;

            if (npcStates.Count == 0)
            {
                terminal.WriteLine("  No NPC stories initialized.", "yellow");
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("white");
            terminal.WriteLine("  NPC Stories:");
            var npcIds = npcStates.Keys.ToList();
            for (int i = 0; i < npcIds.Count; i++)
            {
                var npcId = npcIds[i];
                var state = npcStates[npcId];
                var npcData = TownNPCStorySystem.MemorableNPCs.GetValueOrDefault(npcId);
                var name = npcData?.Name ?? npcId;
                terminal.WriteLine($"    [{i + 1}] {name} (Stage {state.CurrentStage})");
            }
            terminal.WriteLine("    [Q] Cancel");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("  Choose NPC to advance: ");

            if (choice.ToUpper() == "Q") return;

            if (int.TryParse(choice, out int index) && index >= 1 && index <= npcIds.Count)
            {
                var npcId = npcIds[index - 1];
                var state = npcStates[npcId];
                // Advance by completing current stage
                TownNPCStorySystem.Instance.CompleteStage(npcId, state.CurrentStage);
                terminal.WriteLine($"  ✓ Advanced {npcId}'s story to stage {state.CurrentStage + 1}!", "green");
            }
            else
            {
                terminal.WriteLine("  Invalid choice.", "red");
            }
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error: {ex.Message}", "red");
        }

        await Task.Delay(1500);
    }

    private async Task ResetNPCStory()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════ RESET NPC STORY ═══════════════════════");
        terminal.WriteLine("");

        try
        {
            var npcStates = TownNPCStorySystem.Instance.NPCStates;

            if (npcStates.Count == 0)
            {
                terminal.WriteLine("  No NPC stories to reset.", "yellow");
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("white");
            terminal.WriteLine("  NPC Stories:");
            var npcIds = npcStates.Keys.ToList();
            for (int i = 0; i < npcIds.Count; i++)
            {
                var npcId = npcIds[i];
                var state = npcStates[npcId];
                var npcData = TownNPCStorySystem.MemorableNPCs.GetValueOrDefault(npcId);
                var name = npcData?.Name ?? npcId;
                terminal.WriteLine($"    [{i + 1}] {name} (Stage {state.CurrentStage})");
            }
            terminal.WriteLine("    [A] Reset ALL stories");
            terminal.WriteLine("    [Q] Cancel");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("  Choose NPC to reset: ");

            if (choice.ToUpper() == "Q") return;

            if (choice.ToUpper() == "A")
            {
                TownNPCStorySystem.Instance.Deserialize(new TownNPCStorySaveData());
                terminal.WriteLine("  ✓ All NPC stories reset!", "green");
            }
            else if (int.TryParse(choice, out int index) && index >= 1 && index <= npcIds.Count)
            {
                var npcId = npcIds[index - 1];
                // Reset by setting state back to initial
                npcStates[npcId] = new MemorableNPCState
                {
                    NPCId = npcId,
                    CurrentStage = 0,
                    CompletedStages = new HashSet<int>(),
                    ChoicesMade = new Dictionary<int, string>()
                };
                terminal.WriteLine($"  ✓ Reset {npcId}'s story!", "green");
            }
            else
            {
                terminal.WriteLine("  Invalid choice.", "red");
            }
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error: {ex.Message}", "red");
        }

        await Task.Delay(1500);
    }

    private async Task TriggerRandomNPCEvent()
    {
        terminal.WriteLine("");
        terminal.WriteLine("  Advancing all NPC stories by one stage...", "yellow");

        try
        {
            var npcStates = TownNPCStorySystem.Instance.NPCStates;
            int advanced = 0;
            foreach (var kvp in npcStates)
            {
                var npcId = kvp.Key;
                var state = kvp.Value;
                if (state.CurrentStage < 10) // Don't advance completed stories
                {
                    TownNPCStorySystem.Instance.CompleteStage(npcId, state.CurrentStage);
                    advanced++;
                }
            }
            terminal.WriteLine($"  ✓ Advanced {advanced} NPC stories!", "green");
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  Error: {ex.Message}", "red");
        }

        await Task.Delay(1500);
    }

    private async Task SetStoryCycle()
    {
        terminal.WriteLine("");
        terminal.SetColor("white");

        var currentCycle = StoryProgressionSystem.Instance.CurrentCycle;
        var cycleTitle = CycleDialogueSystem.Instance.GetCycleTitle();
        terminal.WriteLine($"  Current cycle: {currentCycle} ({(string.IsNullOrEmpty(cycleTitle) ? "First Journey" : cycleTitle)})");
        terminal.WriteLine("");
        terminal.WriteLine("  Cycle affects NPC dialogue awareness:");
        terminal.WriteLine("    1 = First playthrough (no deja vu)");
        terminal.WriteLine("    2 = Second cycle (subtle hints)");
        terminal.WriteLine("    3 = Third cycle (NPCs notice)");
        terminal.WriteLine("    4 = Fourth cycle (growing awareness)");
        terminal.WriteLine("    5+ = Full cycle awareness");
        terminal.WriteLine("");

        var input = await terminal.GetInput("  Enter new cycle number (1-10): ");

        if (int.TryParse(input, out int newCycle) && newCycle >= 1 && newCycle <= 10)
        {
            try
            {
                StoryProgressionSystem.Instance.CurrentCycle = newCycle;
                var newTitle = CycleDialogueSystem.Instance.GetCycleTitle();
                terminal.WriteLine($"  ✓ Cycle set to {newCycle}!", "green");
                if (!string.IsNullOrEmpty(newTitle))
                {
                    terminal.WriteLine($"  Title: {newTitle}", "cyan");
                }
            }
            catch (Exception ex)
            {
                terminal.WriteLine($"  Error: {ex.Message}", "red");
            }
        }
        else
        {
            terminal.WriteLine("  Invalid cycle number.", "red");
        }

        await Task.Delay(1500);
    }

    #endregion
}
