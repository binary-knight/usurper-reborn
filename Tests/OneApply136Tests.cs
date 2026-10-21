using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G3 (CP2, Q8): one cast, one apply. On the live ability path (ApplyAbilityEffectsMultiMonster, the player's and,
/// through TryTeammateClassAbility, every NPC teammate's) the caster gets each part of one cast (heal, attack bonus,
/// defence bonus) once: the generic block applies it, or the effect's handler does, never both. Divine Avatar gives one
/// attack bonus and one defence bonus, both from the spell result, with no flat +55 on top (PvE and PvP).
/// </summary>
[Collection("SharedGameSingletons")]
public class OneApply136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const long Big = 1_000_000;

    internal sealed class Cast
    {
        public ClassAbilityResult Result = null!;
        public long Healed;
        public int Attack, AttackRounds, Defense, DefenseRounds;
        public long[] MateHealed = Array.Empty<long>();
        public int[] MateAttack = Array.Empty<int>(), MateDefense = Array.Empty<int>();
        public string Shown = "";
        public Character Actor = null!;
    }

    /// <summary>A caster of the class at level 50, wounded far below any heal (so no heal is capped), clean slots.</summary>
    internal static Character Caster(CharacterClass cls, bool npc = false, int level = 50)
    {
        var c = npc ? G.Mate("Ally", hp: Big, level: level) : G.Hero(hp: Big, level: level, cls: cls);
        c.Class = cls;
        c.Constitution = 30; c.BaseConstitution = 30;
        c.HP = 1000;
        c.TempAttackBonus = 0; c.TempAttackBonusDuration = 0;
        c.TempDefenseBonus = 0; c.TempDefenseBonusDuration = 0;
        return c;
    }

    private static Character WoundedMate(string name)
    {
        var m = G.Mate(name, hp: Big);
        m.HP = 1000;
        return m;
    }

    /// <summary>
    /// One cast through ApplyAbilityEffectsMultiMonster. <paramref name="npc"/>: an NPC teammate casts (the actor is not
    /// the fight's player, as in TryTeammateClassAbility). <paramref name="mates"/>: wounded teammates in result.Teammates.
    /// <paramref name="casts"/>: the same ability cast that many times in a row on the same caster.
    /// </summary>
    internal static Cast Run(string id, CharacterClass cls, bool npc = false, int mates = 0, int casts = 1, Character? caster = null,
        bool casterInTeammates = false, int seed = 136)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try
        {
            GameConfig.PlayerHitRollEnabled = false;
            var hero = npc ? G.Hero(hp: Big) : (caster ?? Caster(cls));
            var actor = npc ? (caster ?? Caster(cls, npc: true)) : hero;
            var team = Enumerable.Range(0, mates).Select(i => WoundedMate("Mate" + i)).ToList();
            var all = new List<Character>(team);
            if (npc && casterInTeammates) all.Insert(0, actor);
            var rig = G.NewRig(hero, new G.RecordingRandom(seed), all, G.Dummy());
            long hp0 = actor.HP;
            var mateHp0 = team.Select(t => t.HP).ToArray();
            ClassAbilityResult r = null!;
            for (int i = 0; i < casts; i++)
            {
                r = ClassAbilitySystem.UseAbility(actor, id, new Random(seed + i));
                r.Success.Should().BeTrue(id);
                ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                    .Invoke(rig.Engine, new object?[] { actor, rig.Monsters[0], rig.Monsters, r, rig.Result })!).GetAwaiter().GetResult();
            }
            return new Cast
            {
                Result = r, Actor = actor, Healed = actor.HP - hp0,
                Attack = actor.TempAttackBonus, AttackRounds = actor.TempAttackBonusDuration,
                Defense = actor.TempDefenseBonus, DefenseRounds = actor.TempDefenseBonusDuration,
                MateHealed = team.Select((t, i) => t.HP - mateHp0[i]).ToArray(),
                MateAttack = team.Select(t => t.TempAttackBonus).ToArray(),
                MateDefense = team.Select(t => t.TempDefenseBonus).ToArray(),
                Shown = rig.Shown(),
            };
        }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    // The generic block's caster lines (English): each must not show for a part the handler applies.
    private const string GenericHealYou = "You recover ";
    private const string GenericAttackYou = "Attack increased by ";
    private const string GenericDefenseYou = "Defense increased by ";

    private static int Count(string text, string part) => text.Split('\n').Count(l => l.Contains(part));

    // heals: the caster healed once

    [Theory]
    [InlineData("healing_mist", CharacterClass.Alchemist, "Your alchemical mist heals you for ")]
    [InlineData("rejuvenating_brew", CharacterClass.Alchemist, "Your alchemical mist heals you for ")]
    [InlineData("grand_remedy", CharacterClass.Alchemist, "Your remedy fully restores you!")]
    [InlineData("circle_of_healing", CharacterClass.Cleric, "Your divine light heals you for ")]
    [InlineData("holy_covenant", CharacterClass.Cleric, "Covenant of Faith heals you for ")]
    [InlineData("beacon_of_light", CharacterClass.Cleric, "Beacon of Faith heals you for ")]
    [InlineData("oceans_embrace", CharacterClass.Tidesworn, "The Ocean embraces you, healing ")]
    public void OneApply136_PartyHeal_HealsThePlayerCasterOnce(string id, CharacterClass cls, string handlerLine)
    {
        ClassAbilitySystem.GetAbility(id)!.CanTargetAlly.Should().BeFalse($"{id} has no ally pick, so the generic block's prompt is not lost");
        var c = Run(id, cls, mates: 2);
        c.Result.Healing.Should().BeGreaterThan(0, id);
        c.Healed.Should().Be(c.Result.Healing, $"{id} heals its caster once, by its one scaled amount");
        Count(c.Shown, GenericHealYou).Should().Be(0, $"{id}: the handler's line shows the heal, the generic line does not repeat it");
        Count(c.Shown, handlerLine).Should().Be(1, $"{id}: one heal line for the caster");
        long share = id == "oceans_embrace" ? c.Result.Healing : (long)(c.Result.Healing * 0.75);
        c.MateHealed.Should().AllBeEquivalentTo(share, $"{id}: each teammate's share is unchanged");
    }

    [Theory]
    [InlineData("healing_mist", CharacterClass.Alchemist)]
    [InlineData("rejuvenating_brew", CharacterClass.Alchemist)]
    [InlineData("grand_remedy", CharacterClass.Alchemist)]
    [InlineData("circle_of_healing", CharacterClass.Cleric)]
    [InlineData("holy_covenant", CharacterClass.Cleric)]
    [InlineData("beacon_of_light", CharacterClass.Cleric)]
    [InlineData("oceans_embrace", CharacterClass.Tidesworn)]
    public void OneApply136_PartyHeal_HealsAnNpcCasterOnce(string id, CharacterClass cls)
    {
        var c = Run(id, cls, npc: true, mates: 1);
        c.Healed.Should().Be(c.Result.Healing, $"{id}: an NPC caster is healed once too");
        Count(c.Shown, c.Actor.DisplayName + " recovers ").Should().Be(0, $"{id}: no generic heal line for the NPC caster");
        long share = id == "oceans_embrace" ? c.Result.Healing : (long)(c.Result.Healing * 0.75);
        c.MateHealed.Should().AllBeEquivalentTo(share, $"{id}: the teammate's share is unchanged");
    }

    [Fact]
    public void OneApply136_NpcCasterInTheTeammateList_TakesItsOwnShareOnce_TheG5Part()
    {
        // When the NPC caster is itself in result.Teammates (as in a real party), G5 (CP1) gives it its own share once and
        // no teammate's 75 percent share on top; G3 removed the generic block's second heal.
        var c = Run("healing_mist", CharacterClass.Alchemist, npc: true, mates: 1, casterInTeammates: true);
        c.Healed.Should().Be(c.Result.Healing);
        c.MateHealed.Should().AllBeEquivalentTo((long)(c.Result.Healing * 0.75), "the teammate's share is unchanged");
    }

    // attack and defence bonuses: once per cast

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneApply136_StimulantBrew_AttackOnce_TeammatesThreeQuarters(bool npc)
    {
        var c = Run("stimulant_brew", CharacterClass.Alchemist, npc: npc, mates: 2);
        c.Result.AttackBonus.Should().BeGreaterThan(0);
        c.Attack.Should().Be(c.Result.AttackBonus, "the attack bonus lands once");
        c.AttackRounds.Should().Be(c.Result.Duration);
        c.MateAttack.Should().AllBeEquivalentTo((int)(c.Result.AttackBonus * 0.75));
        if (!npc)
        {
            Count(c.Shown, GenericAttackYou).Should().Be(0, "the handler's line shows the bonus");
            Count(c.Shown, "Your stimulant vials energize you!").Should().Be(1);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneApply136_SmokeScreen_DefenseOnce_TeammatesFull(bool npc)
    {
        var c = Run("smoke_screen", CharacterClass.Alchemist, npc: npc, mates: 2);
        c.Defense.Should().Be(c.Result.DefenseBonus, "the defence bonus lands once");
        c.DefenseRounds.Should().Be(c.Result.Duration);
        c.MateDefense.Should().AllBeEquivalentTo(c.Result.DefenseBonus);
        if (!npc) Count(c.Shown, GenericDefenseYou).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneApply136_BattleTincture_AttackAndDefenseOnce(bool npc)
    {
        var c = Run("battle_tincture", CharacterClass.Alchemist, npc: npc, mates: 2);
        c.Attack.Should().Be(c.Result.AttackBonus);
        c.Defense.Should().Be(c.Result.DefenseBonus);
        c.MateAttack.Should().AllBeEquivalentTo((int)(c.Result.AttackBonus * 0.75));
        c.MateDefense.Should().AllBeEquivalentTo((int)(c.Result.DefenseBonus * 0.75));
        if (!npc)
        {
            Count(c.Shown, GenericAttackYou).Should().Be(0);
            Count(c.Shown, GenericDefenseYou).Should().Be(0);
            Count(c.Shown, "Your battle brew empowers you!").Should().Be(1);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneApply136_BeaconOfLight_DefenseOnce_TeammatesFull(bool npc)
    {
        var c = Run("beacon_of_light", CharacterClass.Cleric, npc: npc, mates: 2);
        c.Defense.Should().Be(c.Result.DefenseBonus);
        c.DefenseRounds.Should().Be(c.Result.Duration);
        c.MateDefense.Should().AllBeEquivalentTo(c.Result.DefenseBonus);
        if (!npc) Count(c.Shown, GenericDefenseYou).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneApply136_SmokeBomb_TableDefenseForItsDuration_NoFlat40(bool npc)
    {
        var c = Run("smoke_bomb", CharacterClass.Alchemist, npc: npc);
        var a = ClassAbilitySystem.GetAbility("smoke_bomb")!;
        a.DefenseBonus.Should().Be(45);
        a.Duration.Should().Be(3);
        c.Result.DefenseBonus.Should().Be((int)(45 * (1.0 + 50 * 0.03)), "the table's 45 on the level scale (a Utility ability)");
        c.Defense.Should().Be(c.Result.DefenseBonus, "the table's defence, with no flat 40 on top");
        c.DefenseRounds.Should().Be(3, "for the table's 3 rounds");
        if (!npc) Count(c.Shown, GenericDefenseYou).Should().Be(1, "the generic block's one line shows it");
    }

    [Fact]
    public void OneApply136_SmokeBomb_TwoCasts_DoNotStack()
    {
        // The generic block's rule (the higher wins) holds the one apply, so a second cast refreshes and never adds;
        // today the flat 40 was added again on every cast.
        var c = Run("smoke_bomb", CharacterClass.Alchemist, casts: 2);
        c.Defense.Should().Be(c.Result.DefenseBonus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneApply136_Rockbiter_DefenseOncePerCast(bool npc)
    {
        var c = Run("rockbiter", CharacterClass.MysticShaman, npc: npc);
        c.Result.DefenseBonus.Should().BeGreaterThan(0);
        c.Defense.Should().Be(c.Result.DefenseBonus, "one cast adds its defence once");
        c.DefenseRounds.Should().Be(GameConfig.ShamanEnchantDuration);
        if (!npc)
        {
            Count(c.Shown, GenericDefenseYou).Should().Be(0, "the enchant line shows the defence");
            Count(c.Shown, "Your weapon pulses with earthen power!").Should().Be(1);
        }
    }

    [Fact]
    public void OneApply136_Rockbiter_TwoCastsInARow_GiveTheBonusOnce_WithTheRoundsRefreshed()
    {
        // Cooldown 0: 1.3.6 G4 (supervisor 2026-10-21T09:13:44Z), Rockbiter is its own slot entry, so a recast refreshes
        // its rounds and does not stack.
        var c = Run("rockbiter", CharacterClass.MysticShaman, casts: 2);
        c.Defense.Should().Be(c.Result.DefenseBonus);
        c.DefenseRounds.Should().Be(GameConfig.ShamanEnchantDuration);
    }

    [Theory]
    [InlineData("shadow_step", 5, 2, false)]
    [InlineData("shadow_step", 5, 2, true)]
    [InlineData("vanish", 4, 1, false)]
    [InlineData("vanish", 4, 1, true)]
    public void OneApply136_AssassinEvasion_DefensePlusConOnce(string id, int conDivisor, int rounds, bool npc)
    {
        var c = Run(id, CharacterClass.Assassin, npc: npc);
        c.Result.DefenseBonus.Should().BeGreaterThan(0);
        int expected = c.Result.DefenseBonus + (int)(c.Actor.Constitution / conDivisor);
        c.Defense.Should().Be(expected, $"{id}: its defence plus the CON part, once");
        c.DefenseRounds.Should().Be(rounds);
        c.Actor.DodgeNextAttack.Should().BeTrue();
        if (!npc)
        {
            Count(c.Shown, GenericDefenseYou).Should().Be(0, $"{id}: the handler's line shows the defence");
            Count(c.Shown, $"+{expected} DEF").Should().Be(1);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OneApply136_HarmonicShield_Forty_Once(bool npc)
    {
        var c = Run("harmonic_shield", CharacterClass.Wavecaller, npc: npc, mates: 2);
        c.Defense.Should().Be(40, "+40 defence, as the text says, once");
        c.DefenseRounds.Should().Be(c.Result.Duration);
        c.MateDefense.Should().AllBeEquivalentTo(40, "each teammate's 40 is unchanged");
        if (!npc) Count(c.Shown, GenericDefenseYou).Should().Be(0);
    }

    // unchanged abilities

    [Fact]
    public void OneApply136_PowerStrike_Unchanged()
    {
        var c = Run("power_strike", CharacterClass.Warrior);
        c.Attack.Should().Be(c.Result.AttackBonus);
        c.Defense.Should().Be(0);
        c.Healed.Should().Be(0);
    }

    [Theory]
    [InlineData("shield_wall", CharacterClass.Warrior)]
    [InlineData("divine_aegis", CharacterClass.Cleric)]
    [InlineData("tidal_colossus", CharacterClass.Tidesworn)]
    public void OneApply136_PlainBuffs_KeepTheGenericBlocksValues(string id, CharacterClass cls)
    {
        var c = Run(id, cls);
        c.Defense.Should().Be(c.Result.DefenseBonus, id);
        c.Attack.Should().Be(c.Result.AttackBonus, id);
        if (c.Result.Healing > 0) c.Healed.Should().Be(c.Result.Healing, id);
        if (c.Result.DefenseBonus > 0) Count(c.Shown, GenericDefenseYou).Should().Be(1, $"{id}: the generic line still shows");
    }

    [Fact]
    public void OneApply136_HandlerTable_NamesOnlyEffectsOfTheFifteen()
    {
        var expected = new[]
        {
            "party_heal_mist", "party_remedy", "party_stimulant", "party_smoke_screen", "party_battle_brew",
            "party_heal_divine", "party_heal_cleanse", "party_beacon", "oceans_embrace", "harmonic_shield",
            "shaman_enchant_earth", "evasion", "vanish",
        };
        CombatEngine.HandlerCasterPartEffects.Should().BeEquivalentTo(expected);
        foreach (var a in ClassAbilitySystem.GetAllAbilities().Where(a => expected.Contains(a.SpecialEffect)))
            a.CanTargetAlly.Should().BeFalse(a.Id);
    }

    // coverage: every class ability through the live path

    /// <summary>
    /// The handlers whose caster defence is a value of their own, not the result's part: the one apply must equal it
    /// exactly (so the generic block's part on top of it fails).
    /// </summary>
    private static readonly Dictionary<string, Func<Cast, int>> OwnValue = new()
    {
        ["shadow_step"] = c => c.Result.DefenseBonus + (int)(c.Actor.Constitution / 5),   // "scales with CON"
        ["vanish"] = c => c.Result.DefenseBonus + (int)(c.Actor.Constitution / 4),        // "scales with CON"
        ["harmonic_shield"] = c => 40,                                                     // "+40 defense"
    };

    public static IEnumerable<object[]> EveryAbility =>
        ClassAbilitySystem.GetAllAbilities().Where(a => a.AvailableToClasses.Length > 0).Select(a => new object[] { a.Id });

    [Theory]
    [MemberData(nameof(EveryAbility))]
    public void OneApply136_Coverage_NoPartLandsAsTheGenericPlusTheHandler(string id)
    {
        var a = ClassAbilitySystem.GetAbility(id)!;
        Cast c;
        try { c = Run(id, a.AvailableToClasses[0]); }
        catch (Exception ex) { throw new Exception($"{id}: {(ex.InnerException ?? ex).Message}", ex); }
        var r = c.Result;
        if (r.Healing > 0)
            c.Healed.Should().BeLessThan(2L * r.Healing, $"{id}: its heal does not land twice on the caster");
        if (r.AttackBonus > 0 && id == "offer_flesh")
            // 1.3.6 G4 (Q13): twice the scaled bonus below a quarter of maximum HP after the sacrifice, once otherwise
            c.Attack.Should().Be(c.Actor.HP < c.Actor.MaxHP / 4 ? 2 * r.AttackBonus : r.AttackBonus, $"{id}: its exact bonus");
        else if (r.AttackBonus > 0)
            c.Attack.Should().BeLessThan(2 * r.AttackBonus, $"{id}: its attack bonus does not land twice");
        if (r.DefenseBonus > 0)
            c.Defense.Should().BeLessThan(2 * r.DefenseBonus, $"{id}: its defence bonus does not land twice");
        if (OwnValue.TryGetValue(id, out var own))
            c.Defense.Should().Be(own(c), $"{id}: its own defence once, with no generic part on top");
        else if (r.DefenseBonus > 0 && c.Defense > r.DefenseBonus)
            c.Defense.Should().BeLessThan(r.DefenseBonus + 40, $"{id}: no flat add on top of its defence (as Smoke Bomb's 40 was)");
    }
}

/// <summary>1.3.6 G3 (Q8): Divine Avatar, one attack bonus and one defence bonus from the spell result.</summary>
[Collection("SharedGameSingletons")]
public class OneApply136AvatarTests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Cleric(string name = "Tester")
    {
        var c = G.Hero(hp: 100_000, level: 90, cls: CharacterClass.Cleric, name: name);   // spell 23 opens at level 90
        c.Intelligence = 40; c.BaseIntelligence = 40; c.Wisdom = 40; c.BaseWisdom = 40;
        c.MaxMana = 100_000; c.Mana = 100_000;
        c.Spell = Enumerable.Range(1, 25).Select(slot => new List<bool> { slot == 23 }).ToList();   // Divine Avatar learned
        return c;
    }

    /// <summary>A successful cast of spell 23 (CastSpell draws from the shared random, so a fumble is cast again).</summary>
    private static SpellSystem.SpellResult Avatar(Character caster)
    {
        for (int i = 0; i < 200; i++)
        {
            caster.Mana = caster.MaxMana;
            var r = SpellSystem.CastSpell(caster, 23, null);
            if (r.Success) return r;
        }
        throw new Exception("Divine Avatar never succeeded in 200 casts");
    }

    [Fact]
    public void OneApply136_DivineAvatar_SelfCast_OneAttackOneDefense_FromTheResult()
    {
        var cleric = Cleric();
        var r = Avatar(cleric);
        r.SpecialEffect.Should().Be("avatar");
        r.AttackBonus.Should().BeGreaterThan(55, "55 plus level");
        r.ProtectionBonus.Should().BeGreaterThan(0);
        r.Duration.Should().Be(999);
        var rig = G.NewRig(cleric, new G.RecordingRandom(136));
        typeof(CombatEngine).GetMethod("ApplySpellEffects", F)!
            .Invoke(rig.Engine, new object?[] { cleric, null, r, rig.Result, rig.Monsters, null });
        cleric.TempAttackBonus.Should().Be(r.AttackBonus, "the spell result's attack bonus, no +55 on top");
        cleric.TempAttackBonusDuration.Should().Be(999, "the whole fight");
        cleric.MagicACBonus.Should().Be(r.ProtectionBonus, "the ward is the spell result's protection");
        cleric.TempDefenseBonus.Should().Be(0, "no flat +55 defence");
        var shown = rig.Shown();
        shown.Should().NotContain("+55");
        shown.Should().Contain("channels divine power!");
    }

    [Fact]
    public void OneApply136_DivineAvatar_OnAnAlly_OneAttackOneDefense()
    {
        // The leader's buff aimed at an ally and a grouped follower's override both land through ApplySpellEffects with
        // the ally as caster (ApplyBuffTo).
        var cleric = Cleric();
        var ally = G.Mate("Ally", hp: 50_000, level: 40);
        var r = Avatar(cleric);
        var rig = G.NewRig(cleric, new G.RecordingRandom(136), new List<Character> { ally });
        typeof(CombatEngine).GetMethod("ApplySpellEffects", F)!
            .Invoke(rig.Engine, new object?[] { ally, null, r, null, rig.Monsters, cleric });
        ally.TempAttackBonus.Should().Be(r.AttackBonus);
        ally.MagicACBonus.Should().Be(r.ProtectionBonus);
        ally.TempDefenseBonus.Should().Be(0);
        cleric.TempAttackBonus.Should().Be(0);
    }

    [Fact]
    public void OneApply136_DivineAvatar_PvP_OneAttackOneDefense()
    {
        // The duel path: the protection lands as the ward, the attack bonus as Power Stance for the spell's duration, and
        // the avatar case adds nothing on top.
        string last = "";
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var attacker = Cleric("Duelist");
            var defender = G.Hero(hp: 100_000, level: 60, name: "Foe");
            var rig = G.NewRig(attacker, new G.RecordingRandom(136));
            var action = new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = 23 };
            ((Task)typeof(CombatEngine).GetMethod("ExecutePvPSpell", F)!
                .Invoke(rig.Engine, new object?[] { attacker, defender, action, rig.Result })!).GetAwaiter().GetResult();
            last = rig.Shown();
            if (attacker.MagicACBonus == 0) continue;   // a fumble: cast again
            attacker.HasStatus(StatusEffect.PowerStance).Should().BeTrue("the attack bonus lands once, as Power Stance");
            attacker.TempAttackBonus.Should().Be(0, "no flat +55 attack");
            attacker.TempDefenseBonus.Should().Be(0, "no flat +55 defence");
            rig.Shown().Should().NotContain("+55 ATK");
            return;
        }
        throw new Exception("Divine Avatar never succeeded in the duel: " + last);
    }

    [Fact]
    public void OneApply136_DivineAvatar_PvPSpecialEffect_AddsNothing()
    {
        var attacker = Cleric("Duelist");
        var defender = G.Hero(hp: 100_000, level: 60, name: "Foe");
        var rig = G.NewRig(attacker, new G.RecordingRandom(136));
        var r = new SpellSystem.SpellResult { Success = true, SpecialEffect = "avatar", Duration = 999, AttackBonus = 115, ProtectionBonus = 90 };
        typeof(CombatEngine).GetMethod("ApplyPvPSpellEffect", F)!.Invoke(rig.Engine, new object?[] { attacker, defender, r });
        attacker.TempAttackBonus.Should().Be(0);
        attacker.TempDefenseBonus.Should().Be(0);
    }

    [Fact]
    public void OneApply136_DivineAvatar_Line_NoNumber_FitsInFiveLanguages()
    {
        foreach (var l in new[] { "en", "es", "fr", "hu", "it" })
        {
            var t = Loc.GetIn(l, "combat.channels_divine_power");
            t.Should().NotContain("55", l);
            t.Should().Contain("{0}", l);
            if (l == "en" || l == "hu")
                string.Format(t, new string('W', 30)).Length.Should().BeLessThanOrEqualTo(79, l);
        }
    }
}
