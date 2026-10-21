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
using UsurperRemake.UI;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G6 (CP4, CP10, CP11): a teammate's invulnerability and Death's Embrace hold on the companion hit path as they
/// do on the leader's; a cure cast on an ally cures it and Cycle Rewind and Blood Pact act on the caster only; a
/// teammate's Deluge of Sanctity and Unmaking cooldowns tick at round end as the leader's do.
/// </summary>
[Collection("SharedGameSingletons")]
public class TeammateGuards136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string Plain(string s) => Regex.Replace(s, "\u001b\\[[0-9;]*m", "");

    /// <summary>The text with its ANSI codes removed and every run of white space one space, so a wrapped row reads whole.</summary>
    private static string Flat(string s) => Regex.Replace(Plain(s), "\\s+", " ");

    private static T WithTactics<T>(Func<T> body)
    {
        bool saved = GameConfig.MonsterTacticsEnabled;
        try { GameConfig.MonsterTacticsEnabled = true; return body(); }
        finally { GameConfig.MonsterTacticsEnabled = saved; }
    }

    private static T InLanguage<T>(string lang, Func<T> body)
    {
        string saved = GameConfig.Language;
        try { GameConfig.Language = lang; return body(); }
        finally { GameConfig.Language = saved; }
    }

    // ---------------------------------------------------------------- the party

    private static Character Member(string name, CharacterClass cls, int level = 100, long hp = 5000) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Computer, Class = cls, Race = CharacterRace.Human, Level = level,
        HP = hp, MaxHP = hp, BaseMaxHP = hp, Strength = 40, BaseStrength = 40, Defence = 10, BaseDefence = 10,
        Dexterity = 20, BaseDexterity = 20, Agility = 10, BaseAgility = 10, Constitution = 40, BaseConstitution = 40,
        Intelligence = 60, BaseIntelligence = 60, Wisdom = 60, BaseWisdom = 60, Stamina = 200, BaseStamina = 200,
        Mana = 100_000, MaxMana = 100_000, Mental = 80, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>Every spell of the class learned, at Legendary, so no cast fumbles.</summary>
    private static T Caster<T>(T c) where T : Character
    {
        c.Spell = Enumerable.Range(1, 25).Select(_ => new List<bool> { true }).ToList();
        for (int slot = 1; slot <= 5; slot++)
            c.SkillProficiencies[TrainingSystem.GetSpellSkillId(c.Class, slot)] = TrainingSystem.ProficiencyLevel.Legendary;
        return c;
    }

    private static G.Rig Rig(Character hero, params Character[] team)
    {
        var rig = G.NewRig(hero, new Random(136), team.ToList(), Brute());
        rig.Result.Monsters = rig.Monsters;
        return rig;
    }

    /// <summary>A plain monster with no ability: its turn on a teammate is the normal attack.</summary>
    private static Monster Brute() => G.Foe("Wolf", 1_000_000, 600, level: 50);

    /// <summary>Level 400, so the ability chance is 30 + 80 and the monster uses its ability every turn.</summary>
    private static Monster Ability(string ability) => new Monster
    {
        Name = "Drake", TierName = "Drake", FamilyName = "Beast", Level = 400, HP = 1_000_000, MaxHP = 1_000_000,
        Strength = 600, WeapPow = 50, IsActive = true, CanSpeak = false, SpecialAbilities = new List<string> { ability },
    };

    private static void HitAlly(G.Rig rig, Monster m, Character ally) =>
        ((Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
            .Invoke(rig.Engine, new object?[] { m, ally, rig.Result, new List<Monster> { m } })!).GetAwaiter().GetResult();

    /// <summary>The ability used by the teammate itself, on its own turn (the path a grouped follower's typed action takes).</summary>
    private static void UseAbility(G.Rig rig, Character mate, string abilityId) =>
        G.Act(rig, G.A(CombatActionType.ClassAbility, abilityId), mate);

    public static IEnumerable<object[]> FiveShields() => new[]
    {
        new object[] { "divine_shield", CharacterClass.Paladin },
        new object[] { "avatar_of_light", CharacterClass.Paladin },
        new object[] { "divine_intervention", CharacterClass.Cleric },
        new object[] { "avatar_of_grace", CharacterClass.Cleric },
        new object[] { "eternal_vigil", CharacterClass.Tidesworn },
    };

    private static Character ShieldedMate(G.Rig rig, string abilityId, CharacterClass cls)
    {
        var mate = rig.Result.Teammates![0];
        mate.Class = cls;
        if (abilityId == "divine_shield")
            mate.EquippedItems[EquipmentSlot.OffHand] = EquipmentDatabase.RegisterDynamic(new Equipment
            {
                Name = "G6 shield", Slot = EquipmentSlot.OffHand, WeaponType = WeaponType.Shield, MinLevel = 1,
                Rarity = EquipmentRarity.Common, ArmorClass = 1,
            });
        UseAbility(rig, mate, abilityId);
        mate.HasStatus(StatusEffect.Invulnerable).Should().BeTrue($"{abilityId} used by a teammate makes it invulnerable");
        mate.HP = mate.MaxHP;
        return mate;
    }

    // ---------------------------------------------------------------- rule 1: invulnerability

    [Theory]
    [MemberData(nameof(FiveShields))]
    public void TeammateGuards136_AShieldOnATeammate_BlocksAMonstersNormalAttack(string abilityId, CharacterClass cls)
    {
        var rig = Rig(G.Hero(), Member("Ally One", cls));
        var mate = ShieldedMate(rig, abilityId, cls);
        var brute = Brute();
        HitAlly(rig, brute, mate);
        mate.HP.Should().Be(mate.MaxHP, $"{abilityId}: the normal attack does no damage to an invulnerable teammate");
        rig.Result.CombatLog.Should().Contain(l => l.Contains("attack on Ally One blocked by Invulnerable"));
        Flat(rig.Shown()).Should().Contain(Flat(Loc.Get("combat.ally_invulnerable_block", mate.DisplayName)));
    }

    [Theory]
    [MemberData(nameof(FiveShields))]
    public void TeammateGuards136_AShieldOnATeammate_BlocksAMonstersAbilityHit(string abilityId, CharacterClass cls)
    {
        WithTactics(() =>
        {
            foreach (var ability in new[] { "CrushingBlow", "FireBreath", "LifeDrain" })
            {
                var rig = Rig(G.Hero(), Member("Ally One", cls));
                var mate = ShieldedMate(rig, abilityId, cls);
                var drake = Ability(ability);
                drake.HP = drake.MaxHP / 2;
                HitAlly(rig, drake, mate);
                mate.HP.Should().Be(mate.MaxHP, $"{abilityId} against {ability}: the ability does no damage to an invulnerable teammate");
                drake.HP.Should().Be(drake.MaxHP / 2, $"{ability}: a blocked drain heals the monster nothing");
                rig.Result.CombatLog.Should().Contain(l => l.Contains($"{ability} on Ally One blocked by Invulnerable"));
            }
            return 0;
        });
    }

    [Fact]
    public void TeammateGuards136_ATeammateWithoutTheStatus_TakesBothHitsAsToday()
    {
        WithTactics(() =>
        {
            var rig = Rig(G.Hero(), Member("Ally One", CharacterClass.Paladin));
            var mate = rig.Result.Teammates![0];
            HitAlly(rig, Brute(), mate);
            mate.HP.Should().BeLessThan(mate.MaxHP, "the normal attack lands");
            foreach (var ability in new[] { "CrushingBlow", "FireBreath" })
            {
                mate.HP = mate.MaxHP;
                var drake = Ability(ability);
                HitAlly(rig, drake, mate);
                mate.HP.Should().BeLessThan(mate.MaxHP, $"{ability} lands");
                rig.Result.CombatLog.Should().Contain(l => l.Contains($"uses {ability} on Ally One for"));
            }
            rig.Result.CombatLog.Should().NotContain(l => l.Contains("blocked by Invulnerable"));
            return 0;
        });
    }

    [Fact]
    public void TeammateGuards136_AGroupedFollowersShield_BlocksTheHit()
    {
        var rig = Rig(G.Hero(), Member("Ally One", CharacterClass.Paladin));
        var mate = rig.Result.Teammates![0];
        mate.GroupPlayerUsername = "g6follower";
        mate.RemoteTerminal = new TerminalEmulator();
        mate.IsGroupedPlayer.Should().BeTrue();
        mate.ApplyStatus(StatusEffect.Invulnerable, 2);
        HitAlly(rig, Brute(), mate);
        mate.HP.Should().Be(mate.MaxHP, "a grouped follower's invulnerability blocks the hit too");
    }

    [Fact]
    public void TeammateGuards136_OnThePlayer_TheShieldBlocksAsToday()
    {
        WithTactics(() =>
        {
            var hero = G.Hero(50_000, level: 50);
            var rig = Rig(hero);
            hero.ApplyStatus(StatusEffect.Invulnerable, 2);
            var drake = Ability("CrushingBlow");
            bool used = ((Task<bool>)typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", F)!
                .Invoke(rig.Engine, new object?[] { drake, hero, rig.Result, new List<Monster> { drake } })!).GetAwaiter().GetResult();
            used.Should().BeTrue();
            hero.HP.Should().Be(hero.MaxHP, "the leader's ability path blocks the hit");

            var brute = Brute();
            ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
                .Invoke(rig.Engine, new object?[] { brute, hero, rig.Result, new List<Monster> { brute } })!).GetAwaiter().GetResult();
            hero.HP.Should().Be(hero.MaxHP, "the leader's normal attack path blocks the hit");
            rig.Result.CombatLog.Should().Contain(l => l.Contains("attack blocked by Invulnerable"));

            hero.RemoveStatus(StatusEffect.Invulnerable);
            ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
                .Invoke(rig.Engine, new object?[] { brute, hero, rig.Result, new List<Monster> { brute } })!).GetAwaiter().GetResult();
            hero.HP.Should().BeLessThan(hero.MaxHP, "without the status the leader is hit");
            return 0;
        });
    }

    // ---------------------------------------------------------------- rule 2: Death's Embrace

    [Fact]
    public void TeammateGuards136_DeathsEmbraceOnATeammate_RevivesOnceAtFifteenPercent_ThenTheNextKillingBlowDowns()
    {
        var rig = Rig(G.Hero(), Member("Ally One", CharacterClass.Voidreaver, hp: 1000));
        var mate = rig.Result.Teammates![0];
        UseAbility(rig, mate, "deaths_embrace");
        mate.DeathsEmbraceActive.Should().BeTrue("the teammate used Death's Embrace");
        mate.DodgeNextAttack = false;   // the ability's own dodge is spent, so the first hit lands

        mate.HP = 10;
        HitAlly(rig, Brute(), mate);
        mate.HP.Should().Be(150, "revived with 15 percent of max HP");
        mate.DeathsEmbraceActive.Should().BeFalse("once");
        mate.DodgeNextAttack.Should().BeTrue("it dodges the next attack");
        mate.IsDowned.Should().BeFalse("the death dispatch did not run");
        rig.Result.Teammates.Should().Contain(mate);
        Flat(rig.Shown()).Should().Contain(Flat(Loc.Get("combat.ally_deaths_embrace_revived", mate.DisplayName, 150)));

        HitAlly(rig, Brute(), mate);
        mate.HP.Should().Be(150, "the next attack is dodged");
        mate.DodgeNextAttack.Should().BeFalse();

        mate.HP = 10;
        HitAlly(rig, Brute(), mate);
        mate.HP.Should().Be(0, "the second killing blow is not revived");
        mate.IsDowned.Should().BeTrue("the ally is Downed as any ally at 0 HP");
    }

    [Fact]
    public void TeammateGuards136_DeathsEmbraceOnATeammate_RevivesFromAnAbilityHit()
    {
        WithTactics(() =>
        {
            foreach (var ability in new[] { "CrushingBlow", "FireBreath" })
            {
                var rig = Rig(G.Hero(), Member("Ally One", CharacterClass.Voidreaver, hp: 1000));
                var mate = rig.Result.Teammates![0];
                mate.DeathsEmbraceActive = true;
                mate.HP = 1;
                HitAlly(rig, Ability(ability), mate);
                mate.HP.Should().Be(150, $"{ability}: revived with 15 percent of max HP");
                mate.DodgeNextAttack.Should().BeTrue();
                mate.IsDowned.Should().BeFalse();
            }
            return 0;
        });
    }

    [Fact]
    public void TeammateGuards136_ATeammateWithoutDeathsEmbrace_IsDownedAsToday()
    {
        var rig = Rig(G.Hero(), Member("Ally One", CharacterClass.Voidreaver, hp: 1000));
        var mate = rig.Result.Teammates![0];
        mate.HP = 10;
        HitAlly(rig, Brute(), mate);
        mate.HP.Should().Be(0);
        mate.IsDowned.Should().BeTrue();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void TeammateGuards136_TheNewLines_FitSeventyNineColumns(string lang) => InLanguage(lang, () =>
    {
        const string longName = "Bartholomew the Unyielding";
        // the leader's screen: the rows WriteRow writes (MessageRows), and the group lines
        var rows = UIHelper.MessageRows(Loc.Get("combat.ally_invulnerable_block", longName))
            .Concat(UIHelper.MessageRows(Loc.Get("combat.ally_deaths_embrace_revived", longName, 15000)))
            .Concat(new[]
            {
                CombatEngine.GroupLineWrapped(lang, "", "combat.ally_invulnerable_block", longName),
                CombatEngine.GroupLineWrapped(lang, "", "combat.ally_deaths_embrace_revived", longName, 15000),
                CombatEngine.GroupLineWrapped(lang, "", "combat.invulnerable_block"),
                CombatEngine.GroupLineWrapped(lang, "", "combat.divine_shield_ability"),
                CombatEngine.GroupLineWrapped(lang, "", "combat.deaths_embrace_trigger"),
                CombatEngine.GroupLineWrapped(lang, "", "combat.deaths_embrace_revived", 15000),
            }.SelectMany(g => g.Split('\n')))
            .Select(Plain).ToList();
        rows.Should().Contain(r => r.Contains("Bartholomew"));
        foreach (var row in rows)
            row.Length.Should().BeLessThanOrEqualTo(79, $"{lang}: \"{row}\"");

        // and in a hit: the rows really shown carry the long name whole
        var rig = Rig(G.Hero(), Member(longName, CharacterClass.Voidreaver, hp: 100_000));
        var mate = rig.Result.Teammates![0];
        mate.ApplyStatus(StatusEffect.Invulnerable, 2);
        HitAlly(rig, Brute(), mate);
        Flat(rig.Shown()).Should().Contain(Flat(Loc.Get("combat.ally_invulnerable_block", longName)));
        return 0;
    });

    // ---------------------------------------------------------------- rule 3: casts on an ally

    private static void Afflict(Character c)
    {
        c.ApplyStatus(StatusEffect.Poisoned, 5);
        c.ApplyStatus(StatusEffect.Diseased, 5);
        c.Poison = 3;
        c.PoisonTurns = 4;
    }

    private static bool Afflicted(Character c) =>
        c.HasStatus(StatusEffect.Poisoned) || c.HasStatus(StatusEffect.Diseased) || c.Poison > 0 || c.PoisonTurns > 0;

    private static void Cast(G.Rig rig, Character caster, CombatAction action) =>
        ((Task)typeof(CombatEngine).GetMethod("ExecuteSpellMultiMonster", F)!
            .Invoke(rig.Engine, new object[] { caster, rig.Monsters, action, rig.Result })!).GetAwaiter().GetResult();

    private static CombatAction SpellAt(int slot, int? ally = null, Character? over = null) =>
        new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = slot, AllyTargetIndex = ally, HealTargetOverride = over };

    [Fact]
    public void TeammateGuards136_PurifyingSurgeOnAnAlly_HealsAndCuresIt()
    {
        var caster = Caster(Member("Tide", CharacterClass.Tidesworn));
        var ally = Member("Ally One", CharacterClass.Warrior, hp: 1000);
        var rig = Rig(caster, ally);
        ally.HP = 300;
        Afflict(ally);
        Cast(rig, caster, SpellAt(2, ally: 0));
        ally.HP.Should().BeGreaterThan(300, "the surge heals the ally");
        Afflicted(ally).Should().BeFalse("the surge removes the ally's poison and disease");
        Flat(rig.Shown()).Should().Contain(Flat(Loc.Get("combat.spell_purified", ally.DisplayName)));
    }

    [Fact]
    public void TeammateGuards136_PurifyingSurgeFromTheAidMenu_HealsAndCuresTheAlly()
    {
        var caster = Caster(Member("Tide", CharacterClass.Tidesworn));
        caster.Healing = 0;
        caster.ManaPotions = 0;
        var ally = Member("Ally One", CharacterClass.Warrior, hp: 1000);
        var terminal = new TerminalEmulator(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("1\n1\n1\n")), new MemoryStream());
        var engine = new CombatEngine(terminal);
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(136));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, caster);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { ally });
        ally.HP = 300;
        Afflict(ally);
        var action = ((Task<CombatAction?>)typeof(CombatEngine).GetMethod("HandleHealAlly", F)!
            .Invoke(engine, new object[] { caster, new List<Monster>() })!).GetAwaiter().GetResult();
        action.Should().NotBeNull();
        ally.HP.Should().BeGreaterThan(300);
        Afflicted(ally).Should().BeFalse("the aid menu's surge cures the ally too");
    }

    [Fact]
    public void TeammateGuards136_ATideswornTeammatesSurgeOnAnAlly_HealsAndCuresIt()
    {
        var tide = Caster(Member("Tide", CharacterClass.Tidesworn));
        var hero = G.Hero(1000);
        var rig = Rig(hero, tide);
        hero.HP = 300;
        Afflict(hero);
        bool acted = ((Task<bool>)typeof(CombatEngine).GetMethod("TryTeammateHealAction", F)!
            .Invoke(rig.Engine, new object[] { tide, new List<Character> { hero, tide }, rig.Result })!).GetAwaiter().GetResult();
        acted.Should().BeTrue();
        hero.HP.Should().BeGreaterThan(300);
        Afflicted(hero).Should().BeFalse("the teammate's surge cures its target");
    }

    [Fact]
    public void TeammateGuards136_IsCasterOnly_IsCycleRewindAndBloodPactAlone()
    {
        foreach (CharacterClass cls in Enum.GetValues(typeof(CharacterClass)))
            foreach (var s in SpellSystem.GetAllSpellsForClass(cls) ?? new List<SpellSystem.SpellInfo>())
            {
                bool expected = (cls == CharacterClass.Cyclebreaker && s.Name == "Cycle Rewind")
                                || (cls == CharacterClass.Voidreaver && s.Name == "Blood Pact");
                SpellSystem.IsCasterOnly(cls, s).Should().Be(expected, $"{cls} {s.Name}");
            }
    }

    [Theory]
    [InlineData(CharacterClass.Cyclebreaker, 4)]
    [InlineData(CharacterClass.Voidreaver, 2)]
    public void TeammateGuards136_CasterOnlySpell_AimedAtAnAlly_LandsOnTheCaster(CharacterClass cls, int slot)
    {
        foreach (bool viaOverride in new[] { false, true })
        {
            var caster = Caster(Member("Self", cls, hp: 1000));
            var ally = Member("Ally One", CharacterClass.Warrior, hp: 1000);
            var rig = Rig(caster, ally);
            caster.HP = 500;
            ally.HP = 500;
            Afflict(ally);
            Cast(rig, caster, viaOverride ? SpellAt(slot, over: ally) : SpellAt(slot, ally: 0));
            ally.HP.Should().Be(500, $"{cls} slot {slot}: the ally is not healed or bled");
            ally.TempAttackBonus.Should().Be(0, "the ally takes no attack bonus");
            Afflicted(ally).Should().BeTrue("the ally is not cured");
            if (cls == CharacterClass.Cyclebreaker)
                caster.HP.Should().Be(500 + 300, "Cycle Rewind heals the caster 30 percent of its max HP");
            else
            {
                caster.HP.Should().Be(500 - 200, "Blood Pact costs the caster 20 percent of its max HP");
                caster.TempAttackBonus.Should().BeGreaterThan(0, "the caster takes the attack bonus");
                caster.HasStatus(StatusEffect.Hidden).Should().BeTrue("and the guaranteed critical");
            }
        }
    }

    [Fact]
    public void TeammateGuards136_CycleRewindOnTheCaster_HealsAndCuresAsToday()
    {
        var caster = Caster(Member("Self", CharacterClass.Cyclebreaker, hp: 1000));
        var rig = Rig(caster);
        caster.HP = 100;
        Afflict(caster);
        Cast(rig, caster, SpellAt(4));
        caster.HP.Should().Be(400);
        Afflicted(caster).Should().BeFalse();
    }

    private static (int? allyTarget, bool cancelled) Quickbar(Character player, Character ally, int slot, string input)
    {
        var terminal = new TerminalEmulator(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(input)), new MemoryStream());
        var engine = new CombatEngine(terminal);
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(136));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { ally });
        player.Quickbar[0] = SpellSystem.GetQuickbarId(slot);
        var task = (Task)typeof(CombatEngine).GetMethod("HandleQuickbarAction", F)!
            .Invoke(engine, new object[] { player, "1", new List<Monster> { Brute() } })!;
        task.GetAwaiter().GetResult();
        object? boxed = task.GetType().GetProperty("Result")!.GetValue(task);
        if (boxed == null) return (null, true);
        return ((int?)boxed.GetType().GetField("Item5")!.GetValue(boxed), false);
    }

    [Theory]
    [InlineData(CharacterClass.Cyclebreaker, 4)]
    [InlineData(CharacterClass.Voidreaver, 2)]
    public void TeammateGuards136_TheQuickbar_DoesNotPromptAnAllyForACasterOnlySpell(CharacterClass cls, int slot)
    {
        var caster = Caster(Member("Self", cls, hp: 1000));
        caster.HP = 500;
        var ally = Member("Ally One", CharacterClass.Warrior, hp: 1000);
        ally.HP = 100;
        var (allyTarget, cancelled) = Quickbar(caster, ally, slot, "1\n");
        cancelled.Should().BeFalse();
        allyTarget.Should().BeNull("no prompt: the spell is cast on the caster");
    }

    [Fact]
    public void TeammateGuards136_TheQuickbar_StillPromptsForPurifyingSurge()
    {
        var caster = Caster(Member("Tide", CharacterClass.Tidesworn));
        var ally = Member("Ally One", CharacterClass.Warrior, hp: 1000);
        ally.HP = 100;
        var (allyTarget, _) = Quickbar(caster, ally, 2, "1\n");
        allyTarget.Should().Be(0, "a heal that is not caster only still prompts and the ally was picked");
    }

    private static CombatAction Typed(G.Rig rig, Character follower, string typed) =>
        (CombatAction)typeof(CombatEngine).GetMethod("ParseGroupCombatInput", F)!
            .Invoke(rig.Engine, new object[] { typed, follower, rig.Hero, rig.Monsters, rig.Result })!;

    [Theory]
    [InlineData(CharacterClass.Cyclebreaker, 4)]
    [InlineData(CharacterClass.Voidreaver, 2)]
    public void TeammateGuards136_TheTypedPath_CastsACasterOnlySpellOnTheCaster(CharacterClass cls, int slot)
    {
        var follower = Caster(Member("Self", cls, hp: 1000));
        var hero = G.Hero(1000);
        var rig = Rig(hero, follower);
        hero.HP = 100;
        var spells = SpellSystem.GetAvailableSpells(follower)
            .Where(s => follower.Mana >= SpellSystem.CalculateManaCost(s, follower)).ToList();
        int n = spells.FindIndex(s => s.Level == slot) + 1;
        n.Should().BeGreaterThan(0);
        var action = Typed(rig, follower, $"C{n}T2");
        action.Type.Should().Be(CombatActionType.CastSpell);
        action.SpellIndex.Should().Be(slot);
        action.HealTargetOverride.Should().BeSameAs(follower, "T2 names the leader, but the spell acts on the caster alone");
    }

    [Fact]
    public void TeammateGuards136_ACyclebreakerFollowersHealCommands_DoNotAimCycleRewindAtTheLeader()
    {
        var follower = Caster(Member("Self", CharacterClass.Cyclebreaker, hp: 1000));
        follower.Healing = 0;
        follower.ManaPotions = 0;
        var hero = G.Hero(1000);
        var rig = Rig(hero, follower);
        hero.HP = 100;
        foreach (var typed in new[] { "H", "C" })
        {
            var action = Typed(rig, follower, typed);
            bool rewindOnLeader = action.Type == CombatActionType.CastSpell && action.SpellIndex == 4
                                  && action.HealTargetOverride == hero;
            rewindOnLeader.Should().BeFalse($"{typed}: Cycle Rewind is never aimed at the leader");
        }
    }

    [Fact]
    public void TeammateGuards136_ACyclebreakerTeammate_NeverRewindsAnAlly_ButRewindsItself()
    {
        var breaker = Caster(Member("Breaker", CharacterClass.Cyclebreaker, hp: 1000));
        breaker.Healing = 0;
        breaker.ManaPotions = 0;
        var hero = G.Hero(1000);
        var rig = Rig(hero, breaker);
        hero.HP = 100;
        bool acted = ((Task<bool>)typeof(CombatEngine).GetMethod("TryTeammateHealAction", F)!
            .Invoke(rig.Engine, new object[] { breaker, new List<Character> { hero, breaker }, rig.Result })!).GetAwaiter().GetResult();
        acted.Should().BeFalse("its only heal acts on the caster alone, so it has no heal for the leader");
        hero.HP.Should().Be(100);

        hero.HP = hero.MaxHP;
        breaker.HP = 100;
        Afflict(breaker);
        acted = ((Task<bool>)typeof(CombatEngine).GetMethod("TryTeammateHealAction", F)!
            .Invoke(rig.Engine, new object[] { breaker, new List<Character> { hero, breaker }, rig.Result })!).GetAwaiter().GetResult();
        acted.Should().BeTrue("hurt itself, it casts Cycle Rewind on itself");
        breaker.HP.Should().Be(400, "30 percent of its own max HP");
        Afflicted(breaker).Should().BeFalse("and cures itself, as the text says");
    }

    // ---------------------------------------------------------------- rule 4: cooldowns

    private static void RoundEnd(G.Rig rig, bool leaderAlive)
    {
        if (leaderAlive)
            typeof(CombatEngine).GetMethod("ProcessEndOfRoundAbilityEffects", F)!.Invoke(rig.Engine, new object[] { rig.Hero });
        else
            typeof(CombatEngine).GetMethod("RegeneratePartyAtRoundEnd", F)!.Invoke(rig.Engine, new object[] { rig.Hero });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TeammateGuards136_AFollowersDelugeAndUnmaking_ComeOffCooldownWithTheLeaders(bool leaderAlive)
    {
        var hero = Caster(Member("Leader", CharacterClass.Tidesworn));
        hero.AI = CharacterAI.Human;
        var tide = Caster(Member("Tide", CharacterClass.Tidesworn));
        tide.GroupPlayerUsername = "g6tide";
        tide.RemoteTerminal = new TerminalEmulator();
        var reaver = Caster(Member("Reaver", CharacterClass.Voidreaver));
        var rig = Rig(hero, tide, reaver);

        // the casts set the cooldowns on the caster, the leader's and the followers' alike
        foreach (var c in new[] { hero, tide })
        {
            SpellSystem.CastSpell(c, 5, null);
            c.DelugeCooldown.Should().Be(3);
        }
        reaver.UnmakingCooldown = 2;   // what the Unmaking cast sets
        hero.UnmakingCooldown = 2;

        if (!leaderAlive) hero.HP = 0;   // the followers fight on after the leader falls
        int rounds = 0;
        while (tide.DelugeCooldown > 0 && rounds < 10)
        {
            RoundEnd(rig, leaderAlive);
            rounds++;
            tide.DelugeCooldown.Should().Be(3 - rounds, $"round {rounds}: the follower's Deluge ticks once a round");
            reaver.UnmakingCooldown.Should().Be(Math.Max(0, 2 - rounds), $"round {rounds}: the follower's Unmaking ticks once a round");
            if (leaderAlive)
            {
                tide.DelugeCooldown.Should().Be(hero.DelugeCooldown, $"round {rounds}: the follower's Deluge ticks with the leader's");
                reaver.UnmakingCooldown.Should().Be(hero.UnmakingCooldown, $"round {rounds}: the follower's Unmaking ticks with the leader's");
            }
        }
        rounds.Should().Be(3, "Deluge comes off cooldown after 3 round ends, the leader's and the follower's");
        reaver.UnmakingCooldown.Should().Be(0);
    }
}
