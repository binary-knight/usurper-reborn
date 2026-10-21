using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G7 (CP6, Q2, Q5, Q6): confusion on a boss follows the soft control rule (a quarter shrugged off, half the
/// rounds, at most GameConfig.MassConfusionBossMaxRounds); Frost Touch and Ice Storm slow through SoftControlRounds;
/// marks count down once a round; Assassinate, Wrath of the Deep and Annihilation do not kill a mini boss outright;
/// Disappearing Act and Escape follow the flee rules.
/// </summary>
[Collection("SharedGameSingletons")]
public class ControlRules136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string Plain(string s) => Regex.Replace(s, "\u001b\\[[0-9;]*[A-Za-z]", "");

    private static int Count(string text, string part)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(part, i, StringComparison.Ordinal)) >= 0) { n++; i += part.Length; }
        return n;
    }

    private static Monster Foe(bool boss = false, bool mini = false, long hp = 1_000_000_000L) => new Monster
    {
        Name = "Golem", TierName = "Golem", Level = 60, HP = hp, MaxHP = hp, Strength = 1, Defence = 0,
        Experience = 1, Gold = 0, IsActive = true, CanSpeak = false, IsBoss = boss, IsMiniBoss = mini,
    };

    private static Character Hero(CharacterClass cls, int level = 100)
    {
        var h = G.Hero(hp: 100_000, strength: 200, level: level, cls: cls);
        h.Intelligence = 100; h.Wisdom = 100; h.Charisma = 100; h.Dexterity = 100;
        h.Mana = 100_000; h.MaxMana = 100_000;
        return h;
    }

    private static G.Rig Rig(Character hero, int seed, params Monster[] monsters)
    {
        var rig = G.NewRig(hero, new Random(seed), null, monsters);
        rig.Result.Monsters = rig.Monsters;
        return rig;
    }

    private static string Shown(G.Rig rig) => Plain(rig.Shown());

    /// <summary>The ability's effects, as the multi-monster path applies them, with no to-hit roll.</summary>
    private static void Ability(G.Rig rig, Character actor, Monster target, string abilityId, int damage = 0)
    {
        var ability = ClassAbilitySystem.GetAbility(abilityId)!;
        var ar = new ClassAbilityResult
        {
            Success = true, AbilityUsed = ability, Damage = damage, Duration = ability.Duration,
            SpecialEffect = ability.SpecialEffect ?? "",
        };
        bool saved = GameConfig.PlayerHitRollEnabled;
        try
        {
            GameConfig.PlayerHitRollEnabled = false;
            ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                .Invoke(rig.Engine, new object?[] { actor, target, rig.Monsters, ar, rig.Result })!).GetAwaiter().GetResult();
        }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    /// <summary>A spell's special effect on one monster, as the multi-monster spell path applies it.</summary>
    private static void Spell(G.Rig rig, Character caster, Monster target, string effect, int duration) =>
        typeof(CombatEngine).GetMethod("HandleSpecialSpellEffectOnMonster", F)!
            .Invoke(rig.Engine, new object?[] { target, effect, duration, caster, 0L, rig.Result });

    /// <summary>One monster turn as combat runs it at the start of a round, then `more` further actions in the same round.</summary>
    private static void MonsterRound(G.Rig rig, Monster m, int more)
    {
        m.StatusTickedThisRound = false;
        for (int i = 0; i <= more; i++)
            ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
                .Invoke(rig.Engine, new object?[] { m, rig.Hero, rig.Result, rig.Monsters })!).GetAwaiter().GetResult();
    }

    private static bool Escaped(G.Rig rig) => (bool)typeof(CombatEngine).GetField("globalEscape", F)!.GetValue(rig.Engine)!;

    private static void ResetEscape(G.Rig rig) => typeof(CombatEngine).GetField("globalEscape", F)!.SetValue(rig.Engine, false);

    // ---------------------------------------------------------------- rule 2: confusion

    [Fact]
    public void ControlRules136_TheGameConfigValues_AreAQuarterAndTwoRounds()
    {
        GameConfig.BossSoftControlResistPercent.Should().Be(25, "Q2: bosses shrug confusion off a quarter of the time");
        GameConfig.MassConfusionBossMaxRounds.Should().Be(2, "Q2: at most 2 rounds on a boss, as Mass Confusion");
    }

    /// <summary>The confusion sources with no rider roll: Primal Scream, Carnival of Chaos (aoe_confusion) and Causality Loop.</summary>
    public static IEnumerable<object[]> PlainConfusers() => new[]
    {
        new object[] { "primal_scream", CharacterClass.Barbarian, 1 },
        new object[] { "carnival_of_chaos", CharacterClass.Jester, 1 },
        new object[] { "causality_loop", CharacterClass.Cyclebreaker, 0 },
    };

    [Theory]
    [MemberData(nameof(PlainConfusers))]
    public void ControlRules136_AConfusionAbilityOnABoss_IsShruggedOffAQuarterOfTheTime_AndHalved(string id, CharacterClass cls, int damage)
    {
        var hero = Hero(cls);
        var rig = Rig(hero, 1361);
        int landed = 0, trials = 800;
        var asked = ClassAbilitySystem.GetAbility(id)!.Duration > 0 ? ClassAbilitySystem.GetAbility(id)!.Duration : (id == "causality_loop" ? 3 : 2);
        for (int i = 0; i < trials; i++)
        {
            var boss = Foe(boss: i % 2 == 0, mini: i % 2 == 1);
            rig.Monsters.Clear(); rig.Monsters.Add(boss);
            Ability(rig, hero, boss, id, damage);
            if (boss.IsConfused)
            {
                landed++;
                boss.ConfusedDuration.Should().Be(Math.Min(Math.Max(1, asked / 2), GameConfig.MassConfusionBossMaxRounds),
                    $"{id} on a boss takes half the rounds, at most the cap");
            }
        }
        int resisted = Count(Shown(rig), Loc.Get("combat.sage_control_resist", MonsterNames.Display(Foe())));
        (landed + resisted).Should().Be(trials, "each cast either confuses the boss or it shrugs it off, and says so");
        ((double)resisted / trials).Should().BeInRange(0.20, 0.30, $"{id}: a quarter shrugged off");
    }

    [Theory]
    [MemberData(nameof(PlainConfusers))]
    public void ControlRules136_AConfusionAbilityOnANormalMonster_IsUnchanged(string id, CharacterClass cls, int damage)
    {
        var hero = Hero(cls);
        var rig = Rig(hero, 1362);
        var m = Foe();
        rig.Monsters.Clear(); rig.Monsters.Add(m);
        Ability(rig, hero, m, id, damage);
        m.IsConfused.Should().BeTrue();
        m.ConfusedDuration.Should().Be(id == "causality_loop" ? 3 : 2);
        Shown(rig).Should().NotContain(Loc.Get("combat.sage_control_resist", MonsterNames.Display(m)));
    }

    /// <summary>Mind Spike (psychic) and Psychic Scream: a quarter of the casts roll the 1 round confusion rider.</summary>
    [Theory]
    [InlineData("psychic")]
    [InlineData("psychic_scream")]
    public void ControlRules136_ThePsychicConfuseRider_OnABoss_LandsThreeQuartersAsOften(string effect)
    {
        var hero = Hero(CharacterClass.Sage);
        double Rate(bool boss)
        {
            var rig = Rig(hero, boss ? 1363 : 1364);
            int confused = 0, trials = 3000;
            for (int i = 0; i < trials; i++)
            {
                var m = Foe(boss: boss);
                Spell(rig, hero, m, effect, effect == "psychic_scream" ? 2 : 0);
                if (m.IsConfused)
                {
                    confused++;
                    m.ConfusedDuration.Should().Be(1, "the rider is one round, on a boss too");
                }
            }
            return (double)confused / trials;
        }
        Rate(false).Should().BeInRange(0.22, 0.28, "a quarter of the casts confuse a normal monster, as before");
        Rate(true).Should().BeInRange(0.16, 0.215, "on a boss the rider is shrugged off a quarter of the time: 0.25 x 0.75");
    }

    [Fact]
    public void ControlRules136_MindSpikesShruggedRider_SaysSo()
    {
        var hero = Hero(CharacterClass.Sage);
        var rig = Rig(hero, 1365);
        int confused = 0, trials = 3000;
        for (int i = 0; i < trials; i++)
        {
            var m = Foe(boss: true);
            Spell(rig, hero, m, "psychic", 0);
            if (m.IsConfused) confused++;
        }
        var text = Shown(rig);
        int resisted = Count(text, Loc.Get("combat.sage_control_resist", MonsterNames.Display(Foe())));
        Count(text, Loc.Get("combat.spell_psychic", MonsterNames.Display(Foe()))).Should().Be(confused);
        ((double)resisted / (resisted + confused)).Should().BeInRange(0.18, 0.32, "a quarter of the riders are shrugged off");
    }

    [Theory]
    [InlineData("confusion")]
    [InlineData("mass_confusion")]
    public void ControlRules136_TheSageConfusionSpells_HoldABossAtMostTwoRounds(string effect)
    {
        var hero = Hero(CharacterClass.Sage);
        var rig = Rig(hero, 1366);
        int landed = 0;
        for (int i = 0; i < 200; i++)
        {
            var boss = Foe(boss: true);
            Spell(rig, hero, boss, effect, 8);
            if (boss.IsConfused)
            {
                landed++;
                boss.ConfusedDuration.Should().Be(GameConfig.MassConfusionBossMaxRounds, $"{effect} for 8 rounds holds a boss 2");
            }
        }
        landed.Should().BeInRange(130, 170, "a quarter shrugged off");
        var normal = Foe();
        Spell(rig, hero, normal, effect, 8);
        normal.ConfusedDuration.Should().Be(8, "a normal monster takes the rounds asked");
    }

    // ---------------------------------------------------------------- rule 3: frost slows

    [Fact]
    public void ControlRules136_FrostTouchAndIceStorm_SlowABossThroughTheSoftControlRule()
    {
        var hero = Hero(CharacterClass.Magician);
        var rig = Rig(hero, 1367);
        int slowed = 0, trials = 800;
        for (int i = 0; i < trials; i++)
        {
            var boss = Foe(boss: i % 2 == 0, mini: i % 2 == 1);
            Spell(rig, hero, boss, "frost", 0);
            if (boss.IsSlowed)
            {
                slowed++;
                boss.SlowDuration.Should().Be(1, "half the 2 rounds on a boss");
            }
            else boss.SlowDuration.Should().Be(0);
        }
        ((double)slowed / trials).Should().BeInRange(0.70, 0.80, "a quarter shrugged off");
        Count(Shown(rig), Loc.Get("combat.sage_control_resist", MonsterNames.Display(Foe()))).Should().Be(trials - slowed);

        var normal = Foe();
        Spell(rig, hero, normal, "frost", 0);
        normal.SlowDuration.Should().Be(2, "a normal monster is slowed the full 2 rounds, as before");
    }

    [Theory]
    [InlineData(5, "Frost Touch", false)]
    [InlineData(13, "Ice Storm", true)]
    public void ControlRules136_FrostTouchAndIceStorm_CarryTheFrostEffectTheRuleCovers(int slot, string name, bool area)
    {
        SpellSystem.GetSpellInfo(CharacterClass.Magician, slot).Name.Should().Be(name);
        var r = new SpellSystem.SpellResult { Success = true };
        typeof(SpellSystem).GetMethod("ExecuteMagicianSpell", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { Hero(CharacterClass.Magician), slot, null, null, r, new Random(1), 1.0f });
        r.SpecialEffect.Should().Be("frost", $"{name} slows through the frost case");
        r.IsMultiTarget.Should().Be(area, "Ice Storm takes the frost case on every surviving monster");
    }

    // ---------------------------------------------------------------- rule 4: ticks once a round

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ControlRules136_AMark_LosesOneRoundPerRound_HoweverManyActions(int moreActions)
    {
        var hero = Hero(CharacterClass.Ranger);
        var m = Foe(boss: true);
        m.Strength = 0;
        var rig = Rig(hero, 1368, m);
        m.IsMarked = true; m.MarkedDuration = 4; m.MarkedBonusPercent = 30;
        MonsterRound(rig, m, moreActions);
        m.MarkedDuration.Should().Be(3, "one round has passed");
        MonsterRound(rig, m, moreActions);
        m.MarkedDuration.Should().Be(2);
        m.IsMarked.Should().BeTrue();
    }

    // ---------------------------------------------------------------- rule 5: no instant kill on a mini boss

    public static IEnumerable<object[]> Killers() => new[]
    {
        new object[] { "assassinate", CharacterClass.Assassin, 0.10 },
        new object[] { "wrath_of_the_deep", CharacterClass.Tidesworn, 0.20 },
        new object[] { "annihilation", CharacterClass.Voidreaver, 0.40 },
    };

    [Theory]
    [MemberData(nameof(Killers))]
    public void ControlRules136_AnInstantKiller_OnAMiniBossBelowItsThreshold_DealsDamageAndDoesNotKill(string id, CharacterClass cls, double hpShare)
    {
        var ability = ClassAbilitySystem.GetAbility(id)!;
        for (int i = 0; i < 40; i++)
        {
            var hero = Hero(cls);
            var mini = Foe(mini: true);
            mini.HP = (long)(mini.MaxHP * hpShare);
            long start = mini.HP;
            var rig = Rig(hero, 1369 + i, mini);
            Ability(rig, hero, mini, id, ability.BaseDamage);
            mini.HP.Should().BeGreaterThan(0, $"{id} does not kill a mini boss outright");
            mini.HP.Should().BeLessThan(start, $"{id}'s damage lands on the mini boss");
        }
    }

    [Theory]
    [MemberData(nameof(Killers))]
    public void ControlRules136_AnInstantKiller_OnANormalMonsterBelowItsThreshold_StillKills(string id, CharacterClass cls, double hpShare)
    {
        var ability = ClassAbilitySystem.GetAbility(id)!;
        int kills = 0;
        for (int i = 0; i < 40; i++)
        {
            var hero = Hero(cls);
            var m = Foe();
            m.HP = (long)(m.MaxHP * hpShare);
            var rig = Rig(hero, 1369 + i, m);
            Ability(rig, hero, m, id, ability.BaseDamage);
            if (m.HP <= 0) kills++;
        }
        kills.Should().BeGreaterThan(0, $"{id} still kills a normal monster outright");
    }

    // ---------------------------------------------------------------- rule 6: escapes follow the flee rules

    private static T WithDifficulty<T>(DifficultyMode mode, Func<T> body)
    {
        var saved = DifficultySystem.CurrentDifficulty;
        try { DifficultySystem.CurrentDifficulty = mode; return body(); }
        finally { DifficultySystem.CurrentDifficulty = saved; }
    }

    /// <summary>Disappearing Act through the ability path, or the Sage's Escape through the spell path.</summary>
    private static void UseEscape(G.Rig rig, bool spell)
    {
        if (spell) Spell(rig, rig.Hero, rig.Monsters[0], "escape", 0);
        else Ability(rig, rig.Hero, rig.Monsters[0], "disappearing_act");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlRules136_WithFleeingOff_AnEscapeAbilityGivesNoEscape(bool spell)
    {
        WithDifficulty(DifficultyMode.Nightmare, () =>
        {
            var rig = Rig(Hero(spell ? CharacterClass.Sage : CharacterClass.Jester), 1370, Foe());
            UseEscape(rig, spell);
            Escaped(rig).Should().BeFalse("fleeing is off");
            var text = Shown(rig);
            text.Should().Contain(Plain(Loc.Get("combat.no_escape")), "the Run action's line");
            text.Should().NotContain(Plain(Loc.Get(spell ? "combat.spell_escape" : "combat.ability_escape", spell ? rig.Hero.DisplayName : Loc.Get("combat.actor_you"))));
            return 0;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlRules136_AgainstOtherMonsters_AnEscapeAbilityIsCertain(bool spell)
    {
        WithDifficulty(DifficultyMode.Normal, () =>
        {
            for (int i = 0; i < 50; i++)
            {
                var rig = Rig(Hero(spell ? CharacterClass.Sage : CharacterClass.Jester), 1371 + i, Foe(), Foe(mini: true));
                UseEscape(rig, spell);
                Escaped(rig).Should().BeTrue("a normal or mini boss fight is a certain escape");
            }
            return 0;
        });
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void ControlRules136_InABossOrOldGodFight_AnEscapeAbilityRollsTheRunActionsChance(bool spell, bool oldGod)
    {
        WithDifficulty(DifficultyMode.Normal, () =>
        {
            var hero = Hero(spell ? CharacterClass.Sage : CharacterClass.Jester);
            var rig = Rig(hero, 1372, Foe(boss: true));
            if (oldGod) rig.Engine.BossContext = G.Boss();
            int chance = (int)typeof(CombatEngine).GetMethod("CalculateFleeChance", F)!.Invoke(rig.Engine, new object[] { hero, true })!;
            chance.Should().Be(20, "the Run action's boss chance at full HP");
            int escaped = 0, trials = 2000;
            for (int i = 0; i < trials; i++)
            {
                ResetEscape(rig);
                UseEscape(rig, spell);
                if (Escaped(rig)) escaped++;
            }
            ((double)escaped / trials).Should().BeInRange(0.17, 0.23, "the Run action's boss chance");
            var text = Shown(rig);
            string fail = oldGod ? Loc.Get("combat.boss_blocks_escape", rig.Monsters[0].Name) : Loc.Get("combat.flee_fail");
            Count(text, Plain(fail)).Should().Be(trials - escaped, "a failed escape shows the Run action's fail line");
            return 0;
        });
    }
}
