using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5 (D11b, v125-bugs 15): the Dark Alley enforcer's name and the Inn's hired sleep guards are stored in
/// English and shown in each reader's language. A guards row saved before keeps and shows its saved names.
/// </summary>
[Collection("SharedGameSingletons")]
public class GuardNames125Tests
{
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private static readonly string[] GuardTypes = { "rookie_npc", "veteran_npc", "elite_npc", "hound", "troll", "drake" };

    private static readonly string[] CombatNameChecks =
    {
        "Skeleton", "Zombie", "Ghost", "Lich", "Wraith", "Vampire", "Undead", "Revenant", "Boss", "Chief", "Lord", "King",
        "Demon", "Devil", "Imp", "Archfiend", "Hellspawn", "Fiend",
    };

    private static T InLang<T>(string lang, Func<T> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            return body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    [Fact]
    public void LongName_IsTheLongestPlayerName() => LongName.Length.Should().Be(GameConfig.MaxNameLength);

    // ---------- the enforcer ----------

    [Fact]
    public void TheEnforcer_IsStoredInEnglish_AndShownPerReader()
    {
        Src("Locations", "DarkAlleyLocation.cs").Should().Contain("enforcer.Name = MonsterNames.FromKey(\"dark_alley.enforcer_name\");")
            .And.NotContain("enforcer.Name = Loc.Get(");

        var enforcer = new Monster { Name = InLang("hu", () => MonsterNames.FromKey("dark_alley.enforcer_name")) };
        enforcer.Name.Should().Be("Loan Shark Enforcer", "stored English in a Hungarian session");
        foreach (var lang in AllLanguages)
            MonsterNames.DisplayIn(lang, enforcer).Should().Be(L(lang, "dark_alley.enforcer_name"), lang);
        foreach (var word in CombatNameChecks)
            enforcer.Name.Should().NotContain(word, "CombatEngine reads monster names for these words");
    }

    [Fact]
    public void TheEnforcer_InDeathNewsAndTheEulogy_IsInTheReadersLanguage()
    {
        var enforcer = new Monster { Name = MonsterNames.FromKey("dark_alley.enforcer_name") };
        var buffer = new List<string>();
        var news = NewsSystem.Instance;
        news.SetCatchUpBuffer(buffer);
        try
        {
            InLang("hu", () => { news.WriteDeathNews(LongName, MonsterNames.Display(enforcer), "Sikator"); return 0; });
            InLang("en", () => { news.WriteDeathNews(LongName, MonsterNames.Display(enforcer), "Dark Alley"); return 0; });
        }
        finally { news.ClearCatchUpBuffer(); }
        buffer[0].Should().Contain(L("hu", "dark_alley.enforcer_name")).And.NotContain("Loan Shark Enforcer");
        buffer[1].Should().Contain("Loan Shark Enforcer");
        foreach (var lang in AllLanguages)
            L(lang, "permadeath.eulogy", LongName, 50, "Warrior", MonsterNames.DisplayIn(lang, enforcer))
                .Should().Contain(L(lang, "dark_alley.enforcer_name"), lang);
    }

    // ---------- sleep guards: storage ----------

    [Fact]
    public void ANewHire_IsStoredInEnglish_WhateverTheHirersLanguage()
    {
        var hire = GuardTypes.Select((t, i) => (t, 100 + i)).ToList();
        foreach (var lang in AllLanguages)
        {
            string json = InLang(lang, () => InnLocation.HiredGuardsJson(hire));
            var rows = JsonNode.Parse(json)!.AsArray();
            rows.Count.Should().Be(GuardTypes.Length);
            for (int i = 0; i < GuardTypes.Length; i++)
            {
                rows[i]!["type"]!.GetValue<string>().Should().Be(GuardTypes[i]);
                rows[i]!["name"]!.GetValue<string>().Should().Be(L("en", InnLocation.GuardNameKey(GuardTypes[i])), $"{lang} {GuardTypes[i]}");
                rows[i]!["hp"]!.GetValue<int>().Should().Be(100 + i);
                rows[i]!["maxHp"]!.GetValue<int>().Should().Be(100 + i);
            }
        }
        InnLocation.HiredGuardsJson(Array.Empty<(string, int)>()).Should().Be("[]");
        Src("Locations", "InnLocation.cs").Should().Contain("var guardsJson = HiredGuardsJson(hiredGuards.Select(g => (g.type, g.hp)));");
    }

    [Fact]
    public void ANewHire_IsReadByAnotherPlayersAttack_AndByTheWorldSimulation_AndShownPerReader()
    {
        string json = InLang("hu", () => InnLocation.HiredGuardsJson(GuardTypes.Select(t => (t, 120))));

        var attack = InnLocation.ParseAttackGuards(json);
        attack.Select(g => g.type).Should().Equal(GuardTypes);
        attack.Should().OnlyContain(g => g.hp == 120 && g.maxHp == 120);

        var sim = WorldSimulator.ParseGuards(json);
        sim.Select(g => g.Type).Should().Equal(GuardTypes);
        sim.Should().OnlyContain(g => g.Hp == 120 && g.MaxHp == 120);

        for (int i = 0; i < GuardTypes.Length; i++)
        {
            string key = InnLocation.GuardNameKey(GuardTypes[i]);
            sim[i].Name.Should().Be(L("en", key), "the world simulation's log keeps English");
            foreach (var lang in AllLanguages)
            {
                InnLocation.GuardNameIn(lang, attack[i].type, attack[i].name).Should().Be(L(lang, key), $"{lang} attack");
                InnLocation.GuardNameIn(lang, null, sim[i].Name).Should().Be(L(lang, key), $"{lang} sleep report");
            }
        }
    }

    [Fact]
    public void AnAttack_WritesTheGuardsBackInEnglish_WhateverTheAttackersLanguage()
    {
        // a row hired before 1.2.5 has no name
        string old = "[{\"type\":\"veteran_npc\",\"hp\":150,\"maxHp\":150}]";
        var guards = InnLocation.ParseAttackGuards(old);
        guards.Single().name.Should().BeNull();
        foreach (var lang in AllLanguages)
        {
            InnLocation.GuardNameIn(lang, "veteran_npc", null).Should().Be(L(lang, "inn.guard_veteran"), lang);
            string back = InLang(lang, () => InnLocation.AttackedGuardsJson(guards));
            JsonNode.Parse(back)!.AsArray()[0]!["name"]!.GetValue<string>().Should().Be("Veteran Guard", lang);
        }

        string inn = Src("Locations", "InnLocation.cs");
        inn.Should().Contain("var guards = ParseAttackGuards(target.GuardsJson);")
            .And.Contain("string gName = GuardName(gType, gStoredName);")
            .And.Contain("guards[gi] = (gType, gStoredName, remainingHp, gMaxHp);")
            .And.Contain("await backend.UpdateSleeperGuards(target.Username, AttackedGuardsJson(guards));")
            .And.NotContain("Loc.Get(\"inn.guard_default\")");
    }

    // ---------- sleep guards: rows saved before ----------

    [Fact]
    public void AHungarianRowSavedBefore_LoadsFightsAndShowsItsSavedNames()
    {
        string hu = L("hu", "inn.guard_default");
        hu.Should().NotBe("Guard");
        // as an attack wrote it before 1.2.5, in a Hungarian session
        string old = "[{\"type\":\"troll\",\"name\":\"" + hu + "\",\"hp\":90,\"max_hp\":200}]";

        var guards = InnLocation.ParseAttackGuards(old);
        guards.Should().ContainSingle();
        guards[0].Should().Be(("troll", (string?)hu, 90, 200));
        foreach (var lang in AllLanguages)
            InnLocation.GuardNameIn(lang, guards[0].type, guards[0].name).Should().Be(hu, $"{lang}: a saved name shows as saved");
        var fighter = HeadlessCombatResolver.CreateGuardCharacter(guards[0].type, guards[0].hp, 20, new Random(1));
        fighter.HP.Should().Be(90);
        fighter.Strength.Should().BeGreaterThan(0);
        JsonNode.Parse(InLang("en", () => InnLocation.AttackedGuardsJson(guards)))!.AsArray()[0]!["name"]!.GetValue<string>()
            .Should().Be(hu, "an attack keeps the saved name");

        // the world simulation and an attack read the same row (the shape the hire writes) without error
        string shared = "[{\"type\":\"troll\",\"name\":\"" + hu + "\",\"hp\":90,\"maxHp\":200}]";
        var sim = WorldSimulator.ParseGuards(shared);
        sim.Should().ContainSingle();
        sim[0].Should().Be(new WorldSimulator.GuardData("troll", hu, 90, 200));
        WorldSimulator.ParseGuards(WorldSimulator.SerializeGuards(sim)).Single().Name.Should().Be(hu, "the world simulation keeps the saved name");
        InnLocation.ParseAttackGuards(shared).Single().name.Should().Be(hu);
        InnLocation.GuardNameIn("en", null, sim[0].Name).Should().Be(hu, "the sleep report shows it as saved");
    }

    [Fact]
    public void ARowWithoutNames_AsHiredBefore_IsReadByBothReaders()
    {
        string old = "[{\"type\":\"hound\",\"hp\":60,\"maxHp\":60},{\"type\":\"drake\",\"hp\":300,\"maxHp\":300}]";
        WorldSimulator.ParseGuards(old).Select(g => g.Name).Should().Equal("Guard Hound", "Guard Drake");
        InnLocation.ParseAttackGuards(old).Select(g => g.type).Should().Equal("hound", "drake");
        // the castle's royal guards have no guard key of their own: a Guard
        foreach (var lang in AllLanguages)
            InnLocation.GuardNameIn(lang, "royal_guard", null).Should().Be(L(lang, "inn.guard_default"), lang);
    }

    [Fact]
    public void TheEnglishGuardNames_AreTheOnesTheGameUsed()
    {
        var english = new[] { "Rookie Guard", "Veteran Guard", "Elite Guard", "Guard Hound", "Guard Troll", "Guard Drake" };
        for (int i = 0; i < GuardTypes.Length; i++)
        {
            InnLocation.GuardStoredName(GuardTypes[i]).Should().Be(english[i]);
            WorldSimulator.GetGuardName(GuardTypes[i]).Should().Be(english[i]);
            HeadlessCombatResolver.CreateGuardCharacter(GuardTypes[i], 50, 10, new Random(1)).Name2.Should().Be(english[i]);
        }
        WorldSimulator.GetGuardName("royal_guard").Should().Be("Guard");
    }

    // ---------- sleep guards: maxHp (v125-bugs 16) ----------

    [Fact]
    public void AfterAPlayersAttack_TheWorldSimulationStillSeesTheSurvivingGuards()
    {
        string hired = InnLocation.HiredGuardsJson(new[] { ("rookie_npc", 80), ("troll", 200), ("drake", 300) });
        var guards = InnLocation.ParseAttackGuards(hired);
        guards.RemoveAt(0);                                   // the attacker cut down the first guard
        guards[0] = (guards[0].type, guards[0].name, 40, guards[0].maxHp);   // the second repelled them, hurt
        string back = InnLocation.AttackedGuardsJson(guards);

        var row = JsonNode.Parse(back)!.AsArray();
        row.Should().OnlyContain(g => g!["maxHp"] != null && g["max_hp"] == null, "the attack writes the key the hire writes");

        var sim = WorldSimulator.ParseGuards(back);
        sim.Select(g => (g.Type, g.Hp, g.MaxHp)).Should().Equal(("troll", 40, 200), ("drake", 300, 300));
        InnLocation.ParseAttackGuards(back).Select(g => (g.type, g.hp, g.maxHp)).Should().Equal(("troll", 40, 200), ("drake", 300, 300));
    }

    [Fact]
    public void AnOldMaxUnderscoreRow_AndAMaxHpRow_LoadInBothReaders()
    {
        foreach (var key in new[] { "max_hp", "maxHp" })
        {
            string json = "[{\"type\":\"elite_npc\",\"name\":\"Guard\",\"hp\":70,\"" + key + "\":250}]";
            WorldSimulator.ParseGuards(json).Select(g => (g.Type, g.Hp, g.MaxHp)).Should().Equal(new[] { ("elite_npc", 70, 250) }, key);
            InnLocation.ParseAttackGuards(json).Select(g => (g.type, g.hp, g.maxHp)).Should().Equal(new[] { ("elite_npc", 70, 250) }, key);
        }
        // neither key: the maximum is the current HP, as before
        WorldSimulator.ParseGuards("[{\"type\":\"hound\",\"hp\":60}]").Single().MaxHp.Should().Be(60);
        InnLocation.ParseAttackGuards("[{\"type\":\"hound\",\"hp\":60}]").Single().maxHp.Should().Be(60);
    }

    // ---------- the guard in the attacker's combat ----------

    [Fact]
    public void TheGuardInCombat_IsEnglishInside_AndShownInTheAttackersLanguage()
    {
        foreach (var type in GuardTypes)
        {
            var guard = InLang("hu", () => HeadlessCombatResolver.CreateGuardCharacter(type, 100, 20, new Random(1)));
            guard.Name2.Should().Be(InnLocation.GuardStoredName(type), "stored English");
            guard.IsSleepGuard.Should().BeTrue();
            foreach (var lang in AllLanguages)
                InLang(lang, () => guard.DisplayName).Should().Be(L(lang, InnLocation.GuardNameKey(type)), $"{type} {lang}");
        }
        InLang("hu", () => new Character { Name2 = "Veteran Guard" }.DisplayName).Should().Be("Veteran Guard", "only a guard stand-in is translated");

        // a saved name (a row from before, in its writer's language) fights under that name
        string hu = L("hu", "inn.guard_default");
        var saved = HeadlessCombatResolver.CreateGuardCharacter("troll", 100, 20, new Random(1));
        saved.Name2 = hu;
        InLang("en", () => saved.DisplayName).Should().Be(hu);
        Src("Locations", "InnLocation.cs").Should().Contain("if (!string.IsNullOrEmpty(gStoredName)) guardChar.Name2 = gStoredName;");
    }

    [Fact]
    public void AGuardsKill_InDeathNews_NamesTheGuardInTheWritersLanguage()
    {
        var guard = HeadlessCombatResolver.CreateGuardCharacter("hound", 100, 20, new Random(1));
        var buffer = new List<string>();
        var news = NewsSystem.Instance;
        news.SetCatchUpBuffer(buffer);
        try
        {
            InLang("hu", () => { news.WriteDeathNews(LongName, guard.DisplayName, "Fogado"); return 0; });
            InLang("en", () => { news.WriteDeathNews(LongName, guard.DisplayName, "Inn"); return 0; });
        }
        finally { news.ClearCatchUpBuffer(); }
        buffer[0].Should().Contain(L("hu", "news.death", LongName, L("hu", "inn.guard_hound"), "Fogado")).And.NotContain("Guard Hound");
        buffer[1].Should().Contain(L("en", "news.death", LongName, "Guard Hound", "Inn"));
        guard.Name2.Should().Be("Guard Hound", "the guard itself stays English");
        Src("Systems", "CombatEngine.cs").Should().Contain("NewsSystem.Instance?.WriteDeathNews(result.Player.DisplayName, result.Opponent?.DisplayName ?? \"an opponent\", location);");
    }

    // ---------- the sleep report ----------

    [Fact]
    public void TheSleepReport_ShowsTheGuardInTheReadersLanguage()
    {
        var entry = JsonNode.Parse("{\"attacker\":\"Grimbold\",\"type\":\"npc\",\"result\":\"repelled\",\"details\":[\"Your Elite Guard fought off Grimbold!\"]}")!;
        string stored = GameEngine.SleepGuardFights(entry, "Grimbold")![0]!["guard"]!.GetValue<string>();
        stored.Should().Be("Elite Guard");
        foreach (var lang in AllLanguages)
            InnLocation.GuardNameIn(lang, null, stored).Should().Be(L(lang, "inn.guard_elite"), lang);
        InnLocation.GuardNameIn("hu", null, null).Should().Be(L("hu", "inn.guard_default"));
        InnLocation.GuardNameIn("hu", null, "Old Tom").Should().Be("Old Tom", "an unknown stored name shows as stored");
        Src("Core", "GameEngine.cs").Should().Contain("string guardName = InnLocation.GuardName(null, gf[\"guard\"]?.GetValue<string>());")
            .And.Contain("WriteRows(Loc.Get(\"engine.guard_fought_off\", guardName, attacker), \"bright_green\");")
            .And.Contain("WriteRows(Loc.Get(\"engine.guard_defeated\", guardName, attacker), \"red\");");
    }

    [Fact]
    public void TheItalianGuardLines_DoNotGiveTheNameAGender()
    {
        // the guard names are masculine and feminine (Segugio, Guardia); the lines name the guard after "guardia"
        L("it", "engine.guard_fought_off", "Segugio della Guardia", "Bo").Should().Be("  La tua guardia (Segugio della Guardia) ha respinto Bo!");
        L("it", "engine.guard_defeated", "Segugio della Guardia", "Bo").Should().Be("  La tua guardia (Segugio della Guardia) è stata sconfitta da Bo!");
        L("it", "inn.atk_guard_blocks", "Guardia Recluta").Should().Be("\n  Guardia Recluta ti sbarra la strada!");
        L("it", "inn.atk_cut_down_guard", "Guardia Recluta").Should().Be("  Abbatti la guardia (Guardia Recluta)!");
        L("it", "inn.atk_guard_repels", "Drago della Guardia").Should().Be("  La guardia (Drago della Guardia) ti respinge! Attacco fallito!");
    }

    [Fact]
    public void TheHungarianGuardLines_LeaveTheNameStandingAlone()
    {
        // no article or case suffix is glued to the name ("a(z) {0}-t" read "a(z) Őrkutya-t")
        L("hu", "inn.atk_guard_blocks", "Őrkutya").Should().Be("\n  Egy őr (Őrkutya) állja utadat!");
        L("hu", "inn.atk_cut_down_guard", "Őrkutya").Should().Be("  Leteríted az őrt (Őrkutya)!");
        L("hu", "inn.atk_guard_repels", "Veterán Őr").Should().Be("  Az őr (Veterán Őr) visszaver! A támadás kudarcba fulladt!");
        L("hu", "engine.guard_fought_off", "Őrtroll", "Bo").Should().Be("  Az őröd (Őrtroll) visszaverte a támadót: Bo!");
        L("hu", "engine.guard_defeated", "Őrtroll", "Bo").Should().Be("  Az őröd (Őrtroll) alulmaradt a támadóval szemben: Bo!");
        foreach (var key in new[] { "inn.atk_guard_blocks", "inn.atk_cut_down_guard", "inn.atk_guard_repels", "engine.guard_fought_off", "engine.guard_defeated" })
            Loc.GetIn("hu", key).Should().NotContain("a(z)", key).And.NotContain("A(z)", key).And.NotContain("}-", key);
    }

    // ---------- width ----------

    [Fact]
    public void EveryGuardRow_FitsIn79Columns_InEveryLanguage()
    {
        var over = new List<string>();
        foreach (var lang in AllLanguages)
        {
            var names = GuardTypes.Select(t => L(lang, InnLocation.GuardNameKey(t))).Append(L(lang, "inn.guard_default")).ToList();
            foreach (var name in names)
            {
                var lines = new List<string>
                {
                    L(lang, "inn.atk_guard_blocks", InLang(lang, () => GameConfig.ArticulateForLanguage(name))),
                    L(lang, "inn.atk_cut_down_guard", name),
                    L(lang, "inn.atk_guard_repels", name),
                };
                // the sleep report writes its guard lines through GameEngine.WrapRows
                foreach (var key in new[] { "engine.guard_fought_off", "engine.guard_defeated" })
                {
                    var rows = GameEngine.WrapRows(L(lang, key, name, LongName));
                    string.Join(" ", rows.Select(r => r.Trim())).Should().Be(L(lang, key, name, LongName).Trim(), "nothing is lost");
                    lines.AddRange(rows);
                }
                foreach (var row in lines.SelectMany(t => t.Replace("\r", "").Split('\n')))
                    if (row.Length > MaxWidth) over.Add($"{lang} {row.Length}: {row}");
            }
        }
        over.Should().BeEmpty();
    }
}
