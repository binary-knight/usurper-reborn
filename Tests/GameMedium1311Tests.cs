using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Locations;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.11 game medium rows: R10 (Magic Shop pages open on a clear), K4 (a quest the world failed is not handed back
/// after a server restart) and N6 (a floor boss is drawn as its theme's boss).
/// </summary>
[Collection("SharedGameSingletons")]
public class GameMedium1311Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;
    private static readonly FieldInfo MudField = typeof(MudServer).GetField("_instance", SNP)!;
    private static readonly FieldInfo EndedField = typeof(QuestSystem).GetField("endedByWorld", SNP)!;
    private static readonly FieldInfo DatabaseField = typeof(QuestSystem).GetField("questDatabase", SNP)!;

    private readonly bool _pacingWas = Pacing.Disabled;
    private readonly List<Quest> _made = new();

    public GameMedium1311Tests() { Pacing.Disabled = true; }

    public void Dispose()
    {
        Pacing.Disabled = _pacingWas;
        foreach (var q in _made) { q.Deleted = true; q.Occupier = ""; q.OfferedTo = ""; }
        ((Dictionary<string, DateTime>)EndedField.GetValue(null)!).Clear();
    }

    private sealed class InMud : IDisposable
    {
        private readonly object? _onlineWas = OnlineField.GetValue(null);
        private readonly object? _mudWas = MudField.GetValue(null);
        public InMud()
        {
            OnlineField.SetValue(null, true);
            MudField.SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(MudServer)));
        }
        public void Dispose() { OnlineField.SetValue(null, _onlineWas); MudField.SetValue(null, _mudWas); }
    }

    private static string SourcePath(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "usurper-reloaded.csproj")))
            dir = Path.GetDirectoryName(dir);
        dir.Should().NotBeNull();
        return Path.Combine(new[] { dir!, "Scripts" }.Concat(parts).ToArray());
    }

    // ---------------------------------------------------------------- R10

    private static string Method(string src, string name)
    {
        int i = src.IndexOf(name, StringComparison.Ordinal);
        i.Should().BeGreaterThan(0, name);
        int j = src.IndexOf("\n    }\n", i, StringComparison.Ordinal);
        return src.Substring(i, j - i);
    }

    [Theory]
    [InlineData("private async Task IdentifyItem(Character player)", "magic_shop.unidentified_items")]
    [InlineData("private async Task RemoveCurse(Character player)", "magic_shop.curse_removal")]
    [InlineData("private async Task BuyDungeonResetScroll(Character player)", "magic_shop.reset_scroll_header")]
    public void TheMagicShopPages_OpenOnAClear_BeforeTheirHeader(string signature, string headerKey)
    {
        var body = Method(File.ReadAllText(SourcePath("Locations", "MagicShopLocation.cs")), signature);
        int clear = body.IndexOf("terminal.ClearScreen();", StringComparison.Ordinal);
        clear.Should().BeGreaterThan(0, "{0} clears the screen", signature);
        body.IndexOf(headerKey, StringComparison.Ordinal).Should().BeGreaterThan(clear, "the clear comes before the page's first line");
        body.Substring(0, clear).Should().NotContain("DisplayMessage", "nothing is written before the clear");
    }

    // ---------------------------------------------------------------- K4

    private Quest Held(string occupier, int days, int limit = 14)
    {
        var q = new Quest { Title = "Gm held", Initiator = "A Patron", Occupier = occupier, OccupiedDays = days, DaysToComplete = limit, Date = DateTime.Now };
        q.Objectives.Add(new QuestObjective(QuestObjectiveType.KillMonsters, "Gm", 5, "", ""));
        QuestSystem.AddQuestToDatabase(q);
        _made.Add(q);
        return q;
    }

    private static QuestData Saved(Quest q, int days) => new QuestData
    {
        Id = q.Id, Title = q.Title, Initiator = q.Initiator, Occupier = q.Occupier, OccupiedDays = days,
        DaysToComplete = q.DaysToComplete, Status = QuestStatus.Active, StartTime = q.Date, MinLevel = 1, MaxLevel = 9999,
    };

    /// <summary>A server restart: the shared list is empty again and the in memory record of failed quests is gone.</summary>
    private static void Restart(Quest failed)
    {
        ((List<Quest>)DatabaseField.GetValue(null)!).Remove(failed);
        ((Dictionary<string, DateTime>)EndedField.GetValue(null)!).Clear();
    }

    [Fact]
    public void K4_AQuestTheWorldFailed_IsNotHandedBackAfterARestart_WhenTheRecordIsLoaded()
    {
        var q = Held("Gm Alpha", days: 15);
        var save = Saved(q, 3);
        string record;
        using (new InMud())
        {
            QuestSystem.ProcessDailyQuestMaintenance();   // the world reset: 16 days over the limit of 14
            q.Deleted.Should().BeTrue();
            record = QuestSystem.SerializeEndedByWorld(DateTime.Now);
            Restart(q);
            QuestSystem.LoadEndedByWorld(record);
            QuestSystem.MergePlayerQuests("Gm Alpha", new List<QuestData> { save });
        }
        QuestSystem.GetPlayerQuests("Gm Alpha").Should().BeEmpty("the world failed it and said so by mail; a restart does not revive it");
    }

    [Fact]
    public void K4_WithoutTheRecord_TheRestartHandsItBack_AsBefore()
    {
        var q = Held("Gm Beta", days: 15);
        var save = Saved(q, 3);
        using (new InMud())
        {
            QuestSystem.ProcessDailyQuestMaintenance();
            Restart(q);
            QuestSystem.MergePlayerQuests("Gm Beta", new List<QuestData> { save });
        }
        var back = QuestSystem.GetPlayerQuests("Gm Beta");
        back.Should().ContainSingle(x => x.Id == q.Id, "this is what the record prevents");
        _made.AddRange(back);
    }

    [Fact]
    public void K4_AQuestNotFailed_StillComesBackAfterARestart()
    {
        var failed = Held("Gm Gamma", days: 15);
        var kept = Held("Gm Gamma", days: 1);
        var save = Saved(kept, 1);
        using (new InMud())
        {
            QuestSystem.ProcessDailyQuestMaintenance();
            string record = QuestSystem.SerializeEndedByWorld(DateTime.Now);
            Restart(failed);
            ((List<Quest>)DatabaseField.GetValue(null)!).Remove(kept);
            QuestSystem.LoadEndedByWorld(record);
            QuestSystem.MergePlayerQuests("Gm Gamma", new List<QuestData> { Saved(failed, 3), save });
        }
        var back = QuestSystem.GetPlayerQuests("Gm Gamma");
        back.Should().ContainSingle(x => x.Id == kept.Id);
        back.Should().NotContain(x => x.Id == failed.Id);
        _made.AddRange(back);
    }

    [Fact]
    public void K4_SinglePlayer_RecordsNothing_AndAFailedQuestStaysAsItWas()
    {
        var q = Held("Gm Delta", days: 15);
        QuestSystem.ProcessDailyQuestMaintenance();
        QuestSystem.IsEndedByWorld(q.Id).Should().BeFalse("only the MUD's world reset keeps the record");
    }

    [Fact]
    public void K4_TheRecord_DropsAnEntryAfterThirtyDays_AndOldWorldStateLoads()
    {
        QuestSystem.LoadEndedByWorld(null);
        QuestSystem.LoadEndedByWorld("");
        QuestSystem.LoadEndedByWorld("not json");
        QuestSystem.LoadEndedByWorld("{}");
        var now = DateTime.Now;
        QuestSystem.LoadEndedByWorld($"{{\"old-id\":\"{now.AddDays(-31):O}\",\"new-id\":\"{now.AddDays(-29):O}\"}}");
        string json = QuestSystem.SerializeEndedByWorld(now);
        json.Should().Contain("new-id").And.NotContain("old-id");
        QuestSystem.IsEndedByWorld("old-id").Should().BeFalse();
        QuestSystem.IsEndedByWorld("new-id").Should().BeTrue();
    }

    [Fact]
    public void K4_TheSimLoadsTheRecordAtStart_AndSavesItAfterTheDailyMaintenance()
    {
        var src = File.ReadAllText(SourcePath("Systems", "WorldSimService.cs"));
        src.Should().Contain("LoadEndedQuestsState();").And.Contain("QuestSystem.LoadEndedByWorld(");
        Regex.IsMatch(src, @"QuestSystem\.ProcessDailyQuestMaintenance\(\);\s*SaveEndedQuestsState\(\);").Should().BeTrue();
    }

    // ---------------------------------------------------------------- N6

    [Fact]
    public void N6_AFloorBoss_IsDrawnFromItsThemesFamily_NotTheFamilyDrawnForItsLevel()
    {
        var boss = new Monster { Name = "Skull Revenant", FamilyName = "Goblinoid", IsBoss = true };
        string[] goblin = MonsterArtDatabase.GetArtForFamily(boss.ArtFamilyName)!;
        boss.ArtFamily = DungeonLocation.BossArtFamily(DungeonTheme.Catacombs);
        boss.ArtFamilyName.Should().Be("Undead");
        MonsterArtDatabase.GetArtForFamily(boss.ArtFamilyName)!.Should().NotEqual(goblin);
        boss.FamilyName.Should().Be("Goblinoid", "only the picture changes; no rule reads ArtFamily");
    }

    [Fact]
    public void N6_EveryTheme_HasAFamilyWithArt_AndAMonsterWithoutOneKeepsItsOwn()
    {
        foreach (DungeonTheme theme in Enum.GetValues(typeof(DungeonTheme)))
        {
            string fam = DungeonLocation.BossArtFamily(theme);
            if (fam.Length > 0) MonsterArtDatabase.GetArtForFamily(fam).Should().NotBeNull(theme.ToString());
        }
        new Monster { FamilyName = "Beast" }.ArtFamilyName.Should().Be("Beast");
        File.ReadAllText(SourcePath("Locations", "DungeonLocation.cs")).Should().Contain("boss.ArtFamily = BossArtFamily(currentFloor.Theme);");
    }
}
