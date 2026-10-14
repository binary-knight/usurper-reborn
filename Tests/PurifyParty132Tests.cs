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

/// <summary>
/// 1.3.2: Purify is typed as a heal but restores no hit points. A party member choosing a heal
/// for an injured ally must never pick it: the cast spent the turn and the mana for nothing.
/// </summary>
[Collection("SharedGameSingletons")]
public class PurifyParty132Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int CureLight = 1, Purify = 3, CureWounds = 5;

    private static SpellSystem.SpellInfo Info(int slot) => SpellSystem.GetSpellInfo(CharacterClass.Cleric, slot);
    private static int Cost(Character caster, int slot) => SpellSystem.CalculateManaCost(Info(slot), caster);

    /// <summary>A Cleric party member (not a companion), at full health.</summary>
    private static Character Cleric(int level, long mana, long potions = 0) => new Character
    {
        Name1 = "Healer", Name2 = "Healer", ID = "npc-healer-132", Class = CharacterClass.Cleric, Race = CharacterRace.Human,
        Level = level, Wisdom = 10, HP = 500, MaxHP = 500, Mana = mana, MaxMana = 5000, Healing = potions,
        CombatSpeed = CombatSpeed.Instant,
    };

    private sealed class Fight
    {
        public CombatEngine Engine = null!;
        public TerminalEmulator Terminal = null!;
        public Character Leader = null!;
        public CombatResult Result = null!;
    }

    /// <summary>The teammate beside a leader at 30 percent health: under every heal threshold.</summary>
    private static Fight Party(Character teammate)
    {
        var leader = new Character
        {
            Name1 = "Leader", Name2 = "Leader", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = 20, HP = 300, MaxHP = 1000, CombatSpeed = CombatSpeed.Instant,
        };
        var terminal = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        var engine = new CombatEngine(terminal);
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, leader);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, leader);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { teammate });
        var result = new CombatResult
        {
            Player = leader, Monsters = new List<Monster>(), Teammates = new List<Character> { teammate }, CombatLog = new List<string>(),
        };
        return new Fight { Engine = engine, Terminal = terminal, Leader = leader, Result = result };
    }

    /// <summary>One heal decision through the real method; returns what it answered and what it printed.</summary>
    private static (bool acted, string shown) HealTurn(Fight fight, Character teammate)
    {
        fight.Terminal.StartCapture();
        bool acted = ((Task<bool>)typeof(CombatEngine).GetMethod("TryTeammateHealAction", F)!
            .Invoke(fight.Engine, new object[] { teammate, new List<Character> { fight.Leader, teammate }, fight.Result })!)
            .GetAwaiter().GetResult();
        string shown = fight.Terminal.StopCapture() ?? "";
        return (acted, shown);
    }

    private static void NoWastedCast(Fight fight, string shown)
    {
        shown.Should().NotContain(Loc.Get("combat.spell_fizzles"), "a heal turn never ends in a spell that does nothing");
        shown.Should().NotContain(Info(Purify).DisplayName, "Purify is not a heal for an injured ally");
        fight.Result.CombatLog.Should().NotContain(l => l.Contains("fizzles"));
    }

    // ---- test 1: the teammate's own heal choice ----

    [Fact]
    public void ATeammateWhoseHighestHealIsPurify_CastsCureLightOnTheInjuredMember()
    {
        // Level 8 to 15: Purify (slot 3) is open and Cure Wounds (slot 5) is not.
        var cleric = Cleric(level: 10, mana: 1000);
        var fight = Party(cleric);
        int cureLightCost = Cost(cleric, CureLight);

        var (acted, shown) = HealTurn(fight, cleric);

        acted.Should().BeTrue("an injured member under the threshold is healed");
        NoWastedCast(fight, shown);
        fight.Leader.HP.Should().BeGreaterThan(300, "Cure Light restores hit points");
        cleric.Mana.Should().Be(1000 - cureLightCost, "the only mana spent is what Cure Light costs");
        shown.Should().Contain(Info(CureLight).DisplayName);
    }

    [Fact]
    public void ATeammateWhoCanAffordPurifyButNotCureWounds_CastsCureLight()
    {
        // Level 16 and up: with mana between the two costs, Purify was the highest heal it could pay for.
        var cleric = Cleric(level: 20, mana: 0);
        int purifyCost = Cost(cleric, Purify), cureWoundsCost = Cost(cleric, CureWounds), cureLightCost = Cost(cleric, CureLight);
        purifyCost.Should().BeLessThan(cureWoundsCost);
        cleric.Mana = purifyCost;
        var fight = Party(cleric);

        var (acted, shown) = HealTurn(fight, cleric);

        acted.Should().BeTrue();
        NoWastedCast(fight, shown);
        fight.Leader.HP.Should().BeGreaterThan(300);
        cleric.Mana.Should().Be(purifyCost - cureLightCost);
    }

    [Fact]
    public void ATeammateWithNoHealingSpellLeft_DrinksAPotionForTheInjuredMember()
    {
        // Cure Light switched off for this teammate: Purify is the only spell of the heal type left.
        var cleric = Cleric(level: 10, mana: 1000, potions: 3);
        var fight = Party(cleric);
        fight.Leader.TeammateDisabledSpells[cleric.GetSkillToggleKey()] = new List<string> { Info(CureLight).Name };

        var (acted, shown) = HealTurn(fight, cleric);

        acted.Should().BeTrue("the potion is the next thing the heal action tries");
        NoWastedCast(fight, shown);
        cleric.Mana.Should().Be(1000, "no spell was cast");
        cleric.Healing.Should().Be(2, "one potion was used");
        fight.Leader.HP.Should().BeGreaterThan(300);
    }

    [Fact]
    public void ATeammateWithNoHealingSpellAndNoPotion_TakesNoHealAction()
    {
        var cleric = Cleric(level: 10, mana: 1000, potions: 0);
        var fight = Party(cleric);
        fight.Leader.TeammateDisabledSpells[cleric.GetSkillToggleKey()] = new List<string> { Info(CureLight).Name };

        var (acted, shown) = HealTurn(fight, cleric);

        acted.Should().BeFalse("with nothing that heals, the turn goes on to the teammate's other actions");
        NoWastedCast(fight, shown);
        cleric.Mana.Should().Be(1000);
        fight.Leader.HP.Should().Be(300);
    }

    // ---- test 2: the helper against what each cast really sets ----

    private static int StaffId() => EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff).Id;

    private static Character CasterOf(CharacterClass cls) => new Character
    {
        Name1 = "Caster", Name2 = "Caster", Class = cls, Race = CharacterRace.Human, Level = 100,
        Wisdom = 50, Intelligence = 50, HP = 1000, MaxHP = 100_000, Mana = 100_000, MaxMana = 100_000,
        CombatSpeed = CombatSpeed.Instant,
        EquippedItems = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.MainHand] = StaffId() },
    };

    [Fact]
    public void EverySpellOfTheHealType_RestoresHitPointsExactlyWhenTheHelperSaysSo()
    {
        var mismatches = new List<string>();
        var seen = new List<string>();
        foreach (CharacterClass cls in Enum.GetValues(typeof(CharacterClass)))
        {
            foreach (var spell in SpellSystem.GetAllSpellsForClass(cls).Where(s => s.SpellType == "Heal"))
            {
                var cast = SpellSystem.CastSpell(CasterOf(cls), spell.Level, null);
                cast.Success.Should().BeTrue($"{cls} {spell.Name} is castable by a level 100 caster with mana");
                bool heals = cast.Healing > 0;
                seen.Add($"{cls}/{spell.Level}/{spell.Name}/{(heals ? "heals" : "none")}");
                if (SpellSystem.RestoresHitPoints(cls, spell) != heals)
                    mismatches.Add($"{cls} slot {spell.Level} {spell.Name}: cast healing {cast.Healing}, helper {SpellSystem.RestoresHitPoints(cls, spell)}");
            }
        }
        mismatches.Should().BeEmpty("a spell of the heal type that sets no healing amount must be excluded by the helper");
        seen.Should().HaveCount(13, "the heal type list is pinned; a new heal spell is added here on purpose");
        seen.Where(s => s.EndsWith("/none")).Should().Equal("Cleric/3/Purify/none");
    }

    [Fact]
    public void TheHelper_IsFalseForSpellsThatAreNotOfTheHealType()
    {
        SpellSystem.RestoresHitPoints(CharacterClass.Cleric, Info(2)).Should().BeFalse("Divine Shield is a buff");
        SpellSystem.RestoresHitPoints(CharacterClass.Cleric, Info(CureLight)).Should().BeTrue();
        SpellSystem.RestoresHitPoints(CharacterClass.Cleric, Info(Purify)).Should().BeFalse();
    }

    // ---- tests 3 and 10: a grouped follower's typed commands ----

    /// <summary>A follower Cleric who has learned exactly the given spell slots.</summary>
    private static Character Follower(params int[] learnedSlots)
    {
        var cleric = Cleric(level: 10, mana: 1000);
        cleric.Spell = Enumerable.Range(1, 25).Select(slot => new List<bool> { learnedSlots.Contains(slot) }).ToList();
        return cleric;
    }

    private static CombatAction Parse(Fight fight, Character follower, string typed) =>
        (CombatAction)typeof(CombatEngine).GetMethod("ParseGroupCombatInput", F)!
            .Invoke(fight.Engine, new object[] { typed, follower, fight.Leader, new List<Monster>(), fight.Result })!;

    [Theory]
    [InlineData("C")]
    [InlineData("H")]
    public void AFollowersHealCommand_PicksCureLightNotPurify(string typed)
    {
        var follower = Follower(CureLight, Purify);
        var fight = Party(follower);

        var action = Parse(fight, follower, typed);

        action.Type.Should().Be(CombatActionType.CastSpell);
        action.SpellIndex.Should().Be(CureLight, "the heal picked for an injured ally restores hit points");
        action.HealTargetOverride.Should().BeSameAs(fight.Leader, "the most injured member is the target");
    }

    [Theory]
    [InlineData("C")]
    [InlineData("H")]
    public void AFollowersHealCommand_WithOnlyPurifyLearned_CastsNothing(string typed)
    {
        var follower = Follower(Purify);
        var fight = Party(follower);

        var action = Parse(fight, follower, typed);

        action.Type.Should().NotBe(CombatActionType.CastSpell, "Purify is not a heal for an injured ally");
    }

    [Fact]
    public void ANumberedOrderOfPurify_StillCastsPurify()
    {
        var follower = Follower(CureLight, Purify);
        var fight = Party(follower);

        var onSelf = Parse(fight, follower, "C2T1");
        onSelf.Type.Should().Be(CombatActionType.CastSpell);
        onSelf.SpellIndex.Should().Be(Purify, "the second spell in the follower's list is Purify, and it was ordered by number");
        onSelf.HealTargetOverride.Should().BeSameAs(follower);

        var onLeader = Parse(fight, follower, "C2T2");
        onLeader.SpellIndex.Should().Be(Purify);
        onLeader.HealTargetOverride.Should().BeSameAs(fight.Leader);
    }

    // ---- test 4: who is counted as able to heal a low ally ----

    [Fact]
    public void AnOwnerWhoseOnlyHealTypeSpellIsPurify_IsNotCountedAsAbleToHeal()
    {
        var owner = Follower(Purify);
        owner.Healing = 0;
        var ally = new Character { Name2 = "Ally", Class = CharacterClass.Warrior, Race = CharacterRace.Human, HP = 40, MaxHP = 100 };
        var engine = new CombatEngine();
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, owner);
        engine.NoteOwnerTurn(owner, new List<Character> { ally }, new CombatAction { Type = CombatActionType.Attack });

        engine.CouldHaveHelped(ally, owner).Should().BeFalse("Purify could not have restored the ally's hit points");

        var withCureLight = Follower(CureLight, Purify);
        withCureLight.Healing = 0;
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, withCureLight);
        engine.NoteOwnerTurn(withCureLight, new List<Character> { ally }, new CombatAction { Type = CombatActionType.Attack });
        engine.CouldHaveHelped(ally, withCureLight).Should().BeTrue("Cure Light could have");
    }

    // ---- test 9: the player's own Purify is unchanged ----

    private static void Afflict(Character c)
    {
        c.ApplyStatus(StatusEffect.Diseased, 5);
        c.ApplyStatus(StatusEffect.Poisoned, 5);
        c.Blind = c.Plague = c.Smallpox = c.Measles = c.Leprosy = c.LoversBane = true;
        c.Poison = 4; c.PoisonTurns = 6;
    }

    private static bool AnyAffliction(Character c) =>
        c.HasStatus(StatusEffect.Diseased) || c.HasStatus(StatusEffect.Poisoned) || c.Blind || c.Plague || c.Smallpox
        || c.Measles || c.Leprosy || c.LoversBane || c.Poison > 0 || c.PoisonTurns > 0;

    private static bool FullyAfflicted(Character c) =>
        c.HasStatus(StatusEffect.Diseased) && c.HasStatus(StatusEffect.Poisoned) && c.Blind && c.Plague && c.Smallpox
        && c.Measles && c.Leprosy && c.LoversBane && c.Poison == 4 && c.PoisonTurns == 6;

    /// <summary>The player is a Cleric with Cure Light and Purify; one ally fights beside them.</summary>
    private static (CombatEngine engine, TerminalEmulator terminal, Character player, Character ally, CombatResult result) PlayerParty(string input = "")
    {
        var player = Follower(CureLight, Purify);
        player.Name1 = player.Name2 = "Priest";
        var ally = new Character
        {
            Name1 = "Ally", Name2 = "Ally", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = 10, HP = 300, MaxHP = 1000, CombatSpeed = CombatSpeed.Instant,
        };
        var terminal = new TerminalEmulator(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(input)), new MemoryStream());
        var engine = new CombatEngine(terminal);
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { ally });
        var result = new CombatResult { Player = player, Monsters = new List<Monster>(), Teammates = new List<Character> { ally }, CombatLog = new List<string>() };
        return (engine, terminal, player, ally, result);
    }

    private static string PlayerCasts(CombatEngine engine, TerminalEmulator terminal, Character player, CombatResult result, CombatAction action)
    {
        terminal.StartCapture();
        ((Task)typeof(CombatEngine).GetMethod("ExecuteSpellMultiMonster", F)!
            .Invoke(engine, new object[] { player, new List<Monster>(), action, result })!).GetAwaiter().GetResult();
        return terminal.StopCapture() ?? "";
    }

    [Fact]
    public void ThePlayersPurifyOnThemselves_CleansEveryStateAndSaysSo()
    {
        var (engine, terminal, player, _, result) = PlayerParty();
        Afflict(player);
        int cost = Cost(player, Purify);

        string shown = PlayerCasts(engine, terminal, player, result, new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = Purify });

        AnyAffliction(player).Should().BeFalse("the self cast removes every state it removed before");
        player.Mana.Should().Be(1000 - cost);
        shown.Should().Contain(Loc.Get("combat.spell_purified", player.DisplayName));
    }

    [Fact]
    public void ThePlayersPurifyOnThemselves_WithNothingToCleanse_SaysAlreadyHealthy()
    {
        var (engine, terminal, player, _, result) = PlayerParty();

        string shown = PlayerCasts(engine, terminal, player, result, new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = Purify });

        shown.Should().Contain(Loc.Get("combat.spell_already_healthy", player.DisplayName));
        shown.Should().NotContain(Loc.Get("combat.spell_purified", player.DisplayName));
    }

    [Fact]
    public void ThePlayersPurifyAimedAtAnAlly_IsPinnedAsItIs_NobodyIsCleansed()
    {
        // Pinned, not endorsed: the cast spends the mana and changes nobody.
        var (engine, terminal, player, ally, result) = PlayerParty();
        Afflict(player);
        Afflict(ally);
        int cost = Cost(player, Purify);

        string shown = PlayerCasts(engine, terminal, player, result,
            new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = Purify, AllyTargetIndex = 0 });

        player.Mana.Should().Be(1000 - cost);
        FullyAfflicted(ally).Should().BeTrue();
        FullyAfflicted(player).Should().BeTrue();
        ally.HP.Should().Be(300);
        shown.Should().NotContain(Loc.Get("combat.spell_purified", ally.DisplayName));
        shown.Should().NotContain(Loc.Get("combat.spell_purified", player.DisplayName));
        shown.Should().NotContain(Loc.Get("combat.spell_already_healthy", player.DisplayName));
        result.CombatLog.Should().Contain($"{player.DisplayName} casts {Info(Purify).Name} on {ally.DisplayName}.");
    }

    [Fact]
    public void ThePlayersPurifyFromTheHealMenu_IsPinnedAsItIs_ListedAndNobodyIsCleansed()
    {
        // Pinned, not endorsed. Menu: 1 = cast a heal spell, 2 = Purify (after Cure Light), 1 = the ally.
        var (engine, terminal, player, ally, _) = PlayerParty("1\n2\n1\n");
        Afflict(player);
        Afflict(ally);
        int cost = Cost(player, Purify);

        terminal.StartCapture();
        var action = ((Task<CombatAction?>)typeof(CombatEngine).GetMethod("HandleHealAlly", F)!
            .Invoke(engine, new object[] { player, new List<Monster>() })!).GetAwaiter().GetResult();
        string shown = terminal.StopCapture() ?? "";

        action.Should().NotBeNull();
        action!.Type.Should().Be(CombatActionType.HealAlly, "the turn is used");
        shown.Should().Contain(Info(Purify).DisplayName, "the heal menu still lists Purify");
        player.Mana.Should().Be(1000 - cost);
        FullyAfflicted(ally).Should().BeTrue();
        FullyAfflicted(player).Should().BeTrue();
        ally.HP.Should().Be(300);
    }
}
