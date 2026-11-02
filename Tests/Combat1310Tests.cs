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
}
