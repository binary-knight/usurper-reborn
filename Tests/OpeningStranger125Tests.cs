using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the Mysterious Stranger's opening scene (mysterious_stranger_intro) is reached from a real location
/// entry (BaseLocation.LocationLoop), once per character, for a character of level 3 or more who has had no
/// encounter with Noctura's disguised stranger and does not know her truth. The seen flag is set when the scene
/// ends; its rewards carry once flags, so a run cut off and played again never pays twice. The first entry after
/// a login (a new Character object) never shows it. DialogueSystem and OpeningSequenceSystem are per session.
/// </summary>
[Collection("SharedGameSingletons")]
public class OpeningStranger125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Tree = "mysterious_stranger_intro";
    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string Name30 = "Wilhelmina Thornbury Ashcombe" + new string('x', GameConfig.MaxNameLength - 29);

    private sealed class NoEncounterRandom : Random
    {
        public override double NextDouble() => 0.999;
        public override int Next(int maxValue) => Math.Max(0, maxValue - 1);
        public override int Next(int min, int max) => Math.Max(min, max - 1);
    }

    private static Character Hero(string name = "Opener", int level = 4) => new()
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Level = level, Mental = 50, HP = 100, MaxHP = 100, MentalHintShown = true, Gold = 100,
    };

    private static string Plain(MemoryStream output) =>
        Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");

    /// <summary>A fresh world outside any session, the scene's roll always under the chance, no street encounters.</summary>
    private static void FreshWorld(double roll = 0.0)
    {
        StoryProgressionSystem.Instance.FullReset();
        StrangerEncounterSystem.Instance.Reset();
        OceanPhilosophySystem.Instance.Reset();
        OpeningSequenceSystem.Instance.Roll = () => roll;
        typeof(OpeningSequenceSystem).GetField("_enteredAs", F)!.SetValue(OpeningSequenceSystem.Instance, null);
        typeof(StreetEncounterSystem).GetField("_random", F)!.SetValue(StreetEncounterSystem.Instance, new NoEncounterRandom());
    }

    private static void RestoreWorld()
    {
        OpeningSequenceSystem.Instance.Roll = () => Random.Shared.NextDouble();
        typeof(OpeningSequenceSystem).GetField("_enteredAs", F)!.SetValue(OpeningSequenceSystem.Instance, null);
        typeof(StreetEncounterSystem).GetField("_random", F)!.SetValue(StreetEncounterSystem.Instance, Random.Shared);
        StoryProgressionSystem.Instance.FullReset();
        StrangerEncounterSystem.Instance.Reset();
    }

    /// <summary>One real entry into a location: BaseLocation.LocationLoop with the given answers. When the answers
    /// run out the stream closes, as a dropped connection does, which ends the entry.</summary>
    private static async Task<(string Text, Exception? End)> Enter(BaseLocation location, Character hero, params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        var loop = (Task)typeof(BaseLocation).GetMethod("LocationLoop", F)!.Invoke(location, null)!;
        (await Task.WhenAny(loop, Task.Delay(30000))).Should().BeSameAs(loop, "the entry ends when the answers run out");
        Exception? end = null;
        try { await loop; } catch (Exception e) { end = e; }
        term.StreamWriterInternal?.Flush();
        return (Plain(output), end);
    }

    private static string SceneMark => Loc.Get("opening.not_alone");
    private static StoryProgressionSystem Story => StoryProgressionSystem.Instance;

    // ---------- reachability and once only ----------

    [Fact]
    public async Task TheScene_IsReachedFromARealLocationEntry_AndShownOnce()
    {
        FreshWorld();
        try
        {
            var street = new MainStreetLocation();
            var hero = Hero();
            long dark = hero.Darkness, xp = hero.Experience;

            var first = await Enter(street, hero);
            first.Text.Should().NotContain(SceneMark, "the first entry of a session never shows the scene");
            Story.HasStoryFlag("met_mysterious_stranger").Should().BeFalse();

            // defiant answer, then the first prophecy answer, then the key press after the scene
            var second = await Enter(street, hero, "3", "1", "");
            second.Text.Should().Contain(SceneMark);
            second.Text.Should().Contain(Loc.Get("dialogue.effect_story_key", Loc.Get("dialogue.reward.ancient_iron_key")));
            second.End.Should().BeOfType<ConnectionClosedException>("the entry went on to the location menu");
            Story.HasStoryFlag("met_mysterious_stranger").Should().BeTrue();
            Story.HasStoryFlag("defiant_to_stranger").Should().BeTrue();
            Story.HasStoryFlag("willing_hero").Should().BeFalse();
            Story.HasStoryFlag("has_ancient_key").Should().BeTrue();
            Story.CurrentChapter.Should().Be(StoryChapter.TheFirstSeal);
            hero.Darkness.Should().Be(dark + 5);
            hero.Experience.Should().Be(xp + 100);
            hero.Inventory.Should().BeEmpty("the key is a story key, never an inventory item");

            var third = await Enter(street, hero, "3", "1", "");
            third.Text.Should().NotContain(SceneMark, "the scene is shown once per character");
            hero.Experience.Should().Be(xp + 100);
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public async Task AFreshLogin_NeverShowsTheSceneOnItsFirstEntry()
    {
        FreshWorld();
        try
        {
            var street = new MainStreetLocation();
            await Enter(street, Hero());
            // the same character logs in again (a door re-entry): a new Character object
            var again = Hero();
            (await Enter(street, again, "3", "1", "")).Text.Should().NotContain(SceneMark, "a login's first entry never shows it");
            Story.HasStoryFlag("met_mysterious_stranger").Should().BeFalse();
            (await Enter(street, again, "3", "1", "")).Text.Should().Contain(SceneMark, "the next entry may");
        }
        finally { RestoreWorld(); }
    }

    // ---------- who may see it (user decision b) ----------

    [Fact]
    public async Task OnlyACharacterWhoNeverMetNocturasStranger_AndIsLevelThree_SeesIt()
    {
        FreshWorld();
        try
        {
            var street = new MainStreetLocation();

            var low = Hero("Low", 2);
            await Enter(street, low);
            (await Enter(street, low, "3", "1", "")).Text.Should().NotContain(SceneMark, "level 2 is too early");

            FreshWorld();
            var met = Hero("Met");
            typeof(StrangerEncounterSystem).GetProperty("EncountersHad")!.SetValue(StrangerEncounterSystem.Instance, 1);
            await Enter(street, met);
            (await Enter(street, met, "3", "1", "")).Text.Should().NotContain(SceneMark, "a character who met Noctura's stranger");
            Story.HasStoryFlag("met_mysterious_stranger").Should().BeFalse();

            FreshWorld();
            var knows = Hero("Knows");
            Story.SetFlag(StoryFlag.KnowsNocturaTruth);
            await Enter(street, knows);
            (await Enter(street, knows, "3", "1", "")).Text.Should().NotContain(SceneMark, "a character who knows Noctura's truth");

            FreshWorld();
            var old = Hero("Old", 80);
            await Enter(street, old);
            (await Enter(street, old, "3", "1", "")).Text.Should().Contain(SceneMark, "a high level character who never met either stranger");
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public void AnOldSave_WithoutTheFlag_LoadsAsNotMet_AndOneWithItAsMet()
    {
        FreshWorld();
        try
        {
            var data = SaveSystem.Instance.SerializeStorySystemsPublic();
            data.StoryFlags.Should().NotContainKey("met_mysterious_stranger");
            Story.FullReset();
            SaveSystem.Instance.RestoreStorySystems(data);
            OpeningSequenceSystem.IsEligible(Hero()).Should().BeTrue("a save from before 1.2.5 has no flag and loads as not met");

            Story.SetStoryFlag("met_mysterious_stranger", true);
            data = SaveSystem.Instance.SerializeStorySystemsPublic();
            Story.FullReset();
            SaveSystem.Instance.RestoreStorySystems(data);
            Story.HasStoryFlag("met_mysterious_stranger").Should().BeTrue();
            OpeningSequenceSystem.IsEligible(Hero()).Should().BeFalse();
        }
        finally { RestoreWorld(); }
    }

    // ---------- where ----------

    [Fact]
    public void TheScene_HappensOnlyOnMainStreet_TheInn_TheDarkAlley_AndTheAuctionHouse()
    {
        var allowed = new[] { GameLocation.MainStreet, GameLocation.TheInn, GameLocation.DarkAlley, GameLocation.AuctionHouse };
        foreach (GameLocation loc in Enum.GetValues(typeof(GameLocation)))
            OpeningSequenceSystem.CanTriggerHere(loc).Should().Be(allowed.Contains(loc), loc.ToString());
        OpeningSequenceSystem.CanTriggerHere(GameLocation.Dungeons).Should().BeFalse("never in the dungeon");

        var hero = Hero(level: 3);
        OpeningSequenceSystem.GetTriggerChance(GameLocation.MainStreet, hero).Should().BeApproximately(0.21, 1e-9);
        OpeningSequenceSystem.GetTriggerChance(GameLocation.TheInn, hero).Should().BeApproximately(0.18, 1e-9);
        OpeningSequenceSystem.GetTriggerChance(GameLocation.DarkAlley, hero).Should().BeApproximately(0.31, 1e-9);
        OpeningSequenceSystem.GetTriggerChance(GameLocation.AuctionHouse, hero).Should().BeApproximately(0.11, 1e-9);
        OpeningSequenceSystem.GetTriggerChance(GameLocation.MainStreet, Hero(level: 40)).Should().Be(0.5);
    }

    [Fact]
    public async Task AnEntryWithAnotherSceneFirst_DoesNotShowIt()
    {
        FreshWorld();
        try
        {
            var hero = Hero();
            var term = new TerminalEmulator(new LineStream(new[] { "3", "1", "" }), new MemoryStream());
            var sys = OpeningSequenceSystem.Instance;
            (await sys.CheckOpeningSequenceTriggers(hero, GameLocation.MainStreet, term)).Should().BeFalse("first entry");
            (await sys.CheckOpeningSequenceTriggers(hero, GameLocation.MainStreet, term, otherSceneShown: true))
                .Should().BeFalse("a street encounter came first on this entry");
            Story.HasStoryFlag("met_mysterious_stranger").Should().BeFalse();
            (await sys.CheckOpeningSequenceTriggers(hero, GameLocation.MainStreet, term)).Should().BeTrue();
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public void TheLocationLoop_PassesTheEntryEncounter_AndSkipsTheOtherScenesWhenItShows()
    {
        var src = File.ReadAllText(Path.Combine(UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "BaseLocation.cs"));
        var loop = MentalBands1115Tests.Method(src, "LocationLoop");
        int call = loop.IndexOf("OpeningSequenceSystem.Instance.CheckOpeningSequenceTriggers(", StringComparison.Ordinal);
        call.Should().BeGreaterThan(loop.IndexOf(".CheckForEncounter(", StringComparison.Ordinal));
        call.Should().BeLessThan(loop.IndexOf("await CheckNarrativeEncounters();", StringComparison.Ordinal));
        call.Should().BeLessThan(loop.IndexOf("while (!exitLocation", StringComparison.Ordinal), "before the location menu");
        loop.Should().Contain("otherSceneShown: entryEncounterShown");
        loop.Should().Contain("if (!openingShown)");
    }

    // ---------- seen at the end, rewards once ----------

    [Fact]
    public async Task ARunCutOff_AndPlayedAgain_PaysEachRewardOnce_AndKeepsTheFirstBranch()
    {
        FreshWorld();
        try
        {
            var street = new MainStreetLocation();
            var hero = Hero();
            long dark = hero.Darkness, chiv = hero.Chivalry, xp = hero.Experience;
            await Enter(street, hero);

            // the defiant answer, then the connection drops at the prophecy menu
            var cut = await Enter(street, hero, "3");
            cut.End.Should().BeOfType<ConnectionClosedException>();
            Story.HasStoryFlag("met_mysterious_stranger").Should().BeFalse("the scene did not end");
            hero.Darkness.Should().Be(dark + 5);

            // played again: the willing answer is no longer on offer; the third answer is the defiant one again
            var willing = Loc.Get(DialogueSystem.ChoiceKey(Tree, "stranger_approach", "choice_4"));
            var replay = await Enter(street, hero, "3", "1", "");
            replay.Text.Should().Contain(SceneMark);
            replay.Text.Should().NotContain(willing, "a replay offers only the branch first taken");
            Story.HasStoryFlag("met_mysterious_stranger").Should().BeTrue();
            Story.HasStoryFlag("defiant_to_stranger").Should().BeTrue();
            Story.HasStoryFlag("willing_hero").Should().BeFalse();
            hero.Darkness.Should().Be(dark + 5, "the darkness is paid once");
            hero.Chivalry.Should().Be(chiv);
            hero.Experience.Should().Be(xp + 100);
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public async Task ARunCutOffAfterTheGift_IsSeen_AndTheExperienceIsNotPaidAgain()
    {
        FreshWorld();
        try
        {
            var street = new MainStreetLocation();
            var hero = Hero();
            long xp = hero.Experience;
            await Enter(street, hero);
            // the willing answer and a prophecy answer; the connection drops at the closing key press
            var cut = await Enter(street, hero, "4", "1");
            cut.End.Should().BeOfType<ConnectionClosedException>();
            Story.HasStoryFlag("met_mysterious_stranger").Should().BeTrue("the scene had ended");
            Story.HasStoryFlag("willing_hero").Should().BeTrue();
            hero.Experience.Should().Be(xp + 100);

            // a direct replay of the dialogue (the worst case) still pays nothing twice; the willing answer is now third
            var term = new TerminalEmulator(new LineStream(new[] { "3", "1" }), new MemoryStream());
            long chiv = hero.Chivalry;
            await DialogueSystem.Instance.StartDialogue(hero, Tree, term);
            hero.Experience.Should().Be(xp + 100);
            hero.Chivalry.Should().Be(chiv);
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public async Task SayingNothing_EndsTheScene_MarksItSeen_AndPaysNothing()
    {
        FreshWorld();
        try
        {
            var street = new MainStreetLocation();
            var hero = Hero();
            long xp = hero.Experience;
            await Enter(street, hero);
            (await Enter(street, hero, "0", "")).Text.Should().Contain(SceneMark);
            Story.HasStoryFlag("met_mysterious_stranger").Should().BeTrue();
            Story.HasStoryFlag("has_ancient_key").Should().BeFalse();
            hero.Experience.Should().Be(xp);
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public async Task TheScene_NeverMovesTheChapterBack()
    {
        FreshWorld();
        try
        {
            Story.AdvanceChapter(StoryChapter.TheWhispers);
            var street = new MainStreetLocation();
            var hero = Hero(level: 30);
            await Enter(street, hero);
            (await Enter(street, hero, "1", "1", "")).Text.Should().Contain(SceneMark);
            Story.CurrentChapter.Should().Be(StoryChapter.TheWhispers);

            Story.AdvanceChapter(StoryChapter.FirstBlood);
            Story.CurrentChapter.Should().Be(StoryChapter.TheWhispers, "AdvanceChapter only advances");
            Story.AdvanceChapter(StoryChapter.GodWar);
            Story.CurrentChapter.Should().Be(StoryChapter.GodWar);
        }
        finally { RestoreWorld(); }
    }

    // ---------- per session ----------

    /// <summary>Answers lines in order; before the line at <paramref name="holdAt"/> it waits for the gate.</summary>
    private sealed class GatedStream : Stream
    {
        private readonly string[] _lines; private readonly int _holdAt; private readonly ManualResetEventSlim _gate;
        public readonly ManualResetEventSlim Waiting = new(false);
        private byte[] _pending = Array.Empty<byte>(); private int _pos, _next;
        public GatedStream(string[] lines, int holdAt, ManualResetEventSlim gate) { _lines = lines; _holdAt = holdAt; _gate = gate; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _pending.Length)
            {
                if (_next >= _lines.Length) return 0;
                if (_next == _holdAt) { Waiting.Set(); _gate.Wait(TimeSpan.FromSeconds(30)); }
                _pending = Encoding.UTF8.GetBytes(_lines[_next++] + "\n");
                _pos = 0;
            }
            int n = Math.Min(count, _pending.Length - _pos);
            Array.Copy(_pending, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var tmp = new byte[buffer.Length];
            int n = Read(tmp, 0, tmp.Length);
            tmp.AsMemory(0, n).CopyTo(buffer);
            return ValueTask.FromResult(n);
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static SessionContext Session(string user)
    {
        var ctx = new SessionContext { Username = user, CharacterKey = user, Language = "en" };
        ctx.InitializeSystems();
        return ctx;
    }

    [Fact]
    public async Task TwoPlayersInTheSceneAtOnce_KeepTheirOwnRuns()
    {
        var ctxA = Session("openera");
        var ctxB = Session("openerb");
        var heroA = Hero("Alderan");
        var heroB = Hero("Brisane");
        var gate = new ManualResetEventSlim(false);
        var streamA = new GatedStream(new[] { "3", "1" }, 0, gate);
        var outA = new MemoryStream();
        var outB = new MemoryStream();

        var runA = Task.Run(async () =>
        {
            SessionContext.Current = ctxA;
            var term = new TerminalEmulator(streamA, outA);
            await DialogueSystem.Instance.StartDialogue(heroA, Tree, term);
            term.StreamWriterInternal?.Flush();
        });
        streamA.Waiting.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("A waits at its first menu");

        await Task.Run(async () =>
        {
            SessionContext.Current = ctxB;
            var term = new TerminalEmulator(new LineStream(new[] { "4", "1" }), outB);
            await DialogueSystem.Instance.StartDialogue(heroB, Tree, term);
            term.StreamWriterInternal?.Flush();
        });
        gate.Set();
        (await Task.WhenAny(runA, Task.Delay(30000))).Should().BeSameAs(runA);
        await runA;

        ctxA.Dialogue.Should().NotBeSameAs(ctxB.Dialogue);
        ctxA.OpeningSequence.Should().NotBeSameAs(ctxB.OpeningSequence);
        heroA.Darkness.Should().Be(5, "A answered defiantly");
        heroA.Chivalry.Should().Be(0);
        heroB.Chivalry.Should().Be(5, "B answered willingly");
        heroB.Darkness.Should().Be(0);
        heroA.Experience.Should().Be(100);
        heroB.Experience.Should().Be(100);
        ctxA.Story.HasStoryFlag("defiant_to_stranger").Should().BeTrue();
        ctxA.Story.HasStoryFlag("willing_hero").Should().BeFalse();
        ctxA.Story.HasStoryFlag("met_mysterious_stranger").Should().BeTrue();
        ctxB.Story.HasStoryFlag("willing_hero").Should().BeTrue();
        ctxB.Story.HasStoryFlag("defiant_to_stranger").Should().BeFalse();
        var textA = Plain(outA);
        var textB = Plain(outB);
        textA.Should().Contain("Alderan").And.NotContain("Brisane");
        textB.Should().Contain("Brisane").And.NotContain("Alderan");
        textA.Should().Contain(Loc.GetIn("en", DialogueSystem.NodeTextKey(Tree, "stranger_gift")).Split('\n')[0]);
    }

    [Fact]
    public void TheTrees_AreBuiltOnce_AndShared()
    {
        var a = new DialogueSystem();
        var b = new DialogueSystem();
        var trees = typeof(DialogueSystem).GetField("dialogueTrees", F)!;
        trees.GetValue(a).Should().BeSameAs(trees.GetValue(b));
        ((System.Collections.IDictionary)trees.GetValue(a)!).Contains(Tree).Should().BeTrue();
    }

    // ---------- the follow-up scenes: the Temple priest and the Maelketh warning ----------

    private static string PriestMark => Loc.Get("opening.priest_approaches");
    private static string WarningMark => Loc.Get("opening.veteran_god_awakens", "Maelketh");

    /// <summary>One real entry into the Temple (TempleLocation.EnterLocation, its own loop).</summary>
    private static async Task<(string Text, Exception? End)> EnterTemple(Character hero, params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        var temple = new TempleLocation();
        var entry = temple.EnterLocation(hero, term);
        (await Task.WhenAny(entry, Task.Delay(30000))).Should().BeSameAs(entry, "the entry ends when the answers run out");
        Exception? end = null;
        try { await entry; } catch (Exception e) { end = e; }
        term.StreamWriterInternal?.Flush();
        return (Plain(output), end);
    }

    private static void MetTheStranger() => Story.SetStoryFlag("met_mysterious_stranger", true);

    [Fact]
    public async Task ThePriest_IsReachedFromARealTempleEntry_Once_AndMarkedAfterTheKeyPress()
    {
        FreshWorld();
        try
        {
            MetTheStranger();
            var hero = Hero(level: 10);
            long xp = hero.Experience, gold = hero.Gold;
            (await EnterTemple(hero)).Text.Should().NotContain(PriestMark, "the first entry of a session never shows it");

            var cut = await EnterTemple(hero);
            cut.Text.Should().Contain(PriestMark);
            cut.End.Should().BeOfType<ConnectionClosedException>("the connection dropped at the closing key press");
            Story.HasStoryFlag("first_seal_hint").Should().BeFalse("it is marked only after the key press");

            var seen = await EnterTemple(hero, "");
            seen.Text.Should().Contain(PriestMark, "a scene cut off is shown again");
            seen.Text.Should().Contain(Loc.Get("opening.priest_first_seal"));
            Story.HasStoryFlag("first_seal_hint").Should().BeTrue();

            (await EnterTemple(hero, "")).Text.Should().NotContain(PriestMark, "once only");
            hero.Experience.Should().Be(xp, "the priest gives nothing");
            hero.Gold.Should().Be(gold);
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public async Task ThePriest_NeedsTheStranger_Level10_AndTheTempleSealUnfound()
    {
        FreshWorld();
        try
        {
            var hero = Hero(level: 10);
            await EnterTemple(hero);
            (await EnterTemple(hero, "")).Text.Should().NotContain(PriestMark, "never met the Stranger");

            MetTheStranger();
            var low = Hero("Low", 9);
            await EnterTemple(low);
            (await EnterTemple(low, "")).Text.Should().NotContain(PriestMark, "level 9");

            await EnterTemple(hero);   // the hero's own first entry after the other character's
            Story.CollectedSeals.Add(SealType.Creation);
            (await EnterTemple(hero, "")).Text.Should().NotContain(PriestMark, "the Seal he speaks of is already found");
            Story.CollectedSeals.Remove(SealType.Creation);
            (await EnterTemple(hero, "")).Text.Should().Contain(PriestMark);

            OpeningSequenceSystem.PriestEligible(hero, GameLocation.MainStreet).Should().BeFalse("only in the Temple");
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public void TheTempleCall_ComesOncePerEntry_BeforeTheMenuLoop()
    {
        var src = File.ReadAllText(Path.Combine(UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "TempleLocation.cs"));
        Regex.Matches(src, Regex.Escape("OpeningSequenceSystem.Instance.CheckOpeningSequenceTriggers(")).Count.Should().Be(1);
        var body = MentalBands1115Tests.Method(src, "ProcessLocation");
        int call = body.IndexOf("OpeningSequenceSystem.Instance.CheckOpeningSequenceTriggers(player, GameLocation.Temple, terminal);", StringComparison.Ordinal);
        call.Should().BeGreaterThan(body.IndexOf("await DisplayWelcomeMessage();", StringComparison.Ordinal));
        call.Should().BeLessThan(body.IndexOf("while (!exitLocation)", StringComparison.Ordinal), "never inside the menu loop");
    }

    [Fact]
    public async Task TheMaelkethWarning_IsReachedFromARealInnEntry_Once_AndMarkedAfterTheKeyPress()
    {
        FreshWorld();
        try
        {
            MetTheStranger();
            var inn = new InnLocation();
            var hero = Hero(level: 25);
            long xp = hero.Experience;
            (await Enter(inn, hero)).Text.Should().NotContain(WarningMark, "first entry");

            var cut = await Enter(inn, hero);
            cut.Text.Should().Contain(WarningMark);
            Story.HasStoryFlag("maelketh_stirring_warning").Should().BeFalse("it is marked only after the key press");

            (await Enter(inn, hero, "")).Text.Should().Contain(WarningMark, "a scene cut off is shown again");
            Story.HasStoryFlag("maelketh_stirring_warning").Should().BeTrue();
            (await Enter(inn, hero, "")).Text.Should().NotContain(WarningMark, "once only");
            hero.Experience.Should().Be(xp, "the warning gives nothing");
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public async Task TheMaelkethWarning_NeedsTheStranger_Level25_AndMaelkethUnmet()
    {
        FreshWorld();
        try
        {
            var inn = new InnLocation();
            var hero = Hero(level: 25);
            await Enter(inn, hero);
            (await Enter(inn, hero, "")).Text.Should().NotContain(WarningMark, "never met the Stranger");

            MetTheStranger();
            var low = Hero("Low", 24);
            await Enter(inn, low);
            (await Enter(inn, low, "")).Text.Should().NotContain(WarningMark, "level 24");

            foreach (var done in new[] { GodStatus.Defeated, GodStatus.Saved, GodStatus.Allied, GodStatus.Consumed, GodStatus.Awakened })
            {
                Story.OldGodStates[OldGodType.Maelketh].Status = done;
                (await Enter(inn, hero, "")).Text.Should().NotContain(WarningMark, $"Maelketh is {done}");
            }
            Story.OldGodStates[OldGodType.Maelketh].Status = GodStatus.Corrupted;   // as a character starts
            (await Enter(inn, hero, "")).Text.Should().Contain(WarningMark);

            OpeningSequenceSystem.MaelkethWarningEligible(Hero(level: 30), GameLocation.MainStreet).Should().BeFalse("only in the Inn");
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public void AnOldSave_HasNeitherFollowUpFlag_AndKeepsThemOnceSet()
    {
        FreshWorld();
        try
        {
            MetTheStranger();
            var data = SaveSystem.Instance.SerializeStorySystemsPublic();
            Story.FullReset();
            SaveSystem.Instance.RestoreStorySystems(data);
            OpeningSequenceSystem.PriestEligible(Hero(level: 10), GameLocation.Temple).Should().BeTrue();
            OpeningSequenceSystem.MaelkethWarningEligible(Hero(level: 25), GameLocation.TheInn).Should().BeTrue();

            Story.SetStoryFlag("first_seal_hint", true);
            Story.SetStoryFlag("maelketh_stirring_warning", true);
            data = SaveSystem.Instance.SerializeStorySystemsPublic();
            Story.FullReset();
            SaveSystem.Instance.RestoreStorySystems(data);
            OpeningSequenceSystem.PriestEligible(Hero(level: 10), GameLocation.Temple).Should().BeFalse();
            OpeningSequenceSystem.MaelkethWarningEligible(Hero(level: 25), GameLocation.TheInn).Should().BeFalse();
        }
        finally { RestoreWorld(); }
    }

    [Fact]
    public async Task TheFollowUps_AreKeptPerSession()
    {
        // A and B take turns entering the Temple. With one shared instance, B's entry would make A's next
        // entry look like a first entry (and B's flags would be A's).
        var ctxA = Session("priesta");
        var ctxB = Session("priestb");
        var heroA = Hero("Alderan", 10);
        var heroB = Hero("Brisane", 10);
        ctxA.Story.SetStoryFlag("met_mysterious_stranger", true);
        ctxB.Story.SetStoryFlag("met_mysterious_stranger", true);

        Task<(string Text, Exception? End)> In(SessionContext ctx, Character hero, params string[] lines) => Task.Run(async () =>
        {
            SessionContext.Current = ctx;
            OpeningSequenceSystem.Instance.Should().BeSameAs(ctx.OpeningSequence);
            return await EnterTemple(hero, lines);
        });

        (await In(ctxA, heroA)).Text.Should().NotContain(PriestMark, "A's first entry");
        (await In(ctxB, heroB)).Text.Should().NotContain(PriestMark, "B's first entry");
        (await In(ctxA, heroA, "")).Text.Should().Contain(PriestMark, "B's entry is not A's");
        ctxA.Story.HasStoryFlag("first_seal_hint").Should().BeTrue();
        ctxB.Story.HasStoryFlag("first_seal_hint").Should().BeFalse("A's flag is not B's");
        (await In(ctxB, heroB, "")).Text.Should().Contain(PriestMark);
        ctxB.Story.HasStoryFlag("first_seal_hint").Should().BeTrue();
    }

    private static string FollowUpIn(string lang, bool priest)
    {
        var prevLang = GameConfig.Language;
        FreshWorld();
        try
        {
            GameConfig.Language = lang;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new LineStream(new[] { "" }), output);
            var name = priest ? "ShowFirstSealHint" : "ShowGodStirringWarning";
            var args = priest ? new object[] { Hero(Name30), term } : new object[] { Hero(Name30), term, "Maelketh" };
            ((Task)typeof(OpeningSequenceSystem).GetMethod(name, F)!.Invoke(OpeningSequenceSystem.Instance, args)!).GetAwaiter().GetResult();
            term.StreamWriterInternal?.Flush();
            return Plain(output);
        }
        finally { GameConfig.Language = prevLang; RestoreWorld(); }
    }

    [Fact]
    public void TheFollowUps_AreInEveryLanguage_AndFitIn79Columns()
    {
        foreach (var lang in AllLanguages)
            foreach (var priest in new[] { true, false })
            {
                var text = FollowUpIn(lang, priest);
                text.Should().Contain(priest ? Loc.GetIn(lang, "opening.priest_first_seal") : Loc.GetIn(lang, "opening.veteran_god_awakens", "Maelketh"));
                foreach (var row in text.Split('\n').Select(r => r.TrimEnd('\r')))
                    row.Length.Should().BeLessThanOrEqualTo(79, $"[{lang}] \"{row}\"");
            }
    }

    [Fact]
    public void ThePriest_PointsToTheTemple_WhereTheFirstSealIs()
    {
        Loc.GetIn("en", "opening.priest_first_seal").Should().Be("\"The first Seal lies here, in this very Temple, among the ancient stones.\"");
        foreach (var lang in AllLanguages)
        {
            Loc.GetIn(lang, "opening.priest_first_seal").Should().NotContain("15", $"{lang}: the first Seal is not on floor 15");
            if (lang != "en")
                Loc.GetIn(lang, "opening.priest_first_seal").Should().NotBe(Loc.GetIn("en", "opening.priest_first_seal"), lang);
        }
        new SevenSealsSystem().GetSeal(SealType.Creation)!.DungeonFloor.Should().Be(0, "the Seal of Creation is found in town, in the Temple");
    }

    // ---------- text ----------

    private static string SceneIn(string lang, bool screenReader, params string[] lines)
    {
        var prevLang = GameConfig.Language;
        bool prevSr = GameConfig.ScreenReaderMode;
        FreshWorld();
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = screenReader;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new LineStream(lines), output);
            var m = typeof(OpeningSequenceSystem).GetMethod("TriggerStrangerEncounter", F)!;
            ((Task)m.Invoke(OpeningSequenceSystem.Instance, new object[] { Hero(Name30), term })!).GetAwaiter().GetResult();
            term.StreamWriterInternal?.Flush();
            return Plain(output);
        }
        finally { GameConfig.Language = prevLang; GameConfig.ScreenReaderMode = prevSr; RestoreWorld(); }
    }

    [Fact]
    public void EveryRowOfTheScene_FitsIn79Columns_InEveryLanguage_WithALongName()
    {
        foreach (var lang in AllLanguages)
            foreach (var path in new[] { new[] { "1", "1", "" }, new[] { "2", "2", "" }, new[] { "3", "3", "" }, new[] { "4", "1", "" } })
            {
                var text = SceneIn(lang, false, path);
                text.Should().Contain(Loc.GetIn(lang, "opening.not_alone"));
                foreach (var row in text.Split('\n').Select(r => r.TrimEnd('\r')))
                    row.Length.Should().BeLessThanOrEqualTo(79, $"[{lang}] {string.Join(",", path)}: \"{row}\"");
            }
    }

    [Fact]
    public void TheStoryKeyLine_IsInEveryLanguage_PromisesNoItem_AndFits()
    {
        foreach (var lang in AllLanguages)
        {
            var text = SceneIn(lang, false, "1", "1", "");
            var line = Loc.GetIn(lang, "dialogue.effect_story_key").Replace("{0}", Loc.GetIn(lang, "dialogue.reward.ancient_iron_key"));
            text.Should().Contain(line, lang);
            line.Length.Should().BeLessThanOrEqualTo(79, lang);
            text.Should().NotContain(Loc.GetIn(lang, "dialogue.effect_item").Replace("{0}", Loc.GetIn(lang, "dialogue.reward.ancient_iron_key")),
                $"{lang}: the old line said the key was received as an item");
            if (lang != "en")
                Loc.GetIn(lang, "dialogue.effect_story_key").Should().NotBe(Loc.GetIn("en", "dialogue.effect_story_key"), lang);
        }
        Loc.GetIn("en", "dialogue.effect_story_key").Should().Be("({0}: a key to your story, not an item for your pack.)");
    }

    [Fact]
    public void InScreenReaderMode_TheSceneHasNoRule()
    {
        SceneIn("en", true, "1", "1", "").Should().NotContain("═");
        SceneIn("en", false, "1", "1", "").Should().Contain("═");
    }
}
