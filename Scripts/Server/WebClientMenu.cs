using System;
using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Systems;

namespace UsurperRemake.Server;

/// <summary>2.0: one key of a Client.Menu group. Enabled is false when the screen's handler takes the key but refuses it.</summary>
public sealed record WebMenuItem(string Key, string Label, bool Enabled = true);

/// <summary>2.0: a labelled group of Client.Menu keys.</summary>
public sealed record WebMenuGroup(string Label, IReadOnlyList<WebMenuItem> Items);

/// <summary>2.0: a help line the text menu printed under a group (Label names that group).</summary>
public sealed record WebMenuHint(string Text, string Label);

/// <summary>
/// 2.0 phase 1: GMCP Client.Menu, the keys a screen takes, for the web client only.
/// Payload: {screen, oneKey, round?, groups:[{label, items:[{key, label, enabled}]}], hint?:{text, label}}; hint only when the text menu
/// printed that line. Each screen builds its groups from the same list
/// its key handler reads, and sends them when the screen is drawn, never per keystroke. The builder runs only for a
/// web client session (GmcpBridge.IsWebClientActive), so every other session does no extra work and sees no change.
/// </summary>
public static class WebClientMenu
{
    public const string Package = "Client.Menu";

    public const string MainStreet = "main_street";
    public const string MainStreetClassic = "main_street_classic";
    public const string DungeonRoom = "dungeon_room";
    public const string Combat = "combat";
    public const string CombatPvp = "combat_pvp";

    /// <summary>Sends the screen's menu to the current session when it is the web client; builds nothing otherwise.</summary>
    /// <param name="round">1.3.9: on a combat screen, the fight's current round (the round the screen prints); left
    /// out of the frame on every other screen.</param>
    public static void Emit(string screen, Func<IEnumerable<WebMenuGroup>> build, Func<WebMenuHint?>? hint = null, int? round = null)
    {
        if (!GmcpBridge.IsWebClientActive) return;
        GmcpBridge.EmitWebClient(Package, Payload(screen, build(), hint?.Invoke(), OneKey(), round));
    }

    /// <summary>
    /// 1.3.9 (play test C1, his pick 4): Client.Menu's oneKey, true when the page may send a menu key on one press
    /// (it sends the key followed by Enter; the server reads lines as before). True only for a web client session
    /// whose character has one-key menus on (Character.WebMenuKeysNeedEnter false, the default) and is not in screen
    /// reader mode. Client.Menu goes to web client sessions only, so no other session ever sees the field.
    /// </summary>
    internal static bool OneKey()
    {
        if (!GmcpBridge.IsWebClientActive) return false;
        var player = SessionContext.Current?.Engine?.CurrentPlayer;
        return player != null && !player.WebMenuKeysNeedEnter && !player.ScreenReaderMode && !GameConfig.ScreenReaderMode;
    }

    /// <summary>The Client.Menu payload; empty groups are left out, and a null hint is left out.</summary>
    internal static object Payload(string screen, IEnumerable<WebMenuGroup> groups, WebMenuHint? hint = null, bool oneKey = false, int? round = null) => new
    {
        screen,
        oneKey,
        round,
        hint = hint == null ? null : new { text = hint.Text, label = hint.Label },
        groups = groups.Where(g => g.Items.Count > 0).Select(g => new
        {
            label = g.Label,
            items = g.Items.Select(i => new { key = i.Key, label = i.Label, enabled = i.Enabled }).ToList(),
        }).ToList(),
    };
}
