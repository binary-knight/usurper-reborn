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
public class PurifyParty132Tests : IDisposable
{
    // Part B touches the companion singleton (a companion's HP sync, a companion's spell switch): restored here.
    private readonly Dictionary<CompanionId, int> _savedCompanionHp;
    private readonly List<string> _savedMiraDisabled;

    public PurifyParty132Tests()
    {
        _savedCompanionHp = new Dictionary<CompanionId, int>(CompanionHp());
        _savedMiraDisabled = CompanionSystem.Instance.GetCompanion(CompanionId.Mira)!.DisabledSpells.ToList();
    }

    public void Dispose()
    {
        var hp = CompanionHp();
        hp.Clear();
        foreach (var kv in _savedCompanionHp) hp[kv.Key] = kv.Value;
        var mira = CompanionSystem.Instance.GetCompanion(CompanionId.Mira)!.DisabledSpells;
        mira.Clear();
        foreach (var n in _savedMiraDisabled) mira.Add(n);
    }

    private static Dictionary<CompanionId, int> CompanionHp() =>
        (Dictionary<CompanionId, int>)typeof(CompanionSystem).GetField("companionCurrentHP", F)!.GetValue(CompanionSystem.Instance)!;

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
    public void ThePlayersPurifyAimedAtAnAlly_CuresTheAllysPoisonAndDisease_AndNotTheCaster()
    {
        // 1.3.2: was pinned as curing nobody; now the ally loses poison and disease, nothing else.
        var (engine, terminal, player, ally, result) = PlayerParty();
        Afflict(player);
        Afflict(ally);
        int cost = Cost(player, Purify);

        string shown = PlayerCasts(engine, terminal, player, result,
            new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = Purify, AllyTargetIndex = 0 });

        player.Mana.Should().Be(1000 - cost);
        AnyChosen(ally).Should().BeFalse();
        AllFlags(ally).Should().BeTrue();
        FullyAfflicted(player).Should().BeTrue();
        ally.HP.Should().Be(300);
        shown.Should().Contain(Loc.Get("combat.spell_purified", ally.DisplayName));
        shown.Should().NotContain(Loc.Get("combat.spell_purified", player.DisplayName));
        shown.Should().NotContain(Loc.Get("combat.spell_already_healthy", player.DisplayName));
        result.CombatLog.Should().Contain($"{player.DisplayName} casts {Info(Purify).Name} on {ally.DisplayName}.");
    }

    [Fact]
    public void ThePlayersPurifyFromTheHealMenu_IsListed_AndCuresTheAllysPoisonAndDisease()
    {
        // 1.3.2: was pinned as curing nobody. Menu: 1 = cast a heal spell, 2 = Purify (after Cure Light), 1 = the ally.
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
        AnyChosen(ally).Should().BeFalse();
        AllFlags(ally).Should().BeTrue();
        FullyAfflicted(player).Should().BeTrue();
        ally.HP.Should().Be(300);
    }

    // =====================================================================================
    // Part B: a party Cleric cures a party member who is poisoned or diseased, and the
    // player's own Purify aimed at an ally cures that ally. The states cured on someone else
    // are the statuses Poisoned and Diseased and the lasting poison counter with its turns.
    // =====================================================================================

    private static Character Member(string name, long hp = 1000, long maxHp = 1000) => new Character
    {
        Name1 = name, Name2 = name, ID = "npc-" + name.ToLowerInvariant(), Class = CharacterClass.Warrior,
        Race = CharacterRace.Human, Level = 10, HP = hp, MaxHP = maxHp, CombatSpeed = CombatSpeed.Instant,
    };

    private static Character CompanionMember(CompanionId id, string name)
    {
        var c = Member(name);
        c.IsCompanion = true;
        c.CompanionId = id;
        return c;
    }

    /// <summary>A Cleric teammate, a healthy leader and other members; every list is the real party.</summary>
    private static Fight PartyOf(Character cleric, params Character[] others)
    {
        var fight = Party(cleric);
        fight.Leader.HP = fight.Leader.MaxHP;
        var mates = new List<Character> { cleric };
        mates.AddRange(others);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(fight.Engine, mates);
        fight.Result.Teammates = new List<Character>(mates);
        return fight;
    }

    private static (bool acted, string shown) PartyTurn(Fight fight, Character cleric)
    {
        var members = new List<Character> { fight.Leader };
        members.AddRange(((List<Character>)typeof(CombatEngine).GetField("currentTeammates", F)!.GetValue(fight.Engine)!).Where(t => t.IsAlive));
        fight.Terminal.StartCapture();
        bool acted = ((Task<bool>)typeof(CombatEngine).GetMethod("TryTeammateHealAction", F)!
            .Invoke(fight.Engine, new object[] { cleric, members, fight.Result })!).GetAwaiter().GetResult();
        return (acted, fight.Terminal.StopCapture() ?? "");
    }

    private static void SetFlags(Character c) => c.Blind = c.Plague = c.Smallpox = c.Measles = c.Leprosy = c.LoversBane = true;
    private static bool AllFlags(Character c) => c.Blind && c.Plague && c.Smallpox && c.Measles && c.Leprosy && c.LoversBane;

    // ---- test 5: the party Cleric cures ----

    [Theory]
    [InlineData("poisoned")]
    [InlineData("diseased")]
    [InlineData("counter")]
    public void APartyCleric_CuresTheLeader(string state)
    {
        var cleric = Cleric(level: 10, mana: 1000);
        var fight = PartyOf(cleric);
        if (state == "poisoned") fight.Leader.ApplyStatus(StatusEffect.Poisoned, 5);
        if (state == "diseased") fight.Leader.ApplyStatus(StatusEffect.Diseased, 5);
        if (state == "counter") { fight.Leader.Poison = 4; fight.Leader.PoisonTurns = 6; }
        int cost = Cost(cleric, Purify);

        var (acted, shown) = PartyTurn(fight, cleric);

        acted.Should().BeTrue("an afflicted leader is cured when nobody needs a heal");
        fight.Leader.HasStatus(StatusEffect.Poisoned).Should().BeFalse();
        fight.Leader.HasStatus(StatusEffect.Diseased).Should().BeFalse();
        fight.Leader.Poison.Should().Be(0);
        fight.Leader.PoisonTurns.Should().Be(0);
        cleric.Mana.Should().Be(1000 - cost, "one Purify at its real cost");
        shown.Should().Contain(Info(Purify).DisplayName);
        shown.Should().Contain(Loc.Get("combat.spell_purified", fight.Leader.DisplayName));
        shown.Should().NotContain(Loc.Get("combat.spell_fizzles"));
    }

    [Fact]
    public void APartyCleric_CuresAPoisonedCompanion()
    {
        var cleric = Cleric(level: 10, mana: 1000);
        var companion = CompanionMember(CompanionId.Aldric, "Aldric");
        companion.ApplyStatus(StatusEffect.Poisoned, 5);
        var fight = PartyOf(cleric, companion);

        var (acted, shown) = PartyTurn(fight, cleric);

        acted.Should().BeTrue();
        companion.HasStatus(StatusEffect.Poisoned).Should().BeFalse();
        cleric.Mana.Should().Be(1000 - Cost(cleric, Purify));
        shown.Should().Contain(Loc.Get("combat.spell_purified", companion.DisplayName));
    }

    [Fact]
    public void APartyCleric_CuresAPoisonedNpcTeammate()
    {
        var cleric = Cleric(level: 10, mana: 1000);
        var npc = Member("Garrick");
        npc.ApplyStatus(StatusEffect.Poisoned, 5);
        var fight = PartyOf(cleric, npc);

        var (acted, _) = PartyTurn(fight, cleric);

        acted.Should().BeTrue();
        npc.HasStatus(StatusEffect.Poisoned).Should().BeFalse();
        cleric.Mana.Should().Be(1000 - Cost(cleric, Purify));
    }

    [Fact]
    public void APartyCleric_CuresAGroupedPlayer_OnThatPlayersOwnCharacter()
    {
        // A grouped follower fights as its own live Character (DungeonLocation sets RemoteTerminal on it),
        // so a cure on that object is the follower's own state.
        var cleric = Cleric(level: 10, mana: 1000);
        var follower = Member("Follower");
        follower.RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        follower.IsGroupedPlayer.Should().BeTrue();
        follower.ApplyStatus(StatusEffect.Poisoned, 5);
        var fight = PartyOf(cleric, follower);

        PartyTurn(fight, cleric).acted.Should().BeTrue();
        follower.HasStatus(StatusEffect.Poisoned).Should().BeFalse();
    }

    [Fact]
    public void APartyCleric_CastsOnePurifyATurn_LeaderFirst_ThenTheLowestHealth()
    {
        var cleric = Cleric(level: 10, mana: 1000);
        var healthy = Member("Healthy", hp: 900);
        var hurt = Member("Hurt", hp: 750);
        foreach (var c in new[] { healthy, hurt }) c.ApplyStatus(StatusEffect.Poisoned, 5);
        var fight = PartyOf(cleric, healthy, hurt);
        fight.Leader.ApplyStatus(StatusEffect.Poisoned, 5);
        int cost = Cost(cleric, Purify);

        PartyTurn(fight, cleric).acted.Should().BeTrue();
        fight.Leader.HasStatus(StatusEffect.Poisoned).Should().BeFalse("the leader is cured first");
        healthy.HasStatus(StatusEffect.Poisoned).Should().BeTrue("one cast a turn");
        hurt.HasStatus(StatusEffect.Poisoned).Should().BeTrue("one cast a turn");
        cleric.Mana.Should().Be(1000 - cost);

        PartyTurn(fight, cleric).acted.Should().BeTrue();
        hurt.HasStatus(StatusEffect.Poisoned).Should().BeFalse("then the member with the lowest health share");
        healthy.HasStatus(StatusEffect.Poisoned).Should().BeTrue();
        cleric.Mana.Should().Be(1000 - 2 * cost);
    }

    // ---- test 6: nobody afflicted ----

    [Fact]
    public void NobodyAfflictedAndNobodyInjured_NoPurify()
    {
        var cleric = Cleric(level: 10, mana: 1000);
        var npc = Member("Garrick");
        var fight = PartyOf(cleric, npc);

        var (acted, shown) = PartyTurn(fight, cleric);

        acted.Should().BeFalse();
        cleric.Mana.Should().Be(1000);
        shown.Should().NotContain(Info(Purify).DisplayName);
        shown.Should().NotContain(Loc.Get("combat.spell_already_healthy", fight.Leader.DisplayName));
    }

    [Fact]
    public void NobodyAfflicted_AMemberHurtButAboveTheThreshold_NoPurify()
    {
        var cleric = Cleric(level: 10, mana: 1000);
        var npc = Member("Garrick", hp: 900);
        var fight = PartyOf(cleric, npc);

        var (_, shown) = PartyTurn(fight, cleric);

        cleric.Mana.Should().Be(1000);
        shown.Should().NotContain(Info(Purify).DisplayName);
    }

    // ---- test 7: heal first, cure next turn ----

    [Fact]
    public void AnInjuredMemberAndAnAfflictedOne_TheHealThisTurn_TheCureNext()
    {
        var cleric = Cleric(level: 10, mana: 1000);
        var injured = Member("Injured", hp: 65, maxHp: 100);
        var fight = PartyOf(cleric, injured);
        fight.Leader.ApplyStatus(StatusEffect.Poisoned, 5);
        int cureLight = Cost(cleric, CureLight), purify = Cost(cleric, Purify);

        PartyTurn(fight, cleric).acted.Should().BeTrue();
        injured.HP.Should().BeGreaterThan(65, "the heal comes first");
        fight.Leader.HasStatus(StatusEffect.Poisoned).Should().BeTrue("the cure waits for the next turn");
        cleric.Mana.Should().Be(1000 - cureLight);
        ((double)injured.HP / injured.MaxHP).Should().BeGreaterOrEqualTo(0.70, "one Cure Light lifts this member over the threshold");

        PartyTurn(fight, cleric).acted.Should().BeTrue();
        fight.Leader.HasStatus(StatusEffect.Poisoned).Should().BeFalse();
        cleric.Mana.Should().Be(1000 - cureLight - purify);
    }

    // ---- test 8: the Inn switch ----

    [Fact]
    public void PurifySwitchedOffForAnNpcTeammate_NeverCast()
    {
        var cleric = Cleric(level: 10, mana: 1000);
        var fight = PartyOf(cleric);
        fight.Leader.TeammateDisabledSpells[cleric.GetSkillToggleKey()] = new List<string> { Info(Purify).Name };
        fight.Leader.ApplyStatus(StatusEffect.Poisoned, 5);

        var (acted, shown) = PartyTurn(fight, cleric);

        acted.Should().BeFalse();
        fight.Leader.HasStatus(StatusEffect.Poisoned).Should().BeTrue();
        cleric.Mana.Should().Be(1000);
        shown.Should().NotContain(Info(Purify).DisplayName);
    }

    [Fact]
    public void PurifySwitchedOffForACompanion_NeverCast()
    {
        var cleric = Cleric(level: 30, mana: 1000);
        cleric.IsCompanion = true;
        cleric.CompanionId = CompanionId.Mira;
        CompanionSystem.Instance.GetCompanion(CompanionId.Mira)!.DisabledSpells.Add(Info(Purify).Name);
        var fight = PartyOf(cleric);
        fight.Leader.ApplyStatus(StatusEffect.Diseased, 5);

        var (acted, shown) = PartyTurn(fight, cleric);

        acted.Should().BeFalse();
        fight.Leader.HasStatus(StatusEffect.Diseased).Should().BeTrue();
        cleric.Mana.Should().Be(1000);
        shown.Should().NotContain(Info(Purify).DisplayName);
    }

    [Fact]
    public void ACompanionClericWithPurifyOn_CuresTheLeader()
    {
        var cleric = Cleric(level: 30, mana: 1000);
        cleric.IsCompanion = true;
        cleric.CompanionId = CompanionId.Mira;
        var fight = PartyOf(cleric);
        fight.Leader.ApplyStatus(StatusEffect.Diseased, 5);

        PartyTurn(fight, cleric).acted.Should().BeTrue();
        fight.Leader.HasStatus(StatusEffect.Diseased).Should().BeFalse();
    }

    // ---- test 9b and 9c: the player's own Purify aimed at an ally ----

    private static void AfflictChosen(Character c)
    {
        c.ApplyStatus(StatusEffect.Diseased, 5);
        c.ApplyStatus(StatusEffect.Poisoned, 5);
        c.Poison = 4; c.PoisonTurns = 6;
    }

    private static bool AnyChosen(Character c) =>
        c.HasStatus(StatusEffect.Diseased) || c.HasStatus(StatusEffect.Poisoned) || c.Poison > 0 || c.PoisonTurns > 0;

    private static (CombatAction? action, string shown) HealMenu(CombatEngine engine, TerminalEmulator terminal, Character player)
    {
        terminal.StartCapture();
        var action = ((Task<CombatAction?>)typeof(CombatEngine).GetMethod("HandleHealAlly", F)!
            .Invoke(engine, new object[] { player, new List<Monster>() })!).GetAwaiter().GetResult();
        return (action, terminal.StopCapture() ?? "");
    }

    [Fact]
    public void ThePlayersPurifyOnAnAfflictedAlly_CuresTheAlly_AndNamesIt()
    {
        var (engine, terminal, player, ally, result) = PlayerParty();
        Afflict(player);
        AfflictChosen(ally);
        int cost = Cost(player, Purify);

        string shown = PlayerCasts(engine, terminal, player, result,
            new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = Purify, AllyTargetIndex = 0 });

        AnyChosen(ally).Should().BeFalse("the ally's poison and disease are gone");
        FullyAfflicted(player).Should().BeTrue("the caster is unchanged");
        player.Mana.Should().Be(1000 - cost);
        shown.Should().Contain(Loc.Get("combat.spell_purified", ally.DisplayName));
        shown.Should().NotContain(Loc.Get("combat.spell_cleansed", player.DisplayName), "the caster was not cleansed");
    }

    [Fact]
    public void ThePlayersPurifyFromTheHealMenu_CuresTheAlly_AndNamesIt()
    {
        var (engine, terminal, player, ally, _) = PlayerParty("1\n2\n1\n");
        Afflict(player);
        AfflictChosen(ally);
        int cost = Cost(player, Purify);

        var (action, shown) = HealMenu(engine, terminal, player);

        action!.Type.Should().Be(CombatActionType.HealAlly);
        AnyChosen(ally).Should().BeFalse();
        FullyAfflicted(player).Should().BeTrue("the caster is unchanged");
        player.Mana.Should().Be(1000 - cost);
        shown.Should().Contain(Loc.Get("combat.spell_purified", ally.DisplayName));
        shown.Should().NotContain(Loc.Get("combat.spell_cleansed", player.DisplayName), "the caster was not cleansed");
    }

    [Fact]
    public void TheHealMenu_LetsPurifyReachAnAfflictedAllyAtFullHealth()
    {
        var (engine, terminal, player, ally, _) = PlayerParty("1\n2\n1\n");
        ally.HP = ally.MaxHP;
        ally.ApplyStatus(StatusEffect.Poisoned, 5);

        var (action, shown) = HealMenu(engine, terminal, player);

        action.Should().NotBeNull();
        ally.HasStatus(StatusEffect.Poisoned).Should().BeFalse();
        shown.Should().NotContain(Loc.Get("combat.aid_full_health", ally.DisplayName));
    }

    [Fact]
    public void TheHealMenu_StillRefusesCureLightOnAnAfflictedAllyAtFullHealth()
    {
        var (engine, terminal, player, ally, _) = PlayerParty("1\n1\n1\n");
        ally.HP = ally.MaxHP;
        ally.ApplyStatus(StatusEffect.Poisoned, 5);

        var (action, shown) = HealMenu(engine, terminal, player);

        action.Should().BeNull("a heal on a full health ally is refused as before");
        player.Mana.Should().Be(1000);
        shown.Should().Contain(Loc.Get("combat.aid_full_health", ally.DisplayName));
    }

    [Fact]
    public void TheHealMenu_StillRefusesPurifyOnAHealthyAllyAtFullHealth()
    {
        var (engine, terminal, player, ally, _) = PlayerParty("1\n2\n1\n");
        ally.HP = ally.MaxHP;

        var (action, shown) = HealMenu(engine, terminal, player);

        action.Should().BeNull();
        player.Mana.Should().Be(1000);
        shown.Should().Contain(Loc.Get("combat.aid_full_health", ally.DisplayName));
    }

    [Fact]
    public void ThePlayersPurifyOnAnAllyWithNothingToCure_NamesTheAlly_AndSpendsTheMana()
    {
        // 9c: the same cost as a cast on oneself with nothing to cleanse
        var (engine, terminal, player, ally, result) = PlayerParty();
        int cost = Cost(player, Purify);

        string shown = PlayerCasts(engine, terminal, player, result,
            new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = Purify, AllyTargetIndex = 0 });

        shown.Should().Contain(Loc.Get("combat.spell_already_healthy", ally.DisplayName));
        shown.Should().NotContain(Loc.Get("combat.spell_already_healthy", player.DisplayName));
        player.Mana.Should().Be(1000 - cost);
        ally.HP.Should().Be(300);
    }

    [Fact]
    public void ThePlayersPurifyFromTheHealMenu_OnAnAllyWithNothingToCure_NamesTheAlly_AndSpendsTheMana()
    {
        var (engine, terminal, player, ally, _) = PlayerParty("1\n2\n1\n");
        int cost = Cost(player, Purify);

        var (action, shown) = HealMenu(engine, terminal, player);

        action!.Type.Should().Be(CombatActionType.HealAlly);
        shown.Should().Contain(Loc.Get("combat.spell_already_healthy", ally.DisplayName));
        player.Mana.Should().Be(1000 - cost);
    }

    // ---- test 10: a numbered order now cures its target ----

    [Fact]
    public void ANumberedOrderOfPurifyOnTheLeader_CuresTheLeader()
    {
        var follower = Follower(CureLight, Purify);
        var fight = Party(follower);
        fight.Leader.HP = fight.Leader.MaxHP;
        fight.Leader.ApplyStatus(StatusEffect.Poisoned, 5);
        int cost = Cost(follower, Purify);

        var action = Parse(fight, follower, "C2T2");
        action.SpellIndex.Should().Be(Purify);
        fight.Terminal.StartCapture();
        ((Task)typeof(CombatEngine).GetMethod("ExecuteSpellMultiMonster", F)!
            .Invoke(fight.Engine, new object[] { follower, new List<Monster>(), action, fight.Result })!).GetAwaiter().GetResult();
        string shown = fight.Terminal.StopCapture() ?? "";

        fight.Leader.HasStatus(StatusEffect.Poisoned).Should().BeFalse();
        follower.Mana.Should().Be(1000 - cost);
        shown.Should().Contain(Loc.Get("combat.spell_purified", fight.Leader.DisplayName));
    }

    // ---- test 11: rows fit 79 columns in en and hu ----

    private static IEnumerable<string> Rows(string captured) =>
        System.Text.RegularExpressions.Regex.Replace(captured, "\u001b\\[[0-9;]*m", "")
            .Replace("\r", "").Split('\n');

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void EveryCureRow_FitsSeventyNineColumns(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            const string longName = "Bartholomew Ironwhiskers";
            var shown = new List<string>();

            // a party Cleric cures a member with a long name
            var cleric = Cleric(level: 10, mana: 1000);
            cleric.Name1 = cleric.Name2 = longName;
            var member = Member(longName);
            member.ApplyStatus(StatusEffect.Poisoned, 5);
            var fight = PartyOf(cleric, member);
            shown.Add(PartyTurn(fight, cleric).shown);

            // the player's Purify on a cured ally and on an ally with nothing to cure, both paths
            foreach (bool afflicted in new[] { true, false })
            {
                var (engine, terminal, player, ally, result) = PlayerParty();
                player.Name1 = player.Name2 = longName;
                ally.Name1 = ally.Name2 = longName;
                if (afflicted) AfflictChosen(ally);
                shown.Add(PlayerCasts(engine, terminal, player, result,
                    new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = Purify, AllyTargetIndex = 0 }));

                var (e2, t2, p2, a2, _) = PlayerParty("1\n2\n1\n");
                p2.Name1 = p2.Name2 = longName;
                a2.Name1 = a2.Name2 = longName;
                if (afflicted) AfflictChosen(a2);
                shown.Add(HealMenu(e2, t2, p2).shown);
            }

            string all = string.Join("\n", shown);
            all.Should().Contain(longName);
            foreach (var row in Rows(all))
                row.Length.Should().BeLessOrEqualTo(79, $"[{lang}] row: {row}");
        }
        finally { GameConfig.Language = prev; }
    }

    // ---- test 12: the states not chosen stay on an ally ----

    [Fact]
    public void APartyCleric_LeavesTheOtherAfflictionsOnTheMember()
    {
        var cleric = Cleric(level: 10, mana: 1000);
        var fight = PartyOf(cleric);
        SetFlags(fight.Leader);
        fight.Leader.ApplyStatus(StatusEffect.Poisoned, 5);

        PartyTurn(fight, cleric).acted.Should().BeTrue();
        fight.Leader.HasStatus(StatusEffect.Poisoned).Should().BeFalse();
        AllFlags(fight.Leader).Should().BeTrue("blindness, plague, measles, smallpox, leprosy and LoversBane are not cured on someone else");
    }

    [Fact]
    public void APartyCleric_DoesNotCastForTheOtherAfflictionsAlone()
    {
        var cleric = Cleric(level: 10, mana: 1000);
        var fight = PartyOf(cleric);
        SetFlags(fight.Leader);

        var (acted, _) = PartyTurn(fight, cleric);

        acted.Should().BeFalse();
        cleric.Mana.Should().Be(1000);
        AllFlags(fight.Leader).Should().BeTrue();
    }

    [Fact]
    public void ThePlayersPurifyOnAnAlly_LeavesTheOtherAfflictions()
    {
        var (engine, terminal, player, ally, result) = PlayerParty();
        Afflict(ally);

        PlayerCasts(engine, terminal, player, result,
            new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = Purify, AllyTargetIndex = 0 });

        AnyChosen(ally).Should().BeFalse();
        AllFlags(ally).Should().BeTrue();
    }

    [Fact]
    public void ThePlayersPurifyFromTheHealMenu_LeavesTheOtherAfflictions()
    {
        var (engine, terminal, player, ally, _) = PlayerParty("1\n2\n1\n");
        Afflict(ally);

        HealMenu(engine, terminal, player);

        AnyChosen(ally).Should().BeFalse();
        AllFlags(ally).Should().BeTrue();
    }

    // ---- follow-up: the quickbar ally prompt (SelectHealTarget) ----

    /// <summary>The player casts from quickbar slot 1; returns the ally index the prompt gave (null = self).</summary>
    private static (int? allyTarget, string shown) Quickbar(CombatEngine engine, TerminalEmulator terminal, Character player, int slot)
    {
        player.Quickbar[0] = SpellSystem.GetQuickbarId(slot);
        terminal.StartCapture();
        var task = (Task)typeof(CombatEngine).GetMethod("HandleQuickbarAction", F)!
            .Invoke(engine, new object[] { player, "1", new List<Monster>() })!;
        task.GetAwaiter().GetResult();
        object? boxed = task.GetType().GetProperty("Result")!.GetValue(task);
        string shown = terminal.StopCapture() ?? "";
        boxed.Should().NotBeNull("the quickbar cast was not cancelled");
        var allyTarget = (int?)boxed!.GetType().GetField("Item5")!.GetValue(boxed);
        return (allyTarget, shown);
    }

    [Fact]
    public void TheQuickbarPurify_ListsAnAfflictedAllyAtFullHealth()
    {
        var (engine, terminal, player, ally, _) = PlayerParty("1\n");
        ally.HP = ally.MaxHP;
        ally.ApplyStatus(StatusEffect.Poisoned, 5);

        var (allyTarget, shown) = Quickbar(engine, terminal, player, Purify);

        allyTarget.Should().Be(0, "the poisoned ally is on the prompt and was picked");
        shown.Should().Contain(ally.DisplayName);
    }

    [Fact]
    public void TheQuickbarPurify_ListsTheInjuredAndTheAfflicted()
    {
        // "2" picks the second row; where there is none the prompt asks again and "0" is self
        var (engine, terminal, player, ally, _) = PlayerParty("2\n0\n");
        var poisoned = Member("Poisoned");
        poisoned.ApplyStatus(StatusEffect.Poisoned, 5);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { ally, poisoned });

        var (allyTarget, _) = Quickbar(engine, terminal, player, Purify);

        allyTarget.Should().Be(1, "the second row is the poisoned ally at full health");
    }

    [Fact]
    public void TheQuickbarCureLight_StillListsInjuredAlliesOnly()
    {
        var (engine, terminal, player, ally, _) = PlayerParty("2\n0\n");
        var poisoned = Member("Poisoned");
        poisoned.ApplyStatus(StatusEffect.Poisoned, 5);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { ally, poisoned });

        var (allyTarget, shown) = Quickbar(engine, terminal, player, CureLight);

        shown.Should().NotContain(poisoned.DisplayName, "a heal lists only injured allies");
        allyTarget.Should().BeNull("row 2 does not exist for Cure Light, so the second answer, 0, is self");
    }

    [Fact]
    public void TheQuickbarCureLight_NoInjuredAlly_NoPrompt()
    {
        var (engine, terminal, player, ally, _) = PlayerParty("1\n");
        ally.HP = ally.MaxHP;
        ally.ApplyStatus(StatusEffect.Poisoned, 5);

        var (allyTarget, shown) = Quickbar(engine, terminal, player, CureLight);

        allyTarget.Should().BeNull("with nobody injured a heal is cast on oneself");
        shown.Should().NotContain(Loc.Get("combat.select_heal_target"));
    }
}
