using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Main Street location - central hub of the game
/// Based on Pascal main_menu procedure from GAMEC.PAS
/// </summary>
public class MainStreetLocation : BaseLocation
{
    public MainStreetLocation() : base(
        GameLocation.MainStreet,
        "Main Street",
        "You are standing on the main street of town. The bustling center of all activity."
    ) { }
    
    protected override void SetupLocation()
    {
        // Pascal-compatible exits from ONLINE.PAS onloc_mainstreet case
        PossibleExits = new List<GameLocation>
        {
            GameLocation.TheInn,       // loc1
            GameLocation.Church,       // loc2  
            GameLocation.Darkness,     // loc3
            GameLocation.Master,       // loc4
            GameLocation.MagicShop,    // loc5
            GameLocation.Dungeons,     // loc6
            GameLocation.WeaponShop,   // loc7
            GameLocation.ArmorShop,    // loc8
            GameLocation.Bank,         // loc9
            GameLocation.AuctionHouse,  // loc10
            GameLocation.DarkAlley,    // loc11
            GameLocation.ReportRoom,   // loc12
            GameLocation.Healer,       // loc13
            GameLocation.AnchorRoad,   // loc14
            GameLocation.Home,         // loc15
            GameLocation.TeamCorner    // loc16
        };
        
        // Location actions based on Pascal main menu
        LocationActions = new List<string>
        {
            "Status",              // (S)tatus
            "Good Deeds",          // (G)ood Deeds
            "Wilderness",          // (E)xplore the Wilderness
            "News",                // (N)ews
            "World Events",        // ($) World Events
            "List Characters",     // (L)ist Characters
            "Fame",                // (F)ame
            // "Relations",        // Removed — redundant with Status screen
            "Inventory"            // (*) Inventory
        };
    }

    protected override string GetMudPromptName() => "Main Street";

    /// <summary>
    /// Returns the menu disclosure tier based on player level.
    /// Tier 1 (Level 1-2): Core combat loop only
    /// Tier 2 (Level 3-4): Town services added
    /// Tier 3 (Level 5+): Full menu
    /// </summary>
    private int GetMenuTier()
    {
        if (currentPlayer == null) return 3;
        if (currentPlayer.Level >= GameConfig.MenuTier3Level) return 3;
        if (currentPlayer.Level >= GameConfig.MenuTier2Level) return 2;
        return 1;
    }

    protected override string[]? GetAmbientMessages() => new[]
    {
        Loc.Get("main_street.ambient_merchant"),
        Loc.Get("main_street.ambient_crowd"),
        Loc.Get("main_street.ambient_bell"),
        Loc.Get("main_street.ambient_cart"),
        Loc.Get("main_street.ambient_bread"),
        Loc.Get("main_street.ambient_dog"),
        Loc.Get("main_street.ambient_guards"),
    };

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();

        if (IsBBSSession)
        {
            DisplayLocationBBS();
            return;
        }

        // ASCII art header (simplified version)
        WriteBoxHeader(Loc.Get("main_street.header"), "bright_blue");
        terminal.WriteLine("");

        // Online status bar (only in online mode)
        if (DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            int onlineCount = OnlineStateManager.Instance.CachedOnlinePlayerCount;
            terminal.SetColor("darkgray");
            terminal.Write($" {Loc.Get("main_street.players_online")}: ");
            terminal.SetColor("bright_green");
            terminal.Write($"{onlineCount}");
            terminal.SetColor("darkgray");
            terminal.Write("  |  ");
            terminal.SetColor("cyan");
            terminal.Write("/say");
            terminal.SetColor("darkgray");
            terminal.Write($" {Loc.Get("main_street.to_chat")}  |  ");
            terminal.SetColor("cyan");
            terminal.Write("/who");
            terminal.SetColor("darkgray");
            terminal.WriteLine($" {Loc.Get("main_street.for_player_list")}");
        }

        // Location description with time/weather
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("main_street.desc_standing", GetTownName()));
        terminal.WriteLine(Loc.Get("main_street.desc_air", GetTimeOfDay(), GetWeather()));
        terminal.WriteLine("");

        // Show NPCs in location
        ShowNPCsInLocation();

        // Main Street menu (Pascal-style layout)
        ShowMainStreetMenu();

        // Check for level eligibility message
        ShowLevelEligibilityMessage();

        // Status line
        ShowStatusLine();

        // Electron graphical client — emit structured game state
        EmitElectronEvents();

        // Captain Aldric's Mission — quest completion when returning with all objectives done
        if (currentPlayer.HintsShown.Contains("aldric_quest_active")
            && currentPlayer.HintsShown.Contains("quest_scout_enter_dungeon")
            && currentPlayer.HintsShown.Contains("quest_scout_kill_monster")
            && currentPlayer.HintsShown.Contains("quest_scout_find_treasure")
            && !currentPlayer.HintsShown.Contains("quest_scout_return"))
        {
            currentPlayer.HintsShown.Add("quest_scout_return");
            currentPlayer.HintsShown.Remove("aldric_quest_active");

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.completion_approach")}");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.completion_made_it")}");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.completion_scouts")}");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.completion_review")}");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.completion_good_work")}");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.completion_handle")}");
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.quest_complete")}");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.reward_gold", 500)}");
            currentPlayer.Gold += 500;
            terminal.SetColor("white");
            terminal.WriteLine("");

            AchievementSystem.TryUnlock(currentPlayer, "first_steps");
        }

        // Show dungeon guidance for brand-new players, or generic navigation hint otherwise
        if (currentPlayer.Level == 1 && currentPlayer.MKills == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.Write("  >>> ");
            terminal.SetColor("bright_white");
            terminal.Write(Loc.Get("main_street.new_adventurer_prefix"));
            terminal.SetColor("bright_yellow");
            terminal.Write(Loc.Get("main_street.new_adventurer_key"));
            terminal.SetColor("bright_white");
            terminal.Write(Loc.Get("main_street.new_adventurer_suffix"));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(" <<<");
            terminal.SetColor("white");
        }
        else
        {
            HintSystem.Instance.TryShowHint(HintSystem.HINT_MAIN_STREET_NAVIGATION, terminal, currentPlayer.HintsShown);
        }

        // Show low HP hint if player is below 25% health
        if (currentPlayer.HP < currentPlayer.MaxHP * 0.25)
        {
            HintSystem.Instance.TryShowHint(HintSystem.HINT_LOW_HP, terminal, currentPlayer.HintsShown);
        }

        // Show mana/spells hint if player's class has spells but none learned yet
        // Exclude Mystic Shaman — they use mana for abilities, not spells, and can't learn spells at the Magic Shop
        if (currentPlayer.MaxMana > 0 && currentPlayer.Class != CharacterClass.MysticShaman
            && SpellSystem.GetAvailableSpells(currentPlayer).Count == 0)
        {
            HintSystem.Instance.TryShowHint(HintSystem.HINT_MANA_SPELLS, terminal, currentPlayer.HintsShown);
        }

        // Show quest system hint for early players who have started fighting
        if (currentPlayer.Level <= 3 && currentPlayer.Statistics.TotalMonstersKilled >= 1)
        {
            HintSystem.Instance.TryShowHint(HintSystem.HINT_QUEST_SYSTEM, terminal, currentPlayer.HintsShown);
        }

        // NPC story notification — hint about NPCs with available story content (v0.49.3)
        string? npcNotification = null;
        if (currentPlayer.Level >= 3)
        {
            npcNotification = TownNPCStorySystem.Instance?.GetNextNotification(currentPlayer);
            if (npcNotification != null)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine(npcNotification);
                terminal.SetColor("white");
            }
        }

        // Street micro-events — show real NPC activity from world simulation (v0.49.6)
        if (npcNotification == null)
        {
            var microEvent = GenerateStreetMicroEvent();
            if (microEvent != null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {microEvent}");
                terminal.SetColor("white");
            }
        }

        // Companion teasers — one-time early sightings before recruitment level (v0.49.6)
        if (currentPlayer.Level >= 4 && CompanionSystem.Instance != null
            && !(CompanionSystem.Instance.GetCompanion(CompanionId.Vex)?.IsRecruited ?? true)
            && !(CompanionSystem.Instance.GetCompanion(CompanionId.Vex)?.IsDead ?? true)
            && !currentPlayer.HintsShown.Contains(HintSystem.HINT_COMPANION_VEX_TEASER))
        {
            currentPlayer.HintsShown.Add(HintSystem.HINT_COMPANION_VEX_TEASER);
            terminal.SetColor("dark_yellow");
            terminal.WriteLine(Loc.Get("main_street.vex_teaser1"));
            terminal.WriteLine(Loc.Get("main_street.vex_teaser2"));
            terminal.SetColor("white");
        }
        else if (currentPlayer.Level >= 5 && CompanionSystem.Instance != null
            && !(CompanionSystem.Instance.GetCompanion(CompanionId.Lyris)?.IsRecruited ?? true)
            && !(CompanionSystem.Instance.GetCompanion(CompanionId.Lyris)?.IsDead ?? true)
            && !currentPlayer.HintsShown.Contains(HintSystem.HINT_COMPANION_LYRIS_TEASER))
        {
            currentPlayer.HintsShown.Add(HintSystem.HINT_COMPANION_LYRIS_TEASER);
            terminal.SetColor("dark_yellow");
            terminal.WriteLine(Loc.Get("main_street.lyris_teaser1"));
            terminal.WriteLine(Loc.Get("main_street.lyris_teaser2"));
            terminal.SetColor("white");
        }

        // God Slayer buff reminder
        if (currentPlayer.HasGodSlayerBuff)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("main_street.god_slayer_buff", currentPlayer.GodSlayerCombats));
            terminal.SetColor("white");
        }
    }

    /// <summary>
    /// Compact Main Street display for BBS 80x25 terminals.
    /// Fits header, description, NPCs, menu, status, and prompt within 25 rows.
    /// </summary>
    private void DisplayLocationBBS()
    {
        // Line 1: Header
        if (IsScreenReader)
        {
            terminal.WriteLine(Loc.Get("main_street.bbs_main_street"), "bright_white");
        }
        else
        {
            terminal.SetColor("bright_blue");
            terminal.Write("╔════════════════════════════════ ");
            terminal.SetColor("bright_white");
            terminal.Write(Loc.Get("main_street.bbs_main_street"));
            terminal.SetColor("bright_blue");
            terminal.WriteLine(" ════════════════════════════════╗");
        }

        // Line 2: Description (one line)
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("main_street.bbs_desc", GetTimeOfDay(), GetTownName(), GetWeather()));

        // Line 3: NPCs (compressed to one line)
        var liveNPCs = GetLiveNPCsAtLocation();
        if (liveNPCs.Count > 0)
        {
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("main_street.you_notice"));
            terminal.SetColor("cyan");
            var names = liveNPCs.Take(2).Select(n => n.Name2).ToList();
            terminal.Write(string.Join(", ", names));
            if (liveNPCs.Count > 2)
            {
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("main_street.and_others", liveNPCs.Count - 2, liveNPCs.Count - 2 == 1 ? "" : "s"));
            }
            terminal.WriteLine("");
        }

        // Line 4: blank
        terminal.WriteLine("");

        // Captain Aldric's Mission — quest completion (BBS path)
        if (currentPlayer.HintsShown.Contains("aldric_quest_active")
            && currentPlayer.HintsShown.Contains("quest_scout_enter_dungeon")
            && currentPlayer.HintsShown.Contains("quest_scout_kill_monster")
            && currentPlayer.HintsShown.Contains("quest_scout_find_treasure")
            && !currentPlayer.HintsShown.Contains("quest_scout_return"))
        {
            currentPlayer.HintsShown.Add("quest_scout_return");
            currentPlayer.HintsShown.Remove("aldric_quest_active");

            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.completion_approach")}");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.completion_bbs")}");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.quest_complete")}");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.reward_gold", 500)}");
            currentPlayer.Gold += 500;
            terminal.WriteLine("");

            AchievementSystem.TryUnlock(currentPlayer, "first_steps");
        }

        // Menu rows — progressive disclosure based on player level
        int tier = GetMenuTier();

        // Tier 1 (always): Core combat loop
        terminal.SetColor("darkgray");
        terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("D"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_dungeons_suffix"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("W"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_weapon_suffix"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("A"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_armor_suffix"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("M"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_magic_suffix"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("U"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("cyan"); terminal.WriteLine(Loc.Get("menu.action.music_shop"));

        terminal.SetColor("darkgray");
        terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("I"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_inn_suffix"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("1"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_healer"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("2"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_quest_hall"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("V"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.WriteLine(Loc.Get("main_street.menu_master"));

        // Tier 2 (Level 3+): Town services
        if (tier >= 2)
        {
            terminal.SetColor("darkgray");
            terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("B"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_bank_suffix"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("T"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_temple_suffix"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("K"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_castle"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("H"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.WriteLine(Loc.Get("main_street.menu_home_suffix"));

            terminal.SetColor("darkgray");
            terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("N"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_news_suffix"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("F"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_fame_suffix"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("E"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("bright_green"); terminal.WriteLine(Loc.Get("main_street.menu_explore_suffix"));
        }

        // Tier 3 (Level 5+): Full menu
        if (tier >= 3)
        {
            terminal.SetColor("darkgray");
            terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("Y"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("gray"); terminal.Write(Loc.Get("main_street.menu_dark_alley"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("X"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("magenta"); terminal.Write(Loc.Get("main_street.menu_love_st"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("O"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_church_suffix"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("J"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.WriteLine(Loc.Get("main_street.menu_auction"));

            terminal.SetColor("darkgray");
            terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("C"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_challenges_suffix"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("L"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_lodging_suffix"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("="); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_stats"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("P"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.WriteLine(Loc.Get("main_street.menu_progress_suffix"));

            terminal.SetColor("darkgray");
            terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("Z"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_team_corner"));
            if (UsurperRemake.Systems.SettlementSystem.Instance?.State.IsEstablished == true)
            {
                terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write(">"); terminal.SetColor("darkgray"); terminal.Write("]");
                terminal.SetColor("bright_green"); terminal.Write(Loc.Get("main_street.menu_outskirts"));
            }
            terminal.WriteLine("");
        }

        // Always: Quit + Settings
        terminal.SetColor("darkgray");
        if (tier < 3) terminal.Write(" "); // indent if not continuing a row
        terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("Q"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("gray"); terminal.Write(Loc.Get("main_street.menu_quit_suffix"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("~"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("gray"); terminal.WriteLine(Loc.Get("main_street.menu_settings"));

        // Compact mode number-key hint (offline only — online mode has its own number row)
        if (GameConfig.CompactMode && !DoorMode.IsOnlineMode)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("main_street.numpad_hint"));
        }

        // Online multiplayer row (only in online mode)
        if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
        {
            int onlineCount = OnlineStateManager.Instance?.CachedOnlinePlayerCount ?? 0;
            if (IsScreenReader)
            {
                terminal.WriteLine(Loc.Get("main_street.online_label"), "bright_green");
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.Write(Loc.Get("main_street.online_separator"));
            }
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("3"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.who_count", onlineCount));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("4"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_chat_short"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("5"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_news_short"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("6"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_arena_short"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("7"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_boss_short"));
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("R"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.WriteLine(Loc.Get("menu.action.guilds"));
        }

        // Blank line
        terminal.WriteLine("");

        // Training points reminder (level-ups are now automatic)
        if (currentPlayer.TrainingPoints > 0)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write(" >>> ");
            terminal.SetColor("bright_green");
            terminal.Write(Loc.Get("main_street.training_points", currentPlayer.TrainingPoints));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(" <<<");
        }

        // New player dungeon guidance (Level 1 with zero kills)
        if (currentPlayer.Level == 1 && currentPlayer.MKills == 0)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write(" >>> ");
            terminal.SetColor("bright_white");
            terminal.Write(Loc.Get("main_street.new_adventurer_prefix"));
            terminal.SetColor("bright_yellow");
            terminal.Write(Loc.Get("main_street.new_adventurer_key"));
            terminal.SetColor("bright_white");
            terminal.Write(Loc.Get("main_street.new_adventurer_suffix"));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(" <<<");
        }

        // Line 14: Status line (compact)
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("main_street.status_hp"));
        float hpPct = currentPlayer.MaxHP > 0 ? (float)currentPlayer.HP / currentPlayer.MaxHP : 0;
        terminal.SetColor(hpPct > 0.5f ? "bright_green" : hpPct > 0.25f ? "yellow" : "bright_red");
        terminal.Write($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("main_street.status_gold"));
        terminal.SetColor("yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");
        if (currentPlayer.MaxMana > 0)
        {
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("main_street.status_mana"));
            terminal.SetColor("blue");
            terminal.Write($"{currentPlayer.Mana}/{currentPlayer.MaxMana}");
        }
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("main_street.status_lv"));
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Level}");
        if (currentPlayer.Level < GameConfig.MaxLevel)
        {
            long curXP = currentPlayer.Experience;
            long nextXP = GetExperienceForLevel(currentPlayer.Level + 1);
            long prevXP = GetExperienceForLevel(currentPlayer.Level);
            long xpInto = curXP - prevXP;
            long xpNeed = nextXP - prevXP;
            int pct = xpNeed > 0 ? (int)((xpInto * 100) / xpNeed) : 0;
            pct = Math.Clamp(pct, 0, 100);
            terminal.SetColor("gray");
            terminal.Write($"({pct}%)");
        }
        terminal.WriteLine("");

        // Line 15: Quick commands (compact)
        terminal.SetColor("darkgray");
        terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("S"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_status_suffix"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("*"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_inv"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("?"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_help_short"));
        if (liveNPCs.Count > 0)
        {
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("0"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.talk_count", liveNPCs.Count));
        }
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("~"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_prefs"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("!"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_bug"));
        terminal.WriteLine("");

        // Line 16: Bottom border
        if (!IsScreenReader)
        {
            terminal.SetColor("bright_blue");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        }

        // Electron graphical client — emit structured game state
        EmitElectronEvents();
    }

    /// <summary>
    /// Emit structured JSON events for the Electron graphical client
    /// </summary>
    private void EmitElectronEvents()
    {
        if (!GameConfig.ElectronMode) return;

        // Location
        ElectronBridge.EmitLocation("Main Street",
            Loc.Get("main_street.desc_standing", GetTownName()),
            GetTimeOfDay());

        // Player stats
        bool isManaClass = currentPlayer.IsManaClass;
        ElectronBridge.EmitStats(
            currentPlayer.HP, currentPlayer.MaxHP,
            isManaClass ? currentPlayer.Mana : 0,
            isManaClass ? currentPlayer.MaxMana : 0,
            isManaClass ? 0 : currentPlayer.Stamina,
            isManaClass ? 0 : currentPlayer.BaseStamina,
            currentPlayer.Gold, currentPlayer.Level,
            currentPlayer.ClassName, currentPlayer.Race.ToString(),
            currentPlayer.DisplayName);

        // Menu items
        int tier = currentPlayer.Level <= 2 ? 1 : currentPlayer.Level <= 4 ? 2 : 3;
        var menuItems = new List<ElectronBridge.MenuItemData>
        {
            new() { Key = "D", Label = "Dungeons", Category = "explore", Icon = "dungeon" },
        };
        if (tier >= 2)
        {
            menuItems.Add(new() { Key = "I", Label = "Inn", Category = "services", Icon = "inn" });
            menuItems.Add(new() { Key = "W", Label = "Weapon Shop", Category = "services", Icon = "weapons" });
            menuItems.Add(new() { Key = "A", Label = "Armor Shop", Category = "services", Icon = "armor" });
            menuItems.Add(new() { Key = "M", Label = "Magic Shop", Category = "services", Icon = "magic" });
            menuItems.Add(new() { Key = "B", Label = "Bank", Category = "services", Icon = "bank" });
            menuItems.Add(new() { Key = "1", Label = "Healer", Category = "services", Icon = "healer" });
            menuItems.Add(new() { Key = "T", Label = "Temple", Category = "services", Icon = "temple" });
        }
        if (tier >= 3)
        {
            menuItems.Add(new() { Key = "E", Label = "Wilderness", Category = "explore", Icon = "wilderness" });
            menuItems.Add(new() { Key = ">", Label = "Outskirts", Category = "explore", Icon = "outskirts" });
            menuItems.Add(new() { Key = "U", Label = "Music Shop", Category = "services", Icon = "music" });
            menuItems.Add(new() { Key = "V", Label = "Level Master", Category = "progress", Icon = "training" });
            menuItems.Add(new() { Key = "2", Label = "Quest Hall", Category = "progress", Icon = "quests" });
            menuItems.Add(new() { Key = "H", Label = "Home", Category = "progress", Icon = "home" });
        }
        menuItems.Add(new() { Key = "S", Label = "Status", Category = "info", Icon = "status" });
        menuItems.Add(new() { Key = "Q", Label = "Quit Game", Category = "info", Icon = "quit" });
        ElectronBridge.EmitMenu(menuItems);

        // NPCs present
        var liveNPCs = GetLiveNPCsAtLocation();
        var npcData = new List<ElectronBridge.NPCPresenceData>();
        foreach (var npc in liveNPCs.Take(10))
        {
            npcData.Add(new ElectronBridge.NPCPresenceData
            {
                Name = npc.Name2 ?? npc.Name1,
                Activity = GetNPCActivity(npc),
                Class = npc.Class.ToString(),
                Level = npc.Level
            });
        }
        ElectronBridge.EmitNPCList(npcData);
    }

    private string GetNPCActivity(NPC npc)
    {
        return "going about their business";
    }

    /// <summary>
    /// Shows a message if the player is eligible for a level raise
    /// </summary>
    private void ShowLevelEligibilityMessage()
    {
        if (currentPlayer.Level >= GameConfig.MaxLevel)
            return;

        long experienceNeeded = GetExperienceForLevel(currentPlayer.Level + 1);

        if (currentPlayer.Experience >= experienceNeeded)
        {
            terminal.WriteLine("");
            if (IsScreenReader)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("main_street.level_eligible_sr"));
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                terminal.SetColor("bright_green");
                terminal.WriteLine($"║{Loc.Get("main_street.level_eligible"),78}║");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            }
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Experience required to have the specified level (cumulative)
    /// </summary>
    private static long GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        long exp = 0;
        for (int i = 2; i <= level; i++)
        {
            // Softened curve: level^2.0 * 50 (rebalanced v0.41.4)
            exp += (long)(Math.Pow(i, 2.0) * 50);
        }
        return exp;
    }

    /// <summary>
    /// Show the Main Street menu - routes to appropriate style based on accessibility settings
    /// </summary>
    private void ShowMainStreetMenu()
    {
        if (currentPlayer.ScreenReaderMode)
        {
            ShowScreenReaderMenu();
        }
        else
        {
            ShowClassicMenu();
        }
    }

    /// <summary>
    /// Show the classic Main Street menu layout (v0.4 style)
    /// Progressive disclosure: Tier 1 (Lv1-2) core loop, Tier 2 (Lv3-4) town services, Tier 3 (Lv5+) full menu.
    /// All keys still work at all levels — only the display is gated.
    /// </summary>
    private void ShowClassicMenu()
    {
        int tier = GetMenuTier();
        terminal.WriteLine("");

        // Helper: write a colored menu key+label, padded to fixed column width
        // Format: [K] Label padded to `col` total chars (4 for "[X] " + label)
        void MI(string key, string label, string color, int col)
        {
            terminal.SetColor("darkgray"); terminal.Write("[");
            terminal.SetColor("bright_yellow"); terminal.Write(key);
            terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor(color); terminal.Write(label.PadRight(col - 3));
        }
        // Last item in a row (no padding)
        void ML(string key, string label, string color)
        {
            terminal.SetColor("darkgray"); terminal.Write("[");
            terminal.SetColor("bright_yellow"); terminal.Write(key);
            terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor(color); terminal.WriteLine(label);
        }

        // Fixed column width: 16 chars per item keeps columns aligned across rows
        // 5 items × 16 = 80 chars (full terminal width)
        const int C = 16;

        // Row 1 - Primary locations (D/I always, T/O tier 2+)
        terminal.Write(" ");
        if (tier >= 2)
        {
            MI("D", Loc.Get("menu.action.dungeon"), "white", C);
            MI("I", Loc.Get("menu.action.inn"), "white", C);
            MI("T", Loc.Get("menu.action.temple"), "white", C);
            ML("O", Loc.Get("menu.action.old_church"), "white");
        }
        else
        {
            MI("D", Loc.Get("menu.action.dungeon"), "white", C);
            ML("I", Loc.Get("menu.action.inn"), "white");
        }

        // Row 2 - Shops (W/A/M/U always, J tier 3+)
        terminal.Write(" ");
        if (tier >= 3)
        {
            MI("W", Loc.Get("menu.action.weapon_shop"), "white", C);
            MI("A", Loc.Get("menu.action.armor_shop"), "white", C);
            MI("M", Loc.Get("menu.action.magic_shop"), "white", C);
            MI("U", Loc.Get("menu.action.music_shop"), "cyan", C);
            ML("J", Loc.Get("menu.action.auction_house"), "white");
        }
        else
        {
            MI("W", Loc.Get("menu.action.weapon_shop"), "white", C);
            MI("A", Loc.Get("menu.action.armor_shop"), "white", C);
            MI("M", Loc.Get("menu.action.magic_shop"), "white", C);
            ML("U", Loc.Get("menu.action.music_shop"), "cyan");
        }

        // Row 3 - Services (B tier 2+, 1/2/V always)
        terminal.Write(" ");
        if (tier >= 2)
        {
            MI("B", Loc.Get("menu.action.bank"), "white", C);
            MI("1", Loc.Get("menu.action.healer"), "white", C);
            MI("2", Loc.Get("menu.action.quest_hall"), "white", C);
            ML("V", Loc.Get("menu.action.level_master"), "white");
        }
        else
        {
            MI("1", Loc.Get("menu.action.healer"), "white", C);
            MI("2", Loc.Get("menu.action.quest_hall"), "white", C);
            ML("V", Loc.Get("menu.action.level_master"), "white");
        }

        // Row 4 - Important locations (K/H tier 2+, C/L/Z tier 3+)
        if (tier >= 2)
        {
            terminal.Write(" ");
            if (tier >= 3)
            {
                MI("K", Loc.Get("menu.action.castle"), "white", C);
                MI("H", Loc.Get("menu.action.home"), "white", C);
                MI("C", Loc.Get("menu.action.challenges"), "white", C);
                MI("L", Loc.Get("menu.action.lodging_short"), "white", C);
                ML("Z", Loc.Get("menu.action.team_corner"), "white");
            }
            else
            {
                MI("K", Loc.Get("menu.action.castle"), "white", C);
                ML("H", Loc.Get("menu.action.home"), "white");
            }
        }

        terminal.WriteLine("");

        // Row 5 - Information (S always, N/F/E tier 2+)
        terminal.Write(" ");
        if (tier >= 2)
        {
            MI("S", Loc.Get("menu.action.status"), "white", C);
            MI("N", Loc.Get("menu.action.news"), "white", C);
            MI("F", Loc.Get("menu.action.fame"), "white", C);
            ML("E", Loc.Get("menu.action.explore"), "bright_green");
        }
        else
        {
            ML("S", Loc.Get("menu.action.status"), "white");
        }

        // Row 6 - Stats & Progress (tier 3+)
        if (tier >= 3)
        {
            terminal.Write(" ");
            if (UsurperRemake.Systems.SettlementSystem.Instance?.State.IsEstablished == true)
            {
                MI("=", Loc.Get("menu.action.stats_record"), "white", C);
                MI("P", Loc.Get("menu.action.progress"), "white", C);
                ML(">", Loc.Get("menu.action.settlement"), "bright_green");
            }
            else
            {
                MI("=", Loc.Get("menu.action.stats_record"), "white", C);
                ML("P", Loc.Get("menu.action.progress"), "white");
            }
        }

        // Row 7 - Shady areas + Quit (Y/X tier 3+, Q/~ always)
        terminal.Write(" ");
        if (tier >= 3)
        {
            MI("Y", Loc.Get("menu.action.dark_alley"), "gray", C);
            MI("X", Loc.Get("menu.action.love_street"), "magenta", C);
            MI("Q", Loc.Get("menu.action.quit_game"), "gray", C);
            ML("~", Loc.Get("menu.action.settings"), "gray");
        }
        else
        {
            MI("Q", Loc.Get("menu.action.quit_game"), "gray", C);
            ML("~", Loc.Get("menu.action.settings"), "gray");
        }

        // Online multiplayer section (only shown in online mode)
        if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
        {
            terminal.WriteLine("");
            if (IsScreenReader)
            {
                terminal.WriteLine(Loc.Get("main_street.online_label"), "bright_white");
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.Write(" ═══ ");
                terminal.SetColor("bright_white");
                terminal.Write(Loc.Get("main_street.online_header"));
                terminal.SetColor("bright_green");
                terminal.WriteLine(" ═══");
            }

            // Show online player count
            int onlineCount = OnlineStateManager.Instance?.CachedOnlinePlayerCount ?? 0;

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("main_street.whos_online_label"));
            terminal.SetColor("bright_green");
            terminal.Write($"({onlineCount}");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("main_street.player_count", onlineCount != 1 ? Loc.Get("main_street.player_plural") : ""));

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("main_street.menu_chat_padded"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("5");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("main_street.menu_news_padded"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("6");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("main_street.menu_arena_padded"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("7");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("main_street.world_boss"));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("menu.action.guilds"));
        }

        terminal.WriteLine("");
        if (!IsScreenReader)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        }
        terminal.SetColor("white");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Show simplified menu for screen readers - plain text, one option per line
    /// </summary>
    private void ShowScreenReaderMenu()
    {
        int tier = GetMenuTier();
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("main_street.menu_title"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("main_street.section_locations"));
        terminal.WriteLine($"  D - {Loc.Get("menu.action.dungeon")}");
        terminal.WriteLine($"  I - {Loc.Get("menu.action.inn")}");
        if (tier >= 2)
        {
            terminal.WriteLine($"  T - {Loc.Get("menu.action.temple")}");
            terminal.WriteLine($"  K - {Loc.Get("menu.action.castle")}");
            terminal.WriteLine($"  H - {Loc.Get("menu.action.home")}");
        }
        if (tier >= 3)
        {
            terminal.WriteLine($"  O - {Loc.Get("menu.action.church")}");
            terminal.WriteLine($"  L - {Loc.Get("menu.action.lodging")}");
        }
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("main_street.section_shops"));
        terminal.WriteLine($"  W - {Loc.Get("menu.action.weapon_shop")}");
        terminal.WriteLine($"  A - {Loc.Get("menu.action.armor_shop")}");
        terminal.WriteLine($"  M - {Loc.Get("menu.action.magic_shop")}");
        terminal.WriteLine($"  U - {Loc.Get("menu.action.music_shop")}");
        if (tier >= 3) terminal.WriteLine($"  J - {Loc.Get("menu.action.auction_house")}");
        if (tier >= 2) terminal.WriteLine($"  B - {Loc.Get("menu.action.bank")}");
        terminal.WriteLine($"  1 - {Loc.Get("menu.action.healer")}");
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("main_street.section_services"));
        terminal.WriteLine($"  V - {Loc.Get("menu.action.level_master")}");
        terminal.WriteLine($"  2 - {Loc.Get("menu.action.quest_hall")}");
        if (tier >= 3) terminal.WriteLine($"  C - {Loc.Get("menu.action.challenges")}");
        if (tier >= 3) terminal.WriteLine($"  Z - {Loc.Get("menu.action.team_corner")}");
        terminal.WriteLine("");

        if (tier >= 2)
        {
            terminal.WriteLine(Loc.Get("main_street.section_info"));
            terminal.WriteLine($"  S - {Loc.Get("menu.action.status")}");
            terminal.WriteLine($"  N - {Loc.Get("menu.action.news")}");
            terminal.WriteLine($"  F - {Loc.Get("menu.action.fame")}");
            if (tier >= 3)
            {
                terminal.WriteLine($"  = - {Loc.Get("menu.action.stats_record")}");
                terminal.WriteLine($"  P - {Loc.Get("menu.action.progress")}");
            }
            terminal.WriteLine("");
        }
        else
        {
            terminal.WriteLine($"  S - {Loc.Get("menu.action.status")}");
            terminal.WriteLine("");
        }

        if (tier >= 2)
        {
            terminal.WriteLine(Loc.Get("main_street.section_exploration_label"));
            terminal.WriteLine($"  E - {Loc.Get("menu.action.wilderness")}");
            if (UsurperRemake.Systems.SettlementSystem.Instance?.State.IsEstablished == true)
                terminal.WriteLine($"  > - {Loc.Get("menu.action.settlement")}");
            terminal.WriteLine("");
        }

        terminal.WriteLine(Loc.Get("main_street.section_other"));
        if (tier >= 3)
        {
            terminal.WriteLine($"  Y - {Loc.Get("menu.action.dark_alley")}");
            terminal.WriteLine($"  X - {Loc.Get("menu.action.love_street")}");
        }
        terminal.WriteLine($"  Q - {Loc.Get("menu.action.quit")}");
        terminal.WriteLine($"  ? - {Loc.Get("menu.action.help")}");
        terminal.WriteLine($"  ! - {Loc.Get("menu.action.report_bug")}");
        terminal.WriteLine("");

        if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
        {
            terminal.WriteLine(Loc.Get("main_street.online_label"));
            terminal.WriteLine($"  3 - {Loc.Get("main_street.whos_online")}");
            terminal.WriteLine($"  4 - {Loc.Get("main_street.chat")}");
            terminal.WriteLine($"  5 - {Loc.Get("main_street.news_feed")}");
            terminal.WriteLine($"  6 - {Loc.Get("main_street.arena_pvp")}");
            terminal.WriteLine($"  7 - {Loc.Get("main_street.world_boss")}");
            terminal.WriteLine($"  R - {Loc.Get("menu.action.guilds")}");
            terminal.WriteLine($"  /say message - {Loc.Get("main_street.broadcast_chat")}");
            terminal.WriteLine($"  /tell player message - {Loc.Get("main_street.private_message")}");
            terminal.WriteLine($"  /who - {Loc.Get("main_street.see_online")}");
            terminal.WriteLine($"  /news - {Loc.Get("main_street.recent_news")}");
            terminal.WriteLine("");
        }
    }
    
    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        // Compact mode: map number keys to common locations for touch-friendly input
        // Only in offline mode — online mode already uses 3-7 for online features
        if (GameConfig.CompactMode && !DoorMode.IsOnlineMode)
        {
            upperChoice = upperChoice switch
            {
                "3" => "W",  // Weapon Shop
                "4" => "A",  // Armor Shop
                "5" => "T",  // Temple
                "6" => "K",  // Castle
                "7" => "H",  // Home
                "8" => "V",  // Level Master
                "0" => "Q",  // Quit
                _ => upperChoice
            };
        }

        // Handle Main Street specific commands
        switch (upperChoice)
        {
            case "S":
                await ShowStatus();
                return false;
                
            case "D":
                await NavigateToLocation(GameLocation.Dungeons);
                return true;
                
            case "B":
                await NavigateToLocation(GameLocation.Bank);
                return true;
                
            case "I":
                await NavigateToLocation(GameLocation.TheInn);
                return true;
                
            case "C":
                await NavigateToLocation(GameLocation.AnchorRoad); // Challenges
                return true;

            case "L":
                await NavigateToLocation(GameLocation.Dormitory); // Lodging
                return true;

            case "A":
                await NavigateToLocation(GameLocation.ArmorShop);
                return true;
                
            case "W":
                await NavigateToLocation(GameLocation.WeaponShop);
                return true;
                
            case "H":
                await NavigateToLocation(GameLocation.Home);
                return true;
                
            case "F":
                await ShowFame();
                return false;
                
            case "1":
                await NavigateToLocation(GameLocation.Healer);
                return true;

            case "2":
                await NavigateToLocation(GameLocation.QuestHall);
                return true;

            case "Q":
                return await QuitGame();
                
            case "G":
                await ShowGoodDeeds();
                return false;
                
            case "E":
                await NavigateToLocation(GameLocation.Wilderness);
                return true;
                
            case "V":
                await NavigateToLocation(GameLocation.Master);
                return true;
                
            case "M":
                await NavigateToLocation(GameLocation.MagicShop);
                return true;
                
            case "N":
                var newsLocation = new NewsLocation();
                await newsLocation.EnterLocation(currentPlayer, terminal);
                return false; // Stay in main street after returning from news

            case "$":
                await ShowWorldEvents();
                return false;
                
            case "Z":
                if (currentPlayer.Level >= GameConfig.MenuTier3Level)
                    await NavigateToTeamCorner();
                else
                    terminal.WriteLine(Loc.Get("main_street.team_corner_level_req"), "yellow");
                return currentPlayer.Level >= GameConfig.MenuTier3Level;

            // List Citizens removed - merged into Fame (F) which now shows locations
            // case "L":
            //     await ListCharacters();
            //     return false;
                
            case "T":
                terminal.WriteLine(Loc.Get("main_street.nav_temple"), "cyan");
                await Task.Delay(1500);
                throw new LocationExitException(GameLocation.Temple);
                
            case "X":
                terminal.WriteLine(Loc.Get("main_street.nav_love_street"), "magenta");
                await Task.Delay(1500);
                throw new LocationExitException(GameLocation.LoveCorner);
                
            case "J":
                if (DoorMode.IsOnlineMode)
                {
                    await ShowAuctionMenu();
                    return false;
                }
                else
                {
                    await NavigateToLocation(GameLocation.AuctionHouse);
                    return true;
                }
                

            case "*":
                await ShowInventory();
                return false;

            case "=":
                await ShowStatistics();
                return false;

            case "U":
                await NavigateToLocation(GameLocation.MusicShop);
                return true;

            // Achievements removed - available via Trophy Room at Home
            // case "!":
            //     await ShowAchievements();
            //     return false;

            case "9":
                return false;
            
            // Quick navigation
            case "K":
                await NavigateToLocation(GameLocation.Castle);
                return true;
                
            case "P":
                await ShowStoryProgress();
                return false;
                
            case "O":
                await NavigateToLocation(GameLocation.Church);
                return true;

            // Assault removed - players can challenge NPCs via Talk feature
            // case "U":
            //     await AttackSomeone();
            //     return false;

            case "Y":
                terminal.WriteLine(Loc.Get("main_street.nav_dark_alley"), "gray");
                await Task.Delay(1500);
                throw new LocationExitException(GameLocation.DarkAlley);

            case ">":
                if (UsurperRemake.Systems.SettlementSystem.Instance?.State.IsEstablished == true)
                {
                    terminal.WriteLine(Loc.Get("main_street.nav_settlement"), "gray");
                    await Task.Delay(1500);
                    throw new LocationExitException(GameLocation.Settlement);
                }
                return false;

            case "?":
                await ShowHelp();
                return false;

            case "!":
                await BugReportSystem.ReportBug(terminal, currentPlayer);
                return false;

            case "R":
                if (DoorMode.IsOnlineMode && GuildSystem.Instance != null)
                {
                    await ShowGuildBoard();
                }
                else
                {
                    terminal.WriteLine($"  {Loc.Get("guild.online_only")}", "gray");
                }
                return false;

            case "3":
                if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
                {
                    await OnlineChatSystem.Instance!.ShowWhosOnline(terminal);
                }
                else
                {
                    await ListCharacters();
                }
                return false;

            case "4":
                if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.Write(Loc.Get("main_street.say_prompt"));
                    terminal.SetColor("white");
                    var chatMsg = await terminal.GetInput("");
                    if (!string.IsNullOrWhiteSpace(chatMsg))
                    {
                        await OnlineChatSystem.Instance!.Say(chatMsg);
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("main_street.say_you", chatMsg));
                        await Task.Delay(1000);
                    }
                }
                return false;

            case "5":
                if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
                {
                    await OnlineChatSystem.Instance!.ShowNews(terminal);
                }
                return false;

            case "6":
                if (DoorMode.IsOnlineMode)
                {
                    throw new LocationExitException(GameLocation.Arena);
                }
                return false;

            case "7":
                if (DoorMode.IsOnlineMode)
                {
                    await ShowWorldBossMenu();
                }
                return false;

            case "SETTINGS":
            case "CONFIG":
                await ShowSettingsMenu();
                return false;
                
            case "MAIL":
            case "CTRL+M":
            case "!M":
                await ShowMail();
                return false;

            // Dev menu removed in v0.53.7 — use admin console instead
            case "DEV":
            case "CHEATER":
            case "DEVMENU":
                terminal.WriteLine("  The dev menu has been removed. Use the admin console.", "gray");
                return false;

            // Talk to NPCs
            case "0":
            case "TALK":
                await TalkToNPC();
                return false;

            // Quick preferences (accessible from any location)
            case "~":
            case "PREFS":
            case "PREFERENCES":
                await ShowPreferencesMenu();
                return false;

            default:
                terminal.WriteLine(Loc.Get("main_street.invalid_choice"), "red");
                await Task.Delay(1500);
                return false;
        }
    }
    
    // Main Street specific action implementations
    private async Task ShowGoodDeeds()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("main_street.good_deeds_title"));
        terminal.WriteLine(Loc.Get("main_street.good_deeds_divider"));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("main_street.your_chivalry", currentPlayer.Chivalry));
        terminal.WriteLine(Loc.Get("main_street.deeds_left", currentPlayer.ChivNr));
        terminal.WriteLine("");
        
        if (currentPlayer.ChivNr > 0)
        {
            terminal.WriteLine(Loc.Get("main_street.available_deeds"));
            terminal.WriteLine(Loc.Get("main_street.deed_give_gold"));
            terminal.WriteLine(Loc.Get("main_street.deed_help_temple"));
            terminal.WriteLine(Loc.Get("main_street.deed_orphanage"));
            terminal.WriteLine("");
            
            var choice = await terminal.GetInput(Loc.Get("main_street.deed_prompt"));
            await ProcessGoodDeed(choice);
        }
        else
        {
            terminal.WriteLine(Loc.Get("main_street.deed_done_today"), "yellow");
        }
        
        await terminal.PressAnyKey();
    }
    
    
    private async Task NavigateToTeamCorner()
    {
        terminal.WriteLine(Loc.Get("main_street.nav_team_corner"), "yellow");
        await Task.Delay(1000);
        
        // Navigate to TeamCornerLocation
        await NavigateToLocation(GameLocation.TeamCorner);
    }
    
    private async Task ShowFame()
    {
        // Get all characters (player + NPCs) and rank them
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;

        // If no NPCs, try to initialize them
        if (npcs == null || npcs.Count == 0)
        {
            await NPCSpawnSystem.Instance.InitializeClassicNPCs();
            npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        }

        // Build a list of all characters for ranking (now includes location)
        var allCharacters = new List<(string Name, int Level, string Class, long Experience, string Location, bool IsPlayer, bool IsAlive)>();

        // Add player
        string playerFameName = currentPlayer.IsKnighted ? $"{currentPlayer.NobleTitle} {currentPlayer.DisplayName}" : currentPlayer.DisplayName;
        allCharacters.Add((playerFameName, currentPlayer.Level, currentPlayer.Class.ToString(), currentPlayer.Experience, "Main Street", true, currentPlayer.IsAlive));

        // Add other online players from the database
        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
        {
            try
            {
                var onlinePlayers = await UsurperRemake.Systems.OnlineStateManager.Instance!.GetAllPlayerSummaries();
                foreach (var op in onlinePlayers)
                {
                    // Skip current player (already added above)
                    if (op.DisplayName.Equals(currentPlayer.DisplayName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string className = ((CharacterClass)op.ClassId).ToString();
                    string onlineTag = op.IsOnline ? "[ON]" : "";
                    string fameDisplayName = !string.IsNullOrEmpty(op.NobleTitle) ? $"{op.NobleTitle} {op.DisplayName}" : op.DisplayName;
                    allCharacters.Add((fameDisplayName, op.Level, className, op.Experience, onlineTag, false, true));
                }
            }
            catch { /* If DB query fails, just show NPCs */ }
        }

        // Add NPCs
        if (npcs != null)
        {
            foreach (var npc in npcs)
            {
                string location = string.IsNullOrEmpty(npc.CurrentLocation) ? "???" : npc.CurrentLocation;
                allCharacters.Add((npc.Name, npc.Level, npc.Class.ToString(), npc.Experience, location, false, npc.IsAlive));
            }
        }

        // Sort by level (desc), then experience (desc), then name
        var ranked = allCharacters
            .Where(c => c.IsAlive)
            .OrderByDescending(c => c.Level)
            .ThenByDescending(c => c.Experience)
            .ThenBy(c => c.Name)
            .ToList();

        // Find player's rank
        int playerRank = ranked.FindIndex(c => c.IsPlayer) + 1;
        if (playerRank == 0) playerRank = ranked.Count + 1; // Player is dead

        int currentPage = 0;
        int itemsPerPage = 15;
        int totalPages = Math.Max(1, (ranked.Count + itemsPerPage - 1) / itemsPerPage);

        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("main_street.hall_fame"), "bright_yellow");
            if (!IsScreenReader)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("main_street.fame_subtitle"));
            }
            terminal.WriteLine("");

            // Show player's rank
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("main_street.fame_your_rank", playerRank, ranked.Count, currentPlayer.DisplayName, currentPlayer.Level));
            terminal.WriteLine("");

            // Column headers (adjusted for location)
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("main_street.fame_rank"),-5} {Loc.Get("main_street.fame_name"),-18} {Loc.Get("main_street.fame_lv"),3} {Loc.Get("main_street.fame_class"),-10} {Loc.Get("main_street.fame_location"),-12} {Loc.Get("main_street.fame_experience"),10}");
            WriteDivider(64);

            // Display current page
            int startIdx = currentPage * itemsPerPage;
            int endIdx = Math.Min(startIdx + itemsPerPage, ranked.Count);

            for (int i = startIdx; i < endIdx; i++)
            {
                var entry = ranked[i];
                int rank = i + 1;

                // Color coding
                string color;
                if (entry.IsPlayer)
                    color = "bright_green";
                else if (rank <= 3)
                    color = rank == 1 ? "bright_yellow" : (rank == 2 ? "white" : "yellow");
                else if (entry.Level > currentPlayer.Level)
                    color = "bright_red";
                else
                    color = "gray";

                terminal.SetColor(color);

                string rankStr = rank <= 3 ? $"#{rank}" : $"{rank}.";
                string marker = entry.IsPlayer ? "*" : " ";
                string nameDisplay = entry.Name.Length > 17 ? entry.Name.Substring(0, 17) : entry.Name;
                string classDisplay = entry.Class.Length > 10 ? entry.Class.Substring(0, 10) : entry.Class;
                string locDisplay = entry.Location.Length > 12 ? entry.Location.Substring(0, 12) : entry.Location;
                terminal.WriteLine($"  {rankStr,-5}{marker}{nameDisplay,-17} {entry.Level,3} {classDisplay,-10} {locDisplay,-12} {entry.Experience,10:N0}");
            }

            terminal.WriteLine("");
            if (!IsScreenReader)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            }
            terminal.WriteLine("");

            // Navigation
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("main_street.fame_page", currentPage + 1, totalPages));
            var options = new List<string>();
            if (currentPage > 0) options.Add("[P]rev");
            if (currentPage < totalPages - 1) options.Add("[N]ext");
            options.Add("[R]eturn");
            terminal.WriteLine($"  {string.Join("  ", options)}");

            string input = (await terminal.GetKeyInput()).ToUpperInvariant();

            if (input == "P" && currentPage > 0)
                currentPage--;
            else if (input == "N" && currentPage < totalPages - 1)
                currentPage++;
            else if (input == "R" || input == "Q" || input == "ESCAPE")
                break;
        }
    }
    
    private async Task ListCharacters()
    {
        // Get NPCs from the spawn system
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;

        // Debug: If no NPCs, try to initialize them
        if (npcs == null || npcs.Count == 0)
        {
            // GD.Print("[ListCharacters] No NPCs found, attempting to initialize...");
            await NPCSpawnSystem.Instance.InitializeClassicNPCs();
            npcs = NPCSpawnSystem.Instance.ActiveNPCs;
            // GD.Print($"[ListCharacters] After init: {npcs?.Count ?? 0} NPCs");
        }

        var aliveNPCs = npcs?.Where(n => n.IsAlive).OrderByDescending(n => n.Level).ThenBy(n => n.Name).ToList() ?? new List<NPC>();
        var deadNPCs = npcs?.Where(n => !n.IsAlive).OrderByDescending(n => n.Level).ThenBy(n => n.Name).ToList() ?? new List<NPC>();

        int currentPage = 0;
        int itemsPerPage = 18;
        int totalAlivePages = (aliveNPCs.Count + itemsPerPage - 1) / itemsPerPage;
        int totalDeadPages = (deadNPCs.Count + itemsPerPage - 1) / itemsPerPage;
        bool viewingDead = false;

        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("main_street.citizens"), "bright_cyan");
            terminal.WriteLine("");

            // Always show player first
            WriteSectionHeader(Loc.Get("main_street.section_players"), "bright_green");
            terminal.SetColor("yellow");
            string playerSex = currentPlayer.Sex == CharacterSex.Male ? "M" : "F";
            string citizenName = currentPlayer.IsKnighted ? $"{currentPlayer.NobleTitle} {currentPlayer.DisplayName}" : currentPlayer.DisplayName;
            terminal.WriteLine($"  * {citizenName,-18} {playerSex} Lv{currentPlayer.Level,3} {currentPlayer.Class,-10} HP:{currentPlayer.HP}/{currentPlayer.MaxHP} {Loc.Get("main_street.citizens_you_tag")}");
            terminal.WriteLine("");

            if (!viewingDead)
            {
                // Show alive NPCs
                int totalPages = Math.Max(1, totalAlivePages);
                WriteSectionHeader(Loc.Get("main_street.citizens_adventurers", aliveNPCs.Count, currentPage + 1, totalPages), "bright_green");

                if (aliveNPCs.Count > 0)
                {
                    int startIdx = currentPage * itemsPerPage;
                    int endIdx = Math.Min(startIdx + itemsPerPage, aliveNPCs.Count);

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        var npc = aliveNPCs[i];
                        // Color based on level relative to player
                        string color = npc.Level > currentPlayer.Level + 5 ? "bright_red" :
                                       npc.Level > currentPlayer.Level ? "yellow" :
                                       npc.Level > currentPlayer.Level - 5 ? "white" : "gray";

                        terminal.SetColor(color);
                        string classStr = npc.Class.ToString();
                        string locationStr = string.IsNullOrEmpty(npc.CurrentLocation) ? "???" : npc.CurrentLocation;
                        string sex = npc.Sex == CharacterSex.Male ? "M" : "F";
                        terminal.WriteLine($"  - {npc.Name,-18} {sex} Lv{npc.Level,3} {classStr,-10} @ {locationStr}");
                    }
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {Loc.Get("main_street.citizens_no_adventurers")}");
                }
            }
            else
            {
                // Show dead NPCs
                int totalPages = Math.Max(1, totalDeadPages);
                WriteSectionHeader(Loc.Get("main_street.citizens_fallen", deadNPCs.Count, currentPage + 1, totalPages), "dark_gray");

                if (deadNPCs.Count > 0)
                {
                    int startIdx = currentPage * itemsPerPage;
                    int endIdx = Math.Min(startIdx + itemsPerPage, deadNPCs.Count);

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        var npc = deadNPCs[i];
                        terminal.SetColor("dark_gray");
                        string sex = npc.Sex == CharacterSex.Male ? "M" : "F";
                        string deathMarker = IsScreenReader ? "(dead)" : "†";
                        terminal.WriteLine($"  {deathMarker} {npc.Name,-18} {sex} Lv{npc.Level,3} {npc.Class,-10} - {Loc.Get("main_street.rip")}");
                    }
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {Loc.Get("main_street.citizens_no_fallen")}");
                }
            }

            terminal.WriteLine("");
            if (!IsScreenReader)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            }
            terminal.WriteLine("");

            // Navigation options
            terminal.SetColor("cyan");
            var options = new List<string>();
            int maxPages = viewingDead ? totalDeadPages : totalAlivePages;
            if (currentPage > 0) options.Add("[P]rev");
            if (currentPage < maxPages - 1) options.Add("[N]ext");
            if (!viewingDead && deadNPCs.Count > 0) options.Add("[D]ead");
            if (viewingDead) options.Add("[A]live");
            options.Add("[R]eturn");

            terminal.WriteLine($"  {string.Join("  ", options)}");
            terminal.WriteLine("");

            string input = (await terminal.GetKeyInput()).ToUpperInvariant();

            if (input == "P" && currentPage > 0)
            {
                currentPage--;
            }
            else if (input == "N" && currentPage < maxPages - 1)
            {
                currentPage++;
            }
            else if (input == "D" && !viewingDead && deadNPCs.Count > 0)
            {
                viewingDead = true;
                currentPage = 0;
            }
            else if (input == "A" && viewingDead)
            {
                viewingDead = false;
                currentPage = 0;
            }
            else if (input == "R" || input == "Q" || input == "ESCAPE")
            {
                break;
            }
        }
    }
    
    /// <summary>
    /// Display comprehensive player statistics
    /// </summary>
    private async Task ShowStatistics()
    {
        var stats = currentPlayer.Statistics;
        stats.UpdateSessionTime(); // Ensure current session is counted

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("main_street.statistics"), "bright_cyan");
        terminal.WriteLine("");

        // Combat Stats
        WriteSectionHeader(Loc.Get("main_street.section_combat"), "bright_red");
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_monsters_slain"),-21} {stats.TotalMonstersKilled,10:N0}     {Loc.Get("main_street.stat_bosses_killed"),-17} {stats.TotalBossesKilled,8:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_unique_monsters"),-21} {stats.TotalUniquesKilled,10:N0}     {Loc.Get("main_street.stat_combat_win_rate"),-17} {stats.GetCombatWinRate(),7:F1}%");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_combats_won"),-21} {stats.TotalCombatsWon,10:N0}     {Loc.Get("main_street.stat_combats_lost"),-17} {stats.TotalCombatsLost,8:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_times_fled"),-21} {stats.TotalCombatsFled,10:N0}     {Loc.Get("main_street.stat_player_kills"),-18}{stats.TotalPlayerKills,7:N0}");
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_damage_dealt"),-21} {stats.TotalDamageDealt,10:N0}     {Loc.Get("main_street.stat_damage_taken"),-17} {stats.TotalDamageTaken,8:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_highest_hit"),-21} {stats.HighestSingleHit,10:N0}     {Loc.Get("main_street.stat_critical_hits"),-17} {stats.TotalCriticalHits,8:N0}");
        terminal.WriteLine("");

        // Economic Stats
        WriteSectionHeader(Loc.Get("main_street.section_economy"), "bright_green");
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_gold_earned"),-21} {stats.TotalGoldEarned,10:N0}     {Loc.Get("main_street.stat_gold_monsters"),-18}{stats.TotalGoldFromMonsters,7:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_gold_spent"),-21} {stats.TotalGoldSpent,10:N0}     {Loc.Get("main_street.stat_peak_gold"),-17} {stats.HighestGoldHeld,8:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_items_bought"),-21} {stats.TotalItemsBought,10:N0}     {Loc.Get("main_street.stat_items_sold"),-17} {stats.TotalItemsSold,8:N0}");
        terminal.WriteLine("");

        // Experience Stats
        WriteSectionHeader(Loc.Get("main_street.section_experience"), "bright_magenta");
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_xp_earned"),-21} {stats.TotalExperienceEarned,10:N0}     {Loc.Get("main_street.stat_level_ups"),-17} {stats.TotalLevelUps,8:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_highest_level"),-21} {stats.HighestLevelReached,10}     {Loc.Get("main_street.stat_current_level"),-17} {currentPlayer.Level,8}");
        terminal.WriteLine("");

        // Exploration Stats
        WriteSectionHeader(Loc.Get("main_street.section_exploration"), "bright_blue");
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_deepest_dungeon"),-21} {stats.DeepestDungeonLevel,10}     {Loc.Get("main_street.stat_floors_explored"),-17} {stats.TotalDungeonFloorsCovered,8:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_chests_opened"),-21} {stats.TotalChestsOpened,10:N0}     {Loc.Get("main_street.stat_secrets_found"),-17} {stats.TotalSecretsFound,8:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_traps_triggered"),-21} {stats.TotalTrapsTriggered,10:N0}     {Loc.Get("main_street.stat_traps_disarmed"),-17} {stats.TotalTrapsDisarmed,8:N0}");
        terminal.WriteLine("");

        // Survival Stats
        WriteSectionHeader(Loc.Get("main_street.section_survival"), "yellow");
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_deaths_monster"),-21} {stats.TotalMonsterDeaths,10:N0}     {Loc.Get("main_street.stat_deaths_pvp"),-17} {stats.TotalPlayerDeaths,8:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_potions_used"),-21} {stats.TotalHealingPotionsUsed,10:N0}     {Loc.Get("main_street.stat_health_restored"),-17} {stats.TotalHealthRestored,8:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_resurrections"),-21} {stats.TotalTimesResurrected,10:N0}     {Loc.Get("main_street.stat_diseases_cured"),-17} {stats.TotalDiseasesCured,8:N0}");
        terminal.WriteLine("");

        // Time Stats
        WriteSectionHeader(Loc.Get("main_street.section_time"), "gray");
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_play_time"),-21} {stats.GetFormattedPlayTime(),10}     {Loc.Get("main_street.stat_sessions"),-17} {stats.TotalSessionsPlayed,8:N0}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_created"),-21} {GameConfig.FormatDate(stats.CharacterCreated, currentPlayer.DateFormatPreference),10}     {Loc.Get("main_street.stat_current_streak"),-17} {stats.CurrentStreak,8} {Loc.Get("main_street.stat_days")}");
        terminal.WriteLine($"  {Loc.Get("main_street.stat_longest_streak"),-21} {stats.LongestStreak,10} {Loc.Get("main_street.stat_days")}");
        terminal.WriteLine("");

        // Difficulty indicator
        terminal.SetColor(DifficultySystem.GetColor(currentPlayer.Difficulty));
        terminal.WriteLine(Loc.Get("main_street.stat_difficulty", DifficultySystem.GetDisplayName(currentPlayer.Difficulty)));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Display player achievements
    /// </summary>
    private async Task ShowAchievements()
    {
        // Initialize if needed
        AchievementSystem.Initialize();

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("main_street.achievements"), "bright_yellow");

        var achievements = currentPlayer.Achievements;
        int totalAchievements = AchievementSystem.TotalAchievements;
        int unlocked = achievements.UnlockedCount;

        // Summary line
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("main_street.achieve_unlocked", unlocked, totalAchievements, achievements.CompletionPercentage)}");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  {Loc.Get("main_street.achieve_points", achievements.TotalPoints)}");
        terminal.WriteLine("");

        // Category selection
        if (IsScreenReader)
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("main_street.achieve_categories_label"));
            terminal.WriteLine(Loc.Get("main_street.achieve_combat"));
            terminal.WriteLine(Loc.Get("main_street.achieve_progression"));
            terminal.WriteLine(Loc.Get("main_street.achieve_economy"));
            terminal.WriteLine(Loc.Get("main_street.achieve_exploration"));
            terminal.WriteLine(Loc.Get("main_street.achieve_social"));
            terminal.WriteLine(Loc.Get("main_street.achieve_challenge"));
            terminal.WriteLine(Loc.Get("main_street.achieve_secret"));
            terminal.WriteLine(Loc.Get("main_street.achieve_all"));
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("main_street.achieve_categories_visual"));
            terminal.WriteLine(Loc.Get("main_street.achieve_categories_visual2"));
        }
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        var input = await terminal.GetInput(Loc.Get("main_street.achieve_select_prompt"));
        input = input.Trim().ToUpper();

        AchievementCategory? selectedCategory = input switch
        {
            "1" => AchievementCategory.Combat,
            "2" => AchievementCategory.Progression,
            "3" => AchievementCategory.Economy,
            "4" => AchievementCategory.Exploration,
            "5" => AchievementCategory.Social,
            "6" => AchievementCategory.Challenge,
            "7" => AchievementCategory.Secret,
            _ => null
        };

        // Display achievements
        terminal.ClearScreen();
        var categoryName = selectedCategory?.ToString() ?? "All";
        WriteBoxHeader($"{categoryName.ToUpper()} ACHIEVEMENTS", "bright_yellow");
        terminal.WriteLine("");

        var achievementsToShow = selectedCategory.HasValue
            ? AchievementSystem.GetByCategory(selectedCategory.Value)
            : AchievementSystem.GetAllAchievements();

        int displayCount = 0;
        foreach (var achievement in achievementsToShow.OrderBy(a => a.Tier).ThenBy(a => a.Name))
        {
            bool isUnlocked = achievements.IsUnlocked(achievement.Id);

            // Show tier symbol and name
            terminal.SetColor(achievement.GetTierColor());
            terminal.Write($" {achievement.GetTierSymbol()} ");

            if (isUnlocked)
            {
                terminal.SetColor("bright_green");
                terminal.Write("+ ");
                terminal.SetColor("white");
                terminal.Write(achievement.Name);
                terminal.SetColor("gray");
                terminal.WriteLine($" - {achievement.Description}");

                // Show unlock date
                var unlockDate = achievements.GetUnlockDate(achievement.Id);
                if (unlockDate.HasValue)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"     Unlocked: {GameConfig.FormatDate(unlockDate.Value, currentPlayer.DateFormatPreference)}   +{achievement.PointValue} pts");
                }
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write("[ ] ");

                if (achievement.IsSecret)
                {
                    terminal.SetColor("gray");
                    terminal.Write("???");
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($" - {achievement.SecretHint}");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.Write(achievement.Name);
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($" - {achievement.Description}");
                }
            }

            displayCount++;

            // Pagination
            if (displayCount > 0 && displayCount % 15 == 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("main_street.achieve_more_prompt"));
                var key = await terminal.GetKeyInput();
                if (key?.ToUpper() == "Q") return;
                terminal.ClearScreen();
                WriteBoxHeader($"{categoryName.ToUpper()} ACHIEVEMENTS", "bright_yellow");
                terminal.WriteLine("");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Attack another character in the current location
    /// </summary>
    private async Task AttackSomeone()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("main_street.attack"), "bright_red");
        terminal.WriteLine("");

        // Get NPCs in the area
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs ?? new List<NPC>();
        var npcsInArea = allNPCs
            .Where(n => n.IsAlive &&
                       (n.CurrentLocation?.Equals("Main Street", StringComparison.OrdinalIgnoreCase) == true ||
                        n.CurrentLocation?.Equals("MainStreet", StringComparison.OrdinalIgnoreCase) == true))
            .Take(10)
            .ToList();

        // Add some random targets if no NPCs found
        if (npcsInArea.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("main_street.attack_no_targets")}");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine($"  {Loc.Get("main_street.attack_who_question")}");
        terminal.WriteLine("");

        terminal.SetColor("white");
        for (int i = 0; i < npcsInArea.Count; i++)
        {
            var npc = npcsInArea[i];
            terminal.SetColor("cyan");
            terminal.Write($"  [{i + 1}] ");
            terminal.SetColor("white");
            terminal.Write($"{npc.Name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" - {Loc.Get("main_street.attack_npc_info", npc.Level, npc.Class)}");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("main_street.attack_cancel")}");
        terminal.WriteLine("");

        string choice = await terminal.GetInput(Loc.Get("main_street.attack_prompt"));

        if (int.TryParse(choice, out int targetIndex) && targetIndex >= 1 && targetIndex <= npcsInArea.Count)
        {
            var target = npcsInArea[targetIndex - 1];

            terminal.SetColor("red");
            terminal.WriteLine($"\n  {Loc.Get("main_street.attack_approach", target.Name)}");
            await Task.Delay(1000);

            // Warn about consequences
            terminal.SetColor("yellow");
            terminal.WriteLine($"\n  {Loc.Get("main_street.attack_warning")}");
            terminal.WriteLine($"  {Loc.Get("main_street.attack_confirm")}");

            string confirm = (await terminal.GetKeyInput()).ToUpperInvariant();

            if (confirm == "Y")
            {
                // Attack!
                var encounterResult = await StreetEncounterSystem.Instance.AttackCharacter(
                    currentPlayer, target, terminal);

                // Increase darkness for unprovoked attack
                currentPlayer.Darkness += 15;

                if (encounterResult.Victory)
                {
                    terminal.SetColor("green");
                    terminal.WriteLine($"\n  {Loc.Get("main_street.attack_victory", target.Name)}");
                    currentPlayer.PKills++;
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\n  {Loc.Get("main_street.attack_defeat", target.Name)}");
                    currentPlayer.PDefeats++;
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"\n  {Loc.Get("main_street.attack_cancel_decision")}");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"\n  {Loc.Get("main_street.attack_change_mind")}");
        }

        await Task.Delay(1500);
    }

    private async Task<bool> QuitGame()
    {
        // Online mode: show sleep options with cancel
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {Loc.Get("main_street.quit_where_sleep")}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("main_street.quit_dormitory_desc")}");
            terminal.WriteLine($"  {Loc.Get("main_street.quit_inn_desc")}");
            if (currentPlayer.HasReinforcedDoor)
                terminal.WriteLine($"  {Loc.Get("main_street.quit_home_desc")}");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.Write("  [D] ");
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("main_street.menu_dormitory_padded"));
            terminal.SetColor("white");
            terminal.Write("[I] ");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("main_street.menu_inn"));
            if (currentPlayer.HasReinforcedDoor)
            {
                terminal.SetColor("white");
                terminal.Write("  [H] ");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("main_street.menu_home_padded"));
            }
            else
            {
                terminal.Write("              ");
            }
            terminal.SetColor("white");
            terminal.Write("[C] ");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("main_street.menu_cancel"));
            terminal.WriteLine("");

            var choice = (await terminal.GetInput($"  {Loc.Get("ui.your_choice")} ")).Trim().ToUpperInvariant();

            if (choice == "C" || string.IsNullOrEmpty(choice))
                return false; // Back to Main Street

            if (choice == "I")
            {
                await NavigateToLocation(GameLocation.TheInn);
                return true;
            }

            if (choice == "H" && currentPlayer.HasReinforcedDoor)
            {
                // Sleep at home — safe behind reinforced door
                currentPlayer.HP = currentPlayer.MaxHP;
                currentPlayer.Mana = currentPlayer.MaxMana;
                currentPlayer.Stamina = Math.Max(currentPlayer.Stamina, currentPlayer.Constitution * 2);

                var backend = SaveSystem.Instance.Backend as UsurperRemake.Systems.SqlSaveBackend;
                if (backend != null)
                {
                    var username = UsurperRemake.BBS.DoorMode.OnlineUsername ?? currentPlayer.Name2;
                    await backend.RegisterSleepingPlayer(username, "home", "[]", 1);
                }

                terminal.SetColor("gray");
                terminal.WriteLine($"\n  {Loc.Get("main_street.quit_home_sleep")}");
                await ShowTomorrowForecast();
                throw new LocationExitException(GameLocation.NoWhere);
            }

            // Default: dormitory sleep
            long dormCost = GameConfig.DormitorySleepCost;
            bool isBroke = false;
            if (currentPlayer.Gold >= dormCost)
            {
                currentPlayer.Gold -= dormCost;
            }
            else if (currentPlayer.Gold + currentPlayer.BankGold >= dormCost)
            {
                long shortfall = dormCost - currentPlayer.Gold;
                currentPlayer.Gold = 0;
                currentPlayer.BankGold -= shortfall;
            }
            else
            {
                isBroke = true;
            }

            currentPlayer.HP = currentPlayer.MaxHP;
            currentPlayer.Mana = currentPlayer.MaxMana;
            currentPlayer.Stamina = Math.Max(currentPlayer.Stamina, currentPlayer.Constitution * 2);

            var dormBackend = SaveSystem.Instance.Backend as UsurperRemake.Systems.SqlSaveBackend;
            if (dormBackend != null)
            {
                var username = UsurperRemake.BBS.DoorMode.OnlineUsername ?? currentPlayer.Name2;
                await dormBackend.RegisterSleepingPlayer(username, isBroke ? "street" : "dormitory", "[]", 0);
            }

            terminal.SetColor("gray");
            terminal.WriteLine(isBroke
                ? $"\n  {Loc.Get("main_street.quit_street_sleep")}"
                : $"\n  {Loc.Get("main_street.quit_dormitory_sleep")}");

            await ShowTomorrowForecast();
            throw new LocationExitException(GameLocation.NoWhere);
        }

        terminal.ClearScreen();

        // Display session summary
        if (currentPlayer?.Statistics != null)
        {
            var summary = currentPlayer.Statistics.GetSessionSummary();

            WriteBoxHeader(Loc.Get("main_street.session_summary"), "bright_cyan");
            terminal.WriteLine("");

            // Session duration
            terminal.SetColor("white");
            terminal.Write($"  {Loc.Get("main_street.session_duration")} ");
            terminal.SetColor("bright_yellow");
            if (summary.Duration.TotalHours >= 1)
                terminal.WriteLine($"{(int)summary.Duration.TotalHours}h {summary.Duration.Minutes}m");
            else
                terminal.WriteLine($"{summary.Duration.Minutes}m {summary.Duration.Seconds}s");

            terminal.WriteLine("");
            WriteDivider(59);
            terminal.WriteLine("");

            // Combat stats
            terminal.SetColor("bright_red");
            terminal.Write($"  {Loc.Get("main_street.session_monsters")} ");
            terminal.SetColor("white");
            terminal.WriteLine($"{summary.MonstersKilled:N0}");

            terminal.SetColor("bright_red");
            terminal.Write($"  {Loc.Get("main_street.session_damage")} ");
            terminal.SetColor("white");
            terminal.WriteLine($"{summary.DamageDealt:N0}");

            // Progress stats
            if (summary.LevelsGained > 0)
            {
                terminal.SetColor("bright_green");
                terminal.Write($"  {Loc.Get("main_street.session_levels")} ");
                terminal.SetColor("white");
                terminal.WriteLine($"+{summary.LevelsGained}");
            }

            terminal.SetColor("bright_magenta");
            terminal.Write($"  {Loc.Get("main_street.session_xp")} ");
            terminal.SetColor("white");
            terminal.WriteLine($"{summary.ExperienceGained:N0}");

            // Economy stats
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {Loc.Get("main_street.session_gold")} ");
            terminal.SetColor("white");
            terminal.WriteLine($"{summary.GoldEarned:N0}");

            if (summary.ItemsBought > 0 || summary.ItemsSold > 0)
            {
                terminal.SetColor("cyan");
                terminal.Write($"  {Loc.Get("main_street.session_items")} ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("main_street.session_items_detail", summary.ItemsBought, summary.ItemsSold));
            }

            // Exploration
            if (summary.RoomsExplored > 0)
            {
                terminal.SetColor("bright_blue");
                terminal.Write($"  {Loc.Get("main_street.session_rooms")} ");
                terminal.SetColor("white");
                terminal.WriteLine($"{summary.RoomsExplored:N0}");
            }

            terminal.WriteLine("");
            WriteDivider(59);
            terminal.WriteLine("");
        }

        // Tomorrow's Forecast
        await ShowTomorrowForecast();

        terminal.SetColor("yellow");
        terminal.WriteLine($"  {Loc.Get("main_street.saving_progress")}");

        // Actually save the game before quitting!
        await GameEngine.Instance.SaveCurrentGame();

        // Final Steam stats sync at session end
        SteamIntegration.SyncCurrentPlayerStats();

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Loc.Get("main_street.thanks_playing")}");
        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.WriteLine($"  {Loc.Get("main_street.press_exit")}");
        await terminal.PressAnyKey();

        // Signal game should quit
        throw new LocationExitException(GameLocation.NoWhere);
    }

    private async Task ShowTomorrowForecast()
    {
        if (currentPlayer == null) return;

        var forecasts = new List<string>();

        // Context-sensitive forecasts based on player state
        if (currentPlayer.Level < 10)
        {
            int nextFloor = Math.Min((currentPlayer.Statistics?.DeepestDungeonLevel ?? 1) + 2, 100);
            forecasts.Add($"The dungeon depths call to you... Floor {nextFloor} holds new challenges.");
        }

        if (currentPlayer.HerbsGatheredToday > 0)
            forecasts.Add("Your herb garden will have fresh herbs ready to gather.");

        if (currentPlayer.Healing < 3)
            forecasts.Add("Stock up on potions at the Healer before your next dungeon run.");

        if (currentPlayer.Gold > 0 && currentPlayer.BankGold == 0 && currentPlayer.Gold >= 500)
            forecasts.Add("Consider depositing your gold at the Bank for safekeeping.");

        if (currentPlayer.Level >= 3 && currentPlayer.MKills > 0 && currentPlayer.MKills < 20)
            forecasts.Add("The Quest Hall may have new bounties worth your time.");

        // Generic forecasts (always available)
        var genericForecasts = new List<string>
        {
            "Merchants are expecting a fresh shipment of goods.",
            "Rumors of treasure on the dungeon's deeper floors spread through town.",
            "The arena champion awaits a worthy challenger.",
            "Strange sounds echo from the dungeon at night...",
            "The townsfolk whisper about events unfolding in the realm.",
            "New bounties may appear at the Quest Hall.",
            "The wilderness holds secrets yet to be discovered.",
        };

        var random = Random.Shared;
        forecasts.Add(genericForecasts[random.Next(genericForecasts.Count)]);

        // Pick up to 2 forecasts to display
        var toShow = forecasts.OrderBy(_ => random.Next()).Take(2).ToList();

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("  Tomorrow's Forecast:");
        terminal.SetColor("gray");
        foreach (var forecast in toShow)
        {
            terminal.WriteLine($"    - {forecast}");
        }
        terminal.WriteLine("");

        await Task.CompletedTask;
    }

    // Helper methods
    private string GetTownName()
    {
        return "Usurper"; // Could be configurable
    }
    
    private string GetTimeOfDay()
    {
        // Use game clock in single-player, real clock in online mode
        int hour;
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
            hour = currentPlayer.GameTimeMinutes / 60;
        else
            hour = DateTime.Now.Hour;

        return hour switch
        {
            >= 6 and < 12 => Loc.Get("main_street.time_morning"),
            >= 12 and < 18 => Loc.Get("main_street.time_afternoon"),
            >= 18 and < 22 => Loc.Get("main_street.time_evening"),
            _ => Loc.Get("main_street.time_night")
        };
    }
    
    private string GetWeather()
    {
        var weatherKeys = new[] { "main_street.weather_clear", "main_street.weather_cloudy", "main_street.weather_misty", "main_street.weather_cool", "main_street.weather_warm", "main_street.weather_breezy" };
        return Loc.Get(weatherKeys[Random.Shared.Next(0, weatherKeys.Length)]);
    }
    
    private int GetPlayerRank()
    {
        // Calculate real rank based on all characters
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        int rank = 1;

        if (npcs != null)
        {
            foreach (var npc in npcs.Where(n => n.IsAlive))
            {
                // NPC ranks higher if higher level, or same level with more XP
                if (npc.Level > currentPlayer.Level ||
                    (npc.Level == currentPlayer.Level && npc.Experience > currentPlayer.Experience))
                {
                    rank++;
                }
            }
        }

        return rank;
    }
    
    private async Task ProcessGoodDeed(string choice)
    {
        if (int.TryParse(choice, out int deed) && deed >= 1 && deed <= 3)
        {
            currentPlayer.ChivNr--;
            currentPlayer.Chivalry += 10;
            
            var deedName = deed switch
            {
                1 => Loc.Get("main_street.deed_giving_gold"),
                2 => Loc.Get("main_street.deed_helping_temple"),
                3 => Loc.Get("main_street.deed_volunteering"),
                _ => Loc.Get("main_street.deed_generic")
            };

            terminal.WriteLine(Loc.Get("main_street.deed_chivalry_gain", deedName), "green");
            await Task.Delay(1500);
        }
    }
    
    
    /// <summary>
    /// Test combat system (DEBUG)
    /// </summary>
    private async Task TestCombat()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("main_street.combat_test_header"));
        terminal.WriteLine("");
        
        // Create a test monster (Street Thug)
        var testMonster = Monster.CreateMonster(
            nr: 1,
            name: "Street Thug",
            hps: 50,
            strength: 15,
            defence: 8,
            phrase: "Give me your gold!",
            grabweap: true,
            grabarm: false,
            weapon: "Rusty Knife",
            armor: "Torn Clothes",
            poisoned: false,
            disease: false,
            punch: 12,
            armpow: 2,
            weappow: 8
        );
        
        terminal.WriteLine(Loc.Get("main_street.combat_test_intro"));
        terminal.WriteLine(Loc.Get("main_street.combat_test_weapon", testMonster.Name, testMonster.Weapon));
        terminal.WriteLine("");
        
        var confirm = await terminal.GetInput(Loc.Get("main_street.combat_test_confirm"));
        
        if (confirm.ToUpper() == "Y")
        {
            // Initialize combat engine
            var combatEngine = new CombatEngine(terminal);
            
            // Execute combat
            var result = await combatEngine.PlayerVsMonster(currentPlayer, testMonster);

            // Check if player should return to temple after resurrection
            if (result.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("main_street.combat_test_temple"));
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }

            // Display result summary
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("main_street.combat_test_summary"));
            terminal.WriteLine("");
            
            foreach (var logEntry in result.CombatLog)
            {
                terminal.WriteLine($"- {logEntry}");
            }
            
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("main_street.combat_test_outcome", result.Outcome));
            
            if (result.Outcome == CombatOutcome.Victory)
            {
                terminal.WriteLine(Loc.Get("main_street.combat_test_victory"), "green");
            }
            else if (result.Outcome == CombatOutcome.PlayerEscaped)
            {
                terminal.WriteLine(Loc.Get("main_street.combat_test_escaped"), "yellow");
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("main_street.combat_test_avoided"), "green");
        }
        
        await terminal.PressAnyKey();
    }
    
    /// <summary>
    /// Show settings and save management menu
    /// </summary>
    private async Task ShowSettingsMenu()
    {
        bool exitSettings = false;
        
        while (!exitSettings)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("main_street.settings"), "bright_cyan");
            terminal.WriteLine("");
            
            var dailyManager = DailySystemManager.Instance;
            var currentMode = dailyManager.CurrentMode;
            
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("main_street.settings_current"));
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                terminal.WriteLine(Loc.Get("main_street.settings_daily_cycle", GetDailyCycleModeDescription(currentMode)), "yellow");
            else if (currentPlayer != null)
                terminal.WriteLine(Loc.Get("main_street.settings_time_of_day", DailySystemManager.GetTimePeriodString(currentPlayer), DailySystemManager.GetTimeString(currentPlayer)), "yellow");
            terminal.WriteLine(Loc.Get("main_street.settings_autosave", dailyManager.AutoSaveEnabled ? Loc.Get("main_street.prefs_enabled") : Loc.Get("main_street.prefs_disabled")), "yellow");
            terminal.WriteLine(Loc.Get("main_street.settings_current_day", dailyManager.CurrentDay), "yellow");
            terminal.WriteLine("");

            terminal.WriteLine(Loc.Get("main_street.settings_options"));
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                terminal.WriteLine(Loc.Get("main_street.settings_change_cycle"));
            else
                terminal.WriteLine(Loc.Get("main_street.settings_time_note"), "gray");
            terminal.WriteLine(Loc.Get("main_street.settings_configure_autosave"));
            terminal.WriteLine(Loc.Get("main_street.settings_save_now"));
            terminal.WriteLine(Loc.Get("main_street.settings_load_save"));
            terminal.WriteLine(Loc.Get("main_street.settings_delete_saves"));
            terminal.WriteLine(Loc.Get("main_street.settings_view_info"));
            terminal.WriteLine(Loc.Get("main_street.settings_force_reset"));
            terminal.WriteLine(Loc.Get("main_street.settings_game_prefs"));
            terminal.WriteLine(Loc.Get("main_street.settings_back"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("main_street.settings_prompt"));

            switch (choice)
            {
                case "1":
                    if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                        await ChangeDailyCycleMode();
                    break;
                    
                case "2":
                    await ConfigureAutoSave();
                    break;
                    
                case "3":
                    await SaveGameNow();
                    break;
                    
                case "4":
                    await LoadDifferentSave();
                    break;
                    
                case "5":
                    await DeleteSaveFiles();
                    break;
                    
                case "6":
                    await ViewSaveFileInfo();
                    break;
                    
                case "7":
                    await ForceDailyReset();
                    break;

                case "8":
                    await ShowGamePreferences();
                    break;

                case "9":
                    exitSettings = true;
                    break;

                default:
                    terminal.WriteLine(Loc.Get("main_street.settings_invalid"), "red");
                    await Task.Delay(1000);
                    break;
            }
        }
    }

    /// <summary>
    /// Show game preferences menu (combat speed, content settings)
    /// </summary>
    private async Task ShowGamePreferences()
    {
        bool exitPrefs = false;

        while (!exitPrefs)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("main_street.preferences"), "bright_cyan");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("main_street.prefs_current"));
            terminal.WriteLine("");

            // Combat Speed
            string speedDesc = currentPlayer.CombatSpeed switch
            {
                CombatSpeed.Instant => Loc.Get("main_street.prefs_speed_instant"),
                CombatSpeed.Fast => Loc.Get("main_street.prefs_speed_fast"),
                _ => Loc.Get("main_street.prefs_speed_normal")
            };
            terminal.WriteLine(Loc.Get("main_street.prefs_combat_speed", speedDesc), "yellow");

            // Auto-heal
            terminal.WriteLine(Loc.Get("main_street.prefs_auto_heal", currentPlayer.AutoHeal ? Loc.Get("main_street.prefs_enabled") : Loc.Get("main_street.prefs_disabled")), "yellow");

            // Skip intimate scenes
            terminal.WriteLine(Loc.Get("main_street.prefs_skip_intimate", currentPlayer.SkipIntimateScenes ? Loc.Get("main_street.prefs_skip_enabled") : Loc.Get("main_street.prefs_skip_disabled")), "yellow");
            terminal.WriteLine("");

            terminal.WriteLine(Loc.Get("main_street.prefs_options"));
            terminal.WriteLine(Loc.Get("main_street.prefs_change_speed"));
            terminal.WriteLine(Loc.Get("main_street.prefs_toggle_heal"));
            terminal.WriteLine(Loc.Get("main_street.prefs_toggle_intimate"));
            terminal.WriteLine(Loc.Get("main_street.prefs_back"));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("main_street.prefs_prompt"));

            switch (choice)
            {
                case "1":
                    await ChangeCombatSpeed();
                    break;

                case "2":
                    currentPlayer.AutoHeal = !currentPlayer.AutoHeal;
                    terminal.WriteLine(Loc.Get("main_street.prefs_autoheal_toggled", currentPlayer.AutoHeal ? "ENABLED" : "DISABLED"), "green");
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1000);
                    break;

                case "3":
                    currentPlayer.SkipIntimateScenes = !currentPlayer.SkipIntimateScenes;
                    if (currentPlayer.SkipIntimateScenes)
                    {
                        terminal.WriteLine(Loc.Get("main_street.prefs_intimate_fade"), "green");
                        terminal.WriteLine(Loc.Get("main_street.prefs_intimate_fade2"), "gray");
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("main_street.prefs_intimate_full"), "green");
                    }
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1500);
                    break;

                case "4":
                    exitPrefs = true;
                    break;

                default:
                    terminal.WriteLine(Loc.Get("main_street.settings_invalid"), "red");
                    await Task.Delay(1000);
                    break;
            }
        }
    }

    /// <summary>
    /// Change combat speed setting
    /// </summary>
    private async Task ChangeCombatSpeed()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("main_street.section_combat_speed"), "bright_cyan");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("main_street.speed_choose"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("main_street.speed_normal_title"), "yellow");
        terminal.WriteLine(Loc.Get("main_street.speed_normal_desc1"));
        terminal.WriteLine(Loc.Get("main_street.speed_normal_desc2"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("main_street.speed_fast_title"), "yellow");
        terminal.WriteLine(Loc.Get("main_street.speed_fast_desc1"));
        terminal.WriteLine(Loc.Get("main_street.speed_fast_desc2"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("main_street.speed_instant_title"), "yellow");
        terminal.WriteLine(Loc.Get("main_street.speed_instant_desc1"));
        terminal.WriteLine(Loc.Get("main_street.speed_instant_desc2"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("main_street.speed_prompt"));

        CombatSpeed? newSpeed = choice switch
        {
            "1" => CombatSpeed.Normal,
            "2" => CombatSpeed.Fast,
            "3" => CombatSpeed.Instant,
            _ => null
        };

        if (newSpeed.HasValue)
        {
            currentPlayer.CombatSpeed = newSpeed.Value;
            string desc = newSpeed.Value switch
            {
                CombatSpeed.Instant => "Instant",
                CombatSpeed.Fast => "Fast",
                _ => "Normal"
            };
            terminal.WriteLine(Loc.Get("main_street.speed_changed", desc), "green");
            await GameEngine.Instance.SaveCurrentGame();
        }

        await Task.Delay(1000);
    }
    
    /// <summary>
    /// Change daily cycle mode
    /// </summary>
    private async Task ChangeDailyCycleMode()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("main_street.section_daily_cycle"), "bright_cyan");
        terminal.WriteLine("");
        
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("main_street.cycle_available"));
        terminal.WriteLine("");
        
        terminal.WriteLine(Loc.Get("main_street.cycle_session_title"), "yellow");
        terminal.WriteLine(Loc.Get("main_street.cycle_session_desc1"));
        terminal.WriteLine(Loc.Get("main_street.cycle_session_desc2"));
        terminal.WriteLine("");
        
        terminal.WriteLine(Loc.Get("main_street.cycle_realtime_title"), "yellow");
        terminal.WriteLine(Loc.Get("main_street.cycle_realtime_desc1"));
        terminal.WriteLine(Loc.Get("main_street.cycle_realtime_desc2"));
        terminal.WriteLine("");
        
        terminal.WriteLine(Loc.Get("main_street.cycle_accel4_title"), "yellow");
        terminal.WriteLine(Loc.Get("main_street.cycle_accel4_desc1"));
        terminal.WriteLine(Loc.Get("main_street.cycle_accel4_desc2"));
        terminal.WriteLine("");
        
        terminal.WriteLine(Loc.Get("main_street.cycle_accel8_title"), "yellow");
        terminal.WriteLine(Loc.Get("main_street.cycle_accel8_desc1"));
        terminal.WriteLine(Loc.Get("main_street.cycle_accel8_desc2"));
        terminal.WriteLine("");
        
        terminal.WriteLine(Loc.Get("main_street.cycle_accel12_title"), "yellow");
        terminal.WriteLine(Loc.Get("main_street.cycle_accel12_desc1"));
        terminal.WriteLine(Loc.Get("main_street.cycle_accel12_desc2"));
        terminal.WriteLine("");
        
        terminal.WriteLine(Loc.Get("main_street.cycle_endless_title"), "yellow");
        terminal.WriteLine(Loc.Get("main_street.cycle_endless_desc1"));
        terminal.WriteLine(Loc.Get("main_street.cycle_endless_desc2"));
        terminal.WriteLine("");
        
        var choice = await terminal.GetInput(Loc.Get("main_street.cycle_prompt"));
        
        var newMode = choice switch
        {
            "1" => DailyCycleMode.SessionBased,
            "2" => DailyCycleMode.RealTime24Hour,
            "3" => DailyCycleMode.Accelerated4Hour,
            "4" => DailyCycleMode.Accelerated8Hour,
            "5" => DailyCycleMode.Accelerated12Hour,
            "6" => DailyCycleMode.Endless,
            _ => (DailyCycleMode?)null
        };
        
        if (newMode.HasValue)
        {
            var dailyManager = DailySystemManager.Instance;
            dailyManager.SetDailyCycleMode(newMode.Value);
            
            terminal.WriteLine(Loc.Get("main_street.cycle_changed", GetDailyCycleModeDescription(newMode.Value)), "green");
            
            // Save the change
            await GameEngine.Instance.SaveCurrentGame();
        }
        else if (choice != "0")
        {
            terminal.WriteLine(Loc.Get("main_street.settings_invalid"), "red");
        }
        
        await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Configure auto-save settings
    /// </summary>
    private async Task ConfigureAutoSave()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("main_street.section_autosave"), "bright_cyan");
        terminal.WriteLine("");
        
        var dailyManager = DailySystemManager.Instance;
        
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("main_street.autosave_current", dailyManager.AutoSaveEnabled ? Loc.Get("main_street.prefs_enabled") : Loc.Get("main_street.prefs_disabled")));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("main_street.autosave_enable"));
        terminal.WriteLine(Loc.Get("main_street.autosave_disable"));
        terminal.WriteLine(Loc.Get("main_street.autosave_change_interval"));
        terminal.WriteLine(Loc.Get("main_street.autosave_back"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("main_street.autosave_prompt"));
        
        switch (choice)
        {
            case "1":
                dailyManager.ConfigureAutoSave(true, TimeSpan.FromMinutes(5));
                terminal.WriteLine(Loc.Get("main_street.autosave_enabled"), "green");
                break;
                
            case "2":
                dailyManager.ConfigureAutoSave(false, TimeSpan.FromMinutes(5));
                terminal.WriteLine(Loc.Get("main_street.autosave_disabled"), "yellow");
                break;
                
            case "3":
                terminal.WriteLine(Loc.Get("main_street.autosave_interval_prompt"));
                var intervalInput = await terminal.GetInput("");
                if (int.TryParse(intervalInput, out var minutes) && minutes >= 1 && minutes <= 60)
                {
                    dailyManager.ConfigureAutoSave(true, TimeSpan.FromMinutes(minutes));
                    terminal.WriteLine(Loc.Get("main_street.autosave_interval_set", minutes), "green");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("main_street.autosave_interval_invalid"), "red");
                }
                break;
                
            case "4":
                return;
                
            default:
                terminal.WriteLine(Loc.Get("main_street.settings_invalid"), "red");
                break;
        }
        
        await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Save game now
    /// </summary>
    private async Task SaveGameNow()
    {
        await GameEngine.Instance.SaveCurrentGame();
        await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Load different save file
    /// </summary>
    private async Task LoadDifferentSave()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("main_street.load_save"), "bright_cyan");
        terminal.WriteLine("");
        
        var saves = SaveSystem.Instance.GetAllSaves();
        
        if (saves.Count == 0)
        {
            terminal.WriteLine(Loc.Get("save.no_saves"), "red");
            await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("main_street.save_available"));
        terminal.WriteLine("");
        
        for (int i = 0; i < saves.Count; i++)
        {
            var save = saves[i];
            terminal.WriteLine(Loc.Get("main_street.save_entry", i + 1, save.PlayerName, save.Level, save.CurrentDay, save.TurnsRemaining));
            terminal.WriteLine(Loc.Get("main_street.save_saved_at", GameConfig.FormatDate(save.SaveTime, currentPlayer.DateFormatPreference, includeTime: true)));
            terminal.WriteLine("");
        }
        
        var choice = await terminal.GetInput(Loc.Get("main_street.save_select", saves.Count));
        
        if (int.TryParse(choice, out var index) && index >= 1 && index <= saves.Count)
        {
            var selectedSave = saves[index - 1];
            terminal.WriteLine(Loc.Get("main_street.save_loading", selectedSave.PlayerName), "yellow");
            
            // This would require restarting the game with the new save
            terminal.WriteLine(Loc.Get("main_street.save_load_note1"), "cyan");
            terminal.WriteLine(Loc.Get("main_street.save_load_note2"), "cyan");
        }
        else if (choice != "0")
        {
            terminal.WriteLine(Loc.Get("main_street.settings_invalid"), "red");
        }
        
        await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Delete save files
    /// </summary>
    private async Task DeleteSaveFiles()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("main_street.section_delete_saves"), "bright_red");
        terminal.WriteLine("");
        
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("main_street.delete_warning"));
        terminal.WriteLine("");
        
        var saves = SaveSystem.Instance.GetAllSaves();
        
        if (saves.Count == 0)
        {
            terminal.WriteLine(Loc.Get("save.no_saves"), "yellow");
            await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
            return;
        }
        
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("main_street.save_available"));
        terminal.WriteLine("");
        
        for (int i = 0; i < saves.Count; i++)
        {
            var save = saves[i];
            terminal.WriteLine(Loc.Get("main_street.save_entry_short", i + 1, save.PlayerName, save.Level, save.CurrentDay));
        }
        
        terminal.WriteLine("");
        var choice = await terminal.GetInput(Loc.Get("main_street.delete_select", saves.Count));
        
        if (int.TryParse(choice, out var index) && index >= 1 && index <= saves.Count)
        {
            var selectedSave = saves[index - 1];
            
            terminal.WriteLine("");
            var confirm = await terminal.GetInput(Loc.Get("ui.confirm_delete", selectedSave.PlayerName));
            
            if (confirm == "DELETE")
            {
                var success = SaveSystem.Instance.DeleteSave(selectedSave.PlayerName);
                if (success)
                {
                    terminal.WriteLine(Loc.Get("main_street.delete_success"), "green");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("main_street.delete_fail"), "red");
                }
            }
            else
            {
                terminal.WriteLine(Loc.Get("main_street.delete_cancelled"), "yellow");
            }
        }
        else if (choice != "0")
        {
            terminal.WriteLine(Loc.Get("main_street.settings_invalid"), "red");
        }
        
        await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// View save file information
    /// </summary>
    private async Task ViewSaveFileInfo()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("main_street.section_save_info"), "bright_cyan");
        terminal.WriteLine("");
        
        var saves = SaveSystem.Instance.GetAllSaves();
        
        if (saves.Count == 0)
        {
            terminal.WriteLine(Loc.Get("save.no_saves"), "red");
            await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
            return;
        }

        terminal.SetColor("white");
        foreach (var save in saves)
        {
            terminal.WriteLine(Loc.Get("save.character", save.PlayerName), "yellow");
            terminal.WriteLine($"{Loc.Get("ui.level")}: {save.Level}");
            terminal.WriteLine(Loc.Get("save.current_day", save.CurrentDay));
            terminal.WriteLine(Loc.Get("save.turns_remaining", save.TurnsRemaining));
            terminal.WriteLine(Loc.Get("save.last_saved", GameConfig.FormatDate(save.SaveTime, currentPlayer.DateFormatPreference, includeTime: true)));
            terminal.WriteLine(Loc.Get("save.file", save.FileName));
            terminal.WriteLine("");
        }
        
        await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Force daily reset
    /// </summary>
    private async Task ForceDailyReset()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("main_street.section_force_reset"), "bright_yellow");
        terminal.WriteLine("");
        
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("main_street.reset_description1"));
        terminal.WriteLine(Loc.Get("main_street.reset_description2"));
        terminal.WriteLine("");
        
        var confirm = await terminal.GetInput(Loc.Get("ui.confirm_yes_no"));
        
        if (confirm.ToLower() == "yes")
        {
            var dailyManager = DailySystemManager.Instance;
            await dailyManager.ForceDailyReset();
            
            terminal.WriteLine(Loc.Get("main_street.reset_completed"), "green");
        }
        else
        {
            terminal.WriteLine(Loc.Get("main_street.reset_cancelled"), "yellow");
        }
        
        await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Get description for daily cycle mode
    /// </summary>
    private string GetDailyCycleModeDescription(DailyCycleMode mode)
    {
        return mode switch
        {
            DailyCycleMode.SessionBased => Loc.Get("main_street.cycle_desc_session"),
            DailyCycleMode.RealTime24Hour => Loc.Get("main_street.cycle_desc_realtime"),
            DailyCycleMode.Accelerated4Hour => Loc.Get("main_street.cycle_desc_accel4"),
            DailyCycleMode.Accelerated8Hour => Loc.Get("main_street.cycle_desc_accel8"),
            DailyCycleMode.Accelerated12Hour => Loc.Get("main_street.cycle_desc_accel12"),
            DailyCycleMode.Endless => Loc.Get("main_street.cycle_desc_endless"),
            _ => Loc.Get("main_street.cycle_desc_unknown")
        };
    }

    /// <summary>
    /// Show player's mailbox using the MailSystem.
    /// </summary>
    private async Task ShowMail()
    {
        terminal.WriteLine(Loc.Get("main_street.mail_checking"), "cyan");
        await MailSystem.ReadPlayerMail(currentPlayer.Name2, terminal);
        terminal.WriteLine(Loc.Get("main_street.mail_return"), "gray");
        await terminal.GetInput("");
    }

    /// <summary>
    /// Show help screen with game commands and tips
    /// </summary>
    private async Task ShowHelp()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("main_street.help"), "bright_cyan");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("help.section_locations"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("help.dungeons"));
        terminal.WriteLine(Loc.Get("help.inn"));
        terminal.WriteLine(Loc.Get("help.weapon_shop"));
        terminal.WriteLine(Loc.Get("help.armor_shop"));
        terminal.WriteLine(Loc.Get("help.magic_shop"));
        terminal.WriteLine(Loc.Get("help.healer"));
        terminal.WriteLine(Loc.Get("help.bank"));
        terminal.WriteLine(Loc.Get("help.temple"));
        terminal.WriteLine(Loc.Get("help.castle"));
        terminal.WriteLine(Loc.Get("help.home"));
        terminal.WriteLine(Loc.Get("help.level_master"));
        terminal.WriteLine(Loc.Get("help.auction"));
        terminal.WriteLine(Loc.Get("help.dark_alley"));
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("help.section_information"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("help.status"));
        terminal.WriteLine(Loc.Get("help.news"));
        terminal.WriteLine(Loc.Get("help.fame"));
        terminal.WriteLine(Loc.Get("help.world_events"));
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("help.section_actions"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("help.good_deeds"));
        terminal.WriteLine(Loc.Get("help.wilderness"));
        terminal.WriteLine(Loc.Get("help.talk_npcs"));
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("help.section_tips"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("help.tip_dungeons"));
        terminal.WriteLine(Loc.Get("help.tip_level"));
        terminal.WriteLine(Loc.Get("help.tip_bank"));
        terminal.WriteLine(Loc.Get("help.tip_npcs"));
        terminal.WriteLine(Loc.Get("help.tip_alignment"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        await terminal.PressAnyKey(Loc.Get("main_street.press_return"));
    }

    /// <summary>
    /// Show current world events affecting the realm
    /// </summary>
    private async Task ShowWorldEvents()
    {
        terminal.ClearScreen();
        WorldEventSystem.Instance.DisplayWorldStatus(terminal);
        terminal.WriteLine("");
        await terminal.PressAnyKey(Loc.Get("ui.press_enter"));
    }

    /// <summary>
    /// Display the player's story progression - seals, gods, awakening, alignment
    /// </summary>
    private async Task ShowStoryProgress()
    {
        var story = StoryProgressionSystem.Instance;
        var ocean = OceanPhilosophySystem.Instance;
        var grief = GriefSystem.Instance;

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("main_street.journey"), "bright_cyan");
        terminal.WriteLine("");

        // === SEALS SECTION ===
        WriteSectionHeader(Loc.Get("main_street.section_seals"), "bright_yellow");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("main_street.seals_desc")}");
        terminal.WriteLine("");

        // Seal status display
        var sealTypes = new[] { SealType.Creation, SealType.FirstWar, SealType.Corruption, SealType.Imprisonment, SealType.Prophecy, SealType.Regret, SealType.Truth };
        var sealNames = new[] { Loc.Get("main_street.seal_creation"), Loc.Get("main_street.seal_first_war"), Loc.Get("main_street.seal_corruption"), Loc.Get("main_street.seal_imprisonment"), Loc.Get("main_street.seal_prophecy"), Loc.Get("main_street.seal_regret"), Loc.Get("main_street.seal_truth") };

        int sealsCollected = story.CollectedSeals?.Count ?? 0;
        terminal.SetColor("white");
        terminal.Write($"  {Loc.Get("main_street.seals_collected", sealsCollected)}   ");

        for (int i = 0; i < sealTypes.Length; i++)
        {
            bool hasIt = story.CollectedSeals?.Contains(sealTypes[i]) ?? false;
            if (hasIt)
            {
                terminal.SetColor("bright_green");
                terminal.Write("[X]");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write("[ ]");
            }
        }
        terminal.WriteLine("");

        // Show detailed seal info (without floor numbers - let players discover them)
        for (int i = 0; i < sealTypes.Length; i++)
        {
            bool hasIt = story.CollectedSeals?.Contains(sealTypes[i]) ?? false;
            string status = hasIt ? "+" : " ";
            string color = hasIt ? "bright_green" : "darkgray";
            string locationHint = hasIt ? Loc.Get("main_street.story_seal_found") : Loc.Get("main_street.story_seal_hidden");
            terminal.SetColor(color);
            terminal.WriteLine(Loc.Get("main_street.seal_display", status, sealNames[i], locationHint));
        }
        terminal.WriteLine("");

        // === GODS SECTION ===
        WriteSectionHeader(Loc.Get("main_street.section_old_gods"), "bright_magenta");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("main_street.gods_desc")}");
        terminal.WriteLine("");

        var allGods = new (OldGodType type, string name, int floor)[]
        {
            (OldGodType.Maelketh, "Maelketh",  25),
            (OldGodType.Veloura,  "Veloura",   40),
            (OldGodType.Thorgrim, "Thorgrim",  55),
            (OldGodType.Noctura,  "Noctura",   70),
            (OldGodType.Aurelion, "Aurelion",  85),
            (OldGodType.Terravok, "Terravok",  95),
            (OldGodType.Manwe,    "Manwe",    100),
        };

        foreach (var (godType, godName, floor) in allGods)
        {
            if (story.OldGodStates.TryGetValue(godType, out var godState) &&
                godState.HasBeenEncountered)
            {
                string statusText = godState.Status switch
                {
                    GodStatus.Defeated  => Loc.Get("main_street.god_defeated"),
                    GodStatus.Consumed  => Loc.Get("main_street.god_defeated"),
                    GodStatus.Saved     => Loc.Get("main_street.god_saved"),
                    GodStatus.Allied    => Loc.Get("main_street.god_saved"),
                    GodStatus.Hostile   => Loc.Get("main_street.god_encountered"),
                    GodStatus.Awakened  => Loc.Get("main_street.god_encountered"),
                    _                   => Loc.Get("main_street.god_encountered"),
                };
                string color = godState.Status switch
                {
                    GodStatus.Defeated or GodStatus.Consumed => "bright_green",
                    GodStatus.Saved    or GodStatus.Allied   => "cyan",
                    _                                        => "bright_yellow",
                };
                terminal.SetColor(color);
                terminal.WriteLine($"    Fl.{floor,-4} {godName,-10} [{statusText}]");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"    Fl.{floor,-4} {"????",-10} [{Loc.Get("main_street.god_unknown")}]");
            }
        }
        terminal.WriteLine("");

        // === AWAKENING SECTION ===
        WriteSectionHeader(Loc.Get("main_street.section_ocean"), "bright_blue");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("main_street.ocean_desc")}");
        terminal.WriteLine("");

        int awakeningLevel = ocean.AwakeningLevel;
        string awakeningDesc = awakeningLevel switch
        {
            0 => Loc.Get("main_street.awakening_0"),
            1 => Loc.Get("main_street.awakening_1"),
            2 => Loc.Get("main_street.awakening_2"),
            3 => Loc.Get("main_street.awakening_3"),
            4 => Loc.Get("main_street.awakening_4"),
            >= 5 => Loc.Get("main_street.awakening_5"),
            _ => Loc.Get("main_street.awakening_unknown")
        };

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  {Loc.Get("main_street.awakening_level", awakeningLevel)}");
        terminal.SetColor("white");
        terminal.WriteLine($"  {awakeningDesc}");
        terminal.WriteLine("");
        // Show per-companion grief details
        var griefDetails = grief.GetActiveGriefDetails();
        if (griefDetails.Count > 0)
        {
            terminal.SetColor("dark_magenta");
            terminal.WriteLine($"  {Loc.Get("grief.health_grieving", griefDetails.Count)}");
            foreach (var (name, stage) in griefDetails)
            {
                string stageLabel = stage switch
                {
                    GriefStage.Denial => Loc.Get("grief.health_stage_denial"),
                    GriefStage.Anger => Loc.Get("grief.health_stage_anger"),
                    GriefStage.Bargaining => Loc.Get("grief.health_stage_bargaining"),
                    GriefStage.Depression => Loc.Get("grief.health_stage_depression"),
                    GriefStage.Acceptance => Loc.Get("grief.health_stage_acceptance"),
                    _ => Loc.Get("grief.health_stage_unknown")
                };
                terminal.SetColor("yellow");
                terminal.Write($"    {name}: ");
                terminal.SetColor(stage == GriefStage.Acceptance ? "bright_green" : "dark_magenta");
                terminal.WriteLine(stageLabel);
            }

            var griefFx = grief.GetCurrentEffects();
            var parts = new List<string>();
            if (griefFx.DamageModifier != 0)
                parts.Add(Loc.Get("main_street.grief_damage", griefFx.DamageModifier > 0 ? "+" : "", $"{griefFx.DamageModifier * 100:0}"));
            if (griefFx.DefenseModifier != 0)
                parts.Add(Loc.Get("main_street.grief_defense", griefFx.DefenseModifier > 0 ? "+" : "", $"{griefFx.DefenseModifier * 100:0}"));
            if (griefFx.CombatModifier != 0)
                parts.Add(Loc.Get("main_street.grief_combat", griefFx.CombatModifier > 0 ? "+" : "", $"{griefFx.CombatModifier * 100:0}"));
            if (griefFx.AllStatModifier != 0)
                parts.Add(Loc.Get("main_street.grief_all_stats", griefFx.AllStatModifier > 0 ? "+" : "", $"{griefFx.AllStatModifier * 100:0}"));
            if (griefFx.PermanentWisdomBonus > 0)
                parts.Add(Loc.Get("main_street.grief_wisdom", griefFx.PermanentWisdomBonus));
            if (parts.Count > 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("main_street.grief_combat_effects", string.Join(", ", parts)));
            }
        }
        else
        {
            string griefStatus = grief.CurrentStage switch
            {
                GriefStage.None => Loc.Get("main_street.grief_none"),
                GriefStage.Acceptance => Loc.Get("main_street.grief_acceptance"),
                _ => Loc.Get("main_street.awakening_unknown")
            };
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("main_street.grief_label")} {griefStatus}");
        }
        terminal.WriteLine("");

        // === ALIGNMENT SECTION ===
        WriteSectionHeader(Loc.Get("main_street.section_alignment"), "bright_white");

        long chivalry = currentPlayer.Chivalry;
        string alignmentDesc;
        string alignColor;
        if (chivalry >= 100)
        {
            alignmentDesc = Loc.Get("main_street.align_paragon");
            alignColor = "bright_cyan";
        }
        else if (chivalry >= 50)
        {
            alignmentDesc = Loc.Get("main_street.align_noble");
            alignColor = "bright_green";
        }
        else if (chivalry >= 20)
        {
            alignmentDesc = Loc.Get("main_street.align_good");
            alignColor = "green";
        }
        else if (chivalry >= -20)
        {
            alignmentDesc = Loc.Get("main_street.align_neutral");
            alignColor = "gray";
        }
        else if (chivalry >= -50)
        {
            alignmentDesc = Loc.Get("main_street.align_questionable");
            alignColor = "yellow";
        }
        else if (chivalry >= -100)
        {
            alignmentDesc = Loc.Get("main_street.align_villain");
            alignColor = "red";
        }
        else
        {
            alignmentDesc = Loc.Get("main_street.align_usurper");
            alignColor = "bright_red";
        }

        terminal.SetColor(alignColor);
        terminal.WriteLine($"  {Loc.Get("main_street.chivalry_label")} {chivalry,4}  -  {alignmentDesc}");

        // Show Darkness and wanted status
        long darkness = currentPlayer.Darkness;
        string darkDesc;
        string darkColor;
        if (darkness > 100)
        {
            darkDesc = Loc.Get("main_street.dark_wanted");
            darkColor = "bright_red";
        }
        else if (darkness > 50)
        {
            darkDesc = Loc.Get("main_street.dark_suspicious");
            darkColor = "yellow";
        }
        else if (darkness > 20)
        {
            darkDesc = Loc.Get("main_street.dark_rumored");
            darkColor = "gray";
        }
        else
        {
            darkDesc = Loc.Get("main_street.dark_clean");
            darkColor = "bright_green";
        }
        terminal.SetColor(darkColor);
        terminal.WriteLine($"  {Loc.Get("main_street.darkness_label")} {darkness,4}  -  {darkDesc}");

        // Next objective hint (vague to encourage exploration)
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        if (sealsCollected < 7)
        {
            terminal.WriteLine($"  {Loc.Get("main_street.hint_seals")}");
        }
        else if (!story.HasStoryFlag("manwe_destroyed") && !story.HasStoryFlag("manwe_saved"))
        {
            terminal.WriteLine($"  {Loc.Get("main_street.hint_creator")}");
        }
        else
        {
            terminal.WriteLine($"  {Loc.Get("main_street.hint_ending")}");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        await terminal.PressAnyKey(Loc.Get("main_street.press_return"));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // World Boss — delegates to WorldBossSystem (v0.48.2)
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ShowWorldBossMenu()
    {
        await WorldBossSystem.Instance.ShowWorldBossUI(currentPlayer, terminal);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Street Micro-Events — surface real NPC simulation state (v0.49.6)
    // ═══════════════════════════════════════════════════════════════════════════

    private string? GenerateStreetMicroEvent()
    {
        var npcs = GetLiveNPCsAtLocation();
        if (npcs.Count < 1) return null;

        // Priority 1: Married/lover pair both at Main Street
        for (int i = 0; i < npcs.Count && i < 6; i++)
        {
            for (int j = i + 1; j < npcs.Count && j < 6; j++)
            {
                if (RelationshipSystem.AreMarried(npcs[i], npcs[j]))
                    return Loc.Get("main_street.micro_married_walk", npcs[i].Name2, npcs[j].Name2);
                int rel = RelationshipSystem.GetRelationshipLevel(npcs[i], npcs[j]);
                if (rel <= GameConfig.RelationLove)
                    return Loc.Get("main_street.micro_lovers_walk", npcs[i].Name2, npcs[j].Name2);
            }
        }

        // Priority 2-7: Check individual NPCs (shuffle for variety)
        var shuffled = npcs.OrderBy(_ => Random.Shared.Next()).ToList();
        foreach (var npc in shuffled)
        {
            var recentEvents = npc.Brain?.Memory?.GetRecentEvents(24);
            if (recentEvents != null)
            {
                // Won a fight recently
                var defeated = recentEvents.FirstOrDefault(e => e.Type == MemoryType.Defeated);
                if (defeated != null)
                    return Loc.Get("main_street.micro_fight_won", npc.Name2, defeated.InvolvedCharacter);

                // Witnessed a death
                var sawDeath = recentEvents.FirstOrDefault(e => e.Type == MemoryType.SawDeath);
                if (sawDeath != null)
                    return Loc.Get("main_street.micro_saw_death", npc.Name2);

                // Was attacked / in a fight
                var attacked = recentEvents.FirstOrDefault(e => e.Type == MemoryType.Attacked);
                if (attacked != null)
                    return Loc.Get("main_street.micro_was_attacked", npc.Name2);
            }

            // Emotional state checks
            if (npc.EmotionalState != null)
            {
                if (npc.EmotionalState.HasEmotion(EmotionType.Anger))
                    return Loc.Get("main_street.micro_angry", npc.Name2);
                if (npc.EmotionalState.HasEmotion(EmotionType.Joy))
                    return Loc.Get("main_street.micro_joyful", npc.Name2);
                if (npc.EmotionalState.HasEmotion(EmotionType.Sadness))
                    return Loc.Get("main_street.micro_sad", npc.Name2);
                if (npc.EmotionalState.HasEmotion(EmotionType.Fear))
                    return Loc.Get("main_street.micro_fearful", npc.Name2);
            }
        }

        // Priority 8: Gang members present together
        var gangNpcs = npcs.Where(n => !string.IsNullOrEmpty(n.GangId)).ToList();
        if (gangNpcs.Count >= 2)
            return Loc.Get("main_street.micro_gang_huddle");

        // Priority 9: Enemy pair
        for (int i = 0; i < npcs.Count && i < 5; i++)
        {
            for (int j = i + 1; j < npcs.Count && j < 5; j++)
            {
                if (RelationshipSystem.GetRelationshipLevel(npcs[i], npcs[j]) >= GameConfig.RelationEnemy)
                    return Loc.Get("main_street.micro_enemies_wary", npcs[i].Name2, npcs[j].Name2);
            }
        }

        // Priority 10: NPC with an emergent role
        foreach (var npc in shuffled)
        {
            if (!string.IsNullOrEmpty(npc.EmergentRole))
            {
                return npc.EmergentRole switch
                {
                    "Defender" => Loc.Get("main_street.micro_role_defender", npc.Name2),
                    "Merchant" => Loc.Get("main_street.micro_role_merchant", npc.Name2),
                    "Healer" => Loc.Get("main_street.micro_role_healer", npc.Name2),
                    "Explorer" => Loc.Get("main_street.micro_role_explorer", npc.Name2),
                    _ => null
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Display the Guild Board — rankings, your guild status, and how to join/create.
    /// Online mode only.
    /// </summary>
    private async Task ShowGuildBoard()
    {
        var guild = GuildSystem.Instance;
        if (guild == null)
        {
            terminal.WriteLine($"  {Loc.Get("guild.online_only")}", "yellow");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        string boardTitle = Loc.Get("guild.board_title");
        if (!IsScreenReader)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  ╔══════════════════════════════════════════════════╗");
            terminal.WriteLine($"  ║              {boardTitle,-36}║");
            terminal.WriteLine("  ╠══════════════════════════════════════════════════╣");
        }
        else
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  --- {boardTitle} ---");
        }

        // Get all guilds
        var allGuilds = guild.GetAllGuilds();

        if (allGuilds.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("guild.board_no_guilds")}");
            terminal.WriteLine($"  {Loc.Get("guild.board_hint_create")}");
        }
        else
        {
            // Guild rankings
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  {Loc.Get("guild.board_col_rank"),-5} {Loc.Get("guild.board_col_guild"),-22} {Loc.Get("guild.board_col_members"),-9} {Loc.Get("guild.board_col_leader"),-16} {Loc.Get("guild.board_col_bank"),8}");
            terminal.SetColor("gray");
            terminal.WriteLine("  " + new string('-', 62));

            int rank = 1;
            foreach (var g in allGuilds.Take(10))
            {
                // Check online members (use Name key, not DisplayName)
                var onlineMembers = guild.GetOnlineGuildMembers(g.Name);
                string memberStr = onlineMembers.Count > 0
                    ? Loc.Get("guild.board_online_suffix", g.MemberCount, onlineMembers.Count)
                    : $"{g.MemberCount}";

                // Truncate names for display
                string guildName = g.DisplayName.Length > 20 ? g.DisplayName[..20] + ".." : g.DisplayName;
                string leaderName = !string.IsNullOrEmpty(g.LeaderDisplayName) ? g.LeaderDisplayName : g.LeaderUsername;
                string leaderDisplay = leaderName.Length > 14 ? leaderName[..14] + ".." : leaderName;

                // Color based on rank
                string color = rank == 1 ? "bright_yellow" : rank <= 3 ? "bright_cyan" : "white";
                terminal.SetColor(color);
                terminal.WriteLine($"  {("#" + rank),-5} {guildName,-22} {memberStr,-9} {leaderDisplay,-16} {g.BankGold,8:N0}g");
                rank++;
            }

            if (allGuilds.Count > 10)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("guild.board_more_guilds", allGuilds.Count - 10)}");
            }
        }

        // Show player's guild status
        terminal.WriteLine("");
        string? playerGuild = guild.GetPlayerGuild(currentPlayer.Name1 ?? "");
        if (playerGuild != null)
        {
            var info = guild.GetGuildInfo(playerGuild);
            if (info != null)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {Loc.Get("guild.board_your_guild", info.DisplayName)}");
                terminal.SetColor("white");
                double bonus = Math.Min(info.MemberCount * GuildSystem.GuildXPBonusPerMember, GuildSystem.MaxGuildXPBonus) * 100;
                terminal.WriteLine($"  {Loc.Get("guild.board_member_info", info.MemberCount, GuildSystem.MaxGuildMembers, info.BankGold.ToString("N0"), bonus.ToString("F0"))}");
                if (!string.IsNullOrEmpty(info.Motto))
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  \"{info.Motto}\"");
                }
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("guild.board_not_in_guild")}");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("guild.board_hint_create_short")}");
            terminal.WriteLine($"  {Loc.Get("guild.board_hint_invite")}");
        }

        if (!IsScreenReader)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  ╚══════════════════════════════════════════════════╝");
        }
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }
}
