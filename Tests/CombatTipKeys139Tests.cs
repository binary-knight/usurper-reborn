using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.9 (play test B4): the combat tips named keys the menu did not show. A Paladin's first battle tip said
/// [1] Lay on Hands, [2] Power Strike over a menu of [1] Power Strike, [2] Lay on Hands; the combat tip said [H] for a
/// potion and [S] for status while the menu has [I] and no status key. Each tip now takes its keys from the menu shown
/// to that character, or names the action without a key. The checks draw the real menu and read its keys from the
/// drawn text, so the tip and the check never share one list.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatTipKeys139Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private enum Mode { Standard, ScreenReader, Compact }

    private sealed class Scope : IDisposable
    {
        private readonly SessionContext? _saved = SessionContext.Current;
        private readonly string _lang = GameConfig.Language;
        private readonly bool _pacing = Pacing.Disabled;
        private readonly bool _sr = GameConfig.ScreenReaderMode;
        private readonly bool _compact = GameConfig.CompactMode;
        public readonly MemoryStream Stream = new();
        public Scope(Character hero, Mode mode, string lang)
        {
            Pacing.Disabled = true;
            var ctx = WebFlagOff200Tests.Session("ssh", Stream, hero)!;
            ctx.InitializeSystems();
            var e = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
            e.CurrentPlayer = hero;
            ctx.Engine = e;
            SessionContext.Current = ctx;
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = mode == Mode.ScreenReader;
            GameConfig.CompactMode = mode == Mode.Compact;
            hero.ScreenReaderMode = mode == Mode.ScreenReader;
        }
        public void Dispose()
        {
            SessionContext.Current = _saved;
            GameConfig.Language = _lang;
            Pacing.Disabled = _pacing;
            GameConfig.ScreenReaderMode = _sr;
            GameConfig.CompactMode = _compact;
        }
    }

    /// <summary>A level 1 character of the class, with the quickbar a new character of it gets.</summary>
    private static Character Fresh(CharacterClass cls)
    {
        var c = new Character
        {
            Name1 = "tipper", Name2 = "Tipper", AI = CharacterAI.Human, Class = cls, Race = CharacterRace.Human,
            Level = 1, HP = 60, MaxHP = 80, BaseMaxHP = 80, Strength = 20, BaseStrength = 20, Defence = 10, BaseDefence = 10,
            Dexterity = 15, BaseDexterity = 15, Agility = 15, BaseAgility = 15, Constitution = 15, BaseConstitution = 15,
            Intelligence = 20, BaseIntelligence = 20, Wisdom = 20, BaseWisdom = 20, Stamina = 50, BaseStamina = 50,
            Mana = 100, MaxMana = 100, Mental = 80, Gold = 100, Healing = 5, MKills = 0, CombatSpeed = CombatSpeed.Normal,
            CurrentCombatStamina = 100,
        };
        GameEngine.AutoPopulateQuickbar(c);
        return c;
    }

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

    private static object? Invoke(object target, string method, params object?[] args)
    {
        try { return typeof(CombatEngine).GetMethod(method, F)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    /// <summary>The keys a drawn menu shows: [X] tokens ([1-9] standing for each number), and "X - " rows read aloud.</summary>
    private static HashSet<string> MenuKeys(string menu, Mode mode)
    {
        var keys = new HashSet<string>();
        if (mode == Mode.ScreenReader)
        {
            foreach (Match m in Regex.Matches(menu, @"(?m)^\s*([A-Z0-9]+) - ")) keys.Add(m.Groups[1].Value);
            return keys;
        }
        foreach (var k in BracketKeys(menu))
        {
            var range = Regex.Match(k, @"^(\d)-(\d)$");
            if (range.Success)
                for (int d = int.Parse(range.Groups[1].Value); d <= int.Parse(range.Groups[2].Value); d++) keys.Add(d.ToString());
            else keys.Add(k);
        }
        return keys;
    }

    private static List<string> BracketKeys(string text) =>
        Regex.Matches(text, @"\[([^\]\s]+)\]").Select(m => m.Groups[1].Value).ToList();

    /// <summary>One prompt of a fight against two foes: the first battle tip and the menu right after it, as drawn.</summary>
    private static async Task<(string tip, string menu)> FirstPrompt(Character hero, Mode mode, string lang)
    {
        using var s = new Scope(hero, mode, lang);
        var t = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("D\n")), s.Stream);
        var engine = new CombatEngine(t);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
        var monsters = new List<Monster> { WebFlagOff200Tests.Foe("Ghoul 1"), WebFlagOff200Tests.Foe("Ghoul 2") };
        var result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 1 };
        var task = (Task<(CombatAction action, bool enableAutoCombat)>)Invoke(engine, "GetPlayerActionMultiMonster", hero, monsters, result)!;
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)))).Should().Be(task, "the prompt must take the scripted D");
        await task;
        t.StreamWriterInternal?.Flush();
        var lines = Strip(Encoding.UTF8.GetString(s.Stream.ToArray())).Split('\n').ToList();
        int head = lines.FindIndex(l => l.Contains(Loc.Get("combat.first_battle_tip")));
        head.Should().BeGreaterThan(-1, "a character with no kills sees the first battle tip");
        int end = mode == Mode.ScreenReader
            ? lines.FindIndex(head + 1, l => l.Trim().Length == 0)
            : lines.FindIndex(head + 1, l => l.StartsWith("└"));
        var tipLines = lines.Skip(head + 1).Take(end - head - 1).ToList();
        return (string.Join("\n", tipLines), string.Join("\n", lines.Skip(end + 1)));
    }

    private static string TipText(string tip) =>
        string.Join(" ", tip.Split('\n').Select(l => l.StartsWith("│ ") ? l.Substring(2) : l).Select(l => l.Trim()));

    // ------------------------------------------------------------------ the first battle tip

    [Theory]
    [InlineData(CharacterClass.Paladin)]
    [InlineData(CharacterClass.Warrior)]
    [InlineData(CharacterClass.Barbarian)]
    [InlineData(CharacterClass.Magician)]
    [InlineData(CharacterClass.Cleric)]
    public async Task TheFirstBattleTip_NamesOnlyKeysOfTheMenuShown_InEveryMenuForm(CharacterClass cls)
    {
        foreach (var mode in new[] { Mode.Standard, Mode.ScreenReader, Mode.Compact })
        foreach (var lang in Langs)
        {
            var (tip, menu) = await FirstPrompt(Fresh(cls), mode, lang);
            var named = BracketKeys(tip);
            named.Should().NotBeEmpty("{0} {1} {2}: the tip names keys", cls, mode, lang);
            named.Except(MenuKeys(menu, mode)).Should().BeEmpty( "{0} {1} {2}: every key the tip names is on the menu drawn after it", cls, mode, lang);
        }
    }

    [Fact]
    public async Task APaladinsTip_TakesEachAbilitysSlot_FromTheBar()
    {
        var hero = Fresh(CharacterClass.Paladin);
        hero.Quickbar[0].Should().Be("power_strike", "a new Paladin's bar starts with Power Strike");
        hero.Quickbar[1].Should().Be("lay_on_hands");
        var (tip, menu) = await FirstPrompt(hero, Mode.Standard, "en");
        menu.Should().Contain("[1] Power Strike").And.Contain("[2] Lay on Hands");
        TipText(tip).Should().Contain("[2] Lay on Hands").And.Contain("[1] Power Strike");

        // the player rearranges the bar: the tip follows it
        var moved = Fresh(CharacterClass.Paladin);
        moved.Quickbar[0] = null; moved.Quickbar[1] = null;
        moved.Quickbar[4] = "lay_on_hands"; moved.Quickbar[6] = "power_strike";
        (tip, menu) = await FirstPrompt(moved, Mode.Standard, "en");
        menu.Should().Contain("[5] Lay on Hands").And.Contain("[7] Power Strike");
        TipText(tip).Should().Contain("[5] Lay on Hands").And.Contain("[7] Power Strike");
    }

    [Fact]
    public async Task AnAbilityNotOnTheBar_IsNotGivenAKey()
    {
        var hero = Fresh(CharacterClass.Paladin);
        hero.Quickbar[1] = null;   // Lay on Hands taken off the bar
        var (tip, menu) = await FirstPrompt(hero, Mode.Standard, "en");
        TipText(tip).Should().Be(Loc.Get("hint.class_combat.default"), "a tip naming Lay on Hands would name a slot it is not in");
        BracketKeys(tip).Should().Equal("A");

        var bare = Fresh(CharacterClass.Warrior);
        for (int i = 0; i < bare.Quickbar.Count; i++) bare.Quickbar[i] = null;
        (tip, _) = await FirstPrompt(bare, Mode.Standard, "en");
        TipText(tip).Should().Be(Loc.Get("hint.class_combat.attack_only"), "with an empty bar no number key is on the menu");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task TheFirstBattleTip_FitsInSeventyNineColumns(string lang)
    {
        foreach (var cls in new[] { CharacterClass.Paladin, CharacterClass.Warrior, CharacterClass.Barbarian, CharacterClass.Magician, CharacterClass.Cleric })
        foreach (var mode in new[] { Mode.Standard, Mode.ScreenReader })
        {
            var (tip, _) = await FirstPrompt(Fresh(cls), mode, lang);
            foreach (var line in tip.Split('\n'))
                line.Length.Should().BeLessThanOrEqualTo(79, "{0} {1} {2}: '{3}'", cls, mode, lang, line);
        }
    }

    [Fact]
    public void EveryClassTip_NamesOneKeyPerQuickbarEntry_InEveryLanguage()
    {
        foreach (var (cls, tip) in HintSystem.ClassCombatTips)
        foreach (var lang in Langs)
        {
            var text = Loc.GetIn(lang, tip.TipKey);
            for (int i = 0; i < tip.SlotIds.Length; i++)
                text.Should().Contain("[{" + i + "}]", "{0} {1}: the key of entry {2} comes from the bar", cls, lang, i);
            BracketKeys(text).Where(k => !k.StartsWith("{")).Except(new[] { "A" }).Should().BeEmpty( "{0} {1}: besides the bar, only [A]", cls, lang);
        }
        foreach (var lang in Langs)
        {
            BracketKeys(Loc.GetIn(lang, "hint.class_combat.default")).Should().Equal("A");
            BracketKeys(Loc.GetIn(lang, "hint.class_combat.attack_only")).Should().Equal("A");
        }
    }

    // ------------------------------------------------------------------ the first combat tip

    private static HashSet<string> DrawnMenuKeys(Character hero, Mode mode, string lang = "en")
    {
        using var s = new Scope(hero, mode, lang);
        var t = new TerminalEmulator(new MemoryStream(), s.Stream);
        var engine = new CombatEngine(t);
        var classInfo = (List<(string key, string name, bool available)>)Invoke(engine, "GetClassSpecificActions", hero)!;
        string drawer = mode switch { Mode.ScreenReader => "ShowDungeonCombatMenuScreenReader", Mode.Compact => "ShowDungeonCombatMenuBBS", _ => "ShowDungeonCombatMenuStandard" };
        Invoke(engine, drawer, hero, false, false, classInfo, false, GodDomain.None);
        t.StreamWriterInternal?.Flush();
        return MenuKeys(Strip(Encoding.UTF8.GetString(s.Stream.ToArray())), mode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFirstCombatTip_NamesOnlyKeysOfTheMenu_InEveryMenuForm(bool holdsPotions)
    {
        foreach (var mode in new[] { Mode.Standard, Mode.ScreenReader, Mode.Compact })
        foreach (var cls in new[] { CharacterClass.Paladin, CharacterClass.Warrior })
        {
            var hero = Fresh(cls);
            if (!holdsPotions) { hero.Healing = 0; hero.ManaPotions = 0; }
            var keys = DrawnMenuKeys(hero, mode);
            string msgKey;
            using (new Scope(hero, mode, "en")) msgKey = CombatEngine.FirstCombatHintKey(hero);
            foreach (var lang in Langs)
            {
                var named = BracketKeys(Loc.GetIn(lang, msgKey));
                named.Should().Contain("A").And.Contain("R");
                named.Except(keys).Should().BeEmpty( "{0} {1} {2} potions={3}: the tip names only keys on the menu", cls, mode, lang, holdsPotions);
            }
        }
    }

    [Fact]
    public void TheFirstCombatTip_NamesThePotionKeyOfTheMenu_AndNoStatusKey()
    {
        foreach (var lang in Langs)
        {
            var named = BracketKeys(Loc.GetIn(lang, "hint.first_combat.msg"));
            named.Should().Equal(new[] { "A", "R", "I" }, "{0}: Attack, Retreat and the potion's key I", lang);
            BracketKeys(Loc.GetIn(lang, "hint.first_combat.msg_no_item")).Should().Equal(new[] { "A", "R" });
        }
    }

    // ------------------------------------------------------------------ the combat tips shown under the menu

    [Theory]
    [InlineData(CharacterClass.Paladin)]
    [InlineData(CharacterClass.Warrior)]
    [InlineData(CharacterClass.Barbarian)]
    [InlineData(CharacterClass.Ranger)]
    public void EveryCombatTip_NamesOnlyKeysOfTheStandardMenu_InEveryLanguage(CharacterClass cls)
    {
        // the tips are shown under the standard menu only
        var keys = DrawnMenuKeys(Fresh(cls), Mode.Standard);
        var tipKeys = ((string[])typeof(CombatEngine).GetField("CombatTipKeys", SF)!.GetValue(null)!).ToList();
        var classTips = (Dictionary<CharacterClass, string[]>)typeof(CombatEngine).GetField("ClassSpecificTipKeys", SF)!.GetValue(null)!;
        if (classTips.TryGetValue(cls, out var own)) tipKeys.AddRange(own);
        foreach (var key in tipKeys)
        foreach (var lang in Langs)
            BracketKeys(Loc.GetIn(lang, key)).Except(keys).Should().BeEmpty( "{0} {1} {2}: '{3}'", cls, key, lang, Loc.GetIn(lang, key));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void TheCombatTips_AsDrawn_FitInSeventyNineColumns_AndNameOnlyMenuKeys(string lang)
    {
        foreach (var cls in new[] { CharacterClass.Barbarian, CharacterClass.Ranger, CharacterClass.Paladin })
        {
            var hero = Fresh(cls);
            var keys = DrawnMenuKeys(hero, Mode.Standard, lang);
            using var s = new Scope(hero, Mode.Standard, lang);
            var t = new TerminalEmulator(new MemoryStream(), s.Stream);
            var engine = new CombatEngine(t);
            engine.SeedRandomForTests(139);
            for (int i = 0; i < 1500; i++) Invoke(engine, "ShowCombatTipIfNeeded", hero);
            t.StreamWriterInternal?.Flush();
            var lines = Strip(Encoding.UTF8.GetString(s.Stream.ToArray())).Split('\n').Where(l => l.Trim().Length > 0).ToList();
            lines.Should().NotBeEmpty();
            foreach (var line in lines)
            {
                line.Length.Should().BeLessThanOrEqualTo(79, "{0} {1}: '{2}'", cls, lang, line);
                BracketKeys(line).Except(keys).Should().BeEmpty( "{0} {1}: '{2}'", cls, lang, line);
            }
            string all = string.Join(" ", lines.Select(l => l.Trim()));
            foreach (var key in ((string[])typeof(CombatEngine).GetField("CombatTipKeys", SF)!.GetValue(null)!))
                all.Should().Contain(HintSystem.WordWrap(Loc.GetIn(lang, key), 77)[0], "{0}: the run drew {1}", cls, key);
        }
    }

    /// <summary>The action each tip is about, by the label the menus print for it.</summary>
    private static readonly (string Tip, string Label)[] TipActions =
    {
        ("combat.tip_power_attack", "combat.power_attack"),
        ("combat.tip_exact_strike", "combat.precise_strike"),
        ("combat.tip_taunt", "combat.taunt"),
        ("combat.tip_disarm", "combat.disarm_label"),
        ("combat.tip_hide", "combat.hide"),
    };

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void ATipAboutAnActionTheStandardMenuDoesNotList_NamesNoKey(string lang)
    {
        // a key on the menu is not enough: [I] is on it, but as Use Item, not Disarm
        var hero = Fresh(CharacterClass.Warrior);
        string menu;
        using (var s = new Scope(hero, Mode.Standard, lang))
        {
            var t = new TerminalEmulator(new MemoryStream(), s.Stream);
            var engine = new CombatEngine(t);
            var classInfo = (List<(string key, string name, bool available)>)Invoke(engine, "GetClassSpecificActions", hero)!;
            Invoke(engine, "ShowDungeonCombatMenuStandard", hero, false, false, classInfo, false, GodDomain.None);
            t.StreamWriterInternal?.Flush();
            menu = Strip(Encoding.UTF8.GetString(s.Stream.ToArray()));
        }
        foreach (var (tip, label) in TipActions)
        {
            menu.Should().NotContain(Loc.GetIn(lang, label).Trim(), "{0}: the standard menu does not list it, or this tip may name its key", label);
            BracketKeys(Loc.GetIn(lang, tip)).Should().BeEmpty("{0} {1}: '{2}'", tip, lang, Loc.GetIn(lang, tip));
        }
        // the potion tip's key is the menu's Use Item row
        var potionKey = BracketKeys(Loc.GetIn(lang, "combat.tip_healing_potions")).Single();
        menu.Split('\n').Single(l => l.Contains("[" + potionKey + "]")).Should().Contain(Loc.GetIn(lang, "combat.use_item_potions_short", hero.Healing, hero.MaxPotions).Trim());
    }

    [Fact]
    public void TheLowHpHint_NamesThePotionKeyOfTheCombatMenu()
    {
        var keys = DrawnMenuKeys(Fresh(CharacterClass.Warrior), Mode.Standard);
        foreach (var lang in Langs)
        foreach (var key in new[] { "hint.low_hp.msg", "hint.low_hp.msg.classic" })
        {
            var named = BracketKeys(Loc.GetIn(lang, key));
            named.Last().Should().Be("I", "{0} {1}: the potions in combat are under [I]", lang, key);
            keys.Should().Contain(named.Last());
        }
    }
}
