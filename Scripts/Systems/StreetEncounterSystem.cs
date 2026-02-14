using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Utils;
using UsurperRemake.Systems;

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
        { GameLocation.Marketplace, 0.08f },     // 8% - Pickpockets
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
        Ambush                // Pre-planned attack
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

            GameLocation.Marketplace => roll switch
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

        terminal.SetColor("cyan");
        terminal.WriteLine("  [F]ight  [R]un  [B]ribe  [T]alk");
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
            terminal.SetColor("white");
            terminal.WriteLine("  [G]rab them  [S]hout for guards  [I]gnore");

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

        terminal.SetColor("white");
        terminal.WriteLine("  [F]ight  [D]uck and run  [B]uy them a drink");
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
        terminal.WriteLine($"  {challenger.Name} approaches you with a confident stride.");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  \"{GetChallengePhrase(challenger, player)}\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  {challenger.Name} - Level {challenger.Level} {challenger.Class}");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("  [A]ccept challenge  [D]ecline  [I]nsult them");
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

        terminal.SetColor("white");
        terminal.WriteLine("  [F]ight  [S]urrender gold  [R]un for it");
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

            terminal.SetColor("white");
            terminal.WriteLine("  [F]ight for territory  [N]egotiate  [L]eave");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  \"You look like you could use some friends...\"");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  [J]oin them  [R]efuse  [F]ight");
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
        terminal.WriteLine("  \"I've heard tales of your adventures. Join me for a drink?\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  [Y]es  [N]o thanks  [F]lirt back");
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

        terminal.SetColor("white");
        for (int i = 0; i < items.Count; i++)
        {
            terminal.WriteLine($"  [{i + 1}] {items[i].Name} - {items[i].Price} gold");
        }
        terminal.WriteLine("  [0] No thanks");
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

        terminal.SetColor("white");
        terminal.WriteLine("  [G]ive gold (10)  [L]arge donation (50)  [I]gnore  [R]ob them");
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
        if (wanted)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  \"Halt! We've been looking for you!\"");
            terminal.SetColor("magenta");
            terminal.WriteLine($"  (Your dark reputation precedes you — Darkness: {player.Darkness})");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  [S]urrender  [F]ight  [R]un  [B]ribe (100 gold)");

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

        terminal.SetColor("white");
        terminal.WriteLine($"  [Y]es, pay {bribeAmount}  [N]o, fight instead");

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
            "Your journey ends here!"
        };
        return phrases[_random.Next(phrases.Length)];
    }

    private string GetChallengePhrase(NPC challenger, Character player)
    {
        string[] phrases = {
            $"I challenge you, {player.Name2}! Let us see who is stronger!",
            "Your reputation precedes you. Face me in honorable combat!",
            "I've heard tales of your prowess. Prove them true!",
            "Think you're tough? Let's find out!",
            "My blade thirsts for a worthy opponent. Are you one?",
            "The arena awaits! Unless you're too cowardly..."
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
