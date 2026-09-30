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

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
