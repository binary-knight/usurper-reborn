using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 lost stat rewards, piece 4 (the decided ASK items). Artifact stats are lasting grants,
/// the Old God defeat result grants them too, and a one-time login restore gives an older save the
/// stats of the artifacts it already holds. Each grant is checked against an untouched twin through
/// a fight start, a save round trip, an equipment change and a level-up.
/// </summary>
[Collection("SharedGameSingletons")]
public class StatRewardsDecisions1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static Character Fresh(string name) => StatRewards1115Tests.Fresh(name);

    /// <summary>Starts a monster fight (its full RecalculateStats) and escapes with a smoke bomb.</summary>
    internal static async Task FightStart(Character c)
    {
        c.SmokeBombs = 1;
        c.CombatSpeed = CombatSpeed.Instant;
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(string.Concat(Enumerable.Repeat("R\n", 8))), new MemoryStream()));
        engine.SeedRandomForTests(1115);
        var dummy = new Monster { Name = "Training Dummy", Level = 1, HP = 5_000_000, MaxHP = 5_000_000, Strength = 0, Defence = 0, Experience = 1, Gold = 0 };
        await engine.PlayerVsMonsters(c, new List<Monster> { dummy });
    }

    /// <summary>
    /// <paramref name="granted"/> holds <paramref name="delta"/> more of <paramref name="stat"/> than
    /// <paramref name="plain"/>, in Base and derived, and still does after a fight start, a save round
    /// trip, an equipment change and a level-up applied to both.
    /// </summary>
    internal static async Task ShouldLastAgainst(Character plain, Character granted, StatKind stat, long delta, string what)
    {
        void Diff(Character p, Character g, string when)
        {
            (g.GetBaseStat(stat) - p.GetBaseStat(stat)).Should().Be(delta, $"{what} is in the Base field {when}");
            (StatRewards1115Tests.Derived(g, stat) - StatRewards1115Tests.Derived(p, stat)).Should().Be(delta, $"{what} is in effect {when}");
        }
        Diff(plain, granted, "after the grant");

        await FightStart(plain);
        await FightStart(granted);
        Diff(plain, granted, "after the start of a monster fight");

        plain = StatRewards1115Tests.RoundTrip(plain);
        granted = StatRewards1115Tests.RoundTrip(granted);
        Diff(plain, granted, "after a save round trip");

        foreach (var c in new[] { plain, granted })
        {
            var ring = new Equipment { Name = "Test Decisions Ring", Slot = EquipmentSlot.LFinger, StrengthBonus = 4, DefenceBonus = 2, MinLevel = 1 };
            EquipmentDatabase.RegisterDynamic(ring);
            c.EquipItem(ring, EquipmentSlot.LFinger, out _).Should().BeTrue();
            c.RecalculateStats();
            c.UnequipSlot(EquipmentSlot.LFinger);
            c.RecalculateStats();
        }
        Diff(plain, granted, "after an equipment change");

        LevelMasterLocation.ApplyClassStatIncreases(plain);
        LevelMasterLocation.ApplyClassStatIncreases(granted);
        Diff(plain, granted, "after a level-up");
    }

    // ---------------- artifacts ----------------

    private static float Mult => MetaProgressionSystem.Instance.GetArtifactMultiplier();

    private static long Expected(ArtifactType type, StatKind stat) =>
        ArtifactSystem.ArtifactStatGrants(ArtifactSystem.Instance.GetArtifact(type)!, Mult)
            .Where(g => g.stat == stat).Sum(g => g.amount);

    /// <summary>Runs <paramref name="body"/> with <paramref name="type"/> not yet collected, then restores the set.</summary>
    private static async Task WithoutArtifact(ArtifactType type, Func<Task> body)
    {
        var set = StoryProgressionSystem.Instance.CollectedArtifacts;
        var saved = set.ToList();
        set.Remove(type);
        try { await body(); }
        finally { set.Clear(); foreach (var a in saved) set.Add(a); }
    }

    private static void ApplyBonuses(Character c, ArtifactType type)
    {
        var m = typeof(ArtifactSystem).GetMethod("ApplyArtifactBonuses", F)!;
        try { m.Invoke(ArtifactSystem.Instance, new object[] { c, ArtifactSystem.Instance.GetArtifact(type)! }); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    [Fact]
    public async Task CollectingAnArtifact_ItsStatsLast()
    {
        var plain = Fresh("SdArtPlain");
        var c = Fresh("SdArt");
        ApplyBonuses(c, ArtifactType.CreatorsEye);   // Wisdom 25, Intelligence 20 (half Wisdom, half Dexterity), Dexterity 15
        long wis = Expected(ArtifactType.CreatorsEye, StatKind.Wisdom);
        long dex = Expected(ArtifactType.CreatorsEye, StatKind.Dexterity);
        wis.Should().BeGreaterThan(0);
        dex.Should().BeGreaterThan(0);
        c.BaseWisdom.Should().Be(20 + wis);
        c.BaseDexterity.Should().Be(10 + dex);
        await ShouldLastAgainst(plain, c, StatKind.Wisdom, wis, "the artifact Wisdom");
    }

    [Fact]
    public async Task AnArtifactsDefenceAndStamina_Last_AndItsMaxHPIsInBase()
    {
        var plain = Fresh("SdWorldPlain");
        var c = Fresh("SdWorld");
        ApplyBonuses(c, ArtifactType.Worldstone);
        long def = Expected(ArtifactType.Worldstone, StatKind.Defence);
        long hp = Expected(ArtifactType.Worldstone, StatKind.MaxHP);
        def.Should().BeGreaterThan(0);
        hp.Should().BeGreaterThan(0);
        c.BaseMaxHP.Should().Be(100 + hp, "the Max HP grant goes to Base");
        c.HP.Should().Be(c.MaxHP, "a full pool stays full: the Max HP grant also heals");
        var plain2 = Fresh("SdWorldPlain2");
        var c2 = Fresh("SdWorld2");
        ApplyBonuses(c2, ArtifactType.Worldstone);
        await ShouldLastAgainst(plain, c, StatKind.Defence, def, "the artifact Defence");
        await ShouldLastAgainst(plain2, c2, StatKind.Stamina, Expected(ArtifactType.Worldstone, StatKind.Stamina), "the artifact Stamina");
    }

    [Fact]
    public async Task TheOldGodDefeatResult_GrantsTheArtifactsStats_Once()
    {
        await WithoutArtifact(ArtifactType.ScalesOfLaw, async () =>
        {
            var plain = Fresh("SdDefeatPlain");
            var c = Fresh("SdDefeat");
            ArtifactSystem.Instance.GrantArtifactIfMissing(c, ArtifactType.ScalesOfLaw).Should().BeTrue();
            StoryProgressionSystem.Instance.CollectedArtifacts.Should().Contain(ArtifactType.ScalesOfLaw);
            ArtifactSystem.Instance.GrantArtifactIfMissing(c, ArtifactType.ScalesOfLaw).Should().BeFalse("an artifact already held is not granted again");
            long def = Expected(ArtifactType.ScalesOfLaw, StatKind.Defence);
            def.Should().BeGreaterThan(0);
            await ShouldLastAgainst(plain, c, StatKind.Defence, def, "the defeat-path artifact Defence");
        });
    }

    [Fact]
    public void TheOldGodDefeatResult_UsesTheGrantingCall()
    {
        string body = Body(File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Locations/DungeonLocation.cs")),
            "private async Task HandleGodEncounterResult(");
        body.Should().Contain("ArtifactSystem.Instance.GrantArtifactIfMissing(player, artifactType.Value)");
        body.Should().NotContain("CollectedArtifacts.Add(", "adding to the list directly skips the stats");
    }

    // ---------------- artifact login restore ----------------

    /// <summary>A character saved by an older version: the restore flag is absent, so it loads false.</summary>
    private static Character OldSave(Character c)
    {
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        PlayerData data;
        try { data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { c })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(data))!.AsObject();
        node.Remove(nameof(PlayerData.ArtifactStatsApplied)).Should().BeTrue("the field is saved");
        var back = JsonSerializer.Deserialize<PlayerData>(node.ToJsonString())!;
        return MenuKeysNeedEnterPref1115Tests.Restore(back);
    }

    private static StorySystemsData Saved(params ArtifactType[] types) =>
        new StorySystemsData { CollectedArtifacts = types.Select(t => (int)t).ToList() };

    private static Dictionary<StatKind, long> Bases(Character c) =>
        Enum.GetValues<StatKind>().ToDictionary(s => s, s => c.GetBaseStat(s));

    [Fact]
    public async Task TheArtifactRestore_RunsOnce_AndIsIdempotentAcrossTwoLogins()
    {
        var c = OldSave(Fresh("SdRestore"));
        c.ArtifactStatsApplied.Should().BeFalse("an older save has no flag, so the restore is due");
        var before = Bases(c);
        long hp = c.HP;
        var story = Saved(ArtifactType.CreatorsEye, ArtifactType.Worldstone);

        GameEngine.RunStatRewardMigrations(c, story);   // first login
        c.ArtifactStatsApplied.Should().BeTrue();
        foreach (var s in Enum.GetValues<StatKind>())
            c.GetBaseStat(s).Should().Be(before[s] + Expected(ArtifactType.CreatorsEye, s) + Expected(ArtifactType.Worldstone, s),
                $"{s} gets both artifacts' amount once");
        c.HP.Should().Be(hp, "the restore does not heal; the pool was given at collection");
        var afterFirst = Bases(c);

        var again = StatRewards1115Tests.RoundTrip(c);   // saved, then the second login
        again.ArtifactStatsApplied.Should().BeTrue();
        GameEngine.RunStatRewardMigrations(again, story);
        Bases(again).Should().Equal(afterFirst, "the second login adds nothing");
        GameEngine.RunStatRewardMigrations(again, story);
        Bases(again).Should().Equal(afterFirst);

        var plain = Fresh("SdRestorePlain");
        await ShouldLastAgainst(plain, again, StatKind.Defence, Expected(ArtifactType.Worldstone, StatKind.Defence), "the restored Defence");
    }

    [Fact]
    public void TheArtifactRestore_WithNothingToRestore_LeavesTheCharacterUnchanged()
    {
        var c = OldSave(Fresh("SdNothing"));
        var before = Bases(c);
        long hp = c.HP, str = c.Strength, def = c.Defence;
        GameEngine.RunStatRewardMigrations(c, Saved());
        Bases(c).Should().Equal(before);
        (c.HP, c.Strength, c.Defence).Should().Be((hp, str, def));
        c.ArtifactStatsApplied.Should().BeTrue("the restore still counts as done");
        GameEngine.RunStatRewardMigrations(c, Saved(ArtifactType.CreatorsEye));
        Bases(c).Should().Equal(before, "an artifact collected later is granted at collection, never by the restore");
    }

    [Fact]
    public void ACharacterMadeOnThisVersion_IsNeverRestored()
    {
        var c = Fresh("SdNew");
        c.ArtifactStatsApplied.Should().BeTrue("its artifacts are granted when collected");
        var r = StatRewards1115Tests.RoundTrip(c);
        r.ArtifactStatsApplied.Should().BeTrue("the flag survives a save");
        var before = Bases(r);
        GameEngine.RunStatRewardMigrations(r, Saved(ArtifactType.CreatorsEye, ArtifactType.Worldstone));
        Bases(r).Should().Equal(before);
    }

    // ---------------- helpers ----------------

    internal static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    /// <summary>The text of a method from its signature to the next member at the same indent.</summary>
    internal static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{signature} must exist");
        int lineStart = src.LastIndexOf('\n', start) + 1;
        string indent = src.Substring(lineStart, start - lineStart);
        var next = new Regex(@"\n" + indent + @"(private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    /// <summary>Serves the scripted bytes once, then reports end of input.</summary>
    internal sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
