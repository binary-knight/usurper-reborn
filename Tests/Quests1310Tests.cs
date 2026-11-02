using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 audit fixes, quests and contracts (piece FB). Each region names its id. All in memory; the MUD is
/// stood in for by the online flag and a MudServer instance made without its constructor.
/// </summary>
[Collection("SharedGameSingletons")]
public class Quests1310Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;
    private static readonly FieldInfo MudField = typeof(MudServer).GetField("_instance", SNP)!;

    private readonly bool _pacingWas;
    private readonly List<Quest> _made = new();

    public Quests1310Tests()
    {
        _pacingWas = Pacing.Disabled;
        Pacing.Disabled = true;
    }

    public void Dispose()
    {
        Pacing.Disabled = _pacingWas;
        foreach (var q in _made) { q.Deleted = true; q.Occupier = ""; q.OfferedTo = ""; }
    }

    /// <summary>The MUD for the length of a using block: online, with a MudServer instance.</summary>
    private sealed class InMud : IDisposable
    {
        private readonly object? _onlineWas = OnlineField.GetValue(null);
        private readonly object? _mudWas = MudField.GetValue(null);
        public InMud()
        {
            OnlineField.SetValue(null, true);
            MudField.SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(MudServer)));
        }
        public void Dispose()
        {
            OnlineField.SetValue(null, _onlineWas);
            MudField.SetValue(null, _mudWas);
        }
    }

    private static Character Hero(string name, int level = 20) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = level,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        HP = 300, MaxHP = 300, BaseMaxHP = 300, Mental = 80,
    };

    private Quest Held(string occupier, int days = 0, int limit = 14)
    {
        var q = new Quest { Title = "Fb held", Initiator = "A Patron", Occupier = occupier, OccupiedDays = days, DaysToComplete = limit, Date = DateTime.Now };
        q.Objectives.Add(new QuestObjective(QuestObjectiveType.KillMonsters, "Fb", 5, "", ""));
        QuestSystem.AddQuestToDatabase(q);
        _made.Add(q);
        return q;
    }

    private Quest Track(Quest q) { _made.Add(q); return q; }

    private static async Task CallDaily(Character current, string step)
    {
        var engine = GameEngine.Instance;
        var old = engine.CurrentPlayer;
        engine.CurrentPlayer = current;
        try
        {
            var m = typeof(DailySystemManager).GetMethod(step, INP)!;
            var task = (Task)m.Invoke(DailySystemManager.Instance, null)!;
            (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(task, "{0} must return", step);
            await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        finally { engine.CurrentPlayer = old; }
    }

    // ---------- B1: online, quests age once a day, by the world reset ----------

    [Fact]
    public async Task B1_InTheMud_APlayersDailyReset_AgesNoQuest_TheirsOrAnotherPlayers()
    {
        var alpha = Hero("Fb Alpha");
        var mine = Held("Fb Alpha");
        var theirs = Held("Fb Beta");
        using (new InMud())
            await CallDaily(alpha, "ProcessPlayerDailyEvents");
        theirs.OccupiedDays.Should().Be(0, "another player's reset never ages a quest that is not theirs");
        theirs.Deleted.Should().BeFalse();
        mine.OccupiedDays.Should().Be(0, "the world reset ages it, once a day for everyone");
    }

    [Fact]
    public async Task B1_SinglePlayer_TheDailyReset_StillAgesTheQuest()
    {
        var alpha = Hero("Fb Solo");
        var mine = Held("Fb Solo");
        OnlineField.GetValue(null).Should().Be(false);
        await CallDaily(alpha, "ProcessDailyEvents");
        mine.OccupiedDays.Should().Be(1);
    }

    [Fact]
    public void B1_TheWorldReset_AgesEveryClaimedQuestOnce()
    {
        var a = Held("Fb Gamma");
        var b = Held("Fb Delta");
        QuestSystem.ProcessDailyQuestMaintenance();
        a.OccupiedDays.Should().Be(1);
        b.OccupiedDays.Should().Be(1);
        File.ReadAllText(SourcePath("Systems", "WorldSimService.cs")).Should().Contain("QuestSystem.ProcessDailyQuestMaintenance();");
    }

    private static QuestData Saved(Quest q, int days) => new QuestData
    {
        Id = q.Id, Title = q.Title, Initiator = q.Initiator, Occupier = q.Occupier, OccupiedDays = days,
        DaysToComplete = q.DaysToComplete, Status = QuestStatus.Active, StartTime = q.Date, MinLevel = 1, MaxLevel = 9999,
    };

    [Fact]
    public void B1_InTheMud_ALogin_KeepsTheDaysTheWorldCounted()
    {
        var q = Held("Fb Epsilon", days: 5);
        var save = Saved(q, 2);
        using (new InMud())
            QuestSystem.MergePlayerQuests("Fb Epsilon", new List<QuestData> { save });
        var held = QuestSystem.GetPlayerQuests("Fb Epsilon");
        held.Should().ContainSingle(x => x.Id == q.Id);
        Track(held.Single()).OccupiedDays.Should().Be(5, "the days the world reset counted while the player was away stay");
    }

    [Fact]
    public void B1_InTheMud_ALogin_DoesNotHandBackAQuestTheWorldFailed()
    {
        var q = Held("Fb Zeta", days: 15);
        var save = Saved(q, 3);
        q.Deleted = true; q.Occupier = "";   // as ProcessQuestFailure leaves it
        using (new InMud())
            QuestSystem.MergePlayerQuests("Fb Zeta", new List<QuestData> { save });
        QuestSystem.GetPlayerQuests("Fb Zeta").Should().BeEmpty("the failure mail said it failed; the player does not hold it");
    }

    [Fact]
    public void B1_SinglePlayer_ALoad_RestoresTheSaveAsSaved()
    {
        var q = Held("Fb Eta", days: 15);
        var save = Saved(q, 3);
        q.Deleted = true; q.Occupier = "";
        QuestSystem.MergePlayerQuests("Fb Eta", new List<QuestData> { save });
        var held = QuestSystem.GetPlayerQuests("Fb Eta");
        held.Should().ContainSingle(x => x.Id == q.Id);
        Track(held.Single()).OccupiedDays.Should().Be(3, "a single player load is the save, whatever an earlier game left in memory");
    }

    // ---------- helpers ----------

    private static string SourcePath(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "usurper-reloaded.csproj")))
            dir = Path.GetDirectoryName(dir);
        dir.Should().NotBeNull();
        return Path.Combine(new[] { dir!, "Scripts" }.Concat(parts).ToArray());
    }
}
