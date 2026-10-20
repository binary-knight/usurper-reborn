using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;
using Rig = UsurperReborn.Tests.PoisonDot136Rig;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 (E2): a weapon poison proc poisons the monster for 4 rounds. The tick is max(5 percent of the hit, the
/// weapon's PoisonDamage after the Armory / 4), at least 1; a new proc restarts the 4 rounds and keeps the larger
/// tick; the proc deals nothing itself. The tick lands once a round in the monster's status step through physical
/// phase immunity and divine armour, as a swing, and a tick to 0 goes through the poison death path.
/// </summary>
[Collection("SharedGameSingletons")]
public class PoisonDot136Tests
{
    private sealed class Fight
    {
        public Character Hero = null!;
        public Character Mate = null!;
        public Monster Foe = null!;
        public CombatEngine Engine = null!;
        public CombatResult Result = null!;
        public MemoryStream Output = null!;

        public void Proc(long hit) => Rig.Proc(Engine, Hero, Foe, hit, Result);
        public void MateProc(long hit) => Rig.Proc(Engine, Mate, Foe, hit, Result);
        public void Step() => Rig.StatusStep(Engine, Foe, Hero, Result);

        public string Shown()
        {
            var term = (TerminalEmulator)typeof(CombatEngine).GetField("terminal", Rig.F)!.GetValue(Engine)!;
            term.StreamWriterInternal?.Flush();
            return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
        }
    }

    private static Fight Setup(int heroPoison = 0, int matePoison = 0, string mateName = "Mate")
    {
        var f = new Fight { Hero = Rig.Hero("Hero", heroPoison), Mate = Rig.Hero(mateName, matePoison), Foe = Rig.Foe(), Output = new MemoryStream() };
        var team = new List<Character> { f.Mate };
        f.Engine = Rig.Engine(f.Hero, team, new Rig.ProcRandom(), f.Output);
        f.Result = new CombatResult { Player = f.Hero, Teammates = team };
        return f;
    }

    // ---------- the tick ----------

    [Theory]
    [InlineData(0, 1000, 50)]     // 5 percent of the hit
    [InlineData(400, 1000, 100)]  // the fixed value / 4 is larger than 5 percent of the hit
    [InlineData(0, 10, 1)]        // the floor of 1
    public void PoisonDot136_ProcSetsFourRoundsOfTheLargerShare(int poisonDamage, long hit, long tick)
    {
        var f = Setup(heroPoison: poisonDamage);
        f.Proc(hit);
        f.Foe.EnchantPoisonRounds.Should().Be(4);
        f.Foe.EnchantPoisonTick.Should().Be(tick, "max(5 percent of {0}, {1} / 4), at least 1", hit, poisonDamage);
        f.Foe.PoisonRounds.Should().Be(0, "never the max-HP Poisoned status");
        f.Foe.Poisoned.Should().BeFalse();
    }

    [Fact]
    public void PoisonDot136_TheProcDealsNoInstantDamage()
    {
        var f = Setup(heroPoison: 400);
        long before = f.Foe.HP;
        long dealtBefore = f.Hero.Statistics.TotalDamageDealt;
        f.Proc(1000);
        f.Foe.EnchantPoisonRounds.Should().Be(4, "the proc fired");
        f.Foe.HP.Should().Be(before, "the proc takes nothing now; the status step takes each tick");
        f.Result.TotalDamageDealt.Should().Be(0);
        f.Hero.Statistics.TotalDamageDealt.Should().Be(dealtBefore);
    }

    [Fact]
    public void PoisonDot136_FourTicksThenNone()
    {
        var f = Setup(heroPoison: 400);
        f.Proc(1000);   // tick 100
        long before = f.Foe.HP;
        long dealtBefore = f.Hero.Statistics.TotalDamageDealt;
        var lost = new List<long>();
        for (int i = 0; i < 6; i++)
        {
            long hp = f.Foe.HP;
            f.Step();
            lost.Add(hp - f.Foe.HP);
        }
        lost.Should().Equal(new long[] { 100, 100, 100, 100, 0, 0 }, "4 ticks of 100, then nothing");
        (before - f.Foe.HP).Should().Be(400);
        f.Foe.EnchantPoisonRounds.Should().Be(0);
        f.Foe.EnchantPoisonTick.Should().Be(0);
        f.Hero.Statistics.TotalDamageDealt.Should().Be(dealtBefore + 400, "each tick is counted to the attacker whose proc set it");
        f.Result.TotalDamageDealt.Should().Be(400);
        f.Shown().Should().Contain(Loc.Get("combat.poison_burn", MonsterNames.Display(f.Foe), 100));
    }

    [Fact]
    public void PoisonDot136_ANewProc_RestartsTheRounds_TheLargerOldTickStays()
    {
        var f = Setup();
        f.Proc(2000);   // tick 100
        f.Step();
        f.Step();
        f.Foe.EnchantPoisonRounds.Should().Be(2);
        f.Proc(1000);   // 50: smaller
        f.Foe.EnchantPoisonRounds.Should().Be(4, "a new proc starts the 4 rounds again");
        f.Foe.EnchantPoisonTick.Should().Be(100, "the larger old tick stays, not 50 and not 150");
    }

    [Fact]
    public void PoisonDot136_ANewProc_RestartsTheRounds_TheLargerNewTickReplaces()
    {
        var f = Setup();
        f.Proc(1000);   // tick 50
        f.Step();
        f.Foe.EnchantPoisonRounds.Should().Be(3);
        f.Proc(6000);   // 300: larger
        f.Foe.EnchantPoisonRounds.Should().Be(4, "a new proc starts the 4 rounds again");
        f.Foe.EnchantPoisonTick.Should().Be(300, "the larger new tick replaces the old, never adds to it");
    }

    [Fact]
    public void PoisonDot136_AProcAfterThePoisonEnded_SetsItsOwnTick()
    {
        var f = Setup();
        f.Proc(6000);   // 300
        for (int i = 0; i < 4; i++) f.Step();
        f.Foe.EnchantPoisonRounds.Should().Be(0);
        f.Proc(1000);   // 50
        f.Foe.EnchantPoisonTick.Should().Be(50, "an ended poison keeps nothing");
        f.Foe.EnchantPoisonRounds.Should().Be(4);
    }

    // ---------- the boss layers and the death path ----------

    [Fact]
    public void PoisonDot136_PhaseImmunityReducesATickAsASwing()
    {
        var f = Setup();
        f.Proc(20_000);   // tick 1000
        f.Foe.IsPhysicalImmune = true;
        long hp = f.Foe.HP;
        f.Step();
        (hp - f.Foe.HP).Should().Be(Math.Max(1, (long)(1000 * GameConfig.ModBossPhaseImmunityResidual)),
            "a weapon poison tick is physical, so physical phase immunity leaves its residual");
        (hp - f.Foe.HP).Should().BeLessThan(1000);
    }

    [Fact]
    public void PoisonDot136_DivineArmourReducesATickAsASwing()
    {
        var f = Setup();
        f.Proc(20_000);   // tick 1000
        f.Engine.BossContext = new BossCombatContext { DivineArmorReduction = 0.25 };
        long hp = f.Foe.HP;
        f.Step();
        (hp - f.Foe.HP).Should().Be(750, "divine armour of 25 percent cuts the tick as it cuts a swing");
    }

    [Fact]
    public void PoisonDot136_ATickToZero_KillsThroughThePoisonDeathPath()
    {
        var f = Setup();
        f.Proc(2000);   // tick 100
        f.Foe.HP = 60;
        f.Step();
        f.Foe.HP.Should().Be(0);
        f.Foe.IsAlive.Should().BeFalse();
        f.Result.DefeatedMonsters.Should().Contain(f.Foe, "the kill counts");
        f.Shown().Should().Contain(Loc.Get("combat.monster_poison_death", MonsterNames.Display(f.Foe)));
    }

    // ---------- once a round, and beside the Poisoned status ----------

    [Fact]
    public void PoisonDot136_OneTickARound_ASecondActionInTheRoundTakesNone()
    {
        var f = Setup();
        f.Proc(2000);   // tick 100
        f.Step();
        f.Foe.EnchantPoisonRounds.Should().Be(3);
        long hp = f.Foe.HP;
        // the monster's second action in the same round (a boss attacks two or three times): not held this time
        f.Foe.StunRounds = 0;
        ((System.Threading.Tasks.Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", Rig.F)!
            .Invoke(f.Engine, new object?[] { f.Foe, f.Hero, f.Result, new List<Monster> { f.Foe } })!).GetAwaiter().GetResult();
        f.Foe.EnchantPoisonRounds.Should().Be(3, "the tick lands once a round (StatusTickedThisRound)");
        f.Foe.HP.Should().Be(hp, "no second tick in the round");
    }

    [Fact]
    public void PoisonDot136_ThePoisonedStatusAndTheWeaponPoison_BothTick()
    {
        var f = Setup();
        f.Foe.Poisoned = true;
        f.Foe.PoisonRounds = 3;
        f.Proc(2000);   // tick 100
        f.Foe.PoisonRounds.Should().Be(3, "the proc does not touch the Poisoned status");
        long hp = f.Foe.HP;
        f.Step();
        f.Foe.PoisonRounds.Should().Be(2, "the Poisoned status ticked");
        f.Foe.EnchantPoisonRounds.Should().Be(3, "the weapon poison ticked");
        long status = hp - f.Foe.HP - 100;
        status.Should().BeGreaterThanOrEqualTo(f.Foe.MaxHP * 3 / 100, "the Poisoned status deals its 3 to 5 percent of max HP besides the tick");
        status.Should().BeLessThanOrEqualTo(f.Foe.MaxHP * 5 / 100 + f.Hero.Level / 5 + 2);
    }

    // ---------- teammates ----------

    [Fact]
    public void PoisonDot136_ATeammateProcSetsTheSameState_AndItsTicksAreTheTeammates()
    {
        var f = Setup(matePoison: 400);
        long heroDealt = f.Hero.Statistics.TotalDamageDealt;
        long mateDealt = f.Mate.Statistics.TotalDamageDealt;
        f.MateProc(1000);
        f.Foe.EnchantPoisonRounds.Should().Be(4);
        f.Foe.EnchantPoisonTick.Should().Be(100);
        f.Shown().Should().Contain(Loc.Get("combat.enchant_venom_tm", f.Mate.DisplayName, 4, 100));
        f.Step();
        f.Mate.Statistics.TotalDamageDealt.Should().Be(mateDealt + 100, "the tick is counted to the teammate whose proc set it");
        f.Hero.Statistics.TotalDamageDealt.Should().Be(heroDealt);
    }

    [Fact]
    public void PoisonDot136_ThePlayerLineNamesTheRoundsAndTheTick()
    {
        var f = Setup();
        f.Proc(1000);
        f.Shown().Should().Contain(Loc.Get("combat.enchant_venom", 4, 50));
    }

    // ---------- the text ----------

    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] Keys = { "combat.enchant_venom", "combat.enchant_venom_tm" };

    private static Dictionary<string, string> File(string lang)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Localization"))) dir = Path.GetDirectoryName(dir)!;
        dir.Should().NotBeNull("the Localization folder is found above the test binaries");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(Path.Combine(dir!, "Localization", lang + ".json")))!;
    }

    [Fact]
    public void PoisonDot136_Text_EachChangedKeyIsInTheFiveFiles_Translated()
    {
        var en = File("en");
        foreach (var lang in Langs)
        {
            var file = File(lang);
            foreach (var key in Keys)
            {
                file.Should().ContainKey(key, lang);
                file[key].Should().NotBeNullOrWhiteSpace(lang + " " + key);
                if (lang != "en") file[key].Should().NotBe(en[key], $"{lang} {key} is a translation, not the English");
            }
            file["combat.enchant_venom"].Should().Contain("{0}").And.Contain("{1}", lang + ": the rounds and the tick");
            file["combat.enchant_venom_tm"].Should().Contain("{0}").And.Contain("{1}").And.Contain("{2}", lang + ": the name, the rounds and the tick");
        }
    }

    [Fact]
    public void PoisonDot136_Text_ThePlayerLineNoLongerNamesVenomstrike()
    {
        // the fixed dagger name each language used in the old line
        var old = new Dictionary<string, string>
        {
            ["en"] = "Venomstrike", ["es"] = "Golpe Venenoso", ["fr"] = "Frappe Venimeuse", ["hu"] = "Méregcsapás", ["it"] = "Colpo Velenoso",
        };
        foreach (var lang in Langs)
        {
            string line = Loc.GetIn(lang, "combat.enchant_venom", 4, 50);
            line.Should().NotContain(old[lang], lang + ": the line serves every poison weapon");
            line.Should().Contain("4").And.Contain("50", lang);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void PoisonDot136_Text_TheRowsFit79Columns(string lang)
    {
        string name = new string('N', GameConfig.MaxNameLength);
        var lines = new[]
        {
            Loc.GetIn(lang, "combat.enchant_venom", 4, 9_999_999),
            Loc.GetIn(lang, "combat.enchant_venom_tm", name, 4, 9_999_999),
            Loc.GetIn(lang, "combat.poison_burn", name, 9_999_999),
        };
        foreach (var line in lines)
            foreach (var row in UIHelper.MessageRows(line))
                row.Length.Should().BeLessThanOrEqualTo(79, $"{lang}: \"{row}\"");
    }

    [Fact]
    public void PoisonDot136_TheRenderedTeammateRowWithALongName_Fits79Columns()
    {
        var f = Setup(mateName: new string('N', GameConfig.MaxNameLength));
        f.MateProc(200_000_000);
        var rows = f.Shown().Replace("\r", "").Split('\n').Where(r => r.Length > 0).ToList();
        rows.Should().NotBeEmpty();
        rows.Should().OnlyContain(r => r.Length <= 79);
    }
}
