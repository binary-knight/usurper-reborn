using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// v1.1.13: Main Street as a few direct places plus districts and hubs. One table drives every
/// renderer (visual, screen reader, BBS), the key handling, the help screen and the unlock lines.
/// </summary>
public partial class MainStreetLocation
{
    internal enum StreetGroup { MainStreet, MerchantRow, GuildRow, HomeHearth, CastleGrounds, StatusHub, NoticeBoard, Online }

    internal enum StreetPlace
    {
        Dungeons, Inn, Explore, TeamCorner, DarkAlley,
        WeaponShop, ArmorShop, MagicShop, MusicShop, Auctions,
        Healer, Temple, OldChurch, Bank, LevelMaster, QuestHall,
        Home, Lodging, LoveStreet,
        Castle, Challenges, Sanctum, Settlement,
        Status, Progress, StatsRecord, Fame, GoodDeeds,
        News, WorldEvents,
        WhosOnline, Chat, Arena, WorldBoss, Guilds
    }

    internal sealed record StreetEntry(StreetGroup Group, string Key, StreetPlace Place, string LabelKey, int Tier, string Color = "white");

    internal sealed record District(StreetGroup Group, string Key, string NameKey, string DescKey);

    /// <summary>What decides which places show: the menu tier, online mode, an established settlement.</summary>
    internal readonly record struct StreetView(int Tier, bool Online, bool Settlement);

    /// <summary>A line of the Main Street menu: a place, a district, or Settings, Help and Quit (Row 2).</summary>
    internal sealed record StreetLine(string Key, string Label, string Color, int Row, StreetEntry? Entry, District? District);

    // v1.1.13: every place, its screen, its key there and the menu tier that shows it (GetMenuTier's levels)
    internal static readonly StreetEntry[] StreetEntries =
    {
        new(StreetGroup.MainStreet, "D", StreetPlace.Dungeons, "menu.action.dungeon", 1),
        new(StreetGroup.MainStreet, "I", StreetPlace.Inn, "menu.action.inn", 1),
        new(StreetGroup.MainStreet, "E", StreetPlace.Explore, "menu.action.explore", 2, "bright_green"),
        new(StreetGroup.MainStreet, "T", StreetPlace.TeamCorner, "menu.action.team_corner", 3),
        new(StreetGroup.MainStreet, "A", StreetPlace.DarkAlley, "menu.action.dark_alley", 3, "gray"),

        new(StreetGroup.MerchantRow, "W", StreetPlace.WeaponShop, "menu.action.weapon_shop", 1),
        new(StreetGroup.MerchantRow, "A", StreetPlace.ArmorShop, "menu.action.armor_shop", 1),
        new(StreetGroup.MerchantRow, "M", StreetPlace.MagicShop, "menu.action.magic_shop", 1),
        new(StreetGroup.MerchantRow, "U", StreetPlace.MusicShop, "menu.action.music_shop", 1, "cyan"),
        new(StreetGroup.MerchantRow, "J", StreetPlace.Auctions, "menu.action.auction_house", 3),

        new(StreetGroup.GuildRow, "H", StreetPlace.Healer, "menu.action.healer", 1),
        new(StreetGroup.GuildRow, "T", StreetPlace.Temple, "menu.action.temple", 2),
        new(StreetGroup.GuildRow, "O", StreetPlace.OldChurch, "menu.action.old_church", 2),
        new(StreetGroup.GuildRow, "B", StreetPlace.Bank, "menu.action.bank", 2),
        new(StreetGroup.GuildRow, "V", StreetPlace.LevelMaster, "menu.action.level_master", 1),
        new(StreetGroup.GuildRow, "U", StreetPlace.QuestHall, "menu.action.quest_hall", 1),

        new(StreetGroup.HomeHearth, "H", StreetPlace.Home, "menu.action.home", 2),
        new(StreetGroup.HomeHearth, "L", StreetPlace.Lodging, "menu.action.lodging_short", 3),
        new(StreetGroup.HomeHearth, "X", StreetPlace.LoveStreet, "menu.action.love_street", 3, "magenta"),

        new(StreetGroup.CastleGrounds, "K", StreetPlace.Castle, "menu.action.castle", 2),
        new(StreetGroup.CastleGrounds, "C", StreetPlace.Challenges, "menu.action.challenges", 3),
        new(StreetGroup.CastleGrounds, "S", StreetPlace.Sanctum, "menu.action.sanctum", 3, "bright_yellow"),
        new(StreetGroup.CastleGrounds, "E", StreetPlace.Settlement, "menu.action.settlement", 3, "bright_green"),

        new(StreetGroup.StatusHub, "S", StreetPlace.Status, "menu.action.status", 1),
        new(StreetGroup.StatusHub, "P", StreetPlace.Progress, "menu.action.progress", 3),
        new(StreetGroup.StatusHub, "T", StreetPlace.StatsRecord, "menu.action.stats_record", 3),
        new(StreetGroup.StatusHub, "F", StreetPlace.Fame, "menu.action.fame", 2),
        new(StreetGroup.StatusHub, "G", StreetPlace.GoodDeeds, "main_street.good_deeds_title", 1),

        new(StreetGroup.NoticeBoard, "N", StreetPlace.News, "menu.action.news", 2),
        new(StreetGroup.NoticeBoard, "W", StreetPlace.WorldEvents, "menu.action.world_events", 2),

        new(StreetGroup.Online, "W", StreetPlace.WhosOnline, "main_street.whos_online", 1),
        new(StreetGroup.Online, "C", StreetPlace.Chat, "main_street.chat", 1),
        new(StreetGroup.Online, "A", StreetPlace.Arena, "main_street.arena_pvp", 1),
        new(StreetGroup.Online, "B", StreetPlace.WorldBoss, "main_street.world_boss", 1),
        new(StreetGroup.Online, "G", StreetPlace.Guilds, "menu.action.guilds", 1),
    };

    internal static readonly District[] Districts =
    {
        new(StreetGroup.MerchantRow, "M", "main_street.district_merchant_row", "main_street.district_merchant_row_desc"),
        new(StreetGroup.GuildRow, "G", "main_street.district_guild_row", "main_street.district_guild_row_desc"),
        new(StreetGroup.HomeHearth, "H", "main_street.district_home_hearth", "main_street.district_home_hearth_desc"),
        new(StreetGroup.CastleGrounds, "C", "main_street.district_castle_grounds", "main_street.district_castle_grounds_desc"),
        new(StreetGroup.StatusHub, "S", "main_street.district_status", "main_street.district_status_desc"),
        new(StreetGroup.NoticeBoard, "N", "main_street.district_notice_board", "main_street.district_notice_board_desc"),
        new(StreetGroup.Online, "O", "main_street.district_online", "main_street.district_online_desc"),
    };

    internal const string ReturnKey = "R";
    internal const string DistrictsNoticeHint = "main_street_districts";

    internal static int TierForLevel(int level) =>
        level >= GameConfig.MenuTier3Level ? 3 : level >= GameConfig.MenuTier2Level ? 2 : 1;

    internal static bool IsUnlocked(StreetEntry e, StreetView v) =>
        e.Tier <= v.Tier && (e.Group != StreetGroup.Online || v.Online) && (e.Place != StreetPlace.Settlement || v.Settlement);

    internal static List<StreetEntry> PlacesIn(StreetGroup group, StreetView v) =>
        StreetEntries.Where(e => e.Group == group && IsUnlocked(e, v)).ToList();

    /// <summary>A district shows on Main Street only when a place inside it is unlocked.</summary>
    internal static List<District> DistrictsShown(StreetView v) =>
        Districts.Where(d => PlacesIn(d.Group, v).Count > 0).ToList();

    internal static string DistrictName(District d, int onlineCount) =>
        d.Group == StreetGroup.Online ? Loc.Get("main_street.district_online_count", onlineCount) : Loc.Get(d.NameKey);

    internal static List<StreetLine> MainStreetLines(StreetView v, int onlineCount)
    {
        var lines = PlacesIn(StreetGroup.MainStreet, v)
            .Select(e => new StreetLine(e.Key, Loc.Get(e.LabelKey), e.Color, 0, e, null)).ToList();
        lines.AddRange(DistrictsShown(v).Select(d => new StreetLine(d.Key, DistrictName(d, onlineCount), "bright_cyan", 1, null, d)));
        lines.Add(new StreetLine("~", Loc.Get("menu.action.settings"), "gray", 2, null, null));
        lines.Add(new StreetLine("?", Loc.Get("menu.action.help"), "gray", 2, null, null));
        lines.Add(new StreetLine("Q", Loc.Get("menu.action.quit_game"), "gray", 2, null, null));
        return lines;
    }

    /// <summary>The place a key opens, as a navigation: its location, or null for a screen shown on Main Street.</summary>
    internal static GameLocation? DestinationOf(StreetPlace place, bool online) => place switch
    {
        StreetPlace.Dungeons => GameLocation.Dungeons,
        StreetPlace.Inn => GameLocation.TheInn,
        StreetPlace.Explore => GameLocation.Wilderness,
        StreetPlace.TeamCorner => GameLocation.TeamCorner,
        StreetPlace.DarkAlley => GameLocation.DarkAlley,
        StreetPlace.WeaponShop => GameLocation.WeaponShop,
        StreetPlace.ArmorShop => GameLocation.ArmorShop,
        StreetPlace.MagicShop => GameLocation.MagicShop,
        StreetPlace.MusicShop => GameLocation.MusicShop,
        StreetPlace.Auctions => online ? null : GameLocation.AuctionHouse,
        StreetPlace.Healer => GameLocation.Healer,
        StreetPlace.Temple => GameLocation.Temple,
        StreetPlace.OldChurch => GameLocation.Church,
        StreetPlace.Bank => GameLocation.Bank,
        StreetPlace.LevelMaster => GameLocation.Master,
        StreetPlace.QuestHall => GameLocation.QuestHall,
        StreetPlace.Home => GameLocation.Home,
        StreetPlace.Lodging => GameLocation.Dormitory,
        StreetPlace.LoveStreet => GameLocation.LoveCorner,
        StreetPlace.Castle => GameLocation.Castle,
        StreetPlace.Challenges => GameLocation.AnchorRoad,
        StreetPlace.Sanctum => GameLocation.Sanctum,
        StreetPlace.Settlement => GameLocation.Settlement,
        StreetPlace.Arena => GameLocation.Arena,
        _ => null,
    };

    // v1.1.13: the places that had their own walking line before the move
    private static string? WalkLineKey(StreetPlace place) => place switch
    {
        StreetPlace.Temple => "main_street.nav_temple",
        StreetPlace.LoveStreet => "main_street.nav_love_street",
        StreetPlace.DarkAlley => "main_street.nav_dark_alley",
        StreetPlace.Sanctum => "main_street.nav_sanctum",
        StreetPlace.Settlement => "main_street.nav_settlement",
        _ => null,
    };

    /// <summary>v1.1.13: Main Street draws only its own keys; %, *, ~, 0, / and ! still work through the global handler.</summary>
    protected override void ShowQuickCommandBar() { }

    private StreetView CurrentStreetView() => new(GetMenuTier(), DoorMode.IsOnlineMode,
        UsurperRemake.Systems.SettlementSystem.Instance?.State.IsEstablished == true);

    private static int OnlinePlayerCount() => OnlineStateManager.Instance?.CachedOnlinePlayerCount ?? 0;

    /// <summary>v1.1.13: does what a place's key says. Returns true when Main Street is left.</summary>
    private async Task<bool> VisitPlace(StreetPlace place)
    {
        bool online = DoorMode.IsOnlineMode;
        var destination = DestinationOf(place, online);
        if (destination != null)
        {
            if (place == StreetPlace.TeamCorner)
            {
                await NavigateToTeamCorner();
                return true;
            }
            if (place == StreetPlace.Arena)
                throw new LocationExitException(GameLocation.Arena);
            string? walk = WalkLineKey(place);
            if (walk != null)
            {
                terminal.WriteLine(Loc.Get(walk), place == StreetPlace.LoveStreet ? "magenta" : place == StreetPlace.Temple ? "cyan"
                    : place == StreetPlace.Sanctum ? "bright_yellow" : "gray");
                await Task.Delay(1500);
                throw new LocationExitException(destination.Value);
            }
            await NavigateToLocation(destination.Value);
            return true;
        }

        switch (place)
        {
            case StreetPlace.Auctions: await ShowAuctionMenu(); break;
            case StreetPlace.Status: await ShowStatus(); break;
            case StreetPlace.Progress: await ShowStoryProgress(); break;
            case StreetPlace.StatsRecord: await ShowStatistics(); break;
            case StreetPlace.Fame: await ShowFame(); break;
            case StreetPlace.GoodDeeds: await ShowGoodDeeds(); break;
            case StreetPlace.News: await ShowNewsFeed(); break;
            case StreetPlace.WorldEvents: await ShowWorldEvents(); break;
            case StreetPlace.WorldBoss: await ShowWorldBossMenu(); break;
            case StreetPlace.Guilds:
                if (GuildSystem.Instance != null) await ShowGuildBoard();
                else terminal.WriteLine($"  {Loc.Get("guild.online_only")}", "gray");
                break;
            case StreetPlace.WhosOnline:
                if (OnlineChatSystem.IsActive) await OnlineChatSystem.Instance!.ShowWhosOnline(terminal);
                else await OnlineUnavailable();
                break;
            case StreetPlace.Chat:
                if (OnlineChatSystem.IsActive) await ChatOnce();
                else await OnlineUnavailable();
                break;
        }
        return false;
    }

    private async Task OnlineUnavailable()
    {
        terminal.WriteLine($"  {Loc.Get("main_street.online_unavailable")}", "gray");
        await Task.Delay(1000);
    }

    private async Task ChatOnce()
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

    /// <summary>
    /// v1.1.13: one news entry. Online it is the shared feed every player sees (the news table: the
    /// world sim mirrors each town news line into it, and online-only events are written there).
    /// Offline it is the town news board.
    /// </summary>
    private async Task ShowNewsFeed()
    {
        if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
        {
            terminal.ClearScreen();
            await OnlineChatSystem.Instance!.ShowNews(terminal);
            return;
        }
        await new NewsLocation().EnterLocation(currentPlayer, terminal);
    }

    /// <summary>
    /// v1.1.13: a district or hub. Shows its unlocked places, takes one valid key (a typo asks again),
    /// and does it. Every place is left back to Main Street, and so is every screen shown here.
    /// </summary>
    private async Task<bool> EnterDistrict(District district)
    {
        var places = PlacesIn(district.Group, CurrentStreetView());
        if (places.Count == 0) return false;
        DrawDistrict(district, places);
        var keys = places.Select(p => p.Key).Append(ReturnKey).ToList();
        string pick = await terminal.GetValidChoice(Loc.Get("ui.your_choice"), keys, ReturnKey);
        if (pick == ReturnKey) return false;
        return await VisitPlace(places.First(p => p.Key == pick).Place);
    }

    private void DrawDistrict(District district, List<StreetEntry> places)
    {
        terminal.ClearScreen();
        string name = DistrictName(district, OnlinePlayerCount());
        string back = Loc.Get("location.return_main_street");
        if (IsScreenReader)
        {
            terminal.WriteLine(name, "bright_white");
            terminal.WriteLine(Loc.Get(district.DescKey), "gray");
            terminal.WriteLine("");
            foreach (var p in places) terminal.WriteLine($"  {p.Key} - {Loc.Get(p.LabelKey)}", "white");
            terminal.WriteLine($"  {ReturnKey} - {back}", "white");
            terminal.WriteLine("");
            return;
        }
        if (IsBBSSession)
        {
            terminal.SetColor("bright_blue");
            terminal.Write("═══ ");
            terminal.SetColor("bright_white");
            terminal.Write(name);
            terminal.SetColor("bright_blue");
            terminal.WriteLine(" ═══");
            terminal.WriteLine(Loc.Get(district.DescKey), "gray");
            WritePackedRow(places.Select(p => (p.Key, Loc.Get(p.LabelKey), p.Color))
                .Append((ReturnKey, back, "gray")));
            terminal.WriteLine("");
            return;
        }
        WriteBoxHeader(name, "bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine($"  {Loc.Get(district.DescKey)}", "gray");
        terminal.WriteLine("");
        foreach (var p in places) WriteKeyedLine(p.Key, Loc.Get(p.LabelKey), p.Color);
        terminal.WriteLine("");
        WriteKeyedLine(ReturnKey, back, "gray");
        terminal.WriteLine("");
    }

    private void WriteKey(string key)
    {
        terminal.SetColor("darkgray"); terminal.Write("[");
        terminal.SetColor("bright_yellow"); terminal.Write(key);
        terminal.SetColor("darkgray"); terminal.Write("] ");
    }

    private void WriteKeyedLine(string key, string label, string color)
    {
        terminal.Write("  ");
        WriteKey(key);
        terminal.SetColor(color);
        terminal.WriteLine(label);
    }

    /// <summary>v1.1.13: "[K] Label" items packed into as few lines of 79 columns as they fit (BBS).</summary>
    private void WritePackedRow(IEnumerable<(string Key, string Label, string Color)> items)
    {
        int width = 1;
        terminal.Write(" ");
        foreach (var (key, label, color) in items)
        {
            int len = key.Length + 3 + label.Length + 2;
            if (width > 1 && width + len > 79)
            {
                terminal.WriteLine("");
                terminal.Write(" ");
                width = 1;
            }
            WriteKey(key);
            terminal.SetColor(color);
            terminal.Write(label + "  ");
            width += len;
        }
        terminal.WriteLine("");
    }

    private static string MenuLabel(StreetLine line) => line.District != null ? line.Label + " ..." : line.Label;

    /// <summary>
    /// v1.1.13: the visual Main Street menu in aligned columns: places, districts, then system keys, each
    /// group on its own rows. A blank line parts the groups only when the menu is tall (tier 3).
    /// </summary>
    private void WriteStreetMenuGrid(List<StreetLine> lines)
    {
        int col = Math.Max(20, lines.Max(l => MenuLabel(l).Length) + 6);
        int perRow = Math.Max(1, 78 / col);
        var groups = new[] { 0, 1, 2 }.Select(r => lines.Where(l => l.Row == r).ToList()).Where(g => g.Count > 0).ToList();
        bool spaced = groups.Sum(g => (g.Count + perRow - 1) / perRow) > 4;
        for (int g = 0; g < groups.Count; g++)
        {
            if (g > 0 && spaced) terminal.WriteLine("");
            var items = groups[g];
            for (int i = 0; i < items.Count; i++)
            {
                bool last = i % perRow == perRow - 1 || i == items.Count - 1;
                if (i % perRow == 0) terminal.Write(" ");
                WriteKey(items[i].Key);
                terminal.SetColor(items[i].Color);
                string label = MenuLabel(items[i]);
                if (last) terminal.WriteLine(label);
                else terminal.Write(label.PadRight(col - 4));
            }
        }
    }

    /// <summary>v1.1.13: the screen reader Main Street menu, one item per line.</summary>
    private void WriteStreetMenuPlain(List<StreetLine> lines)
    {
        terminal.WriteLine(Loc.Get("main_street.menu_title"));
        foreach (var l in lines) terminal.WriteLine($"  {l.Key} - {l.Label}");
        terminal.WriteLine("");
    }

    /// <summary>v1.1.13: the BBS Main Street menu: places, districts, then Settings, Help and Quit, packed.</summary>
    private void WriteStreetMenuCompact(List<StreetLine> lines)
    {
        foreach (int row in new[] { 0, 1, 2 })
        {
            var items = lines.Where(l => l.Row == row).Select(l => (l.Key, MenuLabel(l), l.Color)).ToList();
            if (items.Count > 0) WritePackedRow(items);
        }
    }

    /// <summary>
    /// v1.1.13: the lines naming what a tier rise opened, grouped by where it now is: new places and
    /// districts on Main Street first, then one line per district that was already open.
    /// Online places and the settlement (it needs founding) are not announced.
    /// </summary>
    internal static List<string> UnlockLines(int fromTier, int toTier)
    {
        var before = new StreetView(fromTier, false, false);
        var after = new StreetView(toTier, false, false);
        bool IsNew(StreetEntry e) => IsUnlocked(e, after) && !IsUnlocked(e, before);
        var openBefore = DistrictsShown(before).Select(d => d.Group).ToHashSet();

        var lines = new List<string>();
        var onStreet = PlacesIn(StreetGroup.MainStreet, after).Where(IsNew).Select(e => Loc.Get(e.LabelKey))
            .Concat(DistrictsShown(after).Where(d => !openBefore.Contains(d.Group)).Select(d => Loc.Get(d.NameKey))).ToList();
        if (onStreet.Count > 0)
            lines.Add(Loc.Get("main_street.unlock_announce", string.Join(", ", onStreet)));
        foreach (var d in Districts.Where(d => openBefore.Contains(d.Group)))
        {
            var added = PlacesIn(d.Group, after).Where(IsNew).Select(e => Loc.Get(e.LabelKey)).ToList();
            if (added.Count > 0)
                lines.Add(Loc.Get("main_street.unlock_announce_in", Loc.Get(d.NameKey), string.Join(", ", added)));
        }
        return lines;
    }

    /// <summary>
    /// v1.1.13: the unlock lines once per tier rise. A character seen for the first time (no menu_tier_1
    /// key) has every tier it already reached marked as announced, silently.
    /// </summary>
    internal static List<string> TakeTierUnlockAnnouncement(Character player)
    {
        if (player == null) return new List<string>();
        int tier = TierForLevel(player.Level);
        bool firstSeen = !player.HintsShown.Contains("menu_tier_1");
        var added = Enumerable.Range(1, tier).Where(t => player.HintsShown.Add($"menu_tier_{t}")).ToList();
        if (firstSeen || added.Count == 0) return new List<string>();
        if (player.ClassicMainStreet) return ClassicUnlockLines(added.Min() - 1, tier); // v1.1.14: no districts to name
        return UnlockLines(added.Min() - 1, tier);
    }

    /// <summary>
    /// v1.1.13: the one-time "reorganised into districts" line for a character that played before it.
    /// A brand-new character (level 1, no kills) is marked silently.
    /// </summary>
    internal static string? TakeDistrictsNotice(Character player)
    {
        if (player == null || !player.HintsShown.Add(DistrictsNoticeHint)) return null;
        bool brandNew = player.Level == 1 && player.MKills == 0;
        return brandNew ? null : Loc.Get("main_street.districts_notice");
    }

    internal const int ClassicTipDrawLimit = 10;

    /// <summary>
    /// v1.1.14: the "switch back to the classic layout" tip, on a character's first ten district Main Street
    /// draws that show it (counted and saved per character; classic draws do not count). The draw that shows the
    /// one-time reorganisation notice, which names the same setting, shows no tip and is not counted.
    /// </summary>
    internal static string? TakeClassicLayoutTip(Character player, bool noticeShown)
    {
        if (player == null || player.ClassicMainStreet || noticeShown || player.ClassicTipDraws >= ClassicTipDrawLimit) return null;
        player.ClassicTipDraws++;
        return Loc.Get("main_street.classic_tip");
    }

    /// <summary>v1.1.13: the help screen: every unlocked place under the key path that reaches it.</summary>
    internal static List<string> HelpMapLines(StreetView v, int onlineCount)
    {
        var lines = new List<string>();
        var street = MainStreetLines(v, onlineCount);
        lines.Add(string.Join("  ", street.Where(l => l.Row == 0).Select(l => $"[{l.Key}] {l.Label}")));
        foreach (var l in street.Where(l => l.District != null))
        {
            var inside = PlacesIn(l.District!.Group, v).Select(e => $"[{e.Key}] {Loc.Get(e.LabelKey)}");
            lines.Add($"[{l.Key}] {l.Label}: {string.Join(", ", inside)}");
        }
        lines.Add(string.Join("  ", street.Where(l => l.Row == 2).Select(l => $"[{l.Key}] {l.Label}")));
        return lines;
    }
}
