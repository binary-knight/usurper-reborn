using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
/// 1.2.0 lost stat rewards, piece 1 commit 2: the PERMANENT stat rewards in Locations (Temple,
/// Dark Alley, Dungeon, Wilderness, Church, town NPC story) go through GrantPermanentStat. Per
/// location group a real site is driven, and its reward survives the full RecalculateStats at the
/// start of a monster fight and a save round trip. A source scan covers every converted method.
/// The Church wedding is covered by the scan only (its path needs a courted NPC).
/// </summary>
[Collection("SharedGameSingletons")]
public class StatRewardsLocations1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static TerminalEmulator Term(string script) =>
        new TerminalEmulator(new ScriptedStream(script + string.Concat(Enumerable.Repeat("\n", 40))), new MemoryStream());

    private static T At<T>(T loc, Character p, TerminalEmulator term) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, p);
        return loc;
    }

    private static async Task Call(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, F) ?? typeof(BaseLocation).GetMethod(method, F);
        m.Should().NotBeNull($"{method} must exist");
        try { await (Task)m!.Invoke(target, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    /// <summary>Starts a monster fight (its full RecalculateStats) and escapes with a smoke bomb.</summary>
    private static async Task FightStart(Character c)
    {
        c.SmokeBombs = 1;
        c.CombatSpeed = CombatSpeed.Instant;
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(string.Concat(Enumerable.Repeat("R\n", 8))), new MemoryStream()));
        engine.SeedRandomForTests(1115);
        var dummy = new Monster { Name = "Training Dummy", Level = 1, HP = 5_000_000, MaxHP = 5_000_000, Strength = 0, Defence = 0, Experience = 1, Gold = 0 };
        await engine.PlayerVsMonsters(c, new List<Monster> { dummy });
    }

    /// <summary>The reward is in Base, and the derived value survives a fight start and a save round trip.</summary>
    private static async Task ShouldLast(Character c, StatKind stat, long expectedBase, string what)
    {
        c.GetBaseStat(stat).Should().Be(expectedBase, $"{what} is written to the Base field");
        long derived = StatRewards1115Tests.Derived(c, stat);
        await FightStart(c);
        StatRewards1115Tests.Derived(c, stat).Should().Be(derived, $"{what} survives the start of a monster fight");
        var r = StatRewards1115Tests.RoundTrip(c);
        r.GetBaseStat(stat).Should().Be(expectedBase);
        StatRewards1115Tests.Derived(r, stat).Should().Be(derived, $"{what} survives a save round trip");
    }

    private static readonly StatKind[] Attributes =
    {
        StatKind.Strength, StatKind.Dexterity, StatKind.Constitution, StatKind.Intelligence, StatKind.Wisdom,
        StatKind.Charisma, StatKind.Defence, StatKind.Stamina, StatKind.Agility
    };

    // ---------------- Temple ----------------

    [Fact]
    public async Task Temple_TheWeaponSacrificeBlessing_Lasts()
    {
        var c = StatRewards1115Tests.Fresh("SrlTemple");
        // A real weapon in hand (the sacrifice gives up the worn item since 1.2.0 piece 3).
        // Power 400: 0.3 + 400/500 >= 1, so the blessing always fires.
        var blade = new Equipment
        {
            Name = "SrlTemple Offering Blade", Slot = EquipmentSlot.MainHand, WeaponPower = 400, Value = 3000, MinLevel = 1,
            Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword
        };
        EquipmentDatabase.RegisterDynamic(blade);
        c.EquippedItems[EquipmentSlot.MainHand] = blade.Id;
        c.RecalculateStats();
        var temple = At(new TempleLocation(), c, Term("Y\n"));
        await Call(temple, "SacrificeEquippedItem", "Solarius", true, EquipmentSlot.MainHand);
        c.GetEquipment(EquipmentSlot.MainHand).Should().BeNull("the weapon is given up");
        c.BaseStrength.Should().BeInRange(12, 15, "the blessing is +2 to +5 Strength");
        await ShouldLast(c, StatKind.Strength, c.BaseStrength, "the Temple blessing");
    }

    // ---------------- Dark Alley ----------------

    [Fact]
    public async Task DarkAlley_Steroids_Last()
    {
        var c = StatRewards1115Tests.Fresh("SrlAlley");
        c.Gold = 1_000_000;
        c.SteroidShopPurchases = 0;
        var alley = At(new DarkAlleyLocation(), c, Term("Y\n"));
        await Call(alley, "VisitSteroidShop");
        c.SteroidShopPurchases.Should().Be(1, "the purchase went through");
        await ShouldLast(c, StatKind.Strength, 15, "the steroid Strength");
        await ShouldLast(c, StatKind.Stamina, 13, "the steroid Stamina");
    }

    // ---------------- Dungeon ----------------

    [Fact]
    public async Task Dungeon_TheOldGodAlliedWisdom_Lasts()
    {
        var c = StatRewards1115Tests.Fresh("SrlDungeon");
        var term = Term("");
        var dungeon = At(new DungeonLocation(), c, term);
        // Manwe skips the town return and the forced save that follow the other gods
        var result = new BossEncounterResult { Success = true, Outcome = BossOutcome.Allied, God = OldGodType.Manwe };
        await Call(dungeon, "HandleGodEncounterResult", result, c, term);
        await ShouldLast(c, StatKind.Wisdom, 22, "the Allied Wisdom");
    }

    // ---------------- Wilderness ----------------

    [Fact]
    public async Task Wilderness_TheShrineStat_Lasts()
    {
        var c = StatRewards1115Tests.Fresh("SrlWild");
        var baseBefore = Attributes.ToDictionary(s => s, s => c.GetBaseStat(s));
        var derivedBefore = Attributes.ToDictionary(s => s, s => StatRewards1115Tests.Derived(c, s));
        var wild = new WildernessLocation();
        StatKind? gained = null;
        // The shrine's stat outcome is a Random.Shared roll (25%); the other outcomes change no attribute
        for (int i = 0; i < 60 && gained == null; i++)
        {
            At(wild, c, Term("P\n"));
            await Call(wild, "ShrineEncounter", new WildernessRegion { Id = "srl", Name = "Srl" });
            gained = Attributes.Cast<StatKind?>().FirstOrDefault(s => StatRewards1115Tests.Derived(c, s!.Value) != derivedBefore[s!.Value]);
        }
        gained.Should().NotBeNull("the shrine stat outcome came up");
        var stat = gained!.Value;
        new[] { StatKind.Strength, StatKind.Dexterity, StatKind.Wisdom }.Should().Contain(stat);
        StatRewards1115Tests.Derived(c, stat).Should().Be(derivedBefore[stat] + 1);
        await ShouldLast(c, stat, baseBefore[stat] + 1, "the shrine stat");
    }

    // ---------------- Town NPC story (BaseLocation) ----------------

    [Fact]
    public async Task TownStory_TheStageReward_Lasts()
    {
        var c = StatRewards1115Tests.Fresh("SrlStory");
        var loc = At(new DarkAlleyLocation(), c, Term(""));
        var npc = new MemorableNPCData { Name = "Srl Teller", Title = "Test", Description = "Test", StoryStages = Array.Empty<NPCStoryStage>() };
        var stage = new NPCStoryStage { StageId = 0, Name = "Srl", Dialogue = new[] { "..." }, Reward = new NPCReward { Wisdom = 3, Dexterity = 2 } };
        await Call(loc, "DisplayTownNPCEncounter", npc, stage, "Srl_Unmapped_Key");
        await ShouldLast(c, StatKind.Wisdom, 23, "the story Wisdom");
        await ShouldLast(c, StatKind.Dexterity, 12, "the story Dexterity");
    }

    // ---------------- source scan: every converted method ----------------

    private static readonly Regex DerivedWrite = new(
        @"\b(currentPlayer|player)\.(Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma|Defence|Stamina|Agility|MaxHP|MaxMana)\s*(\+=|-=)");

    [Theory]
    [InlineData("Scripts/Locations/TempleLocation.cs", "private async Task SacrificeEquippedItem(")]
    [InlineData("Scripts/Locations/TempleLocation.cs", "private async Task VisitInnerSanctum(")]
    [InlineData("Scripts/Locations/DarkAlleyLocation.cs", "private async Task VisitSteroidShop(")]
    [InlineData("Scripts/Locations/DarkAlleyLocation.cs", "private async Task VisitAlchemistHeaven(")]
    [InlineData("Scripts/Locations/DarkAlleyLocation.cs", "private async Task HandleShadyEncounter(")]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "private async Task HandleGodEncounterResult(")]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "private async Task MysteriousShrine(")]
    [InlineData("Scripts/Locations/WildernessLocation.cs", "private async Task ShrineEncounter(")]
    [InlineData("Scripts/Locations/ChurchLocation.cs", "private async Task ProcessMarriageCeremony(")]
    [InlineData("Scripts/Locations/BaseLocation.cs", "private async Task DisplayTownNPCEncounter(")]
    public void EveryConvertedReward_UsesTheHelper_AndWritesNoDerivedStat(string file, string signature)
    {
        string body = Body(File.ReadAllText(Path.Combine(RepoRoot(), file)), signature);
        body.Should().Contain("GrantPermanentStat", $"{signature} grants through the helper");
        var hits = DerivedWrite.Matches(body).Select(m => m.Value).ToList();
        hits.Should().BeEmpty($"{signature} must not write a derived stat");
    }

    [Fact]
    public void TheSanctum_MapsEveryRoll_ToAGrant()
    {
        string body = Body(File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Locations/TempleLocation.cs")), "private async Task VisitInnerSanctum(");
        foreach (var s in new[] { "Strength", "Defence", "Stamina", "Agility", "Charisma", "Dexterity", "Wisdom", "Intelligence", "Constitution" })
            body.Should().Contain($"sanctumStat = StatKind.{s}; statName = \"{s}\";");
        body.Should().Contain("currentPlayer.GrantPermanentStat(sanctumStat, 1);");
    }

    // ---------------- helpers ----------------

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    /// <summary>The text of a method from its signature to the next member at the same indent.</summary>
    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{signature} must exist");
        int lineStart = src.LastIndexOf('\n', start) + 1;
        string indent = src.Substring(lineStart, start - lineStart);
        var next = new Regex(@"\n" + indent + @"(private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    /// <summary>Serves the scripted bytes once, then reports end of input.</summary>
    private sealed class ScriptedStream : Stream
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
