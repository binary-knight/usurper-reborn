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
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.2: the second half of CombatEngine.cs reads in the player's language, and the lines that reach
/// other group members (the captured status tick, the loot notice, the follower's cannot-act line, hits,
/// deaths, rewards and god messages) are built in each recipient's own language.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatLocB122Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static (CombatEngine engine, TerminalEmulator term, MemoryStream output) Engine()
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        return (new CombatEngine(term), term, output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Strip(Encoding.UTF8.GetString(output.ToArray()));
    }

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;]*[A-Za-z]", "");

    /// <summary>Writes a render to USURPER_EVIDENCE_DIR when set, for a by-eye check.</summary>
    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 30, HP = 500, MaxHP = 500,
        AI = CharacterAI.Human, Healing = 3,
    };

    private static async Task<T> WithLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            return await body();
        }
        finally { GameConfig.Language = prev; }
    }

    private static Task<T> WithLanguage<T>(string lang, Func<T> body) => WithLanguage(lang, () => Task.FromResult(body()));

    public static IEnumerable<object[]> AllLanguages() => new[] { "en", "es", "fr", "hu", "it" }.Select(l => new object[] { l });

    private static string Src() => File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));

    // ---------- 1. the captured status tick, re-rendered per recipient ----------

    /// <summary>A status tick written on a Hungarian leader's screen, captured with its Loc recording.</summary>
    private static (LocRecording rec, string captured) HungarianStatusCapture()
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = "hu";
            var (_, term, _) = Engine();
            term.StartCapture();
            var rec = Loc.BeginRecording();
            try
            {
                term.WriteLine(Loc.Get("combat.status_effects_label"), "yellow");
                term.WriteLine(Loc.Get("status.tick_poison", "Tester", 7), "green");
                term.WriteLine(Loc.Get("status.end_effect_fades", "Tester", Loc.Get("status.stunned")), "white");
                term.WriteLine("Kobold 12/40", "white");
            }
            finally { Loc.EndRecording(); }
            return (rec, term.StopCapture()!);
        }
        finally { GameConfig.Language = prev; }
    }

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    public void CapturedStatus_ReachesEachMemberInTheirLanguage(string lang)
    {
        var (rec, captured) = HungarianStatusCapture();
        string shown = Strip(CombatEngine.CapturedInLanguage(rec, captured, lang, "Tester"));
        Capture($"combat-b-captured-status-{lang}.txt", shown);
        shown.Should().Contain(Loc.GetIn(lang, "combat.status_effects_label"));
        shown.Should().Contain(Loc.GetIn(lang, "status.tick_poison", "Tester", 7));
        shown.Should().Contain(Loc.GetIn(lang, "status.end_effect_fades", "Tester", Loc.GetIn(lang, "status.stunned")),
            "a status name inside a status line follows the recipient's language too");
        shown.Should().Contain("Kobold 12/40", "text that did not come from Loc is kept");
        shown.Should().NotContain(Loc.GetIn("hu", "status.tick_poison", "Tester", 7)).And.NotContain("Kábult");
    }

    [Fact]
    public void CapturedStatus_HungarianRecipientGetsTheCaptureUnchanged()
    {
        var (rec, captured) = HungarianStatusCapture();
        rec.Render(captured, "hu").Should().Be(captured);
        Strip(captured).Should().Contain("Tester 7 méregsebzést szenved!");
    }

    [Fact]
    public void LocRecording_StopsAtEndRecording()
    {
        var rec = Loc.BeginRecording();
        Loc.Get("status.stunned");
        Loc.EndRecording();
        Loc.Get("status.poisoned");
        rec.Render(Loc.GetIn("en", "status.poisoned"), "hu").Should().Be("Poisoned", "a lookup after EndRecording is not recorded");
    }

    [Fact]
    public void BothStatusCaptures_GoThroughTheLocalizedGroupSend()
    {
        string src = Src();
        src.Should().Contain("BroadcastGroupLocalized(result, lang => CapturedInLanguage(statusRecording, statusOutput, lang, player.DisplayName));");
        src.Should().Contain("BroadcastGroupLocalized(result, lang => CapturedInLanguage(monsterStatusRecording, statusOutput, lang, player.DisplayName));");
        src.Should().NotContain("ConvertToThirdPerson(statusOutput, player.DisplayName)");
    }

    // ---------- 2. the loot notice on the other members' terminals ----------

    private static async Task<CombatEngine.LocalizedLines> LeaderLootLines(Item loot, Character finder)
    {
        return await WithLanguage("en", async () =>
        {
            var (engine, _, _) = Engine();
            var lines = new CombatEngine.LocalizedLines();
            var monster = new Monster { Name = "Kobold", Level = 3, HP = 0, MaxHP = 10 };
            await (Task)typeof(CombatEngine).GetMethod("RenderEquipment", F)!.Invoke(engine, new object?[] { loot, monster, finder, lines })!;
            return lines;
        });
    }

    private static Item CursedStaffless() => new()
    {
        Name = "Test Sword", Type = ObjType.Weapon, Attack = 9, Strength = 3, Value = 100, IsIdentified = true, IsCursed = true,
    };

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task LootNotice_IsInTheRecipientsLanguage(string lang)
    {
        var mage = Hero();
        mage.Class = CharacterClass.Magician;
        var lines = await LeaderLootLines(CursedStaffless(), mage);
        string shown = Strip(CombatEngine.GroupLootMessage(lang, "Tester", lines));
        Capture($"combat-b-loot-notice-{lang}.txt", shown);
        shown.Should().Contain(Loc.GetIn(lang, "combat.loot_broadcast_for", "Tester"));
        shown.Should().Contain(Loc.GetIn(lang, "combat.loot_item_found").Trim());
        shown.Should().Contain(Loc.GetIn(lang, "combat.loot_attack_power", 9));
        shown.Should().Contain(Loc.GetIn(lang, "combat.loot_bonuses", $"{Loc.GetIn(lang, "ui.stat_str")} +3"));
        shown.Should().Contain(Loc.GetIn(lang, "combat.loot_group_cursed"));
        shown.Should().Contain(Loc.GetIn(lang, "combat.loot_group_spell_req", "Staff"));
        foreach (var english in new[] { "ITEM FOUND!", "WARNING: CURSED!", "NOTE: Requires", "Attack Power", "Bonuses", "[Group loot]" })
            shown.Should().NotContain(english);
    }

    [Fact]
    public async Task LootNotice_UnidentifiedAndEnglishKeepTheirWording()
    {
        var mage = Hero();
        mage.Class = CharacterClass.Magician;
        var lines = await LeaderLootLines(CursedStaffless(), mage);
        string en = CombatEngine.GroupLootMessage("en", "Tester", lines);
        en.Should().Contain("\u001b[1;37mITEM FOUND!\u001b[0m").And.Contain("\u001b[31m  WARNING: CURSED!\u001b[0m")
            .And.Contain("\u001b[31m  NOTE: Requires Staff for spells\u001b[0m");
        var unid = await LeaderLootLines(new Item { Name = "Test Plate", Type = ObjType.Body, Armor = 4, IsIdentified = false }, Hero());
        string it = Strip(CombatEngine.GroupLootMessage("it", "Tester", unid));
        it.Should().Contain("(non identificato)").And.NotContain("(Unidentified)");
    }

    // ---------- 3. the follower's cannot-act line ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task CannotActLine_IsInTheFollowersLanguage(string lang)
    {
        // the leader plays in Italian; the follower's line is built in the follower's language
        string line = await WithLanguage("it", () => CombatEngine.CannotActLine(lang, "Tester", StatusEffect.Stunned));
        Capture($"combat-b-cannot-act-{lang}.txt", line);
        line.Should().Be(Loc.GetIn(lang, "combat.teammate_status_prevented", "Tester", Loc.GetIn(lang, "status.stunned").ToLower()));
        line.Should().NotContain("stordito").And.NotContain("cannot act");
    }

    [Fact]
    public void CannotActLine_OnTheFollowersTerminalUsesTheirLanguage()
    {
        string src = Src();
        src.Should().Contain("remoteTerminal.WriteLine(CannotActLine(remoteLang, teammate.DisplayName, preventingStatus), \"yellow\");");
        src.Should().Contain("remoteTerminal.WriteLine(tickRecording.Render(msg, remoteLang), color);");
        src.Should().NotContain("remoteTerminal.WriteLine(prevented");
    }

    // ---------- 4. hits, deaths, rewards and god lines sent to other players ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public void GroupHitAndDeathLines_AreInTheRecipientsLanguage(string lang)
    {
        string you = CombatEngine.GroupLine(lang, "\u001b[31m", "combat.group_hits_you", "Kobold", 12, 30, 50);
        string ally = CombatEngine.GroupLine(lang, "\u001b[31m", "combat.group_hits_ally", "Kobold", "Lyra", 12);
        string fallen = CombatEngine.GroupYouHaveFallenLine(lang, "Kobold");
        string slain = CombatEngine.GroupDeathLine(lang, "combat.group_slain_by", "Lyra", "Kobold");
        string all = Strip(string.Join("\n", you, ally, fallen, slain));
        Capture($"combat-b-group-hits-{lang}.txt", all);
        all.Should().Contain(Loc.GetIn(lang, "combat.group_hits_you", "Kobold", 12, 30, 50))
            .And.Contain(Loc.GetIn(lang, "world_boss.struck_you_down", "Kobold"))
            .And.Contain(Loc.GetIn(lang, "combat.group_you_have_fallen"));
        foreach (var english in new[] { "hits you for", "hits Lyra for", "YOU HAVE FALLEN", "struck you down", "has been slain by" })
            all.Should().NotContain(english);
    }

    [Fact]
    public void GroupHitAndDeathLines_KeepTheirEnglish()
    {
        CombatEngine.GroupLine("en", "\u001b[31m", "combat.group_hits_you", "Kobold", 12, 30, 50)
            .Should().Be("\u001b[31m  Kobold hits you for 12 damage! (30/50 HP)\u001b[0m");
        CombatEngine.GroupYouHaveFallenLine("en", "Kobold")
            .Should().Be("\u001b[1;31m  ══ YOU HAVE FALLEN ══\u001b[0m\n\u001b[31m  Kobold has struck you down!\u001b[0m");
        CombatEngine.GroupStarLine("en", "\u001b[1;33m", "combat.group_boss_defeated_one", "Tester", "Kobold", 1)
            .Should().Be("\u001b[1;33m  *** Tester defeated Kobold in 1 round! ***\u001b[0m");
        CombatEngine.GroupStarLine("en", "\u001b[1;33m", "combat.group_boss_defeated_many", "Tester", "Kobold", 4)
            .Should().Be("\u001b[1;33m  *** Tester defeated Kobold in 4 rounds! ***\u001b[0m");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public void GroupRewardMessage_IsInTheGroupedPlayersLanguage(string lang)
    {
        string msg = Strip(CombatEngine.GroupRewardMessage(lang, false, "Lyra", 1234, 56, 0.5f, 11, true));
        Capture($"combat-b-group-reward-{lang}.txt", msg);
        msg.Should().Contain(Loc.GetIn(lang, "combat.group_your_rewards", "Lyra"))
            .And.Contain(Loc.GetIn(lang, "combat.xp_label", 1234.ToString("N0")))
            .And.Contain(Loc.GetIn(lang, "combat.group_gap_penalty", 50))
            .And.Contain(Loc.GetIn(lang, "combat.group_level_up", 11))
            .And.Contain(Loc.GetIn(lang, "combat.group_levelup_restored"));
        foreach (var english in new[] { "YOUR REWARDS", "Experience gained", "Gold gained", "gap penalty", "LEVEL UP!", "Stats increased" })
            msg.Should().NotContain(english);
    }

    [Fact]
    public void GroupRewardMessage_KeepsItsEnglish()
    {
        CombatEngine.GroupRewardMessage("en", false, "Lyra", 1234, 56, 0.5f, 11, true).Should().Be(
            "\u001b[1;32m\n  ═══ YOUR REWARDS (Lyra) ═══\u001b[0m\n\u001b[33m  Experience gained: 1,234\u001b[0m\n\u001b[33m  Gold gained: 56\u001b[0m" +
            "\n\u001b[33m  (Group level gap penalty: 50% XP rate)\u001b[0m" +
            "\n\u001b[1;35m  ★ LEVEL UP! You are now Level 11! ★\u001b[0m\n\u001b[35m  HP restored to full. Stats increased!\u001b[0m");
        CombatEngine.GroupRewardMessage("en", true, "Lyra", 5, 6, 1f, 2, false)
            .Should().Be("\u001b[1;32m\n  --- YOUR REWARDS (Lyra) ---\u001b[0m\n\u001b[33m  Experience gained: 5\u001b[0m\n\u001b[33m  Gold gained: 6\u001b[0m");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public void GodLines_AreInTheGodPlayersLanguage(string lang)
    {
        var two = new List<Monster> { new() { Name = "Kobold" }, new() { Name = "Orc" } };
        string sac = CombatEngine.GodSacrificeLine(lang, "Tester", CombatEngine.MonsterDescIn(lang, two), 1500);
        string rank = CombatEngine.GodRankLine(lang, "Demigod");
        string all = Strip(sac + "\n" + rank);
        Capture($"combat-b-god-{lang}.txt", all);
        all.Should().Contain(Loc.GetIn(lang, "combat.god_sacrificed_in_name", "Tester", Loc.GetIn(lang, "combat.god_monsters_count", 2), 1500.ToString("N0")));
        all.Should().Contain(Loc.GetIn(lang, "daily.divine_power_grows", "Demigod").Trim());
        all.Should().NotContain("sacrificed").And.NotContain("2 monsters").And.NotContain("divine power grows");
        CombatEngine.GodSacrificeLine("en", "Tester", "Kobold", 1500)
            .Should().Be("\u001b[33m  ✦ Tester sacrificed Kobold in your name (+1,500 divine power) ✦\u001b[0m");
        CombatEngine.GodRankLine("en", "Demigod").Should().Be("\u001b[1;36m  ✦ Your divine power grows! You are now a Demigod! ✦\u001b[0m");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task GroupedPlayerTurn_IsAnnouncedInTheLeadersLanguage(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            var (engine, term, output) = Engine();
            typeof(CombatEngine).GetMethod("AnnounceGroupedPlayerTurn", F)!.Invoke(engine, new object?[] { new Character { Name2 = "Lyra" }, new CombatResult() });
            return Shown(term, output);
        });
        Capture($"combat-b-turn-{lang}.txt", text);
        text.Should().Contain($"── {Loc.GetIn(lang, "combat.group_leader_turn", "Lyra")} ──").And.NotContain("Lyra's turn");
    }

    [Fact]
    public void LinesForOtherPlayers_AreNoLongerBuiltInTheLeadersLanguage()
    {
        string src = Src();
        foreach (var english in new[] { "hits you for {actualDamage}", "YOU HAVE FALLEN", "Combat over. Waiting", "YOUR REWARDS",
                     "Experience gained: {playerExp", "You sacrifice the remains", "sacrificed {", "Your divine power grows!",
                     "has reached Level {groupedPlayer", "dark powers! ═══", "WARNING: CURSED!", "(Unidentified)", "NOTE: Requires",
                     "has ascended to the rank", "{teammate.DisplayName}'s turn", "string framed" })
            src.Should().NotContain(english);
        src.Should().Contain("GroupSystem.Instance!.BroadcastToGroupSessionsLocalized(group,")
            .And.Contain("GroupSystem.Instance!.BroadcastToGroupSessionsLocalized(deathGroup,")
            .And.Contain("lang => GroupLootMessage(lang, recipientName, lootBroadcastSb)")
            .And.Contain("string gpLang = gpSession.Context?.Language ?? \"en\";");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
