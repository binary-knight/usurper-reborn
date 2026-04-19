using Xunit;
using FluentAssertions;
using System.Text.Json;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// Comprehensive save round-trip tests that verify ALL daily-reset properties,
/// buff combat counters, and other state that has historically been lost on
/// save/load cycles. These are the highest-value tests for beta stability.
///
/// Each test sets every property to a non-default value, serializes to JSON,
/// deserializes back, and asserts all values match. If a property is missing
/// from serialization (e.g. missing [JsonInclude] or missing from save/load
/// code), these tests will catch it.
/// </summary>
public class SaveRoundTripTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true
    };

    #region Daily Reset Properties

    [Fact]
    public void PlayerData_RoundTrip_PreservesDailyLimits_FightsAndCounters()
    {
        var original = new PlayerData
        {
            Fights = 7,
            PFights = 4,
            TFights = 3,
            Thiefs = 5,
            Brawls = 2,
            Assa = 1,
            DarkNr = 6,
            ChivNr = 8
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Fights.Should().Be(7, "Fights daily counter must survive save/load");
        restored.PFights.Should().Be(4, "PFights daily counter must survive save/load");
        restored.TFights.Should().Be(3, "TFights daily counter must survive save/load");
        restored.Thiefs.Should().Be(5, "Thiefs daily counter must survive save/load");
        restored.Brawls.Should().Be(2, "Brawls daily counter must survive save/load");
        restored.Assa.Should().Be(1, "Assa daily counter must survive save/load");
        restored.DarkNr.Should().Be(6, "DarkNr daily counter must survive save/load");
        restored.ChivNr.Should().Be(8, "ChivNr daily counter must survive save/load");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesDailyCounters_HomeAndHerbs()
    {
        var original = new PlayerData
        {
            HomeRestsToday = 3,
            HerbsGatheredToday = 5,
            Fatigue = 42
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.HomeRestsToday.Should().Be(3, "HomeRestsToday must survive save/load");
        restored.HerbsGatheredToday.Should().Be(5, "HerbsGatheredToday must survive save/load");
        restored.Fatigue.Should().Be(42, "Fatigue must survive save/load");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesDailyCounters_Wilderness()
    {
        var original = new PlayerData
        {
            WildernessExplorationsToday = 3,
            WildernessRevisitsToday = 2
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.WildernessExplorationsToday.Should().Be(3,
            "WildernessExplorationsToday must survive save/load");
        restored.WildernessRevisitsToday.Should().Be(2,
            "WildernessRevisitsToday must survive save/load");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesDailyCounters_Settlement()
    {
        var original = new PlayerData
        {
            SettlementGoldClaimedToday = true,
            SettlementHerbClaimedToday = true,
            SettlementShrineUsedToday = true,
            SettlementCircleUsedToday = true,
            SettlementWorkshopUsedToday = true
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.SettlementGoldClaimedToday.Should().BeTrue(
            "SettlementGoldClaimedToday must survive save/load");
        restored.SettlementHerbClaimedToday.Should().BeTrue(
            "SettlementHerbClaimedToday must survive save/load");
        restored.SettlementShrineUsedToday.Should().BeTrue(
            "SettlementShrineUsedToday must survive save/load");
        restored.SettlementCircleUsedToday.Should().BeTrue(
            "SettlementCircleUsedToday must survive save/load");
        restored.SettlementWorkshopUsedToday.Should().BeTrue(
            "SettlementWorkshopUsedToday must survive save/load");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesDailyCounters_DarkAlley()
    {
        var original = new PlayerData
        {
            GamblingRoundsToday = 7,
            PitFightsToday = 3,
            DesecrationsToday = 2,
            SethFightsToday = 4,
            ArmWrestlesToday = 1,
            RoyQuestsToday = 2,
            MurdersToday = 2
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.GamblingRoundsToday.Should().Be(7, "GamblingRoundsToday must survive save/load");
        restored.PitFightsToday.Should().Be(3, "PitFightsToday must survive save/load");
        restored.DesecrationsToday.Should().Be(2, "DesecrationsToday must survive save/load");
        restored.SethFightsToday.Should().Be(4, "SethFightsToday must survive save/load");
        restored.ArmWrestlesToday.Should().Be(1, "ArmWrestlesToday must survive save/load");
        restored.RoyQuestsToday.Should().Be(2, "RoyQuestsToday must survive save/load");
        restored.MurdersToday.Should().Be(2, "MurdersToday must survive save/load (v0.57.6 daily murder cap)");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesDailyCounters_KingdomAndPrison()
    {
        var original = new PlayerData
        {
            ThroneChallengedToday = true,
            ExecutionsToday = 3,
            PlayerImprisonedToday = true,
            NPCsImprisonedToday = 4
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.ThroneChallengedToday.Should().BeTrue(
            "ThroneChallengedToday must survive save/load");
        restored.ExecutionsToday.Should().Be(3,
            "ExecutionsToday must survive save/load");
        restored.PlayerImprisonedToday.Should().BeTrue(
            "PlayerImprisonedToday must survive save/load");
        restored.NPCsImprisonedToday.Should().Be(4,
            "NPCsImprisonedToday must survive save/load");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesDailyCounters_NewV053Properties()
    {
        // Properties added in v0.53.12 - most likely to have serialization gaps
        var original = new PlayerData
        {
            DrinksLeft = 5,
            PrisonsLeft = 3,
            ExecuteLeft = 2,
            QuestsLeft = 7,
            PrisonActivitiesToday = 2,
            BardSongsLeft = 4
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.DrinksLeft.Should().Be(5, "DrinksLeft must survive save/load");
        restored.PrisonsLeft.Should().Be(3, "PrisonsLeft must survive save/load");
        restored.ExecuteLeft.Should().Be(2, "ExecuteLeft must survive save/load");
        restored.QuestsLeft.Should().Be(7, "QuestsLeft must survive save/load");
        restored.PrisonActivitiesToday.Should().Be(2,
            "PrisonActivitiesToday must survive save/load");
        restored.BardSongsLeft.Should().Be(4, "BardSongsLeft must survive save/load");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesRealDateTracking()
    {
        var prayer = new DateTime(2026, 3, 15, 10, 30, 0);
        var sanctum = new DateTime(2026, 3, 14, 8, 0, 0);
        var binding = new DateTime(2026, 3, 13, 20, 0, 0);
        var boundary = new DateTime(2026, 3, 15, 19, 0, 0);

        var original = new PlayerData
        {
            LastPrayerRealDate = prayer,
            LastInnerSanctumRealDate = sanctum,
            LastBindingOfSoulsRealDate = binding,
            LastDailyResetBoundary = boundary
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.LastPrayerRealDate.Should().BeCloseTo(prayer, TimeSpan.FromSeconds(1));
        restored.LastInnerSanctumRealDate.Should().BeCloseTo(sanctum, TimeSpan.FromSeconds(1));
        restored.LastBindingOfSoulsRealDate.Should().BeCloseTo(binding, TimeSpan.FromSeconds(1));
        restored.LastDailyResetBoundary.Should().BeCloseTo(boundary, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Combat Buff Counters (decrement per combat)

    [Fact]
    public void PlayerData_RoundTrip_PreservesCombatBuffCounters()
    {
        var original = new PlayerData
        {
            WellRestedCombats = 5,
            WellRestedBonus = 0.15f,
            LoversBlissCombats = 3,
            LoversBlissBonus = 0.10f,
            GodSlayerCombats = 20,
            GodSlayerDamageBonus = 0.20f,
            GodSlayerDefenseBonus = 0.10f,
            HerbBuffCombats = 3,
            HerbBuffType = 2,
            HerbBuffValue = 0.25f,
            HerbExtraAttacks = 2,
            SongBuffCombats = 5,
            SongBuffType = 1,
            SongBuffValue = 0.15f,
            SongBuffValue2 = 0.05f,
            DarkPactCombats = 10,
            DarkPactDamageBonus = 0.30f,
            SettlementBuffCombats = 8,
            SettlementBuffType = 3,
            SettlementBuffValue = 0.10f,
            WorkshopBuffCombats = 5,
            PoisonCoatingCombats = 4,
            DivineBlessingCombats = 12,
            DivineBlessingBonus = 0.05f,
            CycleExpMultiplier = 1.5f
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.WellRestedCombats.Should().Be(5);
        restored.WellRestedBonus.Should().BeApproximately(0.15f, 0.001f);
        restored.LoversBlissCombats.Should().Be(3);
        restored.LoversBlissBonus.Should().BeApproximately(0.10f, 0.001f);
        restored.GodSlayerCombats.Should().Be(20);
        restored.GodSlayerDamageBonus.Should().BeApproximately(0.20f, 0.001f);
        restored.GodSlayerDefenseBonus.Should().BeApproximately(0.10f, 0.001f);
        restored.HerbBuffCombats.Should().Be(3);
        restored.HerbBuffType.Should().Be(2);
        restored.HerbBuffValue.Should().BeApproximately(0.25f, 0.001f);
        restored.HerbExtraAttacks.Should().Be(2);
        restored.SongBuffCombats.Should().Be(5);
        restored.SongBuffType.Should().Be(1);
        restored.SongBuffValue.Should().BeApproximately(0.15f, 0.001f);
        restored.SongBuffValue2.Should().BeApproximately(0.05f, 0.001f);
        restored.DarkPactCombats.Should().Be(10);
        restored.DarkPactDamageBonus.Should().BeApproximately(0.30f, 0.001f);
        restored.SettlementBuffCombats.Should().Be(8);
        restored.SettlementBuffType.Should().Be(3);
        restored.SettlementBuffValue.Should().BeApproximately(0.10f, 0.001f);
        restored.WorkshopBuffCombats.Should().Be(5);
        restored.PoisonCoatingCombats.Should().Be(4);
        restored.DivineBlessingCombats.Should().Be(12);
        restored.DivineBlessingBonus.Should().BeApproximately(0.05f, 0.001f);
        restored.CycleExpMultiplier.Should().BeApproximately(1.5f, 0.001f);
    }

    #endregion

    #region Herb Pouch Inventory

    [Fact]
    public void PlayerData_RoundTrip_PreservesHerbInventory()
    {
        var original = new PlayerData
        {
            HerbHealing = 3,
            HerbIronbark = 2,
            HerbFirebloom = 1,
            HerbSwiftthistle = 4,
            HerbStarbloom = 2
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.HerbHealing.Should().Be(3, "HerbHealing must survive save/load");
        restored.HerbIronbark.Should().Be(2, "HerbIronbark must survive save/load");
        restored.HerbFirebloom.Should().Be(1, "HerbFirebloom must survive save/load");
        restored.HerbSwiftthistle.Should().Be(4, "HerbSwiftthistle must survive save/load");
        restored.HerbStarbloom.Should().Be(2, "HerbStarbloom must survive save/load");
    }

    #endregion

    #region Login Streak & Weekly Rankings

    [Fact]
    public void PlayerData_RoundTrip_PreservesLoginStreak()
    {
        var original = new PlayerData
        {
            LoginStreak = 42,
            LongestLoginStreak = 90,
            LastLoginDate = "2026-03-15"
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.LoginStreak.Should().Be(42, "LoginStreak must survive save/load");
        restored.LongestLoginStreak.Should().Be(90, "LongestLoginStreak must survive save/load");
        restored.LastLoginDate.Should().Be("2026-03-15", "LastLoginDate must survive save/load");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesWeeklyRankingsAndRival()
    {
        var original = new PlayerData
        {
            WeeklyRank = 3,
            PreviousWeeklyRank = 7,
            RivalName = "DarkKnight42",
            RivalLevel = 55
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.WeeklyRank.Should().Be(3, "WeeklyRank must survive save/load");
        restored.PreviousWeeklyRank.Should().Be(7, "PreviousWeeklyRank must survive save/load");
        restored.RivalName.Should().Be("DarkKnight42", "RivalName must survive save/load");
        restored.RivalLevel.Should().Be(55, "RivalLevel must survive save/load");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesBloodMoon()
    {
        var original = new PlayerData
        {
            BloodMoonDay = 15,
            IsBloodMoon = true
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.BloodMoonDay.Should().Be(15, "BloodMoonDay must survive save/load");
        restored.IsBloodMoon.Should().BeTrue("IsBloodMoon must survive save/load");
    }

    #endregion

    #region Fame & Knighthood

    [Fact]
    public void PlayerData_RoundTrip_PreservesFame()
    {
        var original = new PlayerData
        {
            Fame = 500
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Fame.Should().Be(500, "Fame was a known serialization bug - must survive save/load");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesNobleTitle()
    {
        var original = new PlayerData
        {
            NobleTitle = "Sir"
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.NobleTitle.Should().Be("Sir", "NobleTitle must survive save/load");
    }

    #endregion

    #region Home Upgrade State

    [Fact]
    public void PlayerData_RoundTrip_PreservesHomeUpgrades()
    {
        var original = new PlayerData
        {
            HomeLevel = 3,
            ChestLevel = 2,
            BedLevel = 4,
            HearthLevel = 1,
            GardenLevel = 2,
            TrainingRoomLevel = 1,
            HasStudy = true,
            HasServants = true,
            HasReinforcedDoor = true,
            HasTrophyRoom = true,
            HasLegendaryArmory = true,
            HasVitalityFountain = true,
            PermanentDamageBonus = 15,
            PermanentDefenseBonus = 10,
            BonusMaxHP = 500,
            BonusWeapPow = 25,
            BonusArmPow = 20
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.HomeLevel.Should().Be(3);
        restored.ChestLevel.Should().Be(2);
        restored.BedLevel.Should().Be(4);
        restored.HearthLevel.Should().Be(1);
        restored.GardenLevel.Should().Be(2);
        restored.TrainingRoomLevel.Should().Be(1);
        restored.HasStudy.Should().BeTrue();
        restored.HasServants.Should().BeTrue();
        restored.HasReinforcedDoor.Should().BeTrue();
        restored.HasTrophyRoom.Should().BeTrue();
        restored.HasLegendaryArmory.Should().BeTrue();
        restored.HasVitalityFountain.Should().BeTrue();
        restored.PermanentDamageBonus.Should().Be(15);
        restored.PermanentDefenseBonus.Should().Be(10);
        restored.BonusMaxHP.Should().Be(500);
        restored.BonusWeapPow.Should().Be(25);
        restored.BonusArmPow.Should().Be(20);
    }

    #endregion

    #region Immortal & God System

    [Fact]
    public void PlayerData_RoundTrip_PreservesImmortalState()
    {
        var original = new PlayerData
        {
            IsImmortal = true,
            DivineName = "Luminara",
            GodLevel = 5,
            GodExperience = 100000L,
            DeedsLeft = 42,
            GodAlignment = "Good",
            AscensionDate = new DateTime(2026, 1, 15),
            HasEarnedAltSlot = true,
            WorshippedGod = "Aurelion",
            DivineBlessingCombats = 8,
            DivineBlessingBonus = 0.15f,
            DivineBoonConfig = "heal:2,shield:1"
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.IsImmortal.Should().BeTrue();
        restored.DivineName.Should().Be("Luminara");
        restored.GodLevel.Should().Be(5);
        restored.GodExperience.Should().Be(100000L);
        restored.DeedsLeft.Should().Be(42);
        restored.GodAlignment.Should().Be("Good");
        restored.AscensionDate.Should().BeCloseTo(new DateTime(2026, 1, 15), TimeSpan.FromSeconds(1));
        restored.HasEarnedAltSlot.Should().BeTrue();
        restored.WorshippedGod.Should().Be("Aurelion");
        restored.DivineBlessingCombats.Should().Be(8);
        restored.DivineBlessingBonus.Should().BeApproximately(0.15f, 0.001f);
        restored.DivineBoonConfig.Should().Be("heal:2,shield:1");
    }

    #endregion

    #region Dark Alley & Faction Consumables

    [Fact]
    public void PlayerData_RoundTrip_PreservesDarkAlleyState()
    {
        var original = new PlayerData
        {
            DarkAlleyReputation = 75,
            LoanAmount = 50000,
            LoanDaysRemaining = 10,
            LoanInterestAccrued = 5000,
            SafeHouseResting = true,
            GroggoShadowBlessingDex = 5,
            SteroidShopPurchases = 3,
            AlchemistINTBoosts = 2
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.DarkAlleyReputation.Should().Be(75);
        restored.LoanAmount.Should().Be(50000);
        restored.LoanDaysRemaining.Should().Be(10);
        restored.LoanInterestAccrued.Should().Be(5000);
        restored.SafeHouseResting.Should().BeTrue();
        restored.GroggoShadowBlessingDex.Should().Be(5);
        restored.SteroidShopPurchases.Should().Be(3);
        restored.AlchemistINTBoosts.Should().Be(2);
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesFactionConsumables()
    {
        var original = new PlayerData
        {
            PoisonCoatingCombats = 4,
            ActivePoisonType = 2,
            PoisonVials = 5,
            SmokeBombs = 3,
            InnerSanctumLastDay = 15
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.PoisonCoatingCombats.Should().Be(4);
        restored.ActivePoisonType.Should().Be(2);
        restored.PoisonVials.Should().Be(5);
        restored.SmokeBombs.Should().Be(3);
        restored.InnerSanctumLastDay.Should().Be(15);
    }

    #endregion

    #region Dark Pact & Dungeon Progression

    [Fact]
    public void PlayerData_RoundTrip_PreservesDarkPactAndDungeon()
    {
        var original = new PlayerData
        {
            DarkPactCombats = 10,
            DarkPactDamageBonus = 0.25f,
            HasShatteredSealFragment = true,
            HasTouchedTheVoid = true,
            ClearedSpecialFloors = new HashSet<int> { 25, 40, 55, 70 }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.DarkPactCombats.Should().Be(10);
        restored.DarkPactDamageBonus.Should().BeApproximately(0.25f, 0.001f);
        restored.HasShatteredSealFragment.Should().BeTrue();
        restored.HasTouchedTheVoid.Should().BeTrue();
        restored.ClearedSpecialFloors.Should().BeEquivalentTo(new[] { 25, 40, 55, 70 });
    }

    #endregion

    #region Prison State

    [Fact]
    public void PlayerData_RoundTrip_PreservesPrisonState()
    {
        var original = new PlayerData
        {
            DaysInPrison = 3,
            IsMurderConvict = true,
            CellDoorOpen = true,
            RescuedBy = "Aldric",
            PrisonEscapes = 1,
            PrisonActivitiesToday = 2
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.DaysInPrison.Should().Be(3);
        restored.IsMurderConvict.Should().BeTrue("IsMurderConvict must survive save/load");
        restored.CellDoorOpen.Should().BeTrue();
        restored.RescuedBy.Should().Be("Aldric");
        restored.PrisonEscapes.Should().Be(1);
        restored.PrisonActivitiesToday.Should().Be(2,
            "PrisonActivitiesToday must survive save/load");
    }

    #endregion

    #region Disease State

    [Fact]
    public void PlayerData_RoundTrip_PreservesDiseases()
    {
        var original = new PlayerData
        {
            Blind = true,
            Plague = true,
            Smallpox = true,
            Measles = true,
            Leprosy = true,
            LoversBane = true,
            Poison = 5,
            PoisonTurns = 3
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Blind.Should().BeTrue();
        restored.Plague.Should().BeTrue();
        restored.Smallpox.Should().BeTrue();
        restored.Measles.Should().BeTrue();
        restored.Leprosy.Should().BeTrue();
        restored.LoversBane.Should().BeTrue();
        restored.Poison.Should().Be(5);
        restored.PoisonTurns.Should().Be(3);
    }

    #endregion

    #region Drug System

    [Fact]
    public void PlayerData_RoundTrip_PreservesDrugState()
    {
        var original = new PlayerData
        {
            Addict = 3,
            SteroidDays = 5,
            DrugEffectDays = 2,
            ActiveDrug = 1,
            DrugTolerance = new Dictionary<int, int> { { 1, 3 }, { 2, 1 } }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Addict.Should().Be(3);
        restored.SteroidDays.Should().Be(5);
        restored.DrugEffectDays.Should().Be(2);
        restored.ActiveDrug.Should().Be(1);
        restored.DrugTolerance.Should().NotBeNull();
        restored.DrugTolerance.Should().ContainKey(1);
        restored.DrugTolerance![1].Should().Be(3);
        restored.DrugTolerance.Should().ContainKey(2);
        restored.DrugTolerance[2].Should().Be(1);
    }

    #endregion

    #region Divine Wrath

    [Fact]
    public void PlayerData_RoundTrip_PreservesDivineWrath()
    {
        var original = new PlayerData
        {
            DivineWrathLevel = 2,
            AngeredGodName = "Thorgrim",
            BetrayedForGodName = "Noctura",
            DivineWrathPending = true,
            DivineWrathTurnsRemaining = 15
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.DivineWrathLevel.Should().Be(2);
        restored.AngeredGodName.Should().Be("Thorgrim");
        restored.BetrayedForGodName.Should().Be("Noctura");
        restored.DivineWrathPending.Should().BeTrue();
        restored.DivineWrathTurnsRemaining.Should().Be(15);
    }

    #endregion

    #region Murder Weight & Royal Loan

    [Fact]
    public void PlayerData_RoundTrip_PreservesMurderWeight()
    {
        var original = new PlayerData
        {
            MurderWeight = 2.5f,
            PermakillLog = new List<string> { "Guard Bob", "Merchant Sue" },
            LastMurderWeightDecay = new DateTime(2026, 3, 10)
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.MurderWeight.Should().BeApproximately(2.5f, 0.001f);
        restored.PermakillLog.Should().BeEquivalentTo(new[] { "Guard Bob", "Merchant Sue" });
        restored.LastMurderWeightDecay.Should().BeCloseTo(new DateTime(2026, 3, 10),
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesRoyalLoan()
    {
        var original = new PlayerData
        {
            RoyalLoanAmount = 100000,
            RoyalLoanDueDay = 30,
            RoyalLoanBountyPosted = true
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.RoyalLoanAmount.Should().Be(100000);
        restored.RoyalLoanDueDay.Should().Be(30);
        restored.RoyalLoanBountyPosted.Should().BeTrue();
    }

    #endregion

    #region Equipment State

    [Fact]
    public void PlayerData_RoundTrip_PreservesEquippedItems()
    {
        var original = new PlayerData
        {
            EquippedItems = new Dictionary<int, int>
            {
                { 0, 101 },  // MainHand
                { 1, 202 },  // OffHand
                { 2, 303 },  // Head
                { 3, 404 }   // Body
            },
            WeaponCursed = true,
            ArmorCursed = false,
            ShieldCursed = true
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.EquippedItems.Should().HaveCount(4);
        restored.EquippedItems[0].Should().Be(101);
        restored.EquippedItems[1].Should().Be(202);
        restored.EquippedItems[2].Should().Be(303);
        restored.EquippedItems[3].Should().Be(404);
        restored.WeaponCursed.Should().BeTrue();
        restored.ArmorCursed.Should().BeFalse();
        restored.ShieldCursed.Should().BeTrue();
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesInventory()
    {
        var original = new PlayerData
        {
            Inventory = new List<InventoryItemData>
            {
                new InventoryItemData
                {
                    Name = "Sword of Fire",
                    Type = ObjType.Weapon,
                    Value = 5000,
                    IsIdentified = true
                },
                new InventoryItemData
                {
                    Name = "Mystery Ring",
                    Type = ObjType.Fingers,
                    Value = 2000,
                    IsIdentified = false
                }
            }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Inventory.Should().HaveCount(2);
        restored.Inventory[0].Name.Should().Be("Sword of Fire");
        restored.Inventory[0].IsIdentified.Should().BeTrue();
        restored.Inventory[1].Name.Should().Be("Mystery Ring");
        restored.Inventory[1].IsIdentified.Should().BeFalse();
    }

    #endregion

    #region Preferences & Settings

    [Fact]
    public void PlayerData_RoundTrip_PreservesPreferences()
    {
        var original = new PlayerData
        {
            AutoHeal = true,
            CombatSpeed = CombatSpeed.Fast,
            SkipIntimateScenes = true,
            ScreenReaderMode = true,
            CompactMode = true,
            Language = "es",
            ColorTheme = ColorThemeType.ClassicDark,
            AutoLevelUp = false,
            AutoEquipDisabled = true,
            DateFormatPreference = 2,
            AutoRedistributeXP = false
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.AutoHeal.Should().BeTrue();
        restored.CombatSpeed.Should().Be(CombatSpeed.Fast);
        restored.SkipIntimateScenes.Should().BeTrue();
        restored.ScreenReaderMode.Should().BeTrue();
        restored.CompactMode.Should().BeTrue();
        restored.Language.Should().Be("es");
        restored.AutoLevelUp.Should().BeFalse();
        restored.AutoEquipDisabled.Should().BeTrue();
        restored.DateFormatPreference.Should().Be(2);
        restored.AutoRedistributeXP.Should().BeFalse();
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesTeamXPDistribution()
    {
        var original = new PlayerData
        {
            TeamXPPercent = new int[] { 60, 20, 10, 10, 0 }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.TeamXPPercent.Should().NotBeNull();
        restored.TeamXPPercent.Should().BeEquivalentTo(new[] { 60, 20, 10, 10, 0 });
    }

    #endregion

    #region Song & Wilderness State

    [Fact]
    public void PlayerData_RoundTrip_PreservesHeardLoreSongs()
    {
        var original = new PlayerData
        {
            HeardLoreSongs = new List<int> { 0, 2, 4 }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.HeardLoreSongs.Should().BeEquivalentTo(new[] { 0, 2, 4 });
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesWildernessDiscoveries()
    {
        var original = new PlayerData
        {
            WildernessDiscoveries = new List<string>
            {
                "forest_ancient_tree",
                "mountain_crystal_cave",
                "swamp_lost_shrine"
            }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.WildernessDiscoveries.Should().HaveCount(3);
        restored.WildernessDiscoveries.Should().Contain("forest_ancient_tree");
        restored.WildernessDiscoveries.Should().Contain("mountain_crystal_cave");
        restored.WildernessDiscoveries.Should().Contain("swamp_lost_shrine");
    }

    #endregion

    #region Comprehensive Single-Test All Daily Properties

    [Fact]
    public void PlayerData_RoundTrip_AllDailyResetProperties_ComprehensiveCheck()
    {
        // This single test sets EVERY property that DailySystemManager resets.
        // If any of these fails, it means the property won't persist across
        // save/load, causing a player to lose their daily state on reconnect.
        var original = new PlayerData
        {
            // Fight counters (set at login)
            Fights = 7,
            PFights = 4,
            TFights = 3,
            Thiefs = 5,
            Brawls = 2,
            Assa = 1,
            DarkNr = 6,
            ChivNr = 8,
            BardSongsLeft = 4,

            // Dark Alley counters
            GamblingRoundsToday = 7,
            PitFightsToday = 3,
            DesecrationsToday = 2,

            // Real-date daily tracking
            SethFightsToday = 4,
            ArmWrestlesToday = 1,
            RoyQuestsToday = 2,

            // Home counters
            HomeRestsToday = 3,
            HerbsGatheredToday = 5,

            // Wilderness
            WildernessExplorationsToday = 3,
            WildernessRevisitsToday = 2,

            // Settlement
            SettlementGoldClaimedToday = true,
            SettlementHerbClaimedToday = true,
            SettlementShrineUsedToday = true,
            SettlementCircleUsedToday = true,
            SettlementWorkshopUsedToday = true,

            // Kingdom
            ThroneChallengedToday = true,
            ExecutionsToday = 3,
            PlayerImprisonedToday = true,
            NPCsImprisonedToday = 4,

            // Prison
            PrisonActivitiesToday = 2,

            // v0.53.12 daily counters
            DrinksLeft = 5,
            PrisonsLeft = 3,
            ExecuteLeft = 2,
            QuestsLeft = 7,

            // Fatigue (reset on full sleep)
            Fatigue = 55
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();

        // Fight counters
        restored!.Fights.Should().Be(7);
        restored.PFights.Should().Be(4);
        restored.TFights.Should().Be(3);
        restored.Thiefs.Should().Be(5);
        restored.Brawls.Should().Be(2);
        restored.Assa.Should().Be(1);
        restored.DarkNr.Should().Be(6);
        restored.ChivNr.Should().Be(8);
        restored.BardSongsLeft.Should().Be(4);

        // Dark Alley
        restored.GamblingRoundsToday.Should().Be(7);
        restored.PitFightsToday.Should().Be(3);
        restored.DesecrationsToday.Should().Be(2);

        // Real-date tracking
        restored.SethFightsToday.Should().Be(4);
        restored.ArmWrestlesToday.Should().Be(1);
        restored.RoyQuestsToday.Should().Be(2);

        // Home
        restored.HomeRestsToday.Should().Be(3);
        restored.HerbsGatheredToday.Should().Be(5);

        // Wilderness
        restored.WildernessExplorationsToday.Should().Be(3);
        restored.WildernessRevisitsToday.Should().Be(2);

        // Settlement
        restored.SettlementGoldClaimedToday.Should().BeTrue();
        restored.SettlementHerbClaimedToday.Should().BeTrue();
        restored.SettlementShrineUsedToday.Should().BeTrue();
        restored.SettlementCircleUsedToday.Should().BeTrue();
        restored.SettlementWorkshopUsedToday.Should().BeTrue();

        // Kingdom
        restored.ThroneChallengedToday.Should().BeTrue();
        restored.ExecutionsToday.Should().Be(3);
        restored.PlayerImprisonedToday.Should().BeTrue();
        restored.NPCsImprisonedToday.Should().Be(4);

        // Prison
        restored.PrisonActivitiesToday.Should().Be(2);

        // v0.53.12 counters
        restored.DrinksLeft.Should().Be(5);
        restored.PrisonsLeft.Should().Be(3);
        restored.ExecuteLeft.Should().Be(2);
        restored.QuestsLeft.Should().Be(7);

        // Fatigue
        restored.Fatigue.Should().Be(55);
    }

    #endregion

    #region Chest Contents (Home Storage)

    [Fact]
    public void PlayerData_RoundTrip_PreservesChestContents()
    {
        // Chest item loss on logout was a real bug (v0.44.0)
        var original = new PlayerData
        {
            ChestContents = new List<InventoryItemData>
            {
                new InventoryItemData
                {
                    Name = "Dragon Scale Armor",
                    Type = ObjType.Body,
                    Value = 25000,
                    IsIdentified = true,
                    Defence = 50,
                    Stamina = 10
                },
                new InventoryItemData
                {
                    Name = "Enchanted Ring",
                    Type = ObjType.Fingers,
                    Value = 10000,
                    Wisdom = 15
                }
            }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.ChestContents.Should().NotBeNull();
        restored.ChestContents.Should().HaveCount(2);
        restored.ChestContents![0].Name.Should().Be("Dragon Scale Armor");
        restored.ChestContents[0].Defence.Should().Be(50);
        restored.ChestContents[0].Stamina.Should().Be(10);
        restored.ChestContents[1].Name.Should().Be("Enchanted Ring");
        restored.ChestContents[1].Wisdom.Should().Be(15);
    }

    #endregion

    #region Recurring Duelist Rival

    [Fact]
    public void PlayerData_RoundTrip_PreservesRecurringDuelist()
    {
        var original = new PlayerData
        {
            RecurringDuelist = new DuelistData
            {
                Name = "Ser Blackthorn",
                Weapon = "Rapier of Shadows",
                Level = 35,
                TimesEncountered = 7,
                PlayerWins = 5,
                PlayerLosses = 2,
                WasInsulted = true,
                IsDead = false
            }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.RecurringDuelist.Should().NotBeNull();
        restored.RecurringDuelist!.Name.Should().Be("Ser Blackthorn");
        restored.RecurringDuelist.Weapon.Should().Be("Rapier of Shadows");
        restored.RecurringDuelist.Level.Should().Be(35);
        restored.RecurringDuelist.TimesEncountered.Should().Be(7);
        restored.RecurringDuelist.PlayerWins.Should().Be(5);
        restored.RecurringDuelist.PlayerLosses.Should().Be(2);
        restored.RecurringDuelist.WasInsulted.Should().BeTrue();
        restored.RecurringDuelist.IsDead.Should().BeFalse();
    }

    #endregion

    #region Hints and MUD Title

    [Fact]
    public void PlayerData_RoundTrip_PreservesHintsShown()
    {
        var original = new PlayerData
        {
            HintsShown = new HashSet<string>
            {
                "HINT_FIRST_COMBAT",
                "HINT_FIRST_PURCHASE_TAX",
                "HINT_LEVEL_MASTER",
                "HINT_QUEST_SYSTEM"
            }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.HintsShown.Should().HaveCount(4);
        restored.HintsShown.Should().Contain("HINT_FIRST_COMBAT");
        restored.HintsShown.Should().Contain("HINT_LEVEL_MASTER");
    }

    [Fact]
    public void PlayerData_RoundTrip_PreservesMudTitle()
    {
        var original = new PlayerData
        {
            MudTitle = "\u001b[1;33mThe Invincible\u001b[0m"
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.MudTitle.Should().Be("\u001b[1;33mThe Invincible\u001b[0m",
            "MUD title with ANSI codes must survive round-trip");
    }

    #endregion

    #region Kill Statistics

    [Fact]
    public void PlayerData_RoundTrip_PreservesKillStats()
    {
        var original = new PlayerData
        {
            MKills = 1500,
            MDefeats = 42,
            PKills = 15,
            PDefeats = 3,
            TotalExecutions = 7
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.MKills.Should().Be(1500);
        restored.MDefeats.Should().Be(42);
        restored.PKills.Should().Be(15);
        restored.PDefeats.Should().Be(3);
        restored.TotalExecutions.Should().Be(7);
    }

    #endregion

    #region GameTimeMinutes

    [Fact]
    public void PlayerData_RoundTrip_PreservesGameTime()
    {
        var original = new PlayerData
        {
            GameTimeMinutes = 900,  // 3:00 PM in-game
            TurnCount = 500
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.GameTimeMinutes.Should().Be(900,
            "Game time must survive save/load for time-of-day system");
        restored.TurnCount.Should().Be(500);
    }

    #endregion

    #region Orientation

    [Fact]
    public void PlayerData_RoundTrip_PreservesOrientation()
    {
        var original = new PlayerData
        {
            Orientation = 2  // Bisexual
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<PlayerData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Orientation.Should().Be(2, "Orientation must survive save/load");
    }

    #endregion

    #region Companion Bag (v0.57.3+) and NPC Teammate Bag (v0.57.4+)

    // Guard-rails for the structural fix that landed across v0.57.3 and v0.57.4:
    // items transferred to a companion (Lumina's "Mira's bag is empty" report) or
    // to an NPC teammate (spouses, recruited citizens via Team Corner) via combat
    // [T] / Home / dungeon Party Inventory viewer. Both paths used to populate a
    // runtime-only Inventory list that no save field persisted, so transferred
    // items evaporated on reload.
    //
    // Each bug was a missing serialization field on a save-data class. These
    // tests fail hard if the field is dropped, renamed, or stops round-tripping
    // (typical regression vectors: new naming policy, forgotten include, a
    // refactor that swaps the field for a property without [JsonInclude]).

    [Fact]
    public void CompanionSaveInfo_RoundTrip_PreservesInventory()
    {
        // v0.57.3: Companion.Inventory was added to the Companion class AND to
        // CompanionSaveInfo so items survive save/load. This test asserts the
        // save-side contract.
        var original = new CompanionSaveInfo
        {
            Id = 1,
            IsRecruited = true,
            IsActive = true,
            Level = 25,
            Inventory = new List<InventoryItemData>
            {
                new InventoryItemData
                {
                    Name = "Healing Potion",
                    Type = ObjType.Potion,
                    Value = 50,
                    IsIdentified = true,
                    HP = 100
                },
                new InventoryItemData
                {
                    Name = "Epic Warhammer",
                    Type = ObjType.Weapon,
                    Value = 15000,
                    Attack = 120,
                    Strength = 8,
                    IsIdentified = true,
                    IsCursed = false
                },
                new InventoryItemData
                {
                    Name = "Unidentified Cloak",
                    Type = ObjType.Abody,  // Pascal "around body" — covers cloaks / robes
                    Value = 500,
                    IsIdentified = false
                }
            }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<CompanionSaveInfo>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Inventory.Should().NotBeNull();
        restored.Inventory.Should().HaveCount(3, "All three items must survive the round trip");
        restored.Inventory[0].Name.Should().Be("Healing Potion");
        restored.Inventory[0].HP.Should().Be(100);
        restored.Inventory[1].Name.Should().Be("Epic Warhammer");
        restored.Inventory[1].Attack.Should().Be(120);
        restored.Inventory[1].Strength.Should().Be(8);
        restored.Inventory[2].Name.Should().Be("Unidentified Cloak");
        restored.Inventory[2].IsIdentified.Should().BeFalse();
    }

    [Fact]
    public void CompanionSaveInfo_RoundTrip_PreservesInventory_WithLootEffects()
    {
        // Dungeon-loot items carry a separate LootEffects list for CON / INT /
        // AllStats bonuses that don't fit in the flat InventoryItemData fields.
        // The v0.57.3 fix serializes them end-to-end; this guards the wire path.
        var original = new CompanionSaveInfo
        {
            Id = 2,
            IsRecruited = true,
            Inventory = new List<InventoryItemData>
            {
                new InventoryItemData
                {
                    Name = "Amulet of Vitality",
                    Type = ObjType.Neck,
                    Value = 8000,
                    IsIdentified = true,
                    LootEffects = new List<LootEffectData>
                    {
                        new LootEffectData { EffectType = 1, Value = 15 },  // CON +15
                        new LootEffectData { EffectType = 2, Value = 10 },  // INT +10
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<CompanionSaveInfo>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Inventory.Should().HaveCount(1);
        var amulet = restored.Inventory[0];
        amulet.LootEffects.Should().NotBeNull();
        amulet.LootEffects.Should().HaveCount(2, "Both CON and INT loot effects must round-trip");
        amulet.LootEffects![0].EffectType.Should().Be(1);
        amulet.LootEffects[0].Value.Should().Be(15);
        amulet.LootEffects[1].EffectType.Should().Be(2);
        amulet.LootEffects[1].Value.Should().Be(10);
    }

    [Fact]
    public void CompanionSaveInfo_LegacySave_MissingInventoryField_LoadsAsEmpty()
    {
        // Saves written before v0.57.3 don't have an `inventory` field at all.
        // They must still load cleanly with an empty list, not null, not crash.
        const string legacyJson = @"{
            ""id"": 3,
            ""isRecruited"": true,
            ""isActive"": true,
            ""level"": 10
        }";

        var restored = JsonSerializer.Deserialize<CompanionSaveInfo>(legacyJson, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Inventory.Should().NotBeNull("Missing inventory field must default to empty list, not null");
        restored.Inventory.Should().BeEmpty();
    }

    [Fact]
    public void NPCData_RoundTrip_PreservesInventory()
    {
        // v0.57.4 (fifth-pass fix): NPC teammates — spouses, recruited citizens,
        // party members added via Team Corner — had no Inventory serialization
        // field at all, despite the runtime NPC class inheriting Inventory from
        // Character. Items transferred to them evaporated every save. This
        // test locks in the serialization contract.
        var original = new NPCData
        {
            Id = "npc_vex",
            Name = "Vex",
            Level = 15,
            Inventory = new List<InventoryItemData>
            {
                new InventoryItemData
                {
                    Name = "Lockpick Set",
                    Type = ObjType.Magic,  // catch-all for utility / scroll / misc items
                    Value = 200,
                    IsIdentified = true
                },
                new InventoryItemData
                {
                    Name = "Legendary Dagger",
                    Type = ObjType.Weapon,
                    Value = 20000,
                    Attack = 85,
                    Dexterity = 12,
                    IsIdentified = true,
                    IsCursed = false
                }
            }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<NPCData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Inventory.Should().NotBeNull();
        restored.Inventory.Should().HaveCount(2);
        restored.Inventory[0].Name.Should().Be("Lockpick Set");
        restored.Inventory[1].Name.Should().Be("Legendary Dagger");
        restored.Inventory[1].Attack.Should().Be(85);
        restored.Inventory[1].Dexterity.Should().Be(12);
    }

    [Fact]
    public void NPCData_RoundTrip_PreservesInventory_WithLootEffects()
    {
        // Same LootEffects guard as the companion test, but on the NPC path.
        var original = new NPCData
        {
            Id = "npc_teammate",
            Name = "Test Teammate",
            Inventory = new List<InventoryItemData>
            {
                new InventoryItemData
                {
                    Name = "Ring of the Old Gods",
                    Type = ObjType.Fingers,
                    Value = 50000,
                    IsIdentified = true,
                    LootEffects = new List<LootEffectData>
                    {
                        new LootEffectData { EffectType = 3, Value = 5 },  // AllStats +5
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(original, _jsonOptions);
        var restored = JsonSerializer.Deserialize<NPCData>(json, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Inventory.Should().HaveCount(1);
        restored.Inventory[0].LootEffects.Should().NotBeNull();
        restored.Inventory[0].LootEffects.Should().HaveCount(1);
        restored.Inventory[0].LootEffects![0].EffectType.Should().Be(3);
        restored.Inventory[0].LootEffects[0].Value.Should().Be(5);
    }

    [Fact]
    public void NPCData_LegacySave_MissingInventoryField_LoadsAsEmpty()
    {
        // Pre-v0.57.4 saves won't have the new `inventory` field on NPC blocks.
        // Must load clean with an empty list — existing NPCs on live servers
        // should upgrade transparently.
        const string legacyJson = @"{
            ""id"": ""npc_old"",
            ""name"": ""Old NPC"",
            ""level"": 10
        }";

        var restored = JsonSerializer.Deserialize<NPCData>(legacyJson, _jsonOptions);

        restored.Should().NotBeNull();
        restored!.Inventory.Should().NotBeNull("Missing inventory field on NPCData must default to empty list");
        restored.Inventory.Should().BeEmpty();
    }

    [Fact]
    public void CompanionSaveInfo_AND_NPCData_Inventory_AreIndependent()
    {
        // Sanity check: the two classes have separate Inventory fields (they
        // live on entirely different save blocks — CompanionSaveInfo is on the
        // StorySystems block, NPCData is on the NPCs list). A refactor that
        // collapsed them or accidentally shared an instance would corrupt
        // save files, so this test pins the expectation that they don't alias.
        var companion = new CompanionSaveInfo { Id = 1 };
        var npc = new NPCData { Id = "npc_x" };

        companion.Inventory.Add(new InventoryItemData { Name = "Companion Sword" });
        npc.Inventory.Add(new InventoryItemData { Name = "NPC Shield" });

        companion.Inventory.Should().HaveCount(1);
        companion.Inventory[0].Name.Should().Be("Companion Sword");
        npc.Inventory.Should().HaveCount(1);
        npc.Inventory[0].Name.Should().Be("NPC Shield");
        companion.Inventory.Should().NotBeSameAs(npc.Inventory);
    }

    #endregion
}
