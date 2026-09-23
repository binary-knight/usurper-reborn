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
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.11: the Team HQ Training bonus (+5% per level) reaches every XP award a player earns from
/// combat, once, after every other modifier and after any party split, so it lands on the player's own
/// share and never flows to NPC teammates. There is no single choke point, so every method in Scripts/
/// that adds to a Character's Experience must either apply TeamHQBonus.ApplyXP, be listed below with its
/// reason (not combat, not a player, dead code, a transfer), or carry an "hq-training: out (reason)"
/// comment on the add.
/// </summary>
[Collection("SharedGameSingletons")]
public class TrainingCoverageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    // "<var>.Experience += ...". ExperienceGained, GodExperience and the like never match.
    private static readonly Regex XPAward = new(@"\b[A-Za-z_]\w*\.Experience\s*\+=");

    private static readonly Regex MethodSignature = new(@"^\s*(?:public|private|internal|protected)\b[^=;]*?(\w+)\s*(?:<[^>]*>)?\s*\(");

    // "File.Method" -> why XP is added there without the Training bonus.
    private static readonly Dictionary<string, string> ExcludedMethods = new()
    {
        // Dead code.
        ["CombatEngine.HandleVictory"] = "dead: only DetermineCombatOutcome calls it, and nothing calls that",
        ["CombatEngine.AwardTeammateExperience"] = "dead: no callers",
        ["CompanionSystem.AwardCompanionExperience"] = "dead: no callers",
        // Not a player Character's XP.
        ["CombatEngine.DistributeTeamSlotXP"] = "NPC teammates and companions: their slot of the un-boosted pot",
        ["CompanionSystem.AwardSpecificCompanionXP"] = "companion XP",
        ["GameEngine.ProcessNPCLeveling"] = "NPC world simulation",
        ["PermadeathHelper.TryDistributeInheritance"] = "heir inheritance, not an award for a fight",
        // Transfers.
        ["LevelMasterLocation.HelpTeamMember"] = "XP gifted from the player to an ally",
        // Not combat: events, quests, features, romance, rare encounters.
        ["OldGodBossSystem.CompleteSaveQuest"] = "an artifact returned to a god, no fight",
        ["SevenSealsSystem.CollectSeal"] = "seal collected",
        ["PuzzleSystem.DisplayPuzzleSuccess"] = "puzzle solved",
        ["QuestSystem.ApplyQuestReward"] = "quest turn-in",
        ["MailSystem.ProcessBirthdayMail"] = "birthday gift",
        ["AchievementSystem.TryUnlock"] = "achievement reward",
        ["AlignmentSystem.CheckAlignmentEvent"] = "alignment event",
        ["DialogueSystem.ApplyEffect"] = "dialogue effect",
        ["NPCPetitionSystem.ExecuteDyingWish"] = "a dying wish granted",
        ["NPCPetitionSystem.ExecuteRivalryReport"] = "a rivalry report",
        ["DiscoverySystem.ApplyEffect"] = "dungeon discovery",
        ["FeatureInteractionSystem.HandleLoreDiscovery"] = "room feature",
        ["FeatureInteractionSystem.HandleSkillSuccess"] = "room feature",
        ["FeatureInteractionSystem.HandleSkillFailure"] = "room feature",
        ["FeatureInteractionSystem.ApplyMoralChoice"] = "room feature",
        ["FeatureInteractionSystem.ApplyClassSpecificBonus"] = "room feature",
        ["FeatureInteractionSystem.HandleMemoryTrigger"] = "room feature",
        ["FeatureInteractionSystem.HandleOceanInsight"] = "room feature",
        ["FeatureInteractionSystem.HandleStandardInteraction"] = "room feature",
        ["DungeonLocation.InteractWithFeature"] = "feature XP shared to teammates",
        ["DungeonLocation.FallenAdventurerEncounter"] = "fallen adventurer's notes",
        ["DungeonLocation.EchoingVoicesEncounter"] = "echoing voices",
        ["DungeonLocation.MysteriousShrine"] = "shrine",
        ["DungeonLocation.RiddleEncounter"] = "riddle",
        ["DungeonLocation.FullPuzzleEncounter"] = "puzzle",
        ["DungeonLocation.MysteryEventEncounter"] = "time warp event",
        ["DungeonLocation.RiddleGateEncounter"] = "riddle gate",
        ["DungeonLocation.ShareEventRewardsWithGroup"] = "the group's share of the events above",
        ["DarkAlleyLocation.ExecuteEvilDeed"] = "evil deed",
        ["CastleLocation.AudienceRequestQuest"] = "royal audience quest advance",
        ["WildernessLocation.ShrineEncounter"] = "shrine",
        ["TempleLocation.PerformEnhancedDesecration"] = "desecration",
        ["InnLocation.PlayDrinkingGame"] = "drinking game",
        ["LoveStreetLocation.ShowIntimateEncounter"] = "romance",
        ["LoveStreetLocation.ProcessDateActivity"] = "romance",
        ["LoveCornerLocation.HandleKiss"] = "romance",
        ["LoveCornerLocation.HandleDinner"] = "romance",
        ["LoveCornerLocation.HandleHoldHands"] = "romance",
        ["LoveCornerLocation.HandleIntimate"] = "romance",
        ["HomeLocation.SpendQualityTime"] = "family time",
        ["RareEncounters.TavernStranger"] = "rare encounter, no fight",
        ["RareEncounters.WanderingMinstrelEncounter"] = "rare encounter, no fight",
        ["RareEncounters.FairyCircleEncounter"] = "rare encounter, no fight",
        ["RareEncounters.DamselInDistressEncounter"] = "rare encounter, no fight",
        ["RareEncounters.UsurperGhostEncounter"] = "rare encounter, no fight",
        ["RareEncounters.GamblingDemonsEncounter"] = "rare encounter, gambling",
        ["RareEncounters.OldHermitEncounter"] = "rare encounter, no fight",
        ["RareEncounters.MysteriousMerchantEncounter"] = "rare encounter, no fight",
        ["RareEncounters.TimeWarpEncounter"] = "rare encounter, no fight",
        ["RareEncounters.AncientLibraryEncounter"] = "rare encounter, no fight",
        ["RareEncounters.WishingWellEncounter"] = "rare encounter, no fight",
        ["RareEncounters.BoneOracleEncounter"] = "rare encounter, no fight",
        ["RareEncounters.RestlessSpiritsEncounter"] = "rare encounter, no fight",
        ["RareEncounters.CryptKeeperEncounter"] = "rare encounter, riddles",
        ["RareEncounters.AlchemistLabEncounter"] = "rare encounter, no fight",
        ["RareEncounters.AncientGolemEncounter"] = "rare encounter, no fight",
        ["RareEncounters.TimeCapsuleEncounter"] = "rare encounter, no fight",
        ["RareEncounters.LostCivilizationEncounter"] = "rare encounter, no fight",
        ["RareEncounters.TorturedSoulsEncounter"] = "rare encounter, no fight",
        ["RareEncounters.LavaBoatEncounter"] = "rare encounter, no fight",
        ["RareEncounters.CosmicEntityEncounter"] = "rare encounter, no fight",
    };

    private static IEnumerable<string> ScriptFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories);

    private static (int start, int end, string name) MethodAround(string[] lines, int index)
    {
        int start = -1; string name = "?";
        for (int j = index; j >= 0; j--)
        {
            var m = MethodSignature.Match(lines[j]);
            if (m.Success) { start = j; name = m.Groups[1].Value; break; }
        }
        int end = index + 1;
        while (end < lines.Length && !MethodSignature.IsMatch(lines[end])) end++;
        return (Math.Max(0, start), end, name);
    }

    private static List<(string file, int line, string method, string text, string body)> Awards()
    {
        var hits = new List<(string, int, string, string, string)>();
        foreach (var path in ScriptFiles())
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].TrimStart();
                if (code.StartsWith("//") || code.StartsWith("*")) continue;
                if (!XPAward.IsMatch(lines[i])) continue;
                var (start, end, name) = MethodAround(lines, i);
                string body = string.Join("\n", lines.Skip(start).Take(end - start));
                hits.Add((Path.GetFileNameWithoutExtension(path), i + 1, name, lines[i].Trim(), body));
            }
        }
        return hits;
    }

    [Fact]
    public void EveryExperienceAward_AppliesTheTrainingOrSaysWhyNot()
    {
        var hits = Awards();
        hits.Count.Should().BeGreaterThan(80, "the scan must find the XP award sites, or it proves nothing");

        var problems = new List<string>();
        foreach (var (file, line, method, text, body) in hits)
        {
            if (ExcludedMethods.ContainsKey($"{file}.{method}")) continue;
            if (body.Contains("TeamHQBonus.ApplyXP")) continue;
            int comment = text.IndexOf("//", StringComparison.Ordinal);
            bool markedOut = comment >= 0 && Regex.IsMatch(text.Substring(comment), @"hq-training: out \(\S[^)]*\)");
            if (!markedOut)
                problems.Add($"{file}.cs:{line} in {method}: {text}");
        }
        problems.Should().BeEmpty("each method that adds a player's XP applies TeamHQBonus.ApplyXP or is marked out with a reason");
    }

    [Fact]
    public void TheScan_SeesTheKnownSites_AndIgnoresOtherExperienceFields()
    {
        var methods = Awards().Select(h => $"{h.file}.{h.method}").ToHashSet();
        methods.Should().Contain(new[]
        {
            "CombatEngine.HandleVictoryMultiMonster", "CombatEngine.HandlePartialVictory", "CombatEngine.DistributeGroupRewards",
            "CombatEngine.DeterminePvPOutcome", "DungeonLocation.AwardDungeonReward", "DungeonLocation.SplitPartyRewards",
            "SecretBosses.HandleVictory", "OldGodBossSystem.HandleBossDefeated", "OldGodBossSystem.HandleBossSaved",
            "OldGodBossSystem.HandleNocturaBetrayal", "AnchorRoadLocation.StartBountyHunting", "AnchorRoadLocation.StartGangWar",
            "AnchorRoadLocation.StartGauntlet", "SanctumLocation.StartHonorTournament", "InnLocation.FightSethAble",
            "DungeonLocation.MysteriousPortalEncounter", "DungeonLocation.ChallengingDuelistEncounter",
            "PrisonWalkLocation.BattlePrisonGuards", "DungeonLocation.FightMalachar", "QuestSystem.AutoCompleteBountyForNPC",
            "WorldBossSystem.DeliverWorldBossRewards",
        });
        XPAward.IsMatch("result.ExperienceGained += 5;").Should().BeFalse();
        XPAward.IsMatch("god.GodExperience += 5;").Should().BeFalse();
        XPAward.IsMatch("player.Experience += 5;").Should().BeTrue();
    }

    [Fact]
    public void TheExcludedMethods_StillExist()
    {
        var present = new HashSet<string>();
        foreach (var path in ScriptFiles())
        {
            string file = Path.GetFileNameWithoutExtension(path);
            foreach (var l in File.ReadLines(path))
            {
                var m = MethodSignature.Match(l);
                if (m.Success) present.Add($"{file}.{m.Groups[1].Value}");
            }
        }
        ExcludedMethods.Keys.Where(k => !present.Contains(k)).Should().BeEmpty("a stale exclusion would hide a new method of the same name");
    }

    [Fact]
    public void TheOldInlineTrainingReader_IsGone()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().NotContain("TeamHQBonus.Training(", "the inline reader truncated, sat before fatigue and the early-game multiplier, and went into the teammates' pot");
    }

    private static string MethodBody(string file, string method)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), file));
        int start = Array.FindIndex(lines, l => { var m = MethodSignature.Match(l); return m.Success && m.Groups[1].Value == method; });
        start.Should().BeGreaterThanOrEqualTo(0, $"{method} must exist in {file}");
        int end = start + 1;
        while (end < lines.Length && !MethodSignature.IsMatch(lines[end])) end++;
        return string.Join("\n", lines.Skip(start).Take(end - start));
    }

    private static int Count(string text, string what) => Regex.Matches(text, Regex.Escape(what)).Count;

    [Theory]
    [InlineData("HandleVictoryMultiMonster", "earlyGameMultMM", "xpSharesMM[0]", "TeamHQBonus.ApplyXP(result.Player, playerXPmm)", "result.Player.Experience += playerXPmm", "DistributeTeamSlotXP(")]
    [InlineData("HandlePartialVictory", "earlyGameMultPV", "xpSharesPV[0]", "TeamHQBonus.ApplyXP(result.Player, playerXPpv)", "result.Player.Experience += playerXPpv", "DistributeTeamSlotXP(")]
    public void TheEngine_AppliesTheTraining_ToThePlayersShare_AfterTheSplit(string method, string lastModifier, string split, string bonus, string add, string pot)
    {
        string body = MethodBody("Scripts/Systems/CombatEngine.cs", method);
        Count(body, "TeamHQBonus.ApplyXP(").Should().Be(1);
        int m = body.IndexOf(lastModifier, StringComparison.Ordinal), s = body.IndexOf(split, StringComparison.Ordinal);
        int b = body.IndexOf(bonus, StringComparison.Ordinal), a = body.IndexOf(add, StringComparison.Ordinal), p = body.IndexOf(pot, StringComparison.Ordinal);
        m.Should().BeGreaterThan(0);
        s.Should().BeGreaterThan(m);
        b.Should().BeGreaterThan(s, "the bonus is the player's own, after the split");
        a.Should().BeGreaterThan(b);
        p.Should().BeGreaterThan(a, "the teammates' pot is the un-boosted total");
    }

    [Fact]
    public void GroupedFollowers_GetTheirOwnTraining_AfterTheGroupGap()
    {
        string body = MethodBody("Scripts/Systems/CombatEngine.cs", "DistributeGroupRewards");
        int gap = body.IndexOf("playerExp = (long)(playerExp * groupXPMult)", StringComparison.Ordinal);
        int bonus = body.IndexOf("TeamHQBonus.ApplyXP(groupedPlayer, playerExp)", StringComparison.Ordinal);
        gap.Should().BeGreaterThan(0);
        bonus.Should().BeGreaterThan(gap);
        bonus.Should().BeLessThan(body.IndexOf("groupedPlayer.Experience += playerExp", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOldGodSave_GetsTheTraining_OnlyWhenItEndedAFight()
    {
        MethodBody("Scripts/Systems/OldGodBossSystem.cs", "ConvertToBossResult").Should().Contain("inCombat: true");
        MethodBody("Scripts/Systems/OldGodBossSystem.cs", "HandleBossSaved").Should().Contain("if (inCombat) xpReward = TeamHQBonus.ApplyXP(player, xpReward);");
        string start = MethodBody("Scripts/Systems/OldGodBossSystem.cs", "StartBossEncounter");
        start.Should().Contain("return await HandleBossSaved(player, boss, terminal);", "the dialogue spare is not a fight");
    }

    [Fact]
    public void OnlyTheFloorBoss_CallsTheDungeonRewardFromCombat()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        var calls = Regex.Matches(src, @"AwardDungeonReward\(([^;]*)\);").Select(m => m.Groups[1].Value).Where(a => !a.StartsWith("long ")).ToList();
        calls.Count.Should().BeGreaterThanOrEqualTo(4);
        calls.Where(a => a.Contains("fromCombat: true")).Should().ContainSingle().Which.Should().Contain("\"Boss Defeated\"");
    }

    // Behaviour.
    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data; private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos); Array.Copy(_data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => _data.Length; public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static TerminalEmulator Term() => new(new ScriptedStream(string.Concat(Enumerable.Repeat("P\n", 40))), new MemoryStream());

    private static Character Hero(int training) => new Character
    {
        Name1 = "training", Name2 = "Training", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 40,
        HP = 5000, MaxHP = 5000, Experience = 0, Gold = 0, AutoLevelUp = false, CombatSpeed = CombatSpeed.Instant, MKills = 100,
        Team = "X", HQLevelsTeam = training > 0 ? "X" : "", HQTrainingLevel = training,
    };

    private static NPC Mate() => new NPC
    {
        Name1 = "mate", Name2 = "Mate", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 40,
        HP = 3000, MaxHP = 3000, Experience = 0,
    };

    private static async Task<(long player, long mate)> WinTogether(int training)
    {
        var hero = Hero(training);
        var mate = Mate();
        var monster = new Monster { Name = "Training Dummy", Level = 40, HP = 0, MaxHP = 100, Experience = 200_000, Gold = 10 };
        var result = new CombatResult { Player = hero, Outcome = CombatOutcome.Victory };
        result.DefeatedMonsters.Add(monster);
        result.Teammates = new List<Character> { mate };
        var engine = new CombatEngine(Term());
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        await (Task)typeof(CombatEngine).GetMethod("HandleVictoryMultiMonster", F)!.Invoke(engine, new object[] { result, false })!;
        // Other awards on this screen (achievements, quests) depend on shared state; the fight's own XP is ExperienceGained.
        hero.Experience.Should().BeGreaterThanOrEqualTo(result.ExperienceGained, "the reported XP was paid");
        return (result.ExperienceGained, mate.Experience);
    }

    [Fact]
    public async Task AVictory_GivesThePlayer10PercentMore_WithTraining2_AndTheTeammateTheSame()
    {
        var plain = await WinTogether(0);
        var boosted = await WinTogether(2);
        plain.player.Should().BeGreaterThan(10_000);
        plain.mate.Should().BeGreaterThan(10_000, "the NPC teammate has a slot of the pot");
        boosted.player.Should().Be((long)Math.Round(plain.player * 1.10), "Training 2 multiplies the player's own share by 1.1");
        boosted.mate.Should().Be(plain.mate, "the leader's Training does not flow into the teammates' pot");
    }

    private static async Task<long> WinPvP(int training)
    {
        var hero = Hero(training);
        var foe = new NPC { Name1 = "foe", Name2 = "Foe", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 40, HP = 0, MaxHP = 1000, Gold = 0 };
        var result = new CombatResult { Player = hero, Opponent = foe, Outcome = CombatOutcome.Victory };
        var engine = new CombatEngine(Term());
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        await (Task)typeof(CombatEngine).GetMethod("DeterminePvPOutcome", F)!.Invoke(engine, new object[] { result })!;
        result.Outcome.Should().Be(CombatOutcome.Victory);
        result.ExperienceGained.Should().Be(hero.Experience);
        return hero.Experience;
    }

    [Fact]
    public async Task APvPWin_GivesThePlayer10PercentMore_WithTraining2()
    {
        long plain = await WinPvP(0);
        long boosted = await WinPvP(2);
        plain.Should().BeGreaterThan(0);
        boosted.Should().Be((long)Math.Round(plain * 1.10));
    }

    private static long DungeonReward(Character hero, long xp, bool fromCombat, List<Character>? party = null)
    {
        var dungeon = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, Term());
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(dungeon, hero);
        if (party != null) dungeon.teammates.AddRange(party);
        var r = ((long xp, long gold))typeof(DungeonLocation).GetMethod("AwardDungeonReward", F)!
            .Invoke(dungeon, new object[] { xp, 0L, "Test", fromCombat })!;
        r.xp.Should().Be(hero.Experience, "the returned XP is what the leader got");
        return hero.Experience;
    }

    [Fact]
    public void TheDungeonReward_OutsideCombat_GetsNoTraining()
    {
        DungeonReward(Hero(2), 10_000, fromCombat: false).Should().Be(10_000, "treasure and the floor-cleared bonus are not combat XP");
        DungeonReward(Hero(0), 10_000, fromCombat: true).Should().Be(10_000);
        DungeonReward(Hero(2), 10_000, fromCombat: true).Should().Be(11_000, "the floor boss is");
    }

    [Fact]
    public void TheFloorBossReward_InAGroup_GivesEachRealPlayerTheirOwnTraining_AndNPCsNone()
    {
        var leader = Hero(2);
        var follower = Hero(4); follower.Name1 = "follower"; follower.Team = "Y"; follower.HQLevelsTeam = "Y";
        follower.RemoteTerminal = Term();
        var npc = Mate();
        DungeonReward(leader, 10_000, fromCombat: true, new List<Character> { follower, npc }).Should().Be(11_000);
        follower.Experience.Should().Be(12_000, "the follower's own Training 4");
        npc.Experience.Should().Be(7_500, "an NPC teammate gets 75% of the plain total");
    }
}
