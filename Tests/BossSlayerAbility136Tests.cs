using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 (E4b, Boss Slayer on every class ability path): Boss Slayer adds 10 percent against a boss or mini-boss to
/// every class ability hit that takes HP from a monster: the area core, each own-handler hit (single target, area,
/// multi-hit, chain, bonus riders), once per hit; never to a spell. The ability's user decides: a teammate's own
/// gear on the teammate path. It draws nothing from the shared random.
/// </summary>
[Collection("SharedGameSingletons")]
public class BossSlayerAbility136Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int D = 10_000;

    // Every item this class registers goes through Slayer, named "BSA136 ..." (under 30 characters); each test removes
    // its own items when it ends. The class runs one test at a time (the SharedGameSingletons collection).
    private static readonly List<int> Registered = new();

    public void Dispose()
    {
        var db = typeof(EquipmentDatabase);
        var all = (Dictionary<int, Equipment>)db.GetField("_allEquipment", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var gate = db.GetField("_lock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        lock (Registered)
        {
            lock (gate)
                foreach (int id in Registered)
                    all.Remove(id);
            Registered.Clear();
        }
    }

    private static int Slayer(string tag)
    {
        int id = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = $"BSA136 {tag}", Slot = EquipmentSlot.Body, MinLevel = 1, Rarity = EquipmentRarity.Common, HasBossSlayer = true,
        });
        lock (Registered) Registered.Add(id);
        return id;
    }

    private static long Slay(long d) => d + (long)(d * 0.10);

    /// <summary>A monster of the given kind: "boss", "mini", "plain", with ":undead" or ":beast" and "%0.2" for its HP share.</summary>
    private static Monster Foe(string spec)
    {
        var m = G.Dummy();
        string kind = spec.Split(':', '%')[0];
        if (kind == "boss") m.IsBoss = true;
        if (kind == "mini") m.IsMiniBoss = true;
        if (kind == "weak") { m.HP = 10; m.MaxHP = 10; }
        if (spec.Contains(":undead")) m.MonsterClass = MonsterClass.Undead;
        if (spec.Contains(":beast")) m.MonsterClass = MonsterClass.Beast;
        int at = spec.IndexOf('%');
        if (at >= 0) m.HP = (long)(m.MaxHP * double.Parse(spec[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture));
        return m;
    }

    private static ClassAbilityResult Use(string id, int damage = D)
    {
        var ability = ClassAbilitySystem.GetAbility(id);
        ability.Should().NotBeNull(id);
        return new ClassAbilityResult { Success = true, AbilityUsed = ability, Damage = damage, SpecialEffect = ability!.SpecialEffect, Duration = 2 };
    }

    private sealed class Outcome
    {
        public long[] Lost = Array.Empty<long>();
        public List<string> Draws = new();
    }

    /// <summary>
    /// One ability action through ApplyAbilityEffectsMultiMonster against the foes of <paramref name="layout"/> (comma
    /// separated, the first is the target). <paramref name="user"/>: "hero" or "mate" (a teammate uses it, as
    /// TryTeammateClassAbility does); the gear goes on the hero, the mate, both or neither.
    /// </summary>
    private static Outcome Act(string id, string layout, bool heroGear, bool mateGear = false, string user = "hero", bool lowHp = false, int seed = 136)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try
        {
            GameConfig.PlayerHitRollEnabled = false;
            var hero = G.Hero();
            var mate = G.Mate("Ally");
            if (heroGear) hero.EquippedItems[EquipmentSlot.Body] = Slayer($"hero {id}");
            if (mateGear) mate.EquippedItems[EquipmentSlot.Body] = Slayer($"mate {id}");
            var actor = user == "mate" ? mate : hero;
            if (lowHp) actor.HP = actor.MaxHP / 10;
            var monsters = layout.Split(',').Select(Foe).ToArray();
            var random = new G.RecordingRandom(seed);
            var rig = G.NewRig(hero, random, new List<Character> { mate }, monsters);
            var before = rig.Monsters.Select(m => m.HP).ToArray();
            ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                .Invoke(rig.Engine, new object?[] { actor, rig.Monsters[0], rig.Monsters, Use(id), rig.Result })!).GetAwaiter().GetResult();
            return new Outcome
            {
                Lost = rig.Monsters.Select((m, i) => before[i] - Math.Max(0, m.HP)).ToArray(),
                Draws = random.Calls,
            };
        }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    private static void ShouldBeTenPercentMore(long with, long without, string why)
    {
        without.Should().BeGreaterThan(0, why);
        // Each hit takes d + floor(d / 10); with hits of 1000 or more the sum is 10 percent less under 0.1 percent.
        ((double)with).Should().BeInRange(without * 1.099, without * 1.1 + 1e-9, why);
    }

    // id, the foes (the first is the target; the boss is the one measured), the user's HP low (Desperate Strike)
    public static IEnumerable<object[]> Paths => new[]
    {
        new object[] { "whirlwind", "boss,plain", false },            // the area core, through the base block's "aoe"
        new object[] { "whirlwind", "plain,boss", false },            // the area core, the boss second (75 percent share)
        new object[] { "maelstrom_faithful", "boss,plain", false },   // the area core, a magical class ability
        new object[] { "execute", "boss%0.2", false },                // own handler, single target, the 30 percent tier
        new object[] { "execute", "boss%0.4", false },                // own handler, single target, the 50 percent tier
        new object[] { "desperate_strike", "boss", true },            // own handler rider
        new object[] { "throw_bomb", "boss:beast", false },           // fire bonus rider
        new object[] { "divine_smite", "boss:undead", false },        // holy bonus rider
        new object[] { "holy_avenger", "boss:undead", false },        // holy avenger rider
        new object[] { "judgment_day", "boss,plain", false },         // own handler area (aoe_holy)
        new object[] { "frenzy", "boss", false },                     // multi-hit
        new object[] { "death_blossom", "boss,plain", false },
        new object[] { "corrosive_cloud", "boss,plain", false },
        new object[] { "sanctified_torrent", "boss,plain", false },
        new object[] { "crescendo", "boss,plain", false },
        new object[] { "grand_finale", "boss,plain", false },
        new object[] { "resonance_cascade", "boss,plain", false },
        new object[] { "harmonic_crescendo", "boss,plain", false },
        new object[] { "entropy_cascade", "boss,plain", false },
        new object[] { "singularity", "boss,plain", false },
        new object[] { "abyssal_eruption", "boss,plain", false },
        new object[] { "abyss_unchained", "boss,plain", false },
        new object[] { "void_rupture", "boss,plain", false },         // the area hit
        new object[] { "void_rupture", "boss,plain,weak", false },    // the area hit and the explosion of a kill
        new object[] { "chain_lightning", "boss,plain", false },      // the chain's primary
        new object[] { "chain_lightning", "plain,boss", false },      // the chained hit
    };

    [Theory]
    [MemberData(nameof(Paths))]
    public void BossSlayerAbility136_AddsTenPercentToTheBossOnEveryPath(string id, string layout, bool lowHp)
    {
        int boss = layout.Split(',').ToList().FindIndex(s => s.StartsWith("boss"));
        var without = Act(id, layout, heroGear: false, lowHp: lowHp);
        var with = Act(id, layout, heroGear: true, lowHp: lowHp);
        with.Draws.Should().Equal(without.Draws, "Boss Slayer draws nothing, so the rolls match");
        ShouldBeTenPercentMore(with.Lost[boss], without.Lost[boss], $"{id} on {layout}");
        for (int i = 0; i < with.Lost.Length; i++)
            if (i != boss) with.Lost[i].Should().Be(without.Lost[i], $"{id}: a plain monster takes nothing more");
    }

    [Theory]
    [InlineData("whirlwind", "mini,plain")]
    [InlineData("execute", "mini%0.2")]
    [InlineData("judgment_day", "mini,plain")]
    [InlineData("frenzy", "mini")]
    [InlineData("chain_lightning", "plain,mini")]
    public void BossSlayerAbility136_AddsTenPercentToAMiniBoss(string id, string layout)
    {
        int mini = layout.Split(',').ToList().FindIndex(s => s.StartsWith("mini"));
        var without = Act(id, layout, heroGear: false);
        var with = Act(id, layout, heroGear: true);
        with.Draws.Should().Equal(without.Draws);
        ShouldBeTenPercentMore(with.Lost[mini], without.Lost[mini], $"{id} on {layout}");
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void BossSlayerAbility136_NothingWithoutTheGear_NothingOnAPlainMonster(string id, string layout, bool lowHp)
    {
        // Without the gear a boss takes what the same plain monster takes; with it, a group of plain monsters takes the same.
        string plain = layout.Replace("boss", "plain");
        var bossNoGear = Act(id, layout, heroGear: false, lowHp: lowHp);
        var plainNoGear = Act(id, plain, heroGear: false, lowHp: lowHp);
        var plainGear = Act(id, plain, heroGear: true, lowHp: lowHp);
        bossNoGear.Lost.Should().Equal(plainNoGear.Lost, $"{id}: no gear, no bonus");
        plainGear.Lost.Should().Equal(plainNoGear.Lost, $"{id}: no boss, no bonus");
        plainGear.Draws.Should().Equal(plainNoGear.Draws);
    }

    [Fact]
    public void BossSlayerAbility136_TheFireRiderTakesItOnceOnItsOwnHit()
    {
        // Throw Bomb: the base hit and the fire bonus (half the ability's damage) are two hits, each from the raw
        // damage; each takes Boss Slayer once, the rider never on top of the base hit's.
        var without = Act("throw_bomb", "boss:beast", heroGear: false);
        var with = Act("throw_bomb", "boss:beast", heroGear: true);
        long rider = D / 2;
        long baseHit = without.Lost[0] - rider;
        baseHit.Should().BeGreaterThan(0);
        with.Lost[0].Should().Be(Slay(baseHit) + Slay(rider));
    }

    [Fact]
    public void BossSlayerAbility136_TheHolyRiderTakesItOnceOnItsOwnHit()
    {
        var without = Act("divine_smite", "boss:undead", heroGear: false);
        var with = Act("divine_smite", "boss:undead", heroGear: true);
        long rider = D;
        long baseHit = without.Lost[0] - rider;
        baseHit.Should().BeGreaterThan(0);
        with.Lost[0].Should().Be(Slay(baseHit) + Slay(rider));
    }

    [Fact]
    public void BossSlayerAbility136_TheOverflowSpreadIsNotBoostedAgain()
    {
        // Seal Fracture: the helper hit (which has Boss Slayer) kills a boss of 10 HP; its overkill spreads to the next
        // boss. The spread is derived from the boosted hit, so it takes no second 10 percent.
        var without = Act("seal_fracture", "boss%0.00000001,boss", heroGear: false);
        var with = Act("seal_fracture", "boss%0.00000001,boss", heroGear: true);
        with.Draws.Should().Equal(without.Draws);
        long primary0 = without.Lost[1] + 10;   // the helper hit: its overkill over 10 HP is the spread
        without.Lost[0].Should().Be(10);
        with.Lost[1].Should().Be(Slay(primary0) - 10, "the spread follows the boosted hit, once");
    }

    [Theory]
    [InlineData("whirlwind", "boss,plain")]
    [InlineData("execute", "boss%0.2")]
    [InlineData("judgment_day", "boss,plain")]
    [InlineData("frenzy", "boss")]
    [InlineData("chain_lightning", "plain,boss")]
    public void BossSlayerAbility136_OnTheTeammatePath_TheUsersGearDecides(string id, string layout)
    {
        int boss = layout.Split(',').ToList().FindIndex(s => s.StartsWith("boss"));
        var none = Act(id, layout, heroGear: false, mateGear: false, user: "mate");
        var mateOnly = Act(id, layout, heroGear: false, mateGear: true, user: "mate");
        var heroOnly = Act(id, layout, heroGear: true, mateGear: false, user: "mate");
        mateOnly.Draws.Should().Equal(none.Draws);
        ShouldBeTenPercentMore(mateOnly.Lost[boss], none.Lost[boss], $"{id}: the teammate's own Boss Slayer");
        heroOnly.Lost.Should().Equal(none.Lost, $"{id}: the player's Boss Slayer is not the teammate's");
    }

    [Fact]
    public void BossSlayerAbility136_AnAreaSpellThroughTheCore_TakesNothing()
    {
        // The player's own area spell reaches the area core with no attacker and the caster as spellCaster.
        long[] Cast(bool gear)
        {
            var hero = G.Hero(cls: CharacterClass.Magician);
            if (gear) hero.EquippedItems[EquipmentSlot.Body] = Slayer("spell");
            var rig = G.NewRig(hero, new G.RecordingRandom(136), null, Foe("boss"), Foe("plain"));
            var before = rig.Monsters.Select(m => m.HP).ToArray();
            ((Task)typeof(CombatEngine).GetMethod("ApplyAoEDamage", F)!
                .Invoke(rig.Engine, new object?[] { rig.Monsters, (long)D, rig.Result, "Fireball", true, null, hero })!).GetAwaiter().GetResult();
            return rig.Monsters.Select((m, i) => before[i] - m.HP).ToArray();
        }
        var without = Cast(false);
        without[0].Should().BeGreaterThan(0);
        Cast(true).Should().Equal(without, "a spell takes no Boss Slayer bonus");
    }

    [Fact]
    public void BossSlayerAbility136_ARunLeavesTheItemDatabaseAsItFoundIt()
    {
        var before = EquipmentDatabase.GetAll().Select(e => e.Id).ToHashSet();
        using (var t = new BossSlayerAbility136Tests()) t.BossSlayerAbility136_AddsTenPercentToTheBossOnEveryPath("whirlwind", "boss,plain", false);
        using (var t = new BossSlayerAbility136Tests()) t.BossSlayerAbility136_OnTheTeammatePath_TheUsersGearDecides("frenzy", "boss");
        EquipmentDatabase.GetAll().Where(e => !before.Contains(e.Id) && e.Name.StartsWith("BSA136 ")).Should().BeEmpty();
    }
}
