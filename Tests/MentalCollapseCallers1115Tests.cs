using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;
using static UsurperReborn.Tests.MentalRecoveryB1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 7 follow-up: a fight that is not entered at Mental 0 returns PlayerEscaped with
/// CombatResult.MentalCollapseNotFought set. The callers that branch on the outcome (the gauntlet, the
/// tournament, an Old God, the arena portal, Seth, the bank robbery, the throne guards, the combat test)
/// read that flag first: no flee text, no flee outcome, one line instead. The mechanics do not change.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalCollapseCallers1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Key = "mental.collapse_before_fight";

    // Behaviour

    [Fact]
    public async Task A_fight_not_entered_says_so_and_a_normal_result_does_not()
    {
        var hero = Hero("Spent", 0);
        var rat = new Monster { Name = "Rat", Level = 1, HP = 10, MaxHP = 10 };
        var engine = new CombatEngine(Term(new MemoryStream()));
        var fight = engine.PlayerVsMonsters(hero, new List<Monster> { rat });
        (await Task.WhenAny(fight, Task.Delay(15000))).Should().BeSameAs(fight, "the fight must return at once");
        var result = await fight;
        result.Outcome.Should().Be(CombatOutcome.PlayerEscaped);
        result.MentalCollapsePending.Should().BeTrue();
        result.MentalCollapseNotFought.Should().BeTrue("the callers tell a fight not entered from a flee by this flag");
        new CombatResult { Outcome = CombatOutcome.PlayerEscaped, MentalCollapsePending = true }
            .MentalCollapseNotFought.Should().BeFalse("a real flee that ends at Mental 0 is still a flee");
    }

    [Fact]
    public async Task An_Old_God_fight_not_entered_counts_as_not_fought()
    {
        var data = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh);
        var hero = Hero("Spent", 0);
        var output = new MemoryStream();
        var notFought = new CombatResult { Player = hero, Outcome = CombatOutcome.PlayerEscaped, MentalCollapsePending = true, MentalCollapseNotFought = true };
        var r = await Convert(notFought, data, Term(output));
        r.Outcome.Should().Be(BossOutcome.NotFought);
        r.Success.Should().BeFalse("nothing was resolved");
        Text(output).Should().Contain(Loc.Get(Key));

        var fled = new CombatResult { Player = hero, Outcome = CombatOutcome.PlayerEscaped, MentalCollapsePending = true };
        (await Convert(fled, data, Term(new MemoryStream()))).Outcome.Should().Be(BossOutcome.Fled, "a real flee is still Fled");
    }

    private static Task<BossEncounterResult> Convert(CombatResult c, UsurperRemake.Data.OldGodBossData data, TerminalEmulator t) =>
        (Task<BossEncounterResult>)typeof(OldGodBossSystem).GetMethod("ConvertToBossResult", F)!
            .Invoke(OldGodBossSystem.Instance, new object[] { c, data, false, t })!;

    [Fact]
    public void A_god_not_fought_does_not_clear_the_room_or_send_the_player_to_town()
    {
        var src = Src("Locations", "DungeonLocation.cs");
        int fledGuards = Regex.Matches(src, @"!= BossOutcome\.Fled").Count;
        fledGuards.Should().Be(2, "the room-clear guard and the town-return guard");
        Regex.Matches(src, @"!= BossOutcome\.Fled\s*&&\s*result\.Outcome != BossOutcome\.NotFought").Count
            .Should().Be(fledGuards, "every guard that leaves a fled god alone leaves a god not fought alone too");
        var clear = Regex.Match(src, @"if \(([^\n]*BossOutcome\.Fled[^\n]*)\)\s*\{\s*room\.IsCleared = true;\s*currentFloor\.BossDefeated = true;");
        clear.Success.Should().BeTrue();
        clear.Groups[1].Value.Should().Contain("!= BossOutcome.NotFought");
        Body(src, "HandleGodEncounterResult").Should().Contain("case BossOutcome.NotFought:");
        Src("Locations", "TempleLocation.cs").Should().Contain("case BossOutcome.NotFought:");
    }

    // Callers

    public static IEnumerable<object[]> FleeSites() => new[]
    {
        new object[] { "Locations/AnchorRoadLocation.cs", "Loc.Get(\"anchor_road.flee_disgrace\")" },
        new object[] { "Locations/SanctumLocation.cs", "Loc.Get(\"tournament.flee\"" },
        new object[] { "Systems/RareEncounters.cs", "Loc.Get(\"encounter.arena.fled\")" },
        new object[] { "Locations/InnLocation.cs", "Loc.Get(\"inn.seth_back_away\")" },
        new object[] { "Locations/BankLocation.cs", "Loc.Get(\"bank.rob_fled_empty\")" },
        new object[] { "Locations/CastleLocation.cs", "Loc.Get(\"castle.monster_guards_overwhelm\")" },
        new object[] { "Locations/CastleLocation.cs", "Loc.Get(\"castle.royal_guards_overwhelm\")" },
        new object[] { "Locations/MainStreetLocation.cs", "Loc.Get(\"main_street.combat_test_escaped\")" },
        new object[] { "Systems/OldGodBossSystem.cs", "Outcome = BossOutcome.Fled,\n" },
        new object[] { "Locations/DarkAlleyLocation.cs", "Loc.Get(\"dark_alley.enforcer_beaten\")" },
        new object[] { "Locations/BaseLocation.cs", "Loc.Get(\"street_encounter.guard.overpowered\")" },
    };

    [Theory]
    [MemberData(nameof(FleeSites))]
    public void Each_flee_branch_checks_for_a_fight_not_entered_first(string file, string flee)
    {
        var src = Src(file.Split('/'));
        int at = At(src, flee);
        int call = src.LastIndexOf("PlayerVsMonster", at, StringComparison.Ordinal);
        call.Should().BeGreaterThan(0, $"{file}: the flee branch follows a fight");
        var between = src.Substring(call, at - call);
        between.Should().Contain(".MentalCollapseNotFought", $"{file}: the fight not entered is checked before the flee text");
        between.Should().Contain($"Loc.Get(\"{Key}\")", $"{file}: one line in place of the flee text");
    }

    [Fact]
    public void Every_PlayerEscaped_branch_after_a_monster_fight_checks_the_flag_first()
    {
        var root = Path.Combine(RepoRoot(), "Scripts");
        var checkedSites = new List<string>();
        foreach (var f in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(f) == "CombatEngine.cs") continue;
            var src = Code(File.ReadAllText(f));
            foreach (Match m in Regex.Matches(src, @"CombatOutcome\.PlayerEscaped"))
            {
                int monster = src.LastIndexOf("PlayerVsMonster", m.Index, StringComparison.Ordinal);
                int pvp = src.LastIndexOf("PlayerVsPlayer", m.Index, StringComparison.Ordinal);
                if (monster < 0 || pvp > monster) continue; // a duel: PlayerVsPlayer never refuses a fight
                int eol = src.IndexOf('\n', m.Index); if (eol < 0) eol = src.Length;
                src.Substring(monster, eol - monster).Should().Contain("MentalCollapseNotFought",
                    $"{Path.GetFileName(f)}: a PlayerEscaped branch after a monster fight reads the flag first");
                checkedSites.Add(Path.GetFileName(f));
            }
        }
        checkedSites.Should().Contain(new[] { "AnchorRoadLocation.cs", "SanctumLocation.cs", "InnLocation.cs", "MainStreetLocation.cs", "OldGodBossSystem.cs" });
    }

    [Fact]
    public void The_line_is_in_all_five_languages()
    {
        string en = null!;
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")));
            doc.RootElement.TryGetProperty(Key, out var v).Should().BeTrue($"{lang} has {Key}");
            var s = v.GetString();
            s.Should().NotBeNullOrWhiteSpace();
            if (lang == "en") { en = s!; s.Should().Be("Your mind gives way before the next fight."); }
            else s.Should().NotBe(en, $"{lang} is translated");
        }
    }
}
