using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 3, commit 2: deeds and taboos. An act of the worshipped god's domain
/// gains Favor (at most +4 a day, and it counts as devotion) or loses it (a taboo). Canon gods by
/// their own domain, player-gods by the domain their immortal chose. Behaviour tests use their own
/// GodSystem; source tests check each real call site calls the hook.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodDeeds1115Tests
{
    private static Character Hero(string name, CharacterClass cls = CharacterClass.Warrior) =>
        new Character { Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 5, HP = 50, MaxHP = 50, Class = cls };

    private static (Character c, GodSystem gods) Worshipper(string name, string god, int favor = 30, CharacterClass cls = CharacterClass.Warrior)
    {
        var gods = new GodSystem();
        var c = Hero(name, cls);
        GodRegistry.SetWorshippedGod(c, god, gods).Should().BeTrue();
        c.GodFavor = favor;
        FavorSystem.Bind(c, gods);
        c.GodFavor = favor;
        return (c, gods);
    }

    private static TerminalEmulator Term(MemoryStream output) =>
        new TerminalEmulator(new LineStream(Enumerable.Repeat("", 5)), output);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    private static int Count(string src, string needle) => Regex.Matches(src, Regex.Escape(needle)).Count;

    // ---------------- Deeds and taboos per god ----------------

    [Theory]
    // Solarius (Light)
    [InlineData("Solarius", GodAct.UndeadSlain, 1)]
    [InlineData("Solarius", GodAct.DrugUse, -3)]
    // Valorian (War)
    [InlineData("Valorian", GodAct.StrongerFoeBeaten, 1)]
    [InlineData("Valorian", GodAct.Fled, -3)]
    // Amara (Love)
    [InlineData("Amara", GodAct.AllyHealed, 1)]
    [InlineData("Amara", GodAct.Marriage, 2)]
    [InlineData("Amara", GodAct.FriendMade, 1)]
    [InlineData("Amara", GodAct.Murder, -10)]
    // Judicar (Law)
    [InlineData("Judicar", GodAct.BountyCollected, 2)]
    [InlineData("Judicar", GodAct.ArrestOrdered, 2)]
    [InlineData("Judicar", GodAct.Theft, -3)]
    [InlineData("Judicar", GodAct.Murder, -5)]
    [InlineData("Judicar", GodAct.Imprisoned, -5)]
    // Umbrath (Shadow)
    [InlineData("Umbrath", GodAct.Theft, 1)]
    [InlineData("Umbrath", GodAct.StealthKill, 1)]
    [InlineData("Umbrath", GodAct.Confession, -5)]
    // Terran (Earth)
    [InlineData("Terran", GodAct.HerbGathered, 1)]
    [InlineData("Terran", GodAct.SettlementWork, 1)]
    [InlineData("Terran", GodAct.Desecration, -10)]
    // Mortis (Death)
    [InlineData("Mortis", GodAct.DeathWitnessed, 1)]
    [InlineData("Mortis", GodAct.UndeadRaised, -5)]
    // Arcanus (Magic)
    [InlineData("Arcanus", GodAct.SpellLearned, 2)]
    [InlineData("Arcanus", GodAct.LibraryRead, 1)]
    [InlineData("Arcanus", GodAct.NoCastWeek, -5)]
    // Sylvana (Nature)
    [InlineData("Sylvana", GodAct.WildernessExplored, 1)]
    // Discordia (Chaos)
    [InlineData("Discordia", GodAct.PvpWin, 2)]
    [InlineData("Discordia", GodAct.StreetBrawl, 1)]
    [InlineData("Discordia", GodAct.Marriage, -5)]
    public void Act_ChangesFavor_ForTheGodOfItsDomain(string god, GodAct act, int expected)
    {
        var (c, gods) = Worshipper("GdAct" + god + act, god, 30);
        GodDeedSystem.Apply(c, act, gods).Should().Be(expected);
        c.GodFavor.Should().Be(30 + expected);
    }

    [Theory]
    [InlineData("Solarius", GodAct.Fled)]
    [InlineData("Valorian", GodAct.DrugUse)]
    [InlineData("Amara", GodAct.Theft)]
    [InlineData("Judicar", GodAct.Confession)]
    [InlineData("Umbrath", GodAct.Murder)]
    [InlineData("Mortis", GodAct.UndeadSlain)]
    [InlineData("Arcanus", GodAct.Marriage)]
    [InlineData("Sylvana", GodAct.SpellLearned)]
    [InlineData("Discordia", GodAct.Desecration)]
    [InlineData("Terran", GodAct.StreetBrawl)]
    public void Act_OfAnotherDomain_ChangesNothing(string god, GodAct act)
    {
        var (c, gods) = Worshipper("GdOther" + god, god, 30);
        GodDeedSystem.Apply(c, act, gods).Should().Be(0);
        c.GodFavor.Should().Be(30);
    }

    [Fact]
    public void Act_NoGodOrNpc_ChangesNothing()
    {
        var gods = new GodSystem();
        GodDeedSystem.Apply(Hero("GdNoGod"), GodAct.Theft, gods).Should().Be(0);
        var npc = new Character { Name1 = "GdNpc", Name2 = "GdNpc", AI = CharacterAI.Computer, WorshippedGod = "Zephyrine", GodFavorGod = "Zephyrine", GodFavor = 5 };
        GodBoonSystem.SetPlayerGodBoon(npc, "Zephyrine", GodDomain.Shadow, 100);
        GodDeedSystem.Apply(npc, GodAct.Theft, gods).Should().Be(0);
        npc.GodFavor.Should().Be(5);
    }

    [Fact]
    public void PlayerGod_UsesTheDomainItsImmortalChose()
    {
        var (c, gods) = Worshipper("GdPgWar", "Zephyrine", 30);
        GodDeedSystem.Apply(c, GodAct.Fled, gods).Should().Be(0, "no domain cached yet, no deeds or taboos");
        GodBoonSystem.SetPlayerGodBoon(c, "Zephyrine", GodDomain.War, 50);
        GodDeedSystem.Apply(c, GodAct.StrongerFoeBeaten, gods).Should().Be(1);
        GodDeedSystem.Apply(c, GodAct.Fled, gods).Should().Be(-3);
        GodDeedSystem.Apply(c, GodAct.Theft, gods).Should().Be(0);
        c.GodFavor.Should().Be(28);
    }

    // ---------------- The daily cap and devotion ----------------

    [Fact]
    public void Deeds_AtMostFourADay_ThenAgainAfterTheReset_TaboosUncapped()
    {
        var (c, gods) = Worshipper("GdCap", "Solarius", 30);
        int gained = 0;
        for (int i = 0; i < 6; i++) gained += GodDeedSystem.Apply(c, GodAct.UndeadSlain, gods);
        gained.Should().Be(GameConfig.GodFavorDeedDailyCap);
        FavorSystem.GainedToday(c, FavorSource.Deed).Should().Be(4);
        c.GodFavor.Should().Be(34);
        GodDeedSystem.Apply(c, GodAct.DrugUse, gods).Should().Be(-3);
        GodDeedSystem.Apply(c, GodAct.DrugUse, gods).Should().Be(-3, "taboos have no daily cap");
        FavorSystem.ApplyDailyReset(c, gods);
        GodDeedSystem.Apply(c, GodAct.UndeadSlain, gods).Should().Be(1);
    }

    [Fact]
    public void Deed_IsDevotion_TabooIsNot()
    {
        var (c, gods) = Worshipper("GdDevote", "Sylvana", 30);
        c.DaysSinceDevotion = 3;
        GodDeedSystem.Apply(c, GodAct.Confession, gods);
        c.DaysSinceDevotion.Should().Be(3);
        GodDeedSystem.Apply(c, GodAct.WildernessExplored, gods);
        c.DaysSinceDevotion.Should().Be(0);
    }

    // ---------------- Victory, the week without casting ----------------

    [Fact]
    public void RecordVictory_UndeadAndStrongerFoe_OncePerFight()
    {
        var output = new MemoryStream();
        var term = Term(output);
        var (sol, gods) = Worshipper("GdVicSol", "Solarius", 30);
        var slain = new List<Monster> { new Monster { Name = "Skeleton", Level = 1 }, new Monster { Name = "Zombie", Level = 1 } };
        GodDeedSystem.RecordVictory(sol, slain, term, gods);
        sol.GodFavor.Should().Be(31, "two undead in one fight are one deed");
        GodDeedSystem.RecordVictory(sol, new List<Monster> { new Monster { Name = "Goblin", Level = 1 } }, term, gods);
        sol.GodFavor.Should().Be(31, "no undead");

        var (val, gods2) = Worshipper("GdVicVal", "Valorian", 30);
        GodDeedSystem.RecordVictory(val, new List<Monster> { new Monster { Name = "Goblin", Level = 5 } }, term, gods2);
        val.GodFavor.Should().Be(30, "same level is not stronger");
        GodDeedSystem.RecordVictory(val, new List<Monster> { new Monster { Name = "Ogre", Level = 6 } }, term, gods2);
        val.GodFavor.Should().Be(31);
    }

    [Fact]
    public void NoCastWeek_ASpellcasterLosesAtSevenResets_OnceThenStartsOver()
    {
        var output = new MemoryStream();
        var term = Term(output);
        var (c, gods) = Worshipper("GdNoCast", "Arcanus", 30, CharacterClass.Magician);
        for (int d = 1; d < GameConfig.GodTabooNoCastDays; d++)
            GodDeedSystem.ApplyDailyReset(c, term, gods).Should().Be(0);
        c.DaysSinceSpellCast.Should().Be(6);
        GodDeedSystem.ApplyDailyReset(c, term, gods).Should().Be(-5);
        c.DaysSinceSpellCast.Should().Be(0);
        c.GodFavor.Should().Be(25);

        c.DaysSinceSpellCast = 6;
        GodDeedSystem.MarkSpellCast(c);
        GodDeedSystem.ApplyDailyReset(c, term, gods).Should().Be(0);
        c.DaysSinceSpellCast.Should().Be(1);
    }

    [Fact]
    public void NoCastWeek_NotCountedForClassesThatCannotCast()
    {
        var (c, gods) = Worshipper("GdNoCastW", "Arcanus", 30, CharacterClass.Warrior);
        for (int d = 0; d < 10; d++) GodDeedSystem.ApplyDailyReset(c, null, gods).Should().Be(0);
        c.DaysSinceSpellCast.Should().Be(0);
        c.GodFavor.Should().Be(30);
    }

    // ---------------- The Favor lines ----------------

    [Fact]
    public void Record_PrintsTheLossLine_AndTheGainLine()
    {
        var output = new MemoryStream();
        var term = Term(output);
        var (c, gods) = Worshipper("GdLines", "Judicar", 30);
        GodDeedSystem.Record(c, GodAct.Theft, term, gods).Should().Be(-3);
        term.StreamWriterInternal!.Flush();
        string loss = Encoding.UTF8.GetString(output.ToArray());
        loss.Should().Contain("Judicar").And.Contain("-3").And.Contain("27");
        GodDeedSystem.Record(c, GodAct.BountyCollected, term, gods).Should().Be(2);
        term.StreamWriterInternal!.Flush();
        Encoding.UTF8.GetString(output.ToArray()).Should().Contain("+2").And.Contain("29");
    }

    [Fact]
    public void ReportLoss_PrintsOnlyBelowZero()
    {
        var output = new MemoryStream();
        var term = Term(output);
        var (c, gods) = Worshipper("GdLossLine", "Amara", 20);
        FavorUi.ReportLoss(term, c, 0, gods);
        FavorUi.ReportLoss(term, c, 3, gods);
        term.StreamWriterInternal!.Flush();
        output.Length.Should().Be(0);
    }

    [Fact]
    public void Loc_LossLine_InAllFiveLanguages()
    {
        foreach (var lang in new[] { "en", "es", "fr", "it", "hu" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")));
            doc.RootElement.TryGetProperty("favor.loss", out var v).Should().BeTrue($"{lang} has favor.loss");
            string s = v.GetString()!;
            for (int i = 0; i < 3; i++) s.Should().Contain("{" + i + "}", $"{lang} favor.loss");
        }
    }

    // ---------------- Save plumbing (DaysSinceSpellCast) ----------------

    [Fact]
    public void DaysSinceSpellCast_RoundTripsThroughTheSave()
    {
        var c = Hero("GdSave", CharacterClass.Magician);
        c.DaysSinceSpellCast = 4;
        var method = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var data = (PlayerData)method.Invoke(SaveSystem.Instance, new object[] { c })!;
        data.DaysSinceSpellCast.Should().Be(4);
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        MenuKeysNeedEnterPref1115Tests.Restore(back).DaysSinceSpellCast.Should().Be(4);
        Source("Scripts/Editor/PlayerSaveEditor.cs").Should().Contain("p.DaysSinceSpellCast = EditorIO.PromptInt(");
    }

    // ---------------- Call sites ----------------

    [Theory]
    [InlineData("Scripts/Systems/CombatEngine.cs", "GodDeedSystem.RecordGroupVictory(result.Player, result.DefeatedMonsters, result.Teammates, terminal);", 1)]
    [InlineData("Scripts/Systems/CombatEngine.cs", "if (globalEscape && !result.PlayerActuallyDied) GodDeedSystem.Record(player, GodAct.Fled, terminal);", 1)]
    [InlineData("Scripts/Systems/CombatEngine.cs", "if (totalHeal > 0) GodDeedSystem.Record(player, GodAct.AllyHealed, terminal);", 1)]
    [InlineData("Scripts/Systems/CombatEngine.cs", "if (actualHeal > 0) GodDeedSystem.Record(player, GodAct.AllyHealed, terminal);", 1)]
    [InlineData("Scripts/Systems/CombatEngine.cs", "if (!target.IsAlive) GodDeedSystem.Record(player, GodAct.StealthKill, terminal);", 1)]
    [InlineData("Scripts/Systems/CombatEngine.cs", "GodDeedSystem.Record(result.Player, GodAct.DeathWitnessed, terminal);", 1)]
    [InlineData("Scripts/Core/Character.cs", "GodDeedSystem.Record(character, GodAct.DrugUse);", 1)]
    [InlineData("Scripts/Systems/RelationshipSystem.cs", "GodDeedSystem.Record(character1, GodAct.Marriage);", 1)]
    [InlineData("Scripts/Systems/RelationshipSystem.cs", "GodDeedSystem.Record(character2, GodAct.Marriage);", 1)]
    [InlineData("Scripts/Systems/RelationshipSystem.cs", "GodDeedSystem.Record(character1, GodAct.FriendMade);", 1)]
    [InlineData("Scripts/Systems/VisualNovelDialogueSystem.cs", "GodDeedSystem.Record(player, GodAct.Marriage, terminal);", 1)]
    [InlineData("Scripts/Systems/QuestSystem.cs", "if (matchingBounties.Count > 0) GodDeedSystem.Record(player, GodAct.BountyCollected);", 1)]
    [InlineData("Scripts/Systems/QuestSystem.cs", "if (claimed.Count > 0) GodDeedSystem.Record(winner, GodAct.BountyCollected);", 1)]
    [InlineData("Scripts/Locations/ArenaLocation.cs", "if (bountyReward > 0) GodDeedSystem.Record(currentPlayer, GodAct.BountyCollected, terminal);", 1)]
    [InlineData("Scripts/Locations/ArenaLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.PvpWin, terminal);", 1)]
    [InlineData("Scripts/Locations/CastleLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.ArrestOrdered, terminal);", 1)]
    [InlineData("Scripts/Locations/CastleLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.DeathWitnessed, terminal);", 1)]
    [InlineData("Scripts/Locations/DarkAlleyLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.Theft, terminal);", 1)]
    [InlineData("Scripts/Locations/DarkAlleyLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.Imprisoned, terminal);", 1)]
    [InlineData("Scripts/Locations/DarkAlleyLocation.cs", "GodDeedSystem.Record(player, GodAct.Imprisoned, term);", 1)]
    [InlineData("Scripts/Locations/BankLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.Theft, terminal);", 1)]
    [InlineData("Scripts/Systems/StreetEncounterSystem.cs", "GodDeedSystem.Record(player, GodAct.Imprisoned, terminal);", 3)]
    [InlineData("Scripts/Systems/StreetEncounterSystem.cs", "GodDeedSystem.Record(player, GodAct.StreetBrawl, terminal);", 1)]
    [InlineData("Scripts/Locations/PrisonWalkLocation.cs", "GodDeedSystem.Record(player, GodAct.Imprisoned, terminal);", 1)]
    [InlineData("Scripts/Locations/BaseLocation.cs", "GodDeedSystem.Record(p, GodAct.Imprisoned, terminal);", 1)]
    [InlineData("Scripts/Locations/BaseLocation.cs", "GodDeedSystem.Record(player, GodAct.Murder, terminal);", 1)]
    [InlineData("Scripts/Locations/BaseLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.Murder, terminal);", 0)]
    [InlineData("Scripts/Systems/CombatEngine.cs", "if (actualHeal > 0 && tgt != player) GodDeedSystem.Record(player, GodAct.AllyHealed, terminal);", 1)]
    [InlineData("Scripts/Systems/CombatEngine.cs", "if (partyAllyHealed) GodDeedSystem.Record(player, GodAct.AllyHealed, terminal);", 1)]
    [InlineData("Scripts/Locations/BaseLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.DeathWitnessed, terminal);", 1)]
    [InlineData("Scripts/Locations/ChurchLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.Confession, terminal);", 1)]
    [InlineData("Scripts/Locations/TempleLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.Confession, terminal);", 1)]
    [InlineData("Scripts/Locations/TempleLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.Desecration, terminal);", 1)]
    [InlineData("Scripts/Locations/HomeLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.HerbGathered, terminal);", 1)]
    [InlineData("Scripts/Locations/SettlementLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.SettlementWork, terminal);", 1)]
    [InlineData("Scripts/Locations/SettlementLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.LibraryRead, terminal);", 1)]
    [InlineData("Scripts/Locations/WildernessLocation.cs", "GodDeedSystem.Record(currentPlayer, GodAct.WildernessExplored, terminal);", 1)]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "GodDeedSystem.Record(player, GodAct.UndeadRaised, terminal);", 1)]
    [InlineData("Scripts/Systems/SpellLearningSystem.cs", "GodDeedSystem.Record(player, GodAct.SpellLearned, terminal);", 1)]
    [InlineData("Scripts/Systems/SpellSystem.cs", "GodDeedSystem.MarkSpellCast(caster);", 1)]
    [InlineData("Scripts/Systems/DailySystemManager.cs", "GodDeedSystem.ApplyDailyReset(player, terminal);", 1)]
    [InlineData("Scripts/Systems/CompanionSystem.cs", "GodDeedSystem.Record(griefPlayer, GodAct.DeathWitnessed, terminal);", 1)]
    [InlineData("Scripts/Systems/CompanionSystem.cs", "GodDeedSystem.Record(griefPlayer, GodAct.DeathWitnessed, griefTerminal);", 1)]
    public void CallSite_CallsTheHook(string file, string hook, int times)
    {
        Count(Source(file), hook).Should().Be(times, $"{file} calls {hook}");
    }

    [Fact]
    public void CallSite_MurderIsRecordedBeforeTheConsequencesThatCanEndTheSession()
    {
        string src = Source("Scripts/Locations/BaseLocation.cs");
        int method = src.IndexOf("internal async Task ApplyMurderConsequences(Character player, NPC victim)", StringComparison.Ordinal);
        int hook = src.IndexOf("GodDeedSystem.Record(player, GodAct.Murder, terminal);", StringComparison.Ordinal);
        method.Should().BeGreaterThan(0);
        int firstAwait = src.IndexOf("await ", method, StringComparison.Ordinal);
        hook.Should().BeInRange(method, firstAwait, "recorded first in ApplyMurderConsequences, before capture, execution or the prison exit");
    }
}
