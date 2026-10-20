using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: gear HP and mana regeneration is a share of the pool a round: the summed value is tenths of a
/// percent (17 is 1.7 percent), at least 1, at most 10 percent however many items carry it. The leader and every
/// living companion and teammate get it from their own gear; a companion's HP is synced to CompanionSystem.
/// </summary>
[Collection("SharedGameSingletons")]
public class RegenShare135Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static CombatEngine Engine(params Character[] teammates)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>(teammates));
        return engine;
    }

    private static void RoundEnd(CombatEngine engine, Character leader) =>
        typeof(CombatEngine).GetMethod("ProcessEndOfRoundAbilityEffects", F)!.Invoke(engine, new object[] { leader });

    private static void Wear(Character c, EquipmentSlot slot, int hp, int mana)
    {
        var e = new Equipment { Name = "Test " + slot, Slot = slot, MinLevel = 1, IsIdentified = true, HPRegen = hp, ManaRegen = mana };
        c.EquippedItems[slot] = EquipmentDatabase.RegisterDynamic(e);
    }

    private static Character Leader(long hp, long maxHp, long mana = 0, long maxMana = 0) => new Character
    {
        Name2 = "Leader", Class = CharacterClass.Warrior, Race = CharacterRace.Human, AI = CharacterAI.Human,
        Level = 10, HP = hp, MaxHP = maxHp, Stamina = 15, Wisdom = 10, Mana = mana, MaxMana = maxMana,
        RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream()),
    };

    private static Character Member(bool companion = false) => new Character
    {
        Name2 = "Member", Class = CharacterClass.Warrior, Race = CharacterRace.Human, IsCompanion = companion,
        Level = 10, HP = 500, MaxHP = 1000, Stamina = 15, Wisdom = 10, Mana = 0, MaxMana = 0,
    };

    [Fact]
    public void T1_Leader_Regen17On2000Hp_Heals34()
    {
        var leader = Leader(1000, 2000);
        Wear(leader, EquipmentSlot.Body, 17, 0);
        RoundEnd(Engine(), leader);
        leader.HP.Should().Be(1034, "17 tenths of a percent of 2,000 is 34");
    }

    [Fact]
    public void T2_Regen1On500Hp_HealsTheFloorOfOne()
    {
        var leader = Leader(100, 500);
        Wear(leader, EquipmentSlot.Body, 1, 0);
        RoundEnd(Engine(), leader);
        leader.HP.Should().Be(101);
        CombatEngine.GearRegenAmount(500, 0).Should().Be(0, "a value of 0 gives 0");
    }

    [Fact]
    public void T3_Companion_With20Regen_Heals20_AndIsSynced()
    {
        var comp = Member(companion: true);
        comp.CompanionId = CompanionId.Lyris;
        Wear(comp, EquipmentSlot.Body, 20, 0);
        RoundEnd(Engine(comp), Leader(100, 100));
        comp.HP.Should().Be(520);
        var map = (Dictionary<CompanionId, int>)typeof(CompanionSystem).GetField("companionCurrentHP", F)!.GetValue(CompanionSystem.Instance)!;
        map[CompanionId.Lyris].Should().Be(520, "the companion's HP is synced to CompanionSystem");
    }

    [Fact]
    public void T4_Teammate_WithNoGearRegen_GetsNone()
    {
        var mate = Member();
        RoundEnd(Engine(mate), Leader(100, 100));
        mate.HP.Should().Be(500);
    }

    [Fact]
    public void T5_Mana_10On600_Restores6()
    {
        var leader = Leader(100, 100, mana: 0, maxMana: 600);
        Wear(leader, EquipmentSlot.Body, 0, 10);
        long before = leader.Mana;
        RoundEnd(Engine(), leader);
        (leader.Mana - before).Should().Be(6 + (leader.IsManaClass ? StatEffectsSystem.GetManaRegenPerRound(leader.Wisdom) : 0));
        var mate = Member();
        mate.MaxMana = 600;
        Wear(mate, EquipmentSlot.Body, 0, 10);
        RoundEnd(Engine(mate), Leader(100, 100));
        mate.Mana.Should().Be(6, "a teammate restores mana from its own gear, silently");
    }

    [Fact]
    public void T7_SummedRegenAboveTheCap_IsCapped_ForHpAndMana()
    {
        CombatEngine.GearRegenAmount(1000, 150).Should().Be(100, "150 is capped at 100 tenths of a percent");
        CombatEngine.GearRegenAmount(600, 120).Should().Be(60);
        var leader = Leader(100, 1000);
        Wear(leader, EquipmentSlot.Body, 90, 0);
        Wear(leader, EquipmentSlot.Head, 60, 0);
        RoundEnd(Engine(), leader);
        leader.HP.Should().Be(200, "a summed 150 on 1,000 heals 100");
        var mate = Member();
        mate.MaxMana = 600;
        Wear(mate, EquipmentSlot.Body, 0, 70);
        Wear(mate, EquipmentSlot.Head, 0, 50);
        RoundEnd(Engine(mate), Leader(100, 100));
        mate.Mana.Should().Be(60, "a summed 120 on 600 restores 60");
    }

    [Fact]
    public void T8_SummedRegenAtTheCap_HealsExactlyTheCap()
    {
        CombatEngine.GearRegenAmount(1000, 100).Should().Be(100);
        CombatEngine.GearRegenAmount(1000, 99).Should().Be(99);
        GameConfig.MaxGearRegenTenthsPercent.Should().Be(100);
    }

    // ---------- T6: the display rows ----------

    private sealed class Plain : BaseLocation
    {
        public Plain() : base(GameLocation.TheInn, "The Inn", "") { }
        protected override List<NPC> GetLiveNPCsAtLocation() => new();
    }

    private static string GearScreen(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            var hero = new Character { Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 100, HP = 1000, MaxHP = 1000, AI = CharacterAI.Human };
            var e = new Equipment { Name = "Test", Slot = EquipmentSlot.Body, MinLevel = 1, IsIdentified = true, ArmorClass = 100, HPRegen = 17, ManaRegen = 9 };
            hero.EquippedItems[EquipmentSlot.Body] = EquipmentDatabase.RegisterDynamic(e);
            var loc = new Plain();
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
            typeof(BaseLocation).GetMethod("ShowDetailedGear", F)!.Invoke(loc, new object?[] { null });
            term.StreamWriterInternal?.Flush();
            return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
        }
        finally { GameConfig.Language = prev; }
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void T6_GearScreen_ShowsThePercentText(string lang)
    {
        string shown = GearScreen(lang);
        shown.Should().Contain(Loc.GetIn(lang, "base.gear_sp_hpregen", "1.7"));
        shown.Should().Contain(Loc.GetIn(lang, "base.gear_sp_mpregen", "0.9"));
        Loc.GetIn(lang, "base.gear_sp_hpregen", "1.7").Should().Contain("1.7%");
    }

    [Fact]
    public void T6_DescriptionLine_ShowsThePercent_AndOtherEffectsStayAsStored()
    {
        LootGenerator.DescriptionLineIn("en", "HP Regen +17, Mana Regen +9").Should().Be("HP Regen +1.7%, Mana Regen +0.9%");
        LootGenerator.DescriptionLineIn("en", "Fire Damage +5").Should().Be("Fire Damage +5");
        LootGenerator.RegenPercentText(17).Should().Be("1.7");
    }

    [Fact]
    public void T6_BlackMarketInspect_FormatsTheRegenValueAsPercent()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "usurper-reloaded.csproj"))) root = Path.GetDirectoryName(root)!;
        string src = File.ReadAllText(Path.Combine(root, "Scripts", "Locations", "DarkAlleyLocation.cs"));
        src.Should().Contain("LootGenerator.RegenPercentText(value) + \"%\"");
    }
}
