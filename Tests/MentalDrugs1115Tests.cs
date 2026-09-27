using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;
using static UsurperReborn.Tests.MentalRecoveryB1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 6, commit 2: drug highs above the cap, tolerance, the crash when the drug
/// wears off, overdose and withdrawal, the order against the daily reset, and the DrugSystem text.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalDrugs1115Tests
{
    private static Character OnDrug(Character c, DrugType drug, int days)
    {
        c.ActiveDrug = drug;
        c.DrugEffectDays = days;
        return c;
    }

    /// <summary>The brace-balanced block after the first occurrence of signature in src (any return type).</summary>
    private static string Block(string src, string signature)
    {
        int at = At(src, signature);
        int brace = src.IndexOf('{', at); int depth = 0;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(brace, i - brace + 1);
        }
        throw new InvalidOperationException("unbalanced braces");
    }

    private const string UseDrugSig = "UseDrug(Character character, DrugType drug";
    private const string DailySig = "string ProcessDailyDrugEffects(Character character)";

    // Behaviour

    [Fact]
    public void A_high_gives_eight_or_fifteen_for_the_dark_drugs_and_records_the_boost()
    {
        GameConfig.MentalDrugHighGain.Should().Be(8);
        GameConfig.MentalDrugHighStrongGain.Should().Be(15);
        var c = Hero("User", 50);
        MentalSystem.ApplyDrugHigh(c, DrugType.Steroids, 10).Should().Be(8);
        c.MentalDrugBoost.Should().Be(8);
        c.MentalDrugUses.Should().Be(1);
        c.MentalLastDrugDay.Should().Be(10);
        MentalSystem.ApplyDrugHigh(Hero("A", 50), DrugType.DarkEssence, 10).Should().Be(15);
        MentalSystem.ApplyDrugHigh(Hero("B", 50), DrugType.DemonBlood, 10).Should().Be(15);
        var npc = Hero("Npc", 50); npc.AI = CharacterAI.Computer;
        MentalSystem.ApplyDrugHigh(npc, DrugType.Steroids, 10).Should().Be(0, "NPCs are skipped");
    }

    [Fact]
    public void A_high_passes_the_addiction_cap_but_never_one_hundred()
    {
        var capped = Hero("Addict", 80, addict: 40);   // cap 80
        MentalSystem.ApplyDrugHigh(capped, DrugType.Steroids, 10).Should().Be(8);
        capped.Mental.Should().Be(88);
        var near = Hero("Near", 96);
        MentalSystem.ApplyDrugHigh(near, DrugType.DemonBlood, 10).Should().Be(4);
        near.Mental.Should().Be(100);
        near.MentalDrugBoost.Should().Be(4, "the boost records what was applied");
    }

    [Fact]
    public void Stacked_highs_add_up_in_the_boost()
    {
        var c = Hero("User", 50);
        MentalSystem.ApplyDrugHigh(c, DrugType.Steroids, 10);
        MentalSystem.ApplyDrugHigh(c, DrugType.Steroids, 10);
        c.MentalDrugBoost.Should().Be(8 + 6);
    }

    [Fact]
    public void Tolerance_shrinks_the_high_within_three_days_and_resets_after()
    {
        GameConfig.MentalDrugToleranceWindowDays.Should().Be(3);
        var c = Hero("User", 20);
        MentalSystem.ApplyDrugHigh(c, DrugType.Steroids, 10).Should().Be(8);
        MentalSystem.ApplyDrugHigh(c, DrugType.Steroids, 12).Should().Be(6, "second use, 75 percent");
        MentalSystem.ApplyDrugHigh(c, DrugType.Steroids, 15).Should().Be(4, "three days later still counts, 50 percent");
        c.MentalDrugUses.Should().Be(3);
        MentalSystem.ApplyDrugHigh(c, DrugType.Steroids, 19).Should().Be(8, "four days later starts over");
        c.MentalDrugUses.Should().Be(1);
        MentalSystem.GetDrugHighPct(4).Should().Be(25);
        MentalSystem.GetDrugHighPct(9).Should().Be(25, "the high never falls below a quarter");
        c.MentalLastDrugDay = 30; c.MentalDrugUses = 2;
        MentalSystem.ApplyDrugHigh(c, DrugType.Steroids, 5).Should().Be(8, "a day counter that went back starts over");
    }

    [Fact]
    public void The_crash_is_twice_the_boost_plus_half_per_extra_use_rounded()
    {
        MentalSystem.GetDrugCrash(8, 1).Should().Be(16);
        MentalSystem.GetDrugCrash(6, 2).Should().Be(15);
        MentalSystem.GetDrugCrash(3, 2).Should().Be(8, "7.5 rounds up");
        MentalSystem.GetDrugCrash(5, 3).Should().Be(15);
        MentalSystem.GetDrugCrash(0, 3).Should().Be(0);
        var c = Hero("User", 88, addict: 40);   // cap 80, above it on the high
        c.MentalDrugBoost = 8; c.MentalDrugUses = 1;
        MentalSystem.ApplyDrugCrash(c).Should().Be(-16);
        c.Mental.Should().Be(72, "a loss ignores the cap");
        c.MentalDrugBoost.Should().Be(0);
        MentalSystem.ApplyDrugCrash(c).Should().Be(0, "nothing left to crash");
    }

    [Fact]
    public void The_crash_lands_when_the_drug_wears_off_not_before()
    {
        var c = OnDrug(Hero("User", 70), DrugType.Haste, 2);
        c.MentalDrugBoost = 8; c.MentalDrugUses = 1;
        DrugSystem.ProcessDailyDrugEffects(c);
        c.Mental.Should().Be(70, "one day left");
        c.MentalDrugBoost.Should().Be(8);
        DrugSystem.ProcessDailyDrugEffects(c).Should().Contain(Loc.Get("drugs.mental_crash"));
        c.Mental.Should().Be(54);
        c.MentalDrugBoost.Should().Be(0);
    }

    [Fact]
    public void The_daily_reset_keeps_a_pending_high_and_the_crash_follows_it()
    {
        // Order: MentalSystem.ApplyDailyReset runs first (RunBasicDailyReset), the wear-off after it
        // (ProcessPlayerDailyEvents or ProcessDailyEvents). The reset leaves the high above the cap
        // while the drug is active, so the crash comes off the high once: never lost, never doubled.
        var c = OnDrug(Hero("User", 90, addict: 20), DrugType.Steroids, 1);   // cap 90
        MentalSystem.ApplyDrugHigh(c, DrugType.Steroids, 10).Should().Be(8);
        MentalSystem.ApplyDailyReset(c).Should().Be(0, "the high is kept on the day it wears off");
        c.Mental.Should().Be(98);
        DrugSystem.ProcessDailyDrugEffects(c);
        c.Mental.Should().Be(82, "98 minus a crash of 16");
        c.MentalDrugBoost.Should().Be(0);
        MentalSystem.ApplyDailyReset(c).Should().Be(8, "next day the daily gain stops at the cap");
    }

    [Fact]
    public void Off_drugs_the_daily_reset_drops_a_surplus_to_the_cap()
    {
        var c = Hero("User", 98, addict: 20);   // cap 90, no drug active
        c.MentalDrugBoost = 8;
        MentalSystem.ApplyDailyReset(c).Should().Be(-8);
        c.Mental.Should().Be(90);
        var therapy = OnDrug(Hero("Therapy", 100, addict: 20), DrugType.Steroids, 2);
        therapy.MentalDrugBoost = 4;
        MentalSystem.ApplyDailyReset(therapy);
        therapy.Mental.Should().Be(94, "only the pending high stays above the cap");
    }

    [Fact]
    public void Overdose_costs_eight_and_withdrawal_three_per_severity()
    {
        GameConfig.MentalOverdoseLoss.Should().Be(8);
        GameConfig.MentalWithdrawalLossPerSeverity.Should().Be(3);
        var c = Hero("User", 50);
        MentalSystem.ApplyOverdose(c).Should().Be(-8);
        MentalSystem.ApplyWithdrawal(c, 0).Should().Be(0);
        MentalSystem.ApplyWithdrawal(c, 2).Should().Be(-6);
        c.Mental.Should().Be(36);
        var addict = Hero("Addict", 40, addict: 75);   // severity 3, not on drugs
        DrugSystem.ProcessDailyDrugEffects(addict);
        addict.Mental.Should().Be(31);
    }

    // Wiring

    [Fact]
    public void UseDrug_applies_the_high_and_the_overdose()
    {
        var body = Block(Src("Core", "Character.cs"), UseDrugSig);
        int overdose = At(body, "MentalSystem.ApplyOverdose(character);");
        At(body, "Loc.Get(\"drugs.overdose\"").Should().BeGreaterThan(overdose);
        int high = At(body, "MentalSystem.ApplyDrugHigh(character, drug, DailySystemManager.Instance.CurrentDay);");
        high.Should().BeGreaterThan(At(body, "character.ActiveDrug = drug;"));
        At(body, "Loc.Get(\"drugs.taken\"").Should().BeGreaterThan(high);
    }

    [Fact]
    public void DrugSystem_text_has_no_english_literals()
    {
        var src = Src("Core", "Character.cs");
        foreach (var method in new[] { UseDrugSig, DailySig })
        {
            var body = Block(src, method);
            body.Should().NotContain("$\"", method);
            body.Should().NotContain("You take the").And.NotContain("OVERDOSE").And.NotContain("have worn off").And.NotContain("crave");
        }
    }

    [Fact]
    public void The_wear_off_and_withdrawal_are_wired_in_ProcessDailyDrugEffects()
    {
        var body = Block(Src("Core", "Character.cs"), DailySig);
        int crash = At(body, "MentalSystem.ApplyDrugCrash(character)");
        At(body, "character.ActiveDrug = DrugType.None;").Should().BeLessThan(crash, "the crash follows the wear-off");
        At(body, "MentalSystem.ApplyWithdrawal(character, withdrawalSeverity)").Should().BeGreaterThan(At(body, "int withdrawalSeverity = character.Addict / 25;"));
    }

    [Fact]
    public void The_drug_palace_reports_the_high_and_announces_the_overdose()
    {
        var body = Method(Src("Locations", "DarkAlleyLocation.cs"), "VisitDrugPalace");
        int before = At(body, "int mentalBeforeDrug = currentPlayer.Mental;");
        int use = At(body, "DrugSystem.UseDrug(currentPlayer, selected.drug)");
        before.Should().BeLessThan(use);
        At(body, "MentalUi.ReportGain(terminal, currentPlayer, mentalBeforeDrug, currentPlayer.Mental - mentalBeforeDrug)").Should().BeGreaterThan(use);
        At(body, "MentalUi.AnnounceMentalChange(terminal, currentPlayer, mentalBeforeDrug)").Should().BeGreaterThan(use);
    }

    [Fact]
    public void Both_daily_paths_announce_the_drug_change_after_the_daily_reset()
    {
        var src = Src("Systems", "DailySystemManager.cs");
        var reset = Body(src, "RunBasicDailyReset");
        int mental = At(reset, "MentalSystem.ApplyDailyReset(player);");
        At(reset, "await ProcessPlayerDailyEvents();").Should().BeGreaterThan(mental, "the wear-off runs after the reset");
        At(reset, "await ProcessDailyEvents();").Should().BeGreaterThan(mental, "the wear-off runs after the reset");

        var online = Body(src, "ProcessPlayerDailyEvents");
        At(online, "player.MentalDrugBoost > 0").Should().BeLessThan(At(online, "DrugSystem.ProcessDailyDrugEffects(player)"));
        At(online, "MentalUi.AnnounceMentalChange(terminal, player, mentalBeforeDrugs)").Should().BeGreaterThan(At(online, "DrugSystem.ProcessDailyDrugEffects(player)"));
        var offline = Body(src, "ProcessDailyEvents");
        At(offline, "drugPlayer.MentalDrugBoost > 0").Should().BeLessThan(At(offline, "DrugSystem.ProcessDailyDrugEffects(drugPlayer)"));
        At(offline, "MentalUi.AnnounceMentalChange(terminal, drugPlayer, mentalBeforeDrugs)").Should().BeGreaterThan(At(offline, "DrugSystem.ProcessDailyDrugEffects(drugPlayer)"));
    }

    [Fact]
    public void Rehab_forgives_the_pending_crash()
    {
        var body = Method(Src("Locations", "HealerLocation.cs"), "CureAddiction");
        int clear = At(body, "player.ActiveDrug = DrugType.None;");
        At(body, "player.MentalDrugBoost = 0;").Should().BeGreaterThan(clear);
        At(body, "player.MentalDrugUses = 0;").Should().BeGreaterThan(clear);
    }

    [Fact]
    public void New_keys_exist_in_all_five_languages()
    {
        var keys = new[]
        {
            "drugs.taken", "drugs.overdose", "drugs.worn_off", "drugs.dark_essence_crash",
            "drugs.withdrawal_shakes", "drugs.withdrawal_agony", "drugs.mental_crash",
        };
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json"));
            foreach (var k in keys) text.Should().Contain($"\"{k}\":", $"{lang} needs {k}");
        }
    }
}
