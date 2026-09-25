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
using UsurperRemake.Locations;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>A Random that always rolls its lowest value, so every chance roll passes.</summary>
internal sealed class LowRandom : Random
{
    public override int Next() => 0;
    public override int Next(int maxValue) => 0;
    public override int Next(int minValue, int maxValue) => minValue;
    public override double NextDouble() => 0.0;
}

/// <summary>A Random that always rolls its highest value, so every chance roll fails.</summary>
internal sealed class HighRandom : Random
{
    public override int Next() => int.MaxValue - 1;
    public override int Next(int maxValue) => Math.Max(0, maxValue - 1);
    public override int Next(int minValue, int maxValue) => Math.Max(minValue, maxValue - 1);
    public override double NextDouble() => 0.999999;
}

/// <summary>
/// v1.1.13 playtest 2: floor difficulty and monster holds on the player.
/// </summary>
[Collection("SharedGameSingletons")]
public class FloorAndStun1113Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public void EarlyFloorTable_RampsThroughFloorSix_FullStrengthAtSeven()
    {
        GameConfig.GetEarlyFloorMonsterStatMultiplier(1).Should().Be(0.5f);
        GameConfig.GetEarlyFloorMonsterStatMultiplier(2).Should().Be(0.65f);
        GameConfig.GetEarlyFloorMonsterStatMultiplier(3).Should().Be(0.8f);
        GameConfig.GetEarlyFloorMonsterStatMultiplier(4).Should().Be(0.85f);
        GameConfig.GetEarlyFloorMonsterStatMultiplier(5).Should().Be(0.9f);
        GameConfig.GetEarlyFloorMonsterStatMultiplier(6).Should().Be(0.95f);
        GameConfig.GetEarlyFloorMonsterStatMultiplier(7).Should().Be(1.0f);
        GameConfig.GetEarlyFloorMonsterStatMultiplier(40).Should().Be(1.0f);
        GameConfig.EarlyFloorMonsterStatMultipliers.Length.Should().Be(GameConfig.EarlyFloorSofteningMaxFloor + 1);
    }

    [Fact]
    public void MonsterGenerator_ReadsTheTable()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "MonsterGenerator.cs"));
        src.Should().Contain("level <= GameConfig.EarlyFloorSofteningMaxFloor");
        src.Should().Contain("GameConfig.GetEarlyFloorMonsterStatMultiplier(level)");
    }

    private static (CombatEngine engine, Character hero, Monster spider, Func<bool> web) SpiderFight()
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new LowRandom());
        var hero = new Character { Name1 = "Hero", Name2 = "Hero", Class = CharacterClass.Warrior, Level = 6, HP = 300, MaxHP = 300 };
        var spider = new Monster { Name = "Giant Spider", Level = 5, HP = 200, MaxHP = 200, Strength = 20 };
        spider.SpecialAbilities = new List<string> { "WebTrap" };
        var m = typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", F)!;
        var result = new CombatResult { Player = hero };
        bool Web()
        {
            bool held = hero.HasStatus(StatusEffect.Stunned);
            ((Task<bool>)m.Invoke(engine, new object?[] { spider, hero, result, null })!).GetAwaiter().GetResult();
            return !held && hero.HasStatus(StatusEffect.Stunned);
        }
        return (engine, hero, spider, Web);
    }

    private static void EndRound(CombatEngine engine, Character c) { c.ProcessStatusEffects(); engine.TickPvPControl(c); }

    [Fact]
    public void Spider_CannotReWebThePlayer_InTheImmunityWindow()
    {
        var (engine, hero, _, web) = SpiderFight();
        web().Should().BeTrue("the first web lands");
        EndRound(engine, hero);
        hero.HasStatus(StatusEffect.Stunned).Should().BeTrue();
        hero.ActiveStatuses[StatusEffect.Stunned] = 1;
        web();
        hero.ActiveStatuses[StatusEffect.Stunned].Should().Be(1, "a new web does not reset the clock while one holds");
        EndRound(engine, hero);                       // the web runs out; immunity starts
        hero.HasStatus(StatusEffect.Stunned).Should().BeFalse();
        for (int r = 0; r < GameConfig.StunImmunityRoundsAfterRecovery; r++)
        {
            web().Should().BeFalse($"immune round {r + 1}");
            EndRound(engine, hero);
        }
        web().Should().BeTrue("the immunity has run out");
    }

    [Fact]
    public void Player_ControlState_IsClearedAtTheStartOfEachFight()
    {
        // v1.1.13: comments stripped, so a commented-out line fails the check as a deleted one does
        var src = CodeOnly(File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs")));
        int start = src.IndexOf("public async Task<CombatResult> PlayerVsMonsters(");
        int loop = src.IndexOf("int roundNumber = 0;", start);
        start.Should().BeGreaterThan(0);
        loop.Should().BeGreaterThan(start);
        src.Substring(start, loop - start).Should().Contain("_pvpControl.Clear();");
        src.Substring(loop, 12000).Should().Contain("TickPvPControl(player);");
    }

    private static string CodeOnly(string src) =>
        string.Join("\n", src.Split('\n').Select(line =>
        {
            int c = line.IndexOf("//", StringComparison.Ordinal);
            return c >= 0 ? line.Substring(0, c) : line;
        }));
}

/// <summary>v1.1.13 playtest 2: the auto-combat healing threshold preference.</summary>
[Collection("SharedGameSingletons")]
public class AutoCombatHeal1113Tests
{
    private static Character Hero(long hp, int potions = 3) =>
        new Character { Name1 = "hero", Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
                        Level = 5, HP = hp, MaxHP = 100, BaseMaxHP = 100, BaseStrength = 20, BaseDefence = 10, Healing = potions };

    [Fact]
    public void Default_IsFiftyPercent()
    {
        new Character().AutoCombatHealPercent.Should().Be(50);
        new PlayerData().AutoCombatHealPercent.Should().Be(50, "an old save without the field keeps today's behaviour");
        System.Text.Json.JsonSerializer.Deserialize<PlayerData>("{}")!.AutoCombatHealPercent.Should().Be(50);
    }

    [Theory]
    [InlineData(20, 21, false)]
    [InlineData(20, 20, true)]
    [InlineData(50, 51, false)]
    [InlineData(50, 50, true)]
    [InlineData(70, 70, true)]
    [InlineData(70, 71, false)]
    public void Threshold_IsRespected(int percent, long hp, bool heals)
    {
        var p = Hero(hp);
        p.AutoCombatHealPercent = percent;
        CombatEngine.ShouldAutoCombatHeal(p).Should().Be(heals);
    }

    [Fact]
    public void NoPotions_NoHeal()
    {
        var p = Hero(5, potions: 0);
        CombatEngine.ShouldAutoCombatHeal(p).Should().BeFalse();
    }

    [Fact]
    public void Cycle_StepsByTen_AndWraps()
    {
        GameConfig.NextAutoCombatHealPercent(50).Should().Be(60);
        GameConfig.NextAutoCombatHealPercent(70).Should().Be(20);
        GameConfig.ClampAutoCombatHealPercent(5).Should().Be(20);
        GameConfig.ClampAutoCombatHealPercent(99).Should().Be(70);
        GameConfig.ClampAutoCombatHealPercent(44).Should().Be(40);
    }

    [Fact]
    public void Threshold_RoundTripsThroughTheSave()
    {
        var p = Hero(100);
        p.AutoCombatHealPercent = 30;
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { p })!;
        data.AutoCombatHealPercent.Should().Be(30);
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        var back = System.Text.Json.JsonSerializer.Deserialize<PlayerData>(json)!;
        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Character restored;
        try { restored = (Character)restore.Invoke(GameEngine.Instance, new object[] { back })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        restored.AutoCombatHealPercent.Should().Be(30);
    }

    [Fact]
    public void AutoCombat_UsesThePreference_NotALiteral()
    {
        var src = File.ReadAllText(Path.Combine(FloorAndStun1113Tests.RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().Contain("else if (ShouldAutoCombatHeal(player))");
        src.Should().NotContain("player.HP < player.MaxHP * 0.5 && player.Healing > 0");
    }
}

/// <summary>
/// v1.1.13 playtest 2: the victory screen's XP lines. "Experience gained" is what was added; the world event
/// line says it is already inside that number; one line shows the other multipliers; the share line's
/// percentage of its pot is the amount gained.
/// </summary>
[Collection("SharedGameSingletons")]
public class VictoryXPDisplay1113Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    private static Character Hero(int level, int training) => new Character
    {
        Name1 = "xpshow", Name2 = "XPShow", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = 5000, MaxHP = 5000, Experience = 0, Gold = 0, AutoLevelUp = false, CombatSpeed = CombatSpeed.Instant, MKills = 100,
        Team = "X", HQLevelsTeam = training > 0 ? "X" : "", HQTrainingLevel = training,
    };

    private static async Task<(CombatResult result, string shown)> Win(string method, int level, int training, bool withMate)
    {
        var hero = Hero(level, training);
        var monster = new Monster { Name = "Display Dummy", Level = level, HP = 0, MaxHP = 100, Experience = 200_000, Gold = 10 };
        var result = new CombatResult { Player = hero, Outcome = CombatOutcome.Victory };
        result.DefeatedMonsters.Add(monster);
        if (withMate)
            result.Teammates = new List<Character> { new NPC { Name1 = "mate", Name2 = "Mate", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level, HP = 3000, MaxHP = 3000 } };
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("P\n", 40)))), output);
        var engine = new CombatEngine(term);
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        await (Task)typeof(CombatEngine).GetMethod(method, F)!.Invoke(engine, new object[] { result, false })!;
        return (result, Plain(term, output));
    }

    private static string GainedLine(string method, long xp) =>
        method == "HandlePartialVictory" ? Loc.Get("combat.experience_gained", xp) : Loc.Get("combat.xp_label", xp.ToString());

    [Theory]
    [InlineData("HandleVictoryMultiMonster")]
    [InlineData("HandlePartialVictory")]
    public async Task TheGainedLine_IsTheXPAdded_AndTheShareLineAddsUpToIt(string method)
    {
        var (result, shown) = await Win(method, 40, training: 2, withMate: true);
        result.ExperienceGained.Should().BeGreaterThan(0);
        shown.Should().Contain(GainedLine(method, result.ExperienceGained));
        var share = Regex.Match(shown, Regex.Escape(Loc.Get("combat.xp_share", "§", "¤")).Replace("§", @"(\d+)").Replace("¤", @"(\d+)"));
        share.Success.Should().BeTrue("a teammate took part of the pot: " + shown);
        long pct = long.Parse(share.Groups[1].Value), pot = long.Parse(share.Groups[2].Value);
        ((double)pot * pct / 100.0).Should().BeApproximately(result.ExperienceGained, 2.0, "the share line's percentage of its pot is the amount gained");
        shown.Should().Contain(Loc.Get("combat.xp_mod.team_hq"), "the Training Grounds bonus is named in the other-modifiers line");
        int gained = shown.IndexOf(GainedLine(method, result.ExperienceGained), StringComparison.Ordinal);
        shown.IndexOf(Loc.Get("combat.xp_mod.team_hq"), StringComparison.Ordinal).Should().BeGreaterThan(gained);
    }

    [Theory]
    [InlineData("HandleVictoryMultiMonster")]
    [InlineData("HandlePartialVictory")]
    public async Task EarlyLevels_ShowTheOtherModifiersLine(string method)
    {
        var (result, shown) = await Win(method, 5, training: 0, withMate: false);
        double early = GameConfig.GetEarlyGameXPMultiplier(5);
        early.Should().BeGreaterThan(1.0);
        shown.Should().Contain(GainedLine(method, result.ExperienceGained));
        shown.Should().Contain(Loc.Get("combat.xp_mod.early_game"));
        shown.Should().NotContain(Loc.Get("combat.xp_share", "100", ""), "no teammate, no share line");
    }

    [Fact]
    public async Task NoOtherModifiers_NoLine()
    {
        var (_, shown) = await Win("HandleVictoryMultiMonster", 40, training: 0, withMate: false);
        var other = Loc.Get("combat.xp_other_modifiers", "§", "¤");
        shown.Should().NotContain(other.Substring(0, other.IndexOf('§')), "the combined effect is 1.0 at level 40 on Normal");
    }

    [Theory]
    [InlineData("HandleVictoryMultiMonster")]
    [InlineData("HandlePartialVictory")]
    public async Task WorldEventLine_SaysItIsIncluded(string method)
    {
        WorldEventSystem.Instance.ClearAllEvents();
        try
        {
            WorldEventSystem.Instance.ForceEvent(WorldEventSystem.EventType.KingWarDeclaration, 1);
            var (result, shown) = await Win(method, 40, training: 0, withMate: false);
            shown.Should().Contain(GainedLine(method, result.ExperienceGained));
            var line = Regex.Match(shown, Regex.Escape(Loc.Get("combat.world_event_xp", "§")).Replace("§", @"(\d+)"));
            line.Success.Should().BeTrue(shown);
            long.Parse(line.Groups[1].Value).Should().BeLessThan(result.ExperienceGained, "the event's share is part of the total, not added to it");
        }
        finally { WorldEventSystem.Instance.ClearAllEvents(); }
    }

    [Fact]
    public void WorldEventWording_SaysIncluded_InEveryLanguage()
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var json = File.ReadAllText(Path.Combine(FloorAndStun1113Tests.RepoRoot(), "Localization", lang + ".json"));
            var en = Regex.Match(json, "\"combat.world_event_xp\": \"([^\"]*)\"").Groups[1].Value;
            en.Should().NotBeEmpty();
            en.Should().NotBe(lang == "en" ? "(World event bonus: +{0} XP)" : "x", "the old wording read as an addition");
        }
        Loc.Get("combat.world_event_xp", "5").Should().Contain("included");
    }

    [Fact]
    public void Tally_MultipliesTheSteps_AndNamesThem()
    {
        var t = new CombatEngine.XPModifierTally();
        long x = t.Note(1000, 1500, "difficulty");
        x = t.Note(x, x, "guild");
        x = t.Note(x, 3000, "early_game");
        t.Multiplier.Should().BeApproximately(3.0, 1e-9);
        t.Sources.Should().Equal("difficulty", "early_game");
    }
}

/// <summary>A Random that hands out the given values in turn (clamped to the asked range), then its lowest.</summary>
internal sealed class ScriptRandom : Random
{
    private readonly Queue<int> _values;
    public ScriptRandom(params int[] values) { _values = new Queue<int>(values); }
    private int Take(int min, int max) => _values.Count > 0 ? Math.Clamp(_values.Dequeue(), min, Math.Max(min, max - 1)) : min;
    public override int Next() => Take(0, int.MaxValue);
    public override int Next(int maxValue) => Take(0, maxValue);
    public override int Next(int minValue, int maxValue) => Take(minValue, maxValue);
    public override double NextDouble() => 0.0;
}

/// <summary>v1.1.13 playtest 2: a command typed at a pause in a line-based mode is the next command.</summary>
[Collection("SharedGameSingletons")]
public class PauseTypeahead1113Tests
{
    private static (TerminalEmulator term, MemoryStream output) Stream(params string[] lines)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new LineStream(lines), output), output);
    }

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    [Fact]
    public async Task AttackTypedAtAPause_IsTheNextCommand()
    {
        var (term, _) = Stream("attack", "x");
        await term.PressAnyKey();
        (await term.GetInput("> ")).Should().Be("attack", "the line typed at the pause is not thrown away");
        (await term.GetInput("> ")).Should().Be("x");
    }

    [Fact]
    public async Task EnterAtAPause_KeepsNothing()
    {
        var (term, _) = Stream("", "x");
        await term.PressAnyKey();
        term.HasPendingLine.Should().BeFalse();
        (await term.GetInput("> ")).Should().Be("x");
    }

    [Fact]
    public async Task ASecondPause_StillWaitsForEnter_AndKeepsTheCommand()
    {
        var (term, _) = Stream("attack", "", "x");
        await term.PressAnyKey();
        await term.PressAnyKey();                     // reads the "" line; the kept command stays kept
        (await term.GetInput("> ")).Should().Be("attack");
        (await term.GetInput("> ")).Should().Be("x");
    }

    [Fact]
    public async Task AKeyRead_TakesTheKeptLinesFirstKey()
    {
        var (term, _) = Stream("attack");
        await term.PressAnyKey();
        (await term.GetKeyInput()).Should().Be("a");
    }

    [Fact]
    public async Task AFlush_DiscardsTheKeptLine()
    {
        var (term, _) = Stream("attack", "x");
        await term.PressAnyKey();
        term.FlushPendingInput();
        (await term.GetInput("> ")).Should().Be("x");
    }

    [Fact]
    public async Task StreamPrompt_AsksForEnter_NotAnyKey()
    {
        var (term, output) = Stream("");
        await term.PressAnyKey();
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("ui.press_enter"));
        shown.Should().NotContain(Loc.Get("ui.press_any_key"));
    }

    [Fact]
    public void LocalAndStdioPaths_KeepAnyKey()
    {
        var src = File.ReadAllText(Path.Combine(FloorAndStun1113Tests.RepoRoot(), "Scripts", "UI", "TerminalEmulator.cs"));
        int start = src.IndexOf("public async Task PressAnyKey(");
        string body = src.Substring(start, src.IndexOf("private async Task PauseForLine", start) - start);
        body.Should().Contain("Loc.Get(\"ui.press_enter\")");
        body.Should().Contain("Loc.Get(\"ui.press_any_key\")");
        body.IndexOf("ui.press_enter").Should().BeLessThan(body.IndexOf("ui.press_any_key"), "the line-based branch returns before the single-key one");
        body.Should().Contain("if (IsLineBasedPause)");
        src.Should().Contain("private bool IsLineBasedPause => (_streamWriter != null && _streamReader != null) || ShouldUseBBSAdapter();",
            "the BBS socket path reads a line too");
    }

    [Fact]
    public async Task TheOtherPauses_KeepTheCommandToo()
    {
        var (term, _) = Stream("attack", "look");
        await term.WaitForKeyPress();
        (await term.GetInput("> ")).Should().Be("attack");
        var (term2, _) = Stream("look");
        await term2.ReadKeyAsync();                   // every caller uses this as a pause
        (await term2.GetInput("> ")).Should().Be("look");

        // the BBS socket reads a real single key in ReadKeyAsync; only stream mode keeps a line there
        var src = File.ReadAllText(Path.Combine(FloorAndStun1113Tests.RepoRoot(), "Scripts", "UI", "TerminalEmulator.cs"));
        int start = src.IndexOf("public async Task<string> ReadKeyAsync()");
        string body = src.Substring(start, src.IndexOf("return await GetKeyInput();", start) - start);
        body.Should().NotContain("IsLineBasedPause").And.NotContain("ShouldUseBBSAdapter");
    }
}

/// <summary>v1.1.13 playtest 2: the player's status line.</summary>
[Collection("SharedGameSingletons")]
public class StatusLine1113Tests
{
    private static Character Hero(int level = 20) => new Character { Name1 = "st", Name2 = "St", Class = CharacterClass.Warrior, Level = level, HP = 400, MaxHP = 400 };

    private static List<string> Entries(Character p) => CombatEngine.BuildPlayerStatusEntries(p).Select(e => e.text).ToList();

    [Fact]
    public void Poison_And_Bleed_ShowTurnsAndDamagePerTurn()
    {
        var p = Hero(20);
        p.ApplyStatus(StatusEffect.Poisoned, 3);
        p.ApplyStatus(StatusEffect.Bleeding, 2);
        var e = Entries(p);
        e.Should().Contain(Loc.Get("combat.status_dot", "PSN", 3, "4-7"));   // 2-5 + 20/10
        e.Should().Contain(Loc.Get("combat.status_dot", "BLD", 2, "5-10"));  // 1-6 + 20/5
    }

    [Fact]
    public void Stun_ShowsTheTurnsActuallyLost()
    {
        var p = Hero();
        p.ApplyStatus(StatusEffect.Stunned, 2);
        Entries(p).Should().Contain(Loc.Get("combat.status_hold", "STN", 1), "a 2 at the top of the round ticks to 1 before the turn: one turn lost");
        p.ProcessStatusEffects();
        p.CanAct().Should().BeFalse("the one turn is lost");
        p.ProcessStatusEffects();
        p.CanAct().Should().BeTrue("and only one");
    }

    [Theory]
    [InlineData(StatusEffect.Poisoned)]
    [InlineData(StatusEffect.Bleeding)]
    public void DamageRange_MatchesTheTick(StatusEffect status)
    {
        var (min, max) = Character.StatusDamagePerTurn(status, 20)!.Value;
        int seenMin = int.MaxValue, seenMax = 0;
        for (int i = 0; i < 400; i++)
        {
            var p = Hero(20);
            p.ApplyStatus(status, 5);
            long before = p.HP;
            p.ProcessStatusEffects();
            int dmg = (int)(before - p.HP);
            seenMin = Math.Min(seenMin, dmg); seenMax = Math.Max(seenMax, dmg);
        }
        (seenMin, seenMax).Should().Be((min, max));
    }

    [Fact]
    public void Rescues_ShowWhetherTheyCanStillFire()
    {
        var p = Hero();
        p.HP = 400; p.CaptureRoundStartHP();
        var e = Entries(p);
        e.Should().Contain(Loc.Get("combat.rescue_last_stand_ready"));
        e.Should().Contain(Loc.Get("combat.rescue_deaths_door_ready"));

        p.HP = 150; p.CaptureRoundStartHP();              // 37%: Last Stand off, Death's Door on
        e = Entries(p);
        e.Should().Contain(Loc.Get("combat.rescue_last_stand_low"));
        e.Should().Contain(Loc.Get("combat.rescue_deaths_door_ready"));

        p.DeathsDoorUsedThisCombat = true;
        Entries(p).Should().Contain(Loc.Get("combat.rescue_deaths_door_spent"));
    }
}

/// <summary>v1.1.13 playtest 2: level-up guidance and /train.</summary>
[Collection("SharedGameSingletons")]
public class TrainCommand1113Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void Train_TravelsFromMainStreet_Only()
    {
        BaseLocation.CanTravelToLevelMaster(GameLocation.MainStreet).Should().BeTrue();
        BaseLocation.CanTravelToLevelMaster(GameLocation.TheInn).Should().BeFalse();
        BaseLocation.CanTravelToLevelMaster(GameLocation.Dungeons).Should().BeFalse();
        BaseLocation.CanTravelToLevelMaster(GameLocation.Master).Should().BeFalse();
    }

    private static (T loc, TerminalEmulator term, MemoryStream output) Rig<T>(T loc) where T : BaseLocation
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "", "", "" }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, new Character { Name1 = "t", Name2 = "T", Level = 10, TrainingPoints = 5 });
        return (loc, term, output);
    }

    private static Task<(bool, bool)> Slash(BaseLocation loc, string cmd) =>
        (Task<(bool, bool)>)typeof(BaseLocation).GetMethod("ProcessSlashCommand", F)!.Invoke(loc, new object[] { cmd })!;

    [Fact]
    public async Task Train_FromMainStreet_GoesToTheLevelMaster()
    {
        var (street, _, _) = Rig(new MainStreetLocation());
        var ex = await Assert.ThrowsAsync<LocationExitException>(() => Slash(street, "train"));
        ex.DestinationLocation.Should().Be(GameLocation.Master);
    }

    [Fact]
    public async Task Train_FromTheInn_SaysHow()
    {
        var (inn, term, output) = Rig(new InnLocation());
        (await Slash(inn, "train")).Should().Be((true, false));
        term.StreamWriterInternal?.Flush();
        Encoding.UTF8.GetString(output.ToArray()).Should().Contain(Loc.Get("base.train_how"));
    }

    [Theory]
    [InlineData(10, 10, 3, true)]
    [InlineData(9, 11, 3, true)]
    [InlineData(10, 10, 0, false)]
    [InlineData(11, 12, 3, false)]
    [InlineData(8, 9, 3, false)]
    public void LevelTenMilestone(int from, int to, int points, bool shown) =>
        BaseLocation.ReachedLevelTenWithPoints(from, to, points).Should().Be(shown);

    [Fact]
    public void Hints_And_Help_MentionTrain()
    {
        Loc.Get("base.training_points_hint").Should().Contain("/train");
        Loc.Get("base.level_ten_milestone", 5).Should().Contain("/train");
        var src = File.ReadAllText(Path.Combine(FloorAndStun1113Tests.RepoRoot(), "Scripts", "Locations", "BaseLocation.cs"));
        Regex.Matches(src, "base.help_train").Count.Should().Be(2, "the boxed and the plain help both list it");
        src.Should().Contain("ReachedLevelTenWithPoints(fromLevel, currentPlayer.Level, currentPlayer.TrainingPoints)");
    }
}

/// <summary>v1.1.13 playtest 2: invalid choices ask again; room events wait for a valid choice; chest search.</summary>
[Collection("SharedGameSingletons")]
public class InvalidChoice1113Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static (DungeonLocation d, TerminalEmulator term, MemoryStream output, Character hero) Dungeon(Random rnd, params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        var hero = new Character { Name1 = "inv", Name2 = "Inv", Class = CharacterClass.Warrior, Level = 8, HP = 300, MaxHP = 300, Gold = 1000, AI = CharacterAI.Human, Dexterity = 60, Wisdom = 60 };
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 5);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, rnd);
        return (d, term, output, hero);
    }

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    private static Task Run(object o, string method, params object[] args) => (Task)o.GetType().GetMethod(method, F)!.Invoke(o, args)!;

    [Fact]
    public async Task Helper_AsksAgain_ThenTakesTheValidAnswer()
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "x", "l" }), output);
        (await term.GetValidChoice("? ", new[] { "O", "L" }, "L")).Should().Be("L");
        term.StreamWriterInternal?.Flush();
        Encoding.UTF8.GetString(output.ToArray()).Should().Contain(Loc.Get("ui.invalid_choice_choose", "O, L"));
    }

    [Fact]
    public async Task Helper_FallsBack_AfterThreeInvalidAnswers()
    {
        var term = new TerminalEmulator(new LineStream(new[] { "q", "q", "q", "O" }), new MemoryStream());
        (await term.GetValidChoice("? ", new[] { "O", "L" }, "L")).Should().Be("L", "a dead or confused peer cannot spin the prompt");
    }

    [Fact]
    public async Task ExamineMenu_AStaleNumber_AsksAgain()
    {
        var (d, term, output, _) = Dungeon(new LowRandom(), "3", "0");
        var room = new DungeonRoom { Id = "r1" };
        room.Features.Add(new RoomFeature("Old Altar", "An altar.", FeatureInteraction.Examine));
        room.Features.Add(new RoomFeature("Bones", "Some bones.", FeatureInteraction.Search));
        await Run(d, "ExamineFeatures", room);
        Plain(term, output).Should().Contain(Loc.Get("ui.invalid_choice_choose", Loc.Get("ui.choice_range_or_zero", "1-2")));
        room.Features.Should().OnlyContain(f => !f.IsInteracted, "0 cancelled; nothing was examined");
    }

    [Fact]
    public async Task Chest_ATypo_AsksAgain_AndTheEventIsSpentByTheChoice()
    {
        var (d, term, output, _) = Dungeon(new LowRandom(), "x", "L");
        var room = new DungeonRoom { Id = "r1", HasEvent = true, EventType = DungeonEventType.TreasureChest };
        await Run(d, "HandleRoomEvent", room);
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("ui.invalid_choice_choose", "O, S, L"));
        shown.Should().Contain(Loc.Get("dungeon.chest_leave_alone"));
        room.EventCompleted.Should().BeTrue("leaving is a valid choice");
    }

    [Fact]
    public async Task Chest_NotSpent_WhenTheConnectionDropsAtThePrompt()
    {
        var (d, _, _, _) = Dungeon(new LowRandom());   // no lines: the stream ends at the prompt
        var room = new DungeonRoom { Id = "r1", HasEvent = true, EventType = DungeonEventType.TreasureChest };
        Func<Task> act = () => Run(d, "HandleRoomEvent", room);
        await act.Should().ThrowAsync<IOException>();
        room.EventCompleted.Should().BeFalse("no valid choice was made");
    }

    [Fact]
    public async Task Chest_Search_FindsAndDisarmsATrap()
    {
        // chest roll 7 = trapped; search roll 0 = found
        var (d, term, output, hero) = Dungeon(new ScriptRandom(7, 0), "S", "L");
        await Run(d, "TreasureChestEncounter");
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("dungeon.chest_search_disarmed"));
        shown.Should().Contain(Loc.Get("dungeon.chest_leave_alone"));
        shown.Should().NotContain(Loc.Get("ui.invalid_choice_choose", "O, L"));
        hero.HP.Should().Be(300);
    }

    [Fact]
    public void Chest_Search_Outcomes()
    {
        var (d, _, _, hero) = Dungeon(new LowRandom());
        var m = typeof(DungeonLocation).GetMethod("SearchChestForTraps", F)!;
        int Search(int roll) => (int)m.Invoke(d, new object[] { hero, roll })!;
        Search(7).Should().Be(0, "a found trap is disarmed; the chest is safe to open");
        Search(8).Should().Be(0);
        Search(9).Should().Be(9, "a mimic is revealed, not removed");
        Search(3).Should().Be(3);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, new HighRandom());
        Search(7).Should().Be(7, "a missed search leaves the trap");
    }

    [Fact]
    public void Chest_SearchChance_FollowsTheStats()
    {
        var clumsy = new Character { Class = CharacterClass.Warrior, Dexterity = 10, Wisdom = 10 };
        var keen = new Character { Class = CharacterClass.Assassin, Dexterity = 120, Wisdom = 60 };
        DungeonLocation.ChestTrapSearchChance(clumsy, 5).Should().Be(5);
        DungeonLocation.ChestTrapSearchChance(keen, 5).Should().BeGreaterThan(50);
        DungeonLocation.ChestTrapSearchChance(keen, 5).Should().BeLessThanOrEqualTo(85);
    }

    [Fact]
    public async Task Shrine_ATypo_AsksAgain()
    {
        var (d, term, output, _) = Dungeon(new LowRandom(), "q", "L");
        await Run(d, "MysteriousShrine");
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("ui.invalid_choice_choose", "P, D, L"));
        shown.Should().Contain(Loc.Get("dungeon.shrine_leave"));
    }

    [Fact]
    public async Task Strangers_And_Damsel_ATypo_AsksAgain()
    {
        var (d, term, output, hero) = Dungeon(new LowRandom(), "z", "E");
        await Run(d, "StrangersEncounter");
        Plain(term, output).Should().Contain(Loc.Get("ui.invalid_choice_choose", "F, P, E"));

        var (d2, term2, output2, _) = Dungeon(new LowRandom(), "z", "I");
        await Run(d2, "HarassedWomanEncounter");
        string shown = Plain(term2, output2);
        shown.Should().Contain(Loc.Get("ui.invalid_choice_choose", "H, I, J"));
        shown.Should().Contain(Loc.Get("dungeon.damsel_ignore_1"));
    }

    [Fact]
    public async Task RoomActions_SayWhy_InsteadOfNothing()
    {
        var (d, term, output, _) = Dungeon(new LowRandom(), Array.Empty<string>());
        var floor = new DungeonFloor { CurrentRoomId = "r1" };
        floor.Rooms.Add(new DungeonRoom { Id = "r1", IsCleared = true });
        typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(d, floor);
        typeof(DungeonLocation).GetField("hasCampedThisFloor", F)!.SetValue(d, true);
        foreach (var key in new[] { "F", "T", "V", "X", "D", "R" })
            await (Task<bool>)typeof(DungeonLocation).GetMethod("ProcessRoomChoice", F)!.Invoke(d, new object[] { key })!;
        string shown = Plain(term, output);
        foreach (var k in new[] { "dungeon.no_action_fight", "dungeon.no_action_treasure", "dungeon.no_action_event", "dungeon.no_action_examine", "dungeon.no_action_stairs", "dungeon.rest_once_per_floor" })
            shown.Should().Contain(Loc.Get(k));
    }

    [Fact]
    public async Task MoralChoice_And_RiskPrompt_ATypo_AsksAgain()
    {
        var hero = new Character { Name1 = "m", Name2 = "M", Class = CharacterClass.Warrior, Level = 8, HP = 300, MaxHP = 300 };
        var fis = FeatureInteractionSystem.Instance;
        var feature = new RoomFeature("Shrine", "A shrine.", FeatureInteraction.Examine);

        var out1 = new MemoryStream();
        var t1 = new TerminalEmulator(new LineStream(new[] { "9", "3", "" }), out1);
        await (Task)typeof(FeatureInteractionSystem).GetMethod("HandleMoralChoice", F)!.Invoke(fis, new object[] { feature, hero, 5, t1, new FeatureOutcome() })!;
        t1.StreamWriterInternal?.Flush();
        string s1 = Encoding.UTF8.GetString(out1.ToArray());
        s1.Should().Contain(Loc.Get("ui.invalid_choice_choose", "1, 2, 3"));
        s1.Should().Contain(Loc.Get("feature.not_your_concern"));

        var out2 = new MemoryStream();
        var t2 = new TerminalEmulator(new LineStream(new[] { "maybe", "N", "" }), out2);
        long gold = hero.Gold, hp = hero.HP;
        await (Task)typeof(FeatureInteractionSystem).GetMethod("HandleRiskReward", F)!.Invoke(fis, new object[] { feature, hero, 5, t2, new FeatureOutcome() })!;
        t2.StreamWriterInternal?.Flush();
        string s2 = Encoding.UTF8.GetString(out2.ToArray());
        s2.Should().Contain(Loc.Get("ui.invalid_choice_choose", Loc.Get("feature.risk_answers")));
        s2.Should().Contain(Loc.Get("feature.wisdom_leave"));
        (hero.Gold, hero.HP).Should().Be((gold, hp));
    }
}
