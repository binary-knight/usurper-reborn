using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using UsurperRemake.Server;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 2.0 phase 1: Main Street's Client.Menu for the web client. The districts layout's keys are the lines the key handler
/// looks up (MainStreetLines); the classic layout's are the rows its menu draws (ClassicVisualRows through ClassicShown)
/// and its online row, every one a key ProcessClassicChoice takes. Sent from the menu draw only.
/// </summary>
public partial class MainStreetLocation
{
    /// <summary>2.0: the classic online row as drawn: key, the place whose unlock shows it (null: always), label.</summary>
    internal static readonly (string Key, StreetPlace? Place, string LabelKey)[] ClassicOnlineItems =
    {
        ("3", StreetPlace.WhosOnline, "main_street.whos_online"),
        ("4", StreetPlace.Chat, "main_street.chat"),
        ("5", null, "main_street.news_feed"),
        ("6", StreetPlace.Arena, "main_street.arena_pvp"),
        ("7", StreetPlace.WorldBoss, "main_street.world_boss"),
        ("R", StreetPlace.Guilds, "menu.action.guilds"),
    };

    /// <summary>2.0: the districts menu: places, districts, then Settings, Help and Quit, from MainStreetLines.</summary>
    internal static List<WebMenuGroup> DistrictsMenuGroups(StreetView v, int onlineCount)
    {
        var lines = MainStreetLines(v, onlineCount);
        WebMenuGroup Row(string labelKey, int row) =>
            new(Loc.Get(labelKey), lines.Where(l => l.Row == row).Select(l => new WebMenuItem(l.Key, l.Label)).ToList());
        return new List<WebMenuGroup> { Row("web.menu.places", 0), Row("web.menu.districts", 1), Row("web.menu.system", 2) };
    }

    /// <summary>2.0: the classic menu: the drawn places, the online row when it is drawn, then Quit and Settings.</summary>
    internal static List<WebMenuGroup> ClassicMenuGroups(StreetView v, bool onlineRowDrawn)
    {
        var shown = ClassicVisualRows.SelectMany(r => ClassicShown(r, v)).ToList();
        var groups = new List<WebMenuGroup>
        {
            new(Loc.Get("web.menu.places"), shown.Where(i => i.Place != null).Select(i => new WebMenuItem(i.Key, Loc.Get(i.LabelKey))).ToList()),
        };
        if (onlineRowDrawn)
            groups.Add(new(Loc.Get("main_street.online_header"), ClassicOnlineItems
                .Where(o => o.Place == null || ClassicOnlineShows(o.Place.Value, v))
                .Select(o => new WebMenuItem(o.Key, Loc.Get(o.LabelKey))).ToList()));
        groups.Add(new(Loc.Get("web.menu.system"), shown.Where(i => i.Place == null).Select(i => new WebMenuItem(i.Key, Loc.Get(i.LabelKey))).ToList()));
        return groups;
    }

    /// <summary>2.0: Client.Menu for this draw of Main Street (web client sessions only; a no-op for every other session).</summary>
    private void EmitWebMenu()
    {
        bool classic = UseClassicLayout;
        WebClientMenu.Emit(classic ? WebClientMenu.MainStreetClassic : WebClientMenu.MainStreet, () => classic
            ? ClassicMenuGroups(CurrentStreetView(), DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
            : DistrictsMenuGroups(CurrentStreetView(), OnlinePlayerCount()));
    }
}
