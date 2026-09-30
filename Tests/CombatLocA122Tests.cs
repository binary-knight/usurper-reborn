using System;
using System.Collections.Generic;
using System.IO;
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
/// v1.2.2: the first half of CombatEngine.cs reads in the player's language: the lines sent to the
/// group (ambush, boss phase, turn, deaths, retreat), the compact BBS menus, the standard menu's status
/// and speed rows, the poison coating menu, the dodge line, the boss kill news and the teammate
/// elemental procs.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatLocA122Tests
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
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

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

    /// <summary>A Random that always rolls low, so every chance based proc fires.</summary>
    private sealed class LowRandom : Random
    {
        protected override double Sample() => 0.0;
        public override double NextDouble() => 0.0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    // ---------- 1. the lines sent to the group, in each recipient's language ----------

    [Fact]
    public void GroupLines_AreInTheRecipientsLanguage()
    {
        foreach (var lang in new[] { "hu", "it" })
        {
            string ambush = CombatEngine.GroupStarLine(lang, "\u001b[1;31m", "combat.group_ambush_full");
            ambush.Should().Contain(Loc.GetIn(lang, "combat.group_ambush_full")).And.NotContain("Monsters strike before");
            string partial = CombatEngine.GroupStarLine(lang, "\u001b[1;33m", "combat.group_ambush_partial", 2, 3);
            partial.Should().Contain(Loc.GetIn(lang, "combat.group_ambush_partial", 2, 3)).And.NotContain("PARTIAL AMBUSH");
            string phase = CombatEngine.GroupStarLine(lang, "\u001b[1;35m", "combat.group_boss_phase", "Maelketh", 2, 40);
            phase.Should().Contain(Loc.GetIn(lang, "combat.group_boss_phase", "Maelketh", 2, 40)).And.NotContain("enters Phase");
            string prevails = CombatEngine.GroupLine(lang, "\u001b[1;33m", "combat.group_fell_prevails", "Tester");
            prevails.Should().NotContain("the party prevails");
            string slain = CombatEngine.GroupDeathLine(lang, "combat.group_slain", "Tester");
            slain.Should().Contain(Loc.GetIn(lang, "combat.group_slain", "Tester")).And.NotContain("has been slain");
            string retreat = CombatEngine.GroupRetreatLine(lang);
            retreat.Should().NotContain("RETREAT").And.NotContain("The party retreats");
            Capture($"combat-a-group-{lang}.txt", string.Join("\n", ambush, partial, phase, prevails, slain, retreat));
        }
        CombatEngine.GroupLine("hu", "\u001b[1;33m", "combat.group_fell_prevails", "Tester")
            .Should().Contain("Tester elesett a csatában, de a csapat győzött!");
        CombatEngine.GroupRetreatLine("it").Should().Contain("RITIRATA").And.Contain("Il gruppo si ritira dal combattimento!");
        CombatEngine.GroupStarLine("hu", "\u001b[1;31m", "combat.group_ambush_full")
            .Should().NotBe(CombatEngine.GroupStarLine("it", "\u001b[1;31m", "combat.group_ambush_full"), "each recipient gets their own language");
    }

    [Fact]
    public void GroupLines_KeepColourIndentAndDecorationInEnglish()
    {
        CombatEngine.GroupStarLine("en", "\u001b[1;33m", "combat.group_ambush_partial", 2, 3)
            .Should().Be("\u001b[1;33m  *** PARTIAL AMBUSH! 2 of 3 monsters strike first! ***\u001b[0m");
        CombatEngine.GroupRetreatLine("en")
            .Should().Be("\u001b[1;33m  ══ RETREAT ══\u001b[0m\n\u001b[33m  The party retreats from combat!\u001b[0m");
        CombatEngine.GroupDeathLine("en", "combat.group_fallen", "Tester")
            .Should().Be("\u001b[1;31m  Tester has fallen! The party fights on!\u001b[0m");
    }

    [Fact]
    public void TheGroupBroadcasts_GoThroughTheLocalizedGroupSend()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        foreach (var english in new[] { "The party sensed the ambush", "PARTIAL AMBUSH!", "AMBUSH! Monsters strike",
                     "{player.DisplayName}'s turn", "has been slain!\\u001b", "has fallen! The party fights on!", "The party retreats from combat!",
                     "fell in battle, but the party prevails!", "hesitates, confused by internal contradictions!\\u001b" })
            src.Should().NotContain(english);
        foreach (var key in new[] { "group_ambush_sensed", "group_ambush_partial", "group_ambush_full", "group_boss_phase",
                     "group_leader_turn", "group_fell_prevails" })
            src.Should().Contain($"\"combat.{key}\"");
        Regex.Matches(src, "BroadcastGroupDeathLine\\(result, \"combat\\.group_(slain|fallen)\"").Count.Should().Be(2);
        src.Should().Contain("BroadcastGroupLocalized(result, GroupRetreatLine)");
        src.Should().Contain("GroupLine(lang, \"\\u001b[36m\", \"combat.boss_confused\", monster.Name)");
    }

    // ---------- 2. the compact BBS menus ----------

    private static string RenderBbsMenus()
    {
        var (engine, term, output) = Engine();
        var hero = Hero();
        hero.PoisonVials = 2;
        var monster = new Monster { Name = "Kobold", Level = 3, HP = 10, MaxHP = 10, IsActive = true };
        typeof(CombatEngine).GetMethod("ShowCombatMenuBBS", F)!.Invoke(engine, new object?[] { hero, monster, null, false });
        var classInfo = new List<(string key, string name, bool available)> { ("1", "Slash", true) };
        typeof(CombatEngine).GetMethod("ShowDungeonCombatMenuBBS", F)!
            .Invoke(engine, new object?[] { hero, true, true, classInfo, false, GodDomain.None });
        return Shown(term, output);
    }

    [Fact]
    public async Task BbsMenus_AreLocalized_Hungarian()
    {
        string text = await WithLanguage("hu", RenderBbsMenus);
        Capture("combat-a-bbs-menu-hu.txt", text);
        text.Should().Contain("[A]Támadás").And.Contain("[D]Védekezés").And.Contain("[R]Menekülés")
            .And.Contain("[P]Erő").And.Contain("[E]Pontos").And.Contain("[T]Gúnyolódás").And.Contain("[S]Adatok")
            .And.Contain("[H]Társgyógyítás").And.Contain("Méreg(2)").And.Contain("Képességek(1)").And.Contain("[SPD]Norm");
        foreach (var english in new[] { "ttack", "efend", "etreat", "ower ", "xact", "aunt", "tats", "ealAlly", "Poison(", "Skills(", "Nrml" })
            text.Should().NotContain(english);
    }

    [Fact]
    public async Task BbsMenus_AreLocalized_Spanish_DroppingTheHotkeyLetter()
    {
        string text = await WithLanguage("es", RenderBbsMenus);
        Capture("combat-a-bbs-menu-es.txt", text);
        text.Should().Contain("[A]tacar").And.Contain("[D]efender").And.Contain("[R]etirarse").And.Contain("[P]otente")
            .And.Contain("[E]xacto").And.Contain("[T]Provocar").And.Contain("[S]Estado").And.Contain("Veneno(2)");
        foreach (var english in new[] { "[A]ttack", "[D]efend ", "[R]etreat", "[P]ower", "[E]xact ", "[T]aunt", "[S]tats", "Poison(", "Nrml" })
            text.Should().NotContain(english);
    }

    [Fact]
    public async Task BbsMenus_EnglishIsUnchanged()
    {
        string text = await WithLanguage("en", RenderBbsMenus);
        text.Should().Contain(" [A]ttack [D]efend ").And.Contain("[R]etreat ").And.Contain("[P]ower [E]xact ")
            .And.Contain("[T]aunt ").And.Contain("[B]Poison(2) ").And.Contain("[AUTO] [SPD]Nrml [S]tats")
            .And.Contain("[H]ealAlly ").And.Contain("[1-9]Skills(1) ");
    }

    // ---------- 3. the standard menu's status and speed rows ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task StandardMenuStatusRow_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            var (engine, term, output) = Engine();
            var hero = Hero();
            hero.ApplyStatus(StatusEffect.Poisoned, 2);
            var monster = new Monster { Name = "Kobold", Level = 3, HP = 10, MaxHP = 10, IsActive = true };
            typeof(CombatEngine).GetMethod("ShowCombatMenuStandard", F)!.Invoke(engine, new object?[] { hero, monster, null, false });
            return Shown(term, output);
        });
        Capture($"combat-a-standard-menu-{lang}.txt", text);
        text.Should().NotContain("Status: ").And.NotContain("Your HP");
        text.Should().Contain(Loc.GetIn(lang, "combat.pvp_your_hp", 500, 500));
        string prefix = Loc.GetIn(lang, "combat.status_prefix");
        text.Should().Contain("║ " + prefix);
        text.Should().Contain("[SPD]  ");
        // the status row keeps its width
        foreach (var line in text.Split('\n'))
            if (line.Contains(prefix)) line.TrimEnd('\r').Length.Should().Be(41);
    }

    // ---------- 4. the poison coating menu ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task PoisonMenu_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var (engine, term, output) = Engine();
            var hero = Hero();
            hero.PoisonVials = 1;
            hero.RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream());   // grouped: picks without a prompt
            await (Task)typeof(CombatEngine).GetMethod("ExecuteCoatBlade", F)!.Invoke(engine, new object?[] { hero, new CombatResult() })!;
            hero.PoisonCoatingCombats.Should().BeGreaterThan(0);
            string t = Shown(term, output);
            t.Should().Contain(Loc.Get("combat.poison_glistens_combats", hero.PoisonCoatingCombats));
            return t;
        });
        Capture($"combat-a-poison-{lang}.txt", text);
        text.Should().NotContain(" combats)").And.NotContain("deadly sheen");
    }

    [Fact]
    public async Task PoisonAlreadyCoated_IsLocalized()
    {
        string hu = await WithLanguage("hu", () => Loc.Get("combat.poison_already_coated", Loc.Get("combat.poison_coating_remaining", "X", 3)));
        hu.Should().Be("A pengéd már be van kenve ezzel: X (még 3 harc).");
        string es = await WithLanguage("es", () => Loc.Get("combat.poison_coating_remaining", "X", 3));
        es.Should().Be("X (quedan 3 combates)");
    }

    // ---------- 5. the dodge line and the boss kill news ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task DodgeAndBossNews_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
            Loc.Get("combat.you_dodge_chance", "Kobold", 12) + "\n" + Loc.Get("combat.news_boss_defeated", "Tester", "Maelketh"));
        text.Should().Contain("Kobold").And.Contain("12%").And.Contain("Maelketh");
        text.Should().NotContain("% dodge").And.NotContain("defeated the boss");
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().Contain("Loc.Get(\"combat.you_dodge_chance\", monster.Name, StatEffectsSystem.GetDodgeChance(player.Agility))");
        src.Should().Contain("Loc.Get(\"combat.news_boss_defeated\", bossKillerName, result.Monster.Name)");
    }

    // ---------- 6. a teammate's elemental procs ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task TeammateEnchantProcs_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            var (engine, term, output) = Engine();
            typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new LowRandom());
            int id = EquipmentDatabase.RegisterDynamic(new Equipment
            {
                Name = "Storm Blade", Slot = EquipmentSlot.MainHand, WeaponPower = 10, MinLevel = 1,
                HasFireEnchant = true, HasLightningEnchant = true,
            });
            var ally = new Character { Name1 = "ally", Name2 = "Lyra", Class = CharacterClass.Warrior, Level = 10, HP = 100, MaxHP = 100 };
            ally.EquippedItems[EquipmentSlot.MainHand] = id;
            var monster = new Monster { Name = "Kobold", Level = 3, HP = 5000, MaxHP = 5000, IsActive = true };
            typeof(CombatEngine).GetMethod("CheckElementalEnchantProcs", F)!
                .Invoke(engine, new object?[] { ally, monster, 100L, new CombatResult(), EquipmentSlot.MainHand });
            return Shown(term, output);
        });
        Capture($"combat-a-enchant-{lang}.txt", text);
        text.Should().Contain("Lyra");
        text.Should().NotContain("Flames erupt").And.NotContain("Lightning arcs").And.NotContain("shock damage");
        if (lang == "hu") text.Should().Contain("Lángok törnek elő Lyra fegyveréből!").And.Contain("Villámok cikáznak Lyra csapásától!");
    }

    // ---------- 7. loot lines on another player's terminal, in that player's language ----------

    private static async Task<string> RenderLootWinnerInventoryFull(string winnerLang)
    {
        var prevLangOf = CombatEngine.LanguageOf;
        try
        {
            CombatEngine.LanguageOf = c => c.Name2 == "Winner" ? winnerLang : GameConfig.Language;
            var (engine, _, leaderOut) = Engine();
            var winnerOut = new MemoryStream();
            var winnerTerm = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("T\n")), winnerOut);
            var winner = new Character { Name1 = "winner", Name2 = "Winner", Class = CharacterClass.Warrior, Level = 10, HP = 100, MaxHP = 100 };
            for (int i = 0; i < GameConfig.MaxInventoryItems; i++) winner.Inventory.Add(new Item { Name = $"Rock {i}", Type = ObjType.Body });
            var loot = new Item { Name = "Fine Blade", Type = ObjType.Weapon, Attack = 5, Value = 10, IsIdentified = true };
            var monster = new Monster { Name = "Kobold", Level = 3, HP = 0, MaxHP = 10 };
            await (Task)typeof(CombatEngine).GetMethod("PromptLootWinner", F)!.Invoke(engine, new object?[] { loot, monster, winner, winnerTerm })!;
            return Shown(winnerTerm, winnerOut);
        }
        finally { CombatEngine.LanguageOf = prevLangOf; }
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task LootInventoryFull_IsInTheWinnersLanguage(string winnerLang)
    {
        // the leader plays in English; the winner's terminal shows the winner's language
        string text = await WithLanguage("en", () => RenderLootWinnerInventoryFull(winnerLang));
        Capture($"combat-a-loot-full-{winnerLang}.txt", text);
        int max = GameConfig.MaxInventoryItems;
        text.Should().Contain(Loc.GetIn(winnerLang, "combat.loot_inventory_full_dropped", max, max));
        text.Should().NotContain("Your inventory is full").And.NotContain("Item dropped");
        if (winnerLang == "hu") text.Should().Contain($"A felszerelésed megtelt ({max}/{max})! A tárgy elveszett.");
    }

    [Fact]
    public void LanguageOf_FallsBackToTheSessionLanguageOutsideAGroup()
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = "fr";
            CombatEngine.LanguageOf(new Character { Name2 = "Solo" }).Should().Be("fr");
        }
        finally { GameConfig.Language = prev; }
    }

    [Fact]
    public void TheOtherPlayerLootLines_UseTheirLanguage()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().NotContain("Could not equip: {equipMsg}").And.NotContain("Your inventory is full ({GameConfig");
        Regex.Matches(src, "winnerTerm\\.WriteLine\\(Loc\\.GetIn\\(winnerLang, \"combat\\.inventory_full_item_dropped\"\\)\\)").Count.Should().Be(2);
        src.Should().Contain("otherTerm.WriteLine(Loc.GetIn(otherLang, \"combat.loot_equip_failed_inventory\", equipMsg))");
        src.Should().Contain("string winnerLang = LanguageOf(winner);").And.Contain("string otherLang = LanguageOf(otherPlayer);");
        // nothing on the winner's or the other player's terminal falls back to the leader's language
        foreach (Match m in Regex.Matches(src, "(winnerTerm|otherTerm)\\.Write(Line)?\\([^\\n]*"))
            m.Value.Should().NotContain("Loc.Get(");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task LootPromptOnTheWinnersTerminal_IsInTheirLanguage(string winnerLang)
    {
        string text = await WithLanguage("en", () => RenderLootWinnerInventoryFull(winnerLang));
        text.Should().Contain(Loc.GetIn(winnerLang, "combat.loot_won_roll", "Kobold")).And.Contain(Loc.GetIn(winnerLang, "combat.loot_take_option"));
        text.Should().NotContain("You won the roll");
    }

    // ---------- 8. the combat status panels ----------

    private static string RenderStatusPanels(bool bbs)
    {
        var (engine, term, output) = Engine();
        var hero = Hero();
        hero.DamageAbsorptionPool = 25;
        hero.MagicACBonus = 4;
        var boss = new Monster { Name = "Maelketh", Level = 30, HP = 900, MaxHP = 1000, IsActive = true, IsBoss = true };
        engine.BossContext = new BossCombatContext { CurrentPhase = 2 };
        var ally = new Character { Name1 = "ally", Name2 = "Lyra", Class = CharacterClass.Cleric, Level = 10, HP = 80, MaxHP = 100 };
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { ally });
        string method = bbs ? "DisplayCombatStatusBBS" : "DisplayCombatStatus";
        typeof(CombatEngine).GetMethod(method, F)!.Invoke(engine, new object?[] { new List<Monster> { boss }, hero, 0 });
        typeof(CombatEngine).GetMethod(method, F)!.Invoke(engine, new object?[] { new List<Monster> { boss }, hero, 3 });
        if (!bbs)
            typeof(CombatEngine).GetMethod("DisplayCombatStatusScreenReader", F)!.Invoke(engine, new object?[] { new List<Monster> { boss }, hero, 3 });
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task StatusPanel_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, () => RenderStatusPanels(bbs: false));
        Capture($"combat-a-status-{lang}.txt", text);
        text.Should().Contain(Loc.GetIn(lang, "combat.combat_status_header")).And.Contain(Loc.GetIn(lang, "combat.allies_header"))
            .And.Contain(Loc.GetIn(lang, "combat.boss_phase_tag", 2)).And.Contain(Loc.GetIn(lang, "combat.bar_st") + ":")
            .And.Contain(Loc.GetIn(lang, "combat.bar_shield") + ": ")
            .And.Contain(Loc.GetIn(lang, "combat.bar_atk") + ": ").And.Contain(Loc.GetIn(lang, "combat.bar_def") + ": ");
        var h = Hero();
        text.Should().Contain(Loc.GetIn(lang, "combat.sr_attack_defense_shield", h.Strength + h.WeapPow, h.Defence + h.ArmPow + 4, 4));
        foreach (var english in new[] { "COMBAT STATUS", "ALLIES", "[Phase", "ST:", "ATK:", "DEF:", "Shield:", "magic shield" })
            text.Should().NotContain(english);
        // the box keeps its width
        foreach (var line in text.Split('\n'))
            if (line.StartsWith("║") && line.Contains(Loc.GetIn(lang, "combat.combat_status_header"))) line.TrimEnd('\r').Length.Should().Be(60);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task BbsStatusPanel_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, () => RenderStatusPanels(bbs: true));
        Capture($"combat-a-status-bbs-{lang}.txt", text);
        text.Should().Contain(Loc.GetIn(lang, "combat.bbs_header_round", Loc.GetIn(lang, "combat.round_label", 3)))
            .And.Contain(" " + Loc.GetIn(lang, "combat.bbs_header") + " ")
            .And.Contain(Loc.GetIn(lang, "combat.boss_phase_line", 2));
        text.Should().NotContain(" COMBAT ").And.NotContain("Boss Phase");
    }

    [Fact]
    public async Task StatusPanels_EnglishKeepsItsWidths()
    {
        string text = await WithLanguage("en", () => RenderStatusPanels(bbs: true));
        text.Should().Contain("═══════════════════════════════ COMBAT ═══════════════════════════════════════")
            .And.Contain("══════════════════════ COMBAT - Round 3 ══════════════════════").And.Contain(" Boss Phase 2");
        CombatEngine.TipBoxTop("TIP").Should().Be("┌─── TIP ────────────────────────────────────────────────────────────────────┐");
        CombatEngine.BoxTitleLine("ALLIES").Length.Should().Be(60);
        CombatEngine.TipBoxTop(await WithLanguage("it", () => Loc.Get("combat.tip_label"))).Length.Should().Be(78);
    }

    // ---------- 9. damage lines, the monster kill sent to the group, and the heal target prompt ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task AoEDamageLine_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var (engine, term, output) = Engine();
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, Hero());
            var monsters = new List<Monster> { new() { Name = "Kobold", Level = 3, HP = 5000, MaxHP = 5000, IsActive = true } };
            await (Task)typeof(CombatEngine).GetMethod("ApplyAoEDamage", F)!
                .Invoke(engine, new object?[] { monsters, 100L, new CombatResult(), "AoE attack", false, null, null })!;
            return Shown(term, output);
        });
        Capture($"combat-a-aoe-{lang}.txt", text);
        text.Should().Contain("Kobold: ").And.Contain(" " + Loc.GetIn(lang, "combat.bar_hp"));
        text.Should().NotMatchRegex("-[0-9]+ HP");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task HolyAndCorrosiveAbilityLines_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var (engine, term, output) = Engine();
            var hero = Hero();
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
            foreach (var (id, effect) in new[] { ("judgment_day", "aoe_holy"), ("corrosive_cloud", "aoe_corrode") })
            {
                var undead = new Monster { Name = "Ghoul", Level = 3, HP = 5, MaxHP = 50, IsActive = true, Undead = 1 };
                var monsters = new List<Monster> { undead };
                var ability = new ClassAbilityResult { AbilityUsed = ClassAbilitySystem.GetAbility(id), SpecialEffect = effect, Damage = 100, Success = true };
                await (Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                    .Invoke(engine, new object?[] { hero, undead, monsters, ability, new CombatResult() })!;
            }
            return Shown(term, output);
        });
        Capture($"combat-a-holy-corrode-{lang}.txt", text);
        text.Should().Contain(Loc.GetIn(lang, "combat.ability_purified", "Ghoul"));
        text.Should().Contain(Loc.GetIn(lang, "combat.hp_loss_holy", 0).Split(' ')[1]);
        text.Should().NotContain("(HOLY!)").And.NotContain("is purified!").And.NotContain("Corrosive cloud hits");
    }

    [Fact]
    public void MonsterKillSentToTheGroup_IsInTheRecipientsLanguage()
    {
        string hu = CombatEngine.GroupLine("hu", "\u001b[1;32m", "combat.group_slays", "Tester", "Kobold");
        hu.Should().Contain("Tester legyőzi: Kobold!").And.NotContain("slays the");
        string it = CombatEngine.GroupLine("it", "\u001b[1;32m", "combat.group_slays", Loc.GetIn("it", "combat.group_someone"), "Kobold");
        it.Should().Contain("Qualcuno abbatte Kobold!").And.NotContain("Someone");
        CombatEngine.GroupLine("en", "\u001b[1;32m", "combat.group_slays", "Tester", "Kobold")
            .Should().Be("\u001b[1;32m  Tester slays the Kobold!\u001b[0m");
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().Contain("killerName ?? Loc.GetIn(lang, \"combat.group_someone\"), target.Name));");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task HealTargetPromptAndTip_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () => Loc.Get("combat.heal_target_prompt") + "\n" + CombatEngine.TipBoxTop(Loc.Get("combat.tip_label")));
        text.Should().NotContain("Target (ENTER=self)").And.NotContain("TIP ");
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().Contain("await terminal.GetInput(\"  \" + Loc.Get(\"combat.heal_target_prompt\"))");
        src.Should().Contain("terminal.WriteLine(TipBoxTop(Loc.Get(\"combat.tip_label\")))");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
