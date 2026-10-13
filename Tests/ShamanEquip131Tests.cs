using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.1: the Mystic Shaman is not a prestige class for equipment. It is the last class value, and every test
/// "Class >= Tidesworn" counted it as one, so its class lists, its own main hand rule and its Medium armor
/// limit never ran. The equip check, the loot rule and the three shops now ask GameConfig.IsPrestigeClass.
/// Worn gear stays worn: nothing is taken off on load, on a recalculation or at the Magic Shop; once taken
/// off, an item the class cannot use cannot be put back on.
/// State lives in memory; the language, the king and the romance tracker are put back in Dispose and the real
/// save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class ShamanEquip131Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly King _prevKing = CastleLocation.GetCurrentKing();
    private readonly RomanceTrackerData _romanceBefore = RomanceTracker.Instance.ToSaveData();

    public ShamanEquip131Tests()
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(null!);   // no sales tax leaves the test
        EquipmentDatabase.Initialize();
    }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        CastleLocation.SetKing(_prevKing);
        RomanceTracker.Instance.LoadFromSaveData(_romanceBefore);
    }

    // ---------- helpers ----------

    private static readonly CharacterClass[] Prestige =
    {
        CharacterClass.Tidesworn, CharacterClass.Wavecaller, CharacterClass.Cyclebreaker,
        CharacterClass.Abysswarden, CharacterClass.Voidreaver,
    };

    public static IEnumerable<object[]> PrestigeClasses() => Prestige.Select(c => new object[] { c });

    /// <summary>Level, Strength and alignment never refuse: only the class rules decide.</summary>
    private static Character Probe(CharacterClass cls, CharacterRace race = CharacterRace.Human)
    {
        var c = new Character
        {
            Name1 = "se131", Name2 = "Shaman Equip", Class = cls, Race = race, Level = GameConfig.MaxLevel,
            AI = CharacterAI.Human, BaseStrength = 5000, Strength = 5000, BaseMaxHP = 500, MaxHP = 500, HP = 500,
            Gold = 9_000_000_000, AutoEquipDisabled = true,
        };
        c.Achievements.Unlock("first_enchant");
        return c;
    }

    private static Character Shaman() => Probe(CharacterClass.MysticShaman, CharacterRace.Troll);

    private static Equipment Weapon(WeaponType type, params CharacterClass[] classes) => new()
    {
        Name = $"Plain {type}", Slot = EquipmentSlot.MainHand, WeaponType = type, WeaponPower = 10, Value = 100,
        MinLevel = 1, Handedness = ShopItemGenerator.InferHandedness(type), ClassRestrictions = classes.ToList(),
    };

    private static Equipment Armor(ArmorWeightClass weight, params CharacterClass[] classes) => new()
    {
        Name = $"Plain {weight} Coat", Slot = EquipmentSlot.Body, ArmorClass = 9, Value = 100, MinLevel = 1,
        WeightClass = weight, ClassRestrictions = classes.ToList(),
    };

    private static Equipment Wear(Character hero, EquipmentSlot slot, Equipment item)
    {
        hero.EquippedItems[slot] = EquipmentDatabase.RegisterDynamic(item);
        hero.RecalculateStats();
        return item;
    }

    private static string ClassLine(CharacterClass c) => Loc.Get("ui.cannot_use_class", GameConfig.GetLocalizedClassName(c));

    /// <summary>The equip check with level, Strength, alignment and weight taken out: the class list and the
    /// Mystic Shaman's main hand rule are what is left.</summary>
    private static bool ClassRulesAdmit(Character who, Equipment item)
    {
        var copy = item.Clone();
        copy.MinLevel = 1;
        copy.StrengthRequired = 0;
        copy.RequiresGood = false;
        copy.RequiresEvil = false;
        copy.WeightClass = ArmorWeightClass.None;
        return copy.CanEquip(who, out _);
    }

    private static List<Equipment> ShopStock() =>
        EquipmentDatabase.GetAll().Where(e => EquipmentDatabase.IsShopGenerated(e.Id)).ToList();

    private static bool IsWeaponShopItem(Equipment e) => e.Slot == EquipmentSlot.MainHand || e.Slot == EquipmentSlot.OffHand;

    private static bool IsArmorShopItem(Equipment e) => e.Slot is EquipmentSlot.Body or EquipmentSlot.Head
        or EquipmentSlot.Arms or EquipmentSlot.Hands or EquipmentSlot.Legs or EquipmentSlot.Feet
        or EquipmentSlot.Waist or EquipmentSlot.Face or EquipmentSlot.Cloak;

    private static List<(string Name, string[] Classes, int Min, int Max)> Templates(string field)
    {
        var list = ((IEnumerable)typeof(LootGenerator).GetField(field, S)!.GetValue(null)!).Cast<object>().ToList();
        var t = list[0].GetType();
        return list.Select(o => ((string)t.GetField("Item1")!.GetValue(o)!, (string[])t.GetField("Item2")!.GetValue(o)!,
            (int)t.GetField("Item3")!.GetValue(o)!, (int)t.GetField("Item4")!.GetValue(o)!)).ToList();
    }

    private static readonly string[] ArmorLists =
    {
        "ArmorTemplates", "HeadArmorTemplates", "ArmsArmorTemplates", "HandsArmorTemplates", "LegsArmorTemplates",
        "FeetArmorTemplates", "WaistArmorTemplates", "FaceArmorTemplates", "CloakArmorTemplates",
    };

    private static bool Names(string[] classes, CharacterClass c) => classes.Contains("All") || classes.Contains(c.ToString());

    private static List<CharacterClass> ListOf(string[] classes) => classes.Contains("All")
        ? new List<CharacterClass>()
        : classes.Select(Enum.Parse<CharacterClass>).ToList();

    // ---------- 1. what a Mystic Shaman cannot newly equip ----------

    [Theory]
    [InlineData(WeaponType.Bow)]
    [InlineData(WeaponType.Staff)]
    [InlineData(WeaponType.Dagger)]
    [InlineData(WeaponType.Greatsword)]
    [InlineData(WeaponType.Instrument)]
    public void Shaman_CannotEquip_AMainHandWeaponItsRuleRefuses(WeaponType type)
    {
        var shaman = Shaman();
        var weapon = Weapon(type);   // no class list: the rule alone refuses it
        EquipmentDatabase.RegisterDynamic(weapon);

        weapon.CanEquip(shaman, out string reason).Should().BeFalse();
        reason.Should().Be(ClassLine(CharacterClass.MysticShaman));
        shaman.EquipItem(weapon, null, out string message).Should().BeFalse();
        message.Should().Be(reason);
        shaman.EquippedItems.Should().BeEmpty();
    }

    [Fact]
    public void Shaman_CannotEquip_HeavyArmor()
    {
        var shaman = Shaman();
        var plate = Armor(ArmorWeightClass.Heavy, CharacterClass.MysticShaman);   // the list admits it, the weight does not
        EquipmentDatabase.RegisterDynamic(plate);

        plate.CanEquip(shaman, out string reason).Should().BeFalse();
        reason.Should().Be(Loc.Get("ui.cannot_wear_weight", ArmorWeightClass.Heavy));
        shaman.EquipItem(plate, null, out _).Should().BeFalse();
        shaman.EquippedItems.Should().BeEmpty();
    }

    [Fact]
    public void Shaman_CannotEquip_AnItemWhoseClassListLacksIt()
    {
        var shaman = Shaman();
        var sword = Weapon(WeaponType.Sword, CharacterClass.Warrior, CharacterClass.Paladin);
        var coat = Armor(ArmorWeightClass.Light, CharacterClass.Magician);
        EquipmentDatabase.RegisterDynamic(sword);
        EquipmentDatabase.RegisterDynamic(coat);

        sword.CanEquip(shaman, out string swordReason).Should().BeFalse();
        swordReason.Should().Be(ClassLine(CharacterClass.MysticShaman));
        coat.CanEquip(shaman, out string coatReason).Should().BeFalse();
        coatReason.Should().Be(ClassLine(CharacterClass.MysticShaman));
        shaman.EquipItem(sword, null, out _).Should().BeFalse();
        shaman.EquipItem(coat, null, out _).Should().BeFalse();
        shaman.EquippedItems.Should().BeEmpty();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Shaman_RefusalRows_Fit79Columns(string language)
    {
        GameConfig.Language = language;
        var shaman = Shaman();

        Weapon(WeaponType.Bow).CanEquip(shaman, out string rule).Should().BeFalse();
        Armor(ArmorWeightClass.Heavy).CanEquip(shaman, out string weight).Should().BeFalse();
        var (lootOk, lootReason) = LootGenerator.CanClassUseLootItem(CharacterClass.MysticShaman,
            new Item { Name = "Longbow", Type = ObjType.Weapon });
        lootOk.Should().BeFalse();

        var rows = new[]
        {
            rule, weight, lootReason!,
            Loc.Get("weapon_shop.warning_cant_equip", rule),   // the weapon shop notice with the rule's line
        };
        foreach (var row in rows)
        {
            row.Should().NotBeNullOrWhiteSpace();
            row.Length.Should().BeLessThanOrEqualTo(79, $"'{row}' is one row in {language}");
        }
    }

    // ---------- 2. what a Mystic Shaman can equip ----------

    [Theory]
    [InlineData(WeaponType.Sword)]
    [InlineData(WeaponType.Axe)]
    [InlineData(WeaponType.Mace)]
    [InlineData(WeaponType.Hammer)]
    [InlineData(WeaponType.Flail)]
    [InlineData(WeaponType.Maul)]
    public void Shaman_CanEquip_TheWeaponsItsRuleAdmits(WeaponType type)
    {
        var shaman = Shaman();
        var weapon = Weapon(type, CharacterClass.Warrior, CharacterClass.MysticShaman);
        EquipmentDatabase.RegisterDynamic(weapon);

        weapon.CanEquip(shaman, out string reason).Should().BeTrue(reason);
        shaman.EquipItem(weapon, null, out _).Should().BeTrue();
        shaman.GetEquipment(EquipmentSlot.MainHand)!.Name.Should().Be(weapon.Name);
    }

    [Theory]
    [InlineData(ArmorWeightClass.Light)]
    [InlineData(ArmorWeightClass.Medium)]
    public void Shaman_CanEquip_LightAndMediumArmorThatAdmitsIt(ArmorWeightClass weight)
    {
        var shaman = Shaman();
        var coat = Armor(weight, CharacterClass.Ranger, CharacterClass.MysticShaman);
        EquipmentDatabase.RegisterDynamic(coat);

        coat.CanEquip(shaman, out string reason).Should().BeTrue(reason);
        shaman.EquipItem(coat, null, out _).Should().BeTrue();
        shaman.GetEquipment(EquipmentSlot.Body)!.Name.Should().Be(coat.Name);
    }

    // ---------- 3. the five prestige classes pass every class rule, as before ----------

    [Theory]
    [MemberData(nameof(PrestigeClasses))]
    public void PrestigeClass_StillPassesTheClassListTheWeightAndEveryMark(CharacterClass cls)
    {
        GameConfig.IsPrestigeClass(cls).Should().BeTrue();
        var hero = Probe(cls);
        var bow = Weapon(WeaponType.Bow, CharacterClass.Ranger);
        var staff = Weapon(WeaponType.Staff, CharacterClass.Magician);
        var plate = Armor(ArmorWeightClass.Heavy, CharacterClass.Warrior);
        var lute = Weapon(WeaponType.Instrument, CharacterClass.Bard);

        foreach (var item in new[] { bow, staff, plate, lute })
            item.CanEquip(hero, out string reason).Should().BeTrue($"{cls} passes every class rule ({reason})");

        WeaponShopLocation.MeetsClass(cls, bow).Should().BeTrue();
        WeaponShopLocation.MeetsClassList(cls, staff).Should().BeTrue();
        ArmorShopLocation.MeetsClass(cls, plate).Should().BeTrue();
        MusicShopLocation.CanEquipInstrument(cls).Should().BeTrue();
        foreach (var t in LootGenerator.GetWeaponTemplates().Select(t => t.Name).Concat(LootGenerator.GetShieldTemplates().Select(t => t.Name)))
        {
            var type = LootGenerator.GetShieldTemplates().Any(s => s.Name == t) ? ObjType.Shield : ObjType.Weapon;
            LootGenerator.CanClassUseLootItem(cls, new Item { Name = t, Type = type }).canUse.Should().BeTrue(t);
        }
    }

    // ---------- 4. worn gear stays worn; once off it cannot go back on ----------

    private static PlayerData Save(Character c)
    {
        var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { c })!;
        return JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
    }

    private static Character Load(PlayerData data)
    {
        try { return (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static string Worn(Character c) => string.Join(",", c.EquippedItems.Where(k => k.Value > 0)
        .OrderBy(k => k.Key).Select(k => $"{k.Key}:{c.GetEquipment(k.Key)?.Name}"));

    [Fact]
    public void Shaman_LoadedWearingABowAndHeavyArmor_KeepsBoth_AndCannotPutTheBowBackOn()
    {
        var shaman = Shaman();
        Wear(shaman, EquipmentSlot.MainHand, Weapon(WeaponType.Bow));
        Wear(shaman, EquipmentSlot.Body, Armor(ArmorWeightClass.Heavy));
        const string both = "Body:Plain Heavy Coat,MainHand:Plain Bow";
        Worn(shaman).Should().Be(both);
        shaman.GetEquipment(EquipmentSlot.MainHand)!.CanEquip(shaman, out _).Should().BeFalse("the class cannot newly equip it");
        shaman.GetEquipment(EquipmentSlot.Body)!.CanEquip(shaman, out _).Should().BeFalse("the class cannot newly equip it");

        // the game's own serializer, in memory, and the game's own load
        var loaded = Load(Save(shaman));
        loaded.Class.Should().Be(CharacterClass.MysticShaman);
        Worn(loaded).Should().Be(both, "the load takes nothing off");
        loaded.Inventory.Should().BeEmpty("nothing moved to the pack");

        loaded.RecalculateStats();
        Worn(loaded).Should().Be(both, "a recalculation takes nothing off");
        loaded.WeapPow.Should().BeGreaterThan(0, "the worn bow still counts");

        // the next login: saved and loaded again
        var again = Load(Save(loaded));
        again.RecalculateStats();
        Worn(again).Should().Be(both, "a second login takes nothing off");
        again.Inventory.Should().BeEmpty();

        // off, and it does not go back on
        var bow = again.UnequipSlot(EquipmentSlot.MainHand);
        bow.Should().NotBeNull();
        again.EquipItem(bow!, null, out string message).Should().BeFalse("once off, the class rule refuses it");
        message.Should().Be(ClassLine(CharacterClass.MysticShaman));
        Worn(again).Should().Be("Body:Plain Heavy Coat");
    }

    // ---------- 5. loot follows the same lists and the same main hand rule ----------

    [Fact]
    public void Loot_UsableForAShaman_IsWhatTheEquipCheckAdmits_ForEveryWeaponAndShieldTemplate()
    {
        var shaman = Shaman();
        int refused = 0, admitted = 0;
        foreach (var t in LootGenerator.GetWeaponTemplates())
        {
            var asEquipment = new Equipment
            {
                Name = t.Name, Slot = EquipmentSlot.MainHand, WeaponType = ShopItemGenerator.InferWeaponType(t.Name),
                MinLevel = 1, ClassRestrictions = ListOf(t.Classes),
            };
            bool equip = asEquipment.CanEquip(shaman, out _);
            var (loot, reason) = LootGenerator.CanClassUseLootItem(CharacterClass.MysticShaman, new Item { Name = t.Name, Type = ObjType.Weapon });
            loot.Should().Be(equip, $"{t.Name}: the loot rule and the equip check agree");
            if (loot) { admitted++; reason.Should().BeNull(); } else { refused++; reason.Should().NotBeNullOrWhiteSpace(); }
        }
        foreach (var t in LootGenerator.GetShieldTemplates())
        {
            var asEquipment = new Equipment
            {
                Name = t.Name, Slot = EquipmentSlot.OffHand, WeaponType = ShopItemGenerator.InferShieldType(t.Name),
                MinLevel = 1, ClassRestrictions = ListOf(t.Classes),
            };
            LootGenerator.CanClassUseLootItem(CharacterClass.MysticShaman, new Item { Name = t.Name, Type = ObjType.Shield }).canUse
                .Should().Be(asEquipment.CanEquip(shaman, out _), $"{t.Name}: the loot rule and the equip check agree");
        }
        admitted.Should().BeGreaterThan(0);
        refused.Should().BeGreaterThan(0, "the Shaman is no longer offered every weapon as usable");
    }

    [Theory]
    [InlineData("Longbow")]       // list "All", refused by the main hand rule
    [InlineData("Fine Dagger")]   // list "All", refused by the main hand rule
    public void Loot_AWeaponTheMainHandRuleRefuses_IsNotUsable_WithTheEquipChecksLine(string name)
    {
        var (canUse, reason) = LootGenerator.CanClassUseLootItem(CharacterClass.MysticShaman,
            new Item { Name = $"Blazing {name} of the Phoenix", Type = ObjType.Weapon });
        canUse.Should().BeFalse();
        reason.Should().Be(ClassLine(CharacterClass.MysticShaman));
        // another class with the same "All" list is not touched by the Shaman's rule
        LootGenerator.CanClassUseLootItem(CharacterClass.Ranger, new Item { Name = name, Type = ObjType.Weapon }).canUse.Should().BeTrue();
    }

    [Fact]
    public void Loot_DropsRolledForAShaman_ComeFromTemplatesThatNameIt()
    {
        var weaponTemplates = LootGenerator.GetWeaponTemplates().Select(t => (t.Name, t.Classes)).ToList();
        var armorTemplates = ArmorLists.SelectMany(Templates).Select(t => (t.Name, t.Classes)).ToList();
        // A rolled name is the template's name with a prefix or a suffix, and can hold a second template's name
        // too ("Holy" + "Mace"); the drop is from the Shaman's lists when one template in its name admits it.
        bool FromAShamanList(List<(string Name, string[] Classes)> all, string itemName) => all
            .Any(t => itemName.Contains(t.Name, StringComparison.OrdinalIgnoreCase) && Names(t.Classes, CharacterClass.MysticShaman));
        bool Known(List<(string Name, string[] Classes)> all, string itemName) =>
            all.Any(t => itemName.Contains(t.Name, StringComparison.OrdinalIgnoreCase));

        int weapons = 0, armors = 0;
        foreach (int level in new[] { 5, 20, 40, 75, 100 })
            for (int i = 0; i < 60; i++)
            {
                var weapon = LootGenerator.GenerateWeapon(level, CharacterClass.MysticShaman);
                if (Known(weaponTemplates, weapon.Name))
                {
                    weapons++;
                    FromAShamanList(weaponTemplates, weapon.Name).Should().BeTrue($"{weapon.Name} (level {level}) is rolled from a list that names the Shaman");
                }

                var armor = LootGenerator.GenerateArmor(level, CharacterClass.MysticShaman);
                if (armor.Type is ObjType.Fingers or ObjType.Neck or ObjType.Shield) continue;   // rolled without a class
                if (Known(armorTemplates, armor.Name))
                {
                    armors++;
                    FromAShamanList(armorTemplates, armor.Name).Should().BeTrue($"{armor.Name} (level {level}) is rolled from a list that names the Shaman");
                }
            }
        weapons.Should().BeGreaterThan(200);
        armors.Should().BeGreaterThan(100);
    }

    // ---------- 6. the shop marks match the equip check on the class list and the main hand rule ----------

    [Fact]
    public void WeaponShop_MarksForAShaman_MatchTheEquipCheck()
    {
        var shaman = Shaman();
        var stock = ShopStock().Where(IsWeaponShopItem).ToList();
        stock.Count.Should().BeGreaterThan(500);
        foreach (var item in stock)
            WeaponShopLocation.MeetsClass(CharacterClass.MysticShaman, item).Should().Be(ClassRulesAdmit(shaman, item),
                $"{item.Name} ({item.WeaponType}): the mark and the equip check agree");

        stock.Count(i => !WeaponShopLocation.MeetsClass(CharacterClass.MysticShaman, i)).Should().BeGreaterThan(0);
        stock.Where(i => i.WeaponType == WeaponType.Bow)
            .Should().OnlyContain(i => !WeaponShopLocation.MeetsClass(CharacterClass.MysticShaman, i), "no bow is marked usable");
        // the class list part alone still admits the "All" bows: the main hand rule is what marks them
        stock.Where(i => i.WeaponType == WeaponType.Bow)
            .Should().Contain(i => WeaponShopLocation.MeetsClassList(CharacterClass.MysticShaman, i));
    }

    [Fact]
    public void ArmorShop_MarksForAShaman_MatchTheEquipCheck_AndTheWeightIsTheOnlyDifferenceLeft()
    {
        var shaman = Shaman();
        var stock = ShopStock().Where(IsArmorShopItem).ToList();
        stock.Count.Should().BeGreaterThan(500);
        foreach (var item in stock)
            ArmorShopLocation.MeetsClass(CharacterClass.MysticShaman, item).Should().Be(ClassRulesAdmit(shaman, item),
                $"{item.Name}: the mark and the equip check agree on the class list");
        stock.Count(i => !ArmorShopLocation.MeetsClass(CharacterClass.MysticShaman, i)).Should().BeGreaterThan(0);

        // The armor shop marks no weight for any class. What is left: marked usable, refused by the weight.
        var markedButRefused = stock
            .Where(i => ArmorShopLocation.MeetsClass(CharacterClass.MysticShaman, i) && !i.CanEquip(shaman, out _))
            .ToList();
        markedButRefused.Should().OnlyContain(i => i.WeightClass == ArmorWeightClass.Heavy);
        markedButRefused.Select(i => i.Family).Distinct().OrderBy(n => n)
            .Should().Equal("Mithril Boots", "Mithril Helm", "Steel Faceplate");
    }

    [Fact]
    public void MusicShop_DoesNotPutAnInstrumentOnAShaman_AsTheEquipCheckWouldRefuseIt()
    {
        var shaman = Shaman();
        var instruments = ShopStock().Where(i => i.WeaponType == WeaponType.Instrument).ToList();
        instruments.Should().NotBeEmpty();

        MusicShopLocation.CanEquipInstrument(CharacterClass.MysticShaman).Should().BeFalse();
        instruments.Count(i => i.CanEquip(shaman, out _)).Should().Be(0);
        MusicShopLocation.CanEquipInstrument(CharacterClass.Bard).Should().BeTrue();
    }

    // ---------- 7. no other class's result changes ----------

    private static bool OldPrestige(CharacterClass c) => c >= CharacterClass.Tidesworn;   // the rule before 1.3.1

    private static bool OldListAdmits(CharacterClass c, Equipment e) =>
        OldPrestige(c) || e.ClassRestrictions == null || e.ClassRestrictions.Count == 0 || e.ClassRestrictions.Contains(c);

    /// <summary>The class part of the equip check as it was before 1.3.1 (the Shaman's main hand rule sat
    /// behind the prestige test and never ran).</summary>
    private static bool OldCanEquip(CharacterClass c, Equipment e) =>
        OldListAdmits(c, e)
        && (OldPrestige(c) || e.WeightClass == ArmorWeightClass.None || (int)e.WeightClass <= (int)GameConfig.GetMaxArmorWeight(c));

    private static List<Equipment> Sample()
    {
        var sample = ShopStock();
        foreach (var type in Enum.GetValues<WeaponType>().Where(t => t != WeaponType.None))
        {
            sample.Add(Weapon(type));
            sample.Add(Weapon(type, CharacterClass.Magician, CharacterClass.MysticShaman));
            sample.Add(Weapon(type, CharacterClass.Warrior));
        }
        foreach (var weight in Enum.GetValues<ArmorWeightClass>())
        {
            sample.Add(Armor(weight));
            sample.Add(Armor(weight, CharacterClass.Sage, CharacterClass.MysticShaman));
            sample.Add(Armor(weight, CharacterClass.Paladin));
        }
        return sample;
    }

    [Fact]
    public void NoOtherClass_ChangesInTheEquipCheck_TheMarksOrTheLootRule()
    {
        var sample = Sample();
        sample.Count.Should().BeGreaterThan(1500);
        var lootNames = LootGenerator.GetWeaponTemplates().Select(t => (t.Name, t.Classes, Type: ObjType.Weapon))
            .Concat(LootGenerator.GetShieldTemplates().Select(t => (t.Name, t.Classes, Type: ObjType.Shield))).ToList();
        int shamanChanged = 0;

        foreach (var cls in Enum.GetValues<CharacterClass>())
        {
            var probe = Probe(cls);
            foreach (var item in sample)
            {
                bool now = item.Clone() is var copy && Neutral(copy).CanEquip(probe, out _);
                bool old = OldCanEquip(cls, item);
                if (cls == CharacterClass.MysticShaman)
                {
                    old.Should().BeTrue("the Shaman passed every class rule before");
                    if (now != old) shamanChanged++;
                    continue;
                }
                now.Should().Be(old, $"{cls} / {item.Name}: the equip check is as before");
                if (IsWeaponShopItem(item))
                    WeaponShopLocation.MeetsClass(cls, item).Should().Be(OldListAdmits(cls, item), $"{cls} / {item.Name}: weapon shop mark");
                if (IsArmorShopItem(item))
                    ArmorShopLocation.MeetsClass(cls, item).Should().Be(OldListAdmits(cls, item), $"{cls} / {item.Name}: armor shop mark");
            }
            if (cls == CharacterClass.MysticShaman) continue;
            MusicShopLocation.CanEquipInstrument(cls).Should().Be(cls == CharacterClass.Bard || OldPrestige(cls));
            foreach (var t in lootNames)
            {
                // a name that stands twice with two lists is matched to one of them: asked of the game's own
                // answer for a class the change cannot touch, the lists of every template of that name
                bool oldLoot = OldPrestige(cls) || lootNames.Where(o => o.Name == t.Name && o.Type == t.Type).Any(o => Names(o.Classes, cls));
                bool nowLoot = LootGenerator.CanClassUseLootItem(cls, new Item { Name = t.Name, Type = t.Type }).canUse;
                if (lootNames.Count(o => o.Name == t.Name) == 1)
                    nowLoot.Should().Be(oldLoot, $"{cls} / {t.Name}: the loot rule is as before");
                else if (nowLoot)
                    oldLoot.Should().BeTrue($"{cls} / {t.Name}: the loot rule is as before");
            }
        }
        shamanChanged.Should().BeGreaterThan(0, "the Shaman is the one class whose result changes");
    }

    /// <summary>Level, Strength and alignment out of the way; the class list, the weapon type and the weight stay.</summary>
    private static Equipment Neutral(Equipment e)
    {
        e.MinLevel = 1;
        e.StrengthRequired = 0;
        e.RequiresGood = false;
        e.RequiresEvil = false;
        return e;
    }

    // ---------- 8. the Magic Shop keeps a Shaman's worn bow worn ----------

    private static readonly EquipmentSlot[] ShopOrder =
    {
        EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head, EquipmentSlot.Body,
        EquipmentSlot.Arms, EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet,
        EquipmentSlot.Waist, EquipmentSlot.Face, EquipmentSlot.Cloak,
        EquipmentSlot.Neck, EquipmentSlot.LFinger, EquipmentSlot.RFinger,
    };

    [Fact]
    public async Task Shaman_EnchantingAWornBowAtTheMagicShop_KeepsItWorn()
    {
        var shaman = Shaman();
        var bow = Wear(shaman, EquipmentSlot.MainHand, Weapon(WeaponType.Bow));
        bow.CanEquip(shaman, out _).Should().BeFalse("the class cannot newly equip the bow");
        int packBefore = shaman.Inventory.Count;
        long goldBefore = shaman.Gold;

        var shop = new MagicShopLocation();
        using var output = new MemoryStream();
        int row = ShopOrder.Where(s => shaman.GetEquipment(s) != null).ToList().IndexOf(EquipmentSlot.MainHand) + 1;
        var lines = new[] { $"{row}", "1", "4", "Y" }.Concat(Enumerable.Repeat("", 12));   // first tier, Defence
        var term = new TerminalEmulator(new LineStream(lines), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, shaman);
        var r = typeof(MagicShopLocation).GetMethod("EnchantEquipment", F)!.Invoke(shop, new object[] { shaman });
        if (r is Task t) await t;

        var worn = shaman.GetEquipment(EquipmentSlot.MainHand);
        worn.Should().NotBeNull("the enchanted bow is still worn");
        worn!.WeaponType.Should().Be(WeaponType.Bow);
        worn.GetEnchantmentCount().Should().Be(1, "the enchant is applied");
        shaman.Gold.Should().BeLessThan(goldBefore, "the enchant is paid for");
        shaman.EquippedItems.Count(k => k.Value > 0).Should().Be(1);
        shaman.Inventory.Count.Should().Be(packBefore, "nothing reaches the pack");
        worn.CanEquip(shaman, out _).Should().BeFalse("it is still an item the class cannot newly equip");
    }
}
