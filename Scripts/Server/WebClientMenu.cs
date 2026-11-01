using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Server;

/// <summary>2.0: one key of a Client.Menu group. Enabled is false when the screen's handler takes the key but refuses it.</summary>
public sealed record WebMenuItem(string Key, string Label, bool Enabled = true);

/// <summary>2.0: a labelled group of Client.Menu keys.</summary>
public sealed record WebMenuGroup(string Label, IReadOnlyList<WebMenuItem> Items);

/// <summary>2.0: a help line the text menu printed under a group (Label names that group).</summary>
public sealed record WebMenuHint(string Text, string Label);

/// <summary>
/// 2.0 phase 1: GMCP Client.Menu, the keys a screen takes, for the web client only.
/// Payload: {screen, groups:[{label, items:[{key, label, enabled}]}], hint?:{text, label}}; hint only when the text menu
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
    public static void Emit(string screen, Func<IEnumerable<WebMenuGroup>> build, Func<WebMenuHint?>? hint = null)
    {
        if (!GmcpBridge.IsWebClientActive) return;
        GmcpBridge.EmitWebClient(Package, Payload(screen, build(), hint?.Invoke()));
    }

    /// <summary>The Client.Menu payload; empty groups are left out, and a null hint is left out.</summary>
    internal static object Payload(string screen, IEnumerable<WebMenuGroup> groups, WebMenuHint? hint = null) => new
    {
        screen,
        hint = hint == null ? null : new { text = hint.Text, label = hint.Label },
        groups = groups.Where(g => g.Items.Count > 0).Select(g => new
        {
            label = g.Label,
            items = g.Items.Select(i => new { key = i.Key, label = i.Label, enabled = i.Enabled }).ToList(),
        }).ToList(),
    };
}
