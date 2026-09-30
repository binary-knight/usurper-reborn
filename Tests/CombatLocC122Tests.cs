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
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.2: the remaining combat files read in the player's language: the Rage cinematic, the Old God
/// narration and boss ability names (re-rendered for each group member), the world boss status, board
/// and spell list, and the ability and spell quickbar menus.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatLocC122Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    public static IEnumerable<object[]> AllLanguages() => Langs.Select(l => new object[] { l });

    private static (TerminalEmulator term, MemoryStream output) Term(string input = "")
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Strip(Encoding.UTF8.GetString(output.ToArray()));
    }

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static IEnumerable<string> Rows(string text) => text.Replace("\r", "").Split('\n');

    /// <summary>Writes a render to USURPER_EVIDENCE_DIR when set, for a by-eye check.</summary>
    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

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

    private static string Src(string file) => File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", file));

    // ---------- 1. the Rage cinematic ----------

    private static readonly string[] RageKeys =
    {
        "rage.world_holds_breath", "rage.presence_forms", "rage.vast", "rage.ancient", "rage.furious", "rage.strides_in",
        "rage.eyes_burning", "rage.silent_centuries", "rage.watching_ends", "rage.sees_every_choice", "rage.every_cheese",
        "rage.not_impressed", "rage.mortal_rumbles", "rage.lived_as_you_saw_fit", "rage.die_as_i_see_fit", "rage.raise_sword",
        "rage.he_laughs", "rage.laugh_end_of_age", "rage.blow_falls", "rage.just_one", "rage.unmade", "rage.sun_rises",
        "rage.no_one_remembers", "rage.record_erased", "rage.name_will_not_answer", "rage.disconnecting",
    };

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task RageCinematic_IsInThePlayersLanguage(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var (term, output) = Term();
            await RageEventSystem.RunRageEventAsync(null!, term, "");
            return Shown(term, output);
        });
        Capture($"combat-c-rage-{lang}.txt", text);
        foreach (var key in RageKeys)
            text.Should().Contain("  " + Loc.GetIn(lang, key), key);
        text.Should().Contain("  " + Loc.GetIn(lang, "rage.looks_upon_you", Loc.GetIn(lang, "rage.mortal")),
            "with no character the fallback name is translated too");
        text.Should().NotContain("He is not impressed").And.NotContain("Disconnecting").And.NotContain("He looks upon you");
    }

    // ---------- 2. Old God narration ----------

    private static readonly string[] OldGodKeys =
    {
        "old_god.appears_again", "old_god.veloura_sees_mira", "old_god.veloura_hesitates", "old_god.entrusts_relic",
        "old_god.noctura_escape_fracture", "old_god.noctura_escape_dissolves", "old_god.noctura_escape_for_now",
        "old_god.noctura_shadow_heal", "old_god.noctura_fair_fight", "old_god.noctura_betrayal_title",
        "old_god.noctura_shadow_falls_title", "old_god.noctura_xp_gained", "old_god.noctura_gold_found",
    };

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public void OldGodNarration_RendersInTheLanguage(string lang)
    {
        var shown = new StringBuilder();
        foreach (var key in OldGodKeys)
        {
            Loc.HasIn(lang, key).Should().BeTrue(key);
            string line = Loc.GetIn(lang, key, "Veloura");
            line.Should().NotBe(Loc.GetIn("en", key, "Veloura"), key);
            shown.AppendLine(line);
        }
        Capture($"combat-c-oldgod-{lang}.txt", shown.ToString());
        Loc.GetIn(lang, "old_god.appears_again", "Veloura").Should().Contain("Veloura");
        Loc.GetIn(lang, "old_god.noctura_shadow_heal", 250).Should().Contain("250");
    }

    [Fact]
    public void OldGodNarration_SourceUsesTheKeys()
    {
        string src = Src("OldGodBossSystem.cs");
        foreach (var key in OldGodKeys)
            src.Should().Contain($"Loc.Get(\"{key}\"", key);
        foreach (var english in new[] { "appears before you again", "Something flickers in the corruption", "entrusts you with a sacred relic",
                     "the world fractures around you", "restoring {healAmount} HP", "NOCTURA, THE SHADOW ASCENDANT", "\"THE SHADOW FALLS\"",
                     "Experience gained: {", "Gold found: {" })
            src.Should().NotContain(english);
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void NocturaBoxTitles_FitTheirBoxInEveryLanguage(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            foreach (var key in new[] { "old_god.noctura_betrayal_title", "old_god.noctura_shadow_falls_title" })
            {
                var (term, output) = Term();
                UIHelper.WriteBoxHeader(term, Loc.Get(key), "dark_magenta", 63);
                var rows = Rows(Shown(term, output)).Where(r => r.Length > 0).ToList();
                rows.Should().HaveCount(3);
                rows.Should().OnlyContain(r => r.Length == 65, $"{lang} {key}: the title fits inside the 63 column box");
            }
        }
        finally { GameConfig.Language = prev; }
    }

    // ---------- 3. Old God ability names, in each group member's language ----------

    private static BossCombatContext ThorgrimContext()
    {
        var ctx = new BossCombatContext();
        typeof(OldGodBossSystem).GetMethod("ConfigureBossPartyMechanics", F)!
            .Invoke(OldGodBossSystem.Instance, new object?[] { ctx, OldGodType.Thorgrim });
        return ctx;
    }

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 60, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human,
    };

    /// <summary>Thorgrim's AoE and channel on a leader's screen in `lang`, captured with its Loc recording.</summary>
    private static async Task<(LocRecording rec, string captured)> BossAbilityCapture(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var (term, _) = Term();
            var engine = new CombatEngine(term);
            var hero = Hero();
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
            var ctx = ThorgrimContext();
            engine.BossContext = ctx;
            var boss = new Monster { Name = "Thorgrim", Level = 55, HP = 50000, MaxHP = 50000, Strength = 100, IsBoss = true, IsActive = true };
            var result = new CombatResult { Player = hero, Monster = boss, Teammates = new List<Character>() };
            term.StartCapture();
            var rec = Loc.BeginRecording();
            try
            {
                await (Task)typeof(CombatEngine).GetMethod("ProcessBossAoE", F)!.Invoke(engine, new object?[] { boss, hero, result })!;
                boss.IsChanneling = true; boss.ChannelingRoundsLeft = 3; boss.ChannelingAbilityName = ctx.ChannelAbilityName;
                typeof(CombatEngine).GetMethod("ProcessBossChannel", F)!.Invoke(engine, new object?[] { boss, hero, result });
            }
            finally { Loc.EndRecording(); }
            return (rec, term.StopCapture()!);
        }
        finally { GameConfig.Language = prev; }
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task OldGodAbilityNames_AreInTheLeadersLanguage(string lang)
    {
        var ctx = ThorgrimContext();
        ctx.AoEAbilityName.Should().Be("old_god.ability.gavel_of_judgment");
        ctx.ChannelAbilityName.Should().Be("old_god.ability.final_verdict");
        var (_, captured) = await BossAbilityCapture(lang);
        string shown = Strip(captured);
        Capture($"combat-c-boss-ability-{lang}.txt", shown);
        shown.Should().Contain(Loc.GetIn(lang, "combat.boss_unleashes", "Thorgrim", Loc.GetIn(lang, "old_god.ability.gavel_of_judgment")));
        shown.Should().Contain(Loc.GetIn(lang, "combat.boss_channeling_continues", "Thorgrim", Loc.GetIn(lang, "old_god.ability.final_verdict"), 2));
        shown.Should().NotContain("Gavel of Judgment").And.NotContain("Final Verdict").And.NotContain("old_god.ability");
    }

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    public async Task OldGodAbilityNames_ReachEachGroupMemberInTheirLanguage(string lang)
    {
        var (rec, captured) = await BossAbilityCapture("hu");
        string shown = Strip(CombatEngine.CapturedInLanguage(rec, captured, lang, "Tester"));
        Capture($"combat-c-boss-ability-hu-to-{lang}.txt", shown);
        shown.Should().Contain(Loc.GetIn(lang, "combat.boss_unleashes", "Thorgrim", Loc.GetIn(lang, "old_god.ability.gavel_of_judgment")));
        shown.Should().Contain(Loc.GetIn(lang, "combat.boss_channeling_continues", "Thorgrim", Loc.GetIn(lang, "old_god.ability.final_verdict"), 2));
        shown.Should().NotContain(Loc.GetIn("hu", "old_god.ability.gavel_of_judgment"));
    }

    [Fact]
    public void BossAbilityLabel_ShowsAPlainNameAsIs()
    {
        CombatEngine.BossAbilityLabel("Doomfire").Should().Be("Doomfire");
        CombatEngine.BossAbilityLabel("").Should().Be("");
    }

    [Fact]
    public void OldGodAbilityNames_HaveAKeyInEveryLanguage()
    {
        foreach (OldGodType god in Enum.GetValues(typeof(OldGodType)))
        {
            var ctx = new BossCombatContext();
            typeof(OldGodBossSystem).GetMethod("ConfigureBossPartyMechanics", F)!.Invoke(OldGodBossSystem.Instance, new object?[] { ctx, god });
            foreach (var key in new[] { ctx.AoEAbilityName, ctx.ChannelAbilityName }.Where(k => k.Length > 0))
                foreach (var l in Langs)
                    Loc.HasIn(l, key).Should().BeTrue($"{god} {key} {l}");
        }
    }

    // ---------- 4. the world boss status, board and spell list ----------

    private static string RenderWorldBossStatus(bool screenReader)
    {
        var (term, output) = Term();
        var boss = new WorldBossInfo { BossName = "Test Boss", BossLevel = 80, MaxHP = 12_345_678_901, CurrentHP = 9_876_543_210, ExpiresAt = DateTime.UtcNow.AddHours(1) };
        var prevSr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.ScreenReaderMode = screenReader;
            typeof(WorldBossSystem).GetMethod("DrawBossStatusScreen", F)!
                .Invoke(WorldBossSystem.Instance, new object?[] { term, boss, null, null });
            var board = new List<WorldBossDamageEntry>
            {
                new() { PlayerName = "tester", DisplayName = "Tester", DamageDealt = 9_876_543_210 },
                new() { PlayerName = "longname", DisplayName = "Averyveryverylongna", DamageDealt = 1_234_567 },
            };
            typeof(WorldBossSystem).GetMethod("DrawLeaderboard", F)!
                .Invoke(WorldBossSystem.Instance, new object?[] { term, board, "tester" });
        }
        finally { GameConfig.ScreenReaderMode = prevSr; }
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task WorldBossStatusAndBoard_AreInThePlayersLanguage(string lang)
    {
        string bars = await WithLanguage(lang, () => Task.FromResult(RenderWorldBossStatus(false)));
        string sr = await WithLanguage(lang, () => Task.FromResult(RenderWorldBossStatus(true)));
        Capture($"combat-c-worldboss-{lang}.txt", bars + "\n----\n" + sr);
        string hpBar = new string('█', 16) + new string('░', 4);
        bars.Should().Contain("  " + Loc.GetIn(lang, "world_boss.hp_bar_line", hpBar, $"{9_876_543_210L:N0}", $"{12_345_678_901L:N0}", $"{80.0:F1}"));
        sr.Should().Contain("  " + Loc.GetIn(lang, "world_boss.hp_line", $"{9_876_543_210L:N0}", $"{12_345_678_901L:N0}", $"{80.0:F1}"));
        bars.Should().Contain("  " + Loc.GetIn(lang, "world_boss.leaderboard_row", 1, "Tester", $"{9_876_543_210L:N0}", $"{99.99:F1}")
            + $" {Loc.GetIn(lang, "world_boss.mvp_tag")} ({Loc.GetIn(lang, "world_boss.you_tag")})");
        bars.Should().NotContain(" dmg ").And.NotContain("  HP: ");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public async Task WorldBossStatusAndBoard_StayWithin80Columns(string lang)
    {
        string text = await WithLanguage(lang, () => Task.FromResult(RenderWorldBossStatus(false) + RenderWorldBossStatus(true)));
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(80, $"{lang} row \"{row}\"");
    }

    private static async Task<string> RenderWorldBossSpells()
    {
        var (term, output) = Term("\n");
        var cleric = Cleric();
        var method = typeof(WorldBossSystem).GetMethod("ProcessSpellCast", F)!;
        await (Task<long>)method.Invoke(WorldBossSystem.Instance, new object?[] { cleric, term, new WorldBossDefinition(), new WorldBossRuntimeData(), new Random(1) })!;
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task WorldBossSpellList_IsInThePlayersLanguage(string lang)
    {
        string text = await WithLanguage(lang, RenderWorldBossSpells);
        Capture($"combat-c-worldboss-spells-{lang}.txt", text);
        var first = SpellSystem.GetSpellInfo(CharacterClass.Cleric, 1)!;
        string name = await WithLanguage(lang, () => Task.FromResult(first.DisplayName));
        text.Should().Contain("  " + Loc.GetIn(lang, "world_boss.spell_option", 1, name, first.ManaCost));
    }

    // ---------- 5. the ability and spell quickbar menus ----------

    private static Character Warrior()
    {
        var p = new Character { Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 100, HP = 500, MaxHP = 500, AI = CharacterAI.Human };
        for (int i = 0; i < 9; i++) p.Quickbar[i] = null;
        p.Quickbar[0] = "power_strike";
        return p;
    }

    private static async Task<string> RenderAbilityMenu(Character player)
    {
        var (term, output) = Term("1\n0\nX\n");
        await ClassAbilitySystem.ShowAbilityLearningMenu(player, term);
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task AbilityMenu_TagsAreInThePlayersLanguage(string lang)
    {
        var player = Warrior();
        string text = await WithLanguage(lang, () => RenderAbilityMenu(player));
        Capture($"combat-c-ability-menu-{lang}.txt", text);
        var strike = ClassAbilitySystem.GetAbility("power_strike")!;
        text.Should().Contain(Loc.GetIn(lang, "ability.cost_st", ClassAbilitySystem.GetEffectiveStaminaCost(strike)));
        text.Should().Contain($"--- {Loc.GetIn(lang, "ui.empty").ToLower()} ---");
        var other = ClassAbilitySystem.GetAvailableAbilities(player).First(a => a.Id != "power_strike");
        text.Should().Contain(Loc.GetIn(lang, "ability.level_tag", other.LevelRequired));
        text.Should().NotContain(" ST)").And.NotContain("--- empty ---");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public async Task AbilityMenu_PickListStaysWithin80Columns(string lang)
    {
        string text = await WithLanguage(lang, () => RenderAbilityMenu(Warrior()));
        // the slot pick list: "  [a] Name  (cost)", no description
        var pick = Rows(text).Where(r => Regex.IsMatch(r, @"^  \[[a-z]\] ") && r.TrimEnd().EndsWith(")")).ToList();
        pick.Should().NotBeEmpty();
        foreach (var row in pick)
            row.Length.Should().BeLessOrEqualTo(80, $"{lang} row \"{row}\"");
    }

    [Fact]
    public void AbilityResult_MessagesAreKeyed()
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = "hu";
            ClassAbilitySystem.UseAbility(Warrior(), "no_such_ability").Message.Should().Be(Loc.GetIn("hu", "combat.unknown_ability"));
            var strike = ClassAbilitySystem.GetAbility("power_strike")!;
            ClassAbilitySystem.UseAbility(Warrior(), "power_strike").Message.Should().Be(Loc.GetIn("hu", "combat.monster_uses_ability", "Tester", strike.DisplayName));
        }
        finally { GameConfig.Language = prev; }
    }

    private static Character Cleric()
    {
        var p = new Character { Name1 = "cleric", Name2 = "Cleric", Class = CharacterClass.Cleric, Level = 100, HP = 500, MaxHP = 500, Mana = 5000, MaxMana = 5000, AI = CharacterAI.Human };
        for (int i = 0; i < 9; i++) p.Quickbar[i] = null;
        p.Quickbar[1] = "power_strike";
        foreach (var s in SpellSystem.GetAllSpellsForClass(CharacterClass.Cleric).Where(s => s.Level <= 3))
        {
            typeof(SpellLearningSystem).GetMethod("EnsureSpellSlot", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object?[] { p, s.Level });
            p.Spell[s.Level - 1][0] = true;
        }
        return p;
    }

    private static async Task<string> RenderSpellMenu(Character player)
    {
        var (term, output) = Term("1\n0\nX\n");
        await SpellLearningSystem.ShowSpellLearningMenu(player, term);
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task SpellMenu_TagsAreInThePlayersLanguage(string lang)
    {
        var player = Cleric();
        string text = await WithLanguage(lang, () => RenderSpellMenu(player));
        Capture($"combat-c-spell-menu-{lang}.txt", text);
        text.Should().Contain(Loc.GetIn(lang, "spell_learning.tag_ability"));
        var known = SpellSystem.GetAllSpellsForClass(CharacterClass.Cleric).First(s => s.Level == 1);
        int cost = SpellSystem.CalculateManaCost(known, player);
        text.Should().Contain(Loc.GetIn(lang, "ability.cost_mp", cost));
        text.Should().Contain($"{Loc.GetIn(lang, "ability.cost_mp", cost)} {Loc.GetIn(lang, "spell_learning.type." + known.SpellType.ToLowerInvariant())}");
        text.Should().NotContain("(ability)").And.NotContain($"({cost} MP) {known.SpellType}");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public async Task SpellMenu_PickListStaysWithin80Columns(string lang)
    {
        string text = await WithLanguage(lang, () => RenderSpellMenu(Cleric()));
        var types = new[] { "attack", "heal", "buff", "debuff", "summon", "transform", "escape" }.Select(t => Loc.GetIn(lang, "spell_learning.type." + t)).ToList();
        var pick = Rows(text).Where(r => Regex.IsMatch(r, @"^  \[[a-z]\] ") && types.Any(t => r.TrimEnd().EndsWith(" " + t))).ToList();
        pick.Should().NotBeEmpty();
        foreach (var row in pick)
            row.Length.Should().BeLessOrEqualTo(80, $"{lang} row \"{row}\"");
    }

    [Fact]
    public void SpellTypes_HaveAKeyInEveryLanguage()
    {
        var types = Enum.GetValues<CharacterClass>().SelectMany(c => SpellSystem.GetAllSpellsForClass(c)).Select(s => s.SpellType).Distinct().ToList();
        types.Should().Contain("Escape");
        foreach (var t in types)
            foreach (var l in Langs)
                Loc.HasIn(l, "spell_learning.type." + t.ToLowerInvariant()).Should().BeTrue($"{t} {l}");
        SpellLearningSystem.SpellTypeLabel("Mystery").Should().Be("Mystery");
    }

    // ---------- 6. class ability names and descriptions ----------

    private static List<ClassAbilitySystem.ClassAbility> AllAbilities() =>
        ClassAbilitySystem.GetAllAbilities().ToList();

    [Fact]
    public void AbilityNamesAndDescriptions_HaveAKeyInEveryLanguage()
    {
        var all = AllAbilities();
        all.Count.Should().Be(179);
        foreach (var a in all)
        {
            Loc.GetIn("en", $"ability.{a.Id}.name").Should().Be(a.Name, "the English key matches the table");
            Loc.GetIn("en", $"ability.{a.Id}.desc").Should().Be(a.Description, "the English key matches the table");
            foreach (var l in Langs)
            {
                Loc.HasIn(l, $"ability.{a.Id}.name").Should().BeTrue($"{a.Id} name {l}");
                Loc.HasIn(l, $"ability.{a.Id}.desc").Should().BeTrue($"{a.Id} desc {l}");
                Loc.GetIn(l, $"ability.{a.Id}.desc").Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void AbilityNames_FitTheirMenuColumn(string lang)
    {
        // the quickbar menus pad names to 24 columns (Level Master, companion skills) and 22 (spell quickbar)
        foreach (var a in AllAbilities().Where(a => a.Id != "maelstrom_faithful" || lang != "en"))
            Loc.GetIn(lang, $"ability.{a.Id}.name").Length.Should().BeLessOrEqualTo(24, $"{lang} {a.Id} \"{Loc.GetIn(lang, $"ability.{a.Id}.name")}\"");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task AbilityMenu_NamesAndDescriptionsAreInThePlayersLanguage(string lang)
    {
        var player = Warrior();
        string text = await WithLanguage(lang, () => RenderAbilityMenu(player));
        Capture($"combat-c-ability-names-{lang}.txt", text);
        var strike = ClassAbilitySystem.GetAbility("power_strike")!;
        text.Should().Contain(Loc.GetIn(lang, "ability.power_strike.name")).And.Contain(Loc.GetIn(lang, "ability.power_strike.desc"));
        var other = ClassAbilitySystem.GetAvailableAbilities(player).First(a => a.Id != "power_strike");
        text.Should().Contain(Loc.GetIn(lang, $"ability.{other.Id}.name")).And.Contain(Loc.GetIn(lang, $"ability.{other.Id}.desc"));
        text.Should().NotContain(strike.Name).And.NotContain(strike.Description).And.NotContain(other.Description);
        string used = await WithLanguage(lang, () => Task.FromResult(ClassAbilitySystem.UseAbility(Warrior(), "power_strike").Message));
        used.Should().Be(Loc.GetIn(lang, "combat.monster_uses_ability", "Tester", Loc.GetIn(lang, "ability.power_strike.name")));
    }

    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    public void AbilityNames_ReachEachGroupMemberInTheirLanguage(string lang)
    {
        var prev = GameConfig.Language;
        LocRecording rec;
        string captured;
        try
        {
            GameConfig.Language = "hu";
            var (term, _) = Term();
            term.StartCapture();
            rec = Loc.BeginRecording();
            try
            {
                var strike = ClassAbilitySystem.GetAbility("power_strike")!;
                term.WriteLine(Loc.Get("combat.teammate_uses_ability_stamina", "Lyra", strike.DisplayName, strike.StaminaCost));
            }
            finally { Loc.EndRecording(); }
            captured = term.StopCapture()!;
        }
        finally { GameConfig.Language = prev; }
        string shown = Strip(CombatEngine.CapturedInLanguage(rec, captured, lang, "Tester"));
        shown.Should().Contain(Loc.GetIn(lang, "combat.teammate_uses_ability_stamina", "Lyra", Loc.GetIn(lang, "ability.power_strike.name"), 15));
        shown.Should().NotContain(Loc.GetIn("hu", "ability.power_strike.name"));
    }

    [Fact]
    public void AbilityDisplayName_FallsBackToTheTableWithoutAKey()
    {
        var custom = new ClassAbilitySystem.ClassAbility { Id = "not_a_real_ability", Name = "Custom", Description = "Custom text." };
        custom.DisplayName.Should().Be("Custom");
        custom.DisplayDescription.Should().Be("Custom text.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
