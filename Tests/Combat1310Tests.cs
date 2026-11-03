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
using UsurperRemake.Locations;
using UsurperRemake.Server;
using UsurperRemake.UI;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10: the combat items from the play test list (B12 enemy rank, G6 fight stamina, B16 text slips, G2 screen reader
/// tips, G3 and C3 the combat menu, G5 the Hungarian compact row, B14 small heals, C5 the death page).
/// </summary>
[Collection("SharedGameSingletons")]
public class Combat1310Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;

    private static object? Invoke(object target, string method, params object?[] args)
    {
        try { return target.GetType().GetMethod(method, F)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    /// <summary>Runs <paramref name="body"/> with the game language set, and puts the language back.</summary>
    private static void InLang(string lang, Action body)
    {
        var saved = GameConfig.Language;
        try { GameConfig.Language = lang; body(); }
        finally { GameConfig.Language = saved; }
    }

    public static readonly TheoryData<string> Langs = new() { "en", "es", "fr", "hu", "it" };

    // ------------------------------------------------------------------ B12: the enemy rank on Char.Combat.Enemies

    [Fact]
    public void CombatEnemies_CarryRank_BossChampionNormal_AndIsBossAsBefore()
    {
        var saved = SessionContext.Current;
        var stream = new MemoryStream();
        try
        {
            SessionContext.Current = WebFlagOff200Tests.Session("mud_gmcp", stream, new Character { Name2 = "Hero" });
            var foes = new List<Monster>
            {
                new() { Name = "Tomb Sentinel", Level = 5, HP = 300, MaxHP = 300, IsActive = true, IsBoss = true },
                new() { Name = "Wolf Champion", Level = 5, HP = 90, MaxHP = 90, IsActive = true, IsMiniBoss = true },
                new() { Name = "Wolf", Level = 5, HP = 40, MaxHP = 40, IsActive = true },
                new() { Name = "Boss Champion", Level = 5, HP = 40, MaxHP = 40, IsActive = true, IsBoss = true, IsMiniBoss = true },
            };
            GmcpBridge.EmitCombatEnemiesIfChanged(foes, 1);
            var json = WebMenu200Tests.Frames(stream.ToArray()).Where(f => f.Package == "Char.Combat.Enemies").Single().Json;
            var list = json.GetProperty("enemies").EnumerateArray().ToList();
            list.Select(e => e.GetProperty("rank").GetString()).Should().Equal("boss", "champion", "normal", "boss");
            list.Select(e => e.GetProperty("isBoss").GetBoolean()).Should().Equal(new[] { true, true, false, true },
                "isBoss stays IsBoss or IsMiniBoss for old pages and MUD clients");

            // the rank is in the change snapshot: a champion that becomes the boss is sent again, nothing else changed
            foes[1].IsBoss = true;
            GmcpBridge.EmitCombatEnemiesIfChanged(foes, 2);
            var frames = WebMenu200Tests.Frames(stream.ToArray()).Where(f => f.Package == "Char.Combat.Enemies").ToList();
            frames.Should().HaveCount(2, "a change of rank alone is sent");
            frames[1].Json.GetProperty("enemies")[1].GetProperty("rank").GetString().Should().Be("boss");
            GmcpBridge.EmitCombatEnemiesIfChanged(foes, 3);
            WebMenu200Tests.Frames(stream.ToArray()).Count(f => f.Package == "Char.Combat.Enemies").Should().Be(2, "no change, no frame");
        }
        finally { SessionContext.Current = saved; }
    }

    // ------------------------------------------------------------------ G6: fight stamina never above its maximum

    [Fact]
    public void CombatStamina_StartsAtTheMaximum_NotAFixed100()
    {
        var hero = new Character { Level = 2, Stamina = 7 };
        hero.MaxCombatStamina.Should().BeLessThan(100, "a low level character's maximum");
        hero.CurrentCombatStamina.Should().Be(hero.MaxCombatStamina, "a new character starts full, at its own maximum");
        var big = new Character { Level = 40, Stamina = 60 };
        big.CurrentCombatStamina.Should().Be(big.MaxCombatStamina, "and a maximum above 100 is full too");
    }

    [Fact]
    public void CombatStamina_ALoadedSave_ShowsItsMaximum()
    {
        var hero = new Character { Name1 = "Hero", Name2 = "Hero", Level = 2, Stamina = 7, BaseStamina = 7, HP = 50, MaxHP = 50, BaseMaxHP = 50 };
        hero.CurrentCombatStamina = 100;   // what every save loaded into before 1.3.10
        var data = (UsurperRemake.Systems.PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;
        data = JsonSerializer.Deserialize<UsurperRemake.Systems.PlayerData>(JsonSerializer.Serialize(data))!;
        Character loaded;
        try { loaded = (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        loaded.MaxCombatStamina.Should().BeLessThan(100);
        loaded.CurrentCombatStamina.Should().Be(loaded.MaxCombatStamina, "a load shows 78/78, never 100/78");
    }

    [Fact]
    public void CombatStamina_AMaximumThatDrops_LowersIt_AndPrisonShadowBoxingStopsAtTheMaximum()
    {
        var hero = new Character { Level = 10, Stamina = 30, BaseStamina = 30, HP = 100, MaxHP = 100, BaseMaxHP = 100 };
        hero.RecalculateStats();
        hero.CurrentCombatStamina = hero.MaxCombatStamina;
        long before = hero.MaxCombatStamina;
        hero.BaseStamina = 10;
        hero.RecalculateStats();
        hero.MaxCombatStamina.Should().BeLessThan(before);
        hero.CurrentCombatStamina.Should().Be(hero.MaxCombatStamina, "a dropped maximum lowers the current value");

        var prisoner = new Character { Level = 2, Stamina = 7, HP = 50, MaxHP = 50 };
        prisoner.CurrentCombatStamina = prisoner.MaxCombatStamina - 3;
        typeof(PrisonActivitySystem).GetMethod("PerformShadowBoxing", F)!.Invoke(PrisonActivitySystem.Instance, new object[] { prisoner });
        prisoner.CurrentCombatStamina.Should().Be(prisoner.MaxCombatStamina, "shadow boxing stops at the maximum, not at 100");
    }
    // ------------------------------------------------------------------ B16: two combat text slips

    [Theory]
    [MemberData(nameof(Langs))]
    public void FloorBoss_SpeaksItsPhrase_WithAVerb(string lang)
    {
        InLang(lang, () =>
        {
            foreach (var theme in new[] { DungeonTheme.Catacombs, DungeonTheme.Sewers, DungeonTheme.AbyssalVoid })
            {
                var boss = new Monster { Name = "Tomb Sentinel", Level = 5, HP = 100, MaxHP = 100, IsBoss = true, CanSpeak = false };
                DungeonLocation.GiveFloorBossPhrase(boss, theme);
                string text = string.Join(" ", CombatEngine.MonsterSaysRows(boss, boss.Phrase, ""));
                text.Should().Be(Loc.Get("combat.monster_says", MonsterNames.TheName(boss), boss.Phrase), "{0} {1}: the boss says its line", lang, theme);
                boss.PhraseInLanguage!().Should().Be(boss.Phrase);
            }
        });
    }

    private static readonly string[] GrazeKeys = { "combat.msg.monster.graze.0", "combat.msg.monster.graze.1", "combat.msg.monster.graze.2" };

    [Theory]
    [MemberData(nameof(Langs))]
    public void MonsterCritical_IsNeverFollowedByAGrazeLine(string lang)
    {
        InLang(lang, () =>
        {
            var grazes = GrazeKeys.Select(k => Loc.Get(k, "red", "Wolf")).ToList();
            var rng = new Random(1310);
            for (int i = 0; i < 60; i++)
            {
                string crit = CombatMessages.GetMonsterAttackMessage("Wolf", "red", 3, 1000, rng, critical: true);
                grazes.Should().NotContain(crit, "a critical hit does not barely touch you");
            }
            // a plain weak hit still grazes, and the seeded draw is one sample either way
            var a = new Random(7); var b = new Random(7);
            grazes.Should().Contain(CombatMessages.GetMonsterAttackMessage("Wolf", "red", 3, 1000, a));
            CombatMessages.GetMonsterAttackMessage("Wolf", "red", 3, 1000, b, critical: true);
            a.Next().Should().Be(b.Next(), "the critical floor draws no extra number");
        });
        Source("Scripts/Systems/CombatEngine.cs").Should().Contain("critical: monsterRoll.IsCriticalSuccess",
            "the monster attack passes its critical roll to the flavour line");
    }
    // ------------------------------------------------------------------ G2: screen reader tips keep their keys

    [Theory]
    [MemberData(nameof(Langs))]
    public void ScreenReaderTips_KeepEveryKeyInWords(string lang)
    {
        var keys = ((string[])typeof(CombatEngine).GetField("CombatTipKeys", S)!.GetValue(null)!).ToList();
        keys.AddRange(((Dictionary<CharacterClass, string[]>)typeof(CombatEngine).GetField("ClassSpecificTipKeys", S)!.GetValue(null)!).Values.SelectMany(v => v));
        int named = 0;
        InLang(lang, () =>
        {
            foreach (var key in keys)
            {
                string raw = Loc.Get(key, "I");
                string sr = CombatEngine.ScreenReaderTip(raw);
                sr.Should().NotContain("[").And.NotContain("]", "{0} {1}: no bracket for the reader", lang, key);
                foreach (Match m in Regex.Matches(raw, @"\[(\w+)\]"))
                {
                    named++;
                    Regex.IsMatch(sr, $@"(^|[^\w]){Regex.Escape(m.Groups[1].Value)}([^\w]|$)").Should().BeTrue(
                        "{0} {1}: the key {2} stays a word of its own in \"{3}\"", lang, key, m.Groups[1].Value, sr);
                }
            }
        });
        named.Should().BeGreaterThan(2, "the tips name keys");
        CombatEngine.ScreenReaderTip("TIP: [D]efend halves it.").Should().Be("TIP: D - Defend halves it.");
        Source("Scripts/Systems/CombatEngine.cs").Should().Contain("tip = ScreenReaderTip(tip);");
    }
    // ------------------------------------------------------------------ C5: the death page

    private static string Plain(byte[] bytes)
    {
        var b = Regex.Replace(Encoding.Latin1.GetString(bytes), "\u00FF\u00FA\u00C9.*?\u00FF\u00F0", "", RegexOptions.Singleline);
        var text = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(b));
        return Regex.Replace(text, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("hu", false)]
    [InlineData("en", true)]
    public async Task DeathBanner_IsPlainWords_AndSendsTheHpTheDeathLeft(string lang, bool screenReader)
    {
        var saved = SessionContext.Current;
        bool savedSr = GameConfig.ScreenReaderMode, savedPacing = Pacing.Disabled;
        var stream = new MemoryStream();
        try
        {
            Pacing.Disabled = true;
            var hero = new Character { Name2 = "Hero", HP = 1, MaxHP = 155 };
            SessionContext.Current = WebFlagOff200Tests.Session("mud_gmcp", stream, hero);
            GmcpBridge.EmitVitalsIfChanged(hero);   // the round's prompt: 1 of 155 after Death's Door
            hero.HP = -4;                            // the blow that killed
            GameConfig.ScreenReaderMode = screenReader;
            var saveLang = GameConfig.Language;
            GameConfig.Language = lang;
            try
            {
                var t = new TerminalEmulator(new MemoryStream(), stream);
                await (Task)typeof(CombatEngine).GetMethod("ShowDeathBanner", F)!.Invoke(new CombatEngine(t), new object?[] { hero, null })!;
                t.StreamWriterInternal?.Flush();
                string text = Plain(stream.ToArray());
                text.Should().Contain("  " + Loc.Get("death.you_died") + "\n", "the words are a plain line of their own");
                text.Should().NotContain("Y O U").And.NotContain("D I E D");
                var vitals = WebMenu200Tests.Frames(stream.ToArray()).Where(f => f.Package == "Char.Vitals").ToList();
                vitals.Last().Json.GetProperty("hp").GetInt64().Should().Be(0, "the death page shows the HP the death left, not the 1 of the last prompt");
                hero.HP.Should().Be(0);
            }
            finally { GameConfig.Language = saveLang; }
        }
        finally { SessionContext.Current = saved; GameConfig.ScreenReaderMode = savedSr; Pacing.Disabled = savedPacing; }
    }

    [Theory]
    [MemberData(nameof(Langs))]
    public void DeathShareLine_WrapsAtWordBounds_Within79(string lang)
    {
        InLang(lang, () =>
        {
            string share = Loc.Get("combat.share_death", "Ptesterwithalongername", "Paladin", 4, "Goblin Shaman of the Deep Catacombs", "1,234", 7);
            var rows = CombatEngine.ShareRows(share);
            rows.Should().HaveCountGreaterThan(1, "the line is longer than one row");
            rows.Should().OnlyContain(r => r.Length <= 79 && r.StartsWith("  "));
            string joined = string.Join(" ", rows.Select(r => r.Trim()));
            joined.Should().Be(Loc.Get("combat.share_label", share), "no word is split across rows");
        });
    }

    [Fact]
    public void LifeNotice_SaysALifeWasSpent_AndTheTempleHallShowsItOnce()
    {
        Loc.GetIn("en", "death.rez_remaining", 2, 3).Should().Be("A life was spent. Lives left: 2 of 3.");
        Source("Scripts/Systems/CombatEngine.cs").Should().Contain("result.Player.PendingLifeNotice = rezLeft == 0");
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        var temple = new TempleLocation(term, null!, UsurperRemake.GodSystemSingleton.Instance);
        var hero = new Character { Name1 = "Hero", Name2 = "Hero", HP = 77, MaxHP = 155, Level = 4 };
        hero.PendingLifeNotice = Loc.Get("death.rez_remaining", 2, 3);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(temple, hero);
        var draw = typeof(TempleLocation).GetMethod("DisplayMenu", F)!;
        ((Task)draw.Invoke(temple, new object[] { true })!).GetAwaiter().GetResult();
        term.StreamWriterInternal?.Flush();
        string first = Plain(output.ToArray());
        first.Should().Contain(Loc.Get("death.rez_remaining", 2, 3));
        hero.PendingLifeNotice.Should().BeNull("shown once");
        output.SetLength(0);
        ((Task)draw.Invoke(temple, new object[] { true })!).GetAwaiter().GetResult();
        term.StreamWriterInternal?.Flush();
        Plain(output.ToArray()).Should().NotContain(Loc.Get("death.rez_remaining", 2, 3), "not again on the next draw");
    }
    // ------------------------------------------------------------------ B14: the heal questions fit 79 columns

    [Theory]
    [MemberData(nameof(Langs))]
    public void HealQuestionsAndRefusal_Fit79Columns_ForEveryPlainHeal(string lang)
    {
        var heals = ClassAbilitySystem.GetAllAbilities().Where(CombatEngine.IsPlainHealAbility).ToList();
        heals.Select(a => a.Id).Should().Contain("lay_on_hands");
        InLang(lang, () =>
        {
            foreach (var a in heals)
            {
                CombatEngine.NoOneHurtRows(a).Should().OnlyContain(r => r.Length <= 79, "{0} {1}", lang, a.Id);
                string ask = $"{Loc.Get("combat.heal_small_confirm", a.DisplayName, 99999)} {Loc.Get("ui.yn_default_no").TrimEnd()}";
                UIHelper.WordWrap(ask, UIHelper.WrapWidth).Should().OnlyContain(r => r.Length <= 79);
            }
        });
        Source("Scripts/Systems/CombatEngine.cs").Should().Contain("foreach (var row in NoOneHurtRows(ability))");
    }
}
