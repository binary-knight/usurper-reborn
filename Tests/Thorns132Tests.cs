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

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: the Thorns monster ability (MonsterAbilities, AbilityType.Thorns) reflects melee damage. It persists
/// on the monster as Monster.ThornsRounds (3) and ThornsPercent (25), as the evasion buff does. A live melee hit
/// on the monster while ThornsRounds is above 0 costs the attacker 25 percent of the damage dealt, at least 1,
/// and never below 1 HP; spells and ranged hits are exempt; the rounds count down at the monster's turn.
/// The line is combat.monster_thorns_wound. All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public class Thorns132Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Key = "combat.monster_thorns_wound";

    // ---------------------------------------------------------------- harness

    private static T InEnglish<T>(Func<T> body)
    {
        string lang = GameConfig.Language;
        bool saved = GameConfig.PlayerHitRollEnabled;
        try { GameConfig.Language = "en"; GameConfig.PlayerHitRollEnabled = false; return body(); }
        finally { GameConfig.Language = lang; GameConfig.PlayerHitRollEnabled = saved; }
    }

    private static void SetInt(object o, string name, int value)
    {
        var p = o.GetType().GetProperty(name);
        p.Should().NotBeNull($"Monster.{name} must exist");
        p!.SetValue(o, value);
    }

    private static int GetInt(object o, string name)
    {
        var p = o.GetType().GetProperty(name);
        p.Should().NotBeNull($"Monster.{name} must exist");
        return (int)p!.GetValue(o)!;
    }

    private static Monster Thorned(int rounds = 3, int percent = 25, int armour = 0)
    {
        var m = G.Dummy(armour: armour);
        SetInt(m, "ThornsRounds", rounds);
        SetInt(m, "ThornsPercent", percent);
        return m;
    }

    /// <summary>The damage of each landed hit, as the engine logs it ("X took N damage from S").</summary>
    private static List<long> Hits(CombatResult result) => result.CombatLog
        .Select(l => Regex.Match(l, @" took (\d+) damage from "))
        .Where(m => m.Success)
        .Select(m => long.Parse(m.Groups[1].Value))
        .ToList();

    private static long Expected(IEnumerable<long> hits) => hits.Sum(d => Math.Max(1, d * 25 / 100));

    private static string Plain(string transcript) => Regex.Replace(transcript, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

    private static void MonsterTurn(G.Rig rig, Monster m)
    {
        m.StatusTickedThisRound = false;
        ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(rig.Engine, new object?[] { m, rig.Hero, rig.Result, rig.Monsters })!).GetAwaiter().GetResult();
    }

    private static void Direct(G.Rig rig, Monster m, long damage, string source, bool spell)
    {
        ((Task<bool>)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
            .Invoke(rig.Engine, new object?[] { m, damage, rig.Result, source, rig.Hero, spell })!).GetAwaiter().GetResult();
    }

    // ---------------------------------------------------------------- the melee swing pays

    [Fact]
    public void T1_AMeleeSwingOnAThornedMonster_CostsTheAttacker25PercentOfTheDamageDealt()
    {
        InEnglish(() =>
        {
            var hero = G.Hero();
            var foe = Thorned();
            var rig = G.NewRig(hero, new Random(132), null, foe);
            long before = hero.HP;
            G.Act(rig, G.A(CombatActionType.Attack));
            var hits = Hits(rig.Result);
            hits.Should().NotBeEmpty("the swing lands (no to-hit roll)");
            hits.Should().OnlyContain(d => d >= 4, "the swing deals real damage");
            (before - hero.HP).Should().Be(Expected(hits), "each hit costs 25 percent of the damage it dealt");
            Plain(rig.Shown()).Should().Contain("Wolf's thorny hide wounds the attacker for ");
            return 0;
        });
    }

    [Fact]
    public void T1_AOneDamageSwing_CostsOne()
    {
        InEnglish(() =>
        {
            var hero = G.Hero();
            var foe = Thorned(armour: 1_000_000);
            var rig = G.NewRig(hero, new Random(132), null, foe);
            long before = hero.HP;
            G.Act(rig, G.A(CombatActionType.Attack));
            var hits = Hits(rig.Result);
            hits.Should().NotBeEmpty();
            hits.Should().OnlyContain(d => d == 1, "the armour leaves one point a hit");
            (before - hero.HP).Should().Be(hits.Count, "a one damage hit costs one");
            return 0;
        });
    }

    [Fact]
    public void T1_AnAttackerAtOneHp_StaysAtOne()
    {
        InEnglish(() =>
        {
            var hero = G.Hero();
            hero.HP = 1;
            var foe = Thorned();
            var rig = G.NewRig(hero, new Random(132), null, foe);
            G.Act(rig, G.A(CombatActionType.Attack));
            Hits(rig.Result).Should().NotBeEmpty();
            hero.HP.Should().Be(1, "thorns never drop the attacker below 1 HP");

            var low = G.Hero();
            low.HP = 3;
            var rig2 = G.NewRig(low, new Random(132), null, Thorned());
            G.Act(rig2, G.A(CombatActionType.Attack));
            low.HP.Should().Be(1, "a big reflect stops at 1 HP");
            return 0;
        });
    }

    [Fact]
    public void T1_ATeammatesMeleeSwing_PaysTheSame()
    {
        InEnglish(() =>
        {
            var hero = G.Hero();
            var mate = G.Mate("Ally");
            var foe = Thorned();
            var rig = G.NewRig(hero, new Random(132), new List<Character> { mate }, foe);
            long before = mate.HP;
            G.TeammateTurn(rig, mate);
            var hits = Hits(rig.Result);
            hits.Should().NotBeEmpty("the teammate swings");
            (before - mate.HP).Should().Be(Expected(hits), "a teammate's swing pays 25 percent of its damage");
            hero.HP.Should().Be(hero.MaxHP, "the reflect hits the one who swung");
            return 0;
        });
    }

    [Fact]
    public void T1_ACompanionsMeleeSwing_PaysTheSame()
    {
        InEnglish(() =>
        {
            var hero = G.Hero();
            var companion = G.Mate("Helper");
            companion.IsCompanion = true;
            var foe = Thorned();
            var rig = G.NewRig(hero, new Random(132), new List<Character> { companion }, foe);
            long before = companion.HP;
            G.TeammateTurn(rig, companion);
            var hits = Hits(rig.Result);
            hits.Should().NotBeEmpty("the companion swings");
            (before - companion.HP).Should().Be(Expected(hits), "a companion's swing pays 25 percent of its damage");
            return 0;
        });
    }

    // ---------------------------------------------------------------- spells and ranged hits are exempt

    [Fact]
    public void T2_ASpellOnAThornedMonster_CostsNothing()
    {
        InEnglish(() =>
        {
            var hero = G.Hero();
            var foe = Thorned();
            var rig = G.NewRig(hero, new Random(132), null, foe);
            long monsterBefore = foe.HP;
            Direct(rig, foe, 400, "Magic Missile", spell: true);
            (monsterBefore - foe.HP).Should().BeGreaterThan(0, "the spell hits");
            hero.HP.Should().Be(hero.MaxHP, "a spell is not reflected");

            ((Task)typeof(CombatEngine).GetMethod("ApplyAoEDamage", F)!
                .Invoke(rig.Engine, new object?[] { rig.Monsters, 400L, rig.Result, "Fireball", true, null, hero })!).GetAwaiter().GetResult();
            hero.HP.Should().Be(hero.MaxHP, "an area spell is not reflected");

            // the same hit as a melee one is reflected (the exemption is the spell, not the call)
            Direct(rig, foe, 400, "your attack", spell: false);
            hero.HP.Should().BeLessThan(hero.MaxHP);
            Plain(rig.Shown()).Should().NotContain("Magic Missile's thorny");
            return 0;
        });
    }

    [Fact]
    public void T2_ABowSwingOnAThornedMonster_CostsNothing()
    {
        InEnglish(() =>
        {
            var hero = G.Hero(main: 40, mainType: WeaponType.Bow);
            var foe = Thorned();
            var rig = G.NewRig(hero, new Random(132), null, foe);
            G.Act(rig, G.A(CombatActionType.Attack));
            Hits(rig.Result).Should().NotBeEmpty();
            hero.HP.Should().Be(hero.MaxHP, "a bow shot is a ranged hit");
            return 0;
        });
    }

    // ---------------------------------------------------------------- the rounds

    [Fact]
    public void T3_TheAbility_PutsThornsOnTheMonster_ForThreeRounds()
    {
        InEnglish(() =>
        {
            var foe = G.Dummy();
            MonsterAbilities.ExecuteAbility(MonsterAbilities.AbilityType.Thorns, foe, G.Hero(), new Random(132));
            GetInt(foe, "ThornsRounds").Should().Be(3);
            GetInt(foe, "ThornsPercent").Should().Be(25);
            return 0;
        });
    }

    [Fact]
    public void T3_ThornsEndAfterThreeRounds()
    {
        InEnglish(() =>
        {
            var hero = G.Hero();
            var foe = Thorned();
            var rig = G.NewRig(hero, new Random(132), null, foe);
            for (int round = 1; round <= 3; round++)
            {
                long before = hero.HP;
                G.Act(rig, G.A(CombatActionType.Attack));
                (before - hero.HP).Should().BeGreaterThan(0, $"thorns are up in round {round}");
                hero.HP = hero.MaxHP;
                MonsterTurn(rig, foe);
                hero.HP = hero.MaxHP;
            }
            GetInt(foe, "ThornsRounds").Should().Be(0, "three monster turns end the thorns");
            G.Act(rig, G.A(CombatActionType.Attack));
            hero.HP.Should().Be(hero.MaxHP, "after its rounds the swing costs nothing");
            return 0;
        });
    }

    // ---------------------------------------------------------------- pin: without Thorns nothing changes

    [Fact]
    public void T4_WithoutThorns_TheSwingIsUnchanged()
    {
        InEnglish(() =>
        {
            var plain = G.Repeat(G.Hero(), G.A(CombatActionType.Attack), times: 10, seed: 132);
            var thorned = G.Repeat(G.Hero(), G.A(CombatActionType.Attack), times: 10, seed: 132, foe: Thorned(rounds: 1000));
            plain.Error.Should().BeNull();
            thorned.Error.Should().BeNull();
            thorned.Draws.Should().Equal(plain.Draws, "the reflect draws nothing from the random");
            thorned.Damage.Should().Be(plain.Damage, "thorns do not change the damage dealt");

            var hero = G.Hero();
            var rig = G.NewRig(hero, new Random(132), null, G.Dummy());
            for (int i = 0; i < 10; i++) G.Act(rig, G.A(CombatActionType.Attack));
            hero.HP.Should().Be(hero.MaxHP, "a monster without thorns costs the swing nothing");
            Plain(rig.Shown()).Should().NotContain("thorny hide wounds the attacker");
            return 0;
        });
    }

    // ---------------------------------------------------------------- the line

    private static readonly string[] Langs = { "en", "es", "fr", "it", "hu" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    [Fact]
    public void T5_TheKey_IsInAllFiveLanguages()
    {
        foreach (var lang in Langs)
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json"));
            Regex.IsMatch(text, "\"" + Regex.Escape(Key) + "\"\\s*:\\s*\"[^\"]*\\{0\\}[^\"]*\\{1\\}[^\"]*\"|\"" + Regex.Escape(Key) + "\"\\s*:\\s*\"[^\"]*\\{1\\}[^\"]*\\{0\\}[^\"]*\"")
                .Should().BeTrue($"{lang} must carry {Key} with both its arguments");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void T5_TheLine_Fits79Columns(string lang)
    {
        Loc.HasIn(lang, Key).Should().BeTrue();
        // the one carrier, with a seven digit reflect: one row as it is
        string korr = Loc.GetIn(lang, Key, "Korr Stonewarden", 9_999_999);
        korr.Length.Should().BeLessOrEqualTo(79);
        // the longest name a fight can show (GameConfig.MaxNameLength): the rows as written fit
        string longest = Loc.GetIn(lang, Key, new string('W', GameConfig.MaxNameLength), 9_999_999);
        UIHelper.MessageRows(longest).Should().OnlyContain(r => r.Length <= 79);
    }
}
