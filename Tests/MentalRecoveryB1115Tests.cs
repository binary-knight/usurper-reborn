using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 5b: the remaining once-a-day recovery sources (talking with an NPC
/// friend, the first wilderness expedition, learning) and their call sites.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalRecoveryB1115Tests
{
    private static NPC Npc(string id, string name) => new NPC { ID = id, Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100 };

    private static void SetScore(Character a, Character b, int score)
    {
        var rec = RelationshipSystem.GetOrCreateRelationship(a, b);
        if (rec.Name1 == a.Name) rec.Relation1 = score; else rec.Relation2 = score;
    }

    /// <summary>Index of text in body, failing when it is missing.</summary>
    private static int At(string body, string text)
    {
        int i = body.IndexOf(text, StringComparison.Ordinal);
        i.Should().BeGreaterOrEqualTo(0, $"expected '{text}'");
        return i;
    }

    // Behaviour

    [Fact]
    public void Talking_with_a_friend_gives_five_once_a_day()
    {
        RelationshipSystem.Instance.Reset();
        var c = Hero("Talker", 40);
        var friend = Npc("npc_b_friend_1", "Good Friend");
        SetScore(c, friend, GameConfig.RelationFriendship);
        GameConfig.MentalFriendTalkGain.Should().Be(5);
        MentalSystem.ApplyFriendTalk(c, friend).Should().Be(5);
        MentalSystem.UsedToday(c, MentalDailySource.FriendTalk).Should().BeTrue();
        MentalSystem.ApplyFriendTalk(c, friend).Should().Be(0, "once a day");
        c.Mental.Should().Be(45);
        MentalSystem.ApplyDailyReset(c);
        int afterReset = c.Mental;
        MentalSystem.ApplyFriendTalk(c, friend).Should().Be(5, "a new day");
        c.Mental.Should().Be(afterReset + 5);
    }

    [Fact]
    public void Talking_with_a_stranger_gives_nothing_and_keeps_the_day()
    {
        RelationshipSystem.Instance.Reset();
        var c = Hero("Talker", 40);
        var stranger = Npc("npc_b_stranger_1", "Stranger");
        var trusted = Npc("npc_b_trusted_1", "Trusted");
        SetScore(c, trusted, GameConfig.RelationTrust);
        MentalSystem.ApplyFriendTalk(c, stranger).Should().Be(0);
        MentalSystem.ApplyFriendTalk(c, trusted).Should().Be(0, "trust is one step short of friendship");
        MentalSystem.UsedToday(c, MentalDailySource.FriendTalk).Should().BeFalse();
        c.Mental.Should().Be(40);
    }

    [Fact]
    public void The_first_wilderness_expedition_gives_six_once_a_day()
    {
        var c = Hero("Ranger", 40);
        GameConfig.MentalWildernessGain.Should().Be(6);
        MentalSystem.ApplyWilderness(c).Should().Be(6);
        MentalSystem.ApplyWilderness(c).Should().Be(0);
        MentalSystem.UsedToday(c, MentalDailySource.Wilderness).Should().BeTrue();
        c.Mental.Should().Be(46);
    }

    [Fact]
    public void Learning_gives_four_once_a_day_shared_by_every_kind()
    {
        var c = Hero("Scholar", 40);
        GameConfig.MentalLearningGain.Should().Be(4);
        MentalSystem.ApplyLearning(c).Should().Be(4);
        MentalSystem.ApplyLearning(c).Should().Be(0, "a spell, training and the Library share one Learning day");
        MentalSystem.UsedToday(c, MentalDailySource.Learning).Should().BeTrue();
        c.Mental.Should().Be(44);
    }

    // Wiring

    [Fact]
    public void Quick_chat_with_an_npc_is_wired()
    {
        var body = Body(Src("Locations", "BaseLocation.cs"), "ChatWithNPC");
        int gain = At(body, "MentalUi.ReportGain(terminal, currentPlayer, mentalBeforeTalk, MentalSystem.ApplyFriendTalk(currentPlayer, npc))");
        At(body, "int mentalBeforeTalk = currentPlayer.Mental;").Should().BeLessThan(gain);
        At(body, "await terminal.PressAnyKey()").Should().BeGreaterThan(gain);
    }

    [Fact]
    public void Conversation_chat_topics_are_wired()
    {
        var body = Body(Src("Systems", "VisualNovelDialogueSystem.cs"), "HandleChatOption");
        int gain = At(body, "MentalUi.ReportGain(terminal, player, mentalBeforeTalk, MentalSystem.ApplyFriendTalk(player, npc))");
        At(body, "int mentalBeforeTalk = player.Mental;").Should().BeLessThan(gain);
        At(body, "await terminal.PressAnyKey()").Should().BeGreaterThan(gain);
    }

    [Fact]
    public void Wilderness_expedition_is_wired_after_the_day_count_and_before_the_encounter()
    {
        var body = Body(Src("Locations", "WildernessLocation.cs"), "ExploreRegion");
        int gain = At(body, "MentalUi.ReportGain(terminal, currentPlayer, mentalBeforeTrip, MentalSystem.ApplyWilderness(currentPlayer))");
        At(body, "currentPlayer.WildernessExplorationsToday++").Should().BeLessThan(gain);
        At(body, "Random.Shared.Next(100)").Should().BeGreaterThan(gain, "a fight cannot skip the gain");
    }

    [Fact]
    public void Learning_a_new_spell_is_wired()
    {
        var src = Src("Systems", "SpellLearningSystem.cs");
        int learn = At(src, "player.Spell[learnLevel - 1][0] = true;");
        int end = src.IndexOf("continue;", learn, StringComparison.Ordinal);
        var block = src.Substring(learn, end - learn);
        block.Should().Contain("MentalUi.ReportGain(terminal, player, mentalBeforeSpell, MentalSystem.ApplyLearning(player))");
    }

    [Fact]
    public void A_training_session_is_wired_after_the_points_are_spent()
    {
        var body = Body(Src("Systems", "TrainingSystem.cs"), "TrainSkill");
        int gain = At(body, "MentalUi.ReportGain(terminal, player, mentalBeforeTraining, MentalSystem.ApplyLearning(player))");
        At(body, "player.TrainingPoints -= trainingPointsToSpend;").Should().BeLessThan(gain);
    }

    [Fact]
    public void Library_reading_is_wired_after_the_buff_is_granted()
    {
        var body = Body(Src("Locations", "SettlementLocation.cs"), "UseLibraryService");
        int gain = At(body, "MentalUi.ReportGain(terminal, currentPlayer, mentalBeforeReading, MentalSystem.ApplyLearning(currentPlayer))");
        At(body, "currentPlayer.SettlementBuffType = (int)SettlementBuffType.LibraryXP;").Should().BeLessThan(gain);
    }

    // Healer (commit 2)

    /// <summary>The brace-balanced body of the first method named method, any return type (Task&lt;bool&gt; too).</summary>
    private static string Method(string src, string method)
    {
        var m = Regex.Match(src, @"\b(?:Task(?:<[^>\n]*>)?|void|int|bool)\s+" + Regex.Escape(method) + @"\s*\(");
        m.Success.Should().BeTrue($"{method} must be defined");
        int brace = src.IndexOf('{', m.Index); int depth = 0;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(brace, i - brace + 1);
        }
        throw new InvalidOperationException("unbalanced braces");
    }

    private static string Healer() => Src("Locations", "HealerLocation.cs");
    private static string Dungeon() => Src("Locations", "DungeonLocation.cs");

    [Fact]
    public void Talk_therapy_restores_to_full_above_the_addiction_cap()
    {
        var c = Hero("Patient", 30, addict: 60);
        MentalSystem.GetCap(c).Should().Be(70);
        MentalSystem.RestoreFull(c).Should().Be(70);
        c.Mental.Should().Be(GameConfig.MaxMentalStability);
        MentalSystem.Change(c, 5).Should().Be(0, "ordinary gains still stop at the cap");
        c.Mental.Should().Be(GameConfig.MaxMentalStability);
    }

    [Fact]
    public void Talk_therapy_clears_the_broken_affliction()
    {
        var c = Hero("Patient", 20);
        c.MentalBroken = true;
        MentalSystem.RestoreFull(c).Should().Be(80);
        c.MentalBroken.Should().BeFalse();
    }

    [Fact]
    public void Talk_therapy_has_nothing_to_treat_only_at_full_without_broken()
    {
        var c = Hero("Patient", GameConfig.MaxMentalStability);
        MentalSystem.NeedsTherapy(c).Should().BeFalse();
        c.MentalBroken = true;
        MentalSystem.NeedsTherapy(c).Should().BeTrue("Broken is treated even at full Mental");
        c.MentalBroken = false; c.Mental = 99;
        MentalSystem.NeedsTherapy(c).Should().BeTrue();
    }

    [Fact]
    public void Talk_therapy_cost_is_missing_points_times_ten_plus_two_per_level_in_long()
    {
        var c = Hero("Patient", 60); c.Level = 20;
        MentalSystem.TherapyCost(c).Should().Be(40L * (10 + 2 * 20));
        c.Mental = 0; c.Level = 1_000_000_000;
        MentalSystem.TherapyCost(c).Should().Be(100L * (10L + 2L * 1_000_000_000), "long math, no wrap");
        c.Mental = GameConfig.MaxMentalStability;
        MentalSystem.TherapyCost(c).Should().Be(0);
    }

    [Fact]
    public void Therapy_says_nothing_to_treat_before_any_charge()
    {
        var body = Method(Healer(), "TalkTherapy");
        int check = At(body, "if (!MentalSystem.NeedsTherapy(player))");
        int nothing = At(body, "Loc.Get(\"healer.therapy_nothing\"");
        int charge = At(body, "player.Gold -= cost;");
        check.Should().BeLessThan(nothing);
        nothing.Should().BeLessThan(charge);
        body.Substring(nothing, charge - nothing).Should().Contain("return;");
    }

    [Fact]
    public void Therapy_refuses_short_gold_before_the_confirm()
    {
        var body = Method(Healer(), "TalkTherapy");
        int shortGold = At(body, "if (player.Gold < cost)");
        int refuse = At(body, "Loc.Get(\"healer.therapy_cant_afford\"");
        int confirm = At(body, "await terminal.AskYesNoAsync(Loc.Get(\"healer.therapy_confirm\"))");
        shortGold.Should().BeLessThan(refuse);
        refuse.Should().BeLessThan(confirm);
        body.Substring(refuse, confirm - refuse).Should().Contain("return;");
    }

    [Fact]
    public void Therapy_confirms_then_charges_then_restores()
    {
        var body = Method(Healer(), "TalkTherapy");
        int confirm = At(body, "if (!await terminal.AskYesNoAsync(Loc.Get(\"healer.therapy_confirm\")))");
        int charge = At(body, "player.Gold -= cost;");
        int restore = At(body, "MentalSystem.RestoreFull(player)");
        confirm.Should().BeLessThan(charge);
        charge.Should().BeLessThan(restore);
        At(body, "long cost = MentalSystem.TherapyCost(player);").Should().BeLessThan(charge);
        At(body, "MentalUi.ReportGain(terminal, player, mentalBefore, restored)").Should().BeGreaterThan(restore);
    }

    [Fact]
    public void Willow_draught_costs_two_healing_potions()
    {
        foreach (int level in new[] { 1, 10, 50, 100 })
            MentalSystem.WillowDraughtPrice(level).Should().Be(2 * GameConfig.GetHealingPotionCost(level));
        var body = Method(Healer(), "BuyWillowDraught");
        int price = At(body, "long price = MentalSystem.WillowDraughtPrice(player.Level);");
        At(body, "CityControlSystem.CalculateHealingTaxedPrice(price)").Should().BeGreaterThan(price, "taxed as BuyPotions taxes");
        At(body, "player.Gold -= priceWithTax;").Should().BeGreaterThan(price);
    }

    [Fact]
    public void Willow_draught_purchase_is_refused_at_three()
    {
        GameConfig.MaxWillowDraughts.Should().Be(3);
        var body = Method(Healer(), "BuyWillowDraught");
        int refuse = At(body, "if (player.WillowDraughts >= GameConfig.MaxWillowDraughts)");
        int charge = At(body, "player.Gold -= priceWithTax;");
        refuse.Should().BeLessThan(charge);
        body.Substring(refuse, charge - refuse).Should().Contain("return;");
        At(body, "player.WillowDraughts++;").Should().BeGreaterThan(charge);
    }

    [Fact]
    public void Willow_draught_purchase_confirms_and_refuses_short_gold()
    {
        var body = Method(Healer(), "BuyWillowDraught");
        int shortGold = At(body, "if (player.Gold < priceWithTax)");
        int confirm = At(body, "await terminal.AskYesNoAsync(Loc.Get(\"healer.willow_confirm\"))");
        int charge = At(body, "player.Gold -= priceWithTax;");
        shortGold.Should().BeLessThan(confirm);
        confirm.Should().BeLessThan(charge);
    }

    [Fact]
    public void Drinking_a_willow_draught_gives_twenty_and_uses_one()
    {
        var c = Hero("Delver", 40); c.WillowDraughts = 2;
        GameConfig.MentalWillowDraughtGain.Should().Be(20);
        MentalSystem.DrinkWillowDraught(c).Should().Be(20);
        c.WillowDraughts.Should().Be(1);
        c.Mental.Should().Be(60);
    }

    [Fact]
    public void A_willow_draught_is_kept_at_the_cap_or_when_none_is_carried()
    {
        var c = Hero("Delver", 70, addict: 60); c.WillowDraughts = 2;
        MentalSystem.DrinkWillowDraught(c).Should().Be(0);
        c.WillowDraughts.Should().Be(2, "at the cap the draught is not drunk");
        var none = Hero("Empty", 40);
        MentalSystem.DrinkWillowDraught(none).Should().Be(0);
        none.WillowDraughts.Should().Be(0);
        none.Mental.Should().Be(40);
    }

    [Fact]
    public void The_dungeon_drink_tells_the_player_at_the_cap_before_drinking()
    {
        var body = Method(Dungeon(), "DrinkWillowDraught");
        int cap = At(body, "if (player.Mental >= MentalSystem.GetCap(player))");
        int tell = At(body, "ExplainNoAction(\"dungeon.willow_at_cap\")");
        int drink = At(body, "MentalSystem.DrinkWillowDraught(player)");
        cap.Should().BeLessThan(tell);
        tell.Should().BeLessThan(drink);
        body.Substring(tell, drink - tell).Should().Contain("return;");
        At(body, "MentalUi.ReportGain(terminal, player, mentalBefore, applied)").Should().BeGreaterThan(drink);
    }

    [Fact]
    public void The_dungeon_drink_key_is_U_and_collides_with_nothing()
    {
        var room = Method(Dungeon(), "ProcessRoomChoice");
        Regex.Matches(room, "case \"U\":").Count.Should().Be(1);
        At(room, "case \"U\":").Should().BeLessThan(At(room, "await DrinkWillowDraught();"));
        Regex.IsMatch(room, "\"U\" => Direction").Should().BeFalse("U is not a direction");
        var global = Method(Src("Locations", "BaseLocation.cs"), "TryProcessGlobalCommand");
        global.Should().NotContain("case \"U\":");
        Regex.Matches(Method(Dungeon(), "ProcessOverviewChoice"), "case \"U\":").Count.Should().Be(0, "drinking is a room action");
    }

    [Fact]
    public void The_dungeon_room_menus_list_the_drink_key()
    {
        var sr = Method(Dungeon(), "ShowRoomActions");
        sr.Should().Contain("WriteSRMenuOption(\"U\", Loc.Get(\"dungeon.willow\"");
        sr.Should().Contain("terminal.Write(\"U\");");
        Method(Dungeon(), "DisplayRoomViewBBS").Should().Contain("row1.Add((\"U\", \"bright_yellow\", Loc.Get(\"dungeon.bbs_willow\"");
    }

    [Fact]
    public void Rehab_lowers_addiction_then_adds_fifteen_and_clears_broken()
    {
        var body = Method(Healer(), "CureAddiction");
        int addict = At(body, "player.Addict = 0;");
        int rehab = At(body, "MentalSystem.ApplyRehab(player)");
        addict.Should().BeLessThan(rehab, "the cap lifts before the gain");
        At(body, "MentalUi.ReportGain(terminal, player, mentalBeforeRehab, MentalSystem.ApplyRehab(player))").Should().Be(rehab - "MentalUi.ReportGain(terminal, player, mentalBeforeRehab, ".Length);

        var c = Hero("Recovering", 50, addict: 80);
        c.MentalBroken = true;
        c.Addict = 0;
        GameConfig.MentalRehabGain.Should().Be(15);
        MentalSystem.ApplyRehab(c).Should().Be(15);
        c.Mental.Should().Be(65);
        c.MentalBroken.Should().BeFalse();
    }

    [Fact]
    public void Healer_keys_T_and_W_are_wired_and_listed()
    {
        var src = Healer();
        var choice = Method(src, "ProcessChoice");
        At(choice, "case \"T\":").Should().BeLessThan(At(choice, "await TalkTherapy();"));
        At(choice, "case \"W\":").Should().BeLessThan(At(choice, "await BuyWillowDraught();"));
        Regex.Matches(choice, "case \"T\":").Count.Should().Be(1);
        Regex.Matches(choice, "case \"W\":").Count.Should().Be(1);
        Method(src, "DisplayLocationSR").Should().Contain("WriteSRMenuOption(\"T\"").And.Contain("WriteSRMenuOption(\"W\"");
        Method(src, "DisplayLocationBBS").Should().Contain("(\"T\", \"bright_yellow\"").And.Contain("(\"W\", \"bright_yellow\"");
        var menu = Method(src, "ShowMenu");
        menu.Should().Contain("terminal.Write(\"T\");").And.Contain("terminal.Write(\"W\");");
        Method(src, "ShowFullMenu").Should().Contain("healer.menu_therapy").And.Contain("healer.menu_willow");
    }

    [Fact]
    public void The_status_sheet_shows_the_willow_draught_count()
    {
        var src = Src("Locations", "BaseLocation.cs");
        int mental = At(src, "Loc.Get(\"base.stat_mental\")");
        int willow = At(src, "Loc.Get(\"status.willow_draughts\", currentPlayer.WillowDraughts, GameConfig.MaxWillowDraughts)");
        willow.Should().BeGreaterThan(mental);
    }

    [Fact]
    public void New_keys_exist_in_all_five_languages()
    {
        var keys = new[]
        {
            "healer.therapy", "healer.willow", "healer.menu_therapy_suffix", "healer.menu_willow_suffix", "healer.menu_therapy",
            "healer.menu_willow", "healer.therapy_title", "healer.therapy_nothing", "healer.therapy_intro", "healer.therapy_quote",
            "healer.therapy_cant_afford", "healer.therapy_confirm", "healer.therapy_session", "healer.willow_max", "healer.willow_desc",
            "healer.willow_price", "healer.willow_cant_afford", "healer.tax_willow", "healer.willow_confirm", "healer.willow_bought",
            "mental.broken_cleared", "dungeon.willow", "dungeon.bbs_willow", "dungeon.no_willow", "dungeon.willow_at_cap",
            "dungeon.willow_drink", "status.willow_draughts",
        };
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json"));
            foreach (var k in keys) text.Should().Contain($"\"{k}\":", $"{lang} needs {k}");
            text.Should().Contain("A,T,W,S,R", $"{lang} expert prompt lists T and W");
        }
    }
}
