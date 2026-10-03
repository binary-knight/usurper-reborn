using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using UsurperRemake.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Temple of the Gods - Complete Pascal-compatible temple system
/// Based on TEMPLE.PAS with worship, sacrifice, and divine services
/// Integrated with Phase 13 God System and Old Gods storyline
///
/// The Temple houses altars to mortal-created gods as well as whispers
/// of the Old Gods - the corrupted divine beings who once guided humanity.
/// Aurelion, The Fading Light, can be encountered in the Deep Temple.
/// </summary>
public partial class TempleLocation : BaseLocation
{
    private readonly LocationManager locationManager;
    private readonly GodSystem godSystem;
    private bool refreshMenu = true;
    private Random random = Random.Shared;

    // Old Gods integration. Keys instead of literal strings so the prophecies
    // and whispers render in the viewer's session language; `Loc.Get` resolves
    // at display time. Both arrays are still accessed by index (the per-god
    // prophecy mapping is positional — index 0 = Broken Blade, etc.).
    private static readonly string[] OldGodsProphecyKeys = new[]
    {
        "temple.prophecy.broken_blade",
        "temple.prophecy.love_withers",
        "temple.prophecy.justice_blind",
        "temple.prophecy.shadow_web",
        "temple.prophecy.light_fades",
        "temple.prophecy.mountains_sleep",
        "temple.prophecy.weary_creator"
    };

    private static readonly string[] DivineWhisperKeys = new[]
    {
        "temple.whisper.beyond_veil",
        "temple.whisper.candles_flicker",
        "temple.whisper.cold_wind",
        "temple.whisper.stones_remember",
        "temple.whisper.prayers_echo",
        "temple.whisper.altar_trembles"
    };
    
    public TempleLocation(TerminalEmulator terminal, LocationManager locationManager, GodSystem godSystem)
    {
        this.terminal = terminal;  // sets base class protected field
        this.locationManager = locationManager;
        this.godSystem = godSystem;
        
        LocationName = "Temple of the Gods";
        LocationId = GameLocation.Temple;
        Description = "The Temple area is crowded with monks, preachers and processions of priests on their way to the altars. The doomsday prophets are trying to get your attention.";
    }
    
    // Parameterless constructor for legacy compatibility
    public TempleLocation() : this(TerminalEmulator.Instance ?? new TerminalEmulator(), LocationManager.Instance, UsurperRemake.GodSystemSingleton.Instance)
    {
    }

    /// <summary>
    /// Override EnterLocation to use our custom temple loop
    /// </summary>
    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        // Set base class fields so helper methods (WriteBoxHeader, IsScreenReader, etc.) work
        currentPlayer = player;
        if (term != null)
            terminal = term;

        // Run the temple's custom processing loop
        var destination = await ProcessLocation(player);

        // Always throw to navigate back (MainStreet is the default)
        throw new LocationExitException(GameLocation.MainStreet);
    }

    /// <summary>
    /// Main temple processing loop based on Pascal TEMPLE.PAS
    /// </summary>
    public async Task<string> ProcessLocation(Character player)
    {
        currentPlayer = player;
        terminal.ClearScreen();
        
        await DisplayWelcomeMessage();
        await VerifyPlayerGodExists();
        // 1.2.0 Temple gods piece 2: a player-god follower's boon follows the god's current standing
        await GodBoonSystem.RefreshPlayerGodBoonAsync(currentPlayer);
        
        bool exitLocation = false;
        refreshMenu = true;
        
        while (!exitLocation)
        {
            // 1.2.0: a god boon update another session left pending (a player-god's reconfig,
            // domain or recruit) is applied here, in this player's own session
            GodBoonSystem.ApplyPendingBoonRecalc(currentPlayer);
            try
            {
                await DisplayMenu(refreshMenu);
                refreshMenu = false;
                
                var choice = await terminal.GetInputAsync(Loc.Get("ui.your_choice"));

                // Handle global quick commands (/health, /bug, etc.)
                var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
                if (handled) { refreshMenu = true; continue; }

                if (choice.Trim() == "?")
                {
                    refreshMenu = true;
                    continue;
                }

                // 1.2.0 Temple gods piece 7: the rooms, and a pointer for a key that moved into one
                if (await RouteTopLevel(choice))
                    exitLocation = true;
            }
            catch (LocationChangeException ex)
            {
                return ex.NewLocation;
            }
        }
        
        return GameLocation.MainStreet.ToString();
    }
    
    /// <summary>
    /// Display temple welcome message (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task DisplayWelcomeMessage()
    {
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.enter_area"), "yellow");
        terminal.WriteLine("");
        
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod))
        {
            terminal.WriteLine(Loc.Get("temple.worship_god", playerGod), "cyan");
        }
        else if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
        {
            terminal.WriteLine(Loc.Get("temple.follow_immortal", currentPlayer.WorshippedGod), "bright_yellow");
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.not_believer"), "gray");
        }

        await Pacing.Wait(1500);
    }
    
    /// <summary>
    /// The Temple's hall screen (1.2.0 Temple gods piece 7): the rooms, one key each. The visual,
    /// screen-reader and BBS menus are drawn from the one list TopMenuItems returns.
    /// </summary>
    private async Task DisplayMenu(bool forceDisplay)
    {
        if (!forceDisplay && currentPlayer.Expert) return;

        terminal.ClearScreen();

        // Phase 4: Electron mode emits Temple menu state. Pattern B.
        if (GameConfig.ElectronMode)
        {
            EmitElectronEvents();
            return;
        }

        WriteBoxHeader(Loc.Get("temple.header_visual"), "bright_cyan");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.description_line1"));
        terminal.WriteLine(Loc.Get("temple.description_line2"));
        terminal.WriteLine(Loc.Get("temple.description_line3"));

        // Hint at ancient stones if seal not collected
        if (!StoryProgressionSystem.Instance.CollectedSeals.Contains(UsurperRemake.Systems.SealType.Creation))
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("temple.ancient_stones_hint1"));
            terminal.WriteLine(Loc.Get("temple.ancient_stones_hint2"));
        }

        terminal.WriteLine("");
        WriteWorshipLine();
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.top.header"));
        terminal.WriteLine("");
        WriteTempleMenu(TopMenuItems(), bbsRows: true);
        terminal.WriteLine("");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Process worship selection and god faith (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task ProcessWorship(TempleRoom room = TempleRoom.Nave)
    {
        terminal.WriteLine("");
        terminal.WriteLine("");

        string currentGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        bool goAhead = true;
        GodSwitchCost? leftFaith = null;   // a god left in this run (its wrath names the god chosen next)

        // Also check if following an immortal player-god (1.2.0 piece 7: the old L, leaving one, is this)
        if (string.IsNullOrEmpty(currentGod) && !string.IsNullOrEmpty(currentPlayer.WorshippedGod))
        {
            terminal.WriteLine(Loc.Get("temple.follow_immortal_currently", currentPlayer.WorshippedGod), "bright_yellow");
            ShowSwitchCost(null);   // 1.2.0 Temple gods piece 4: the cost before the choice
            // v1.1.15: yesno-convert-a, strict (Y/N)
            if (await terminal.AskYesNoAsync(Loc.Get("temple.abandon_for_elder", currentPlayer.WorshippedGod)))
            {
                string oldGod = currentPlayer.WorshippedGod;
                leftFaith = await SwitchGodAsync(null);

                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("temple.renounce_immortal", oldGod));
            }
            else
            {
                terminal.WriteLine(Loc.Get("temple.remain_faithful"), "green");
                goAhead = false;
            }
        }
        else if (!string.IsNullOrEmpty(currentGod))
        {
            terminal.WriteLine(Loc.Get("temple.currently_worship", currentGod), "white");
            ShowSwitchCost(null);   // 1.2.0 Temple gods piece 4: the cost before the choice

            // v1.1.15: yesno-convert-a, strict (Y/N)
            if (await terminal.AskYesNoAsync(Loc.Get("temple.lost_faith", currentGod)))
            {
                // Abandon faith
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.dont_believe", currentGod), "white");
                terminal.WriteLine(Loc.Get("temple.powers_diminish", currentGod), "yellow");

                string note = "";
                // v1.1.15: yesno-convert-a, strict (Y/N)
                if (await terminal.AskYesNoAsync(Loc.Get("temple.send_note", currentGod)))
                {
                    note = await terminal.GetInputAsync(Loc.Get("temple.note_prompt"));
                    terminal.WriteLine(Loc.Get("temple.done"), "green");
                }

                if (string.IsNullOrEmpty(note))
                {
                    var randomNotes = new[]
                    {
                        "You are not my God!",
                        "farewell..",
                        "never again will I follow you!"
                    };
                    note = randomNotes[Random.Shared.Next(randomNotes.Length)];
                }

                // Remove from god system (1.2.0: all Favor and the god's wrath go with it)
                leftFaith = await SwitchGodAsync(null);

                // In Pascal, this would send mail to the god and news
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.no_longer_believer"), "yellow");
            }
            else
            {
                terminal.WriteLine(Loc.Get("temple.gods_dont_like_apostates"), "green");
                goAhead = false;
            }
        }

        if (goAhead)
        {
            // 1.2.0 Temple gods piece 7: one list of altars, the canon gods and the ascended
            // player-gods (the old J joins here); in the Undercroft, the dark altars only
            var pick = await SelectAltar(Loc.Get("temple.choose_god_worship"), room, requireConfirmation: true);
            var selectedGod = pick?.Canon;

            if (pick?.PlayerGod is { } chosenPlayerGod)
            {
                await WorshipImmortalGod(chosenPlayerGod);
                if (string.Equals(currentPlayer.WorshippedGod, chosenPlayerGod.DivineName, StringComparison.OrdinalIgnoreCase))
                    GodSwitchSystem.NameBetrayedFor(currentPlayer, leftFaith, chosenPlayerGod.DivineName);
            }
            else if (selectedGod != null)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.raise_hands_pray", selectedGod.Name), "white");
                terminal.Write(Loc.Get("temple.for_forgiveness"), "white");

                // Delay dots animation (Pascal Make_Delay_Dots)
                for (int i = 0; i < 15; i++)
                {
                    terminal.Write(".", "white");
                    await Pacing.Wait(300);
                }
                terminal.WriteLine("");

                terminal.WriteLine(Loc.Get("temple.now_believer", selectedGod.Name), "yellow");

                // Set in god system (1.2.0: one god, and the new god starts at Favor 0; a god
                // left above was already left, so this costs nothing more)
                await SwitchGodAsync(selectedGod.Name);
                GodSwitchSystem.NameBetrayedFor(currentPlayer, leftFaith, selectedGod.Name);

                // In Pascal, this would send mail to god and news
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.gods_smile"), "cyan");
            }
        }

        await Pacing.Wait(2000);
    }
    
    /// <summary>
    /// Process altar desecration (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task ProcessDesecrateAltar()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        
        if (currentPlayer.DesecrationsToday >= 2)
        {
            terminal.WriteLine(Loc.Get("temple.desecration_limit"), "red");
            terminal.WriteLine(Loc.Get("temple.wait_tomorrow"), "gray");
            await Pacing.Wait(2000);
            return;
        }

        if (currentPlayer.DarkNr < 1)
        {
            terminal.WriteLine(Loc.Get("temple.no_evil_deeds"), "red");
            await Pacing.Wait(2000);
            return;
        }

        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (!await terminal.AskYesNoAsync(Loc.Get("temple.upset_gods")))
        {
            terminal.WriteLine(Loc.Get("temple.good_for_you"), "green");
            await Pacing.Wait(1000);
            return;
        }
        
        var selectedGod = await SelectGod(Loc.Get("temple.select_god_desecrate"), requireConfirmation: false);
        if (selectedGod == null) return;

        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod) && playerGod == selectedGod.Name)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.not_allowed_abuse_own"), "red");
            await Pacing.Wait(2000);
            return;
        }

        terminal.SetColor("red");
        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (!await terminal.AskYesNoAsync(Loc.Get("temple.confirm_desecrate", selectedGod.Name)))
        {
            terminal.WriteLine(Loc.Get("temple.wise_choice"), "gray");
            return;
        }

        await PerformEnhancedDesecration(selectedGod);
    }
    
    /// <summary>
    /// v0.65.6: whether the Rite of Return menu entry should render. Online
    /// permadeath mode only, and hidden while the player's lives are full.
    /// (Single-player death does consume Resurrections via the free-revive
    /// prompt; the rite is simply not offered there, since running out costs
    /// the Veil of Death penalties rather than the character.)
    /// </summary>
    private bool CanShowRiteOfReturn()
    {
        return DoorMode.IsOnlineMode
            && GameConfig.OnlinePermadeathEnabled
            && currentPlayer != null
            && currentPlayer.Resurrections < Math.Max(1, currentPlayer.MaxResurrections);
    }

    /// <summary>
    /// v0.65.8 (R5): the Hall of the Fallen memorial renders only where the
    /// shared server DB exists (online mode). Single-player has no permadeath
    /// erasure, so there is nothing to memorialize.
    /// </summary>
    private bool CanShowHallOfTheFallen()
    {
        return DoorMode.IsOnlineMode
            && SaveSystem.Instance?.Backend is UsurperRemake.Systems.SqlSaveBackend;
    }

    /// <summary>
    /// v0.65.8 (R5): memorial wall for characters erased by permadeath. Every
    /// erased character's name endures here -- deletion produces a story
    /// instead of nothing (see DOCS/PLAYER_EXPERIENCE_ANALYSIS.md R5).
    /// </summary>
    private async Task ShowHallOfTheFallen()
    {
        if (!CanShowHallOfTheFallen()) return;

        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("temple.hall_fallen_header"), "bright_cyan");
        terminal.SetColor("gray");
        terminal.WriteLine(" " + Loc.Get("temple.hall_fallen_desc"));
        terminal.WriteLine("");

        var backend = SaveSystem.Instance?.Backend as UsurperRemake.Systems.SqlSaveBackend;
        var memorials = backend?.GetFallenMemorials(15) ?? new List<(string, int, string, string, string)>();

        if (memorials.Count == 0)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(" " + Loc.Get("temple.hall_fallen_empty"));
        }
        else
        {
            foreach (var (name, level, className, killer, diedAt) in memorials)
            {
                terminal.SetColor("bright_white");
                terminal.Write($" {name}");
                terminal.SetColor("gray");
                string classDisplay = GameConfig.GetLocalizedClassNameFromString(className);
                string killerDisplay = string.IsNullOrWhiteSpace(killer)
                    ? Loc.Get("temple.hall_fallen_unknown_end") : killer;
                terminal.WriteLine("  " + Loc.Get("temple.hall_fallen_row", level, classDisplay, killerDisplay, diedAt));
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(" " + Loc.Get("temple.hall_fallen_footer"));
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// v0.65.6 Rite of Return: restore one lost resurrection for steep,
    /// level-scaled gold (GameConfig.GetRiteOfReturnCost). One rite per
    /// GameConfig.RiteOfReturnCooldownHours wall-clock hours per character.
    /// Part of the renewable-resurrections pass -- converts the 3-lifetime-lives
    /// absorbing chain into a renewal process without making death free
    /// (a mid-30s rite costs roughly 45 fights of gold income).
    /// </summary>
    private async Task ProcessRiteOfReturn()
    {
        terminal.WriteLine("");

        if (!DoorMode.IsOnlineMode || !GameConfig.OnlinePermadeathEnabled)
        {
            terminal.WriteLine(Loc.Get("temple.invalid_choice"), "red");
            await Pacing.Wait(1000);
            return;
        }

        int maxLives = Math.Max(1, currentPlayer.MaxResurrections);
        if (currentPlayer.Resurrections >= maxLives)
        {
            terminal.WriteLine(Loc.Get("temple.rite_full", maxLives), "gray");
            await terminal.PressAnyKey();
            return;
        }

        // Wall-clock cooldown (v0.57.6 lesson: day-counters drift in MUD mode).
        var elapsed = DateTime.UtcNow - currentPlayer.LastRiteOfReturnUtc;
        var cooldown = TimeSpan.FromHours(GameConfig.RiteOfReturnCooldownHours);
        if (currentPlayer.LastRiteOfReturnUtc != DateTime.MinValue && elapsed < cooldown)
        {
            int hoursLeft = (int)Math.Ceiling((cooldown - elapsed).TotalHours);
            terminal.WriteLine(Loc.Get("temple.rite_cooldown", hoursLeft), "yellow");
            await terminal.PressAnyKey();
            return;
        }

        long cost = GameConfig.GetRiteOfReturnCost(currentPlayer.Level);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("temple.rite_header"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.rite_intro"));
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("temple.rite_cost", cost, currentPlayer.Gold));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.rite_lives", Math.Max(0, currentPlayer.Resurrections), maxLives));
        terminal.WriteLine("");

        if (currentPlayer.Gold < cost)
        {
            terminal.WriteLine(Loc.Get("temple.rite_no_gold"), "red");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("white");
        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (!await terminal.AskYesNoAsync(Loc.Get("temple.rite_confirm", cost)))
        {
            terminal.WriteLine(Loc.Get("temple.rite_cancel"), "gray");
            await terminal.PressAnyKey();
            return;
        }

        currentPlayer.Gold -= cost;
        currentPlayer.Resurrections++;
        currentPlayer.LastRiteOfReturnUtc = DateTime.UtcNow;
        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LIVES",
            $"{currentPlayer.Name2 ?? currentPlayer.Name1} performed the Rite of Return for {cost} gold ({currentPlayer.Resurrections}/{maxLives} lives).");

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.rite_success", Math.Max(0, currentPlayer.Resurrections), maxLives), "bright_yellow");

        // Persist immediately -- a life purchased for six figures of gold must not
        // be lost to a crash / unclean disconnect before the next autosave.
        if (GameEngine.Instance != null)
            await GameEngine.Instance.SaveCurrentGame();

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Gold at a canon god's altar (Pascal TEMPLE.PAS contribute_to_god). 1.2.0 Temple gods piece 7:
    /// the altar is chosen in the Nave's or the Undercroft's offering (ProcessOffering).
    /// 1.2.2: no good deed is needed; the offering follows only the Favor rules.
    /// </summary>
    private async Task ProcessContribute(God selectedGod)
    {
        terminal.WriteLine("");

        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        bool wrongGod = false;
        bool goAhead = true;
        
        if (!string.IsNullOrEmpty(playerGod) && playerGod != selectedGod.Name)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.not_your_god", selectedGod.Name), "red");
            terminal.WriteLine(Loc.Get("temple.mighty_not_happy", playerGod), "red");

            // v1.1.15: yesno-convert-a, strict (Y/N)
            if (await terminal.AskYesNoAsync(Loc.Get("temple.continue_prompt")))
            {
                wrongGod = true;
            }
            else
            {
                terminal.WriteLine(Loc.Get("temple.good_for_you"), "green");
                goAhead = false;
            }
        }
        
        if (goAhead)
        {
            await ProcessGoldSacrifice(selectedGod, wrongGod);
        }

        await Pacing.Wait(1000);
    }
    
    /// <summary>
    /// Display god ranking (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task DisplayGodRanking()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");

        // 1.2.0 Temple gods: one ranking of the unified god list (canon ten plus player-gods, never
        // Manwe) by standing, the sum of the followers' Favor; followers are the saved characters
        // worshipping the god plus living NPC followers
        var playerImmortals = await GetImmortalGodsAsync();
        var godNames = playerImmortals.Select(ig => ig.DivineName).ToList();
        if (currentPlayer.IsImmortal && !string.IsNullOrEmpty(currentPlayer.DivineName))
            godNames.Add(currentPlayer.DivineName);
        var standings = await GodRegistry.GetStandingsAsync(currentPlayer);

        var ranking = new List<(string Name, string Title, int Followers, long Standing, bool IsPlayer)>();
        foreach (var entry in GodRegistry.AllGods(godNames))
        {
            string title;
            if (entry.IsCanon)
                title = godSystem.GetGod(entry.Name)?.GetTitle() ?? "";
            else
            {
                var ig = playerImmortals.FirstOrDefault(i => i.DivineName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
                int level = ig?.GodLevel ?? currentPlayer.GodLevel;
                title = GodText.Title(level);
            }
            standings.TryGetValue(entry.Name, out var standing);
            ranking.Add((entry.Name, title, standing.AllFollowers, standing.Standing, !entry.IsCanon));
        }

        ranking = ranking.OrderByDescending(r => r.Standing).ThenByDescending(r => r.Followers).ToList();

        if (ranking.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_gods_exist"), "gray");
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.god_ranking_header"), "white");
            WriteThickDivider(71, "magenta");

            for (int i = 0; i < ranking.Count; i++)
            {
                var entry = ranking[i];
                string line = $"{(i + 1).ToString().PadLeft(3)}. {entry.Name.PadRight(25)} {entry.Title.PadRight(20)} {entry.Followers.ToString().PadLeft(10)} {entry.Standing.ToString().PadLeft(8)}";
                terminal.WriteLine(line, entry.IsPlayer ? "bright_cyan" : "yellow");
            }
        }

        // 1.2.0 Temple gods piece 6: this week's strongest god, picked at the weekly reset, and its bonus
        var weekPick = await Task.Run(() => WeeklyGodSystem.Current(currentPlayer));
        terminal.WriteLine("");
        if (weekPick is { } week && !string.IsNullOrEmpty(week.God))
        {
            terminal.WriteLine(Loc.Get("temple.week_god", week.God, GameConfig.GodWeeklyXpBonusPct), "bright_yellow");
            if (WeeklyGodSystem.IsFollowerOfWeek(currentPlayer, week))
                terminal.WriteLine(Loc.Get("temple.week_god_yours", week.God, GameConfig.GodWeeklyXpBonusPct), "bright_green");
        }
        else
            terminal.WriteLine(Loc.Get("temple.week_god_none"), "gray");

        await terminal.PressAnyKey();
    }
    
    /// <summary>
    /// Select a god from available options (Pascal Select_A_God function)
    /// Shows the list automatically and supports partial name matching
    /// </summary>
    private async Task<God?> SelectGod(string prompt = "Select a god", bool requireConfirmation = true)
    {
        var activeGods = godSystem.GetActiveGods()
            .Where(g => g.Id != "SUPREME") // Exclude Manwe (system god, not worshippable)
            .OrderBy(g => g.Name).ToList();
        if (activeGods.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_gods_available"), "red");
            await Pacing.Wait(1000);
            return null;
        }

        // Always show the list first
        DisplayGodListCompact(activeGods);

        while (true)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.select_prompt", prompt), "white");
            var input = await terminal.GetInputAsync("> ");

            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            // Find matching gods (partial match, case-insensitive)
            var matches = activeGods.Where(g =>
                g.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains(input, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (matches.Count == 0)
            {
                terminal.WriteLine(Loc.Get("temple.no_god_match", input), "red");
                continue;
            }

            God selectedGod;
            if (matches.Count == 1)
            {
                selectedGod = matches[0];
            }
            else
            {
                // Multiple matches - prefer exact start match, then show options
                var startsWithMatch = matches.FirstOrDefault(g =>
                    g.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase));

                if (startsWithMatch != null && matches.Count(g =>
                    g.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase)) == 1)
                {
                    selectedGod = startsWithMatch;
                }
                else
                {
                    // Show ambiguous matches
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("temple.multiple_matches"), "yellow");
                    foreach (var match in matches)
                    {
                        terminal.WriteLine($"  - {match.Name} ({GodAlignmentLabel(match)})", "white");
                    }
                    terminal.WriteLine(Loc.Get("temple.be_more_specific"), "gray");
                    continue;
                }
            }

            // Show selected god and ask for confirmation
            int godSide = Math.Sign(selectedGod.Goodness - selectedGod.Darkness);
            string godAlignment = GodAlignmentLabel(selectedGod);
            string alignColor = godSide > 0 ? "bright_cyan" : godSide < 0 ? "dark_red" : "yellow";

            terminal.WriteLine("");
            terminal.SetColor(alignColor);
            terminal.WriteLine(Loc.Get("temple.selected_god", selectedGod.Name, selectedGod.GetTitle()));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("temple.alignment_info", godAlignment, selectedGod.Believers));

            if (requireConfirmation)
            {
                terminal.WriteLine("");
                // v1.1.15: yesno-convert-a, strict (Y/N)
                if (!await terminal.AskYesNoAsync(Loc.Get("ui.confirm_choose", selectedGod.Name)))
                {
                    terminal.WriteLine(Loc.Get("temple.selection_cancelled"), "gray");
                    continue;
                }
            }

            return selectedGod;
        }
    }

    /// <summary>
    /// Display a compact list of available gods for selection
    /// </summary>
    private void DisplayGodListCompact(List<God> gods, bool nameHint = true)
    {
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.available_gods"), "cyan");
        terminal.WriteLine("");

        if (gods.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_gods_accept"), "gray");
            return;
        }

        foreach (var god in gods)
        {
            // 1.2.0 Temple gods: the epithet in the player's language (GodText)
            string domain = GodText.Epithet(god);

            // Color based on alignment
            string color = "yellow";
            string alignmentMarker = " ";
            if (god.Goodness > god.Darkness * 2)
            {
                color = "bright_cyan";
                alignmentMarker = "+";  // Light
            }
            else if (god.Darkness > god.Goodness * 2)
            {
                color = "dark_red";
                alignmentMarker = "*";  // Dark
            }

            terminal.WriteLine($"  {alignmentMarker} {god.Name} - {domain}", color);

            // 1.2.0 Temple gods piece 2: the god's boon (canon boons are fixed, full strength) and ward
            var boonDomain = GodBoonSystem.DomainOfCanon(god.Name);
            if (boonDomain != GodDomain.None)
            {
                Say(Loc.Get("god.boon_line", GodBoonSystem.DescribeBoon(boonDomain, 100), 100), "gray", "      ");   // 1.2.0 piece 7: wrapped to 80 columns
                Say(Loc.Get("god.ward_line", GodBoonSystem.DescribeWard(boonDomain)), "darkgray", "      ");
                // 1.2.0 Temple gods piece 5: the god's Miracle, for its Chosen
                Say(Loc.Get("miracle.altar_line", MiracleSystem.Name(boonDomain), MiracleSystem.Describe(boonDomain)), "darkgray", "      ");
            }
        }

        if (!nameHint) return;   // 1.2.0 Temple gods piece 7: the altar list prints the hint after the player-gods
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.type_name_hint"));
    }

    /// <summary>
    /// Display list of available gods (full details version)
    /// </summary>
    private void DisplayGodList()
    {
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.available_gods"), "cyan");
        terminal.WriteLine("");

        var activeGods = godSystem.GetActiveGods()
            .Where(g => g.Id != "SUPREME") // Exclude Manwe (system god, not worshippable)
            .OrderBy(g => g.Name).ToList();

        if (activeGods.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_gods_accept"), "gray");
            terminal.WriteLine("");
            return;
        }

        foreach (var god in activeGods)
        {
            // 1.2.0 Temple gods: the epithet in the player's language (GodText)
            string domain = GodText.Epithet(god);

            // Color based on alignment
            string color = "yellow";
            if (god.Goodness > god.Darkness * 2)
                color = "bright_cyan";
            else if (god.Darkness > god.Goodness * 2)
                color = "dark_red";

            terminal.WriteLine($"  {god.Name}, {domain}", color);

            // Show description if available (1.2.0: in the player's language, GodText)
            string description = GodText.Description(god);
            if (description.Length > 0)
            {
                terminal.WriteLine($"    {description}", "gray");
            }

            terminal.WriteLine(Loc.Get("temple.god_list_stats", god.Believers, god.Experience.ToString("N0")), "white");
            terminal.WriteLine("");
        }
    }
    
    /// <summary>
    /// Perform altar desecration (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task PerformDesecration(God god)
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        
        var random = Random.Shared;
        switch (random.Next(2))
        {
            case 0:
                terminal.WriteLine(Loc.Get("temple.desecrate_hack_line1"), "white");
                terminal.WriteLine(Loc.Get("temple.desecrate_hack_line2"), "white");
                terminal.Write(Loc.Get("temple.desecrate_hack_word"), "red");
                for (int i = 0; i < 4; i++)
                {
                    await Pacing.Wait(500);
                    terminal.Write(".", "red");
                }
                terminal.Write(Loc.Get("temple.desecrate_hack_word_lower"), "red");
                for (int i = 0; i < 4; i++)
                {
                    await Pacing.Wait(500);
                    terminal.Write(".", "red");
                }
                terminal.WriteLine(Loc.Get("temple.desecrate_hack_final"), "red");
                break;

            case 1:
                terminal.WriteLine(Loc.Get("temple.desecrate_unholy_line1"), "white");
                terminal.WriteLine(Loc.Get("temple.desecrate_unholy_line2"), "white");
                terminal.WriteLine(Loc.Get("temple.desecrate_altar_damaged"), "red");
                break;
        }

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.desecrated_altar", god.Name), "red");
        terminal.WriteLine(Loc.Get("temple.gods_remember_blasphemy"), "red");
        
        // Process desecration in god system
        godSystem.ProcessAltarDesecration(god.Name, currentPlayer.Name2);
        
        // Use evil deed
        currentPlayer.DarkNr--;
        
        await Pacing.Wait(3000);
    }
    
    /// <summary>
    /// Process gold sacrifice (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task ProcessGoldSacrifice(God god, bool wrongGod)
    {
        terminal.WriteLine("");

        if (GameConfig.ElectronMode)
        {
            ElectronBridge.EmitAmountEntry(
                title: Loc.Get("temple.sacrifice_to", god.Name),
                prompt: Loc.Get("temple.gold_sacrifice_prompt"),
                maxAmount: currentPlayer.Gold,
                currency: "gold");
        }

        var goldStr = await terminal.GetInputAsync(Loc.Get("temple.gold_sacrifice_prompt"));

        if (!long.TryParse(goldStr, out long goldAmount) || goldAmount <= 0)
        {
            terminal.WriteLine(Loc.Get("temple.invalid_amount"), "red");
            await Pacing.Wait(1000);
            return;
        }

        if (goldAmount > currentPlayer.Gold)
        {
            terminal.WriteLine(Loc.Get("temple.not_enough_gold"), "red");
            await Pacing.Wait(1000);
            return;
        }

        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (!await terminal.AskYesNoAsync(Loc.Get("temple.confirm_sacrifice_gold", goldAmount, god.Name))) return;

        // Process sacrifice
        currentPlayer.Gold -= goldAmount;
        var powerGained = godSystem.ProcessGoldSacrifice(god.Name, goldAmount, currentPlayer.Name2);

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.god_power_growing", god.Name), "yellow");
        terminal.WriteLine(Loc.Get("temple.reward_will_come"), "white");
        terminal.WriteLine(Loc.Get("temple.power_increased", powerGained), "cyan");

        // Grant temporary blessing from sacrifice (if worshipping this god)
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        // 1.2.0 Temple gods piece 3: gold given to your own god is Favor (capped per day)
        if (!wrongGod && playerGod == god.Name)
            FavorUi.ReportGain(terminal, currentPlayer, FavorSystem.GoldSacrifice(currentPlayer, goldAmount, godSystem), godSystem);
        if (!wrongGod && playerGod == god.Name && goldAmount >= 100)
        {
            var tempBlessing = UsurperRemake.Systems.DivineBlessingSystem.Instance.GrantSacrificeBlessing(
                currentPlayer, goldAmount, god.Name);

            if (tempBlessing != null)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"*** {tempBlessing.Name} ***");
                terminal.SetColor("white");
                terminal.WriteLine(tempBlessing.Description);
                var duration = tempBlessing.ExpiresAt - DateTime.Now;
                terminal.WriteLine(Loc.Get("temple.duration_minutes", duration.TotalMinutes.ToString("F0")), "gray");
            }
        }

        // Check if god is evil (Darkness > Goodness) to determine faction effect
        bool isEvilGod = god.Darkness > god.Goodness;
        // v0.60.0 alignment-cheese pass: cap per-sacrifice alignment gain. Pre-fix:
        // a 100,000g donation produced standingGain = 1000, exactly the AlignmentCap,
        // so one transaction maxed out darkness or chivalry. Player report from
        // in-game gossip: "Donate 100k to an evil god, probably less, and I have
        // max darkness." With the cap, the same donation grants 50, so reaching
        // the cap requires sustained behavior across many sessions. The faction
        // reputation gain below shares the same capped value, which is also
        // intentional: big donors should not buy a top faction rank in one shot.
        int standingGain = Math.Min(GameConfig.MaxAlignmentGainPerTempleSacrifice,
            Math.Max(1, (int)(goldAmount / 100)));

        if (isEvilGod)
        {
            // Evil god sacrifice - dark act — v0.57.12: paired movement
            // 1.2.2: an offering is not a deed, so no good or dark deed count is refunded
            AlignmentSystem.Instance.ChangeAlignment(currentPlayer, standingGain, isGood: false, "temple.evil_sacrifice");
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheShadows, standingGain);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("temple.darkness_accepts", standingGain));
        }
        else
        {
            // Good god sacrifice - light act — v0.57.12: paired movement
            // 1.2.2: an offering is not a deed, so no good or dark deed count is refunded
            AlignmentSystem.Instance.ChangeAlignment(currentPlayer, standingGain, isGood: true, "temple.good_sacrifice");
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, standingGain);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.devotion_noted", standingGain));
        }

        // Divine Wrath System - record betrayal when sacrificing to wrong god
        if (wrongGod && !string.IsNullOrEmpty(playerGod))
        {
            // Check if player's god is opposite alignment from the one they're sacrificing to
            var playerGodData = godSystem.GetGod(playerGod);
            bool playerGodIsEvil = playerGodData != null && playerGodData.Darkness > playerGodData.Goodness;
            bool isOppositeAlignment = playerGodIsEvil != isEvilGod;

            // Severity: 1 = same alignment different god, 2 = opposite alignment, 3 = opposite + large sacrifice
            int severity = 1;
            if (isOppositeAlignment) severity = 2;
            if (isOppositeAlignment && goldAmount >= 500) severity = 3;

            currentPlayer.RecordDivineWrath(playerGod, god.Name, severity);

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            if (severity >= 3)
            {
                terminal.WriteLine(Loc.Get("temple.god_seethes", playerGod.ToUpper()));
                terminal.WriteLine(Loc.Get("temple.betrayal_not_forgotten"));
            }
            else if (severity == 2)
            {
                terminal.WriteLine(Loc.Get("temple.god_furious", playerGod));
                terminal.WriteLine(Loc.Get("temple.darkness_awaits"));
            }
            else
            {
                terminal.WriteLine(Loc.Get("temple.god_displeased", playerGod));
                terminal.WriteLine(Loc.Get("temple.beware_shadows"));
            }
        }

        await Pacing.Wait(3000);
    }

    /// <summary>
    /// 1.2.0 Temple gods piece 4: shows what leaving the current god for newGod (null for none)
    /// costs, before the player is asked: all Favor with it, and its wrath by the Favor lost.
    /// Read-only (GodSwitchSystem.Preview).
    /// </summary>
    private void ShowSwitchCost(string? newGod)
    {
        var cost = GodSwitchSystem.Preview(currentPlayer, newGod);
        if (!cost.LeavesAGod || cost.OldGod.Equals(newGod ?? "", StringComparison.OrdinalIgnoreCase)) return;
        terminal.WriteLine("");
        if (cost.FavorLost > 0)
            terminal.WriteLine(Loc.Get("god.switch_cost_favor", cost.OldGod, cost.FavorLost), "yellow");
        else
            terminal.WriteLine(Loc.Get("god.switch_cost_no_favor", cost.OldGod), "gray");
        if (cost.WrathLevel > 0)
            terminal.WriteLine(Loc.Get("god.switch_cost_wrath", cost.OldGod, WrathSeverityName(cost.WrathLevel)), "red");
        if (cost.SmiteDamage > 0)
            terminal.WriteLine(Loc.Get("god.switch_cost_smite", cost.OldGod, cost.SmiteDamage), "red");
        terminal.WriteLine(Loc.Get("god.switch_cost_new_zero"), "gray");
    }

    /// <summary>
    /// 1.2.0 Temple gods piece 4: the player's own god change at the Temple (W, J, L), through the
    /// one switching rule (GodSwitchSystem.Switch, by the player): all Favor with the left god is
    /// lost and its wrath follows. Online, the character is then saved at once (forced past the
    /// autosave throttle), so the god, the Favor, the wrath and LastGodSwitchDay land in one write
    /// and a dropped session cannot keep the new god without its cost.
    /// </summary>
    private async Task<GodSwitchCost?> SwitchGodAsync(string? newGod)
    {
        var cost = GodSwitchSystem.Switch(currentPlayer, newGod, GodChangeBy.Player);
        if (cost is { } c)
        {
            if (c.WrathLevel > 0)
                terminal.WriteLine(Loc.Get("god.switch_wrath_now", c.OldGod, WrathSeverityName(c.WrathLevel)), "bright_red");
            if (c.SmiteDamage > 0)
                terminal.WriteLine(Loc.Get("god.switch_smite_now", c.OldGod, c.SmiteDamage, currentPlayer.HP, currentPlayer.MaxHP), "bright_red");
        }
        if (DoorMode.IsOnlineMode)
        {
            try { await SaveSystem.Instance.AutoSave(currentPlayer, force: true); }
            catch (Exception ex) { DebugLogger.Instance.LogError("FAITH", $"Save after a god switch failed: {ex.Message}"); }
        }
        return cost;
    }

    /// <summary>The name of a Divine Wrath level (base.wrath_minor, moderate, severe).</summary>
    private static string WrathSeverityName(int level) => level switch
    {
        1 => Loc.Get("base.wrath_minor"),
        2 => Loc.Get("base.wrath_moderate"),
        _ => Loc.Get("base.wrath_severe"),
    };

    /// <summary>
    /// Verify player's god still exists (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task VerifyPlayerGodExists()
    {
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod))
        {
            if (!godSystem.VerifyGodExists(playerGod))
            {
                terminal.WriteLine(Loc.Get("temple.god_no_longer_exists", playerGod), "red");
                terminal.WriteLine(Loc.Get("temple.faith_shaken"), "gray");
                GodSwitchSystem.Switch(currentPlayer, null, GodChangeBy.Other);   // 1.2.0: the game's change, no wrath
                await Pacing.Wait(2000);
            }
        }
    }
    
    /// <summary>
    /// The devotion part of the Nave's Altars screen (1.2.0 Temple gods piece 7; the old S status
    /// screen, which repeated the character sheet, is gone): the god (canon or player-god), the
    /// Favor and its tier, the boon at its current strength and the ward, the good and evil deeds
    /// left, and the Miracle. The Altars screen waits for a key after the ranking that follows.
    /// </summary>
    private async Task DisplayPlayerStatus()
    {
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.devotion.header"), "cyan");

        var worshipped = GodRegistry.GetWorshippedGod(currentPlayer, godSystem);
        if (worshipped is { } w)
        {
            int favor = FavorSystem.GetFavor(currentPlayer, godSystem);
            terminal.WriteLine(Loc.Get("temple.devotion.favor", w.Name, favor, TierName(FavorSystem.GetTier(favor))), w.IsCanon ? "cyan" : "bright_yellow");

            // the boon at the character's current strength, and the ward (as the status sheet shows them)
            var boonDomain = GodBoonSystem.GetDomain(currentPlayer, godSystem);
            if (boonDomain != GodDomain.None)
            {
                int strength = GodBoonSystem.GetStrengthPct(currentPlayer, godSystem);
                terminal.WriteLine($"  {Loc.Get("god.boon_line", GodBoonSystem.DescribeBoon(boonDomain, strength), strength)}", "gray");
                bool warded = FavorSystem.GetTier(favor) >= GodFavorTier.Devout;
                terminal.WriteLine($"  {Loc.Get(warded ? "god.ward_line_active" : "god.ward_line", GodBoonSystem.DescribeWard(boonDomain))}", warded ? "gray" : "darkgray");
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.devotion.none"), "gray");
        }

        terminal.WriteLine(Loc.Get("temple.good_deeds", currentPlayer.ChivNr), "green");
        terminal.WriteLine(Loc.Get("temple.evil_deeds", currentPlayer.DarkNr), "red");

        // 1.2.0 Temple gods piece 5: the Miracle (at Chosen, ready or used today; below, the tier that unlocks it)
        string miracleLine = MiracleSystem.TempleLine(currentPlayer, godSystem);
        if (miracleLine.Length > 0)
            terminal.WriteLine(miracleLine, MiracleSystem.IsReady(currentPlayer, godSystem) ? "bright_magenta" : "gray");

        await Task.CompletedTask;
    }

    #region Old Gods Integration

    /// <summary>
    /// Display prophecies about the Old Gods - hints about the main storyline
    /// </summary>
    private async Task DisplayOldGodsProphecies()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.prophecies"), "bright_magenta");
        terminal.WriteLine("");

        // Random divine whisper intro
        terminal.WriteLine(Loc.Get(DivineWhisperKeys[random.Next(DivineWhisperKeys.Length)]), "gray");
        await Pacing.Wait(1500);
        terminal.WriteLine("");

        // Show prophecies based on story progression
        var story = StoryProgressionSystem.Instance;
        int propheciesRevealed = 0;

        // Maelketh - The Broken Blade (War God)
        if (story.OldGodStates.TryGetValue(OldGodType.Maelketh, out var maelkethState))
        {
            if (maelkethState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_maelketh_defeated"), "green");
            else if (maelkethState.Status == GodStatus.Saved)
                terminal.WriteLine(Loc.Get("temple.prophecy_maelketh_saved"), "bright_green");
            else
                terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[0]), "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 50)
        {
            terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[0]), "red");
            propheciesRevealed++;
        }

        // Veloura - The Withered Heart (Love Goddess)
        if (story.OldGodStates.TryGetValue(OldGodType.Veloura, out var velouraState))
        {
            if (velouraState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_veloura_defeated"), "gray");
            else if (velouraState.Status == GodStatus.Saved)
                terminal.WriteLine(Loc.Get("temple.prophecy_veloura_saved"), "bright_magenta");
            else
                terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[1]), "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 40)
        {
            terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[1]), "red");
            propheciesRevealed++;
        }

        // Thorgrim - The Hollow Judge (Law God)
        if (story.OldGodStates.TryGetValue(OldGodType.Thorgrim, out var thorgrimState))
        {
            if (thorgrimState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_thorgrim_defeated"), "yellow");
            else
                terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[2]), "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 60)
        {
            terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[2]), "red");
            propheciesRevealed++;
        }

        // Noctura - The Shadow Weaver
        if (story.OldGodStates.TryGetValue(OldGodType.Noctura, out var nocturaState))
        {
            if (nocturaState.Status == GodStatus.Allied)
                terminal.WriteLine(Loc.Get("temple.prophecy_noctura_allied"), "bright_cyan");
            else if (nocturaState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_noctura_defeated"), "gray");
            else
                terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[3]), "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 70)
        {
            terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[3]), "red");
            propheciesRevealed++;
        }

        // Aurelion - The Fading Light (encountered at Temple)
        if (story.OldGodStates.TryGetValue(OldGodType.Aurelion, out var aurelionState))
        {
            if (aurelionState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_aurelion_defeated"), "gray");
            else if (aurelionState.Status == GodStatus.Saved)
                terminal.WriteLine(Loc.Get("temple.prophecy_aurelion_saved"), "bright_yellow");
            else
                terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[4]), "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 55)
        {
            terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[4]), "red");
            propheciesRevealed++;
        }

        // Terravok - The Sleeping Mountain
        if (story.OldGodStates.TryGetValue(OldGodType.Terravok, out var terravokState))
        {
            if (terravokState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_terravok_defeated"), "gray");
            else if (terravokState.Status == GodStatus.Saved)
                terminal.WriteLine(Loc.Get("temple.prophecy_terravok_saved"), "bright_green");
            else
                terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[5]), "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 75)
        {
            terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[5]), "red");
            propheciesRevealed++;
        }

        // Manwe - The Weary Creator (final boss)
        if (story.OldGodStates.TryGetValue(OldGodType.Manwe, out var manweState))
        {
            if (manweState.Status != GodStatus.Imprisoned)
                terminal.WriteLine(Loc.Get("temple.prophecy_manwe_resolved"), "bright_white");
            else
                terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[6]), "bright_magenta");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 90)
        {
            terminal.WriteLine(Loc.Get(OldGodsProphecyKeys[6]), "bright_magenta");
            propheciesRevealed++;
        }

        if (propheciesRevealed == 0)
        {
            terminal.WriteLine(Loc.Get("temple.prophecies_sealed"), "gray");
            terminal.WriteLine(Loc.Get("temple.prophecies_grow_stronger"), "gray");
        }

        terminal.WriteLine("");

        // Chance for divine vision
        if (random.NextDouble() < 0.15 && currentPlayer.Level >= 30)
        {
            await DisplayDivineVision();
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Display a divine vision - rare insight into the story
    /// </summary>
    private async Task DisplayDivineVision()
    {
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("temple.vision"), "bright_cyan", 63);
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        var story = StoryProgressionSystem.Instance;
        int godsFaced = story.OldGodStates.Count(s => s.Value.Status != GodStatus.Imprisoned);

        if (godsFaced == 0)
        {
            terminal.WriteLine(Loc.Get("temple.vision_seven_figures"), "white");
            terminal.WriteLine(Loc.Get("temple.vision_faces_beautiful"), "white");
            await Pacing.Wait(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_darkness_creeps"), "gray");
            terminal.WriteLine(Loc.Get("temple.vision_beauty_twists"), "gray");
            await Pacing.Wait(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_meant_to_guide"), "bright_magenta");
            terminal.WriteLine(Loc.Get("temple.vision_broke_hearts"), "bright_magenta");
        }
        else if (godsFaced < 4)
        {
            terminal.WriteLine(Loc.Get("temple.vision_endless_halls"), "white");
            terminal.WriteLine(Loc.Get("temple.vision_faint_light"), "white");
            await Pacing.Wait(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_light_fades"), "bright_yellow");
            terminal.WriteLine(Loc.Get("temple.vision_find_me"), "bright_yellow");
            await Pacing.Wait(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_from_temple"), "bright_cyan");
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.vision_throne_stars"), "white");
            terminal.WriteLine(Loc.Get("temple.vision_figure_older"), "white");
            await Pacing.Wait(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_come_far"), "bright_white");
            terminal.WriteLine(Loc.Get("temple.vision_final_question"), "bright_white");
            await Pacing.Wait(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_worth_cost"), "bright_magenta");
        }

        terminal.WriteLine("");
        await Pacing.Wait(2000);

        // Record divine vision in story
        story.SetStoryFlag("had_divine_vision", true);

        // Generate news
        NewsSystem.Instance.Newsy(false, Loc.Get("temple.news_vision", currentPlayer.Name2));
    }

    /// <summary>
    /// 1.2.0 Temple gods piece 7: the Deep Temple is Aurelion's one site, and it shows only when his
    /// fight can actually start: the story's own gate (OldGodBossSystem.CanEncounterBoss: his level
    /// and the three Old Gods faced before him, or the Sunforged Blade for an awakened save). A save
    /// that already resolved him finds his memory in the Halls of Memory instead.
    /// </summary>
    private bool CanEnterDeepTemple() =>
        currentPlayer != null && OldGodBossSystem.Instance.CanEncounterBoss(currentPlayer, OldGodType.Aurelion);

    /// <summary>
    /// Enter the Deep Temple, Aurelion's domain, and face him. The result is resolved as the dungeon
    /// resolves an Old God (DungeonLocation.ResolveOldGodAtTemple: the artifact, alignment, God Slayer
    /// surge, forced save and the town's reaction), so the story advances the same from here.
    /// </summary>
    private async Task EnterDeepTemple()
    {
        if (!CanEnterDeepTemple())
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.deep_temple_sealed"), "red");
            terminal.WriteLine(Loc.Get("temple.deep_temple_prove"), "gray");
            await Pacing.Wait(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.deep_temple"), "bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.deep_temple_descend"), "white");
        terminal.WriteLine(Loc.Get("temple.deep_temple_torches"), "white");
        await Pacing.Wait(1500);
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.deep_temple_air_thick"), "gray");
        terminal.WriteLine(Loc.Get("temple.deep_temple_watches"), "gray");
        await Pacing.Wait(1500);

        var story = StoryProgressionSystem.Instance;
        var bossSystem = OldGodBossSystem.Instance;

        // An awakened Aurelion (an older save's quest) and the Sunforged Blade: the save quest ends here
        if (story.OldGodStates.TryGetValue(OldGodType.Aurelion, out var aurelionState) && aurelionState.Status == GodStatus.Awakened)
        {
            var saveResult = await bossSystem.CompleteSaveQuest(currentPlayer, OldGodType.Aurelion, terminal);
            await ResolveAurelionAtTemple(saveResult);
            await terminal.PressAnyKey(Loc.Get("temple.press_enter_return"));
            return;
        }

        // Aurelion encounter available
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.aurelion_glow"), "bright_yellow");
        terminal.WriteLine(Loc.Get("temple.aurelion_weak"), "white");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.aurelion_see_me"), "bright_yellow");
        terminal.WriteLine(Loc.Get("temple.aurelion_few_can"), "bright_yellow");
        terminal.WriteLine("");

        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (await terminal.AskYesNoAsync(Loc.Get("temple.approach_light")))
        {
            story.SetStoryFlag("aurelion_encountered", true);

            // Start Aurelion boss encounter (the gate above is the story's own)
            var result = await bossSystem.StartBossEncounter(currentPlayer, OldGodType.Aurelion, terminal);

            if (result.Success)
            {
                // Generate news
                switch (result.Outcome)
                {
                    case BossOutcome.Defeated:
                        NewsSystem.Instance.Newsy(true, Loc.Get("temple.news_aurelion_destroyed", currentPlayer.Name2));
                        break;
                    case BossOutcome.Saved:
                        NewsSystem.Instance.Newsy(true, Loc.Get("temple.news_aurelion_saved", currentPlayer.Name2));
                        break;
                    case BossOutcome.Allied:
                        NewsSystem.Instance.Newsy(true, Loc.Get("temple.news_aurelion_allied", currentPlayer.Name2));
                        break;
                    case BossOutcome.NotFought:
                        break; // v1.1.15: not entered for a Mental collapse, no news
                }
            }
            await ResolveAurelionAtTemple(result);
        }
        else
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.step_back"), "white");
            terminal.WriteLine(Loc.Get("temple.aurelion_understand"), "bright_yellow");
            terminal.WriteLine(Loc.Get("temple.aurelion_not_ready"), "bright_yellow");
        }

        await terminal.PressAnyKey(Loc.Get("temple.press_enter_return"));
    }

    /// <summary>
    /// 1.2.0 Temple gods piece 7: Aurelion's result at the Deep Temple goes through the dungeon's own
    /// Old God handling, so a Temple fight grants and saves what a floor fight did. A resolved
    /// fight ends with the town's reaction and a return to Main Street (LocationExitException).
    /// </summary>
    private async Task ResolveAurelionAtTemple(BossEncounterResult result)
    {
        var dungeon = locationManager?.GetLocation(GameLocation.Dungeons) as DungeonLocation ?? new DungeonLocation();
        await dungeon.ResolveOldGodAtTemple(result, currentPlayer, terminal);
    }

    /// <summary>True once Aurelion is resolved (defeated, saved, allied or consumed): the story is done with him.</summary>
    internal static bool AurelionResolved()
    {
        var story = StoryProgressionSystem.Instance;
        return story.OldGodStates.TryGetValue(OldGodType.Aurelion, out var s) &&
               (s.Status == GodStatus.Defeated || s.Status == GodStatus.Saved ||
                s.Status == GodStatus.Allied || s.Status == GodStatus.Consumed);
    }

    /// <summary>
    /// Halls of Memory, A: the Deep Temple after Aurelion was resolved (the dark altar and its ash,
    /// or his warm light in its new vessel). Memory only, never a fight.
    /// </summary>
    private async Task ShowAurelionMemory()
    {
        if (!AurelionResolved()) return;
        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.deep_temple"), "bright_yellow");
        var status = StoryProgressionSystem.Instance.OldGodStates[OldGodType.Aurelion].Status;
        terminal.WriteLine("");
        if (status == GodStatus.Saved || status == GodStatus.Allied)
        {
            terminal.WriteLine(Loc.Get("temple.aurelion_warm_light"), "bright_yellow");
            terminal.WriteLine(Loc.Get("temple.aurelion_presence"), "bright_white");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.aurelion_thank_you"), "bright_cyan");
            terminal.WriteLine(Loc.Get("temple.aurelion_new_vessel"), "bright_cyan");
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.aurelion_altar_dark"), "gray");
            terminal.WriteLine(Loc.Get("temple.aurelion_ash_remains"), "gray");
            terminal.WriteLine(Loc.Get("temple.aurelion_sense_loss"), "white");
        }
        await terminal.PressAnyKey(Loc.Get("temple.press_enter_return"));
    }

    /// <summary>
    /// Process item sacrifice - sacrifice equipment for divine favor
    /// </summary>
    private async Task ProcessItemSacrifice()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.item_sacrifice"), "cyan");
        terminal.WriteLine("");

        // 1.2.0 Temple gods piece 3: any god, canon or player-god (one god system)
        var worshipped = GodRegistry.GetWorshippedGod(currentPlayer, godSystem);

        if (worshipped == null)
        {
            terminal.WriteLine(Loc.Get("temple.must_worship_first"), "red");
            terminal.WriteLine(Loc.Get("temple.visit_worship"), "gray");
            await Pacing.Wait(2000);
            return;
        }
        string currentGod = worshipped.Value.Name;
        bool isCanon = worshipped.Value.IsCanon;

        terminal.WriteLine(Loc.Get("temple.kneel_before", currentGod), "white");
        terminal.WriteLine("", "white");
        terminal.WriteLine(Loc.Get("temple.what_sacrifice"), "cyan");
        terminal.WriteLine("", "white");
        terminal.WriteLine(Loc.Get("temple.sacrifice_weapon_option"), "yellow");
        terminal.WriteLine(Loc.Get("temple.sacrifice_armor_option"), "yellow");
        terminal.WriteLine(Loc.Get("temple.sacrifice_potions_option"), "yellow");
        terminal.WriteLine(Loc.Get("temple.sacrifice_return"), "yellow");
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync(Loc.Get("temple.sacrifice_prompt"));

        switch (choice.ToUpper())
        {
            case "W":
                await SacrificeEquippedItem(currentGod, isCanon, EquipmentSlot.MainHand);
                break;
            case "A":
                await SacrificeEquippedItem(currentGod, isCanon, EquipmentSlot.Body);
                break;
            case "H":
                await SacrificePotions(currentGod, isCanon);
                break;
            case "R":
                return;
        }
    }

    /// <summary>
    /// 1.2.0 Temple gods piece 3: sacrifice the weapon in hand (MainHand) or the body armor worn
    /// (Body). The item is really given up (FavorSystem.SacrificeEquipped unequips and drops it and
    /// recalculates the stats) and the god's Favor comes by its value. A cursed or unique item is
    /// refused. A canon god's power grows as before; a player-god has no such power here.
    /// </summary>
    private async Task SacrificeEquippedItem(string godName, bool isCanon, EquipmentSlot slot)
    {
        bool weapon = slot == EquipmentSlot.MainHand;
        var item = currentPlayer.GetEquipment(slot);
        if (item == null)
        {
            terminal.WriteLine(Loc.Get(weapon ? "temple.no_weapon" : "temple.no_armor"), "red");
            await Pacing.Wait(1500);
            return;
        }
        if (item.IsCursed || item.IsUnique)
        {
            terminal.WriteLine(Loc.Get("temple.sacrifice_refused_item", item.Name, godName), "red");
            await Pacing.Wait(1500);
            return;
        }

        int power = weapon ? item.WeaponPower : item.ArmorClass;
        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (!await terminal.AskYesNoAsync(Loc.Get(weapon ? "temple.confirm_sacrifice_weapon" : "temple.confirm_sacrifice_armor", power, godName))) return;

        var (outcome, _, favor) = FavorSystem.SacrificeEquipped(currentPlayer, slot, godSystem);
        if (outcome != ItemSacrificeOutcome.Done)
        {
            terminal.WriteLine(Loc.Get("temple.sacrifice_refused_item", item.Name, godName), "red");
            await Pacing.Wait(1500);
            return;
        }

        long powerGained = Math.Max(1, power) * 2L;
        if (isCanon)
            godSystem.ProcessGoldSacrifice(godName, powerGained * 100, currentPlayer.Name2); // Convert to equivalent gold power

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get(weapon ? "temple.dissolves_divine_light" : "temple.armor_dissolves"), "bright_yellow");
        terminal.WriteLine(Loc.Get("temple.god_accepts", godName), "cyan");
        if (isCanon)
            terminal.WriteLine(Loc.Get("temple.divine_power_increased", powerGained), "bright_cyan");
        FavorUi.ReportGain(terminal, currentPlayer, favor, godSystem);

        // Chance for divine blessing based on the item's power (1.2.0: lasting, written to Base)
        if (random.NextDouble() < 0.3 + (power / 500.0))
        {
            int blessingBonus = random.Next(2, 6);
            currentPlayer.GrantPermanentStat(weapon ? StatKind.Strength : StatKind.Defence, blessingBonus);
            terminal.WriteLine(Loc.Get(weapon ? "temple.blessing_strength" : "temple.blessing_defence", godName, blessingBonus), "bright_green");
        }

        // Apply faction effects based on god alignment
        ApplyFactionEffectForSacrifice(godName, (int)Math.Max(1, powerGained / 10));

        // Generate news
        NewsSystem.Instance.Newsy(false, Loc.Get(weapon ? "temple.news_sacrificed_weapon" : "temple.news_sacrificed_armor",
            currentPlayer.Name2, godName));

        await Pacing.Wait(2500);
    }

    /// <summary>
    /// Sacrifice healing potions to god
    /// </summary>
    private async Task SacrificePotions(string godName, bool isCanon)
    {
        if (currentPlayer.Healing <= 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_potions"), "red");
            await Pacing.Wait(1500);
            return;
        }

        terminal.WriteLine(Loc.Get("temple.have_potions", currentPlayer.Healing), "white");
        var amountStr = await terminal.GetInputAsync(Loc.Get("temple.how_many_sacrifice"));

        if (!int.TryParse(amountStr, out int amount) || amount <= 0)
        {
            terminal.WriteLine(Loc.Get("temple.invalid_amount"), "red");
            await Pacing.Wait(1000);
            return;
        }

        if (amount > currentPlayer.Healing)
        {
            terminal.WriteLine(Loc.Get("temple.not_enough_potions"), "red");
            await Pacing.Wait(1000);
            return;
        }

        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (!await terminal.AskYesNoAsync(Loc.Get("temple.confirm_sacrifice_potions", amount, godName))) return;

        long powerGained = amount * 5; // Each potion gives 5 power
        if (isCanon)
            godSystem.ProcessGoldSacrifice(godName, powerGained * 50, currentPlayer.Name2);

        currentPlayer.Healing -= amount;
        // 1.2.0 Temple gods piece 3: potions are items too, valued at the shop price
        int favor = FavorSystem.ItemSacrifice(currentPlayer, GameConfig.GetHealingPotionCost(currentPlayer.Level) * amount, godSystem);

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.potions_evaporate"), "bright_yellow");
        terminal.WriteLine(Loc.Get("temple.god_accepts", godName), "cyan");
        if (isCanon)
            terminal.WriteLine(Loc.Get("temple.divine_power_increased", powerGained), "bright_cyan");
        FavorUi.ReportGain(terminal, currentPlayer, favor, godSystem);

        // Chance for divine healing
        if (amount >= 3 && random.NextDouble() < 0.5)
        {
            currentPlayer.HP = currentPlayer.MaxHP;
            terminal.WriteLine(Loc.Get("temple.god_restores_health", godName), "bright_green");
        }

        // Apply faction effects based on god alignment
        ApplyFactionEffectForSacrifice(godName, Math.Max(1, amount / 2));

        await Pacing.Wait(2500);
    }

    /// <summary>
    /// Apply faction standing effects based on god alignment
    /// Good gods (Goodness > Darkness) = Light action → Faith standing
    /// Evil gods (Darkness > Goodness) = Dark action → Shadows standing
    /// </summary>
    private void ApplyFactionEffectForSacrifice(string godName, int amount)
    {
        var god = godSystem.GetGod(godName);
        if (god == null) return;

        bool isEvilGod = god.Darkness > god.Goodness;

        if (isEvilGod)
        {
            // v0.57.12: paired movement
            // 1.2.2: an offering is not a deed, so no good or dark deed count is refunded
            AlignmentSystem.Instance.ChangeAlignment(currentPlayer, amount, isGood: false, "temple.evil_devotion");
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheShadows, amount);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("temple.darkness_notes_devotion", amount));
        }
        else
        {
            // v0.57.12: paired movement
            // 1.2.2: an offering is not a deed, so no good or dark deed count is refunded
            AlignmentSystem.Instance.ChangeAlignment(currentPlayer, amount, isGood: true, "temple.good_devotion");
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, amount);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.light_notes_devotion", amount));
        }
    }

    /// <summary>
    /// Enhanced desecration that rewards XP and darkness (from Pascal TEMPLE.PAS)
    /// </summary>
    private async Task PerformEnhancedDesecration(God god)
    {
        terminal.WriteLine("");
        terminal.WriteLine("");

        var random = Random.Shared;

        // Desecration flavour text
        string[] desecrationKeys = new[]
        {
            "temple.desecrate_method_smash",
            "temple.desecrate_method_pour",
            "temple.desecrate_method_carve",
            "temple.desecrate_method_fire",
            "temple.desecrate_method_topple"
        };

        terminal.WriteLine(Loc.Get(desecrationKeys[random.Next(desecrationKeys.Length)]), "red");
        await Pacing.Wait(1500);

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.desecrated_altar", god.Name), "bright_red");

        // Process desecration in god system
        godSystem.ProcessAltarDesecration(god.Name, currentPlayer.Name2);

        // v0.57.0: desecrate altar bumped 10-25 → 15-30, plus paired chivalry loss via ChangeAlignment helper
        int darknessGain = random.Next(15, 31);
        long xpGain = (long)(Math.Pow(currentPlayer.Level, 1.5) * 20);

        UsurperRemake.Systems.AlignmentSystem.Instance.ChangeAlignment(currentPlayer, darknessGain, isGood: false, reason: $"desecrated altar of {god.Name}");
        currentPlayer.Experience += xpGain;
        currentPlayer.DarkNr--;
        currentPlayer.DesecrationsToday++;
        GodDeedSystem.Record(currentPlayer, GodAct.Desecration, terminal);   // 1.2.0 Temple gods: Earth taboo; Shadow, Death, Chaos deed
        GodStandingPenalty.RecordDesecration(currentPlayer, god.Name);       // 1.2.0 Temple gods piece 4: that god's standing, until the weekly reset

        terminal.WriteLine("", "white");
        terminal.WriteLine(Loc.Get("temple.darkness_flows", darknessGain), "dark_red");
        terminal.WriteLine(Loc.Get("temple.xp_from_profane", xpGain), "yellow");

        // Divine retribution — escalates with repeated desecrations
        // First desecration: 30% curse chance, mild damage
        // Second desecration: guaranteed curse, heavy damage + stat loss
        double curseChance = currentPlayer.DesecrationsToday >= 2 ? 1.0 : 0.3;
        if (random.NextDouble() < curseChance)
        {
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.god_curses", god.Name), "bright_red");

            int curseDamage = random.Next(10, 30 + currentPlayer.Level);
            if (currentPlayer.DesecrationsToday >= 2)
            {
                // Second desecration: much heavier punishment
                curseDamage *= 3;
                terminal.WriteLine(Loc.Get("temple.divine_fury"), "bright_red");

                // Lose a random base stat point
                // v1.2.5: the stat is picked by its index; its name is shown in the player's language
                string lostStat;
                switch (random.Next(6))
                {
                    case 0: currentPlayer.BaseStrength = Math.Max(1, currentPlayer.BaseStrength - 1); lostStat = Loc.Get("ui.stat_strength"); break;
                    case 1: currentPlayer.BaseDexterity = Math.Max(1, currentPlayer.BaseDexterity - 1); lostStat = Loc.Get("ui.stat_dexterity"); break;
                    case 2: currentPlayer.BaseConstitution = Math.Max(1, currentPlayer.BaseConstitution - 1); lostStat = Loc.Get("ui.stat_constitution"); break;
                    case 3: currentPlayer.BaseIntelligence = Math.Max(1, currentPlayer.BaseIntelligence - 1); lostStat = Loc.Get("ui.stat_intelligence"); break;
                    case 4: currentPlayer.BaseWisdom = Math.Max(1, currentPlayer.BaseWisdom - 1); lostStat = Loc.Get("ui.stat_wisdom"); break;
                    default: currentPlayer.BaseCharisma = Math.Max(1, currentPlayer.BaseCharisma - 1); lostStat = Loc.Get("ui.stat_charisma"); break;
                }
                currentPlayer.RecalculateStats();
                terminal.WriteLine(Loc.Get("temple.stat_diminish", lostStat, lostStat), "red");
            }

            currentPlayer.HP = Math.Max(1, currentPlayer.HP - curseDamage);
            terminal.WriteLine(Loc.Get("temple.divine_damage", curseDamage), "red");
        }

        // Generate news
        NewsSystem.Instance.Newsy(true, Loc.Get("temple.news_desecrated", currentPlayer.Name2, god.Name));

        await Pacing.Wait(3000);
    }

    /// <summary>
    /// Examine the ancient stones in the temple - discover the Seal of Creation
    /// "Where prayers echo in golden halls, seek the stone that predates the temple itself."
    /// </summary>
    private async Task ExamineAncientStones()
    {
        var story = StoryProgressionSystem.Instance;

        // Already collected
        if (story.CollectedSeals.Contains(UsurperRemake.Systems.SealType.Creation))
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.ancient_stones_revealed"), "gray");
            terminal.WriteLine(Loc.Get("temple.remember_truth"), "gray");
            await Pacing.Wait(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.walk_past_altars"));
        terminal.WriteLine(Loc.Get("temple.far_corner"));
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.WriteLine(Loc.Get("temple.massive_stones"));
        terminal.WriteLine(Loc.Get("temple.older_than_temple"));
        terminal.WriteLine(Loc.Get("temple.whose_altar"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.monks_say"));
        terminal.WriteLine(Loc.Get("temple.before_mortals"));
        terminal.WriteLine(Loc.Get("temple.before_gods"));
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (!await terminal.AskYesNoAsync(Loc.Get("temple.touch_stone")))
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.step_back_stones"), "gray");
            terminal.WriteLine(Loc.Get("temple.perhaps_another_time"), "gray");
            await Pacing.Wait(1000);
            return;
        }

        // Discovery sequence
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("temple.hand_touches"));
        terminal.WriteLine("");
        await Pacing.Wait(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.at_first_nothing"));
        terminal.WriteLine("");
        await Pacing.Wait(800);

        terminal.WriteLine(Loc.Get("temple.warmth_pulse"));
        terminal.WriteLine("");
        await Pacing.Wait(800);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.stone_glows"));
        terminal.WriteLine(Loc.Get("temple.ancient_symbols"));
        terminal.WriteLine(Loc.Get("temple.older_language"));
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("temple.voice_speaks"));
        terminal.WriteLine("");
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("temple.seek_truth"));
        terminal.WriteLine(Loc.Get("temple.first_seal"));
        terminal.WriteLine(Loc.Get("temple.remember_well"));
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("gray");
        await terminal.PressAnyKey(Loc.Get("temple.press_enter_continue"));

        // Collect the seal
        var sealSystem = UsurperRemake.Systems.SevenSealsSystem.Instance;
        await sealSystem.CollectSeal(currentPlayer, UsurperRemake.Systems.SealType.Creation, terminal);

        // Generate news
        NewsSystem.Instance.Newsy(true, Loc.Get("temple.news_seal_creation", currentPlayer.Name2));

        refreshMenu = true;
    }

    /// <summary>
    /// Process daily prayer - grants a temporary blessing once per day
    /// </summary>
    private async Task ProcessDailyPrayer()
    {
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        string worshippedImmortal = currentPlayer.WorshippedGod ?? "";

        if (string.IsNullOrEmpty(playerGod) && string.IsNullOrEmpty(worshippedImmortal))
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.must_worship_to_pray"), "yellow");
            terminal.WriteLine(Loc.Get("temple.visit_worship_first"), "gray");
            await Pacing.Wait(2000);
            return;
        }

        if (!UsurperRemake.Systems.DivineBlessingSystem.Instance.CanPrayToday(currentPlayer.Name2))
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.already_prayed"), "gray");
            terminal.WriteLine(Loc.Get("temple.return_tomorrow"), "gray");
            await Pacing.Wait(1500);
            return;
        }

        // 1.2.0 Temple gods: a prayer is devotion, so the neglect count starts over (either kind of god)
        FavorSystem.MarkDevotion(currentPlayer);
        // 1.2.0 Temple gods piece 3: prayer gives Favor once a day (the cap is saved, so a reload cannot repeat it)
        int prayerFavor = FavorSystem.Prayer(currentPlayer, godSystem);

        // === Prayer to an immortal player-god ===
        if (!string.IsNullOrEmpty(worshippedImmortal))
        {
            terminal.WriteLine("");
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.kneel_altar", worshippedImmortal));
            await Pacing.Wait(1000);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("temple.prayers_rise_immortal"));
            await Pacing.Wait(1000);

            // v1.1.15: prayer eases the mind once a day.
            int mentalBeforeImmortal = currentPlayer.Mental;
            MentalUi.ReportGain(terminal, currentPlayer, mentalBeforeImmortal, MentalSystem.TryDailyGain(currentPlayer, MentalDailySource.TemplePrayer, GameConfig.MentalTemplePrayerGain));
            FavorUi.ReportGain(terminal, currentPlayer, prayerFavor, godSystem);

            // Mark prayer as done for today (set LastPrayerRealDate for online mode)
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                currentPlayer.LastPrayerRealDate = DateTime.UtcNow;

            // Calculate boon effects and double them for prayer
            var baseEffects = currentPlayer.CachedBoonEffects;
            if (baseEffects != null && baseEffects.HasAnyEffect)
            {
                var boosted = baseEffects.Multiply(GameConfig.GodBoonPrayerMultiplier);

                // Apply as temporary DivineBlessingCombats/Bonus using the strongest buff
                // The prayer buff lasts for a time-based duration simulated as combat count
                // 1.2.0 Temple gods piece 2: twice as long at Zealot and up
                int prayerCombats = DivineBlessingSystem.PrayerBlessingCombats(FavorSystem.GetTier(FavorSystem.GetFavor(currentPlayer)));
                float prayerBonus = Math.Max(boosted.DamagePercent, boosted.DefensePercent);
                if (prayerBonus > 0)
                {
                    currentPlayer.DivineBlessingCombats = Math.Max(currentPlayer.DivineBlessingCombats, prayerCombats);
                    currentPlayer.DivineBlessingBonus = Math.Max(currentPlayer.DivineBlessingBonus, prayerBonus);
                }

                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("temple.divine_power_surges", worshippedImmortal));
                terminal.WriteLine("");

                // Show boosted boon effects
                var boonLines = DivineBoonRegistry.GetEffectSummaryLines(currentPlayer.DivineBoonConfig);
                // Recalculate from worshipped god's config
                string godConfig = "";
                var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                if (backend != null && DoorMode.IsOnlineMode)
                {
                    try { godConfig = await backend.GetGodBoonConfig(worshippedImmortal); } catch { }
                }
                boonLines = DivineBoonRegistry.GetEffectSummaryLines(godConfig);

                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("temple.prayer_amplifies"));
                foreach (var line in boonLines)
                {
                    terminal.SetColor("white");
                    terminal.WriteLine($"    • {line} (x{GameConfig.GodBoonPrayerMultiplier:0.#})");
                }

                if (prayerBonus > 0)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("temple.combat_blessing", (int)(prayerBonus * 100), prayerCombats));
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("temple.no_boons_configured", worshippedImmortal));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("temple.prayer_heard_no_boons"));
            }

            // Grant the god experience from the prayer
            if (DoorMode.IsOnlineMode)
            {
                try
                {
                    var prayerBackend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                    if (prayerBackend != null)
                        await prayerBackend.AddGodExperience(worshippedImmortal, 10);
                }
                catch { }
            }

            // Notify the god if online
            if (DoorMode.IsOnlineMode && UsurperRemake.Server.MudServer.Instance != null)
            {
                foreach (var kvp in UsurperRemake.Server.MudServer.Instance.ActiveSessions)
                {
                    var p = kvp.Value.Context?.Engine?.CurrentPlayer;
                    if (p != null && p.DivineName == worshippedImmortal)
                    {
                        // v1.2.5: in the god's session language
                        kvp.Value.EnqueueMessage(PrayedToYouMessage(kvp.Value.Context?.Language ?? "en", currentPlayer.Name2, 10));
                        break;
                    }
                }
            }

            terminal.WriteLine("");
            await Pacing.Wait(2000);
            refreshMenu = true;
            return;
        }

        // === Prayer to an NPC god (existing system) ===
        var god = godSystem.GetGod(playerGod);
        if (god == null)
        {
            terminal.WriteLine(Loc.Get("temple.god_no_longer_exists_short"), "red");
            await Pacing.Wait(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.kneel_altar", playerGod));
        await Pacing.Wait(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.prayers_rise_incense"));
        await Pacing.Wait(1000);

        // v1.1.15: prayer eases the mind once a day.
        int mentalBeforePrayer = currentPlayer.Mental;
        MentalUi.ReportGain(terminal, currentPlayer, mentalBeforePrayer, MentalSystem.TryDailyGain(currentPlayer, MentalDailySource.TemplePrayer, GameConfig.MentalTemplePrayerGain));
        FavorUi.ReportGain(terminal, currentPlayer, prayerFavor, godSystem);

        // Determine prayer response based on god's alignment
        float alignment = (float)(god.Goodness - god.Darkness) / Math.Max(1, god.Goodness + god.Darkness);

        if (alignment > 0.3f)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("temple.warm_light_fills"));
        }
        else if (alignment < -0.3f)
        {
            terminal.SetColor("dark_magenta");
            terminal.WriteLine(Loc.Get("temple.shadows_coil"));
        }
        else
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.balance_clarity"));
        }
        await Pacing.Wait(1000);

        // Grant the daily prayer blessing
        var blessing = UsurperRemake.Systems.DivineBlessingSystem.Instance.GrantPrayerBlessing(currentPlayer);

        if (blessing != null)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"*** {blessing.Name} ***");
            terminal.SetColor("white");
            terminal.WriteLine(blessing.Description);
            terminal.WriteLine("");

            if (blessing.DamageBonus > 0)
                terminal.WriteLine(Loc.Get("temple.damage_bonus", blessing.DamageBonus), "red");
            if (blessing.DefenseBonus > 0)
                terminal.WriteLine(Loc.Get("temple.defense_bonus", blessing.DefenseBonus), "cyan");
            if (blessing.XPBonus > 0)
                terminal.WriteLine(Loc.Get("temple.xp_bonus", blessing.XPBonus), "yellow");

            var duration = blessing.ExpiresAt - DateTime.Now;
            terminal.WriteLine(Loc.Get("temple.duration_label", duration.TotalMinutes.ToString("F0")), "gray");

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("temple.blessing_upon_you", playerGod));
        }
        else
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.prayers_unanswered"), "gray");
        }

        // Apply small faction effect for daily prayer based on god alignment
        if (alignment > 0.3f)
        {
            // Good god - light action
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, 1);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.faith_standing_gain"));
        }
        else if (alignment < -0.3f)
        {
            // Evil god - dark action
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheShadows, 1);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("temple.shadows_standing_gain"));
        }

        await Pacing.Wait(2000);
        refreshMenu = true;
    }

    #endregion

    #region Mira Companion Recruitment

    /// <summary>
    /// Check if Mira can be met at the temple
    /// </summary>
    private bool CanMeetMira()
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var mira = companionSystem.GetCompanion(UsurperRemake.Systems.CompanionId.Mira);

        // Check requirements
        if (mira == null || mira.IsRecruited || mira.IsDead)
            return false;

        // Level requirement
        if (currentPlayer.Level < mira.RecruitLevel)
            return false;

        // Already completed the encounter (declined)
        var story = StoryProgressionSystem.Instance;
        if (story.HasStoryFlag("mira_temple_encounter_complete"))
            return false;

        return true;
    }

    /// <summary>
    /// Visit the Meditation Chapel - Mira recruitment location
    /// </summary>
    private async Task VisitMeditationChapel()
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var mira = companionSystem.GetCompanion(UsurperRemake.Systems.CompanionId.Mira);

        if (!CanMeetMira())
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.meditation_chapel_empty"), "gray");
            terminal.WriteLine(Loc.Get("temple.only_silence"), "gray");
            await Pacing.Wait(1500);
            refreshMenu = true;
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("temple.meditation"), "bright_green", 66);
        terminal.WriteLine("");
        await Pacing.Wait(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.step_into_chapel"));
        terminal.WriteLine(Loc.Get("temple.candle_illuminates"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.faded_robes"));
        terminal.WriteLine(Loc.Get("temple.hands_clasped"));
        terminal.WriteLine(Loc.Get("temple.prays_to_nothing"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        // First dialogue
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.notices_watching"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"\"{mira.DialogueHints[0]}\"");
        terminal.WriteLine("");
        await Pacing.Wait(2000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.turns_back"));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{mira.DialogueHints[1]}\"");
        terminal.WriteLine("");
        await Pacing.Wait(2000);

        // Show her details
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("temple.this_is_companion", mira.Name, mira.Title));
        terminal.WriteLine(Loc.Get("temple.role_label", mira.CombatRole));
        terminal.WriteLine(Loc.Get("temple.abilities_label", string.Join(", ", mira.Abilities)));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(mira.BackstoryBrief);
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("bright_yellow");
        if (IsScreenReader)
        {
            terminal.WriteLine(Loc.Get("temple.sr_ask_join"));
            terminal.WriteLine(Loc.Get("temple.sr_talk_past"));
            terminal.WriteLine(Loc.Get("temple.sr_leave_prayers"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.visual_ask_join"));
            terminal.WriteLine(Loc.Get("temple.visual_talk_past"));
            terminal.WriteLine(Loc.Get("temple.visual_leave_prayers"));
        }
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync(Loc.Get("ui.your_choice"));

        switch (choice.ToUpper())
        {
            case "R":
                await AttemptMiraRecruitment(mira);
                break;

            case "T":
                await TalkToMira(mira);
                break;

            default:
                terminal.SetColor("gray");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.leave_silent_vigil"));
                terminal.WriteLine(Loc.Get("temple.speaks_without_turning"));
                terminal.SetColor("cyan");
                terminal.WriteLine($"\"{mira.DialogueHints[2]}\"");
                break;
        }

        // Mark encounter as complete
        StoryProgressionSystem.Instance.SetStoryFlag("mira_temple_encounter_complete", true);
        await terminal.PressAnyKey();
        refreshMenu = true;
    }

    /// <summary>
    /// Attempt to recruit Mira
    /// </summary>
    private async Task AttemptMiraRecruitment(UsurperRemake.Systems.Companion mira)
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.dungeons_dangerous"));
        terminal.WriteLine("");
        await Pacing.Wait(1000);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.looks_long_moment", mira.Name));
        terminal.WriteLine(Loc.Get("temple.flickers_in_eyes"));
        terminal.WriteLine(Loc.Get("temple.a_question"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.want_me_to_heal"));
        terminal.WriteLine(Loc.Get("temple.always_been_able"));
        terminal.WriteLine(Loc.Get("temple.will_it_matter"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.doesnt_wait"));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.perhaps_help"));
        terminal.WriteLine("");
        await Pacing.Wait(1000);

        bool success = await companionSystem.RecruitCompanion(
            UsurperRemake.Systems.CompanionId.Mira, currentPlayer, terminal);

        if (success)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.rises_from_altar", mira.Name));
            terminal.WriteLine(Loc.Get("temple.candle_flickers"));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("temple.companion_death_warning"));

            // Generate news
            NewsSystem.Instance.Newsy(false, Loc.Get("temple.news_found_mira", currentPlayer.Name2, mira.Name));
        }
    }

    /// <summary>
    /// Have a deeper conversation with Mira about her past
    /// </summary>
    private async Task TalkToMira(UsurperRemake.Systems.Companion mira)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.sit_beside"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("white");
        terminal.WriteLine(mira.Description);
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.was_healer_veloura"));
        terminal.WriteLine(Loc.Get("temple.corruption_came"));
        terminal.WriteLine(Loc.Get("temple.escaped_left_faith"));
        terminal.WriteLine("");
        await Pacing.Wait(2000);

        if (!string.IsNullOrEmpty(mira.PersonalQuestDescription))
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("temple.personal_quest_label", mira.PersonalQuestName));
            terminal.WriteLine($"\"{mira.PersonalQuestDescription}\"");
            terminal.WriteLine("");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.keep_praying"));
        terminal.WriteLine(Loc.Get("temple.to_empty_altar"));
        terminal.WriteLine(Loc.Get("temple.if_i_stop"));
        terminal.WriteLine("");
        await Pacing.Wait(2000);

        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (await terminal.AskYesNoAsync(Loc.Get("temple.ask_join_prompt")))
        {
            await AttemptMiraRecruitment(mira);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.squeeze_shoulder"));
            terminal.WriteLine(Loc.Get("temple.perhaps_another_time"));
        }
    }

    #endregion

    #region The Faith Faction Recruitment

    /// <summary>
    /// Show The Faith faction recruitment UI
    /// Meet High Priestess Mirael and potentially join The Faith
    /// </summary>
    private async Task ShowFaithRecruitment()
    {
        var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("temple.the_faith"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.faith_approach"));
        terminal.WriteLine(Loc.Get("temple.faith_devoted"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.mirael_intro"));
        terminal.WriteLine(Loc.Get("temple.mirael_watched"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        // Check if already in a faction
        if (factionSystem.PlayerFaction != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("temple.mirael_studies"));
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.already_serve", UsurperRemake.Systems.FactionSystem.Factions[factionSystem.PlayerFaction.Value].Name));
            terminal.WriteLine(Loc.Get("temple.no_divided_loyalties"));
            terminal.WriteLine(Loc.Get("temple.renounce_seek_again"));
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            refreshMenu = true;
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.sacred_flames"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.faith_old_gods_pure"));
        terminal.WriteLine(Loc.Get("temple.faith_guided"));
        terminal.WriteLine(Loc.Get("temple.faith_corrupted"));
        terminal.WriteLine(Loc.Get("temple.faith_absorbed"));
        terminal.WriteLine("");
        await Pacing.Wait(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.faith_healed"));
        terminal.WriteLine(Loc.Get("temple.faith_devotion"));
        terminal.WriteLine(Loc.Get("temple.faith_restore"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        // Show faction benefits
        WriteSectionHeader(Loc.Get("temple.faith_benefits"), "bright_yellow");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.benefit_healing"));
        terminal.WriteLine(Loc.Get("temple.benefit_prayers"));
        terminal.WriteLine(Loc.Get("temple.benefit_npcs"));
        terminal.WriteLine(Loc.Get("temple.benefit_standing"));
        terminal.WriteLine("");

        // Check requirements
        var (canJoin, reason) = factionSystem.CanJoinFaction(UsurperRemake.Systems.Faction.TheFaith, currentPlayer);

        if (!canJoin)
        {
            WriteSectionHeader(Loc.Get("temple.requirements_not_met"), "red");
            terminal.SetColor("yellow");
            terminal.WriteLine(reason);
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("temple.faith_requires"));
            terminal.WriteLine(Loc.Get("temple.faith_req_level"));
            terminal.WriteLine(Loc.Get("temple.faith_req_standing"));
            terminal.WriteLine(Loc.Get("temple.faith_your_standing", factionSystem.FactionStanding[UsurperRemake.Systems.Faction.TheFaith]));
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.mirael_return"));
            terminal.WriteLine(Loc.Get("temple.mirael_offerings"));
            await terminal.PressAnyKey();
            refreshMenu = true;
            return;
        }

        // Can join - offer the choice
        WriteSectionHeader(Loc.Get("temple.requirements_met"), "bright_green");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.mirael_extends"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.mirael_noted"));
        terminal.WriteLine(Loc.Get("temple.mirael_oath"));
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("temple.join_warning"));
        terminal.WriteLine(Loc.Get("temple.join_lock_out"));
        terminal.WriteLine(Loc.Get("temple.join_decrease"));
        terminal.WriteLine("");

        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (await terminal.AskYesNoAsync(Loc.Get("temple.join_prompt")))
        {
            await PerformFaithOath(factionSystem);
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("temple.mirael_understanding"));
            terminal.WriteLine(Loc.Get("temple.mirael_not_for_everyone"));
            terminal.WriteLine(Loc.Get("temple.mirael_doors_open"));
        }

        await terminal.PressAnyKey();
        refreshMenu = true;
    }

    /// <summary>
    /// Perform the oath ceremony to join The Faith
    /// </summary>
    private async Task PerformFaithOath(UsurperRemake.Systems.FactionSystem factionSystem)
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("temple.sacred_oath"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.kneel_sacred_flames"));
        terminal.WriteLine(Loc.Get("temple.mirael_stands"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.repeat_after_me"));
        terminal.WriteLine("");
        await Pacing.Wait(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("temple.oath_line1"));
        await Pacing.Wait(1200);
        terminal.WriteLine(Loc.Get("temple.oath_line2"));
        await Pacing.Wait(1200);
        terminal.WriteLine(Loc.Get("temple.oath_line3"));
        await Pacing.Wait(1200);
        terminal.WriteLine(Loc.Get("temple.oath_line4"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.flames_flare"));
        terminal.WriteLine(Loc.Get("temple.profound_peace"));
        terminal.WriteLine("");
        await Pacing.Wait(1500);

        // Actually join the faction
        factionSystem.JoinFaction(UsurperRemake.Systems.Faction.TheFaith, currentPlayer);

        WriteBoxHeader(Loc.Get("temple.joined_faith"), "bright_green");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.mirael_welcome"));
        terminal.WriteLine(Loc.Get("temple.mirael_never_waver"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.as_member_receive"));
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("temple.receive_discount"));
        terminal.WriteLine(Loc.Get("temple.receive_recognition"));
        terminal.WriteLine(Loc.Get("temple.receive_blessings"));
        terminal.WriteLine("");

        // Generate news
        NewsSystem.Instance.Newsy(true, Loc.Get("temple.news_joined_faith", currentPlayer.Name2));

        // Log to debug
        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("FACTION", $"{currentPlayer.Name2} joined The Faith");
    }

    #endregion

    #region Inner Sanctum

    private async Task VisitInnerSanctum()
    {
        if (FactionSystem.Instance?.HasTempleAccess() != true)
        {
            terminal.SetColor("red");
            terminal.WriteLine("\n" + Loc.Get("temple.sanctum_sealed"));
            terminal.WriteLine(Loc.Get("temple.sanctum_faith_only"));
            await Pacing.Wait(2000);
            return;
        }

        bool alreadyMeditated;
        if (DoorMode.IsOnlineMode)
        {
            var boundary = DailySystemManager.GetCurrentResetBoundary();
            alreadyMeditated = currentPlayer.LastInnerSanctumRealDate >= boundary;
        }
        else
        {
            int today = DailySystemManager.Instance?.CurrentDay ?? 0;
            alreadyMeditated = currentPlayer.InnerSanctumLastDay >= today;
        }
        if (alreadyMeditated)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\n" + Loc.Get("temple.sanctum_already_meditated"));
            terminal.WriteLine(Loc.Get("temple.sanctum_ready_tomorrow"));
            await Pacing.Wait(2000);
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("temple.inner_sanctum"), "bright_cyan", 66);
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.sanctum_stillness"));
        terminal.WriteLine(Loc.Get("temple.sanctum_runes"));
        terminal.SetColor("yellow");
        terminal.WriteLine("\n" + Loc.Get("temple.sanctum_cost", GameConfig.InnerSanctumCost));
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.sanctum_grant"));
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("temple.sanctum_gold_label", currentPlayer.Gold.ToString("N0")));
        terminal.WriteLine("");

        // v1.1.15: yesno-convert-a, strict (Y/N)
        if (!await terminal.AskYesNoAsync(Loc.Get("temple.sanctum_enter_prompt")))
            return;

        if (currentPlayer.Gold < GameConfig.InnerSanctumCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("temple.sanctum_cant_afford"));
            await Pacing.Wait(2000);
            return;
        }

        currentPlayer.Gold -= GameConfig.InnerSanctumCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.InnerSanctumCost);
        if (DoorMode.IsOnlineMode)
            currentPlayer.LastInnerSanctumRealDate = DateTime.UtcNow;
        else
            currentPlayer.InnerSanctumLastDay = DailySystemManager.Instance?.CurrentDay ?? 0;

        terminal.SetColor("gray");
        terminal.WriteLine("\n" + Loc.Get("temple.sanctum_kneel"));
        await Pacing.Wait(2000);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.sanctum_warmth"));
        await Pacing.Wait(1500);

        // Grant +1 to a random stat
        var rng = Random.Shared;
        string statName;
        // 1.2.0: the +1 is written to the Base field through GrantPermanentStat, so it lasts
        StatKind sanctumStat;
        switch (rng.Next(9))
        {
            case 0: sanctumStat = StatKind.Strength; statName = Loc.Get("ui.stat_strength"); break;
            case 1: sanctumStat = StatKind.Defence; statName = Loc.Get("combat.status_defence_label"); break;
            case 2: sanctumStat = StatKind.Stamina; statName = Loc.Get("ui.stat_stamina"); break;
            case 3: sanctumStat = StatKind.Agility; statName = Loc.Get("ui.stat_agility"); break;
            case 4: sanctumStat = StatKind.Charisma; statName = Loc.Get("ui.stat_charisma"); break;
            case 5: sanctumStat = StatKind.Dexterity; statName = Loc.Get("ui.stat_dexterity"); break;
            case 6: sanctumStat = StatKind.Wisdom; statName = Loc.Get("ui.stat_wisdom"); break;
            case 7: sanctumStat = StatKind.Intelligence; statName = Loc.Get("ui.stat_intelligence"); break;
            case 8: sanctumStat = StatKind.Constitution; statName = Loc.Get("ui.stat_constitution"); break;
            default: sanctumStat = StatKind.Strength; statName = Loc.Get("ui.stat_strength"); break;
        }
        currentPlayer.GrantPermanentStat(sanctumStat, 1);

        terminal.SetColor("bright_green");
        terminal.WriteLine("\n" + Loc.Get("temple.sanctum_stat_gain", statName));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.sanctum_power_mark"));
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }

    #endregion

    #region Immortal God Worship (v0.45.0)

    /// <summary>Get all ascended immortal gods (from NPCSpawnSystem or online DB)</summary>
    private async Task<List<ImmortalGodInfo>> GetImmortalGodsAsync()
    {
        var gods = new List<ImmortalGodInfo>();

        // In MUD mode, query all immortals from the DB
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend != null && DoorMode.IsOnlineMode)
        {
            try
            {
                var immortals = await backend.GetImmortalPlayers();
                // 1.2.0: one standings read for the whole listing: follower counts and each god's domain boon scale.
                var standings = await Task.Run(() => backend.GetGodStandings());
                long strongestCanon = GodBoonSystem.StrongestCanon(standings);
                foreach (var god in immortals)
                {
                    // Don't show the player's own god entry if they ARE the immortal.
                    // Alt characters on the same account (mortal) should still see and worship their god.
                    if (currentPlayer.IsImmortal && !string.IsNullOrEmpty(currentPlayer.DivineName)
                        && currentPlayer.DivineName.Equals(god.DivineName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    gods.Add(new ImmortalGodInfo
                    {
                        DivineName = god.DivineName,
                        GodLevel = god.GodLevel,
                        GodAlignment = god.GodAlignment,
                        Believers = PantheonLocation.CountBelievers(god.DivineName, standings),
                        IsOnline = god.IsOnline,
                        Username = god.Username,
                        DivineBoonConfig = god.DivineBoonConfig ?? "",
                        Domain = GodBoonSystem.ParseDomain(god.DivineDomain),
                        BoonScalePct = GodBoonSystem.PlayerGodScalePct(
                            GodBoonSystem.StandingOf(standings, god.DivineName), strongestCanon,
                            GodBoonSystem.DaysInactive(god.IsOnline, god.LastLogin, DateTime.UtcNow))
                    });
                }
            }
            catch { /* DB unavailable */ }
        }
        else
        {
            // Single-player: only the current player (if they've ascended, which shouldn't happen in Temple)
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player != null && player.IsImmortal && !string.IsNullOrEmpty(player.DivineName))
            {
                gods.Add(new ImmortalGodInfo
                {
                    DivineName = player.DivineName,
                    GodLevel = player.GodLevel,
                    GodAlignment = player.GodAlignment,
                    Believers = PantheonLocation.CountBelievers(player.DivineName),
                    IsOnline = true
                });
            }
        }

        return gods;
    }

    /// <summary>
    /// 1.2.0 Temple gods piece 7: the ascended player-gods' altars, listed under the canon gods in
    /// the one altar list (the old J screen's list): each god's title, whether they are online,
    /// alignment and followers, domain boon at the god's current scale, ward and configured boons.
    /// </summary>
    private void DisplayPlayerGodAltars(List<ImmortalGodInfo> gods)
    {
        if (gods.Count == 0) return;
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.altars_ascended"), "bright_yellow");
        terminal.WriteLine("");

        for (int i = 0; i < gods.Count; i++)
        {
            var god = gods[i];
            string title = PantheonLocation.GetGodTitle(god.GodLevel);
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {Loc.Get("temple.pgod.row", god.DivineName, title)}  ");
            terminal.SetColor(god.IsOnline ? "bright_green" : "gray");
            terminal.Write(Loc.Get(god.IsOnline ? "temple.pgod.online" : "temple.pgod.offline"));
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("temple.pgod.followers", AlignmentName(god.GodAlignment), god.Believers)}");

            // 1.2.0 Temple gods piece 2: the god's domain, its boon at the god's current scale, and its ward
            terminal.SetColor("cyan");
            if (god.Domain == GodDomain.None)
                terminal.WriteLine($"     {Loc.Get("god.player_domain_none")}");
            else
            {
                Say(Loc.Get("god.player_domain_line", GodBoonSystem.DomainName(god.Domain), god.BoonScalePct), "cyan", "     ");
                Say(Loc.Get("god.boon_line", GodBoonSystem.DescribeBoon(god.Domain, god.BoonScalePct), god.BoonScalePct), "gray", "     ");
                Say(Loc.Get("god.ward_line", GodBoonSystem.DescribeWard(god.Domain)), "darkgray", "     ");
            }

            // Show boon description
            Say(DivineBoonRegistry.GenerateDescription(god.DivineBoonConfig, god.GodAlignment), "gray", "     ");

            // Show individual boons
            foreach (var line in DivineBoonRegistry.GetEffectSummaryLines(god.DivineBoonConfig))
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"     • {line}");
            }

            if (i < gods.Count - 1) terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Worship an ascended player-god chosen from the one altar list (1.2.0 Temple gods piece 7:
    /// the old J key's switch, reached from W). The cost of leaving a current god is shown and asked
    /// before the switch, which goes through SwitchGodAsync like every Temple god change.
    /// </summary>
    private async Task WorshipImmortalGod(ImmortalGodInfo chosen)
    {
        // Check if already following this god
        if (currentPlayer.WorshippedGod == chosen.DivineName)
        {
            terminal.WriteLine(Loc.Get("temple.already_follow", chosen.DivineName), "gray");
            await terminal.PressAnyKey();
            return;
        }

        // If already following another player god, show the cost and ask
        if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
        {
            terminal.WriteLine(Loc.Get("temple.currently_follow", currentPlayer.WorshippedGod), "yellow");
            ShowSwitchCost(chosen.DivineName);   // 1.2.0 Temple gods piece 4: the cost before the choice
            // v1.1.15: yesno-convert-a, strict (Y/N)
            if (!await terminal.AskYesNoAsync(Loc.Get("temple.abandon_prompt"))) return;
        }

        // If following a canon god, show the cost and ask. 1.2.0 Temple gods piece 4: its wrath is
        // the one switching rule (Divine Wrath by the Favor lost), no longer a random smite here.
        string oldNpcGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(oldNpcGod))
        {
            terminal.WriteLine(Loc.Get("temple.currently_worship_elder", oldNpcGod), "yellow");
            ShowSwitchCost(chosen.DivineName);
            // v1.1.15: yesno-convert-a, strict (Y/N)
            if (!await terminal.AskYesNoAsync(Loc.Get("temple.abandon_elder_prompt", oldNpcGod))) return;

            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("temple.renounce_elder", oldNpcGod));
        }

        await SwitchGodAsync(chosen.DivineName);

        // Cache boon effects from the chosen god
        currentPlayer.CachedBoonEffects = DivineBoonRegistry.CalculateEffects(chosen.DivineBoonConfig);
        await GodBoonSystem.RefreshPlayerGodBoonAsync(currentPlayer);   // 1.2.0 Temple gods piece 2: the domain boon

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.kneel_immortal", chosen.DivineName));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.divine_presence"));

        // Show boon effects the player will receive
        var effects = currentPlayer.CachedBoonEffects;
        if (effects != null && effects.HasAnyEffect)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("temple.divine_favors_flow"));
            foreach (var line in DivineBoonRegistry.GetEffectSummaryLines(chosen.DivineBoonConfig))
            {
                terminal.SetColor("white");
                terminal.WriteLine($"    • {line}");
            }
        }

        // Notify the god if online
        if (DoorMode.IsOnlineMode && chosen.IsOnline && UsurperRemake.Server.MudServer.Instance != null)
        {
            // v1.2.5: in the god's session language
            string mortal = currentPlayer.Name2;
            UsurperRemake.Server.MudServer.Instance.SendToPlayerLocalized(chosen.Username, lang => NewWorshipperMessage(lang, mortal));
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    private async Task SacrificeToImmortalGod()
    {
        if (string.IsNullOrEmpty(currentPlayer.WorshippedGod))
        {
            terminal.WriteLine(IsScreenReader
                ? Loc.Get("temple.must_worship_immortal_sr")
                : Loc.Get("temple.must_worship_immortal"), "gray");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("temple.sacrifice_gold_to", currentPlayer.WorshippedGod));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.gold_on_hand", currentPlayer.Gold.ToString("N0")));
        terminal.WriteLine("");

        string input = await terminal.GetInputAsync(Loc.Get("temple.amount_to_sacrifice"));
        if (!long.TryParse(input, out long amount) || amount <= 0) return;

        if (amount > currentPlayer.Gold)
        {
            terminal.WriteLine(Loc.Get("temple.not_enough_gold_sacrifice"), "red");
            await terminal.PressAnyKey();
            return;
        }

        currentPlayer.Gold -= amount;
        int power = PantheonLocation.GetSacrificePower(amount);

        // Deliver experience to the god
        bool delivered = false;

        // Check if the god is online (in-memory delivery)
        if (DoorMode.IsOnlineMode && UsurperRemake.Server.MudServer.Instance != null)
        {
            foreach (var kvp in UsurperRemake.Server.MudServer.Instance.ActiveSessions)
            {
                var godPlayer = kvp.Value.Context?.Engine?.CurrentPlayer;
                if (godPlayer != null && godPlayer.IsImmortal && godPlayer.DivineName == currentPlayer.WorshippedGod)
                {
                    godPlayer.GodExperience += power;
                    // v1.2.5: in the god's session language
                    kvp.Value.EnqueueMessage(GoldSacrificedMessage(kvp.Value.Context?.Language ?? "en", currentPlayer.Name2, amount, power));
                    delivered = true;
                    break;
                }
            }
        }

        // Offline god: atomic DB update + message
        if (!delivered && DoorMode.IsOnlineMode)
        {
            var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
            if (backend != null)
            {
                try
                {
                    await backend.AddGodExperience(currentPlayer.WorshippedGod, power);
                    // Find the god's username for the message
                    var immortals = await backend.GetImmortalPlayers();
                    var godInfo = immortals.FirstOrDefault(g => g.DivineName == currentPlayer.WorshippedGod);
                    if (godInfo != null)
                    {
                        string sacrificer = currentPlayer.Name2;
                        await backend.SendMessageToKeyLocalized("Temple", godInfo.Username, "divine",
                            lang => Loc.GetIn(lang, "mail.sacrifice", sacrificer, $"{amount:N0}", power));
                    }
                    delivered = true;
                }
                catch { /* DB unavailable */ }
            }
        }

        // Single-player fallback
        if (!delivered)
        {
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player != null && player.IsImmortal && player.DivineName == currentPlayer.WorshippedGod)
            {
                player.GodExperience += power;
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("temple.gold_upon_altar", amount.ToString("N0"), currentPlayer.WorshippedGod));
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.offering_burns", power));
        // 1.2.0 Temple gods piece 3: the same Favor for gold as a canon god (one god system)
        FavorUi.ReportGain(terminal, currentPlayer, FavorSystem.GoldSacrifice(currentPlayer, amount, godSystem), godSystem);

        // v0.61.3: player report — "when you are affiliated with a player god,
        // sacrificing gold to your deity doesn't make faith standing go higher."
        // Confirmed: this function delivered divine experience to the immortal
        // god correctly, but never updated the worshipper's Faith faction
        // reputation or alignment the way the elder-god sacrifice path does
        // (TempleLocation.ProcessGoldSacrifice, lines 1466-1495). Mirror that
        // logic here: paired-movement alignment shift, faction reputation
        // bump, capped per sacrifice to match the v0.60.0 anti-cheese cap.
        // Player-immortal worshippers are treated as faithful by default
        // (most player-immortals run good-aligned campaigns); a future patch
        // could read the immortal's own Chivalry/Darkness if we want to
        // route evil-immortal sacrifices to TheShadows instead.
        int standingGain = Math.Min(GameConfig.MaxAlignmentGainPerTempleSacrifice,
            Math.Max(1, (int)(amount / 100)));
        if (standingGain > 0)
        {
            // 1.2.2: an offering is not a deed, so no good or dark deed count is refunded
            AlignmentSystem.Instance.ChangeAlignment(currentPlayer, standingGain, isGood: true, "temple.immortal_sacrifice");
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, standingGain);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.devotion_noted", standingGain));
        }

        // Small blessing for the worshipper
        if (power >= 3)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("temple.warm_glow"));
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    internal class ImmortalGodInfo
    {
        public string DivineName { get; set; } = "";
        public int GodLevel { get; set; }
        public string GodAlignment { get; set; } = "";
        public int Believers { get; set; }
        public bool IsOnline { get; set; }
        public string Username { get; set; } = "";
        public string DivineBoonConfig { get; set; } = "";
        public GodDomain Domain { get; set; } = GodDomain.None;     // 1.2.0 Temple gods piece 2
        public int BoonScalePct { get; set; }                       // percent of a canon god's boon
    }

    #endregion

    #region Temple rooms (1.2.0 Temple gods piece 7)

    /// <summary>
    /// 1.2.0 Temple gods piece 7: the Temple's rooms. The hall screen lists them, one key each: the
    /// Nave of the Gods (every altar, worship, prayer, offerings, the Altars screen), the Undercroft
    /// (the dark altars and desecration), the Faith (Mirael, the oath, the Cloister), the Old Stones
    /// (prophecies, visions, the foundation stones) and the Halls of Memory (the Fallen, the Rite
    /// of Return, the Ascended). The Meditation Chapel and the Deep Temple are scenes, not rooms.
    /// </summary>
    internal enum TempleRoom { Nave, Undercroft, Faith, OldStones, Memory }

    /// <summary>One menu entry: its key, its label (visual and screen reader) and its short BBS label.</summary>
    internal readonly record struct TempleMenuItem(string Key, string Label, string Short);

    /// <summary>One altar from the one god list: a canon god or an ascended player-god.</summary>
    internal sealed record AltarPick(string Name, God? Canon, ImmortalGodInfo? PlayerGod);

    /// <summary>
    /// Keys the hall screen had before the rooms, and where each went. Typed at the hall they print
    /// this pointer and do nothing else.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> MovedKeys = new Dictionary<string, string>
    {
        ["W"] = "temple.moved.worship", ["J"] = "temple.moved.worship", ["L"] = "temple.moved.worship",
        ["Y"] = "temple.moved.pray",
        ["C"] = "temple.moved.offer", ["I"] = "temple.moved.offer", ["$"] = "temple.moved.offer",
        ["S"] = "temple.moved.altars", ["G"] = "temple.moved.altars",
        ["D"] = "temple.moved.desecrate",
        ["E"] = "temple.moved.stones",
        ["K"] = "temple.moved.memory", ["V"] = "temple.moved.memory",
        ["N"] = "temple.moved.faith",
        ["O"] = "temple.moved.confession",
    };

    /// <summary>Tests only: the ascended player-gods the altar lists use instead of the save backend.</summary>
    internal List<ImmortalGodInfo>? ImmortalGodsForTests;

    private static TempleMenuItem Item(string key, string labelKey, string shortKey) =>
        new(key, Loc.Get(labelKey), Loc.Get(shortKey));

    /// <summary>The hall screen's rooms, in order; H, M and T only where they apply.</summary>
    private List<TempleMenuItem> TopMenuItems()
    {
        var items = new List<TempleMenuItem>
        {
            Item("A", "temple.top.nave", "temple.room.nave"),
            Item("U", "temple.top.undercroft", "temple.room.undercroft"),
            Item("F", "temple.top.faith", "temple.room.faith"),
            Item("P", "temple.top.stones", "temple.room.stones"),
        };
        if (HallsOfMemoryOpen()) items.Add(Item("H", "temple.top.memory", "temple.room.memory"));
        if (CanMeetMira()) items.Add(Item("M", "temple.top.chapel", "temple.room.chapel"));
        if (CanEnterDeepTemple()) items.Add(Item("T", "temple.top.deep", "temple.room.deep"));
        items.Add(Item("R", "temple.top.return", "temple.room.return"));
        return items;
    }

    /// <summary>What a hall key opens. M and T check their own conditions again.</summary>
    private Func<Task>? TopAction(string key) => key switch
    {
        "A" => () => RunRoom(TempleRoom.Nave),
        "U" => () => RunRoom(TempleRoom.Undercroft),
        "F" => () => RunRoom(TempleRoom.Faith),
        "P" => () => RunRoom(TempleRoom.OldStones),
        "H" => () => RunRoom(TempleRoom.Memory),
        "M" => VisitMeditationChapel,
        "T" => EnterDeepTemple,
        _ => null
    };

    /// <summary>
    /// One key at the hall screen. A listed key opens its room (R leaves the Temple: true). M and T
    /// while hidden run their own closed message. An old key that moved into a room prints a
    /// one-line pointer and does nothing else; any other key is invalid.
    /// </summary>
    private async Task<bool> RouteTopLevel(string choice)
    {
        string key = (choice ?? "").Trim().ToUpperInvariant();
        bool listed = TopMenuItems().Any(i => i.Key == key);
        if (key == "R" && listed) return true;
        // T once Aurelion is resolved: his memory is in the Halls of Memory, never a fight
        if (key == "T" && !listed && AurelionResolved())
        {
            terminal.WriteLine("");
            Say(Loc.Get("temple.moved.aurelion"), "yellow");
            await terminal.PressAnyKey();
            return false;
        }
        if (listed || key == "M" || key == "T")
        {
            var action = TopAction(key);
            if (action != null) await action();
            refreshMenu = true;
            return false;
        }

        // H while no hall applies: the old H (holy news) became the Nave's Altars screen
        string? pointer = key == "H" ? "temple.moved.altars" : MovedKeys.TryGetValue(key, out var k) ? k : null;
        // K and V moved into the Halls of Memory: the pointer only makes sense while that hall is open
        if (pointer == "temple.moved.memory" && !HallsOfMemoryOpen()) pointer = null;
        if (pointer != null)
        {
            terminal.WriteLine("");
            Say(Loc.Get(pointer), "yellow");
            await terminal.PressAnyKey();
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.invalid_choice"), "red");
            await Pacing.Wait(1000);
        }
        return false;
    }

    /// <summary>A room's entries, in order, only those that apply now.</summary>
    private async Task<List<TempleMenuItem>> RoomItems(TempleRoom room)
    {
        var items = new List<TempleMenuItem>();
        string back = Loc.Get("temple.room.back");
        switch (room)
        {
            case TempleRoom.Nave:
                items.Add(Item("W", "temple.nave.worship", "temple.nave.worship"));
                AddPrayItem(items);
                items.Add(Item("O", "temple.nave.offer", "temple.nave.offer"));
                items.Add(Item("A", "temple.nave.altars", "temple.nave.altars"));
                break;
            case TempleRoom.Undercroft:
                items.Add(Item("W", "temple.undercroft.worship", "temple.undercroft.worship"));
                if (await OwnGodIsDark()) AddPrayItem(items);
                items.Add(Item("O", "temple.undercroft.offer", "temple.undercroft.offer"));
                items.Add(Item("D", "temple.undercroft.desecrate", "temple.undercroft.desecrate"));
                break;
            case TempleRoom.Faith:
                items.Add(Item("M", "temple.faith_hall.mirael", "temple.faith_hall.mirael"));
                if (FactionSystem.Instance?.HasTempleAccess() == true)
                {
                    string cloister = MeditatedInCloisterToday()
                        ? Loc.Get("temple.faith_hall.cloister_done")
                        : Loc.Get("temple.faith_hall.cloister", GameConfig.InnerSanctumCost);
                    items.Add(new TempleMenuItem("C", cloister, cloister));
                }
                break;
            case TempleRoom.OldStones:
                items.Add(Item("P", "temple.stones.prophecies", "temple.stones.prophecies"));
                if (!StoryProgressionSystem.Instance.CollectedSeals.Contains(UsurperRemake.Systems.SealType.Creation))
                    items.Add(Item("E", "temple.stones.examine", "temple.stones.examine"));
                break;
            case TempleRoom.Memory:
                if (CanShowHallOfTheFallen()) items.Add(Item("K", "temple.memory.fallen", "temple.memory.fallen"));
                if (CanShowRiteOfReturn())
                {
                    string rite = Loc.Get("temple.memory.rite", GameConfig.GetRiteOfReturnCost(currentPlayer.Level));
                    items.Add(new TempleMenuItem("U", rite, rite));
                }
                if (HasAscendedStatues()) items.Add(Item("V", "temple.memory.ascended", "temple.memory.ascended"));
                if (AurelionResolved()) items.Add(Item("A", "temple.memory.aurelion", "temple.memory.aurelion"));
                break;
        }
        items.Add(new TempleMenuItem("R", back, back));
        return items;
    }

    /// <summary>Y: prayer to the character's own god, once a day (shown only with a god).</summary>
    private void AddPrayItem(List<TempleMenuItem> items)
    {
        var own = GodRegistry.GetWorshippedGod(currentPlayer, godSystem);
        if (own == null) return;
        bool canPray = DivineBlessingSystem.Instance.CanPrayToday(currentPlayer.Name2);
        string label = Loc.Get(canPray ? "temple.nave.pray" : "temple.nave.prayed", own.Value.Name);
        items.Add(new TempleMenuItem("Y", label, label));
    }

    /// <summary>What a room key does. Desecration exists only in the Undercroft.</summary>
    private Func<Task>? RoomAction(TempleRoom room, string key) => (room, key) switch
    {
        (TempleRoom.Nave, "W") => () => ProcessWorship(TempleRoom.Nave),
        (TempleRoom.Nave, "Y") => ProcessDailyPrayer,
        (TempleRoom.Nave, "O") => () => ProcessOffering(TempleRoom.Nave),
        (TempleRoom.Nave, "A") => ShowAltarsScreen,
        (TempleRoom.Undercroft, "W") => () => ProcessWorship(TempleRoom.Undercroft),
        (TempleRoom.Undercroft, "Y") => ProcessDailyPrayer,
        (TempleRoom.Undercroft, "O") => () => ProcessOffering(TempleRoom.Undercroft),
        (TempleRoom.Undercroft, "D") => ProcessDesecrateAltar,
        (TempleRoom.Faith, "M") => ShowFaithRecruitment,
        (TempleRoom.Faith, "C") => VisitInnerSanctum,
        (TempleRoom.OldStones, "P") => DisplayOldGodsProphecies,
        (TempleRoom.OldStones, "E") => ExamineAncientStones,
        (TempleRoom.Memory, "K") => ShowHallOfTheFallen,
        (TempleRoom.Memory, "U") => ProcessRiteOfReturn,
        (TempleRoom.Memory, "V") => ShowAscendedStatues,
        (TempleRoom.Memory, "A") => ShowAurelionMemory,
        _ => null
    };

    /// <summary>
    /// A room's loop: its screen (skipped in Expert mode unless asked with ?), the global commands,
    /// a listed key's action, and R or Enter back to the hall screen.
    /// </summary>
    private async Task RunRoom(TempleRoom room)
    {
        bool redraw = true;
        while (true)
        {
            GodBoonSystem.ApplyPendingBoonRecalc(currentPlayer);
            var items = await RoomItems(room);
            if (redraw || !currentPlayer.Expert) DrawRoom(room, items);
            redraw = false;

            string choice = ((await terminal.GetInputAsync(Loc.Get("ui.your_choice"))) ?? "").Trim();
            var (handled, _) = await TryProcessGlobalCommand(choice);
            if (handled) { redraw = true; continue; }

            string key = choice.ToUpperInvariant();
            if (key == "?") { redraw = true; continue; }
            if (key == "R" || key.Length == 0) return;

            var action = items.Any(i => i.Key == key) ? RoomAction(room, key) : null;
            if (action == null)
            {
                terminal.WriteLine(Loc.Get("temple.invalid_choice"), "red");
                await Pacing.Wait(1000);
                continue;
            }
            await action();
        }
    }

    /// <summary>A room's screen: its header, what the player sees there, and its menu.</summary>
    private void DrawRoom(TempleRoom room, List<TempleMenuItem> items)
    {
        terminal.ClearScreen();
        switch (room)
        {
            case TempleRoom.Nave:
                WriteBoxHeader(Loc.Get("temple.nave.header"), "bright_cyan");
                terminal.WriteLine("");
                Say(Loc.Get("temple.nave.desc1"), "white");
                Say(Loc.Get("temple.nave.desc2"), "white");
                terminal.WriteLine("");
                WriteWorshipLine();
                if (IsEvilPlayer()) Say(Loc.Get("temple.nave.evil_unwelcome"), "red");
                break;
            case TempleRoom.Undercroft:
                WriteBoxHeader(Loc.Get("temple.undercroft.header"), "dark_red");
                terminal.WriteLine("");
                Say(Loc.Get("temple.undercroft.desc1"), "white");
                Say(Loc.Get("temple.undercroft.desc2"), "white");
                terminal.WriteLine("");
                WriteWorshipLine();
                var band = AlignmentSystem.Instance.GetAlignment(currentPlayer);
                bool dark = band == AlignmentSystem.AlignmentType.Evil || band == AlignmentSystem.AlignmentType.Dark;
                Say(Loc.Get(dark ? "temple.undercroft.welcome_dark" : "temple.undercroft.welcome_other"), dark ? "bright_magenta" : "gray");
                break;
            case TempleRoom.Faith:
                WriteBoxHeader(Loc.Get("temple.faith_hall.header"), "bright_yellow");
                terminal.WriteLine("");
                Say(Loc.Get("temple.faith_hall.creed1"), "bright_cyan");
                Say(Loc.Get("temple.faith_hall.creed2"), "bright_cyan");
                Say(Loc.Get("temple.faith_hall.creed3"), "bright_cyan");
                break;
            case TempleRoom.OldStones:
                WriteBoxHeader(Loc.Get("temple.stones.header"), "bright_magenta");
                terminal.WriteLine("");
                Say(Loc.Get("temple.stones.desc1"), "white");
                Say(Loc.Get("temple.stones.desc2"), "white");
                break;
            case TempleRoom.Memory:
                WriteBoxHeader(Loc.Get("temple.memory.header"), "bright_cyan");
                terminal.WriteLine("");
                Say(Loc.Get("temple.memory.desc1"), "white");
                break;
        }
        terminal.WriteLine("");
        WriteTempleMenu(items, bbsRows: false);
        terminal.WriteLine("");
    }

    /// <summary>
    /// A menu from its item list, the same keys in every mode: the screen reader reads "K. label"
    /// lines, the BBS hall screen packs the short labels three to a row, and otherwise each entry
    /// is one "[K] label" line.
    /// </summary>
    private void WriteTempleMenu(List<TempleMenuItem> items, bool bbsRows)
    {
        if (IsScreenReader)
        {
            foreach (var i in items) WriteSRMenuOption(i.Key, i.Label);
            return;
        }
        if (bbsRows && IsBBSSession)
        {
            for (int n = 0; n < items.Count; n += 3)
                ShowBBSMenuRow(items.Skip(n).Take(3).Select(i => (i.Key, "bright_yellow", i.Short)).ToArray());
            return;
        }
        foreach (var i in items)
        {
            terminal.Write(" ");
            WriteSRMenuOption(i.Key, i.Label);
        }
    }

    /// <summary>A line of Temple text, wrapped to the 80-column screen.</summary>
    private void Say(string text, string color, string indent = " ")
    {
        terminal.SetColor(color);
        UsurperRemake.UI.UIHelper.WriteWrapped(terminal, text, indent);
    }

    /// <summary>The god the player worships (canon or player-god), or that they worship none.</summary>
    private void WriteWorshipLine()
    {
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod))
            terminal.WriteLine(Loc.Get("temple.worship_god", playerGod), "cyan");
        else if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
            terminal.WriteLine(Loc.Get("temple.follow_immortal", currentPlayer.WorshippedGod), "bright_yellow");
        else
            terminal.WriteLine(Loc.Get("temple.not_believer"), "gray");
    }

    /// <summary>
    /// The Nave's Altars screen: the player's devotion (DisplayPlayerStatus), then every god on one
    /// ranking by standing with this week's god (DisplayGodRanking, which waits for a key). It
    /// replaces the old altar list, rankings, holy news and status keys; Manwe is on neither list.
    /// </summary>
    private async Task ShowAltarsScreen()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("temple.altars"), "magenta");
        await DisplayPlayerStatus();
        await DisplayGodRanking();
    }

    /// <summary>
    /// The altars of a room: the Nave has every god (the ten canon gods and each ascended
    /// player-god), the Undercroft only the dark ones (Umbrath, Mortis, Discordia, and player-gods
    /// of the Dark). Never Manwe.
    /// </summary>
    private async Task<List<AltarPick>> AltarsFor(TempleRoom room)
    {
        var list = new List<AltarPick>();
        foreach (var god in godSystem.GetActiveGods().Where(g => GodRegistry.IsCanon(g.Name)).OrderBy(g => g.Name))
        {
            var pick = new AltarPick(god.Name, god, null);
            if (room != TempleRoom.Undercroft || IsDarkAltar(pick)) list.Add(pick);
        }
        foreach (var ig in ImmortalGodsForTests ?? await GetImmortalGodsAsync())
        {
            if (string.IsNullOrWhiteSpace(ig.DivineName) || GodRegistry.IsCanon(ig.DivineName) || GodRegistry.IsManwe(ig.DivineName)) continue;
            var pick = new AltarPick(ig.DivineName, null, ig);
            if (room != TempleRoom.Undercroft || IsDarkAltar(pick)) list.Add(pick);
        }
        return list;
    }

    /// <summary>A god of the light: a canon god with more Goodness than Darkness, or a player-god of the Light.</summary>
    private static bool IsGoodAltar(AltarPick pick) =>
        pick.Canon != null ? pick.Canon.Goodness > pick.Canon.Darkness
                           : string.Equals(pick.PlayerGod?.GodAlignment, "Light", StringComparison.OrdinalIgnoreCase);

    /// <summary>A dark god: a canon god with more Darkness than Goodness, or a player-god of the Dark.</summary>
    private static bool IsDarkAltar(AltarPick pick) =>
        pick.Canon != null ? pick.Canon.Darkness > pick.Canon.Goodness
                           : string.Equals(pick.PlayerGod?.GodAlignment, "Dark", StringComparison.OrdinalIgnoreCase);

    private bool IsEvilPlayer() =>
        AlignmentSystem.Instance.GetAlignment(currentPlayer) == AlignmentSystem.AlignmentType.Evil;

    /// <summary>
    /// Evil players are unwelcome in the Nave: its priests refuse their offerings to a good god,
    /// except their own: an Evil follower of a good god may still offer to the god they worship.
    /// </summary>
    private bool NaveRefusesOffering(TempleRoom room, AltarPick pick)
    {
        if (room != TempleRoom.Nave || !IsEvilPlayer() || !IsGoodAltar(pick)) return false;
        var own = GodRegistry.GetWorshippedGod(currentPlayer, godSystem);
        bool isOwn = own is { } o && o.Name.Equals(pick.Name, StringComparison.OrdinalIgnoreCase);
        return !isOwn;
    }

    /// <summary>True when the character's own god has an altar in the Undercroft.</summary>
    private async Task<bool> OwnGodIsDark()
    {
        var own = GodRegistry.GetWorshippedGod(currentPlayer, godSystem);
        if (own == null) return false;
        return (await AltarsFor(TempleRoom.Undercroft)).Any(a => a.Name.Equals(own.Value.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Choose an altar of this room from the one god list by (part of) its name. Enter cancels.
    /// </summary>
    private async Task<AltarPick?> SelectAltar(string prompt, TempleRoom room, bool requireConfirmation)
    {
        var altars = await AltarsFor(room);
        if (altars.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_gods_available"), "red");
            await Pacing.Wait(1000);
            return null;
        }

        DisplayGodListCompact(altars.Where(a => a.Canon != null).Select(a => a.Canon!).ToList(), nameHint: false);
        DisplayPlayerGodAltars(altars.Where(a => a.PlayerGod != null).Select(a => a.PlayerGod!).ToList());
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.type_name_hint"));

        while (true)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.select_prompt", prompt), "white");
            string input = ((await terminal.GetInputAsync("> ")) ?? "").Trim();
            if (input.Length == 0) return null;

            var pick = altars.FirstOrDefault(a => a.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (pick == null)
            {
                var starts = altars.Where(a => a.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToList();
                var contains = altars.Where(a => a.Name.Contains(input, StringComparison.OrdinalIgnoreCase)).ToList();
                if (starts.Count == 1) pick = starts[0];
                else if (starts.Count == 0 && contains.Count == 1) pick = contains[0];
                else if (contains.Count == 0)
                {
                    terminal.WriteLine(Loc.Get("temple.no_god_match", input), "red");
                    continue;
                }
                else
                {
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("temple.multiple_matches"), "yellow");
                    foreach (var m in starts.Count > 0 ? starts : contains)
                        terminal.WriteLine($"  {m.Name}", "white");
                    terminal.WriteLine(Loc.Get("temple.be_more_specific"), "gray");
                    continue;
                }
            }

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.selected_altar", pick.Name), IsDarkAltar(pick) ? "dark_red" : IsGoodAltar(pick) ? "bright_cyan" : "yellow");
            if (requireConfirmation)
            {
                // v1.1.15: yesno-convert-a, strict (Y/N)
                if (!await terminal.AskYesNoAsync(Loc.Get("ui.confirm_choose", pick.Name)))
                {
                    terminal.WriteLine(Loc.Get("temple.selection_cancelled"), "gray");
                    continue;
                }
            }
            return pick;
        }
    }

    /// <summary>
    /// O: one offering at an altar of this room (the old C, I and $). Gold goes to any canon altar
    /// (with the old warning and wrath when it is not your god's) or to your own player-god; goods
    /// (weapon, armor, healing potions) go only to your own god. In the Nave, an Evil player's
    /// offering to a good god is refused.
    /// </summary>
    private async Task ProcessOffering(TempleRoom room)
    {
        terminal.WriteLine("");
        var pick = await SelectAltar(Loc.Get("temple.offer.prompt"), room, requireConfirmation: false);
        if (pick == null) return;

        if (NaveRefusesOffering(room, pick))
        {
            terminal.WriteLine("");
            Say(Loc.Get("temple.offer.refused_evil", pick.Name), "red");
            Say(Loc.Get("temple.offer.refused_evil_hint"), "gray");
            await terminal.PressAnyKey();
            return;
        }

        var own = GodRegistry.GetWorshippedGod(currentPlayer, godSystem);
        bool isOwn = own is { } o && o.Name.Equals(pick.Name, StringComparison.OrdinalIgnoreCase);
        if (pick.PlayerGod != null && !isOwn)
        {
            Say(Loc.Get("temple.offer.followers_only", pick.Name), "yellow");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        var kinds = new List<TempleMenuItem> { new("G", Loc.Get("temple.offer.gold"), "") };
        if (isOwn) kinds.Add(new TempleMenuItem("I", Loc.Get("temple.offer.goods"), ""));
        kinds.Add(new TempleMenuItem("R", Loc.Get("temple.room.back_short"), ""));
        WriteTempleMenu(kinds, bbsRows: false);

        string kind = ((await terminal.GetInputAsync(Loc.Get("ui.your_choice"))) ?? "").Trim().ToUpperInvariant();
        if (kind == "G")
        {
            if (pick.Canon != null) await ProcessContribute(pick.Canon);
            else await SacrificeToImmortalGod();
        }
        else if (kind == "I" && isOwn)
        {
            await ProcessItemSacrifice();
        }
        else if (kind != "R" && kind.Length > 0)
        {
            terminal.WriteLine(Loc.Get("temple.invalid_choice"), "red");
            await Pacing.Wait(1000);
        }
    }

    /// <summary>The Halls of Memory open when one of its halls applies.</summary>
    private bool HallsOfMemoryOpen() => CanShowHallOfTheFallen() || CanShowRiteOfReturn() || HasAscendedStatues() || AurelionResolved();

    /// <summary>The Hall of the Ascended shows when founder statues stand at the Pantheon.</summary>
    private static bool HasAscendedStatues() =>
        UsurperRemake.Data.FounderStatueData.GetStatuesAt(UsurperRemake.Data.FounderStatueData.StatueLocationTag.Pantheon).Any();

    /// <summary>
    /// V: the founder statues. Mortals can't enter the Pantheon, so the immortal-founder statues are
    /// mirrored here where the public venerates the gods. Same data as the Pantheon's [H] menu.
    /// </summary>
    private Task ShowAscendedStatues() =>
        UsurperRemake.Systems.FounderStatueSystem.ShowStatuesAt(
            UsurperRemake.Data.FounderStatueData.StatueLocationTag.Pantheon, terminal);

    /// <summary>True when the Cloister's meditation was used today (online by the reset boundary).</summary>
    private bool MeditatedInCloisterToday()
    {
        if (DoorMode.IsOnlineMode)
            return currentPlayer.LastInnerSanctumRealDate >= DailySystemManager.GetCurrentResetBoundary();
        return currentPlayer.InnerSanctumLastDay >= (DailySystemManager.Instance?.CurrentDay ?? 0);
    }

    /// <summary>A Favor tier's name.</summary>
    private static string TierName(GodFavorTier tier) => tier switch
    {
        GodFavorTier.Chosen => Loc.Get("temple.tier.chosen"),
        GodFavorTier.Zealot => Loc.Get("temple.tier.zealot"),
        GodFavorTier.Devout => Loc.Get("temple.tier.devout"),
        _ => Loc.Get("temple.tier.follower"),
    };

    /// <summary>A player-god's alignment (Light, Dark, Balance) in the player's language.</summary>
    private static string AlignmentName(string? alignment) => alignment?.Trim().ToLowerInvariant() switch
    {
        "light" => Loc.Get("temple.align.light"),
        "dark" => Loc.Get("temple.align.dark"),
        _ => Loc.Get("temple.align.balance"),
    };

    #endregion

    /// <summary>
    /// Phase 4: emit Temple menu state for the Electron client. Top-level
    /// menu only. Pattern B.
    /// </summary>
    /// <summary>
    /// v1.2.5: a god's side (Light, Dark or Neutral, from its Goodness and Darkness) in the player's language.
    /// Display only; nothing stores or compares it.
    /// </summary>
    internal static string GodAlignmentLabel(God god) =>
        god.Goodness > god.Darkness ? Loc.Get("temple.align.light")
        : god.Darkness > god.Goodness ? Loc.Get("temple.align.dark")
        : Loc.Get("ui.neutral");

    /// <summary>v1.2.5: to a player god's session, in that session's language: a mortal prayed.</summary>
    internal static string PrayedToYouMessage(string lang, string mortal, int exp) =>
        $"\u001b[1;33m  \u2726 {Loc.GetIn(lang, "temple.msg_prayed_to_you", mortal, exp)} \u2726\u001b[0m";

    /// <summary>v1.2.5: to a player god's session, in that session's language: a new worshipper.</summary>
    internal static string NewWorshipperMessage(string lang, string mortal) =>
        $"\u001b[1;33m  \u2726 {Loc.GetIn(lang, "temple.msg_new_worshipper", mortal)} \u2726\u001b[0m";

    /// <summary>v1.2.5: to a player god's session, in that session's language: gold sacrificed at the altar.</summary>
    internal static string GoldSacrificedMessage(string lang, string mortal, long amount, long power) =>
        $"\u001b[1;33m  \u2726 {Loc.GetIn(lang, "temple.msg_gold_sacrificed", mortal, $"{amount:N0}", power)} \u2726\u001b[0m";

    private void EmitElectronEvents()
    {
        var player = GetCurrentPlayer();
        if (player == null) return;

        ElectronBridge.EmitLocation(
            name: Loc.Get("temple.header_visual"),
            description: Loc.Get("temple.description_line1"),
            timeOfDay: "");

        bool isManaClass = player is Player p && p.IsManaClass;
        ElectronBridge.EmitStats(
            hp: player.HP, maxHp: player.MaxHP,
            mana: isManaClass ? player.Mana : 0, maxMana: isManaClass ? player.MaxMana : 0,
            stamina: isManaClass ? 0 : player.Stamina, maxStamina: isManaClass ? 0 : player.BaseStamina,
            gold: player.Gold, level: player.Level,
            className: player.ClassName, raceName: player.Race.ToString(),
            playerName: player.DisplayName);

        var menu = new List<ElectronBridge.MenuItemData>
        {
            new() { Key = GameConfig.TempleMenuWorship, Label = Loc.Get("temple.electron_worship"), Category = "faith", Icon = "worship" },
            new() { Key = GameConfig.TempleMenuAltars, Label = Loc.Get("temple.electron_altars"), Category = "info", Icon = "altar" },
            new() { Key = GameConfig.TempleMenuContribute, Label = Loc.Get("temple.electron_contribute"), Category = "faith", Icon = "donate" },
            new() { Key = GameConfig.TempleMenuDesecrate, Label = Loc.Get("temple.electron_desecrate"), Category = "evil", Icon = "desecrate" },
            new() { Key = "F", Label = Loc.Get("temple.room.faith"), Category = "info", Icon = "scripture" },
            new() { Key = "O", Label = Loc.Get("church.bbs_confess"), Category = "faith", Icon = "confess" },
            new() { Key = "M", Label = Loc.Get("temple.electron_mira"), Category = "social", Icon = "bishop" },
            new() { Key = "S", Label = Loc.Get("menu.action.status"), Category = "info", Icon = "info" },
            new() { Key = "R", Label = Loc.Get("ui.return"), Category = "navigate", Icon = "back" },
        };
        ElectronBridge.EmitMenu(menu);

        EmitNPCsInLocationToElectron();
    }
}
