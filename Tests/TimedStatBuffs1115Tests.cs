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
/// 1.2.0 lost stat rewards, piece 3: temporary stat buffs. A buff lives in Character.TimedStatBuffs
/// and is applied inside RecalculateStats, so it survives the fight-start recalc and a save round
/// trip. A Rest buff ends at the next rest (OnRest, from every rest entry point); a Combats buff
/// ends after its fights (counted down in CombatEngine.ConsumeCombatBuffs). A repeated buff of the
/// same source and stat refreshes, never stacks.
/// </summary>
[Collection("SharedGameSingletons")]
public class TimedStatBuffs1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static TerminalEmulator Term(string script) => Term(script, new MemoryStream());

    internal static TerminalEmulator Term(string script, MemoryStream output) =>
        new TerminalEmulator(new ScriptedStream(script + string.Concat(Enumerable.Repeat("\n", 40)), null), output);

    internal static T At<T>(T loc, Character p, TerminalEmulator term) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, p);
        return loc;
    }

    internal static async Task Call(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, F) ?? typeof(BaseLocation).GetMethod(method, F);
        m.Should().NotBeNull($"{method} must exist");
        try { await (Task)m!.Invoke(target, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    /// <summary>
    /// Starts a monster fight (its full RecalculateStats) and escapes with a smoke bomb. When
    /// <paramref name="duringFight"/> is given it runs at the fight's first input read, which comes
    /// after the fight-start recalc and before the fight ends.
    /// </summary>
    internal static async Task Fight(Character c, Action? duringFight = null)
    {
        c.SmokeBombs = 1;
        c.CombatSpeed = CombatSpeed.Instant;
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(string.Concat(Enumerable.Repeat("R\n", 8)), duringFight), new MemoryStream()));
        engine.SeedRandomForTests(1115);
        var dummy = new Monster { Name = "Training Dummy", Level = 1, HP = 5_000_000, MaxHP = 5_000_000, Strength = 0, Defence = 0, Experience = 1, Gold = 0 };
        await engine.PlayerVsMonsters(c, new List<Monster> { dummy });
    }

    // ---------------- applied in RecalculateStats ----------------

    [Fact]
    public async Task ARestBuff_SurvivesAFightStart_AndASaveRoundTrip_AndEndsAtRest()
    {
        var c = StatRewards1115Tests.Fresh("TsbRest");
        c.AddTimedStatBuff("tsb_test", StatKind.Charisma, 2, StatBuffEnd.Rest);
        c.Charisma.Should().Be(12, "the buff is applied at once");
        c.BaseCharisma.Should().Be(10, "a buff never touches the Base field");

        long seen = 0;
        await Fight(c, () => seen = c.Charisma);
        seen.Should().Be(12, "the buff is in force during the fight");
        c.Charisma.Should().Be(12, "a Rest buff survives a fight");
        c.TimedStatBuffs.Should().ContainSingle();

        var r = StatRewards1115Tests.RoundTrip(c);
        r.Charisma.Should().Be(12, "the buff survives a save round trip");
        r.TimedStatBuffs.Should().ContainSingle(b => b.Source == "tsb_test" && b.Stat == StatKind.Charisma && b.Amount == 2 && b.EndsOn == StatBuffEnd.Rest);

        r.OnRest();
        r.Charisma.Should().Be(10, "a rest ends the buff");
        r.TimedStatBuffs.Should().BeEmpty();
        r.RecalculateStats();
        r.Charisma.Should().Be(10);
    }

    [Fact]
    public void ABuff_SurvivesAnyRecalculation()
    {
        var c = StatRewards1115Tests.Fresh("TsbRecalc");
        c.AddTimedStatBuff("tsb_test", StatKind.Strength, 3, StatBuffEnd.Rest);
        c.RecalculateStats();
        c.RecalculateStats();
        c.Strength.Should().Be(13);
    }

    [Fact]
    public async Task ACombatsOneBuff_IsInForceDuringItsFight_AndGoneAfterIt()
    {
        var c = StatRewards1115Tests.Fresh("TsbCombat");
        c.AddTimedStatBuff("tsb_test", StatKind.Dexterity, 2, StatBuffEnd.Combats, 1);
        c.Dexterity.Should().Be(12);
        c.OnRest();
        c.Dexterity.Should().Be(12, "a rest does not end a fight-count buff");

        long seen = 0;
        await Fight(c, () => seen = c.Dexterity);
        seen.Should().Be(12, "the buff is in force for the whole next fight");
        c.Dexterity.Should().Be(10, "the buff ends with its fight");
        c.TimedStatBuffs.Should().BeEmpty();

        seen = 0;
        await Fight(c, () => seen = c.Dexterity);
        seen.Should().Be(10, "the fight after has no buff");
    }

    [Fact]
    public async Task ACombatsTwoBuff_LastsTwoFights()
    {
        var c = StatRewards1115Tests.Fresh("TsbCombat2");
        c.AddTimedStatBuff("tsb_test", StatKind.Agility, 2, StatBuffEnd.Combats, 2);
        await Fight(c);
        c.Agility.Should().Be(12);
        c.TimedStatBuffs.Single().CombatsLeft.Should().Be(1);
        var r = StatRewards1115Tests.RoundTrip(c);
        r.TimedStatBuffs.Single().CombatsLeft.Should().Be(1, "the countdown is saved");
        await Fight(r);
        r.Agility.Should().Be(10);
    }

    // ---------------- refresh, never stack ----------------

    [Fact]
    public void TheSameSourceAndStat_Refreshes_AndNeverStacks()
    {
        var c = StatRewards1115Tests.Fresh("TsbStack");
        c.AddTimedStatBuff("tsb_test", StatKind.Charisma, 2, StatBuffEnd.Rest);
        c.AddTimedStatBuff("tsb_test", StatKind.Charisma, 2, StatBuffEnd.Rest);
        c.AddTimedStatBuff("tsb_test", StatKind.Charisma, 2, StatBuffEnd.Rest);
        c.Charisma.Should().Be(12, "a repeated buff refreshes");
        c.TimedStatBuffs.Should().ContainSingle();

        c.AddTimedStatBuff("tsb_test", StatKind.Dexterity, 1, StatBuffEnd.Combats, 1);
        c.AddTimedStatBuff("tsb_test", StatKind.Dexterity, 1, StatBuffEnd.Combats, 3);
        c.TimedStatBuffs.Should().HaveCount(2, "another stat is its own buff");
        c.TimedStatBuffs.Single(b => b.Stat == StatKind.Dexterity).CombatsLeft.Should().Be(3, "a refresh takes the new duration");

        c.AddTimedStatBuff("tsb_other", StatKind.Charisma, 1, StatBuffEnd.Rest);
        c.Charisma.Should().Be(13, "another source adds its own buff");
    }

    // ---------------- floors and pools ----------------

    [Fact]
    public void ANegativeBuff_StopsAtTheFloor_AndLeavesBaseAlone()
    {
        var c = StatRewards1115Tests.Fresh("TsbFloor");
        c.GrantPermanentStat(StatKind.Wisdom, -18);   // Base 2
        c.AddTimedStatBuff("tsb_test", StatKind.Wisdom, -5, StatBuffEnd.Rest);
        c.Wisdom.Should().Be(1);
        c.BaseWisdom.Should().Be(2);
        c.OnRest();
        c.Wisdom.Should().Be(2);
    }

    [Fact]
    public void OnRest_KeepsAFullManaPoolFull()
    {
        var c = StatRewards1115Tests.Fresh("TsbPool", CharacterClass.Magician);
        c.AddTimedStatBuff("tsb_test", StatKind.Wisdom, -1, StatBuffEnd.Rest);
        long lowered = c.MaxMana;
        c.Mana = c.MaxMana;
        c.OnRest();
        c.MaxMana.Should().BeGreaterThanOrEqualTo(lowered);
        c.Mana.Should().Be(c.MaxMana, "a full pool stays full when a rest ends a buff");
    }

    [Fact]
    public void OnRest_WithNothingToEnd_ChangesNothing()
    {
        var c = StatRewards1115Tests.Fresh("TsbNoop");
        c.HP = 7;
        c.Strength = 99;   // a derived value no recalc would keep
        c.OnRest();
        c.Strength.Should().Be(99, "no recalc runs when no buff ends");
        c.HP.Should().Be(7);
    }

    // ---------------- NPCs ----------------

    [Fact]
    public void AnNpc_HasAnEmptyList_AndIsUnaffected()
    {
        var npc = new NPC { Name1 = "Tsb Npc", Name2 = "Tsb Npc", Level = 5, BaseStrength = 15, BaseDexterity = 12, BaseMaxHP = 80, BaseConstitution = 10 };
        npc.TimedStatBuffs.Should().BeEmpty();
        npc.RecalculateStats();
        npc.Strength.Should().Be(15);
        npc.Dexterity.Should().Be(12);
        npc.OnRest();
        npc.Strength.Should().Be(15);
        typeof(NPCData).GetProperty("TimedStatBuffs").Should().BeNull("NPC saves carry no timed buffs");
    }

    // ---------------- save sites ----------------

    [Fact]
    public void ThePvpSnapshot_CarriesTheOwnersBuffs()
    {
        var c = StatRewards1115Tests.Fresh("TsbSnap");
        c.AddTimedStatBuff("tsb_test", StatKind.Strength, 4, StatBuffEnd.Combats, 1);
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        var data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { c })!;
        var snap = PlayerCharacterLoader.CreateFromSaveData(data, "TsbSnap");
        snap.TimedStatBuffs.Should().ContainSingle();
        snap.Strength.Should().Be(c.Strength, "the snapshot fights with the owner's buff");
    }

    [Fact]
    public void TimedStatBuffData_DropsRowsWithUnknownValues()
    {
        var rows = new List<TimedStatBuffData>
        {
            new() { Source = "ok", Stat = (int)StatKind.Charisma, Amount = 2, EndsOn = (int)StatBuffEnd.Rest },
            new() { Source = "bad_stat", Stat = 99, Amount = 2, EndsOn = (int)StatBuffEnd.Rest },
            new() { Source = "bad_end", Stat = (int)StatKind.Charisma, Amount = 2, EndsOn = 7 },
        };
        TimedStatBuffData.ToBuffs(rows).Should().ContainSingle(b => b.Source == "ok");
        TimedStatBuffData.ToBuffs(null).Should().BeEmpty();
    }

    // ---------------- rest entry points, driven ----------------

    private static Character Buffed(string name)
    {
        var c = StatRewards1115Tests.Fresh(name);
        c.AddTimedStatBuff("tsb_test", StatKind.Charisma, 2, StatBuffEnd.Rest);
        c.Charisma.Should().Be(12);
        return c;
    }

    [Fact]
    public async Task Inn_RestAtTable_EndsTheRestBuffs()
    {
        var c = Buffed("TsbInnTable");
        await Call(At(new InnLocation(), c, Term("")), "RestAtTable");
        c.Charisma.Should().Be(10);
        c.TimedStatBuffs.Should().BeEmpty();
    }

    [Fact]
    public async Task Home_DoRest_EndsTheRestBuffs()
    {
        var c = Buffed("TsbHomeRest");
        c.HomeRestsToday = 0;
        await Call(At(new HomeLocation(), c, Term("")), "DoRest");
        c.Charisma.Should().Be(10);
        c.TimedStatBuffs.Should().BeEmpty();
    }

    [Fact]
    public async Task Dungeon_RestInRoom_EndsTheRestBuffs()
    {
        var c = Buffed("TsbCamp");
        await Call(At(new DungeonLocation(), c, Term("")), "RestInRoom");
        c.Charisma.Should().Be(10);
        c.TimedStatBuffs.Should().BeEmpty();
    }

    // ---------------- rest entry points, source ----------------

    [Theory]
    [InlineData("Scripts/Locations/InnLocation.cs", "private async Task RestAtTable(", "OnRest()")]
    [InlineData("Scripts/Locations/InnLocation.cs", "private async Task SleepAtInn(", "RestAndAdvanceToMorning(")]
    [InlineData("Scripts/Locations/InnLocation.cs", "private async Task RentRoom(", "OnRest()")]
    [InlineData("Scripts/Locations/CastleLocation.cs", "private async Task RoyalSleep(", "OnRest()")]
    [InlineData("Scripts/Locations/HomeLocation.cs", "private async Task DoRest(", "OnRest()")]
    [InlineData("Scripts/Locations/HomeLocation.cs", "private async Task SleepAtHomeOnline(", "OnRest()")]
    [InlineData("Scripts/Locations/HomeLocation.cs", "private async Task SleepAtHome(", "RestAndAdvanceToMorning(")]
    [InlineData("Scripts/Locations/DormitoryLocation.cs", "private async Task GoToSleep(", "OnRest()")]
    [InlineData("Scripts/Locations/DormitoryLocation.cs", "private async Task GoToSleepOnline(", "OnRest()")]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "private async Task RestInRoom(", "OnRest()")]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "private async Task RestSpotEncounter(", "RestAndAdvanceToMorning(")]
    [InlineData("Scripts/Systems/DailySystemManager.cs", "public async Task RestAndAdvanceToMorning(", "OnRest()")]
    public void EveryRestEntryPoint_EndsTheRestBuffs(string file, string signature, string call)
    {
        Body(Src(file), signature).Should().Contain(call, $"{signature} ends the rest buffs");
    }

    [Fact]
    public void MainStreet_QuitGame_BothSleepBranches_EndTheRestBuffs()
    {
        string body = Body(Src("Scripts/Locations/MainStreetLocation.cs"), "private async Task<bool> QuitGame(");
        int home = body.IndexOf("await backend.RegisterSleepingPlayer(username, \"home\"", StringComparison.Ordinal);
        int dorm = body.IndexOf("await dormBackend.RegisterSleepingPlayer(", StringComparison.Ordinal);
        home.Should().BeGreaterThan(0);
        dorm.Should().BeGreaterThan(home);
        body.Substring(0, home).Should().Contain("OnRest()", "the home sleep ends the rest buffs");
        body.Substring(home, dorm - home).Should().Contain("OnRest()", "the dormitory or street sleep ends the rest buffs");
    }

    [Fact]
    public void RestAndAdvanceToMorning_EndsTheBuffs_BeforeTheOnlineReturn()
    {
        string body = Body(Src("Scripts/Systems/DailySystemManager.cs"), "public async Task RestAndAdvanceToMorning(");
        body.IndexOf("OnRest()", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf("IsOnlineMode", StringComparison.Ordinal));
    }

    [Fact]
    public void ConsumeCombatBuffs_CountsDownTheStatBuffs()
    {
        Body(Src("Scripts/Systems/CombatEngine.cs"), "private static void ConsumeCombatBuffs(")
            .Should().Contain("TickTimedStatBuffsAfterCombat()");
    }

    // ---------------- helpers ----------------

    internal static string Src(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(dir!, relative));
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

    /// <summary>Serves the scripted bytes once, then reports end of input. Runs a probe at the first read.</summary>
    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        private Action? _probe;
        public ScriptedStream(string script, Action? probe) { _data = Encoding.UTF8.GetBytes(script); _probe = probe; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var p = _probe; _probe = null; p?.Invoke();
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }
}
