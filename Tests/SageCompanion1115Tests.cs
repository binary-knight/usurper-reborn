using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.15 Sage piece 4: a Sage teammate casts control and party wards.</summary>
[Collection("SharedGameSingletons")]
public class SageCompanion1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Sage(int level)
    {
        var sage = new Character
        {
            Name1 = "Sage", Name2 = "Sage", Class = CharacterClass.Sage, Level = level, Wisdom = 50, Intelligence = 50,
            HP = 100_000, MaxHP = 100_000, Mana = 100_000, MaxMana = 100_000, CombatSpeed = CombatSpeed.Instant,
        };
        var staff = EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff);
        sage.EquippedItems[EquipmentSlot.MainHand] = staff.Id;
        return sage;
    }

    private static Character Ally(string name, CharacterClass cls) => new Character
    {
        Name1 = name, Name2 = name, Class = cls, Level = 40, HP = 5_000, MaxHP = 5_000, CombatSpeed = CombatSpeed.Instant,
    };

    private static Monster Ogre(int level = 20, bool boss = false, string family = "") => new Monster
    {
        Name = "Ogre", Level = level, HP = 5000, MaxHP = 5000, Strength = 50, Defence = 20, IsBoss = boss, FamilyName = family,
    };

    /// <summary>A Sage teammate beside a leader and a tank. The leader is result.Player.</summary>
    private static (CombatEngine engine, Character sage, Character leader, Character tank, CombatResult result) Party(int level)
    {
        var sage = Sage(level);
        var leader = Ally("Leader", CharacterClass.Ranger);
        var tank = Ally("Tank", CharacterClass.Warrior);
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, leader);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { sage, tank });
        return (engine, sage, leader, tank, new CombatResult { Player = leader });
    }

    /// <summary>Every kind of Sage ward already up on everyone.</summary>
    private static void WardEveryone(params Character[] party)
    {
        foreach (var c in party)
        {
            CombatEngine.ApplyWardHighestWins(c, 500, 999);
            c.ApplyStatus(StatusEffect.Blur, 999);
            c.HasStatusImmunity = true;
            c.HasOceanMemory = true;
        }
    }

    private static int? Choose(CombatEngine engine, Character sage, List<Monster> monsters, CombatResult result, out bool isWard, out Monster? target)
    {
        var c = engine.ChooseSageTeammateSpell(sage, monsters, result);
        isWard = c?.isWard ?? false;
        target = c?.target;
        return c?.spell.Level;
    }

    // ---- area control ----

    [Fact]
    public void APack_GetsAreaControl()
    {
        var (engine, sage, leader, tank, result) = Party(75);
        WardEveryone(sage, leader, tank);
        var pack = new List<Monster> { Ogre(), Ogre(), Ogre() };
        Choose(engine, sage, pack, result, out bool ward, out var target).Should().Be(19, "Mass Confusion on a pack of three");
        ward.Should().BeFalse();
        target.Should().BeNull("an area spell has no single target");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OneOrTwoWeakMonsters_GetNoAreaControl(int count)
    {
        var (engine, sage, leader, tank, result) = Party(75);
        WardEveryone(sage, leader, tank);
        var few = Enumerable.Range(0, count).Select(_ => Ogre()).ToList();
        Choose(engine, sage, few, result, out _, out _).Should().BeNull("no pack and no strong target: the Sage attacks");
    }

    [Fact]
    public void AreaControl_SkipsAPackItAlreadyHolds_AndTakesTheNextSpell()
    {
        var (engine, sage, leader, tank, result) = Party(75);
        WardEveryone(sage, leader, tank);
        var pack = new List<Monster> { Ogre(), Ogre(), Ogre() };
        foreach (var m in pack) { m.IsConfused = true; m.ConfusedDuration = 2; }
        Choose(engine, sage, pack, result, out _, out _).Should().Be(18, "the pack is confused already, so Unveil the Pattern");
    }

    [Fact]
    public void OldGods_AreNotCompelled()
    {
        var (engine, sage, leader, tank, result) = Party(55);
        WardEveryone(sage, leader, tank);
        var gods = new List<Monster> { Ogre(60, true, "OldGod"), Ogre(60, true, "OldGod"), Ogre(60, true, "OldGod") };
        foreach (var m in gods) { m.IsSlowed = true; m.SlowDuration = 2; }
        var spell = Choose(engine, sage, gods, result, out _, out var target);
        spell.Should().NotBe(14, "Old Gods resist Compel");
        spell.Should().Be(6, "Scholar's Mark on the strongest god instead");
        target.Should().NotBeNull();
    }

    [Fact]
    public void Bosses_AreNotSlumbered()
    {
        var (engine, sage, leader, tank, result) = Party(55);
        WardEveryone(sage, leader, tank);
        var bosses = new List<Monster> { Ogre(60, true), Ogre(60, true), Ogre(60, true) };
        foreach (var m in bosses) { m.IsSlowed = true; m.SlowDuration = 2; m.TauntRoundsLeft = 2; }
        Choose(engine, sage, bosses, result, out _, out _).Should().NotBe(10, "bosses are immune to Slumber Mist");
    }

    [Fact]
    public void ADisabledSpell_IsNotCast()
    {
        var (engine, sage, leader, tank, result) = Party(75);
        WardEveryone(sage, leader, tank);
        leader.TeammateDisabledSpells[sage.GetSkillToggleKey()] = new List<string> { "Mass Confusion" };
        Choose(engine, sage, new List<Monster> { Ogre(), Ogre(), Ogre() }, result, out _, out _).Should().Be(18);
    }

    // ---- single strong target ----

    [Fact]
    public void AStrongMonster_IsFrozen_ThroughTheHoldBudget()
    {
        var (engine, sage, leader, tank, result) = Party(40);
        WardEveryone(sage, leader, tank);
        var brute = Ogre(45);
        Choose(engine, sage, new List<Monster> { brute }, result, out _, out var target).Should().Be(4, "Freeze");
        target.Should().BeSameAs(brute);

        for (int i = 0; i < 40 && !brute.IsFrozen; i++)
        {
            sage.Mana = sage.MaxMana;
            engine.TryTeammateSageSpell(sage, new List<Monster> { brute }, result).GetAwaiter().GetResult().Should().BeTrue();
        }
        brute.IsFrozen.Should().BeTrue();
        brute.FrozenDuration.Should().BeInRange(1, GameConfig.MaxStunDurationNormal);
        brute.HoldsThisFight.Should().Be(1, "the freeze counts against the shared hold budget");
        Choose(engine, sage, new List<Monster> { brute }, result, out _, out _).Should().NotBe(4, "a held monster is not frozen again");
    }

    // ---- party wards ----

    [Fact]
    public void AnUnwardedParty_GetsAWard()
    {
        var (engine, sage, _, _, result) = Party(75);
        Choose(engine, sage, new List<Monster> { Ogre() }, result, out bool ward, out _).Should().Be(20, "Noctura's Veil, the strongest ward");
        ward.Should().BeTrue();
    }

    [Fact]
    public void AWardedParty_GetsNoWard()
    {
        var (engine, sage, leader, tank, result) = Party(75);
        WardEveryone(sage, leader, tank);
        Choose(engine, sage, new List<Monster> { Ogre() }, result, out bool ward, out _).Should().BeNull();
        ward.Should().BeFalse();
    }

    [Fact]
    public void Wards_AreNotSpammed()
    {
        var (engine, sage, leader, tank, result) = Party(95);
        var one = new List<Monster> { Ogre() };
        var cast = new List<int>();
        for (int turn = 0; turn < 30; turn++)
        {
            sage.Mana = sage.MaxMana;
            var c = engine.ChooseSageTeammateSpell(sage, one, result);
            if (c == null || !c.Value.isWard) break;
            // a fizzle repeats the same ward next turn; count each run once
            if (cast.Count == 0 || cast[^1] != c.Value.spell.Level) cast.Add(c.Value.spell.Level);
            engine.TryTeammateSageSpell(sage, one, result).GetAwaiter().GetResult();
        }
        cast.Should().Equal(new[] { 20, 16, 22 }, "Noctura's Veil, then Mind Blank for the immunity, then Ocean's Memory, each once");
        foreach (var c in new[] { sage, leader, tank })
        {
            c.MagicACBonus.Should().BeGreaterThan(0, c.Name2);
            c.HasOceanMemory.Should().BeTrue(c.Name2);
        }
    }

    [Fact]
    public void ADulledPack_IsSlowed_ByTheTeammatesCast()
    {
        var (engine, sage, leader, tank, result) = Party(20);
        WardEveryone(sage, leader, tank);
        var pack = new List<Monster> { Ogre(10), Ogre(10), Ogre(10), Ogre(10) };
        for (int i = 0; i < 40 && !pack.All(m => m.IsSlowed); i++)
        {
            sage.Mana = sage.MaxMana;
            engine.TryTeammateSageSpell(sage, pack, result).GetAwaiter().GetResult().Should().BeTrue();
        }
        pack.Should().OnlyContain(m => m.IsSlowed, "Dulling Mist reaches every enemy");
    }

    // ---- renamed spells in a teammate's disabled list ----

    private static HashSet<string> Disabled(CombatEngine engine, Character caster) =>
        (HashSet<string>)typeof(CombatEngine).GetMethod("GetDisabledSpellsFor", F)!.Invoke(engine, new object[] { caster })!;

    [Fact]
    public void ASagesOldSpellNames_MoveToTheNewNames()
    {
        var (engine, sage, leader, _, _) = Party(75);
        var stored = new List<string> { "Duplicate", "Roast", "Giant Form", "Dominate", "Summon Demon", "Mind Spike" };
        leader.TeammateDisabledSpells[sage.GetSkillToggleKey()] = stored;
        Disabled(engine, sage).Should().BeEquivalentTo(new[]
            { "Dulling Mist", "Scholar's Mark", "Slumber Mist", "Compel", "Unveil the Pattern", "Mind Spike" });
        stored.Should().BeEquivalentTo(new[]
            { "Dulling Mist", "Scholar's Mark", "Slumber Mist", "Compel", "Unveil the Pattern", "Mind Spike" }, "the stored list is fixed too");
    }

    [Fact]
    public void AMagiciansSummonDemon_KeepsItsName()
    {
        var (engine, _, leader, _, _) = Party(75);
        var mage = Ally("Mage", CharacterClass.Magician);
        leader.TeammateDisabledSpells[mage.GetSkillToggleKey()] = new List<string> { "Summon Demon", "Dominate" };
        Disabled(engine, mage).Should().BeEquivalentTo(new[] { "Summon Demon", "Dominate" });
        SpellSystem.RemapLegacySageDisabledSpells(mage, new List<string> { "Summon Demon" }).Should().BeFalse();
    }

    [Fact]
    public void TheSkillEditor_AlsoRenames()
    {
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        src.Should().Contain("SpellSystem.RemapLegacySageDisabledSpells(teammate, disabledSpells);");
    }

    // ---- equal wards: the longer one wins ----

    [Fact]
    public void AnEqualWholeFightWard_ReplacesAShortOne()
    {
        var ally = Ally("Tank", CharacterClass.Warrior);
        CombatEngine.ApplyWardHighestWins(ally, 100, 5).Should().BeTrue();
        CombatEngine.ApplyWardHighestWins(ally, 100, 999).Should().BeTrue("same strength, longer ward");
        ally.ActiveStatuses[StatusEffect.Blessed].Should().Be(999);
    }

    [Fact]
    public void AnEqualShortWard_DoesNotCutAWholeFightOne()
    {
        var ally = Ally("Tank", CharacterClass.Warrior);
        CombatEngine.ApplyWardHighestWins(ally, 100, 999).Should().BeTrue();
        CombatEngine.ApplyWardHighestWins(ally, 100, 5).Should().BeFalse();
        CombatEngine.ApplyWardHighestWins(ally, 100, 999).Should().BeFalse("the same ward again changes nothing");
        ally.ActiveStatuses[StatusEffect.Blessed].Should().Be(999);
        ally.MagicACBonus.Should().Be(100);
    }

    // ---- knowledge: the seals and the Settlement Library ----

    /// <summary>The player's own Sage casting (currentPlayer is the caster), with some seals collected.</summary>
    private static (CombatEngine engine, Character sage, Character ally, CombatResult result) PlayerSage(int seals)
    {
        var sage = Sage(60);
        var ally = Ally("Tank", CharacterClass.Warrior);
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, sage);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { ally });
        var story = StoryProgressionSystem.Instance.CollectedSeals;
        story.Clear();
        foreach (var s in Enum.GetValues<SealType>().Take(seals)) story.Add(s);
        return (engine, sage, ally, new CombatResult { Player = sage });
    }

    private static SpellSystem.SpellResult Ward(int bonus, int duration, string effect = "fog") =>
        new SpellSystem.SpellResult { Success = true, ProtectionBonus = bonus, Duration = duration, SpecialEffect = effect };

    private static void GiveLibrary(Character c)
    {
        c.SettlementBuffType = (int)SettlementBuffType.LibraryXP;
        c.SettlementBuffCombats = 5;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(3, 15)]
    [InlineData(7, 35)]
    [InlineData(9, 35)]
    public void EachSeal_AddsFivePercent_UpToThirtyFive(int seals, int percent)
    {
        CombatEngine.SageSealWardPercentFor(seals).Should().Be(percent);
    }

    [Fact]
    public void TheSealBonus_CountsBeforeHighestWins()
    {
        var (engine, sage, ally, result) = PlayerSage(7);
        try
        {
            CombatEngine.ApplyWardHighestWins(ally, 120, 999);
            engine.ApplySagePartyWard(sage, Ward(100, 999), result);
            ally.MagicACBonus.Should().Be(135, "100 plus 35% beats the 120 ward already up");
            sage.MagicACBonus.Should().Be(135);
        }
        finally { StoryProgressionSystem.Instance.CollectedSeals.Clear(); }
    }

    [Fact]
    public void ASageTeammate_UsesTheLeadersSeals()
    {
        var (engine, _, ally, result) = PlayerSage(7);
        try
        {
            var teammate = Sage(60);
            engine.ApplySagePartyWard(teammate, Ward(100, 999), result);
            ally.MagicACBonus.Should().Be(135, "a Sage teammate's ward takes the leader's seals");
        }
        finally { StoryProgressionSystem.Instance.CollectedSeals.Clear(); }
    }

    /// <summary>An engine whose printed text can be read back.</summary>
    private static (CombatEngine engine, MemoryStream output) MarkEngine(Character player)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
        return (engine, output);
    }

    private static string Printed(CombatEngine engine, MemoryStream output)
    {
        var term = (TerminalEmulator)typeof(CombatEngine).GetField("terminal", F)!.GetValue(engine)!;
        term.StreamWriterInternal?.Flush();
        return System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    private static void Mark(CombatEngine engine, Monster m, string effect, Character caster) =>
        typeof(CombatEngine).GetMethod("HandleSpecialSpellEffectOnMonster", F)!
            .Invoke(engine, new object[] { m, effect, effect == "scholars_mark" ? 3 : 2, caster, 0L, new CombatResult { Player = caster } });

    /// <summary>One hit of 1000 on the monster through the shared damage path; the damage it took.</summary>
    private static long HitFor1000(CombatEngine engine, Monster m, Character player)
    {
        m.ArmPow = 0;
        long before = m.HP;
        ((Task<bool>)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
            .Invoke(engine, new object?[] { m, 1000L, new CombatResult { Player = player }, "attack", null, false })!).GetAwaiter().GetResult();
        return before - m.HP;
    }

    [Fact]
    public void TheLibrary_SharpensScholarsMark_To45Percent()
    {
        var sage = Sage(60);
        GiveLibrary(sage);
        var (engine, output) = MarkEngine(sage);
        var ogre = Ogre(45);
        Mark(engine, ogre, "scholars_mark", sage);
        ogre.IsMarked.Should().BeTrue();
        ogre.MarkedBonusPercent.Should().Be(45);
        HitFor1000(engine, ogre, sage).Should().Be(1450, "a Library-sharpened mark adds 45%");
        Printed(engine, output).Should().Contain("Library study sharpens it", "the sharper mark says so when it lands");
    }

    [Fact]
    public void WithoutTheLibrary_ScholarsMarkStaysAt30Percent()
    {
        var sage = Sage(60);
        var (engine, output) = MarkEngine(sage);
        var ogre = Ogre(45);
        Mark(engine, ogre, "scholars_mark", sage);
        ogre.IsMarked.Should().BeTrue();
        HitFor1000(engine, ogre, sage).Should().Be(1300, "no Library visit, the usual 30%");
        Printed(engine, output).Should().NotContain("Library study sharpens it");
    }

    [Fact]
    public void TheSharperMark_IsFixedAtCast()
    {
        var sage = Sage(60);
        GiveLibrary(sage);
        var (engine, _) = MarkEngine(sage);
        var ogre = Ogre(45);
        Mark(engine, ogre, "scholars_mark", sage);
        sage.SettlementBuffCombats = 0;   // the Library buff runs out while the mark is up
        sage.HasSettlementBuff.Should().BeFalse();
        HitFor1000(engine, ogre, sage).Should().Be(1450, "the mark keeps the percent it was cast with");
    }

    [Fact]
    public void TheLibrary_SharpensUnveilThePattern_Too()
    {
        var sage = Sage(60);
        GiveLibrary(sage);
        var (engine, _) = MarkEngine(sage);
        var ogre = Ogre(45);
        Mark(engine, ogre, "unveil_pattern", sage);
        ogre.MarkedBonusPercent.Should().Be(45);
        HitFor1000(engine, ogre, sage).Should().Be(1450);
    }

    [Fact]
    public void ASageTeammate_MarksThroughTheSamePath()
    {
        var leader = Ally("Leader", CharacterClass.Ranger);
        var (engine, _) = MarkEngine(leader);
        var teammate = Sage(60);
        var plain = Ogre(45);
        Mark(engine, plain, "scholars_mark", teammate);
        HitFor1000(engine, plain, leader).Should().Be(1300, "a teammate without the Library buff marks for 30%");

        GiveLibrary(teammate);
        var sharp = Ogre(45);
        Mark(engine, sharp, "scholars_mark", teammate);
        HitFor1000(engine, sharp, leader).Should().Be(1450, "the buff is read from the caster, player or teammate");
    }

    [Fact]
    public void TheLibrary_LeavesMarkRoundsWardsAndHoldsAlone()
    {
        var sage = Sage(60);
        GiveLibrary(sage);
        var (engine, _) = MarkEngine(sage);
        var ogre = Ogre(45);
        Mark(engine, ogre, "scholars_mark", sage);
        ogre.MarkedDuration.Should().Be(3, "the Library changes the percent, not the rounds");

        var (wardEngine, wardSage, ally, result) = PlayerSage(0);
        GiveLibrary(wardSage);
        wardEngine.ApplySagePartyWard(wardSage, Ward(100, 4, "shadow"), result);
        ally.ActiveStatuses[StatusEffect.Blessed].Should().Be(4, "the Library no longer touches a ward");
        ally.ActiveStatuses[StatusEffect.Blur].Should().Be(4);

        var brute = Ogre(45);
        typeof(CombatEngine).GetMethod("HandleSpecialSpellEffectOnMonster", F)!
            .Invoke(wardEngine, new object[] { brute, "freeze", 2, wardSage, 0L, result });
        brute.IsFrozen.Should().BeTrue();
        brute.FrozenDuration.Should().Be(2, "never a hold");
    }

    [Fact]
    public void AnEndedMark_ForgetsItsPercent()
    {
        var sage = Sage(60);
        GiveLibrary(sage);
        var (engine, _) = MarkEngine(sage);
        var ogre = Ogre(45);
        Mark(engine, ogre, "scholars_mark", sage);
        ogre.MarkedDuration = 1;
        ogre.StatusTickedThisRound = false;
        ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { ogre, sage, new CombatResult { Player = sage }, null })!).GetAwaiter().GetResult();
        ogre.IsMarked.Should().BeFalse();
        ogre.MarkedBonusPercent.Should().Be(0, "a later plain mark must not inherit 45%");
        ogre.IsMarked = true; ogre.MarkedDuration = 3;   // another class's mark sets no percent
        HitFor1000(engine, ogre, sage).Should().Be(1300);
    }

    [Fact]
    public void TheSharperMarkLine_IsInEveryLanguage()
    {
        string dir = Path.Combine(Leftovers1114BTests.RepoRoot(), "Localization");
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            File.ReadAllText(Path.Combine(dir, lang + ".json")).Should().Contain("\"combat.sage_marked_library\":", lang);
    }

    [Fact]
    public void TheSealLine_IsInEveryLanguage()
    {
        string dir = Path.Combine(Leftovers1114BTests.RepoRoot(), "Localization");
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            File.ReadAllText(Path.Combine(dir, lang + ".json")).Should().Contain("\"combat.sage_seal_ward\":", lang);
    }

    // ---- a teammate's Veloura's Embrace wards the party ----

    /// <summary>
    /// A Sage teammate casts its party heal until the cast lands (SpellSystem.CastSpell rolls on
    /// Random.Shared and can fizzle). Returns the party and the ward value the landed cast gave.
    /// </summary>
    private static (Character sage, Character leader, Character tank, Character fallen, int ward) TeammateEmbrace(int tankWard)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var (engine, sage, leader, tank, result) = Party(100);
            var fallen = Ally("Fallen", CharacterClass.Warrior);
            fallen.HP = 0;
            typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { sage, tank, fallen });
            if (tankWard > 0) CombatEngine.ApplyWardHighestWins(tank, tankWard, 999);
            leader.HP = 1000;
            var cast = ((Task<bool>)typeof(CombatEngine).GetMethod("TeammateHealWithSpell", F)!
                .Invoke(engine, new object[] { sage, leader, result })!).GetAwaiter().GetResult();
            cast.Should().BeTrue();
            if (leader.HP == 1000) continue;   // the cast fizzled
            return (sage, leader, tank, fallen, leader.MagicACBonus);
        }
        throw new Exception("the party heal never landed");
    }

    [Fact]
    public void ATeammatesVelourasEmbrace_WardsEachLivingAlly()
    {
        var (sage, leader, tank, _, ward) = TeammateEmbrace(0);
        ward.Should().BeGreaterThan(0, "the party heal carries a ward");
        foreach (var c in new[] { sage, leader, tank })
        {
            c.MagicACBonus.Should().Be(ward, c.DisplayName);
            c.ActiveStatuses.Should().ContainKey(StatusEffect.Blessed, c.DisplayName);
        }
    }

    [Fact]
    public void ATeammatesVelourasEmbrace_KeepsAStrongerWard()
    {
        var (_, leader, tank, _, ward) = TeammateEmbrace(100_000);
        ward.Should().BeGreaterThan(0);
        tank.MagicACBonus.Should().Be(100_000, "the highest ward wins on the teammate path too");
    }

    [Fact]
    public void ATeammatesVelourasEmbrace_SkipsAFallenAlly()
    {
        var (_, _, _, fallen, ward) = TeammateEmbrace(0);
        ward.Should().BeGreaterThan(0);
        fallen.MagicACBonus.Should().Be(0, "a fallen ally gets no ward");
        fallen.ActiveStatuses.Should().NotContainKey(StatusEffect.Blessed);
    }

    [Fact]
    public void TheTeammateTurn_TriesTheSageSpellsFirst()
    {
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        int sage = src.IndexOf("if (teammate.Class == CharacterClass.Sage && await TryTeammateSageSpell(teammate, monsters, result))", StringComparison.Ordinal);
        int offense = src.IndexOf("var spellAction = await TryTeammateOffensiveSpell(teammate, monsters, result);", StringComparison.Ordinal);
        sage.Should().BeGreaterThan(0);
        offense.Should().BeGreaterThan(sage);
    }
}
