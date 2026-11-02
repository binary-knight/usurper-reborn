using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
/// 1.3.9 play test, round 2: the potion tip under the PvP menu, Enter at the target prompt repeating the last target
/// chosen in the fight, and the web client's one-key menu preference with Client.Menu's oneKey field.
/// </summary>
[Collection("SharedGameSingletons")]
public class HighFindingsRound2139Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private sealed class Scope : IDisposable
    {
        private readonly SessionContext? _saved = SessionContext.Current;
        private readonly string _lang = GameConfig.Language;
        private readonly bool _pacing = Pacing.Disabled;
        private readonly bool _sr = GameConfig.ScreenReaderMode;
        private readonly bool _compact = GameConfig.CompactMode;
        public readonly MemoryStream Stream = new();
        public readonly SessionContext? Ctx;
        public Scope(string kind, Character hero, bool screenReader = false, bool engine = true)
        {
            Pacing.Disabled = true;
            GameConfig.Language = "en";
            GameConfig.CompactMode = false;
            GameConfig.ScreenReaderMode = screenReader;
            hero.ScreenReaderMode = screenReader;
            Ctx = WebFlagOff200Tests.Session(kind, Stream, hero);
            if (Ctx != null && engine)
            {
                Ctx.InitializeSystems();
                var e = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
                e.CurrentPlayer = hero;
                Ctx.Engine = e;
            }
            SessionContext.Current = Ctx;
        }
        public TerminalEmulator Term(string input) => new(new MemoryStream(Encoding.UTF8.GetBytes(input)), Stream);
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

    private static string Plain(byte[] b) =>
        Regex.Replace(Encoding.UTF8.GetString(b), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

    private static object? Invoke(object target, string method, params object?[] args)
    {
        try { return target.GetType().GetMethod(method, F)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static List<string> BracketKeys(string text) =>
        Regex.Matches(text, @"\[([^\]\s]+)\]").Select(m => m.Groups[1].Value).ToList();

    private static Character Hero()
    {
        var h = WebFlagOff200Tests.Hero(false, false);
        h.CombatSpeed = CombatSpeed.Normal;
        return h;
    }

    // ------------------------------------------------------------------ the potion tip under the PvP menu

    /// <summary>Many PvP prompts in a row (a tip every 5th), D each time: everything drawn.</summary>
    private static async Task<string> PvpPrompts(Character hero, int prompts)
    {
        using var s = new Scope("ssh", hero);
        var t = s.Term(string.Concat(Enumerable.Repeat("D\n", prompts)));
        var engine = new CombatEngine(t);
        engine.SeedRandomForTests(1392);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        var foe = WebFlagOff200Tests.Hero(false, false);
        foe.Name1 = foe.Name2 = "Rival";
        var result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 1 };
        for (int i = 0; i < prompts; i++)
            ((CombatAction)await (Task<CombatAction>)Invoke(engine, "GetPlayerAction", hero, null, result, foe)!).Type.Should().Be(CombatActionType.Defend);
        return Plain(s.Bytes(t));
    }

    [Fact]
    public async Task UnderThePvpMenu_ThePotionTipNamesH_AndEveryTipNamesOnlyThatMenusKeys()
    {
        var text = await PvpPrompts(Hero(), 400);
        var menuKeys = BracketKeys(text.Substring(0, text.IndexOf('╚'))).ToHashSet();   // the first menu drawn
        menuKeys.Should().Contain("H").And.Contain("I").And.NotContain("O", "the PvP menu: H heals, I disarms, no Auto-Combat");
        text.Should().Contain(HintSystem.WordWrap(Loc.Get("combat.tip_healing_potions", "H"), 77)[0], "the potion tip names the PvP menu's Heal key");
        text.Should().NotContain(HintSystem.WordWrap(Loc.Get("combat.tip_healing_potions", "I"), 77)[0], "I is Disarm under this menu");
        text.Should().NotContain(Loc.Get("combat.tip_auto"), "the Auto-Combat tip names a key this menu does not list");
        var tipPrefix = Loc.Get("combat.tip_spd").Split(':')[0] + ":";
        foreach (var line in text.Split('\n').Where(l => l.TrimStart().StartsWith(tipPrefix)))
            BracketKeys(line).Except(menuKeys).Should().BeEmpty("'{0}'", line);
    }

    [Fact]
    public void ThePotionTip_TakesTheKeyItIsGiven_InEveryLanguage()
    {
        foreach (var lang in Langs)
        {
            BracketKeys(Loc.GetIn(lang, "combat.tip_healing_potions", "H")).Should().Equal("H");
            BracketKeys(Loc.GetIn(lang, "combat.tip_healing_potions", "I")).Should().Equal("I");
        }
        CombatEngine.PvpMenuTipKeys.PotionKey.Should().Be("H");
        CombatEngine.DungeonMenuTipKeys.PotionKey.Should().Be("I");
    }

    // ------------------------------------------------------------------ Enter repeats the last target

    private static List<Monster> Foes(int n) => Enumerable.Range(1, n).Select(i => WebFlagOff200Tests.Foe("Ghoul " + i)).ToList();

    private static async Task<(int? pick, string text)> Target(CombatEngine engine, Scope s, TerminalEmulator t, List<Monster> foes)
    {
        long before = s.Stream.Length;
        var pick = await (Task<int?>)Invoke(engine, "GetTargetSelection", foes, true)!;
        var all = s.Bytes(t);
        return (pick, Plain(all.Skip((int)before).ToArray()));
    }

    [Fact]
    public async Task Enter_RepeatsTheLastChosenTarget_WhileItLives_ThenFallsBackToRandom()
    {
        var hero = Hero();
        using var s = new Scope("ssh", hero);
        var t = s.Term("\n2\n\n\n\n");
        var engine = new CombatEngine(t);
        var foes = Foes(3);

        var (pick, text) = await Target(engine, s, t, foes);
        pick.Should().BeNull("the first time, with no target chosen, Enter is random");
        text.Should().Contain(Loc.Get("combat.target_monster_random", 3));

        (pick, _) = await Target(engine, s, t, foes);
        pick.Should().Be(1, "2 picks Ghoul 2");

        (pick, text) = await Target(engine, s, t, foes);
        pick.Should().Be(1, "Enter takes Ghoul 2 again");
        text.Should().Contain(Loc.Get("combat.target_monster_last", 3, 2), "the prompt says Enter repeats it");
        (pick, _) = await Target(engine, s, t, foes);
        pick.Should().Be(1, "and again");

        foes[1].HP = 0;
        (pick, text) = await Target(engine, s, t, foes);
        pick.Should().BeNull("Ghoul 2 is dead: Enter is random again");
        text.Should().Contain(Loc.Get("combat.target_monster_random", 3));
    }

    [Fact]
    public async Task ANewFight_ForgetsTheLastTarget()
    {
        var hero = Hero();
        using var s = new Scope("ssh", hero);
        var t = s.Term(WebFlagOff3aTests.Attacks());
        var engine = new CombatEngine(t);
        var old = Foes(1)[0];
        typeof(CombatEngine).GetField("_lastChosenTarget", F)!.SetValue(engine, old);
        engine.SeedRandomForTests(7);
        var foes = Foes(2);
        var fight = engine.PlayerVsMonsters(hero, foes, offerMonkEncounter: false);
        (await Task.WhenAny(fight, Task.Delay(TimeSpan.FromSeconds(60)))).Should().Be(fight);
        typeof(CombatEngine).GetField("_lastChosenTarget", F)!.GetValue(engine).Should().BeNull("the fight started without the old fight's target");
    }

    [Fact]
    public void TheLastTargetPrompt_IsInEveryLanguage_AndFits()
    {
        foreach (var lang in Langs)
        {
            var text = Loc.GetIn(lang, "combat.target_monster_last", 4, 3);
            text.Should().Contain("1-4").And.Contain("3");
            text.Length.Should().BeLessThanOrEqualTo(79, lang);
        }
    }

    // ------------------------------------------------------------------ the web one-key preference

    [Fact]
    public void WebMenuKeys_DefaultToOneKey_ForANewCharacter_AnEmptySave_AndAnOldSave()
    {
        new Character().WebMenuKeysNeedEnter.Should().BeFalse("a new character's web menus act on one key");
        new PlayerData().WebMenuKeysNeedEnter.Should().BeFalse();
        JsonSerializer.Deserialize<PlayerData>("{}")!.WebMenuKeysNeedEnter.Should().BeFalse();
        MenuKeysNeedEnterPref1115Tests.Restore(JsonSerializer.Deserialize<PlayerData>("{\"MenuKeysNeedEnter\":true}")!)
            .WebMenuKeysNeedEnter.Should().BeFalse("a save from before the field reads as one key");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WebMenuKeys_RoundTripThroughTheSave(bool needEnter)
    {
        var p = new Character { Name1 = "Round", Name2 = "Round", Level = 5, HP = 50, MaxHP = 50, WebMenuKeysNeedEnter = needEnter };
        var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { p })!;
        data.WebMenuKeysNeedEnter.Should().Be(needEnter);
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        MenuKeysNeedEnterPref1115Tests.Restore(back).WebMenuKeysNeedEnter.Should().Be(needEnter);
        File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), "Scripts", "Editor", "PlayerSaveEditor.cs"))
            .Should().Contain("p.WebMenuKeysNeedEnter = EditorIO.PromptBool(", "the save editor's fifth site");
    }

    private static JsonElement CombatMenu(string kind, Character hero, bool screenReader = false)
    {
        using var s = new Scope(kind, hero, screenReader);
        var t = s.Term("D\n");
        var engine = new CombatEngine(t);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
        var result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 1 };
        var task = (Task<(CombatAction action, bool enableAutoCombat)>)Invoke(engine, "GetPlayerActionMultiMonster", hero, Foes(2), result)!;
        task.Wait(TimeSpan.FromSeconds(20)).Should().BeTrue();
        var menus = WebMenu200Tests.Frames(s.Bytes(t)).Where(f => f.Package == "Client.Menu").Select(f => f.Json).ToList();
        return menus.SingleOrDefault();
    }

    [Fact]
    public void ClientMenu_OneKey_IsTrue_ForAWebClientSession_ByDefault()
    {
        var menu = CombatMenu("web", Hero());
        menu.GetProperty("oneKey").GetBoolean().Should().BeTrue();
        WebClientMenu.Payload("combat", new List<WebMenuGroup>(), null, true).GetType().GetProperty("oneKey").Should().NotBeNull();
    }

    [Fact]
    public void ClientMenu_OneKey_IsFalse_WhenThePlayerWantsEnter()
    {
        var hero = Hero();
        hero.WebMenuKeysNeedEnter = true;
        CombatMenu("web", hero).GetProperty("oneKey").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ClientMenu_OneKey_IsFalse_ForAScreenReader()
    {
        CombatMenu("web", Hero(), screenReader: true).GetProperty("oneKey").GetBoolean().Should().BeFalse("a screen reader session never gets one key");
    }

    [Theory]
    [InlineData("mud_gmcp")]
    [InlineData("ssh")]
    [InlineData("web_nogmcp")]
    [InlineData("door")]
    public void NoClientMenu_OutsideAWebClientSession(string kind)
    {
        CombatMenu(kind, Hero()).ValueKind.Should().Be(JsonValueKind.Undefined, "{0} gets no Client.Menu, so no oneKey", kind);
    }

    private static async Task<string> Settings(string kind, Character hero, params string[] keys)
    {
        var street = new MainStreetLocation();
        using var s = new Scope(kind, hero, engine: false);   // the save goes to the game's own engine, as in the console test
        var t = new TerminalEmulator(new LineStream(keys), s.Stream);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, t);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        await (Task)typeof(BaseLocation).GetMethod("ShowPreferencesMenu", F)!.Invoke(street, null)!;
        return Plain(s.Bytes(t));
    }

    [Fact]
    public async Task SettingsE_OnAWebClientSession_TogglesTheWebPreference_AndTheNextMenuFollows()
    {
        var hero = Hero();
        bool console = hero.MenuKeysNeedEnter;
        var text = await Settings("web", hero, "E", "0");
        hero.WebMenuKeysNeedEnter.Should().BeTrue("E turned web one-key menus off");
        hero.MenuKeysNeedEnter.Should().Be(console, "the console preference is untouched");
        text.Should().Contain($"E] {Loc.Get("prefs.web_menu_keys_need_enter")}: {Loc.Get("prefs.off")}");
        text.Should().Contain(Loc.Get("base.pref_web_menu_keys_need_enter_set", Loc.Get("prefs.on")));
        CombatMenu("web", hero).GetProperty("oneKey").GetBoolean().Should().BeFalse("the next Client.Menu says Enter is needed");
        await Settings("web", hero, "E", "0");
        hero.WebMenuKeysNeedEnter.Should().BeFalse();
        CombatMenu("web", hero).GetProperty("oneKey").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task SettingsE_OutsideAWebClientSession_IsNotTheWebPreference()
    {
        var hero = Hero();
        var text = await Settings("ssh", hero, "0");
        text.Should().NotContain(Loc.Get("prefs.web_menu_keys_need_enter"));
        hero.WebMenuKeysNeedEnter.Should().BeFalse();
    }

    [Fact]
    public void TheWebLabels_AreInFiveLanguages_AndFit()
    {
        foreach (var lang in Langs)
        {
            Loc.GetIn(lang, "prefs.web_menu_keys_need_enter").Should().Contain("web", lang);
            ($"  [E] {Loc.GetIn(lang, "prefs.web_menu_keys_need_enter")}: {Loc.GetIn(lang, "prefs.off")}").Length.Should().BeLessThanOrEqualTo(79);
            Loc.GetIn(lang, "base.pref_web_menu_keys_need_enter_set", "x").Should().Contain("x");
        }
    }

    // ------------------------------------------------------------------ a whole fight on a web client session

    /// <summary>A fight against two foes on a web client session: the frames in order, each with the text drawn before it.</summary>
    private static async Task<List<(string Package, JsonElement Json, string TextBefore)>> WebFight(Character hero, string script)
    {
        using var s = new Scope("web", hero);
        var t = s.Term(script);
        var engine = new CombatEngine(t);
        engine.SeedRandomForTests(314);
        var fight = engine.PlayerVsMonsters(hero, WebFlagOff3aTests.Foes(), offerMonkEncounter: false);
        (await Task.WhenAny(fight, Task.Delay(TimeSpan.FromSeconds(60)))).Should().Be(fight, "the fight must finish on the scripted input");
        await fight;
        var bytes = s.Bytes(t);
        var list = new List<(string, JsonElement, string)>();
        var text = new List<byte>();
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i + 2 < bytes.Length && bytes[i] == 0xFF && bytes[i + 1] == 0xFA && bytes[i + 2] == 0xC9)
            {
                int start = i + 3, end = start;
                while (end + 1 < bytes.Length && !(bytes[end] == 0xFF && bytes[end + 1] == 0xF0)) end++;
                string body = Encoding.UTF8.GetString(bytes, start, end - start);
                int sp = body.IndexOf(' ');
                string pkg = sp < 0 ? body : body.Substring(0, sp);
                var json = sp < 0 ? default : JsonDocument.Parse(body.Substring(sp + 1)).RootElement.Clone();
                list.Add((pkg, json, Plain(text.ToArray())));
                i = end + 1;
                continue;
            }
            text.Add(bytes[i]);
        }
        return list;
    }

    [Fact]
    public async Task TheCombatClientMenu_CarriesTheRoundTheScreenPrints_EveryRound()
    {
        var frames = await WebFight(Hero(), WebFlagOff3aTests.Attacks());
        var label = new Regex(Regex.Escape(Loc.Get("combat.round_label", 999)).Replace("999", @"(\d+)"));
        var menus = frames.Where(f => f.Package == "Client.Menu" && f.Json.GetProperty("screen").GetString() == WebClientMenu.Combat).ToList();
        menus.Count.Should().BeGreaterThanOrEqualTo(3, "the fight lasts at least three rounds");
        foreach (var m in menus)
        {
            var printed = label.Matches(m.TextBefore);
            printed.Count.Should().BeGreaterThan(0, "a round label is drawn before the prompt");
            m.Json.GetProperty("round").GetInt32().Should().Be(int.Parse(printed[^1].Groups[1].Value), "the frame's round is the round the screen printed last");
        }
        menus.Select(m => m.Json.GetProperty("round").GetInt32()).Distinct().Count().Should().BeGreaterThanOrEqualTo(3);
        frames.Where(f => f.Package == "Client.Menu" && f.Json.GetProperty("screen").GetString() != WebClientMenu.Combat)
            .Where(f => f.Json.TryGetProperty("round", out var r)).Should().BeEmpty("other screens carry no round");
    }

    [Fact]
    public async Task APotionDrunkInAFight_ReachesTheFooter_BeforeTheNextCombatPrompt()
    {
        var hero = Hero();
        hero.HP = 150;   // below max, so the potion is drunk
        hero.Healing = 3;
        var frames = await WebFight(hero, "I\n" + WebFlagOff3aTests.Attacks());
        var combatMenus = frames.Select((f, k) => (f, k)).Where(x => x.f.Package == "Client.Menu" && x.f.Json.GetProperty("screen").GetString() == WebClientMenu.Combat)
            .Select(x => x.k).ToList();
        combatMenus.Count.Should().BeGreaterThanOrEqualTo(3);
        // the Client.Menu is drawn, then the prompt reads: the footer for a prompt follows that prompt's menu
        var firstPrompt = frames.Skip(combatMenus[0]).Take(combatMenus[1] - combatMenus[0]).Where(f => f.Package == "Client.Footer").ToList();
        firstPrompt.Should().NotBeEmpty("the first combat prompt sends the footer");
        firstPrompt.First().Json.GetProperty("potions").GetInt64().Should().Be(3);
        var secondPrompt = frames.Skip(combatMenus[1]).Take(combatMenus[2] - combatMenus[1]).Where(f => f.Package == "Client.Footer").ToList();
        secondPrompt.Should().NotBeEmpty("the prompt after the potion sends the new count");
        // the count the second menu printed (its Use Item row), or 0 where it printed the no potion row
        var row = new Regex(Regex.Escape(Loc.Get("combat.use_item_potions_short", 777, 888)).Replace("777", @"(\d+)").Replace("888", @"\d+"));
        var shown = row.Matches(frames[combatMenus[1]].TextBefore);
        long printed = shown.Count > 0 && frames[combatMenus[1]].TextBefore.LastIndexOf(shown[^1].Value) > frames[combatMenus[1]].TextBefore.LastIndexOf(Loc.Get("combat.use_item_none_short"))
            ? long.Parse(shown[^1].Groups[1].Value) : 0;
        printed.Should().BeLessThan(3, "the potion was drunk");
        secondPrompt.First().Json.GetProperty("potions").GetInt64().Should().Be(printed, "the footer shows the count the menu printed");
    }
}
