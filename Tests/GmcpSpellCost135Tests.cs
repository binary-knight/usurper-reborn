using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: the GMCP Char.Skills.List spell entries send the mana a cast charges
/// (SpellSystem.CalculateManaCost) and the level the spell opens at
/// (SpellSystem.GetLevelRequired), not the SpellInfo table values.
/// </summary>
[Collection("SharedGameSingletons")]
public class GmcpSpellCost135Tests
{
    private static Character Hero(CharacterClass cls, int level, long wisdom) => new Character
    {
        Name1 = "hero", Name2 = "Hero", AI = CharacterAI.Human,
        Class = cls, Race = CharacterRace.Human,
        Level = level, HP = 100, MaxHP = 100, Wisdom = wisdom,
    };

    private static JsonElement Capture(Character hero)
    {
        var saved = SessionContext.Current;
        var stream = new MemoryStream();
        SessionContext.Current = new SessionContext { OutputStream = stream, GmcpEnabled = true };
        try
        {
            GmcpBridge.EmitSkillsList(hero);
            string text = Encoding.UTF8.GetString(stream.ToArray());
            int a = text.IndexOf('{');
            int b = text.LastIndexOf('}');
            a.Should().BeGreaterThan(-1, "a Char.Skills.List frame was written");
            using var doc = JsonDocument.Parse(text.Substring(a, b - a + 1));
            return doc.RootElement.Clone();
        }
        finally { SessionContext.Current = saved; }
    }

    private static JsonElement Entry(JsonElement frame, string name) =>
        frame.GetProperty("spells").EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);

    [Fact]
    public void T1_ClericSpells_SendWhatACastChargesAndWhenTheSpellOpens()
    {
        var hero = Hero(CharacterClass.Cleric, 40, 10);
        var spells = SpellSystem.GetAvailableSpells(hero);
        spells.Count.Should().BeGreaterThan(3);
        var frame = Capture(hero);
        frame.GetProperty("spells").GetArrayLength().Should().Be(spells.Count);
        foreach (var spell in spells)
        {
            var e = Entry(frame, spell.DisplayName);
            e.GetProperty("manaCost").GetInt32().Should().Be(SpellSystem.CalculateManaCost(spell, hero),
                $"{spell.Name} sends the mana a cast charges");
            e.GetProperty("requiresLevel").GetInt32().Should().Be(SpellSystem.GetLevelRequired(CharacterClass.Cleric, spell.Level),
                $"{spell.Name} sends the level it opens at");
        }
        var cure = spells.Single(s => s.Name == "Cure Light");
        Entry(frame, cure.DisplayName).GetProperty("manaCost").GetInt32().Should().Be(15, "the listed 5 is not what a cast charges");
    }

    [Fact]
    public void T2_HighWisdom_LowersTheSentManaCost()
    {
        var low = Hero(CharacterClass.Cleric, 40, 10);
        var high = Hero(CharacterClass.Cleric, 40, 30);
        var cure = SpellSystem.GetAvailableSpells(low).Single(s => s.Name == "Cure Light");
        int lowCost = Entry(Capture(low), cure.DisplayName).GetProperty("manaCost").GetInt32();
        int highCost = Entry(Capture(high), cure.DisplayName).GetProperty("manaCost").GetInt32();
        lowCost.Should().Be(SpellSystem.CalculateManaCost(cure, low));
        highCost.Should().Be(SpellSystem.CalculateManaCost(cure, high));
        highCost.Should().BeLessThan(lowCost, "Wisdom cuts what a cast charges and the message follows it");
    }

    [Fact]
    public void T3_CyclebreakerCycleRewind_SendsTheCastCostNotTheListedHundred()
    {
        var hero = Hero(CharacterClass.Cyclebreaker, 60, 10);
        var rewind = SpellSystem.GetAvailableSpells(hero).Single(s => s.Name == "Cycle Rewind");
        var e = Entry(Capture(hero), rewind.DisplayName);
        int expected = SpellSystem.CalculateManaCost(rewind, hero);
        expected.Should().NotBe(100);
        e.GetProperty("manaCost").GetInt32().Should().Be(expected, "the listed 100 is not what a cast charges");
    }

    [Fact]
    public void T4_WarriorAbilities_AreUnchanged()
    {
        var hero = Hero(CharacterClass.Warrior, 30, 10);
        var first = ClassAbilitySystem.GetAvailableAbilities(hero).First();
        var frame = Capture(hero);
        var e = frame.GetProperty("abilities").EnumerateArray().First();
        e.GetProperty("manaCost").GetInt32().Should().Be(first.ManaCost);
        e.GetProperty("requiresLevel").GetInt32().Should().Be(first.LevelRequired);
    }
}
