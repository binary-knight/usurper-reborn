using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems;

/// <summary>
/// 1.2.0 Temple gods piece 5: Miracles. A Chosen follower (Favor GodFavorTierChosenMin and up)
/// has one Miracle a day, from their god's domain: a canon god's own, or for a player-god the
/// domain its immortal chose (GodBoonSystem.GetDomain). The day's use is saved
/// (Character.MiracleUsedToday, five save sites) and cleared by the daily reset, so a reload does
/// not give it back. Mortis's Miracle (Death) is not called on: it cheats death once that day, by
/// itself, when the follower would die in a monster fight (TryCheatDeath). The other nine are
/// called from the combat menu in monster fights. PvP and the world boss have no Miracles.
/// NPCs never have one.
/// </summary>
public static class MiracleSystem
{
    /// <summary>
    /// The domain of the Miracle the character has: their god's domain at Chosen, else None
    /// (below Chosen, no god, an NPC, or a player-god whose domain is not known yet).
    /// </summary>
    public static GodDomain GetMiracle(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return GodDomain.None;
        if (FavorSystem.GetTier(FavorSystem.GetFavor(c, gods)) != GodFavorTier.Chosen) return GodDomain.None;
        return GodBoonSystem.GetDomain(c, gods);
    }

    /// <summary>True while the character has a Miracle and has not used it today.</summary>
    public static bool IsReady(Character c, GodSystem? gods = null) =>
        c != null && !c.MiracleUsedToday && GetMiracle(c, gods) != GodDomain.None;

    /// <summary>True for a Miracle the follower calls on (every domain but Death, which acts by itself).</summary>
    public static bool IsCalled(GodDomain d) => d != GodDomain.None && d != GodDomain.Death;

    /// <summary>Spends today's Miracle when it is ready. Returns false (nothing changed) when it is not.</summary>
    public static bool TryConsume(Character c, GodSystem? gods = null)
    {
        if (!IsReady(c, gods)) return false;
        c.MiracleUsedToday = true;
        return true;
    }

    /// <summary>Daily reset: the Miracle is ready again. NPCs are skipped. Called from DailySystemManager.RunBasicDailyReset.</summary>
    public static void ApplyDailyReset(Character c)
    {
        if (c == null || c.IsNPC) return;
        c.MiracleUsedToday = false;
    }

    /// <summary>
    /// Mortis's Miracle: a follower of Death at Chosen who has fallen to 0 HP or less in a fight,
    /// with today's Miracle unused, is left at 1 HP and the Miracle is spent. Returns true when
    /// it fired. Anyone else, or a living character, is unchanged. Callers leave out the fights
    /// with no real death (PvP, arrest, exhibition, the world boss, a Mental collapse).
    /// </summary>
    public static bool TryCheatDeath(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC || c.HP > 0) return false;
        if (GetMiracle(c, gods) != GodDomain.Death) return false;
        if (!TryConsume(c, gods)) return false;
        c.HP = 1;
        return true;
    }

    // ---------------- Text ----------------

    /// <summary>The name of a domain's Miracle, in the player's language ("" for None).</summary>
    public static string Name(GodDomain d) =>
        d == GodDomain.None ? "" : Loc.Get("miracle.name." + d.ToString().ToLowerInvariant());

    /// <summary>What a domain's Miracle does, in the player's language ("" for None).</summary>
    public static string Describe(GodDomain d) => d switch
    {
        GodDomain.None => "",
        GodDomain.Light => Loc.Get("miracle.desc.light", GameConfig.MiracleBanishBossDamagePct),
        GodDomain.Law => Loc.Get("miracle.desc.law", GameConfig.MiracleBindRounds),
        GodDomain.Chaos => Loc.Get("miracle.desc.chaos", GameConfig.MiracleConfuseRounds),
        _ => Loc.Get("miracle.desc." + d.ToString().ToLowerInvariant())
    };

    /// <summary>
    /// The Temple's line for the character's own Miracle: at Chosen its name, what it does and
    /// whether it is ready today; below Chosen the Miracle their god gives and the tier that
    /// unlocks it; "" with no god or no known domain.
    /// </summary>
    public static string TempleLine(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return "";
        var domain = GodBoonSystem.GetDomain(c, gods);
        if (domain == GodDomain.None) return "";
        if (GetMiracle(c, gods) == GodDomain.None)
            return Loc.Get("miracle.temple_locked", Name(domain), GameConfig.GodFavorTierChosenMin);
        string state = c.MiracleUsedToday ? Loc.Get("miracle.state_used") : Loc.Get("miracle.state_ready");
        return Loc.Get("miracle.temple_line", Name(domain), Describe(domain), state);
    }
}
