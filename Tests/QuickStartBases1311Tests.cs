using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.BBS;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.11: a Quick Start character gets the same setup after its roll as a custom character. Quick Start used to
/// return before the base stats were set and the stat bonuses applied, so the first RecalculateStats collapsed the
/// character (Stamina and Agility to 0, Mana to 0). Driven through CreateNewCharacter, online and local.
/// </summary>
[Collection("SharedGameSingletons")]
public class QuickStartBases1311Tests : IDisposable
{
    private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly int[] Dice = { 10, 11, 12, 13, 14, 15, 16, 17, 7 };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-qs-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly bool _online = DoorMode.IsOnlineMode;

    public QuickStartBases1311Tests()
    {
        _db = new SqlSaveBackend(_path);
    }

    public void Dispose()
    {
        OnlineField.SetValue(null, _online);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // archetype key on the Quick Start list, then the race and class menu keys that make the same pair on the custom path
    private static (CharacterRace Race, CharacterClass Class, string RaceKey, string ClassKey) Archetype(string key) => key switch
    {
        "1" => (CharacterRace.Human, CharacterClass.Warrior, "0", "0"),
        "2" => (CharacterRace.Elf, CharacterClass.Magician, "2", "7"),
        "3" => (CharacterRace.Dwarf, CharacterClass.Cleric, "4", "8"),
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private void StoreRoll(string key, int[]? dice = null) =>
        _db.SaveCreationRoll(key, JsonSerializer.Serialize(new CharacterCreationSystem.CreationRoll { Dice = dice ?? Dice, RerollsRemaining = 4 }));

    /// <summary>Quick Start through the real screens. The online path asks for a display name first; an empty one keeps the account name.</summary>
    private async Task<Character> Quick(string key, string archetype, bool online)
    {
        OnlineField.SetValue(null, online);
        var lines = online ? new[] { "", "Q", archetype, "1", "" } : new[] { "Q", archetype, "1", "" };
        return await Create(key, lines);
    }

    /// <summary>The custom path through its real screens, accepting the stored roll. Difficulty is asked only locally.</summary>
    private async Task<Character> Custom(string key, string archetype, bool online)
    {
        OnlineField.SetValue(null, online);
        var a = Archetype(archetype);
        var lines = online
            ? new[] { "", "C", "M", "Y", "1", a.RaceKey, "Y", a.ClassKey, "Y", "A", "", "Y" }
            : new[] { "C", "M", "Y", "1", a.RaceKey, "Y", a.ClassKey, "Y", "N", "A", "", "Y" };
        return await Create(key, lines);
    }

    private async Task<Character> Create(string key, string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        var ccs = new CharacterCreationSystem(term) { RollStore = _db };
        var made = await ccs.CreateNewCharacter(key);
        term.StreamWriterInternal?.Flush();
        string text = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
        made.Should().NotBeNull("creation should finish; the screens were:\n" + text);
        return made!;
    }

    private static PlayerData Save(Character c)
    {
        var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", Any)!.Invoke(SaveSystem.Instance, new object[] { c })!;
        return JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
    }

    private static Character Load(PlayerData d)
    {
        try { return (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", Any)!.Invoke(GameEngine.Instance, new object[] { d })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static string Numbers(Character c) =>
        $"HP {c.HP}/{c.MaxHP} Mana {c.Mana}/{c.MaxMana} Def {c.Defence} Sta {c.Stamina} Agi {c.Agility} " +
        $"Str {c.Strength} Dex {c.Dexterity} Con {c.Constitution} Int {c.Intelligence} Wis {c.Wisdom} Cha {c.Charisma} " +
        $"Fight {c.CurrentCombatStamina}/{c.MaxCombatStamina} WeapPow {c.WeapPow} ArmPow {c.ArmPow} | " +
        $"base HP {c.BaseMaxHP} Mana {c.BaseMaxMana} Def {c.BaseDefence} Sta {c.BaseStamina} Agi {c.BaseAgility} " +
        $"Str {c.BaseStrength} Dex {c.BaseDexterity} Con {c.BaseConstitution} Int {c.BaseIntelligence} Wis {c.BaseWisdom} Cha {c.BaseCharisma}";

    /// <summary>The bases hold the rolled stats. Max HP, max Mana and Defence are shown with their bonuses on top, so
    /// their bases are checked against the roll's own formula (ApplyDice) and not against the shown number.</summary>
    private static void BasesHoldTheRoll(Character c, string archetype)
    {
        var a = Archetype(archetype);
        c.Race.Should().Be(a.Race);
        c.Class.Should().Be(a.Class);
        c.BaseStrength.Should().Be(c.Strength);
        c.BaseDexterity.Should().Be(c.Dexterity);
        c.BaseConstitution.Should().Be(c.Constitution);
        c.BaseIntelligence.Should().Be(c.Intelligence);
        c.BaseWisdom.Should().Be(c.Wisdom);
        c.BaseCharisma.Should().Be(c.Charisma);
        c.BaseStamina.Should().Be(c.Stamina);
        c.BaseAgility.Should().Be(c.Agility);
        var cls = GameConfig.ClassStartingAttributes[a.Class];
        var race = GameConfig.RaceAttributes[a.Race];
        c.BaseDefence.Should().Be(cls.Defence + race.DefenceBonus);
        long hpDice = c.BaseMaxHP - (cls.HP * 3 + race.HPBonus + c.Constitution / 3);
        hpDice.Should().BeInRange(2, 12, "base max HP is two dice plus the class and race parts");
        c.BaseMaxMana.Should().Be(cls.MaxMana > 0 ? cls.MaxMana + c.Intelligence / 4 : 0);
    }

    // ---- a fresh roll: online with no stored roll, and local with no store at all ----

    private async Task<Character> QuickFreshRoll(string key, string archetype, bool online)
    {
        OnlineField.SetValue(null, online);
        var lines = online ? new[] { "", "Q", archetype, "1", "" } : new[] { "Q", archetype, "1", "" };
        var term = new TerminalEmulator(new LineStream(lines), new MemoryStream());
        var ccs = new CharacterCreationSystem(term) { RollStore = online ? _db : null };
        var made = await ccs.CreateNewCharacter(key);
        made.Should().NotBeNull();
        return made!;
    }

    [Theory]
    [InlineData("1", true)] [InlineData("2", true)] [InlineData("3", true)]
    [InlineData("1", false)] [InlineData("2", false)] [InlineData("3", false)]
    public async Task FreshRoll_BasesHoldTheRoll_AndHpAndManaAreAtMaximum(string archetype, bool online)
    {
        var c = await QuickFreshRoll("qsfresh" + archetype, archetype, online);
        BasesHoldTheRoll(c, archetype);
        c.HP.Should().Be(c.MaxHP);
        c.Mana.Should().Be(c.MaxMana);
    }

    [Theory]
    [InlineData("1", true)] [InlineData("2", true)] [InlineData("3", true)]
    [InlineData("1", false)] [InlineData("2", false)] [InlineData("3", false)]
    public async Task FreshRoll_FirstRecalculateAndSaveAndLoad_ChangeNothing(string archetype, bool online)
    {
        var c = await QuickFreshRoll("qsfreshr" + archetype, archetype, online);
        string created = Numbers(c);
        Numbers(Load(Save(c))).Should().Be(created, "a save and load straight after creation");
        c.RecalculateStats();
        Numbers(c).Should().Be(created, "the first recalculate, as at the first fight");
    }

    // ---- the same stored roll ----

    [Theory]
    [InlineData("1", true)] [InlineData("2", true)] [InlineData("3", true)]
    [InlineData("1", false)] [InlineData("2", false)] [InlineData("3", false)]
    public async Task StoredRoll_BasesHoldTheRoll(string archetype, bool online)
    {
        StoreRoll("qsbase" + archetype);
        var c = await Quick("qsbase" + archetype, archetype, online);
        BasesHoldTheRoll(c, archetype);
        var cls = GameConfig.ClassStartingAttributes[Archetype(archetype).Class];
        c.BaseAgility.Should().Be(Dice[2] + cls.Agility, "the stored dice, not a fresh roll");
    }

    [Theory]
    [InlineData("1", true, 74, 0)] [InlineData("2", true, 55, 129)] [InlineData("3", true, 70, 98)]
    [InlineData("1", false, 74, 0)] [InlineData("2", false, 55, 129)] [InlineData("3", false, 70, 98)]
    public async Task StoredRoll_HpAndManaAreAtTheirMaximum(string archetype, bool online, long maxHp, long maxMana)
    {
        StoreRoll("qsfull" + archetype);
        var c = await Quick("qsfull" + archetype, archetype, online);
        c.MaxHP.Should().Be(maxHp, "this roll's custom number, with the Constitution bonus");
        c.HP.Should().Be(maxHp);
        c.MaxMana.Should().Be(maxMana, "this roll's custom number, with the Intelligence and Wisdom bonuses");
        c.Mana.Should().Be(maxMana);
    }

    [Theory]
    [InlineData("1", true)] [InlineData("2", true)] [InlineData("3", true)]
    [InlineData("1", false)] [InlineData("2", false)] [InlineData("3", false)]
    public async Task StoredRoll_FirstRecalculate_ChangesNothing(string archetype, bool online)
    {
        StoreRoll("qsrecalc" + archetype);
        var c = await Quick("qsrecalc" + archetype, archetype, online);
        string created = Numbers(c);
        c.RecalculateStats();
        Numbers(c).Should().Be(created);
    }

    [Theory]
    [InlineData("1", true)] [InlineData("2", true)] [InlineData("3", true)]
    [InlineData("1", false)] [InlineData("2", false)] [InlineData("3", false)]
    public async Task StoredRoll_SaveAndLoad_ChangesNothing_BeforeOrAfterTheFirstRecalculate(string archetype, bool online)
    {
        StoreRoll("qsload" + archetype);
        var c = await Quick("qsload" + archetype, archetype, online);
        string created = Numbers(c);
        Numbers(Load(Save(c))).Should().Be(created, "saved before the first fight");
        c.RecalculateStats();
        var loaded = Load(Save(c));
        Numbers(loaded).Should().Be(created, "saved after the first fight");
        loaded.RecalculateStats();
        Numbers(loaded).Should().Be(created);
    }

    [Theory]
    [InlineData("1", true)] [InlineData("2", true)] [InlineData("3", true)]
    [InlineData("1", false)] [InlineData("2", false)] [InlineData("3", false)]
    public async Task StoredRoll_GivesTheSameCharacter_AsTheCustomPath(string archetype, bool online)
    {
        StoreRoll("qssame" + archetype);
        var quick = await Quick("qssame" + archetype, archetype, online);
        StoreRoll("cusame" + archetype);
        var custom = await Custom("cusame" + archetype, archetype, online);
        custom.Race.Should().Be(quick.Race);
        custom.Class.Should().Be(quick.Class);
        Numbers(quick).Should().Be(Numbers(custom));
    }

    // The lowest and highest rolls. A low Constitution takes max HP below the rolled number, on both paths: the
    // Constitution bonus is negative under 10 (StatEffectsSystem.GetConstitutionHPBonus).
    [Theory]
    [InlineData("1", true, 3, 2)] [InlineData("2", true, 3, 2)] [InlineData("3", true, 3, 2)]
    [InlineData("1", false, 3, 2)] [InlineData("2", false, 3, 2)] [InlineData("3", false, 3, 2)]
    [InlineData("1", true, 18, 12)] [InlineData("2", true, 18, 12)] [InlineData("3", true, 18, 12)]
    [InlineData("1", false, 18, 12)] [InlineData("2", false, 18, 12)] [InlineData("3", false, 18, 12)]
    public async Task LowestAndHighestRolls_MatchTheCustomPath_AndStaySteady(string archetype, bool online, int stat, int hp)
    {
        var dice = new[] { stat, stat, stat, stat, stat, stat, stat, stat, hp };
        StoreRoll("qsedge" + archetype, dice);
        var quick = await Quick("qsedge" + archetype, archetype, online);
        StoreRoll("cuedge" + archetype, dice);
        var custom = await Custom("cuedge" + archetype, archetype, online);
        string created = Numbers(quick);
        created.Should().Be(Numbers(custom));
        quick.HP.Should().Be(quick.MaxHP);
        quick.Mana.Should().Be(quick.MaxMana);
        BasesHoldTheRoll(quick, archetype);
        Numbers(Load(Save(quick))).Should().Be(created);
        quick.RecalculateStats();
        Numbers(quick).Should().Be(created);
    }
}
