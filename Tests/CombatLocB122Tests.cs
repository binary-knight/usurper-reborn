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

    // ---------- 5. the ability menus ----------

    private static (CombatEngine engine, TerminalEmulator term, MemoryStream output) EngineWithInput(string input)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output);
        return (new CombatEngine(term), term, output);
    }

    /// <summary>Both ability menus for a level 100 character of `cls` with no stamina left, so every row carries a need tag.</summary>
    private static async Task<string> RenderAbilityMenus(CharacterClass cls)
    {
        var hero = Hero();
        hero.Class = cls;
        hero.Level = 100;
        hero.CurrentCombatStamina = 0;
        hero.Mana = 0;
        hero.MaxMana = 100;
        var monster = new Monster { Name = "Kobold", Level = 3, HP = 10, MaxHP = 10, IsActive = true };
        var (e1, t1, o1) = EngineWithInput("\n");
        await (Task)typeof(CombatEngine).GetMethod("ShowAbilityMenuAndExecute", F)!
            .Invoke(e1, new object?[] { hero, new List<Monster> { monster }, new CombatResult() })!;
        var (e2, t2, o2) = EngineWithInput("\n");
        await (Task)typeof(CombatEngine).GetMethod("ExecuteUseAbility", F)!
            .Invoke(e2, new object?[] { hero, monster, new CombatResult() })!;
        return Shown(t1, o1) + Shown(t2, o2);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task AbilityMenus_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () => RenderAbilityMenus(CharacterClass.Warrior));
        Capture($"combat-b-ability-menu-{lang}.txt", text);
        text.Should().Contain(Loc.GetIn(lang, "combat.ability_cost_stamina", 10).Split(' ')[1]);
        text.Should().Contain(Loc.GetIn(lang, "combat.ability_tag_need_stamina", "X").Split('X')[0]);
        text.Should().Contain(Loc.GetIn(lang, "combat.ability_tag_need_stamina_have", "X", 0).Split('X')[1]);
        text.Should().NotContain(" stamina]").And.NotContain("stamina, have").And.NotMatchRegex(" - [0-9]+ stamina");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public async Task AbilityMenuRows_StayWithin80Columns(string lang)
    {
        foreach (CharacterClass cls in Enum.GetValues(typeof(CharacterClass)))
        {
            string text = await WithLanguage(lang, () => RenderAbilityMenus(cls));
            foreach (var row in Rows(text).Where(r => Regex.IsMatch(r, "^  [0-9]+\\. ")))
                row.Length.Should().BeLessOrEqualTo(80, $"{lang} {cls} row \"{row}\"");
        }
    }

    private static string[] Rows(string text) => text.Replace("\r", "").Split('\n');

    // ---------- 6. the aid ally and heal target lists ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task AidAllyLists_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var hero = Hero();
            hero.Healing = 0;
            hero.ManaPotions = 2;
            var ally = new Character { Name1 = "ally", Name2 = "Lyra", Class = CharacterClass.Cleric, Level = 10, HP = 40, MaxHP = 100, Mana = 50, MaxMana = 50 };
            var (engine, term, output) = EngineWithInput("1\n0\n");
            typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { ally });
            await (Task<CombatAction?>)typeof(CombatEngine).GetMethod("HandleHealAlly", F)!.Invoke(engine, new object?[] { hero, new List<Monster>() })!;
            var (e2, t2, o2) = EngineWithInput("0\n");
            typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(e2, new List<Character> { ally });
            await (Task<int?>)typeof(CombatEngine).GetMethod("SelectHealTarget", F)!.Invoke(e2, new object?[] { hero })!;
            var (e3, t3, o3) = EngineWithInput("0\n");
            typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(e3, new List<Character> { ally });
            await (Task<int?>)typeof(CombatEngine).GetMethod("SelectBuffTarget", F)!.Invoke(e3, new object?[] { hero })!;
            return Shown(term, output) + Shown(t2, o2) + Shown(t3, o3);
        });
        Capture($"combat-b-aid-ally-{lang}.txt", text);
        text.Should().Contain($"═══ {Loc.GetIn(lang, "combat.aid_ally_title")} ═══");
        text.Should().Contain(Loc.GetIn(lang, "combat.ally_mp_row", 1, "Lyra", 50, 50, 100, Loc.GetIn(lang, "combat.full_status")));
        text.Should().Contain(Loc.GetIn(lang, "combat.ally_hp_row", 1, "Lyra", 40, 100, 40, ""));
        text.Should().Contain(Loc.GetIn(lang, "combat.ally_hp_row_short", 1, "Lyra", 40, 100));
        text.Should().NotContain("AID ALLY").And.NotContain("- HP:").And.NotContain("(Full)");
    }

    // ---------- 7. a monster hitting a companion ----------

    /// <summary>The literal pieces of a localized line around its arguments, for lines with rolled numbers.</summary>
    private static IEnumerable<string> Pieces(string lang, string key, params object[] args)
        => Regex.Split(Loc.GetIn(lang, key, args), "#").Select(p => p.Trim()).Where(p => p.Length > 2);

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task MonsterHittingACompanion_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var (engine, term, output) = Engine();
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, Hero());
            var companion = new Character { Name1 = "ally", Name2 = "Lyra", Class = CharacterClass.Warrior, Level = 10, HP = 5000, MaxHP = 5000, Defence = 5 };
            companion.ApplyStatus(StatusEffect.Reflecting, 3);
            var monster = new Monster { Name = "Kobold", Level = 20, HP = 1, MaxHP = 400, Strength = 200, IsActive = true };
            await (Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
                .Invoke(engine, new object?[] { monster, companion, new CombatResult(), null })!;
            return Shown(term, output);
        });
        Capture($"combat-b-companion-hit-{lang}.txt", text);
        foreach (var piece in Pieces(lang, "combat.monster_hits_companion_hp", "#", "Lyra", "#", "#"))
            text.Should().Contain(piece);
        foreach (var piece in Pieces(lang, "combat.tidal_barrier_reflects", "Lyra", "#", "Kobold"))
            text.Should().Contain(piece);
        text.Should().NotContain(" HP)").And.NotContain("tidal barrier").And.NotContain("damage vs");
    }

    // ---------- 8. the first kill box, the boss and death summaries ----------

    private static async Task<string> RenderSummaries(bool screenReader)
    {
        var prevSr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.ScreenReaderMode = screenReader;
            var hero = Hero();
            hero.Class = CharacterClass.Magician;
            var monster = new Monster { Name = "Kobold King", Level = 12, HP = 0, MaxHP = 900, IsBoss = true };
            var ally = new Character { Name1 = "ally", Name2 = "Lyra", Level = 10, HP = 50, MaxHP = 100 };
            var result = new CombatResult { Player = hero, Monster = monster, CurrentRound = 7, TotalDamageDealt = 12345 };
            result.Teammates = new List<Character> { ally };
            var (engine, term, output) = EngineWithInput("\n\n\n\n");
            await (Task)typeof(CombatEngine).GetMethod("ShowFirstKillBonus", F)!.Invoke(engine, new object?[] { hero, term })!;
            await (Task)typeof(CombatEngine).GetMethod("ShowBossKillSummary", F)!.Invoke(engine, new object?[] { result, 4321L, 99L })!;
            await (Task)typeof(CombatEngine).GetMethod("ShowDeathSummary", F)!.Invoke(engine, new object?[] { result })!;
            return Shown(term, output);
        }
        finally { GameConfig.ScreenReaderMode = prevSr; }
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task FirstKillBossAndDeathSummaries_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () => RenderSummaries(false));
        Capture($"combat-b-summaries-{lang}.txt", text);
        string cls = Loc.GetIn(lang, "class.magician");
        text.Should().Contain(Loc.GetIn(lang, "combat.first_blood_title")).And.Contain(Loc.GetIn(lang, "combat.first_blood_message"))
            .And.Contain(Loc.GetIn(lang, "combat.first_blood_bonus", GameConfig.FirstKillGoldBonus));
        text.Should().Contain(Loc.GetIn(lang, "combat.boss_summary_title"))
            .And.Contain(Loc.GetIn(lang, "combat.boss_summary_line", "Tester", 30, cls, "Kobold King"))
            .And.Contain(Loc.GetIn(lang, "combat.boss_summary_rounds_many", 7, 12345.ToString("N0")))
            .And.Contain(Loc.GetIn(lang, "combat.boss_summary_earned", 4321.ToString("N0"), 99))
            .And.Contain(Loc.GetIn(lang, "combat.share_label", Loc.GetIn(lang, "combat.share_boss_allies", "Tester", cls, 30, "Kobold King", 7, 1, 12345.ToString("N0"))));
        text.Should().Contain(Loc.GetIn(lang, "combat.death_story_title"))
            .And.Contain(Loc.GetIn(lang, "combat.death_story_who", "Tester", 30, cls))
            .And.Contain(Loc.GetIn(lang, "combat.death_story_fell", 12, "Kobold King"))
            .And.Contain(Loc.GetIn(lang, "combat.death_story_companions", "Lyra"));
        foreach (var english in new[] { "You slew your first", "Bonus reward", "BOSS KILL SUMMARY", "Fought alongside", "Earned ", "Share: ",
                     "DEATH STORY", "fell on Floor", "They explored", "Their companions", " the Lv", "Magician" })
            text.Should().NotContain(english);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public async Task SummaryBoxes_KeepTheirWidth(string lang)
    {
        string text = await WithLanguage(lang, () => RenderSummaries(false));
        foreach (var row in Rows(text).Where(r => r.Contains('║') || r.Contains('╔') || r.Contains('╚') || r.Contains('╠')))
        {
            if (row.Contains('╚') || row.Contains('╔') || row.Contains('╠') || row.TrimEnd().EndsWith("║"))
                row.TrimEnd().Length.Should().Be(54, $"{lang} row \"{row}\"");
        }
        Loc.GetIn(lang, "combat.berserker_rage_title").Length.Should().BeLessOrEqualTo(40, "the berserker box is 40 wide");
        Loc.GetIn(lang, "combat.companion_sacrifice_title").Length.Should().BeLessOrEqualTo(52, "the sacrifice box is 52 wide");
        string sr = await WithLanguage(lang, () => RenderSummaries(true));
        sr.Should().Contain($"--- {Loc.GetIn(lang, "combat.boss_summary_title")} ---").And.Contain($"--- {Loc.GetIn(lang, "combat.death_story_title")} ---");
    }

    [Fact]
    public async Task Summaries_EnglishKeepsItsWording()
    {
        string text = await WithLanguage("en", () => RenderSummaries(false));
        text.Should().Contain("  ║            ★  FIRST BLOOD!  ★                    ║")
            .And.Contain("  ║  You slew your first monster!                    ║")
            .And.Contain("  ║  The dungeons hold many more challenges...       ║")
            .And.Contain("  Tester the Lv30 Magician defeated Kobold King")
            .And.Contain("  in 7 rounds, dealing 12,345 total damage.")
            .And.Contain("  Share: Tester the Magician (Lv30) defeated Kobold King in 7 rounds with 1 allies! [12,345 dmg] #UsurperReborn")
            .And.Contain("  fell on Floor 12 to Kobold King.");
    }

    // ---------- 9. resurrection, death count and dark bargain ----------

    private static string RenderDeathLines(int deaths, int rezLeft)
    {
        var (engine, term, output) = Engine();
        var hero = Hero();
        hero.PlaythroughDeaths = deaths;
        typeof(CombatEngine).GetMethod("ShowDeathCountWarning", F)!.Invoke(engine, new object?[] { hero });
        typeof(CombatEngine).GetMethod("ShowDivineRestore", F)!.Invoke(engine, new object?[] { 250, 500L, rezLeft, 3 });
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task DeathCountAndResurrectionLines_AreLocalized(string lang)
    {
        int max = GameConfig.MaxPlaythroughDeaths;
        string text = await WithLanguage(lang, () => string.Join("\n", RenderDeathLines(max, 0), RenderDeathLines(max - 1, 2),
            RenderDeathLines(max - 2, 1), RenderDeathLines(0, 1)));
        Capture($"combat-b-death-lines-{lang}.txt", text);
        text.Should().Contain(Loc.GetIn(lang, "combat.final_death_warning")).And.Contain(Loc.GetIn(lang, "combat.final_death_times", max))
            .And.Contain(Loc.GetIn(lang, "combat.deaths_remaining_one", max - 1, max, 1))
            .And.Contain(Loc.GetIn(lang, "combat.deaths_remaining_many", max - 2, max, 2))
            .And.Contain(Loc.GetIn(lang, "combat.deaths_used", 0, max))
            .And.Contain(Loc.GetIn(lang, "death.divine_restore", 250, 500)).And.Contain(Loc.GetIn(lang, "death.final_warning2"))
            .And.Contain(Loc.GetIn(lang, "death.rez_remaining", 2, 3));
        foreach (var english in new[] { "FINAL DEATH WARNING", "You have died", "deaths used", "Divine intervention", "Resurrections remaining", "death(s)" })
            text.Should().NotContain(english);
    }

    [Fact]
    public async Task DarkBargainStatName_IsLocalized()
    {
        var (hu1, hu2) = await WithLanguage("hu", () =>
        {
            int a = 2, b = 4;
            var c = Hero();
            return (CombatEngine.ApplyDarkBargainStatLoss(c, 0, ref a), CombatEngine.ApplyDarkBargainStatLoss(c, 5, ref b));
        });
        hu1.Should().Be(Loc.GetIn("hu", "ui.stat_strength"));
        hu2.Should().Be(Loc.GetIn("hu", "combat.stat_max_hp_loss", 20)).And.NotContain("HP");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task BerserkerRage_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var (engine, term, output) = Engine();
            var hero = Hero();
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
            var monster = new Monster { Name = "Kobold", Level = 1, HP = 1, MaxHP = 1, IsActive = true };
            await (Task)typeof(CombatEngine).GetMethod("ExecuteFightToDeath", F)!.Invoke(engine, new object?[] { hero, monster, new CombatResult { Player = hero, Monster = monster } })!;
            return Shown(term, output);
        });
        Capture($"combat-b-rage-{lang}.txt", text);
        text.Should().Contain(Loc.GetIn(lang, "combat.berserker_rage_title")).And.Contain($"═══ {Loc.GetIn(lang, "combat.rage_round", 1)} ═══");
        text.Should().NotContain("BERSERKER RAGE!").And.NotContain("RAGE ROUND");
    }

    [Fact]
    public void NewsAndFallbacks_UseLocKeys()
    {
        string src = Src();
        foreach (var english in new[] { "\"the dungeons\"", "\"an unknown end\"", "\"unknown forces\"", "\"the unknown\"", "\"Hero\"", "\"Ally\"",
                     "fell forever to", "COMPANION SACRIFICE", "is afflicted with {", "echo dissipates...\"" })
            src.Should().NotContain(english);
        src.Should().Contain("Loc.Get(\"combat.news_permadeath\", displayName, finalLevel, GameConfig.GetLocalizedClassName(player.Class), killerName)");
    }

    // ---------- 10. boss mechanics ----------

    private static async Task<string> RenderBossMechanics()
    {
        var (engine, term, output) = Engine();
        var hero = Hero();
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        var ctx = new BossCombatContext { EnrageRound = 8, AoEDamage = 50, TankAbsorptionRate = 0.5, ChannelDamage = 60 };
        engine.BossContext = ctx;
        var boss = new Monster { Name = "Maelketh", Level = 50, HP = 5000, MaxHP = 5000, Strength = 100, IsBoss = true, IsActive = true };
        var result = new CombatResult { Player = hero, Monster = boss, Teammates = new List<Character>() };
        var check = typeof(CombatEngine).GetMethod("CheckBossEnrage", F)!;
        check.Invoke(engine, new object?[] { ctx, boss, 4 });
        check.Invoke(engine, new object?[] { ctx, boss, 6 });
        typeof(CombatEngine).GetMethod("CreateSpectralSoldiers", F)!.Invoke(engine, new object?[] { 2, 50 });
        boss.IsChanneling = true; boss.ChannelingRoundsLeft = 3; boss.ChannelingAbilityName = "Doomfire";
        var channel = typeof(CombatEngine).GetMethod("ProcessBossChannel", F)!;
        channel.Invoke(engine, new object?[] { boss, hero, result });
        boss.ChannelingRoundsLeft = 1;
        channel.Invoke(engine, new object?[] { boss, hero, result });
        await (Task)typeof(CombatEngine).GetMethod("ProcessBossAoE", F)!.Invoke(engine, new object?[] { boss, hero, result })!;
        var immune = typeof(CombatEngine).GetMethod("ApplyPhaseImmunity", F)!;
        immune.Invoke(engine, new object?[] { boss, true, 2 });
        immune.Invoke(engine, new object?[] { boss, false, 2 });
        var healer = new Character { Name1 = "healer", Name2 = "Lyra", Class = CharacterClass.Cleric, Level = 40, HP = 100, MaxHP = 100 };
        hero.DoomCountdown = 2;
        typeof(CombatEngine).GetMethod("TryHealerCleanse", F)!.Invoke(engine, new object?[] { healer, hero, result });
        hero.CorruptionStacks = 5;
        typeof(CombatEngine).GetMethod("TryHealerCleanse", F)!.Invoke(engine, new object?[] { healer, hero, result });
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task BossMechanics_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, RenderBossMechanics);
        Capture($"combat-b-boss-{lang}.txt", text);
        text.Should().Contain(Loc.GetIn(lang, "combat.boss_enrage_countdown", "Maelketh", 4))
            .And.Contain(Loc.GetIn(lang, "combat.boss_power_builds", "Maelketh"))
            .And.Contain($"*** {Loc.GetIn(lang, "combat.boss_enrage_in", 2)} ***")
            .And.Contain(Loc.GetIn(lang, "combat.spectral_soldiers", 2))
            .And.Contain(Loc.GetIn(lang, "combat.boss_channeling_continues", "Maelketh", "Doomfire", 2))
            .And.Contain($"*** {Loc.GetIn(lang, "combat.boss_unleashes", "Maelketh", "Doomfire")} ***")
            .And.Contain($"*** {Loc.GetIn(lang, "combat.boss_unleashes", "Maelketh", Loc.GetIn(lang, "combat.boss_aoe_default"))} ***")
            .And.Contain(Loc.GetIn(lang, "combat.boss_immune_physical", "Maelketh", 2))
            .And.Contain(Loc.GetIn(lang, "combat.use_physical_attacks"))
            .And.Contain(Loc.GetIn(lang, "combat.dispels_doom", "Lyra", "Tester"));
        foreach (var piece in Pieces(lang, "combat.takes_damage_absorbing", "Tester", "#"))
            text.Should().Contain(piece);
        foreach (var piece in Pieces(lang, "combat.cleanses_corruption", "Lyra", "#", "Tester", "#"))
            text.Should().Contain(piece);
        foreach (var english in new[] { "grows impatient", "power builds", "ENRAGE in", "Spectral Soldiers", "continues channeling", "unleashes",
                     "Devastating Blast", "[ABSORBING]", "becomes immune", "Use magical", "Use physical", "dispels DOOM", "cleanses", " takes " })
            text.Should().NotContain(english);
    }

    // ---------- 11. the follower's party line and spell list, in the follower's language ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public void FollowerPartyLine_IsInTheFollowersLanguage(string lang)
    {
        var prevLangOf = CombatEngine.LanguageOf;
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";   // the leader
            CombatEngine.LanguageOf = _ => lang;
            var (engine, term, output) = Engine();
            var self = new Character { Name1 = "f", Name2 = "Follower", HP = 50, MaxHP = 100 };
            var leader = new Character { Name1 = "l", Name2 = "Leader", HP = 100, MaxHP = 100 };
            var down = new Character { Name1 = "d", Name2 = "Lyra", HP = 0, MaxHP = 100 };
            typeof(CombatEngine).GetMethod("RenderFollowerPartyLine", F)!.Invoke(engine, new object?[] { term, self, leader, new List<Character> { down } });
            string text = Shown(term, output);
            Capture($"combat-b-follower-party-{lang}.txt", text);
            text.Should().StartWith($"  {Loc.GetIn(lang, "combat.party_label")} {Loc.GetIn(lang, "combat.party_you")} 50/100 (50%)");
            text.Should().Contain($"Leader {Loc.GetIn(lang, "party.tag_leader")}").And.Contain(Loc.GetIn(lang, "combat.party_down", "Lyra"));
            text.Should().NotContain("Party:").And.NotContain("(leader)").And.NotContain("DOWN");
        }
        finally { CombatEngine.LanguageOf = prevLangOf; GameConfig.Language = prev; }
    }

    // ---------- 12. teammate experience, alignment flavour, quickbar labels ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task TeammateExperienceLines_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            var (engine, term, output) = Engine();
            var mate = new Character { Name1 = "mate", Name2 = "Lyra", Class = CharacterClass.Warrior, Level = 2, HP = 50, MaxHP = 50, Experience = 0 };
            typeof(CombatEngine).GetMethod("AwardTeammateExperience", F)!.Invoke(engine, new object?[] { new List<Character> { mate }, 100000L, term, 30 });
            var hero = Hero();
            var mate2 = new Character { Name1 = "mate2", Name2 = "Orin", Class = CharacterClass.Warrior, Level = 2, HP = 50, MaxHP = 50, Experience = 0 };
            var mates = new List<Character> { mate2 };
            typeof(CombatEngine).GetMethod("DistributeTeamSlotXP", F)!.Invoke(engine, new object?[] { hero, mates, 100000L, term, CombatEngine.ResolveTeamXPShares(hero, mates) });
            return Shown(term, output);
        });
        Capture($"combat-b-teammate-xp-{lang}.txt", text);
        text.Should().Contain(Loc.GetIn(lang, "combat.teammate_leveled_up", "Lyra", 3)).And.Contain(Loc.GetIn(lang, "combat.teammate_leveled_up", "Orin", 3));
        foreach (var piece in Pieces(lang, "combat.catch_up_label", "#"))
            text.Should().Contain(piece);
        foreach (var piece in Pieces(lang, "combat.teammate_xp_share_progress", "Orin", "#", "#", "#", "#", ""))
            text.Should().Contain(piece);
        text.Should().NotContain("leveled up").And.NotContain("catch-up");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task AlignmentFlavourAndQuickbarLabels_AreLocalized(string lang)
    {
        var (desc, label) = await WithLanguage(lang, () =>
        {
            var (engine, _, _) = Engine();
            var dark = Hero();
            dark.Darkness = 1000;
            dark.Chivalry = 0;
            var target = new Monster { Name = "Kobold", Level = 3, HP = 10, MaxHP = 10 };
            var bonus = ((long, string))typeof(CombatEngine).GetMethod("GetAlignmentBonusDamage", F)!.Invoke(engine, new object?[] { dark, target, 100L })!;
            var hero = Hero();
            hero.Level = 100;
            var ability = ClassAbilitySystem.GetClassAbilities(CharacterClass.Warrior).First(a => a.ManaCost == 0 && a.LevelRequired <= 100);
            hero.Quickbar = new List<string> { ability.Id };
            var actions = (System.Collections.IList)typeof(CombatEngine).GetMethod("GetQuickbarActions", F)!.Invoke(engine, new object?[] { hero })!;
            var first = ((string key, string slotId, string displayName, bool available))actions[0]!;
            return (bonus.Item2, (Name: ability.DisplayName, ability.StaminaCost, first.displayName));
        });
        Capture($"combat-b-align-quickbar-{lang}.txt", desc + "\n" + label.displayName);
        new[] { "combat.align_evil_drain", "combat.align_dark_bonus" }.Select(k => Loc.GetIn(lang, k, 10))
            .Should().Contain(desc, "an evil or dark character gets a localized flavour line");
        label.displayName.Should().Be(Loc.GetIn(lang, "combat.qb_stamina", label.Name, label.StaminaCost));
        label.displayName.Should().NotContain(" ST)");
    }

    // ---------- 13. titles, the identify list and the ancestral heal ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task TitlesAndSpellLines_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var (engine, term, output) = EngineWithInput("\n\n\n");
            var hero = Hero();
            var foe = new Character { Name1 = "foe", Name2 = "Rival", Class = CharacterClass.Warrior, Level = 30, HP = 500, MaxHP = 500 };
            await (Task)typeof(CombatEngine).GetMethod("ShowPvPIntroduction", F)!.Invoke(engine, new object?[] { hero, foe, new CombatResult { Player = hero } })!;
            var monster = new Monster { Name = "Kobold", Level = 3, HP = 10, MaxHP = 10, IsActive = true };
            typeof(CombatEngine).GetMethod("ProcessSpellCasting", F)!.Invoke(engine, new object?[] { hero, monster, new CombatResult { Player = hero } });
            hero.Inventory.Add(new Item { Name = "Old Ring", Type = ObjType.Fingers, Attack = 1, Armor = 2 });
            typeof(CombatEngine).GetMethod("HandleSpecialSpellEffect", F)!.Invoke(engine, new object?[] { hero, monster, "identify", 0 });
            var mate = new Character { Name1 = "mate", Name2 = "Lyra", HP = 10, MaxHP = 100 };
            hero.HP = hero.MaxHP;
            typeof(CombatEngine).GetMethod("ApplyAncestralGuidanceHealing", F)!.Invoke(engine, new object?[] { hero, 40L, new CombatResult { Teammates = new List<Character> { mate } } });
            return Shown(term, output);
        });
        Capture($"combat-b-titles-{lang}.txt", text);
        text.Should().Contain($"═══ {Loc.GetIn(lang, "combat.pvp_title")} ═══").And.Contain($"═══ {Loc.GetIn(lang, "combat.spell_casting_title")} ═══")
            .And.Contain(Loc.GetIn(lang, "combat.identify_item_row", "Old Ring", ObjType.Fingers, 1, 2))
            .And.Contain(Loc.GetIn(lang, "combat.ally_healed_for", "Lyra", 40));
        text.Should().NotContain("PLAYER FIGHT").And.NotContain("Spell Casting").And.NotContain("(Type:").And.NotContain("is healed for");
    }

    [Fact]
    public void RemainingNewsAndLines_UseLocKeys()
    {
        string src = Src();
        foreach (var english in new[] { "spared {opponentName}'s life", "has achieved Level {teammate", "\"your opponent\"", "falls into a magical slumber",
                     "Searing Totem blasts {", "the enemy's weakness lies bare", "private void ShowGroupCombatMenu(", "{ \"[A]ttack\" }", "(CD:{" })
            src.Should().NotContain(english);
        src.Should().Contain("NewsSystem.Instance?.Newsy(false, Loc.Get(\"combat.news_spared\", result.Player.DisplayName, opponentName, location));");
        src.Should().Contain("terminal.Write($\"={Loc.GetIn(followerLang, \"combat.follower_spell_cost\", sp.DisplayName, cost)} \");");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
