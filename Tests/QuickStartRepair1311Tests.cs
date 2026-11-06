using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.BBS;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.11: a saved Quick Start character that lost its rolled bases gets the average roll added once at load.
/// The rows are real ones from the serializer. The five bases are zeroed by hand, which is the state Quick Start used
/// to leave, so these rows do not depend on how creation behaves today.
/// </summary>
[Collection("SharedGameSingletons")]
public class QuickStartRepair1311Tests : IDisposable
{
    private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly int[] Dice = { 10, 11, 12, 13, 14, 15, 16, 17, 7 };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-qsr-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly bool _online = DoorMode.IsOnlineMode;

    public QuickStartRepair1311Tests()
    {
        OnlineField.SetValue(null, false);
        _db = new SqlSaveBackend(_path);
    }

    public void Dispose()
    {
        OnlineField.SetValue(null, _online);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // The three rows he approved, as literals: a table change must not move a repair unseen.
    private static (long HP, long Mana, long Def, long Sta, long Agi) Row(string archetype) => archetype switch
    {
        "1" => (37, 0, 8, 18, 13),
        "2" => (27, 43, 3, 14, 12),
        "3" => (37, 23, 7, 16, 12),
        _ => throw new ArgumentOutOfRangeException(nameof(archetype)),
    };

    private async Task<Character> Quick(string key, string archetype)
    {
        OnlineField.SetValue(null, false);
        _db.SaveCreationRoll(key, JsonSerializer.Serialize(new CharacterCreationSystem.CreationRoll { Dice = Dice, RerollsRemaining = 4 }));
        var term = new TerminalEmulator(new LineStream(new[] { "Q", archetype, "1", "" }), new MemoryStream());
        var made = await new CharacterCreationSystem(term) { RollStore = _db }.CreateNewCharacter(key);
        made.Should().NotBeNull();
        return made!;
    }

    private static void ZeroFiveBases(Character c)
    {
        c.BaseMaxHP = 0; c.BaseMaxMana = 0; c.BaseDefence = 0; c.BaseStamina = 0; c.BaseAgility = 0;
    }

    private static void LevelTo(Character c, int to)
    {
        for (int l = c.Level + 1; l <= to; l++)
        {
            LevelMasterLocation.ApplyClassStatIncreases(c, l);
            c.RaiseLevel(l);
            c.RecalculateStats();
        }
    }

    /// <summary>A character the old Quick Start made, as saved: fought once, then levelled.</summary>
    private async Task<PlayerData> HurtRow(string archetype, int level)
    {
        var c = await Quick($"qsr{Guid.NewGuid():N}", archetype);
        ZeroFiveBases(c);
        c.RecalculateStats();   // the first fight
        LevelTo(c, level);
        return Save(c);
    }

    /// <summary>A character the old Quick Start made, as saved before its first fight.</summary>
    private async Task<PlayerData> NotFoughtRow(string archetype)
    {
        var c = await Quick($"qsr{Guid.NewGuid():N}", archetype);
        ZeroFiveBases(c);
        return Save(c);
    }

    /// <summary>A character with whole bases, as the custom path leaves it.</summary>
    private async Task<PlayerData> CustomRow(string archetype, int level)
    {
        var c = await Quick($"qsr{Guid.NewGuid():N}", archetype);
        c.InitializeBaseStats();
        c.RecalculateStats();
        LevelTo(c, level);
        return Save(c);
    }

    private static PlayerData Save(Character c)
    {
        var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", Any)!.Invoke(SaveSystem.Instance, new object[] { c })!;
        return Copy(data);
    }

    private static PlayerData Copy(PlayerData d) => JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(d))!;

    private static Character Load(PlayerData d)
    {
        try { return (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", Any)!.Invoke(GameEngine.Instance, new object[] { d })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static void ShouldHaveTheSavedBasesPlus(Character loaded, PlayerData saved, (long HP, long Mana, long Def, long Sta, long Agi) add)
    {
        loaded.BaseMaxHP.Should().Be(saved.BaseMaxHP + add.HP);
        loaded.BaseMaxMana.Should().Be(saved.BaseMaxMana + add.Mana);
        loaded.BaseDefence.Should().Be(saved.BaseDefence + add.Def);
        loaded.BaseStamina.Should().Be(saved.BaseStamina + add.Sta);
        loaded.BaseAgility.Should().Be(saved.BaseAgility + add.Agi);
    }

    /// <summary>What the load does with no repair: a base above 0 is kept and any other falls back to its stat.</summary>
    private static void ShouldBeTheLoadWithNoRepair(Character loaded, PlayerData saved)
    {
        loaded.BaseMaxHP.Should().Be(saved.BaseMaxHP > 0 ? saved.BaseMaxHP : saved.MaxHP);
        loaded.BaseMaxMana.Should().Be(saved.BaseMaxMana > 0 ? saved.BaseMaxMana : saved.MaxMana);
        loaded.BaseDefence.Should().Be(saved.BaseDefence > 0 ? saved.BaseDefence : saved.Defence);
        loaded.BaseStamina.Should().Be(saved.BaseStamina > 0 ? saved.BaseStamina : saved.Stamina);
        loaded.BaseAgility.Should().Be(saved.BaseAgility > 0 ? saved.BaseAgility : saved.Agility);
    }

    [Theory]
    [InlineData("1", CharacterRace.Human, CharacterClass.Warrior)]
    [InlineData("2", CharacterRace.Elf, CharacterClass.Magician)]
    [InlineData("3", CharacterRace.Dwarf, CharacterClass.Cleric)]
    public async Task TheAverageRoll_IsTheApprovedRow_AndThePairIsTheMenusArchetype(string archetype, CharacterRace race, CharacterClass cls)
    {
        var made = await Quick($"qsr{archetype}", archetype);
        made.Race.Should().Be(race);
        made.Class.Should().Be(cls);

        var got = CharacterCreationSystem.QuickStartAverageBases(race, cls)!.Value;
        var row = Row(archetype);
        (got.MaxHP, got.MaxMana, got.Defence, got.Stamina, got.Agility).Should().Be(row);
    }

    [Fact]
    public void TheAverageRoll_HasNoValue_ForAnyOtherPair()
    {
        foreach (var race in Enum.GetValues<CharacterRace>())
            foreach (var cls in Enum.GetValues<CharacterClass>())
            {
                bool archetype = (race, cls) is (CharacterRace.Human, CharacterClass.Warrior) or (CharacterRace.Elf, CharacterClass.Magician) or (CharacterRace.Dwarf, CharacterClass.Cleric);
                if (!archetype) CharacterCreationSystem.QuickStartAverageBases(race, cls).Should().BeNull($"{race} {cls}");
            }
    }

    [Theory]
    [InlineData("1", 1)] [InlineData("1", 5)] [InlineData("1", 10)]
    [InlineData("2", 1)] [InlineData("2", 5)] [InlineData("2", 10)]
    [InlineData("3", 1)] [InlineData("3", 5)] [InlineData("3", 10)]
    public async Task HurtRow_GetsTheRowAddedToItsSavedBases(string archetype, int level)
    {
        var saved = await HurtRow(archetype, level);
        saved.BaseAgility.Should().Be(0, "the row is the hurt one");
        saved.Agility.Should().Be(0);

        var loaded = Load(Copy(saved));

        ShouldHaveTheSavedBasesPlus(loaded, saved, Row(archetype));
        loaded.BaseStrength.Should().Be(saved.BaseStrength, "only the five lost bases move");
        loaded.BaseConstitution.Should().Be(saved.BaseConstitution);
        loaded.BaseIntelligence.Should().Be(saved.BaseIntelligence);
    }

    [Theory]
    [InlineData("1", 1)] [InlineData("1", 5)] [InlineData("1", 10)]
    [InlineData("2", 1)] [InlineData("2", 5)] [InlineData("2", 10)]
    [InlineData("3", 1)] [InlineData("3", 5)] [InlineData("3", 10)]
    public async Task HurtRow_StatsAfterRecalculate_FollowTheNewBases(string archetype, int level)
    {
        var saved = await HurtRow(archetype, level);
        var row = Row(archetype);

        var c = Load(Copy(saved));
        c.RecalculateStats();

        c.Stamina.Should().Be(saved.BaseStamina + row.Sta);
        c.Agility.Should().Be(saved.BaseAgility + row.Agi);
        c.Defence.Should().Be(saved.BaseDefence + row.Def + StatEffectsSystem.GetAgilityDefenseBonus(c.Agility));
        c.MaxHP.Should().Be(saved.BaseMaxHP + row.HP + StatEffectsSystem.GetConstitutionHPBonus(c.Constitution, c.Level));
        if (row.Mana > 0)
            c.MaxMana.Should().Be(saved.BaseMaxMana + row.Mana + StatEffectsSystem.GetIntelligenceManaBonus(c.Intelligence, c.Level) + StatEffectsSystem.GetWisdomManaBonus(c.Wisdom));
        else
            c.MaxMana.Should().Be(0, "a Warrior has no mana");
    }

    [Theory]
    [InlineData("1")] [InlineData("2")] [InlineData("3")]
    public async Task Level1_BaseHpZero_SavedMaxHpAboveZero_EndsAtTheTable_NotTablePlusSavedMaxHp(string archetype)
    {
        var saved = await HurtRow(archetype, 1);
        saved.BaseMaxHP.Should().Be(0);
        saved.MaxHP.Should().BeGreaterThan(0);

        Load(Copy(saved)).BaseMaxHP.Should().Be(Row(archetype).HP);
    }

    [Theory]
    [InlineData("2")] [InlineData("3")]
    public async Task Level1_BaseManaZero_SavedMaxManaAboveZero_EndsAtTheTable_NotTablePlusSavedMaxMana(string archetype)
    {
        var saved = await HurtRow(archetype, 1);
        saved.BaseMaxMana.Should().Be(0);
        saved.MaxMana = 5;   // gear with a mana bonus, on the hurt row

        Load(Copy(saved)).BaseMaxMana.Should().Be(Row(archetype).Mana);
    }

    [Theory]
    [InlineData("1")] [InlineData("2")] [InlineData("3")]
    public async Task AboveLevel1_AgilityFromGear_IsRepaired_AndThatStatDoesNotBecomeTheBase(string archetype)
    {
        var saved = await HurtRow(archetype, 5);
        saved.BaseAgility.Should().Be(0);
        saved.Agility = 6;   // gear lifted the stat, the base stayed 0

        var loaded = Load(Copy(saved));

        ShouldHaveTheSavedBasesPlus(loaded, saved, Row(archetype));
    }

    [Theory]
    [InlineData("1", 5L)]
    [InlineData("2", 0L)]
    [InlineData("3", 4L)]
    public async Task ReloadedHurtRow_WithANegativeSavedDefence_NeverFallsBack_AndNeverLoses(string archetype, long expectedBaseDefence)
    {
        var saved = await HurtRow(archetype, 1);
        // The shape the load has always left when a hurt save was saved again (r1311-n1 measure): base HP is the old
        // MaxHP, base Defence is the negative stat, and the stat has fallen again.
        saved.BaseMaxHP = saved.MaxHP;
        saved.MaxHP *= 2;
        saved.BaseDefence = saved.Defence;
        saved.Defence = saved.BaseDefence * 2;
        saved.BaseDefence.Should().BeNegative();

        var loaded = Load(Copy(saved));

        loaded.BaseDefence.Should().Be(expectedBaseDefence);
        loaded.BaseDefence.Should().BeGreaterThan(saved.BaseDefence);
        loaded.BaseMaxHP.Should().Be(saved.BaseMaxHP + Row(archetype).HP);
    }

    [Theory]
    [InlineData(CharacterRace.Dwarf, "1")]
    [InlineData(CharacterRace.Elf, "1")]
    [InlineData(CharacterRace.Human, "2")]
    [InlineData(CharacterRace.Dwarf, "2")]
    [InlineData(CharacterRace.Human, "3")]
    [InlineData(CharacterRace.Elf, "3")]
    public async Task TheSameClass_WithAnotherRace_IsUnchanged(CharacterRace race, string archetype)
    {
        foreach (int level in new[] { 1, 5 })
        {
            var saved = await HurtRow(archetype, level);
            saved.Race = race;
            ShouldBeTheLoadWithNoRepair(Load(Copy(saved)), saved);
        }
    }

    [Theory]
    [InlineData(CharacterClass.Barbarian)]
    [InlineData(CharacterClass.Paladin)]
    [InlineData(CharacterClass.Sage)]
    public async Task AnotherClass_IsUnchanged(CharacterClass cls)
    {
        foreach (int level in new[] { 1, 5 })
        {
            var saved = await HurtRow("1", level);
            saved.Class = cls;
            ShouldBeTheLoadWithNoRepair(Load(Copy(saved)), saved);
        }
    }

    [Theory]
    [InlineData("1")] [InlineData("2")] [InlineData("3")]
    public async Task NotFoughtAtLevel1_IsLeftToTheLoadsOwnRepair(string archetype)
    {
        var saved = await NotFoughtRow(archetype);
        saved.BaseAgility.Should().Be(0);
        saved.Agility.Should().BeGreaterThan(0);

        var loaded = Load(Copy(saved));

        ShouldBeTheLoadWithNoRepair(loaded, saved);
        loaded.BaseAgility.Should().Be(saved.Agility, "the real roll is still in the row");
    }

    [Theory]
    [InlineData("1", 1)] [InlineData("1", 5)]
    [InlineData("2", 1)] [InlineData("2", 5)]
    [InlineData("3", 1)] [InlineData("3", 5)]
    public async Task CustomCharacter_IsUnchanged(string archetype, int level)
    {
        var saved = await CustomRow(archetype, level);
        saved.BaseAgility.Should().BeGreaterThan(0);

        var loaded = Load(Copy(saved));

        loaded.BaseMaxHP.Should().Be(saved.BaseMaxHP);
        loaded.BaseMaxMana.Should().Be(saved.BaseMaxMana);
        loaded.BaseDefence.Should().Be(saved.BaseDefence);
        loaded.BaseStamina.Should().Be(saved.BaseStamina);
        loaded.BaseAgility.Should().Be(saved.BaseAgility);
    }

    [Theory]
    [InlineData("1", 1)] [InlineData("1", 5)]
    [InlineData("2", 1)] [InlineData("2", 5)]
    [InlineData("3", 1)] [InlineData("3", 5)]
    public async Task OldSaveWithNoBases_IsUnchanged(string archetype, int level)
    {
        var saved = await HurtRow(archetype, level);
        saved.BaseStrength = 0; saved.BaseDexterity = 0; saved.BaseConstitution = 0;
        saved.BaseIntelligence = 0; saved.BaseWisdom = 0; saved.BaseCharisma = 0;
        saved.BaseMaxHP = 0; saved.BaseMaxMana = 0; saved.BaseDefence = 0; saved.BaseStamina = 0; saved.BaseAgility = 0;
        saved.Agility = 0;

        ShouldBeTheLoadWithNoRepair(Load(Copy(saved)), saved);
    }

    [Theory]
    [InlineData("1", 1)] [InlineData("1", 5)]
    [InlineData("2", 1)] [InlineData("2", 5)]
    [InlineData("3", 1)] [InlineData("3", 5)]
    public async Task ASecondLoadOfTheSameRow_GivesTheSame_AndTheRowIsNotWritten(string archetype, int level)
    {
        var saved = await HurtRow(archetype, level);
        var row = Copy(saved);

        var first = Load(row);
        var second = Load(row);

        (row.BaseMaxHP, row.BaseMaxMana, row.BaseDefence, row.BaseStamina, row.BaseAgility)
            .Should().Be((saved.BaseMaxHP, saved.BaseMaxMana, saved.BaseDefence, saved.BaseStamina, saved.BaseAgility), "the repair does not write the row it reads");
        second.BaseMaxHP.Should().Be(first.BaseMaxHP);
        second.BaseMaxMana.Should().Be(first.BaseMaxMana);
        second.BaseDefence.Should().Be(first.BaseDefence);
        second.BaseStamina.Should().Be(first.BaseStamina);
        second.BaseAgility.Should().Be(first.BaseAgility);
    }

    [Theory]
    [InlineData("1", 1)] [InlineData("1", 5)] [InlineData("1", 10)]
    [InlineData("2", 1)] [InlineData("2", 5)] [InlineData("2", 10)]
    [InlineData("3", 1)] [InlineData("3", 5)] [InlineData("3", 10)]
    public async Task SaveAndLoadAfterTheRepair_IsStable(string archetype, int level)
    {
        var saved = await HurtRow(archetype, level);
        var once = Load(Copy(saved));
        var firstStats = (once.MaxHP, once.MaxMana, once.Defence, once.Stamina, once.Agility);
        var firstBases = (once.BaseMaxHP, once.BaseMaxMana, once.BaseDefence, once.BaseStamina, once.BaseAgility);

        for (int i = 0; i < 3; i++)
        {
            var again = Load(Save(once));
            (again.BaseMaxHP, again.BaseMaxMana, again.BaseDefence, again.BaseStamina, again.BaseAgility).Should().Be(firstBases);
            again.RecalculateStats();
            (again.MaxHP, again.MaxMana, again.Defence, again.Stamina, again.Agility).Should().Be(firstStats);
            once = again;
        }
    }

    [Theory]
    [InlineData("1", 5)] [InlineData("2", 5)] [InlineData("3", 5)]
    public async Task CurrentHpAndMana_AreNotRaised(string archetype, int level)
    {
        var saved = await HurtRow(archetype, level);
        saved.HP = 11;
        saved.Mana = 0;

        var loaded = Load(Copy(saved));

        loaded.HP.Should().Be(11);
        loaded.Mana.Should().Be(0);
        loaded.MaxHP.Should().BeGreaterThan(saved.MaxHP);
    }

    [Theory]
    [InlineData(false, "1")] [InlineData(true, "1")]
    [InlineData(false, "2")] [InlineData(true, "2")]
    [InlineData(false, "3")] [InlineData(true, "3")]
    public async Task OnlineAndLocal_BothRepair(bool online, string archetype)
    {
        var saved = await HurtRow(archetype, 5);
        OnlineField.SetValue(null, online);

        ShouldHaveTheSavedBasesPlus(Load(Copy(saved)), saved, Row(archetype));
    }

    [Theory]
    [InlineData("1")] [InlineData("2")] [InlineData("3")]
    public async Task ReadingAnotherPlayersSave_DoesNotRepair_AndLeavesTheRowAlone(string archetype)
    {
        var saved = await HurtRow(archetype, 5);
        var row = Copy(saved);
        var before = JsonSerializer.Serialize(row);

        var fighter = PlayerCharacterLoader.CreateFromSaveData(row, "someone");

        JsonSerializer.Serialize(row).Should().Be(before);
        fighter.BaseAgility.Should().Be(0, "this reader fights the save as it stands until its owner loads");
        fighter.BaseMaxHP.Should().Be(saved.BaseMaxHP);
        fighter.BaseStamina.Should().Be(saved.BaseStamina);
    }

    [Theory]
    [InlineData("1")] [InlineData("2")] [InlineData("3")]
    public async Task AnAttackWriteBack_LeavesTheBases_SoTheOwnersLoadStillRepairs(string archetype)
    {
        var saved = await HurtRow(archetype, 5);
        const string key = "qsrvictim";
        (await _db.WriteGameData(key, new SaveGameData { Version = GameConfig.MinSaveVersion, Player = Copy(saved) })).Should().BeTrue();

        // The same edit the attack paths make on the blob they read: an item and experience, then the whole blob back.
        var blob = await _db.ReadGameData(key);
        blob!.Player.Experience = Math.Max(0, blob.Player.Experience - 1);
        blob.Player.EquippedItems?.Clear();
        blob.Player.DynamicEquipment?.Clear();
        (await _db.WriteGameData(key, blob)).Should().BeTrue();

        var after = (await _db.ReadGameData(key))!.Player;
        after.BaseMaxHP.Should().Be(saved.BaseMaxHP);
        after.BaseMaxMana.Should().Be(saved.BaseMaxMana);
        after.BaseDefence.Should().Be(saved.BaseDefence);
        after.BaseStamina.Should().Be(saved.BaseStamina);
        after.BaseAgility.Should().Be(0);
        after.Agility.Should().Be(0);

        ShouldHaveTheSavedBasesPlus(Load(after), saved, Row(archetype));
    }
}
