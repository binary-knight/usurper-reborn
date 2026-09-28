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
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods, boon recalculation fix. (1) A god boon change updates only the boons' share
/// of max HP and max mana (Character.RecalculateBoonShare), so stat gains written straight into
/// the derived stats (a Temple item-sacrifice blessing, the Sanctum's +1, Groggo's Dexterity)
/// survive a Temple entry, a Favor tier crossing and a worship change. (2) A change made by another
/// session (a player-god's reconfig, domain choice or recruit) only writes the caches and sets
/// GodBoonRecalcPending; the follower's own session applies it at the top of the location loop
/// and at the end of a fight.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodBoonRecalcFix1115Tests
{
    private static Character Hero(string name, CharacterClass cls = CharacterClass.Warrior) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 10, HP = 100, MaxHP = 100,
        BaseMaxHP = 100, BaseStrength = 10, BaseDexterity = 10, BaseConstitution = 10, BaseIntelligence = 20,
        BaseWisdom = 20, BaseCharisma = 10, BaseDefence = 5, BaseStamina = 10, BaseAgility = 10,
        BaseMaxMana = cls == CharacterClass.Magician ? 100 : 0,
        Mental = 80, Class = cls, Race = CharacterRace.Human
    };

    /// <summary>A character with no god and its stats from the pipeline.</summary>
    private static Character Fresh(string name, CharacterClass cls = CharacterClass.Warrior)
    {
        var c = Hero(name, cls);
        GodRegistry.SetWorshippedGod(c, null);
        c.RecalculateStats();
        c.HP = c.MaxHP;
        c.Mana = c.MaxMana;
        return c;
    }

    private record Gains(long Str, long Def, long Wis, long Dex, long Con);

    /// <summary>
    /// The gains the way their sites write them: the Temple weapon and armor sacrifice blessings
    /// (Strength and Defence, TempleLocation), the Sanctum's +1 (Wisdom here) and Groggo's
    /// shadow blessing (+3 Dexterity and its marker, DarkAlleyLocation).
    /// </summary>
    private static Gains GiveDerivedGains(Character c)
    {
        c.Strength += 4;
        c.Defence += 3;
        c.Wisdom += 1;
        c.GroggoShadowBlessingDex = 3;
        c.Dexterity += 3;
        return new Gains(c.Strength, c.Defence, c.Wisdom, c.Dexterity, c.Constitution);
    }

    private static void ShouldKeep(Character c, Gains g, string trigger)
    {
        c.Strength.Should().Be(g.Str, $"the Temple weapon blessing survives {trigger}");
        c.Defence.Should().Be(g.Def, $"the Temple armor blessing survives {trigger}");
        c.Wisdom.Should().Be(g.Wis, $"the Sanctum stat survives {trigger}");
        c.Dexterity.Should().Be(g.Dex, $"Groggo's Dexterity survives {trigger}");
        c.Constitution.Should().Be(g.Con);
    }

    private static long TerranAt(long plain, int strengthPct) =>
        plain + GodBoonSystem.Bonus(plain, GameConfig.GodBoonTerranMaxHpPct * strengthPct / 100.0);

    // ---------------- 1. The derived gains survive ----------------

    [Fact]
    public async Task DerivedGains_SurviveATempleEntry()
    {
        UsurperRemake.BBS.DoorMode.IsOnlineMode.Should().BeFalse();
        var c = Fresh("GbfTemple");
        try
        {
            GodRegistry.SetWorshippedGod(c, "Terran").Should().BeTrue();
            var g = GiveDerivedGains(c);
            await GodBoonSystem.RefreshPlayerGodBoonAsync(c);   // the Temple entry refresh
            ShouldKeep(c, g, "a Temple entry");
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    [Fact]
    public void DerivedGains_SurviveATierCrossing_AndMaxHpFollowsTheTier()
    {
        var c = Fresh("GbfTier");
        long plain = c.MaxHP;
        try
        {
            GodRegistry.SetWorshippedGod(c, "Terran").Should().BeTrue();
            var g = GiveDerivedGains(c);
            FavorSystem.Change(c, GameConfig.GodFavorTierZealotMin);
            c.MaxHP.Should().Be(TerranAt(plain, GameConfig.GodBoonZealotStrengthPct), "the tier's boon is on max HP at once");
            ShouldKeep(c, g, "a tier crossing");
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    [Fact]
    public void DerivedGains_SurviveAWorshipChange()
    {
        var c = Fresh("GbfWorship");
        long plain = c.MaxHP;
        try
        {
            var g = GiveDerivedGains(c);
            GodRegistry.SetWorshippedGod(c, "Terran").Should().BeTrue();
            c.MaxHP.Should().Be(TerranAt(plain, GameConfig.GodBoonFollowerStrengthPct));
            ShouldKeep(c, g, "choosing a god");
            GodRegistry.SetWorshippedGod(c, "Solarius").Should().BeTrue();
            ShouldKeep(c, g, "changing god");
            c.MaxHP.Should().Be(plain);
            GodRegistry.SetWorshippedGod(c, null).Should().BeTrue();
            ShouldKeep(c, g, "leaving the god");
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    [Fact]
    public void Parity_AFullRecalculation_AfterTheTargetedUpdate_GivesTheSameMaxHpAndMana()
    {
        var c = Fresh("GbfParity", CharacterClass.Magician);
        c.BonusMaxHP = 37;   // added after the boon segment, so the difference must keep it
        c.RecalculateStats();
        c.MaxMana.Should().BeGreaterThan(0);
        try
        {
            void Parity(string step)
            {
                long hp = c.MaxHP, mana = c.MaxMana;
                c.RecalculateStats();
                c.MaxHP.Should().Be(hp, $"max HP after {step}");
                c.MaxMana.Should().Be(mana, $"max mana after {step}");
            }

            GodRegistry.SetWorshippedGod(c, "Terran").Should().BeTrue();
            Parity("worshipping Terran");
            FavorSystem.Change(c, GameConfig.GodFavorTierZealotMin);
            Parity("a tier crossing");
            GodRegistry.SetWorshippedGod(c, "GbfParityGod").Should().BeTrue();
            GodBoonSystem.SetPlayerGodBoon(c, "GbfParityGod", GodDomain.Earth, 100);
            c.GodFavor = GameConfig.GodFavorTierZealotMin;
            GodBoonSystem.SetConfiguredBoons(c, "divine_vitality:3,mana_well:3");
            GodBoonSystem.ApplyPendingBoonRecalc(c);
            c.MaxMana.Should().BeGreaterThan(100, "the mana boon applies");
            Parity("a player-god's domain and configured boons");
            GodBoonSystem.SetConfiguredBoons(c, "divine_vitality:1");
            GodBoonSystem.ApplyPendingBoonRecalc(c);
            Parity("a reconfig");
            GodRegistry.SetWorshippedGod(c, null).Should().BeTrue();
            Parity("leaving the god");
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    // ---------------- 2. Another session's character ----------------

    [Fact]
    public void OtherSession_TheRecruitOnlySetsTheFlag_AndTheOwnSessionAppliesIt()
    {
        var c = Fresh("GbfOther");
        long plain = c.MaxHP;
        try
        {
            GodRegistry.SetWorshippedGod(c, "Terran", otherSession: true).Should().BeTrue();
            c.GodBoonRecalcPending.Should().BeTrue();
            c.MaxHP.Should().Be(plain, "no direct update on another session's character");
            c.HP -= 7;   // the player's own session takes a hit meanwhile
            GodBoonSystem.ApplyPendingBoonRecalc(c);
            c.GodBoonRecalcPending.Should().BeFalse();
            c.MaxHP.Should().Be(TerranAt(plain, GameConfig.GodBoonFollowerStrengthPct));
            c.HP.Should().Be(plain - 7, "the hit is kept");
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    [Fact]
    public void Wiring_TheCrossSessionPathsRequest_AndOnlyTheOwnPathsApply()
    {
        string gbs = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Systems/GodBoonSystem.cs"));
        foreach (var sig in new[] { "public static void SetConfiguredBoons(", "public static void ApplyDomainChange(" })
            Body(gbs, sig).Should().Contain("RequestRecalcForBoon(").And.NotContain("RecalculateForBoon(");
        Body(gbs, "public static void ApplyRecruit(").Should().NotContain("RecalculateForBoon(");
        Body(gbs, "public static void RecalculateForBoon(").Should().Contain("c.RecalculateBoonShare();").And.NotContain("RecalculateStats()");
    }

    [Fact]
    public void Wiring_TheLocationLoopAndTheFightEndsApplyThePendingUpdate()
    {
        string loc = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Locations/BaseLocation.cs"));
        Regex.IsMatch(Body(loc, "protected virtual async Task LocationLoop("),
                @"while \(!exitLocation && currentPlayer\.IsAlive\)[^\n]*\n\s*\{\s*(//[^\n]*\n\s*)*GodBoonSystem\.ApplyPendingBoonRecalc\(currentPlayer\);")
            .Should().BeTrue("the top of every location loop pass");
        string combat = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Systems/CombatEngine.cs"));
        Regex.IsMatch(Body(combat, "public async Task<CombatResult> PlayerVsMonsters("),
                @"finally\s*\{\s*ConsumeCombatBuffs\(player\);\s*GodBoonSystem\.ApplyPendingBoonRecalc\(player\);")
            .Should().BeTrue("the end of every fight, the fight's player only");
        Body(combat, "private void EndPvPCombat(").Should().Contain("GodBoonSystem.ApplyPendingBoonRecalc(attacker);")
            .And.NotContain("ApplyPendingBoonRecalc(defender)");
    }

    [Fact]
    public void Wiring_TheTempleAndPantheonLoopsApplyThePendingUpdate()
    {
        string temple = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Locations/TempleLocation.cs"));
        Regex.IsMatch(Body(temple, "public async Task<string> ProcessLocation("),
                @"while \(!exitLocation\)[^\n]*\n\s*\{\s*(//[^\n]*\n\s*)*GodBoonSystem\.ApplyPendingBoonRecalc\(currentPlayer\);")
            .Should().BeTrue("the top of every Temple loop pass, since the Temple never reaches BaseLocation.LocationLoop");

        string pantheon = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Locations/PantheonLocation.cs"));
        Regex.IsMatch(Body(pantheon, "private async Task RunPantheonLoop("),
                @"while \(!exitLoop\)[^\n]*\n\s*\{\s*(//[^\n]*\n\s*)*GodBoonSystem\.ApplyPendingBoonRecalc\(currentPlayer\);")
            .Should().BeTrue("the top of every Pantheon loop pass, since the Pantheon never reaches BaseLocation.LocationLoop");
    }

    [Fact]
    public async Task AFight_AppliesThePendingUpdate_AtItsEnd()
    {
        var c = Fresh("GbfFight");
        c.SmokeBombs = 1;   // a guaranteed escape
        c.CombatSpeed = CombatSpeed.Instant;
        try
        {
            GodRegistry.SetWorshippedGod(c, "Terran", otherSession: true).Should().BeTrue();
            c.GodBoonRecalcPending.Should().BeTrue();
            var output = new MemoryStream();
            var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(string.Concat(Enumerable.Repeat("R\n", 8))), output));
            engine.SeedRandomForTests(1115);
            var dummy = new Monster { Name = "Training Dummy", Level = 1, HP = 5_000_000, MaxHP = 5_000_000, Strength = 0, Defence = 0, Experience = 1, Gold = 0 };
            Exception? error = null;
            try { await engine.PlayerVsMonsters(c, new List<Monster> { dummy }); }
            catch (Exception ex) { error = ex; }
            error.Should().BeNull();
            c.GodBoonRecalcPending.Should().BeFalse("the fight's end applied the pending update");
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    [Fact]
    public async Task Pantheon_AppliesThePendingUpdate_AtTheTopOfItsLoop()
    {
        var c = Fresh("GbfPantheon");
        long plain = c.MaxHP;
        try
        {
            GodRegistry.SetWorshippedGod(c, "Terran", otherSession: true).Should().BeTrue();
            c.GodBoonRecalcPending.Should().BeTrue();

            var pantheon = new PantheonLocation();
            var output = new MemoryStream();
            var term = new TerminalEmulator(new LineStream(new[] { "Q" }), output);
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(pantheon, term);
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(pantheon, c);

            var loop = typeof(PantheonLocation).GetMethod("RunPantheonLoop", F)!;
            var ex = await Assert.ThrowsAsync<LocationExitException>(() => (Task)loop.Invoke(pantheon, null)!);
            ex.DestinationLocation.Should().Be(GameLocation.NoWhere);

            c.GodBoonRecalcPending.Should().BeFalse("the top of the Pantheon loop applied it before Q was read");
            c.MaxHP.Should().Be(TerranAt(plain, GameConfig.GodBoonFollowerStrengthPct));
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
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
        var next = new Regex(@"\n    (private|public|internal|protected) ").Match(src, start + signature.Length);
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
