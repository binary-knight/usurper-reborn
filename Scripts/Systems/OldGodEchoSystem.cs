using System;

namespace UsurperRemake.Systems;

/// <summary>
/// 1.2.0 Temple gods piece 6: the Old Gods link. Each canon god echoes an Old God (Solarius
/// Aurelion, Valorian Maelketh, Amara Veloura, Judicar Thorgrim, Umbrath Noctura, Terran Terravok,
/// Arcanus Manwe); Discordia echoes the Sundering, which is not an Old God that is fought, and
/// Mortis and Sylvana echo none. A player-god echoes through the canon god of its domain. A Zealot
/// or Chosen follower facing the echoed Old God hears one extra line as the encounter opens
/// (OldGodBossSystem.StartBossEncounter) and deals GodEchoDamagePct more damage to it
/// (DivineBlessingSystem.CalculateBonusDamage, the weapon attack path of the canon damage boons, and
/// the player's damage spells in CombatEngine.ExecuteSpellMultiMonster: a single-target spell on its
/// damage, an area spell per target in ApplyAoEDamage);
/// a Chosen follower's god speaks once when that Old God falls (OldGodBossSystem.HandleBossDefeated).
/// </summary>
public static class OldGodEchoSystem
{
    /// <summary>The Old God a canon god echoes, or null (Mortis, Sylvana, Discordia's Sundering, any other name).</summary>
    public static OldGodType? EchoOfCanon(string? canonGod) => (GodRegistry.CanonName(canonGod) ?? "") switch
    {
        "Solarius" => OldGodType.Aurelion,
        "Valorian" => OldGodType.Maelketh,
        "Amara" => OldGodType.Veloura,
        "Judicar" => OldGodType.Thorgrim,
        "Umbrath" => OldGodType.Noctura,
        "Terran" => OldGodType.Terravok,
        "Arcanus" => OldGodType.Manwe,
        _ => null
    };

    /// <summary>The canon god whose echo a character carries: its canon god, or its player-god's domain god ("" with none).</summary>
    public static string EchoCanonGod(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return "";
        var god = GodRegistry.GetWorshippedGod(c, gods);
        if (god == null) return "";
        return god.Value.IsCanon ? god.Value.Name : GodBoonSystem.CanonGodOf(GodBoonSystem.GetDomain(c, gods));
    }

    /// <summary>The Old God a character's god echoes, or null.</summary>
    public static OldGodType? EchoFor(Character c, GodSystem? gods = null) => EchoOfCanon(EchoCanonGod(c, gods));

    /// <summary>True when the character is a Zealot or Chosen of a god that echoes this Old God.</summary>
    public static bool Applies(Character c, OldGodType oldGod, GodSystem? gods = null) =>
        c != null && !c.IsNPC && EchoFor(c, gods) == oldGod
        && FavorSystem.GetTier(FavorSystem.GetFavor(c, gods)) >= GodFavorTier.Zealot;

    /// <summary>True when the character is Chosen of a god that echoes this Old God (its god speaks at the fall).</summary>
    public static bool ChosenSpeaks(Character c, OldGodType oldGod, GodSystem? gods = null) =>
        Applies(c, oldGod, gods) && FavorSystem.GetTier(FavorSystem.GetFavor(c, gods)) == GodFavorTier.Chosen;

    /// <summary>The extra damage against an Old God monster: GodEchoDamagePct of the damage when Applies, else 0.</summary>
    public static long BonusDamage(Character attacker, Monster defender, long damage, GodSystem? gods = null)
    {
        if (attacker == null || defender?.OldGod is not { } oldGod || damage <= 0) return 0;
        return Applies(attacker, oldGod, gods) ? damage * GameConfig.GodEchoDamagePct / 100 : 0;
    }

    /// <summary>The Loc key suffix of an Old God.</summary>
    private static string Key(OldGodType t) => t.ToString().ToLowerInvariant();

    /// <summary>The extra line as the encounter opens, or null when it does not apply.</summary>
    public static string? EncounterLine(Character c, OldGodType oldGod, GodSystem? gods = null)
    {
        if (!Applies(c, oldGod, gods)) return null;
        string god = GodRegistry.GetWorshippedGod(c, gods)?.Name ?? "";
        return Loc.Get("old_god.echo." + Key(oldGod), god);
    }

    /// <summary>The Chosen follower's god speaking at the Old God's fall, or null when it does not apply.</summary>
    public static string? FallLine(Character c, OldGodType oldGod, string oldGodName, GodSystem? gods = null)
    {
        if (!ChosenSpeaks(c, oldGod, gods)) return null;
        string god = GodRegistry.GetWorshippedGod(c, gods)?.Name ?? "";
        return Loc.Get("old_god.echo_fall", god, oldGodName);
    }
}
