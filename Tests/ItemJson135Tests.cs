using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using Effect = global::LootGenerator.SpecialEffect;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.3.5 (X5): an Item kept as JSON in a database row keeps its LootEffects. The tuples are fields, which
/// System.Text.Json writes only with IncludeFields; the auction, the guild bank and the NPC market used the
/// default options and lost every effect. ItemJson holds the one shared setting per row shape.
/// </summary>
public class ItemJson135Tests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static readonly (int, int)[] ThreeEffects =
    {
        ((int)Effect.FireDamage, 12),
        ((int)Effect.LifeSteal, 3),
        ((int)Effect.CriticalStrike, 7),
    };

    /// <summary>An enchanted sword: fire, life steal and crit, with markers, a pre-enchant form and stats.</summary>
    private static Item EnchantedSword()
    {
        var item = new Item
        {
            Name = "Blazing Long Sword",
            Type = ObjType.Weapon,
            Value = 4200,
            Attack = 55,
            Strength = 6,
            Dexterity = 4,
            Rarity = EquipmentRarity.Epic,
            Family = "Long Sword",
            EnchantMarkers = "[E:2] [ES:fire,crit]",
            EnchantBase = "Long Sword|55|0",
            IsIdentified = true,
        };
        item.Description[0] = "Fire Damage +12";
        item.LootEffects = ThreeEffects.Select(e => (EffectType: e.Item1, Value: e.Item2)).ToList();
        return item;
    }

    private static void ShouldHoldTheSword(Item? read, bool withEffects)
    {
        read.Should().NotBeNull();
        read!.Name.Should().Be("Blazing Long Sword");
        read.Attack.Should().Be(55);
        read.Strength.Should().Be(6);
        read.Dexterity.Should().Be(4);
        read.Value.Should().Be(4200);
        read.Rarity.Should().Be(EquipmentRarity.Epic);
        read.Family.Should().Be("Long Sword");
        read.EnchantMarkers.Should().Be("[E:2] [ES:fire,crit]");
        read.EnchantBase.Should().Be("Long Sword|55|0");
        read.Description[0].Should().Be("Fire Damage +12");
        if (withEffects)
            read.LootEffects.Select(e => (e.EffectType, e.Value)).Should().Equal(ThreeEffects,
                "the fire, life steal and crit effects must come back as written");
        else
            read.LootEffects.Should().BeEmpty("an effect a pre-1.3.5 row lost must not come back as a (None, 0) entry");
    }

    // ---- F1 proof: IncludeFields newly writes only the LootEffects tuple fields ----

    private static void CollectPaths(JsonElement e, string path, SortedSet<string> into)
    {
        into.Add(path);
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject()) CollectPaths(p.Value, path + "." + p.Name, into);
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) CollectPaths(x, path + "[]", into);
                break;
        }
    }

    private static SortedSet<string> Paths(string json)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        CollectPaths(doc.RootElement, "$", set);
        return set;
    }

    [Fact]
    public void IncludeFields_NewlyWrites_OnlyTheLootEffectsTupleFields()
    {
        var item = EnchantedSword();
        var before = Paths(JsonSerializer.Serialize(item, new JsonSerializerOptions()));
        var after = Paths(ItemJson.WriteStore(item));

        before.Except(after).Should().BeEmpty("the shared setting must not drop any member the rows already hold");
        after.Except(before).Should().BeEquivalentTo(new[] { "$.LootEffects[].Item1", "$.LootEffects[].Item2" },
            "only the LootEffects tuple fields may be new in a row; any other new member is a change to the stored shape");
    }

    [Fact]
    public void TheInheritanceSetting_IsTheShapeItsRowsAlreadyHave()
    {
        var item = EnchantedSword();
        var old = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };
        JsonSerializer.Serialize(item, ItemJson.Inheritance).Should().Be(JsonSerializer.Serialize(item, old),
            "the inheritance queue already wrote camelCase with IncludeFields");
        ItemJson.WriteStore(item).Should().Contain("\"LootEffects\":[{\"Item1\":", "the store rows keep PascalCase names");
    }

    // ---- G3: each row shape reads back with what it holds ----

    [Fact]
    public void APascalCaseRow_WrittenWithTheDefaultOptions_ReadsBackWithWhatItHolds_AndNoEmptyEffect()
    {
        // the shape of an auction, guild bank or NPC market row written before 1.3.5
        string row = JsonSerializer.Serialize(EnchantedSword());
        row.Should().Contain("\"LootEffects\":[{},{},{}]", "the default options write each effect as an empty object");

        ShouldHoldTheSword(ItemJson.ReadStore(row), withEffects: false);
    }

    [Fact]
    public void ACamelCaseRow_WrittenWithIncludeFields_ReadsBackWithWhatItHolds()
    {
        // the shape of an inheritance row written before 1.3.5 (WorldSimulator, WorldBossSystem)
        var old = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };
        string row = JsonSerializer.Serialize(EnchantedSword(), old);
        row.Should().Contain("\"lootEffects\":[{\"item1\":");

        // GameEngine reads the inheritance queue with ItemJson.Inheritance
        ShouldHoldTheSword(JsonSerializer.Deserialize<Item>(row, ItemJson.Inheritance), withEffects: true);
    }

    // ---- each store keeps LootEffects through its write and its read ----

    private static readonly Regex ItemJsonCall = new(@"Deserialize<(?:global::)?Item>\(|JsonSerializer\.Serialize\(item\b");

    /// <summary>The Item JSON calls in a source file that do not go through the shared setting: neither an
    /// ItemJson helper nor an options variable the file sets to ItemJson.Inheritance.</summary>
    private static List<string> CallsOffTheSharedSetting(string relPath)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), relPath));
        var shared = lines
            .Select(l => Regex.Match(l, @"var\s+(\w+)\s*=\s*ItemJson\.Inheritance\s*;"))
            .Where(m => m.Success).Select(m => m.Groups[1].Value).ToHashSet();
        var off = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (!ItemJsonCall.IsMatch(lines[i])) continue;
            var opt = Regex.Match(lines[i], @"(?:Deserialize<(?:global::)?Item>|JsonSerializer\.Serialize)\([^,()]*(?:\.[\w]+)*\s*,\s*(\w+)\s*\)");
            if (opt.Success && shared.Contains(opt.Groups[1].Value)) continue;
            off.Add($"{relPath}:{i + 1}: {lines[i].Trim()}");
        }
        return off;
    }

    private static int Count(string relPath, string text) =>
        Regex.Matches(File.ReadAllText(Path.Combine(RepoRoot(), relPath)), Regex.Escape(text)).Count;

    private static void StoreRoundTrip(string store)
    {
        var read = ItemJson.ReadStore(ItemJson.WriteStore(EnchantedSword()));
        read.Should().NotBeNull(store);
        read!.LootEffects.Select(e => (e.EffectType, e.Value)).Should().Equal(ThreeEffects,
            $"the {store} must keep fire, life steal and crit through its write and its read");
        ShouldHoldTheSword(read, withEffects: true);
    }

    [Fact]
    public void TheAuction_KeepsLootEffects_ThroughItsWriteAndItsReads()
    {
        const string file = "Scripts/Locations/BaseLocation.cs";
        CallsOffTheSharedSetting(file).Should().BeEmpty("every auction write and read must use the shared setting");
        Count(file, "ItemJson.WriteStore(item)").Should().Be(1, "the listing write");
        Count(file, "ItemJson.ReadStore(").Should().Be(5, "the browse, buy, my-listings, cancel and expiry reads");
        StoreRoundTrip("auction");
    }

    [Fact]
    public void TheGuildBank_KeepsLootEffects_ThroughItsWriteAndItsReads()
    {
        const string file = "Scripts/Server/MudChatSystem.cs";
        CallsOffTheSharedSetting(file).Should().BeEmpty("every guild bank write and read must use the shared setting");
        Count(file, "ItemJson.WriteStore(item)").Should().Be(1, "the deposit");
        Count(file, "ItemJson.ReadStore(").Should().Be(2, "the bank list and the withdrawal");
        StoreRoundTrip("guild bank");
    }

    [Fact]
    public void TheNpcMarket_KeepsLootEffects_ThroughItsWriteAndItsRead()
    {
        const string file = "Scripts/Systems/WorldSimulator.cs";
        CallsOffTheSharedSetting(file).Should().BeEmpty("every NPC market and inheritance write and read must use the shared setting");
        Count(file, "ItemJson.WriteStore(item)").Should().Be(1, "the NPC listing");
        Count(file, "ItemJson.ReadStore(").Should().Be(1, "the NPC purchase reads an auction row");
        StoreRoundTrip("NPC market");
    }

    [Fact]
    public void TheInheritanceQueue_KeepsLootEffects_ThroughItsWritesAndItsRead()
    {
        foreach (var file in new[] { "Scripts/Systems/WorldSimulator.cs", "Scripts/Systems/WorldBossSystem.cs", "Scripts/Core/GameEngine.cs" })
            CallsOffTheSharedSetting(file).Should().BeEmpty($"{file}: every inheritance write and read must use the shared setting");

        string row = JsonSerializer.Serialize(EnchantedSword(), ItemJson.Inheritance);
        ShouldHoldTheSword(JsonSerializer.Deserialize<Item>(row, ItemJson.Inheritance), withEffects: true);
    }

    [Fact]
    public void ARealEffect_OfValueZeroOrOfKindNone_IsKept_OnlyTheEmptyPairIsDropped()
    {
        var item = EnchantedSword();
        item.LootEffects = new List<(int EffectType, int Value)> { ((int)Effect.FireDamage, 0), (0, 5), (0, 0) };
        ItemJson.ReadStore(ItemJson.WriteStore(item))!.LootEffects
            .Should().Equal(new List<(int EffectType, int Value)> { ((int)Effect.FireDamage, 0), (0, 5) });
    }
}
