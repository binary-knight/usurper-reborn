using UsurperRemake.Utils;
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
        terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════════════════╗", "bright_cyan");
        terminal.WriteLine("║                    W E L C O M E   T O   U S U R P E R                       ║", "bright_cyan");
        terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝", "bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine("You are about to enter the medieval world of Usurper, a realm of", "white");
        terminal.WriteLine("magic, combat, politics, and intrigue. First, you must create", "white");
        terminal.WriteLine("your character - the persona you will inhabit in this world.", "white");
        terminal.WriteLine("");
        terminal.WriteLine("Your character's race and class will determine their abilities,", "white");
        terminal.WriteLine("strengths, and weaknesses. Choose wisely, as these decisions", "white");
        terminal.WriteLine("will shape your entire adventure!", "white");
        terminal.WriteLine("");
        
        await terminal.GetInputAsync("Press Enter to begin character creation...");
        
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
                terminal.WriteLine("Character creation was cancelled.", "yellow");
                terminal.WriteLine("You will need to create a character to play Usurper.", "white");
                terminal.WriteLine("");
                
                var retry = await terminal.GetInputAsync("Would you like to try again? (Y/N): ");
                if (retry.ToUpper() == "Y")
                {
                    await HandleCharacterCreation(player);
                    return;
                }
                else
                {
                    terminal.WriteLine("Goodbye!", "cyan");
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
            terminal.WriteLine("Character creation aborted.", "red");
            await ExitToMainMenu();
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"An error occurred during character creation: {ex.Message}", "red");
            terminal.WriteLine("Please try again.", "yellow");
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
        terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════════════════╗", "bright_green");
        terminal.WriteLine("║                     W E L C O M E   T O   T H E   R E A L M !                 ║", "bright_green");
        terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════════════════╝", "bright_green");
        terminal.WriteLine("");
        terminal.WriteLine($"Greetings, {player.Name2}!", "bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine($"You are now {GameConfig.RaceDescriptions[player.Race]},", "white");
        terminal.WriteLine($"a {GameConfig.ClassNames[(int)player.Class]} seeking fame and fortune", "white");
        terminal.WriteLine("in the medieval realm of Usurper.", "white");
        terminal.WriteLine("");
        terminal.WriteLine("Your adventure begins now in the bustling Main Street,", "cyan");
        terminal.WriteLine("where merchants, adventurers, and nobles gather.", "cyan");
        terminal.WriteLine("Explore the realm, gain experience, acquire wealth,", "cyan");
        terminal.WriteLine("and perhaps one day claim the throne!", "cyan");
        terminal.WriteLine("");
        terminal.WriteLine("═══ HELPFUL HINTS ═══", "yellow");
        terminal.WriteLine("- Visit the Inn to rest and recover", "white");
        terminal.WriteLine("- Check the Weapon and Armor shops for equipment", "white");
        terminal.WriteLine("- Enter the Dungeons to gain experience and gold", "white");
        terminal.WriteLine("- Use the Bank to store your wealth safely", "white");
        terminal.WriteLine("- Visit the Healer when wounded or diseased", "white");
        terminal.WriteLine("- The Temple offers spiritual guidance and services", "white");
        terminal.WriteLine("");
        terminal.WriteLine("Remember: Death is permanent in Usurper!", "red");
        terminal.WriteLine("Fight wisely and choose your battles carefully.", "red");
        terminal.WriteLine("");
        
        await terminal.GetInputAsync("Press Enter to enter the realm...");
    }
    
    /// <summary>
    /// Transfer player to Main Street to begin the game
    /// </summary>
    private async Task TransferToMainStreet(Character player)
    {
        terminal.Clear();
        terminal.WriteLine("");
        terminal.WriteLine("You step through the portal into the realm...", "cyan");
        terminal.WriteLine("");
        await Task.Delay(2000);
        
        terminal.WriteLine("The sights and sounds of Main Street greet you!", "green");
        terminal.WriteLine("Your adventure begins now!", "bright_green");
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
        terminal.WriteLine("Returning to main menu...", "cyan");
        await Task.Delay(1000);
        
        // This would typically return to the main game menu
        // For now, we'll just clear and show a message
        terminal.Clear();
        terminal.WriteLine("Please restart the game to try again.", "yellow");
    }
    
    public Task<bool> HandleInput(Character player, string input)
    {
        return Task.FromResult(true);
    }
    
    public void ShowLocationHeader(Character player)
    {
        terminal.WriteLine($"Welcome to {Name}, {player.DisplayName}!", "cyan");
        terminal.WriteLine(Description, "white");
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
