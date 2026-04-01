using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/// <summary>
/// The Inn location - social hub with Seth Able, drinking, and team activities
/// Based on Pascal INN.PAS and INNC.PAS
/// </summary>
public class InnLocation : BaseLocation
{
    private NPC sethAble = null!;
    private bool sethAbleAvailable = true;
    private int sethFightsToday = 0;     // Daily fight counter - max 3 per day
    private int sethDefeatsTotal = 0;    // Total times player has beaten Seth this session
    private int lastSethFightDay = -1;   // Track which game day the fights counter is for
    
    public InnLocation() : base(
        GameLocation.TheInn,
        "The Inn",
        "You enter the smoky tavern. The air is thick with the smell of ale and the sound of rowdy conversation."
    ) { }
    
    protected override string[]? GetAmbientMessages() => new[]
    {
        "The hearth crackles and spits a shower of sparks.",
        "Distant laughter erupts from a corner table.",
        "The smell of stale ale and pipe smoke hangs in the air.",
        "A bard strums somewhere, barely audible over the din.",
        "The floorboards creak as someone shuffles past.",
        "A mug is slammed on the bar with a satisfied thud.",
        "The door swings open and closed, letting in a gust of cold air.",
    };

    protected override void SetupLocation()
    {
        // Pascal-compatible exits from ONLINE.PAS onloc_theinn case
        PossibleExits = new List<GameLocation>
        {
            GameLocation.MainStreet    // loc1 - back to main street
        };
        
        // Inn-specific actions
        LocationActions = new List<string>
        {
            "Buy a drink (5 gold)",         // Drinking system
            "Challenge Seth Able",          // Fight Seth Able
            "Talk to patrons",              // Social interaction  
            "Play drinking game",           // Drinking competition
            "Listen to gossip",             // Information gathering (real simulation events)
            "Rest at table",                // Minor healing
            "Order food (10 gold)"          // Stamina boost
        };
        
        // Create Seth Able NPC
        CreateSethAble();
    }
    
    /// <summary>
    /// Create the famous Seth Able NPC
    /// </summary>
    private void CreateSethAble()
    {
        sethAble = new NPC("Seth Able", "drunk_fighter", CharacterClass.Warrior, 15)
        {
            IsSpecialNPC = true,
            SpecialScript = "drunk_fighter",
            IsHostile = false,
            CurrentLocation = "Inn"
        };
        
        // Set Seth Able's stats (he's tough!)
        sethAble.Strength = 45;
        sethAble.Defence = 35;
        sethAble.HP = 200;
        sethAble.MaxHP = 200;
        sethAble.Level = 15;
        sethAble.Experience = 50000;
        sethAble.Gold = 1000;
        
        // Seth is usually drunk
        sethAble.Mental = 30; // Poor mental state from drinking
        
        AddNPC(sethAble);
    }
    
    /// <summary>
    /// Override entry to check for Aldric's bandit defense event
    /// </summary>
    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        await base.EnterLocation(player, term);

        // Check if Aldric bandit event should trigger (only once per session)
        await CheckAldricBanditEvent();
    }

    /// <summary>
    /// Flag to track if bandit event already triggered this session
    /// </summary>
    private bool aldricBanditEventTriggered = false;

    /// <summary>
    /// Check if Aldric's recruitment event should trigger
    /// Aldric defends the player from bandits in the tavern
    /// </summary>
    private async Task CheckAldricBanditEvent()
    {
        // Only trigger if:
        // 1. Player is at least level 10 (Aldric's recruit level)
        // 2. Aldric has NOT been recruited yet
        // 3. Aldric is NOT dead
        // 4. Event hasn't triggered this session
        // 5. 20% chance each visit
        if (aldricBanditEventTriggered) return;

        var aldric = CompanionSystem.Instance.GetCompanion(CompanionId.Aldric);
        if (aldric == null || aldric.IsRecruited || aldric.IsDead) return;
        if (currentPlayer.Level < aldric.RecruitLevel) return;

        // 20% chance to trigger the event
        var random = Random.Shared;
        if (random.NextDouble() > 0.20) return;

        aldricBanditEventTriggered = true;
        await TriggerAldricBanditEvent(aldric);
    }

    /// <summary>
    /// Trigger the Aldric bandit defense event
    /// </summary>
    private async Task TriggerAldricBanditEvent(Companion aldric)
    {
        terminal.ClearScreen();

        // Dramatic encounter
        WriteBoxHeader(Loc.Get("inn.trouble"), "red");
        terminal.WriteLine("");

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.bandit.sitting_at_bar"));
        terminal.WriteLine(Loc.Get("inn.bandit.bandits_enter"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("inn.bandit.leader_threat1"));
        terminal.WriteLine(Loc.Get("inn.bandit.leader_threat2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.bandit.draw_weapons"));
        terminal.WriteLine(Loc.Get("inn.bandit.patrons_scatter"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Aldric intervenes
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("inn.bandit.chair_scrapes"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("inn.bandit.stranger_rises"));
        terminal.WriteLine(Loc.Get("inn.bandit.tattered_armor"));
        terminal.WriteLine(Loc.Get("inn.bandit.battered_shield"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("inn.bandit.aldric_sporting"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("inn.bandit.leader_stay_out"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("inn.bandit.aldric_i_am_trouble"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        // Battle description
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("inn.bandit.stranger_efficiency"));
        terminal.WriteLine(Loc.Get("inn.bandit.shield_deflects"));
        terminal.WriteLine(Loc.Get("inn.bandit.strike_sprawling"));
        terminal.WriteLine(Loc.Get("inn.bandit.leader_flees"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.bandit.wipes_blood"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("inn.bandit.aldric_you_alright", currentPlayer.Name2 ?? currentPlayer.Name1));
        terminal.WriteLine(Loc.Get("inn.bandit.aldric_reputation"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("inn.bandit.extends_hand"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("inn.bandit.aldric_name"));
        terminal.WriteLine(Loc.Get("inn.bandit.aldric_purpose"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.bandit.glances_appraisingly"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("inn.bandit.aldric_shield_back"));
        terminal.WriteLine(Loc.Get("inn.bandit.aldric_protecting"));
        terminal.WriteLine("");

        await Task.Delay(1000);

        // Recruitment choice
        terminal.SetColor("bright_yellow");
        if (IsScreenReader)
        {
            terminal.WriteLine($"Y. {Loc.Get("inn.bandit.accept_aldric")}");
            terminal.WriteLine($"N. {Loc.Get("inn.bandit.decline_aldric")}");
        }
        else
        {
            terminal.WriteLine($"[Y] {Loc.Get("inn.bandit.accept_aldric")}");
            terminal.WriteLine($"[N] {Loc.Get("inn.bandit.decline_aldric")}");
        }
        terminal.WriteLine("");

        var choice = await GetChoice();

        if (choice.ToUpper() == "Y")
        {
            bool success = await CompanionSystem.Instance.RecruitCompanion(CompanionId.Aldric, currentPlayer, terminal);
            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("inn.bandit.aldric_nods_solemnly"));
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("inn.bandit.aldric_find_trouble"));
                terminal.WriteLine(Loc.Get("inn.bandit.aldric_got_your_back"));
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("inn.bandit.aldric_joined"));
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("inn.bandit.aldric_tank_hint"));
            }
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("inn.bandit.aldric_disappointed"));
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("inn.bandit.aldric_understand"));
            terminal.WriteLine(Loc.Get("inn.bandit.aldric_change_mind"));
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.bandit.aldric_recruit_later"));
        }

        await terminal.PressAnyKey();
    }

    protected override void DisplayLocation()
    {
        if (IsBBSSession) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        // Inn header - standardized format
        WriteBoxHeader(Loc.Get("inn.header"), "bright_cyan", 77);
        terminal.WriteLine("");
        
        // Atmospheric description
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.desc_dimly_lit"));
        terminal.WriteLine(Loc.Get("inn.desc_bartender"));
        terminal.WriteLine("");

        // Special Seth Able description
        if (sethAbleAvailable)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.desc_seth_hunched"));
            terminal.WriteLine(Loc.Get("inn.desc_seth_bloodshot"));
            terminal.WriteLine("");
        }
        
        // Show other NPCs
        ShowNPCsInLocation();

        // Aldric companion teaser — one-time sighting before recruitment level (v0.49.6)
        if (currentPlayer != null && currentPlayer.Level >= 3 && CompanionSystem.Instance != null
            && !(CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)?.IsRecruited ?? true)
            && !(CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)?.IsDead ?? true)
            && !currentPlayer.HintsShown.Contains(HintSystem.HINT_COMPANION_ALDRIC_TEASER))
        {
            currentPlayer.HintsShown.Add(HintSystem.HINT_COMPANION_ALDRIC_TEASER);
            terminal.SetColor("dark_yellow");
            terminal.WriteLine($"  {Loc.Get("inn.teaser_scarred_soldier")}");
            terminal.WriteLine($"  {Loc.Get("inn.teaser_seen_fights")}");
            terminal.SetColor("white");
            terminal.WriteLine("");
        }

        // Show inn-specific menu
        ShowInnMenu();

        // Status line
        ShowStatusLine();
    }
    
    /// <summary>
    /// Show Inn-specific menu options
    /// </summary>
    private void ShowInnMenu()
    {
        // Check for recruitable companions (needed by both branches)
        var recruitableCompanions = CompanionSystem.Instance.GetRecruitableCompanions(currentPlayer?.Level ?? 1).ToList();
        var recruitedCompanions = CompanionSystem.Instance.GetAllCompanions()
            .Where(c => c.IsRecruited && !c.IsDead).ToList();

        if (IsScreenReader)
        {
            terminal.WriteLine(Loc.Get("inn.activities"));
            terminal.WriteLine("");
            WriteSRMenuOption("D", $"{Loc.Get("inn.drink")} (5 {Loc.Get("status.gold_label")})");
            WriteSRMenuOption("T", Loc.Get("inn.talk"));
            WriteSRMenuOption("F", Loc.Get("inn.fight_seth"));
            WriteSRMenuOption("G", Loc.Get("inn.drinking_game"));
            WriteSRMenuOption("U", Loc.Get("inn.gossip"));
            WriteSRMenuOption("E", Loc.Get("inn.rest"));
            WriteSRMenuOption("O", $"{Loc.Get("inn.order_food")} (10 {Loc.Get("status.gold_label")})");
            terminal.WriteLine("");

            if (recruitableCompanions.Any())
                terminal.WriteLine(Loc.Get("inn.stranger_noticed"));
            if (recruitedCompanions.Any())
                terminal.WriteLine(Loc.Get("inn.companions_resting", recruitedCompanions.Count));
            if (recruitableCompanions.Any() || recruitedCompanions.Any())
                terminal.WriteLine("");

            terminal.WriteLine(Loc.Get("inn.special_areas"));
            WriteSRMenuOption("W", Loc.Get("inn.train"));
            WriteSRMenuOption("L", Loc.Get("inn.gambling"));
            if (recruitableCompanions.Any())
                WriteSRMenuOption("A", Loc.Get("inn.approach_stranger", recruitableCompanions.Count));
            if (recruitedCompanions.Any())
                WriteSRMenuOption("P", Loc.Get("inn.manage_party", recruitedCompanions.Count));
            terminal.WriteLine("");

            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                long roomCost = (long)(currentPlayer.Level * GameConfig.InnRoomCostPerLevel);
                WriteSRMenuOption("N", Loc.Get("inn.rent_room_logout", roomCost));
                WriteSRMenuOption("K", Loc.Get("inn.attack_sleeper"));
            }
            if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
            {
                if (DailySystemManager.CanRestForNight(currentPlayer))
                    WriteSRMenuOption("Z", Loc.Get("inn.sleep_morning"));
                else
                    WriteSRMenuOption("Z", Loc.Get("inn.wait_night"));
            }

            terminal.WriteLine(Loc.Get("inn.navigation"));
            WriteSRMenuOption("R", Loc.Get("inn.return"));
            WriteSRMenuOption("S", Loc.Get("menu.action.status"));
            WriteSRMenuOption("?", Loc.Get("menu.action.help"));
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.activities"));
            terminal.WriteLine("");

            // Row 1
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write($"{Loc.Get("inn.drink")} (5 {Loc.Get("status.gold_label")})      ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.talk"));

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write($"{Loc.Get("inn.fight_seth")}       ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.drinking_game"));

            // Row 3
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("U");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write($"{Loc.Get("inn.gossip")}          ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.rest"));

            // Row 4
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("O");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine($"{Loc.Get("inn.order_food")} (10 {Loc.Get("status.gold_label")})");
            terminal.WriteLine("");

            if (recruitableCompanions.Any())
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("inn.stranger_noticed"));
                terminal.WriteLine("");
            }

            if (recruitedCompanions.Any())
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("inn.companions_resting", recruitedCompanions.Count));
                terminal.WriteLine("");
            }

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("inn.special_areas"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("W");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.train"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.gambling"));

            // Show companion option if available
            if (recruitableCompanions.Any())
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("A");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("inn.approach_stranger", recruitableCompanions.Count));
            }

            // Show party management if player has companions
            if (recruitedCompanions.Any())
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("P");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("inn.manage_party", recruitedCompanions.Count));
            }
            terminal.WriteLine("");

            // Online mode options: Rent a Room + Attack a Sleeper
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                long roomCost = (long)(currentPlayer.Level * GameConfig.InnRoomCostPerLevel);
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("N");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("bright_green");
                terminal.Write($"{Loc.Get("inn.rent_room_logout", roomCost)}    ");

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("K");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("inn.attack_sleeper"));
            }

            // Single-player: Sleep/Wait option
            if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("Z");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                if (DailySystemManager.CanRestForNight(currentPlayer))
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("inn.sleep_morning"));
                }
                else
                {
                    terminal.SetColor("dark_cyan");
                    terminal.WriteLine(Loc.Get("inn.wait_night"));
                }
            }

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.navigation"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("red");
            terminal.Write($"{Loc.Get("inn.return")}    ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write($"{Loc.Get("menu.action.status")}    ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("?");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("menu.action.help"));
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals.
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();

        // Header
        ShowBBSHeader(Loc.Get("inn.header"));

        // 1-line description
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("inn.bbs_desc")}");

        // NPCs
        ShowBBSNPCs();

        // Companions hint
        var recruitableCompanions = CompanionSystem.Instance.GetRecruitableCompanions(currentPlayer?.Level ?? 1).ToList();
        var recruitedCompanions = CompanionSystem.Instance.GetAllCompanions()
            .Where(c => c.IsRecruited && !c.IsDead).ToList();
        if (recruitableCompanions.Any())
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($" {Loc.Get("inn.stranger_noticed")}");
        }
        if (recruitedCompanions.Any())
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($" {Loc.Get("inn.companions_resting", recruitedCompanions.Count)}");
        }

        terminal.WriteLine("");

        // Menu rows
        terminal.SetColor("yellow");
        terminal.WriteLine($" {Loc.Get("inn.activities")}");
        ShowBBSMenuRow(("D", "bright_yellow", Loc.Get("inn.bbs_drink")), ("F", "bright_yellow", Loc.Get("inn.bbs_seth")), ("T", "bright_yellow", Loc.Get("inn.bbs_talk")), ("G", "bright_yellow", Loc.Get("inn.bbs_drinking_game")));
        ShowBBSMenuRow(("U", "bright_yellow", Loc.Get("inn.bbs_gossip")), ("E", "bright_yellow", Loc.Get("inn.bbs_rest")), ("O", "bright_yellow", Loc.Get("inn.bbs_food")));

        terminal.SetColor("cyan");
        terminal.WriteLine($" {Loc.Get("inn.special_areas")}");
        ShowBBSMenuRow(("W", "bright_yellow", Loc.Get("inn.bbs_train")), ("L", "bright_yellow", Loc.Get("inn.bbs_gambling")));
        if (recruitableCompanions.Any() || recruitedCompanions.Any())
        {
            var items = new List<(string, string, string)>();
            if (recruitableCompanions.Any())
                items.Add(("A", "bright_yellow", $"Stranger({recruitableCompanions.Count})"));
            if (recruitedCompanions.Any())
                items.Add(("P", "bright_yellow", $"Party({recruitedCompanions.Count})"));
            ShowBBSMenuRow(items.ToArray());
        }

        // Online-mode options
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            long roomCost = (long)(currentPlayer.Level * GameConfig.InnRoomCostPerLevel);
            ShowBBSMenuRow(("N", "bright_yellow", $"Room({roomCost}g)"), ("K", "bright_yellow", "Attack Sleeper"));
        }

        // Single-player: Sleep/Wait option
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
        {
            string zLabel = DailySystemManager.CanRestForNight(currentPlayer) ? "Sleep" : "Wait";
            ShowBBSMenuRow(("Z", "bright_yellow", zLabel), ("R", "bright_yellow", "eturn"));
        }
        else
        {
            ShowBBSMenuRow(("R", "bright_yellow", "eturn"));
        }

        // Footer: status + quick commands
        ShowBBSFooter();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        switch (upperChoice)
        {
            case "D":
                await BuyDrink();
                return false;
                
            case "F":
                await ChallengeSethAble();
                return false;
                
            case "T":
                await TalkToPatrons();
                return false;
                
            case "G":
                await PlayDrinkingGame();
                return false;
                
            case "U":
                await ListenToRumors();
                return false;

            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "E":
                await RestAtTable();
                return false;
                
            case "O":
                await OrderFood();
                return false;
                
            case "A":
                await ApproachCompanions();
                return false;

            case "P":
                await ManageParty();
                return false;

            case "W":
                await HandleStatTraining();
                return false;

            case "L":
                await HandleGamblingDen();
                return false;

            case "N":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    await RentRoom();
                return false;

            case "K":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    await AttackInnSleeper();
                return false;

            case "Z":
                if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
                {
                    if (DailySystemManager.CanRestForNight(currentPlayer))
                        await SleepAtInn();
                    else
                        await DailySystemManager.Instance.WaitUntilEvening(currentPlayer, terminal);
                }
                return false;

            case "Q":
            case "M":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "S":
                await ShowStatus();
                return false;
                
            case "?":
                // Menu already shown
                return false;

            case "0":
                // Talk to NPC (standard "0" option from BaseLocation)
                await TalkToPatrons();
                return false;

            default:
                terminal.WriteLine(Loc.Get("inn.invalid_choice"), "red");
                await Task.Delay(1500);
                return false;
        }
    }
    
    /// <summary>
    /// Buy a drink at the inn
    /// </summary>
    private async Task BuyDrink()
    {
        long drinkBasePrice = 5;
        var (drinkKingTax, drinkCityTax, drinkTotalWithTax) = CityControlSystem.CalculateTaxedPrice(drinkBasePrice);

        if (currentPlayer.Gold < drinkTotalWithTax)
        {
            terminal.WriteLine(Loc.Get("ui.not_enough_gold_drink"), "red");
            await Task.Delay(2000);
            return;
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Drink", drinkBasePrice);

        currentPlayer.Gold -= drinkTotalWithTax;
        CityControlSystem.Instance.ProcessSaleTax(drinkBasePrice);
        currentPlayer.DrinksLeft--;
        
        terminal.SetColor("green");
        terminal.WriteLine(Loc.Get("inn.drink_order_ale"));
        terminal.WriteLine(Loc.Get("inn.drink_bitter_brew"));

        // Random drink effects
        var effect = Random.Shared.Next(1, 5);
        switch (effect)
        {
            case 1:
                terminal.WriteLine(Loc.Get("inn.drink_effect_charisma"));
                currentPlayer.Charisma += 2;
                break;
            case 2:
                terminal.WriteLine(Loc.Get("inn.drink_effect_strength"));
                currentPlayer.Strength += 1;
                break;
            case 3:
                terminal.WriteLine(Loc.Get("inn.drink_effect_wisdom"));
                currentPlayer.Wisdom = Math.Max(1, currentPlayer.Wisdom - 1);
                break;
            case 4:
                terminal.WriteLine(Loc.Get("inn.drink_effect_hp"));
                currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + 5);
                break;
        }
        
        await Task.Delay(2500);
    }
    
    /// <summary>
    /// Challenge Seth Able to a fight
    /// Max 3 fights per game day. Seth scales to player level so he's always a challenge.
    /// </summary>
    private async Task ChallengeSethAble()
    {
        if (!sethAbleAvailable)
        {
            terminal.WriteLine(Loc.Get("inn.seth_passed_out"), "gray");
            await Task.Delay(1500);
            return;
        }

        // Reset daily counter if new day
        int today = DailySystemManager.Instance?.CurrentDay ?? 0;
        if (today != lastSethFightDay)
        {
            sethFightsToday = 0;
            lastSethFightDay = today;
            sethAbleAvailable = true; // Seth recovers each new day
        }

        // Daily fight limit: 3 per day
        var sethFights = DoorMode.IsOnlineMode ? currentPlayer.SethFightsToday : sethFightsToday;
        if (sethFights >= 3)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.seth_waves_off"));
            terminal.WriteLine(Loc.Get("inn.seth_enough"), "yellow");
            terminal.WriteLine(Loc.Get("inn.seth_come_back"), "yellow");
            await Task.Delay(2000);
            return;
        }

        // Calculate Seth's level for display - he scales with player
        int sethLevel = GetSethLevel();

        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("inn.challenging_seth"), "red");
        terminal.WriteLine("");

        // Seth's drunken response
        var responses = new[]
        {
            "*hiccup* You want a piece of me?!",
            "You lookin' at me funny, stranger?",
            "*burp* Think you can take the great Seth Able?",
            "I'll show you what a REAL fighter can do!",
            "*sways* Come on then, if you think you're hard enough!"
        };

        terminal.SetColor("yellow");
        terminal.WriteLine($"Seth Able: \"{responses[Random.Shared.Next(0, responses.Length)]}\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.seth_warning"));
        terminal.WriteLine(Loc.Get("inn.seth_stats_display", sethLevel, GetSethHP(sethLevel)));
        terminal.WriteLine(Loc.Get("inn.seth_player_stats", currentPlayer.Level, currentPlayer.HP, currentPlayer.MaxHP));
        if (sethFights > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.seth_fights_today", sethFights));
        }
        terminal.WriteLine("");

        var confirm = await terminal.GetInput(Loc.Get("ui.confirm_fight"));

        if (confirm.ToUpper() == "Y")
        {
            await FightSethAble();
        }
        else
        {
            terminal.WriteLine(Loc.Get("inn.seth_coward"), "yellow");
            await Task.Delay(2000);
        }
    }

    /// <summary>
    /// Get Seth's effective level - scales with player but always 2-5 levels ahead
    /// Minimum level 15 (his base), scales to always be a challenge
    /// </summary>
    private int GetSethLevel()
    {
        int playerLevel = (int)currentPlayer.Level;
        // Seth is always 3 levels above player, minimum 15, max 80
        return Math.Clamp(playerLevel + 3, 15, 80);
    }

    /// <summary>
    /// Get Seth's HP for a given level
    /// </summary>
    private static long GetSethHP(int sethLevel)
    {
        return 100 + sethLevel * 12;
    }

    /// <summary>
    /// Fight Seth Able using full combat engine.
    /// Seth scales to player level so he's always a genuine challenge.
    /// Uses nr:1 to prevent inflated XP from level-based formulas.
    /// </summary>
    private async Task FightSethAble()
    {
        terminal.WriteLine(Loc.Get("inn.seth_inn_falls_silent"), "red");
        await Task.Delay(2000);

        int sethLevel = GetSethLevel();
        long sethHP = GetSethHP(sethLevel);
        // Stats scale with level: always a tough brawler
        long sethStr = 20 + sethLevel;
        long sethDef = 10 + sethLevel / 2;
        long sethPunch = 20 + sethLevel;
        long sethArmPow = 8 + sethLevel / 3;
        long sethWeapPow = 15 + sethLevel / 2;

        // nr:1 keeps monster Level=1 so GetExperienceReward()/GetGoldReward() yield
        // minimal base rewards. The real reward is the flat bonus below.
        var sethMonster = Monster.CreateMonster(
            nr: 1,
            name: "Seth Able",
            hps: sethHP,
            strength: sethStr,
            defence: sethDef,
            phrase: "You lookin' at me funny?!",
            grabweap: false,
            grabarm: false,
            weapon: "Massive Fists",
            armor: "Thick Skin",
            poisoned: false,
            disease: false,
            punch: sethPunch,
            armpow: sethArmPow,
            weappow: sethWeapPow
        );

        // Override display level (for UI) without affecting reward formulas
        // Note: CreateMonster sets Level = Math.Max(1, nr), so Level=1 for rewards
        sethMonster.IsUnique = true;
        sethMonster.IsBoss = false;
        sethMonster.CanSpeak = true;

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsMonster(currentPlayer, sethMonster);

        if (DoorMode.IsOnlineMode)
            currentPlayer.SethFightsToday++;
        else
            sethFightsToday++;

        if (result.ShouldReturnToTemple)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.awaken_temple"));
            await Task.Delay(2000);
            await NavigateToLocation(GameLocation.Temple);
            return;
        }

        switch (result.Outcome)
        {
            case CombatOutcome.Victory:
                sethDefeatsTotal++;
                terminal.SetColor("bright_green");
                terminal.WriteLine("");

                if (sethDefeatsTotal == 1)
                {
                    terminal.WriteLine(Loc.Get("inn.seth_incredible"));
                    terminal.WriteLine(Loc.Get("inn.seth_shocked_silence"));
                    terminal.WriteLine(Loc.Get("inn.seth_bartender_drops"));
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("inn.seth_legend"));
                    currentPlayer.PKills++;
                    currentPlayer.Fame += 10;
                    currentPlayer.Chivalry += 5;
                }
                else
                {
                    terminal.WriteLine(Loc.Get("inn.seth_beaten_again"));
                    terminal.WriteLine(Loc.Get("inn.seth_patrons_cheer"));
                    // Diminishing fame - 1 point after first win
                    currentPlayer.Fame += 1;
                }

                // Flat reward: modest XP and gold, NOT scaling with fake level
                // This replaces the combat engine's level-based reward (which is tiny at nr=1)
                long xpReward = currentPlayer.Level * 200;
                long goldReward = 50 + currentPlayer.Level * 5;

                // Diminishing returns: halve rewards after 3rd lifetime win
                if (sethDefeatsTotal > 3)
                {
                    xpReward /= 2;
                    goldReward /= 2;
                }

                currentPlayer.Experience += xpReward;
                currentPlayer.Gold += goldReward;

                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("inn.seth_earn_reward", $"{xpReward:N0}", $"{goldReward:N0}"));

                // Seth is knocked out for the rest of the day
                sethAbleAvailable = false;
                sethAble.SetState(NPCState.Unconscious);
                break;

            case CombatOutcome.PlayerDied:
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("inn.seth_knocks_out"));
                terminal.WriteLine(Loc.Get("inn.seth_massive_headache"));
                currentPlayer.HP = 1;
                currentPlayer.PDefeats++;
                break;

            case CombatOutcome.PlayerEscaped:
                terminal.SetColor("yellow");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("inn.seth_back_away"));
                terminal.WriteLine(Loc.Get("inn.seth_walk_away"));
                terminal.WriteLine(Loc.Get("inn.seth_chuckle_retreat"));
                break;

            default:
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("inn.seth_fist_connects"));
                terminal.WriteLine(Loc.Get("inn.seth_crash_table"));
                terminal.WriteLine(Loc.Get("inn.seth_patrons_laugh"));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("inn.seth_next_time"));
                currentPlayer.PDefeats++;
                break;
        }

        await Task.Delay(3000);
    }
    
    /// <summary>
    /// Override base TalkToNPC so the global [0] command routes through the Inn's
    /// patron interaction flow instead of the generic base location Talk screen.
    /// Without this, [0] in TryProcessGlobalCommand calls base TalkToNPC() directly,
    /// bypassing the Inn's TalkToPatrons() and showing stale relationship labels.
    /// </summary>
    protected override async Task TalkToNPC()
    {
        await TalkToPatrons();
    }

    /// <summary>
    /// Talk to other patrons - now with interactive NPC selection
    /// </summary>
    private async Task TalkToPatrons()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("inn.mingle_patrons"), "cyan");
        terminal.WriteLine("");

        // Get live NPCs at the Inn
        var npcsHere = GetLiveNPCsAtLocation();

        if (npcsHere.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.patrons_quiet"));
            await terminal.PressAnyKey();
            return;
        }

        // Show NPCs with interaction options
        int displayCount = Math.Min(npcsHere.Count, 8);
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("inn.patrons_following")} ({displayCount})");
        terminal.WriteLine("");

        for (int i = 0; i < displayCount; i++)
        {
            var npc = npcsHere[i];
            var alignColor = npc.Darkness > npc.Chivalry ? "red" : (npc.Chivalry > 500 ? "bright_green" : "cyan");
            terminal.SetColor(alignColor);
            terminal.WriteLine(IsScreenReader
                ? $"  {i + 1}. {npc.Name2} - {Loc.Get("inn.npc_level_class", npc.Level, npc.ClassName)} ({GetAlignmentDisplay(npc)})"
                : $"  [{i + 1}] {npc.Name2} - {Loc.Get("inn.npc_level_class", npc.Level, npc.ClassName)} ({GetAlignmentDisplay(npc)})");
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(IsScreenReader ? $"0. {Loc.Get("inn.return_to_menu")}" : $"[0] {Loc.Get("inn.return_to_menu")}");
        terminal.WriteLine("");

        var choice = await terminal.GetInput($"Choose (0-{displayCount}): ");

        if (int.TryParse(choice, out int npcIndex) && npcIndex > 0 && npcIndex <= displayCount)
        {
            await InteractWithNPC(npcsHere[npcIndex - 1]);
        }
    }

    /// <summary>
    /// Interactive menu for NPC interaction (Inn-specific override)
    /// Uses the VisualNovelDialogueSystem for full romance features
    /// </summary>
    protected override async Task InteractWithNPC(NPC npc)
    {
        bool continueInteraction = true;

        while (continueInteraction)
        {
            terminal.ClearScreen();
            WriteSectionHeader(Loc.Get("inn.interacting_with", npc.Name2), "bright_cyan");
            terminal.WriteLine("");

            // Show NPC info
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("inn.npc_level_class", npc.Level, npc.Class)}");
            terminal.WriteLine($"  {GetNPCMood(npc)}");
            terminal.WriteLine("");

            // Get relationship status
            var relationship = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
            terminal.SetColor(GetRelationshipColor(relationship));
            terminal.WriteLine($"  {Loc.Get("inn.interact_relationship", GetRelationshipText(relationship))}");

            // Show alignment compatibility
            var reactionMod = AlignmentSystem.Instance.GetNPCReactionModifier(currentPlayer, npc);
            if (reactionMod >= 1.3f)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {Loc.Get("inn.alignment_kindred")}");
            }
            else if (reactionMod >= 1.0f)
            {
                terminal.SetColor("green");
                terminal.WriteLine($"  {Loc.Get("inn.alignment_compatible")}");
            }
            else if (reactionMod >= 0.7f)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("inn.alignment_neutral")}");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("inn.alignment_opposing")}");
            }
            terminal.WriteLine("");

            // Show interaction options
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.what_to_do"));
            terminal.WriteLine("");

            terminal.SetColor("bright_yellow");
            terminal.Write("[T]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("inn.option_talk_deep")}");
            terminal.SetColor("bright_yellow");
            terminal.Write("[C]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("inn.option_challenge_duel")}");
            terminal.SetColor("bright_yellow");
            terminal.Write("[G]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("inn.option_gift_cost")}");

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.Write("[0]");
            terminal.SetColor("gray");
            terminal.WriteLine($" {Loc.Get("ui.return")}");
            terminal.WriteLine("");

            var choice = await GetChoice();

            switch (choice.ToUpper())
            {
                case "T":
                    // Use the full VisualNovelDialogueSystem for all conversation/romance features
                    await UsurperRemake.Systems.VisualNovelDialogueSystem.Instance.StartConversation(currentPlayer, npc, terminal);
                    break;
                case "C":
                    await ChallengeNPC(npc);
                    continueInteraction = false; // Exit after combat
                    break;
                case "G":
                    await GiveGiftToNPC(npc);
                    break;
                case "0":
                    continueInteraction = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Challenge an NPC to a duel
    /// </summary>
    private async Task ChallengeNPC(NPC npc)
    {
        // Seth Able has a dedicated challenge system with daily limits and flat rewards.
        // Redirect to it regardless of how the player reached this point.
        if (npc.IsSpecialNPC && npc.SpecialScript == "drunk_fighter")
        {
            await ChallengeSethAble();
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("inn.challenging_npc", npc.Name2));
        terminal.WriteLine("");

        // Daily duel limit — prevent XP farming by dueling the same NPCs repeatedly
        const int maxDuelsPerDay = 3;
        if (currentPlayer.SethFightsToday >= maxDuelsPerDay)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.too_many_duels"));
            await terminal.PressAnyKey();
            return;
        }

        // Check if they'll accept
        bool accepts = npc.Darkness > 300 || Random.Shared.Next(100) < 50;

        if (!accepts)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.npc_declines_duel", npc.Name2));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.npc_accepts_challenge", npc.Name2));
        terminal.WriteLine(Loc.Get("inn.npc_regret_decision"));
        terminal.WriteLine("");

        var confirm = await terminal.GetInput(Loc.Get("inn.fight_now_prompt"));
        if (confirm.ToUpper() != "Y")
        {
            terminal.WriteLine(Loc.Get("inn.npc_changed_mind", npc.Name2), "gray");
            await Task.Delay(2000);
            return;
        }

        // Create monster from NPC for combat
        var npcMonster = Monster.CreateMonster(
            nr: npc.Level,
            name: npc.Name2,
            hps: npc.HP,
            strength: npc.Strength,
            defence: npc.Defence,
            phrase: $"{npc.Name2} readies for battle!",
            grabweap: false,
            grabarm: false,
            weapon: "Weapon",
            armor: "Armor",
            poisoned: false,
            disease: false,
            punch: npc.Strength / 2,
            armpow: npc.ArmPow,
            weappow: npc.WeapPow
        );
        npcMonster.IsProperName = true;
        npcMonster.CanSpeak = true;

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsMonster(currentPlayer, npcMonster);

        // Check if player should return to temple after resurrection
        if (result.ShouldReturnToTemple)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.awaken_temple"));
            await Task.Delay(2000);
            await NavigateToLocation(GameLocation.Temple);
            return;
        }

        if (result.Outcome == CombatOutcome.Victory)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("inn.defeated_npc", npc.Name2));
            terminal.WriteLine(Loc.Get("inn.victory_spreads"));

            // XP already awarded by combat engine — no double reward
            currentPlayer.PKills++;
            currentPlayer.SethFightsToday++; // Count toward daily duel limit

            // Update relationship negatively
            RelationshipSystem.UpdateRelationship(currentPlayer, npc, -1, 5, false, false);

            // Record defeat memory on NPC for consequence encounters
            npc.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.Defeated,
                Description = $"Defeated in a tavern duel by {currentPlayer.Name2}",
                InvolvedCharacter = currentPlayer.Name2,
                Importance = 0.8f,
                EmotionalImpact = -0.7f,
                Location = "Inn"
            });

            // Generate news
            NewsSystem.Instance?.Newsy(true, $"{currentPlayer.Name} defeated {npc.Name2} in a tavern brawl!");
        }
        else if (result.Outcome == CombatOutcome.PlayerDied)
        {
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("inn.npc_knocks_unconscious", npc.Name2));
            currentPlayer.HP = 1; // Inn fights don't kill
            currentPlayer.PDefeats++;
        }

        await Task.Delay(3000);
    }

    /// <summary>
    /// Give a gift to an NPC
    /// </summary>
    private async Task GiveGiftToNPC(NPC npc)
    {
        if (currentPlayer.Gold < 50)
        {
            terminal.WriteLine(Loc.Get("ui.not_enough_gold_gift"), "red");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("inn.giving_gift", npc.Name2));
        terminal.WriteLine("");

        currentPlayer.Gold -= 50;

        var random = Random.Shared;
        var responses = new[] {
            $"{npc.Name2}'s eyes light up. \"For me? How thoughtful!\"",
            $"{npc.Name2} accepts the gift graciously. \"You're too kind.\"",
            $"{npc.Name2} smiles broadly. \"I won't forget this kindness.\"",
        };

        terminal.SetColor("white");
        terminal.WriteLine(responses[random.Next(responses.Length)]);
        terminal.WriteLine("");

        // Big relationship boost
        RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, 5, false, false);
        terminal.SetColor("green");
        terminal.WriteLine(Loc.Get("inn.relationship_improves"));
        terminal.WriteLine(Loc.Get("inn.gift_cost"));

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Get NPC mood description
    /// </summary>
    private string GetNPCMood(NPC npc)
    {
        if (npc.Darkness > npc.Chivalry + 200) return Loc.Get("inn.mood_aggressive");
        if (npc.Chivalry > npc.Darkness + 200) return Loc.Get("inn.mood_friendly");
        if (npc.HP < npc.MaxHP / 2) return Loc.Get("inn.mood_tired");
        return Loc.Get("inn.mood_relaxed");
    }

    /// <summary>
    /// Get relationship status text
    /// </summary>
    private string GetRelationshipText(int relationship)
    {
        // Lower numbers are better relationships in Pascal system
        if (relationship <= GameConfig.RelationMarried) return Loc.Get("inn.rel_married");
        if (relationship <= GameConfig.RelationLove) return Loc.Get("inn.rel_in_love");
        if (relationship <= GameConfig.RelationFriendship) return Loc.Get("inn.rel_close_friend");
        if (relationship <= GameConfig.RelationNormal) return Loc.Get("inn.rel_neutral");
        if (relationship <= GameConfig.RelationEnemy) return Loc.Get("inn.rel_disliked");
        return Loc.Get("inn.rel_hated");
    }

    /// <summary>
    /// Get relationship color
    /// </summary>
    private string GetRelationshipColor(int relationship)
    {
        // Lower numbers are better relationships in Pascal system
        if (relationship <= GameConfig.RelationLove) return "bright_magenta";
        if (relationship <= GameConfig.RelationFriendship) return "green";
        if (relationship <= GameConfig.RelationNormal) return "gray";
        if (relationship <= GameConfig.RelationEnemy) return "bright_red";
        return "red";
    }
    
    /// <summary>
    /// Play drinking game - full minigame based on original Pascal DRINKING.PAS
    /// Up to 5 NPC opponents, drink choice, soberness tracking, drunk comments, player input per round
    /// </summary>
    private async Task PlayDrinkingGame()
    {
        if (currentPlayer.Gold < 20)
        {
            terminal.WriteLine(Loc.Get("inn.drinking_need_gold"), "red");
            await Task.Delay(1500);
            return;
        }

        // Gather living NPCs as potential opponents
        var maxOpponents = 5;
        var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !n.IsDead && n.HP > 0 && n.Name2 != currentPlayer.Name2)
            .OrderBy(_ => (float)Random.Shared.NextDouble())
            .Take(maxOpponents)
            .ToList() ?? new List<NPC>();

        if (allNPCs.Count < 2)
        {
            terminal.WriteLine(Loc.Get("inn.drinking_not_enough_patrons"), "red");
            await Task.Delay(1500);
            return;
        }

        currentPlayer.Gold -= 20;

        // --- Intro ---
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("inn.drinking_contest"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.drinking_jump_bar"));
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("inn.drinking_challenge_shout"));
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("inn.drinking_silence"));
        await Task.Delay(600);
        terminal.Write("...");
        await Task.Delay(600);
        terminal.WriteLine("...");
        await Task.Delay(400);
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.drinking_rowdy_approach"));
        terminal.WriteLine("");

        // Show opponents joining
        var howdyLines = new[]
        {
            " accepts your challenge! \"I need to show you who's the master!\"",
            " sits down and says: \"I'm in! I can't see any competition here though...\"",
            " sits down and stares at you intensely...",
            " sits down and says: \"I feel sorry for you, {0}!\"",
            " sits down and mutters something you can't hear.",
            " sits down and says: \"Are you ready to lose, {0}!? Haha!\"",
            " sits down and says: \"Make room for me, you cry-babies!\"",
            " sits down and says: \"I can't lose!\"",
            " sits down and says: \"You are looking at the current Beer Champion!\"",
            " sits down without saying a word....",
        };

        foreach (var npc in allNPCs)
        {
            var line = howdyLines[Random.Shared.Next(0, howdyLines.Length)];
            line = string.Format(line, currentPlayer.Name2);
            terminal.SetColor("bright_green");
            terminal.Write($"  {npc.Name2}");
            terminal.SetColor("white");
            terminal.WriteLine(line);
            await Task.Delay(400);
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();

        // --- Drink Choice ---
        terminal.ClearScreen();
        terminal.WriteLine("");
        if (IsScreenReader)
        {
            terminal.WriteLine(Loc.Get("inn.drinking_choose_drink"));
            terminal.WriteLine("");
            WriteSRMenuOption("A", Loc.Get("inn.drink_ale"));
            WriteSRMenuOption("S", Loc.Get("inn.drink_stout"));
            WriteSRMenuOption("K", Loc.Get("inn.drink_bomber"));
        }
        else
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("inn.drinking_choose_drink"));
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [A] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.drink_ale_desc"));
            terminal.SetColor("bright_yellow");
            terminal.Write("  [S] ");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.drink_stout_desc"));
            terminal.SetColor("bright_yellow");
            terminal.Write("  [K] ");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.drink_bomber_desc"));
        }
        terminal.WriteLine("");

        string drinkName;
        int drinkStrength;
        string drinkReaction;
        var drinkChoice = (await terminal.GetInput("  Your choice: ")).Trim().ToUpperInvariant();

        switch (drinkChoice)
        {
            case "S":
                drinkName = "Stout";
                drinkStrength = 3;
                drinkReaction = "Your choice seems to have made everybody content...";
                break;
            case "K":
                drinkName = "Seth's Bomber";
                drinkStrength = 6;
                drinkReaction = "There is a buzz of wonder in the crowded bar...";
                break;
            default: // A or anything else
                drinkName = "Ale";
                drinkStrength = 2;
                drinkReaction = "\"That was a wimpy choice!\", someone shouts from the back.";
                break;
        }

        terminal.SetColor("bright_white");
        terminal.WriteLine($"  {drinkName}!");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {drinkReaction}");
        terminal.WriteLine("");
        await terminal.PressAnyKey();

        // --- Calculate soberness values ---
        // Based on original: (stamina + strength + charisma + 10) / 10, capped at 100
        long playerSoberness = Math.Min(100, (currentPlayer.Stamina + currentPlayer.Strength + currentPlayer.Constitution + 10) / 10);
        if (playerSoberness < 5) playerSoberness = 5; // minimum floor

        var opponents = new List<(string Name, long Soberness, bool Male)>();
        foreach (var npc in allNPCs)
        {
            long sob = Math.Min(100, (npc.Stamina + npc.Strength + npc.Constitution + 10) / 10);
            if (sob < 3) sob = 3;
            opponents.Add((npc.Name2, sob, npc.Sex == CharacterSex.Male));
        }

        // Rank and show favourite
        var allSob = opponents.Select(o => (o.Name, o.Soberness)).ToList();
        allSob.Add((currentPlayer.Name2, playerSoberness));
        allSob.Sort((a, b) => b.Soberness.CompareTo(a.Soberness));

        terminal.ClearScreen();
        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.Write(Loc.Get("inn.drinking_favourite"));
        terminal.SetColor("bright_white");
        terminal.WriteLine($"{allSob[0].Name}!");
        terminal.WriteLine("");
        await terminal.PressAnyKey();

        // --- Main contest loop ---
        int round = 0;
        bool playerAlive = true;
        int playerRounds = 0;

        while (true)
        {
            round++;

            // Count remaining contestants
            int remaining = opponents.Count(o => o.Soberness > 0) + (playerAlive ? 1 : 0);
            if (remaining <= 1) break;

            terminal.ClearScreen();
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("inn.drinking_round", round, remaining));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("inn.drinking_drink_name", drinkName));
            terminal.WriteLine("");

            // --- Player's turn ---
            if (playerAlive)
            {
                // Player chooses: drink or try to bow out
                terminal.SetColor("bright_white");
                terminal.WriteLine(Loc.Get("inn.drinking_soberness", GetSobernessBar(playerSoberness)));
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.Write("  [D]");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("inn.drinking_down"));
                terminal.SetColor("bright_yellow");
                terminal.Write("  [Q]");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("inn.drinking_bow_out"));
                terminal.WriteLine("");

                var action = (await terminal.GetInput(Loc.Get("inn.drinking_what_do"))).Trim().ToUpperInvariant();

                if (action == "Q")
                {
                    // CON check to bow out without embarrassment
                    int bowOutChance = 30 + (int)(currentPlayer.Constitution / 2);
                    if (bowOutChance > 80) bowOutChance = 80;
                    if (Random.Shared.Next(1, 101) <= bowOutChance)
                    {
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("inn.drinking_bow_success1"));
                        terminal.WriteLine(Loc.Get("inn.drinking_bow_success2"));
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("inn.drinking_bow_success3"));
                        playerAlive = false;
                        playerRounds = round;
                        terminal.WriteLine("");
                        await terminal.PressAnyKey();
                        continue;
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("inn.drinking_bow_fail1"));
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("inn.drinking_bow_fail2"));
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("inn.drinking_bow_fail3"));
                        terminal.WriteLine("");
                        await Task.Delay(800);
                        // Falls through to drinking
                    }
                }

                // Drink!
                terminal.SetColor("bright_cyan");
                terminal.Write(Loc.Get("inn.drinking_take_beer"));
                await Task.Delay(300);
                terminal.Write(Loc.Get("inn.drinking_glugg"));
                await Task.Delay(200);
                terminal.Write(Loc.Get("inn.drinking_glugg"));
                await Task.Delay(200);
                terminal.WriteLine(Loc.Get("inn.drinking_glugg_end"));

                // Reduce soberness: random(23 + drinkStrength)
                long reduction = Random.Shared.Next(1, (22 + drinkStrength) + 1);
                playerSoberness -= reduction;

                if (playerSoberness <= 0)
                {
                    playerSoberness = 0;
                    playerAlive = false;
                    playerRounds = round;
                    terminal.WriteLine("");
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("inn.drinking_spinning"));
                    terminal.WriteLine(Loc.Get("inn.drinking_laughter"));
                    terminal.WriteLine(Loc.Get("inn.drinking_fall"));
                    terminal.SetColor("bright_red");
                    terminal.WriteLine(Loc.Get("inn.drinking_failed"));
                    terminal.WriteLine("");
                    await terminal.PressAnyKey();
                }
                else
                {
                    await Task.Delay(300);
                }
            }

            // --- Opponents' turns ---
            for (int i = 0; i < opponents.Count; i++)
            {
                var opp = opponents[i];
                if (opp.Soberness <= 0) continue;

                long oppReduction = Random.Shared.Next(1, (22 + drinkStrength) + 1);
                var newSob = opp.Soberness - oppReduction;

                if (playerAlive || round == playerRounds) // Only show if player is conscious
                {
                    terminal.SetColor("bright_green");
                    terminal.Write($"  {opp.Name}");
                    terminal.SetColor("white");
                    terminal.Write(opp.Male ? Loc.Get("inn.drinking_takes_his") : Loc.Get("inn.drinking_takes_her"));
                    await Task.Delay(200);
                    terminal.Write(Loc.Get("inn.drinking_glugg"));
                    await Task.Delay(150);
                    terminal.Write(Loc.Get("inn.drinking_glugg"));
                    await Task.Delay(150);
                    terminal.WriteLine(Loc.Get("inn.drinking_glugg_end"));

                    if (newSob <= 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.Write($"  {opp.Name}");
                        terminal.SetColor("white");
                        terminal.WriteLine(Loc.Get("inn.drinking_opp_reels"));
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("inn.drinking_opp_falls", opp.Name));
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine(Loc.Get("inn.drinking_bites_dust"));
                        terminal.WriteLine("");
                        await Task.Delay(500);
                    }
                }

                opponents[i] = (opp.Name, Math.Max(0, newSob), opp.Male);
            }

            // --- Soberness report ---
            if (playerAlive)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("inn.drinking_soberness_eval"));
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.Write(Loc.Get("inn.drinking_you_dash"));
                terminal.SetColor("white");
                terminal.WriteLine(GetDrunkComment(playerSoberness));

                foreach (var opp in opponents)
                {
                    if (opp.Soberness > 0)
                    {
                        terminal.SetColor("bright_green");
                        terminal.Write($"  {opp.Name} - ");
                        terminal.SetColor("white");
                        terminal.WriteLine(GetDrunkComment(opp.Soberness));
                    }
                }

                terminal.WriteLine("");
                await terminal.PressAnyKey();
            }

            // Check if contest is over
            remaining = opponents.Count(o => o.Soberness > 0) + (playerAlive ? 1 : 0);
            if (remaining <= 1) break;
        }

        // --- Results ---
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("inn.contest_results"), "bright_yellow");
        terminal.WriteLine("");

        // Determine winner
        string winnerName = "";
        if (playerAlive)
        {
            winnerName = currentPlayer.Name2;
        }
        else
        {
            var npcWinner = opponents.FirstOrDefault(o => o.Soberness > 0);
            if (npcWinner.Name != null)
                winnerName = npcWinner.Name;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.contest_lasted", round, drinkName));
        terminal.WriteLine("");

        if (playerAlive)
        {
            // Player won!
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.contest_congrats"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.contest_stayed_sober"));
            terminal.SetColor("bright_yellow");
            terminal.Write(Loc.Get("inn.contest_three_cheers"));
            await Task.Delay(400);
            terminal.Write(Loc.Get("inn.drinking_hooray"));
            await Task.Delay(400);
            terminal.Write(Loc.Get("inn.drinking_hooray"));
            await Task.Delay(400);
            terminal.WriteLine(Loc.Get("inn.drinking_hooray_end"));
            terminal.WriteLine("");

            // XP reward: level * 700 (from original Pascal)
            long xpReward = currentPlayer.Level * 700;
            long goldReward = 50 + currentPlayer.Level * 10;
            currentPlayer.Experience += xpReward;
            currentPlayer.Gold += goldReward;

            terminal.SetColor("bright_white");
            terminal.WriteLine(Loc.Get("inn.contest_xp_reward", xpReward));
            terminal.WriteLine(Loc.Get("inn.contest_gold_reward", goldReward));

            currentPlayer.Statistics?.RecordGoldChange(currentPlayer.Gold);
        }
        else
        {
            // Player lost
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.contest_passed_out", playerRounds));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.contest_headache"));

            if (!string.IsNullOrEmpty(winnerName))
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("inn.contest_winner", winnerName, round));
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("inn.contest_no_winner"));
            }

            // Small consolation XP for participating
            long consolationXP = currentPlayer.Level * 100;
            currentPlayer.Experience += consolationXP;
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.contest_consolation_xp", consolationXP));
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Get a soberness bar visual indicator
    /// </summary>
    private static string GetSobernessBar(long soberness)
    {
        int bars = (int)(soberness / 5);
        if (bars < 0) bars = 0;
        if (bars > 20) bars = 20;
        string filled = new string('#', bars);
        string empty = new string('-', 20 - bars);
        string label;
        if (soberness > 60) label = Loc.Get("inn.sober_sober");
        else if (soberness > 40) label = Loc.Get("inn.sober_tipsy");
        else if (soberness > 20) label = Loc.Get("inn.sober_dizzy");
        else if (soberness > 5) label = Loc.Get("inn.sober_wasted");
        else label = Loc.Get("inn.sober_blind_drunk");
        return $"[{filled}{empty}] {soberness}% - {label}";
    }

    /// <summary>
    /// Get a drunk comment based on soberness level (from original Pascal Drunk_Comment)
    /// </summary>
    private static string GetDrunkComment(long soberness)
    {
        if (soberness <= 0) return "*Blind drunk, out of competition*";
        if (soberness <= 1) return "Burp. WhheramIi?3$...???";
        if (soberness <= 4) return "Hihiii! I can see that everybody has a twin!";
        if (soberness <= 8) return "Gosh! That floor IS REALLY moving!";
        if (soberness <= 12) return "Stand still you rats! Why is the room spinning!?";
        if (soberness <= 15) return "I'm a little dizzy, that's all!";
        if (soberness <= 18) return "That beer hasn't got to me yet!";
        if (soberness <= 24) return "I'm fine, but where is the bathroom please!";
        if (soberness <= 30) return "And a happy new year to ya all! (burp..)";
        if (soberness <= 35) return "Gimme another one, Bartender!";
        if (soberness <= 40) return "Ha! I'm unbeatable!";
        if (soberness <= 50) return "Sober as a rock...";
        if (soberness <= 55) return "A clear and steady mind...";
        if (soberness <= 60) return "Refill please!";
        return "This is boriiiing... (yawn)";
    }
    
    /// <summary>
    /// Listen to rumors
    /// </summary>
    private async Task ListenToRumors()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("inn.tavern_gossip"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("inn.gossip_lean_back"));
        terminal.WriteLine("");

        var gossip = NewsSystem.Instance?.GetRecentGossip(4) ?? new List<string>();

        if (gossip.Count > 0)
        {
            var gossipPrefixes = new[]
            {
                "\"Did you hear?",
                "\"Word around town is that",
                "\"I heard from a friend that",
                "\"Someone was saying",
                "\"You won't believe this, but",
                "\"The talk of the town is that",
                "\"Between you and me,",
            };

            foreach (var item in gossip)
            {
                terminal.SetColor("white");
                // Strip timestamp prefix [HH:mm] if present
                var text = item.TrimStart();
                if (text.Length > 7 && text[0] == '[' && text[6] == ']')
                    text = text.Substring(7).TrimStart();

                // Strip leading emoji/symbol characters for cleaner dialogue
                while (text.Length > 0 && !char.IsLetterOrDigit(text[0]) && text[0] != '"')
                    text = text.Substring(1).TrimStart();

                if (string.IsNullOrWhiteSpace(text)) continue;

                var prefix = gossipPrefixes[Random.Shared.Next(0, gossipPrefixes.Length)];
                terminal.Write($"  {prefix} ");
                terminal.SetColor("bright_white");
                // Lowercase first char for natural dialogue flow
                if (text.Length > 0 && char.IsUpper(text[0]))
                    text = char.ToLower(text[0]) + text.Substring(1);
                terminal.WriteLine($"{text}\"");
                terminal.WriteLine("");
            }
        }
        else
        {
            // Fallback to static rumors when no simulation events exist yet
            var staticRumors = new[]
            {
                "\"They say the King is planning to increase the royal guard...\"",
                "\"Word is that someone found a magical sword in the dungeons last week.\"",
                "\"The priests at the temple are worried about strange omens.\"",
                "\"A new monster has been spotted in the lower dungeon levels.\"",
                "\"The weapon shop is expecting a shipment of rare items soon.\"",
            };

            terminal.SetColor("white");
            for (int i = 0; i < 3; i++)
            {
                terminal.WriteLine($"  {staticRumors[Random.Shared.Next(0, staticRumors.Length)]}");
                terminal.WriteLine("");
            }
        }

        await terminal.PressAnyKey();
    }
    
    
    /// <summary>
    /// Rest at table for minor healing
    /// </summary>
    private async Task RestAtTable()
    {
        terminal.WriteLine(Loc.Get("inn.rest_quiet_corner"), "green");
        await Task.Delay(2000);

        // Remove Groggo's Shadow Blessing on rest (v0.41.0)
        if (currentPlayer.GroggoShadowBlessingDex > 0)
        {
            currentPlayer.Dexterity = Math.Max(1, currentPlayer.Dexterity - currentPlayer.GroggoShadowBlessingDex);
            terminal.WriteLine(Loc.Get("inn.rest_shadow_fades"), "gray");
            currentPlayer.GroggoShadowBlessingDex = 0;
        }

        // Blood Price rest penalty — dark memories reduce rest effectiveness
        float restEfficiency = 1.0f;
        if (currentPlayer.MurderWeight >= 6f) restEfficiency = 0.50f;
        else if (currentPlayer.MurderWeight >= 3f) restEfficiency = 0.75f;

        // Recover 50% of missing HP and mana
        long missingHP = currentPlayer.MaxHP - currentPlayer.HP;
        long missingMana = currentPlayer.MaxMana - currentPlayer.Mana;
        var healing = (long)(missingHP * 0.50f * restEfficiency);
        var manaRecovery = (long)(missingMana * 0.50f * restEfficiency);

        if (healing > 0 || manaRecovery > 0)
        {
            if (healing > 0)
            {
                currentPlayer.HP += healing;
                terminal.WriteLine(Loc.Get("inn.rest_recover_hp", healing), "green");
            }
            if (manaRecovery > 0)
            {
                currentPlayer.Mana += manaRecovery;
                terminal.WriteLine(Loc.Get("inn.rest_recover_mana", manaRecovery), "blue");
            }
            if (restEfficiency < 1.0f)
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine(Loc.Get("inn.rest_dark_memories"));
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("inn.rest_full_health"), "white");
        }

        // Reduce fatigue from inn rest (single-player only)
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer.Fatigue > 0)
        {
            int oldFatigue = currentPlayer.Fatigue;
            currentPlayer.Fatigue = Math.Max(0, currentPlayer.Fatigue - GameConfig.FatigueReductionInnRest);
            if (currentPlayer.Fatigue < oldFatigue)
                terminal.WriteLine(Loc.Get("inn.rest_fatigue_reduced", oldFatigue - currentPlayer.Fatigue), "bright_green");
        }

        // Check for dreams during rest (nightmares take priority if MurderWeight > 0)
        var dream = DreamSystem.Instance.GetDreamForRest(currentPlayer, 0);
        if (dream != null)
        {
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("dark_magenta");
            terminal.WriteLine(Loc.Get("inn.rest_dream_begins"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"=== {dream.Title} ===");
            terminal.WriteLine("");

            terminal.SetColor("magenta");
            foreach (var line in dream.Content)
            {
                terminal.WriteLine($"  {line}");
                await Task.Delay(1200);
            }

            if (!string.IsNullOrEmpty(dream.PhilosophicalHint))
            {
                terminal.WriteLine("");
                terminal.SetColor("dark_cyan");
                terminal.WriteLine($"  ({dream.PhilosophicalHint})");
            }

            terminal.WriteLine("");
            DreamSystem.Instance.ExperienceDream(dream.Id);
            await terminal.PressAnyKey();
        }
        else
        {
            await Task.Delay(2000);
            await terminal.PressAnyKey();
        }
    }

    /// <summary>
    /// Sleep at the Inn to advance the day (single-player only).
    /// Full HP/Mana/Stamina recovery, dreams, no Well-Rested bonus (that's Home-only).
    /// </summary>
    private async Task SleepAtInn()
    {
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode || currentPlayer == null)
            return;

        if (!DailySystemManager.CanRestForNight(currentPlayer))
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.sleep_not_late"));
            await terminal.WaitForKey();
            return;
        }

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("inn.sleep_room_shown"), "gray");
        await Task.Delay(1500);
        terminal.WriteLine(Loc.Get("inn.sleep_settle_in"), "gray");
        await Task.Delay(1500);

        // Full HP/Mana/Stamina recovery with Blood Price penalty
        float restEfficiency = 1.0f;
        if (currentPlayer.MurderWeight >= 6f) restEfficiency = 0.50f;
        else if (currentPlayer.MurderWeight >= 3f) restEfficiency = 0.75f;

        long healAmount = (long)((currentPlayer.MaxHP - currentPlayer.HP) * restEfficiency);
        long manaAmount = (long)((currentPlayer.MaxMana - currentPlayer.Mana) * restEfficiency);
        long staminaAmount = (long)((currentPlayer.MaxCombatStamina - currentPlayer.CurrentCombatStamina) * restEfficiency);
        currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + healAmount);
        currentPlayer.Mana = Math.Min(currentPlayer.MaxMana, currentPlayer.Mana + manaAmount);
        currentPlayer.CurrentCombatStamina = Math.Min(currentPlayer.MaxCombatStamina, currentPlayer.CurrentCombatStamina + staminaAmount);

        if (currentPlayer.MurderWeight >= 3f)
        {
            terminal.WriteLine(Loc.Get("inn.sleep_dark_memories"), "dark_red");
        }

        if (restEfficiency >= 1.0f)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.sleep_refreshed"));
        }
        else
        {
            terminal.SetColor("green");
            if (currentPlayer.IsManaClass)
                terminal.WriteLine(Loc.Get("inn.sleep_recover_mana", healAmount, manaAmount, (int)(restEfficiency * 100)));
            else
                terminal.WriteLine(Loc.Get("inn.sleep_recover_stamina", healAmount, staminaAmount, (int)(restEfficiency * 100)));
        }

        // Check for dreams
        var dream = DreamSystem.Instance.GetDreamForRest(currentPlayer, 0);
        if (dream != null)
        {
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("dark_magenta");
            terminal.WriteLine(Loc.Get("inn.sleep_dreams_unfold"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"=== {dream.Title} ===");
            terminal.WriteLine("");

            terminal.SetColor("magenta");
            foreach (var line in dream.Content)
            {
                terminal.WriteLine($"  {line}");
                await Task.Delay(1200);
            }

            if (!string.IsNullOrEmpty(dream.PhilosophicalHint))
            {
                terminal.WriteLine("");
                terminal.SetColor("dark_cyan");
                terminal.WriteLine($"  ({dream.PhilosophicalHint})");
            }

            terminal.WriteLine("");
            DreamSystem.Instance.ExperienceDream(dream.Id);
        }

        // Advance to morning
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("inn.sleep_drift_off"));
        await Task.Delay(2000);
        await DailySystemManager.Instance.RestAndAdvanceToMorning(currentPlayer);
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.sleep_new_day", DailySystemManager.Instance.CurrentDay));
        await Task.Delay(1500);

        await terminal.WaitForKey();
    }

    /// <summary>
    /// Order food for stamina boost
    /// </summary>
    private async Task OrderFood()
    {
        long mealBasePrice = 10;
        var (mealKingTax, mealCityTax, mealTotalWithTax) = CityControlSystem.CalculateTaxedPrice(mealBasePrice);

        if (currentPlayer.Gold < mealTotalWithTax)
        {
            terminal.WriteLine(Loc.Get("ui.not_enough_gold_meal"), "red");
            await Task.Delay(2000);
            return;
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Meal", mealBasePrice);

        currentPlayer.Gold -= mealTotalWithTax;
        CityControlSystem.Instance.ProcessSaleTax(mealBasePrice);
        
        terminal.WriteLine(Loc.Get("inn.food_hearty_meal"), "green");
        terminal.WriteLine(Loc.Get("inn.food_stamina_boost"));
        
        currentPlayer.Stamina += 5;
        var healing = Math.Min(15, currentPlayer.MaxHP - currentPlayer.HP);
        if (healing > 0)
        {
            currentPlayer.HP += healing;
            terminal.WriteLine(Loc.Get("inn.food_hp_recover", healing), "green");
        }

        await Task.Delay(2500);
    }

    /// <summary>
    /// Approach potential companions in the inn
    /// </summary>
    private async Task ApproachCompanions()
    {
        var recruitableCompanions = CompanionSystem.Instance.GetRecruitableCompanions(currentPlayer.Level).ToList();

        if (!recruitableCompanions.Any())
        {
            terminal.WriteLine(Loc.Get("inn.no_companions_available"), "gray");
            await terminal.PressAnyKey();
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("inn.companions"), "bright_magenta");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.companions_watching"));
        terminal.WriteLine("");

        int index = 1;
        foreach (var companion in recruitableCompanions)
        {
            terminal.SetColor("yellow");
            terminal.Write($"[{index}] ");
            terminal.SetColor("bright_cyan");
            terminal.Write($"{companion.Name} - {companion.Title}");
            terminal.SetColor("gray");
            terminal.WriteLine($" ({companion.CombatRole})");
            terminal.SetColor("dark_gray");
            terminal.WriteLine($"    {companion.Description.Substring(0, Math.Min(70, companion.Description.Length))}...");
            terminal.WriteLine($"    Level Req: {companion.RecruitLevel} | Trust: {companion.TrustLevel}%");
            terminal.WriteLine("");
            index++;
        }

        terminal.SetColor("bright_yellow");
        terminal.Write("[0]");
        terminal.SetColor("yellow");
        terminal.WriteLine($" {Loc.Get("inn.return_to_bar")}");
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("inn.approach_who"));

        if (int.TryParse(choice, out int selection) && selection > 0 && selection <= recruitableCompanions.Count)
        {
            var selectedCompanion = recruitableCompanions[selection - 1];
            await AttemptCompanionRecruitment(selectedCompanion);
        }
    }

    /// <summary>
    /// Attempt to recruit a specific companion
    /// </summary>
    private async Task AttemptCompanionRecruitment(Companion companion)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("inn.you_approach", companion.Name, companion.Title));
        terminal.WriteLine("");

        // Show companion's introduction from DialogueHints
        terminal.SetColor("white");
        if (companion.DialogueHints.Length > 0)
        {
            terminal.WriteLine($"\"{companion.DialogueHints[0]}\"");
        }
        else
        {
            terminal.WriteLine(Loc.Get("inn.greetings_traveler"));
        }
        terminal.WriteLine("");

        // Show companion details
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("inn.background_label", companion.BackstoryBrief));
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.combat_role_label", companion.CombatRole));
        terminal.WriteLine(Loc.Get("inn.abilities_label", string.Join(", ", companion.Abilities)));
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        if (IsScreenReader)
        {
            terminal.WriteLine($"R. {Loc.Get("inn.recruit_companion")}");
            terminal.WriteLine($"T. {Loc.Get("inn.talk_more")}");
            terminal.WriteLine($"0. {Loc.Get("inn.leave_them_be")}");
        }
        else
        {
            terminal.WriteLine($"[R] {Loc.Get("inn.recruit_companion")}");
            terminal.WriteLine($"[T] {Loc.Get("inn.talk_more")}");
            terminal.WriteLine($"[0] {Loc.Get("inn.leave_them_be")}");
        }
        terminal.WriteLine("");

        var choice = await GetChoice();

        switch (choice.ToUpper())
        {
            case "R":
                bool success = await CompanionSystem.Instance.RecruitCompanion(companion.Id, currentPlayer, terminal);
                if (success)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("inn.companion_joined", companion.Name));
                    terminal.WriteLine(Loc.Get("inn.companion_fight_by_side"));
                    terminal.WriteLine("");
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("inn.companion_die_warning"));
                }
                break;

            case "T":
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("inn.companion_shares_story", companion.Name));
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine(companion.BackstoryBrief);
                if (!string.IsNullOrEmpty(companion.PersonalQuestDescription))
                {
                    terminal.WriteLine("");
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(Loc.Get("inn.personal_quest_label", companion.PersonalQuestName));
                    terminal.WriteLine($"\"{companion.PersonalQuestDescription}\"");
                }
                break;

            default:
                terminal.WriteLine(Loc.Get("inn.nod_return", companion.Name), "gray");
                break;
        }

        await terminal.PressAnyKey();
    }

    #region Party Management

    /// <summary>
    /// Manage your recruited companions
    /// </summary>
    private async Task ManageParty()
    {
        var allCompanions = CompanionSystem.Instance.GetAllCompanions()
            .Where(c => c.IsRecruited && !c.IsDead).ToList();

        if (!allCompanions.Any())
        {
            terminal.WriteLine(Loc.Get("inn.no_companions_yet"), "gray");
            await terminal.PressAnyKey();
            return;
        }

        while (true)
        {
            terminal.ClearScreen();

            // Show pending notifications first
            if (CompanionSystem.Instance.HasPendingNotifications)
            {
                WriteBoxHeader(Loc.Get("inn.notifications"), "bright_yellow");
                terminal.WriteLine("");

                foreach (var notification in CompanionSystem.Instance.GetAndClearNotifications())
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(notification);
                    terminal.WriteLine("");
                }

                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("ui.press_enter"));
                await terminal.ReadKeyAsync();
                terminal.ClearScreen();
            }

            WriteBoxHeader(Loc.Get("inn.party_management"), "bright_cyan");
            terminal.WriteLine("");

            // Show active companions
            var activeCompanions = CompanionSystem.Instance.GetActiveCompanions().ToList();
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.active_companions", activeCompanions.Count, CompanionSystem.MaxActiveCompanions));
            terminal.WriteLine("");

            if (activeCompanions.Any())
            {
                foreach (var companion in activeCompanions)
                {
                    DisplayCompanionSummary(companion, true);
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("inn.no_active_companions")}");
            }
            terminal.WriteLine("");

            // Show reserved companions
            var reserveCompanions = allCompanions.Where(c => !c.IsActive).ToList();
            if (reserveCompanions.Any())
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("inn.reserve_companions"));
                terminal.WriteLine("");
                foreach (var companion in reserveCompanions)
                {
                    DisplayCompanionSummary(companion, false);
                }
                terminal.WriteLine("");
            }

            // Show fallen companions
            var fallen = CompanionSystem.Instance.GetFallenCompanions().ToList();
            if (fallen.Any())
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine(Loc.Get("inn.fallen_companions"));
                foreach (var (companion, death) in fallen)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {companion.Name} - {companion.Title}");
                    terminal.SetColor("dark_gray");
                    terminal.WriteLine($"    Died: {death.Circumstance}");
                }
                terminal.WriteLine("");
            }

            // Menu options
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.options"));
            terminal.SetColor("white");
            int index = 1;
            foreach (var companion in allCompanions)
            {
                terminal.WriteLine($"  [{index}] {Loc.Get("inn.talk_to", companion.Name)}");
                index++;
            }
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [S]");
            terminal.SetColor("cyan");
            terminal.WriteLine($" {Loc.Get("inn.switch_companions")}");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [0]");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.return_to_bar"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("ui.choice"));

            if (choice == "0" || string.IsNullOrWhiteSpace(choice))
                break;

            if (choice.ToUpper() == "S")
            {
                await SwitchActiveCompanions(allCompanions);
                continue;
            }

            if (int.TryParse(choice, out int selection) && selection > 0 && selection <= allCompanions.Count)
            {
                await TalkToRecruitedCompanion(allCompanions[selection - 1]);
            }
        }
    }

    /// <summary>
    /// Display a companion's summary in the party menu
    /// </summary>
    private void DisplayCompanionSummary(Companion companion, bool isActive)
    {
        var companionSystem = CompanionSystem.Instance;
        int currentHP = companionSystem.GetCompanionHP(companion.Id);
        int maxHP = companionSystem.GetCompanionMaxHP(companion);

        // Name and title
        terminal.SetColor(isActive ? "bright_white" : "white");
        terminal.Write($"  {companion.Name}");
        terminal.SetColor("gray");
        terminal.WriteLine($" - {companion.Title}");

        // Stats line
        terminal.SetColor("dark_gray");
        terminal.Write($"    Lvl {companion.Level} {companion.CombatRole} | ");

        // HP with color coding
        terminal.SetColor(currentHP > maxHP / 2 ? "green" : currentHP > maxHP / 4 ? "yellow" : "red");
        terminal.Write($"{Loc.Get("combat.bar_hp")}: {currentHP}/{maxHP}");
        terminal.SetColor("dark_gray");
        terminal.WriteLine("");

        // Loyalty and trust
        string loyaltyColor = companion.LoyaltyLevel >= 75 ? "bright_green" :
                              companion.LoyaltyLevel >= 50 ? "yellow" :
                              companion.LoyaltyLevel >= 25 ? "orange" : "red";
        terminal.SetColor("dark_gray");
        terminal.Write(Loc.Get("inn.loyalty_label"));
        terminal.SetColor(loyaltyColor);
        terminal.Write($"{companion.LoyaltyLevel}%");
        terminal.SetColor("dark_gray");
        terminal.Write(Loc.Get("inn.trust_label"));
        terminal.SetColor("cyan");
        terminal.WriteLine($"{companion.TrustLevel}%");

        // Personal quest status
        if (companion.PersonalQuestCompleted)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"    Quest: {companion.PersonalQuestName} (COMPLETE)");
        }
        else if (companion.PersonalQuestStarted)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine($"    Quest: {companion.PersonalQuestName} (In Progress)");
            if (!string.IsNullOrEmpty(companion.PersonalQuestLocationHint))
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"      -> {companion.PersonalQuestLocationHint}");
            }
        }
        else if (companion.LoyaltyLevel >= 50 || companion.PersonalQuestAvailable)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"    Quest: {companion.PersonalQuestName} (UNLOCKED - Talk to begin!)");
        }
        else
        {
            terminal.SetColor("dark_gray");
            terminal.WriteLine($"    Quest: Build more loyalty ({companion.LoyaltyLevel}/50)");
        }

        // Romance level (if applicable)
        if (companion.RomanceAvailable && companion.RomanceLevel > 0)
        {
            terminal.SetColor("bright_magenta");
            string hearts = new string('*', Math.Min(companion.RomanceLevel, 10));
            terminal.WriteLine($"    Romance: {hearts} ({companion.RomanceLevel}/10)");
        }

        terminal.WriteLine("");
    }

    /// <summary>
    /// Switch which companions are active in dungeon
    /// </summary>
    private async Task SwitchActiveCompanions(List<Companion> allCompanions)
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("inn.select_companions"), "cyan");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("inn.companion_limit", CompanionSystem.MaxActiveCompanions));
        terminal.WriteLine(Loc.Get("inn.companion_warning"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.select_to_activate"));
        terminal.WriteLine("");

        int index = 1;
        foreach (var companion in allCompanions)
        {
            bool isCurrentlyActive = companion.IsActive;
            terminal.SetColor(isCurrentlyActive ? "bright_green" : "white");
            terminal.Write($"  [{index}] {companion.Name}");
            terminal.SetColor("gray");
            terminal.Write($" ({companion.CombatRole})");
            if (isCurrentlyActive)
            {
                terminal.SetColor("bright_green");
                terminal.Write(Loc.Get("inn.active_label"));
            }
            terminal.WriteLine("");
            index++;
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.example_activate"));
        terminal.WriteLine(Loc.Get("inn.enter_nothing"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("inn.activate_prompt"));

        if (string.IsNullOrWhiteSpace(input))
        {
            terminal.WriteLine(Loc.Get("inn.no_changes"), "gray");
            await Task.Delay(1000);
            return;
        }

        // Parse selection
        var selections = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var selectedIds = new List<CompanionId>();

        foreach (var sel in selections)
        {
            if (int.TryParse(sel.Trim(), out int num) && num > 0 && num <= allCompanions.Count)
            {
                if (selectedIds.Count < CompanionSystem.MaxActiveCompanions)
                {
                    selectedIds.Add(allCompanions[num - 1].Id);
                }
            }
        }

        if (selectedIds.Count == 0)
        {
            terminal.WriteLine(Loc.Get("inn.no_valid_selected"), "yellow");
            await Task.Delay(1500);
            return;
        }

        // Apply selection
        bool success = CompanionSystem.Instance.SetActiveCompanions(selectedIds);
        if (success)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("inn.party_updated"));
            foreach (var id in selectedIds)
            {
                var c = CompanionSystem.Instance.GetCompanion(id);
                terminal.WriteLine($"  {Loc.Get("inn.now_active", c?.Name)}");
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("inn.update_failed"), "red");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Have a conversation with a recruited companion
    /// </summary>
    private async Task TalkToRecruitedCompanion(Companion companion)
    {
        terminal.ClearScreen();
        WriteBoxHeader($"{companion.Name} - {companion.Title}", "bright_cyan", 76);
        terminal.WriteLine("");

        // Show full description
        terminal.SetColor("white");
        terminal.WriteLine(companion.Description);
        terminal.WriteLine("");

        // Show backstory
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("inn.background"));
        terminal.SetColor("dark_cyan");
        terminal.WriteLine(companion.BackstoryBrief);
        terminal.WriteLine("");

        // Dialogue based on loyalty level
        terminal.SetColor("cyan");
        string dialogueHint = GetCompanionDialogue(companion);
        terminal.WriteLine($"\"{dialogueHint}\"");
        terminal.WriteLine("");

        // Show stats — always use effective stats (gear included)
        var tempChar = CreateCompanionCharacterWrapper(companion);
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.stats"));
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("inn.level_role", companion.Level, companion.CombatRole)}");
        terminal.WriteLine($"  {Loc.Get("inn.hp_atk_def", tempChar.MaxHP, tempChar.Strength, tempChar.Defence)}");
        terminal.WriteLine($"  STR: {tempChar.Strength}  DEX: {tempChar.Dexterity}  AGI: {tempChar.Agility}  CON: {tempChar.Constitution}");
        terminal.WriteLine($"  INT: {tempChar.Intelligence}  WIS: {tempChar.Wisdom}  CHA: {tempChar.Charisma}  STA: {tempChar.Stamina}");
        if (companion.EquippedItems.Count > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("inn.items_equipped", companion.EquippedItems.Count, companion.EquippedItems.Count != 1 ? "s" : "")}");
        }

        // Show abilities from class ability system (matches toggle menu)
        var abilityCharClass = companion.CombatRole switch
        {
            CombatRole.Tank => CharacterClass.Warrior,
            CombatRole.Healer => CharacterClass.Cleric,
            CombatRole.Damage => CharacterClass.Assassin,
            CombatRole.Hybrid => CharacterClass.Ranger,
            CombatRole.Bard => CharacterClass.Bard,
            _ => CharacterClass.Warrior
        };
        var abilityChar = new Character { Class = abilityCharClass, Level = companion.Level };
        var companionAbilities = ClassAbilitySystem.GetAvailableAbilities(abilityChar);
        if (companionAbilities.Count > 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine($"  Abilities ({companionAbilities.Count}): {string.Join(", ", companionAbilities.Select(a => a.Name))}");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"  Abilities: {string.Join(", ", companion.Abilities)}");
        }
        terminal.WriteLine("");

        // Menu options
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.options"));
        terminal.SetColor("white");

        bool sr = GameConfig.ScreenReaderMode;

        // Show personal quest option if available
        if (!companion.PersonalQuestStarted && companion.LoyaltyLevel >= 50)
        {
            if (sr)
                terminal.WriteLine($"  Q. {Loc.Get("inn.begin_quest", companion.PersonalQuestName)}", "bright_magenta");
            else
            {
                terminal.SetColor("bright_yellow"); terminal.Write("  [Q]");
                terminal.SetColor("bright_magenta"); terminal.WriteLine($" {Loc.Get("inn.begin_quest", companion.PersonalQuestName)}");
            }
        }
        else if (companion.PersonalQuestStarted && !companion.PersonalQuestCompleted)
        {
            if (sr)
                terminal.WriteLine($"  Q. {Loc.Get("inn.discuss_progress")}", "magenta");
            else
            {
                terminal.SetColor("bright_yellow"); terminal.Write("  [Q]");
                terminal.SetColor("magenta"); terminal.WriteLine($" {Loc.Get("inn.discuss_progress")}");
            }
        }

        if (companion.RomanceAvailable)
        {
            string bondLabel = companion.RomancedToday ? Loc.Get("inn.deepen_bond_today")
                : companion.RomanceLevel >= 10 ? Loc.Get("inn.spend_time_max")
                : Loc.Get("inn.deepen_bond");
            string bondColor = companion.RomancedToday ? "dark_gray" : companion.RomanceLevel >= 10 ? "gray" : "bright_magenta";
            if (sr)
                terminal.WriteLine($"  R. {bondLabel}", bondColor);
            else
            {
                terminal.SetColor("bright_yellow"); terminal.Write("  [R]");
                terminal.SetColor(bondColor); terminal.WriteLine($" {bondLabel}");
            }
        }

        if (sr)
        {
            terminal.WriteLine($"  G. {Loc.Get("inn.give_a_gift")}", "white");
            terminal.WriteLine($"  H. {Loc.Get("inn.view_history")}", "white");
            terminal.WriteLine($"  E. {Loc.Get("inn.manage_equipment")}", "white");
        }
        else
        {
            terminal.SetColor("bright_yellow"); terminal.Write("  [G]");
            terminal.SetColor("white"); terminal.WriteLine($" {Loc.Get("inn.give_a_gift")}");
            terminal.SetColor("bright_yellow"); terminal.Write("  [H]");
            terminal.SetColor("white"); terminal.WriteLine($" {Loc.Get("inn.view_history")}");
            terminal.SetColor("bright_yellow"); terminal.Write("  [E]");
            terminal.SetColor("white"); terminal.WriteLine($" {Loc.Get("inn.manage_equipment")}");
        }
        terminal.SetColor("bright_yellow");
        terminal.Write("  [A]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("inn.manage_skills")}");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("yellow");
        terminal.WriteLine($" {Loc.Get("inn.return")}");
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("ui.choice"));

        switch (choice.ToUpper())
        {
            case "Q":
                await HandlePersonalQuestInteraction(companion);
                break;
            case "R":
                if (companion.RomanceAvailable)
                    await HandleRomanceInteraction(companion);
                break;
            case "G":
                await HandleGiveGift(companion);
                break;
            case "H":
                await ShowCompanionHistory(companion);
                break;
            case "E":
                await ManageCompanionEquipment(companion);
                break;
            case "A":
                await ManageCompanionAbilities(companion);
                break;
        }
    }

    /// <summary>
    /// Get contextual dialogue based on companion's state
    /// </summary>
    private string GetCompanionDialogue(Companion companion)
    {
        // High loyalty dialogue
        if (companion.LoyaltyLevel >= 80)
        {
            return companion.Id switch
            {
                CompanionId.Lyris => "I never thought I'd find someone I could trust again. You've given me hope.",
                CompanionId.Aldric => "You remind me of what I used to fight for. It's... good to feel that again.",
                CompanionId.Mira => "With you, healing feels like it means something. Thank you for that.",
                CompanionId.Vex => "You know, for once... I'm glad I'm still here. Don't tell anyone I said that.",
                CompanionId.Melodia => "Every adventure is a verse in a song that's still being written. Ours is becoming my favorite.",
                _ => "We've been through a lot together."
            };
        }
        // Medium loyalty
        else if (companion.LoyaltyLevel >= 50)
        {
            return companion.Id switch
            {
                CompanionId.Lyris => "There's something about you... like we've met before, in another life.",
                CompanionId.Aldric => "You fight well. I'm glad to have my shield at your side.",
                CompanionId.Mira => "I've been thinking about what you said. Maybe there is a reason to keep going.",
                CompanionId.Vex => "Not bad for an adventurer. Maybe I'll stick around a bit longer.",
                CompanionId.Melodia => "You have an interesting rhythm to you. I might write a song about it someday.",
                _ => "We're starting to understand each other."
            };
        }
        // Low loyalty - use default hints
        else if (companion.DialogueHints.Length > 0)
        {
            int hintIndex = Math.Min(companion.LoyaltyLevel / 20, companion.DialogueHints.Length - 1);
            return companion.DialogueHints[hintIndex];
        }

        return "...";
    }

    /// <summary>
    /// Handle personal quest interaction
    /// </summary>
    private async Task HandlePersonalQuestInteraction(Companion companion)
    {
        terminal.ClearScreen();
        WriteSectionHeader(companion.PersonalQuestName, "bright_magenta");
        terminal.WriteLine("");

        if (!companion.PersonalQuestStarted)
        {
            // Start the quest
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.speaks_quietly", companion.Name));
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine($"\"{companion.PersonalQuestDescription}\"");
            terminal.WriteLine("");

            terminal.SetColor("bright_yellow");
            terminal.Write("[Y]");
            terminal.SetColor("yellow");
            terminal.WriteLine($" {Loc.Get("inn.accept_quest")}");
            terminal.SetColor("bright_yellow");
            terminal.Write("[N]");
            terminal.SetColor("yellow");
            terminal.WriteLine($" {Loc.Get("inn.not_yet")}");
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("inn.will_you_help"));

            if (choice.ToUpper() == "Y")
            {
                bool started = CompanionSystem.Instance.StartPersonalQuest(companion.Id);
                if (started)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("inn.quest_begun", companion.PersonalQuestName));
                    terminal.WriteLine("");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("inn.nods_gratefully", companion.Name));
                    CompanionSystem.Instance.ModifyLoyalty(companion.Id, 10, "Accepted personal quest");
                }
            }
        }
        else
        {
            // Quest in progress - show status
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.quest_in_progress"));
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"\"{companion.PersonalQuestDescription}\"");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.seek_clues"));
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Handle romance interaction
    /// </summary>
    private async Task HandleRomanceInteraction(Companion companion)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("inn.quiet_moment"), "bright_magenta");
        terminal.WriteLine("");

        // Already romanced today — once per day limit
        if (companion.RomancedToday)
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.already_spent_time", companion.Name));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.perhaps_tomorrow"));
            await terminal.PressAnyKey();
            return;
        }

        // Mark as used for today
        companion.RomancedToday = true;

        if (companion.RomanceLevel < 1)
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.quiet_corner", companion.Name));
            terminal.WriteLine(Loc.Get("inn.tavern_fades"));
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine($"\"{companion.DialogueHints[0]}\"");
        }
        else
        {
            string milestone = companion.RomanceLevel switch
            {
                1 => Loc.Get("inn.romance_1", companion.Name),
                2 => Loc.Get("inn.romance_2"),
                3 => Loc.Get("inn.romance_3", companion.Name),
                4 => Loc.Get("inn.romance_4"),
                5 => Loc.Get("inn.romance_5", companion.Name),
                _ => Loc.Get("inn.romance_default", companion.Name)
            };
            terminal.SetColor("white");
            terminal.WriteLine(milestone);
        }

        terminal.WriteLine("");

        // Advance romance if loyalty is high enough
        if (companion.RomanceLevel >= 10)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("inn.bond_max", companion.Name));
        }
        else if (companion.LoyaltyLevel >= 60)
        {
            // CHA-based success chance: 30% base + 1% per CHA point, cap 80%
            int charisma = (int)(currentPlayer?.Charisma ?? 10);
            int successChance = Math.Min(80, 30 + charisma);
            int roll = Random.Shared.Next(100);

            if (roll < successChance)
            {
                bool advanced = CompanionSystem.Instance.AdvanceRomance(companion.Id, 1);
                if (advanced)
                {
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(Loc.Get("inn.bond_stronger"));
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  (Romance: {companion.RomanceLevel}/10)");
                }
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("inn.seems_distracted", companion.Name));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("inn.higher_cha"));
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.build_trust"));
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Give a gift to a companion
    /// </summary>
    private async Task HandleGiveGift(Companion companion)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("inn.give_gift"), "yellow");
        terminal.WriteLine("");

        if (currentPlayer.Gold < 50)
        {
            terminal.WriteLine(Loc.Get("ui.not_enough_gold_gift_need"), "red");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.gift_options"));
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [1]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("inn.gift_simple")}");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [2]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("inn.gift_fine")}");

        if (currentPlayer.Gold >= 500)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [3]");
            terminal.SetColor("white");
            terminal.WriteLine($" {Loc.Get("inn.gift_rare")}");
        }

        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("white");
        terminal.WriteLine($" {Loc.Get("inn.cancel")}");
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("inn.choose"));

        int cost = 0;
        int loyaltyGain = 0;
        string giftDesc = "";

        switch (choice)
        {
            case "1":
                cost = 50;
                loyaltyGain = 3;
                giftDesc = Loc.Get("inn.gift_trinket");
                break;
            case "2":
                if (currentPlayer.Gold >= 200)
                {
                    cost = 200;
                    loyaltyGain = 8;
                    giftDesc = Loc.Get("inn.gift_jewelry");
                }
                break;
            case "3":
                if (currentPlayer.Gold >= 500)
                {
                    cost = 500;
                    loyaltyGain = 15;
                    giftDesc = Loc.Get("inn.gift_artifact");
                }
                break;
        }

        if (cost > 0 && currentPlayer.Gold >= cost)
        {
            currentPlayer.Gold -= cost;
            CompanionSystem.Instance.ModifyLoyalty(companion.Id, loyaltyGain, $"Received gift: {giftDesc}");

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("inn.give_gift_desc", companion.Name, giftDesc));
            terminal.WriteLine(Loc.Get("inn.gift_smile", companion.Name, loyaltyGain));
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Show history with this companion
    /// </summary>
    private async Task ShowCompanionHistory(Companion companion)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("inn.history_with", companion.Name), "cyan");
        terminal.WriteLine("");

        if (companion.History.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.journey_begun"));
        }
        else
        {
            terminal.SetColor("white");
            // Show last 10 events
            var recentHistory = companion.History.TakeLast(10).Reverse();
            foreach (var evt in recentHistory)
            {
                terminal.SetColor("gray");
                terminal.Write($"  {GameConfig.FormatShortDate(evt.Timestamp, currentPlayer.DateFormatPreference)} - ");
                terminal.SetColor("white");
                terminal.WriteLine(evt.Description);
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.days_together", companion.RecruitedDay > 0 ? StoryProgressionSystem.Instance.CurrentGameDay - companion.RecruitedDay : 0));
        terminal.WriteLine(Loc.Get("inn.total_loyalty", companion.LoyaltyLevel));

        await terminal.PressAnyKey();
    }

    #region Companion Equipment & Abilities

    /// <summary>
    /// Create a Character wrapper from a Companion for equipment management UI
    /// </summary>
    private Character CreateCompanionCharacterWrapper(Companion companion)
    {
        var wrapper = new Character
        {
            Name2 = companion.Name,
            Level = companion.Level,
            Class = companion.CombatRole switch
            {
                CombatRole.Tank => CharacterClass.Warrior,
                CombatRole.Healer => CharacterClass.Cleric,
                CombatRole.Damage => CharacterClass.Assassin,
                CombatRole.Hybrid => CharacterClass.Ranger,
                CombatRole.Bard => CharacterClass.Bard,
                _ => CharacterClass.Warrior
            },
            BaseStrength = companion.BaseStats.Attack,
            BaseDefence = companion.BaseStats.Defense,
            BaseDexterity = companion.BaseStats.Speed,
            BaseAgility = companion.BaseStats.Speed,
            BaseIntelligence = companion.BaseStats.MagicPower,
            BaseWisdom = companion.BaseStats.HealingPower,
            BaseCharisma = 10,
            BaseConstitution = 10 + companion.Level,
            BaseStamina = 10 + companion.Level,
            BaseMaxHP = companion.BaseStats.HP,
            BaseMaxMana = companion.BaseStats.MagicPower * 5
        };

        // Copy companion's equipment
        foreach (var kvp in companion.EquippedItems)
            wrapper.EquippedItems[kvp.Key] = kvp.Value;

        wrapper.RecalculateStats();

        // Set current HP to max for display purposes
        wrapper.HP = wrapper.MaxHP;
        wrapper.Mana = wrapper.MaxMana;

        return wrapper;
    }

    /// <summary>
    /// Manage equipment for a companion via Character wrapper
    /// </summary>
    private async Task ManageCompanionEquipment(Companion companion)
    {
        var wrapper = CreateCompanionCharacterWrapper(companion);

        await ManageCompanionCharacterEquipment(wrapper);

        // Sync equipment changes back to the companion
        companion.EquippedItems.Clear();
        foreach (var kvp in wrapper.EquippedItems)
        {
            if (kvp.Value > 0)
                companion.EquippedItems[kvp.Key] = kvp.Value;
        }

        await SaveSystem.Instance.AutoSave(currentPlayer);
    }

    /// <summary>
    /// Manage equipment for a specific character (companion wrapper)
    /// Based on TeamCornerLocation.ManageCharacterEquipment
    /// </summary>
    private async Task ManageCompanionCharacterEquipment(Character target)
    {
        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader($"EQUIPMENT: {target.DisplayName.ToUpper()}", "bright_cyan");
            terminal.WriteLine("");

            // Show target's stats
            terminal.SetColor("white");
            terminal.WriteLine($"  Level: {target.Level}  Class: {target.ClassName}  Race: {target.Race}");
            terminal.WriteLine($"  HP: {target.HP}/{target.MaxHP}  Mana: {target.Mana}/{target.MaxMana}");
            terminal.WriteLine($"  STR: {target.Strength}  DEX: {target.Dexterity}  AGI: {target.Agility}  CON: {target.Constitution}");
            terminal.WriteLine($"  INT: {target.Intelligence}  WIS: {target.Wisdom}  CHA: {target.Charisma}  DEF: {target.Defence}");
            terminal.WriteLine("");

            // Show current equipment
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("inn.current_equipment"));
            terminal.SetColor("white");

            CompanionDisplayEquipmentSlot(target, EquipmentSlot.MainHand, "Main Hand");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.OffHand, "Off Hand");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Head, "Head");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Body, "Body");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Arms, "Arms");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Hands, "Hands");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Legs, "Legs");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Feet, "Feet");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Waist, "Belt");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Face, "Face");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Cloak, "Cloak");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Neck, "Neck");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.LFinger, "Left Ring");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.RFinger, "Right Ring");
            terminal.WriteLine("");

            // Show options
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("inn.options"));
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.equip_from_inventory"));
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("U");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.unequip_item"));
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.take_all_equipment"));
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.equip_best"));
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.done_return"));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("ui.choice"));
            terminal.SetColor("white");

            var choice = (await terminal.ReadLineAsync()).ToUpper().Trim();

            switch (choice)
            {
                case "E":
                    await CompanionEquipItemToCharacter(target);
                    break;
                case "U":
                    await CompanionUnequipItemFromCharacter(target);
                    break;
                case "T":
                    await CompanionTakeAllEquipment(target);
                    break;
                case "B":
                    await CompanionEquipBestGear(target);
                    break;
                case "Q":
                case "":
                    return;
            }
        }
    }

    /// <summary>
    /// Get display name for an equipment item, hiding real name if unidentified
    /// </summary>
    private static string GetEquipmentDisplayName(Equipment item)
    {
        if (item.IsIdentified) return item.Name;
        return item.Slot switch
        {
            EquipmentSlot.MainHand => "Unidentified Weapon",
            EquipmentSlot.OffHand => item.WeaponPower > 0 ? "Unidentified Weapon" : "Unidentified Shield",
            EquipmentSlot.Head => "Unidentified Helm",
            EquipmentSlot.Body => "Unidentified Armor",
            EquipmentSlot.Arms => "Unidentified Bracers",
            EquipmentSlot.Hands => "Unidentified Gauntlets",
            EquipmentSlot.Legs => "Unidentified Greaves",
            EquipmentSlot.Feet => "Unidentified Boots",
            EquipmentSlot.Waist => "Unidentified Belt",
            EquipmentSlot.Face => "Unidentified Mask",
            EquipmentSlot.Cloak => "Unidentified Cloak",
            EquipmentSlot.Neck => "Unidentified Amulet",
            EquipmentSlot.LFinger or EquipmentSlot.RFinger => "Unidentified Ring",
            _ => "Unidentified Item"
        };
    }

    private void CompanionDisplayEquipmentSlot(Character target, EquipmentSlot slot, string label)
    {
        var item = target.GetEquipment(slot);
        terminal.SetColor("gray");
        terminal.Write($"  {label,-12}: ");
        if (item != null)
        {
            if (!item.IsIdentified)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine(GetEquipmentDisplayName(item));
            }
            else
            {
                terminal.SetColor(item.GetRarityColor());
                terminal.Write(item.Name);

                // Build compact stat summary
                var stats = new List<string>();
                if (item.WeaponPower > 0) stats.Add($"{Loc.Get("ui.stat_wp")}:{item.WeaponPower}");
                if (item.ArmorClass > 0) stats.Add($"{Loc.Get("ui.stat_ac")}:{item.ArmorClass}");
                if (item.ShieldBonus > 0) stats.Add($"{Loc.Get("ui.stat_block")}:{item.ShieldBonus}");
                if (item.DefenceBonus > 0) stats.Add($"{Loc.Get("ui.stat_def")}:{item.DefenceBonus}");
                if (item.StrengthBonus != 0) stats.Add($"{Loc.Get("ui.stat_str")}:{item.StrengthBonus:+#;-#}");
                if (item.DexterityBonus != 0) stats.Add($"{Loc.Get("ui.stat_dex")}:{item.DexterityBonus:+#;-#}");
                if (item.AgilityBonus != 0) stats.Add($"{Loc.Get("ui.stat_agi")}:{item.AgilityBonus:+#;-#}");
                if (item.ConstitutionBonus != 0) stats.Add($"{Loc.Get("ui.stat_con")}:{item.ConstitutionBonus:+#;-#}");
                if (item.IntelligenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_int")}:{item.IntelligenceBonus:+#;-#}");
                if (item.WisdomBonus != 0) stats.Add($"{Loc.Get("ui.stat_wis")}:{item.WisdomBonus:+#;-#}");
                if (item.CharismaBonus != 0) stats.Add($"{Loc.Get("ui.stat_cha")}:{item.CharismaBonus:+#;-#}");
                if (item.MaxHPBonus > 0) stats.Add($"{Loc.Get("ui.stat_hp")}:{item.MaxHPBonus:+#}");
                if (item.MaxManaBonus > 0) stats.Add($"{Loc.Get("ui.stat_mp")}:{item.MaxManaBonus:+#}");
                if (item.CriticalChanceBonus > 0) stats.Add($"{Loc.Get("ui.stat_crit")}:{item.CriticalChanceBonus}%");
                if (item.LifeSteal > 0) stats.Add($"{Loc.Get("ui.stat_leech")}:{item.LifeSteal}%");
                if (item.MagicResistance > 0) stats.Add($"{Loc.Get("ui.stat_mr")}:{item.MagicResistance}%");
                if (item.PoisonDamage > 0) stats.Add($"{Loc.Get("ui.stat_psn")}:{item.PoisonDamage}");

                if (stats.Count > 0)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write($" [{string.Join(" ", stats)}]");
                }
                terminal.WriteLine("");
            }
        }
        else
        {
            // Check if off-hand is empty because of a two-handed weapon
            if (slot == EquipmentSlot.OffHand)
            {
                var mainHand = target.GetEquipment(EquipmentSlot.MainHand);
                if (mainHand?.Handedness == WeaponHandedness.TwoHanded)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(Loc.Get("inn.using_2h_weapon"));
                    return;
                }
            }
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("inn.slot_empty"));
        }
    }

    private async Task CompanionEquipItemToCharacter(Character target)
    {
        terminal.ClearScreen();
        WriteSectionHeader($"EQUIP ITEM TO {target.DisplayName.ToUpper()}", "bright_cyan");
        terminal.WriteLine("");

        // Step 1: Pick a slot
        var selectedSlot = await PromptForEquipmentSlot(target);
        if (selectedSlot == null) return;

        // Step 2: Get items that match this slot
        var equipmentItems = GetItemsForSlot(selectedSlot.Value);

        if (equipmentItems.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("  No items available for this slot.");
            await Task.Delay(2000);
            return;
        }

        // Step 3: Show current item in slot
        terminal.WriteLine("");
        var currentItem = target.GetEquipment(selectedSlot.Value);
        terminal.SetColor("white");
        terminal.Write($"  Current: ");
        if (currentItem != null)
        {
            if (!currentItem.IsIdentified)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine(GetEquipmentDisplayName(currentItem));
            }
            else
            {
                terminal.SetColor(currentItem.GetRarityColor());
                terminal.Write(currentItem.Name);
                WriteEquipmentStatSummary(currentItem);
                terminal.WriteLine("");
            }
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("Empty");
        }
        terminal.WriteLine("");

        // Step 4: Display matching items with full stats
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.available_equipment"));
        terminal.WriteLine("");
        DisplayEquipmentItemList(equipmentItems, target);

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("inn.select_item"));
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int itemIdx) || itemIdx < 1 || itemIdx > equipmentItems.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        var (selectedItem, wasEquipped, sourceSlot) = equipmentItems[itemIdx - 1];

        // Block unidentified items
        if (!selectedItem.IsIdentified)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.must_identify"));
            await Task.Delay(2000);
            return;
        }

        // Check if target can equip
        if (!selectedItem.CanEquip(target, out string equipReason))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.cannot_use", target.DisplayName, equipReason));
            await Task.Delay(2000);
            return;
        }

        // Use the slot the player already picked (no need to ask which hand)
        EquipmentSlot? targetSlot = selectedSlot.Value;

        // Track items in target's inventory BEFORE equipping, so we can move displaced items to player
        var targetInventoryBefore = target.Inventory.Count;

        // Equip to target FIRST - EquipItem adds displaced items to target's inventory
        var result = target.EquipItem(selectedItem, targetSlot, out string message);
        target.RecalculateStats();

        if (result)
        {
            // Equip succeeded — NOW remove the source item from player
            if (wasEquipped && sourceSlot.HasValue)
            {
                currentPlayer.UnequipSlot(sourceSlot.Value);
                currentPlayer.RecalculateStats();
            }
            else
            {
                // Remove from inventory by reference (not name — avoids wrong-item removal with duplicates)
                // Find the matching Item by name, value, attack — use best match
                var invItem = currentPlayer.Inventory.FirstOrDefault(i =>
                    i.Name == selectedItem.Name && i.Attack == selectedItem.WeaponPower && i.Armor == selectedItem.ArmorClass);
                if (invItem == null)
                    invItem = currentPlayer.Inventory.FirstOrDefault(i => i.Name == selectedItem.Name);
                if (invItem != null)
                    currentPlayer.Inventory.Remove(invItem);
            }

            // Move any items that were added to target's inventory (displaced equipment) to player's inventory
            if (target.Inventory.Count > targetInventoryBefore)
            {
                var displacedItems = target.Inventory.Skip(targetInventoryBefore).ToList();
                foreach (var displaced in displacedItems)
                {
                    target.Inventory.Remove(displaced);
                    currentPlayer.Inventory.Add(displaced);
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.equipped_item", target.DisplayName, selectedItem.Name));
            if (!string.IsNullOrEmpty(message))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(message);
            }
        }
        else
        {
            // Failed - item was never removed from player, just show error
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.failed_equip", message));
        }

        await Task.Delay(2000);
    }

    private async Task CompanionUnequipItemFromCharacter(Character target)
    {
        terminal.ClearScreen();
        WriteSectionHeader($"UNEQUIP FROM {target.DisplayName.ToUpper()}", "bright_cyan");
        terminal.WriteLine("");

        // Get all equipped slots
        var equippedSlots = new List<(EquipmentSlot slot, Equipment item)>();
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var item = target.GetEquipment(slot);
            if (item != null)
            {
                equippedSlots.Add((slot, item));
            }
        }

        if (equippedSlots.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{target.DisplayName} has no equipment to unequip.");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.equipped_items"));
        terminal.WriteLine("");

        for (int i = 0; i < equippedSlots.Count; i++)
        {
            var (slot, item) = equippedSlots[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("gray");
            terminal.Write($"[{slot.GetDisplayName(),-12}] ");
            terminal.SetColor(item.IsIdentified ? "white" : "magenta");
            terminal.Write(GetEquipmentDisplayName(item));
            if (item.IsCursed)
            {
                terminal.SetColor("red");
                terminal.Write(Loc.Get("inn.cursed_label"));
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("inn.select_unequip"));
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int slotIdx) || slotIdx < 1 || slotIdx > equippedSlots.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        var (selectedSlot, selectedItem) = equippedSlots[slotIdx - 1];

        // Check if cursed
        if (selectedItem.IsCursed)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.cursed_cannot_remove", selectedItem.Name));
            await Task.Delay(2000);
            return;
        }

        // Unequip and add to player inventory
        var unequipped = target.UnequipSlot(selectedSlot);
        if (unequipped != null)
        {
            target.RecalculateStats();
            var legacyItem = CompanionConvertEquipmentToItem(unequipped);
            currentPlayer.Inventory.Add(legacyItem);

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.took_from", unequipped.Name, target.DisplayName));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("inn.added_inventory"));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.failed_unequip"));
        }

        await Task.Delay(2000);
    }

    private async Task CompanionTakeAllEquipment(Character target)
    {
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.take_all_confirm", target.DisplayName));
        terminal.Write(Loc.Get("inn.leave_nothing"));
        terminal.SetColor("white");

        var confirm = await terminal.ReadLineAsync();
        if (!confirm.ToUpper().StartsWith("Y"))
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        int itemsTaken = 0;
        var cursedItems = new List<string>();

        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var item = target.GetEquipment(slot);
            if (item != null)
            {
                if (item.IsCursed)
                {
                    cursedItems.Add(GetEquipmentDisplayName(item));
                    continue;
                }

                var unequipped = target.UnequipSlot(slot);
                if (unequipped != null)
                {
                    var legacyItem = CompanionConvertEquipmentToItem(unequipped);
                    currentPlayer.Inventory.Add(legacyItem);
                    itemsTaken++;
                }
            }
        }

        target.RecalculateStats();

        terminal.WriteLine("");
        if (itemsTaken > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.took_items", itemsTaken, itemsTaken != 1 ? "s" : "", target.DisplayName));
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.no_equipment_to_take", target.DisplayName));
        }

        if (cursedItems.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.cursed_not_removed", string.Join(", ", cursedItems)));
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Auto-equip the best available items from player inventory across all slots for a companion.
    /// Scores items by primary stat (weapon power for weapons, armor class for armor, stat total for accessories).
    /// </summary>
    private async Task CompanionEquipBestGear(Character target)
    {
        terminal.ClearScreen();
        WriteBoxHeader($"{Loc.Get("inn.equip_best")}: {target.DisplayName.ToUpper()}", "bright_cyan");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("inn.equip_best_confirm", target.DisplayName));
        var confirm = (await terminal.ReadLineAsync()).ToUpper().Trim();
        if (confirm != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("inn.equip_best_scanning", target.DisplayName));
        terminal.WriteLine("");

        int equippedCount = 0;

        // Process each equipment slot
        var slotsToCheck = new[] {
            EquipmentSlot.MainHand, EquipmentSlot.OffHand,
            EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Arms,
            EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet,
            EquipmentSlot.Waist, EquipmentSlot.Face, EquipmentSlot.Cloak,
            EquipmentSlot.Neck, EquipmentSlot.LFinger, EquipmentSlot.RFinger
        };

        foreach (var slot in slotsToCheck)
        {
            // Skip off-hand if companion is using a two-handed weapon
            if (slot == EquipmentSlot.OffHand && target.IsTwoHanding)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"  {slot.GetDisplayName()}: Using two-handed weapon");
                continue;
            }

            // Get all matching items from player inventory for this slot
            var candidates = GetItemsForSlot(slot)
                .Where(x => !x.isEquipped && x.item.IsIdentified && !x.item.IsCursed)
                .Where(x => x.item.CanEquip(target, out _))
                .ToList();

            if (candidates.Count == 0)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("inn.equip_best_no_upgrade", slot.GetDisplayName()));
                continue;
            }

            // Score each candidate by primary stat value
            var bestCandidate = candidates
                .OrderByDescending(x => ScoreEquipment(x.item, slot))
                .First();

            var currentItem = target.GetEquipment(slot);
            int currentScore = currentItem != null ? ScoreEquipment(currentItem, slot) : 0;
            int newScore = ScoreEquipment(bestCandidate.item, slot);

            // Only equip if it's an upgrade (or slot is empty)
            if (newScore <= currentScore && currentItem != null)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("inn.equip_best_no_upgrade", slot.GetDisplayName()));
                continue;
            }

            // Remove from player inventory (find by name match)
            var invItem = currentPlayer.Inventory.FirstOrDefault(i => i.Name == bestCandidate.item.Name);
            if (invItem == null) continue;
            currentPlayer.Inventory.Remove(invItem);

            // Track items before equipping so displaced items go to player
            var targetInventoryBefore = target.Inventory.Count;

            // Equip to target
            if (target.EquipItem(bestCandidate.item, slot, out string message))
            {
                // Move displaced items back to player inventory
                if (target.Inventory.Count > targetInventoryBefore)
                {
                    var displacedItems = target.Inventory.Skip(targetInventoryBefore).ToList();
                    foreach (var displaced in displacedItems)
                    {
                        target.Inventory.Remove(displaced);
                        currentPlayer.Inventory.Add(displaced);
                    }
                }

                equippedCount++;
                terminal.SetColor("bright_green");
                if (currentItem != null)
                    terminal.WriteLine(Loc.Get("inn.equip_best_upgraded", slot.GetDisplayName(), currentItem.Name, bestCandidate.item.Name));
                else
                    terminal.WriteLine(Loc.Get("inn.equip_best_equipped", slot.GetDisplayName(), bestCandidate.item.Name));
            }
            else
            {
                // Failed - return item to player
                currentPlayer.Inventory.Add(invItem);
            }
        }

        target.RecalculateStats();
        terminal.WriteLine("");

        if (equippedCount > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.equip_best_done", equippedCount, target.DisplayName));
            await SaveSystem.Instance.AutoSave(currentPlayer);
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.equip_best_none", target.DisplayName));
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Score an equipment item for auto-equip comparison.
    /// Weapons scored by weapon power, armor by AC, accessories by total stat bonuses.
    /// </summary>
    private static int ScoreEquipment(Equipment item, EquipmentSlot slot)
    {
        // Base score from primary stat
        int score = 0;

        if (slot == EquipmentSlot.MainHand || slot == EquipmentSlot.OffHand)
        {
            score = item.WeaponPower * 10 + item.ShieldBonus * 8;
        }
        else if (slot == EquipmentSlot.LFinger || slot == EquipmentSlot.RFinger ||
                 slot == EquipmentSlot.Neck)
        {
            // Accessories: score by total stat bonuses
            score = 0;
        }
        else
        {
            // Armor slots
            score = item.ArmorClass * 10;
        }

        // Add stat bonuses (weighted equally)
        score += (item.StrengthBonus + item.DexterityBonus + item.AgilityBonus +
                  item.ConstitutionBonus + item.IntelligenceBonus + item.WisdomBonus +
                  item.CharismaBonus) * 3;
        score += item.MaxHPBonus * 2;
        score += item.MaxManaBonus * 2;
        score += item.DefenceBonus * 3;
        score += item.MagicResistance * 2;
        score += item.CriticalChanceBonus * 2;
        score += item.LifeSteal * 2;
        score += item.StaminaBonus * 2;

        return score;
    }

    private Item CompanionConvertEquipmentToItem(Equipment equipment)
    {
        // Delegate to the canonical implementation that preserves LootEffects
        // (INT, CON, enchantments, proc effects)
        return currentPlayer.ConvertEquipmentToLegacyItem(equipment);
    }

    private ObjType CompanionSlotToObjType(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head => ObjType.Head,
        EquipmentSlot.Body => ObjType.Body,
        EquipmentSlot.Arms => ObjType.Arms,
        EquipmentSlot.Hands => ObjType.Hands,
        EquipmentSlot.Legs => ObjType.Legs,
        EquipmentSlot.Feet => ObjType.Feet,
        EquipmentSlot.MainHand => ObjType.Weapon,
        EquipmentSlot.OffHand => ObjType.Shield,
        EquipmentSlot.Neck => ObjType.Neck,
        EquipmentSlot.Neck2 => ObjType.Neck,
        EquipmentSlot.LFinger => ObjType.Fingers,
        EquipmentSlot.RFinger => ObjType.Fingers,
        EquipmentSlot.Cloak => ObjType.Abody,
        EquipmentSlot.Waist => ObjType.Waist,
        _ => ObjType.Magic
    };

    /// <summary>
    /// Manage combat abilities for a companion - toggle on/off
    /// </summary>
    private async Task ManageCompanionAbilities(Companion companion)
    {
        // Map CombatRole to CharacterClass (same as GetCompanionsAsCharacters)
        var charClass = companion.CombatRole switch
        {
            CombatRole.Tank => CharacterClass.Warrior,
            CombatRole.Healer => CharacterClass.Cleric,
            CombatRole.Damage => CharacterClass.Assassin,
            CombatRole.Hybrid => CharacterClass.Ranger,
            CombatRole.Bard => CharacterClass.Bard,
            _ => CharacterClass.Warrior
        };

        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader($"COMBAT SKILLS: {companion.Name.ToUpper()}", "bright_cyan");
            terminal.SetColor("white");
            terminal.WriteLine($"  Role: {companion.CombatRole} (as {charClass}) | Level: {companion.Level}");
            terminal.WriteLine("");

            // Get all abilities for this class at this level
            var tempChar = new Character { Class = charClass, Level = companion.Level };
            var abilities = ClassAbilitySystem.GetAvailableAbilities(tempChar);

            if (abilities.Count == 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("inn.no_abilities_yet"));
                await terminal.PressAnyKey();
                return;
            }

            int enabledCount = 0;
            for (int i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];
                bool isDisabled = companion.DisabledAbilities.Contains(ability.Id);
                if (!isDisabled) enabledCount++;

                terminal.SetColor("bright_yellow");
                terminal.Write($"  [{i + 1,2}] ");
                terminal.SetColor(isDisabled ? "darkgray" : "bright_green");
                terminal.Write(isDisabled ? "[OFF] " : "[ON]  ");
                terminal.SetColor(isDisabled ? "gray" : "white");
                terminal.Write($"{ability.Name,-24}");
                terminal.SetColor("darkgray");
                terminal.Write($" {ability.StaminaCost,2} ST  Lv{ability.LevelRequired,-3}  ");
                terminal.SetColor(isDisabled ? "darkgray" : "gray");
                terminal.WriteLine(ability.Description.Length > 30 ? ability.Description[..30] + "..." : ability.Description);
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {enabledCount}/{abilities.Count} abilities enabled");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(IsScreenReader
                ? "  1 through N. Toggle ability  A. Enable all  0. Return"
                : "  [1-N] Toggle ability  [A] Enable all  [0] Return");
            terminal.WriteLine("");

            var input = await terminal.GetInput(Loc.Get("ui.choice"));
            if (string.IsNullOrWhiteSpace(input) || input.Trim() == "0") break;

            if (input.Trim().ToUpper() == "A")
            {
                companion.DisabledAbilities.Clear();
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("inn.all_abilities_enabled"));
                await Task.Delay(800);
                continue;
            }

            if (int.TryParse(input.Trim(), out int idx) && idx >= 1 && idx <= abilities.Count)
            {
                var ability = abilities[idx - 1];
                if (companion.DisabledAbilities.Contains(ability.Id))
                {
                    companion.DisabledAbilities.Remove(ability.Id);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  Enabled: {ability.Name}");
                }
                else
                {
                    companion.DisabledAbilities.Add(ability.Id);
                    terminal.SetColor("red");
                    terminal.WriteLine($"  Disabled: {ability.Name}");
                }
                await Task.Delay(600);
            }
        }

        await SaveSystem.Instance.AutoSave(currentPlayer);
    }

    #endregion

    #endregion

    #region Stat Training

    private async Task HandleStatTraining()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("inn.master_trainer"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.trainer_intro1"));
        terminal.WriteLine(Loc.Get("inn.trainer_intro2"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("inn.trainer_quote1"));
        terminal.WriteLine(Loc.Get("inn.trainer_quote2"));
        terminal.WriteLine(Loc.Get("inn.trainer_quote3"));
        terminal.WriteLine("");

        var statNames = new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA", "AGI", "STA" };
        var statLabels = new[] { "Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma", "Agility", "Stamina" };

        terminal.SetColor("cyan");
        terminal.WriteLine($"{"#",-4} {"Stat",-16} {"Current",-10} {"Trained",-10} {"Next Cost",-12}");
        WriteDivider(55, "darkgray");

        for (int i = 0; i < statNames.Length; i++)
        {
            int timesTrained = 0;
            currentPlayer.StatTrainingCounts?.TryGetValue(statNames[i], out timesTrained);

            long currentVal = GetStatValue(statNames[i]);
            string trainedStr = $"{timesTrained}/{GameConfig.MaxStatTrainingsPerStat}";

            if (timesTrained >= GameConfig.MaxStatTrainingsPerStat)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"{i + 1,-4} {statLabels[i],-16} {currentVal,-10} {trainedStr,-10} {"MAXED",-12}");
            }
            else
            {
                long cost = CalculateTrainingCost(timesTrained);
                terminal.SetColor("white");
                terminal.Write($"{i + 1,-4} {statLabels[i],-16} {currentVal,-10} {trainedStr,-10} {cost:N0}g");

                // Show material requirements for 4th/5th training
                var matReqs = GetTrainingMaterialRequirements(timesTrained);
                if (matReqs != null)
                {
                    terminal.Write("  ");
                    for (int j = 0; j < matReqs.Length; j++)
                    {
                        var mat = GameConfig.GetMaterialById(matReqs[j].materialId);
                        bool has = currentPlayer.HasMaterial(matReqs[j].materialId, matReqs[j].count);
                        terminal.SetColor(has ? "bright_green" : "red");
                        terminal.Write($"{matReqs[j].count}x {mat?.Name ?? matReqs[j].materialId}");
                        if (j < matReqs.Length - 1)
                        {
                            terminal.SetColor("gray");
                            terminal.Write(" + ");
                        }
                    }
                }
                terminal.WriteLine("");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("inn.your_gold", currentPlayer.Gold.ToString("N0")));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("inn.train_prompt"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= 8)
        {
            string statKey = statNames[choice - 1];
            string statLabel = statLabels[choice - 1];

            int timesTrained = 0;
            currentPlayer.StatTrainingCounts?.TryGetValue(statKey, out timesTrained);

            if (timesTrained >= GameConfig.MaxStatTrainingsPerStat)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("inn.stat_maxed_quote", statLabel));
                terminal.WriteLine(Loc.Get("inn.find_other_ways"));
                await terminal.PressAnyKey();
                return;
            }

            long cost = CalculateTrainingCost(timesTrained);

            if (currentPlayer.Gold < cost)
            {
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("inn.need_gold_train", cost.ToString("N0"), currentPlayer.Gold.ToString("N0")));
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("inn.come_back_afford"));
                await terminal.PressAnyKey();
                return;
            }

            // Check material requirements for 4th/5th training
            var trainingMatReqs = GetTrainingMaterialRequirements(timesTrained);
            if (trainingMatReqs != null)
            {
                var missing = trainingMatReqs.Where(r => !currentPlayer.HasMaterial(r.materialId, r.count)).ToList();
                if (missing.Count > 0)
                {
                    terminal.WriteLine("");
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(Loc.Get("inn.need_materials"));
                    foreach (var req in missing)
                    {
                        var mat = GameConfig.GetMaterialById(req.materialId);
                        terminal.SetColor("red");
                        terminal.WriteLine($"  {Loc.Get("inn.missing_material", req.count, mat?.Name ?? req.materialId)}");
                    }
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"  {Loc.Get("inn.materials_dungeon")}");
                    await terminal.PressAnyKey();
                    return;
                }
            }

            // Pay and train
            currentPlayer.Gold -= cost;
            currentPlayer.Statistics?.RecordGoldSpent(cost);

            // Consume materials
            if (trainingMatReqs != null)
            {
                foreach (var req in trainingMatReqs)
                {
                    currentPlayer.ConsumeMaterial(req.materialId, req.count);
                    var mat = GameConfig.GetMaterialById(req.materialId);
                    terminal.SetColor(mat?.Color ?? "white");
                    terminal.WriteLine($"  The {mat?.Name ?? req.materialId} dissolves into your body, fueling the transformation...");
                }
                await Task.Delay(500);
            }

            // Apply the +1 stat bonus
            ApplyStatBonus(statKey);

            // Record training
            currentPlayer.StatTrainingCounts ??= new Dictionary<string, int>();
            currentPlayer.StatTrainingCounts[statKey] = timesTrained + 1;

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("inn.pay_trainer", cost.ToString("N0")));
            terminal.WriteLine("");

            // Training narrative
            switch (statKey)
            {
                case "STR":
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("inn.train_str"));
                    break;
                case "DEX":
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("inn.train_dex"));
                    break;
                case "CON":
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("inn.train_con"));
                    break;
                case "INT":
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("inn.train_int"));
                    break;
                case "WIS":
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("inn.train_wis"));
                    break;
                case "CHA":
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("inn.train_cha"));
                    break;
                case "AGI":
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("inn.train_agi"));
                    break;
                case "STA":
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("inn.train_sta"));
                    break;
            }

            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            long newVal = GetStatValue(statKey);
            terminal.WriteLine(Loc.Get("inn.stat_increased", statLabel, newVal));
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("inn.sessions_remaining", GameConfig.MaxStatTrainingsPerStat - timesTrained - 1, statLabel));
        }

        await terminal.PressAnyKey();
    }

    private long CalculateTrainingCost(int timesTrained)
    {
        long baseCost = currentPlayer.Level * GameConfig.StatTrainingBaseCostPerLevel;
        return baseCost * (long)((timesTrained + 1) * (timesTrained + 1));
    }

    /// <summary>
    /// Returns material requirements for the Nth stat training (0-indexed).
    /// 4th training (index 3) requires Heart of the Ocean.
    /// 5th training (index 4) requires Heart of the Ocean + Eye of Manwe.
    /// </summary>
    private static (string materialId, int count)[]? GetTrainingMaterialRequirements(int timesTrained)
    {
        return timesTrained switch
        {
            3 => new[] { ("heart_of_the_ocean", 1) },
            4 => new[] { ("heart_of_the_ocean", 1), ("eye_of_manwe", 1) },
            _ => null
        };
    }

    private long GetStatValue(string statKey)
    {
        return statKey switch
        {
            "STR" => currentPlayer.Strength,
            "DEX" => currentPlayer.Dexterity,
            "CON" => currentPlayer.Constitution,
            "INT" => currentPlayer.Intelligence,
            "WIS" => currentPlayer.Wisdom,
            "CHA" => currentPlayer.Charisma,
            "AGI" => currentPlayer.Agility,
            "STA" => currentPlayer.Stamina,
            _ => 0
        };
    }

    private void ApplyStatBonus(string statKey)
    {
        switch (statKey)
        {
            case "STR": currentPlayer.BaseStrength++; currentPlayer.Strength++; break;
            case "DEX": currentPlayer.BaseDexterity++; currentPlayer.Dexterity++; break;
            case "CON": currentPlayer.BaseConstitution++; currentPlayer.Constitution++; break;
            case "INT": currentPlayer.BaseIntelligence++; currentPlayer.Intelligence++; break;
            case "WIS": currentPlayer.BaseWisdom++; currentPlayer.Wisdom++; break;
            case "CHA": currentPlayer.BaseCharisma++; currentPlayer.Charisma++; break;
            case "AGI": currentPlayer.BaseAgility++; currentPlayer.Agility++; break;
            case "STA": currentPlayer.BaseStamina++; currentPlayer.Stamina++; break;
        }
    }

    #endregion

    #region Gambling Den

    private int _armWrestlesToday = 0;

    private async Task HandleGamblingDen()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("inn.gambling_den"), "bright_red");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.gambling_intro1"));
        terminal.WriteLine(Loc.Get("inn.gambling_intro2"));
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("inn.your_gold", currentPlayer.Gold.ToString("N0")));
        terminal.WriteLine("");

        var armWrestles = DoorMode.IsOnlineMode ? currentPlayer.ArmWrestlesToday : _armWrestlesToday;

        if (IsScreenReader)
        {
            terminal.WriteLine(Loc.Get("inn.games_available"));
            terminal.WriteLine("");
            WriteSRMenuOption("1", Loc.Get("inn.game_dice"));
            WriteSRMenuOption("2", Loc.Get("inn.game_bones"));
            WriteSRMenuOption("3", Loc.Get("inn.game_arm_wrestling", armWrestles, GameConfig.MaxArmWrestlesPerDay));
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("inn.games_available"));
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.game_highlow"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.game_skulls"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.game_arm_wrestling", armWrestles, GameConfig.MaxArmWrestlesPerDay));
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("inn.choose_game"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        switch (input?.Trim())
        {
            case "1":
                await PlayHighLowDice();
                break;
            case "2":
                await PlaySkullAndBones();
                break;
            case "3":
                await PlayArmWrestling();
                break;
        }
    }

    private async Task PlayHighLowDice()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("inn.high_low_dice"), "bright_yellow");
        terminal.WriteLine("");

        if (currentPlayer.Gold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.no_gold_wager"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.gold_on_hand", currentPlayer.Gold.ToString("N0")));
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("inn.how_much_wager"));
        terminal.SetColor("white");
        string betInput = await terminal.ReadLineAsync();

        if (!long.TryParse(betInput, out long bet) || bet <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.invalid_bet"));
            await terminal.PressAnyKey();
            return;
        }

        if (bet > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.not_enough_gold_wager"));
            await terminal.PressAnyKey();
            return;
        }

        var rng = Random.Shared;
        int doubleDownCount = 0;

        while (true)
        {
            int firstRoll = rng.Next(1, 7);
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("inn.dealer_rolls", firstRoll));
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("inn.hl_will_next_be"));
            terminal.SetColor("bright_yellow");
            terminal.Write("[H]");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("inn.hl_igher_or"));
            terminal.SetColor("bright_yellow");
            terminal.Write("[L]");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("inn.hl_ower"));
            terminal.SetColor("white");
            string guess = (await terminal.ReadLineAsync()).ToUpper().Trim();

            if (guess != "H" && guess != "L")
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("inn.invalid_forfeited"));
                currentPlayer.Gold -= bet;
                currentPlayer.Statistics?.RecordGoldSpent(bet);
                break;
            }

            int secondRoll = rng.Next(1, 7);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("inn.next_roll", secondRoll));
            await Task.Delay(800);

            if (secondRoll == firstRoll)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("inn.tie_returned"));
                break;
            }

            bool guessedHigher = guess == "H";
            bool wasHigher = secondRoll > firstRoll;

            if (guessedHigher == wasHigher)
            {
                long winnings = (long)(bet * GameConfig.HighLowPayoutMultiplier) - bet;
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("inn.you_win", winnings.ToString("N0")));
                doubleDownCount++;

                if (doubleDownCount < GameConfig.GamblingMaxDoubleDown)
                {
                    long totalPot = bet + winnings;
                    terminal.WriteLine("");
                    terminal.SetColor("cyan");
                    terminal.Write(Loc.Get("inn.double_nothing", totalPot.ToString("N0")));
                    terminal.SetColor("bright_yellow");
                    terminal.Write("[Y]");
                    terminal.SetColor("cyan");
                    terminal.Write("/");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("[N]");
                    terminal.SetColor("cyan");
                    terminal.Write(" ");
                    terminal.SetColor("white");
                    string dd = (await terminal.ReadLineAsync()).ToUpper().Trim();

                    if (dd == "Y")
                    {
                        bet = totalPot;
                        continue;
                    }
                    else
                    {
                        currentPlayer.Gold += winnings;
                        break;
                    }
                }
                else
                {
                    currentPlayer.Gold += winnings;
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("inn.max_double_down"));
                    break;
                }
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("inn.you_lose", bet.ToString("N0")));
                currentPlayer.Gold -= bet;
                currentPlayer.Statistics?.RecordGoldSpent(bet);
                break;
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("inn.gold_remaining", currentPlayer.Gold.ToString("N0")));
        await terminal.PressAnyKey();
    }

    private async Task PlaySkullAndBones()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("inn.skull_bones"), "bright_cyan");
        terminal.WriteLine("");

        if (currentPlayer.Gold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.no_gold_wager"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.sb_rules1"));
        terminal.WriteLine(Loc.Get("inn.sb_rules2"));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.gold_on_hand", currentPlayer.Gold.ToString("N0")));
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("inn.how_much_wager"));
        terminal.SetColor("white");
        string betInput = await terminal.ReadLineAsync();

        if (!long.TryParse(betInput, out long bet) || bet <= 0 || bet > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine(bet > currentPlayer.Gold ? Loc.Get("inn.not_enough_gold_bet") : Loc.Get("inn.invalid_bet"));
            await terminal.PressAnyKey();
            return;
        }

        var rng = Random.Shared;
        string[] faceTiles = { "Skull", "Crown", "Sword" };

        // Player's turn
        int playerTotal = 0;
        int playerCards = 0;
        var playerHand = new List<string>();

        int DrawTile()
        {
            int val = rng.Next(1, 11);
            if (val == 10)
            {
                string face = faceTiles[rng.Next(faceTiles.Length)];
                playerHand.Add(face);
            }
            else
            {
                playerHand.Add(val.ToString());
            }
            return val;
        }

        // Initial two tiles
        int tile1 = DrawTile();
        playerTotal += tile1;
        playerCards++;
        int tile2 = DrawTile();
        playerTotal += tile2;
        playerCards++;

        // Check for blackjack
        bool playerBlackjack = playerTotal == 21 && playerCards == 2;

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("inn.sb_your_tiles", string.Join(", ", playerHand), playerTotal));

        if (playerBlackjack)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.sb_natural21"));
        }

        // Player draws
        while (playerTotal < 21 && !playerBlackjack)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.Write(Loc.Get("inn.sb_hit"));
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("inn.sb_or"));
            terminal.SetColor("bright_yellow");
            terminal.Write(Loc.Get("inn.sb_stand"));
            terminal.SetColor("cyan");
            terminal.Write("? ");
            terminal.SetColor("white");
            string action = (await terminal.ReadLineAsync()).ToUpper().Trim();

            if (action == "H")
            {
                playerHand.Clear();
                int newTile = rng.Next(1, 11);
                string tileName = newTile == 10 ? faceTiles[rng.Next(faceTiles.Length)] : newTile.ToString();
                playerTotal += newTile;
                playerCards++;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("inn.sb_drew", tileName, newTile, playerTotal));

                if (playerTotal > 21)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("inn.sb_bust"));
                    currentPlayer.Gold -= bet;
                    currentPlayer.Statistics?.RecordGoldSpent(bet);
                    terminal.WriteLine(Loc.Get("inn.sb_bust_lose", bet.ToString("N0"), currentPlayer.Gold.ToString("N0")));
                    await terminal.PressAnyKey();
                    return;
                }
            }
            else
            {
                break;
            }
        }

        // Dealer's turn
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.sb_dealer_turn"));
        await Task.Delay(800);

        int dealerTotal = 0;
        int dealerCards = 0;
        while (dealerTotal < 17)
        {
            int tile = rng.Next(1, 11);
            dealerTotal += tile;
            dealerCards++;
        }

        bool dealerBlackjack = dealerTotal == 21 && dealerCards == 2;
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("inn.sb_dealer_total", dealerTotal));
        await Task.Delay(500);

        // Determine winner
        terminal.WriteLine("");
        if (dealerTotal > 21)
        {
            terminal.SetColor("bright_green");
            long winnings = playerBlackjack ? (long)(bet * GameConfig.BlackjackBonusPayout) - bet : bet;
            terminal.WriteLine(Loc.Get("inn.sb_dealer_busts", winnings.ToString("N0")));
            currentPlayer.Gold += winnings;
        }
        else if (playerBlackjack && !dealerBlackjack)
        {
            long winnings = (long)(bet * GameConfig.BlackjackBonusPayout) - bet;
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.sb_beats_dealer", winnings.ToString("N0")));
            currentPlayer.Gold += winnings;
        }
        else if (playerTotal > dealerTotal)
        {
            long winnings = (long)(bet * GameConfig.BlackjackPayoutMultiplier) - bet;
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.sb_you_beat", winnings.ToString("N0")));
            currentPlayer.Gold += winnings;
        }
        else if (playerTotal == dealerTotal)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.sb_push"));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.sb_dealer_wins", bet.ToString("N0")));
            currentPlayer.Gold -= bet;
            currentPlayer.Statistics?.RecordGoldSpent(bet);
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("inn.gold_remaining", currentPlayer.Gold.ToString("N0")));
        await terminal.PressAnyKey();
    }

    private async Task PlayArmWrestling()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("inn.arm_wrestling"), "bright_red");
        terminal.WriteLine("");

        var armWrestles = DoorMode.IsOnlineMode ? currentPlayer.ArmWrestlesToday : _armWrestlesToday;
        if (armWrestles >= GameConfig.MaxArmWrestlesPerDay)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.aw_enough_today"));
            await terminal.PressAnyKey();
            return;
        }

        // Find an NPC to wrestle
        var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (allNPCs == null || allNPCs.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.aw_no_interested"));
            await terminal.PressAnyKey();
            return;
        }

        var rng = Random.Shared;
        var candidates = allNPCs.Where(n => n.IsAlive && !n.IsDead).ToList();
        if (candidates.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.aw_no_interested"));
            await terminal.PressAnyKey();
            return;
        }

        var opponent = candidates[rng.Next(candidates.Count)];
        long wagerAmount = opponent.Level * GameConfig.ArmWrestleBetPerLevel;

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.aw_challenger_slams", opponent.DisplayName, opponent.Level, opponent.Strength));
        terminal.WriteLine(Loc.Get("inn.aw_challenger_grins"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("inn.aw_wager_challenge", wagerAmount.ToString("N0")));
        terminal.WriteLine("");

        if (currentPlayer.Gold < wagerAmount)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.aw_need_gold", wagerAmount.ToString("N0")));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("inn.aw_accept_challenge", wagerAmount.ToString("N0")));
        terminal.SetColor("bright_yellow");
        terminal.Write("[Y]");
        terminal.SetColor("cyan");
        terminal.Write("/");
        terminal.SetColor("bright_yellow");
        terminal.Write("[N]");
        terminal.SetColor("cyan");
        terminal.Write(" ");
        terminal.SetColor("white");
        string accept = (await terminal.ReadLineAsync()).ToUpper().Trim();

        if (accept != "Y")
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.aw_back_away"));
            await terminal.PressAnyKey();
            return;
        }

        if (DoorMode.IsOnlineMode)
            currentPlayer.ArmWrestlesToday++;
        else
            _armWrestlesToday++;

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.aw_clasp_hands"));
        await Task.Delay(1000);
        terminal.WriteLine(Loc.Get("inn.aw_three_two_one"));
        await Task.Delay(800);

        // STR contest with randomness
        double playerScore = currentPlayer.Strength * (0.7 + rng.NextDouble() * 0.6);
        double npcScore = opponent.Strength * (0.7 + rng.NextDouble() * 0.6);

        terminal.WriteLine("");
        if (playerScore > npcScore)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.aw_you_slam", opponent.DisplayName));
            terminal.WriteLine(Loc.Get("inn.aw_you_win", wagerAmount.ToString("N0")));
            currentPlayer.Gold += wagerAmount;

            // Positive impression
            if (opponent.Brain?.Memory != null && currentPlayer.Name2 != null)
            {
                var impr = opponent.Brain.Memory.CharacterImpressions;
                impr[currentPlayer.Name2] = (impr.TryGetValue(currentPlayer.Name2, out float c1) ? c1 : 0f) + 0.1f;
            }
        }
        else if (playerScore < npcScore)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("inn.aw_they_slam", opponent.DisplayName));
            terminal.WriteLine(Loc.Get("inn.aw_you_lose", wagerAmount.ToString("N0")));
            currentPlayer.Gold -= wagerAmount;
            currentPlayer.Statistics?.RecordGoldSpent(wagerAmount);

            // Slightly negative impression
            if (opponent.Brain?.Memory != null && currentPlayer.Name2 != null)
            {
                var impr = opponent.Brain.Memory.CharacterImpressions;
                impr[currentPlayer.Name2] = (impr.TryGetValue(currentPlayer.Name2, out float c2) ? c2 : 0f) - 0.1f;
            }
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.aw_draw"));
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("inn.gold_remaining", currentPlayer.Gold.ToString("N0")));
        terminal.SetColor("darkgray");
        var armWrestlesDone = DoorMode.IsOnlineMode ? currentPlayer.ArmWrestlesToday : _armWrestlesToday;
        terminal.WriteLine(Loc.Get("inn.aw_matches_today", armWrestlesDone, GameConfig.MaxArmWrestlesPerDay));
        await terminal.PressAnyKey();
    }

    #endregion

    #region Rent a Room (Online Mode)

    private static readonly (string type, string name, int baseCost, int baseHp)[] GuardOptions = new[]
    {
        ("rookie_npc",  "Rookie Guard",  GameConfig.GuardRookieBaseCost,  80),
        ("veteran_npc", "Veteran Guard", GameConfig.GuardVeteranBaseCost, 150),
        ("elite_npc",   "Elite Guard",   GameConfig.GuardEliteBaseCost,   250),
        ("hound",       "Guard Hound",   GameConfig.GuardHoundBaseCost,   60),
        ("troll",       "Guard Troll",   GameConfig.GuardTrollBaseCost,   200),
        ("drake",       "Guard Drake",   GameConfig.GuardDrakeBaseCost,   300),
    };

    private async Task RentRoom()
    {
        long roomCost = (long)(currentPlayer.Level * GameConfig.InnRoomCostPerLevel);
        long totalAvailable = currentPlayer.Gold + currentPlayer.BankGold;
        if (totalAvailable < roomCost)
        {
            terminal.WriteLine(Loc.Get("inn.rent_need_gold", roomCost.ToString("N0"), currentPlayer.Gold.ToString("N0"), currentPlayer.BankGold.ToString("N0")), "red");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("inn.rent_room"), "bright_cyan");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("inn.rent_room_cost", roomCost.ToString("N0")));
        terminal.WriteLine(Loc.Get("inn.rent_healed_logout"));
        terminal.SetColor("green");
        terminal.WriteLine(Loc.Get("inn.rent_sleep_bonus"));

        // Guard hiring loop
        var hiredGuards = new List<(string type, string name, int hp)>();
        float levelMultiplier = 1.0f + currentPlayer.Level / 10.0f;
        long totalGuardCost = 0;

        while (hiredGuards.Count < GameConfig.MaxSleepGuards)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.rent_guards_hired", hiredGuards.Count, GameConfig.MaxSleepGuards));
            if (hiredGuards.Count > 0)
            {
                terminal.SetColor("cyan");
                foreach (var g in hiredGuards)
                    terminal.WriteLine($"    - {g.name} (HP: {g.hp})");
            }
            terminal.WriteLine("");

            terminal.SetColor("white");
            for (int i = 0; i < GuardOptions.Length; i++)
            {
                var opt = GuardOptions[i];
                int cost = GetGuardCost(opt.baseCost, levelMultiplier, hiredGuards.Count);
                int hp = (int)(opt.baseHp * levelMultiplier);
                bool canAfford = currentPlayer.Gold + currentPlayer.BankGold - roomCost - totalGuardCost >= cost;
                terminal.SetColor(canAfford ? "white" : "dark_red");
                terminal.WriteLine($"  [{i + 1}] {opt.name,-16} {cost,6:N0}g  (HP: {hp})");
            }
            terminal.SetColor("bright_yellow");
            terminal.Write("  [D]");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("inn.rent_done_hiring"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("inn.rent_gold_summary", currentPlayer.Gold.ToString("N0"), currentPlayer.BankGold.ToString("N0"), roomCost.ToString("N0"), totalGuardCost.ToString("N0"), (roomCost + totalGuardCost).ToString("N0")));

            var input = await terminal.GetInput("\n  Choice: ");
            if (string.IsNullOrWhiteSpace(input) || input.Trim().ToUpper() == "D")
                break;

            if (int.TryParse(input.Trim(), out int guardIdx) && guardIdx >= 1 && guardIdx <= GuardOptions.Length)
            {
                var chosen = GuardOptions[guardIdx - 1];
                int cost = GetGuardCost(chosen.baseCost, levelMultiplier, hiredGuards.Count);
                int hp = (int)(chosen.baseHp * levelMultiplier);

                if (currentPlayer.Gold + currentPlayer.BankGold - roomCost - totalGuardCost < cost)
                {
                    terminal.WriteLine(Loc.Get("inn.rent_cant_afford_guard"), "red");
                    await Task.Delay(1000);
                    continue;
                }

                totalGuardCost += cost;
                hiredGuards.Add((chosen.type, chosen.name, hp));
                terminal.WriteLine(Loc.Get("inn.rent_hired_guard", chosen.name, hp), "green");
                await Task.Delay(500);
            }
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("inn.rent_room"), "bright_cyan");
            terminal.SetColor("white");
        }

        // Confirm total cost
        long totalCost = roomCost + totalGuardCost;
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("inn.rent_total_cost", totalCost.ToString("N0"), roomCost.ToString("N0"), totalGuardCost.ToString("N0")));
        var confirm = await terminal.GetInput(Loc.Get("inn.rent_confirm"));
        if (!confirm.Equals("Y", StringComparison.OrdinalIgnoreCase))
        {
            terminal.WriteLine(Loc.Get("inn.rent_cancelled"), "gray");
            await Task.Delay(1000);
            return;
        }

        if (currentPlayer.Gold + currentPlayer.BankGold < totalCost)
        {
            terminal.WriteLine(Loc.Get("inn.rent_cant_afford"), "red");
            await Task.Delay(1500);
            return;
        }

        // Pay from gold on hand first, then bank
        if (currentPlayer.Gold >= totalCost)
        {
            currentPlayer.Gold -= totalCost;
        }
        else
        {
            long shortfall = totalCost - currentPlayer.Gold;
            currentPlayer.Gold = 0;
            currentPlayer.BankGold -= shortfall;
            terminal.WriteLine(Loc.Get("inn.rent_bank_withdraw", shortfall.ToString("N0")), "gray");
        }

        // Remove Groggo's Shadow Blessing on rest (v0.41.0)
        if (currentPlayer.GroggoShadowBlessingDex > 0)
        {
            currentPlayer.Dexterity = Math.Max(1, currentPlayer.Dexterity - currentPlayer.GroggoShadowBlessingDex);
            terminal.WriteLine(Loc.Get("inn.rent_shadow_fades"), "gray");
            currentPlayer.GroggoShadowBlessingDex = 0;
        }

        // Restore HP/Mana/Stamina
        currentPlayer.HP = currentPlayer.MaxHP;
        currentPlayer.Mana = currentPlayer.MaxMana;
        currentPlayer.Stamina = Math.Max(currentPlayer.Stamina, currentPlayer.Constitution * 2);

        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            await DailySystemManager.Instance.ForceDailyReset();
        }

        // Save game
        await GameEngine.Instance.SaveCurrentGame();

        // Build guards JSON
        var guardsJson = "[]";
        if (hiredGuards.Count > 0)
        {
            var guardsList = hiredGuards.Select(g => new { type = g.type, hp = g.hp, maxHp = g.hp }).ToList();
            guardsJson = System.Text.Json.JsonSerializer.Serialize(guardsList);
        }

        // Register as sleeping at the Inn (protected)
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend != null)
        {
            var username = UsurperRemake.BBS.DoorMode.OnlineUsername ?? currentPlayer.Name2;
            await backend.RegisterSleepingPlayer(username, "inn", guardsJson, 1);
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("inn.rent_room_desc1"));
        terminal.WriteLine(Loc.Get("inn.rent_room_desc2"));
        if (hiredGuards.Count > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("inn.rent_guards_position", hiredGuards.Count));
        }
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("inn.rent_deep_sleep"));
        await Task.Delay(2000);

        throw new LocationExitException(GameLocation.NoWhere);
    }

    private static int GetGuardCost(int baseCost, float levelMultiplier, int guardsAlreadyHired)
    {
        return (int)(baseCost * levelMultiplier * (1.0f + GameConfig.GuardCostMultiplierPerExtra * guardsAlreadyHired));
    }

    private async Task AttackInnSleeper()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null)
        {
            terminal.WriteLine(Loc.Get("inn.atk_not_available"), "gray");
            await Task.Delay(1000);
            return;
        }

        // Gather targets: sleeping NPCs at inn + offline players at inn
        var sleepingNPCNames = WorldSimulator.GetSleepingNPCsAt("inn");
        var offlineSleepers = await backend.GetSleepingPlayers();
        var innPlayerSleepers = offlineSleepers
            .Where(s => s.SleepLocation == "inn" && !s.IsDead)
            .Where(s => !s.Username.Equals(DoorMode.OnlineUsername ?? "", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sleepingNPCNames.Count == 0 && innPlayerSleepers.Count == 0)
        {
            terminal.WriteLine(Loc.Get("inn.atk_no_sleepers"), "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("inn.atk_sleeping_guests"));
        terminal.WriteLine("");

        // Skip NPCs on the player's team or spouse/lover
        // Level filter: can only attack sleepers within ±5 levels
        string playerTeam = currentPlayer?.Team ?? "";
        string playerName = currentPlayer?.Name2 ?? currentPlayer?.Name1 ?? "";
        int attackerLevel = currentPlayer?.Level ?? 1;
        var targets = new List<(string name, bool isNPC)>();
        foreach (var npcName in sleepingNPCNames)
        {
            var npc = NPCSpawnSystem.Instance.GetNPCByName(npcName);
            if (npc != null && !string.IsNullOrEmpty(playerTeam) &&
                playerTeam.Equals(npc.Team, StringComparison.OrdinalIgnoreCase))
                continue;
            if (npc != null && (npc.SpouseName.Equals(playerName, StringComparison.OrdinalIgnoreCase)
                || RelationshipSystem.IsMarriedOrLover(npcName, playerName)))
                continue;
            if (npc != null && Math.Abs(npc.Level - attackerLevel) > 5)
                continue;
            string lvlStr = npc != null ? $" (Lvl {npc.Level})" : "";
            terminal.WriteLine($"  {targets.Count + 1}. {npcName}{lvlStr} [SLEEPING NPC]", "yellow");
            targets.Add((npcName, true));
        }
        foreach (var s in innPlayerSleepers)
        {
            // Level filter: can only attack players within ±5 levels
            var targetSave = await backend.ReadGameData(s.Username);
            int targetLevel = targetSave?.Player?.Level ?? 1;
            if (Math.Abs(targetLevel - attackerLevel) > 5)
                continue;

            int guardCount = 0;
            try { guardCount = JsonSerializer.Deserialize<List<object>>(s.GuardsJson)?.Count ?? 0; } catch { }
            string guardLabel = guardCount > 0 ? $" [{guardCount} guard{(guardCount != 1 ? "s" : "")}]" : "";
            terminal.WriteLine($"  {targets.Count + 1}. {s.Username} (Lvl {targetLevel}){guardLabel} [SLEEPING PLAYER]", "red");
            targets.Add((s.Username, false));
        }

        terminal.SetColor("white");
        var input = await terminal.GetInput(Loc.Get("inn.atk_who_attack"));
        if (string.IsNullOrWhiteSpace(input)) return;

        (string name, bool isNPC) chosen = default;
        if (int.TryParse(input, out int idx) && idx >= 1 && idx <= targets.Count)
            chosen = targets[idx - 1];
        else
        {
            var match = targets.FirstOrDefault(t => t.name.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (match.name != null)
                chosen = match;
        }

        if (chosen.name == null)
        {
            terminal.WriteLine(Loc.Get("inn.atk_no_such_sleeper"), "red");
            await Task.Delay(1000);
            return;
        }

        if (chosen.isNPC)
            await AttackInnSleepingNPC(chosen.name);
        else
            await AttackInnSleepingPlayer(backend, chosen.name);
    }

    private async Task AttackInnSleepingNPC(string npcName)
    {
        var npc = NPCSpawnSystem.Instance.GetNPCByName(npcName);
        if (npc == null || !npc.IsAlive || npc.IsDead)
        {
            terminal.WriteLine(Loc.Get("inn.atk_no_longer_here"), "gray");
            await Task.Delay(1000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("inn.atk_pick_lock", npcName));
        await Task.Delay(1500);

        currentPlayer.Darkness += 30; // Extra darkness for invading inn

        // Inn NPCs fight with defense boost (+50% STR/DEF, better rested)
        long origStr = npc.Strength;
        long origDef = npc.Defence;
        npc.Strength = (long)(npc.Strength * (1.0 + GameConfig.InnDefenseBoost));
        npc.Defence = (long)(npc.Defence * (1.0 + GameConfig.InnDefenseBoost));

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsPlayer(currentPlayer, npc);

        // Restore NPC stats
        npc.Strength = origStr;
        npc.Defence = origDef;

        if (result.Outcome == CombatOutcome.Victory)
        {
            long stolenGold = (long)(npc.Gold * GameConfig.SleeperGoldTheftPercent);
            if (stolenGold > 0)
            {
                currentPlayer.Gold += stolenGold;
                npc.Gold -= stolenGold;
                terminal.WriteLine(Loc.Get("inn.atk_steal_gold", stolenGold.ToString("N0")), "yellow");
            }

            terminal.SetColor("dark_red");
            terminal.WriteLine(Loc.Get("inn.atk_leave_body", npcName));

            // Record murder memory
            npc.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.Murdered,
                Description = $"Murdered in my sleep at the Inn by {currentPlayer.Name2}",
                InvolvedCharacter = currentPlayer.Name2,
                Importance = 1.0f,
                EmotionalImpact = -1.0f,
                Location = "Inn"
            });

            // Faction standing penalty — worse at the inn (civilized place)
            if (npc.NPCFaction.HasValue)
            {
                var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;
                factionSystem?.ModifyReputation(npc.NPCFaction.Value, -250);
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("inn.atk_faction_plummet", UsurperRemake.Systems.FactionSystem.Factions[npc.NPCFaction.Value].Name));
            }

            // Witness memories for NPCs at the Inn
            foreach (var witness in LocationManager.Instance.GetNPCsInLocation(GameLocation.TheInn)
                .Where(n => n.IsAlive && n.Name2 != npcName))
            {
                witness.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.SawDeath,
                    Description = $"Witnessed {currentPlayer.Name2} murder {npcName} at the Inn",
                    InvolvedCharacter = currentPlayer.Name2,
                    Importance = 0.8f,
                    EmotionalImpact = -0.6f,
                    Location = "Inn"
                });
            }

            WorldSimulator.WakeUpNPC(npcName);

            try { OnlineStateManager.Instance?.AddNews($"{currentPlayer.Name2} murdered {npcName} in their sleep at the Inn!", "combat"); } catch { }

            await Task.Delay(2000);
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("inn.atk_fought_off", npcName));
            WorldSimulator.WakeUpNPC(npcName);
            await Task.Delay(2000);
        }
        await terminal.WaitForKeyPress();
    }

    private async Task AttackInnSleepingPlayer(SqlSaveBackend backend, string targetUsername)
    {
        var rng = Random.Shared;
        var target = (await backend.GetSleepingPlayers())
            .FirstOrDefault(s => s.Username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        var victimSave = await backend.ReadGameData(target.Username);
        if (victimSave?.Player == null)
        {
            terminal.WriteLine(Loc.Get("inn.atk_cant_load"), "red");
            await Task.Delay(1000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("inn.atk_sneak_toward", target.Username));
        await Task.Delay(1500);

        // Fight through guards
        bool guardsRepelled = false;
        var guards = new List<(string type, string name, int hp, int maxHp)>();
        try
        {
            var guardArray = JsonNode.Parse(target.GuardsJson) as JsonArray;
            if (guardArray != null)
            {
                foreach (var g in guardArray)
                {
                    if (g == null) continue;
                    string gType = g["type"]?.GetValue<string>() ?? "rookie_npc";
                    string gName = g["name"]?.GetValue<string>() ?? "Guard";
                    int gHp = g["hp"]?.GetValue<int>() ?? 50;
                    int gMaxHp = g["max_hp"]?.GetValue<int>() ?? gHp;
                    guards.Add((gType, gName, gHp, gMaxHp));
                }
            }
        }
        catch { }

        int victimLevel = victimSave.Player.Level;

        for (int gi = 0; gi < guards.Count; gi++)
        {
            var (gType, gName, gHp, gMaxHp) = guards[gi];
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("inn.atk_guard_blocks", gName));
            await Task.Delay(1000);

            var guardChar = HeadlessCombatResolver.CreateGuardCharacter(gType, gHp, victimLevel, rng);
            var guardCombat = new CombatEngine(terminal);
            var guardResult = await guardCombat.PlayerVsPlayer(currentPlayer, guardChar);

            if (guardResult.Outcome == CombatOutcome.Victory)
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("inn.atk_cut_down_guard", gName));
                guards.RemoveAt(gi);
                gi--;
                await Task.Delay(1000);
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("inn.atk_guard_repels", gName));
                int remainingHp = (int)Math.Max(1, guardChar.HP);
                guards[gi] = (gType, gName, remainingHp, gMaxHp);
                guardsRepelled = true;
                await Task.Delay(2000);
                break;
            }
        }

        // Update guards in DB
        var updatedGuards = guards.Select(g => new { type = g.type, name = g.name, hp = g.hp, max_hp = g.maxHp });
        await backend.UpdateSleeperGuards(target.Username, JsonSerializer.Serialize(updatedGuards));

        if (guardsRepelled)
        {
            var failLog = JsonSerializer.Serialize(new
            {
                attacker = currentPlayer.Name2,
                type = "player",
                result = "guards_repelled"
            });
            await backend.AppendSleepAttackLog(target.Username, failLog);
            await terminal.WaitForKeyPress();
            return;
        }

        // Guards defeated — fight the sleeper with inn defense boost
        var victim = PlayerCharacterLoader.CreateFromSaveData(victimSave.Player, target.Username);
        long victimGold = victim.Gold;
        victim.Gold = 0;

        if (target.InnDefenseBoost)
        {
            victim.Strength = (long)(victim.Strength * (1.0 + GameConfig.InnDefenseBoost));
            victim.Defence = (long)(victim.Defence * (1.0 + GameConfig.InnDefenseBoost));
        }

        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("inn.atk_reach_target", target.Username));
        await Task.Delay(1000);

        currentPlayer.Darkness += 30;

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsPlayer(currentPlayer, victim);

        if (result.Outcome == CombatOutcome.Victory)
        {
            long stolenGold = (long)(victimGold * GameConfig.SleeperGoldTheftPercent);
            if (stolenGold > 0)
            {
                currentPlayer.Gold += stolenGold;
                await backend.DeductGoldFromPlayer(target.Username, stolenGold);
                terminal.WriteLine(Loc.Get("inn.atk_steal_gold", stolenGold.ToString("N0")), "yellow");
            }

            string stolenItemName = await StealRandomItem(backend, target.Username, victimSave);
            if (stolenItemName != null)
                terminal.WriteLine(Loc.Get("inn.atk_steal_item", stolenItemName), "yellow");

            long xpLoss = (long)(victimSave.Player.Experience * GameConfig.SleeperXPLossPercent / 100.0);
            if (xpLoss > 0)
                await DeductXPFromPlayer(backend, target.Username, xpLoss);

            await backend.MarkSleepingPlayerDead(target.Username);

            var logEntry = JsonSerializer.Serialize(new
            {
                attacker = currentPlayer.Name2,
                type = "player",
                result = "attacker_won",
                gold_stolen = stolenGold,
                item_stolen = stolenItemName ?? (object)null!,
                xp_lost = xpLoss
            });
            await backend.AppendSleepAttackLog(target.Username, logEntry);

            await backend.SendMessage(currentPlayer.Name2, target.Username, "sleep_attack",
                $"{currentPlayer.Name2} broke into your Inn room and murdered you! They stole {stolenGold:N0} gold{(stolenItemName != null ? $" and your {stolenItemName}" : "")}.");

            terminal.SetColor("dark_red");
            terminal.WriteLine(Loc.Get("inn.atk_leave_body", target.Username));
            await Task.Delay(2000);
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("inn.atk_player_fought_off", target.Username));
            await Task.Delay(2000);
        }
        await terminal.WaitForKeyPress();
    }

    private async Task<string?> StealRandomItem(SqlSaveBackend backend, string username, SaveGameData saveData)
    {
        var rng = Random.Shared;
        try
        {
            var playerData = saveData.Player;
            if (playerData == null) return null;

            var stealable = new List<(int index, string name)>();
            if (playerData.DynamicEquipment != null)
            {
                for (int i = 0; i < playerData.DynamicEquipment.Count; i++)
                {
                    var eq = playerData.DynamicEquipment[i];
                    if (eq != null && !string.IsNullOrEmpty(eq.Name))
                        stealable.Add((i, eq.Name));
                }
            }

            if (stealable.Count == 0) return null;

            var (index, name) = stealable[rng.Next(stealable.Count)];
            var stolenEquip = playerData.DynamicEquipment![index];

            if (playerData.EquippedItems != null)
            {
                var slotToRemove = playerData.EquippedItems
                    .Where(kvp => kvp.Value == stolenEquip.Id)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault(-1);
                if (slotToRemove >= 0)
                    playerData.EquippedItems.Remove(slotToRemove);
            }

            playerData.DynamicEquipment.RemoveAt(index);
            await backend.WriteGameData(username, saveData);
            return name;
        }
        catch { return null; }
    }

    private async Task DeductXPFromPlayer(SqlSaveBackend backend, string username, long xpLoss)
    {
        try
        {
            var saveData = await backend.ReadGameData(username);
            if (saveData?.Player != null)
            {
                saveData.Player.Experience = Math.Max(0, saveData.Player.Experience - xpLoss);
                await backend.WriteGameData(username, saveData);
            }
        }
        catch { }
    }

    #endregion
}
