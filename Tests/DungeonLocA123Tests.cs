using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.3: DungeonLocation.cs lines 1 to 9999 read in the player's language: the NG+ entry notice,
/// the Floor 5 Guardian, the exit markers, the floor hint, Mira's reaction to Veloura, the fallen
/// adventurer's journal, the echoing whispers, the group broadcasts (built per follower) and the
/// news lines. Each Hungarian screen is rendered and every row must fit in 79 columns.
/// </summary>
[Collection("SharedGameSingletons")]
public class DungeonLocA123Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    private static (TerminalEmulator term, MemoryStream output) Term(string input = "")
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Strip(Encoding.UTF8.GetString(output.ToArray()));
    }

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static IEnumerable<string> Rows(string text) => text.Replace("\r", "").Split('\n');

    /// <summary>Writes a render to USURPER_EVIDENCE_DIR when set, for a by-eye check.</summary>
    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    // A BBS header (BaseLocation.ShowBBSHeader) is a box drawn exactly 80 columns wide on every
    // screen, whatever the title; every other row must fit in 79.
    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
        {
            if (row.StartsWith("╔")) row.Length.Should().Be(80, $"the {screen} header box keeps its width: \"{row}\"");
            else row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
        }
    }

    private static async Task<T> InHungarian<T>(Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = "hu";
            GameConfig.ScreenReaderMode = false;
            return await body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static string Hu(string key, params object[] args) => Loc.GetIn("hu", key, args);

    private static Character Hero(int level = 20) => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = level, HP = 900, MaxHP = 900,
        AI = CharacterAI.Human, Mental = 100,
    };

    private static DungeonLocation Dungeon(TerminalEmulator term, Character hero, int level, DungeonFloor? floor = null)
    {
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, level);
        if (floor != null) typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(d, floor);
        return d;
    }

    private static string Src() =>
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));

    // ---------- 1. the NG+ notice on dungeon entry ----------

    [Fact]
    public async Task NgPlusModifiers_RenderInHungarian_AndFit()
    {
        string text = await InHungarian(() =>
        {
            var (term, output) = Term();
            typeof(DungeonLocation).GetMethod("ShowNgPlusModifiers", FS)!.Invoke(null, new object[] { term, 4 });
            return Task.FromResult(Shown(term, output));
        });
        Capture("dungeon-a-ngplus-hu.txt", text);
        text.Should().Contain("  " + Hu("dungeon.ngplus_active"));
        foreach (var key in new[] { "dungeon.ngplus_empowered_line", "dungeon.ngplus_ancient_line", "dungeon.ngplus_convergence_line" })
            text.Should().Contain("    " + Hu(key), key);
        text.Should().NotContain("Active NG+ Modifiers").And.NotContain("Empowered Monsters");
        EveryRowFits(text, "NG+ notice");
    }

    // ---------- 2. the Floor 5 Guardian ----------

    [Fact]
    public async Task GuardianVictory_RendersInHungarian_AndFits()
    {
        string text = await InHungarian(() =>
        {
            var (term, output) = Term();
            typeof(DungeonLocation).GetMethod("ShowGuardianVictory", FS)!.Invoke(null, new object[] { term, 1000L });
            return Task.FromResult(Shown(term, output));
        });
        Capture("dungeon-a-guardian-hu.txt", text);
        foreach (var key in new[] { "dungeon.guardian_falls", "dungeon.guardian_proven_worthy", "dungeon.guardian_deeper_await" })
            text.Should().Contain("  " + Hu(key), key);
        text.Should().Contain("  " + Hu("dungeon.guardian_drops_gold", $"{1000L:N0}"));
        text.Should().NotContain("falls!").And.NotContain("proven worthy").And.NotContain("pouch of");
        EveryRowFits(text, "Guardian victory");

        Hu("dungeon.guardian_name").Should().NotBe(Loc.GetIn("en", "dungeon.guardian_name"));
        Hu("dungeon.guardian_phrase").Should().NotBe(Loc.GetIn("en", "dungeon.guardian_phrase"));
        Src().Should().Contain("guardian.Name = Loc.Get(\"dungeon.guardian_name\")")
            .And.Contain("guardian.Phrase = Loc.Get(\"dungeon.guardian_phrase\")");
    }

    // ---------- 3. the exit markers in the room view ----------

    private static DungeonFloor ExitFloor()
    {
        var here = new DungeonRoom { Id = "r1", Name = "Hall", Description = "A quiet hall.", DangerRating = 1, IsExplored = true, IsCleared = true };
        var cleared = new DungeonRoom { Id = "r2", Name = "Crypt", Description = "A crypt.", DangerRating = 1, IsExplored = true, IsCleared = true };
        var explored = new DungeonRoom { Id = "r3", Name = "Vault", Description = "A vault.", DangerRating = 1, IsExplored = true, HasMonsters = true };
        var unknown = new DungeonRoom { Id = "r4", Name = "Pit", Description = "A pit.", DangerRating = 1 };
        here.Exits[Direction.North] = new RoomExit("r2", "north");
        here.Exits[Direction.South] = new RoomExit("r3", "south");
        here.Exits[Direction.East] = new RoomExit("r4", "east");
        cleared.Exits[Direction.South] = new RoomExit("r1", "south");
        var floor = new DungeonFloor { Level = 6, Theme = DungeonTheme.Catacombs, CurrentRoomId = "r1" };
        floor.Rooms.AddRange(new[] { here, cleared, explored, unknown });
        return floor;
    }

    private static async Task<string> RoomView(bool screenReader)
    {
        return await InHungarian(() =>
        {
            GameConfig.ScreenReaderMode = screenReader;
            var (term, output) = Term();
            var floor = ExitFloor();
            var d = Dungeon(term, Hero(), 6, floor);
            typeof(DungeonLocation).GetMethod("DisplayRoomViewBBS", F)!.Invoke(d, new object[] { floor.Rooms[0] });
            return Task.FromResult(Shown(term, output));
        });
    }

    [Fact]
    public async Task ExitMarkers_RenderInHungarian_AndTheRoomViewFits()
    {
        string text = await RoomView(screenReader: false);
        Capture("dungeon-a-exits-hu.txt", text);
        string exits = Rows(text).Single(r => r.StartsWith(Loc.GetIn("hu", "dungeon.bbs_exits").TrimEnd()));
        exits.Should().Contain("]" + Hu("dungeon.exit_cleared"));
        exits.Should().Contain("]" + Hu("dungeon.exit_explored"));
        exits.Should().Contain("](?)");
        exits.Should().NotContain("(clr)").And.NotContain("(exp)");
        EveryRowFits(text, "room view");
    }

    [Fact]
    public async Task ExitMarkers_ScreenReaderAllClear_RendersInHungarian_AndFits()
    {
        string text = await RoomView(screenReader: true);
        Capture("dungeon-a-exits-sr-hu.txt", text);
        text.Should().Contain("]" + Hu("dungeon.exit_all_clear"));
        text.Should().NotContain("(all clear)");
        EveryRowFits(text, "screen reader room view");
    }

    // ---------- 4. the floor 90 hint ----------

    [Fact]
    public async Task SunforgedBladeHint_RendersInHungarian_AndFits()
    {
        var story = StoryProgressionSystem.Instance;
        bool had = story.HasStoryFlag("aurelion_save_quest");
        try
        {
            story.SetStoryFlag("aurelion_save_quest", true);
            string text = await InHungarian(() =>
            {
                var (term, output) = Term();
                var d = Dungeon(term, Hero(80), 90);
                typeof(DungeonLocation).GetMethod("ShowFloorGuidance", F)!.Invoke(d, new object[] { 90 });
                return Task.FromResult(Shown(term, output));
            });
            Capture("dungeon-a-hint90-hu.txt", text);
            text.Should().Contain(Hu("dungeon.hint_sunforged_blade"));
            text.Should().NotContain("blazing with ancient light");
            EveryRowFits(text, "floor 90 hint");
        }
        finally { story.SetStoryFlag("aurelion_save_quest", had); }
    }

    // ---------- 5. Mira's reaction to Veloura ----------

    public static IEnumerable<object[]> MiraCases() => new[]
    {
        new object[] { BossOutcome.Saved, "merciful", new[] { "saved_1", "saved_2", "saved_3", "saved_4" } },
        new object[] { BossOutcome.Defeated, "aggressive", new[] { "aggressive_1", "aggressive_2", "aggressive_3" } },
        new object[] { BossOutcome.Defeated, "merciful", new[] { "mercy_1", "mercy_2", "mercy_3" } },
        new object[] { BossOutcome.Allied, "diplomatic", new[] { "allied_1", "allied_2", "allied_3" } },
    };

    [Theory]
    [MemberData(nameof(MiraCases))]
    public async Task MiraReaction_RendersInHungarian_AndFits(BossOutcome outcome, string approach, string[] lines)
    {
        var companions = CompanionSystem.Instance;
        var all = (Dictionary<CompanionId, Companion>)typeof(CompanionSystem).GetField("companions", F)!.GetValue(companions)!;
        var active = (List<CompanionId>)typeof(CompanionSystem).GetField("activeCompanions", F)!.GetValue(companions)!;
        try
        {
            all[CompanionId.Mira].IsRecruited = true;
            active.Add(CompanionId.Mira);
            string text = await InHungarian(async () =>
            {
                var (term, output) = Term("\n\n\n");
                var hero = Hero(60);
                var d = Dungeon(term, hero, 40);
                var result = new BossEncounterResult { Success = true, God = OldGodType.Veloura, Outcome = outcome, ApproachType = approach };
                await (Task)typeof(DungeonLocation).GetMethod("ShowTownReactionScene", F)!.Invoke(d, new object[] { result, hero, term })!;
                return Shown(term, output);
            });
            Capture($"dungeon-a-mira-{outcome}-{approach}-hu.txt", text);
            foreach (var line in lines)
                text.Should().Contain("  " + Hu("dungeon.react_veloura_mira_" + line), line);
            text.Should().NotContain("Mira sinks").And.NotContain("Her face is stone").And.NotContain("Mira kneels")
                .And.NotContain("An alliance?");
            EveryRowFits(text, "town reaction");
        }
        finally
        {
            active.Remove(CompanionId.Mira);
            all[CompanionId.Mira].IsRecruited = false;
        }
    }

    // ---------- 6. the fallen adventurer's journal and 7. the echoing whispers ----------

    private static string[] Keys(string prefix, int n) => Enumerable.Range(1, n).Select(i => $"{prefix}{i}").ToArray();

    public static IEnumerable<object[]> JournalCases() => new[]
    {
        new object[] { 10, Keys("dungeon.journal_shallow_", 4) },
        new object[] { 45, Keys("dungeon.journal_middle_", 4) },
        new object[] { 75, Keys("dungeon.journal_deep_", 4) },
    };

    [Theory]
    [MemberData(nameof(JournalCases))]
    public async Task FallenAdventurerJournal_RendersInHungarian_AndFits(int level, string[] keys)
    {
        for (int seed = 0; seed < 8; seed++)
        {
            string text = await InHungarian(async () =>
            {
                var (term, output) = Term("X\n\n\n");
                var d = Dungeon(term, Hero(level), level);
                typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, new Random(seed));
                await (Task)typeof(DungeonLocation).GetMethod("FallenAdventurerEncounter", F)!.Invoke(d, null)!;
                // A MemoryStream does not echo the player's Enter; in play it ends the prompt's row.
                string prompt = Loc.Get("dungeon.fallen_adventurer_choice");
                return Shown(term, output).Replace(prompt, prompt + "\n");
            });
            if (seed == 0) Capture($"dungeon-a-journal-{level}-hu.txt", text);
            keys.Count(k => text.Contains(Hu(k))).Should().Be(1, $"one journal entry is read out (seed {seed})");
            text.Should().NotContain("\"Day 12").And.NotContain("Old Gods stir").And.NotContain("Manwe's throne");
            EveryRowFits(text, "fallen adventurer");
        }
    }

    public static IEnumerable<object[]> WhisperCases() => new[]
    {
        new object[] { 20, 100, Keys("dungeon.whisper_weary_", 3) },
        new object[] { 85, 900, Keys("dungeon.whisper_deep_", 3) },
        new object[] { 20, 900, Keys("dungeon.whisper_", 5) },
    };

    [Theory]
    [MemberData(nameof(WhisperCases))]
    public async Task EchoingWhispers_RenderInHungarian_AndFit(int level, int hp, string[] keys)
    {
        for (int seed = 0; seed < 8; seed++)
        {
            string text = await InHungarian(async () =>
            {
                var (term, output) = Term("\n\n");
                var hero = Hero(level);
                hero.HP = hp;
                var d = Dungeon(term, hero, level);
                typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, new Random(seed));
                await (Task)typeof(DungeonLocation).GetMethod("EchoingVoicesEncounter", F)!.Invoke(d, null)!;
                return Shown(term, output);
            });
            if (seed == 0) Capture($"dungeon-a-whispers-{level}-{hp}-hu.txt", text);
            keys.Count(k => text.Contains(Hu(k))).Should().Be(1, $"one whisper is heard (seed {seed})");
            text.Should().NotContain("\"Rest...").And.NotContain("Manwe watches").And.NotContain("\"Deeper...");
            EveryRowFits(text, "echoing voices");
        }
    }

    // ---------- 8. group broadcasts, built in each follower's language ----------

    // Follower lines as BroadcastDungeonEvent sends them: two spaces, then the text. The sample
    // name is a long player name and the numbers are six digits, so the check is a worst case.
    private const string LongName = "Hosszunevu Kalandor";
    private const int Big = 123456;

    private static readonly (string key, object[] args)[] Broadcasts =
    {
        ("dungeon.bc_trap", Array.Empty<object>()),
        ("dungeon.bc_trap_evaded", new object[] { LongName }),
        ("dungeon.bc_trap_pit", new object[] { LongName, Big }),
        ("dungeon.bc_trap_darts", new object[] { LongName, Big }),
        ("dungeon.bc_trap_fire", new object[] { LongName, Big }),
        ("dungeon.bc_trap_acid", new object[] { LongName, Big }),
        ("dungeon.bc_trap_curse_resisted", new object[] { LongName }),
        ("dungeon.bc_trap_curse_drain", new object[] { LongName, Big }),
        ("dungeon.bc_trap_salvage", new object[] { LongName, Big }),
        ("dungeon.bc_boss_encounter", new object[] { "3 Skeletons, Bone Lord" }),
        ("dungeon.bc_combat", new object[] { "3 Skeletons, 2 Goblins" }),
        ("dungeon.bc_party_treasure", Array.Empty<object>()),
        ("dungeon.bc_descends", new object[] { 100, DungeonTheme.AncientRuins }),
        ("dungeon.bc_chest_opened", Array.Empty<object>()),
        ("dungeon.bc_chest_trapped", Array.Empty<object>()),
        ("dungeon.bc_chest_mimic", Array.Empty<object>()),
        ("dungeon.bc_shrine_healed", new object[] { LongName }),
        ("dungeon.bc_shrine_strength", new object[] { LongName, 5 }),
        ("dungeon.bc_shrine_exp", new object[] { LongName, Big }),
        ("dungeon.bc_shrine_nothing", new object[] { LongName }),
        ("dungeon.bc_shrine_hp", new object[] { LongName, Big }),
        ("dungeon.bc_shrine_gold", new object[] { LongName, Big }),
        ("dungeon.feature_your_share", new object[] { "Ancient Altar" }),
    };

    [Fact]
    public void GroupBroadcasts_RenderInHungarian_AndFit()
    {
        var sb = new StringBuilder();
        foreach (var (key, args) in Broadcasts)
        {
            Loc.HasIn("hu", key).Should().BeTrue(key);
            string line = "  " + Hu(key, args);
            line.Should().NotBe("  " + Loc.GetIn("en", key, args), key);
            sb.AppendLine(line);
        }
        string text = sb.ToString();
        Capture("dungeon-a-broadcasts-hu.txt", text);
        EveryRowFits(text, "follower broadcast");
    }

    [Fact]
    public void GroupBroadcasts_SourceBuildsThemPerFollower()
    {
        string src = Src();
        foreach (var (key, _) in Broadcasts.Where(b => b.key.StartsWith("dungeon.bc_")))
            src.Should().Contain($"Loc.GetIn(lang, \"{key}\"", key);
        src.Should().Contain("BroadcastToAllGroupSessionsLocalized(group, buildMessage");
        src.Should().Contain("Loc.GetIn(shareLang, \"dungeon.feature_your_share\"");
        foreach (var english in new[] { "*** TRAP! ***", "quick reflexes avoid", "falls into a pit", "prays at a shrine",
                     "BOSS ENCOUNTER:", "The group faces", "The party found treasure", "FLOOR CLEARED ═══",
                     "The group descends", "The chest was", "{feature.Name} (Your Share)" })
            src.Should().NotContain(english);
    }

    // ---------- 9. news lines ----------

    [Fact]
    public void NewsLines_AreHungarian_AndFitTheNewsBoard()
    {
        // The news board prints "[HH:mm] " and the line as it is, without wrapping.
        var lines = new List<string>
        {
            Hu("dungeon.news_old_god_slain_temple", "Tester", "Veloura"),
            Hu("dungeon.news_old_god_slain_floor", "Tester", "Veloura", 40),
            Hu("dungeon.news_old_god_saved", "Tester", "Veloura"),
            Hu("dungeon.news_old_god_spared", "Tester", "Veloura"),
            Hu("dungeon.news_lyris_found", "Tester", "Lyris"),
        };
        lines.AddRange(Keys("dungeon.respawn_news_", 12).Select(k => Hu(k)));
        string text = string.Join("\n", lines.Select(l => "[12:00] " + l));
        Capture("dungeon-a-news-hu.txt", text);
        EveryRowFits(text, "news board");
        text.Should().NotContain("has slain").And.NotContain("The dungeon");

        var respawn = (string[])typeof(DungeonLocation).GetField("DungeonRespawnMessages", FS)!.GetValue(null)!;
        respawn.Should().BeEquivalentTo(Keys("dungeon.respawn_news_", 12), "the static list holds keys, read in the language of the moment");
    }

    // ---------- 10. the duelist's weapon ----------

    [Fact]
    public void DuelistWeapon_IsSavedAsAKey_AndShownInHungarian()
    {
        var (term, _) = Term();
        var d = Dungeon(term, Hero(), 20);
        var hero = new Character { Name1 = "duelref", Name2 = "Duelref", ID = "duel-ref-123", Level = 20 };
        var duelist = typeof(DungeonLocation).GetMethod("GetOrCreateRecurringDuelist", F)!.Invoke(d, new object[] { hero })!;
        string weapon = (string)duelist.GetType().GetProperty("Weapon")!.GetValue(duelist)!;
        weapon.Should().StartWith("dungeon.duelist_weapon_", "a saved duelist keeps its weapon as a key");
        Loc.HasIn("hu", weapon).Should().BeTrue();

        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = "hu";
            DungeonLocation.ShownDuelistWeapon("dungeon.duelist_weapon_mace").Should().Be(Hu("dungeon.duelist_weapon_mace"));
            DungeonLocation.ShownDuelistWeapon("Longsword of Honor").Should().Be("Longsword of Honor", "a pre 1.2.3 save shows its English name");
            DungeonLocation.ShownDuelistWeapon("").Should().Be("");
        }
        finally { GameConfig.Language = prev; }
    }

    // ---------- 11. the rest of the keyed lines ----------

    [Fact]
    public void OtherKeyedLines_AreHungarian_AndFit()
    {
        var lines = new[]
        {
            "  " + Hu("dungeon.ally_fee_line", LongName, $"{Big:N0}"),
            "  " + Hu("quest_hall.active"),
            "  " + Hu("dungeon.quest_objectives_count", 3, 5),
            Hu("dungeon.must_defeat_god_floor", Hu("dungeon.an_old_god"), 100),
            Hu("dungeon.xp_hint_plain", Big),
        };
        string text = string.Join("\n", lines);
        Capture("dungeon-a-other-hu.txt", text);
        text.Should().Contain("célkitűzés").And.Contain("TP").And.Contain("Ősi Isten");
        EveryRowFits(text, "other dungeon lines");

        string src = Src();
        foreach (var english in new[] { "Active NG+ Modifiers", "objectives]", "\"(clr)\"", "\"(exp)\"", "\"an Old God\"",
                     "} powerful [", "$\"~{estXP} XP\"", "has slain the Old God", "at a forgotten shrine" })
            src.Should().NotContain(english);
    }
}
