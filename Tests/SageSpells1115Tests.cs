using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.15 Sage piece 2: the new control spells and soft control on bosses.</summary>
[Collection("SharedGameSingletons")]
public class SageSpells1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static (CombatEngine engine, MemoryStream output) Engine(Random? rng = null, List<Character>? teammates = null)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output));
        if (rng != null) typeof(CombatEngine).GetField("random", F)!.SetValue(engine, rng);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, Sage());
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, teammates ?? new List<Character>());
        return (engine, output);
    }

    private static Character Sage() => new Character
    {
        Name1 = "Sage", Name2 = "Sage", Class = CharacterClass.Sage, Level = 30, Wisdom = 50,
        HP = 100_000, MaxHP = 100_000, CombatSpeed = CombatSpeed.Instant,
    };

    private static Character Ally(string name, CharacterClass cls, long maxHp) => new Character
    {
        Name1 = name, Name2 = name, Class = cls, Level = 30, HP = maxHp, MaxHP = maxHp, CombatSpeed = CombatSpeed.Instant,
    };

    private static Monster Ogre(bool boss = false, bool mini = false, string family = "") =>
        new Monster { Name = "Ogre", Level = 30, HP = 5000, MaxHP = 5000, Strength = 50, Defence = 20, IsBoss = boss, IsMiniBoss = mini, FamilyName = family };

    private static void SpellEffect(CombatEngine engine, Monster m, string effect, int duration, Character? caster = null, CombatResult? result = null) =>
        typeof(CombatEngine).GetMethod("HandleSpecialSpellEffectOnMonster", F)!
            .Invoke(engine, new object[] { m, effect, duration, caster ?? Sage(), 0L, result ?? new CombatResult() });

    /// <summary>One monster turn, the way combat runs it: statuses tick, a held monster skips.</summary>
    private static void MonsterTurn(CombatEngine engine, Monster m)
    {
        m.StatusTickedThisRound = false;
        var result = new CombatResult { Player = Sage() };
        ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { m, Sage(), result, null })!).GetAwaiter().GetResult();
    }

    private static long Hit(CombatEngine engine, Monster m, long damage, Character attacker)
    {
        long before = m.HP;
        ((Task<bool>)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
            .Invoke(engine, new object?[] { m, damage, new CombatResult { Player = Sage() }, "attack", attacker, false })!).GetAwaiter().GetResult();
        return before - m.HP;
    }

    private static string Text(CombatEngine engine, MemoryStream output)
    {
        var term = (TerminalEmulator)typeof(CombatEngine).GetField("terminal", F)!.GetValue(engine)!;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    // ---- the kit ----

    [Fact]
    public void TheNewSpells_SitInTheOldSlots_AtTheOldLevels()
    {
        var expect = new (int slot, string name, int level, bool area)[]
        {
            (5, "Dulling Mist", 5, true), (6, "Scholar's Mark", 6, false), (10, "Slumber Mist", 10, true),
            (12, "Psychic Scream", 12, true), (14, "Compel", 14, true), (18, "Unveil the Pattern", 18, true),
        };
        foreach (var e in expect)
        {
            var info = SpellSystem.GetSpellInfo(CharacterClass.Sage, e.slot);
            info.Name.Should().Be(e.name);
            info.LevelRequired.Should().Be(e.level);
            info.IsMultiTarget.Should().Be(e.area, e.name);
        }
    }

    [Theory]
    [InlineData(5, "Dulling Mist", "dulling_mist", 2)]
    [InlineData(6, "Scholar's Mark", "scholars_mark", 3)]
    [InlineData(10, "Slumber Mist", "slumber_mist", 2)]
    [InlineData(12, "Psychic Scream", "psychic_scream", 2)]
    [InlineData(14, "Compel", "compel", 2)]
    [InlineData(18, "Unveil the Pattern", "unveil_pattern", 2)]
    public void EachNewSpell_CastsItsEffect_ForItsRounds(int slot, string name, string effect, int rounds)
    {
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "SpellSystem.cs"));
        var m = Regex.Match(src, @"case " + slot + @": // " + Regex.Escape(name) + @"\b(.*?)break;", RegexOptions.Singleline);
        m.Success.Should().BeTrue(name);
        m.Groups[1].Value.Should().Contain($"result.SpecialEffect = \"{effect}\";");
        m.Groups[1].Value.Should().Contain($"result.Duration = {rounds};");
    }

    [Fact]
    public void AControlSpell_WithNoDamage_DealsNoAreaDamage()
    {
        CombatEngine.AreaSpellDealsDamage("Debuff", 0).Should().BeFalse("Slumber Mist, Compel, Mass Confusion set no damage");
        CombatEngine.AreaSpellDealsDamage("Debuff", 40).Should().BeTrue("Siren's Lament rolls its own damage");
        CombatEngine.AreaSpellDealsDamage("Attack", 0).Should().BeTrue("an attack spell keeps its fallback");
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().Contain("if (AreaSpellDealsDamage(spellInfo.SpellType, totalDamage))");
    }

    // ---- Dulling Mist ----

    [Fact]
    public void DullingMist_SlowsForTwoRounds()
    {
        var (engine, _) = Engine(new HighRandom());
        var m = Ogre();
        SpellEffect(engine, m, "dulling_mist", 2);
        m.IsSlowed.Should().BeTrue();
        m.SlowDuration.Should().Be(2);
    }

    // ---- Scholar's Mark ----

    [Fact]
    public void ScholarsMark_RaisesAllyDamage_OnlyOnTheMarkedTarget_AndOnlyForThreeRounds()
    {
        var (engine, _) = Engine(new LowRandom());
        var ally = Ally("Aldra", CharacterClass.Warrior, 5000);
        var marked = Ogre();
        var plain = Ogre();
        marked.ArmPow = 0; plain.ArmPow = 0;
        SpellEffect(engine, marked, "scholars_mark", 3);
        marked.IsMarked.Should().BeTrue();
        marked.MarkedDuration.Should().Be(3);

        Hit(engine, plain, 1000, ally).Should().Be(1000);
        Hit(engine, marked, 1000, ally).Should().Be(1300, "+30% on the marked target");

        for (int i = 0; i < 3; i++) MonsterTurn(engine, marked);
        marked.IsMarked.Should().BeFalse("the mark lasts three rounds");
        Hit(engine, marked, 1000, ally).Should().Be(1000);
    }

    [Fact]
    public void ScholarsMark_AlsoRaisesATeammatesSpellDamage()
    {
        // SpellSystem.CastSpell rolls on Random.Shared, so cast until one lands
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var (engine, _) = Engine(new LowRandom());
            var cleric = new Character
            {
                Name1 = "Ilse", Name2 = "Ilse", Class = CharacterClass.Cleric, Level = 100, Wisdom = 200, Intelligence = 200,
                Mana = 1_000_000, MaxMana = 1_000_000, HP = 5000, MaxHP = 5000, CombatSpeed = CombatSpeed.Instant,
            };
            var monsters = new List<Monster> { Ogre(), Ogre(), Ogre() };
            foreach (var o in monsters) { o.HP = 1_000_000; o.MaxHP = 1_000_000; }
            monsters[0].IsMarked = true;
            monsters[0].MarkedDuration = 3;
            ((Task<bool>)typeof(CombatEngine).GetMethod("TryTeammateOffensiveSpell", F)!
                .Invoke(engine, new object[] { cleric, monsters, new CombatResult { Player = Sage() } })!).GetAwaiter().GetResult();
            long marked = 1_000_000 - monsters[0].HP, plain = 1_000_000 - monsters[1].HP;
            if (plain <= 0) continue;   // the cast fizzled
            (1_000_000 - monsters[2].HP).Should().Be(plain);
            marked.Should().Be(plain + (long)(plain * 0.3), "+30% on the marked target");
            return;
        }
        throw new Exception("no teammate spell landed in 100 casts");
    }

    // ---- Slumber Mist ----

    [Fact]
    public void SlumberMist_Sleeps_UntilDamaged_ThenTheMonsterWakes()
    {
        var (engine, output) = Engine(new LowRandom());
        var m = Ogre();
        SpellEffect(engine, m, "slumber_mist", 2);
        m.IsSleeping.Should().BeTrue();
        m.SleepDuration.Should().Be(2);

        var undisturbed = Ogre();
        SpellEffect(engine, undisturbed, "slumber_mist", 2);
        MonsterTurn(engine, undisturbed);
        undisturbed.IsSleeping.Should().BeTrue("no damage, still asleep");

        m.HP -= 10;
        MonsterTurn(engine, m);
        m.IsSleeping.Should().BeFalse("any damage wakes it");
        m.StunImmunityRounds.Should().Be(GameConfig.StunImmunityRoundsAfterRecovery - 1, "the post-hold immunity starts, and it acts this turn so a round ticks off");
        Text(engine, output).Should().Contain(Loc.Get("combat.sage_slumber_broken", "Ogre"));
    }

    [Fact]
    public void SlumberMist_BreaksOnDamageOverTime()
    {
        var (engine, _) = Engine(new LowRandom());
        var m = Ogre();
        SpellEffect(engine, m, "slumber_mist", 2);
        m.Poisoned = true;
        m.PoisonRounds = 3;
        MonsterTurn(engine, m);
        m.IsSleeping.Should().BeFalse("the poison tick wakes it");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SlumberMist_BossesAreImmune(bool boss, bool mini)
    {
        var (engine, output) = Engine(new HighRandom());   // rolls past every resist
        var m = Ogre(boss, mini);
        SpellEffect(engine, m, "slumber_mist", 2);
        m.IsSleeping.Should().BeFalse();
        m.HoldsThisFight.Should().Be(0);
        Text(engine, output).Should().Contain(Loc.Get("combat.sage_slumber_boss_immune", "Ogre"));
    }

    // ---- Psychic Scream ----

    [Fact]
    public void PsychicScream_DistractsForTwoRounds()
    {
        var (engine, output) = Engine(new HighRandom());
        var m = Ogre();
        SpellEffect(engine, m, "psychic_scream", 2);
        m.Distracted.Should().BeTrue();
        m.DistractedRounds.Should().Be(2);
        int penalty = 5 + 30 / 5 + 50 / 10;
        m.DistractedPenalty.Should().Be(penalty);

        var marker = $"(distracted: -{penalty})";
        MonsterTurn(engine, m);
        MonsterTurn(engine, m);
        Regex.Matches(Text(engine, output), Regex.Escape(marker)).Count.Should().Be(2, "both rounds' attacks are distracted");
        MonsterTurn(engine, m);
        Regex.Matches(Text(engine, output), Regex.Escape(marker)).Count.Should().Be(2, "the third is not");
        m.Distracted.Should().BeFalse();
    }

    // ---- Compel ----

    [Fact]
    public void Compel_TauntsOntoTheTank_AndWeakens()
    {
        var tank = Ally("Bram", CharacterClass.Warrior, 3000);
        var bigCaster = Ally("Wynn", CharacterClass.Magician, 9000);
        var (engine, _) = Engine(new HighRandom(), new List<Character> { bigCaster, tank });
        var m = Ogre();
        SpellEffect(engine, m, "compel", 2);
        m.TauntedBy.Should().Be(tank.DisplayName, "the tank, not the ally with the most hit points");
        m.TauntRoundsLeft.Should().Be(2);
        m.TauntStickChance.Should().Be(GameConfig.SoftTauntStickChance);
        m.WeakenRounds.Should().Be(2);
    }

    [Fact]
    public void Compel_WithNoTank_TauntsOntoTheSturdiestMember()
    {
        var small = Ally("Pell", CharacterClass.Magician, 800);
        var big = Ally("Wynn", CharacterClass.Cleric, 150_000);
        var (engine, _) = Engine(new HighRandom(), new List<Character> { small, big });
        var m = Ogre();
        SpellEffect(engine, m, "compel", 2);
        m.TauntedBy.Should().Be(big.DisplayName);
    }

    [Fact]
    public void Compel_FailsOnOldGods()
    {
        var tank = Ally("Bram", CharacterClass.Warrior, 3000);
        var (engine, output) = Engine(new HighRandom(), new List<Character> { tank });
        var god = Ogre(boss: true, family: "OldGod");
        SpellEffect(engine, god, "compel", 2);
        god.TauntedBy.Should().BeNull();
        god.TauntRoundsLeft.Should().Be(0);
        god.WeakenRounds.Should().Be(0);
        Text(engine, output).Should().Contain(Loc.Get("combat.sage_compel_old_god", "Ogre"));
    }

    // ---- Unveil the Pattern ----

    [Fact]
    public void UnveilThePattern_MarksForTwoRounds()
    {
        var (engine, _) = Engine(new HighRandom());
        var m = Ogre();
        SpellEffect(engine, m, "unveil_pattern", 2);
        m.IsMarked.Should().BeTrue();
        m.MarkedDuration.Should().Be(2);
    }

    // ---- soft control on bosses ----

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Bosses_TakeHalfTheSoftControl(bool boss, bool mini)
    {
        var tank = Ally("Bram", CharacterClass.Warrior, 3000);
        var (engine, _) = Engine(new HighRandom(), new List<Character> { tank });   // past the resist
        Monster B() => Ogre(boss, mini);

        var mist = B(); SpellEffect(engine, mist, "dulling_mist", 2); mist.SlowDuration.Should().Be(1);
        var mark = B(); SpellEffect(engine, mark, "scholars_mark", 3); mark.MarkedDuration.Should().Be(1);
        var unveil = B(); SpellEffect(engine, unveil, "unveil_pattern", 2); unveil.MarkedDuration.Should().Be(1);
        var scream = B(); SpellEffect(engine, scream, "psychic_scream", 2); scream.DistractedRounds.Should().Be(1);
        var compel = B(); SpellEffect(engine, compel, "compel", 2); compel.TauntRoundsLeft.Should().Be(1); compel.WeakenRounds.Should().Be(1);
        var conf = B(); SpellEffect(engine, conf, "confusion", 6); conf.ConfusedDuration.Should().Be(3);
        var mass = B(); SpellEffect(engine, mass, "mass_confusion", 6); mass.ConfusedDuration.Should().Be(2, "half, then at most 2");

        var ogre = Ogre(); SpellEffect(engine, ogre, "confusion", 6); ogre.ConfusedDuration.Should().Be(6, "a normal monster takes it all");
        var ogre2 = Ogre(); SpellEffect(engine, ogre2, "mass_confusion", 6); ogre2.ConfusedDuration.Should().Be(6);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Bosses_ResistSoftControl_WhenTheRollFails(bool boss, bool mini)
    {
        var tank = Ally("Bram", CharacterClass.Warrior, 3000);
        var (engine, output) = Engine(new LowRandom(), new List<Character> { tank });   // rolls 0: under the 25% resist
        Monster B() => Ogre(boss, mini);

        var mist = B(); SpellEffect(engine, mist, "dulling_mist", 2); mist.IsSlowed.Should().BeFalse();
        var mark = B(); SpellEffect(engine, mark, "scholars_mark", 3); mark.IsMarked.Should().BeFalse();
        var compel = B(); SpellEffect(engine, compel, "compel", 2); compel.TauntedBy.Should().BeNull();
        var conf = B(); SpellEffect(engine, conf, "confusion", 6); conf.IsConfused.Should().BeFalse();
        var mass = B(); SpellEffect(engine, mass, "mass_confusion", 6); mass.IsConfused.Should().BeFalse();
        Text(engine, output).Should().Contain(Loc.Get("combat.sage_control_resist", "Ogre"));

        var ogre = Ogre(); SpellEffect(engine, ogre, "dulling_mist", 2); ogre.IsSlowed.Should().BeTrue("a normal monster has no resist");
    }

    [Fact]
    public void Bosses_ResistAboutAQuarter_WithASeededEngine()
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        engine.SeedRandomForTests(1115);
        int resisted = 0;
        for (int i = 0; i < 400; i++)
            if (engine.SoftControlRounds(Ogre(boss: true), 2) == 0) resisted++;
        resisted.Should().BeInRange(70, 130);
    }

    // ---- names and descriptions ----

    [Theory]
    [InlineData("es", "Niebla del Sueño")]
    [InlineData("fr", "Brume du Sommeil")]
    [InlineData("en", "Slumber Mist")]
    public void TheNewSpells_ShowTheirNameInThePlayersLanguage(string lang, string expected)
    {
        var before = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var info = SpellSystem.GetSpellInfo(CharacterClass.Sage, 10);
            info.DisplayName.Should().Be(expected);
            info.DisplayDescription.Should().Be(Loc.Get("spell.sage.10.desc"));
            info.Name.Should().Be("Slumber Mist", "the identifier stays English");
        }
        finally { GameConfig.Language = before; }
    }

    [Fact]
    public void EveryNewSageSpell_HasItsNameAndDescription_InAllFiveLanguages()
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Localization", lang + ".json")))!;
            foreach (var slot in new[] { 5, 6, 10, 12, 14, 18 })
            {
                dict.Should().ContainKey($"spell.sage.{slot}.name", lang);
                dict.Should().ContainKey($"spell.sage.{slot}.desc", lang);
            }
        }
        foreach (var slot in new[] { 5, 6, 10, 12, 14, 18 })
        {
            var info = SpellSystem.GetSpellInfo(CharacterClass.Sage, slot);
            Loc.GetIn("en", $"spell.sage.{slot}.name").Should().Be(info.Name, "English matches the table");
            Loc.GetIn("en", $"spell.sage.{slot}.desc").Should().Be(info.Description);
        }
    }

    [Fact]
    public void AServerOverride_OfTheName_StillShows()
    {
        var info = new SpellSystem.SpellInfo(10, "Slumber Mist", "d", 1, 1, "w") { LocKeyBase = "spell.sage.10" };
        info.Name = "Dream Fog";
        info.DisplayName.Should().Be("Dream Fog");
    }

    [Fact]
    public void TheSpellScreens_ShowTheDisplayName()
    {
        string root = Leftovers1114BTests.RepoRoot();
        string combat = File.ReadAllText(Path.Combine(root, "Scripts", "Systems", "CombatEngine.cs"));
        combat.Should().Contain("Loc.Get(\"combat.you_cast_spell\", spellInfo.DisplayName)");
        combat.Should().Contain("Loc.Get(\"combat.teammate_casts_spell\", teammate.DisplayName, spell.DisplayName)");
        combat.Should().Contain("displayName = $\"{spell.DisplayName} ({manaCost} MP)\";");
        string library = File.ReadAllText(Path.Combine(root, "Scripts", "Systems", "SpellLearningSystem.cs"));
        Regex.IsMatch(library, @"\b(spell|chosen|currentSpell\?|knownUnequipped\[i\])\.(Name|Description)\b")
            .Should().BeFalse("the spell library shows the display name and description");
    }
}
