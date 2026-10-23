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
/// 1.3.7 AB: the Abysswarden's Corruption Harvest heals at the kill of each poisoned foe inside the fight, once a foe, and
/// the victory block does not repeat it; the level loss and NPC tables use the +3 Constitution the level up gives, as does
/// the admin level edit; Soul Eater's text says every kill.
/// </summary>
[Collection("SharedGameSingletons")]
public class AB137Abysswarden
{
    private static readonly Regex Ansi = new("\u001b\\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);
    private const string HarvestLine = "Corruption Harvest: Absorbed";

    private static Character Warden(CharacterClass cls = CharacterClass.Abysswarden)
    {
        var c = G.Hero(2000, 300, 10, cls);
        c.HP = 1000;
        return c;
    }

    private static Monster Rat(bool poisoned)
    {
        var m = G.Foe("Rat", 1, 1);
        if (poisoned) { m.Poisoned = true; m.PoisonRounds = 50; }
        return m;
    }

    private static long Heal(Character c) => Math.Max(1, (long)(c.MaxHP * GameConfig.AbysswardenCorruptionHealPercent));

    private static async Task<string> Go(Character hero, List<Monster> foes, string script)
    {
        var run = await G.Fight(hero, foes, null, 1, script);
        run.Error.Should().BeNull();
        run.Result!.Outcome.Should().Be(CombatOutcome.Victory);
        return Ansi.Replace(run.Transcript, "");
    }

    private static int Count(string text, string needle) => Regex.Matches(text, Regex.Escape(needle)).Count;
    private static string Rats(int n, string target = "1") => string.Concat(Enumerable.Repeat("A\n" + target + "\n", n)) + string.Concat(Enumerable.Repeat("P\n", 10));

    [Fact]
    public async Task AB137_CorruptionHarvest_HealsAtTheFirstPoisonedKill_WhileFoesRemain()
    {
        var tank = G.Foe("Tank", 400, 1);
        var text = await Go(Warden(), new List<Monster> { Rat(true), tank }, Rats(40));
        int feed = text.IndexOf(HarvestLine, StringComparison.Ordinal);
        int round2 = text.IndexOf("Round 2", StringComparison.Ordinal);
        feed.Should().BeGreaterThan(0);
        round2.Should().BeGreaterThan(0);
        feed.Should().BeLessThan(round2, "the first poisoned kill heals in round 1, while the tank still stands");
    }

    [Fact]
    public async Task AB137_EachPoisonedFoeHealsOnce_AndNothingMoreAtVictory()
    {
        var hero = Warden();
        var text = await Go(hero, new List<Monster> { Rat(true), Rat(true), Rat(true) }, Rats(40, ""));
        Count(text, HarvestLine).Should().Be(3, "one heal per fallen poisoned foe, none repeated by the victory block");
        var amounts = Regex.Matches(text, @"Absorbed (\d+) HP").Select(m => long.Parse(m.Groups[1].Value)).ToList();
        amounts.Sum().Should().Be(3 * Heal(hero));
    }

    [Fact]
    public async Task AB137_AnUnpoisonedFoe_HealsNothing()
    {
        var text = await Go(Warden(), new List<Monster> { Rat(false), Rat(true) }, Rats(40, ""));
        Count(text, HarvestLine).Should().Be(1, "only the poisoned foe pays");
    }

    [Fact]
    public async Task AB137_AClassOtherThanAbysswarden_GetsNothing()
    {
        var text = await Go(Warden(CharacterClass.Warrior), new List<Monster> { Rat(true), Rat(true) }, Rats(40));
        Count(text, HarvestLine).Should().Be(0);
    }

    [Fact]
    public void AB137_LevelLossAndRegain_KeepsConstitutionEven()
    {
        var hero = Warden();
        long con = hero.BaseConstitution;
        LevelMasterLocation.ApplyClassStatIncreases(hero);
        hero.BaseConstitution.Should().Be(con + 3);
        LevelMasterLocation.ReverseClassStatIncrease(hero);
        hero.BaseConstitution.Should().Be(con, "a level lost takes back the 3 a level gave");
    }

    [Fact]
    public void AB137_TheNpcTable_ExpectsPlusThreeConstitution()
    {
        var npc = Warden();
        npc.Level = 21;
        npc.BaseConstitution = 1;
        LevelMasterLocation.EnsureClassStatsForLevel(npc);
        npc.BaseConstitution.Should().Be(10 + 20 * 3);
    }

    [Fact]
    public void AB137_TheAdminLevelEdit_AddsThreeConstitutionAWardenLevel()
    {
        var data = new PlayerData { Class = CharacterClass.Abysswarden, BaseConstitution = 10 };
        var m = typeof(OnlineAdminConsole).GetMethod("ApplyClassStatIncreasesToSaveData", BindingFlags.NonPublic | BindingFlags.Static);
        m.Should().NotBeNull();
        m!.Invoke(null, new object[] { data, 4 });
        data.BaseConstitution.Should().Be(10 + 4 * 3);
    }

    [Fact]
    public void AB137_SoulEaterText_SaysEveryKill_InFiveLanguages()
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var text = Loc.GetIn(lang, "base.buff_soul_eater", 5);
            text.Should().NotBe("base.buff_soul_eater");
            text.Should().NotContain("killing blow");
            text.Length.Should().BeLessThan(80, lang);
        }
        Loc.GetIn("en", "base.buff_soul_eater", 5).Should().Contain("every kill");
        Loc.GetIn("hu", "base.buff_soul_eater", 5).Should().Contain("minden ölésnél");
    }
}
