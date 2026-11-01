using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Server;
using UsurperRemake.Systems;

/// <summary>
/// 2.0 phase 1: the fight prompts' Client.Menu for the web client. Each list holds every key the prompt's handler takes,
/// with Enabled as that handler decides it: false where the handler refuses the key (an "invalid action" or "not
/// ready" line, or PvP's "not available in PvP"). Sent from the combat owner's own prompt only (GetPlayerActionMultiMonster
/// and PvP's GetPlayerAction), never from the shared menu drawers a grouped follower's prompt also uses.
/// </summary>
public partial class CombatEngine
{
    private static string SpeedName(Character player) => player.CombatSpeed switch
    {
        CombatSpeed.Instant => Loc.Get("combat.speed_instant"),
        CombatSpeed.Fast => Loc.Get("combat.speed_fast"),
        _ => Loc.Get("combat.speed_normal")
    };

    private List<WebMenuItem> QuickbarMenuItems(Character player) =>
        GetQuickbarActions(player).Select(q => new WebMenuItem(q.key, q.displayName, q.available)).ToList();

    /// <summary>
    /// 2.0: the dungeon fight prompt's keys (GetPlayerActionMultiMonster's switch), one foe or several. The quickbar
    /// lists the filled slots; an empty slot's digit is refused with "quickbar empty" and is not listed.
    /// </summary>
    internal List<WebMenuGroup> MultiMonsterMenuGroups(Character player, GodDomain miracle)
    {
        bool usablePotion = (player.Healing > 0 && player.HP < player.MaxHP) || (player.ManaPotions > 0 && player.Mana < player.MaxMana);
        bool canAid = (currentTeammates?.Any(t => t.IsAlive) ?? false)
            && (player.Healing > 0 || player.ManaPotions > 0 || (ClassAbilitySystem.IsSpellcaster(player.Class) && player.Mana > 0));
        var mods = player.GetClassCombatModifiers();
        return new List<WebMenuGroup>
        {
            new(Loc.Get("web.menu.actions"), new List<WebMenuItem>
            {
                new("A", Loc.Get("combat.attack")),
                new("D", Loc.Get("combat.defend")),
                new("I", Loc.Get("combat.use_item_potions_short", player.Healing, player.MaxPotions), usablePotion),
                new("H", Loc.Get("combat.aid_ally"), canAid),
                new("U", Loc.Get("combat.rescue_menu"), RescueOffered(player)),
                new("M", miracle != GodDomain.None ? MiracleSystem.MenuLabel(miracle) : Loc.Get("web.menu.miracle"), miracle != GodDomain.None),
                new("V", Loc.Get("combat.save_boss_short"), BossContext?.CanSave == true),
                new("R", Loc.Get("combat.flee")),
            }),
            new(Loc.Get("web.menu.tactics"), new List<WebMenuItem>
            {
                new("P", Loc.Get("combat.power_attack")),
                new("E", Loc.Get("combat.precise_strike")),
                new("T", Loc.Get("combat.taunt")),
                new("W", Loc.Get("combat.disarm")),
                new("L", Loc.Get("combat.hide")),
                new("K", Loc.Get("combat.backstab_label"), CanBackstab(player)),
                new("Z", Loc.Get("combat.smite_label", player.SmiteChargesRemaining), mods.SmiteCharges > 0 && player.SmiteChargesRemaining > 0),
                new("G", Loc.Get("combat.rage_short"), mods.RageAvailable && !player.IsRaging),
                new("B", Loc.Get("combat.coat_blade_short", player.PoisonVials), player.PoisonVials > 0),
                new("J", Loc.Get("combat.herb_pouch_short", player.TotalHerbCount), player.TotalHerbCount > 0),
            }),
            new(Loc.Get("web.menu.quickbar"), QuickbarMenuItems(player)),
            new(Loc.Get("web.menu.options"), new List<WebMenuItem>
            {
                new("AUTO", Loc.Get("combat.auto_short")),
                new("SPD", Loc.Get("combat.speed_label", SpeedName(player))),
            }),
        };
    }

    /// <summary>
    /// 2.0: the PvP prompt's keys (GetPlayerAction with a PvP opponent: S, SPD, the quickbar, then ParseCombatAction).
    /// Power Attack, Precise Strike, Fight to Death and Beg for Mercy are refused in PvP and listed as not enabled.
    /// G, B and J are listed only while ParseCombatAction reads them as Rage, Coat Blade and the herb pouch; otherwise
    /// they fall through to its default, a plain attack, as any other key does.
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
        if (player.PoisonVials > 0) tactics.Add(new("B", Loc.Get("combat.coat_blade_short", player.PoisonVials)));
        if (player.TotalHerbCount > 0) tactics.Add(new("J", Loc.Get("combat.herb_pouch_short", player.TotalHerbCount)));
        tactics.Add(new("P", Loc.Get("combat.power_attack"), false));
        tactics.Add(new("E", Loc.Get("combat.precise_strike"), false));
        tactics.Add(new("F", Loc.Get("combat.fight_death_short"), false));
        tactics.Add(new("M", Loc.Get("combat.mercy_short"), false));
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
            new(Loc.Get("web.menu.options"), new List<WebMenuItem> { new("SPD", Loc.Get("combat.speed_label", SpeedName(player))) }),
        };
    }
}
