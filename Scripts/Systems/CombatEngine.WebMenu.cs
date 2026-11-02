using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Server;
using UsurperRemake.Systems;

/// <summary>
/// 2.0 phase 1: the fight prompts' Client.Menu for the web client. 1.3.10 (C3): each list holds exactly the keys the text
/// menu drew for this character and this round, so an action that cannot be taken (Aid Ally with no ally in need,
/// Rescue, a Miracle not offered, Save, Rage for a class without it, Coat Blade or the herb pouch with none left, Power
/// Attack in PvP) is not listed. Enabled is false only for a row the text draws in grey or the handler refuses this
/// round (Use Item with no usable potion, Aid Ally with nothing to give, a quickbar skill not ready). A key not listed
/// still does what it did when typed. Sent from the combat owner's own prompt only (GetPlayerActionMultiMonster and
/// PvP's GetPlayerAction), never from the shared menu drawers a grouped follower's prompt also uses.
/// </summary>
public partial class CombatEngine
{
    /// <summary>
    /// 2.0: true when the menu just drawn printed the quickbar hint line ("type ?1-?9 ..."). Cleared before each
    /// prompt's menu draw and set by the drawer that prints the line, so Client.Menu carries the hint exactly when the
    /// text shows it.
    /// </summary>
    private bool quickbarHintDrawn;

    /// <summary>2.0: the Client.Menu hint for the quickbar line, or null when this draw did not print it.</summary>
    private WebMenuHint? QuickbarHintIfDrawn() =>
        quickbarHintDrawn ? new WebMenuHint(Loc.Get("combat.quickbar_info_hint"), Loc.Get("web.menu.quickbar")) : null;

    private static string SpeedName(Character player) => player.CombatSpeed switch
    {
        CombatSpeed.Instant => Loc.Get("combat.speed_instant"),
        CombatSpeed.Fast => Loc.Get("combat.speed_fast"),
        _ => Loc.Get("combat.speed_normal")
    };

    private List<WebMenuItem> QuickbarMenuItems(Character player) =>
        GetQuickbarActions(player).Select(q => new WebMenuItem(q.key, q.displayName, q.available)).ToList();

    /// <summary>
    /// 2.0: the dungeon fight prompt's keys (GetPlayerActionMultiMonster's switch), one foe or several. 1.3.10: the
    /// keys of the standard and screen reader menus (ShowDungeonCombatMenuStandard), from the same conditions: Aid Ally
    /// only while an ally needs aid, Rescue, Miracle, Save, Rage, Backstab, Smite, Coat Blade and the herb pouch only
    /// while each can be taken. The quickbar lists the filled slots; an empty slot's digit is refused with "quickbar
    /// empty" and is not listed.
    /// </summary>
    internal List<WebMenuGroup> MultiMonsterMenuGroups(Character player, GodDomain miracle, bool hasTeammatesNeedingAid, bool canHealAlly)
    {
        bool usablePotion = (player.Healing > 0 && player.HP < player.MaxHP) || (player.ManaPotions > 0 && player.Mana < player.MaxMana);
        var actions = new List<WebMenuItem>
        {
            new("A", Loc.Get("combat.attack")),
            new("D", Loc.Get("combat.defend")),
            new("I", Loc.Get("combat.use_item_potions_short", player.Healing, player.MaxPotions), usablePotion),
        };
        if (hasTeammatesNeedingAid) actions.Add(new("H", Loc.Get("combat.aid_ally"), canHealAlly));
        if (RescueOffered(player)) actions.Add(new("U", Loc.Get("combat.rescue_menu")));
        if (miracle != GodDomain.None) actions.Add(new("M", MiracleSystem.MenuLabel(miracle)));
        if (BossContext?.CanSave == true) actions.Add(new("V", Loc.Get("combat.save_boss_short")));
        actions.Add(new("R", Loc.Get("combat.flee")));

        var tactics = new List<WebMenuItem>
        {
            new("P", Loc.Get("combat.power_attack")),
            new("E", Loc.Get("combat.precise_strike")),
            new("T", Loc.Get("combat.taunt")),
            new("W", Loc.Get("combat.disarm")),
            new("L", Loc.Get("combat.hide")),
        };
        if (CanBackstab(player)) tactics.Add(new("K", Loc.Get("combat.backstab_label")));
        if (CanSmite(player)) tactics.Add(new("Z", Loc.Get("combat.smite_label", player.SmiteChargesRemaining)));
        if (CanRage(player)) tactics.Add(new("G", Loc.Get("combat.rage_short")));
        if (player.PoisonVials > 0) tactics.Add(new("B", Loc.Get("combat.coat_blade_short", player.PoisonVials)));
        if (player.TotalHerbCount > 0) tactics.Add(new("J", Loc.Get("combat.herb_pouch_short", player.TotalHerbCount)));

        return new List<WebMenuGroup>
        {
            new(Loc.Get("web.menu.actions"), actions),
            new(Loc.Get("web.menu.tactics"), tactics),
            new(Loc.Get("web.menu.quickbar"), QuickbarMenuItems(player)),
            new(Loc.Get("web.menu.options"), new List<WebMenuItem>
            {
                new("O", Loc.Get("combat.auto_short")),   // 1.3.9: single keys; AUTO and SPD typed out still work
                new("X", Loc.Get("combat.speed_label", SpeedName(player))),
            }),
        };
    }

    /// <summary>
    /// 2.0: the PvP prompt's keys (GetPlayerAction with a PvP opponent: S, X or SPD, the quickbar, then ParseCombatAction).
    /// Power Attack, Precise Strike, Fight to Death and Beg for Mercy are refused in PvP and, from 1.3.10, not listed.
    /// G is listed only while ParseCombatAction reads it as Rage; otherwise it falls through to its default, a plain
    /// attack, as any other key does. B and J are not listed: ParseCombatAction reads them as Coat Blade and the herb
    /// pouch, but ProcessPlayerVsPlayerAction has no case for either, so in PvP they too make a plain attack.
    /// </summary>
    internal List<WebMenuGroup> PvpMenuGroups(Character player)
    {
        var tactics = new List<WebMenuItem>
        {
            new("I", Loc.Get("combat.disarm")),
            new("T", Loc.Get("combat.taunt")),
            new("L", Loc.Get("combat.hide")),
            new("V", Loc.Get("combat.ranged_short")),
        };
        if (player.Class == CharacterClass.Barbarian && !player.IsRaging) tactics.Add(new("G", Loc.Get("combat.rage_short")));
        // 1.3.10 (C3): Power Attack, Precise Strike, Fight to Death and Beg for Mercy are refused in PvP every round, and
        // the PvP text menus do not draw them, so they are not listed; typed, they still say they are not available
        return new List<WebMenuGroup>
        {
            new(Loc.Get("web.menu.actions"), new List<WebMenuItem>
            {
                new("A", Loc.Get("combat.attack")),
                new("D", Loc.Get("combat.defend")),
                new("H", Loc.Get("combat.heal_potions_short", player.Healing, player.MaxPotions)),
                new("Q", Loc.Get("web.menu.quick_heal")),
                new("R", Loc.Get("combat.flee_pvp_short")),
                new("S", Loc.Get("combat.view_status_short")),
            }),
            new(Loc.Get("web.menu.tactics"), tactics),
            new(Loc.Get("web.menu.quickbar"), QuickbarMenuItems(player)),
            new(Loc.Get("web.menu.options"), new List<WebMenuItem> { new("X", Loc.Get("combat.speed_label", SpeedName(player))) }),
        };
    }
}
