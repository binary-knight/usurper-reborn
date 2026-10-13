using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.1: a current save is never handled as an old save at load. The old-save handling (equipment
/// picked from the saved power values, base stats set from the current values) is for a save that
/// carries no base stats; a save that carries them keeps them, and gains no item.
/// Save data is built in memory with the game's own serializer; no save folder is read or written.
/// </summary>
[Collection("SharedGameSingletons")]
public class LoadBaseStats131Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly RomanceTrackerData _romanceBefore;

    public LoadBaseStats131Tests()
    {
        _romanceBefore = RomanceTracker.Instance.ToSaveData();
    }

    public void Dispose()
    {
        RomanceTracker.Instance.LoadFromSaveData(_romanceBefore);
    }

    // ---------- helpers ----------

    /// <summary>A character with all eleven base stats set and nothing equipped, recalculated.</summary>
    private static Character Hero(long bonusWeapPow, long bonusArmPow, CharacterClass cls = CharacterClass.Cleric)
    {
        var c = new Character
        {
            Name1 = "lb131", Name2 = "Load Base", Class = cls, Race = CharacterRace.Human,
            Level = 10, AI = CharacterAI.Human,
            BaseStrength = 30, BaseDexterity = 22, BaseConstitution = 24, BaseIntelligence = 26,
            BaseWisdom = 28, BaseCharisma = 18, BaseMaxHP = 150, BaseMaxMana = 90,
            BaseDefence = 14, BaseStamina = 20, BaseAgility = 25,
            BonusWeapPow = bonusWeapPow, BonusArmPow = bonusArmPow,
        };
        c.Inventory.Add(new Item { Name = "Plain Stone" });
        c.RecalculateStats();
        c.HP = c.MaxHP;
        c.Mana = c.MaxMana;
        return c;
    }

    private static long[] Bases(Character c) => new[]
    {
        c.BaseStrength, c.BaseDexterity, c.BaseConstitution, c.BaseIntelligence, c.BaseWisdom, c.BaseCharisma,
        c.BaseMaxHP, c.BaseMaxMana, c.BaseDefence, c.BaseStamina, c.BaseAgility,
    };

    private static long[] SavedBases(PlayerData d) => new[]
    {
        d.BaseStrength, d.BaseDexterity, d.BaseConstitution, d.BaseIntelligence, d.BaseWisdom, d.BaseCharisma,
        d.BaseMaxHP, d.BaseMaxMana, d.BaseDefence, d.BaseStamina, d.BaseAgility,
    };

    private static string SaveJson(Character c)
    {
        var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { c })!;
        return JsonSerializer.Serialize(data);
    }

    private static PlayerData Parse(string json) => JsonSerializer.Deserialize<PlayerData>(json)!;

    private static Character Load(PlayerData data)
    {
        try { return (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    // ---------- 1: a current save keeps its base stats and gains no item ----------

    [Theory]
    [InlineData(25, 0)]   // the weapon power side
    [InlineData(0, 25)]   // the armor power side
    public void CurrentSave_WithNothingEquipped_KeepsItsBaseStats_AndGainsNoItem(long bonusWeapPow, long bonusArmPow)
    {
        var hero = Hero(bonusWeapPow, bonusArmPow);
        var original = Bases(hero);
        var data = Parse(SaveJson(hero));

        // the save is a current one: it carries the base stats, an empty equipped dictionary, and a power value above 0
        SavedBases(data).Should().Equal(original);
        data.EquippedItems.Should().BeEmpty();
        (data.WeapPow + data.ArmPow).Should().Be(25);
        hero.MaxHP.Should().BeGreaterThan(hero.BaseMaxHP, "the derived value differs from the base, so an overwrite would show");

        var loaded = Load(data);

        Bases(loaded).Should().Equal(original, "every base stat is the saved base stat");
        loaded.EquippedItems.Should().BeEmpty("a current save with nothing equipped loads with nothing equipped");
        loaded.Inventory.Select(i => i.Name).Should().Equal(new[] { "Plain Stone" }, "the pack is unchanged");
    }

    [Theory]
    [InlineData(30, 0)]   // the weapon power side
    [InlineData(0, 12)]   // the armor power side
    public void CurrentSave_WithNothingEquipped_NoBonus_AndASavedPowerValueAboveZero_KeepsItsBaseStats_AndGainsNoItem(long savedWeapPow, long savedArmPow)
    {
        var hero = Hero(0, 0);
        var original = Bases(hero);
        var data = Parse(SaveJson(hero));
        // no permanent bonus stands behind the value: it is set on the save itself
        data.BonusWeapPow.Should().Be(0);
        data.BonusArmPow.Should().Be(0);
        data.WeapPow = savedWeapPow;
        data.ArmPow = savedArmPow;

        SavedBases(data).Should().Equal(original);
        data.EquippedItems.Should().BeEmpty();

        var loaded = Load(data);

        Bases(loaded).Should().Equal(original, "every base stat is the saved base stat, whatever the saved power value comes from");
        loaded.EquippedItems.Should().BeEmpty("a current save with nothing equipped loads with nothing equipped");
        loaded.Inventory.Select(i => i.Name).Should().Equal(new[] { "Plain Stone" }, "the pack is unchanged");
    }

    // ---------- 2: repeated loads and saves grow nothing ----------

    [Theory]
    [InlineData(25, 0)]
    [InlineData(0, 25)]
    public void CurrentSave_LoadedAndSavedThreeTimes_GrowsNoStat_WithATimedBuffAndTheAwakeningWisdomActive(long bonusWeapPow, long bonusArmPow)
    {
        var hero = Hero(bonusWeapPow, bonusArmPow);
        hero.AddTimedStatBuff("lb131 buff", StatKind.Strength, 5, StatBuffEnd.Rest);
        hero.AddTimedStatBuff("lb131 buff", StatKind.MaxHP, 20, StatBuffEnd.Rest);
        AwakeningBonus.Stamp(hero, 1);
        hero.RecalculateStats();

        var bases = Bases(hero);
        var derived = Derived(hero);
        hero.Strength.Should().Be(hero.BaseStrength + 5, "the timed buff is active at save time");
        hero.Wisdom.Should().Be(hero.BaseWisdom + AwakeningBonus.Wisdom1, "the awakening's Wisdom is active at save time");
        hero.MaxMana.Should().BeGreaterThan(hero.BaseMaxMana, "a mana class, so the mana check means something");

        string json = SaveJson(hero);
        for (int round = 1; round <= 3; round++)
        {
            var loaded = Load(Parse(json));
            // the session gives the loaded player its awakening stage and recalculates, as the load does
            AwakeningBonus.Stamp(loaded, 1);
            loaded.RecalculateStats();

            loaded.TimedStatBuffs.Should().HaveCount(2, $"the buffs are still on the character in round {round}");
            Bases(loaded).Should().Equal(bases, $"no base stat grows in round {round}");
            Derived(loaded).Should().Equal(derived, $"no attribute, MaxHP or MaxMana grows in round {round}");
            loaded.WeapPow.Should().Be(bonusWeapPow, $"round {round}");
            loaded.ArmPow.Should().Be(bonusArmPow, $"round {round}");
            loaded.EquippedItems.Should().BeEmpty($"no item appears in round {round}");
            json = SaveJson(loaded);
        }
    }

    // ---------- 3: a true old save is still migrated, once ----------

    [Fact]
    public void OldSave_WithoutBaseStatsOrEquippedDictionary_IsStillGivenEquipment_AndItsBaseStatsAreSetOnce()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(SaveJson(Hero(0, 0)))!.AsObject();
        foreach (var key in BaseKeys) node.Remove(key).Should().BeTrue($"{key} is a saved field");
        node.Remove("EquippedItems").Should().BeTrue();
        node["WeapPow"] = 30;
        node["ArmPow"] = 12;
        var old = Parse(node.ToJsonString());

        SavedBases(old).Should().OnlyContain(v => v == 0, "an old save carries no base stats");
        old.EquippedItems.Should().BeEmpty("an old save carries no equipped dictionary");

        var weapon = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded)
            .Where(w => w.WeaponPower <= 30).OrderByDescending(w => w.WeaponPower).First();
        var armor = EquipmentDatabase.GetBySlot(EquipmentSlot.Body)
            .Where(a => a.ArmorClass <= 12).OrderByDescending(a => a.ArmorClass).First();

        var loaded = Load(old);

        loaded.EquippedItems.Should().ContainKey(EquipmentSlot.MainHand).WhoseValue.Should().Be(weapon.Id);
        loaded.EquippedItems.Should().ContainKey(EquipmentSlot.Body).WhoseValue.Should().Be(armor.Id);
        var firstBases = Bases(loaded);
        firstBases.Should().Equal(new[]
        {
            old.Strength, old.Dexterity, old.Constitution, old.Intelligence, old.Wisdom, old.Charisma,
            old.MaxHP, old.MaxMana, old.Defence, old.Stamina, old.Agility,
        }, "the base stats of an old save are set from its saved values");

        // saved again it is a current save: the second load keeps the base stats and the two items
        var again = Load(Parse(SaveJson(loaded)));
        Bases(again).Should().Equal(firstBases, "the base stats are set once");
        again.EquippedItems.Should().HaveCount(2);
        again.EquippedItems[EquipmentSlot.MainHand].Should().Be(weapon.Id);
        again.EquippedItems[EquipmentSlot.Body].Should().Be(armor.Id);
    }

    // ---------- 4: saves the old rule never touched load as before ----------

    [Theory]
    [InlineData(true)]    // items equipped
    [InlineData(false)]   // nothing equipped and no bonus
    public void CurrentSave_TheOldRuleNeverMatched_LoadsFieldByFieldAsBefore(bool equipped)
    {
        var hero = Hero(0, 0);
        if (equipped)
        {
            hero.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded)
                .Where(w => w.WeaponPower > 0).OrderBy(w => w.WeaponPower).First().Id;
            hero.EquippedItems[EquipmentSlot.Body] = EquipmentDatabase.GetBySlot(EquipmentSlot.Body)
                .Where(a => a.ArmorClass > 0).OrderBy(a => a.ArmorClass).First().Id;
            hero.RecalculateStats();
        }
        var data = Parse(SaveJson(hero));

        // the rule of the commit before the fix and the rule after it, computed on the same save: neither matches,
        // so nothing in the load differs for this save
        bool oldRule = (data.EquippedItems == null || data.EquippedItems.Count == 0) && (data.WeapPow > 0 || data.ArmPow > 0);
        bool newRule = oldRule && SavedBases(data).All(v => v == 0);
        oldRule.Should().BeFalse();
        newRule.Should().BeFalse();
        if (equipped) (data.WeapPow > 0 && data.ArmPow > 0).Should().BeTrue("the equipped save carries power from its items");

        var loaded = Load(data);

        Bases(loaded).Should().Equal(SavedBases(data));
        Derived(loaded).Should().Equal(new[]
        {
            data.Strength, data.Dexterity, data.Constitution, data.Intelligence, data.Wisdom, data.Charisma,
            data.MaxHP, data.MaxMana, data.Defence, data.Stamina, data.Agility,
        });
        loaded.WeapPow.Should().Be(data.WeapPow);
        loaded.ArmPow.Should().Be(data.ArmPow);
        loaded.EquippedItems.ToDictionary(kv => (int)kv.Key, kv => kv.Value).Should().Equal(data.EquippedItems);
        loaded.Inventory.Select(i => i.Name).Should().Equal(new[] { "Plain Stone" });
    }

    // ---------- 5: the other load paths ----------

    [Theory]
    [InlineData(25, 0)]
    [InlineData(0, 25)]
    public void SharedCharacterLoader_KeepsTheSavedBaseStats_AndEquipsNothing(long bonusWeapPow, long bonusArmPow)
    {
        var hero = Hero(bonusWeapPow, bonusArmPow);
        var data = Parse(SaveJson(hero));

        var loaded = PlayerCharacterLoader.CreateFromSaveData(data, "Load Base");

        Bases(loaded).Should().Equal(Bases(hero));
        loaded.EquippedItems.Should().BeEmpty();
    }

    [Fact]
    public void OldSaveHandling_HasOneCallSite_BehindTheBaseStatCheck_AndNoOtherLoaderReachesIt()
    {
        string engine = Code("Core", "GameEngine.cs");
        Regex.Matches(engine, @"(?<!void )MigrateOldEquipmentToNew\(").Count.Should().Be(1, "one call site");
        int check = engine.IndexOf("if (!SaveCarriesBaseStats(playerData)", StringComparison.Ordinal);
        int call = engine.IndexOf("MigrateOldEquipmentToNew(player, playerData.WeapPow, playerData.ArmPow);", StringComparison.Ordinal);
        check.Should().BeGreaterThan(0);
        call.Should().BeInRange(check, check + 400, "the call sits behind the base stat check");
        Regex.Matches(engine, @"\.InitializeBaseStats\(\)").Count.Should().Be(1, "only the old-save handling sets base stats from current values in the engine");
        // the offline and the online load share the one restore
        Regex.Matches(engine, @"currentPlayer = RestorePlayerFromSaveData\(saveData\.Player\);").Count.Should().Be(2);

        foreach (var file in new[] { "PlayerCharacterLoader.cs", "WorldSimService.cs", "OnlineStateManager.cs", "SaveSystem.cs" })
        {
            string src = Code("Systems", file);
            src.Should().NotContain("MigrateOldEquipmentToNew", file);
            src.Should().NotContain("InitializeBaseStats", file);
        }
    }

    // ---------- more helpers ----------

    private static readonly string[] BaseKeys =
    {
        "BaseStrength", "BaseDexterity", "BaseConstitution", "BaseIntelligence", "BaseWisdom", "BaseCharisma",
        "BaseMaxHP", "BaseMaxMana", "BaseDefence", "BaseStamina", "BaseAgility",
    };

    private static long[] Derived(Character c) => new[]
    {
        c.Strength, c.Dexterity, c.Constitution, c.Intelligence, c.Wisdom, c.Charisma,
        c.MaxHP, c.MaxMana, c.Defence, c.Stamina, c.Agility,
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Code(params string[] parts)
    {
        string src = File.ReadAllText(Path.Combine(new[] { RepoRoot(), "Scripts" }.Concat(parts).ToArray()));
        src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(src, @"//[^\n]*", "");
    }
}
