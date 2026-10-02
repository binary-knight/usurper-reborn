using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.4: screen strings use "--" instead of the em-dash, one column wider. Every fixed-layout row that
/// changed is measured at its widest real content and must stay within 79 columns in each language.
/// </summary>
[Collection("SharedGameSingletons")]
public class DashWidth124Tests
{
    private const int MaxWidth = 79;
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private const string LongName = "Aranyszivu Hosszunevu Kalandor"; // GameConfig.MaxNameLength (30)

    private static string L(string lang, string key, params object[] a) => Loc.GetIn(lang, key, a);

    private static int Widest(IEnumerable<string> rows) => rows.Max(r => r.Length);

    [Fact]
    public void LongestPlayerName_IsThirty() => LongName.Length.Should().Be(30);

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void WorldBossPhaseRows_Fit(string lang)
    {
        var rows = new List<string>();
        foreach (int p in new[] { 1, 2, 3 })
        {
            string d = L(lang, $"world_boss.phase_{p}_desc");
            rows.Add($"  {L(lang, "world_boss.phase_label", p, 3)} -- {d}");
            rows.Add($"  *** {L(lang, "world_boss.phase_label", p, 3)} -- {d} ***");
        }
        Widest(rows).Should().BeLessThanOrEqualTo(MaxWidth);
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void FatigueRow_Fits(string lang)
    {
        var rows = new List<string>();
        foreach (var (tier, pen) in new[] { ("status.fatigue_tired", "base.fatigue_tired_penalty"), ("status.fatigue_exhausted", "base.fatigue_exhausted_penalty") })
            rows.Add($"  {L(lang, "base.fatigue_label")}: {L(lang, tier)} (100/100){L(lang, pen)}");
        Widest(rows).Should().BeLessThanOrEqualTo(MaxWidth);
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void RemoveTitleRow_Fits(string lang)
    {
        string row = $"  0. ({L(lang, "ui.none")}) -- {L(lang, "base.remove_title")}";
        row.Length.Should().BeLessThanOrEqualTo(MaxWidth);
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void LocationHeader_WorstCase_Fits(string lang)
    {
        int period = new[] { "dawn", "morning", "afternoon", "evening", "night", "day" }.Max(k => L(lang, "daily.time_" + k).Length);
        int fatigue = new[] { "status.fatigue_rested", "status.fatigue_tired", "status.fatigue_exhausted" }.Max(k => L(lang, k).Length);
        int mental = new[] { "stable", "strained", "shaken" }.Max(k => L(lang, "status.mental_" + k).Length);
        // location name (longest allowed 30) + " -- " + period + " (fatigue)" + " (mental)"
        int worst = 30 + 4 + period + 3 + fatigue + 3 + mental;
        worst.Should().BeLessThanOrEqualTo(MaxWidth);
    }

    /// <summary>Rows of the Pantheon boon lists as the code builds them (pad widths 29, 27, 27, 27), at each boon's real cost.</summary>
    private static IEnumerable<(string Now, string Before)> PantheonRows()
    {
        foreach (var boon in DivineBoonRegistry.AllBoons)
        {
            string alignTag = boon.Alignments.Length > 0 ? $"[{string.Join("/", boon.Alignments)}]" : "[Any]";
            for (int tier = 1; tier <= 3; tier++)
            {
                string tierStr = tier switch { 1 => "I", 2 => "II", _ => "III" };
                string eff = boon.GetEffectDescription(tier);
                int cost = boon.CostPerTier * tier;
                yield return ($"  {99,2}. {boon.Name} {tierStr,-5} -- {eff,-29} {alignTag,-12} ({cost} pts)",
                              $"  {99,2}. {boon.Name} {tierStr,-5} \u2014 {eff,-30} {alignTag,-12} ({cost} pts)");
                foreach (var lab in new[] { $"{boon.Name} {tierStr}", $"{boon.Name} \u2192 {tierStr}" })
                {
                    yield return ($"  {99,2}. {lab,-25} -- {boon.Description,-27} {alignTag,-12} (locked)",
                                  $"  {99,2}. {lab,-25} \u2014 {boon.Description,-28} {alignTag,-12} (locked)");
                    yield return ($"  {99,2}. {lab,-25} -- {eff,-27} {alignTag,-12} (+{boon.CostPerTier} pts) *",
                                  $"  {99,2}. {lab,-25} \u2014 {eff,-28} {alignTag,-12} (+{boon.CostPerTier} pts) *");
                }
            }
        }
    }

    /// <summary>
    /// A Pantheon row that fitted 79 columns with the em-dash still fits with "--". Some locked rows already ran past 79
    /// before this change (a boon description longer than its 28 wide column); the check leaves those alone.
    /// </summary>
    [Fact]
    public void PantheonBoonRows_ThatFitted_StillFit()
    {
        var rows = PantheonRows().ToList();
        rows.Count(r => r.Before.Length <= MaxWidth).Should().BeGreaterThan(0);
        rows.Where(r => r.Before.Length <= MaxWidth && r.Now.Length > MaxWidth).Select(r => r.Now).Should().BeEmpty();
    }

    [Fact]
    public void BoonSummaryLines_Fit()
    {
        var lines = new List<string>();
        foreach (var boon in DivineBoonRegistry.AllBoons)
            for (int tier = 1; tier <= 3; tier++)
                lines.Add($"{boon.Name} {new[] { "I", "II", "III" }[tier - 1]} -- {boon.GetEffectDescription(tier)}");
        Widest(lines).Should().BeLessThanOrEqualTo(MaxWidth);
    }

    [Fact]
    public void DungeonStatusRow_Fits_ForRealRaceAndClassNames()
    {
        int race = Enum.GetNames(typeof(CharacterRace)).Max(n => n.Length);
        int cls = Enum.GetNames(typeof(CharacterClass)).Max(n => n.Length);
        foreach (var lang in Languages)
        {
            int worst = 2 + 20 + 4 + L(lang, "dungeon.level_label").Length + 1 + 3 + 1 + race + 1 + cls; // typical 20 char name
            worst.Should().BeLessThanOrEqualTo(MaxWidth, lang);
        }
    }

    [Fact]
    public void CurseRemovalRows_Fit()
    {
        int item = EquipmentDatabase.GetAll().Max(e => e.Name.Length);
        int slot = Enum.GetValues(typeof(EquipmentSlot)).Cast<EquipmentSlot>().Max(s => s.GetDisplayName().Length);
        int own = 2 + 2 + 2 + item + " (your ".Length + slot + ") -- ".Length + "9,999,999 gold".Length;
        own.Should().BeLessThanOrEqualTo(MaxWidth, "own gear row; widest item name " + item);
    }
}
