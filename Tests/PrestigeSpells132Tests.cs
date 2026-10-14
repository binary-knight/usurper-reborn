using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using P = TrainingSystem.ProficiencyLevel;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: the spells of the five prestige classes (Tidesworn, Wavecaller, Cyclebreaker, Abysswarden,
/// Voidreaver) start at Average, as Cleric, Magician and Sage spells do. Their ids stay "spell_spell_N".
/// A saved spell of such a class below Average is raised to Average at load, with its progress cleared;
/// a value at or above Average is never lowered. A respec counts from Average and credits nothing for the raise.
/// Save data is built in memory with the game's own serializer; no save folder is read or written.
/// </summary>
[Collection("SharedGameSingletons")]
public class PrestigeSpells132Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string RaiseName = "RaiseSavedSpellsToAverage";
    private readonly RomanceTrackerData _romanceBefore;

    public PrestigeSpells132Tests()
    {
        _romanceBefore = RomanceTracker.Instance.ToSaveData();
    }

    public void Dispose()
    {
        RomanceTracker.Instance.LoadFromSaveData(_romanceBefore);
    }

    public static readonly CharacterClass[] Prestige =
    {
        CharacterClass.Tidesworn, CharacterClass.Wavecaller, CharacterClass.Cyclebreaker,
        CharacterClass.Abysswarden, CharacterClass.Voidreaver,
    };

    public static IEnumerable<object[]> PrestigeSlots()
    {
        foreach (var c in Prestige)
            for (int slot = 1; slot <= 5; slot++)
                yield return new object[] { c, slot };
    }

    public static IEnumerable<object[]> PrestigeClasses() => Prestige.Select(c => new object[] { c });

    // ---------- helpers ----------

    private static Player Fresh(CharacterClass cls, int level = 30) =>
        new Player { Name1 = "ps132", Name2 = "Prestige Spell", Class = cls, Race = CharacterRace.Human, Level = level, AI = CharacterAI.Human };

    private static bool Raise(Character c)
    {
        var m = typeof(TrainingSystem).GetMethod(RaiseName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        m.Should().NotBeNull($"TrainingSystem.{RaiseName} raises a saved spell below Average");
        try { return (bool)m!.Invoke(null, new object[] { c })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static PlayerData Serialize(Character c)
    {
        var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { c })!;
        return JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
    }

    private static Character Load(PlayerData data)
    {
        try { return (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static P Prof(Character c, string id) => TrainingSystem.GetSkillProficiency(c, id);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    /// <summary>The text of a method from its signature to the next member at the same indent.</summary>
    private static string Body(string file, string signature)
    {
        string src = Source(file);
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{signature} must exist in {file}");
        var next = new Regex(@"\n    (private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    private static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    // ---------- 1: a new prestige character's spells start at Average ----------

    [Theory]
    [MemberData(nameof(PrestigeSlots))]
    public void NewPrestigeCharacter_EverySpell_StartsAtAverage(CharacterClass cls, int slot)
    {
        var c = Fresh(cls);
        string id = TrainingSystem.GetSpellSkillId(cls, slot);
        id.Should().Be($"spell_spell_{slot}", "the ids are unchanged");
        c.SkillProficiencies.Should().NotContainKey(id);

        Prof(c, id).Should().Be(P.Average);
        TrainingSystem.CalculateTotalPointsInvested(c, id).Should().Be(0, "nothing is invested at the starting tier");
        c.SkillProficiencies.Should().NotContainKey(id, "reading adds no key");
    }

    // ---------- 2: the three working classes and the non casters are unchanged ----------

    [Theory]
    [InlineData(CharacterClass.Cleric, "cleric_spell_")]
    [InlineData(CharacterClass.Magician, "magician_spell_")]
    [InlineData(CharacterClass.Sage, "sage_spell_")]
    public void Cleric_Magician_Sage_Spells_StillStartAtAverage(CharacterClass cls, string prefix)
    {
        var c = Fresh(cls);
        for (int slot = 1; slot <= 25; slot++)
        {
            string id = TrainingSystem.GetSpellSkillId(cls, slot);
            id.Should().Be(prefix + slot);
            Prof(c, id).Should().Be(P.Average);
        }
    }

    [Theory]
    [InlineData(CharacterClass.Warrior, "spell_spell_1")]
    [InlineData(CharacterClass.MysticShaman, "spell_spell_1")]
    [InlineData(CharacterClass.Cleric, "spell_spell_1")]
    [InlineData(CharacterClass.Magician, "spell_spell_1")]
    [InlineData(CharacterClass.Sage, "spell_spell_1")]
    [InlineData(CharacterClass.Magician, "cleric_spell_1")]
    [InlineData(CharacterClass.Voidreaver, "cleric_spell_1")]
    public void AStraySpellId_OfAnotherClass_StaysUntrained(CharacterClass cls, string id)
    {
        Prof(Fresh(cls), id).Should().Be(P.Untrained);
    }

    [Fact]
    public void TheCasterList_IsTheListCastSpellGatesOn()
    {
        foreach (CharacterClass cls in Enum.GetValues(typeof(CharacterClass)))
            ClassAbilitySystem.IsSpellcaster(cls).Should().Be(SpellSystem.HasSpells(new Character { Class = cls }), cls.ToString());
    }

    // ---------- 3: the raise helper ----------

    [Theory]
    [MemberData(nameof(PrestigeClasses))]
    public void Raise_UntrainedAndPoor_BecomeAverage_WithProgressCleared(CharacterClass cls)
    {
        var c = Fresh(cls);
        c.TrainingPoints = 37;
        c.Gold = 12345;
        c.SkillProficiencies["spell_spell_1"] = P.Untrained;
        c.SkillProficiencies["spell_spell_2"] = P.Poor;
        c.SkillTrainingProgress["spell_spell_2"] = 1;

        Raise(c).Should().BeTrue();

        Prof(c, "spell_spell_1").Should().Be(P.Average);
        Prof(c, "spell_spell_2").Should().Be(P.Average);
        c.SkillProficiencies["spell_spell_1"].Should().Be(P.Average, "the raised value is stored, so the next save writes it");
        c.SkillProficiencies["spell_spell_2"].Should().Be(P.Average);
        TrainingSystem.GetTrainingProgress(c, "spell_spell_1").Should().Be(0);
        TrainingSystem.GetTrainingProgress(c, "spell_spell_2").Should().Be(0, "the raised key's progress is cleared");
        c.TrainingPoints.Should().Be(37, "the raise credits no training points");
        c.Gold.Should().Be(12345, "the raise touches no gold");
    }

    [Theory]
    [InlineData(P.Average)]
    [InlineData(P.Good)]
    [InlineData(P.Skilled)]
    [InlineData(P.Legendary)]
    public void Raise_NeverLowers_AndKeepsProgress(P saved)
    {
        var c = Fresh(CharacterClass.Voidreaver);
        c.SkillProficiencies["spell_spell_3"] = saved;
        c.SkillTrainingProgress["spell_spell_3"] = 2;

        Raise(c).Should().BeFalse("nothing is below Average");

        c.SkillProficiencies["spell_spell_3"].Should().Be(saved);
        TrainingSystem.GetTrainingProgress(c, "spell_spell_3").Should().Be(2);
    }

    [Fact]
    public void Raise_TwiceChangesNothing_TheSecondTime()
    {
        var c = Fresh(CharacterClass.Abysswarden);
        c.SkillProficiencies["spell_spell_4"] = P.Poor;
        c.SkillProficiencies["spell_spell_5"] = P.Good;

        Raise(c).Should().BeTrue();
        var after = new Dictionary<string, P>(c.SkillProficiencies);
        var progress = new Dictionary<string, int>(c.SkillTrainingProgress);

        Raise(c).Should().BeFalse("a second call finds nothing below Average");
        c.SkillProficiencies.Should().Equal(after);
        c.SkillTrainingProgress.Should().Equal(progress);
    }

    [Fact]
    public void Raise_TouchesNoOtherSkill_AndAddsNoKey()
    {
        var c = Fresh(CharacterClass.Voidreaver);
        c.SkillProficiencies["basic_attack"] = P.Poor;
        c.SkillTrainingProgress["basic_attack"] = 1;
        c.SkillProficiencies["hungering_strike"] = P.Untrained;
        c.SkillProficiencies["cleric_spell_1"] = P.Poor;
        c.SkillProficiencies["spell_spell_2"] = P.Poor;

        Raise(c).Should().BeTrue();

        c.SkillProficiencies["basic_attack"].Should().Be(P.Poor);
        TrainingSystem.GetTrainingProgress(c, "basic_attack").Should().Be(1);
        c.SkillProficiencies["hungering_strike"].Should().Be(P.Untrained);
        c.SkillProficiencies["cleric_spell_1"].Should().Be(P.Poor);
        c.SkillProficiencies.Keys.Should().BeEquivalentTo(new[] { "basic_attack", "hungering_strike", "cleric_spell_1", "spell_spell_2" });
    }

    [Theory]
    [InlineData(CharacterClass.Cleric, "cleric_spell_1")]
    [InlineData(CharacterClass.Magician, "magician_spell_1")]
    [InlineData(CharacterClass.Sage, "sage_spell_1")]
    [InlineData(CharacterClass.Cleric, "spell_spell_1")]
    [InlineData(CharacterClass.Warrior, "spell_spell_1")]
    [InlineData(CharacterClass.MysticShaman, "spell_spell_1")]
    public void Raise_LeavesOtherClasses_Alone(CharacterClass cls, string id)
    {
        var c = Fresh(cls);
        c.SkillProficiencies[id] = P.Poor;
        c.SkillTrainingProgress[id] = 1;

        Raise(c).Should().BeFalse();

        c.SkillProficiencies[id].Should().Be(P.Poor);
        TrainingSystem.GetTrainingProgress(c, id).Should().Be(1);
    }

    // ---------- 4: the raise at load ----------

    [Theory]
    [InlineData(CharacterClass.Voidreaver, P.Poor, 1)]
    [InlineData(CharacterClass.Voidreaver, P.Untrained, 0)]
    [InlineData(CharacterClass.Tidesworn, P.Poor, 0)]
    [InlineData(CharacterClass.Wavecaller, P.Untrained, 0)]
    [InlineData(CharacterClass.Cyclebreaker, P.Poor, 1)]
    [InlineData(CharacterClass.Abysswarden, P.Untrained, 0)]
    public void Load_ASavedPrestigeSpellBelowAverage_LoadsAsAverage_WithProgressZero(CharacterClass cls, P saved, int progress)
    {
        var hero = Fresh(cls);
        hero.TrainingPoints = 21;
        hero.Gold = 500;
        hero.SkillProficiencies["spell_spell_3"] = saved;
        hero.SkillTrainingProgress["spell_spell_3"] = progress;
        var data = Serialize(hero);
        data.SkillProficiencies["spell_spell_3"].Should().Be((int)saved, "the save holds the old value");

        var loaded = Load(data);

        Prof(loaded, "spell_spell_3").Should().Be(P.Average);
        loaded.SkillProficiencies["spell_spell_3"].Should().Be(P.Average);
        TrainingSystem.GetTrainingProgress(loaded, "spell_spell_3").Should().Be(0);
        loaded.TrainingPoints.Should().Be(21);
        loaded.Gold.Should().Be(500);
        Serialize(loaded).SkillProficiencies["spell_spell_3"].Should().Be((int)P.Average, "the next save writes the raised value");
    }

    [Fact]
    public void Load_ASavedGoodPrestigeSpell_StaysGood()
    {
        var hero = Fresh(CharacterClass.Voidreaver);
        hero.SkillProficiencies["spell_spell_2"] = P.Good;
        hero.SkillTrainingProgress["spell_spell_2"] = 3;

        var loaded = Load(Serialize(hero));

        loaded.SkillProficiencies["spell_spell_2"].Should().Be(P.Good);
        TrainingSystem.GetTrainingProgress(loaded, "spell_spell_2").Should().Be(3);
    }

    [Fact]
    public void Load_Twice_ChangesNothing()
    {
        var hero = Fresh(CharacterClass.Wavecaller);
        hero.SkillProficiencies["spell_spell_1"] = P.Poor;
        hero.SkillProficiencies["spell_spell_2"] = P.Skilled;
        hero.SkillTrainingProgress["spell_spell_2"] = 4;

        var once = Load(Serialize(hero));
        var twice = Load(Serialize(once));

        once.SkillProficiencies["spell_spell_1"].Should().Be(P.Average);
        twice.SkillProficiencies.Should().Equal(once.SkillProficiencies);
        twice.SkillTrainingProgress.Should().Equal(once.SkillTrainingProgress);
    }

    [Fact]
    public void Load_ACleric_PoorSpell_StaysPoor()
    {
        var hero = Fresh(CharacterClass.Cleric);
        hero.SkillProficiencies["cleric_spell_3"] = P.Poor;
        hero.SkillTrainingProgress["cleric_spell_3"] = 1;

        var loaded = Load(Serialize(hero));

        loaded.SkillProficiencies["cleric_spell_3"].Should().Be(P.Poor);
        TrainingSystem.GetTrainingProgress(loaded, "cleric_spell_3").Should().Be(1);
    }

    [Fact]
    public void Wiring_BothLoadPaths_RunTheRaise_InTheSharedRestore()
    {
        const string file = "Scripts/Core/GameEngine.cs";
        string engine = Source(file);
        Count(engine, "currentPlayer = RestorePlayerFromSaveData(saveData.Player);")
            .Should().Be(2, "the save menu or login load and the single player load both restore through it");
        Body(file, "private async Task LoadSaveByFileName(").Should().Contain("currentPlayer = RestorePlayerFromSaveData(saveData.Player);");
        Body(file, "private async Task LoadExistingGame(").Should().Contain("currentPlayer = RestorePlayerFromSaveData(saveData.Player);");
        Body(file, "private Character RestorePlayerFromSaveData(PlayerData playerData)").Should().Contain("return RestorePlayerFromSaveDataCore(playerData);");

        string core = Body(file, "private Character RestorePlayerFromSaveDataCore(");
        Count(core, $"TrainingSystem.{RaiseName}(player)").Should().Be(1, "the raise runs once in the shared restore");
        Count(engine, $"TrainingSystem.{RaiseName}(").Should().Be(1, "one production caller");
        core.IndexOf($"TrainingSystem.{RaiseName}(player)", StringComparison.Ordinal)
            .Should().BeGreaterThan(core.IndexOf("player.SkillTrainingProgress = new Dictionary<string, int>(playerData.SkillTrainingProgress);", StringComparison.Ordinal),
                "it runs after the saved proficiencies and progress are copied");
    }

    // ---------- 5: respec counts from Average (decision B1) ----------

    [Theory]
    [MemberData(nameof(PrestigeClasses))]
    public void Respec_APrestigeSpellAtGood_RefundsSix_AndReturnsToAverage(CharacterClass cls)
    {
        var c = Fresh(cls);
        c.TrainingPoints = 0;
        c.SkillProficiencies["spell_spell_1"] = P.Good;

        TrainingSystem.CalculateTotalPointsInvested(c, "spell_spell_1").Should().Be(6, "3 steps at 2 points from Average to Good");
        TrainingSystem.ResetSkillProficiency(c, "spell_spell_1").Should().Be(6);
        c.SkillProficiencies["spell_spell_1"].Should().Be(P.Average);
        TrainingSystem.GetTrainingProgress(c, "spell_spell_1").Should().Be(0);
        c.TrainingPoints.Should().Be(0, "the reset returns the count; nothing is credited by the reset itself");
    }

    [Fact]
    public void Respec_ARaisedSpell_RefundsNothing()
    {
        var c = Fresh(CharacterClass.Cyclebreaker);
        c.SkillProficiencies["spell_spell_2"] = P.Poor;
        c.SkillTrainingProgress["spell_spell_2"] = 1;
        Raise(c).Should().BeTrue();

        TrainingSystem.CalculateTotalPointsInvested(c, "spell_spell_2").Should().Be(0, "no points are credited for the raise");
        TrainingSystem.ResetAllProficiencies(c).Should().Be(0);
        c.SkillProficiencies["spell_spell_2"].Should().Be(P.Average);
    }

    // ---------- 6: NPCs and player copies ----------

    [Fact]
    public void AnNpcPrestigeCaster_TakesTheNpcLevelLadder()
    {
        var npc = new NPC { Name1 = "ps132 npc", Name2 = "ps132 npc", Class = CharacterClass.Voidreaver, Level = 20 };
        Prof(npc, "spell_spell_1").Should().Be(P.Skilled);
    }

    [Theory]
    [MemberData(nameof(PrestigeClasses))]
    public void APlayerCopy_ForPvpCoopOrTeamWars_CastsAtAverage(CharacterClass cls)
    {
        var data = Serialize(Fresh(cls));
        var copy = PlayerCharacterLoader.CreateFromSaveData(data, "ps132 copy");

        copy.Class.Should().Be(cls);
        for (int slot = 1; slot <= 5; slot++)
            Prof(copy, TrainingSystem.GetSpellSkillId(cls, slot)).Should().Be(P.Average);
    }
}
