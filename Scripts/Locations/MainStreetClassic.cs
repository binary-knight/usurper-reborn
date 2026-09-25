using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// v1.1.14: the classic Main Street, the layout before the v1.1.13 districts (git tag mainstreet-classic),
/// for a character with the ClassicMainStreet preference. The rows, labels, colours and keys are the
/// tagged code's. What each row shows is decided by the district table (StreetEntries), the single
/// source of unlock tiers, so the classic menu can never show a place the districts keep locked.
/// </summary>
public partial class MainStreetLocation
{
    /// <summary>v1.1.14: a classic menu item. Place is null for Quit and Settings, which always show.</summary>
    internal sealed record ClassicItem(string Key, StreetPlace? Place, string LabelKey, string Color = "white");

    private static ClassicItem CI(string key, StreetPlace? place, string labelKey, string color = "white") => new(key, place, labelKey, color);

    // v1.1.14: the tagged ShowClassicMenu rows, in order; a blank line follows the fourth row
    internal static readonly ClassicItem[][] ClassicVisualRows =
    {
        new[] { CI("D", StreetPlace.Dungeons, "menu.action.dungeon"), CI("I", StreetPlace.Inn, "menu.action.inn"),
                CI("T", StreetPlace.Temple, "menu.action.temple"), CI("O", StreetPlace.OldChurch, "menu.action.old_church") },
        new[] { CI("W", StreetPlace.WeaponShop, "menu.action.weapon_shop"), CI("A", StreetPlace.ArmorShop, "menu.action.armor_shop"),
                CI("M", StreetPlace.MagicShop, "menu.action.magic_shop"), CI("U", StreetPlace.MusicShop, "menu.action.music_shop", "cyan"),
                CI("J", StreetPlace.Auctions, "menu.action.auction_house") },
        new[] { CI("B", StreetPlace.Bank, "menu.action.bank"), CI("1", StreetPlace.Healer, "menu.action.healer"),
                CI("2", StreetPlace.QuestHall, "menu.action.quest_hall"), CI("V", StreetPlace.LevelMaster, "menu.action.level_master") },
        new[] { CI("K", StreetPlace.Castle, "menu.action.castle"), CI("H", StreetPlace.Home, "menu.action.home"),
                CI("C", StreetPlace.Challenges, "menu.action.challenges"), CI("L", StreetPlace.Lodging, "menu.action.lodging_short"),
                CI("Z", StreetPlace.TeamCorner, "menu.action.team_corner") },
        new[] { CI("S", StreetPlace.Status, "menu.action.status"), CI("N", StreetPlace.News, "menu.action.news"),
                CI("F", StreetPlace.Fame, "menu.action.fame"), CI("E", StreetPlace.Explore, "main_street.classic_explore", "bright_green"),
                CI("$", StreetPlace.WorldEvents, "menu.action.world_events") },
        new[] { CI("=", StreetPlace.StatsRecord, "menu.action.stats_record"), CI("P", StreetPlace.Progress, "menu.action.progress"),
                CI(">", StreetPlace.Settlement, "menu.action.settlement", "bright_green") },
        new[] { CI("Y", StreetPlace.DarkAlley, "menu.action.dark_alley", "gray"), CI("+", StreetPlace.Sanctum, "menu.action.sanctum", "bright_yellow"),
                CI("X", StreetPlace.LoveStreet, "menu.action.love_street", "magenta"), CI("Q", null, "menu.action.quit_game", "gray"),
                CI("~", null, "menu.action.settings", "gray") },
    };

    // v1.1.14: the tagged ShowScreenReaderMenu sections (header key, items)
    internal static readonly (string Header, ClassicItem[] Items)[] ClassicScreenReaderSections =
    {
        ("main_street.section_locations", new[] { CI("D", StreetPlace.Dungeons, "menu.action.dungeon"), CI("I", StreetPlace.Inn, "menu.action.inn"),
            CI("T", StreetPlace.Temple, "menu.action.temple"), CI("K", StreetPlace.Castle, "menu.action.castle"), CI("H", StreetPlace.Home, "menu.action.home"),
            CI("O", StreetPlace.OldChurch, "menu.action.church"), CI("L", StreetPlace.Lodging, "menu.action.lodging") }),
        ("main_street.section_shops", new[] { CI("W", StreetPlace.WeaponShop, "menu.action.weapon_shop"), CI("A", StreetPlace.ArmorShop, "menu.action.armor_shop"),
            CI("M", StreetPlace.MagicShop, "menu.action.magic_shop"), CI("U", StreetPlace.MusicShop, "menu.action.music_shop"),
            CI("J", StreetPlace.Auctions, "menu.action.auction_house"), CI("B", StreetPlace.Bank, "menu.action.bank"), CI("1", StreetPlace.Healer, "menu.action.healer") }),
        ("main_street.section_services", new[] { CI("V", StreetPlace.LevelMaster, "menu.action.level_master"), CI("2", StreetPlace.QuestHall, "menu.action.quest_hall"),
            CI("C", StreetPlace.Challenges, "menu.action.challenges"), CI("Z", StreetPlace.TeamCorner, "menu.action.team_corner") }),
        ("main_street.section_info", new[] { CI("S", StreetPlace.Status, "menu.action.status"), CI("N", StreetPlace.News, "menu.action.news"),
            CI("F", StreetPlace.Fame, "menu.action.fame"), CI("$", StreetPlace.WorldEvents, "menu.action.world_events"),
            CI("=", StreetPlace.StatsRecord, "menu.action.stats_record"), CI("P", StreetPlace.Progress, "menu.action.progress") }),
        ("main_street.section_exploration_label", new[] { CI("E", StreetPlace.Explore, "menu.action.wilderness"), CI(">", StreetPlace.Settlement, "menu.action.settlement") }),
        ("main_street.section_other", new[] { CI("Y", StreetPlace.DarkAlley, "menu.action.dark_alley"), CI("+", StreetPlace.Sanctum, "menu.action.sanctum"),
            CI("X", StreetPlace.LoveStreet, "menu.action.love_street"), CI("Q", null, "menu.action.quit"), CI("?", null, "menu.action.help"),
            CI("!", null, "menu.action.report_bug") }),
    };

    // v1.1.14: the tagged DisplayLocationBBS menu rows; the Team Corner row decides the Quit row's indent
    internal static readonly ClassicItem[][] ClassicBbsRows =
    {
        new[] { CI("D", StreetPlace.Dungeons, "main_street.menu_dungeons_suffix"), CI("W", StreetPlace.WeaponShop, "main_street.menu_weapon_suffix"),
                CI("A", StreetPlace.ArmorShop, "main_street.menu_armor_suffix"), CI("M", StreetPlace.MagicShop, "main_street.menu_magic_suffix"),
                CI("U", StreetPlace.MusicShop, "menu.action.music_shop", "cyan") },
        new[] { CI("I", StreetPlace.Inn, "main_street.menu_inn_suffix"), CI("1", StreetPlace.Healer, "main_street.menu_healer"),
                CI("2", StreetPlace.QuestHall, "main_street.menu_quest_hall"), CI("V", StreetPlace.LevelMaster, "main_street.menu_master") },
        new[] { CI("B", StreetPlace.Bank, "main_street.menu_bank_suffix"), CI("T", StreetPlace.Temple, "main_street.menu_temple_suffix"),
                CI("K", StreetPlace.Castle, "main_street.menu_castle"), CI("H", StreetPlace.Home, "main_street.menu_home_suffix") },
        new[] { CI("N", StreetPlace.News, "main_street.menu_news_suffix"), CI("F", StreetPlace.Fame, "main_street.menu_fame_suffix"),
                CI("E", StreetPlace.Explore, "main_street.menu_explore_suffix", "bright_green"), CI("$", StreetPlace.WorldEvents, "main_street.menu_events_suffix") },
        new[] { CI("Y", StreetPlace.DarkAlley, "main_street.menu_dark_alley", "gray"), CI("+", StreetPlace.Sanctum, "main_street.menu_sanctum", "bright_yellow"),
                CI("X", StreetPlace.LoveStreet, "main_street.menu_love_st", "magenta"), CI("O", StreetPlace.OldChurch, "main_street.menu_church_suffix"),
                CI("J", StreetPlace.Auctions, "main_street.menu_auction") },
        new[] { CI("C", StreetPlace.Challenges, "main_street.menu_challenges_suffix"), CI("L", StreetPlace.Lodging, "main_street.menu_lodging_suffix"),
                CI("=", StreetPlace.StatsRecord, "main_street.menu_stats"), CI("P", StreetPlace.Progress, "main_street.menu_progress_suffix") },
        new[] { CI("Z", StreetPlace.TeamCorner, "main_street.menu_team_corner"), CI(">", StreetPlace.Settlement, "main_street.menu_outskirts", "bright_green") },
    };

    /// <summary>v1.1.14: true when this character asked for the classic Main Street.</summary>
    private bool UseClassicLayout => currentPlayer?.ClassicMainStreet == true;

    /// <summary>v1.1.14: whether a classic item shows, from the district table's tiers (Quit, Settings, Help and Bug always).</summary>
    internal static bool ClassicShows(ClassicItem item, StreetView v) =>
        item.Place == null || IsUnlocked(StreetEntries.Single(e => e.Place == item.Place), v);

    internal static List<ClassicItem> ClassicShown(IEnumerable<ClassicItem> row, StreetView v) =>
        row.Where(i => ClassicShows(i, v)).ToList();

    /// <summary>
    /// v1.1.14: the "New on Main Street" line for a tier rise in the classic layout: the places that opened,
    /// in classic menu order and with the classic menu's labels. Online places and the settlement are not announced.
    /// </summary>
    internal static List<string> ClassicUnlockLines(int fromTier, int toTier)
    {
        var before = new StreetView(fromTier, false, false);
        var after = new StreetView(toTier, false, false);
        var opened = ClassicVisualRows.SelectMany(r => r)
            .Where(i => i.Place != null && i.Place != StreetPlace.Settlement)
            .Where(i => ClassicShows(i, after) && !ClassicShows(i, before))
            .Select(i => Loc.Get(i.LabelKey)).ToList();
        return opened.Count == 0 ? new List<string>() : new List<string> { Loc.Get("main_street.unlock_announce", string.Join(", ", opened)) };
    }

    /// <summary>
    /// Show the classic Main Street menu layout (v0.4 style)
    /// Progressive disclosure: Tier 1 (Lv1-2) core loop, Tier 2 (Lv3-4) town services, Tier 3 (Lv5+) full menu.
    /// All keys still work at all levels, only the display is gated.
    /// </summary>
    private void ShowClassicLayoutMenu()
    {
        var view = CurrentStreetView(); // v1.1.14: the district table decides what shows
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

        // v1.1.14: each tagged row, drawn when anything in it is unlocked; the last item ends the line
        for (int r = 0; r < ClassicVisualRows.Length; r++)
        {
            var items = ClassicShown(ClassicVisualRows[r], view);
            if (items.Count > 0)
            {
                terminal.Write(" ");
                for (int i = 0; i < items.Count; i++)
                {
                    if (i < items.Count - 1) MI(items[i].Key, Loc.Get(items[i].LabelKey), items[i].Color, C);
                    else ML(items[i].Key, Loc.Get(items[i].LabelKey), items[i].Color);
                }
            }
            if (r == 3) terminal.WriteLine("");
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

            if (ClassicOnlineShows(StreetPlace.WhosOnline, view))
            {
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
            }

            terminal.SetColor("darkgray");
            terminal.Write(" ");
            if (ClassicOnlineShows(StreetPlace.Chat, view))
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("4");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write(Loc.Get("main_street.menu_chat_padded"));
            }

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("5");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("main_street.menu_news_padded"));

            if (ClassicOnlineShows(StreetPlace.Arena, view))
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("6");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write(Loc.Get("main_street.menu_arena_padded"));
            }

            if (ClassicOnlineShows(StreetPlace.WorldBoss, view))
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("7");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write(Loc.Get("main_street.world_boss"));
            }

            if (ClassicOnlineShows(StreetPlace.Guilds, view))
            {
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("R");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write(Loc.Get("menu.action.guilds"));
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        // v1.1.14: the tagged menu ended with a lone bottom border ("╚═══╝") that closed no box; it is left out
        terminal.SetColor("white");
        terminal.WriteLine("");
    }

    /// <summary>v1.1.14: an online item of the classic menu shows when the district table unlocks it.</summary>
    private static bool ClassicOnlineShows(StreetPlace place, StreetView v) =>
        IsUnlocked(StreetEntries.Single(e => e.Place == place), v);

    /// <summary>
    /// Show simplified menu for screen readers - plain text, one option per line
    /// </summary>
    private void ShowClassicLayoutScreenReaderMenu()
    {
        var view = CurrentStreetView(); // v1.1.14: the district table decides what shows
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("main_street.menu_title"));
        terminal.WriteLine("");

        // v1.1.14: each tagged section with its unlocked items. Information has its header only when it
        // lists more than Status, as the tagged menu did below tier 2; an empty section is left out.
        foreach (var (header, all) in ClassicScreenReaderSections)
        {
            var items = ClassicShown(all, view);
            if (items.Count == 0) continue;
            bool statusOnly = header == "main_street.section_info" && items.Count == 1;
            if (!statusOnly) terminal.WriteLine(Loc.Get(header));
            foreach (var item in items)
                terminal.WriteLine($"  {item.Key} - {Loc.Get(item.LabelKey)}");
            terminal.WriteLine("");
        }

        if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
        {
            terminal.WriteLine(Loc.Get("main_street.online_label"));
            if (ClassicOnlineShows(StreetPlace.WhosOnline, view)) terminal.WriteLine($"  3 - {Loc.Get("main_street.whos_online")}");
            if (ClassicOnlineShows(StreetPlace.Chat, view)) terminal.WriteLine($"  4 - {Loc.Get("main_street.chat")}");
            terminal.WriteLine($"  5 - {Loc.Get("main_street.news_feed")}");
            if (ClassicOnlineShows(StreetPlace.Arena, view)) terminal.WriteLine($"  6 - {Loc.Get("main_street.arena_pvp")}");
            if (ClassicOnlineShows(StreetPlace.WorldBoss, view)) terminal.WriteLine($"  7 - {Loc.Get("main_street.world_boss")}");
            if (ClassicOnlineShows(StreetPlace.Guilds, view)) terminal.WriteLine($"  R - {Loc.Get("menu.action.guilds")}");
            terminal.WriteLine($"  /say message - {Loc.Get("main_street.broadcast_chat")}");
            terminal.WriteLine($"  /tell player message - {Loc.Get("main_street.private_message")}");
            terminal.WriteLine($"  /who - {Loc.Get("main_street.see_online")}");
            terminal.WriteLine($"  /news - {Loc.Get("main_street.recent_news")}");
            terminal.WriteLine("");
        }
    }

    /// <summary>v1.1.14: the tagged DisplayLocationBBS menu rows (Menu rows, progressive disclosure based on player level).</summary>
    private void WriteClassicLayoutBBSMenu()
    {
        var view = CurrentStreetView(); // v1.1.14: the district table decides what shows

        bool teamRowDrawn = false;
        foreach (var row in ClassicBbsRows)
        {
            var items = ClassicShown(row, view);
            if (items.Count == 0) continue;
            for (int i = 0; i < items.Count; i++)
            {
                terminal.SetColor("darkgray"); terminal.Write(i == 0 ? " [" : "["); terminal.SetColor("bright_yellow"); terminal.Write(items[i].Key); terminal.SetColor("darkgray"); terminal.Write("]");
                terminal.SetColor(items[i].Color); terminal.Write(Loc.Get(items[i].LabelKey));
            }
            terminal.WriteLine("");
            if (row == ClassicBbsRows[^1]) teamRowDrawn = true;
        }

        // Always: Quit + Settings
        terminal.SetColor("darkgray");
        if (!teamRowDrawn) terminal.Write(" "); // indent if not continuing a row
        terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("Q"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("gray"); terminal.Write(Loc.Get("main_street.menu_quit_suffix"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("~"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("gray"); terminal.WriteLine(Loc.Get("main_street.menu_settings"));

        // Compact mode number-key hint (offline only, online mode has its own number row)
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
            if (ClassicOnlineShows(StreetPlace.WhosOnline, view))
            {
                terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("3"); terminal.SetColor("darkgray"); terminal.Write("]");
                terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.who_count", onlineCount));
            }
            if (ClassicOnlineShows(StreetPlace.Chat, view))
            {
                terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("4"); terminal.SetColor("darkgray"); terminal.Write("]");
                terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_chat_short"));
            }
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("5"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_news_short"));
            if (ClassicOnlineShows(StreetPlace.Arena, view))
            {
                terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("6"); terminal.SetColor("darkgray"); terminal.Write("]");
                terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_arena_short"));
            }
            if (ClassicOnlineShows(StreetPlace.WorldBoss, view))
            {
                terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("7"); terminal.SetColor("darkgray"); terminal.Write("]");
                terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_boss_short"));
            }
            if (ClassicOnlineShows(StreetPlace.Guilds, view))
            {
                terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("R"); terminal.SetColor("darkgray"); terminal.Write("]");
                terminal.SetColor("white"); terminal.Write(Loc.Get("menu.action.guilds"));
            }
            terminal.WriteLine("");
        }
    }

    /// <summary>v1.1.14: the tagged DisplayLocationBBS "Line 15: Quick commands (compact)".</summary>
    private void WriteClassicLayoutBBSQuickCommands(int npcCount)
    {
        terminal.SetColor("darkgray");
        terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("S"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_status_suffix"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("*"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_inv"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("?"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_help_short"));
        if (npcCount > 0)
        {
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("0"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.talk_count", npcCount));
        }
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("~"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_prefs"));
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("!"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("main_street.menu_bug"));
        terminal.WriteLine("");
    }

    /// <summary>
    /// v1.1.14: the tagged ProcessChoice, for the classic layout. Every classic key does what it did,
    /// at every level as before (only the display is gated). The global quick commands (*, ?, !, 0, ~,
    /// % and the slash commands) are taken first, as they were, so the tagged cases for those keys never
    /// ran and are not repeated here.
    /// </summary>
    private async Task<bool> ProcessClassicChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        // Compact mode: map number keys to common locations for touch-friendly input
        // Only in offline mode, online mode already uses 3-7 for online features
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

            case "=":
                await ShowStatistics();
                return false;

            case "U":
                await NavigateToLocation(GameLocation.MusicShop);
                return true;

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

            case "Y":
                terminal.WriteLine(Loc.Get("main_street.nav_dark_alley"), "gray");
                await Task.Delay(1500);
                throw new LocationExitException(GameLocation.DarkAlley);

            case "+":
                // v0.62.x Phase 6: The Sanctum -- Light activity hub (yin/yang mirror of Dark Alley).
                // Evil players are wards-barred at the door inside AlignmentSystem.CanAccessLocation.
                terminal.WriteLine(Loc.Get("main_street.nav_sanctum"), "bright_yellow");
                await Task.Delay(1500);
                throw new LocationExitException(GameLocation.Sanctum);

            case ">":
                if (UsurperRemake.Systems.SettlementSystem.Instance?.State.IsEstablished == true)
                {
                    terminal.WriteLine(Loc.Get("main_street.nav_settlement"), "gray");
                    await Task.Delay(1500);
                    throw new LocationExitException(GameLocation.Settlement);
                }
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

            // v0.60.6 security removal: bare-word "SETTINGS" / "CONFIG" aliases removed (see ProcessChoice).

            case "MAIL":
            case "CTRL+M":
            case "!M":
                await ShowMail();
                return false;

            // Dev menu removed in v0.53.7, use admin console instead
            case "DEV":
            case "CHEATER":
            case "DEVMENU":
                terminal.WriteLine("  The dev menu has been removed. Use the admin console.", "gray");
                return false;

            default:
                terminal.WriteLine(Loc.Get("main_street.invalid_choice"), "red");
                await Task.Delay(1500);
                return false;
        }
    }
}
