using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 (E5, Phoenix Fire and Frostbite worth it): the shop's Phoenix Fire and Frostbite add 30 weapon power and
/// write the kind codes "phoenix" and "frostbite"; an item enchanted before keeps "fire" or "frost" and its +20. Both
/// go on a main or off hand weapon only, one of each element per item. A weapon whose kinds hold either code of an
/// element takes the new chances (Phoenix Fire 0.30 for 0.25 of the hit, Frostbite 0.25) on the dungeon, PvP and
/// world boss steps; a loot fire or frost weapon keeps 0.20 for 0.15 and 0.15. The four sites that read what an
/// enchant added read it by the item's own kind code.
/// Every item this class registers is removed when its test ends, and every name is 30 characters or fewer.
/// </summary>
[Collection("SharedGameSingletons")]
public class ElementWorth136Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
    private const int Phoenix = 13, Frostbite = 14;
    private const long D = 1000;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static readonly List<int> Registered = new();
    private readonly string _prevLanguage = GameConfig.Language;
    private readonly King _prevKing = CastleLocation.GetCurrentKing();
    private readonly Random _prevRandom = MagicShopLocation.EnchantRandom;

    public ElementWorth136Tests()
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(null!);   // no sales tax leaves the test
        MagicShopLocation.EnchantRandom = new Coverage136Random(0.99);
    }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        CastleLocation.SetKing(_prevKing);
        MagicShopLocation.EnchantRandom = _prevRandom;
        var db = typeof(EquipmentDatabase);
        var all = (Dictionary<int, Equipment>)db.GetField("_allEquipment", S)!.GetValue(null)!;
        var gate = db.GetField("_lock", S)!.GetValue(null)!;
        lock (Registered)
        {
            lock (gate)
                foreach (int id in Registered)
                    all.Remove(id);
            Registered.Clear();
        }
    }

    // ---------- items ----------

    private static int Register(Equipment e)
    {
        e.Name.Length.Should().BeLessOrEqualTo(30, "a test item name stays short");
        int id = EquipmentDatabase.RegisterDynamic(e);
        lock (Registered) Registered.Add(id);
        return id;
    }

    private static Equipment Blade(string name = "E136 Blade") => new()
    {
        Name = name, Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Sword, Handedness = WeaponHandedness.OneHanded,
        WeaponPower = 50, MinLevel = 1, Value = 100, Rarity = EquipmentRarity.Common,
    };

    /// <summary>A weapon as the shop left it: the base recorded, then each kind code with its own delta (and a stat
    /// kind "pow" with its bonus).</summary>
    private static Equipment Enchanted(params string[] kinds)
    {
        var e = Blade();
        e.RecordEnchantBase();
        foreach (var k in kinds)
        {
            e.IncrementEnchantmentCount();
            e.AddEnchantedKind(k);
            if (k == "pow") e.WeaponPower += 24;
            else MagicShopLocation.ApplyNamedKind(e, k);
        }
        return e;
    }

    /// <summary>An item as a save from before 1.3.6 holds it: the shop's "fire" or "frost" kind and +20 in its power.</summary>
    private static Equipment OldElement(string kind)
    {
        var e = Blade("E136 Old " + kind);
        e.RecordEnchantBase();
        e.IncrementEnchantmentCount();
        e.AddEnchantedKind(kind);
        e.WeaponPower += 20;
        if (kind == "fire") e.HasFireEnchant = true; else e.HasFrostEnchant = true;
        return e;
    }

    // ---------- the shop ----------

    private static Character Hero(int level = 100)
    {
        var hero = new Character
        {
            Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
            HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, BaseStrength = 100, Strength = 100,
            AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
        };
        hero.Achievements.Unlock("first_enchant");
        hero.Achievements.Unlock("master_enchanter");
        foreach (var m in GameConfig.CraftingMaterials) hero.AddMaterial(m.Id, 5);
        return hero;
    }

    private static Equipment Wear(Character hero, Equipment item)
    {
        hero.EquippedItems[item.Slot] = Register(item);
        hero.RecalculateStats();
        return item;
    }

    private static async Task<string> Run(Character hero, string method, params string[] lines)
    {
        var shop = new MagicShopLocation();
        using var output = new MemoryStream();
        TerminalEmulator term = null!;
        term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            term.StreamWriterInternal?.Flush();
            output.WriteByte((byte)'\n');
        }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
        var r = typeof(MagicShopLocation).GetMethod(method, F)!.Invoke(shop, new object[] { hero });
        if (r is Task t) await t;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
    }

    private static Task<string> Enchant(Character hero, int tier) => Run(hero, "EnchantEquipment", "1", $"{tier}", "Y");

    private static async Task<T> InLangAsync<T>(string lang, Func<Task<T>> body)
    {
        string saved = GameConfig.Language;
        try { GameConfig.Language = lang; return await body(); }
        finally { GameConfig.Language = saved; }
    }

    private static string Fields(Equipment e) =>
        $"pow {e.WeaponPower} ac {e.ArmorClass} str {e.StrengthBonus} fire {e.HasFireEnchant} frost {e.HasFrostEnchant} "
        + $"| {e.Name} | E{e.GetEnchantmentCount()} | {string.Join(",", e.GetEnchantedKinds())} | v{e.Value}";

    [Theory]
    [InlineData(Phoenix, "phoenix")]
    [InlineData(Frostbite, "frostbite")]
    public async Task ElementWorth136_Shop_AddsThirtyWeaponPower_AndWritesTheNewCode(int tier, string code)
    {
        var hero = Hero();
        Wear(hero, Blade());
        long gold = hero.Gold;
        await Enchant(hero, tier);
        var w = hero.GetEquipment(EquipmentSlot.MainHand)!;
        w.WeaponPower.Should().Be(50 + 30, "a new enchant adds 30 weapon power to the stored power");
        w.GetEnchantedKinds().Should().Equal(new[] { code }, "an enchant from 1.3.6 writes the new kind code");
        (tier == Phoenix ? w.HasFireEnchant : w.HasFrostEnchant).Should().BeTrue();
        hero.Gold.Should().BeLessThan(gold, "the enchant is paid");
    }

    [Fact]
    public async Task ElementWorth136_Shop_OneOfEachElement_PhoenixThenFrostbite()
    {
        var hero = Hero();
        Wear(hero, Blade());
        await Enchant(hero, Phoenix);
        await Enchant(hero, Frostbite);
        var w = hero.GetEquipment(EquipmentSlot.MainHand)!;
        w.WeaponPower.Should().Be(50 + 30 + 30, "a fire and a frost enchant on one weapon");
        w.GetEnchantedKinds().Should().Equal("phoenix", "frostbite");
    }

    public static IEnumerable<object[]> NotAWeapon() => new[]
    {
        new object[] { Phoenix, "armour" }, new object[] { Frostbite, "armour" },
        new object[] { Phoenix, "shield" }, new object[] { Frostbite, "shield" },
    };

    [Theory]
    [MemberData(nameof(NotAWeapon))]
    public async Task ElementWorth136_Shop_RefusedOffAWeapon_NoGoldTaken(int tier, string what)
    {
        var hero = Hero();
        var item = what == "armour"
            ? new Equipment { Name = "E136 Cap", Slot = EquipmentSlot.Head, ArmorClass = 5, MinLevel = 1, Value = 100 }
            : new Equipment { Name = "E136 Shield", Slot = EquipmentSlot.OffHand, WeaponType = WeaponType.Shield, ShieldBonus = 10, MinLevel = 1, Value = 100 };
        Wear(hero, item);
        string before = Fields(hero.GetEquipment(item.Slot)!);
        long gold = hero.Gold;
        string text = await Enchant(hero, tier);
        text.Should().Contain("go on a weapon only", "Phoenix Fire and Frostbite are refused on {0}", what);
        hero.Gold.Should().Be(gold, "no gold is taken on a refusal");
        Fields(hero.GetEquipment(item.Slot)!).Should().Be(before);
    }

    [Theory]
    [InlineData(Phoenix)]
    [InlineData(Frostbite)]
    public async Task ElementWorth136_Shop_RefusedOnALootElementWeapon_NoGoldTaken(int tier)
    {
        var hero = Hero();
        var loot = Blade("E136 Loot");
        if (tier == Phoenix) loot.HasFireEnchant = true; else loot.HasFrostEnchant = true;
        Wear(hero, loot);
        string before = Fields(hero.GetEquipment(EquipmentSlot.MainHand)!);
        long gold = hero.Gold;
        string text = await Enchant(hero, tier);
        text.Should().Contain("already carries that element", "a loot fire or frost weapon takes no second of that element");
        hero.Gold.Should().Be(gold, "no gold is taken on a refusal");
        Fields(hero.GetEquipment(EquipmentSlot.MainHand)!).Should().Be(before);
    }

    [Theory]
    [InlineData(Phoenix, "fire")]
    [InlineData(Phoenix, "phoenix")]
    [InlineData(Frostbite, "frost")]
    [InlineData(Frostbite, "frostbite")]
    public async Task ElementWorth136_Shop_SameElement_EitherCode_IsTheSameEnchant(int tier, string code)
    {
        var hero = Hero();
        Wear(hero, code is "fire" or "frost" ? OldElement(code) : Enchanted(code));
        string before = Fields(hero.GetEquipment(EquipmentSlot.MainHand)!);
        long gold = hero.Gold;
        string text = await Enchant(hero, tier);
        text.Should().Contain("already carries that enchantment", "\"{0}\" is the same enchant as tier {1}", code, tier);
        hero.Gold.Should().Be(gold, "no gold is taken on a refusal");
        Fields(hero.GetEquipment(EquipmentSlot.MainHand)!).Should().Be(before);
    }

    // ---------- the kind codes ----------

    private static int NamedTierOf(string kind) =>
        (int)typeof(MagicShopLocation).GetMethod("NamedTierOf", S)!.Invoke(null, new object[] { kind })!;

    [Fact]
    public void ElementWorth136_Codes_BothCodesOfAnElementAreOneTier_EachWithItsWeaponPower()
    {
        NamedTierOf("fire").Should().Be(Phoenix);
        NamedTierOf("phoenix").Should().Be(Phoenix);
        NamedTierOf("frost").Should().Be(Frostbite);
        NamedTierOf("frostbite").Should().Be(Frostbite);
        MagicShopLocation.ElementKindWeaponPower("fire").Should().Be(20);
        MagicShopLocation.ElementKindWeaponPower("frost").Should().Be(20);
        MagicShopLocation.ElementKindWeaponPower("phoenix").Should().Be(30);
        MagicShopLocation.ElementKindWeaponPower("frostbite").Should().Be(30);
        MagicShopLocation.ElementKindWeaponPower("pow").Should().Be(0);
        foreach (var code in new[] { "phoenix", "frostbite" })
            Regex.IsMatch(code, "^[a-z]+$").Should().BeTrue("the [ES:...] marker holds lowercase letters only");
    }

    [Theory]
    [InlineData(Phoenix, "phoenix")]
    [InlineData(Frostbite, "frostbite")]
    public async Task ElementWorth136_Codes_SurviveTheBackpack(int tier, string code)
    {
        var hero = Hero();
        Wear(hero, Blade());
        await Enchant(hero, tier);
        var w = hero.GetEquipment(EquipmentSlot.MainHand)!;
        var item = hero.ConvertEquipmentToLegacyItem(w);
        var saved = InventoryItemData.FromItem(item);
        var json = System.Text.Json.JsonSerializer.Deserialize<InventoryItemData>(System.Text.Json.JsonSerializer.Serialize(saved))!;
        var back = Character.BuildEquipmentFromItem(json.ToItem(), EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword);
        back.GetEnchantedKinds().Should().Equal(new[] { code }, "the Item markers carry the new code through the bag");
        back.WeaponPower.Should().Be(80);
    }

    // ---------- W1 (b): an item enchanted before keeps +20 and takes the new chances ----------

    private static PlayerData Save(Character hero) =>
        (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;

    private static Character Reload(Character hero)
    {
        var data = System.Text.Json.JsonSerializer.Deserialize<PlayerData>(System.Text.Json.JsonSerializer.Serialize(Save(hero)))!;
        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
        try { return (Character)restore.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    /// <summary>The test's load step: a save and reload, and the weapon the reloaded hero wears.</summary>
    private static (Character Hero, Equipment Weapon) LoadStep(Character hero)
    {
        var loaded = Reload(hero);
        var w = loaded.GetEquipment(EquipmentSlot.MainHand)!;
        lock (Registered) Registered.Add(w.Id);
        return (loaded, w);
    }

    [Theory]
    [InlineData("fire")]
    [InlineData("frost")]
    public void ElementWorth136_W1b_AnOldItemKeepsTwenty_AfterASaveAndLoad_AndTakesTheNewChance(string kind)
    {
        var hero = Hero();
        Wear(hero, OldElement(kind));
        var (loaded, w) = LoadStep(hero);
        w.WeaponPower.Should().Be(50 + 20, "an item enchanted before 1.3.6 keeps its stored +20 through a save and load");
        w.GetEnchantedKinds().Should().Equal(new[] { kind });
        if (kind == "fire")
            PvE(loaded, Plain(), 0.25).Loss.Should().Be(250, "an old Phoenix Fire weapon takes the new 30 percent for a quarter of the hit");
        else
            PvE(loaded, Plain(), 0.20).Monster.IsSlowed.Should().BeTrue("an old Frostbite weapon takes the new 25 percent");
    }

    // ---------- the dungeon step ----------

    private static Character Fighter(Equipment weapon)
    {
        var c = new Character
        {
            Name1 = "Hero", Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 60,
            Strength = 200, HP = 1_000, MaxHP = 1_000_000, CombatSpeed = CombatSpeed.Instant, AI = CharacterAI.Human,
        };
        c.EquippedItems[EquipmentSlot.MainHand] = Register(weapon);
        return c;
    }

    private static Equipment Loot(string kind)
    {
        var e = Blade("E136 Loot " + kind);
        if (kind == "fire") e.HasFireEnchant = true; else e.HasFrostEnchant = true;
        return e;
    }

    /// <summary>A shop element weapon by its code: "fire" and "frost" as a save from before holds them, the new codes
    /// as the shop writes them now.</summary>
    private static Equipment ShopElement(string code) => code is "fire" or "frost" ? OldElement(code) : Enchanted(code);

    private static Monster Plain() => new()
    {
        Name = "Cave Bear", Level = 60, HP = 10_000_000, MaxHP = 10_000_000, Strength = 10, Defence = 0, ArmPow = 0, IsActive = true,
    };

    private static Monster Boss() => new()
    {
        Name = "Terravok", Level = 60, HP = 10_000_000, MaxHP = 10_000_000, Strength = 10, Defence = 0, ArmPow = 0,
        IsBoss = true, IsActive = true,
    };

    private static CombatEngine Engine(Character player, Random random)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, random);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
        return engine;
    }

    /// <summary>The real dungeon post-hit step on a hit of D, every draw at <paramref name="roll"/>.</summary>
    private static (long Loss, Monster Monster, CombatEngine Engine) PvE(Character hero, Monster m, double roll, int n = 99)
    {
        var engine = Engine(hero, new Coverage136Random(roll, n));
        typeof(CombatEngine).GetMethod("ApplyPostHitEnchantments", F)!
            .Invoke(engine, new object[] { hero, m, D, new CombatResult { Player = hero }, false, EquipmentSlot.MainHand, false });
        return (m.MaxHP - m.HP, m, engine);
    }

    [Theory]
    [InlineData("fire")]
    [InlineData("phoenix")]
    public void ElementWorth136_PvE_PhoenixFire_ThirtyPercent_ForAQuarterOfTheHit(string code)
    {
        PvE(Fighter(ShopElement(code)), Plain(), 0.29).Loss.Should().Be(250, "Phoenix Fire (\"{0}\") procs below 0.30 for 0.25 of the hit", code);
        PvE(Fighter(ShopElement(code)), Plain(), 0.25).Loss.Should().Be(250);
        PvE(Fighter(ShopElement(code)), Plain(), 0.31).Loss.Should().Be(0, "no proc at or past 0.30");
    }

    [Fact]
    public void ElementWorth136_PvE_LootFire_KeepsTwentyPercent_ForFifteen()
    {
        PvE(Fighter(Loot("fire")), Plain(), 0.25).Loss.Should().Be(0, "a loot fire weapon keeps the 0.20 chance");
        PvE(Fighter(Loot("fire")), Plain(), 0.19).Loss.Should().Be(150, "a loot fire weapon keeps 0.15 of the hit");
    }

    [Theory]
    [InlineData("frost")]
    [InlineData("frostbite")]
    public void ElementWorth136_PvE_Frostbite_SlowsAtTwentyFivePercent(string code)
    {
        var slowed = PvE(Fighter(ShopElement(code)), Plain(), 0.20).Monster;
        slowed.IsSlowed.Should().BeTrue("Frostbite (\"{0}\") slows below 0.25", code);
        slowed.SlowDuration.Should().Be(GameConfig.FrostEnchantDuration);
        PvE(Fighter(ShopElement(code)), Plain(), 0.26).Monster.IsSlowed.Should().BeFalse("no slow past 0.25");
    }

    [Fact]
    public void ElementWorth136_PvE_LootFrost_KeepsFifteenPercent()
    {
        PvE(Fighter(Loot("frost")), Plain(), 0.20).Monster.IsSlowed.Should().BeFalse("a loot frost weapon keeps the 0.15 chance");
        PvE(Fighter(Loot("frost")), Plain(), 0.14).Monster.IsSlowed.Should().BeTrue();
    }

    [Fact]
    public void ElementWorth136_PvE_FrostbiteOnABoss_TakesTheSoftControlResist_AndCountsOnceARound()
    {
        // Next(100) at 0 is under the boss resist: the proc is resisted; at 99 the boss takes half the rounds, at least 1
        PvE(Fighter(Enchanted("frostbite")), Boss(), 0.20, n: 0).Monster.IsSlowed.Should().BeFalse("a boss resists the Frostbite slow");
        var hero = Fighter(Enchanted("frostbite"));
        var (_, boss, engine) = PvE(hero, Boss(), 0.20, n: 99);
        boss.IsSlowed.Should().BeTrue();
        boss.SlowDuration.Should().Be(Math.Max(1, GameConfig.FrostEnchantDuration / 2), "a boss takes the soft control rounds");

        // a regular monster slowed for the full rounds, acting twice in one round, loses one round
        hero.HP = 1_000_000;
        var (_, bear, e2) = PvE(hero, Plain(), 0.20);
        bear.SlowDuration.Should().Be(GameConfig.FrostEnchantDuration);
        e2.SeedRandomForTests(136);
        var process = typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!;
        bear.StatusTickedThisRound = false;
        for (int a = 0; a < 2; a++)
            ((Task)process.Invoke(e2, new object?[] { bear, hero, new CombatResult { Player = hero }, null })!).GetAwaiter().GetResult();
        bear.SlowDuration.Should().Be(GameConfig.FrostEnchantDuration - 1, "the Frostbite slow counts once a round");
    }

    // ---------- the PvP step ----------

    private static Character Duelist(string name, CharacterAI ai, Equipment? weapon = null)
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, AI = ai, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 30,
            HP = 100_000, MaxHP = 100_000, BaseMaxHP = 100_000, Strength = 300, BaseStrength = 300, Defence = 10, BaseDefence = 10,
            CombatSpeed = CombatSpeed.Instant,
        };
        if (weapon != null) c.EquippedItems[EquipmentSlot.MainHand] = Register(weapon);
        return c;
    }

    /// <summary>The real PvP post-hit step on a hit of D, every draw at <paramref name="roll"/>.</summary>
    private static Character PvP(Equipment weapon, double roll)
    {
        var a = Duelist("Attacker", CharacterAI.Human, weapon);
        var d = Duelist("Defender", CharacterAI.Computer);
        var engine = Engine(a, new Coverage136Random(roll));
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, a);
        typeof(CombatEngine).GetMethod("ApplyPvPPostHit", F)!.Invoke(engine, new object[] { a, d, D, EquipmentSlot.MainHand, false });
        return d;
    }

    [Theory]
    [InlineData("fire")]
    [InlineData("phoenix")]
    public void ElementWorth136_PvP_PhoenixFire_ThirtyPercent_ForAQuarter(string code)
    {
        var d = PvP(ShopElement(code), 0.25);
        (d.MaxHP - d.HP).Should().Be(250, "the PvP step takes Phoenix Fire's (\"{0}\") 0.30 for 0.25", code);
        PvP(Loot("fire"), 0.25).HP.Should().Be(100_000, "a loot fire weapon keeps 0.20 in PvP");
        var loot = PvP(Loot("fire"), 0.19);
        (loot.MaxHP - loot.HP).Should().Be(150, "a loot fire weapon keeps 0.15 of the hit in PvP");
    }

    [Theory]
    [InlineData("frost")]
    [InlineData("frostbite")]
    public void ElementWorth136_PvP_Frostbite_TwentyFivePercent(string code)
    {
        PvP(ShopElement(code), 0.20).HasStatus(StatusEffect.Slow).Should().BeTrue("the PvP step takes Frostbite's (\"{0}\") 0.25", code);
        PvP(ShopElement(code), 0.26).HasStatus(StatusEffect.Slow).Should().BeFalse();
        PvP(Loot("frost"), 0.20).HasStatus(StatusEffect.Slow).Should().BeFalse("a loot frost weapon keeps 0.15 in PvP");
    }

    // ---------- the world boss step ----------

    [Theory]
    [InlineData("fire")]
    [InlineData("phoenix")]
    public void ElementWorth136_WorldBoss_PhoenixFire_ThirtyPercent_ForAQuarter(string code)
    {
        WorldBossSystem.WeaponHitRiders(Duelist("Raider", CharacterAI.Human, ShopElement(code)), D, "The Boss", new Coverage136Random(0.25), null)
            .Should().Be(250, "the world boss step takes Phoenix Fire's (\"{0}\") 0.30 for 0.25", code);
        WorldBossSystem.WeaponHitRiders(Duelist("Raider", CharacterAI.Human, Loot("fire")), D, "The Boss", new Coverage136Random(0.25), null)
            .Should().Be(0, "a loot fire weapon keeps 0.20 on the world boss");
        WorldBossSystem.WeaponHitRiders(Duelist("Raider", CharacterAI.Human, Loot("fire")), D, "The Boss", new Coverage136Random(0.19), null)
            .Should().Be(150, "a loot fire weapon keeps 0.15 of the hit on the world boss");
    }

    // ---------- (d) the four delta sites read by kind ----------

    private static Equipment Template()
    {
        var t = EquipmentDatabase.GetBuiltInTemplates().First(e => e.Slot == EquipmentSlot.MainHand && e.WeaponPower > 0
            && !e.HasFireEnchant && !e.HasFrostEnchant && MagicShopLocation.StripEnchantTags(e.Name) == e.Name);
        return t;
    }

    /// <summary>A pre-1.2.5 item (no record): the template plus each element kind with the given power.</summary>
    private static Equipment Legacy(params (string Kind, int Power)[] kinds)
    {
        var e = Template().Clone();
        foreach (var (kind, power) in kinds)
        {
            e.IncrementEnchantmentCount();
            e.AddEnchantedKind(kind);
            e.WeaponPower += power;
            if (MagicShopLocation.IsPhoenixKind(kind)) { e.HasFireEnchant = true; e.Name += " (Phoenix Fire)"; }
            else { e.HasFrostEnchant = true; e.Name += " (Frostbite)"; }
        }
        e.Value += 1000;
        return e;
    }

    [Fact]
    public void ElementWorth136_Delta_Template_ReadsEachKindsOwnPower()
    {
        MagicShopLocation.VerifiedTemplate(Legacy(("fire", 20))).Should().NotBeNull("an old Phoenix Fire item (+20) matches its template");
        MagicShopLocation.VerifiedTemplate(Legacy(("frost", 20))).Should().NotBeNull("an old Frostbite item (+20) matches its template");
        MagicShopLocation.VerifiedTemplate(Legacy(("phoenix", 30))).Should().NotBeNull("a \"phoenix\" item (+30) matches its template");
        MagicShopLocation.VerifiedTemplate(Legacy(("frostbite", 30))).Should().NotBeNull("a \"frostbite\" item (+30) matches its template");
        MagicShopLocation.VerifiedTemplate(Legacy(("fire", 20), ("frostbite", 30))).Should().NotBeNull("old fire and new frostbite");
        MagicShopLocation.VerifiedTemplate(Legacy(("frost", 20), ("phoenix", 30))).Should().NotBeNull("old frost and new phoenix");
        MagicShopLocation.VerifiedTemplate(Legacy(("fire", 30))).Should().BeNull("a \"fire\" item is +20, exactly");
        MagicShopLocation.VerifiedTemplate(Legacy(("phoenix", 20))).Should().BeNull("a \"phoenix\" item is +30, exactly");
    }

    private sealed class Pick : Random
    {
        private readonly int _i;
        public Pick(int i) { _i = i; }
        public override int Next(int maxValue) => Math.Min(_i, maxValue - 1);
        public override double NextDouble() => 0.0;
    }

    [Theory]
    [InlineData("fire")]
    [InlineData("phoenix")]
    public void ElementWorth136_Delta_DestroyFire_TakesExactlyItsOwnPower(string code)
    {
        var item = code == "fire" ? OldElement("fire") : Enchanted("phoenix");
        var (after, kind) = MagicShopLocation.DestroyOneEnchant(item, new Pick(0));
        kind.Should().Be(code);
        after.WeaponPower.Should().Be(50, "destroying \"{0}\" takes exactly what it added", code);
        after.HasFireEnchant.Should().BeFalse();
        after.GetEnchantedKinds().Should().BeEmpty();
    }

    [Theory]
    [InlineData("frost")]
    [InlineData("frostbite")]
    public void ElementWorth136_Delta_DestroyFrost_TakesExactlyItsOwnPower(string code)
    {
        var item = code == "frost" ? OldElement("frost") : Enchanted("frostbite");
        var (after, kind) = MagicShopLocation.DestroyOneEnchant(item, new Pick(0));
        kind.Should().Be(code);
        after.WeaponPower.Should().Be(50, "destroying \"{0}\" takes exactly what it added", code);
        after.HasFrostEnchant.Should().BeFalse();
    }

    [Theory]
    [InlineData("fire", "frostbite", 0, 80)]
    [InlineData("fire", "frostbite", 1, 70)]
    [InlineData("frost", "phoenix", 0, 80)]
    [InlineData("frost", "phoenix", 1, 70)]
    public void ElementWorth136_Delta_DestroyOnAnItemWithBothElements_TakesThePickedKindsPower(string old, string now, int pick, int left)
    {
        var item = Enchanted(old, now);
        item.WeaponPower.Should().Be(50 + 20 + 30);
        var (after, kind) = MagicShopLocation.DestroyOneEnchant(item, new Pick(pick));
        kind.Should().Be(pick == 0 ? old : now);
        after.WeaponPower.Should().Be(left, "destroying \"{0}\" leaves the other element's power", kind);
    }

    [Fact]
    public void ElementWorth136_Delta_StatBonus_ReadsTheWeaponPowerEnchantExactly()
    {
        MagicShopLocation.CarriedStatBonus(Enchanted("fire", "pow"), 1).Should().Be(24, "an old Phoenix Fire's 20 is taken off");
        MagicShopLocation.CarriedStatBonus(Enchanted("phoenix", "pow"), 1).Should().Be(24, "a new Phoenix Fire's 30 is taken off");
        MagicShopLocation.CarriedStatBonus(Enchanted("frostbite", "pow"), 1).Should().Be(24, "a new Frostbite's 30 is taken off");
        MagicShopLocation.CarriedStatBonus(Enchanted("fire", "frostbite", "pow"), 1).Should().Be(24, "both elements");
        MagicShopLocation.CarriedStatBonus(Enchanted("frost", "phoenix", "pow"), 1).Should().Be(24, "both elements, the reverse");
    }

    // ---------- text ----------

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task ElementWorth136_Text_MenuRowsFit_AndNameTheNumbers(string lang)
    {
        var hero = Hero();
        Wear(hero, Blade());
        string shown = await InLangAsync(lang, () => Run(hero, "EnchantEquipment", "1", "0"));
        var rows = shown.Replace("\r", "").Split('\n');
        foreach (var row in rows)
            row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"{lang} row fits: [{row}]");
        string saved = GameConfig.Language;
        string phoenix, frost;
        try { GameConfig.Language = lang; phoenix = MagicShopLocation.TierDescription(Phoenix - 1); frost = MagicShopLocation.TierDescription(Frostbite - 1); }
        finally { GameConfig.Language = saved; }
        rows.Should().Contain(r => r.Contains($"[{Phoenix}]") && r.Contains(phoenix.TrimEnd()));
        rows.Should().Contain(r => r.Contains($"[{Frostbite}]") && r.Contains(frost.TrimEnd()));
        phoenix.Should().Contain("+30").And.Contain("30%").And.Contain("25%");
        frost.Should().Contain("+30").And.Contain("25%");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task ElementWorth136_Text_RefusalRowsFit(string lang)
    {
        var hero = Hero();
        Wear(hero, new Equipment { Name = "E136 Cap", Slot = EquipmentSlot.Head, ArmorClass = 5, MinLevel = 1, Value = 100 });
        string text = await InLangAsync(lang, () => Enchant(hero, Phoenix));
        var hero2 = Hero();
        Wear(hero2, Loot("frost"));
        text += await InLangAsync(lang, () => Enchant(hero2, Frostbite));
        foreach (var row in text.Replace("\r", "").Split('\n'))
            row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"{lang} row fits: [{row}]");
    }

    [Fact]
    public void ElementWorth136_Text_EveryChangedKey_InFiveLanguages_NoEnglishCopied()
    {
        string[] keys =
        {
            "magic_shop.tier_desc_phoenix", "magic_shop.tier_desc_frostbite", "magic_shop.element_weapon_only",
            "magic_shop.element_one_per_item", "combat.enchant_fire", "combat.enchant_fire_tm", "combat.enchant_frost",
            "combat.enchant_frost_tm",
        };
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !(Directory.Exists(Path.Combine(root.FullName, "Scripts")) && Directory.Exists(Path.Combine(root.FullName, "Localization"))))
            root = root.Parent;
        var dir = Path.Combine(root!.FullName, "Localization");
        var files = Langs.ToDictionary(l => l, l => System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, l + ".json"))).RootElement);
        foreach (var key in keys)
        {
            string en = files["en"].GetProperty(key).GetString()!;
            foreach (var lang in Langs.Skip(1))
                files[lang].GetProperty(key).GetString().Should().NotBe(en, $"{lang} {key} is translated");
        }
        foreach (var lang in Langs)
            foreach (var key in keys.Where(k => k.StartsWith("combat.")))
                files[lang].GetProperty(key).GetString()!.Should().NotContain("(", $"W2: {lang} {key} names no enchant");
        files["hu"].GetProperty("magic_shop.tier_desc_phoenix").GetString().Should().Contain("fegyvererő").And.NotContain(" erő");
        files["hu"].GetProperty("magic_shop.tier_desc_frostbite").GetString().Should().Contain("fegyvererő").And.NotContain(" erő");
    }
}
