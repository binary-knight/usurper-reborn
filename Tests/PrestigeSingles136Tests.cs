using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G11: the prestige singles. Living Waters heals 20 a round for its 3 rounds; Harmonic Crescendo counts and
/// consumes the party's buffs; Prison Warden's Command is a timed cut that edits no base stat; Reap's kill resets the
/// player's cooldown; Consume the Fallen heals a flat 25 percent (Q14); Alethia's Ward reflects once, 15 percent (Q20).
/// </summary>
[Collection("SharedGameSingletons")]
public class PrestigeSingles136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;
    private static readonly string LongArg = new string('W', 31);

    private static string Plain(string s) => Regex.Replace(s, "\u001b\\[[0-9;]*m", "");

    private static T NoHitRoll<T>(Func<T> body)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try { GameConfig.PlayerHitRollEnabled = false; return body(); }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    internal static Character Hero(CharacterClass cls, int level = 100, long hp = 100_000)
    {
        var c = G.Hero(hp: hp, level: level, cls: cls);
        c.HP = 1000;   // far below the heals
        return c;
    }

    internal static Character Mate(string name, CharacterClass cls = CharacterClass.Warrior, long hp = 100_000)
    {
        var m = G.Mate(name, hp: hp, level: 50);
        m.Class = cls;
        m.HP = 1000;
        return m;
    }

    /// <summary>One cast by <paramref name="actor"/> through the live ability path.</summary>
    internal static ClassAbilityResult Cast(G.Rig rig, Character actor, string id, Monster? target = null, int seed = 136)
    {
        var r = ClassAbilitySystem.UseAbility(actor, id, new Random(seed));
        r.Success.Should().BeTrue(id);
        ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
            .Invoke(rig.Engine, new object?[] { actor, target ?? rig.Monsters[0], rig.Monsters, r, rig.Result })!).GetAwaiter().GetResult();
        return r;
    }

    // ---------------------------------------------------------------- rule 1: Living Waters

    [Fact]
    public void PrestigeSingles136_LivingWaters_HealsTwentyEachRoundForThreeRounds()
    {
        var hero = Hero(CharacterClass.Tidesworn);
        var rig = G.NewRig(hero, new Random(136));
        NoHitRoll(() => Cast(rig, hero, "living_waters"));
        hero.ActiveStatuses[StatusEffect.Regenerating].Should().Be(3);
        for (int round = 1; round <= 3; round++)
        {
            long before = hero.HP;
            hero.ProcessStatusEffects();
            (hero.HP - before).Should().Be(CombatEngine.LivingWatersRegenPerRound, $"round {round} heals the flat 20");
        }
        hero.HasStatus(StatusEffect.Regenerating).Should().BeFalse("the 3 rounds are over");
        long after = hero.HP;
        hero.ProcessStatusEffects();
        hero.HP.Should().Be(after, "no heal after its rounds");
    }

    [Fact]
    public void PrestigeSingles136_OtherRegenerating_KeepsItsOneToSix()
    {
        var c = Hero(CharacterClass.Warrior);
        var heals = new List<long>();
        for (int i = 0; i < 60; i++)
        {
            c.ActiveStatuses[StatusEffect.Regenerating] = 2;
            long before = c.HP;
            c.ProcessStatusEffects();
            heals.Add(c.HP - before);
        }
        heals.Should().OnlyContain(h => h >= 1 && h <= 6, "a Regenerating of another source heals 1d6");
    }

    [Fact]
    public void PrestigeSingles136_LivingWaters_OnALongerRegen_TwentyForItsRoundsThenTheOneToSix()
    {
        var hero = Hero(CharacterClass.Tidesworn);
        var rig = G.NewRig(hero, new Random(136));
        hero.ActiveStatuses[StatusEffect.Regenerating] = 6;   // another source, 6 rounds
        NoHitRoll(() => Cast(rig, hero, "living_waters"));
        hero.ActiveStatuses[StatusEffect.Regenerating].Should().Be(6, "the longer status is kept");
        var heals = new List<long>();
        for (int round = 0; round < 6; round++)
        {
            long before = hero.HP;
            hero.ProcessStatusEffects();
            heals.Add(hero.HP - before);
        }
        heals.Take(3).Should().OnlyContain(h => h == 20, "Living Waters' 3 rounds");
        heals.Skip(3).Should().OnlyContain(h => h >= 1 && h <= 6, "then the other source's 1d6");
    }

    // ---------------------------------------------------------------- rule 3: Harmonic Crescendo

    private static void Buff(Character c, params StatusEffect[] statuses)
    {
        foreach (var s in statuses) c.ActiveStatuses[s] = 5;
    }

    [Fact]
    public void PrestigeSingles136_HarmonicCrescendo_CountsAndConsumesThePartysBuffs()
    {
        var hero = Hero(CharacterClass.Wavecaller);
        var a = Mate("Ann");
        var b = Mate("Bo");
        var foe = G.Dummy();
        var rig = G.NewRig(hero, new Random(136), new List<Character> { a, b }, foe);
        Buff(hero, StatusEffect.Haste);
        Buff(a, StatusEffect.Blessed, StatusEffect.Protected);
        Buff(b, StatusEffect.Shielded, StatusEffect.Reflecting);
        Buff(b, StatusEffect.Poisoned);   // not a buff
        var r = NoHitRoll(() => Cast(rig, hero, "harmonic_crescendo"));
        (foe.MaxHP - foe.HP).Should().Be(r.Damage + 5 * 50, "five buffs on the party, 50 each");
        foreach (var m in new[] { hero, a, b })
            m.ActiveStatuses.Keys.Where(CombatEngine.IsGrandFinaleBuff).Should().BeEmpty($"{m.DisplayName}'s buffs are consumed");
        b.HasStatus(StatusEffect.Poisoned).Should().BeTrue("a status that is not a buff stays");
        Plain(rig.Shown()).Should().Contain("5 buffs consumed for +250");
    }

    [Fact]
    public void PrestigeSingles136_HarmonicCrescendo_ByATeammate_CountsThePlayer()
    {
        var hero = Hero(CharacterClass.Warrior);
        var caster = Mate("Wave", CharacterClass.Wavecaller);
        caster.Level = 100;
        var foe = G.Dummy();
        var rig = G.NewRig(hero, new Random(136), new List<Character> { caster }, foe);
        Buff(hero, StatusEffect.Blessed, StatusEffect.Haste);
        var r = NoHitRoll(() => Cast(rig, caster, "harmonic_crescendo"));
        (foe.MaxHP - foe.HP).Should().Be(r.Damage + 2 * 50, "the player's two buffs count");
        hero.ActiveStatuses.Keys.Where(CombatEngine.IsGrandFinaleBuff).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- rule 4: Prison Warden's Command

    private static Monster Prisoner(bool boss = false, bool mini = false) => new Monster
    {
        Name = "Ogre", TierName = "Ogre", Level = 50, HP = 1_000_000, MaxHP = 1_000_000, Strength = 100, Defence = 40,
        ArmPow = 20, IsBoss = boss, IsMiniBoss = mini, IsActive = true, CanSpeak = false, Experience = 1,
    };

    private static Monster Command(Monster m, int casts = 1, bool tickBetween = false)
    {
        var hero = Hero(CharacterClass.Abysswarden);
        var rig = G.NewRig(hero, new Random(136), null, m);
        for (int i = 0; i < casts; i++)
        {
            if (i > 0 && tickBetween) CombatEngine.TickDominatedMonsters(rig.Monsters);
            NoHitRoll(() => Cast(rig, hero, "prison_wardens_command", m));
        }
        m.WeakenRounds = 0;   // the Weaken rider is its own status; this reads the command's cut alone
        return m;
    }

    [Theory]
    [InlineData(false, false, 50)]
    [InlineData(true, false, 25)]
    [InlineData(false, true, 25)]
    public void PrestigeSingles136_PrisonWardensCommand_CutsForThreeRoundsThenEnds_NoBaseEdit(bool boss, bool mini, int pct)
    {
        var plain = Prisoner(boss, mini);
        long atk0 = plain.GetAttackPower(), def0 = plain.WeakenDefenceCut(plain.Defence), arm0 = plain.WeakenDefenceCut(plain.ArmPow);
        var m = Command(Prisoner(boss, mini));
        m.Strength.Should().Be(100, "no base stat is edited");
        m.Defence.Should().Be(40, "no base stat is edited");
        for (int round = 1; round <= 3; round++)
        {
            m.GetAttackPower().Should().Be(atk0 - atk0 * pct / 100, $"round {round}: attack cut {pct} percent");
            m.WeakenDefenceCut(m.Defence).Should().Be(def0 - def0 * pct / 100, $"round {round}: Defence cut");
            m.WeakenDefenceCut(m.ArmPow).Should().Be(arm0 - arm0 * pct / 100, $"round {round}: armour cut");
            CombatEngine.TickDominatedMonsters(new[] { m });
        }
        m.GetAttackPower().Should().Be(atk0, "the cut ends after 3 rounds");
        m.WeakenDefenceCut(m.Defence).Should().Be(def0);
        m.Strength.Should().Be(100);
        m.Defence.Should().Be(40);
    }

    [Fact]
    public void PrestigeSingles136_PrisonWardensCommand_ARecastRefreshes_NeverCompounds()
    {
        long atk0 = Prisoner().GetAttackPower();
        var m = Command(Prisoner(), casts: 3, tickBetween: true);
        m.Strength.Should().Be(100);
        m.Defence.Should().Be(40);
        m.DominatedRounds.Should().Be(3, "the recast refreshed the rounds");
        m.GetAttackPower().Should().Be(atk0 / 2, "one 50 percent cut, not 50 on 50");
    }

    [Fact]
    public void PrestigeSingles136_PrisonWardensCommand_ALiveHitReadsTheCut()
    {
        // the area core takes the monster's armour off through WeakenDefenceCut (G8's read)
        long Lost(Monster m)
        {
            var hero = Hero(CharacterClass.Abysswarden);
            var rig = G.NewRig(hero, new Random(7), null, m);
            ((Task)typeof(CombatEngine).GetMethod("ApplyAoEDamageHits", F)!
                .Invoke(rig.Engine, new object?[] { rig.Monsters, 1000L, rig.Result, "test", false, hero, null, false, false })!).GetAwaiter().GetResult();
            return m.MaxHP - m.HP;
        }
        long plain = Lost(Prisoner());
        var commanded = Command(Prisoner());
        commanded.IsMarked = false; commanded.MarkedDuration = 0;   // the Marked rider is its own bonus
        long cut = Lost(commanded);
        cut.Should().Be(plain + 10, "half of the 20 armour is not taken off");
    }

    // ---------------------------------------------------------------- rule 5: Reap

    private static Dictionary<string, int> Cooldowns(G.Rig rig, Character actor) =>
        (Dictionary<string, int>)typeof(CombatEngine).GetMethod("CooldownsFor", F)!.Invoke(rig.Engine, new object[] { actor })!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrestigeSingles136_Reap_AKillResetsTheCooldown(bool follower)
    {
        var hero = Hero(CharacterClass.Voidreaver, level: 50);
        var mate = Mate("Reaper", CharacterClass.Voidreaver);
        var weak = G.Foe("Wolf", 40, 1, level: 5, defence: 0);
        var rig = G.NewRig(hero, new Random(136), new List<Character> { mate }, weak);
        var actor = follower ? mate : hero;
        NoHitRoll(() => { G.Act(rig, G.A(CombatActionType.ClassAbility, "reap"), actor); return 0; });
        weak.IsAlive.Should().BeFalse("the reap kills");
        Cooldowns(rig, actor).GetValueOrDefault("reap").Should().Be(0, "the kill resets Reap's cooldown");
        Plain(rig.Shown()).Should().Contain("Reap cooldown reset!");
    }

    [Fact]
    public void PrestigeSingles136_Reap_NoKill_KeepsTheCooldown()
    {
        var hero = Hero(CharacterClass.Voidreaver, level: 50);
        var rig = G.NewRig(hero, new Random(136), null, G.Dummy());
        NoHitRoll(() => { G.Act(rig, G.A(CombatActionType.ClassAbility, "reap")); return 0; });
        Cooldowns(rig, hero)["reap"].Should().Be(ClassAbilitySystem.GetAbility("reap")!.Cooldown);
    }

    // ---------------------------------------------------------------- rule 7: Consume the Fallen (Q14)

    private static SpellSystem.SpellResult Spell(string method, Character caster, int level, int seed, float prof)
    {
        var r = new SpellSystem.SpellResult { Success = true };
        typeof(SpellSystem).GetMethod(method, SF)!
            .Invoke(null, new object?[] { caster, level, null, null, r, new Random(seed), prof });
        return r;
    }

    private static Character Reaver(int level, long wisdom, long maxHp) => new Character
    {
        Name1 = "ps136", Name2 = "Reaver", Class = CharacterClass.Voidreaver, Race = CharacterRace.Human, Level = level,
        Wisdom = wisdom, Intelligence = wisdom, HP = 1, MaxHP = maxHp, Mana = 100_000, MaxMana = 100_000,
        AI = CharacterAI.Human, CombatSpeed = CombatSpeed.Instant,
    };

    [Theory]
    [InlineData(20, 10)]
    [InlineData(20, 300)]
    [InlineData(95, 10)]
    [InlineData(95, 300)]
    public void PrestigeSingles136_ConsumeTheFallen_HealsAFlatQuarter_AtLeast100(int level, long wisdom)
    {
        foreach (long maxHp in new long[] { 200, 1234, 31_007 })
            foreach (float prof in new[] { 1.0f, 1.5f })
                for (int seed = 1; seed <= 10; seed++)
                {
                    var r = Spell("ExecuteVoidreaverSpell", Reaver(level, wisdom, maxHp), 4, seed, prof);
                    r.Healing.Should().Be((int)Math.Max(100, maxHp / 4), $"level {level}, Wisdom {wisdom}, max HP {maxHp}");
                }
    }

    // ---------------------------------------------------------------- rule 8: Alethia's Ward (Q20)

    private static Character Tide(int level = 40, long intel = 120) => new Character
    {
        Name1 = "ps136t", Name2 = "Tide", Class = CharacterClass.Tidesworn, Race = CharacterRace.Human, Level = level,
        Intelligence = intel, Wisdom = 40, HP = 50_000, MaxHP = 50_000, BaseMaxHP = 50_000, Mana = 100_000,
        MaxMana = 100_000, Defence = 10, BaseDefence = 10, AI = CharacterAI.Human, CombatSpeed = CombatSpeed.Instant,
    };

    [Fact]
    public void PrestigeSingles136_AlethiasWard_NoThornReflect_ProtectionAsTheCodeGivesIt()
    {
        var caster = Tide();
        var r = Spell("ExecuteTideswornSpell", caster, 1, 1, 1.0f);
        caster.TempThornReflectPercent.Should().Be(0, "the second reflect is gone");
        caster.TempThornReflectDuration.Should().Be(0);
        r.SpecialEffect.Should().Be("tidal_reflect", "the one reflect, the Reflecting status");
        int expected = (int)typeof(SpellSystem).GetMethod("ScaleProtectionEffect", SF)!
            .Invoke(null, new object?[] { 15 + caster.Level * 2, caster, 1.0f })!;
        r.ProtectionBonus.Should().Be(expected, "15 + 2 x level, through the protection scaler");
        r.Message.Should().Contain("reflects 15% melee damage");
    }

    [Fact]
    public void PrestigeSingles136_AlethiasWard_AHitIsReflectedOnce_FifteenPercent()
    {
        var hero = Tide();
        hero.Strength = 10; hero.BaseStrength = 10;
        Spell("ExecuteTideswornSpell", hero, 1, 1, 1.0f);
        hero.ActiveStatuses[StatusEffect.Reflecting] = 999;   // what tidal_reflect gives the caster
        int hits = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            var brute = G.Foe("Wolf", 1_000_000, 400, level: 30);
            var rig = G.NewRig(hero, new Random(seed), null, brute);
            hero.HP = hero.MaxHP;
            ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
                .Invoke(rig.Engine, new object?[] { brute, hero, rig.Result, new List<Monster> { brute } })!).GetAwaiter().GetResult();
            long taken = hero.MaxHP - hero.HP;
            if (taken <= 0) continue;
            hits++;
            (brute.MaxHP - brute.HP).Should().Be(Math.Max(1, (long)(taken * GameConfig.WavecallerReflectionPercent)),
                $"seed {seed}: one reflect of 15 percent of {taken}");
        }
        hits.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void PrestigeSingles136_AlethiasWard_TextStatesTheProtectionAndTheFifteen(string lang)
    {
        string desc = Loc.GetIn(lang, "spell.tidesworn.1.desc");
        desc.Should().Contain("15 + 2").And.Contain("15").And.NotContain("+20").And.NotContain("10");
        var info = SpellSystem.GetSpellInfo(CharacterClass.Tidesworn, 1)!;
        info.Description.Should().Be(Loc.GetIn("en", "spell.tidesworn.1.desc"), "the built-in text matches the English key");
    }

    // ---------------------------------------------------------------- widths

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void PrestigeSingles136_ChangedRows_FitSeventyNine(string lang)
    {
        var rows = new List<string>();
        rows.AddRange(UIHelper.MessageRows(Loc.GetIn(lang, "combat.ability_prison_wardens_command", LongArg, 25, 25)));
        rows.AddRange(SpellSystem.MessageRows(Loc.GetIn(lang, "spell.tidesworn.1.desc")));
        rows.AddRange(SpellSystem.MessageRows(Loc.GetIn(lang, "combat.spell_alethias_ward_cast", LongArg, 9999, 15)));
        rows.Should().OnlyContain(r => r.Length <= MaxWidth, lang);
    }
}
