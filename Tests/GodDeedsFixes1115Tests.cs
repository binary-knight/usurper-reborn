using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 3 follow-up. The Murder taboo is recorded once, at the top of
/// BaseLocation.ApplyMurderConsequences, so the Magic Shop death spell (which calls it) costs Favor
/// like a street murder, and a street murder costs it once. Amara's AllyHealed deed is also recorded
/// for a heal spell cast on an ally from the spell menu and for a party heal that reaches an ally;
/// a self heal records nothing.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodDeedsFixes1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int PowerHatSlot = 8;          // Magician, single-target Heal
    private const int VelourasEmbraceSlot = 24;  // Sage, party Heal

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    // ---------------- Murder ----------------

    private static Player Murderer(string name, string god)
    {
        var p = new Player { Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human };
        p.RecalculateStats();
        p.HP = 100; p.MaxHP = 100; p.Gold = 1000; p.DaysInPrison = 0;
        p.Mental = 0; p.MentalHintShown = true; // Mental 0 Surrender: capture, no execution roll
        GodRegistry.SetWorshippedGod(p, god).Should().BeTrue();
        p.GodFavor = 30;
        return p;
    }

    private static async Task Murder(BaseLocation place, Player hero)
    {
        var term = new TerminalEmulator(new LineStream(new[] { "S", "", "", "" }), new MemoryStream());
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(place, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(place, hero);
        var victim = new NPC { Name1 = "Victim", Name2 = "Victim", Level = 5, HP = 50, MaxHP = 50 };
        var task = (Task)typeof(BaseLocation).GetMethod("ApplyMurderConsequences", F)!.Invoke(place, new object[] { hero, victim })!;
        var exit = await Assert.ThrowsAsync<LocationExitException>(() => task);
        exit.DestinationLocation.Should().Be(GameLocation.Prison);
    }

    [Theory]
    [InlineData("Amara", 20)]
    [InlineData("Judicar", 20)]   // Murder -5, then the capture's Imprisoned -5
    public async Task ADeathSpellMurder_CostsFavor(string god, int expected)
    {
        var hero = Murderer("DsMurder" + god, god);
        try
        {
            await Murder(new MagicShopLocation(), hero);
            hero.GodFavor.Should().Be(expected, "the death spell's murder goes through ApplyMurderConsequences");
        }
        finally { GodRegistry.SetWorshippedGod(hero, null); }
    }

    [Fact]
    public async Task AStreetMurder_CostsFavorOnce()
    {
        var hero = Murderer("StMurder", "Amara");
        try
        {
            await Murder(new MainStreetLocation(), hero);
            hero.GodFavor.Should().Be(20, "one murder is one Murder taboo of 10");
        }
        finally { GodRegistry.SetWorshippedGod(hero, null); }
    }

    [Fact]
    public void Murder_IsRecordedOnce_FirstInApplyMurderConsequences_AndTheDeathSpellCallsIt()
    {
        string src = Source("Scripts/Locations/BaseLocation.cs");
        Regex.Matches(src, Regex.Escape("GodAct.Murder")).Count.Should().Be(1, "one record covers the street murder and the death spell");
        int method = src.IndexOf("internal async Task ApplyMurderConsequences(Character player, NPC victim)", StringComparison.Ordinal);
        int hook = src.IndexOf("GodDeedSystem.Record(player, GodAct.Murder, terminal);", StringComparison.Ordinal);
        method.Should().BeGreaterThan(0);
        int firstAwait = src.IndexOf("await ", method, StringComparison.Ordinal);
        hook.Should().BeInRange(method, firstAwait, "the record comes before anything that can end the session");

        int streetStart = src.IndexOf("currentPlayer.MurdersToday++;", StringComparison.Ordinal);
        int streetEnd = src.IndexOf("await ApplyMurderConsequences(currentPlayer, npc);", streetStart, StringComparison.Ordinal);
        src.Substring(streetStart, streetEnd - streetStart)
            .Should().NotContain("GodDeedSystem.Record", "the street murder records through ApplyMurderConsequences only");

        string shop = Source("Scripts/Locations/MagicShopLocation.cs");
        shop.Should().Contain("await ApplyMurderConsequences(player, targetNPC);");
        shop.Should().NotContain("GodAct.Murder");
    }

    // ---------------- Amara: AllyHealed from the spell menu ----------------

    private static int StaffId() => EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff).Id;

    private static Character Caster(CharacterClass cls, int slot)
    {
        var c = new Character { Name1 = "Healer", Name2 = "Healer", AI = CharacterAI.Human, Class = cls, Level = 100, CombatSpeed = CombatSpeed.Instant };
        c.RecalculateStats();
        c.Wisdom = 50; c.Intelligence = 50; c.HP = 1000; c.MaxHP = 100_000; c.Mana = 100_000; c.MaxMana = 100_000;
        c.Healing = 0; c.ManaPotions = 0;
        c.EquippedItems = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.MainHand] = StaffId() };
        c.Spell[slot - 1][0] = true;
        GodRegistry.SetWorshippedGod(c, "Amara").Should().BeTrue();
        c.GodFavor = 30;
        return c;
    }

    private static Character Ally(string name) => new Character
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Ranger, Level = 40, HP = 1000, MaxHP = 100_000, CombatSpeed = CombatSpeed.Instant,
    };

    private static void Cast(Character caster, List<Character> teammates, CombatAction action, Character? leader)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, caster);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, teammates);
        var result = new CombatResult { Player = leader ?? caster };
        ((Task)typeof(CombatEngine).GetMethod("ExecuteSpellMultiMonster", F)!
            .Invoke(engine, new object[] { caster, new List<Monster>(), action, result })!).GetAwaiter().GetResult();
    }

    /// <summary>Casts until the spell lands on the watched character (a cast can fizzle); returns the caster's Favor change.</summary>
    private static int FavorChangeOnLandedCast(CharacterClass cls, int slot,
        Func<Character, (List<Character> team, CombatAction action, Character watched, Character? leader)> setup)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var caster = Caster(cls, slot);
            try
            {
                var (team, action, watched, leader) = setup(caster);
                long before = watched.HP;
                Cast(caster, team, action, leader);
                if (watched.HP == before) { caster.GodFavor.Should().Be(30, "a fizzled cast heals no one"); continue; }
                return caster.GodFavor - 30;
            }
            finally { GodRegistry.SetWorshippedGod(caster, null); }
        }
        throw new Exception("the heal never landed");
    }

    private static CombatAction SpellAction(int slot, int? allyIndex = null, Character? overrideTarget = null) =>
        new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = slot, AllyTargetIndex = allyIndex, HealTargetOverride = overrideTarget };

    [Fact]
    public void AHealSpellOnAnAlly_FromTheSpellMenu_IsAnAmaraDeed()
    {
        FavorChangeOnLandedCast(CharacterClass.Magician, PowerHatSlot, c =>
        {
            var ally = Ally("Menu");
            return (new List<Character> { ally }, SpellAction(PowerHatSlot, allyIndex: 0), ally, null);
        }).Should().Be(1);
    }

    [Fact]
    public void AHealSpellOnAnAlly_ByAGroupFollower_IsAnAmaraDeed()
    {
        FavorChangeOnLandedCast(CharacterClass.Magician, PowerHatSlot, c =>
        {
            var leader = Ally("Leader");
            return (new List<Character>(), SpellAction(PowerHatSlot, overrideTarget: leader), leader, leader);
        }).Should().Be(1);
    }

    [Fact]
    public void AHealSpellOnSelf_IsNoDeed()
    {
        FavorChangeOnLandedCast(CharacterClass.Magician, PowerHatSlot, c =>
            (new List<Character> { Ally("Bystander") }, SpellAction(PowerHatSlot), c, null)).Should().Be(0);
        FavorChangeOnLandedCast(CharacterClass.Magician, PowerHatSlot, c =>
            (new List<Character>(), SpellAction(PowerHatSlot, overrideTarget: c), c, null)).Should().Be(0);
    }

    [Fact]
    public void APartyHealReachingAllies_IsOneAmaraDeed()
    {
        FavorChangeOnLandedCast(CharacterClass.Sage, VelourasEmbraceSlot, c =>
        {
            var first = Ally("First");
            return (new List<Character> { first, Ally("Second") }, SpellAction(VelourasEmbraceSlot), first, null);
        }).Should().Be(1, "one deed per cast, however many allies it reaches");
    }

    [Fact]
    public void APartyHealByAFollower_ReachingTheLeader_IsAnAmaraDeed()
    {
        FavorChangeOnLandedCast(CharacterClass.Sage, VelourasEmbraceSlot, c =>
        {
            var leader = Ally("Lead");
            return (new List<Character>(), SpellAction(VelourasEmbraceSlot), leader, leader);
        }).Should().Be(1);
    }

    [Fact]
    public void APartyHealWithNoAlly_IsNoDeed()
    {
        FavorChangeOnLandedCast(CharacterClass.Sage, VelourasEmbraceSlot, c =>
            (new List<Character>(), SpellAction(VelourasEmbraceSlot), c, null)).Should().Be(0);
    }

    [Fact]
    public void AnAllyHeal_RespectsTheDailyDeedCap()
    {
        FavorChangeOnLandedCast(CharacterClass.Magician, PowerHatSlot, c =>
        {
            c.GodFavorDayGains[FavorSource.Deed.ToString()] = GameConfig.GodFavorDeedDailyCap;
            var ally = Ally("Capped");
            return (new List<Character> { ally }, SpellAction(PowerHatSlot, allyIndex: 0), ally, null);
        }).Should().Be(0, "the deed cap for today is spent");
    }
}
