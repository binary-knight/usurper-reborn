using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.5: an answered moral paradox is kept in the save. After a save and reload the floor 95 paradox
/// (destroy_darkness) is not offered again and its effects are not applied a second time, through the
/// file save and the SQL save. An old save without the field loads with no paradox answered.
/// </summary>
[Collection("SharedGameSingletons")]
public class ParadoxSave125Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const string Paradox = "destroy_darkness";
    private const string PlayerName = "ParadoxSaveHero";

    private static readonly FieldInfo StoryField = typeof(StoryProgressionSystem).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo OceanField = typeof(OceanPhilosophySystem).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo AmnesiaField = typeof(AmnesiaSystem).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo MoralField = typeof(MoralParadoxSystem).GetField("_fallbackInstance", SNP)!;
    private readonly object? _oldStory = StoryField.GetValue(null);
    private readonly object? _oldOcean = OceanField.GetValue(null);
    private readonly object? _oldAmnesia = AmnesiaField.GetValue(null);
    private readonly object? _oldMoral = MoralField.GetValue(null);
    private readonly string _language = GameConfig.Language;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-paradox-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_dir, "saves.db");

    public ParadoxSave125Tests()
    {
        Directory.CreateDirectory(_dir);
        GameConfig.Language = "en";
    }

    public void Dispose()
    {
        StoryField.SetValue(null, _oldStory);
        OceanField.SetValue(null, _oldOcean);
        AmnesiaField.SetValue(null, _oldAmnesia);
        MoralField.SetValue(null, _oldMoral);
        GameConfig.Language = _language;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- helpers ----------

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 60)), i =>
        {
            if (i >= 40) throw new InvalidOperationException("the screen asked for input more than 40 times");
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static Character Hero() => new()
    {
        Name1 = PlayerName, Name2 = PlayerName, Class = CharacterClass.Warrior, Level = 100, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 1000, Chivalry = 300, Darkness = 300, Wisdom = 40, BaseWisdom = 40,
    };

    /// <summary>A world where the floor 95 paradox is on offer: the Ascension chapter and the Sunforged Blade.</summary>
    private static void FreshWorld()
    {
        var story = new StoryProgressionSystem();
        story.AdvanceChapter(StoryChapter.Ascension);
        story.CollectArtifact(ArtifactType.SunforgedBlade);
        StoryField.SetValue(null, story);
        var ocean = new OceanPhilosophySystem();
        ocean.RestoreFromSave(Array.Empty<WaveFragment>(), Array.Empty<AwakeningMoment>(), null, 3);
        OceanField.SetValue(null, ocean);
        AmnesiaField.SetValue(null, new AmnesiaSystem());
        MoralField.SetValue(null, new MoralParadoxSystem());
    }

    private static (int Chivalry, int Darkness, int Wisdom, int BaseWisdom) Stats(Character c) =>
        ((int)c.Chivalry, (int)c.Darkness, (int)c.Wisdom, (int)c.BaseWisdom);

    /// <summary>Answers the floor 95 paradox (third option, take the darkness) and returns the save data.</summary>
    private static SaveGameData AnswerAndSave(Character hero)
    {
        MoralParadoxSystem.Instance.IsParadoxAvailable(Paradox, hero).Should().BeTrue("the floor 95 paradox is on offer before it is answered");
        var s = NewScreen("3");
        var choice = MoralParadoxSystem.Instance.PresentParadox(Paradox, hero, s.Term).GetAwaiter().GetResult();
        choice.Should().NotBeNull();
        choice!.OptionId.Should().Be("take_darkness");
        return new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            SaveTime = DateTime.Now,
            Player = new PlayerData { Name1 = PlayerName, Name2 = PlayerName, Level = 100 },
            StorySystems = SaveSystem.Instance.SerializeStorySystemsPublic(),
        };
    }

    /// <summary>A new session (a fresh paradox system and story) that loads the saved story state.</summary>
    private static void Reload(SaveGameData? loaded)
    {
        loaded.Should().NotBeNull("the save reads back");
        FreshWorld();
        SaveSystem.Instance.RestoreStorySystems(loaded!.StorySystems);
    }

    private static void NotOfferedAgain_AndNotReapplied(Character hero)
    {
        MoralParadoxSystem.Instance.CompletedParadoxIds.Should().Equal(Paradox);
        MoralParadoxSystem.Instance.IsParadoxAvailable(Paradox, hero).Should().BeFalse("the answered paradox does not come back after a reload");
        MoralParadoxSystem.Instance.HasMadeChoice(Paradox).Should().BeTrue();

        var before = Stats(hero);
        var s = NewScreen("3");
        MoralParadoxSystem.Instance.PresentParadox(Paradox, hero, s.Term).GetAwaiter().GetResult();
        s.Text.Should().Contain(Loc.GetIn("en", "moral.already_chosen"));
        s.Text.Should().NotContain(Loc.GetIn("en", MoralParadoxSystem.ParadoxKey(Paradox, "name")), "the paradox screen is not shown again");
        Stats(hero).Should().Be(before, "the effects of the answer are not applied a second time");
    }

    private FileSaveBackend FileBackend()
    {
        var backend = new FileSaveBackend();
        typeof(FileSaveBackend).GetField("baseSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(backend, _dir);
        return backend;
    }

    private static JsonObject WithoutField(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var story = root["storySystems"]!.AsObject();
        story.ContainsKey("completedParadoxIds").Should().BeTrue("the save writes the field");
        story.Remove("completedParadoxIds");
        return root;
    }

    private static void OldSaveLoads_WithNothingAnswered(SaveGameData? loaded, Character hero)
    {
        loaded.Should().NotBeNull("an old save without the field still loads");
        loaded!.StorySystems.CompletedParadoxIds.Should().BeEmpty();
        FreshWorld();
        // the session had answered it in memory; the load replaces that with the save's (empty) record
        MoralParadoxSystem.Instance.RestoreFromSave(new[] { Paradox });
        var act = () => SaveSystem.Instance.RestoreStorySystems(loaded.StorySystems);
        act.Should().NotThrow();
        MoralParadoxSystem.Instance.CompletedParadoxIds.Should().BeEmpty();
        MoralParadoxSystem.Instance.IsParadoxAvailable(Paradox, hero).Should().BeTrue();
    }

    // ---------- tests ----------

    [Fact]
    public async Task FileSave_AnsweredParadox_DoesNotReturn_OrReapply_AfterReload()
    {
        FreshWorld();
        var hero = Hero();
        var data = AnswerAndSave(hero);
        var answered = Stats(hero);
        answered.Wisdom.Should().Be(60, "the answer gave its wisdom once");

        var backend = FileBackend();
        (await backend.WriteGameData(PlayerName, data)).Should().BeTrue();
        Reload(await backend.ReadGameData(PlayerName));

        NotOfferedAgain_AndNotReapplied(hero);
        Stats(hero).Should().Be(answered);
    }

    [Fact]
    public async Task SqlSave_AnsweredParadox_DoesNotReturn_OrReapply_AfterReload()
    {
        FreshWorld();
        var hero = Hero();
        var data = AnswerAndSave(hero);
        var answered = Stats(hero);

        var backend = new SqlSaveBackend(DbPath);
        (await backend.WriteGameData(PlayerName, data)).Should().BeTrue();
        Reload(await backend.ReadGameData(PlayerName));

        NotOfferedAgain_AndNotReapplied(hero);
        Stats(hero).Should().Be(answered);
    }

    [Fact]
    public async Task OldFileSave_WithoutTheField_LoadsWithNothingAnswered()
    {
        FreshWorld();
        var hero = Hero();
        var data = AnswerAndSave(hero);
        var backend = FileBackend();
        (await backend.WriteGameData(PlayerName, data)).Should().BeTrue();

        var file = Directory.GetFiles(_dir, "*.json").Single();
        File.WriteAllText(file, WithoutField(File.ReadAllText(file)).ToJsonString());

        OldSaveLoads_WithNothingAnswered(await backend.ReadGameData(PlayerName), Hero());
    }

    [Fact]
    public async Task OldSqlSave_WithoutTheField_LoadsWithNothingAnswered()
    {
        FreshWorld();
        var hero = Hero();
        var data = AnswerAndSave(hero);
        var backend = new SqlSaveBackend(DbPath);
        (await backend.WriteGameData(PlayerName, data)).Should().BeTrue();

        using (var conn = new SqliteConnection($"Data Source={DbPath}"))
        {
            conn.Open();
            using var read = conn.CreateCommand();
            read.CommandText = "SELECT player_data FROM players WHERE LOWER(username) = LOWER(@u);";
            read.Parameters.AddWithValue("@u", PlayerName);
            var json = (string)read.ExecuteScalar()!;
            using var write = conn.CreateCommand();
            write.CommandText = "UPDATE players SET player_data = @d WHERE LOWER(username) = LOWER(@u);";
            write.Parameters.AddWithValue("@d", WithoutField(json).ToJsonString());
            write.Parameters.AddWithValue("@u", PlayerName);
            write.ExecuteNonQuery().Should().Be(1);
        }

        OldSaveLoads_WithNothingAnswered(await backend.ReadGameData(PlayerName), Hero());
    }
}
