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

    // ---------------- In a monster fight ----------------

    /// <summary>The nine Miracles a follower calls on, in Temple order (every domain but Death).</summary>
    public static IReadOnlyList<GodDomain> AllCalled => GodBoonSystem.AllDomains.Where(IsCalled).ToList();

    /// <summary>The Miracles aimed at one foe (the menu asks which): Light, War and Law.</summary>
    public static bool NeedsTarget(GodDomain d) => d == GodDomain.Light || d == GodDomain.War || d == GodDomain.Law;

    /// <summary>Solarius: a living undead or demon foe (DivineBlessingSystem's own test).</summary>
    public static bool CanBanish(Monster m) => m != null && m.IsAlive && DivineBlessingSystem.IsUndeadOrDemon(m);

    /// <summary>
    /// Solarius: an ordinary foe is banished outright. A boss, mini-boss or Old God is never
    /// removed at once (as every instant kill in combat spares bosses); it takes BanishBossDamage.
    /// </summary>
    public static bool BanishKillsOutright(Monster m) =>
        m != null && !m.IsBoss && !m.IsMiniBoss && m.FamilyName != "OldGod";

    /// <summary>Solarius on a boss, mini-boss or Old God: MiracleBanishBossDamagePct of its max HP (at least 1), as holy damage.</summary>
    public static long BanishBossDamage(Monster m) =>
        m == null ? 0 : Math.Max(1, m.MaxHP * GameConfig.MiracleBanishBossDamagePct / 100);

    /// <summary>
    /// The Miracle the actor can call on now in a monster fight, or None: it must be ready today,
    /// one that is called (not Mortis), with a living foe, and of use here: Solarius needs a living
    /// undead or demon foe, Amara someone on the actor's side below full HP, Umbrath a fight that
    /// can be fled (canFlee: not Nightmare), Terran the actor below full HP, Arcanus the actor below
    /// full mana. party is everyone on the actor's side (the actor counts even when left out).
    /// </summary>
    public static GodDomain OfferedInFight(Character actor, IEnumerable<Monster>? monsters, IEnumerable<Character>? party,
        bool canFlee, GodSystem? gods = null)
    {
        if (!IsReady(actor, gods)) return GodDomain.None;
        var d = GetMiracle(actor, gods);
        if (!IsCalled(d)) return GodDomain.None;
        var foes = monsters?.Where(m => m != null && m.IsAlive).ToList() ?? new List<Monster>();
        if (foes.Count == 0) return GodDomain.None;
        bool useful = d switch
        {
            GodDomain.Light => foes.Any(CanBanish),
            GodDomain.Love => (party ?? Enumerable.Empty<Character>()).Append(actor).Any(p => p != null && p.IsAlive && p.HP < p.MaxHP),
            GodDomain.Shadow => canFlee,
            GodDomain.Earth => actor.HP < actor.MaxHP,
            GodDomain.Magic => actor.MaxMana > 0 && actor.Mana < actor.MaxMana,
            _ => true
        };
        return useful ? d : GodDomain.None;
    }

    /// <summary>Terran: the character back to full HP.</summary>
    public static void HealToFull(Character c)
    {
        if (c != null && c.IsAlive) c.HP = c.MaxHP;
    }

    /// <summary>Amara: every living member below full HP back to full. Returns the ones healed, each once.</summary>
    public static List<Character> HealPartyToFull(IEnumerable<Character>? party)
    {
        var healed = new List<Character>();
        foreach (var p in party ?? Enumerable.Empty<Character>())
        {
            if (p == null || !p.IsAlive || p.HP >= p.MaxHP || healed.Contains(p)) continue;
            p.HP = p.MaxHP;
            healed.Add(p);
        }
        return healed;
    }

    /// <summary>Arcanus: the character's mana back to full.</summary>
    public static void RefillMana(Character c)
    {
        if (c != null && c.MaxMana > 0) c.Mana = c.MaxMana;
    }

    /// <summary>
    /// Sylvana: the beast that answers, a tamed-pet combat teammate (BeastData.BuildCombatWrapper,
    /// GameConfig.MiracleBeastId) at the owner's level, for this fight only. Null if the beast is
    /// not defined.
    /// </summary>
    public static Character? SummonBeast(Character owner)
    {
        var def = UsurperRemake.Data.BeastData.GetById(GameConfig.MiracleBeastId);
        if (def == null || owner == null) return null;
        var beast = UsurperRemake.Data.BeastData.BuildCombatWrapper(def, Math.Max(1, owner.Level), Loc.Get("miracle.beast_name"));
        beast.IsMiracleAlly = true;
        return beast;
    }

    /// <summary>The combat menu's label for a Miracle ("Miracle: name").</summary>
    public static string MenuLabel(GodDomain d) => Loc.Get("miracle.menu", Name(d));

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
