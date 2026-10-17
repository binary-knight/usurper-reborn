using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 difficulty pass, DESIGN item 2 (rows E1 to E8, and the switch): on dungeon floors 4 to 10 a regular
/// encounter follows the templates (sizes 1, 2 and 3, one family half the time, a budgeted group of 3, one
/// caster in a mixed group), behind GameConfig.EncounterTemplatesEnabled and only for a caller that passes
/// EncounterRules.Dungeon. Everywhere else MonsterGenerator.GenerateMonsterGroup draws what it drew before:
/// the golden lists below were written by the code of release-1.2.8 at 0c8b24a5, before this change.
/// </summary>
[Collection("SharedGameSingletons")]
public class EncounterTemplates128Tests
{
    private const int Groups = 10_000;

    /// <summary>Level, real floor, seed: name/family/level/weapon/armor of each member, from the code before
    /// this change, called without rules.</summary>
    private static readonly Dictionary<string, string> GoldenDeep = new()
    {
        ["11,11,1"] = "Giant Spider/Insectoid/11/Battle Axe/Studded Leather",
        ["11,11,2"] = "Pixie/Fey/11/Battle Axe/Ring Mail|Pixie/Fey/11/Iron Sword/Chain Mail",
        ["11,11,3"] = "Orc/Orcish/11/Iron Sword/Studded Leather|Pixie/Fey/11/Battle Axe/Scale Mail|Giant Spider/Insectoid/11/Iron Sword/Chain Mail",
        ["11,11,4"] = "Imp/Demonic/11/Iron Sword/Scale Mail|Imp/Demonic/11/Battle Axe/Scale Mail|Imp/Demonic/11/Mace/Studded Leather",
        ["11,11,5"] = "Spark/Elemental/11/Spear/Chain Mail",
        ["11,11,6"] = "Animated Armor/Construct/11/Spear/Scale Mail|Ogre/Giant/11/Battle Axe/Scale Mail",
        ["11,11,7"] = "Hobgoblin/Goblinoid/11/Mace/Chain Mail|Hobgoblin/Goblinoid/11/Spear/Ring Mail|Hobgoblin/Goblinoid/11/Spear/Studded Leather",
        ["11,11,8"] = "Kobold/Draconic/11/Mace/Studded Leather",
        ["30,30,1"] = "Sprite/Fey/30/War Hammer/Splint Mail",
        ["30,30,2"] = "Shadow/Shadow/30/War Hammer/Full Plate|Shadow/Shadow/30/Steel Longsword/Plate Armor",
        ["30,30,3"] = "Orc Warrior/Orcish/30/Steel Longsword/Splint Mail|Shadow/Shadow/30/War Hammer/Banded Mail|Sea Troll/Aquatic/30/Steel Longsword/Plate Armor",
        // 1.3.3: the group size grows each floor from 25, which changes these draws
        ["30,30,4"] = "Dire Wolf/Beast/30/Steel Longsword/Banded Mail|Dire Wolf/Beast/30/War Hammer/Banded Mail|Dire Wolf/Beast/30/Halberd/Splint Mail|Dire Wolf/Beast/30/Greatsword/Banded Mail",
        // 1.3.3: the group size grows each floor from 25, which changes these draws
        ["30,30,5"] = "Phase Spider/Insectoid/30/Greatsword/Plate Armor|Phase Spider/Insectoid/30/Halberd/Plate Armor",
        // 1.3.3: the group size grows each floor from 25, which changes these draws
        ["30,30,6"] = "Sea Troll/Aquatic/30/Greatsword/Banded Mail|Dire Wolf/Beast/30/War Hammer/Banded Mail|Orc Warrior/Orcish/30/Halberd/Plate Armor",
        ["30,30,7"] = "Goblin Champion/Goblinoid/30/Halberd/Plate Armor|Goblin Champion/Goblinoid/30/Greatsword/Full Plate|Goblin Champion/Goblinoid/30/Greatsword/Splint Mail",
        ["30,30,8"] = "Drake/Draconic/30/Halberd/Splint Mail",
        ["61,61,1"] = "Treant/Fey/61/Mythril Axe/Mythril Armor|Treant/Fey/61/Enchanted Blade/Dragonscale Mail|Treant/Fey/61/Enchanted Blade/Mythril Armor",
        ["61,61,2"] = "Nightshade/Shadow/61/Mythril Axe/Elven Chain|Nightshade/Shadow/61/Enchanted Blade/Enchanted Plate|Nightshade/Shadow/61/Enchanted Blade/Enchanted Plate|Nightshade/Shadow/61/Mythril Axe/Mythril Armor",
        ["61,61,3"] = "Orc Chieftain/Orcish/61/Enchanted Blade/Mythril Armor|Nightshade/Shadow/61/Mythril Axe/Dragonscale Mail|Kraken Spawn/Aquatic/61/Enchanted Blade/Enchanted Plate|Seraph/Celestial/61/Dragonbone Mace/Elven Chain|Fire Lord/Elemental/61/Flaming Sword/Mythril Armor",
        ["61,61,4"] = "Alpha Werewolf/Beast/61/Enchanted Blade/Dragonscale Mail|Alpha Werewolf/Beast/61/Mythril Axe/Dragonscale Mail|Alpha Werewolf/Beast/61/Flaming Sword/Mythril Armor|Alpha Werewolf/Beast/61/Dragonbone Mace/Dragonscale Mail|Alpha Werewolf/Beast/61/Mythril Axe/Elven Chain",
        ["61,61,5"] = "Spider Queen/Insectoid/61/Dragonbone Mace/Enchanted Plate|Spider Queen/Insectoid/61/Flaming Sword/Enchanted Plate|Spider Queen/Insectoid/61/Mythril Axe/Enchanted Plate",
        ["61,61,6"] = "Kraken Spawn/Aquatic/61/Dragonbone Mace/Dragonscale Mail|Alpha Werewolf/Beast/61/Mythril Axe/Dragonscale Mail|Orc Chieftain/Orcish/61/Flaming Sword/Enchanted Plate|Treant/Fey/61/Flaming Sword/Elven Chain",
        ["61,61,7"] = "Goblin Warlord/Goblinoid/61/Flaming Sword/Enchanted Plate|Goblin Warlord/Goblinoid/61/Dragonbone Mace/Elven Chain|Goblin Warlord/Goblinoid/61/Dragonbone Mace/Mythril Armor|Goblin Warlord/Goblinoid/61/Dragonbone Mace/Mythril Armor|Goblin Warlord/Goblinoid/61/Mythril Axe/Elven Chain",
        ["61,61,8"] = "Young Dragon/Draconic/61/Flaming Sword/Mythril Armor|Young Dragon/Draconic/61/Dragonbone Mace/Enchanted Plate|Young Dragon/Draconic/61/Flaming Sword/Enchanted Plate",
        ["7,14,1"] = "Ooze/Aberration/7/Wooden Club/Tattered Robes",
        ["7,14,2"] = "Pixie/Fey/7/Wooden Club/None",
        ["7,14,3"] = "Zombie/Undead/7/Rusty Dagger/Tattered Robes|Pixie/Fey/7/Wooden Club/Hide Armor",
        ["7,14,4"] = "Kobold/Draconic/7/Rusty Dagger/Hide Armor|Kobold/Draconic/7/Wooden Club/Hide Armor",
        ["7,14,5"] = "Spark/Elemental/7/Crude Axe/Leather Armor",
        ["7,14,6"] = "Giant Spider/Insectoid/7/Crude Axe/Hide Armor|Wolf/Beast/7/Wooden Club/Hide Armor",
        ["7,14,7"] = "Goblin/Goblinoid/7/Short Sword/Leather Armor|Goblin/Goblinoid/7/Crude Axe/None",
        ["7,14,8"] = "Orc/Orcish/7/Short Sword/Tattered Robes",
    };

    private static readonly Dictionary<string, string> GoldenShallow = new()
    {
        ["7,7,1"] = "Ooze/Aberration/7/Wooden Club/Tattered Robes",
        ["7,7,2"] = "Pixie/Fey/7/Wooden Club/None",
        ["7,7,3"] = "Zombie/Undead/7/Rusty Dagger/Tattered Robes|Pixie/Fey/7/Wooden Club/Hide Armor",
        ["7,7,4"] = "Kobold/Draconic/7/Rusty Dagger/Hide Armor|Kobold/Draconic/7/Wooden Club/Hide Armor",
        ["7,7,5"] = "Spark/Elemental/7/Crude Axe/Leather Armor",
        ["7,7,6"] = "Giant Spider/Insectoid/7/Crude Axe/Hide Armor|Wolf/Beast/7/Wooden Club/Hide Armor",
        ["7,7,7"] = "Goblin/Goblinoid/7/Short Sword/Leather Armor|Goblin/Goblinoid/7/Crude Axe/None",
        ["7,7,8"] = "Orc/Orcish/7/Short Sword/Tattered Robes",
        ["5,5,1"] = "Ooze/Aberration/5/Wooden Club/Tattered Robes",
        ["5,5,2"] = "Pixie/Fey/5/Wooden Club/None",
        ["5,5,3"] = "Zombie/Undead/5/Rusty Dagger/Tattered Robes|Pixie/Fey/5/Wooden Club/Hide Armor",
        ["5,5,4"] = "Kobold/Draconic/5/Rusty Dagger/Hide Armor|Kobold/Draconic/5/Wooden Club/Hide Armor",
        ["5,5,5"] = "Spark/Elemental/5/Crude Axe/Leather Armor",
        ["5,5,6"] = "Giant Spider/Insectoid/5/Crude Axe/Hide Armor|Wolf/Beast/5/Wooden Club/Hide Armor",
        ["5,5,7"] = "Goblin/Goblinoid/5/Short Sword/Leather Armor|Goblin/Goblinoid/5/Crude Axe/None",
        ["5,5,8"] = "Orc/Orcish/5/Short Sword/Tattered Robes",
        ["1,6,1"] = "Wolf/Beast/1/Wooden Club/Tattered Robes",
        ["1,6,2"] = "Pixie/Fey/1/Wooden Club/None",
        ["1,6,3"] = "Goblin/Goblinoid/1/Rusty Dagger/Tattered Robes|Pixie/Fey/1/Wooden Club/Hide Armor",
        ["1,6,4"] = "Kobold/Draconic/1/Rusty Dagger/Hide Armor|Kobold/Draconic/1/Wooden Club/Hide Armor",
        ["1,6,5"] = "Wolf/Beast/1/Crude Axe/Leather Armor",
        ["1,6,6"] = "Pixie/Fey/1/Crude Axe/Hide Armor|Kobold/Draconic/1/Wooden Club/Hide Armor",
        ["1,6,7"] = "Goblin/Goblinoid/1/Short Sword/Leather Armor|Goblin/Goblinoid/1/Crude Axe/None",
        ["1,6,8"] = "Zombie/Undead/1/Short Sword/Tattered Robes",
        ["3,3,1"] = "Ooze/Aberration/3/Wooden Club/Tattered Robes",
        ["3,3,2"] = "Pixie/Fey/3/Wooden Club/None",
        ["3,3,3"] = "Zombie/Undead/3/Rusty Dagger/Tattered Robes|Pixie/Fey/3/Wooden Club/Hide Armor",
        ["3,3,4"] = "Kobold/Draconic/3/Rusty Dagger/Hide Armor|Kobold/Draconic/3/Wooden Club/Hide Armor",
        ["3,3,5"] = "Wolf/Beast/3/Crude Axe/Leather Armor",
        ["3,3,6"] = "Pixie/Fey/3/Crude Axe/Hide Armor|Kobold/Draconic/3/Wooden Club/Hide Armor",
        ["3,3,7"] = "Goblin/Goblinoid/3/Short Sword/Leather Armor|Goblin/Goblinoid/3/Crude Axe/None",
        ["3,3,8"] = "Zombie/Undead/3/Short Sword/Tattered Robes",
    };

    private sealed class RecordingRandom : Random
    {
        public readonly List<string> Calls = new();
        public RecordingRandom(int seed) : base(seed) { }
        public override int Next() { int v = base.Next(); Calls.Add($"Next()={v}"); return v; }
        public override int Next(int maxValue) { int v = base.Next(maxValue); Calls.Add($"Next({maxValue})={v}"); return v; }
        public override int Next(int minValue, int maxValue) { int v = base.Next(minValue, maxValue); Calls.Add($"Next({minValue},{maxValue})={v}"); return v; }
        public override double NextDouble() { double v = base.NextDouble(); Calls.Add($"NextDouble()={v:R}"); return v; }
    }

    private static T WithSwitch<T>(bool on, Func<T> body)
    {
        // elites (the piece after this one, EliteMonsters128Tests) are off here: these rows read the templates alone
        bool saved = GameConfig.EncounterTemplatesEnabled, savedElites = GameConfig.EliteMonstersEnabled;
        try { GameConfig.EncounterTemplatesEnabled = on; GameConfig.EliteMonstersEnabled = false; return body(); }
        finally { GameConfig.EncounterTemplatesEnabled = saved; GameConfig.EliteMonstersEnabled = savedElites; }
    }

    private static string Sig(IEnumerable<Monster> group) =>
        string.Join("|", group.Select(m => $"{m.Name}/{m.FamilyName}/{m.Level}/{m.Weapon}/{m.Armor}"));

    /// <summary>The dungeon's call: monster level, the real floor in the rules.</summary>
    private static List<Monster> Dungeon(int level, int floor, int seed) =>
        MonsterGenerator.GenerateMonsterGroup(level, new Random(seed), MonsterGenerator.OldGodApproachScale(floor), EncounterRules.Dungeon(floor));

    /// <summary>Every other caller's call: no rules.</summary>
    private static List<Monster> Plain(int level, int floor, int seed) =>
        MonsterGenerator.GenerateMonsterGroup(level, new Random(seed), MonsterGenerator.OldGodApproachScale(floor));

    private static bool IsChampion(List<Monster> g) => g.Count == 1 && g[0].IsMiniBoss;
    private static bool IsCaster(Monster m) => !m.IsMiniBoss && m.Name != m.TierName;

    private static List<List<Monster>> Floor7() =>
        WithSwitch(true, () => Enumerable.Range(0, Groups).Select(seed => Dungeon(7, 7, seed)).ToList());

    // ---------------------------------------------------------------- E1

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void E1_Floors1To3_KeepSizes1And2_WithTheGroupsOfBefore(int floor)
    {
        WithSwitch(true, () =>
        {
            int[] sizes = new int[6];
            for (int seed = 0; seed < Groups; seed++)
            {
                var g = Dungeon(floor, floor, seed);
                g.Count.Should().BeInRange(1, 2, "floor {0} seed {1}", floor, seed);
                Sig(g).Should().Be(Sig(Plain(floor, floor, seed)), "floor {0} seed {1} is not templated", floor, seed);
                g.Should().NotContain(m => IsCaster(m));
                sizes[g.Count]++;
            }
            sizes[2].Should().BeInRange(Groups * 40 / 100, Groups * 50 / 100, "0.9 x 1/2 of the groups are pairs, as before");
            return 0;
        });
        if (floor == 3)
            foreach (int seed in Enumerable.Range(1, 8))
                WithSwitch(true, () => Sig(Dungeon(3, 3, seed))).Should().Be(GoldenShallow[$"3,3,{seed}"]);
    }

    // ---------------------------------------------------------------- E2

    [Fact]
    public void E2_Floors4To10_SizeThreeAboutOneInFive_NeverAboveThree()
    {
        var regular = Floor7().Where(g => !IsChampion(g)).ToList();
        regular.Count.Should().BeInRange(Groups * 88 / 100, Groups * 92 / 100, "one encounter in ten is a single champion, as before");
        regular.Max(g => g.Count).Should().Be(3);
        double Share(int n) => regular.Count(g => g.Count == n) / (double)regular.Count;
        Share(3).Should().BeInRange(0.18, 0.22);
        Share(1).Should().BeInRange(0.33, 0.37);
        Share(2).Should().BeInRange(0.43, 0.47);

        MonsterGenerator.TemplateGroupSize(0.0).Should().Be(1);
        MonsterGenerator.TemplateGroupSize(0.3499).Should().Be(1);
        MonsterGenerator.TemplateGroupSize(0.35).Should().Be(2);
        MonsterGenerator.TemplateGroupSize(0.7999).Should().Be(2);
        MonsterGenerator.TemplateGroupSize(0.80).Should().Be(3);
        MonsterGenerator.TemplateGroupSize(0.999999).Should().Be(3);

        foreach (int floor in new[] { 4, 5, 6, 8, 9, 10 })
        {
            var groups = WithSwitch(true, () => Enumerable.Range(0, 2000).Select(seed => Dungeon(floor, floor, seed)).ToList());
            groups.Max(g => g.Count).Should().Be(3, "floor {0}", floor);
        }
    }

    // ---------------------------------------------------------------- E3

    [Fact]
    public void E3_AGroupOfThree_IsBudgeted()
    {
        // the unbudgeted stats of each tier at level 7, from the generator without rules
        var basis = new Dictionary<string, Monster>();
        for (int seed = 0; seed < 3000; seed++)
            foreach (var m in Plain(7, 7, seed))
                if (!m.IsMiniBoss) basis.TryAdd(m.TierName, m);

        int budgeted = 0, plain = 0;
        foreach (var g in Floor7().Where(g => !IsChampion(g)))
            foreach (var m in g)
            {
                var b = basis[m.TierName];
                bool three = g.Count == 3;
                long hp = three ? Math.Max(1, (long)(b.MaxHP * 0.8)) : b.MaxHP;
                long strength = three ? (long)(b.Strength * 0.9) : b.Strength;
                if (IsCaster(m)) strength = (long)(strength * 0.85);
                m.MaxHP.Should().Be(hp, "{0} in a group of {1}", m.Name, g.Count);
                m.HP.Should().Be(hp);
                m.Strength.Should().Be(strength, "{0} in a group of {1}", m.Name, g.Count);
                m.Punch.Should().Be(three ? (long)(b.Punch * 0.9) : b.Punch);
                m.WeapPow.Should().Be(three ? (long)(b.WeapPow * 0.9) : b.WeapPow);
                m.Defence.Should().Be(b.Defence, "defence is not budgeted");
                m.ArmPow.Should().Be(b.ArmPow);
                if (three) budgeted++; else plain++;
            }
        budgeted.Should().BeGreaterThan(4000);
        plain.Should().BeGreaterThan(4000);
        var wolf = basis["Wolf"];
        ((long)(wolf.MaxHP * 0.8)).Should().BeLessThan(wolf.MaxHP);
        ((long)(wolf.Strength * 0.9)).Should().BeLessThan(wolf.Strength);
    }

    // ---------------------------------------------------------------- E4

    [Fact]
    public void E4_SameFamilyHalfTheTime_OnFloors4To10()
    {
        // A mixed group can come out as one tier by coincidence. A family with a spell then still gets a
        // caster, so only the families without one hide a mixed group; their share is read from the
        // generator itself.
        var share = new Dictionary<string, double>();
        const int Draws = 20_000;
        var rng = new Random(128);
        for (int i = 0; i < Draws; i++)
        {
            var m = MonsterGenerator.GenerateMonster(7, random: rng);
            share[m.TierName] = share.GetValueOrDefault(m.TierName) + 1.0 / Draws;
        }
        var silent = MonsterFamilies.AllFamilies.Where(f => !MonsterGenerator.CasterSpells.ContainsKey(f.FamilyName))
            .SelectMany(f => f.Tiers.Select(t => t.Name)).ToHashSet();

        var groups = Floor7().Where(g => g.Count >= 2).ToList();
        groups.Count.Should().BeGreaterThan(5000);
        double looksSame = 0, hidden = 0;
        foreach (var g in groups)
        {
            if (g.All(m => m.TierName == g[0].TierName) && !g.Any(IsCaster)) looksSame++;
            hidden += share.Where(kv => silent.Contains(kv.Key)).Sum(kv => Math.Pow(kv.Value, g.Count));
        }
        looksSame /= groups.Count;
        hidden /= groups.Count;   // the chance that a mixed group of that size looks like one family
        double sameFamily = (looksSame - hidden) / (1 - hidden);
        sameFamily.Should().BeInRange(0.47, 0.53, "looks the same {0:F4}, hidden mixed {1:F4}", looksSame, hidden);
    }

    // ---------------------------------------------------------------- E5

    [Fact]
    public void E5_AtMostOneCaster_AndItsSpellIsASpell()
    {
        var all = Floor7();
        foreach (int floor in new[] { 4, 5, 6, 8, 9, 10 })
            all.AddRange(WithSwitch(true, () => Enumerable.Range(0, 2000).Select(seed => Dungeon(floor, floor, seed)).ToList()));

        int casters = 0, sureMixedWithSpellFamily = 0;
        foreach (var g in all)
        {
            var c = g.Where(IsCaster).ToList();
            c.Count.Should().BeLessThanOrEqualTo(1, Sig(g));
            if (g.Count == 1) c.Should().BeEmpty("a lone monster is never a caster");
            if (g.Count >= 2 && !IsChampion(g) && g.Select(m => m.FamilyName).Distinct().Count() >= 2
                && g.Any(m => MonsterGenerator.CasterSpells.ContainsKey(m.FamilyName)))
            {
                sureMixedWithSpellFamily++;
                c.Count.Should().Be(1, "a mixed group with a spell family has its caster: {0}", Sig(g));
            }
            foreach (var m in c)
            {
                casters++;
                MonsterGenerator.CasterSpells.Should().ContainKey(m.FamilyName);
                string spell = MonsterGenerator.CasterSpells[m.FamilyName];
                m.SpecialAbilities.Count(a => a == spell).Should().Be(1);
                Enum.TryParse<MonsterAbilities.AbilityType>(spell, out var type).Should().BeTrue(spell);
                CombatEngine.MonsterSpellAbilities.Should().Contain(type);
                CombatEngine.MonsterAbilityEventKind(type).Should().Be(CombatEventKind.Spell);
                m.Name.Should().Be(m.TierName + " Shaman");
            }
            foreach (var m in g.Where(m => !IsCaster(m) && !m.IsMiniBoss))
                m.SpecialAbilities.Should().NotContain(a => a == "Fireball" || a == "Spellcasting" || a == "Hellfire",
                    "only the caster holds a spell on these floors: {0}", Sig(g));
        }
        casters.Should().BeGreaterThan(1000);
        sureMixedWithSpellFamily.Should().BeGreaterThan(1000);

        // the design's families, and no other
        MonsterGenerator.CasterSpells.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["Draconic"] = "Fireball", ["Elemental"] = "Fireball", ["Undead"] = "Spellcasting", ["Goblinoid"] = "Spellcasting", ["Demonic"] = "Hellfire",
        });
        all.SelectMany(g => g).Where(IsCaster).Select(m => m.FamilyName).Distinct()
            .Should().BeSubsetOf(new[] { "Draconic", "Elemental", "Undead", "Goblinoid", "Demonic" })
            .And.NotContain(new[] { "Fey", "Aberration" });
    }

    [Fact]
    public void E5_MonsterTactics_PicksTheCastersSpell()
    {
        var caster = Floor7().SelectMany(g => g).First(m => IsCaster(m) && m.TierName == "Kobold");
        var hero = new Character { Name1 = "hero", Name2 = "Hero", Class = CharacterClass.Warrior, Level = 8, HP = 500, MaxHP = 500 };
        var rng = new Random(5);
        var picks = Enumerable.Range(0, 400).Select(_ => MonsterTactics.Choose(caster, hero, 1, null, rng)).ToList();
        picks.Should().Contain("Fireball");
        picks.Should().Contain("Poison", "the Kobold keeps its signature beside the spell");
        MonsterTactics.Wasted("Fireball", caster, hero).Should().BeFalse();
    }

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task E5_ASeededFight_RecordsTheCastersSpellAsSpellDamage(bool tactics)
    {
        // a generated mixed group of floor 7 with its caster, made sturdy so the fight lasts
        var group = Floor7().First(g => g.Count == 2 && g.Any(IsCaster));
        var caster = group.Single(IsCaster);
        foreach (var m in group) { m.HP = m.MaxHP = 5000; m.Defence = 5; }
        foreach (var m in group.Where(m => m != caster)) m.SpecialAbilities.Clear();
        var hero = new Character
        {
            Name1 = "Tester", Name2 = "Tester", AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 8,
            HP = 60_000, MaxHP = 60_000, BaseMaxHP = 60_000, Strength = 60, BaseStrength = 60, Defence = 20, BaseDefence = 20,
            Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20, Constitution = 30, BaseConstitution = 30,
            Intelligence = 20, BaseIntelligence = 20, Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
            Mental = 80, Gold = 0, Healing = 0, AutoHeal = false, CombatSpeed = CombatSpeed.Instant,
        };
        string script = string.Concat(Enumerable.Repeat("A\n\n", 60)) + string.Concat(Enumerable.Repeat("P\n", 10));
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(script), output));
        engine.SeedRandomForTests(128);
        var seen = new Recorder();
        engine.Observer = seen;
        bool saved = GameConfig.MonsterTacticsEnabled;
        CombatResult? result = null;
        try
        {
            GameConfig.MonsterTacticsEnabled = tactics;
            var task = engine.PlayerVsMonsters(hero, group, offerMonkEncounter: false);
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (done == task) result = await task;
        }
        catch (Exception) { /* the script ran out with both monsters standing: the fight was long enough */ }
        finally { GameConfig.MonsterTacticsEnabled = saved; }
        string transcript = Encoding.UTF8.GetString(output.ToArray());
        long Taken(CombatEventKind kind) => seen.Events
            .Where(e => e.Actor == CombatSide.Monster && e.Target == CombatSide.Player && e.Kind == kind).Sum(e => e.Amount);
        seen.Events.Should().NotBeEmpty(transcript);
        Taken(CombatEventKind.Spell).Should().BeGreaterThan(0, "the caster's {0} is recorded as spell damage", MonsterGenerator.CasterSpells[caster.FamilyName]);
        if (result != null) result.Tally.DmgToPlayerSpell.Should().Be(Taken(CombatEventKind.Spell));
        if (!tactics) Taken(CombatEventKind.Ability).Should().Be(0, "with tactics off the spell is the only ability in this fight");
    }

    private sealed class Recorder : ICombatObserver
    {
        public readonly List<CombatEvent> Events = new();
        public void OnFightStart(CombatFightStart start) { }
        public void OnEvent(CombatEvent e) => Events.Add(e);
        public void OnFightEnd(long playerHpEnd) { }
    }

    // ---------------------------------------------------------------- E6

    [Fact]
    public void E6_Floors11AndDeeper_DrawExactlyAsBefore_StragglersIncluded()
    {
        WithSwitch(true, () =>
        {
            foreach (var (key, golden) in GoldenDeep)
            {
                var p = key.Split(',').Select(int.Parse).ToArray();
                Sig(Dungeon(p[0], p[1], p[2])).Should().Be(golden, "level {0}, real floor {1}, seed {2}", p[0], p[1], p[2]);
            }
            // every floor from 11, and every straggler level a floor from 11 can ask for, against the call without rules
            for (int floor = 11; floor <= 100; floor++)
                for (int seed = 0; seed < 40; seed++)
                {
                    var a = new RecordingRandom(seed); var b = new RecordingRandom(seed);
                    var withRules = MonsterGenerator.GenerateMonsterGroup(floor, a, MonsterGenerator.OldGodApproachScale(floor), EncounterRules.Dungeon(floor));
                    var without = MonsterGenerator.GenerateMonsterGroup(floor, b, MonsterGenerator.OldGodApproachScale(floor));
                    Sig(withRules).Should().Be(Sig(without));
                    a.Calls.Should().Equal(b.Calls);
                }
            for (int floor = 11; floor <= 18; floor++)
                for (int level = Math.Max(1, floor - 8); level <= floor - 5; level++)
                    for (int seed = 0; seed < 300; seed++)
                    {
                        var a = new RecordingRandom(seed); var b = new RecordingRandom(seed);
                        var straggler = MonsterGenerator.GenerateMonsterGroup(level, a, MonsterGenerator.OldGodApproachScale(floor), EncounterRules.Dungeon(floor));
                        var without = MonsterGenerator.GenerateMonsterGroup(level, b, MonsterGenerator.OldGodApproachScale(floor));
                        Sig(straggler).Should().Be(Sig(without), "floor {0} straggler at level {1}, seed {2}", floor, level, seed);
                        a.Calls.Should().Equal(b.Calls);
                        straggler.Count.Should().BeLessThanOrEqualTo(level <= 10 ? 2 : 3);
                        straggler.Should().NotContain(m => IsCaster(m));
                    }
            return 0;
        });
    }

    [Fact]
    public void E6_TheBandIsTheRealFloor_AStragglerOnFloor6To10IsTemplated()
    {
        // floor 8, straggler at level 2: the floor is in the band, so the templates apply to level 2 monsters
        var groups = WithSwitch(true, () => Enumerable.Range(0, 3000).Select(seed => Dungeon(2, 8, seed)).ToList());
        groups.Max(g => g.Count).Should().Be(3);
        groups.SelectMany(g => g).Should().OnlyContain(m => m.Level == 2);
        groups.SelectMany(g => g).Should().Contain(m => IsCaster(m));
        MonsterGenerator.TemplatesApply(EncounterRules.Dungeon(3)).Should().BeFalse();
        MonsterGenerator.TemplatesApply(EncounterRules.Dungeon(4)).Should().BeTrue();
        MonsterGenerator.TemplatesApply(EncounterRules.Dungeon(10)).Should().BeTrue();
        MonsterGenerator.TemplatesApply(EncounterRules.Dungeon(11)).Should().BeFalse();
        MonsterGenerator.TemplatesApply(null).Should().BeFalse();
    }

    // ---------------------------------------------------------------- E7

    [Fact]
    public void E7_CallersOutsideTheDungeon_AreUnchanged()
    {
        WithSwitch(true, () =>
        {
            foreach (var (key, golden) in GoldenShallow.Concat(GoldenDeep))
            {
                var p = key.Split(',').Select(int.Parse).ToArray();
                Sig(Plain(p[0], p[1], p[2])).Should().Be(golden, "no rules: level {0}, seed {2}", p[0], p[1], p[2]);
            }
            // the calls WorldSimulator and RareEncounters make: a level and nothing else
            for (int level = 1; level <= 12; level++)
                for (int seed = 0; seed < 2000; seed++)
                {
                    var g = MonsterGenerator.GenerateMonsterGroup(level, new Random(seed));
                    g.Count.Should().BeLessThanOrEqualTo(level <= 10 ? 2 : 3, "level {0} seed {1}", level, seed);
                    g.Should().NotContain(m => IsCaster(m));
                }
            return 0;
        });
    }

    [Fact]
    public void E7_OnlyTheTwoDungeonCallSites_PassTheRules_WithTheRealFloor()
    {
        string root = Leftovers1114BTests.RepoRoot();
        var users = Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("EncounterRules.Dungeon(")).Select(Path.GetFileName).ToList();
        users.Should().BeEquivalentTo(new[] { "DungeonLocation.cs" });
        var lines = File.ReadAllLines(Path.Combine(root, "Scripts", "Locations", "DungeonLocation.cs"))
            .Where(l => l.Contains("MonsterGenerator.GenerateMonsterGroup(") && !l.TrimStart().StartsWith("//")).ToList();
        lines.Should().HaveCount(2);
        lines.Should().ContainSingle(l => l.Contains("GenerateMonsterGroup(effectiveMonsterLevel,")
            && l.Contains("RoomEncounterRules(currentDungeonLevel, room.IsBossRoom)"), "the room fight keeps its straggler level and passes the real floor");
        lines.Should().ContainSingle(l => l.Contains("GenerateMonsterGroup(currentDungeonLevel,") && l.Contains("EncounterRules.Dungeon(currentDungeonLevel)"),
            "MonsterEncounter is the exploration fight: it has no room and so no boss room");
    }

    // ---------------------------------------------------------------- E9

    /// <summary>The room fight's call (DungeonLocation.RoomEncounterRules decides the rules).</summary>
    private static List<Monster> Room(int level, int floor, bool bossRoom, Random random) =>
        MonsterGenerator.GenerateMonsterGroup(level, random, MonsterGenerator.OldGodApproachScale(floor), DungeonLocation.RoomEncounterRules(floor, bossRoom));

    [Fact]
    public void E9_ABossRoom_PassesNoRules_AndIsDrawnAsBefore_ARegularRoomKeepsTheTemplate()
    {
        WithSwitch(true, () =>
        {
            for (int floor = 1; floor <= 100; floor++)
                DungeonLocation.RoomEncounterRules(floor, bossRoom: true).Should().BeNull("floor {0}", floor);
            DungeonLocation.RoomEncounterRules(7, bossRoom: false)!.RealFloor.Should().Be(7);

            // the golden lists of the code before this piece (0c8b24a5), floors 5 and 7
            foreach (int floor in new[] { 5, 7 })
                foreach (int seed in Enumerable.Range(1, 8))
                    Sig(Room(floor, floor, true, new Random(seed))).Should().Be(GoldenShallow[$"{floor},{floor},{seed}"], "boss room, floor {0}, seed {1}", floor, seed);

            for (int floor = 4; floor <= 10; floor++)
            {
                int threes = 0, casters = 0, differ = 0;
                for (int seed = 0; seed < 2000; seed++)
                {
                    var a = new RecordingRandom(seed); var b = new RecordingRandom(seed);
                    var boss = Room(floor, floor, true, a);
                    var before = MonsterGenerator.GenerateMonsterGroup(floor, b, MonsterGenerator.OldGodApproachScale(floor));
                    Sig(boss).Should().Be(Sig(before), "boss room, floor {0}, seed {1}", floor, seed);
                    a.Calls.Should().Equal(b.Calls, "boss room, floor {0}, seed {1}: the same draws in the same order", floor, seed);
                    boss.Select(m => (m.MaxHP, m.Strength, m.Punch, m.WeapPow)).Should().Equal(before.Select(m => (m.MaxHP, m.Strength, m.Punch, m.WeapPow)));
                    boss.Count.Should().BeLessThanOrEqualTo(2);
                    boss.Should().NotContain(m => IsCaster(m));

                    var regular = Room(floor, floor, false, new Random(seed));
                    Sig(regular).Should().Be(Sig(Dungeon(floor, floor, seed)));
                    if (regular.Count == 3) threes++;
                    casters += regular.Count(IsCaster);
                    if (Sig(regular) != Sig(before)) differ++;
                }
                threes.Should().BeInRange(300, 420, "a regular room on floor {0} still gets the template (18% threes)", floor);
                casters.Should().BeGreaterThan(200);
                differ.Should().BeGreaterThan(800);
            }
            return 0;
        });
    }

    // ---------------------------------------------------------------- E8

    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    [Fact]
    public void E8_ACastersName_IsStoredEnglish_AndShownPerLanguage()
    {
        string saved = GameConfig.Language;
        try
        {
            GameConfig.Language = "es";   // the generating player reads Spanish
            var kobold = Floor7().SelectMany(g => g).First(m => IsCaster(m) && m.TierName == "Kobold");
            kobold.Name.Should().Be("Kobold Shaman");
            kobold.TierName.Should().Be("Kobold");
            MonsterNames.DisplayIn("en", kobold).Should().Be("Kobold Shaman");
            MonsterNames.DisplayIn("es", kobold).Should().Be(Loc.GetIn("es", "monster.role.caster", Loc.GetIn("es", "monster.name.kobold")));
            MonsterNames.DisplayIn("es", kobold).Should().Be("Chamán Kobold");
            MonsterNames.DisplayIn("fr", kobold).Should().Be("Chaman Kobold");
            MonsterNames.DisplayIn("hu", kobold).Should().Be("Kobold Sámán");
            MonsterNames.DisplayIn("it", kobold).Should().Be("Sciamano Coboldo");
            MonsterNames.DisplayIn("it", kobold.Name).Should().Be("Sciamano Coboldo", "the stored name alone is enough (a follower's broadcast)");
            MonsterNames.Display(kobold).Should().Be("Chamán Kobold");

            foreach (var caster in Floor7().SelectMany(g => g).Where(IsCaster))
            {
                caster.Name.Should().Be(Loc.GetIn("en", "monster.role.caster", caster.TierName));
                foreach (string lang in Langs)
                    MonsterNames.DisplayIn(lang, caster).Should().Be(
                        Loc.GetIn(lang, "monster.role.caster", MonsterNames.DisplayIn(lang, caster.TierName)), "{0} in {1}", caster.Name, lang);
            }
        }
        finally { GameConfig.Language = saved; }
    }

    [Fact]
    public void E8_TheKey_IsInFiveLanguages_TranslatedAndWithItsArgument()
    {
        string en = Loc.GetIn("en", "monster.role.caster");
        en.Should().Be("{0} Shaman");
        foreach (string lang in Langs)
        {
            Loc.HasIn(lang, "monster.role.caster").Should().BeTrue(lang);
            string text = Loc.GetIn(lang, "monster.role.caster");
            text.Should().Contain("{0}", lang);
            text.Should().NotMatchRegex(@"[\u2013\u2014]", lang);
            if (lang != "en") text.Should().NotBe(en, "{0} is translated", lang);
        }
    }

    /// <summary>The tier names a caster can carry on floors 4 to 10: a spell family's tier that starts at
    /// level 10 or lower (a straggler asks for levels 1 to 5).</summary>
    private static List<string> CasterTiers() => MonsterFamilies.AllFamilies
        .Where(f => MonsterGenerator.CasterSpells.ContainsKey(f.FamilyName))
        .SelectMany(f => f.Tiers.Where(t => t.MinLevel <= 10).Select(t => t.Name)).ToList();

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void E8_EveryRowACasterAppearsIn_Fits79Columns(string lang)
    {
        var casterTiers = CasterTiers();
        casterTiers.Should().Contain(new[] { "Goblin", "Zombie", "Kobold", "Spark", "Imp" });
        var others = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Where(t => t.MinLevel <= 10).Select(t => t.Name)).ToList();
        string Shown(string tier) => MonsterNames.DisplayIn(lang, tier);
        string Caster(string tier) => MonsterNames.DisplayIn(lang, MonsterNames.FromKey("monster.role.caster", tier));

        int longest = 0;
        foreach (string c in casterTiers)
        {
            // the mixed encounter line of DungeonLocation: three names, one of each
            foreach (string a in others)
                foreach (string b in others)
                {
                    string line = Loc.GetIn(lang, "dungeon.you_face_prefix") + $"1 {Shown(a)}, 1 {Caster(c)}" + Loc.GetIn(lang, "dungeon.and_separator") + $"1 {Shown(b)}!";
                    longest = Math.Max(longest, line.Length);
                    line.Length.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, line);
                }
            // the spell lines the caster speaks
            foreach (string row in new[]
            {
                Loc.GetIn(lang, "mability.fireball", Caster(c)), Loc.GetIn(lang, "mability.spellcasting", Caster(c)),
                Loc.GetIn(lang, "mability.hellfire.you", Caster(c)), Loc.GetIn(lang, "mability.hellfire.ally", Caster(c), "Aldric the Bold"),
            })
                row.Length.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row);
        }
        longest.Should().BeGreaterThan(40);
    }

    // ---------------------------------------------------------------- the switch

    [Fact]
    public void SwitchOff_TheDungeonDrawsWhatItDrewBefore_OnEveryFloor()
    {
        WithSwitch(false, () =>
        {
            foreach (var (key, golden) in GoldenShallow.Concat(GoldenDeep))
            {
                var p = key.Split(',').Select(int.Parse).ToArray();
                Sig(Dungeon(p[0], p[1], p[2])).Should().Be(golden, "switch off: level {0}, real floor {1}, seed {2}", p[0], p[1], p[2]);
            }
            for (int floor = 1; floor <= 12; floor++)
                for (int seed = 0; seed < 1000; seed++)
                {
                    var a = new RecordingRandom(seed); var b = new RecordingRandom(seed);
                    var off = MonsterGenerator.GenerateMonsterGroup(floor, a, 1.0, EncounterRules.Dungeon(floor));
                    var plain = MonsterGenerator.GenerateMonsterGroup(floor, b, 1.0);
                    Sig(off).Should().Be(Sig(plain));
                    a.Calls.Should().Equal(b.Calls, "floor {0} seed {1}", floor, seed);
                    off.Select(m => (m.MaxHP, m.Strength, m.Punch, m.WeapPow)).Should().Equal(plain.Select(m => (m.MaxHP, m.Strength, m.Punch, m.WeapPow)));
                }
            MonsterGenerator.TemplatesApply(EncounterRules.Dungeon(7)).Should().BeFalse();
            return 0;
        });
        WithSwitch(true, () => MonsterGenerator.TemplatesApply(EncounterRules.Dungeon(7))).Should().BeTrue();
    }

    [Fact]
    public void TheSwitch_IsOnByDefault_AndNotASysopSetting()
    {
        var p = typeof(GameConfig).GetProperty(nameof(GameConfig.EncounterTemplatesEnabled))!;
        GameConfig.EncounterTemplatesEnabled.Should().BeTrue();
        p.SetMethod!.IsAssembly.Should().BeTrue("the setter is internal");
        string root = Leftovers1114BTests.RepoRoot();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name is "GameConfig.cs" or "MonsterGenerator.cs") continue;
            File.ReadAllText(file).Should().NotContain("EncounterTemplatesEnabled", $"{name} must not read or set the switch");
        }
        foreach (var file in Directory.GetFiles(root, "balance*.json", SearchOption.TopDirectoryOnly))
            File.ReadAllText(file).Should().NotContain("EncounterTemplates");
    }
}
