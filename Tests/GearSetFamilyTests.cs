using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using Xunit.Abstractions;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.9: gear pieces that predate v1.1.0 carry no Family, so they never counted toward a set
/// (player report: identical pieces are set pieces or not depending on when they dropped). Their
/// family is now inferred from the name, at read time, under three guards. The table test is the
/// one that decides it: every loot template, in every language, in every name the generator can
/// give it, must resolve to exactly its own family, or to none when it is not a set template.
/// </summary>
[Collection("SharedGameSingletons")]
public class GearSetFamilyTests
{
    private readonly ITestOutputHelper _out;
    static GearSetFamilyTests() => Loc.Initialize();
    public GearSetFamilyTests(ITestOutputHelper output) { _out = output; }

    private static IEnumerable<(string Name, GearSetSlotKind Kind)> LootTemplates() =>
        LootGenerator.GetWeaponTemplates().Select(t => (t.Name, GearSetSlotKind.Weapon))
        .Concat(LootGenerator.GetBodyArmorTemplates().Select(t => (t.Name, GearSetSlotKind.Body)))
        .Concat(LootGenerator.GetHeadArmorTemplates().Select(t => (t.Name, GearSetSlotKind.Head)))
        .Concat(LootGenerator.GetArmsArmorTemplates().Select(t => (t.Name, GearSetSlotKind.Arms)))
        .Concat(LootGenerator.GetHandsArmorTemplates().Select(t => (t.Name, GearSetSlotKind.Hands)))
        .Concat(LootGenerator.GetLegsArmorTemplates().Select(t => (t.Name, GearSetSlotKind.Legs)))
        .Concat(LootGenerator.GetFeetArmorTemplates().Select(t => (t.Name, GearSetSlotKind.Feet)))
        .Concat(LootGenerator.GetWaistArmorTemplates().Select(t => (t.Name, GearSetSlotKind.Waist)))
        .Concat(LootGenerator.GetFaceArmorTemplates().Select(t => (t.Name, GearSetSlotKind.Face)))
        .Concat(LootGenerator.GetCloakArmorTemplates().Select(t => (t.Name, GearSetSlotKind.Cloak)))
        .Concat(LootGenerator.GetShieldTemplates().Select(t => (t.Name, GearSetSlotKind.Shield)))
        .Concat(LootGenerator.GetRingTemplates().Select(t => (t.Name, GearSetSlotKind.Ring)))
        .Concat(LootGenerator.GetNecklaceTemplates().Select(t => (t.Name, GearSetSlotKind.Neck)));

    [Fact]
    public void EveryTemplate_InEveryLanguage_InEveryNameTheGeneratorGivesIt_ResolvesToItsOwnFamily()
    {
        // Two templates of one kind can be given the very same name in some language. The name
        // cannot say which it came from, so the resolver must decline rather than guess.
        var producedBy = new Dictionary<(string, GearSetSlotKind, string), HashSet<string>>();
        foreach (var (code, _) in Loc.AvailableLanguages)
            foreach (var (template, kind) in LootTemplates())
                foreach (var form in LootGenerator.AllNameFormsFor(template, code))
                {
                    var k = (code, kind, form.ToLowerInvariant());
                    if (!producedBy.TryGetValue(k, out var set)) producedBy[k] = set = new HashSet<string>();
                    set.Add(template);
                }
        var ambiguous = new SortedSet<string>();

        var decorations = EnchantDecorations();
        var bossPrefixes = LootGenerator.WorldBossNamePrefixes;
        var failures = new List<string>();
        int checkedNames = 0, checkedDecorated = 0;
        foreach (var (code, _) in Loc.AvailableLanguages)
        {
            foreach (var (template, kind) in LootTemplates())
            {
                foreach (var form in LootGenerator.AllNameFormsFor(template, code).Distinct())
                {
                    var sources = producedBy[(code, kind, form.ToLowerInvariant())];
                    string? expected = GearSetRegistry.ForFamily(template) != null ? template : null;
                    // shared names between two non-set templates ("Fine" + "Dagger" is the Fine Dagger)
                    // cannot change a set count, so only a shared name involving a set is recorded
                    if (sources.Count > 1 && sources.Any(s => GearSetRegistry.ForFamily(s) != null))
                    {
                        ambiguous.Add($"{code}: {string.Join(" / ", sources.OrderBy(x => x))}");
                        if (sources.Any(s => GearSetRegistry.ForFamily(s) == null)) expected = null;
                    }
                    checkedNames++;
                    var got = GearSetFamilyResolver.FamilyOf(null, form, kind);
                    if (got != expected)
                        failures.Add($"[{code}] {kind} '{form}' (template '{template}') -> {got ?? "none"}, expected {expected ?? "none"}");
                    // the same name after a magic shop enchant: one single decoration in rotation, so
                    // each lands on thousands of names across every template and language, and the stacks
                    var single = decorations[checkedNames % decorations.Count];
                    // and a world boss drop: an element prefix in rotation, alone and under an enchant
                    var boss = bossPrefixes[checkedNames % bossPrefixes.Count] + " " + form;
                    var variants = decorations.Where(d => d == single || d.Count(ch => ch == '(' || ch == '+') > 1).Select(d => form + d)
                        .Append(boss).Append(boss + single);
                    foreach (var v in variants)
                    {
                        checkedDecorated++;
                        var dgot = GearSetFamilyResolver.FamilyOf(null, v, kind);
                        if (dgot != expected)
                            failures.Add($"[{code}] {kind} '{v}' (template '{template}') -> {dgot ?? "none"}, expected {expected ?? "none"}");
                    }
                }
            }
        }

        _out.WriteLine($"checked {checkedNames:N0} names and {checkedDecorated:N0} enchanted names ({decorations.Count} enchant decorations, {bossPrefixes.Count} world boss prefixes) across {Loc.AvailableLanguages.Length} languages; {failures.Count} wrong");
        foreach (var a in ambiguous) _out.WriteLine($"ambiguous, resolves to none: {a}");
        foreach (var f in failures.Take(60)) _out.WriteLine(f);
        // the only shared names in the shipped tables; a new one needs a look before it ships
        ambiguous.Should().BeEquivalentTo(new[] { "fr: Cloak of Shadows / Shadow Cloak" });
        // a table that checked nothing passes; the first run of this test did exactly that
        Loc.AvailableLanguages.Length.Should().BeGreaterThanOrEqualTo(5, "every language a player can drop loot in");
        checkedNames.Should().BeGreaterThan(10_000, "every template in every form in every language");
        decorations.Count.Should().BeGreaterThan(15, "every enchant tag and every stat abbreviation");
        bossPrefixes.Count.Should().BeGreaterThanOrEqualTo(9, "one per boss element and the fallback");
        failures.Should().BeEmpty($"every generated name must resolve to its own template's family; {failures.Count} did not");
    }

    [Fact]
    public void RealDrops_WithTheirFamilyBlanked_ResolveBackToIt()
    {
        // End to end, so the forms the resolver knows cannot drift from the names the generator
        // actually gives: every drop since v1.1.0 carries its Family, so blank it and read it back.
        // The generators read the language through GameConfig.Language, which without a session is
        // process-wide, and other test classes run in parallel; a SessionContext (AsyncLocal) keeps
        // each language switch to this test's own flow.
        var savedContext = UsurperRemake.Server.SessionContext.Current;
        var session = new UsurperRemake.Server.SessionContext { InputStream = System.IO.Stream.Null, OutputStream = System.IO.Stream.Null };
        UsurperRemake.Server.SessionContext.Current = session;
        var failures = new List<string>();
        int checkedDrops = 0;
        var classes = new[] { CharacterClass.Warrior, CharacterClass.Magician, CharacterClass.Assassin, CharacterClass.Ranger, CharacterClass.Cleric, CharacterClass.Paladin };
        var elements = new[] { "Water", "Void", "Shadow", "Fire", "Undead", "Physical", "Poison", "Eldritch" };
        try
        {
            foreach (var (code, _) in Loc.AvailableLanguages)
            {
                session.Language = code;
                GameConfig.Language.Should().Be(code, "the switch must land on the session, not the process");
                for (int i = 0; i < 1500; i++)
                {
                    int level = 1 + i % 100;
                    var cls = classes[i % classes.Length];
                    var drops = new[]
                    {
                        LootGenerator.GenerateDungeonLoot(level, cls),
                        LootGenerator.GenerateRing(level),
                        LootGenerator.GenerateNecklace(level),
                        LootGenerator.GenerateShield(level),
                        i % 10 == 0 ? LootGenerator.GenerateWorldBossLoot(Math.Max(level, 30), LootGenerator.ItemRarity.Rare, elements[i / 10 % elements.Length], cls) : null,
                    };
                    foreach (var d in drops)
                    {
                        if (d == null || string.IsNullOrEmpty(d.Family)) continue;
                        checkedDrops++;
                        string? expected = GearSetRegistry.ForFamily(d.Family) != null ? d.Family : null;
                        if (code == "fr" && (d.Family is "Shadow Cloak" or "Cloak of Shadows")) expected = null;   // the one shared name
                        var got = GearSetFamilyResolver.FamilyOf(null, d.Name, GearSetFamilyResolver.KindOf(d));
                        if (got != expected)
                            failures.Add($"[{code}] {d.Type} '{d.Name}' (family '{d.Family}') -> {got ?? "none"}, expected {expected ?? "none"}");
                    }
                }
            }
        }
        finally { UsurperRemake.Server.SessionContext.Current = savedContext; }

        _out.WriteLine($"checked {checkedDrops:N0} drops; {failures.Count} wrong");
        foreach (var f in failures.Distinct().Take(60)) _out.WriteLine(f);
        checkedDrops.Should().BeGreaterThan(10_000);
        failures.Should().BeEmpty();
    }

    /// <summary>
    /// What the magic shop enchant appends to a name (MagicShopLocation, the enchant flow): " +N Abc"
    /// with the first three letters of each stat, read from the shop's own StatNames so a new stat
    /// is covered; each parenthesized tag; the legacy flow's bare " +N"; and stacks of them.
    /// </summary>
    private static List<string> EnchantDecorations()
    {
        var statNames = (string[])typeof(global::MagicShopLocation)
            .GetField("StatNames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        var list = statNames.Select(s => $" +6 {s.Substring(0, 3)}").ToList();
        list.AddRange(new[] { " (Blessed)", " (Ocean-Touched)", " (Warded)", " (Predator)", " (Lifedrinker)", " (Phoenix Fire)", " (Frostbite)" });
        list.Add(" +3");
        list.Add(" (Lifedrinker) (Blessed)");
        list.Add(" (Warded) +6 Agi");
        list.Add(" +6 Con +6 Agi");
        return list;
    }

    [Theory]
    // every enchanted name on the live server, 2026-09-21, that resolved to a set (the sweep's 23)
    [InlineData("Titan's Forged Armguards (Predator)", GearSetSlotKind.Arms, "Forged Armguards")]
    [InlineData("Chain Shirt (Lifedrinker) (Warded)", GearSetSlotKind.Body, "Chain Shirt")]
    [InlineData("Chain Shirt of Insight +6 Con", GearSetSlotKind.Body, "Chain Shirt")]
    [InlineData("Leather Armor of Healing +4 Arm +4 Def", GearSetSlotKind.Body, "Leather Armor")]
    [InlineData("Mighty Reinforced Chain (Lifedrinker)", GearSetSlotKind.Body, "Reinforced Chain")]
    [InlineData("Purified Leather Armor +2 Str +2 Str", GearSetSlotKind.Body, "Leather Armor")]
    [InlineData("Leather Face Guard (Lifedrinker)", GearSetSlotKind.Face, "Leather Face Guard")]
    [InlineData("Shadow Mask of Healing +6 Con +6 Agi", GearSetSlotKind.Face, "Shadow Mask")]
    [InlineData("Leather Boots (Lifedrinker) (Blessed)", GearSetSlotKind.Feet, "Leather Boots")]
    [InlineData("Steel Sabatons of Agility +8 Def", GearSetSlotKind.Feet, "Steel Sabatons")]
    [InlineData("Chain Gauntlets of Agility +6 Arm", GearSetSlotKind.Hands, "Chain Gauntlets")]
    [InlineData("Leather Gloves (Lifedrinker) (Blessed)", GearSetSlotKind.Hands, "Leather Gloves")]
    [InlineData("Forged Helm of Fortitude (Predator)", GearSetSlotKind.Head, "Forged Helm")]
    [InlineData("Purified Chain Coif (Warded) +6 Agi", GearSetSlotKind.Head, "Chain Coif")]
    [InlineData("Purified Chain Coif +4 Con", GearSetSlotKind.Head, "Chain Coif")]
    [InlineData("Robust Reinforced Helm +4 Wea", GearSetSlotKind.Head, "Reinforced Helm")]
    [InlineData("Steel Helm (Blessed)", GearSetSlotKind.Head, "Steel Helm")]
    [InlineData("Steel Helm of Protection +6 Con", GearSetSlotKind.Head, "Steel Helm")]
    [InlineData("Leather Leggings (Lifedrinker)", GearSetSlotKind.Legs, "Leather Leggings")]
    [InlineData("Leather Cord of the Arcane (Warded)", GearSetSlotKind.Neck, "Leather Cord")]
    [InlineData("Chain Belt of Healing (Lifedrinker)", GearSetSlotKind.Waist, "Chain Belt")]
    [InlineData("Leather Belt (Lifedrinker) (Blessed)", GearSetSlotKind.Waist, "Leather Belt")]
    [InlineData("Venomous Forged Mace (Predator)", GearSetSlotKind.Weapon, "Forged Mace")]
    // world boss drops: the element prefix goes on the already decorated name (Codex's example)
    [InlineData("Abyssal Steel Vambraces of Power", GearSetSlotKind.Arms, "Steel Vambraces")]
    [InlineData("World Boss Fine Leather Cap (Blessed)", GearSetSlotKind.Head, "Leather Cap")]
    public void LiveEnchantedNames_ResolveToTheirSet(string name, GearSetSlotKind kind, string family) =>
        GearSetFamilyResolver.FamilyOf(null, name, kind).Should().Be(family, name);

    [Theory]
    // an enchant never turns a piece that is not a set piece into one
    [InlineData("Studded Leather Cap (Blessed)", GearSetSlotKind.Head)]
    [InlineData("Reinforced Chain Boots +6 Con", GearSetSlotKind.Feet)]
    [InlineData("Cloak of Shadows (Warded)", GearSetSlotKind.Cloak)]
    [InlineData("Shadowforged Cloak (Predator) +4 Str", GearSetSlotKind.Cloak)]
    [InlineData("Forged-Thread Cape (Lifedrinker)", GearSetSlotKind.Cloak)]
    [InlineData("Cape des Ombres (Blessed)", GearSetSlotKind.Cloak)]   // the French shared name stays undecided
    [InlineData("Studded Leather of the Sentinel +3", GearSetSlotKind.Body)]
    [InlineData("Abyssal Studded Leather Cap (Warded)", GearSetSlotKind.Head)]   // a world boss prefix too
    public void EnchantedNamesThatAreNotSetPieces_StayNull(string name, GearSetSlotKind kind) =>
        GearSetFamilyResolver.FamilyOf(null, name, kind).Should().BeNull(name);

    [Fact]
    public void EverySetFamily_IsALootTemplate_SoItCanBeResolvedAtAll()
    {
        var loot = LootTemplates().Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        GearSetRegistry.Sets.SelectMany(s => s.Families).Where(f => !loot.Contains(f))
            .Should().BeEmpty("a family no loot table can produce could never be inferred");
    }

    [Theory]
    // the player's own pieces, from the screenshots
    [InlineData("Fine Leather Leggings", GearSetSlotKind.Legs, "Leather Leggings")]
    [InlineData("Empowering Leather Cap", GearSetSlotKind.Head, "Leather Cap")]
    [InlineData("Leather Boots of Immunity", GearSetSlotKind.Feet, "Leather Boots")]
    [InlineData("Leather Gloves of Protection", GearSetSlotKind.Hands, "Leather Gloves")]
    [InlineData("Reinforced Leggings of the Yeti", GearSetSlotKind.Legs, "Reinforced Leggings")]
    [InlineData("Chain Leggings", GearSetSlotKind.Legs, "Chain Leggings")]
    // enchant and reforge suffixes still leave the template intact
    [InlineData("Leather Cap +2 Str", GearSetSlotKind.Head, "Leather Cap")]
    [InlineData("Leather Cap (Blessed)", GearSetSlotKind.Head, "Leather Cap")]
    public void OldPieces_ResolveToTheirSet(string name, GearSetSlotKind kind, string family) =>
        GearSetFamilyResolver.FamilyOf(null, name, kind).Should().Be(family, name);

    [Theory]
    // the two counterexamples the original design named
    [InlineData("Forged-Thread Cape", GearSetSlotKind.Cloak)]
    [InlineData("Cloak of Shadows", GearSetSlotKind.Cloak)]
    // built-ins that contain a set template's name but are not set pieces (the supervisor's find)
    [InlineData("Studded Leather Cap", GearSetSlotKind.Head)]
    [InlineData("Reinforced Chain Boots", GearSetSlotKind.Feet)]
    [InlineData("Reinforced Chain Arms", GearSetSlotKind.Arms)]
    [InlineData("Reinforced Chain Legs", GearSetSlotKind.Legs)]
    [InlineData("Reinforced Chain Gloves", GearSetSlotKind.Hands)]
    // not a set template at all
    [InlineData("Studded Leather of the Sentinel", GearSetSlotKind.Body)]
    // a set template inside a longer word must not count
    [InlineData("Shadowforged Cloak", GearSetSlotKind.Cloak)]
    public void NamesThatOnlyContainATemplate_AreNotSetPieces(string name, GearSetSlotKind kind) =>
        GearSetFamilyResolver.FamilyOf(null, name, kind).Should().BeNull(name);

    [Fact]
    public void ModdedEquipment_IsACandidate_SoItsNameIsNotReadAsTheSetPieceItContains()
    {
        // Modded items (GameData/equipment.json) have IDs in the dynamic range; the catalog must still
        // hold them as non-set candidates. The live catalog is built once per process, so this builds
        // a fresh one with a modded item present and reads its head-slot candidates.
        var prop = typeof(GameDataLoader).GetProperty("CustomEquipment")!;
        var saved = prop.GetValue(null);
        try
        {
            var padded = Equipment.CreateArmor(200001, "Padded Leather Cap", EquipmentSlot.Head, ArmorType.Leather, 3, 100);
            prop.SetValue(null, new List<Equipment> { padded });
            var build = typeof(GearSetFamilyResolver).GetMethod("BuildCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var catalog = (System.Collections.IDictionary)build.Invoke(null, null)!;
            var head = ((System.Collections.IEnumerable)catalog[GearSetSlotKind.Head]!).Cast<object>()
                .Select(c => (string)c.GetType().GetProperty("Form")!.GetValue(c)!).ToList();
            head.Should().Contain("Padded Leather Cap");
            head.Should().Contain("Leather Cap");
        }
        finally { prop.SetValue(null, saved); }
    }

    [Fact]
    public void AMatchNeverCrossesSlots()
    {
        // "Leather Cap" is a head piece; the same words on a pair of boots or a ring are not
        GearSetFamilyResolver.FamilyOf(null, "Leather Cap", GearSetSlotKind.Feet).Should().BeNull();
        GearSetFamilyResolver.FamilyOf(null, "Leather Cap", GearSetSlotKind.Ring).Should().BeNull();
        GearSetFamilyResolver.FamilyOf(null, "Leather Cap", GearSetSlotKind.Head).Should().Be("Leather Cap");
    }

    [Fact]
    public void AStoredFamily_AlwaysWins_AndIsNeverRewritten()
    {
        GearSetFamilyResolver.FamilyOf("Chain Coif", "Empowering Leather Cap", GearSetSlotKind.Head).Should().Be("Chain Coif");
        var item = new Item { Name = "Fine Leather Leggings", Type = ObjType.Legs };
        GearSetFamilyResolver.FamilyOf(item).Should().Be("Leather Leggings");
        item.Family.Should().BeEmpty("the inference is read at the point of use and never written into the item");
    }

    [Fact]
    public void FourOldLeatherPieces_NowCountAsTheLeatherSet()
    {
        var hero = new Character { Name1 = "sets", Name2 = "Sets", Class = CharacterClass.Ranger, Level = 30, HP = 400, MaxHP = 400 };
        void Wear(string name, EquipmentSlot slot)
        {
            var e = new Equipment { Name = name, Slot = slot, ArmorClass = 10, Value = 500 };   // no Family: an item from before v1.1.0
            EquipmentDatabase.RegisterDynamic(e);
            hero.EquippedItems[slot] = e.Id;
        }
        Wear("Empowering Leather Cap", EquipmentSlot.Head);
        Wear("Leather Gloves of Protection", EquipmentSlot.Hands);
        Wear("Fine Leather Leggings", EquipmentSlot.Legs);
        Wear("Leather Boots of Immunity", EquipmentSlot.Feet);
        Wear("Studded Leather of the Sentinel", EquipmentSlot.Body);   // not a leather set piece

        var counts = GearSetRegistry.CountEquipped(hero);
        counts.Should().ContainSingle(c => c.Set.Id == "leather").Which.Count.Should().Be(4);
    }
}
