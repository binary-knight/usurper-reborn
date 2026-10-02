using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4 (design item F, second slice): a spouse neglected for SpouseNeglectLetterDays present
/// days writes the player one letter per neglect episode. The 28-day leaving scene is not built.
/// </summary>
[Collection("SharedGameSingletons")]
public class NeglectLetter124Tests : IDisposable
{
    private readonly Player _player;
    private readonly Character _spouse;

    public NeglectLetter124Tests()
    {
        RelationshipSystem.Instance.Reset();
        MailSystem.ClearAllMail();
        RomanceTracker.Instance.Spouses.Clear();
        _player = new Player { Name2 = "Hero", ID = "player-hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human };
        _spouse = new Character { Name2 = "Mira", ID = "npc-mira", Class = CharacterClass.Cleric, Race = CharacterRace.Elf };
    }

    public void Dispose()
    {
        RelationshipSystem.Instance.Reset();
        MailSystem.ClearAllMail();
        RomanceTracker.Instance.Spouses.Clear();
    }

    private void Marry(Character npc)
    {
        var r = RelationshipSystem.GetOrCreateRelationship(_player, npc);
        r.Relation1 = GameConfig.RelationMarried; r.Relation2 = GameConfig.RelationMarried;
        r.MarriedDays = 1;
        RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = npc.ID!, NPCName = npc.Name, LoveLevel = 20 });
    }

    private void Day(int presentDay)
    {
        _player.PresentDays = presentDay;
        RelationshipSystem.ProcessNeglect(_player);
    }

    private List<MailRecord> Letters(string from) =>
        MailSystem.MailFor(_player.Name2).Where(m => m.Sender == from).ToList();

    [Fact]
    public void NoLetterAt20_OneAt21_NoneAgainThrough30()
    {
        Marry(_spouse);
        for (int d = 1; d <= 20; d++) Day(d);
        Letters(_spouse.Name).Should().BeEmpty("twenty days is short of the letter");
        Day(21);
        Letters(_spouse.Name).Should().HaveCount(1, "the spouse writes at 21 neglect days");
        for (int d = 22; d <= 30; d++) Day(d);
        Letters(_spouse.Name).Should().HaveCount(1, "one letter per neglect episode");
    }

    [Fact]
    public void ContactStartsANewEpisode_And21MoreDaysSendASecondLetter()
    {
        Marry(_spouse);
        for (int d = 1; d <= 25; d++) Day(d);
        Letters(_spouse.Name).Should().HaveCount(1);
        RelationshipSystem.UpdateRelationship(_player, _spouse, +1); // contact on day 25
        for (int d = 26; d <= 45; d++) Day(d);
        Letters(_spouse.Name).Should().HaveCount(1, "20 days after contact is not yet a new letter");
        Day(46);
        Letters(_spouse.Name).Should().HaveCount(2, "a new episode can send a new letter");
    }

    [Fact]
    public void TwoSpouses_EachSendTheirOwn()
    {
        var second = new Character { Name2 = "Talen", ID = "npc-talen", Class = CharacterClass.Ranger, Race = CharacterRace.Human };
        Marry(_spouse);
        Marry(second);
        for (int d = 1; d <= 21; d++) Day(d);
        Letters(_spouse.Name).Should().HaveCount(1);
        Letters(second.Name).Should().HaveCount(1);
    }

    [Fact]
    public void LoverWhoIsNotASpouse_GetsNoLetter()
    {
        var r = RelationshipSystem.GetOrCreateRelationship(_player, _spouse);
        r.Relation1 = GameConfig.RelationLove; r.Relation2 = GameConfig.RelationLove;
        for (int d = 1; d <= 21; d++) Day(d);
        MailSystem.MailFor(_player.Name2).Should().BeEmpty("only spouses write the neglect letter");
    }

    [Fact]
    public void SaveAndLoadBetween21And22_DoesNotResend()
    {
        Marry(_spouse);
        for (int d = 1; d <= 21; d++) Day(d);
        Letters(_spouse.Name).Should().HaveCount(1);

        var json = JsonSerializer.Serialize(RelationshipSystem.ExportAllRelationships());
        RelationshipSystem.Instance.Reset();
        RelationshipSystem.ImportAllRelationships(JsonSerializer.Deserialize<List<RelationshipSaveData>>(json));
        RelationshipSystem.GetOrCreateRelationship(_player, _spouse).NeglectLetterSentDay.Should().Be(21);

        for (int d = 22; d <= 30; d++) Day(d);
        Letters(_spouse.Name).Should().HaveCount(1, "the sent stamp survives save and load");
    }

    [Fact]
    public void OldSaveWithoutTheField_LoadsWithNoneSent()
    {
        Marry(_spouse);
        var exported = RelationshipSystem.ExportAllRelationships();
        var json = JsonSerializer.Serialize(exported);
        json.Should().Contain("\"NeglectLetterSentDay\":0");
        string legacy = json.Replace(",\"NeglectLetterSentDay\":0", "").Replace("\"NeglectLetterSentDay\":0,", "");
        legacy.Should().NotContain("NeglectLetterSentDay");
        RelationshipSystem.Instance.Reset();
        RelationshipSystem.ImportAllRelationships(JsonSerializer.Deserialize<List<RelationshipSaveData>>(legacy));

        var r = RelationshipSystem.GetOrCreateRelationship(_player, _spouse);
        r.NeglectLetterSentDay.Should().Be(0);
        r.LastPlayerContactDay.Should().Be(0, "a marriage never stamped by contact still gets its letter");
        for (int d = 1; d <= 21; d++) Day(d);
        Letters(_spouse.Name).Should().HaveCount(1);
    }

    [Fact]
    public void CatchUpResets_DoNotAdvanceTheClock_OrSendALetter()
    {
        Marry(_spouse);
        for (int d = 1; d <= 20; d++) Day(d);
        var engine = GameEngine.Instance;
        var old = engine.CurrentPlayer;
        engine.CurrentPlayer = _player;
        try
        {
            for (int i = 0; i < 30; i++) DailySystemManager.Instance.RunCatchUpDailyReset();
        }
        finally { engine.CurrentPlayer = old; }
        _player.PresentDays.Should().Be(20, "absence adds no present days");
        MailSystem.MailFor(_player.Name2).Should().BeEmpty("catch-up days are not neglect days");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void LetterFitsTheMailScreen_With30CharacterName(string lang)
    {
        const int nameLength = 30;
        nameLength.Should().Be(GameConfig.MaxNameLength);
        var longSpouse = new Character { Name2 = new string('W', nameLength), ID = "npc-long", Class = CharacterClass.Cleric, Race = CharacterRace.Human };
        Marry(longSpouse);
        using (Loc.RenderLanguage(lang))
        {
            for (int d = 1; d <= 21; d++) Day(d);
            var mail = Letters(longSpouse.Name).Should().ContainSingle().Subject;
            mail.Lines.Should().HaveCount(3).And.OnlyContain(l => !string.IsNullOrWhiteSpace(l));
            mail.Lines[2].Should().Contain(longSpouse.Name, "the letter is signed by the spouse");
            mail.Subject.Should().Be(Loc.Get("mail.neglect_letter_subject"));
            var rows = new List<string>
            {
                // the inbox row and the header rows ReadPlayerMail and DisplayMailMessage print
                $"[1] {mail.Subject} - {mail.Sender} ({Loc.Get("mail.status_new")})",
                Loc.Get("mail.from", mail.Sender),
                Loc.Get("mail.to", mail.Receiver),
                Loc.Get("mail.subject", mail.Subject),
            };
            rows.AddRange(mail.Lines);
            foreach (var row in rows)
                row.Length.Should().BeLessThanOrEqualTo(79, $"[{lang}] {row}");
            if (lang == "hu") mail.Lines[0].Should().Be(Loc.GetIn("hu", "mail.neglect_letter_line1"));
        }
    }

    [Fact]
    public void NoDivorceAt28OrLater()
    {
        Marry(_spouse);
        for (int d = 1; d <= 60; d++) Day(d);
        RelationshipSystem.AreMarried(_player, _spouse).Should().BeTrue("the leaving scene is held");
        RomanceTracker.Instance.Spouses.Should().ContainSingle(s => s.NPCId == _spouse.ID);
        RelationshipSystem.GetOrCreateRelationship(_player, _spouse).MarriedDays.Should().Be(1);
    }
}
