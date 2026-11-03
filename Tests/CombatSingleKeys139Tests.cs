using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.9: Auto-Combat is [O] and Combat Speed is [X] on every combat menu that offers them, so a one key web menu no
/// longer fires A (Attack) on the way to typing AUTO, or S (Status) on the way to SPD. AUTO and SPD typed out still work.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatSingleKeys139Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    public enum Mode { Standard, ScreenReader, Compact }

    private sealed class Scope : IDisposable
    {
        private readonly SessionContext? _saved = SessionContext.Current;
        private readonly string _lang = GameConfig.Language;
        private readonly bool _pacing = Pacing.Disabled;
        private readonly bool _sr = GameConfig.ScreenReaderMode;
        private readonly bool _compact = GameConfig.CompactMode;
        public readonly MemoryStream Stream = new();
        public Scope(string kind, Character hero, Mode mode = Mode.Standard, string lang = "en")
        {
            Pacing.Disabled = true;
            SessionContext.Current = WebFlagOff200Tests.Session(kind, Stream, hero);
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = mode == Mode.ScreenReader;
            GameConfig.CompactMode = mode == Mode.Compact;
            hero.ScreenReaderMode = mode == Mode.ScreenReader;
        }
        public byte[] Bytes(TerminalEmulator t) { t.StreamWriterInternal?.Flush(); return Stream.ToArray(); }
        public void Dispose()
        {
            SessionContext.Current = _saved;
            GameConfig.Language = _lang;
            Pacing.Disabled = _pacing;
            GameConfig.ScreenReaderMode = _sr;
            GameConfig.CompactMode = _compact;
        }
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        try { return typeof(CombatEngine).GetMethod(method, F)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static string Plain(byte[] b)
    {
        var o = new List<byte>();
        for (int i = 0; i < b.Length; i++)
        {
            if (i + 2 < b.Length && b[i] == 0xFF && b[i + 1] == 0xFA && b[i + 2] == 0xC9)
            {
                int end = i + 3;
                while (end + 1 < b.Length && !(b[end] == 0xFF && b[end + 1] == 0xF0)) end++;
                i = end + 1;
                continue;
            }
            o.Add(b[i]);
        }
        return Regex.Replace(Encoding.UTF8.GetString(o.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    private static List<string> BracketKeys(string text) =>
        Regex.Matches(text, @"\[([^\]\s]+)\]").Select(m => m.Groups[1].Value).ToList();

    private static Character Hero(CharacterClass cls = CharacterClass.Warrior)
    {
        var h = WebFlagOff200Tests.Hero(false, false);
        h.Class = cls;
        h.CombatSpeed = CombatSpeed.Normal;
        return h;
    }

    private sealed class Rig
    {
        public CombatEngine Engine = null!;
        public TerminalEmulator Term = null!;
        public List<Monster> Monsters = null!;
        public CombatResult Result = null!;
    }

    private static Rig Dungeon(Character hero, Scope s, string script)
    {
        var t = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(script)), s.Stream);
        var engine = new CombatEngine(t);
        engine.SeedRandomForTests(139);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
        return new Rig
        {
            Engine = engine, Term = t,
            Monsters = new List<Monster> { WebFlagOff200Tests.Foe("Ghoul 1"), WebFlagOff200Tests.Foe("Ghoul 2") },
            Result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 1 },
        };
    }

    private static async Task<(CombatAction action, bool auto)> DungeonPrompt(Rig r, Character hero)
    {
        var task = (Task<(CombatAction action, bool enableAutoCombat)>)Invoke(r.Engine, "GetPlayerActionMultiMonster", hero, r.Monsters, r.Result)!;
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)))).Should().Be(task, "the prompt must take the scripted keys");
        return await task;
    }

    private static async Task<CombatAction> PvpPrompt(CombatEngine engine, TerminalEmulator t, Character hero, CombatResult result)
    {
        var foe = WebFlagOff200Tests.Hero(false, false);
        foe.Name1 = foe.Name2 = "Rival";
        var task = (Task<CombatAction>)Invoke(engine, "GetPlayerAction", hero, null, result, foe)!;
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)))).Should().Be(task, "the prompt must take the scripted keys");
        return await task;
    }

    private static (CombatEngine engine, TerminalEmulator t, CombatResult result) Pvp(Character hero, Scope s, string script)
    {
        var t = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(script)), s.Stream);
        var engine = new CombatEngine(t);
        engine.SeedRandomForTests(139);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        return (engine, t, new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 1 });
    }

    // ------------------------------------------------------------------ the keys do what they say

    [Theory]
    [InlineData("O", Mode.Standard)]
    [InlineData("o", Mode.Standard)]
    [InlineData("AUTO", Mode.Standard)]
    [InlineData("auto", Mode.Standard)]
    [InlineData("O", Mode.Compact)]
    [InlineData("O", Mode.ScreenReader)]
    [InlineData("AUTO", Mode.Compact)]
    public async Task DungeonMenu_OOrAuto_StartsAutoCombat(string key, Mode mode)
    {
        var hero = Hero();
        using var s = new Scope("ssh", hero, mode);
        var r = Dungeon(hero, s, key + "\n");
        var (action, auto) = await DungeonPrompt(r, hero);
        auto.Should().BeTrue("{0} starts auto combat", key);
        action.Type.Should().Be(CombatActionType.Attack);
        Plain(s.Bytes(r.Term)).Should().Contain(Loc.Get("combat.auto_combat_on").Substring(0, 20));
    }

    [Theory]
    [InlineData("X", Mode.Standard)]
    [InlineData("x", Mode.Standard)]
    [InlineData("SPD", Mode.Standard)]
    [InlineData("X", Mode.Compact)]
    [InlineData("X", Mode.ScreenReader)]
    public async Task DungeonMenu_XOrSpd_ChangesSpeed_AndSpendsNoTurn(string key, Mode mode)
    {
        var hero = Hero();
        using var s = new Scope("ssh", hero, mode);
        var r = Dungeon(hero, s, key + "\n" + key + "\nD\n");
        var (action, auto) = await DungeonPrompt(r, hero);
        hero.CombatSpeed.Should().Be(CombatSpeed.Instant, "two presses: Normal to Fast to Instant");
        action.Type.Should().Be(CombatActionType.Defend, "the menu came back and took D");
        auto.Should().BeFalse();
    }

    [Theory]
    [InlineData("X")]
    [InlineData("x")]
    [InlineData("SPD")]
    public async Task PvpMenu_XOrSpd_ChangesSpeed_AndSpendsNoTurn(string key)
    {
        var hero = Hero();
        using var s = new Scope("ssh", hero);
        var (engine, t, result) = Pvp(hero, s, key + "\nD\n");
        (await PvpPrompt(engine, t, hero, result)).Type.Should().Be(CombatActionType.Defend);
        hero.CombatSpeed.Should().Be(CombatSpeed.Fast);
    }

    [Fact]
    public async Task AtTheTargetPrompt_XStillCancels_AndDoesNotChangeSpeed()
    {
        var hero = Hero();
        using var s = new Scope("ssh", hero);
        var r = Dungeon(hero, s, "A\nX\nD\n");
        var (action, _) = await DungeonPrompt(r, hero);
        action.Type.Should().Be(CombatActionType.Defend, "X cancelled the target pick and the menu took D");
        hero.CombatSpeed.Should().Be(CombatSpeed.Normal);
    }

    [Fact]
    public async Task TheElectronClient_StillSendsAutoAndSpd_AndTheyStillWork()
    {
        var oldOut = Console.Out;
        bool electron = GameConfig.ElectronMode;
        try
        {
            GameConfig.ElectronMode = true;
            Console.SetOut(new StringWriter());
            var hero = Hero();
            using var s = new Scope("ssh", hero);
            var r = Dungeon(hero, s, "SPD\nAUTO\n");
            var (action, auto) = await DungeonPrompt(r, hero);
            auto.Should().BeTrue();
            action.Type.Should().Be(CombatActionType.Attack);
            hero.CombatSpeed.Should().Be(CombatSpeed.Fast);
        }
        finally { Console.SetOut(oldOut); GameConfig.ElectronMode = electron; }
    }

    // ------------------------------------------------------------------ menus that do not offer them

    [Theory]
    [InlineData("O")]
    [InlineData("AUTO")]
    public async Task PvpMenu_HasNoAutoCombat_OAndAutoMakeThePlainAttackAnyUnlistedKeyMakes(string key)
    {
        var hero = Hero();
        using var s = new Scope("ssh", hero);
        var (engine, t, result) = Pvp(hero, s, key + "\nN\n");
        var first = await PvpPrompt(engine, t, hero, result);
        var unlisted = await PvpPrompt(engine, t, hero, result);
        first.Type.Should().Be(unlisted.Type, "{0} is not a PvP key; it does what any unlisted key does", key);
        first.Type.Should().Be(CombatActionType.Attack);
        hero.CombatSpeed.Should().Be(CombatSpeed.Normal);
    }

    [Theory]
    [InlineData("O")]
    [InlineData("X")]
    [InlineData("SPD")]
    public void AGroupedFollower_OXAndSpd_AreTheDefaultAttack(string key)
    {
        var hero = Hero();
        using var s = new Scope("ssh", hero);
        var r = Dungeon(hero, s, "");
        r.Monsters[0].HP = 5;   // the weakest: the default attack's target
        var mate = Hero(CharacterClass.Warrior);
        var a = (CombatAction)Invoke(r.Engine, "ParseGroupCombatInput", key, mate, hero, r.Monsters, r.Result)!;
        var n = (CombatAction)Invoke(r.Engine, "ParseGroupCombatInput", "N", mate, hero, r.Monsters, r.Result)!;
        a.Type.Should().Be(CombatActionType.Attack);
        a.TargetIndex.Should().Be(n.TargetIndex, "the follower menu offers neither key, so it is the default");
        var auto = (CombatAction)Invoke(r.Engine, "ParseGroupCombatInput", "AUTO", mate, hero, r.Monsters, r.Result)!;
        auto.Type.Should().Be(CombatActionType.Attack, "AUTO typed by a follower is still one attack on a random foe");
        auto.TargetIndex.Should().BeNull();
    }

    // ------------------------------------------------------------------ the text menus show the keys

    private static string DrawDungeonMenu(Character hero, Mode mode, string lang)
    {
        using var s = new Scope("ssh", hero, mode, lang);
        var r = Dungeon(hero, s, "");
        string method = mode switch { Mode.Compact => "ShowDungeonCombatMenuBBS", Mode.ScreenReader => "ShowDungeonCombatMenuScreenReader", _ => "ShowDungeonCombatMenuStandard" };
        Invoke(r.Engine, method, hero, false, false, new List<(string key, string name, bool available)>(), false, GodDomain.None);
        return Plain(s.Bytes(r.Term));
    }

    private static string DrawPvpMenu(Character hero, Mode mode, string lang)
    {
        using var s = new Scope("ssh", hero, mode, lang);
        var (engine, t, _) = Pvp(hero, s, "");
        var foe = WebFlagOff200Tests.Hero(false, false);
        foe.Name1 = foe.Name2 = "Rival";
        string method = mode switch { Mode.Compact => "ShowCombatMenuBBS", Mode.ScreenReader => "ShowCombatMenuScreenReader", _ => "ShowCombatMenuStandard" };
        Invoke(engine, method, hero, null, foe, true);
        return Plain(s.Bytes(t));
    }

    public static IEnumerable<object[]> LangModes() =>
        from l in Langs from m in new[] { Mode.Standard, Mode.Compact, Mode.ScreenReader } select new object[] { l, m };

    [Theory]
    [MemberData(nameof(LangModes))]
    public void DungeonMenu_ShowsOAndX_NeverAutoOrSpd_Within79Columns(string lang, Mode mode)
    {
        var text = DrawDungeonMenu(Hero(), mode, lang);
        if (mode == Mode.ScreenReader)
        {
            text.Should().MatchRegex(@"(?m)^\s*O - ").And.MatchRegex(@"(?m)^\s*X - ");
        }
        else
        {
            BracketKeys(text).Should().Contain("O").And.Contain("X");
            text.Should().Contain(mode == Mode.Compact ? "[O]" + Loc.GetIn(lang, "combat.bbs_auto") : "[O] " + Loc.GetIn(lang, "combat.auto_short"));
        }
        text.Should().NotContain("AUTO").And.NotContain("SPD");
        foreach (var line in text.Split('\n')) line.Length.Should().BeLessThanOrEqualTo(79, "'{0}'", line);
    }

    [Theory]
    [MemberData(nameof(LangModes))]
    public void PvpMenu_ShowsX_AndNoAutoCombat(string lang, Mode mode)
    {
        var text = DrawPvpMenu(Hero(), mode, lang);
        if (mode == Mode.ScreenReader) text.Should().MatchRegex(@"(?m)^\s*X - ").And.NotMatchRegex(@"(?m)^\s*O - ");
        else BracketKeys(text).Should().Contain("X").And.NotContain("O");
        text.Should().NotContain("AUTO").And.NotContain("SPD");
        foreach (var line in text.Split('\n')) line.Length.Should().BeLessThanOrEqualTo(79, "'{0}'", line);
    }

    [Fact]
    public void TheCompactRow_NamesWhatTheKeysDo()
    {
        var text = DrawDungeonMenu(Hero(), Mode.Compact, "en");
        text.Should().Contain("[O]Auto [X]Spd");
        DrawDungeonMenu(Hero(), Mode.Compact, "hu").Should().Contain("[X]Seb");
    }

    private static string Compact(Character hero, bool pvp, bool worst, bool follower, string lang)
    {
        using var s = new Scope("ssh", hero, Mode.Compact, lang);
        if (pvp) return DrawPvpMenu(hero, Mode.Compact, lang);
        var r = Dungeon(hero, s, "");
        if (worst) r.Engine.BossContext = new BossCombatContext { CanSave = true };
        var classInfo = Enumerable.Range(1, worst ? 9 : 1).Select(i => ($"{i}", "Slash", true)).ToList();
        Invoke(r.Engine, "ShowDungeonCombatMenuBBS", hero, true, true, classInfo, follower, worst ? GodDomain.Light : GodDomain.None);
        return Plain(s.Bytes(r.Term));
    }

    public static IEnumerable<object[]> LangsData() => Langs.Select(l => new object[] { l });

    /// <summary>
    /// 1.3.9 round 2 (c1): the current speed ([X]Nrml, [X]Fast, [X]Inst) was to come back only if every compact row fits
    /// 79 columns in all 5 languages. It does not: the Hungarian full house first row is 80 columns with either label,
    /// so the row keeps [X]Spd. This holds the row the speed key is on within 79 columns, and the typical menu that
    /// held two rows before the change at two rows.
    /// </summary>
    [Theory]
    [MemberData(nameof(LangsData))]
    public void TheCompactSpeedKey_ShowsSpd_AndItsRowFits79Columns(string lang)
    {
        foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Assassin, CharacterClass.Paladin, CharacterClass.Barbarian, CharacterClass.Cleric })
        foreach (var speed in new[] { CombatSpeed.Normal, CombatSpeed.Fast, CombatSpeed.Instant })
        foreach (bool worst in new[] { false, true })
        foreach (bool pvp in new[] { false, true })
        {
            var hero = Hero(cls);
            hero.CombatSpeed = speed;
            hero.PoisonVials = worst ? 12 : 2;
            if (worst) { hero.ManaPotions = 14; hero.Healing = 13; hero.HerbHealing = 11; }
            string text = Compact(hero, pvp, worst, false, lang);
            var rows = text.TrimEnd('\n').Split('\n');
            var speedRow = rows.Single(r => r.Contains("[X]"));
            speedRow.Should().Contain("[X]" + Loc.GetIn(lang, "combat.bbs_speed"), "{0} {1}: the key says what it does", lang, cls);
            speedRow.Length.Should().BeLessThanOrEqualTo(79, "{0} {1} {2} worst={3} pvp={4}: '{5}'", lang, cls, speed, worst, pvp, speedRow);
            // the Assassin's [K] row made three rows before 1.3.9 too; 1.3.10 (G3): the Barbarian's [G] Rage likewise
            if (!worst && (pvp || (cls != CharacterClass.Assassin && cls != CharacterClass.Barbarian)))
                rows.Length.Should().Be(2, "{0} {1} pvp={2}: the usual menu keeps two rows:\n{3}", lang, cls, pvp, text);
        }
    }

    /// <summary>
    /// 1.3.10 (G5): every row of the compact menu fits 79 columns in every setup the 1.3.9 c1 measure took (5 languages,
    /// 5 classes, 3 speeds, typical and full house, dungeon and PvP, owner and follower); the Hungarian full house first
    /// row was 80.
    /// </summary>
    [Theory]
    [MemberData(nameof(LangsData))]
    public void EveryCompactRow_Fits79Columns_InEveryMeasuredSetup(string lang)
    {
        int setups = 0, widest = 0;
        foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Assassin, CharacterClass.Paladin, CharacterClass.Barbarian, CharacterClass.Cleric })
        foreach (var speed in new[] { CombatSpeed.Normal, CombatSpeed.Fast, CombatSpeed.Instant })
        foreach (bool worst in new[] { false, true })
        foreach (bool pvp in new[] { false, true })
        foreach (bool follower in new[] { false, true })
        {
            if (pvp && follower) continue;   // a PvP prompt has no follower
            var hero = Hero(cls);
            hero.CombatSpeed = speed;
            hero.PoisonVials = worst ? 12 : 2;
            if (worst) { hero.ManaPotions = 14; hero.Healing = 13; hero.HerbHealing = 11; }
            foreach (var row in Compact(hero, pvp, worst, follower, lang).TrimEnd('\n').Split('\n'))
            {
                widest = Math.Max(widest, row.Length);
                row.Length.Should().BeLessThanOrEqualTo(79, "{0} {1} {2} worst={3} pvp={4} follower={5}: '{6}'", lang, cls, speed, worst, pvp, follower, row);
            }
            setups++;
        }
        setups.Should().Be(90, "5 classes, 3 speeds, typical and full house, dungeon owner, dungeon follower and PvP");
        widest.Should().BeGreaterThan(60, "the rows were measured");
    }

    [Fact]
    public void TheStandardBox_LinesUpTheKeys()
    {
        var text = DrawDungeonMenu(Hero(), Mode.Standard, "en");
        text.Should().Contain("║ [O] Auto-Combat").And.Contain("║ [X] Combat Speed: Normal").And.Contain("║ [R] ");
    }

    // ------------------------------------------------------------------ Client.Menu

    private static List<string> MenuKeys(byte[] bytes) =>
        WebMenu200Tests.Frames(bytes).Where(f => f.Package == "Client.Menu").Single().Json.GetProperty("groups").EnumerateArray()
            .SelectMany(g => g.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("key").GetString()!)).ToList();

    [Fact]
    public async Task ClientMenu_ListsOAndX_OnTheDungeonMenu_AndXOnThePvpMenu()
    {
        var hero = Hero();
        using (var s = new Scope("web", hero))
        {
            var r = Dungeon(hero, s, "D\n");
            await DungeonPrompt(r, hero);
            MenuKeys(s.Bytes(r.Term)).Should().Contain("O").And.Contain("X").And.NotContain("AUTO").And.NotContain("SPD");
        }
        using (var s = new Scope("web", hero))
        {
            var (engine, t, result) = Pvp(hero, s, "D\n");
            await PvpPrompt(engine, t, hero, result);
            MenuKeys(s.Bytes(t)).Should().Contain("X").And.NotContain("O").And.NotContain("SPD").And.NotContain("AUTO");
        }
    }

    // ------------------------------------------------------------------ tips name the keys the menu shows

    [Fact]
    public void TheTipKeyLists_AreTheNewKeys()
    {
        CombatEngine.DungeonMenuTipKeys.Keys.Should().Contain("O").And.Contain("X").And.NotContain("AUTO").And.NotContain("SPD");
        CombatEngine.PvpMenuTipKeys.Keys.Should().Contain("X").And.NotContain("O").And.NotContain("SPD");
        foreach (var lang in Langs)
        {
            BracketKeys(Loc.GetIn(lang, "combat.tip_auto")).Should().Equal(new[] { "O" }, lang);
            BracketKeys(Loc.GetIn(lang, "combat.tip_spd")).Should().Equal(new[] { "X" }, lang);
            Loc.GetIn(lang, "combat.tip_auto").Should().NotContainEquivalentOf("'auto'", "{0}: the tip names the key", lang);
        }
    }

    [Theory]
    [InlineData(CharacterClass.Warrior)]
    [InlineData(CharacterClass.Magician)]
    [InlineData(CharacterClass.Paladin)]
    public async Task UnderTheDungeonMenu_EveryTipNamesOnlyKeysTheMenuShows_AndTheAutoTipAppears(CharacterClass cls)
    {
        var hero = Hero(cls);
        using var s = new Scope("ssh", hero);
        const int prompts = 300;
        var r = Dungeon(hero, s, string.Concat(Enumerable.Repeat("D\n", prompts)));
        for (int i = 0; i < prompts; i++) await DungeonPrompt(r, hero);
        var text = Plain(s.Bytes(r.Term));
        var menuKeys = BracketKeys(text.Substring(0, text.IndexOf('╚'))).ToHashSet();
        menuKeys.Should().Contain("O").And.Contain("X");
        text.Should().Contain(HintSystem.WordWrap(Loc.Get("combat.tip_auto"), 77)[0], "the Auto-Combat tip shows under the menu that has [O]");
        text.Should().Contain(HintSystem.WordWrap(Loc.Get("combat.tip_spd"), 77)[0]);
        var tipPrefix = Loc.Get("combat.tip_spd").Split(':')[0] + ":";
        foreach (var line in text.Split('\n').Where(l => l.TrimStart().StartsWith(tipPrefix)))
            BracketKeys(line).Except(menuKeys).Should().BeEmpty("'{0}'", line);
    }
}
