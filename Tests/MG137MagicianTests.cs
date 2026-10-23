using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 MG: Power Hat (Magician spell 8) heals at least 20 percent of the caster's max HP, plus 1.2 percent a level
/// above L75 (50 percent at L100). The rule lives in one case of SpellSystem.CastSpell, so a player, a teammate and a
/// companion all get it. Cure Critical (Cleric spell 8) shares the old "50-75 hp" text and keeps it.
/// </summary>
[Collection("SharedGameSingletons")]
public class MG137Magician
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static int StaffId() => EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff).Id;

    private static Character Magician(int level, long maxHp) => new Character
    {
        Name1 = "Magician", Name2 = "Magician", Class = CharacterClass.Magician, Level = level,
        Wisdom = 50, Intelligence = 50, HP = maxHp, MaxHP = maxHp, Mana = 100_000, MaxMana = 100_000,
        CombatSpeed = CombatSpeed.Instant,
        EquippedItems = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.MainHand] = StaffId() },
    };

    private static int PowerHatHeal(int level, long maxHp)
    {
        var m = Magician(level, maxHp);
        var r = SpellSystem.CastSpell(m, 8, null);
        r.Success.Should().BeTrue();
        return r.Healing;
    }

    [Theory]
    [InlineData(30, 0.20)]
    [InlineData(75, 0.20)]
    [InlineData(80, 0.26)]
    [InlineData(90, 0.38)]
    [InlineData(100, 0.50)]
    public void PowerHatHealsTheLevelFraction(int level, double fraction)
    {
        const long maxHp = 100_000;
        for (int i = 0; i < 20; i++)
            PowerHatHeal(level, maxHp).Should().BeGreaterThanOrEqualTo((int)(maxHp * fraction) - 1);
    }

    [Fact]
    public void PowerHatKeepsItsWard()
    {
        var m = Magician(60, 10_000);
        var r = SpellSystem.CastSpell(m, 8, null);
        r.ProtectionBonus.Should().BeGreaterThan(0);
        r.Duration.Should().Be(999);
    }

    [Fact]
    public void ATeammateMagiciansPowerHatHealsTheSameRule()
    {
        var leader = new Character { Name1 = "Leader", Name2 = "Leader", Class = CharacterClass.Ranger, Level = 40,
            HP = 1000, MaxHP = 50_000, CombatSpeed = CombatSpeed.Instant };
        var mage = Magician(100, 10_000);
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, leader);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { mage });
        var result = new CombatResult { Player = leader };
        var cast = ((Task<bool>)typeof(CombatEngine).GetMethod("TeammateHealWithSpell", F)!
            .Invoke(engine, new object[] { mage, leader, result })!).GetAwaiter().GetResult();
        cast.Should().BeTrue();
        // the mage's own 50 percent of 10000 is 5000, far above the old 50-75 base
        leader.HP.Should().BeGreaterThanOrEqualTo(1000 + 5000);
    }

    private static Dictionary<string, string> Load(string lang)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Localization"))) dir = dir.Parent;
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            File.ReadAllText(Path.Combine(dir!.FullName, "Localization", lang + ".json")))!;
        return doc.Where(kv => kv.Value.ValueKind == JsonValueKind.String).ToDictionary(kv => kv.Key, kv => kv.Value.GetString() ?? "");
    }

    [Fact]
    public void PowerHatTextStatesTheNewRuleInFiveLanguages()
    {
        foreach (var lang in Langs)
        {
            var d = Load(lang)["spell.magician.8.desc"];
            d.Should().NotContain("50-75 hp +", lang);
            d.Should().Contain("50-75").And.Contain("20").And.Contain("75").And.Contain("100", lang);
            (d.Contains("1,2") || d.Contains("1.2")).Should().BeTrue(lang);
            // a wrapped paragraph of two 79 column rows at most (en and hu tested)
            if (lang == "en" || lang == "hu") d.Length.Should().BeLessThanOrEqualTo(158, lang);
        }
        SpellSystem.GetSpellInfo(CharacterClass.Magician, 8)!.Description.Should().Contain("at least 20% of max HP");
    }

    [Fact]
    public void CureCriticalTextIsUnchanged()
    {
        var cc = SpellSystem.GetSpellInfo(CharacterClass.Cleric, 8)!;
        cc.Description.Should().Be("Powerful divine healing with chivalry blessing. Effect: 50-75 hp, scales with WIS. Removes up to 10 Darkness; with no Darkness, +10 Chivalry.");
        Load("en")["spell.cleric.8.desc"].Should().Be(cc.Description);
        foreach (var lang in Langs) Load(lang)["spell.cleric.8.desc"].Should().Contain("50-75");
    }
}
