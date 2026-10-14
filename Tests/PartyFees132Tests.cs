using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: the dungeon entry fee. The relationship modifier follows the relation scale (a LOWER value is
/// friendlier): Friendship or closer earns the discount, Anger or worse the surcharge, and the fee
/// breakdown's reason line follows the same test. When gold does not cover every fee, only the fee NPCs
/// that gold cannot cover stay behind; allies that pay no fee come along exactly as when the fees are paid.
/// </summary>
[Collection("SharedGameSingletons")]
public class PartyFees132Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int Gap = 15;

    private static Character Hero(long gold = 1_000_000) => new Character
    {
        Name1 = "Fee Tester", Name2 = "Fee Tester", ID = "fee_tester", Level = 10, Gold = gold, AI = CharacterAI.Human,
    };

    private static NPC Ally(string id, int level) => new NPC
    {
        ID = id, Name1 = "Ally " + id, Name2 = "Ally " + id, Level = level, Class = CharacterClass.Warrior,
        HP = 100, MaxHP = 100, AI = CharacterAI.Computer,
    };

    private static void SetScore(Character a, Character b, int score)
    {
        var rec = RelationshipSystem.GetOrCreateRelationship(a, b);
        if (rec.Name1 == a.Name) rec.Relation1 = score; else rec.Relation2 = score;
    }

    private static void ResetWorld()
    {
        RelationshipSystem.Instance.Reset();
        RomanceTracker.Instance.Reset();
    }

    private static TeamBalanceSystem Balance => TeamBalanceSystem.Instance;

    private static (long fee, string reason) FeeAt(int? score, Action<NPC>? setup = null)
    {
        ResetWorld();
        var hero = Hero();
        var npc = Ally("a1", hero.Level + Gap);
        if (score.HasValue) SetScore(hero, npc, score.Value);
        setup?.Invoke(npc);
        long fee = Balance.CalculateEntryFee(hero, npc);
        var reason = Balance.GetFeeBreakdown(hero, new List<Character> { npc }).Single().reason;
        return (fee, reason);
    }

    // ---------- tests 1 and 2: the modifier and its reason line ----------

    [Theory]
    [InlineData(GameConfig.RelationMarried)]
    [InlineData(GameConfig.RelationLove)]
    [InlineData(GameConfig.RelationFriendship)]
    public void Friendship_OrCloser_GetsTheDiscount_AndItsReason(int score)
    {
        var normal = FeeAt(GameConfig.RelationNormal);
        var (fee, reason) = FeeAt(score);
        fee.Should().BeLessThan(normal.fee, "a friendly feeling lowers the fee");
        fee.Should().BeInRange((long)(normal.fee * 0.85f) - 1, (long)(normal.fee * 0.85f) + 1, "15 percent off");
        reason.Should().Be(Loc.Get("team.levels_relationship_bonus", Gap));
    }

    [Theory]
    [InlineData(GameConfig.RelationNormal)]
    [InlineData(GameConfig.RelationRespect)]
    [InlineData(GameConfig.RelationSuspicious)]
    public void BetweenFriendshipAndAnger_NeitherModifier_AndPlainReason(int score)
    {
        var (fee, reason) = FeeAt(score);
        var none = FeeAt(null);
        fee.Should().Be(none.fee, "no record at all is Normal, which has no modifier");
        reason.Should().Be(Loc.Get("team.levels_above_you", Gap));
    }

    [Theory]
    [InlineData(GameConfig.RelationAnger)]
    [InlineData(GameConfig.RelationEnemy)]
    [InlineData(GameConfig.RelationHate)]
    public void Anger_OrWorse_GetsTheSurcharge_AndItsReason(int score)
    {
        var normal = FeeAt(GameConfig.RelationNormal);
        var (fee, reason) = FeeAt(score);
        fee.Should().BeGreaterThan(normal.fee, "an angry feeling raises the fee");
        fee.Should().BeInRange((long)(normal.fee * 1.25f) - 1, (long)(normal.fee * 1.25f) + 1, "25 percent more");
        reason.Should().Be(Loc.Get("team.levels_low_relationship", Gap));
    }

    [Fact]
    public void Spouse_AndLover_AsToday_EvenAtAngerOrFriendship()
    {
        var normal = FeeAt(GameConfig.RelationNormal);

        var spouse = FeeAt(GameConfig.RelationHate, n => RomanceTracker.Instance.Spouses.Add(
            new Spouse { NPCId = n.ID, NPCName = n.Name, LoveLevel = 80 }));
        spouse.fee.Should().BeInRange((long)(normal.fee * 0.5f) - 1, (long)(normal.fee * 0.5f) + 1);
        spouse.reason.Should().Be(Loc.Get("team.levels_spouse_discount", Gap));

        var lover = FeeAt(GameConfig.RelationHate, n => RomanceTracker.Instance.AddLover(n.ID, 50));
        lover.fee.Should().BeInRange((long)(normal.fee * 0.75f) - 1, (long)(normal.fee * 0.75f) + 1, "no surcharge on top of the lover discount");
        lover.reason.Should().Be(Loc.Get("team.levels_lover_discount", Gap));

        var loverFriend = FeeAt(GameConfig.RelationLove, n => RomanceTracker.Instance.AddLover(n.ID, 50));
        loverFriend.fee.Should().BeInRange((long)(normal.fee * 0.75f) - 1, (long)(normal.fee * 0.75f) + 1, "the larger discount wins");
        loverFriend.reason.Should().Be(Loc.Get("team.levels_lover_discount", Gap));
    }

    // ---------- tests 3 and 4: who stays when the fees are not all paid ----------

    private static (DungeonLocation d, Character hero, Dictionary<string, Character> party, long cheap, long dear)
        Setup(long? gold, string input)
    {
        ResetWorld();
        var hero = Hero();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output);
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);

        var dear = Ally("dear", 30);
        var cheap = Ally("cheap", 20);
        var free = Ally("free", 12);
        var companion = new Character { Name1 = "Comp", Name2 = "Comp", IsCompanion = true, Level = 40, HP = 100, MaxHP = 100 };
        var beast = new Character { Name1 = "Beast", Name2 = "Beast", IsPet = true, Level = 40, HP = 100, MaxHP = 100 };
        var echo = new Character { Name1 = "Echo", Name2 = "Echo", IsEcho = true, Level = 40, HP = 100, MaxHP = 100 };
        var merc = new Character { Name1 = "Merc", Name2 = "Merc", IsMercenary = true, Level = 40, HP = 100, MaxHP = 100 };

        long cheapFee = Balance.CalculateEntryFee(hero, cheap);
        long dearFee = Balance.CalculateEntryFee(hero, dear);
        cheapFee.Should().BeGreaterThan(0);
        dearFee.Should().BeGreaterThan(cheapFee);
        hero.Gold = gold ?? (cheapFee + dearFee + 5);
        if (gold == -1) hero.Gold = cheapFee + 1;

        d.teammates.AddRange(new Character[] { companion, beast, echo, merc, free, cheap, dear });
        var party = new Dictionary<string, Character>
        {
            ["companion"] = companion, ["beast"] = beast, ["echo"] = echo, ["merc"] = merc,
            ["free"] = free, ["cheap"] = cheap, ["dear"] = dear,
        };
        return (d, hero, party, cheapFee, dearFee);
    }

    private static async Task Enter(DungeonLocation d, Character hero)
    {
        Pacing.Disabled = true;
        var m = typeof(DungeonLocation).GetMethod("CheckAndPayEntryFees", F)!;
        var term = (TerminalEmulator)typeof(BaseLocation).GetField("terminal", F)!.GetValue(d)!;
        (await (Task<bool>)m.Invoke(d, new object[] { hero, term })!).Should().BeTrue();
    }

    [Fact]
    public async Task GoldShort_OnlyTheUnaffordableFeeNpcStaysBehind()
    {
        var (d, hero, p, cheap, dear) = Setup(-1, "y\n");
        await Enter(d, hero);
        d.teammates.Should().Contain(p["companion"]);
        d.teammates.Should().Contain(p["beast"]);
        d.teammates.Should().Contain(p["echo"]);
        d.teammates.Should().Contain(p["merc"]);
        d.teammates.Should().Contain(p["free"], "an NPC ally who pays no fee comes along");
        d.teammates.Should().Contain(p["cheap"], "the affordable fee NPC is paid for and comes along");
        d.teammates.Should().NotContain(p["dear"], "gold cannot cover this fee");
        hero.Gold.Should().Be(1, "only the affordable fee was paid");
    }

    [Fact]
    public async Task GoldShort_AndTheAffordableFeeRefused_FeeNpcsStay_NoFeeAlliesCome()
    {
        var (d, hero, p, cheap, dear) = Setup(-1, "n\n");
        await Enter(d, hero);
        foreach (var k in new[] { "companion", "beast", "echo", "merc", "free" })
            d.teammates.Should().Contain(p[k], k + " pays no fee");
        d.teammates.Should().NotContain(p["cheap"]);
        d.teammates.Should().NotContain(p["dear"]);
        hero.Gold.Should().Be(cheap + 1, "nothing was paid");
    }

    [Fact]
    public async Task GoldCoversEveryFee_PartyUnchanged()
    {
        var (d, hero, p, cheap, dear) = Setup(null, "y\n");
        var before = d.teammates.ToList();
        await Enter(d, hero);
        d.teammates.Should().Equal(before);
        hero.Gold.Should().Be(5);
    }

    [Fact]
    public async Task FeesDeclined_FeeNpcsStayBehind_EveryNoFeeAllyComes()
    {
        var (d, hero, p, cheap, dear) = Setup(null, "n\n");
        await Enter(d, hero);
        foreach (var k in new[] { "companion", "beast", "echo", "merc", "free" })
            d.teammates.Should().Contain(p[k], k + " pays no fee");
        d.teammates.Should().NotContain(p["cheap"]);
        d.teammates.Should().NotContain(p["dear"]);
        hero.Gold.Should().Be(cheap + dear + 5, "nothing was paid");
    }
}
