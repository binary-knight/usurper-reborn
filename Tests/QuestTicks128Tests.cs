using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8: one kill or one defeat ticks each quest objective at most once. A boss or champion kill is
/// reported under two ids (its name and its base tier) and an objective with no target matches both; an NPC
/// defeat was reported twice to a quest that names the NPC. Objectives that name a target tick as before.
/// A champion (mini-boss) kill ticks a boss objective that names the champion's tier, and no other.
/// </summary>
[Collection("SharedGameSingletons")]
public class QuestTicks128Tests
{
    private static Character Hunter(string tag) => new Character { Name1 = "ticks_" + tag, Name2 = "Ticks " + tag, Level = 30 };

    /// <summary>A quest the hunter holds with one objective needing two, removed from the shared database on dispose.</summary>
    private sealed class Held : IDisposable
    {
        public readonly Quest Quest;
        public QuestObjective Objective => Quest.Objectives[0];
        public Held(Character hunter, QuestObjectiveType type, string targetId, int required = 2, string targetNpc = "")
        {
            Quest = new Quest { Title = "Ticks", Initiator = "A Patron", Occupier = hunter.Name2, TargetNPCName = targetNpc, Date = DateTime.Now, DaysToComplete = 30 };
            Quest.Objectives.Add(new QuestObjective(type, "Ticks", required, targetId, targetId));
            QuestSystem.AddQuestToDatabase(Quest);
        }
        public void Dispose() => Quest.Deleted = true;
    }

    // ---------- a boss kill ----------

    [Fact]
    public void OneFloorBossKill_TicksABossObjectiveWithNoTargetOnce()
    {
        var hunter = Hunter("boss_any");
        using var held = new Held(hunter, QuestObjectiveType.KillBoss, "");
        QuestSystem.OnMonsterKilled(hunter, "Grak the Vile", isBoss: true, tierName: "Orc");
        held.Objective.CurrentProgress.Should().Be(1, "one kill is one tick, though the boss answers to its name and its tier");
    }

    [Theory]
    [InlineData("orc", 1)]             // names the tier
    [InlineData("grak_the_vile", 1)]   // names the boss
    [InlineData("ORC", 1)]             // case does not matter
    [InlineData("zombie", 0)]          // names another monster
    public void OneFloorBossKill_TicksABossObjectiveThatNamesATarget_AsBefore(string targetId, int ticks)
    {
        var hunter = Hunter("boss_" + targetId);
        using var held = new Held(hunter, QuestObjectiveType.KillBoss, targetId);
        QuestSystem.OnMonsterKilled(hunter, "Grak the Vile", isBoss: true, tierName: "Orc");
        held.Objective.CurrentProgress.Should().Be(ticks);
    }

    [Fact]
    public void AKillThatIsNotABoss_TicksNoBossObjective()
    {
        var hunter = Hunter("not_boss");
        using var any = new Held(hunter, QuestObjectiveType.KillBoss, "");
        using var named = new Held(hunter, QuestObjectiveType.KillBoss, "orc");
        QuestSystem.OnMonsterKilled(hunter, "Orc Champion", isBoss: false, tierName: "Orc");
        any.Objective.CurrentProgress.Should().Be(0);
        named.Objective.CurrentProgress.Should().Be(0);
    }

    [Fact]
    public void ABossWithNoTierName_TicksOnce()
    {
        var hunter = Hunter("boss_no_tier");
        using var any = new Held(hunter, QuestObjectiveType.KillBoss, "");
        using var named = new Held(hunter, QuestObjectiveType.KillBoss, "old_god");
        QuestSystem.OnMonsterKilled(hunter, "Old God", isBoss: true);
        any.Objective.CurrentProgress.Should().Be(1);
        named.Objective.CurrentProgress.Should().Be(1);
    }

    // ---------- a kill of a named monster type ----------

    [Fact]
    public void OneChampionKill_TicksAMonsterObjectiveWithNoTargetOnce()
    {
        var hunter = Hunter("monster_any");
        using var held = new Held(hunter, QuestObjectiveType.KillSpecificMonster, "");
        using var count = new Held(hunter, QuestObjectiveType.KillMonsters, "");
        QuestSystem.OnMonsterKilled(hunter, "Orc Champion", isBoss: false, tierName: "Orc");
        held.Objective.CurrentProgress.Should().Be(1, "one kill is one tick, though the champion answers to its name and its tier");
        count.Objective.CurrentProgress.Should().Be(1);
    }

    [Theory]
    [InlineData("orc", 1)]            // names the tier: a champion counts for its tier
    [InlineData("orc_champion", 1)]   // names the champion
    [InlineData("Orc", 1)]            // a mercenary contract's id is capitalized
    [InlineData("ghoul", 0)]
    public void OneChampionKill_TicksAMonsterObjectiveThatNamesATarget_AsBefore(string targetId, int ticks)
    {
        var hunter = Hunter("monster_" + targetId);
        using var held = new Held(hunter, QuestObjectiveType.KillSpecificMonster, targetId);
        QuestSystem.OnMonsterKilled(hunter, "Orc Champion", isBoss: false, tierName: "Orc");
        held.Objective.CurrentProgress.Should().Be(ticks);
    }

    [Fact]
    public void TwoKills_CompleteAnObjectiveNeedingTwo()
    {
        var hunter = Hunter("two_kills");
        using var boss = new Held(hunter, QuestObjectiveType.KillBoss, "");
        using var monster = new Held(hunter, QuestObjectiveType.KillSpecificMonster, "orc");
        for (int i = 0; i < 2; i++) QuestSystem.OnMonsterKilled(hunter, "Grak the Vile", isBoss: true, tierName: "Orc");
        boss.Objective.IsComplete.Should().BeTrue();
        monster.Objective.IsComplete.Should().BeTrue();
        boss.Objective.CurrentProgress.Should().Be(2);
    }

    // ---------- an NPC defeat ----------

    [Theory]
    [InlineData("")]              // no target on the objective
    [InlineData("Ticks Mark")]    // the objective names the NPC
    public void OneDefeat_TicksTheObjectiveOfAQuestThatNamesTheNpcOnce(string objectiveTarget)
    {
        var hunter = Hunter("defeat_" + objectiveTarget.Length);
        var npc = new NPC { ID = "npc_ticks_mark", Name1 = "Ticks Mark", Name2 = "Ticks Mark", Level = 30 };
        using var held = new Held(hunter, QuestObjectiveType.DefeatNPC, objectiveTarget, targetNpc: npc.Name);
        QuestSystem.OnNPCDefeated(hunter, npc);
        held.Objective.CurrentProgress.Should().Be(1, "one defeat is one tick");
        held.Quest.OccupiedDays.Should().BeGreaterThanOrEqualTo(1, "the quest that names the NPC is still marked active");
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("Ticks Mark", 1)]
    [InlineData("Someone Else", 0)]
    public void OneDefeat_TicksAQuestThatNamesNoNpc_AsBefore(string objectiveTarget, int ticks)
    {
        var hunter = Hunter("defeat_plain_" + objectiveTarget.Length);
        var npc = new NPC { ID = "npc_ticks_mark", Name1 = "Ticks Mark", Name2 = "Ticks Mark", Level = 30 };
        using var held = new Held(hunter, QuestObjectiveType.DefeatNPC, objectiveTarget);
        QuestSystem.OnNPCDefeated(hunter, npc);
        held.Objective.CurrentProgress.Should().Be(ticks);
    }

    [Fact]
    public void ABountyNeedingOneDefeat_IsStillMetByOne()
    {
        var hunter = Hunter("defeat_one");
        var npc = new NPC { ID = "npc_ticks_mark", Name1 = "Ticks Mark", Name2 = "Ticks Mark", Level = 30 };
        using var held = new Held(hunter, QuestObjectiveType.DefeatNPC, npc.Name, required: 1, targetNpc: npc.Name);
        QuestSystem.OnNPCDefeated(hunter, npc);
        held.Objective.IsComplete.Should().BeTrue();
    }

    // ---------- the board quest for a tier's champion ----------

    private static string TierId(string tierName) => tierName.ToLower().Replace(" ", "_");

    /// <summary>The board's "defeat the champion" quest as the Quest Hall builds it for a player of this level.</summary>
    private static Quest ChampionQuest(Character hunter, int playerLevel, byte difficulty)
    {
        var quest = new Quest { QuestTarget = QuestTarget.ClearBoss, Difficulty = difficulty, Occupier = hunter.Name2, Date = DateTime.Now, DaysToComplete = 30 };
        typeof(QuestSystem).GetMethod("GenerateDungeonQuestObjectives", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { quest, playerLevel, 0 });
        return quest;
    }

    [Fact]
    public void EveryTierTheChampionQuestCanName_IsATierADungeonChampionCanHave()
    {
        for (int level = 1; level <= 100; level++)
        {
            // the tiers MonsterFamilies.GetMonsterForLevel can return at this level: the quest draws its target from them
            var questIds = MonsterFamilies.AllFamilies
                .Select(f => f.Tiers.FirstOrDefault(t => level >= t.MinLevel && level <= t.MaxLevel))
                .Where(t => t != null).Select(t => TierId(t!.Name)).ToHashSet();
            questIds.Should().NotBeEmpty($"some family has a tier at level {level}");

            var championIds = new HashSet<string>();
            for (int seed = 0; seed < 300 && !championIds.IsSupersetOf(questIds); seed++)
            {
                var champion = MonsterGenerator.GenerateMonster(level, isBoss: false, isMiniBoss: true, random: new Random(seed));
                champion.IsMiniBoss.Should().BeTrue();
                champion.IsBoss.Should().BeFalse();
                championIds.Add(TierId(champion.TierName));
            }
            championIds.Should().BeEquivalentTo(questIds, $"the champions of floor {level} carry the tiers the quest can name there");
        }
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(12, 2)] [InlineData(30, 3)] [InlineData(55, 4)] [InlineData(95, 4)]
    public void TheChampionQuest_IsCompletedByKillingTheChampionOfItsTier(int playerLevel, byte difficulty)
    {
        var hunter = Hunter($"champion_{playerLevel}");
        var quest = ChampionQuest(hunter, playerLevel, difficulty);
        QuestSystem.AddQuestToDatabase(quest);
        try
        {
            var objective = quest.Objectives.Single();
            objective.ObjectiveType.Should().Be(QuestObjectiveType.KillBoss);
            objective.RequiredProgress.Should().Be(1);
            objective.TargetId.Should().NotBeEmpty();

            // the champion of that tier, on the floor the quest drew its tier for
            int floor = Math.Min(playerLevel + difficulty * 3, Math.Min(100, playerLevel + 10));
            Monster? champion = null;
            for (int seed = 0; seed < 300 && champion == null; seed++)
            {
                var m = MonsterGenerator.GenerateMonster(floor, isBoss: false, isMiniBoss: true, random: new Random(seed));
                if (TierId(m.TierName) == objective.TargetId) champion = m;
            }
            champion.Should().NotBeNull($"floor {floor} has a champion of tier {objective.TargetId}");

            QuestSystem.OnMonsterKilled(hunter, champion!.Name, champion.IsBoss, champion.TierName, champion.IsMiniBoss);
            objective.IsComplete.Should().BeTrue($"killing {champion.Name} completes the quest for {objective.TargetId}");
        }
        finally { quest.Deleted = true; }
    }

    [Fact]
    public void OneChampionKill_TicksABossObjectiveThatNamesItsTierOnce()
    {
        var hunter = Hunter("champion_once");
        using var held = new Held(hunter, QuestObjectiveType.KillBoss, "orc");
        QuestSystem.OnMonsterKilled(hunter, "Orc Warchief", isBoss: false, tierName: "Orc", isMiniBoss: true);
        held.Objective.CurrentProgress.Should().Be(1, "one kill, one tick");
    }

    [Fact]
    public void AChampionKill_DoesNotTickABossObjectiveWithNoTarget()
    {
        var hunter = Hunter("champion_untargeted");
        using var held = new Held(hunter, QuestObjectiveType.KillBoss, "", required: 1);
        QuestSystem.OnMonsterKilled(hunter, "Orc Warchief", isBoss: false, tierName: "Orc", isMiniBoss: true);
        held.Objective.CurrentProgress.Should().Be(0, "a boss objective with no target needs a floor boss");
    }

    [Theory]
    [InlineData("Orc Warchief", "Ghoul", "orc")]          // a champion of another tier
    [InlineData("Mimic", "", "mimic")]                    // a mini-boss with no tier, even when the objective names it
    [InlineData("Mimic", "", "")]                         // a mini-boss with no tier and an objective with no target
    [InlineData("Orc Warchief", "Orc", "orc_warchief")]   // the objective names the champion's own name, not its tier
    public void AMiniBossKill_TicksOnlyABossObjectiveThatNamesItsTier(string name, string tier, string targetId)
    {
        var hunter = Hunter("champion_other_" + targetId);
        using var held = new Held(hunter, QuestObjectiveType.KillBoss, targetId);
        QuestSystem.OnMonsterKilled(hunter, name, isBoss: false, tierName: tier, isMiniBoss: true);
        held.Objective.CurrentProgress.Should().Be(0);
    }

    [Fact]
    public void AFloorBossThatIsAlsoFlaggedMiniBoss_TicksOnce()
    {
        var hunter = Hunter("champion_both");
        using var named = new Held(hunter, QuestObjectiveType.KillBoss, "orc");
        using var any = new Held(hunter, QuestObjectiveType.KillBoss, "");
        QuestSystem.OnMonsterKilled(hunter, "Grak the Vile", isBoss: true, tierName: "Orc", isMiniBoss: true);
        named.Objective.CurrentProgress.Should().Be(1);
        any.Objective.CurrentProgress.Should().Be(1);
    }

    [Fact]
    public async Task AChampionKilledInARealFight_CompletesTheQuestForItsTier()
    {
        var hero = new Character
        {
            Name1 = "ticks_fight", Name2 = "Ticks Fight", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = 18, AI = CharacterAI.Human, HP = 50000, MaxHP = 50000, BaseMaxHP = 50000,
            Strength = 5000, BaseStrength = 5000, Defence = 2000, BaseDefence = 2000, WeapPow = 5000,
            Dexterity = 200, BaseDexterity = 200, Agility = 200, BaseAgility = 200, Constitution = 200, BaseConstitution = 200,
            Stamina = 200, BaseStamina = 200, CombatSpeed = CombatSpeed.Instant, MKills = 1,
        };
        var champion = MonsterGenerator.GenerateMonster(8, isBoss: false, isMiniBoss: true, random: new Random(3));
        using var held = new Held(hero, QuestObjectiveType.KillBoss, TierId(champion.TierName), required: 1);
        using var untargeted = new Held(hero, QuestObjectiveType.KillBoss, "", required: 1);

        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("A", 12)
            .Concat(Enumerable.Range(0, 60).Select(i => i % 2 == 0 ? "P" : "N"))), output);   // P passes on the loot drop
        var result = await new CombatEngine(term).PlayerVsMonsters(hero, new List<Monster> { champion }, offerMonkEncounter: false);

        result.Outcome.Should().Be(CombatOutcome.Victory);
        held.Objective.IsComplete.Should().BeTrue("the fight reports the champion's kill with its tier");
        untargeted.Objective.CurrentProgress.Should().Be(0);
    }

    [Theory]
    [InlineData("QuestSystem.OnMonsterKilled(result.Player, result.Monster.Name, isBoss, result.Monster.TierName, result.Monster.IsMiniBoss);")]
    [InlineData("QuestSystem.OnMonsterKilled(result.Player, monster.Name, isBoss, monster.TierName, monster.IsMiniBoss);")]
    [InlineData("QuestSystem.OnMonsterKilled(result.Player, monster.Name, isBossPV, monster.TierName, monster.IsMiniBoss);")]
    [InlineData("QuestSystem.OnMonsterKilled(groupedPlayer, monster.Name, monster.IsBoss, monster.TierName, monster.IsMiniBoss);")]
    public void EveryKillReportInTheCombatEngine_SaysWhetherTheMonsterIsAMiniBoss(string call)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Systems", "CombatEngine.cs"));
        Regex.Matches(src, Regex.Escape(call)).Count.Should().Be(1);
        Regex.Matches(src, "QuestSystem\\.OnMonsterKilled\\(").Count.Should().Be(4, "these four are every kill report");
    }

    // ---------- saved quests ----------

    [Fact]
    public void ASavedObjective_HoldsTheSameFields_SoNothingMigrates()
    {
        typeof(QuestObjectiveData).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(new[]
        {
            "Id", "Description", "DescriptionKey", "DescriptionArgs", "ObjectiveType", "TargetId", "TargetName",
            "RequiredProgress", "CurrentProgress", "IsOptional", "BonusReward",
        });
    }
}
