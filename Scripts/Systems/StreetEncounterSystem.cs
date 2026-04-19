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

    private Random _random = Random.Shared;

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
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.hostile.title"), "bright_red");
        terminal.WriteLine("");

        // Find or create an attacker
        NPC attacker = FindHostileNPC(player, location);
        if (attacker == null)
        {
            attacker = CreateRandomHostileNPC(player.Level);
        }

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("street_encounter.hostile.blocks_path", attacker.Name));
        terminal.SetColor("yellow");
        terminal.WriteLine($"  \"{GetHostilePhrase(attacker)}\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("street_encounter.hostile.stats", attacker.Name, attacker.Level, attacker.Class, attacker.HP, attacker.MaxHP));
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("F", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.hostile.opt_fight")}  [", "white");
        terminal.Write("R", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.hostile.opt_run")}  [", "white");
        terminal.Write("B", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.hostile.opt_bribe")}  [", "white");
        terminal.Write("T", "bright_yellow");
        terminal.WriteLine($"]{Loc.Get("street_encounter.hostile.opt_talk")}", "white");
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
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.pickpocket.title"), "yellow");
        terminal.WriteLine("");

        // Dexterity check to notice
        int noticeRoll = _random.Next(20) + 1;
        int dexMod = (int)(player.Dexterity - 10) / 2;
        bool noticed = noticeRoll + dexMod >= 12;

        if (noticed)
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("street_encounter.pickpocket.feel_hand"));
            terminal.WriteLine("");
            terminal.Write("  [", "white");
            terminal.Write("G", "bright_yellow");
            terminal.Write($"]{Loc.Get("street_encounter.pickpocket.opt_grab")}  [", "white");
            terminal.Write("S", "bright_yellow");
            terminal.Write($"]{Loc.Get("street_encounter.pickpocket.opt_shout")}  [", "white");
            terminal.Write("I", "bright_yellow");
            terminal.WriteLine($"]{Loc.Get("street_encounter.pickpocket.opt_ignore")}", "white");

            string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

            if (choice == "G")
            {
                // Create a low-level thief
                var thief = CreateRandomHostileNPC(Math.Max(1, player.Level - 3));
                thief.Class = CharacterClass.Assassin;
                thief.Name2 = "Pickpocket"; thief.Name1 = "Pickpocket";

                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("street_encounter.pickpocket.grab_thief", thief.Name));
                await Task.Delay(1000);

                await FightNPC(player, thief, result, terminal);
            }
            else if (choice == "S")
            {
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("street_encounter.pickpocket.shout_guards"));
                terminal.WriteLine(Loc.Get("street_encounter.pickpocket.thief_flees"));
                result.Message = Loc.Get("street_encounter.pickpocket.msg_scared_away");
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("street_encounter.pickpocket.pretend_not_notice"));
                result.Message = Loc.Get("street_encounter.pickpocket.msg_avoided");
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
                terminal.WriteLine(Loc.Get("street_encounter.pickpocket.bumps_into"));
                terminal.WriteLine(Loc.Get("street_encounter.pickpocket.gold_missing", stolenAmount));
                result.GoldLost = stolenAmount;
                result.Message = Loc.Get("street_encounter.pickpocket.msg_stolen", stolenAmount);
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("street_encounter.pickpocket.nothing_to_steal"));
                result.Message = Loc.Get("street_encounter.pickpocket.msg_found_nothing");
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
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.brawl.title"), "bright_yellow");
        terminal.WriteLine("");

        string[] brawlReasonKeys = {
            "street_encounter.brawl.reason_ale",
            "street_encounter.brawl.reason_dice",
            "street_encounter.brawl.reason_insult",
            "street_encounter.brawl.reason_bar_fight",
            "street_encounter.brawl.reason_mercenary",
            "street_encounter.brawl.reason_seat"
        };

        terminal.SetColor("yellow");
        terminal.WriteLine($"  {Loc.Get(brawlReasonKeys[_random.Next(brawlReasonKeys.Length)])}");
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("F", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.brawl.opt_fight")}  [", "white");
        terminal.Write("D", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.brawl.opt_duck")}  [", "white");
        terminal.Write("B", "bright_yellow");
        terminal.WriteLine($"]{Loc.Get("street_encounter.brawl.opt_buy_drink")}", "white");
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
            terminal.WriteLine(Loc.Get("street_encounter.brawl.squares_up", brawler.Name));
            await Task.Delay(1000);

            await FightNPC(player, brawler, result, terminal, isBrawl: true);
        }
        else if (choice == "D")
        {
            int dexCheck = _random.Next(20) + 1 + (int)(player.Dexterity - 10) / 2;
            if (dexCheck >= 10)
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.brawl.duck_escape"));
                result.Message = Loc.Get("street_encounter.brawl.msg_escaped");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("street_encounter.brawl.hit_by_mug"));
                player.HP -= _random.Next(5, 15);
                if (player.HP < 1) player.HP = 1;
                result.Message = Loc.Get("street_encounter.brawl.msg_hit_escaping");
            }
        }
        else if (choice == "B")
        {
            if (player.Gold >= 20)
            {
                player.Gold -= 20;
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.brawl.buy_round"));
                terminal.WriteLine(Loc.Get("street_encounter.brawl.toast_health"));
                result.GoldLost = 20;
                result.Message = Loc.Get("street_encounter.brawl.msg_bought_drinks");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("street_encounter.brawl.not_enough_gold"));
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
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.challenge.title"), "bright_cyan");
        terminal.WriteLine("");

        // Find an NPC near player's level
        NPC challenger = FindChallengerNPC(player);
        if (challenger == null)
        {
            challenger = CreateRandomHostileNPC(player.Level);
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("street_encounter.challenge.walks_up", challenger.Name));
        terminal.SetColor("yellow");
        terminal.WriteLine($"  \"{GetChallengePhrase(challenger, player)}\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("street_encounter.challenge.info", challenger.Name, challenger.Level, challenger.Class));
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("A", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.challenge.opt_accept")}  [", "white");
        terminal.Write("D", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.challenge.opt_decline")}  [", "white");
        terminal.Write("I", "bright_yellow");
        terminal.WriteLine($"]{Loc.Get("street_encounter.challenge.opt_insult")}", "white");
        terminal.WriteLine("");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "A")
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("street_encounter.challenge.accept_honor"));
            await Task.Delay(1000);
            await FightNPC(player, challenger, result, terminal, isHonorDuel: true);
        }
        else if (choice == "D")
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("street_encounter.challenge.decline_reply"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("street_encounter.challenge.scoffs", challenger.Name));

            // Declining hurts reputation slightly
            player.Fame = Math.Max(0, player.Fame - 5);
            result.Message = Loc.Get("street_encounter.challenge.msg_declined");
        }
        else if (choice == "I")
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("street_encounter.challenge.insult_honor"));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("street_encounter.challenge.pay_for_that", challenger.Name));
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
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.mugging.title"), "bright_red");
        terminal.WriteLine("");

        int muggerCount = _random.Next(2, 4);
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("street_encounter.mugging.thugs_emerge", muggerCount));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("street_encounter.mugging.gold_or_life"));
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("F", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.hostile.opt_fight")}  [", "white");
        terminal.Write("S", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.mugging.opt_surrender")}  [", "white");
        terminal.Write("R", "bright_yellow");
        terminal.WriteLine($"]{Loc.Get("street_encounter.mugging.opt_run")}", "white");
        terminal.WriteLine("");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        // Invalid input defaults to fight — you can't ignore thugs
        if (choice != "S" && choice != "R")
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("street_encounter.mugging.draw_weapon"));
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

            var muggerTeammates = GetStreetCombatTeammates(player);
            var combatEngine = new CombatEngine(terminal);
            var combatResult = await combatEngine.PlayerVsMonsters(player, muggers, muggerTeammates);

            if (combatResult.Outcome == CombatOutcome.Victory)
            {
                long loot = _random.Next(50, 150) * muggerCount;
                player.Gold += loot;
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.mugging.defeated_found_gold", loot));
                result.GoldGained = loot;
                result.Message = Loc.Get("street_encounter.mugging.msg_defeated", muggerCount);
            }
            else
            {
                result.Message = Loc.Get("street_encounter.mugging.msg_lost");
            }
        }
        else if (choice == "S")
        {
            long surrenderAmount = Math.Min(player.Gold, _random.Next(100, 300));
            player.Gold -= surrenderAmount;

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("street_encounter.mugging.hand_over", surrenderAmount));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("street_encounter.mugging.thugs_disappear"));
            result.GoldLost = surrenderAmount;
            result.Message = Loc.Get("street_encounter.mugging.msg_surrendered", surrenderAmount);
        }
        else if (choice == "R")
        {
            int escapeChance = 30 + (int)(player.Dexterity * 2);
            if (_random.Next(100) < escapeChance)
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.mugging.sprint_escape"));
                result.Message = Loc.Get("street_encounter.mugging.msg_escaped");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("street_encounter.mugging.caught_beaten"));

                int damage = _random.Next(20, 50);
                player.HP -= damage;
                if (player.HP < 1) player.HP = 1;

                long stolenGold = Math.Min(player.Gold, _random.Next(50, 200));
                player.Gold -= stolenGold;

                terminal.WriteLine(Loc.Get("street_encounter.mugging.damage_and_gold", damage, stolenGold));
                result.GoldLost = stolenGold;
                result.Message = Loc.Get("street_encounter.mugging.msg_caught");
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
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.gang.title"), "bright_magenta");
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
        terminal.WriteLine(Loc.Get("street_encounter.gang.block_path", gangName));
        terminal.WriteLine("");

        if (playerHasTeam)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("street_encounter.gang.heard_team", player.Team));
            terminal.WriteLine(Loc.Get("street_encounter.gang.our_territory"));
            terminal.WriteLine("");

            terminal.Write("  [", "white");
            terminal.Write("F", "bright_yellow");
            terminal.Write($"]{Loc.Get("street_encounter.gang.opt_fight_territory")}  [", "white");
            terminal.Write("N", "bright_yellow");
            terminal.Write($"]{Loc.Get("street_encounter.gang.opt_negotiate")}  [", "white");
            terminal.Write("L", "bright_yellow");
            terminal.WriteLine($"]{Loc.Get("street_encounter.gang.opt_leave")}", "white");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("street_encounter.gang.need_friends"));
            terminal.WriteLine("");

            terminal.Write("  [", "white");
            terminal.Write("J", "bright_yellow");
            terminal.Write($"]{Loc.Get("street_encounter.gang.opt_join")}  [", "white");
            terminal.Write("R", "bright_yellow");
            terminal.Write($"]{Loc.Get("street_encounter.gang.opt_refuse")}  [", "white");
            terminal.Write("F", "bright_yellow");
            terminal.WriteLine($"]{Loc.Get("street_encounter.hostile.opt_fight")}", "white");
        }

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "F")
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("street_encounter.gang.wrong_answer"));

            // Create gang leader
            var gangLeader = CreateRandomHostileNPC(player.Level + 2);
            gangLeader.Name2 = $"{gangName} Leader"; gangLeader.Name1 = gangLeader.Name2;

            await FightNPC(player, gangLeader, result, terminal);

            if (result.Victory)
            {
                player.Fame += 20;
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.gang.victory_spreads", gangName));
                terminal.WriteLine(Loc.Get("street_encounter.gang.plus_fame"));
            }
        }
        else if (choice == "J" && !playerHasTeam)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("street_encounter.gang.looks_you_over"));
            await Task.Delay(1000);

            // Check if this is an actual existing team with members
            bool isRealTeam = eligibleTeams != null && eligibleTeams.Any(t => t.Name == gangName);

            if (player.Level >= 3 && isRealTeam)
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.gang.welcome", gangName));

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

                result.Message = Loc.Get("street_encounter.gang.msg_joined", gangName);

                // Announce to news
                NewsSystem.Instance?.WriteTeamNews("Gang Recruitment!",
                    $"{GameConfig.NewsColorPlayer}{player.Name2}{GameConfig.NewsColorDefault} joined {GameConfig.NewsColorHighlight}{gangName}{GameConfig.NewsColorDefault}!");
            }
            else if (player.Level < 3)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("street_encounter.gang.too_weak"));
                result.Message = Loc.Get("street_encounter.gang.msg_too_weak");
            }
            else
            {
                // Team doesn't actually exist - decline the invitation
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("street_encounter.gang.not_recruiting_1"));
                terminal.WriteLine(Loc.Get("street_encounter.gang.not_recruiting_2"));
                result.Message = Loc.Get("street_encounter.gang.msg_not_recruiting");
            }
        }
        else if (choice == "N" || choice == "L" || choice == "R")
        {
            int charismaCheck = _random.Next(20) + 1 + (int)(player.Charisma - 10) / 2;
            if (charismaCheck >= 12 || choice == "L")
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.gang.let_you_pass"));
                result.Message = Loc.Get("street_encounter.gang.msg_avoided");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("street_encounter.gang.nobody_refuses"));
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
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.romance.title"), "bright_magenta");
        terminal.WriteLine("");

        string[] admirerKeys = player.Sex == CharacterSex.Male ?
            new[] { "street_encounter.romance.admirer_maiden", "street_encounter.romance.admirer_beautiful", "street_encounter.romance.admirer_mysterious_w", "street_encounter.romance.admirer_charming_w" } :
            new[] { "street_encounter.romance.admirer_handsome", "street_encounter.romance.admirer_dashing", "street_encounter.romance.admirer_mysterious_m", "street_encounter.romance.admirer_charming_m" };

        string admirer = Loc.Get(admirerKeys[_random.Next(admirerKeys.Length)]);

        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("street_encounter.romance.approaches", admirer));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("street_encounter.romance.buy_drink"));
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("Y", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.romance.opt_yes")}  [", "white");
        terminal.Write("N", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.romance.opt_no")}  [", "white");
        terminal.Write("F", "bright_yellow");
        terminal.WriteLine($"]{Loc.Get("street_encounter.romance.opt_flirt")}", "white");
        terminal.WriteLine("");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "Y" || choice == "F")
        {
            terminal.SetColor("magenta");
            terminal.WriteLine(Loc.Get("street_encounter.romance.pleasant_time"));
            await Task.Delay(1500);

            // Random outcomes
            int outcome = _random.Next(100);
            if (outcome < 60)
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.romance.wonderful_conversation"));
                player.Charisma = Math.Min(player.Charisma + 1, 30);
                result.Message = Loc.Get("street_encounter.romance.msg_connection");
            }
            else if (outcome < 80)
            {
                // They're actually a pickpocket
                long stolen = Math.Min(player.Gold / 5, _random.Next(20, 80));
                if (stolen > 0)
                {
                    player.Gold -= stolen;
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("street_encounter.romance.purse_lighter"));
                    terminal.WriteLine(Loc.Get("street_encounter.romance.stole_gold", stolen));
                    result.GoldLost = stolen;
                    result.Message = Loc.Get("street_encounter.romance.msg_scam");
                }
            }
            else
            {
                // Genuine connection
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("street_encounter.romance.token_affection"));
                player.Fame += 5;
                result.Message = Loc.Get("street_encounter.romance.msg_fame");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("street_encounter.romance.another_time"));
            terminal.WriteLine(Loc.Get("street_encounter.romance.smile_walk_away"));
            result.Message = Loc.Get("street_encounter.romance.msg_declined");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Process merchant encounter
    /// </summary>
    private async Task ProcessMerchantEncounter(Character player, GameLocation location,
        EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.merchant.title"), "bright_yellow");
        terminal.WriteLine("");

        bool shadyMerchant = location == GameLocation.DarkAlley;

        if (shadyMerchant)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("street_encounter.merchant.cloaked_figure"));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("street_encounter.merchant.buy_special"));
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("street_encounter.merchant.waves_over"));
            terminal.WriteLine(Loc.Get("street_encounter.merchant.fine_wares"));
        }
        terminal.WriteLine("");

        // Generate random items
        var items = GenerateMerchantItems(player.Level, shadyMerchant);

        for (int i = 0; i < items.Count; i++)
        {
            terminal.Write("  [", "white");
            terminal.Write($"{i + 1}", "bright_yellow");
            terminal.Write($"] {items[i].Name}", "white");
            terminal.WriteLine($" - {items[i].Price} gold", "yellow");
            if (!string.IsNullOrEmpty(items[i].Description))
                terminal.WriteLine($"      {items[i].Description}", "gray");
        }
        terminal.Write("  [", "white");
        terminal.Write("0", "bright_yellow");
        terminal.WriteLine($"] {Loc.Get("street_encounter.merchant.no_thanks")}", "white");
        terminal.WriteLine("");

        string choice = await terminal.GetInput(Loc.Get("street_encounter.merchant.buy_prompt"));
        if (int.TryParse(choice, out int itemChoice) && itemChoice >= 1 && itemChoice <= items.Count)
        {
            var item = items[itemChoice - 1];
            if (player.Gold >= item.Price)
            {
                player.Gold -= item.Price;
                player.Statistics?.RecordPurchase(item.Price);
                player.Statistics?.RecordGoldSpent(item.Price);
                player.Statistics?.RecordGoldChange(player.Gold);
                ApplyMerchantItem(player, item);

                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.merchant.purchased", item.Name));
                if (!string.IsNullOrEmpty(item.Description))
                    terminal.WriteLine($"  {item.Description}", "cyan");
                result.GoldLost = item.Price;
                result.Message = Loc.Get("street_encounter.merchant.msg_bought", item.Name);
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("street_encounter.merchant.not_enough_gold"));
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("street_encounter.merchant.come_back"));
            result.Message = Loc.Get("street_encounter.merchant.msg_declined");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Process beggar encounter
    /// </summary>
    private async Task ProcessBeggarEncounter(Character player, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.beggar.title"), "gray");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("street_encounter.beggar.approaches"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("street_encounter.beggar.spare_coins"));
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("G", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.beggar.opt_give")}  [", "white");
        terminal.Write("L", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.beggar.opt_large")}  [", "white");
        terminal.Write("I", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.pickpocket.opt_ignore")}  [", "white");
        terminal.Write("R", "bright_yellow");
        terminal.WriteLine($"]{Loc.Get("street_encounter.beggar.opt_rob")}", "white");
        terminal.WriteLine("");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "G" && player.Gold >= 10)
        {
            player.Gold -= 10;
            player.Chivalry += 5;
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("street_encounter.beggar.thanks"));
            terminal.WriteLine(Loc.Get("street_encounter.beggar.plus_chivalry", 5));
            result.GoldLost = 10;
            result.Message = Loc.Get("street_encounter.beggar.msg_gave");
        }
        else if (choice == "L" && player.Gold >= 50)
        {
            player.Gold -= 50;
            player.Chivalry += 20;
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("street_encounter.beggar.bless_you"));
            terminal.WriteLine(Loc.Get("street_encounter.beggar.plus_chivalry", 20));

            // Chance for a reward
            if (_random.Next(100) < 20)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("street_encounter.beggar.strange_amulet"));
                terminal.WriteLine(Loc.Get("street_encounter.beggar.luck_amulet"));

            }

            result.GoldLost = 50;
            result.Message = Loc.Get("street_encounter.beggar.msg_large_donation");
        }
        else if (choice == "R")
        {
            player.Darkness += 15;
            player.Chivalry = Math.Max(0, player.Chivalry - 10);

            int foundGold = _random.Next(1, 10);
            player.Gold += foundGold;

            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("street_encounter.beggar.rob_possessions"));
            terminal.WriteLine(Loc.Get("street_encounter.beggar.rob_result", foundGold));
            result.GoldGained = foundGold;
            result.Message = Loc.Get("street_encounter.beggar.msg_robbed");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("street_encounter.beggar.walk_past"));
            result.Message = Loc.Get("street_encounter.beggar.msg_ignored");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Process rumor encounter
    /// </summary>
    private async Task ProcessRumorEncounter(Character player, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.rumor.title"), "cyan");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("street_encounter.rumor.overhear"));
        terminal.WriteLine("");

        string rumor = GetRandomRumor(player);
        terminal.SetColor("yellow");
        terminal.WriteLine($"  \"{rumor}\"");
        terminal.WriteLine("");

        result.Message = Loc.Get("street_encounter.rumor.msg_heard");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Process guard patrol encounter
    /// </summary>
    private async Task ProcessGuardPatrolEncounter(Character player, EncounterResult result, TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.guard.title"), "bright_white");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("street_encounter.guard.patrol_approaches"));

        bool wanted = player.Darkness > 100;

        // Crown faction members get a pass from the guards
        if (wanted && (FactionSystem.Instance?.HasGuardFavor() ?? false))
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("street_encounter.guard.crown_insignia"));
            terminal.WriteLine(Loc.Get("street_encounter.guard.crown_business"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("street_encounter.guard.step_aside"));
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        if (wanted)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("street_encounter.guard.halt"));
            terminal.SetColor("magenta");
            terminal.WriteLine(Loc.Get("street_encounter.guard.looking_for_you", player.Darkness));
            terminal.WriteLine("");

            terminal.Write("  [", "white");
            terminal.Write("S", "bright_yellow");
            terminal.Write($"]{Loc.Get("street_encounter.guard.opt_surrender")}  [", "white");
            terminal.Write("F", "bright_yellow");
            terminal.Write($"]{Loc.Get("street_encounter.hostile.opt_fight")}  [", "white");
            terminal.Write("R", "bright_yellow");
            terminal.Write($"]{Loc.Get("street_encounter.hostile.opt_run")}  [", "white");
            terminal.Write("B", "bright_yellow");
            terminal.WriteLine($"]{Loc.Get("street_encounter.guard.opt_bribe")}", "white");

            string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

            if (choice == "S")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("street_encounter.guard.arrested"));
                terminal.WriteLine("");

                // Confiscate some gold
                long confiscated = Math.Min(player.Gold, player.Gold / 4 + 50);
                if (confiscated > 0)
                {
                    player.Gold -= confiscated;
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("street_encounter.guard.confiscate", confiscated));
                }

                int sentence = GameConfig.DefaultPrisonSentence;
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("street_encounter.guard.sentenced", sentence));
                await Task.Delay(2000);

                player.DaysInPrison = (byte)Math.Min(255, sentence);
                result.Message = Loc.Get("street_encounter.guard.msg_arrested");
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
                    terminal.WriteLine(Loc.Get("street_encounter.guard.darkness_guards"));
                }
                else if (player.IsAlive)
                {
                    // Lost the fight but survived — arrested
                    terminal.SetColor("yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("street_encounter.guard.overpowered"));
                    int sentence = GameConfig.DefaultPrisonSentence + 2; // Extra for resisting with violence
                    player.Darkness += 30;
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("street_encounter.guard.sentenced_resisting", sentence));
                    await Task.Delay(2000);

                    player.DaysInPrison = (byte)Math.Min(255, sentence);
                    result.Message = Loc.Get("street_encounter.guard.msg_defeated_arrested");
                    throw new LocationExitException(GameLocation.Prison);
                }
            }
            else if (choice == "B" && player.Gold >= 100)
            {
                player.Gold -= 100;
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.guard.bribed"));
                result.GoldLost = 100;
                result.Message = Loc.Get("street_encounter.guard.msg_bribed");
            }
            else if (choice == "R")
            {
                int escape = _random.Next(100);
                if (escape < 40 + player.Dexterity)
                {
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("street_encounter.guard.escape_crowd"));
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("street_encounter.guard.catch_you"));
                    terminal.WriteLine("");

                    // Extra day for resisting
                    int sentence = GameConfig.DefaultPrisonSentence + 1;
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("street_encounter.guard.running_extra"));
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("street_encounter.guard.sentenced_days", sentence));
                    await Task.Delay(2000);

                    player.DaysInPrison = (byte)Math.Min(255, sentence);
                    result.Message = Loc.Get("street_encounter.guard.msg_caught");
                    throw new LocationExitException(GameLocation.Prison);
                }
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("street_encounter.guard.stay_out_trouble"));
            terminal.WriteLine(Loc.Get("street_encounter.guard.continue_patrol"));
            result.Message = Loc.Get("street_encounter.guard.msg_questioned");
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
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.ambush.title"), "bright_red");
        terminal.WriteLine("");

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("street_encounter.ambush.assassins_leap"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("street_encounter.ambush.paid_gold"));
        terminal.WriteLine("");

        // No choice - must fight
        var assassin = CreateRandomHostileNPC(player.Level + 1);
        assassin.Name2 = "Hired Assassin"; assassin.Name1 = "Hired Assassin";
        assassin.Class = CharacterClass.Assassin;

        // Assassin gets first strike
        int firstStrikeDamage = _random.Next(10, 25);
        player.HP -= firstStrikeDamage;

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("street_encounter.ambush.first_strike", firstStrikeDamage));
        terminal.WriteLine("");
        await Task.Delay(1500);

        if (player.HP > 0)
        {
            await FightNPC(player, assassin, result, terminal);

            if (result.Victory)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("street_encounter.ambush.find_note"));
                terminal.WriteLine(Loc.Get("street_encounter.ambush.contract", player.Name2));
                terminal.WriteLine(Loc.Get("street_encounter.ambush.unreadable"));
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
    /// Build a list of the player's available combat allies (companions + royal mercenaries)
    /// for street encounters. Mirrors the dungeon's teammate assembly but without display text.
    /// </summary>
    private List<Character> GetStreetCombatTeammates(Character player)
    {
        var teammates = new List<Character>();
        const int maxPartySize = 4;

        // Add active companions
        var companionSystem = CompanionSystem.Instance;
        if (companionSystem != null)
        {
            var companionChars = companionSystem.GetCompanionsAsCharacters();
            foreach (var c in companionChars)
            {
                if (teammates.Count >= maxPartySize) break;
                if (c.IsAlive) teammates.Add(c);
            }
        }

        // Add royal mercenaries (bodyguards for king players)
        if (player.King && player.RoyalMercenaries != null)
        {
            foreach (var merc in player.RoyalMercenaries)
            {
                if (teammates.Count >= maxPartySize) break;
                if (merc.HP <= 0) continue;

                var character = new Character
                {
                    Name2 = merc.Name,
                    Class = merc.Class,
                    Sex = merc.Sex,
                    Level = merc.Level,
                    HP = merc.HP,
                    MaxHP = merc.MaxHP,
                    Mana = merc.Mana,
                    MaxMana = merc.MaxMana,
                    Strength = merc.Strength,
                    Defence = merc.Defence,
                    WeapPow = merc.WeapPow,
                    ArmPow = merc.ArmPow,
                    Agility = merc.Agility,
                    Dexterity = merc.Dexterity,
                    Wisdom = merc.Wisdom,
                    Intelligence = merc.Intelligence,
                    Constitution = merc.Constitution,
                    Healing = merc.Healing,
                    AI = CharacterAI.Computer,
                    IsMercenary = true,
                    MercenaryName = merc.Name,
                    BaseMaxHP = merc.MaxHP,
                    BaseMaxMana = merc.MaxMana,
                    BaseStrength = merc.Strength,
                    BaseDefence = merc.Defence,
                    BaseAgility = merc.Agility,
                    BaseDexterity = merc.Dexterity,
                    BaseWisdom = merc.Wisdom,
                    BaseIntelligence = merc.Intelligence,
                    BaseConstitution = merc.Constitution,
                    Stamina = 5 + merc.Level * 2
                };
                teammates.Add(character);
            }
        }

        return teammates;
    }

    /// <summary>
    /// Fight an NPC using the combat engine
    /// </summary>
    private async Task FightNPC(Character player, NPC npc, EncounterResult result, TerminalEmulator terminal,
        bool isBrawl = false, bool isHonorDuel = false)
    {
        // Convert NPC to Monster for combat engine
        // Pass NPC's level as the 'nr' parameter so the monster displays the correct level
        // v0.57.2 — use MaxHP (fallback to HP if MaxHP is somehow 0). Using current HP meant
        // an NPC awaiting respawn after a world-sim defeat would enter the duel already dead.
        var monster = Monster.CreateMonster(
            nr: npc.Level,
            name: npc.Name,
            hps: (int)(npc.MaxHP > 0 ? npc.MaxHP : Math.Max(1, npc.HP)),
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
        monster.IsProperName = true; // NPC — no "The" prefix
        monster.CanSpeak = true;     // NPCs can speak

        // Include player's companions and bodyguards in street combat
        var teammates = GetStreetCombatTeammates(player);
        var combatEngine = new CombatEngine(terminal);
        var combatResult = await combatEngine.PlayerVsMonster(player, monster, teammates);

        result.Victory = combatResult.Outcome == CombatOutcome.Victory;

        if (result.Victory)
        {
            // XP and gold are handled by the combat engine's HandleVictory — no double reward
            // Just track what the combat engine gave for display
            long expGain = combatResult.ExperienceGained;
            long goldGain = combatResult.GoldGained;

            if (isHonorDuel)
            {
                player.Fame += 15;
                result.Message = Loc.Get("street.fight.honor_duel_victory", npc.Name, expGain.ToString(), goldGain.ToString());
            }
            else if (isBrawl)
            {
                result.Message = Loc.Get("street.fight.brawl_victory", expGain.ToString());
            }
            else
            {
                result.Message = Loc.Get("street.fight.defeated_npc", npc.Name, expGain.ToString(), goldGain.ToString());
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
                terminal.WriteLine(Loc.Get("street.fight.bounty_collected", bountyReward.ToString("N0")));
                result.GoldGained += bountyReward;
            }
        }
        else
        {
            result.Message = Loc.Get("street.fight.lost_to_npc", npc.Name);
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
        terminal.WriteLine(Loc.Get("street_encounter.flee.try_run"));
        await Task.Delay(1000);

        if (_random.Next(100) < fleeChance)
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("street_encounter.flee.escape_success"));
            result.Message = Loc.Get("street_encounter.flee.msg_fled");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("street_encounter.flee.catches_you", npc.Name));
            terminal.WriteLine(Loc.Get("street_encounter.flee.attack_back_turned"));

            // Take damage from failed flee
            int damage = _random.Next(10, 25);
            player.HP -= damage;
            terminal.WriteLine(Loc.Get("street_encounter.flee.take_damage", damage));

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
        terminal.WriteLine(Loc.Get("street_encounter.bribe.offer", bribeAmount));
        await Task.Delay(500);

        if (player.Gold < bribeAmount)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("street_encounter.merchant.not_enough_gold"));
            terminal.WriteLine(Loc.Get("street_encounter.bribe.npc_attacks", npc.Name));
            await Task.Delay(1000);
            await FightNPC(player, npc, result, terminal);
            return;
        }

        terminal.Write("  [", "white");
        terminal.Write("Y", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.bribe.opt_yes", bribeAmount)}  [", "white");
        terminal.Write("N", "bright_yellow");
        terminal.WriteLine($"]{Loc.Get("street_encounter.bribe.opt_no")}", "white");

        string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

        if (choice == "Y")
        {
            int bribeChance = 50 + (int)(player.Charisma - 10) * 3;
            if (_random.Next(100) < bribeChance)
            {
                player.Gold -= bribeAmount;
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("street_encounter.bribe.takes_gold_leaves", npc.Name));
                result.GoldLost = bribeAmount;
                result.Message = Loc.Get("street_encounter.bribe.msg_bribed", npc.Name, bribeAmount);
            }
            else
            {
                player.Gold -= bribeAmount;
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("street_encounter.bribe.takes_gold_attacks", npc.Name));
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
        terminal.WriteLine(Loc.Get("street_encounter.talk.try_reason"));
        await Task.Delay(1000);

        int talkChance = 20 + (int)(player.Charisma - 10) * 4;
        if (player.Class == CharacterClass.Bard) talkChance += 20;

        if (_random.Next(100) < talkChance)
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("street_encounter.talk.reconsiders", npc.Name));
            result.Message = Loc.Get("street_encounter.talk.msg_talked_down");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("street_encounter.talk.not_interested", npc.Name));
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

        // Find NPCs at this location who might be hostile (excluding romantic partners,
        // player's team members, and player's spouse)
        string playerTeam = (player as Player)?.Team ?? "";
        string playerSpouse = (player as Player)?.SpouseName ?? "";
        var potentialEnemies = npcs
            .Where(n => n.IsAlive && n.Level >= player.Level - 5 && n.Level <= player.Level + 5)
            .Where(n => !protectedIds.Contains(n.ID)) // Never attack romantic partners
            .Where(n => string.IsNullOrEmpty(playerTeam) || !playerTeam.Equals(n.Team, StringComparison.OrdinalIgnoreCase)) // Skip player's team
            .Where(n => string.IsNullOrEmpty(playerSpouse) || !(n.Name2 ?? n.Name).Equals(playerSpouse, StringComparison.OrdinalIgnoreCase)) // Skip player's spouse
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

        // Generate stats based on level (scaled to match player power curve)
        int levelSq = level * level;
        npc.MaxHP = 50 + level * 25 + levelSq / 2 + _random.Next(level * 10);
        npc.HP = npc.MaxHP;
        npc.Strength = 12 + level * 3 + levelSq / 20 + _random.Next(5);
        npc.Dexterity = 10 + level * 2 + _random.Next(5);
        npc.Constitution = 10 + level * 2 + _random.Next(5);
        npc.Intelligence = 8 + level + _random.Next(8);
        npc.Wisdom = 8 + level + _random.Next(8);
        npc.Charisma = 6 + _random.Next(6);
        npc.Defence = 8 + level * 3 + levelSq / 15;
        npc.WeapPow = 8 + level * 4 + levelSq / 20;
        npc.ArmPow = 5 + level * 3 + levelSq / 20;

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
            Loc.Get("street_encounter.rumor.dungeons_dangerous"),
            Loc.Get("street_encounter.rumor.king_adventurers"),
            Loc.Get("street_encounter.rumor.strange_creatures"),
            Loc.Get("street_encounter.rumor.temple_blessings"),
            Loc.Get("street_encounter.rumor.fortune_hunting"),
            Loc.Get("street_encounter.rumor.guild_recruits"),
            Loc.Get("street_encounter.rumor.watch_back"),
            Loc.Get("street_encounter.rumor.weapon_shipment"),
            Loc.Get("street_encounter.rumor.secret_passage"),
            Loc.Get("street_encounter.rumor.exotic_goods")
        };

        // Add NPC-specific rumors
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (npcs != null && npcs.Count > 0)
        {
            var randomNPC = npcs[_random.Next(npcs.Count)];
            rumors.Add(Loc.Get("street_encounter.rumor.saw_npc", randomNPC.Name, randomNPC.CurrentLocation ?? "inn"));
            rumors.Add(Loc.Get("street_encounter.rumor.npc_team", randomNPC.Name));
        }

        return rumors[_random.Next(rumors.Count)];
    }

    private List<MerchantItem> GenerateMerchantItems(int playerLevel, bool shady)
    {
        var items = new List<MerchantItem>();
        int potionPrice = 40 + playerLevel * 3;

        if (shady)
        {
            // Shady merchant sells useful but morally questionable items
            items.Add(new MerchantItem { Name = "Poison Vial", Price = 80 + playerLevel * 5, Type = "consumable",
                Description = "Poisons your weapon for your next dungeon fight" });
            items.Add(new MerchantItem { Name = "Smoke Bomb", Price = 60 + playerLevel * 4, Type = "consumable",
                Description = "Guaranteed escape from your next combat" });
            items.Add(new MerchantItem { Name = "Healing Potions (x3)", Price = potionPrice * 3, Type = "consumable",
                Description = "Three healing potions at a discount" });
            items.Add(new MerchantItem { Name = "Dark Tonic", Price = 200 + playerLevel * 10, Type = "consumable",
                Description = $"Restores {30 + playerLevel}% of your max HP right now" });
        }
        else
        {
            // Normal traveling merchant — useful consumables
            items.Add(new MerchantItem { Name = "Healing Potion", Price = potionPrice, Type = "consumable",
                Description = "+1 healing potion" });
            items.Add(new MerchantItem { Name = "Healing Potions (x5)", Price = (long)(potionPrice * 4.5), Type = "consumable",
                Description = "Five potions — buy in bulk and save!" });
            items.Add(new MerchantItem { Name = "Antidote", Price = 50 + playerLevel * 2, Type = "consumable",
                Description = "Cures poison immediately" });
            items.Add(new MerchantItem { Name = "Fortifying Elixir", Price = 150 + playerLevel * 8, Type = "consumable",
                Description = $"Restores {20 + playerLevel / 2}% of your max HP right now" });
        }

        return items;
    }

    private void ApplyMerchantItem(Character player, MerchantItem item)
    {
        switch (item.Name)
        {
            case "Healing Potion":
                if (player.Healing < player.MaxPotions)
                    player.Healing++;
                break;
            case "Healing Potions (x3)":
                player.Healing = Math.Min(player.MaxPotions, player.Healing + 3);
                break;
            case "Healing Potions (x5)":
                player.Healing = Math.Min(player.MaxPotions, player.Healing + 5);
                break;
            case "Antidote":
                player.Poison = 0;
                player.PoisonTurns = 0;
                break;
            case "Poison Vial":
                player.Poison = Math.Max(player.Poison, 3 + player.Level / 5);
                player.PoisonTurns = Math.Max(player.PoisonTurns, 10 + player.Level / 5);
                // Poison applied to the player's weapon concept — stored as a buff
                // The poison value on the player is repurposed here temporarily
                break;
            case "Smoke Bomb":
                // Gives a free flee for next combat — minor darkness boost as token
                player.Darkness += 2;
                break;
            case "Fortifying Elixir":
            {
                int healAmount = (int)(player.MaxHP * (0.20 + player.Level / 200.0));
                player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
                break;
            }
            case "Dark Tonic":
            {
                int healAmount = (int)(player.MaxHP * (0.30 + player.Level / 100.0));
                player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
                player.Darkness += 5;
                break;
            }
        }
    }

    private struct MerchantItem
    {
        public string Name;
        public long Price;
        public string Type;
        public string Description;
    }

    /// <summary>
    /// Attack a specific character in the current location
    /// </summary>
    public async Task<EncounterResult> AttackCharacter(Character player, Character target, TerminalEmulator terminal)
    {
        var result = new EncounterResult { EncounterOccurred = true, Type = EncounterType.HostileNPC };

        terminal.ClearScreen();
        UIHelper.WriteBoxHeader(terminal, Loc.Get("street_encounter.attack.title"), "bright_red");
        terminal.WriteLine("");

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("street_encounter.attack.you_attack", target.Name2));
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
                terminal.WriteLine(Loc.Get("street_encounter.grudge.steps_out", grudgeNpc.Name2));
                terminal.WriteLine(Loc.Get("street_encounter.grudge.hesitates"));
                terminal.SetColor("dark_yellow");
                terminal.WriteLine(Loc.Get("street_encounter.grudge.thinks_better", grudgeNpc.Level, grudgeNpc.Class));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("street_encounter.grudge.self_preservation", levelGap));
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
            terminal.WriteLine(Loc.Get("street_encounter.grudge.guard_rushes"));
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("street_encounter.grudge.guard_strikes", grudgeNpc.Name2, guardDmg));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("street_encounter.grudge.wounded_standing", grudgeNpc.Name2, grudgeNpc.HP, grudgeNpc.MaxHP));
            terminal.WriteLine("");
        }

        if (isMurderRevenge)
        {
            // === MURDER REVENGE — Rage buff, no bribe/apologize ===
            // Apply rage buff
            grudgeNpc.Strength = (long)(grudgeNpc.Strength * (1.0f + GameConfig.MurderGrudgeRageBonusSTR));
            grudgeNpc.HP = (long)Math.Min(grudgeNpc.MaxHP * (1.0f + GameConfig.MurderGrudgeRageBonusHP), grudgeNpc.MaxHP * 1.5f);

            UIHelper.DrawBoxTop(terminal, Loc.Get("street_encounter.grudge.murder_revenge_title"), "dark_red");
            UIHelper.DrawBoxEmpty(terminal, "dark_red");
            UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.grudge.emerges_fury", grudgeNpc.Name2), "dark_red", "white");
            UIHelper.DrawBoxEmpty(terminal, "dark_red");
            UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.grudge.murder_quote_1"), "dark_red", "bright_red");
            UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.grudge.murder_quote_2"), "dark_red", "bright_red");
            UIHelper.DrawBoxEmpty(terminal, "dark_red");
            UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.grudge.enraged_stats", grudgeNpc.Level, grudgeNpc.Class, grudgeNpc.HP, grudgeNpc.MaxHP), "dark_red", "bright_yellow");
            UIHelper.DrawBoxEmpty(terminal, "dark_red");
            UIHelper.DrawBoxSeparator(terminal, "dark_red");
            UIHelper.DrawMenuOption(terminal, "F", Loc.Get("street_encounter.grudge.opt_fight"), "dark_red", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "R", Loc.Get("street_encounter.grudge.opt_run"), "dark_red", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "dark_red");

            var choice = await terminal.GetInput(Loc.Get("street_encounter.grudge.your_response"));

            if (choice.Trim().ToUpper() == "R")
            {
                int fleeChance = Math.Min(50, 20 + (int)(player.Dexterity * 1.5)); // Harder to flee murder revenge
                if (_random.Next(100) < fleeChance)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("street_encounter.grudge.barely_escape", grudgeNpc.Name2));
                }
                else
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine(Loc.Get("street_encounter.grudge.cuts_off_escape", grudgeNpc.Name2));
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
                terminal.WriteLine(Loc.Get("street_encounter.grudge.murder_goes_down", grudgeNpc.Name2));
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
                terminal.WriteLine(Loc.Get("street_encounter.grudge.stands_over", grudgeNpc.Name2));
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("street_encounter.grudge.now_even"));
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("street_encounter.grudge.take_gold", goldTaken));
                result.GoldLost = goldTaken;
                NewsSystem.Instance?.Newsy($"{grudgeNpc.Name2} got bloody revenge on {player.Name2}!");
            }
        }
        else if (isWitnessRevenge)
        {
            // === WITNESS REVENGE — Saw the player murder someone ===
            UIHelper.DrawBoxTop(terminal, Loc.Get("street_encounter.witness.title"), "bright_red");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.witness.steps_in_front", grudgeNpc.Name2), "bright_red", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            if (!string.IsNullOrEmpty(victimName))
                UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.witness.saw_victim", victimName), "bright_red", "bright_cyan");
            else
                UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.witness.saw_murder"), "bright_red", "bright_cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.witness.stats", grudgeNpc.Level, grudgeNpc.Class, grudgeNpc.HP, grudgeNpc.MaxHP), "bright_red", "yellow");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxSeparator(terminal, "bright_red");
            UIHelper.DrawMenuOption(terminal, "F", Loc.Get("street_encounter.grudge.opt_fight"), "bright_red", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "B", Loc.Get("street_encounter.witness.opt_bribe", grudgeNpc.Level * 50), "bright_red", "bright_yellow", "yellow");
            UIHelper.DrawMenuOption(terminal, "R", Loc.Get("street_encounter.grudge.opt_run"), "bright_red", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_red");

            var choice = await terminal.GetInput(Loc.Get("street_encounter.grudge.your_response"));

            switch (choice.Trim().ToUpper())
            {
                case "F":
                    await FightNPC(player, grudgeNpc, result, terminal);
                    if (result.Victory)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("street_encounter.witness.one_less", grudgeNpc.Name2));
                        player.Darkness += 10; // Extra darkness for silencing a witness
                        NewsSystem.Instance?.Newsy($"{player.Name2} defeated {grudgeNpc.Name2} who confronted them!");
                    }
                    else
                    {
                        long goldTaken = player.Gold / 10;
                        player.Gold -= goldTaken;
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("street_encounter.witness.justice_served", grudgeNpc.Name2, goldTaken));
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
                            terminal.WriteLine(Loc.Get("street_encounter.witness.takes_bribe", grudgeNpc.Name2, witnessBribe));
                            terminal.SetColor("gray");
                            terminal.WriteLine(Loc.Get("street_encounter.witness.didnt_see"));
                            result.GoldLost = witnessBribe;
                        }
                        else
                        {
                            player.Gold -= witnessBribe;
                            terminal.SetColor("bright_red");
                            terminal.WriteLine(Loc.Get("street_encounter.witness.gold_buy_silence"));
                            result.GoldLost = witnessBribe;
                            await FightNPC(player, grudgeNpc, result, terminal);
                        }
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("street_encounter.witness.not_enough_attacks", grudgeNpc.Name2));
                        await FightNPC(player, grudgeNpc, result, terminal);
                    }
                    break;

                default: // Run
                    int fleeChance = Math.Min(65, 25 + (int)(player.Dexterity * 2));
                    if (_random.Next(100) < fleeChance)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("street_encounter.witness.slip_away", grudgeNpc.Name2));
                    }
                    else
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine(Loc.Get("street_encounter.witness.catches_guilty", grudgeNpc.Name2));
                        await FightNPC(player, grudgeNpc, result, terminal);
                    }
                    break;
            }
        }
        else
        {
            // === STANDARD GRUDGE — Defeated in combat ===
            UIHelper.DrawBoxTop(terminal, Loc.Get("street_encounter.grudge.standard_title"), "bright_red");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.grudge.waiting_unhappy", grudgeNpc.Name2), "bright_red", "white");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.grudge.think_again"), "bright_red", "bright_cyan");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.witness.stats", grudgeNpc.Level, grudgeNpc.Class, grudgeNpc.HP, grudgeNpc.MaxHP), "bright_red", "yellow");
            UIHelper.DrawBoxEmpty(terminal, "bright_red");
            UIHelper.DrawBoxSeparator(terminal, "bright_red");
            UIHelper.DrawMenuOption(terminal, "F", Loc.Get("street_encounter.grudge.opt_fight"), "bright_red", "bright_yellow", "white");
            UIHelper.DrawMenuOption(terminal, "A", Loc.Get("street_encounter.grudge.opt_apologize"), "bright_red", "bright_yellow", "white");
            long bribeCost = grudgeNpc.Level * 30;
            UIHelper.DrawMenuOption(terminal, "B", Loc.Get("street_encounter.witness.opt_bribe", bribeCost), "bright_red", "bright_yellow", "yellow");
            UIHelper.DrawMenuOption(terminal, "R", Loc.Get("street_encounter.grudge.opt_run"), "bright_red", "bright_yellow", "gray");
            UIHelper.DrawBoxBottom(terminal, "bright_red");

            var choice = await terminal.GetInput(Loc.Get("street_encounter.grudge.your_response"));

            switch (choice.ToUpper())
            {
                case "F": // Fight
                    await FightNPC(player, grudgeNpc, result, terminal);
                    if (result.Victory)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("street_encounter.grudge.settles_that", grudgeNpc.Name2));
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
                        terminal.WriteLine(Loc.Get("street_encounter.grudge.takes_gold_satisfied", grudgeNpc.Name2, goldTaken));
                        result.GoldLost = goldTaken;
                        NewsSystem.Instance?.Newsy($"{grudgeNpc.Name2} got revenge on {player.Name2}!");
                    }
                    break;

                case "A": // Apologize
                    int apologyChance = Math.Min(75, 30 + (int)(player.Charisma * 2));
                    if (_random.Next(100) < apologyChance)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("street_encounter.grudge.lowers_fists", grudgeNpc.Name2));
                        terminal.SetColor("white");
                        terminal.WriteLine(Loc.Get("street_encounter.grudge.dont_cross"));
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
                        terminal.WriteLine(Loc.Get("street_encounter.grudge.sorry_not_enough"));
                        terminal.SetColor("white");
                        terminal.WriteLine(Loc.Get("street_encounter.grudge.attacks_fury", grudgeNpc.Name2));
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
                            terminal.WriteLine(Loc.Get("street_encounter.grudge.pockets_gold", grudgeNpc.Name2, bribeCost));
                            terminal.SetColor("white");
                            terminal.WriteLine(Loc.Get("street_encounter.grudge.even_for_now"));
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
                            terminal.WriteLine(Loc.Get("street_encounter.grudge.takes_gold_attacks", grudgeNpc.Name2));
                            result.GoldLost = bribeCost;
                            await FightNPC(player, grudgeNpc, result, terminal);
                        }
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("street_encounter.grudge.not_enough_attacks", grudgeNpc.Name2));
                        await FightNPC(player, grudgeNpc, result, terminal);
                    }
                    break;

                default: // Run
                    int fleeChance = Math.Min(75, 30 + (int)(player.Dexterity * 2));
                    if (_random.Next(100) < fleeChance)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("street_encounter.grudge.slip_away", grudgeNpc.Name2));
                    }
                    else
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine(Loc.Get("street_encounter.grudge.catches_first_strike", grudgeNpc.Name2));
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
                terminal.WriteLine(Loc.Get("street_encounter.spouse.storms_toward", spouse.Name2));
                terminal.WriteLine(Loc.Get("street_encounter.spouse.stops_short"));
                terminal.SetColor("dark_yellow");
                terminal.WriteLine(Loc.Get("street_encounter.spouse.not_over"));
                await terminal.PressAnyKey();
                result.EncounterOccurred = false;
                return;
            }
        }

        // Find who the player is having an affair with (the spouse's partner)
        string partnerName = RelationshipSystem.GetSpouseName(spouse);

        terminal.ClearScreen();
        UIHelper.DrawBoxTop(terminal, Loc.Get("street_encounter.spouse.title"), "bright_red");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.spouse.fists_clenched", spouse.Name2), "bright_red", "white");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.spouse.know_doing", partnerName), "bright_red", "bright_cyan");
        UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.spouse.find_out"), "bright_red", "cyan");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxSeparator(terminal, "bright_red");
        UIHelper.DrawMenuOption(terminal, "D", Loc.Get("street_encounter.spouse.opt_deny"), "bright_red", "bright_yellow", "white");
        UIHelper.DrawMenuOption(terminal, "A", Loc.Get("street_encounter.spouse.opt_admit"), "bright_red", "bright_yellow", "white");
        UIHelper.DrawMenuOption(terminal, "T", Loc.Get("street_encounter.spouse.opt_taunt"), "bright_red", "bright_yellow", "red");
        UIHelper.DrawMenuOption(terminal, "F", Loc.Get("street_encounter.grudge.opt_fight"), "bright_red", "bright_yellow", "white");
        UIHelper.DrawBoxBottom(terminal, "bright_red");

        var choice = await terminal.GetInput(Loc.Get("street_encounter.grudge.your_response"));

        switch (choice.ToUpper())
        {
            case "D": // Deny
                int denyChance = Math.Min(75, 40 + (int)(player.Charisma * 2));
                if (_random.Next(100) < denyChance)
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine(Loc.Get("street_encounter.spouse.just_friends"));
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("street_encounter.spouse.looks_uncertain", spouse.Name2));

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
                    terminal.WriteLine(Loc.Get("street_encounter.spouse.liar"));
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("street_encounter.spouse.attacks_rage", spouse.Name2));
                    await FightNPC(player, spouse, result, terminal);
                }
                break;

            case "A": // Admit
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("street_encounter.spouse.admit_sorry"));
                terminal.SetColor("yellow");
                long damage = 10 + _random.Next(15);
                player.HP = Math.Max(1, player.HP - damage);
                terminal.WriteLine(Loc.Get("street_encounter.spouse.punches_you", spouse.Name2, damage));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("street_encounter.spouse.stay_away", partnerName));

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
                terminal.WriteLine(Loc.Get("street_encounter.spouse.taunt_better", partnerName));
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("street_encounter.spouse.roars_fury", spouse.Name2));
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
                terminal.WriteLine(Loc.Get("street_encounter.spouse.settle_this"));
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
        UIHelper.DrawBoxTop(terminal, Loc.Get("street_encounter.throne.title"), "bright_yellow");
        UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
        UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.throne.confronts", challenger.Name2, challenger.Level, challenger.Class), "bright_yellow", "white");
        UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
        UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.throne.times_up"), "bright_yellow", "bright_cyan");
        UIHelper.DrawBoxEmpty(terminal, "bright_yellow");
        UIHelper.DrawBoxSeparator(terminal, "bright_yellow");
        UIHelper.DrawMenuOption(terminal, "A", Loc.Get("street_encounter.throne.opt_accept"), "bright_yellow", "bright_yellow", "bright_green");
        UIHelper.DrawMenuOption(terminal, "D", Loc.Get("street_encounter.throne.opt_dismiss"), "bright_yellow", "bright_yellow", "white");
        UIHelper.DrawMenuOption(terminal, "N", Loc.Get("street_encounter.throne.opt_negotiate"), "bright_yellow", "bright_yellow", "white");
        UIHelper.DrawMenuOption(terminal, "I", Loc.Get("street_encounter.throne.opt_imprison"), "bright_yellow", "bright_yellow", "red");
        UIHelper.DrawBoxBottom(terminal, "bright_yellow");

        var choice = await terminal.GetInput(Loc.Get("street_encounter.throne.your_decree"));

        switch (choice.ToUpper())
        {
            case "A": // Accept
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("street_encounter.throne.accept_steel"));
                await FightNPC(player, challenger, result, terminal);

                if (result.Victory)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.still_king", challenger.Name2));
                    player.Fame += 25;

                    // Imprison the challenger
                    NPCSpawnSystem.Instance?.ImprisonNPC(challenger, 7);
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.imprisoned", challenger.Name2, 7));
                    NewsSystem.Instance?.Newsy($"King {player.Name2} defeated {challenger.Name2}'s throne challenge! The challenger is imprisoned.");
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.defeated_claims", challenger.Name2));
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
                        terminal.WriteLine(Loc.Get("street_encounter.throne.guards_remove"));
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("street_encounter.throne.guards_drag", challenger.Name2));
                        NewsSystem.Instance?.Newsy($"King {player.Name2}'s guards repelled {challenger.Name2}'s challenge.");
                    }
                    else
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine(Loc.Get("street_encounter.throne.guards_hesitate"));
                        terminal.SetColor("white");
                        terminal.WriteLine(Loc.Get("street_encounter.throne.face_yourself", challenger.Name2));
                        await FightNPC(player, challenger, result, terminal);
                    }
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.no_guards", challenger.Name2));
                    await FightNPC(player, challenger, result, terminal);
                }
                break;

            case "N": // Negotiate
                int negotiateChance = Math.Min(70, 30 + (int)(player.Charisma * 2));
                if (_random.Next(100) < negotiateChance)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.offer_role"));
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.considers_seat", challenger.Name2));

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
                    terminal.WriteLine(Loc.Get("street_encounter.throne.want_throne"));
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.challenger_attacks", challenger.Name2));
                    await FightNPC(player, challenger, result, terminal);
                }
                break;

            default: // Imprison
                if (king?.Guards != null && king.Guards.Count > 0)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.seize_treason"));

                    NPCSpawnSystem.Instance?.ImprisonNPC(challenger, 14);

                    // Guards lose loyalty (tyrannical act)
                    foreach (var guard in king.Guards)
                        guard.Loyalty = Math.Max(0, guard.Loyalty - 10);

                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.dragged_away", challenger.Name2));
                    player.Darkness += 5;
                    NewsSystem.Instance?.Newsy($"King {player.Name2} imprisoned {challenger.Name2} for challenging the throne.");
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.no_guards_imprison"));
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("street_encounter.throne.laughs_attacks", challenger.Name2));
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
        UIHelper.DrawBoxTop(terminal, Loc.Get("street_encounter.turf.title"), "bright_red");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.turf.surround", rivalTeam), "bright_red", "white");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxLine(terminal, Loc.Get("street_encounter.turf.hold_ends", rival.Name2), "bright_red", "bright_cyan");
        UIHelper.DrawBoxEmpty(terminal, "bright_red");
        UIHelper.DrawBoxSeparator(terminal, "bright_red");
        UIHelper.DrawMenuOption(terminal, "F", Loc.Get("street_encounter.turf.opt_fight"), "bright_red", "bright_yellow", "bright_green");
        long payoffCost = rival.Level * 50;
        UIHelper.DrawMenuOption(terminal, "P", Loc.Get("street_encounter.turf.opt_pay", payoffCost), "bright_red", "bright_yellow", "yellow");
        UIHelper.DrawMenuOption(terminal, "S", Loc.Get("street_encounter.turf.opt_surrender"), "bright_red", "bright_yellow", "gray");
        UIHelper.DrawMenuOption(terminal, "R", Loc.Get("street_encounter.grudge.opt_run"), "bright_red", "bright_yellow", "gray");
        UIHelper.DrawBoxBottom(terminal, "bright_red");

        var choice = await terminal.GetInput(Loc.Get("street_encounter.grudge.your_response"));

        switch (choice.ToUpper())
        {
            case "F": // Fight champion
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("street_encounter.turf.fight_best"));
                await FightNPC(player, rival, result, terminal);

                if (result.Victory)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("street_encounter.turf.backs_off", rival.Name2, rivalTeam));
                    player.Fame += 20;
                    NewsSystem.Instance?.Newsy($"{player.Name2} defended their turf by defeating {rival.Name2} of '{rivalTeam}'!");
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("street_encounter.turf.cheers_lost", rivalTeam));
                    NewsSystem.Instance?.Newsy($"'{rivalTeam}' defeated {player.Name2} in a turf war!");
                }
                break;

            case "P": // Pay off
                if (player.Gold >= payoffCost)
                {
                    player.Gold -= payoffCost;
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("street_encounter.turf.hand_over", payoffCost));
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("street_encounter.turf.smart_move", rival.Name2));
                    result.GoldLost = payoffCost;
                    NewsSystem.Instance?.Newsy($"{player.Name2} paid off '{rivalTeam}' to avoid a turf war.");
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("street_encounter.merchant.not_enough_gold"));
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("street_encounter.turf.hard_way", rival.Name2));
                    await FightNPC(player, rival, result, terminal);
                }
                break;

            case "S": // Surrender
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("street_encounter.turf.surrender"));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("street_encounter.turf.takes_control", rivalTeam));
                player.Fame = Math.Max(0, player.Fame - 10);
                NewsSystem.Instance?.Newsy($"{player.Name2} surrendered turf to '{rivalTeam}' without a fight.");
                break;

            default: // Run
                int fleeChance = Math.Min(75, 30 + (int)(player.Dexterity * 2));
                if (_random.Next(100) < fleeChance)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("street_encounter.turf.dodge_escape"));
                }
                else
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine(Loc.Get("street_encounter.turf.cut_off", rival.Name2));
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
        monster.IsProperName = true; // NPC — no "The" prefix
        monster.CanSpeak = true;     // NPCs can speak

        // Show backstab message
        if (backstabBonus > 0.15f)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("street_encounter.murder.assassin_strike", (int)(backstabBonus * 100)));
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("street_encounter.murder.off_guard", (int)(backstabBonus * 100)));
        }
        terminal.WriteLine("");

        // Combat (include companions and bodyguards)
        var murderTeammates = GetStreetCombatTeammates(player);
        var combatEngine = new CombatEngine(terminal);
        var combatResult = await combatEngine.PlayerVsMonster(player, monster, murderTeammates);

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
                // realNpc.IsPermaDead = true;  // Disabled — NPCs now respawn after death
            }

            // === GOLD THEFT ===
            long stolenGold = (long)(npc.Gold * GameConfig.MurderGoldTheftPercent);
            if (stolenGold > 0)
            {
                player.Gold += stolenGold;
                if (realNpc != null) realNpc.Gold -= stolenGold;
            }
            result.GoldGained = stolenGold;

            // === XP REWARD (combat engine already awarded XP — no double reward) ===
            long expGain = combatResult.ExperienceGained;
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
                terminal.WriteLine(Loc.Get("street_encounter.murder.witnesses", witnesses.Count));
            }

            // === QUEST COMPLETION ===
            string npcNameForBounty = npc.Name ?? npc.Name2 ?? "";

            // IMPORTANT: Check bounty initiator BEFORE AutoCompleteBountyForNPC, which marks
            // the quest as Deleted. GetActiveBountyInitiator filters on !Deleted, so checking
            // after completion would always return null — causing blood price on sanctioned kills.
            string npcNameForBloodPrice = npc.Name2 ?? npc.Name ?? "";
            var bountyInitiator = QuestSystem.GetActiveBountyInitiator(player.Name2, npcNameForBloodPrice);

            long bountyReward = QuestSystem.AutoCompleteBountyForNPC(player, npcNameForBounty);
            QuestSystem.OnNPCDefeated(player, npc);

            // Track whether this was a sanctioned kill (bounty/contract)
            result.WasBountyKill = bountyInitiator != null;

            if (bountyReward > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("street.fight.bounty_collected", bountyReward.ToString("N0")));
                result.GoldGained += bountyReward;
            }

            // === NEWS (permadeath — this one isn't coming back) ===
            NewsSystem.Instance?.Newsy(
                $"\u2620 {player.Name2} murdered {npc.Name2 ?? npc.Name} in cold blood! They will not return.");

            // No respawn queue — deliberate murder is always permanent (IsPermaDead blocks respawn)

            // === BLOOD PRICE (adjusted by bounty type) ===
            if (realNpc != null)
            {

                if (bountyInitiator == GameConfig.FactionInitiatorCrown
                    || bountyInitiator == "The Crown"   // King bounties
                    || bountyInitiator == "Bounty Board")
                {
                    // Crown/King bounties are state-sanctioned — no blood price
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("street_encounter.murder.sanctioned"));
                }
                else if (bountyInitiator == GameConfig.FactionInitiatorShadows)
                {
                    // Shadows contract — professional hit, reduced weight with chance to skip
                    if (_random.NextDouble() < GameConfig.ShadowContractBloodPriceSkipChance)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("street_encounter.murder.clean_job"));
                    }
                    else
                    {
                        WorldSimulator.ApplyBloodPrice(player, realNpc, GameConfig.MurderWeightPerShadowContract, isDeliberate: true);
                        terminal.SetColor("dark_red");
                        terminal.WriteLine(Loc.Get("street_encounter.murder.see_face"));
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
                    terminal.WriteLine(Loc.Get("street_encounter.murder.faction_drop", victimFaction));
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

            // Prevent save cheesing — persist negative outcomes immediately
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                _ = GameEngine.Instance.SaveCurrentGame();
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
    public bool WasBountyKill { get; set; }
    public string Message { get; set; } = "";
    public long GoldLost { get; set; }
    public long GoldGained { get; set; }
    public long ExperienceGained { get; set; }
    public List<string> Log { get; set; } = new List<string>();
}
