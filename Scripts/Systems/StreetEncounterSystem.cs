using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.UI;

/// <summary>
/// Street Encounter System - Handles random encounters, PvP attacks, and street events
/// Based on Usurper's town encounter mechanics
/// </summary>
public class StreetEncounterSystem
{
    private static StreetEncounterSystem _instance;
    public static StreetEncounterSystem Instance => _instance ??= new StreetEncounterSystem();

    private Random _random = new Random();

    /// <summary>
    /// Encounter chance modifiers by location
    /// </summary>
    private static readonly Dictionary<GameLocation, float> LocationDangerLevel = new()
    {
        { GameLocation.MainStreet, 0.05f },      // 5% base chance
        { GameLocation.DarkAlley, 0.25f },       // 25% - Very dangerous
        { GameLocation.AuctionHouse, 0.08f },     // 8% - Pickpockets
        { GameLocation.TheInn, 0.10f },          // 10% - Brawlers
        { GameLocation.AnchorRoad, 0.15f },      // 15% - Dueling grounds
        { GameLocation.Dungeons, 0.0f },         // 0% - Handled by dungeon system
        { GameLocation.Castle, 0.02f },          // 2% - Guards intervene
        { GameLocation.Church, 0.01f },          // 1% - Sacred ground
        { GameLocation.Temple, 0.01f },          // 1% - Sacred ground
        { GameLocation.Bank, 0.03f },            // 3% - Guards present
        { GameLocation.Home, 0.0f },             // 0% - Safe zone
    };

    // Use TerminalEmulator wrapper methods for ITerminal compatibility
    private void TerminalWriteLine(TerminalEmulator terminal, string text) => terminal.WriteLine(text);
    private void TerminalWrite(TerminalEmulator terminal, string text) => terminal.Write(text);
    private void TerminalSetColor(TerminalEmulator terminal, string color) => terminal.SetColor(color);
    private void TerminalClear(TerminalEmulator terminal) => terminal.ClearScreen();
    private async Task<string> TerminalGetKeyInput(TerminalEmulator terminal) => await terminal.GetKeyInput();
    private async Task<string> TerminalGetInput(TerminalEmulator terminal, string prompt) => await terminal.GetInput(prompt);
    private async Task TerminalPressAnyKey(TerminalEmulator terminal) => await terminal.PressAnyKey();

    /// <summary>
    /// Types of street encounters
    /// </summary>
    public enum EncounterType
    {
        None,
        HostileNPC,           // NPC attacks player
        Pickpocket,           // Someone tries to steal
        Brawl,                // Tavern fight
        Challenge,            // NPC challenges to duel
        Mugging,              // Group attack
        GangEncounter,        // Enemy gang confrontation
        RomanticEncounter,    // NPC flirts/approaches
        MerchantEncounter,    // Traveling merchant
        BeggarEncounter,      // Beggar asks for gold
        RumorEncounter,       // Hear interesting gossip
        GuardPatrol,          // Guards question you
        Ambush,               // Pre-planned attack
        GrudgeConfrontation,  // Defeated NPC seeking revenge
        SpouseConfrontation,  // Suspicious spouse confronting player
        ThroneChallenge,      // Ambitious NPC challenges player king
        CityControlContest    // Rival team contests player's city control
    }

    /// <summary>
    /// Check for random encounter when entering a location
    /// </summary>
    public async Task<EncounterResult> CheckForEncounter(Character player, GameLocation location, TerminalEmulator terminal)
    {
        var result = new EncounterResult { EncounterOccurred = false };

        // Get base danger level for location
        float dangerLevel = LocationDangerLevel.GetValueOrDefault(location, 0.05f);

        // Modify based on time of day
        var hour = DateTime.Now.Hour;
        if (hour >= 22 || hour < 6) // Night time
        {
            dangerLevel *= 2.0f; // Double danger at night
        }

        // Modify based on player alignment
        if (player.Darkness > player.Chivalry + 50)
        {
            dangerLevel *= 1.5f; // Evil players attract more trouble
        }

        // Roll for encounter
        float roll = (float)_random.NextDouble();
        if (roll > dangerLevel)
        {
            return result; // No encounter
        }

        // Determine encounter type based on location
        var encounterType = DetermineEncounterType(player, location);
        if (encounterType == EncounterType.None)
        {
            return result;
        }

        result.EncounterOccurred = true;
        result.Type = encounterType;

        // Process the encounter
        await ProcessEncounter(player, encounterType, location, result, terminal);

        return result;
    }

    /// <summary>
    /// Determine what type of encounter occurs
    /// </summary>
    private EncounterType DetermineEncounterType(Character player, GameLocation location)
    {
        int roll = _random.Next(100);

        return location switch
        {
            GameLocation.DarkAlley => roll switch
            {
                < 30 => EncounterType.Mugging,
                < 50 => EncounterType.HostileNPC,
                < 65 => EncounterType.Pickpocket,
                < 75 => EncounterType.GangEncounter,
                < 85 => EncounterType.MerchantEncounter, // Shady merchant
                < 95 => EncounterType.RumorEncounter,
                _ => EncounterType.Ambush
            },

            GameLocation.TheInn => roll switch
            {
                < 40 => EncounterType.Brawl,
                < 55 => EncounterType.Challenge,
                < 70 => EncounterType.RumorEncounter,
                < 85 => EncounterType.RomanticEncounter,
                _ => EncounterType.HostileNPC
            },

            GameLocation.AuctionHouse => roll switch
            {
                < 40 => EncounterType.Pickpocket,
                < 60 => EncounterType.MerchantEncounter,
                < 75 => EncounterType.BeggarEncounter,
                < 90 => EncounterType.RumorEncounter,
                _ => EncounterType.HostileNPC
            },

            GameLocation.MainStreet => roll switch
            {
                < 25 => EncounterType.BeggarEncounter,
                < 45 => EncounterType.RumorEncounter,
                < 55 => EncounterType.Challenge,
                < 65 => EncounterType.MerchantEncounter,
                < 75 => EncounterType.GuardPatrol,
                < 85 => EncounterType.RomanticEncounter,
                _ => EncounterType.HostileNPC
            },

            GameLocation.AnchorRoad => roll switch
            {
                < 50 => EncounterType.Challenge,
                < 70 => EncounterType.HostileNPC,
                < 85 => EncounterType.GangEncounter,
                _ => EncounterType.Brawl
            },

            GameLocation.Castle => roll switch
            {
                < 50 => EncounterType.GuardPatrol,
                < 80 => EncounterType.RumorEncounter,
                _ => EncounterType.Challenge
            },

            _ => roll switch
            {
                < 30 => EncounterType.RumorEncounter,
                < 50 => EncounterType.BeggarEncounter,
                < 70 => EncounterType.MerchantEncounter,
                _ => EncounterType.HostileNPC
            }
        };
    }

    /// <summary>
    /// Process an encounter
    /// </summary>
    private async Task ProcessEncounter(Character player, EncounterType type, GameLocation location,
        EncounterResult result, TerminalEmulator terminal)
    {
        switch (type)
        {
            case EncounterType.HostileNPC:
                await ProcessHostileNPCEncounter(player, location, result, terminal);
                break;

            case EncounterType.Pickpocket:
                await ProcessPickpocketEncounter(player, result, terminal);
                break;

            case EncounterType.Brawl:
                await ProcessBrawlEncounter(player, result, terminal);
                break;

            case EncounterType.Challenge:
                await ProcessChallengeEncounter(player, location, result, terminal);
                break;

            case EncounterType.Mugging:
                await ProcessMuggingEncounter(player, location, result, terminal);
                break;

            case EncounterType.GangEncounter:
                await ProcessGangEncounter(player, result, terminal);
                break;

            case EncounterType.RomanticEncounter:
                await ProcessRomanticEncounter(player, result, terminal);
                break;

            case EncounterType.MerchantEncounter:
                await ProcessMerchantEncounter(player, location, result, terminal);
                break;

            case EncounterType.BeggarEncounter:
                await ProcessBeggarEncounter(player, result, terminal);
                break;

            case EncounterType.RumorEncounter:
                await ProcessRumorEncounter(player, result, terminal);
                break;

            case EncounterType.GuardPatrol:
                await ProcessGuardPatrolEncounter(player, result, terminal);
                break;

            case EncounterType.Ambush:
                await ProcessAmbushEncounter(player, location, result, terminal);
                break;
        }
    }

    /// <summary>
    /// Process hostile NPC encounter - They attack first
    /// </summary>
    private async Task ProcessHostileNPCEncounter(Character player, GameLocation location,
        EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         HOSTILE ENCOUNTER!                                   ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Find or create an attacker
        NPC attacker = FindHostileNPC(player, location);
        if (attacker == null)
        {
            attacker = CreateRandomHostileNPC(player.Level);
        }

        terminal.SetColor("red");
        terminal.WriteLine($"  {attacker.Name} blocks your path!");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  \"{GetHostilePhrase(attacker)}\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  Level {attacker.Level} {attacker.Class} - HP: {attacker.HP}/{attacker.MaxHP}");
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("F", "bright_yellow");
        terminal.Write("]ight  [", "white");
        terminal.Write("R", "bright_yellow");
        terminal.Write("]un  [", "white");
        terminal.Write("B", "bright_yellow");
        terminal.Write("]ribe  [", "white");
        terminal.Write("T", "bright_yellow");
        terminal.WriteLine("]alk", "white");
        terminal.WriteLine("");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        switch (choice)
        {
            case "F":
                await FightNPC(player, attacker, result, terminal);
                break;

            case "R":
                await AttemptFlee(player, attacker, result, terminal);
                break;

            case "B":
                await AttemptBribe(player, attacker, result, terminal);
                break;

            case "T":
                await AttemptTalk(player, attacker, result, terminal);
                break;

            default:
                // Default to fight if invalid input
                await FightNPC(player, attacker, result, terminal);
                break;
        }
    }

    /// <summary>
    /// Process pickpocket encounter
    /// </summary>
    private async Task ProcessPickpocketEncounter(Character player, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           PICKPOCKET!                                        ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Dexterity check to notice
        int noticeRoll = _random.Next(20) + 1;
        int dexMod = (int)(player.Dexterity - 10) / 2;
        bool noticed = noticeRoll + dexMod >= 12;

        if (noticed)
        {
            terminal.SetColor("green");
            terminal.WriteLine("  You feel a hand reaching for your coin purse!");
            terminal.WriteLine("");
            terminal.Write("  [", "white");
            terminal.Write("G", "bright_yellow");
            terminal.Write("]rab them  [", "white");
            terminal.Write("S", "bright_yellow");
            terminal.Write("]hout for guards  [", "white");
            terminal.Write("I", "bright_yellow");
            terminal.WriteLine("]gnore", "white");

            string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

            if (choice == "G")
            {
                // Create a low-level thief
                var thief = CreateRandomHostileNPC(Math.Max(1, player.Level - 3));
                thief.Class = CharacterClass.Assassin;
                thief.Name2 = "Pickpocket"; thief.Name1 = "Pickpocket";

                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine($"  You grab the {thief.Name}!");
                await Task.Delay(1000);

                await FightNPC(player, thief, result, terminal);
            }
            else if (choice == "S")
            {
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine("  \"Guards! Guards!\" you shout.");
                terminal.WriteLine("  The thief flees into the crowd.");
                result.Message = "Pickpocket scared away by guards.";
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("  You pretend not to notice. The thief slinks away.");
                result.Message = "Pickpocket encounter avoided.";
            }
        }
        else
        {
            // Failed to notice - they steal some gold
            long stolenAmount = Math.Min(player.Gold / 10, _random.Next(50, 200));
            if (stolenAmount > 0)
            {
                player.Gold -= stolenAmount;
                terminal.SetColor("red");
                terminal.WriteLine($"  Someone bumps into you on the street...");
                terminal.WriteLine($"  Later, you realize {stolenAmount} gold is missing!");
                result.GoldLost = stolenAmount;
                result.Message = $"Pickpocketed for {stolenAmount} gold!";
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  Someone bumps into you, but finds nothing to steal.");
                result.Message = "Pickpocket found nothing.";
            }
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Process tavern brawl encounter
    /// </summary>
    private async Task ProcessBrawlEncounter(Character player, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           TAVERN BRAWL!                                      ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        string[] brawlReasons = {
            "A drunk spills ale on you!",
            "Someone accuses you of cheating at dice!",
            "A patron insults your appearance!",
            "You're caught in the middle of a bar fight!",
            "A mercenary picks a fight with you!",
            "Someone claims you're sitting in their seat!"
        };

        terminal.SetColor("yellow");
        terminal.WriteLine($"  {brawlReasons[_random.Next(brawlReasons.Length)]}");
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("F", "bright_yellow");
        terminal.Write("]ight  [", "white");
        terminal.Write("D", "bright_yellow");
        terminal.Write("]uck and run  [", "white");
        terminal.Write("B", "bright_yellow");
        terminal.WriteLine("]uy them a drink", "white");
        terminal.WriteLine("");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "F")
        {
            // Create a brawler NPC
            var brawler = CreateRandomHostileNPC(player.Level);
            brawler.Class = CharacterClass.Warrior;
            string brawlerName = GetRandomBrawlerName();
            brawler.Name2 = brawlerName; brawler.Name1 = brawlerName;

            terminal.SetColor("red");
            terminal.WriteLine($"  {brawler.Name} squares up against you!");
            await Task.Delay(1000);

            await FightNPC(player, brawler, result, terminal, isBrawl: true);
        }
        else if (choice == "D")
        {
            int dexCheck = _random.Next(20) + 1 + (int)(player.Dexterity - 10) / 2;
            if (dexCheck >= 10)
            {
                terminal.SetColor("green");
                terminal.WriteLine("  You duck under a flying chair and escape the brawl!");
                result.Message = "Escaped tavern brawl.";
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("  You try to escape but get hit by a flying mug!");
                player.HP -= _random.Next(5, 15);
                if (player.HP < 1) player.HP = 1;
                result.Message = "Got hit escaping brawl.";
            }
        }
        else if (choice == "B")
        {
            if (player.Gold >= 20)
            {
                player.Gold -= 20;
                terminal.SetColor("green");
                terminal.WriteLine("  You buy a round of drinks and defuse the situation!");
                terminal.WriteLine("  The brawlers toast to your health!");
                result.GoldLost = 20;
                result.Message = "Bought drinks to avoid brawl.";
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("  You don't have enough gold! The brawl continues!");
                var brawler = CreateRandomHostileNPC(player.Level);
                brawler.Name2 = "Angry Drunk"; brawler.Name1 = "Angry Drunk";
                await FightNPC(player, brawler, result, terminal, isBrawl: true);
            }
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Process challenge encounter - NPC formally challenges player
    /// </summary>
    private async Task ProcessChallengeEncounter(Character player, GameLocation location,
        EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           CHALLENGE!                                         ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Find an NPC near player's level
        NPC challenger = FindChallengerNPC(player);
        if (challenger == null)
        {
            challenger = CreateRandomHostileNPC(player.Level);
        }

        terminal.SetColor("cyan");
        terminal.WriteLine($"  {challenger.Name} walks up to you, looking for a fight.");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  \"{GetChallengePhrase(challenger, player)}\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  {challenger.Name} - Level {challenger.Level} {challenger.Class}");
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("A", "bright_yellow");
        terminal.Write("]ccept challenge  [", "white");
        terminal.Write("D", "bright_yellow");
        terminal.Write("]ecline  [", "white");
        terminal.Write("I", "bright_yellow");
        terminal.WriteLine("]nsult them", "white");
        terminal.WriteLine("");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "A")
        {
            terminal.SetColor("green");
            terminal.WriteLine("  \"Let us fight with honor!\" you declare.");
            await Task.Delay(1000);
            await FightNPC(player, challenger, result, terminal, isHonorDuel: true);
        }
        else if (choice == "D")
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  \"Not today,\" you reply.");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {challenger.Name} scoffs but lets you pass.");

            // Declining hurts reputation slightly
            player.Fame = Math.Max(0, player.Fame - 5);
            result.Message = "Declined a challenge. (-5 Fame)";
        }
        else if (choice == "I")
        {
            terminal.SetColor("red");
            terminal.WriteLine("  You insult their honor!");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {challenger.Name}: \"You'll pay for that!\"");
            await Task.Delay(1000);

            // They attack with anger bonus
            challenger.Strength += 5;
            await FightNPC(player, challenger, result, terminal);
        }

        await Task.Delay(1500);
    }

    /// <summary>
    /// Process mugging encounter - Multiple attackers
    /// </summary>
    private async Task ProcessMuggingEncounter(Character player, GameLocation location,
        EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           AMBUSH!                                            ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        int muggerCount = _random.Next(2, 4);
        terminal.SetColor("red");
        terminal.WriteLine($"  {muggerCount} thugs emerge from the shadows!");
        terminal.SetColor("yellow");
        terminal.WriteLine("  \"Your gold or your life!\"");
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("F", "bright_yellow");
        terminal.Write("]ight  [", "white");
        terminal.Write("S", "bright_yellow");
        terminal.Write("]urrender gold  [", "white");
        terminal.Write("R", "bright_yellow");
        terminal.WriteLine("]un for it", "white");
        terminal.WriteLine("");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "F")
        {
            terminal.SetColor("red");
            terminal.WriteLine("  You draw your weapon and prepare to fight!");
            await Task.Delay(1000);

            // Create multiple monsters for multi-monster combat
            var muggers = new List<Monster>();
            for (int i = 0; i < muggerCount; i++)
            {
                int muggerLevel = Math.Max(1, player.Level - 2 + _random.Next(-1, 2));
                var mugger = Monster.CreateMonster(
                    nr: i + 1,
                    name: GetMuggerName(i),
                    hps: 20 + muggerLevel * 8,
                    strength: 8 + muggerLevel * 2,
                    defence: 5 + muggerLevel,
                    phrase: "Die!",
                    grabweap: false,
                    grabarm: false,
                    weapon: "Club",
                    armor: "Rags",
                    poisoned: false,
                    disease: false,
                    punch: 10 + muggerLevel,
                    armpow: 2,
                    weappow: 5 + muggerLevel
                );
                muggers.Add(mugger);
            }

            var combatEngine = new CombatEngine(terminal);
            var combatResult = await combatEngine.PlayerVsMonsters(player, muggers);

            if (combatResult.Outcome == CombatOutcome.Victory)
            {
                long loot = _random.Next(50, 150) * muggerCount;
                player.Gold += loot;
                terminal.SetColor("green");
                terminal.WriteLine($"  You defeated the muggers and found {loot} gold on their bodies!");
                result.GoldGained = loot;
                result.Message = $"Defeated {muggerCount} muggers!";
            }
            else
            {
                result.Message = "Lost to muggers...";
            }
        }
        else if (choice == "S")
        {
            long surrenderAmount = Math.Min(player.Gold, _random.Next(100, 300));
            player.Gold -= surrenderAmount;

            terminal.SetColor("yellow");
            terminal.WriteLine($"  You hand over {surrenderAmount} gold.");
            terminal.SetColor("gray");
            terminal.WriteLine("  The thugs take your gold and disappear into the shadows.");
            result.GoldLost = surrenderAmount;
            result.Message = $"Surrendered {surrenderAmount} gold to muggers.";
        }
        else if (choice == "R")
        {
            int escapeChance = 30 + (int)(player.Dexterity * 2);
            if (_random.Next(100) < escapeChance)
            {
                terminal.SetColor("green");
                terminal.WriteLine("  You sprint away and lose them in the streets!");
                result.Message = "Escaped from muggers.";
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("  They catch you and beat you badly!");

                int damage = _random.Next(20, 50);
                player.HP -= damage;
                if (player.HP < 1) player.HP = 1;

                long stolenGold = Math.Min(player.Gold, _random.Next(50, 200));
                player.Gold -= stolenGold;

                terminal.WriteLine($"  You take {damage} damage and lose {stolenGold} gold.");
                result.GoldLost = stolenGold;
                result.Message = "Caught by muggers!";
            }
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Process gang encounter
    /// </summary>
    private async Task ProcessGangEncounter(Character player, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                        GANG ENCOUNTER!                                       ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        bool playerHasTeam = !string.IsNullOrEmpty(player.Team);

        // Get an actual existing team from the world (not a made-up name)
        string gangName = "";
        string gangPassword = "";
        var activeTeams = WorldInitializerSystem.Instance.ActiveTeams;

        // Find a team that exists and has members, preferring teams not full
        var eligibleTeams = activeTeams?
            .Where(t => t.MemberNames.Count < GameConfig.MaxTeamMembers && t.MemberNames.Count > 0)
            .ToList();

        if (eligibleTeams != null && eligibleTeams.Count > 0)
        {
            var selectedTeam = eligibleTeams[_random.Next(eligibleTeams.Count)];
            gangName = selectedTeam.Name;

            // Get the team password from an actual member
            var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
            var teamMember = npcs?.FirstOrDefault(n => n.Team == gangName && !string.IsNullOrEmpty(n.TeamPW));
            gangPassword = teamMember?.TeamPW ?? Guid.NewGuid().ToString().Substring(0, 8);
        }
        else
        {
            // No eligible teams - create a fallback gang name but don't allow joining
            string[] fallbackNames = { "Shadow Blades", "Iron Fists", "Blood Ravens", "Night Wolves", "Storm Riders" };
            gangName = fallbackNames[_random.Next(fallbackNames.Length)];
        }

        terminal.SetColor("magenta");
        terminal.WriteLine($"  Members of the {gangName} block your path!");
        terminal.WriteLine("");

        if (playerHasTeam)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  \"We hear you're with {player.Team}...\"");
            terminal.WriteLine("  \"This is our territory!\"");
            terminal.WriteLine("");

            terminal.Write("  [", "white");
            terminal.Write("F", "bright_yellow");
            terminal.Write("]ight for territory  [", "white");
            terminal.Write("N", "bright_yellow");
            terminal.Write("]egotiate  [", "white");
            terminal.Write("L", "bright_yellow");
            terminal.WriteLine("]eave", "white");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  \"You look like you could use some friends...\"");
            terminal.WriteLine("");

            terminal.Write("  [", "white");
            terminal.Write("J", "bright_yellow");
            terminal.Write("]oin them  [", "white");
            terminal.Write("R", "bright_yellow");
            terminal.Write("]efuse  [", "white");
            terminal.Write("F", "bright_yellow");
            terminal.WriteLine("]ight", "white");
        }

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "F")
        {
            terminal.SetColor("red");
            terminal.WriteLine("  \"Wrong answer!\" they shout.");

            // Create gang leader
            var gangLeader = CreateRandomHostileNPC(player.Level + 2);
            gangLeader.Name2 = $"{gangName} Leader"; gangLeader.Name1 = gangLeader.Name2;

            await FightNPC(player, gangLeader, result, terminal);

            if (result.Victory)
            {
                player.Fame += 20;
                terminal.SetColor("green");
                terminal.WriteLine($"  Word spreads of your victory over the {gangName}!");
                terminal.WriteLine("  (+20 Fame)");
            }
        }
        else if (choice == "J" && !playerHasTeam)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  The gang looks you over...");
            await Task.Delay(1000);

            // Check if this is an actual existing team with members
            bool isRealTeam = eligibleTeams != null && eligibleTeams.Any(t => t.Name == gangName);

            if (player.Level >= 3 && isRealTeam)
            {
                terminal.SetColor("green");
                terminal.WriteLine($"  \"Welcome to the {gangName}!\"");

                // Properly join the team with password
                player.Team = gangName;
                player.TeamPW = gangPassword;
                player.CTurf = false;
                player.TeamRec = 0;

                // Update the team record
                var teamRecord = activeTeams?.FirstOrDefault(t => t.Name == gangName);
                if (teamRecord != null && !teamRecord.MemberNames.Contains(player.Name2))
                {
                    teamRecord.MemberNames.Add(player.Name2);
                }

                result.Message = $"Joined the {gangName}!";

                // Announce to news
                NewsSystem.Instance?.WriteTeamNews("Gang Recruitment!",
                    $"{GameConfig.NewsColorPlayer}{player.Name2}{GameConfig.NewsColorDefault} joined {GameConfig.NewsColorHighlight}{gangName}{GameConfig.NewsColorDefault}!");
            }
            else if (player.Level < 3)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  \"Come back when you're stronger, weakling!\"");
                result.Message = "Too weak to join gang.";
            }
            else
            {
                // Team doesn't actually exist - decline the invitation
                terminal.SetColor("yellow");
                terminal.WriteLine("  The gang members exchange looks and back away...");
                terminal.WriteLine("  \"Actually, we're not recruiting right now.\"");
                result.Message = "Gang decided not to recruit.";
            }
        }
        else if (choice == "N" || choice == "L" || choice == "R")
        {
            int charismaCheck = _random.Next(20) + 1 + (int)(player.Charisma - 10) / 2;
            if (charismaCheck >= 12 || choice == "L")
            {
                terminal.SetColor("green");
                terminal.WriteLine("  They let you pass... this time.");
                result.Message = "Avoided gang confrontation.";
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("  \"Nobody refuses us!\"");
                var gangMember = CreateRandomHostileNPC(player.Level);
                gangMember.Name2 = $"{gangName} Enforcer"; gangMember.Name1 = gangMember.Name2;
                await FightNPC(player, gangMember, result, terminal);
            }
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Process romantic encounter
    /// </summary>
    private async Task ProcessRomanticEncounter(Character player, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                        ROMANTIC ENCOUNTER                                    ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        string[] admirerNames = player.Sex == CharacterSex.Male ?
            new[] { "Lovely maiden", "Beautiful stranger", "Mysterious woman", "Charming lady" } :
            new[] { "Handsome stranger", "Dashing rogue", "Mysterious man", "Charming gentleman" };

        string admirer = admirerNames[_random.Next(admirerNames.Length)];

        terminal.SetColor("magenta");
        terminal.WriteLine($"  A {admirer} catches your eye and approaches...");
        terminal.SetColor("yellow");
        terminal.WriteLine("  \"Hey. Buy me a drink?\"");
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("Y", "bright_yellow");
        terminal.Write("]es  [", "white");
        terminal.Write("N", "bright_yellow");
        terminal.Write("]o thanks  [", "white");
        terminal.Write("F", "bright_yellow");
        terminal.WriteLine("]lirt back", "white");
        terminal.WriteLine("");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "Y" || choice == "F")
        {
            terminal.SetColor("magenta");
            terminal.WriteLine("  You spend a pleasant time together...");
            await Task.Delay(1500);

            // Random outcomes
            int outcome = _random.Next(100);
            if (outcome < 60)
            {
                terminal.SetColor("green");
                terminal.WriteLine("  You have a wonderful conversation and make a new friend!");
                player.Charisma = Math.Min(player.Charisma + 1, 30);
                result.Message = "Made a romantic connection. (+1 Charisma)";
            }
            else if (outcome < 80)
            {
                // They're actually a pickpocket
                long stolen = Math.Min(player.Gold / 5, _random.Next(20, 80));
                if (stolen > 0)
                {
                    player.Gold -= stolen;
                    terminal.SetColor("red");
                    terminal.WriteLine("  You wake up later to find your purse lighter...");
                    terminal.WriteLine($"  They stole {stolen} gold!");
                    result.GoldLost = stolen;
                    result.Message = "Romantic encounter was a scam!";
                }
            }
            else
            {
                // Genuine connection
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("  They give you a small token of affection.");
                player.Fame += 5;
                result.Message = "Romantic encounter. (+5 Fame)";
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  \"Perhaps another time,\" you say.");
            terminal.WriteLine("  They smile and walk away.");
            result.Message = "Declined romantic encounter.";
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Process merchant encounter
    /// </summary>
    private async Task ProcessMerchantEncounter(Character player, GameLocation location,
        EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                      TRAVELING MERCHANT                                      ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        bool shadyMerchant = location == GameLocation.DarkAlley;

        if (shadyMerchant)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  A cloaked figure beckons from the shadows...");
            terminal.SetColor("yellow");
            terminal.WriteLine("  \"Psst! Want to buy something... special?\"");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  A traveling merchant waves you over.");
            terminal.WriteLine("  \"Fine wares! Rare items! Best prices in town!\"");
        }
        terminal.WriteLine("");

        // Generate random items
        var items = GenerateMerchantItems(player.Level, shadyMerchant);

        for (int i = 0; i < items.Count; i++)
        {
            terminal.Write("  [", "white");
            terminal.Write($"{i + 1}", "bright_yellow");
            terminal.WriteLine($"] {items[i].Name} - {items[i].Price} gold", "white");
        }
        terminal.Write("  [", "white");
        terminal.Write("0", "bright_yellow");
        terminal.WriteLine("] No thanks", "white");
        terminal.WriteLine("");

        string choice = await terminal.GetInput("Buy which item? ");
        if (int.TryParse(choice, out int itemChoice) && itemChoice >= 1 && itemChoice <= items.Count)
        {
            var item = items[itemChoice - 1];
            if (player.Gold >= item.Price)
            {
                player.Gold -= item.Price;
                ApplyMerchantItem(player, item);

                terminal.SetColor("green");
                terminal.WriteLine($"  You purchase the {item.Name}!");
                result.GoldLost = item.Price;
                result.Message = $"Bought {item.Name} from merchant.";
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("  You don't have enough gold!");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  \"Come back anytime!\" the merchant calls.");
            result.Message = "Declined merchant's wares.";
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Process beggar encounter
    /// </summary>
    private async Task ProcessBeggarEncounter(Character player, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("gray");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           BEGGAR                                             ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("  A ragged beggar approaches you...");
        terminal.SetColor("yellow");
        terminal.WriteLine("  \"Please, kind adventurer, spare a few coins for the poor?\"");
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("G", "bright_yellow");
        terminal.Write("]ive gold (10)  [", "white");
        terminal.Write("L", "bright_yellow");
        terminal.Write("]arge donation (50)  [", "white");
        terminal.Write("I", "bright_yellow");
        terminal.Write("]gnore  [", "white");
        terminal.Write("R", "bright_yellow");
        terminal.WriteLine("]ob them", "white");
        terminal.WriteLine("");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "G" && player.Gold >= 10)
        {
            player.Gold -= 10;
            player.Chivalry += 5;
            terminal.SetColor("green");
            terminal.WriteLine("  The beggar thanks you profusely!");
            terminal.WriteLine("  (+5 Chivalry)");
            result.GoldLost = 10;
            result.Message = "Gave gold to beggar.";
        }
        else if (choice == "L" && player.Gold >= 50)
        {
            player.Gold -= 50;
            player.Chivalry += 20;
            terminal.SetColor("bright_green");
            terminal.WriteLine("  \"Bless you, kind soul!\" the beggar weeps with joy.");
            terminal.WriteLine("  (+20 Chivalry)");

            // Chance for a reward
            if (_random.Next(100) < 20)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("  The beggar hands you a strange amulet...");
                terminal.WriteLine("  \"This brought me luck once. May it serve you well.\"");
                // TODO: Add amulet to inventory
            }

            result.GoldLost = 50;
            result.Message = "Made large donation to beggar.";
        }
        else if (choice == "R")
        {
            player.Darkness += 15;
            player.Chivalry = Math.Max(0, player.Chivalry - 10);

            int foundGold = _random.Next(1, 10);
            player.Gold += foundGold;

            terminal.SetColor("red");
            terminal.WriteLine("  You rob the beggar of their meager possessions...");
            terminal.WriteLine($"  You find {foundGold} gold. (+15 Darkness, -10 Chivalry)");
            result.GoldGained = foundGold;
            result.Message = "Robbed a beggar.";
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You walk past without a word.");
            result.Message = "Ignored beggar.";
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Process rumor encounter
    /// </summary>
    private async Task ProcessRumorEncounter(Character player, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           RUMORS                                             ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("  You overhear an interesting conversation...");
        terminal.WriteLine("");

        string rumor = GetRandomRumor(player);
        terminal.SetColor("yellow");
        terminal.WriteLine($"  \"{rumor}\"");
        terminal.WriteLine("");

        result.Message = "Heard an interesting rumor.";
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Process guard patrol encounter
    /// </summary>
    private async Task ProcessGuardPatrolEncounter(Character player, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_white");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                        GUARD PATROL                                          ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  A patrol of town guards approaches...");

        bool wanted = player.Darkness > 100;

        // Crown faction members get a pass from the guards
        if (wanted && (FactionSystem.Instance?.HasGuardFavor() ?? false))
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("  The patrol leader squints at you, then notices your Crown insignia.");
            terminal.WriteLine("  \"...Move along. Crown business.\"");
            terminal.SetColor("gray");
            terminal.WriteLine("  The guards step aside without another word.");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        if (wanted)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  \"Halt! We've been looking for you!\"");
            terminal.SetColor("magenta");
            terminal.WriteLine($"  (Theyre looking for you — Darkness: {player.Darkness})");
            terminal.WriteLine("");

            terminal.Write("  [", "white");
            terminal.Write("S", "bright_yellow");
            terminal.Write("]urrender  [", "white");
            terminal.Write("F", "bright_yellow");
            terminal.Write("]ight  [", "white");
            terminal.Write("R", "bright_yellow");
            terminal.Write("]un  [", "white");
            terminal.Write("B", "bright_yellow");
            terminal.WriteLine("]ribe (100 gold)", "white");

            string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

            if (choice == "S")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("  The guards arrest you and take you to prison...");
                terminal.WriteLine("");

                // Confiscate some gold
                long confiscated = Math.Min(player.Gold, player.Gold / 4 + 50);
                if (confiscated > 0)
                {
                    player.Gold -= confiscated;
                    terminal.SetColor("red");
                    terminal.WriteLine($"  The guards confiscate {confiscated} gold!");
                }

                int sentence = GameConfig.DefaultPrisonSentence;
                terminal.SetColor("gray");
                terminal.WriteLine($"  You are sentenced to {sentence} day{(sentence == 1 ? "" : "s")} in prison.");
                await Task.Delay(2000);

                player.DaysInPrison = (byte)Math.Min(255, sentence);
                result.Message = "Arrested by guards!";
                throw new LocationExitException(GameLocation.Prison);
            }
            else if (choice == "F")
            {
                var guard = CreateRandomHostileNPC(player.Level + 3);
                guard.Name2 = "Town Guard Captain"; guard.Name1 = "Town Guard Captain";
                guard.Class = CharacterClass.Warrior;
                await FightNPC(player, guard, result, terminal);

                if (result.Victory)
                {
                    player.Darkness += 30;
                    terminal.SetColor("red");
                    terminal.WriteLine("  (+30 Darkness for attacking guards)");
                }
                else if (player.IsAlive)
                {
                    // Lost the fight but survived — arrested
                    terminal.SetColor("yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine("  The guards overpower you and clap you in irons!");
                    int sentence = GameConfig.DefaultPrisonSentence + 2; // Extra for resisting with violence
                    player.Darkness += 30;
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  You are sentenced to {sentence} days in prison for resisting arrest.");
                    await Task.Delay(2000);

                    player.DaysInPrison = (byte)Math.Min(255, sentence);
                    result.Message = "Defeated and arrested!";
                    throw new LocationExitException(GameLocation.Prison);
                }
            }
            else if (choice == "B" && player.Gold >= 100)
            {
                player.Gold -= 100;
                terminal.SetColor("green");
                terminal.WriteLine("  The guards pocket your gold and look the other way...");
                result.GoldLost = 100;
                result.Message = "Bribed guards.";
            }
            else if (choice == "R")
            {
                int escape = _random.Next(100);
                if (escape < 40 + player.Dexterity)
                {
                    terminal.SetColor("green");
                    terminal.WriteLine("  You escape into the crowd!");
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("  The guards catch you!");
                    terminal.WriteLine("");

                    // Extra day for resisting
                    int sentence = GameConfig.DefaultPrisonSentence + 1;
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  \"Running from the law, eh? That'll cost you extra!\"");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  You are sentenced to {sentence} days in prison.");
                    await Task.Delay(2000);

                    player.DaysInPrison = (byte)Math.Min(255, sentence);
                    result.Message = "Caught by guards!";
                    throw new LocationExitException(GameLocation.Prison);
                }
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  \"Stay out of trouble, citizen.\"");
            terminal.WriteLine("  The guards continue on their patrol.");
            result.Message = "Questioned by guards.";
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Process ambush encounter - pre-planned attack
    /// </summary>
    private async Task ProcessAmbushEncounter(Character player, GameLocation location,
        EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                        AMBUSH!                                               ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("red");
        terminal.WriteLine("  Assassins leap from the shadows!");
        terminal.SetColor("yellow");
        terminal.WriteLine("  \"Someone paid good gold to see you dead!\"");
        terminal.WriteLine("");

        // No choice - must fight
        var assassin = CreateRandomHostileNPC(player.Level + 1);
        assassin.Name2 = "Hired Assassin"; assassin.Name1 = "Hired Assassin";
        assassin.Class = CharacterClass.Assassin;

        // Assassin gets first strike
        int firstStrikeDamage = _random.Next(10, 25);
        player.HP -= firstStrikeDamage;

        terminal.SetColor("red");
        terminal.WriteLine($"  The assassin's first strike hits for {firstStrikeDamage} damage!");
        terminal.WriteLine("");
        await Task.Delay(1500);

        if (player.HP > 0)
        {
            await FightNPC(player, assassin, result, terminal);

            if (result.Victory)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine("  You find a note on the assassin's body...");
                terminal.WriteLine("  \"Contract: Eliminate the one called " + player.Name2 + "\"");
                terminal.WriteLine("  The signature is unreadable.");
            }
        }
        else
        {
            player.HP = 1; // Don't die from first strike
            await FightNPC(player, assassin, result, terminal);
        }

        await Task.Delay(2000);
    }

    // ======================== HELPER METHODS ========================

    /// <summary>
    /// Fight an NPC using the combat engine
    /// </summary>
    private async Task FightNPC(Character player, NPC npc, EncounterResult result, TerminalEmulator terminal,
        bool isBrawl = false, bool isHonorDuel = false)
    {
        // Convert NPC to Monster for combat engine
        // Pass NPC's level as the 'nr' parameter so the monster displays the correct level
        var monster = Monster.CreateMonster(
            nr: npc.Level,
            name: npc.Name,
            hps: (int)npc.HP,
            strength: (int)npc.Strength,
            defence: (int)npc.Defence,
            phrase: GetHostilePhrase(npc),
            grabweap: false,
            grabarm: false,
            weapon: GetRandomWeaponName(npc.Level),
            armor: GetRandomArmorName(npc.Level),
            poisoned: false,
            disease: false,
            punch: (int)(npc.Strength / 2),
            armpow: (int)npc.ArmPow,
            weappow: (int)npc.WeapPow
        );

        var combatEngine = new CombatEngine(terminal);
        var combatResult = await combatEngine.PlayerVsMonster(player, monster);

        result.Victory = combatResult.Outcome == CombatOutcome.Victory;

        if (result.Victory)
        {
            // Calculate rewards
            long expGain = npc.Level * 100 + _random.Next(50, 150);
            long goldGain = _random.Next(10, 50) * npc.Level;

            player.Experience += expGain;
            player.Gold += goldGain;

            if (isHonorDuel)
            {
                player.Fame += 15;
                result.Message = $"Won honor duel against {npc.Name}! (+{expGain} XP, +{goldGain} gold, +15 Fame)";
            }
            else if (isBrawl)
            {
                result.Message = $"Won tavern brawl! (+{expGain} XP)";
            }
            else
            {
                result.Message = $"Defeated {npc.Name}! (+{expGain} XP, +{goldGain} gold)";
            }

            result.ExperienceGained = expGain;
            result.GoldGained = goldGain;

            // Handle NPC death
            npc.HP = 0;

            // Record defeat memory on the real NPC for consequence encounters
            var realNpc = NPCSpawnSystem.Instance?.GetNPCByName(npc.Name2 ?? npc.Name);
            if (realNpc != null)
            {
                realNpc.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Defeated,
                    Description = $"Defeated in street combat by {player.Name2}",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.8f,
                    EmotionalImpact = -0.7f,
                    Location = "Street"
                });
            }

            // Check for bounty reward BEFORE calling OnNPCDefeated
            string npcNameForBounty = npc.Name ?? npc.Name2 ?? "";
            long bountyReward = QuestSystem.AutoCompleteBountyForNPC(player, npcNameForBounty);

            // Update quest progress (don't duplicate bounty processing)
            QuestSystem.OnNPCDefeated(player, npc);

            // Show bounty reward if any
            if (bountyReward > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  *** BOUNTY COLLECTED! +{bountyReward:N0} gold ***");
                result.GoldGained += bountyReward;
            }
        }
        else
        {
            result.Message = $"Lost to {npc.Name}...";
        }
    }

    /// <summary>
    /// Attempt to flee from an NPC
    /// </summary>
    private async Task AttemptFlee(Character player, NPC npc, EncounterResult result, TerminalEmulator terminal)
    {
        int fleeChance = 40 + (int)(player.Dexterity - npc.Dexterity) * 5;
        fleeChance = Math.Clamp(fleeChance, 10, 90);

        terminal.SetColor("yellow");
        terminal.WriteLine("  You try to run away...");
        await Task.Delay(1000);

        if (_random.Next(100) < fleeChance)
        {
            terminal.SetColor("green");
            terminal.WriteLine("  You escape successfully!");
            result.Message = "Fled from encounter.";
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {npc.Name} catches you!");
            terminal.WriteLine("  They attack while your back is turned!");

            // Take damage from failed flee
            int damage = _random.Next(10, 25);
            player.HP -= damage;
            terminal.WriteLine($"  You take {damage} damage!");

            await Task.Delay(1000);

            if (player.HP > 0)
            {
                await FightNPC(player, npc, result, terminal);
            }
        }
    }

    /// <summary>
    /// Attempt to bribe an NPC
    /// </summary>
    private async Task AttemptBribe(Character player, NPC npc, EncounterResult result, TerminalEmulator terminal)
    {
        long bribeAmount = npc.Level * 20 + _random.Next(20, 50);

        terminal.SetColor("yellow");
        terminal.WriteLine($"  \"How about {bribeAmount} gold and we forget this happened?\"");
        await Task.Delay(500);

        if (player.Gold < bribeAmount)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  You don't have enough gold!");
            terminal.WriteLine($"  {npc.Name} attacks!");
            await Task.Delay(1000);
            await FightNPC(player, npc, result, terminal);
            return;
        }

        terminal.Write("  [", "white");
        terminal.Write("Y", "bright_yellow");
        terminal.Write($"]es, pay {bribeAmount}  [", "white");
        terminal.Write("N", "bright_yellow");
        terminal.WriteLine("]o, fight instead", "white");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "Y")
        {
            int bribeChance = 50 + (int)(player.Charisma - 10) * 3;
            if (_random.Next(100) < bribeChance)
            {
                player.Gold -= bribeAmount;
                terminal.SetColor("green");
                terminal.WriteLine($"  {npc.Name} takes your gold and leaves.");
                result.GoldLost = bribeAmount;
                result.Message = $"Bribed {npc.Name} for {bribeAmount} gold.";
            }
            else
            {
                player.Gold -= bribeAmount;
                terminal.SetColor("red");
                terminal.WriteLine($"  {npc.Name} takes your gold and attacks anyway!");
                result.GoldLost = bribeAmount;
                await Task.Delay(1000);
                await FightNPC(player, npc, result, terminal);
            }
        }
        else
        {
            await FightNPC(player, npc, result, terminal);
        }
    }

    /// <summary>
    /// Attempt to talk down an NPC
    /// </summary>
    private async Task AttemptTalk(Character player, NPC npc, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("  You try to reason with them...");
        await Task.Delay(1000);

        int talkChance = 20 + (int)(player.Charisma - 10) * 4;
        if (player.Class == CharacterClass.Bard) talkChance += 20;

        if (_random.Next(100) < talkChance)
        {
            terminal.SetColor("green");
            terminal.WriteLine($"  {npc.Name} reconsiders and walks away.");
            result.Message = "Talked down hostile encounter.";
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {npc.Name} isn't interested in talking!");
            await Task.Delay(1000);
            await FightNPC(player, npc, result, terminal);
        }
    }

    /// <summary>
    /// Find a hostile NPC in the current location
    /// </summary>
    private NPC FindHostileNPC(Character player, GameLocation location)
    {
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (npcs == null || npcs.Count == 0) return null;

        // Get romantic partner IDs to exclude from hostile encounters
        var romanceTracker = RomanceTracker.Instance;
        var protectedIds = new HashSet<string>();
        if (romanceTracker != null)
        {
            foreach (var spouse in romanceTracker.Spouses)
                protectedIds.Add(spouse.NPCId);
            foreach (var lover in romanceTracker.CurrentLovers)
                protectedIds.Add(lover.NPCId);
        }

        // Find NPCs at this location who might be hostile (excluding romantic partners)
        var potentialEnemies = npcs
            .Where(n => n.IsAlive && n.Level >= player.Level - 5 && n.Level <= player.Level + 5)
            .Where(n => !protectedIds.Contains(n.ID)) // Never attack romantic partners
            .Where(n => n.Darkness > n.Chivalry || _random.Next(100) < 20) // Evil or random chance
            .ToList();

        if (potentialEnemies.Count > 0)
        {
            return potentialEnemies[_random.Next(potentialEnemies.Count)];
        }

        return null;
    }

    /// <summary>
    /// Find a challenger NPC
    /// </summary>
    private NPC FindChallengerNPC(Character player)
    {
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (npcs == null || npcs.Count == 0) return null;

        // Get romantic partner IDs to exclude from hostile encounters
        var romanceTracker = RomanceTracker.Instance;
        var protectedIds = new HashSet<string>();
        if (romanceTracker != null)
        {
            foreach (var spouse in romanceTracker.Spouses)
                protectedIds.Add(spouse.NPCId);
            foreach (var lover in romanceTracker.CurrentLovers)
                protectedIds.Add(lover.NPCId);
        }

        // Find NPCs near player's level who might challenge (excluding romantic partners)
        var potentialChallengers = npcs
            .Where(n => n.IsAlive && Math.Abs(n.Level - player.Level) <= 3)
            .Where(n => !protectedIds.Contains(n.ID)) // Romantic partners don't challenge to fights
            .ToList();

        if (potentialChallengers.Count > 0)
        {
            return potentialChallengers[_random.Next(potentialChallengers.Count)];
        }

        return null;
    }

    /// <summary>
    /// Create a random hostile NPC
    /// </summary>
    private NPC CreateRandomHostileNPC(int level)
    {
        level = Math.Max(1, level);

        string[] names = {
            "Street Thug", "Ruffian", "Cutthroat", "Brigand", "Footpad",
            "Rogue", "Bandit", "Highwayman", "Scoundrel", "Villain",
            "Desperado", "Outlaw", "Marauder", "Raider", "Prowler"
        };

        string selectedName = names[_random.Next(names.Length)];
        var npc = new NPC
        {
            Name1 = selectedName,
            Name2 = selectedName,
            Level = level,
            Class = (CharacterClass)_random.Next(1, 11),
            Race = (CharacterRace)_random.Next(1, 8),
            Sex = _random.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female,
            Darkness = _random.Next(20, 80), // Hostile NPCs have high darkness
        };

        // Generate stats based on level
        npc.MaxHP = 30 + level * 15 + _random.Next(level * 5);
        npc.HP = npc.MaxHP;
        npc.Strength = 10 + level * 2 + _random.Next(5);
        npc.Dexterity = 10 + level + _random.Next(5);
        npc.Constitution = 10 + level + _random.Next(5);
        npc.Intelligence = 8 + _random.Next(8);
        npc.Wisdom = 8 + _random.Next(8);
        npc.Charisma = 6 + _random.Next(6);
        npc.Defence = 5 + level * 2;
        npc.WeapPow = 5 + level * 3;
        npc.ArmPow = 3 + level * 2;

        // Equipment is handled by WeapPow/ArmPow stats already set
        return npc;
    }

    private string GetRandomWeaponName(int level)
    {
        if (level < 5) return new[] { "Rusty Knife", "Club", "Dagger", "Short Sword" }[_random.Next(4)];
        if (level < 10) return new[] { "Long Sword", "Mace", "Axe", "Rapier" }[_random.Next(4)];
        return new[] { "Bastard Sword", "War Hammer", "Battle Axe", "Katana" }[_random.Next(4)];
    }

    private string GetRandomArmorName(int level)
    {
        if (level < 5) return new[] { "Rags", "Leather Vest", "Padded Armor" }[_random.Next(3)];
        if (level < 10) return new[] { "Chain Shirt", "Scale Mail", "Studded Leather" }[_random.Next(3)];
        return new[] { "Chain Mail", "Plate Armor", "Full Plate" }[_random.Next(3)];
    }

    private string GetHostilePhrase(NPC npc)
    {
        string[] phrases = {
            "Your gold or your life!",
            "This is your last day!",
            "I'll cut you down!",
            "Prepare to die!",
            "Nobody escapes me!",
            "Time to bleed!",
            "Say your prayers!",
            "You picked the wrong street!",
            "I've been waiting for someone like you!",
            "End of the line for you!"
        };
        return phrases[_random.Next(phrases.Length)];
    }

    private string GetChallengePhrase(NPC challenger, Character player)
    {
        string[] phrases = {
            $"I challenge you, {player.Name2}! Let us see who is stronger!",
            "Ive heard about you. Fight me!",
            "They say youre tough. Lets see about that!",
            "Think youre tough? Lets find out!",
            "My sword needs blood. Youll do.",
            "Come on then. Unless youre scared."
        };
        return phrases[_random.Next(phrases.Length)];
    }

    private string GetRandomBrawlerName()
    {
        string[] names = {
            "Drunk Sailor", "Angry Patron", "Burly Mercenary", "Rowdy Barbarian",
            "Tavern Regular", "Off-duty Guard", "Gambling Loser", "Jealous Rival"
        };
        return names[_random.Next(names.Length)];
    }

    private string GetMuggerName(int index)
    {
        string[] names = { "Mugger", "Thug", "Brute", "Goon" };
        return names[index % names.Length];
    }

    private string GetRandomRumor(Character player)
    {
        // Get dynamic rumors based on game state
        var rumors = new List<string>
        {
            "They say the dungeons have gotten more dangerous lately...",
            "I heard the King is looking for brave adventurers.",
            "Strange creatures have been spotted near the Dark Alley.",
            "The temple priests are offering blessings to those who donate.",
            "There's a fortune to be made in monster hunting!",
            "The guild masters are always looking for new recruits.",
            "Watch your back in the alleys at night...",
            "The weapon shop just got a new shipment of fine blades.",
            "Some say there's a secret passage in the dungeons...",
            "The market traders have exotic goods from distant lands."
        };

        // Add NPC-specific rumors
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (npcs != null && npcs.Count > 0)
        {
            var randomNPC = npcs[_random.Next(npcs.Count)];
            rumors.Add($"I saw {randomNPC.Name} at the {randomNPC.CurrentLocation ?? "inn"} earlier.");
            rumors.Add($"They say {randomNPC.Name} is looking to form a team.");
        }

        return rumors[_random.Next(rumors.Count)];
    }

    private List<MerchantItem> GenerateMerchantItems(int playerLevel, bool shady)
    {
        var items = new List<MerchantItem>();

        if (shady)
        {
            // Shady merchant sells questionable items
            items.Add(new MerchantItem { Name = "Poison Vial", Price = 100, Type = "consumable" });
            items.Add(new MerchantItem { Name = "Lockpicks", Price = 50, Type = "tool" });
            items.Add(new MerchantItem { Name = "Smoke Bomb", Price = 75, Type = "consumable" });
            items.Add(new MerchantItem { Name = "Stolen Map", Price = 200, Type = "quest" });
        }
        else
        {
            // Normal traveling merchant
            items.Add(new MerchantItem { Name = "Healing Potion", Price = 50, Type = "consumable" });
            items.Add(new MerchantItem { Name = "Antidote", Price = 30, Type = "consumable" });
            items.Add(new MerchantItem { Name = "Travel Rations", Price = 20, Type = "consumable" });
            items.Add(new MerchantItem { Name = "Lucky Charm", Price = 150 + playerLevel * 10, Type = "accessory" });
        }

        return items;
    }

    private void ApplyMerchantItem(Character player, MerchantItem item)
    {
        switch (item.Name)
        {
            case "Healing Potion":
                player.Healing++;
                break;
            case "Antidote":
                // Add to inventory
                break;
            case "Lucky Charm":
                // Gives temporary luck boost - tracked via status effects
                player.Charisma = Math.Min(player.Charisma + 1, 30); // Minor stat boost
                break;
            // Other items...
        }
    }

    private struct MerchantItem
    {
        public string Name;
        public long Price;
        public string Type;
    }

    /// <summary>
    /// Attack a specific character in the current location
    /// </summary>
    public async Task<EncounterResult> AttackCharacter(Character player, Character target, TerminalEmulator terminal)
    {
        var result = new EncounterResult { EncounterOccurred = true, Type = EncounterType.HostileNPC };

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         ATTACK!                                              ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("red");
        terminal.WriteLine($"  You attack {target.Name2}!");
        terminal.WriteLine("");

        // Convert target to NPC if needed
        if (target is NPC npc)
        {
            await FightNPC(player, npc, result, terminal);
        }
        else
        {
            // Create temporary NPC from character
            var tempNPC = new NPC
            {
                Name1 = target.Name2,
                Name2 = target.Name2,
                Level = target.Level,
                HP = target.HP,
                MaxHP = target.MaxHP,
                Strength = target.Strength,
                Dexterity = target.Dexterity,
                Defence = target.Defence,
                WeapPow = target.WeapPow,
                ArmPow = target.ArmPow,
                Class = target.Class,
            };
            await FightNPC(player, tempNPC, result, terminal);
        }

        // Attacking someone increases darkness
        player.Darkness += 10;

        return result;
    }

    #region Consequence Encounters

    // Rate limiting for consequence encounters
    private static int _consequenceLocationChanges = 0;
    private static DateTime _lastConsequenceTime = DateTime.MinValue;

    /// <summary>
    /// Check for consequence encounters — NPCs retaliating for player wrongs.
    /// Called BEFORE random encounters in BaseLocation.LocationLoop().
    /// </summary>
    public async Task<EncounterResult> CheckForConsequenceEncounter(
        Character player, GameLocation location, TerminalEmulator terminal)
    {
        var result = new EncounterResult();
        _consequenceLocationChanges++;

        // Rate limiting
        if (_consequenceLocationChanges < GameConfig.MinMovesBetweenConsequences)
            return result;
        if ((DateTime.Now - _lastConsequenceTime).TotalMinutes < GameConfig.MinMinutesBetweenConsequences)
            return result;
        // Shared cooldown with petition system
        if ((DateTime.Now - NPCPetitionSystem.LastWorldEncounterTime).TotalMinutes < GameConfig.MinMinutesBetweenConsequences)
            return result;

        // Skip safe zones
        if (location == GameLocation.Home || location == GameLocation.Bank || location == GameLocation.Church)
            return result;

        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (npcs == null || npcs.Count == 0) return result;

        // Priority order: grudge (murder=guaranteed), jealous spouse, throne challenge, city contest
        bool hasMurderGrudge = HasMurderGrudge(player, npcs);
        if (hasMurderGrudge || _random.NextDouble() < GameConfig.GrudgeConfrontationChance)
        {
            var grudgeNpc = FindGrudgeNPC(player, npcs);
            if (grudgeNpc != null)
            {
                MarkConsequenceFired();
                await ExecuteGrudgeConfrontation(grudgeNpc, player, terminal, result);
                return result;
            }
        }

        if (_random.NextDouble() < GameConfig.SpouseConfrontationChance)
        {
            var jealousSpouse = FindJealousSpouse(player, npcs);
            if (jealousSpouse != null)
            {
                MarkConsequenceFired();
                await ExecuteSpouseConfrontation(jealousSpouse, player, terminal, result);
                return result;
            }
        }

        // NOTE: Throne challenges and city control contests are handled by the background
        // simulation (ChallengeSystem and CityControlSystem) rather than consequence encounters.
        // NPCs infiltrate the castle or contest turf through normal game mechanics.

        return result;
    }

    private void MarkConsequenceFired()
    {
        _consequenceLocationChanges = 0;
        _lastConsequenceTime = DateTime.Now;
        NPCPetitionSystem.LastWorldEncounterTime = DateTime.Now;
    }

    #region Find Consequence NPCs

    private NPC? FindGrudgeNPC(Character player, List<NPC> npcs)
    {
        // Prioritize murder grudges — the victim who respawned and wants revenge
        var murderGrudge = npcs.FirstOrDefault(npc =>
            !npc.IsDead && npc.IsAlive &&
            npc.Memory != null &&
            npc.Memory.HasMemoryOfEvent(MemoryType.Murdered, player.Name2, hoursAgo: 720) && // 30 days
            Math.Abs(npc.Level - player.Level) <= 15); // Wider level range for murder revenge

        if (murderGrudge != null) return murderGrudge;

        // Then check witness revenge — NPCs who saw the player murder someone
        var witnessGrudge = npcs.FirstOrDefault(npc =>
            !npc.IsDead && npc.IsAlive &&
            npc.Memory != null &&
            npc.Memory.GetCharacterImpression(player.Name2) <= -0.5f &&
            npc.Memory.HasMemoryOfEvent(MemoryType.SawDeath, player.Name2, hoursAgo: 336) && // 14 days
            Math.Abs(npc.Level - player.Level) <= 10);

        if (witnessGrudge != null) return witnessGrudge;

        // Standard grudge — defeated in combat
        return npcs.FirstOrDefault(npc =>
            !npc.IsDead && npc.IsAlive &&
            npc.Memory != null &&
            npc.Memory.GetCharacterImpression(player.Name2) <= -0.5f &&
            npc.Memory.HasMemoryOfEvent(MemoryType.Defeated, player.Name2, hoursAgo: 168) && // 7 days
            Math.Abs(npc.Level - player.Level) <= 10);
    }

    /// <summary>
    /// Check if an NPC has a murder grudge (for 100% encounter chance bypass)
    /// </summary>
    private bool HasMurderGrudge(Character player, List<NPC> npcs)
    {
        return npcs.Any(npc =>
            !npc.IsDead && npc.IsAlive &&
            npc.Memory != null &&
            (npc.Memory.HasMemoryOfEvent(MemoryType.Murdered, player.Name2, hoursAgo: 720) ||
             npc.Memory.HasMemoryOfEvent(MemoryType.SawDeath, player.Name2, hoursAgo: 336)) &&
            Math.Abs(npc.Level - player.Level) <= 15);
    }

    private NPC? FindJealousSpouse(Character player, List<NPC> npcs)
    {
        var affairs = NPCMarriageRegistry.Instance?.GetAllAffairs();
        if (affairs == null) return null;

        foreach (var affair in affairs)
        {
            if (affair.SpouseSuspicion < GameConfig.MinSuspicionForConfrontation) continue;
            if (affair.SeducerId != player.ID && affair.SeducerId != player.Name2) continue;

            // Find the married NPC's spouse
            var marriedNpc = npcs.FirstOrDefault(n => n.ID == affair.MarriedNpcId || n.Name2 == affair.MarriedNpcId);
            if (marriedNpc == null) continue;

            string spouseName = RelationshipSystem.GetSpouseName(marriedNpc);
            if (string.IsNullOrEmpty(spouseName)) continue;

            var spouse = NPCSpawnSystem.Instance?.GetNPCByName(spouseName);
            if (spouse != null && !spouse.IsDead && spouse.IsAlive)
                return spouse;
        }

        return null;
    }

    private NPC? FindThroneChallenger(Character player, List<NPC> npcs)
    {
        return npcs.FirstOrDefault(npc =>
            !npc.IsDead && npc.IsAlive &&
            npc.Level >= 15 &&
            npc.Brain?.Personality != null &&
            npc.Brain.Personality.Ambition > 0.6f);
    }

    private NPC? FindCityContestRival(Character player, List<NPC> npcs)
    {
        if (string.IsNullOrEmpty(player.Team)) return null;

        // Find NPC in a rival team
        return npcs.FirstOrDefault(npc =>
            !npc.IsDead && npc.IsAlive &&
            !string.IsNullOrEmpty(npc.Team) &&
            npc.Team != player.Team &&
            npc.Level >= 5);
    }

    #endregion

    #region Consequence Encounter Scenes

    private async Task ExecuteGrudgeConfrontation(NPC grudgeNpc, Character player,
        TerminalEmulator terminal, EncounterResult result)
    {
        result.EncounterOccurred = true;
        result.Type = EncounterType.GrudgeConfrontation;

        // Self-preservation: vastly outmatched NPCs may reconsider
        int levelGap = player.Level - grudgeNpc.Level;
        if (levelGap >= 8)
        {
            // Chance to back down: 10% per level above 7, capped at 80%
            int backDownChance = Math.Min(80, (levelGap - 7) * 10);
            if (_random.Next(100) < backDownChance)
            {
                terminal.ClearScreen();
                terminal.SetColor("gray");
                terminal.WriteLine($"  {grudgeNpc.Name2} steps out of the shadows, fists clenched...");
                terminal.WriteLine($"  ...then sees you clearly and hesitates.");
                terminal.SetColor("dark_yellow");
                terminal.WriteLine($"  The level {grudgeNpc.Level} {grudgeNpc.Class} thinks better of it and melts back into the crowd.");
                terminal.SetColor("gray");
                terminal.WriteLine($"  (Self-preservation overrides grudge — {levelGap} level gap)");
                await terminal.PressAnyKey();
                result.EncounterOccurred = false;
                return;
            }
        }

        // Determine grudge type for different dialogue and mechanics
        bool isMurderRevenge = grudgeNpc.Memory?.HasMemoryOfEvent(MemoryType.Murdered, player.Name2, hoursAgo: 720) == true;
        bool isWitnessRevenge = !isMurderRevenge &&
            grudgeNpc.Memory?.HasMemoryOfEvent(MemoryType.SawDeath, player.Name2, hoursAgo: 336) == true;

        // Find victim name for witness dialogue
        string victimName = "";
        if (isWitnessRevenge)
        {
            var witnessMemory = grudgeNpc.Memory?.GetMemoriesOfType(MemoryType.SawDeath)
                .FirstOrDefault(m => m.InvolvedCharacter == player.Name2);
            if (witnessMemory != null && witnessMemory.Description.Contains("murder "))
            {
                // Extract victim name from "Witnessed {player} murder {victim}"
                var parts = witnessMemory.Description.Split("murder ");
                if (parts.Length > 1) victimName = parts[1].Trim();
            }
        }

        terminal.ClearScreen();

        // Crown guard intervention — guards may rush to help Crown faction members
        float interventionChance = FactionSystem.Instance?.GetGuardInterventionChance() ?? 0f;
        if (interventionChance > 0 && _random.NextDouble() < interventionChance)
        {
            long guardDmg = grudgeNpc.MaxHP / 4;
            grudgeNpc.HP = Math.Max(1, grudgeNpc.HP - guardDmg);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  A royal guard rushes to your aid!");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  The guard strikes {grudgeNpc.Name2} for {guardDmg} damage before being pushed back.");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {grudgeNpc.Name2} is wounded but still standing. ({grudgeNpc.HP}/{grudgeNpc.MaxHP} HP)");
            terminal.WriteLine("");
        }

        if (isMurderRevenge)
        {
            // === MURDER REVENGE — Rage buff, no bribe/apologize ===
            // Apply rage buff
            grudgeNpc.Strength = (long)(grudgeNpc.Strength * (1.0f + GameConfig.MurderGrudgeRageBonusSTR));
            grudgeNpc.HP = (long)Math.Min(grudgeNpc.MaxHP * (1.0f + GameConfig.MurderGrudgeRageBonusHP), grudgeNpc.MaxHP * 1.5f);

            UIHelper.DrawBoxTop(terminal, "MURDER REVENGE!", "dark_red");
            UIHelper.DrawBoxEmpty(terminal, "dark_red");
            UIHelper.DrawBoxLine(terminal, $"  {grudgeNpc.Name2} emerges from the shadows, burning with fury.", "dark_red", "white");
            UIHelper.DrawBoxEmpty(terminal, "dark_red");
            UIHelper.DrawBoxLine(terminal, $"  \"You thought you could murder me and get away with it?!\"", "dark_red", "bright_red");
            UIHelper.DrawBoxLine(terminal, $"  \"I crawled back from death for THIS moment!\"", "dark_red", "bright_red");
            UIHelper.DrawBoxEmpty(terminal, "dark_red");
            UIHelper.DrawBoxLine(terminal, $"  Level {grudgeNpc.Level} {grudgeNpc.Class} — HP: {grudgeNpc.HP}/{grudgeNpc.MaxHP} (ENRAGED)", "dark_red", "bright_yellow");
            UIHelper.DrawBoxEmpty(terminal, "dark_red");
            UIHelper.DrawBoxSeparator(terminal, "dark_red");
            UIHelper.DrawMenuOption(terminal, "F", "Fight", "dark_red", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "R", "Run", "dark_red", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "dark_red");

            var choice = await terminal.GetInput("\n  Your response? ");

            if (choice.Trim().ToUpper() == "R")
            {
                int fleeChance = Math.Min(50, 20 + (int)(player.Dexterity * 1.5)); // Harder to flee murder revenge
                if (_random.Next(100) < fleeChance)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"\n  You barely escape {grudgeNpc.Name2}'s wrath!");
                }
                else
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"\n  {grudgeNpc.Name2} cuts off your escape! \"You're not getting away this time!\"");
                    await FightNPC(player, grudgeNpc, result, terminal);
                }
            }
            else
            {
                // Fight (default for any input)
                await FightNPC(player, grudgeNpc, result, terminal);
            }

            if (result.Victory)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"\n  {grudgeNpc.Name2} goes down again. So much for revenge.");
                grudgeNpc.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Defeated,
                    Description = $"Defeated again by {player.Name2} — murder revenge failed",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.6f,
                    EmotionalImpact = -0.5f
                });
                NewsSystem.Instance?.Newsy($"{player.Name2} defeated {grudgeNpc.Name2}'s murder revenge attempt!");
            }
            else
            {
                long goldTaken = player.Gold / 5; // Take 20% for murder revenge (more severe)
                player.Gold -= goldTaken;
                terminal.SetColor("dark_red");
                terminal.WriteLine($"\n  {grudgeNpc.Name2} stands over you, satisfied.");
                terminal.SetColor("red");
                terminal.WriteLine($"  \"Now we're even. But I won't forget.\"");
                terminal.SetColor("yellow");
                terminal.WriteLine($"  They take {goldTaken:N0} gold.");
                result.GoldLost = goldTaken;
                NewsSystem.Instance?.Newsy($"{grudgeNpc.Name2} got bloody revenge on {player.Name2}!");
            }
        }
        else if (isWitnessRevenge)
        {
            // === WITNESS REVENGE — Saw the player murder someone ===
            UIHelper.DrawBoxTop(terminal, "WITNESS CONFRONTATION!", "bright_red");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, $"  {grudgeNpc.Name2} steps in front of you, glaring.", "bright_red", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            if (!string.IsNullOrEmpty(victimName))
                UIHelper.DrawBoxLine(terminal, $"  \"I saw what you did to {victimName}. You'll answer for that.\"", "bright_red", "bright_cyan");
            else
                UIHelper.DrawBoxLine(terminal, $"  \"I saw what you did. Murderer. You'll answer for that.\"", "bright_red", "bright_cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, $"  Level {grudgeNpc.Level} {grudgeNpc.Class} — HP: {grudgeNpc.HP}/{grudgeNpc.MaxHP}", "bright_red", "yellow");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxSeparator(terminal, "bright_red");
            UIHelper.DrawMenuOption(terminal, "F", "Fight", "bright_red", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "B", $"Bribe ({grudgeNpc.Level * 50}g)", "bright_red", "bright_yellow", "yellow");
            UIHelper.DrawMenuOption(terminal, "R", "Run", "bright_red", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_red");

            var choice = await terminal.GetInput("\n  Your response? ");

            switch (choice.Trim().ToUpper())
            {
                case "F":
                    await FightNPC(player, grudgeNpc, result, terminal);
                    if (result.Victory)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  {grudgeNpc.Name2} falls. One less witness.");
                        player.Darkness += 10; // Extra darkness for silencing a witness
                        NewsSystem.Instance?.Newsy($"{player.Name2} defeated {grudgeNpc.Name2} who confronted them!");
                    }
                    else
                    {
                        long goldTaken = player.Gold / 10;
                        player.Gold -= goldTaken;
                        terminal.SetColor("red");
                        terminal.WriteLine($"\n  {grudgeNpc.Name2} takes {goldTaken:N0} gold. \"Justice is served.\"");
                        result.GoldLost = goldTaken;
                        NewsSystem.Instance?.Newsy($"{grudgeNpc.Name2} brought justice to {player.Name2}!");
                    }
                    break;

                case "B":
                    long witnessBribe = grudgeNpc.Level * 50;
                    if (player.Gold >= witnessBribe)
                    {
                        int bribeChance = Math.Min(60, 30 + (int)(player.Charisma * 2)); // Harder to bribe witnesses
                        if (_random.Next(100) < bribeChance)
                        {
                            player.Gold -= witnessBribe;
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"\n  {grudgeNpc.Name2} takes the {witnessBribe} gold and looks away.");
                            terminal.SetColor("gray");
                            terminal.WriteLine($"  \"I didn't see anything. But my memory might come back...\"");
                            result.GoldLost = witnessBribe;
                        }
                        else
                        {
                            player.Gold -= witnessBribe;
                            terminal.SetColor("bright_red");
                            terminal.WriteLine($"\n  \"You think gold will buy my silence?!\" They take it AND attack!");
                            result.GoldLost = witnessBribe;
                            await FightNPC(player, grudgeNpc, result, terminal);
                        }
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"\n  Not enough gold! {grudgeNpc.Name2} attacks!");
                        await FightNPC(player, grudgeNpc, result, terminal);
                    }
                    break;

                default: // Run
                    int fleeChance = Math.Min(65, 25 + (int)(player.Dexterity * 2));
                    if (_random.Next(100) < fleeChance)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  You slip away from {grudgeNpc.Name2}. For now...");
                    }
                    else
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"\n  {grudgeNpc.Name2} catches you! \"Running makes you look guilty!\"");
                        await FightNPC(player, grudgeNpc, result, terminal);
                    }
                    break;
            }
        }
        else
        {
            // === STANDARD GRUDGE — Defeated in combat ===
            UIHelper.DrawBoxTop(terminal, "GRUDGE CONFRONTATION!", "bright_red");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, $"  {grudgeNpc.Name2} is waiting for you. Doesnt look happy.", "bright_red", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, $"  \"You thought I'd forget what you did to me? Think again.\"", "bright_red", "bright_cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, $"  Level {grudgeNpc.Level} {grudgeNpc.Class} — HP: {grudgeNpc.HP}/{grudgeNpc.MaxHP}", "bright_red", "yellow");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxSeparator(terminal, "bright_red");
            UIHelper.DrawMenuOption(terminal, "F", "Fight", "bright_red", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "A", "Apologize", "bright_red", "bright_yellow", "white");
            long bribeCost = grudgeNpc.Level * 30;
            UIHelper.DrawMenuOption(terminal, "B", $"Bribe ({bribeCost}g)", "bright_red", "bright_yellow", "yellow");
            UIHelper.DrawMenuOption(terminal, "R", "Run", "bright_red", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_red");

            var choice = await terminal.GetInput("\n  Your response? ");

            switch (choice.ToUpper())
            {
                case "F": // Fight
                    await FightNPC(player, grudgeNpc, result, terminal);
                    if (result.Victory)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  {grudgeNpc.Name2} goes down. That settles that.");
                        grudgeNpc.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.Defeated,
                            Description = $"Defeated again by {player.Name2} — grudge settled",
                            InvolvedCharacter = player.Name2,
                            Importance = 0.5f,
                            EmotionalImpact = -0.3f
                        });
                        NewsSystem.Instance?.Newsy($"{player.Name2} defeated {grudgeNpc.Name2} in a grudge match!");
                    }
                    else
                    {
                        long goldTaken = player.Gold / 10;
                        player.Gold -= goldTaken;
                        terminal.SetColor("red");
                        terminal.WriteLine($"\n  {grudgeNpc.Name2} takes {goldTaken} gold and walks away satisfied.");
                        result.GoldLost = goldTaken;
                        NewsSystem.Instance?.Newsy($"{grudgeNpc.Name2} got revenge on {player.Name2}!");
                    }
                    break;

                case "A": // Apologize
                    int apologyChance = Math.Min(75, 30 + (int)(player.Charisma * 2));
                    if (_random.Next(100) < apologyChance)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"\n  {grudgeNpc.Name2} hesitates... then lowers their fists.");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  \"Fine. But don't cross me again.\"");
                        grudgeNpc.Memory?.RecordEvent(new MemoryEvent
                        {
                            Type = MemoryType.SocialInteraction,
                            Description = $"{player.Name2} apologized sincerely",
                            InvolvedCharacter = player.Name2,
                            Importance = 0.6f,
                            EmotionalImpact = 0.3f
                        });
                        player.Darkness = Math.Max(0, player.Darkness - 5);
                    }
                    else
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"\n  \"SORRY doesn't cut it!\"");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {grudgeNpc.Name2} attacks with fury! (+15% STR)");
                        grudgeNpc.Strength = (long)(grudgeNpc.Strength * 1.15);
                        await FightNPC(player, grudgeNpc, result, terminal);
                    }
                    break;

                case "B": // Bribe
                    if (player.Gold >= bribeCost)
                    {
                        int bribeChance = Math.Min(80, 60 + (int)(player.Charisma * 2));
                        if (_random.Next(100) < bribeChance)
                        {
                            player.Gold -= bribeCost;
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"\n  {grudgeNpc.Name2} pockets the {bribeCost} gold.");
                            terminal.SetColor("white");
                            terminal.WriteLine($"  \"We're even. For now.\"");
                            result.GoldLost = bribeCost;
                            grudgeNpc.Memory?.RecordEvent(new MemoryEvent
                            {
                                Type = MemoryType.GainedGold,
                                Description = $"{player.Name2} paid off their debt",
                                InvolvedCharacter = player.Name2,
                                Importance = 0.5f,
                                EmotionalImpact = 0.2f
                            });
                        }
                        else
                        {
                            player.Gold -= bribeCost;
                            terminal.SetColor("bright_red");
                            terminal.WriteLine($"\n  {grudgeNpc.Name2} takes the gold AND attacks!");
                            result.GoldLost = bribeCost;
                            await FightNPC(player, grudgeNpc, result, terminal);
                        }
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"\n  You don't have enough gold! {grudgeNpc.Name2} attacks!");
                        await FightNPC(player, grudgeNpc, result, terminal);
                    }
                    break;

                default: // Run
                    int fleeChance = Math.Min(75, 30 + (int)(player.Dexterity * 2));
                    if (_random.Next(100) < fleeChance)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"\n  You slip away before {grudgeNpc.Name2} can react!");
                    }
                    else
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"\n  {grudgeNpc.Name2} catches you! They get the first strike!");
                        await FightNPC(player, grudgeNpc, result, terminal);
                    }
                    break;
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task ExecuteSpouseConfrontation(NPC spouse, Character player,
        TerminalEmulator terminal, EncounterResult result)
    {
        result.EncounterOccurred = true;
        result.Type = EncounterType.SpouseConfrontation;

        // Self-preservation: vastly outmatched spouse may confront verbally but not fight
        int spouseLevelGap = player.Level - spouse.Level;
        if (spouseLevelGap >= 8)
        {
            int backDownChance = Math.Min(80, (spouseLevelGap - 7) * 10);
            if (_random.Next(100) < backDownChance)
            {
                terminal.ClearScreen();
                terminal.SetColor("gray");
                terminal.WriteLine($"  {spouse.Name2} storms toward you, face twisted with anger...");
                terminal.WriteLine($"  ...then stops short, remembering who they're dealing with.");
                terminal.SetColor("dark_yellow");
                terminal.WriteLine($"  \"This isn't over,\" they hiss, retreating to find another way.");
                await terminal.PressAnyKey();
                result.EncounterOccurred = false;
                return;
            }
        }

        // Find who the player is having an affair with (the spouse's partner)
        string partnerName = RelationshipSystem.GetSpouseName(spouse);

        terminal.ClearScreen();
        UIHelper.DrawBoxTop(terminal, "JEALOUS SPOUSE!", "bright_red");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxLine(terminal, $"  {spouse.Name2} is standing right in front of you. Fists clenched.", "bright_red", "white");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxLine(terminal, $"  \"I know what you've been doing with {partnerName}.\"", "bright_red", "bright_cyan");
        UIHelper.DrawBoxLine(terminal, $"  \"Did you think I wouldn't find out?\"", "bright_red", "cyan");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxSeparator(terminal, "bright_red");
        UIHelper.DrawMenuOption(terminal, "D", "Deny everything", "bright_red", "bright_yellow", "white");
        UIHelper.DrawMenuOption(terminal, "A", "Admit and apologize", "bright_red", "bright_yellow", "white");
        UIHelper.DrawMenuOption(terminal, "T", "Taunt them", "bright_red", "bright_yellow", "red");
        UIHelper.DrawMenuOption(terminal, "F", "Fight", "bright_red", "bright_yellow", "white");
        UIHelper.DrawBoxBottom(terminal, "bright_red");

        var choice = await terminal.GetInput("\n  Your response? ");

        switch (choice.ToUpper())
        {
            case "D": // Deny
                int denyChance = Math.Min(75, 40 + (int)(player.Charisma * 2));
                if (_random.Next(100) < denyChance)
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"\n  \"I don't know what you're talking about. We're just friends.\"");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {spouse.Name2} looks uncertain and backs down.");

                    // Reduce suspicion
                    var affairs = NPCMarriageRegistry.Instance?.GetAllAffairs();
                    if (affairs != null)
                    {
                        foreach (var affair in affairs)
                        {
                            if (affair.SeducerId == player.ID || affair.SeducerId == player.Name2)
                            {
                                affair.SpouseSuspicion = Math.Max(0, affair.SpouseSuspicion - 20);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"\n  \"LIAR! I've seen the looks between you two!\"");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {spouse.Name2} attacks in a rage!");
                    await FightNPC(player, spouse, result, terminal);
                }
                break;

            case "A": // Admit
                terminal.SetColor("white");
                terminal.WriteLine($"\n  \"You're right. I'm sorry. It was wrong of me.\"");
                terminal.SetColor("yellow");
                long damage = 10 + _random.Next(15);
                player.HP = Math.Max(1, player.HP - damage);
                terminal.WriteLine($"  {spouse.Name2} punches you! (-{damage} HP)");
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"Stay away from {partnerName}. This is your only warning.\"");

                // Reduce suspicion significantly
                var admitAffairs = NPCMarriageRegistry.Instance?.GetAllAffairs();
                if (admitAffairs != null)
                {
                    foreach (var affair in admitAffairs)
                    {
                        if (affair.SeducerId == player.ID || affair.SeducerId == player.Name2)
                        {
                            affair.SpouseSuspicion = Math.Max(0, affair.SpouseSuspicion - 30);
                            break;
                        }
                    }
                }

                // Relationship damaged
                RelationshipSystem.UpdateRelationship(player, spouse, -1, 3);
                break;

            case "T": // Taunt
                terminal.SetColor("red");
                terminal.WriteLine($"\n  \"Maybe if you were a better spouse, {partnerName} wouldn't need to look elsewhere.\"");
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {spouse.Name2} ROARS with fury! (+25% STR)");
                spouse.Strength = (long)(spouse.Strength * 1.25);
                await FightNPC(player, spouse, result, terminal);

                player.Darkness += 10;

                if (result.Victory)
                {
                    NewsSystem.Instance?.Newsy($"{player.Name2} humiliated {spouse.Name2} in a confrontation over an affair!");
                }
                break;

            default: // Fight
                terminal.SetColor("white");
                terminal.WriteLine($"\n  \"If that's how you want to settle this...\"");
                await FightNPC(player, spouse, result, terminal);
                player.Darkness += 10;

                NewsSystem.Instance?.Newsy($"{player.Name2} and {spouse.Name2} came to blows over a love affair!");
                break;
        }

        await terminal.PressAnyKey();
    }

    private async Task ExecuteThroneChallenge(NPC challenger, Character player,
        TerminalEmulator terminal, EncounterResult result)
    {
        result.EncounterOccurred = true;
        result.Type = EncounterType.ThroneChallenge;

        var king = CastleLocation.GetCurrentKing();

        terminal.ClearScreen();
        UIHelper.DrawBoxTop(terminal, "THRONE CHALLENGE!", "bright_yellow");
        UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
        UIHelper.DrawBoxLine(terminal, $"  {challenger.Name2}, Level {challenger.Level} {challenger.Class}, confronts you.", "bright_yellow", "white");
        UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
        UIHelper.DrawBoxLine(terminal, $"  \"Your times up. Im taking that throne.\"", "bright_yellow", "bright_cyan");
        UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
        UIHelper.DrawBoxSeparator(terminal, "bright_yellow");
        UIHelper.DrawMenuOption(terminal, "A", "Accept the challenge", "bright_yellow", "bright_yellow", "bright_green");
        UIHelper.DrawMenuOption(terminal, "D", "Dismiss (send guards)", "bright_yellow", "bright_yellow", "white");
        UIHelper.DrawMenuOption(terminal, "N", "Negotiate", "bright_yellow", "bright_yellow", "white");
        UIHelper.DrawMenuOption(terminal, "I", "Imprison them", "bright_yellow", "bright_yellow", "red");
        UIHelper.DrawBoxBottom(terminal, "bright_yellow");

        var choice = await terminal.GetInput("\n  Your decree, Majesty? ");

        switch (choice.ToUpper())
        {
            case "A": // Accept
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"\n  \"I accept your challenge. Let us settle this with steel!\"");
                await FightNPC(player, challenger, result, terminal);

                if (result.Victory)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"\n  {challenger.Name2} goes down! Still king.");
                    player.Fame += 25;

                    // Imprison the challenger
                    NPCSpawnSystem.Instance?.ImprisonNPC(challenger, 7);
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {challenger.Name2} is imprisoned for 7 days.");
                    NewsSystem.Instance?.Newsy($"King {player.Name2} defeated {challenger.Name2}'s throne challenge! The challenger is imprisoned.");
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\n  You are defeated! {challenger.Name2} claims the throne!");
                    player.King = false;
                    player.RoyalMercenaries?.Clear(); // Dismiss bodyguards on dethronement
                    player.RecalculateStats(); // Remove Royal Authority HP bonus
                    // NPC becomes king
                    if (king != null)
                    {
                        king.Name = challenger.Name2;
                        king.AI = CharacterAI.Civilian;
                    }
                    NewsSystem.Instance?.Newsy($"{challenger.Name2} defeated King {player.Name2} and seized the throne!");
                }
                break;

            case "D": // Dismiss with guards
                if (king?.Guards != null && king.Guards.Count > 0)
                {
                    int avgLoyalty = (int)king.Guards.Average(g => g.Loyalty);
                    if (avgLoyalty >= 30)
                    {
                        terminal.SetColor("white");
                        terminal.WriteLine($"\n  \"Guards! Remove this fool from my sight!\"");
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  Your loyal guards drag {challenger.Name2} away.");
                        NewsSystem.Instance?.Newsy($"King {player.Name2}'s guards repelled {challenger.Name2}'s challenge.");
                    }
                    else
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"\n  Your guards hesitate... their loyalty wavers!");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  You must face {challenger.Name2} yourself!");
                        await FightNPC(player, challenger, result, terminal);
                    }
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\n  You have no guards! You must face {challenger.Name2} yourself!");
                    await FightNPC(player, challenger, result, terminal);
                }
                break;

            case "N": // Negotiate
                int negotiateChance = Math.Min(70, 30 + (int)(player.Charisma * 2));
                if (_random.Next(100) < negotiateChance)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"\n  \"Perhaps there's a role for someone of your talents in my court.\"");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {challenger.Name2} considers... \"A seat on the court? That could work.\"");

                    if (king != null)
                    {
                        king.CourtMembers.Add(new CourtMember
                        {
                            Name = challenger.Name2,
                            Role = "Advisor",
                            LoyaltyToKing = 40
                        });
                    }

                    NewsSystem.Instance?.Newsy($"King {player.Name2} negotiated with would-be usurper {challenger.Name2}, offering them a court position.");
                }
                else
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"\n  \"I don't want a SEAT. I want the THRONE!\"");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {challenger.Name2} attacks!");
                    await FightNPC(player, challenger, result, terminal);
                }
                break;

            default: // Imprison
                if (king?.Guards != null && king.Guards.Count > 0)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\n  \"Seize them! 14 days in the dungeon for treason!\"");

                    NPCSpawnSystem.Instance?.ImprisonNPC(challenger, 14);

                    // Guards lose loyalty (tyrannical act)
                    foreach (var guard in king.Guards)
                        guard.Loyalty = Math.Max(0, guard.Loyalty - 10);

                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {challenger.Name2} is dragged away. Your guards exchange uneasy glances.");
                    player.Darkness += 5;
                    NewsSystem.Instance?.Newsy($"King {player.Name2} imprisoned {challenger.Name2} for challenging the throne.");
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\n  Without guards, you can't imprison anyone!");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {challenger.Name2} laughs and attacks!");
                    await FightNPC(player, challenger, result, terminal);
                }
                break;
        }

        await terminal.PressAnyKey();
    }

    private async Task ExecuteCityControlContest(NPC rival, Character player,
        TerminalEmulator terminal, EncounterResult result)
    {
        result.EncounterOccurred = true;
        result.Type = EncounterType.CityControlContest;

        string rivalTeam = rival.Team ?? "Unknown";

        terminal.ClearScreen();
        UIHelper.DrawBoxTop(terminal, "TURF WAR!", "bright_red");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxLine(terminal, $"  Members of '{rivalTeam}' surround you.", "bright_red", "white");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxLine(terminal, $"  {rival.Name2}: \"Your team's hold on this city ends now!\"", "bright_red", "bright_cyan");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxSeparator(terminal, "bright_red");
        UIHelper.DrawMenuOption(terminal, "F", "Fight their champion", "bright_red", "bright_yellow", "bright_green");
        long payoffCost = rival.Level * 50;
        UIHelper.DrawMenuOption(terminal, "P", $"Pay them off ({payoffCost}g)", "bright_red", "bright_yellow", "yellow");
        UIHelper.DrawMenuOption(terminal, "S", "Surrender turf", "bright_red", "bright_yellow", "gray");
        UIHelper.DrawMenuOption(terminal, "R", "Run", "bright_red", "bright_yellow", "gray");
        UIHelper.DrawBoxBottom(terminal, "bright_red");

        var choice = await terminal.GetInput("\n  Your response? ");

        switch (choice.ToUpper())
        {
            case "F": // Fight champion
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"\n  \"I'll fight your best. Bring it on!\"");
                await FightNPC(player, rival, result, terminal);

                if (result.Victory)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"\n  {rival.Name2} goes down! '{rivalTeam}' backs off.");
                    player.Fame += 20;
                    NewsSystem.Instance?.Newsy($"{player.Name2} defended their turf by defeating {rival.Name2} of '{rivalTeam}'!");
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\n  '{rivalTeam}' cheers. You lost.");
                    NewsSystem.Instance?.Newsy($"'{rivalTeam}' defeated {player.Name2} in a turf war!");
                }
                break;

            case "P": // Pay off
                if (player.Gold >= payoffCost)
                {
                    player.Gold -= payoffCost;
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"\n  You hand over {payoffCost} gold.");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {rival.Name2}: \"Smart move. We'll leave you alone... for now.\"");
                    result.GoldLost = payoffCost;
                    NewsSystem.Instance?.Newsy($"{player.Name2} paid off '{rivalTeam}' to avoid a turf war.");
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\n  You don't have enough gold!");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {rival.Name2}: \"Then we settle this the hard way!\"");
                    await FightNPC(player, rival, result, terminal);
                }
                break;

            case "S": // Surrender
                terminal.SetColor("gray");
                terminal.WriteLine($"\n  \"Fine. Take it. It's yours.\"");
                terminal.SetColor("white");
                terminal.WriteLine($"  '{rivalTeam}' takes control of the area. Your reputation takes a hit.");
                player.Fame = Math.Max(0, player.Fame - 10);
                NewsSystem.Instance?.Newsy($"{player.Name2} surrendered turf to '{rivalTeam}' without a fight.");
                break;

            default: // Run
                int fleeChance = Math.Min(75, 30 + (int)(player.Dexterity * 2));
                if (_random.Next(100) < fleeChance)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"\n  You dodge through the crowd and escape!");
                }
                else
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"\n  They cut off your escape! {rival.Name2} attacks!");
                    await FightNPC(player, rival, result, terminal);
                }
                break;
        }

        await terminal.PressAnyKey();
    }

    #endregion

    #endregion

    #region NPC Murder System

    /// <summary>
    /// Murder an NPC — permanent death, gold theft, witness memories, faction consequences.
    /// Called from BaseLocation.AttackNPC() after the player commits to attacking.
    /// </summary>
    public async Task<EncounterResult> MurderNPC(Character player, NPC npc, TerminalEmulator terminal, GameLocation location)
    {
        var result = new EncounterResult
        {
            EncounterOccurred = true,
            Type = EncounterType.GrudgeConfrontation // Reuse closest type
        };

        // Apply backstab bonus — Assassin class gets better first strike
        float backstabBonus = player.Class == CharacterClass.Assassin
            ? GameConfig.AssassinBackstabBonusDamage
            : GameConfig.GenericBackstabBonusDamage;

        // Create monster from NPC (same pattern as FightNPC)
        int effectiveHP = Math.Max(1, (int)(npc.HP * (1.0f - backstabBonus)));

        var monster = Monster.CreateMonster(
            nr: npc.Level,
            name: npc.Name,
            hps: effectiveHP,
            strength: (int)npc.Strength,
            defence: (int)npc.Defence,
            phrase: $"You'll pay for this, {player.Name2}!",
            grabweap: false,
            grabarm: false,
            weapon: GetRandomWeaponName(npc.Level),
            armor: GetRandomArmorName(npc.Level),
            poisoned: false,
            disease: false,
            punch: (int)(npc.Strength / 2),
            armpow: (int)npc.ArmPow,
            weappow: (int)npc.WeapPow
        );

        // Show backstab message
        if (backstabBonus > 0.15f)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  Your assassin training grants a devastating first strike! (-{(int)(backstabBonus * 100)}% enemy HP)");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  You catch them off guard! (-{(int)(backstabBonus * 100)}% enemy HP)");
        }
        terminal.WriteLine("");

        // Combat
        var combatEngine = new CombatEngine(terminal);
        var combatResult = await combatEngine.PlayerVsMonster(player, monster);

        result.Victory = combatResult.Outcome == CombatOutcome.Victory;

        // Look up the real NPC in the spawn system
        var realNpc = NPCSpawnSystem.Instance?.GetNPCByName(npc.Name2 ?? npc.Name);

        if (result.Victory)
        {
            // === PERMANENT DEATH (deliberate murder = always permadeath) ===
            npc.HP = 0;
            if (realNpc != null)
            {
                realNpc.HP = 0;
                realNpc.IsDead = true;
                realNpc.IsPermaDead = true;  // Murder is always permanent
            }

            // === GOLD THEFT ===
            long stolenGold = (long)(npc.Gold * GameConfig.MurderGoldTheftPercent);
            if (stolenGold > 0)
            {
                player.Gold += stolenGold;
                if (realNpc != null) realNpc.Gold -= stolenGold;
            }
            result.GoldGained = stolenGold;

            // === XP REWARD ===
            long expGain = npc.Level * 120 + _random.Next(50, 200);
            player.Experience += expGain;
            result.ExperienceGained = expGain;
            result.Message = $"Murdered {npc.Name2 ?? npc.Name}! (+{expGain} XP, +{stolenGold} gold)";

            // === RECORD MURDERED MEMORY ON VICTIM ===
            if (realNpc?.Memory != null)
            {
                realNpc.Memory.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Murdered,
                    Description = $"Murdered by {player.Name2}",
                    InvolvedCharacter = player.Name2,
                    Importance = 1.0f,
                    EmotionalImpact = -1.0f,
                    Location = BaseLocation.GetLocationName(location)
                });
            }

            // === WITNESS MEMORIES ===
            var locationName = BaseLocation.GetLocationName(location);
            var witnesses = NPCSpawnSystem.Instance?.ActiveNPCs?
                .Where(w => !w.IsDead && w.IsAlive
                    && w.Name != npc.Name
                    && w.CurrentLocation == locationName)
                .ToList() ?? new List<NPC>();

            foreach (var witness in witnesses)
            {
                witness.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.SawDeath,
                    Description = $"Witnessed {player.Name2} murder {npc.Name2 ?? npc.Name}",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.9f,
                    EmotionalImpact = -0.8f,
                    Location = locationName
                });
            }

            if (witnesses.Count > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {witnesses.Count} witness{(witnesses.Count > 1 ? "es" : "")} saw the murder!");
            }

            // === QUEST COMPLETION ===
            string npcNameForBounty = npc.Name ?? npc.Name2 ?? "";
            long bountyReward = QuestSystem.AutoCompleteBountyForNPC(player, npcNameForBounty);
            QuestSystem.OnNPCDefeated(player, npc);

            if (bountyReward > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  *** BOUNTY COLLECTED! +{bountyReward:N0} gold ***");
                result.GoldGained += bountyReward;
            }

            // === NEWS (permadeath — this one isn't coming back) ===
            NewsSystem.Instance?.Newsy(
                $"\u2620 {player.Name2} murdered {npc.Name2 ?? npc.Name} in cold blood! They will not return.");

            // No respawn queue — deliberate murder is always permanent (IsPermaDead blocks respawn)

            // === BLOOD PRICE (adjusted by bounty type) ===
            if (realNpc != null)
            {
                string npcNameForBloodPrice = npc.Name2 ?? npc.Name ?? "";
                var bountyInitiator = QuestSystem.GetActiveBountyInitiator(player.Name2, npcNameForBloodPrice);

                if (bountyInitiator == GameConfig.FactionInitiatorCrown
                    || bountyInitiator == "The Crown"   // King bounties
                    || bountyInitiator == "Bounty Board")
                {
                    // Crown/King bounties are state-sanctioned — no blood price
                    terminal.SetColor("gray");
                    terminal.WriteLine("  (Sanctioned kill — your conscience is clear.)");
                }
                else if (bountyInitiator == GameConfig.FactionInitiatorShadows)
                {
                    // Shadows contract — professional hit, reduced weight with chance to skip
                    if (_random.NextDouble() < GameConfig.ShadowContractBloodPriceSkipChance)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("  (A clean job. No loose ends, no guilt.)");
                    }
                    else
                    {
                        WorldSimulator.ApplyBloodPrice(player, realNpc, GameConfig.MurderWeightPerShadowContract, isDeliberate: true);
                        terminal.SetColor("dark_red");
                        terminal.WriteLine("  (Contract or not, you still see their face...)");
                    }
                }
                else
                {
                    // No bounty — full blood price for unprovoked murder
                    WorldSimulator.ApplyBloodPrice(player, realNpc, GameConfig.MurderWeightPerDeliberateMurder, isDeliberate: true);
                }
            }

            // === FACTION STANDING PENALTY ===
            if (realNpc?.NPCFaction != null)
            {
                var factionSystem = FactionSystem.Instance;
                if (factionSystem != null)
                {
                    var victimFaction = realNpc.NPCFaction.Value;
                    factionSystem.ModifyReputation(victimFaction, -GameConfig.MurderFactionStandingPenalty);
                    terminal.SetColor("dark_red");
                    terminal.WriteLine($"  Your standing with {victimFaction} has dropped sharply!");
                }
            }

            // === FRIEND HOSTILITY ===
            // NPCs who liked the victim now hate the player
            if (realNpc != null)
            {
                var friends = NPCSpawnSystem.Instance?.ActiveNPCs?
                    .Where(f => !f.IsDead && f.IsAlive
                        && f.Name != npc.Name
                        && f.Memory != null
                        && f.Memory.GetCharacterImpression(realNpc.Name2 ?? realNpc.Name) > 0.3f)
                    .ToList() ?? new List<NPC>();

                foreach (var friend in friends)
                {
                    friend.Memory?.RecordEvent(new MemoryEvent
                    {
                        Type = MemoryType.MadeEnemy,
                        Description = $"Heard that {player.Name2} murdered my friend {npc.Name2 ?? npc.Name}",
                        InvolvedCharacter = player.Name2,
                        Importance = 0.85f,
                        EmotionalImpact = -0.7f
                    });
                }
            }

            // === STATISTICS ===
            if (player is Player p)
            {
                p.Statistics?.RecordMonsterKill(expGain, stolenGold, false, false);
            }
        }
        else
        {
            // Player lost — NPC remembers the attempt
            result.Message = $"Failed to murder {npc.Name2 ?? npc.Name}...";

            if (realNpc?.Memory != null)
            {
                realNpc.Memory.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.Attacked,
                    Description = $"{player.Name2} tried to murder me!",
                    InvolvedCharacter = player.Name2,
                    Importance = 0.95f,
                    EmotionalImpact = -0.9f,
                    Location = BaseLocation.GetLocationName(location)
                });
            }
        }

        return result;
    }

    #endregion
}

/// <summary>
/// Result of a street encounter
/// </summary>
public class EncounterResult
{
    public bool EncounterOccurred { get; set; }
    public StreetEncounterSystem.EncounterType Type { get; set; }
    public bool Victory { get; set; }
    public string Message { get; set; } = "";
    public long GoldLost { get; set; }
    public long GoldGained { get; set; }
    public long ExperienceGained { get; set; }
    public List<string> Log { get; set; } = new List<string>();
}
