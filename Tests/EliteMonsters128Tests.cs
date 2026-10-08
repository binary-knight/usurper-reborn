using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 difficulty pass, DESIGN item 3, the core (rows F1 to F5 and F11 to F14): a regular dungeon encounter
/// can promote one member to an elite after the group is built, behind GameConfig.EliteMonstersEnabled and
/// only for a caller that passes EncounterRules.Dungeon. What an affix does is not built here (rows F6 to
/// F10 and Warded); the affix is drawn, stored and named.
/// </summary>
[Collection("SharedGameSingletons")]
public class EliteMonsters128Tests
{
    private const int Groups = 10_000;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] Affixes = { "frenzied", "warded", "venomous", "commanding", "mending", "swift" };

    private sealed class RecordingRandom : Random
    {
        public readonly List<string> Calls = new();
        public RecordingRandom(int seed) : base(seed) { }
        public override int Next() { int v = base.Next(); Calls.Add($"Next()={v}"); return v; }
        public override int Next(int maxValue) { int v = base.Next(maxValue); Calls.Add($"Next({maxValue})={v}"); return v; }
        public override int Next(int minValue, int maxValue) { int v = base.Next(minValue, maxValue); Calls.Add($"Next({minValue},{maxValue})={v}"); return v; }
        public override double NextDouble() { double v = base.NextDouble(); Calls.Add($"NextDouble()={v:R}"); return v; }
    }

    /// <summary>Answers Next(max) from a list (the member, then the affixes of PromoteElite).</summary>
    private sealed class FixedRandom : Random
    {
        private readonly Queue<int> _picks;
        public FixedRandom(params int[] picks) { _picks = new Queue<int>(picks); }
        public override int Next(int maxValue) { int v = _picks.Dequeue(); v.Should().BeLessThan(maxValue); return v; }
    }

    private static T WithElites<T>(bool on, Func<T> body)
    {
        bool saved = GameConfig.EliteMonstersEnabled;
        try { GameConfig.EliteMonstersEnabled = on; return body(); }
        finally { GameConfig.EliteMonstersEnabled = saved; }
    }

    /// <summary>The dungeon's call with elites on or off: monster level, the real floor in the rules.</summary>
    private static List<Monster> Dungeon(bool elites, int level, int floor, Random rng) =>
        WithElites(elites, () => MonsterGenerator.GenerateMonsterGroup(level, rng, MonsterGenerator.OldGodApproachScale(floor), EncounterRules.Dungeon(floor)));

    /// <summary>The champion encounter is the first draw of the call (one in ten).</summary>
    private static bool ChampionSeed(int seed) => new Random(seed).NextDouble() < 0.10;

    private static string Sig(Monster m) => $"{m.TierName}/{m.FamilyName}/{m.Level}/{m.Weapon}/{m.Armor}";
    private static (long, long, long, long, long, int, long) Stats(Monster m) => (m.MaxHP, m.HP, m.Strength, m.Punch, m.WeapPow, m.Defence, m.ArmPow);

    /// <summary>One seed on one floor: the group with elites off (the group before promotion: every draw before
    /// the elite roll is the same), the group with elites on, and both draw lists.</summary>
    private sealed record Pair(int Seed, List<Monster> Off, List<Monster> On, List<string> OffCalls, List<string> OnCalls)
    {
        public Monster? Elite => On.FirstOrDefault(m => m.IsElite);
        /// <summary>The promoted member's place in the group before promotion: the draw after the elite roll.</summary>
        public int Slot => int.Parse(OnCalls[OffCalls.Count + 1].Split('=')[1]);
    }

    private static Pair Draw(int level, int floor, int seed)
    {
        var a = new RecordingRandom(seed); var b = new RecordingRandom(seed);
        var off = Dungeon(false, level, floor, a);
        var on = Dungeon(true, level, floor, b);
        return new Pair(seed, off, on, a.Calls, b.Calls);
    }

    private static List<Pair> Floor(int floor, int count = Groups) => Enumerable.Range(0, count).Select(seed => Draw(floor, floor, seed)).ToList();

    // ---------------------------------------------------------------- F1

    [Theory]
    [InlineData(1, 0.0, 0.0)]
    [InlineData(2, 0.0, 0.0)]
    [InlineData(3, 0.07, 0.09)]
    [InlineData(7, 0.07, 0.09)]
    [InlineData(10, 0.07, 0.09)]
    [InlineData(11, 0.09, 0.11)]
    [InlineData(30, 0.09, 0.11)]
    [InlineData(31, 0.11, 0.13)]
    [InlineData(60, 0.11, 0.13)]
    [InlineData(100, 0.11, 0.13)]
    public void F1_EliteChance_PerFloorBand(int floor, double low, double high)
    {
        int regular = 0, elite = 0;
        for (int seed = 0; seed < Groups; seed++)
        {
            var group = Dungeon(true, floor, floor, new Random(seed));
            group.Count(m => m.IsElite).Should().BeLessThanOrEqualTo(1, "floor {0} seed {1}", floor, seed);
            if (ChampionSeed(seed)) continue;
            regular++;
            if (group.Any(m => m.IsElite)) elite++;
        }
        regular.Should().BeInRange(Groups * 88 / 100, Groups * 92 / 100);
        // the chance is per encounter that is not a champion's
        (elite / (double)regular).Should().BeInRange(low, high, "floor {0}: {1} of {2}", floor, elite, regular);
    }

    [Fact]
    public void F1_TheChanceTable_IsTheDesigns()
    {
        MonsterGenerator.EliteChance(1).Should().Be(0.0);
        MonsterGenerator.EliteChance(2).Should().Be(0.0);
        MonsterGenerator.EliteChance(3).Should().Be(0.08);
        MonsterGenerator.EliteChance(10).Should().Be(0.08);
        MonsterGenerator.EliteChance(11).Should().Be(0.10);
        MonsterGenerator.EliteChance(30).Should().Be(0.10);
        MonsterGenerator.EliteChance(31).Should().Be(0.12);
        MonsterGenerator.EliteChance(100).Should().Be(0.12);
    }

    [Fact]
    public void F1_TheBand_IsReadFromTheRealFloor_NotTheMonsterLevel()
    {
        // a straggler room: level 1 monsters on floor 6 (the level alone would give no elite), level 7 on floor 14
        foreach (var (level, floor, low, high) in new[] { (1, 6, 0.07, 0.09), (7, 14, 0.09, 0.11), (25, 33, 0.11, 0.13) })
        {
            int regular = 0, elite = 0;
            for (int seed = 0; seed < Groups; seed++)
            {
                var group = Dungeon(true, level, floor, new Random(seed));
                if (ChampionSeed(seed)) continue;
                regular++;
                if (group.Any(m => m.IsElite)) elite++;
                group.Should().OnlyContain(m => m.Level == level);
            }
            (elite / (double)regular).Should().BeInRange(low, high, "level {0} on floor {1}", level, floor);
        }
    }

    // ---------------------------------------------------------------- F2

    [Fact]
    public void F2_ABossRoom_PassesNoRules_AndHoldsNoElite()
    {
        foreach (int floor in new[] { 3, 5, 7, 10, 25, 60, 100 })
        {
            DungeonLocation.RoomEncounterRules(floor, bossRoom: true).Should().BeNull("floor {0}", floor);
            DungeonLocation.RoomEncounterRules(floor, bossRoom: false)!.RealFloor.Should().Be(floor);
            for (int seed = 0; seed < 2000; seed++)
            {
                var a = new RecordingRandom(seed); var b = new RecordingRandom(seed);
                var boss = WithElites(true, () => MonsterGenerator.GenerateMonsterGroup(floor, a, MonsterGenerator.OldGodApproachScale(floor), DungeonLocation.RoomEncounterRules(floor, bossRoom: true)));
                var plain = WithElites(false, () => MonsterGenerator.GenerateMonsterGroup(floor, b, MonsterGenerator.OldGodApproachScale(floor)));
                boss.Should().NotContain(m => m.IsElite, "boss room, floor {0} seed {1}", floor, seed);
                boss.Should().OnlyContain(m => m.EliteAffixes.Count == 0);
                boss.Select(Sig).Should().Equal(plain.Select(Sig));
                boss.Select(m => m.Name).Should().Equal(plain.Select(m => m.Name));
                boss.Select(Stats).Should().Equal(plain.Select(Stats));
                a.Calls.Should().Equal(b.Calls, "boss room, floor {0} seed {1}: no extra draw", floor, seed);
            }
        }
    }

    [Fact]
    public void F2_AChampionEncounter_HoldsNoElite_AndDrawsNothingMore()
    {
        foreach (int floor in new[] { 3, 7, 20, 60, 100 })
        {
            int champions = 0;
            foreach (var p in Floor(floor))
            {
                if (!ChampionSeed(p.Seed)) continue;
                champions++;
                p.On.Should().ContainSingle();
                p.On[0].IsMiniBoss.Should().BeTrue();
                p.On[0].IsElite.Should().BeFalse("champion, floor {0} seed {1}", floor, p.Seed);
                p.On[0].EliteAffixes.Should().BeEmpty();
                p.On[0].Name.Should().Be(p.Off[0].Name);
                Stats(p.On[0]).Should().Be(Stats(p.Off[0]));
                p.OnCalls.Should().Equal(p.OffCalls, "champion, floor {0} seed {1}: no elite roll", floor, p.Seed);
            }
            champions.Should().BeInRange(Groups * 8 / 100, Groups * 12 / 100);
        }
    }

    // ---------------------------------------------------------------- F3

    [Fact]
    public void F3_TheEliteTakesAMembersPlace_AndAGroupOfThreeOrMoreLosesOneMore()
    {
        var seen = new HashSet<(int Before, int After)>();
        foreach (int floor in new[] { 7, 20, 45, 80 })
        {
            foreach (var p in Floor(floor))
            {
                if (p.Elite == null)
                {
                    p.On.Select(Sig).Should().Equal(p.Off.Select(Sig));
                    p.On.Select(Stats).Should().Equal(p.Off.Select(Stats));
                    continue;
                }
                int before = p.Off.Count, after = p.On.Count, slot = p.Slot;
                seen.Add((before, after));
                after.Should().Be(before >= 3 ? before - 1 : before, "floor {0} seed {1}", floor, p.Seed);
                p.On.Count(m => m.IsElite).Should().Be(1);
                // the group before promotion, less the last member that is not the promoted one (3 or more)
                var expected = p.Off.Select(Sig).ToList();
                if (before >= 3) expected.RemoveAt(slot == before - 1 ? before - 2 : before - 1);
                p.On.Select(Sig).Should().Equal(expected, "floor {0} seed {1}", floor, p.Seed);
                Sig(p.Elite).Should().Be(Sig(p.Off[slot]));
                // the members that stay are untouched
                foreach (var m in p.On.Where(m => !m.IsElite))
                    p.Off.Any(o => Sig(o) == Sig(m) && Stats(o) == Stats(m) && o.Name == m.Name).Should().BeTrue("floor {0} seed {1}: {2} is untouched", floor, p.Seed, m.Name);
            }
        }
        seen.Should().BeEquivalentTo(new[] { (1, 1), (2, 2), (3, 2), (4, 3), (5, 4) });
    }

    // ---------------------------------------------------------------- F4

    [Fact]
    public void F4_TheElite_HasItsMembersStats_TimesTheMultipliers()
    {
        GameConfig.EliteHpMultiplier.Should().Be(1.6);
        GameConfig.EliteDamageMultiplier.Should().Be(1.15);
        GameConfig.EliteDefenceMultiplier.Should().Be(1.1);
        int elites = 0, budgeted = 0, casters = 0;
        foreach (int floor in new[] { 3, 7, 20, 60 })
            foreach (var p in Floor(floor))
            {
                if (p.Elite is not Monster e) continue;
                var m = p.Off[p.Slot];
                elites++;
                if (floor == 7 && p.Off.Count == 3) budgeted++;
                if (m.Name != m.TierName) casters++;
                e.MaxHP.Should().Be(Math.Max(1, (long)(m.MaxHP * 1.6)), "floor {0} seed {1}", floor, p.Seed);
                e.HP.Should().Be(e.MaxHP);
                e.Strength.Should().Be((long)(m.Strength * 1.15));
                e.Punch.Should().Be((long)(m.Punch * 1.15));
                e.WeapPow.Should().Be((long)(m.WeapPow * 1.15));
                e.Defence.Should().Be((int)(m.Defence * 1.1));
                e.ArmPow.Should().Be((long)(m.ArmPow * 1.1));
                e.MaxHP.Should().BeGreaterThan(m.MaxHP);
                e.Level.Should().Be(m.Level);
                e.SpecialAbilities.Should().Equal(m.SpecialAbilities);
            }
        elites.Should().BeGreaterThan(2500);
        budgeted.Should().BeGreaterThan(50, "elites promoted from a budgeted group of three keep the budget");
        casters.Should().BeGreaterThan(50, "elites promoted from a caster keep its Strength");
    }

    // ---------------------------------------------------------------- F5

    private static List<Monster> KoboldShamanPair()
    {
        // a templated mixed pair of floor 7 whose caster is a Kobold Shaman, as the generator builds it
        for (int seed = 0; seed < 20_000; seed++)
        {
            var g = Dungeon(false, 7, 7, new Random(seed));
            if (g.Count == 2 && g.Any(m => m.Name == "Kobold Shaman")) return g;
        }
        throw new InvalidOperationException("no Kobold Shaman pair in 20,000 seeds");
    }

    [Fact]
    public void F5_AnElitesName_IsStoredEnglish_AndShownPerLanguage()
    {
        string saved = GameConfig.Language;
        try
        {
            GameConfig.Language = "hu";   // the reader's language must not reach the stored name
            int elites = 0;
            var names = new HashSet<string>();
            var firstAffix = new Dictionary<string, int>();
            foreach (var p in Floor(7))
            {
                if (p.Elite is not Monster e) continue;
                elites++;
                string member = p.Off[p.Slot].Name;
                string affix = e.EliteAffixes.Should().ContainSingle("one affix below floor 51").Subject;
                Affixes.Should().Contain(affix);
                firstAffix[affix] = firstAffix.GetValueOrDefault(affix) + 1;
                e.Name.Should().Be(Loc.GetIn("en", "monster.elite." + affix, member));
                e.TierName.Should().Be(p.Off[p.Slot].TierName);
                e.FamilyName.Should().Be(p.Off[p.Slot].FamilyName);
                names.Add(e.Name);
                foreach (string lang in Langs)
                    MonsterNames.DisplayIn(lang, e).Should().Be(
                        Loc.GetIn(lang, "monster.elite." + affix, MonsterNames.DisplayIn(lang, member)), "{0} in {1}", e.Name, lang);
            }
            names.Should().Contain("Frenzied Orc");
            MonsterNames.DisplayIn("en", "Frenzied Orc").Should().Be("Frenzied Orc");
            MonsterNames.DisplayIn("it", "Frenzied Orc").Should().Be("Orco in Frenesia");
            MonsterNames.DisplayIn("hu", "Frenzied Orc").Should().Be("Őrjöngő Ork");
            MonsterNames.DisplayIn("es", "Frenzied Orc").Should().Be("Orco en Frenesí");
            MonsterNames.DisplayIn("fr", "Frenzied Orc").Should().Be("Orc en Furie");
            // each of the six affixes is drawn about one time in six
            firstAffix.Keys.Should().BeEquivalentTo(Affixes);
            foreach (var (affix, n) in firstAffix)
                (n / (double)elites).Should().BeInRange(0.12, 0.21, affix);
        }
        finally { GameConfig.Language = saved; }
    }

    [Fact]
    public void F5_AnEliteCaster_KeepsItsRole_InsideTheAffix()
    {
        var pair = KoboldShamanPair();
        int slot = pair.FindIndex(m => m.Name == "Kobold Shaman");
        var elite = MonsterGenerator.PromoteElite(pair, new FixedRandom(slot, 0), 7);
        elite.EliteAffixes.Should().Equal("frenzied");
        elite.Name.Should().Be("Frenzied Kobold Shaman");
        elite.TierName.Should().Be("Kobold");
        elite.SpecialAbilities.Should().Contain("Fireball");
        MonsterNames.DisplayIn("es", elite).Should().Be("Chamán Kobold en Frenesí");
        MonsterNames.DisplayIn("hu", elite).Should().Be("Őrjöngő Kobold Sámán");
        MonsterNames.DisplayIn("it", elite).Should().Be("Sciamano Coboldo in Frenesia");
        MonsterNames.DisplayIn("fr", elite).Should().Be("Chaman Kobold en Furie");
        MonsterNames.DisplayIn("en", elite).Should().Be("Frenzied Kobold Shaman");
    }

    [Fact]
    public void F5_OneAffixToFloor50_TwoDifferentOnesFromFloor51_TheFirstOutermost()
    {
        GameConfig.EliteSecondAffixFloor.Should().Be(51);
        foreach (int floor in new[] { 3, 30, 50 })
            Floor(floor, 4000).Where(p => p.Elite != null).Should().OnlyContain(p => p.Elite!.EliteAffixes.Count == 1, "floor {0}", floor);
        foreach (int floor in new[] { 51, 80, 100 })
        {
            var pairsSeen = new HashSet<string>();
            foreach (var p in Floor(floor, 4000))
            {
                if (p.Elite is not Monster e) continue;
                e.EliteAffixes.Should().HaveCount(2, "floor {0} seed {1}", floor, p.Seed);
                e.EliteAffixes.Distinct().Should().HaveCount(2);
                e.EliteAffixes.Should().OnlyContain(a => Affixes.Contains(a));
                pairsSeen.Add(string.Join("+", e.EliteAffixes));
                string member = p.Off[p.Slot].Name;
                string inner = Loc.GetIn("en", "monster.elite." + e.EliteAffixes[1], member);
                e.Name.Should().Be(Loc.GetIn("en", "monster.elite." + e.EliteAffixes[0], inner));
                foreach (string lang in Langs)
                    MonsterNames.DisplayIn(lang, e).Should().Be(Loc.GetIn(lang, "monster.elite." + e.EliteAffixes[0],
                        Loc.GetIn(lang, "monster.elite." + e.EliteAffixes[1], MonsterNames.DisplayIn(lang, member))), "{0} in {1}", e.Name, lang);
            }
            pairsSeen.Count.Should().BeGreaterThan(20, "floor {0}: the 30 ordered pairs are drawn", floor);
        }
    }

    // ---------------------------------------------------------------- the text

    private static readonly string[] Keys = Affixes.Select(a => "monster.elite." + a)
        .Concat(Affixes.Select(a => "monster.elite_desc." + a))
        .Concat(new[] { "dungeon.elite_appears", "dungeon.elite_appears_alone" }).ToArray();

    [Fact]
    public void TheKeys_AreInFiveLanguages_Translated_WithTheirArguments()
    {
        Keys.Should().HaveCount(14);
        Loc.GetIn("en", "monster.elite.frenzied").Should().Be("Frenzied {0}");
        Loc.GetIn("en", "dungeon.elite_appears").Should().Be("An elite {0} leads the group: {1}.");
        foreach (string key in Keys)
            foreach (string lang in Langs)
            {
                Loc.HasIn(lang, key).Should().BeTrue("{0} in {1}", key, lang);
                string text = Loc.GetIn(lang, key);
                text.Should().NotBeNullOrWhiteSpace();
                text.Should().NotMatchRegex(@"[\u2013\u2014]", "{0} in {1}", key, lang);
                text.Should().MatchRegex(@"^[\p{L}\p{M} ,.:'{}0-9\u00A0]+$", "{0} in {1}: plain words, readable by a screen reader and on a BBS", key, lang);
                if (key.StartsWith("monster.elite.")) text.Should().Contain("{0}").And.NotContain("{1}");
                if (key.StartsWith("monster.elite_desc.")) text.Should().NotContain("{");
                if (key.StartsWith("dungeon.")) text.Should().Contain("{0}").And.Contain("{1}");
                if (lang != "en") text.Should().NotBe(Loc.GetIn("en", key), "{0} is translated in {1}", key, lang);
            }
    }

    private static Monster Named(string member, params string[] affixes)
    {
        var m = new Monster { Name = member, TierName = member, IsElite = true };
        m.EliteAffixes.AddRange(affixes);
        for (int i = affixes.Length - 1; i >= 0; i--) m.Name = MonsterNames.FromKey("monster.elite." + affixes[i], m.Name);
        return m;
    }

    /// <summary>The rows the player reads (WriteEliteIntro: UIHelper.WriteWrapped) and the rows a follower reads
    /// (BroadcastDungeonEvent: FollowerMessage), for an elite with or without company.</summary>
    private static IEnumerable<string> IntroRows(string lang, Monster elite, bool alone)
    {
        var group = alone ? new List<Monster> { elite } : new List<Monster> { elite, new Monster { Name = "Wolf", TierName = "Wolf" } };
        string intro = DungeonLocation.EliteIntro(lang, group)!;
        intro.Should().Be(Loc.GetIn(lang, alone ? "dungeon.elite_appears_alone" : "dungeon.elite_appears", MonsterNames.DisplayIn(lang, elite),
            string.Join(Loc.GetIn(lang, "dungeon.and_separator"), elite.EliteAffixes.Select(a => Loc.GetIn(lang, "monster.elite_desc." + a)))));
        var rows = UIHelper.WordWrap(intro, UIHelper.WrapWidth);
        string.Join(" ", rows).Should().Be(intro, "wrapping keeps every word");
        foreach (var row in rows) yield return row;
        foreach (var row in DungeonLocation.FollowerMessage(DungeonLocation.EliteBroadcast(group))(lang).Split('\n')) yield return row;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public void TheIntroLine_FitsIn79Columns_WithTheLongestNamesAndDescriptions(string lang)
    {
        UIHelper.WrapWidth.Should().Be(79);
        var tiers = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Select(t => (t.Name, t.MinLevel, Caster: MonsterGenerator.CasterSpells.ContainsKey(f.FamilyName)))).ToList();
        tiers.Count.Should().BeGreaterThan(60);
        int widest = 0, longestIntro = 0, mostRows = 0;
        void Check(Monster elite)
        {
            foreach (bool alone in new[] { false, true })
            {
                var rows = IntroRows(lang, elite, alone).ToList();
                mostRows = Math.Max(mostRows, rows.Count / 2);
                longestIntro = Math.Max(longestIntro, DungeonLocation.EliteIntro(lang, new List<Monster> { elite })!.Length);
                foreach (var row in rows)
                {
                    int width = UIHelper.VisibleLength(row);
                    widest = Math.Max(widest, width);
                    width.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row);
                }
            }
        }
        foreach (var (tier, minLevel, caster) in tiers)
        {
            foreach (string a in Affixes)
            {
                Check(Named(tier, a));                                                   // one affix
                if (caster && minLevel <= 10) Check(Named(MonsterNames.FromKey("monster.role.caster", tier), a));   // the nested caster
                foreach (string b in Affixes.Where(b => b != a)) Check(Named(tier, a, b));   // two affixes, floor 51 and deeper
            }
        }
        widest.Should().BeGreaterThan(60);
        longestIntro.Should().BeGreaterThan(79, "the longest line is wrapped, not cut");
        mostRows.Should().BeInRange(2, 3);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void TheEncounterLines_OfFloors3To10_FitIn79Columns_WithAnElite(string lang)
    {
        // floors 3 to 10: an elite stands alone or with one other member (a group of three loses one)
        var early = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Where(t => t.MinLevel <= 10).Select(t => (t.Name, Caster: MonsterGenerator.CasterSpells.ContainsKey(f.FamilyName)))).ToList();
        string Shown(string stored) => MonsterNames.DisplayIn(lang, stored);
        int widest = 0;
        void Fits(string row) { widest = Math.Max(widest, row.Length); row.Length.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row); }
        foreach (var (tier, caster) in early)
            foreach (string a in Affixes)
            {
                var names = new List<string> { Named(tier, a).Name };
                if (caster) names.Add(Named(MonsterNames.FromKey("monster.role.caster", tier), a).Name);
                foreach (string elite in names)
                {
                    Fits(Loc.GetIn(lang, "dungeon.monster_attacks", GameConfig.ArticulateForLanguage(Shown(elite))));
                    foreach (var (other, _) in early)
                    {
                        Fits(Loc.GetIn(lang, "dungeon.you_face_prefix") + $"1 {Shown(elite)}" + Loc.GetIn(lang, "dungeon.and_separator") + $"1 {Shown(other)}!");
                        Fits(Loc.GetIn(lang, "dungeon.you_encounter") + $"1 {Shown(elite)}" + Loc.GetIn(lang, "dungeon.and_separator") + $"1 {Shown(other)}!");
                    }
                }
            }
        widest.Should().BeGreaterThan(50);
    }

    // ---------------------------------------------------------------- F13

    [Fact]
    public void F13_AFollower_ReadsTheElite_InTheirOwnLanguage()
    {
        string saved = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";   // the leader reads English
            var pair = KoboldShamanPair();
            var elite = MonsterGenerator.PromoteElite(pair, new FixedRandom(pair.FindIndex(m => m.Name == "Kobold Shaman"), 2), 7);
            elite.Name.Should().Be("Venomous Kobold Shaman");
            var broadcast = DungeonLocation.FollowerMessage(DungeonLocation.EliteBroadcast(pair));
            string es = broadcast("es");
            UIHelper.VisibleLength(es.Replace("\n", "")).Should().BeGreaterThan(20);
            string esText = string.Join(" ", es.Split('\n').Select(r => System.Text.RegularExpressions.Regex.Replace(r, "\u001b\\[[0-9;]*m", "").Trim()));
            esText.Should().Be("Un monstruo de élite, Chamán Kobold con Veneno, lidera el grupo: sus golpes pueden envenenar.");
            string huText = string.Join(" ", broadcast("hu").Split('\n').Select(r => System.Text.RegularExpressions.Regex.Replace(r, "\u001b\\[[0-9;]*m", "").Trim()));
            huText.Should().Be("Elit szörny vezeti a csapatot, Mérgező Kobold Sámán: ütései megmérgezhetnek.");
            broadcast("en").Should().Contain("An elite Venomous Kobold Shaman leads the group: its blows can poison.");
            foreach (string lang in Langs)
                foreach (string row in broadcast(lang).Split('\n'))
                    UIHelper.VisibleLength(row).Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row);
            // the names in the group line a follower reads are in the follower's language too
            MonsterNames.DisplayIn("es", elite).Should().Be("Chamán Kobold con Veneno");
            // no elite: no line
            DungeonLocation.EliteIntro("es", KoboldShamanPair()).Should().BeNull();
        }
        finally { GameConfig.Language = saved; }
    }

    [Fact]
    public void F13_BothEncounterSites_WriteTheLine_AndTheRoomFightBroadcastsIt()
    {
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        System.Text.RegularExpressions.Regex.Matches(src, @"\n\s+WriteEliteIntro\(monsters\);").Count.Should().Be(2, "the room fight and MonsterEncounter");
        src.Should().Contain("if (monsters.Any(m => m.IsElite)) BroadcastDungeonEvent(EliteBroadcast(monsters));");
        src.Should().Contain("if (EliteIntro(GameConfig.Language, monsters) is not string intro) return;");
        src.Should().Contain("UsurperRemake.UI.UIHelper.WriteWrapped(terminal, intro);");
    }

    // ---------------------------------------------------------------- F11

    private static Character Hero(long darkness = 0) => new Character
    {
        Name1 = "Tester", Name2 = "Tester", AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 8,
        HP = 60_000, MaxHP = 60_000, BaseMaxHP = 60_000, Strength = 400, BaseStrength = 400, Defence = 20, BaseDefence = 20,
        Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20, Constitution = 30, BaseConstitution = 30,
        Intelligence = 20, BaseIntelligence = 20, Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
        Mental = 80, Gold = 0, Healing = 0, AutoHeal = false, CombatSpeed = CombatSpeed.Instant, Darkness = darkness,
    };

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

    private static async Task<(CombatResult? Result, string Transcript)> Fight(Character hero, List<Monster> group, int attacks)
    {
        string script = string.Concat(Enumerable.Repeat("A\n\n", attacks)) + string.Concat(Enumerable.Repeat("\n", 40));
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(script), output));
        engine.SeedRandomForTests(128);
        CombatResult? result = null;
        try
        {
            var task = engine.PlayerVsMonsters(hero, group, offerMonkEncounter: false);
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (done == task) result = await task;
        }
        catch (Exception) { /* the script ran out: the fight was long enough */ }
        return (result, Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>A floor 7 pair as the generator builds it, its rewards set to known values.</summary>
    private static List<Monster> PairWithRewards(long experience, long gold)
    {
        var pair = Dungeon(false, 7, 7, new Random(Enumerable.Range(0, 1000).First(s => !ChampionSeed(s) && Dungeon(false, 7, 7, new Random(s)).Count == 2)));
        foreach (var m in pair) { m.Experience = experience; m.Gold = gold; }
        return pair;
    }

    [Fact]
    public void F11_Promotion_RaisesTheStoredReward_OfTheEliteOnly()
    {
        GameConfig.EliteExperienceMultiplier.Should().Be(1.6);
        GameConfig.EliteGoldMultiplier.Should().Be(1.5);
        foreach (var (xp, gold) in new[] { (1000L, 400L), (333L, 77L), (15L, 10L) })
            foreach (int slot in new[] { 0, 1 })
            {
                var pair = PairWithRewards(xp, gold);
                var elite = MonsterGenerator.PromoteElite(pair, new FixedRandom(slot, 3), 7);
                elite.Should().BeSameAs(pair[slot]);
                elite.Experience.Should().Be((long)(xp * 1.6));
                elite.Gold.Should().Be((long)(gold * 1.5));
                pair[1 - slot].Experience.Should().Be(xp);
                pair[1 - slot].Gold.Should().Be(gold);
            }
        // a generated elite carries more than the plain formula of its level gives its member
        foreach (var p in Floor(7, 3000).Where(p => p.Elite != null))
            p.Elite!.Experience.Should().Be((long)(p.Off[p.Slot].Experience * 1.6), "seed {0}", p.Seed);
    }

    [Fact]
    public async Task F11_AVictoryOverOneElite_PaysItsExperienceTimes1Point6_AndItsGoldTimes1Point5()
    {
        // the same seeded one-blow fight twice: a member with a known reward, then the same member promoted.
        // Only the stored reward differs, so the paid reward is the formula's.
        async Task<CombatResult> Win(bool promote)
        {
            var pair = PairWithRewards(1000, 400);
            var foe = promote ? MonsterGenerator.PromoteElite(pair, new FixedRandom(0, 5), 7) : pair[0];
            foe.IsElite.Should().Be(promote);
            foe.HP = foe.MaxHP = 1; foe.Defence = 0; foe.ArmPow = 0; foe.Strength = 1; foe.Punch = 1; foe.WeapPow = 1;
            foe.SpecialAbilities.Clear(); foe.SignatureAbilities.Clear();
            var (result, transcript) = await Fight(Hero(), new List<Monster> { foe }, 20);
            result.Should().NotBeNull(transcript);
            result!.Outcome.Should().Be(CombatOutcome.Victory, transcript);
            result.DefeatedMonsters.Should().ContainSingle();
            return result;
        }
        var plain = await Win(false);
        var elite = await Win(true);
        plain.ExperienceGained.Should().BeGreaterThan(100);
        plain.GoldGained.Should().BeGreaterThan(100);
        ((double)elite.ExperienceGained / plain.ExperienceGained).Should().BeApproximately(1.6, 0.01, "experience {0} against {1}", elite.ExperienceGained, plain.ExperienceGained);
        ((double)elite.GoldGained / plain.GoldGained).Should().BeApproximately(1.5, 0.01, "gold {0} against {1}", elite.GoldGained, plain.GoldGained);
    }

    // ---------------------------------------------------------------- F12

    [Fact]
    public void F12_AnElite_IsNotAMiniBoss_AndIsNotSaved()
    {
        int elites = 0;
        foreach (int floor in new[] { 3, 7, 10, 20, 60, 100 })
            foreach (var p in Floor(floor, 4000))
            {
                if (p.Elite is not Monster e) continue;
                elites++;
                // promotion leaves the flag as the member had it (a tier named a lord carries it from its name)
                e.IsMiniBoss.Should().Be(p.Off[p.Slot].IsMiniBoss, "floor {0} seed {1} {2}", floor, p.Seed, e.Name);
                e.IsBoss.Should().BeFalse();
                if (floor <= 10) e.IsMiniBoss.Should().BeFalse("floor {0} seed {1} {2}", floor, p.Seed, e.Name);
            }
        elites.Should().BeGreaterThan(1500);

        var pair = PairWithRewards(100, 100);
        var elite = MonsterGenerator.PromoteElite(pair, new FixedRandom(0, 0), 7);
        string json = JsonSerializer.Serialize(elite);
        json.Should().NotContain("IsElite").And.NotContain("EliteAffixes");
        json.Should().Contain("\"IsMiniBoss\":false");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task F12_FearCanLandOnAnElite_AsOnARegularMonster(bool miniBossControl)
    {
        var pair = PairWithRewards(100, 100);
        var elite = MonsterGenerator.PromoteElite(pair, new FixedRandom(0, 0), 7);
        elite.HP = elite.MaxHP = 50_000_000; elite.Strength = 1; elite.Punch = 1; elite.WeapPow = 1;
        elite.SpecialAbilities.Clear(); elite.SignatureAbilities.Clear();
        if (miniBossControl) elite.IsMiniBoss = true;   // the control: a mini-boss resists, so the line below is the flag's doing
        // an Evil character frightens on one hit in ten (AlignmentSystem.GetFearOnHitChance); bosses and mini-bosses resist
        var (_, transcript) = await Fight(Hero(darkness: 1000), new List<Monster> { elite }, 60);
        string line = Loc.GetIn("en", "combat.terror_incarnate", elite.Name);
        if (miniBossControl) transcript.Should().NotContain(line);
        else transcript.Should().Contain(line);
    }

    // ---------------------------------------------------------------- F14

    [Fact]
    public void F14_SwitchOff_OrNoRules_NoExtraDraw_AndTheSameGroup()
    {
        foreach (int floor in Enumerable.Range(1, 12).Concat(new[] { 20, 30, 31, 51, 60, 100 }))
            for (int seed = 0; seed < 1000; seed++)
            {
                double scale = MonsterGenerator.OldGodApproachScale(floor);
                var a = new RecordingRandom(seed); var b = new RecordingRandom(seed); var c = new RecordingRandom(seed);
                // today's group: no rules, as every caller before this piece
                var plain = WithTemplatesOff(() => WithElites(false, () => MonsterGenerator.GenerateMonsterGroup(floor, a, scale)));
                var off = WithTemplatesOff(() => WithElites(false, () => MonsterGenerator.GenerateMonsterGroup(floor, b, scale, EncounterRules.Dungeon(floor))));
                var noRules = WithTemplatesOff(() => WithElites(true, () => MonsterGenerator.GenerateMonsterGroup(floor, c, scale)));
                foreach (var (group, rng, what) in new[] { (off, b, "switch off"), (noRules, c, "no rules") })
                {
                    group.Should().NotContain(m => m.IsElite, "{0}: floor {1} seed {2}", what, floor, seed);
                    group.Select(Sig).Should().Equal(plain.Select(Sig));
                    group.Select(m => m.Name).Should().Equal(plain.Select(m => m.Name));
                    group.Select(Stats).Should().Equal(plain.Select(Stats));
                    rng.Calls.Should().Equal(a.Calls, "{0}: floor {1} seed {2}", what, floor, seed);
                }
                // the next random number after the call is today's
                int next = a.Next();
                b.Next().Should().Be(next);
                c.Next().Should().Be(next);
            }
        WithElites(false, () => MonsterGenerator.ElitesApply(EncounterRules.Dungeon(7))).Should().BeFalse();
        WithElites(true, () => MonsterGenerator.ElitesApply(EncounterRules.Dungeon(7))).Should().BeTrue();
        WithElites(true, () => MonsterGenerator.ElitesApply(null)).Should().BeFalse();
        WithElites(true, () => MonsterGenerator.ElitesApply(EncounterRules.Dungeon(2))).Should().BeFalse();
    }

    private static T WithTemplatesOff<T>(Func<T> body)
    {
        bool saved = GameConfig.EncounterTemplatesEnabled;
        try { GameConfig.EncounterTemplatesEnabled = false; return body(); }
        finally { GameConfig.EncounterTemplatesEnabled = saved; }
    }

    [Fact]
    public void F14_SwitchOn_EveryDrawBeforeTheEliteRoll_IsTodays_AndOnlyThePromotionDiffers()
    {
        foreach (int floor in new[] { 1, 2, 3, 5, 7, 10, 11, 30, 50, 51, 80 })
            foreach (var p in Floor(floor, 3000))
            {
                p.OnCalls.Take(p.OffCalls.Count).Should().Equal(p.OffCalls, "floor {0} seed {1}", floor, p.Seed);
                int extra = p.OnCalls.Count - p.OffCalls.Count;
                if (floor <= 2 || ChampionSeed(p.Seed)) { extra.Should().Be(0, "floor {0} seed {1}: no roll", floor, p.Seed); p.Elite.Should().BeNull(); }
                else if (p.Elite == null) extra.Should().Be(1, "floor {0} seed {1}: the roll alone", floor, p.Seed);
                else extra.Should().Be(floor >= 51 ? 4 : 3, "floor {0} seed {1}: the roll, the member, the affixes", floor, p.Seed);
                if (extra > 0) p.OnCalls[p.OffCalls.Count].Should().StartWith("NextDouble()=");
            }
    }

    [Fact]
    public void TheSwitch_IsOnByDefault_AndNotASysopSetting()
    {
        var p = typeof(GameConfig).GetProperty(nameof(GameConfig.EliteMonstersEnabled))!;
        GameConfig.EliteMonstersEnabled.Should().BeTrue();
        p.SetMethod!.IsAssembly.Should().BeTrue("the setter is internal");
        string root = Leftovers1114BTests.RepoRoot();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name is "GameConfig.cs" or "MonsterGenerator.cs") continue;
            File.ReadAllText(file).Should().NotContain("EliteMonstersEnabled", $"{name} must not read or set the switch");
        }
        foreach (var file in Directory.GetFiles(root, "balance*.json", SearchOption.TopDirectoryOnly))
            File.ReadAllText(file).Should().NotContain("EliteMonsters");
    }

    [Fact]
    public void NoAffixBehaviour_IsBuiltInThisPiece()
    {
        // the affix is drawn, stored and named here; what it does comes with rows F6 to F10 and Warded
        string root = Leftovers1114BTests.RepoRoot();
        File.ReadAllText(Path.Combine(root, "Localization", "en.json")).Should().NotContain("\"combat.elite_");
        string engine = File.ReadAllText(Path.Combine(root, "Scripts", "Systems", "CombatEngine.cs"));
        engine.Should().NotContain("EliteAffixes").And.NotContain("IsElite");
    }
}
