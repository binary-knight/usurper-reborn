using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Character Creation Location - New Player Creation Interface
/// Provides a location wrapper for the character creation system
/// </summary>
public class CharacterCreationLocation : BaseLocation
{
    private CharacterCreationSystem creationSystem = null!;
    private LocationManager locationManager = LocationManager.Instance;
    
    public CharacterCreationLocation() : base("Character Creation", GameLocation.NoWhere)
    {
        Description = "Welcome to the realm of Usurper! Create your character here.";
        ShortDescription = "New Player Creation";
    }
    
    public async Task EnterLocation(Character player)
    {
        await base.EnterLocation(player, TerminalEmulator.Instance ?? new TerminalEmulator());
        
        // Initialize creation system
        creationSystem = new CharacterCreationSystem(terminal);
        
        // Welcome message
        terminal.Clear();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("creation.welcome_to_usurper"), "bright_cyan", 79);
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("creation.intro_line1"), "white");
        terminal.WriteLine(Loc.Get("creation.intro_line2"), "white");
        terminal.WriteLine(Loc.Get("creation.intro_line3"), "white");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("creation.intro_line4"), "white");
        terminal.WriteLine(Loc.Get("creation.intro_line5"), "white");
        terminal.WriteLine(Loc.Get("creation.intro_line6"), "white");
        terminal.WriteLine("");

        await terminal.GetInputAsync(Loc.Get("creation.press_enter_begin"));
        
        // Start character creation process
        await HandleCharacterCreation(player);
    }
    
    private async Task HandleCharacterCreation(Character player)
    {
        try
        {
            // Use the player's Name1 (real name) for character creation
            var newCharacter = await creationSystem.CreateNewCharacter(player.Name1);
            
            if (newCharacter == null)
            {
                // Character creation was aborted
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("creation.cancelled"), "yellow");
                terminal.WriteLine(Loc.Get("creation.need_character"), "white");
                terminal.WriteLine("");

                var retry = await terminal.GetInputAsync(Loc.Get("creation.try_again_prompt"));
                if (retry.ToUpper() == "Y")
                {
                    await HandleCharacterCreation(player);
                    return;
                }
                else
                {
                    terminal.WriteLine(Loc.Get("creation.goodbye"), "cyan");
                    await ExitToMainMenu();
                    return;
                }
            }
            
            // Character creation successful - copy the new character data to the player
            CopyCharacterData(newCharacter, player);
            
            // Show welcome to the realm message
            await ShowWelcomeMessage(player);
            
            // Move to Main Street to begin the game
            await TransferToMainStreet(player);
        }
        catch (OperationCanceledException)
        {
            terminal.WriteLine(Loc.Get("creation.aborted"), "red");
            await ExitToMainMenu();
        }
        catch (Exception ex)
        {
            terminal.WriteLine(Loc.Get("creation.error_occurred", ex.Message), "red");
            terminal.WriteLine(Loc.Get("creation.please_try_again"), "yellow");
            await HandleCharacterCreation(player);
        }
    }
    
    /// <summary>
    /// Copy character creation data to the actual player object
    /// </summary>
    private void CopyCharacterData(Character source, Character target)
    {
        // Copy all relevant character data from creation to the actual player
        target.Name2 = source.Name2; // Game alias
        target.Sex = source.Sex;
        target.Race = source.Race;
        target.Class = source.Class;
        target.Age = source.Age;
        target.Height = source.Height;
        target.Weight = source.Weight;
        target.Eyes = source.Eyes;
        target.Hair = source.Hair;
        target.Skin = source.Skin;
        
        // Copy attributes
        target.HP = source.HP;
        target.MaxHP = source.MaxHP;
        target.Strength = source.Strength;
        target.Defence = source.Defence;
        target.Stamina = source.Stamina;
        target.Agility = source.Agility;
        target.Charisma = source.Charisma;
        target.Dexterity = source.Dexterity;
        target.Wisdom = source.Wisdom;
        target.Mana = source.Mana;
        target.MaxMana = source.MaxMana;
        
        // Copy starting resources and settings
        target.Gold = source.Gold;
        target.Experience = source.Experience;
        target.Level = source.Level;
        target.Healing = source.Healing;
        target.Fights = source.Fights;
        target.PFights = source.PFights;
        target.DarkNr = source.DarkNr;
        target.ChivNr = source.ChivNr;
        target.Loyalty = source.Loyalty;
        target.Mental = source.Mental;
        target.TFights = source.TFights;
        target.Thiefs = source.Thiefs;
        target.Brawls = source.Brawls;
        target.Assa = source.Assa;
        target.Trains = source.Trains;
        target.WeapHag = source.WeapHag;
        target.ArmHag = source.ArmHag;
        target.Resurrections = source.Resurrections;
        target.PickPocketAttempts = source.PickPocketAttempts;
        target.BankRobberyAttempts = source.BankRobberyAttempts;
        target.ID = source.ID;
        
        // Copy arrays
        target.Item = new List<int>(source.Item);
        target.ItemType = new List<ObjType>(source.ItemType);
        target.Phrases = new List<string>(source.Phrases);
        target.Description = new List<string>(source.Description);
        target.Spell = new List<List<bool>>();
        foreach (var spell in source.Spell)
        {
            target.Spell.Add(new List<bool>(spell));
        }
        target.Skill = new List<int>(source.Skill);
        target.Medal = new List<bool>(source.Medal);
        
        // Set character as allowed to play
        target.Allowed = true;
        target.Deleted = false;
        
        // Set offline location to dormitory (Pascal default)
        target.Location = GameConfig.OfflineLocationDormitory;
        
        // Set last on date
        target.LastOn = DateTimeOffset.Now.ToUnixTimeSeconds();
    }
    
    /// <summary>
    /// Show welcome message after character creation
    /// </summary>
    private async Task ShowWelcomeMessage(Character player)
    {
        terminal.Clear();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("creation.welcome_to_realm"), "bright_green", 79);
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("creation.greetings", player.Name2), "bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("creation.you_are_now", GameConfig.RaceDescriptions[player.Race]), "white");
        terminal.WriteLine(Loc.Get("creation.class_seeking", GameConfig.ClassNames[(int)player.Class]), "white");
        terminal.WriteLine(Loc.Get("creation.in_realm"), "white");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("creation.adventure_begins"), "cyan");
        terminal.WriteLine(Loc.Get("creation.merchants_gather"), "cyan");
        terminal.WriteLine(Loc.Get("creation.explore_realm"), "cyan");
        terminal.WriteLine(Loc.Get("creation.claim_throne"), "cyan");
        terminal.WriteLine("");
        if (IsScreenReader)
            terminal.WriteLine(Loc.Get("creation.helpful_hints_sr"), "yellow");
        else
            terminal.WriteLine(Loc.Get("creation.helpful_hints"), "yellow");
        terminal.WriteLine(Loc.Get("creation.hint_inn"), "white");
        terminal.WriteLine(Loc.Get("creation.hint_shops"), "white");
        terminal.WriteLine(Loc.Get("creation.hint_dungeons"), "white");
        terminal.WriteLine(Loc.Get("creation.hint_bank"), "white");
        terminal.WriteLine(Loc.Get("creation.hint_healer"), "white");
        terminal.WriteLine(Loc.Get("creation.hint_temple"), "white");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("creation.death_warning"), "red");
        terminal.WriteLine(Loc.Get("creation.fight_wisely"), "red");
        terminal.WriteLine("");

        await terminal.GetInputAsync(Loc.Get("creation.press_enter_realm"));
    }
    
    /// <summary>
    /// Transfer player to Main Street to begin the game
    /// </summary>
    private async Task TransferToMainStreet(Character player)
    {
        terminal.Clear();
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("creation.step_through_portal"), "cyan");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.WriteLine(Loc.Get("creation.main_street_greets"), "green");
        terminal.WriteLine(Loc.Get("creation.adventure_begins_now"), "bright_green");
        await Task.Delay(1500);
        
        // Exit to main street location
        await locationManager.ChangeLocation(player, GameLocation.MainStreet);
    }
    
    /// <summary>
    /// Exit back to main menu (for aborted creation)
    /// </summary>
    private async Task ExitToMainMenu()
    {
        terminal.Clear();
        terminal.WriteLine(Loc.Get("creation.returning_menu"), "cyan");
        await Task.Delay(1000);

        // This would typically return to the main game menu
        // For now, we'll just clear and show a message
        terminal.Clear();
        terminal.WriteLine(Loc.Get("creation.restart_game"), "yellow");
    }
    
    public Task<bool> HandleInput(Character player, string input)
    {
        return Task.FromResult(true);
    }
    
    public void ShowLocationHeader(Character player)
    {
        terminal.WriteLine(Loc.Get("creation.welcome_location", Name, player.DisplayName), "cyan");
        terminal.WriteLine(Loc.Get("creation.location_desc"), "white");
        terminal.WriteLine("");
    }
    
    public Task ShowMenu(Character player)
    {
        // No menu needed - character creation is automated
        return Task.CompletedTask;
    }
    
    public List<string> GetMenuOptions(Character player)
    {
        // No menu options - this is an automated process
        return new List<string>();
    }
} 
