using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.7: the reforge is a bounded roll. Value follows the formula in long arithmetic with no
/// running maximum; power cannot pass one and a half times the strongest legitimate drop for the
/// character's level; no number of reforges escapes the item bounds; the cost has a floor.
/// </summary>
[Collection("SharedGameSingletons")]
public class ReforgeTests
{
    private static Equipment Sword(int power, EquipmentRarity rarity = EquipmentRarity.Common) => new Equipment
    {
        Name = "Test Sword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword,
        WeaponPower = power, StrengthBonus = 10, DexterityBonus = 8, CriticalChanceBonus = 5, LifeSteal = 3, DefenceBonus = -4,
        Rarity = rarity, Value = 1_000,
    };

    [Fact]
    public void Value_FollowsTheFormula_InLongArithmetic_AndCanGoDown()
    {
        WeaponShopLocation.ReforgeValue(100, EquipmentRarity.Common).Should().Be(1_500);
        WeaponShopLocation.ReforgeValue(100, EquipmentRarity.Artifact).Should().Be(5_250, "100 x 15 x 3.5");
        WeaponShopLocation.ReforgeValue(200_000_000, EquipmentRarity.Artifact).Should().Be(GameConfig.MaxItemValue, "the old int multiply wrapped here; now it saturates");
        WeaponShopLocation.ReforgeValue(0, EquipmentRarity.Common).Should().Be(1);

        // a weapon carrying an inflated historical value: the running maximum used to keep it forever
        var w = Sword(100, EquipmentRarity.Artifact); w.Value = 15_000_000;
        var r = WeaponShopLocation.RollReforge(w, 100, new Random(1), out _);
        r.Value.Should().Be(WeaponShopLocation.ReforgeValue(r.WeaponPower, r.Rarity)).And.BeLessThan(10_000, "value is the formula, not Math.Max with the past");
    }

    [Fact]
    public void AnArtifactReroll_StaysWithinFifteenPercent_AndNeverUpgrades()
    {
        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            var r = WeaponShopLocation.RollReforge(Sword(1000, EquipmentRarity.Artifact), 100, rng, out bool up);
            up.Should().BeFalse();
            r.Rarity.Should().Be(EquipmentRarity.Artifact);
            r.WeaponPower.Should().BeInRange(850, 1150);
            r.DefenceBonus.Should().BeNegative("a penalty stays a penalty");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(100)]
    public void TenThousandReforges_NeverPassTheLevelBound_OrTheItemBounds(int level)
    {
        int bound = WeaponShopLocation.ReforgePowerBound(level);
        bound.Should().BeLessThanOrEqualTo(GameConfig.MaxItemPower);
        var rng = new Random(level);
        var w = Sword(Math.Min(60, bound));
        int peak = 0;
        for (int i = 0; i < 10_000; i++)
        {
            var r = WeaponShopLocation.RollReforge(w, level, rng, out _);
            WeaponShopLocation.ApplyReforge(w, r);
            peak = Math.Max(peak, w.WeaponPower);
            w.ClampStats().Should().BeFalse("a reforge never leaves an item over a bound");
        }
        peak.Should().BeLessThanOrEqualTo(bound, $"level {level} bound is {bound}");
        w.Value.Should().BeLessThanOrEqualTo(GameConfig.MaxItemValue);
        w.StrengthBonus.Should().BeLessThanOrEqualTo(GameConfig.MaxItemStatBonus);
        w.CriticalChanceBonus.Should().BeLessThanOrEqualTo(GameConfig.MaxItemPercent);
    }

    [Fact]
    public void TheLevelBound_IsOneAndAHalfTimesTheStrongestLegitimateDrop()
    {
        WeaponShopLocation.ReforgePowerBound(25).Should().BeInRange(1_215, 1_225, "815 x 1.5");
        WeaponShopLocation.ReforgePowerBound(100).Should().BeInRange(2_090, 2_100, "1,397 x 1.5");
        WeaponShopLocation.ReforgePowerBound(100).Should().BeGreaterThan(941, "the strongest weapon measured on the live server can still be reforged at level 100");
    }

    [Fact]
    public void AnInflatedInput_IsClampedBeforeTheRoll_AndTheCastCannotWrap()
    {
        var w = Sword(int.MaxValue, EquipmentRarity.Legendary); w.DexterityBonus = 609_147_534; w.Value = 6_324_554_610;
        var r = WeaponShopLocation.RollReforge(w, 25, new Random(3), out _);
        r.WeaponPower.Should().BeInRange(1, WeaponShopLocation.ReforgePowerBound(25));
        r.DexterityBonus.Should().BeInRange(1, GameConfig.MaxItemStatBonus);
        r.Value.Should().BeInRange(1, GameConfig.MaxItemValue);
        WeaponShopLocation.RerollStat(int.MaxValue, 0.15, 1.15, new Random(1), GameConfig.MaxItemPower).Should().Be(GameConfig.MaxItemPower);
        WeaponShopLocation.RerollStat(int.MinValue + 1, 0.15, 1.15, new Random(1), GameConfig.MaxItemPower).Should().Be(-GameConfig.MaxItemPower);
    }

    [Fact]
    public void TheCost_HasAFloor_AndKeepsTheEndgameSurcharge()
    {
        WeaponShopLocation.ReforgeCost(1).Should().Be(GameConfig.ReforgeMinCost, "level 1 paid 50 gold");
        WeaponShopLocation.ReforgeCost(8).Should().Be(GameConfig.ReforgeMinCost);
        WeaponShopLocation.ReforgeCost(25).Should().Be(31_250);
        WeaponShopLocation.ReforgeCost(100).Should().Be(100L * 100 * GameConfig.ReforgeCostMultiplier + 20 * GameConfig.ReforgeEndgameSurchargePerLevel);
        GameConfig.MaxReforgesPerDay.Should().Be(3);
    }

    // ───────────── the real screen ─────────────

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data; private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos); Array.Copy(_data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => _data.Length; public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private static async Task<string> Reforge(Character hero, string script)
    {
        var shop = new WeaponShopLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(script), output);
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(BaseLocation).GetField("terminal", f)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", f)!.SetValue(shop, hero);
        await (Task)typeof(WeaponShopLocation).GetMethod("ReforgeWeapon", f)!.Invoke(shop, null)!;
        term.StreamWriterInternal!.Flush();
        return new System.Text.RegularExpressions.Regex("\u001b\\[[0-9;]*[A-Za-z]").Replace(Encoding.UTF8.GetString(output.ToArray()), "");
    }

    [Fact]
    public async Task ThreeReforgesADay_TheFourthIsRefused_ACancelCostsNothing_AndAWeaponBeyondTheBoundIsHandedBack()
    {
        var hero = new Character { Name1 = "anvil", Name2 = "Anvil", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 25, HP = 500, MaxHP = 500, Gold = 1_000_000 };
        var sword = Sword(60); EquipmentDatabase.RegisterDynamic(sword);
        hero.EquippedItems[EquipmentSlot.MainHand] = sword.Id;
        long cost = WeaponShopLocation.ReforgeCost(25);

        await Reforge(hero, "N\n\n");
        hero.Gold.Should().Be(1_000_000, "a cancel costs nothing"); hero.ReforgesToday.Should().Be(0);

        for (int i = 1; i <= 3; i++)
        {
            await Reforge(hero, "Y\n\n");
            hero.ReforgesToday.Should().Be(i);
            hero.Gold.Should().Be(1_000_000 - i * cost, "payment, count and result commit together");
        }
        sword.Value.Should().Be(WeaponShopLocation.ReforgeValue(sword.WeaponPower, sword.Rarity));

        int powerBefore = sword.WeaponPower; long goldBefore = hero.Gold;
        var text = await Reforge(hero, "Y\n\n");
        text.Should().Contain(Loc.Get("weapon_shop.reforge_daily_limit", GameConfig.MaxReforgesPerDay));
        hero.Gold.Should().Be(goldBefore); hero.ReforgesToday.Should().Be(3); sword.WeaponPower.Should().Be(powerBefore);

        // a new day; a drop far beyond what the smith can better at level 25 is refused, never shrunk
        hero.ReforgesToday = 0;
        sword.WeaponPower = 1_397;
        text = await Reforge(hero, "Y\n\n");
        text.Should().Contain("finer work");
        sword.WeaponPower.Should().Be(1_397); hero.Gold.Should().Be(goldBefore); hero.ReforgesToday.Should().Be(0);
    }

    // Reads the source on purpose: the daily reset is a private method with wide side effects (turns,
    // quests, the world), and calling it would test far more than this counter. Persistence is covered
    // by the save round-trip test.
    [Fact]
    public void TheDailyReset_ClearsTheCounter()
    {
        var src = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts", "Systems", "DailySystemManager.cs"));
        src.Should().Contain("player.ReforgesToday = 0;", "the cap resets with the other daily counters");
    }
}
