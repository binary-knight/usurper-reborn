using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: after a PvP win the winner salvages the loser's weapon (30%) and body armour (25%) for half the
/// clamped value, from the pieces the loser actually wears. The gate used to compare the legacy slot's name to
/// "None", which that slot reads in the reader's language, so English never salvaged and es/fr/hu/it did.
/// Every language now salvages the same, and an empty slot gives nothing.
/// </summary>
[Collection("SharedGameSingletons")]
public class PvpSalvage125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class ZeroRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0;
    }

    private sealed class HighRandom : Random
    {
        public override int Next() => int.MaxValue - 1;
        public override int Next(int maxValue) => maxValue - 1;
        public override int Next(int minValue, int maxValue) => maxValue - 1;
        public override double NextDouble() => 0.999;
    }

    private static Character Hero(int level = 50) => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = level, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 1000, Strength = 500,
    };

    private static NPC Foe(string id) => new() { ID = id, Name1 = "Foe", Name2 = "Foe", Level = 50, HP = 0, MaxHP = 400, Gold = 0 };

    private static Equipment Template(EquipmentSlot slot) =>
        EquipmentDatabase.GetBuiltInTemplates().First(e => e.Slot == slot && e.Value > 1000 && e.Value < 100_000);

    private static long Half(Equipment e) => (long)(Math.Clamp(e.Value, 0, GameConfig.MaxItemValue) * 0.5);

    private static async Task<(CombatResult Result, string Text)> Win(string lang, Character hero, Character foe, Random rng)
    {
        var prevLang = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        var output = new MemoryStream();
        TerminalEmulator term = null!;
        term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 12), _ =>
        {
            term.StreamWriterInternal?.Flush();
            output.WriteByte((byte)'\n');
        }), output);
        var result = new CombatResult { Player = hero, Opponent = foe };
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            var engine = new CombatEngine(term);
            typeof(CombatEngine).GetField("random", F)!.SetValue(engine, rng);
            await (Task)typeof(CombatEngine).GetMethod("DeterminePvPOutcome", F)!.Invoke(engine, new object[] { result })!;
        }
        finally { GameConfig.Language = prevLang; GameConfig.ScreenReaderMode = sr; }
        term.StreamWriterInternal?.Flush();
        string text = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
        return (result, text);
    }

    [Fact]
    public async Task EnglishWinner_SalvagesTheWornWeaponAndArmour()
    {
        var sword = Template(EquipmentSlot.MainHand);
        var mail = Template(EquipmentSlot.Body);
        var hero = Hero();
        var foe = Foe("npc_salvage125_en");
        foe.EquippedItems[EquipmentSlot.MainHand] = sword.Id;
        foe.EquippedItems[EquipmentSlot.Body] = mail.Id;
        long before = hero.Gold;

        var (result, text) = await Win("en", hero, foe, new ZeroRandom());

        result.Outcome.Should().Be(CombatOutcome.Victory);
        result.EquipmentSalvageGold.Should().Be(Half(sword) + Half(mail), "an English winner salvages both worn pieces");
        result.ItemsFound.Should().Contain(new[] { sword.Name, mail.Name });
        hero.Gold.Should().Be(before + result.GoldGained, "the salvage gold is paid with the rest of the win");
        text.Should().Contain(Loc.GetIn("en", "combat.equipment_salvaged"));
        text.Should().Contain(Loc.GetIn("en", "combat.salvaged_item_row", ItemNames.DisplayIn("en", sword.Name), $"{Half(sword):N0}").Trim());
        text.Should().Contain(Loc.GetIn("en", "combat.salvaged_item_row", ItemNames.DisplayIn("en", mail.Name), $"{Half(mail):N0}").Trim());
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task EveryLanguage_SalvagesTheSame(string lang)
    {
        var sword = Template(EquipmentSlot.MainHand);
        var mail = Template(EquipmentSlot.Body);
        var hero = Hero();
        var foe = Foe("npc_salvage125_" + lang);
        foe.EquippedItems[EquipmentSlot.MainHand] = sword.Id;
        foe.EquippedItems[EquipmentSlot.Body] = mail.Id;

        var (result, text) = await Win(lang, hero, foe, new ZeroRandom());

        result.EquipmentSalvageGold.Should().Be(Half(sword) + Half(mail), $"{lang} salvages like every other language");
        result.ItemsFound.Should().Equal(sword.Name, mail.Name);
        text.Should().Contain(Loc.GetIn(lang, "combat.equipment_salvaged"));
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task EmptySlots_SalvageNothing(string lang)
    {
        var hero = Hero();
        var foe = Foe("npc_salvage125_empty_" + lang);
        long before = hero.Gold;

        var (result, text) = await Win(lang, hero, foe, new ZeroRandom());

        result.Outcome.Should().Be(CombatOutcome.Victory);
        result.EquipmentSalvageGold.Should().Be(0, "nothing is worn, nothing is salvaged");
        result.ItemsFound.Should().BeEmpty();
        hero.Gold.Should().Be(before + result.GoldGained);
        text.Should().NotContain(Loc.GetIn(lang, "combat.equipment_salvaged"));
    }

    [Fact]
    public async Task OnlyTheWornSlot_IsSalvaged()
    {
        var mail = Template(EquipmentSlot.Body);
        var foe = Foe("npc_salvage125_armouronly");
        foe.EquippedItems[EquipmentSlot.Body] = mail.Id;

        var (result, _) = await Win("en", Hero(), foe, new ZeroRandom());

        result.EquipmentSalvageGold.Should().Be(Half(mail));
        result.ItemsFound.Should().Equal(mail.Name);
    }

    [Fact]
    public async Task FailedRolls_SalvageNothing()
    {
        var foe = Foe("npc_salvage125_unlucky");
        foe.EquippedItems[EquipmentSlot.MainHand] = Template(EquipmentSlot.MainHand).Id;
        foe.EquippedItems[EquipmentSlot.Body] = Template(EquipmentSlot.Body).Id;

        var (result, _) = await Win("en", Hero(), foe, new HighRandom());

        result.EquipmentSalvageGold.Should().Be(0, "the 30% and 25% chances still apply");
        result.ItemsFound.Should().BeEmpty();
    }

    [Fact]
    public async Task GoldAdded_IsHalfTheClampedValue()
    {
        var relic = new Equipment
        {
            Name = "Salvage Test Relic", Slot = EquipmentSlot.MainHand, MinLevel = 1, Value = GameConfig.MaxItemValue * 3,
            WeaponPower = 40, Handedness = WeaponHandedness.OneHanded,
        };
        EquipmentDatabase.RegisterDynamic(relic);
        var hero = Hero(level: 5000);   // a per-fight cap above half the clamp, so the clamp is what limits the payout
        GameConfig.PvPGoldPerFightCap(hero.Level).Should().BeGreaterThan(GameConfig.MaxItemValue / 2);
        var foe = Foe("npc_salvage125_relic");
        foe.EquippedItems[EquipmentSlot.MainHand] = relic.Id;

        var (result, _) = await Win("en", hero, foe, new ZeroRandom());

        result.EquipmentSalvageGold.Should().Be(GameConfig.MaxItemValue / 2, "half of the value clamped to MaxItemValue");
    }

    [Fact]
    public async Task LegacySlotItem_WithNothingEquipped_IsStillSalvaged()
    {
        var sword = Template(EquipmentSlot.MainHand);
        var foe = Foe("npc_salvage125_legacy");
        foe.RHand = sword.Id;

        var (result, _) = await Win("en", Hero(), foe, new ZeroRandom());

        result.EquipmentSalvageGold.Should().Be(Half(sword), "a shop template in the legacy slot is priced by id, not by name");
        result.ItemsFound.Should().Equal(sword.Name);
    }
}
