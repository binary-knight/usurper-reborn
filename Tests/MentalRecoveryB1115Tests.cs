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
}
