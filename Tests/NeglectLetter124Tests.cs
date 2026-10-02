using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.Utils;
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

    // ---- delivery: the online store, the on-screen showing, and the pending flag ----

    private static (TerminalEmulator term, MemoryStream output) Terminal(string input = "")
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void Online(SqlSaveBackend db, Action body)
    {
        var online = typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;
        var inst = typeof(SaveSystem).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool wasOnline = (bool)online.GetValue(null)!;
        var before = inst.GetValue(null);
        online.SetValue(null, true);
        SaveSystem.InitializeWithBackend(db);
        try { body(); }
        finally { online.SetValue(null, wasOnline); inst.SetValue(null, before); }
    }

    [Fact]
    public async Task Online_TheLetterLandsInThePersistentStore_UnreadAndInTheInbox_AndShowsOnScreenOnce()
    {
        string path = Path.Combine(Path.GetTempPath(), $"usurper-nl-{Guid.NewGuid():N}.db");
        try
        {
            var db = new SqlSaveBackend(path);
            Marry(_spouse);
            Online(db, () => { for (int d = 1; d <= 30; d++) Day(d); });

            db.GetUnreadMailCount(_player.DisplayName).Should().Be(1, "one letter, counted unread");
            var inbox = await db.GetMailInbox(_player.DisplayName);
            var letter = inbox.Should().ContainSingle().Subject;
            letter.FromPlayer.Should().Be(_spouse.Name);
            letter.MessageType.Should().Be("mail", "the type player mail uses");
            letter.Message.Should().Contain(Loc.Get("mail.neglect_letter_line1")).And.Contain(Loc.Get("mail.neglect_letter_line3", _spouse.Name));
            MailSystem.MailFor(_player.Name2).Should().BeEmpty("online the letter goes to the saved store, not the in-process mailbox");

            var (term, output) = Terminal("\n\n");
            (await MailSystem.ShowPendingSpouseLetters(term, _player)).Should().BeTrue();
            string shown = Shown(term, output);
            shown.Should().Contain(Loc.Get("mail.neglect_letter_arrives", _spouse.Name)).And.Contain(Loc.Get("mail.neglect_letter_line2"));
            var (term2, _) = Terminal("\n\n");
            (await MailSystem.ShowPendingSpouseLetters(term2, _player)).Should().BeFalse("the on-screen letter shows once");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task SinglePlayer_ShowsTheLetterOnScreen_Once()
    {
        Marry(_spouse);
        for (int d = 1; d <= 21; d++) Day(d);
        Letters(_spouse.Name).Should().HaveCount(1);
        var (term, output) = Terminal("\n\n");
        (await MailSystem.ShowPendingSpouseLetters(term, _player)).Should().BeTrue();
        string shown = Shown(term, output);
        shown.Should().Contain(Loc.Get("mail.neglect_letter_arrives", _spouse.Name));
        foreach (var line in MailSystem.SpouseNeglectLetterLines(_spouse.Name)) shown.Should().Contain(line);
        for (int d = 22; d <= 30; d++) Day(d);
        var (term2, _) = Terminal("\n\n");
        (await MailSystem.ShowPendingSpouseLetters(term2, _player)).Should().BeFalse("the same letter is not shown again");
    }

    [Fact]
    public async Task AQuitBeforeTheRedraw_ShowsTheLetterAtTheNextLogin()
    {
        Marry(_spouse);
        for (int d = 1; d <= 21; d++) Day(d);
        _player.PendingSpouseLetters.Should().ContainSingle().Which.Should().Be(_spouse.Name);

        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { _player })!;
        var json = JsonSerializer.Serialize(data);
        var restoreM = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Character restored;
        try { restored = (Character)restoreM.Invoke(GameEngine.Instance, new object[] { JsonSerializer.Deserialize<PlayerData>(json)! })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }

        restored.PendingSpouseLetters.Should().ContainSingle().Which.Should().Be(_spouse.Name, "the pending letter is saved");
        var (term, output) = Terminal("\n\n");
        (await MailSystem.ShowPendingSpouseLetters(term, restored)).Should().BeTrue();
        Shown(term, output).Should().Contain(Loc.Get("mail.neglect_letter_arrives", _spouse.Name));

        JsonSerializer.Deserialize<PlayerData>("{}")!.PendingSpouseLetters.Should().BeEmpty("an old save has none waiting");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void OnScreenAndOnlineRows_Fit79_With30CharacterName(string lang)
    {
        string name = new string('W', GameConfig.MaxNameLength);
        using (Loc.RenderLanguage(lang))
        {
            var rows = new List<string> { $"  {Loc.Get("mail.neglect_letter_arrives", name)}" };
            rows.AddRange(MailSystem.SpouseNeglectLetterLines(name).Select(l => $"  {l}"));
            rows.Add(BaseLocation.MailboxRow("*", 10, name, "2026-10-02", new string('x', 32) + "..."));
            rows.Add(Loc.Get("base.mail_message_from_box", name.ToUpper()));
            foreach (var row in rows)
                row.Length.Should().BeLessThanOrEqualTo(79, $"[{lang}] {row}");
        }
    }

    [Fact]
    public void TheLocationLoop_ShowsPendingLetters_BeforeTheCommandRuns()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        string loop = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Locations", "BaseLocation.cs"));
        int loopStart = loop.IndexOf("while (!exitLocation && currentPlayer.IsAlive)");
        int show = loop.IndexOf("await MailSystem.ShowPendingSpouseLetters(terminal, currentPlayer);");
        int process = loop.IndexOf("exitLocation = await ProcessChoice(choice);");
        loopStart.Should().BeGreaterThan(0);
        show.Should().BeGreaterThan(loopStart).And.BeLessThan(process);
    }
}
