using System;
using System.IO;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;
using static UsurperReborn.Tests.MentalRecoveryB1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Healer talk therapy: the healing tax as the Healer's other services charge it, and a
/// minimum of MentalTherapyBrokenMinPoints billed points while Broken.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalTherapyTax1115Tests
{
    private static long PerPoint(int level) => GameConfig.MentalTherapyCostBase + (long)GameConfig.MentalTherapyCostPerLevel * level;

    [Fact]
    public void Broken_at_full_mental_bills_the_minimum_ten_points()
    {
        GameConfig.MentalTherapyBrokenMinPoints.Should().Be(10);
        var c = Hero("Patient", GameConfig.MaxMentalStability); c.Level = 20;
        c.MentalBroken = true;
        MentalSystem.NeedsTherapy(c).Should().BeTrue();
        MentalSystem.TherapyCost(c).Should().Be(10 * PerPoint(20));
        c.Mental = 95;
        MentalSystem.TherapyCost(c).Should().Be(10 * PerPoint(20), "five missing points still bill ten while Broken");
    }

    [Fact]
    public void Broken_with_more_than_ten_missing_bills_the_missing_points()
    {
        var c = Hero("Patient", 50); c.Level = 20;
        c.MentalBroken = true;
        MentalSystem.TherapyCost(c).Should().Be(50 * PerPoint(20));
    }

    [Fact]
    public void Not_broken_keeps_the_missing_points_formula_and_nothing_at_full()
    {
        var c = Hero("Patient", 95); c.Level = 20;
        MentalSystem.TherapyCost(c).Should().Be(5 * PerPoint(20), "no minimum without Broken");
        c.Mental = GameConfig.MaxMentalStability;
        MentalSystem.NeedsTherapy(c).Should().BeFalse();
        MentalSystem.TherapyCost(c).Should().Be(0);
    }

    [Fact]
    public void Therapy_quote_shows_the_taxed_total_and_the_breakdown()
    {
        var body = Method(Healer(), "TalkTherapy");
        int cost = At(body, "long cost = MentalSystem.TherapyCost(player);");
        int tax = At(body, "var (_, _, costWithTax) = CityControlSystem.CalculateHealingTaxedPrice(cost);");
        int quote = At(body, "Loc.Get(\"healer.therapy_quote\", player.Mental, GameConfig.MaxMentalStability, $\"{costWithTax:N0}\")");
        int breakdown = At(body, "CityControlSystem.Instance.DisplayTaxBreakdown(terminal, Loc.Get(\"healer.tax_therapy\"), cost);");
        int confirm = At(body, "await terminal.AskYesNoAsync(Loc.Get(\"healer.therapy_confirm\"))");
        cost.Should().BeLessThan(tax);
        tax.Should().BeLessThan(quote);
        breakdown.Should().BeLessThan(confirm);
    }

    [Fact]
    public void Healer_menu_lists_the_taxed_therapy_price()
    {
        Healer().Should().Contain("Loc.Get(\"healer.menu_therapy\", $\"{CityControlSystem.CalculateHealingTaxedPrice(MentalSystem.TherapyCost(player)).total:N0}\")");
    }

    [Fact]
    public void Therapy_refuses_short_of_the_taxed_total_before_the_confirm()
    {
        var body = Method(Healer(), "TalkTherapy");
        int shortGold = At(body, "if (player.Gold < costWithTax)");
        int confirm = At(body, "await terminal.AskYesNoAsync(Loc.Get(\"healer.therapy_confirm\"))");
        shortGold.Should().BeLessThan(confirm);
        body.Should().NotContain("player.Gold < cost)");
    }

    [Fact]
    public void Therapy_charges_the_taxed_total_and_records_the_sale_tax()
    {
        var body = Method(Healer(), "TalkTherapy");
        int confirm = At(body, "if (!await terminal.AskYesNoAsync(Loc.Get(\"healer.therapy_confirm\")))");
        int charge = At(body, "player.Gold -= costWithTax;");
        int saleTax = At(body, "CityControlSystem.Instance.ProcessSaleTax(cost);");
        confirm.Should().BeLessThan(charge);
        charge.Should().BeLessThan(saleTax);
        At(body, "player.Statistics.RecordGoldSpent(costWithTax);").Should().BeGreaterThan(confirm);
        body.Should().NotContain("player.Gold -= cost;");
    }

    [Fact]
    public void Therapy_tax_key_exists_in_all_five_languages()
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json"))
                .Should().Contain("\"healer.tax_therapy\":", $"{lang} needs healer.tax_therapy");
    }
}
