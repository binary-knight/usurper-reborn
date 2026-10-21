using System;
using System.Collections.Generic;
using System.IO;
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
using P = UsurperReborn.Tests.PrestigeSingles136Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G11: Apotheosis of Ruin's swings hit every enemy with 10 percent life steal for 3 rounds (rule 6), and
/// Empathic Link links one ally, splits a monster's hit on it with the caster on every hit path and gives the ally
/// the +30 defence (rule 2).
/// </summary>
[Collection("SharedGameSingletons")]
public class PrestigeSingles136LinkTests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;
    private static readonly string LongArg = new string('W', 31);

    private static string Plain(string s) => Regex.Replace(s, "\u001b\\[[0-9;]*m", "");

    private static T NoHitRoll<T>(Func<T> body)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try { GameConfig.PlayerHitRollEnabled = false; return body(); }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    private static T WithTactics<T>(Func<T> body)
    {
        bool saved = GameConfig.MonsterTacticsEnabled;
        try { GameConfig.MonsterTacticsEnabled = true; return body(); }
        finally { GameConfig.MonsterTacticsEnabled = saved; }
    }

    /// <summary>G.NewRig with typed input for the prompts.</summary>
    private static G.Rig ScriptedRig(Character hero, string input, List<Character> team, params Monster[] monsters)
    {
        var rig = G.NewRig(hero, new Random(136), team, monsters);
        rig.Terminal = new TerminalEmulator(new G.ScriptedStream(input), rig.Output);
        rig.Engine = new CombatEngine(rig.Terminal);
        typeof(CombatEngine).GetField("random", F)!.SetValue(rig.Engine, rig.Random);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(rig.Engine, hero);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(rig.Engine, hero);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(rig.Engine, team);
        return rig;
    }

    private static void RoundEnd(G.Rig rig) =>
        typeof(CombatEngine).GetMethod("ProcessEndOfRoundAbilityEffects", F)!.Invoke(rig.Engine, new object[] { rig.Hero });

    // ---------------------------------------------------------------- rule 6: Apotheosis of Ruin

    private static Monster Big(string name) => new Monster
    {
        Name = name, TierName = "Wolf", Level = 10, HP = 1_000_000_000L, MaxHP = 1_000_000_000L, Strength = 1, Defence = 0,
        ArmPow = 0, Experience = 1, Gold = 0, IsActive = true, CanSpeak = false,
    };

    private static long Lost(Monster m) => m.MaxHP - m.HP;

    [Fact]
    public void PrestigeSingles136_Apotheosis_SwingsHitEveryEnemy_TenPercentLifesteal_ForThreeRounds()
    {
        NoHitRoll(() =>
        {
            var hero = P.Hero(CharacterClass.Voidreaver, level: 60, hp: 1_000_000);
            var foes = new[] { Big("Wolf"), Big("Bear"), Big("Boar") };
            var rig = G.NewRig(hero, new Random(136), null, foes);
            var r = P.Cast(rig, hero, "apotheosis_of_ruin", foes[0]);
            hero.IsRaging.Should().BeFalse("Raging's x1.5 is not in the text");
            hero.HasStatus(StatusEffect.Raging).Should().BeFalse();
            hero.SlotBonusValue(BonusSlot.Attack, CombatEngine.SlotSource(r)).Should().Be(r.AttackBonus, "the attack bonus is a G4 entry");
            hero.TempAttackBonusDuration.Should().Be(3);
            hero.HasStatus(StatusEffect.Lifesteal).Should().BeTrue();
            hero.StatusLifestealPercent.Should().Be(10);

            for (int round = 1; round <= 2; round++)
            {
                RoundEnd(rig);   // the cast's round ends; the next two rounds still hold it
                hero.ProcessStatusEffects();   // the next round starts: statuses tick, as in the round loop
                var before = foes.Select(Lost).ToArray();
                hero.HP = 1000;
                G.Act(rig, G.A(CombatActionType.Attack));
                var hit = foes.Select((f, i) => Lost(f) - before[i]).ToArray();
                hit.Should().OnlyContain(h => h > 0, $"round {round}: every enemy is hit");
                hit[1].Should().BeLessThan(hit[0] * 2, "the second takes a diminished swing");
                long expectedHeal = hit.Sum(h => Math.Max(1, h * 10 / 100));
                (hero.HP - 1000).Should().Be(expectedHeal, $"round {round}: 10 percent of each hit");
            }
            RoundEnd(rig);   // the third round ends
            hero.ProcessStatusEffects();
            var b2 = foes.Select(Lost).ToArray();
            hero.HP = 1000;
            G.Act(rig, G.A(CombatActionType.Attack));
            (Lost(foes[0]) - b2[0]).Should().BeGreaterThan(0);
            (Lost(foes[1]) - b2[1]).Should().Be(0, "after its 3 rounds the swing hits its target only");
            (Lost(foes[2]) - b2[2]).Should().Be(0);
            hero.HP.Should().Be(1000, "no life steal after its rounds");
            return 0;
        });
    }

    [Fact]
    public void PrestigeSingles136_Apotheosis_ATeammatesSwingsHitEveryEnemy()
    {
        NoHitRoll(() =>
        {
            var hero = P.Hero(CharacterClass.Warrior);
            var mate = P.Mate("Ruin", CharacterClass.Voidreaver, hp: 1_000_000);
            mate.Level = 60;
            var foes = new[] { Big("Wolf"), Big("Bear") };
            var rig = G.NewRig(hero, new Random(136), new List<Character> { mate }, foes);
            P.Cast(rig, mate, "apotheosis_of_ruin", foes[0]);
            mate.HP = mate.MaxHP;   // full: no heal or shield pick
            mate.CurrentCombatStamina = 0; mate.Mana = 0;   // no ability: the turn is a swing
            G.TeammateTurn(rig, mate);
            foes.Should().OnlyContain(f => Lost(f) > 0, "the teammate's swing hits every enemy");
            return 0;
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void PrestigeSingles136_Apotheosis_LineStatesSixtyAndTen(string lang)
    {
        string line = Loc.GetIn(lang, "combat.ability_apotheosis_detail");
        line.Should().Contain("+60").And.Contain("10%").And.NotContain("100").And.NotContain("20%");
    }

    // ---------------------------------------------------------------- rule 2: Empathic Link

    private static Character Wave(long hp = 100_000)
    {
        var c = P.Hero(CharacterClass.Wavecaller, level: 60, hp: hp);
        c.HP = hp;
        return c;
    }

    private static Dictionary<string, int> Cooldowns(G.Rig rig, Character actor) =>
        (Dictionary<string, int>)typeof(CombatEngine).GetMethod("CooldownsFor", F)!.Invoke(rig.Engine, new object[] { actor })!;

    [Fact]
    public void PrestigeSingles136_EmpathicLink_ThePlayerPicksAnAlly_TheAllyGetsThirty_TheCasterNothing()
    {
        var hero = Wave();
        var a = P.Mate("Ann"); a.HP = a.MaxHP;
        var b = P.Mate("Bo"); b.HP = b.MaxHP;
        var rig = ScriptedRig(hero, "2\n", new List<Character> { a, b }, G.Dummy());
        G.Act(rig, G.A(CombatActionType.ClassAbility, "empathic_link"));
        b.EmpathicLinkCaster.Should().Be(hero, "the picked ally is linked");
        b.EmpathicLinkRounds.Should().Be(4);
        a.EmpathicLinkCaster.Should().BeNull();
        b.TempDefenseBonus.Should().Be(30, "the ally's +30 defence");
        b.TempDefenseBonusDuration.Should().Be(4);
        hero.TempDefenseBonus.Should().Be(0, "the caster gets no defence");
        hero.HasStatus(StatusEffect.Reflecting).Should().BeFalse("no Reflecting the text does not state");
        a.TempDefenseBonus.Should().Be(0);
    }

    [Fact]
    public void PrestigeSingles136_EmpathicLink_EnterLinksTheMostHurtAlly_AnNpcCasterPicksTheMostHurt()
    {
        var hero = Wave();
        var a = P.Mate("Ann"); a.HP = a.MaxHP;
        var b = P.Mate("Bo"); b.HP = b.MaxHP / 3;
        var rig = ScriptedRig(hero, "\n", new List<Character> { a, b }, G.Dummy());
        G.Act(rig, G.A(CombatActionType.ClassAbility, "empathic_link"));
        b.EmpathicLinkCaster.Should().Be(hero, "Enter links the most hurt ally");

        var hero2 = P.Hero(CharacterClass.Warrior); hero2.HP = hero2.MaxHP / 4;
        var caster = P.Mate("Wave", CharacterClass.Wavecaller); caster.Level = 60; caster.HP = caster.MaxHP;
        var other = P.Mate("Ann"); other.HP = other.MaxHP;
        var rig2 = G.NewRig(hero2, new Random(1), new List<Character> { caster, other }, G.Dummy());
        P.Cast(rig2, caster, "empathic_link");
        hero2.EmpathicLinkCaster.Should().Be(caster, "an NPC caster links the most hurt living ally, the player here");
        hero2.TempDefenseBonus.Should().Be(30);
        caster.TempDefenseBonus.Should().Be(0);
    }

    [Fact]
    public void PrestigeSingles136_EmpathicLink_NoAlly_RefusedBeforeAnyCost()
    {
        var hero = Wave();
        var rig = G.NewRig(hero, new Random(136), null, G.Dummy());
        hero.CurrentCombatStamina = 500;
        ((Task)typeof(CombatEngine).GetMethod("ProcessPlayerActionMultiMonster", F)!
            .Invoke(rig.Engine, new object?[] { G.A(CombatActionType.ClassAbility, "empathic_link"), hero, rig.Monsters, rig.Result })!).GetAwaiter().GetResult();
        hero.CurrentCombatStamina.Should().Be(500, "no stamina is spent");
        Cooldowns(rig, hero).GetValueOrDefault("empathic_link").Should().Be(0, "no cooldown is set");
        Plain(rig.Shown()).Should().Contain(Loc.Get("combat.no_allies_to_aid"));
    }

    [Fact]
    public void PrestigeSingles136_EmpathicLink_Cancel_NoCost()
    {
        var hero = Wave();
        var a = P.Mate("Ann");
        var rig = ScriptedRig(hero, "Q\n", new List<Character> { a }, G.Dummy());
        hero.CurrentCombatStamina = 500;
        ((Task)typeof(CombatEngine).GetMethod("ProcessPlayerActionMultiMonster", F)!
            .Invoke(rig.Engine, new object?[] { G.A(CombatActionType.ClassAbility, "empathic_link"), hero, rig.Monsters, rig.Result })!).GetAwaiter().GetResult();
        hero.CurrentCombatStamina.Should().Be(500);
        Cooldowns(rig, hero).GetValueOrDefault("empathic_link").Should().Be(0);
        a.EmpathicLinkCaster.Should().BeNull();
    }

    // the split on each path

    private static Monster Brute() => G.Foe("Wolf", 1_000_000, 600, level: 50);

    private static Monster Ability(string ability) => new Monster
    {
        Name = "Drake", TierName = "Drake", FamilyName = "Beast", Level = 400, HP = 1_000_000, MaxHP = 1_000_000,
        Strength = 600, WeapPow = 50, IsActive = true, CanSpeak = false, SpecialAbilities = new List<string> { ability },
    };

    /// <summary>The ally takes the hit; the caster's loss is half of the whole, the ally's the rest.</summary>
    private static void ShouldSplit(Character ally, long ally0, Character caster, long caster0, string path)
    {
        long allyLost = ally0 - ally.HP, casterLost = caster0 - caster.HP;
        long whole = allyLost + casterLost;
        whole.Should().BeGreaterThan(1, path);
        casterLost.Should().Be(whole / 2, $"{path}: the caster takes half");
        allyLost.Should().Be(whole - whole / 2, $"{path}: the ally the rest");
    }

    private static (G.Rig rig, Character caster, Character mate) LinkedMate()
    {
        var hero = Wave(1_000_000);
        var mate = P.Mate("Ann", hp: 1_000_000); mate.HP = mate.MaxHP;
        var rig = G.NewRig(hero, new Random(136), new List<Character> { mate }, Brute());
        rig.Result.Monsters = rig.Monsters;
        mate.EmpathicLinkCaster = hero; mate.EmpathicLinkRounds = 4;
        return (rig, hero, mate);
    }

    private static void HitAlly(G.Rig rig, Monster m, Character ally) =>
        ((Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
            .Invoke(rig.Engine, new object?[] { m, ally, rig.Result, new List<Monster> { m } })!).GetAwaiter().GetResult();

    [Theory]
    [InlineData("")]               // the normal attack
    [InlineData("FireBreath")]     // the ability's direct damage
    [InlineData("CrushingBlow")]   // the ability's multiplier damage
    [InlineData("LifeDrain")]      // the multiplier damage with life drain
    public void PrestigeSingles136_EmpathicLink_SplitsAHitOnALinkedTeammate(string ability)
    {
        WithTactics(() =>
        {
            int splits = 0;
            for (int seed = 1; seed <= 12 && splits < 2; seed++)
            {
                var (rig, caster, mate) = LinkedMate();
                rig.Random = new Random(seed);
                typeof(CombatEngine).GetField("random", F)!.SetValue(rig.Engine, rig.Random);
                var m = ability == "" ? Brute() : Ability(ability);
                long a0 = mate.HP, c0 = caster.HP;
                HitAlly(rig, m, mate);
                if (a0 == mate.HP) continue;   // dodged or missed
                ShouldSplit(mate, a0, caster, c0, ability == "" ? "normal attack" : ability);
                Plain(rig.Shown()).Should().Contain("through the link");
                splits++;
            }
            splits.Should().BeGreaterThan(0, $"{ability}: at least one hit lands");
            return 0;
        });
    }

    private static (G.Rig rig, Character caster, Character hero) LinkedPlayer()
    {
        var hero = P.Hero(CharacterClass.Warrior, hp: 1_000_000); hero.HP = hero.MaxHP;
        var caster = P.Mate("Wave", CharacterClass.Wavecaller, hp: 1_000_000); caster.HP = caster.MaxHP;
        var rig = G.NewRig(hero, new Random(136), new List<Character> { caster }, Brute());
        rig.Result.Monsters = rig.Monsters;
        hero.EmpathicLinkCaster = caster; hero.EmpathicLinkRounds = 4;
        return (rig, caster, hero);
    }

    [Theory]
    [InlineData("")]
    [InlineData("FireBreath")]
    [InlineData("CrushingBlow")]
    [InlineData("LifeDrain")]
    public void PrestigeSingles136_EmpathicLink_SplitsAHitOnALinkedPlayer(string ability)
    {
        WithTactics(() =>
        {
            int splits = 0;
            for (int seed = 1; seed <= 12 && splits < 2; seed++)
            {
                var (rig, caster, hero) = LinkedPlayer();
                rig.Random = new Random(seed);
                typeof(CombatEngine).GetField("random", F)!.SetValue(rig.Engine, rig.Random);
                long h0 = hero.HP, c0 = caster.HP;
                if (ability == "")
                {
                    var brute = Brute();
                    ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
                        .Invoke(rig.Engine, new object?[] { brute, hero, rig.Result, new List<Monster> { brute } })!).GetAwaiter().GetResult();
                }
                else
                {
                    var drake = Ability(ability);
                    ((Task<bool>)typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", F)!
                        .Invoke(rig.Engine, new object?[] { drake, hero, rig.Result, new List<Monster> { drake } })!).GetAwaiter().GetResult();
                }
                if (h0 == hero.HP) continue;
                ShouldSplit(hero, h0, caster, c0, ability == "" ? "leader normal attack" : "leader " + ability);
                splits++;
            }
            splits.Should().BeGreaterThan(0, $"{ability}: at least one hit lands");
            return 0;
        });
    }

    [Theory]
    [InlineData("ApplyBossAbilityDamageToPlayer")]
    [InlineData("ApplyManweAbilityDamageToPlayer")]
    public void PrestigeSingles136_EmpathicLink_SplitsTheBossAbilityHits(string method)
    {
        var (rig, caster, hero) = LinkedPlayer();
        var boss = Brute(); boss.IsBoss = true;
        long h0 = hero.HP, c0 = caster.HP;
        typeof(CombatEngine).GetMethod(method, F)!.Invoke(rig.Engine, new object?[] { boss, hero, 5000L, "Smite", rig.Result });
        ShouldSplit(hero, h0, caster, c0, method);
    }

    [Fact]
    public void PrestigeSingles136_EmpathicLink_NoSplitAfterADeath_OrAfterItsRounds()
    {
        WithTactics(() =>
        {
            var (rig, caster, mate) = LinkedMate();
            caster.HP = 0;   // the caster is dead
            long c0 = caster.HP;
            int landed = 0;
            for (int i = 0; i < 6; i++)
            {
                long a0 = mate.HP;
                HitAlly(rig, Brute(), mate);
                if (mate.HP < a0) landed++;
            }
            landed.Should().BeGreaterThan(0);
            caster.HP.Should().Be(c0, "a dead caster takes no share");

            var (rig2, caster2, mate2) = LinkedMate();
            for (int i = 0; i < 4; i++) caster2.TickPrestigeRounds();   // not the caster's own: the ally holds the rounds
            for (int i = 0; i < 4; i++) mate2.TickPrestigeRounds();
            mate2.EmpathicLinkCaster.Should().BeNull("the link ends after 4 rounds");
            long c2 = caster2.HP;
            for (int i = 0; i < 6; i++) HitAlly(rig2, Brute(), mate2);
            caster2.HP.Should().Be(c2, "no split after the link's rounds");
            return 0;
        });
    }

    [Fact]
    public void PrestigeSingles136_EmpathicLink_RoundEnd_EndsTheLinkAndTheDefence()
    {
        var hero = Wave();
        var a = P.Mate("Ann");
        var rig = ScriptedRig(hero, "1\n", new List<Character> { a }, G.Dummy());
        G.Act(rig, G.A(CombatActionType.ClassAbility, "empathic_link"));
        for (int i = 0; i < 3; i++) RoundEnd(rig);
        a.EmpathicLinkCaster.Should().Be(hero, "3 round ends: still linked");
        a.TempDefenseBonus.Should().Be(30);
        RoundEnd(rig);
        a.EmpathicLinkCaster.Should().BeNull("4 round ends: the link is over");
        a.TempDefenseBonus.Should().Be(0);
    }

    // ---------------------------------------------------------------- widths

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void PrestigeSingles136_NewRows_FitSeventyNine(string lang)
    {
        var rows = new List<string>();
        rows.AddRange(UIHelper.MessageRows(Loc.GetIn(lang, "combat.empathic_link_formed", LongArg, LongArg, 4, 30)));
        rows.AddRange(UIHelper.MessageRows(Loc.GetIn(lang, "combat.empathic_link_share", LongArg, 9_999_999, LongArg)));
        rows.AddRange(UIHelper.MessageRows(Loc.GetIn(lang, "combat.apotheosis_sweep")));
        rows.AddRange(UIHelper.MessageRows(Loc.GetIn(lang, "combat.apotheosis_sweep_npc", LongArg)));
        rows.Add(Loc.GetIn(lang, "combat.ability_apotheosis_detail"));   // written as one line
        rows.Should().OnlyContain(r => r.Length <= MaxWidth, lang);
    }
}
