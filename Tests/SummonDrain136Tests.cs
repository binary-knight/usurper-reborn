using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G9: Summon Angel and Summon Demon are buffs on the caster, with no target asked and their attack bonus
/// landing once for the fight (CP5); Steal Life and Energy Drain heal half of the HP the target lost (Q7); Grand
/// Finale's damage counts in the fight's damage total and the player's recorded damage (B F7).
/// </summary>
[Collection("SharedGameSingletons")]
public class SummonDrain136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    // ---------------------------------------------------------------- the rig

    private static Character Member(string name, CharacterClass cls, int level = 100) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Computer, Class = cls, Race = CharacterRace.Human, Level = level,
        HP = 1000, MaxHP = 10_000_000, BaseMaxHP = 10_000_000, Strength = 40, BaseStrength = 40, Defence = 10, BaseDefence = 10,
        Dexterity = 20, BaseDexterity = 20, Agility = 10, BaseAgility = 10, Constitution = 40, BaseConstitution = 40,
        Intelligence = 60, BaseIntelligence = 60, Wisdom = 60, BaseWisdom = 60, Stamina = 200, BaseStamina = 200,
        Mana = 100_000, MaxMana = 100_000, Mental = 80, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>Every spell of the class learned at Legendary, so no cast fumbles, and the staff a Magician or Sage needs.</summary>
    private static Character Caster(Character c)
    {
        c.Spell = Enumerable.Range(1, 25).Select(_ => new List<bool> { true }).ToList();
        for (int slot = 1; slot <= 25; slot++)
            c.SkillProficiencies[TrainingSystem.GetSpellSkillId(c.Class, slot)] = TrainingSystem.ProficiencyLevel.Legendary;
        if (!SpellSystem.HasRequiredSpellWeapon(c))
            foreach (var w in EquipmentDatabase.GetAll().Where(e => e.Slot == EquipmentSlot.MainHand))
            {
                c.EquippedItems[EquipmentSlot.MainHand] = w.Id;
                if (SpellSystem.HasRequiredSpellWeapon(c)) break;
            }
        SpellSystem.HasRequiredSpellWeapon(c).Should().BeTrue("the caster holds the weapon its spells need");
        return c;
    }

    private static Monster Wolf(long hp = 10_000_000, long armPow = 0, bool boss = false) => new Monster
    {
        Name = "Wolf", TierName = "Wolf", Level = 50, HP = hp, MaxHP = 10_000_000, Strength = 10, Defence = 0, ArmPow = armPow,
        Experience = 1, Gold = 0, IsActive = true, CanSpeak = false, IsBoss = boss,
    };

    private static G.Rig Rig(Character hero, List<Character> team, params Monster[] monsters)
    {
        var rig = G.NewRig(hero, new Random(136), team, monsters);
        rig.Result.Monsters = rig.Monsters;
        return rig;
    }

    private static CombatAction Spell(int slot, int? target = null, int? ally = null, Character? over = null) => new CombatAction
    {
        Type = CombatActionType.CastSpell, SpellIndex = slot, TargetIndex = target, AllyTargetIndex = ally, HealTargetOverride = over,
    };

    private static CombatAction Typed(G.Rig rig, Character follower, string typed) =>
        (CombatAction)typeof(CombatEngine).GetMethod("ParseGroupCombatInput", F)!
            .Invoke(rig.Engine, new object[] { typed, follower, rig.Hero, rig.Monsters, rig.Result })!;

    // ---------------------------------------------------------------- rule 1: the Summon route

    public static IEnumerable<object[]> Summons() => new[]
    {
        new object[] { CharacterClass.Cleric, 16, "spell_angel", 55 },
        new object[] { CharacterClass.Magician, 23, "spell_demon", 100 },
    };

    /// <summary>The bonus a Legendary cast gives: the base plus level, times 2, or 2.5 on a great roll, or 3 on a critical.</summary>
    private static int[] Bonuses(int baseAttack, int level) =>
        new[] { 2.0f, 2.0f * 1.25f, 2.0f * 1.5f }.Select(m => (int)((baseAttack + level) * m)).ToArray();

    private static int Count(Character c, string source) => c.AttackBonusEntries.Count(e => e.Source == source);

    /// <summary>The caster holds one whole fight attack entry of the spell's source, of the spell's bonus, and shows it.</summary>
    private static void HoldsTheBonusOnce(Character caster, string source, int baseAttack)
    {
        Count(caster, source).Should().Be(1, "the summon's attack bonus lands once");
        var entry = caster.AttackBonusEntries.Single(e => e.Source == source);
        entry.Value.Should().BeOneOf(Bonuses(baseAttack, caster.Level), "the spell's AttackBonus");
        entry.Rounds.Should().Be(999, "for the whole fight");
        caster.TempAttackBonus.Should().Be(entry.Value, "the attack slot shows it");
    }

    [Theory]
    [MemberData(nameof(Summons))]
    public void SummonDrain136_ASummonCastByThePlayer_InAMultiMonsterFight_RaisesTheCastersAttackOnce(CharacterClass cls, int slot, string source, int baseAttack)
    {
        var caster = Caster(Member("Caster", cls));
        var ally = Member("Ally One", CharacterClass.Warrior);
        var rig = Rig(caster, new List<Character> { ally }, Wolf(), Wolf());
        G.Act(rig, Spell(slot, target: 1));
        HoldsTheBonusOnce(caster, source, baseAttack);
        rig.Monsters.Should().OnlyContain(m => m.HP == 10_000_000, "a summon hits no monster");
        ally.AttackBonusEntries.Should().BeEmpty("the summon is on the caster alone");
    }

    [Fact]
    public void SummonDrain136_SummonAngel_GivesTheCasterItsDodge()
    {
        var caster = Caster(Member("Caster", CharacterClass.Cleric));
        var rig = Rig(caster, new List<Character>(), Wolf(), Wolf());
        caster.DodgeNextAttack = false;
        G.Act(rig, Spell(16, target: 0));
        caster.DodgeNextAttack.Should().BeTrue("Summon Angel: dodge next attack");
    }

    [Theory]
    [MemberData(nameof(Summons))]
    public void SummonDrain136_ASummonAimedAtAnAlly_LandsOnTheCaster(CharacterClass cls, int slot, string source, int baseAttack)
    {
        foreach (bool viaOverride in new[] { false, true })
        {
            var caster = Caster(Member("Caster", cls));
            var ally = Member("Ally One", CharacterClass.Warrior);
            var rig = Rig(caster, new List<Character> { ally }, Wolf(), Wolf());
            G.Act(rig, viaOverride ? Spell(slot, over: ally) : Spell(slot, ally: 0));
            HoldsTheBonusOnce(caster, source, baseAttack);
            ally.AttackBonusEntries.Should().BeEmpty($"override {viaOverride}: the ally takes nothing");
        }
    }

    [Theory]
    [MemberData(nameof(Summons))]
    public void SummonDrain136_ASummonCastTwice_HoldsOneEntry(CharacterClass cls, int slot, string source, int baseAttack)
    {
        var caster = Caster(Member("Caster", cls));
        var rig = Rig(caster, new List<Character>(), Wolf(), Wolf());
        G.Act(rig, Spell(slot, target: 0));
        G.Act(rig, Spell(slot, target: 1));
        HoldsTheBonusOnce(caster, source, baseAttack);
    }

    /// <summary>The quickbar action for slot key "1", with "Q" waiting as input: a target prompt would read it and cancel.</summary>
    private static object? Quickbar(Character player, Character ally, int slot)
    {
        var terminal = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("Q\nQ\n")), new MemoryStream());
        var engine = new CombatEngine(terminal);
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(136));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { ally });
        player.Quickbar[0] = SpellSystem.GetQuickbarId(slot);
        var task = (Task)typeof(CombatEngine).GetMethod("HandleQuickbarAction", F)!
            .Invoke(engine, new object[] { player, "1", new List<Monster> { Wolf(), Wolf() } })!;
        task.GetAwaiter().GetResult();
        return task.GetType().GetProperty("Result")!.GetValue(task);
    }

    [Theory]
    [MemberData(nameof(Summons))]
    public void SummonDrain136_TheQuickbar_AsksNoTargetForASummon(CharacterClass cls, int slot, string source, int baseAttack)
    {
        _ = source; _ = baseAttack;
        var caster = Caster(Member("Caster", cls));
        var ally = Member("Ally One", CharacterClass.Warrior);
        var boxed = Quickbar(caster, ally, slot);
        boxed.Should().NotBeNull("no prompt read the waiting Q, so nothing was cancelled");
        var t = boxed!.GetType();
        t.GetField("Item1")!.GetValue(boxed).Should().Be(CombatActionType.CastSpell);
        ((int?)t.GetField("Item2")!.GetValue(boxed)).Should().BeNull("no monster target");
        ((int)t.GetField("Item4")!.GetValue(boxed)!).Should().Be(slot);
        ((int?)t.GetField("Item5")!.GetValue(boxed)).Should().BeNull("no ally target");
        ((bool)t.GetField("Item6")!.GetValue(boxed)!).Should().BeFalse("not an area cast");
    }

    [Fact]
    public void SummonDrain136_TheQuickbar_StillAsksATargetForAnAttackSpell()
    {
        var caster = Caster(Member("Caster", CharacterClass.Cleric));
        var ally = Member("Ally One", CharacterClass.Warrior);
        Quickbar(caster, ally, 17).Should().BeNull("Divine Lightning asks for a monster, and the waiting Q cancels it");
    }

    [Theory]
    [MemberData(nameof(Summons))]
    public void SummonDrain136_AFollowersTypedCast_IsOnTheFollower_Once(CharacterClass cls, int slot, string source, int baseAttack)
    {
        foreach (var suffix in new[] { "", "T1", "T2" })
        {
            var hero = G.Hero(1000);
            var follower = Caster(Member("Follower", cls));
            var rig = Rig(hero, new List<Character> { follower }, Wolf(), Wolf());
            var spells = SpellSystem.GetAvailableSpells(follower)
                .Where(s => follower.Mana >= SpellSystem.CalculateManaCost(s, follower)).ToList();
            int n = spells.FindIndex(s => s.Level == slot) + 1;
            n.Should().BeGreaterThan(0);
            var action = Typed(rig, follower, $"C{n}{suffix}");
            action.Type.Should().Be(CombatActionType.CastSpell);
            action.SpellIndex.Should().Be(slot);
            action.TargetIndex.Should().BeNull($"C{n}{suffix}: no monster target");
            action.HealTargetOverride.Should().BeNull($"C{n}{suffix}: no ally target");
            G.Act(rig, action, follower);
            HoldsTheBonusOnce(follower, source, baseAttack);
            hero.AttackBonusEntries.Should().BeEmpty("the leader takes nothing");
            rig.Monsters.Should().OnlyContain(m => m.HP == 10_000_000);
        }
    }

    [Theory]
    [MemberData(nameof(Summons))]
    public void SummonDrain136_AFollowersQuickbarCast_IsOnTheFollower_Once(CharacterClass cls, int slot, string source, int baseAttack)
    {
        var hero = G.Hero(1000);
        var follower = Caster(Member("Follower", cls));
        follower.Quickbar[0] = SpellSystem.GetQuickbarId(slot);
        var rig = Rig(hero, new List<Character> { follower }, Wolf(), Wolf());
        var action = Typed(rig, follower, "1");
        action.Type.Should().Be(CombatActionType.CastSpell);
        action.SpellIndex.Should().Be(slot);
        action.TargetIndex.Should().BeNull("no monster target");
        G.Act(rig, action, follower);
        HoldsTheBonusOnce(follower, source, baseAttack);
        hero.AttackBonusEntries.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Summons))]
    public void SummonDrain136_AnNpcTeammate_NeverPicksASummon(CharacterClass cls, int slot, string source, int baseAttack)
    {
        _ = slot; _ = source; _ = baseAttack;
        var mate = Caster(Member("Mate", cls));
        var rig = Rig(G.Hero(1000), new List<Character> { mate }, Wolf(), Wolf());
        foreach (bool aoe in new[] { false, true })
        {
            var pick = (SpellSystem.SpellInfo?)typeof(CombatEngine).GetMethod("GetBestOffensiveSpell", F)!
                .Invoke(rig.Engine, new object?[] { mate, aoe, rig.Monsters });
            CombatEngine.IsSummonSpell(pick).Should().BeFalse("the teammate's attack pick takes Attack spells only");
        }
        var heal = (SpellSystem.SpellInfo?)typeof(CombatEngine).GetMethod("GetBestHealSpell", F)!
            .Invoke(rig.Engine, new object?[] { mate, null });
        CombatEngine.IsSummonSpell(heal).Should().BeFalse("the teammate's heal pick takes heals only");
    }

    [Theory]
    [MemberData(nameof(Summons))]
    public void SummonDrain136_APvPSummon_LandsOnTheCasterOnce(CharacterClass cls, int slot, string source, int baseAttack)
    {
        _ = source; _ = baseAttack;
        var attacker = Caster(Member("Caster", cls));
        var defender = Member("Rival", CharacterClass.Warrior);
        defender.HP = 5000;
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(136));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, attacker);
        attacker.DodgeNextAttack = false;
        var result = new CombatResult { Player = attacker, CombatLog = new List<string>() };
        ((Task)typeof(CombatEngine).GetMethod("ExecutePvPSpell", F)!
            .Invoke(engine, new object[] { attacker, defender, Spell(slot), result })!).GetAwaiter().GetResult();
        attacker.HasStatus(StatusEffect.PowerStance).Should().BeTrue("PvP gives a spell's attack bonus as Power Stance, as for a buff");
        attacker.DodgeNextAttack.Should().BeTrue("the summon's dodge is on the caster");
        defender.HP.Should().Be(5000, "the summon does not hit the rival");
        defender.HasStatus(StatusEffect.PowerStance).Should().BeFalse();
        result.CombatLog.Count(l => l.Contains("casts")).Should().Be(1, "one cast");
    }

    [Fact]
    public void SummonDrain136_IsSummonSpell_IsSummonAngelAndSummonDemonAlone()
    {
        foreach (CharacterClass cls in Enum.GetValues(typeof(CharacterClass)))
            foreach (var s in SpellSystem.GetAllSpellsForClass(cls) ?? new List<SpellSystem.SpellInfo>())
            {
                bool expected = (cls == CharacterClass.Cleric && s.Name == "Summon Angel")
                                || (cls == CharacterClass.Magician && s.Name == "Summon Demon");
                CombatEngine.IsSummonSpell(s).Should().Be(expected, $"{cls} {s.Name}");
            }
    }

    // ---------------------------------------------------------------- rule 2: the drain heal

    /// <summary>One drain cast by the player through the live spell path; the HP the target lost and the HP the caster gained.</summary>
    private static (long lost, long healed) Drain(int slot, Monster target, BossCombatContext? ctx = null)
    {
        var caster = Caster(Member("Sage", CharacterClass.Sage));
        var rig = Rig(caster, new List<Character>(), target);
        rig.Engine.BossContext = ctx;
        long before = target.HP, hp = caster.HP;
        G.Act(rig, Spell(slot, target: 0));
        return (before - target.HP, caster.HP - hp);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(15)]
    public void SummonDrain136_ADrainOnAnArmouredTarget_HealsHalfTheHpItLost(int slot)
    {
        var (lost, healed) = Drain(slot, Wolf(armPow: 200));
        lost.Should().BeGreaterThan(0);
        healed.Should().Be(lost / 2, "half of the damage dealt after the armour");
    }

    [Theory]
    [InlineData(11)]
    [InlineData(15)]
    public void SummonDrain136_ADrainOnABossBehindDivineArmour_HealsHalfTheHpItLost(int slot)
    {
        var (lost, healed) = Drain(slot, Wolf(boss: true), new BossCombatContext { DivineArmorReduction = 0.5 });
        lost.Should().BeGreaterThan(0);
        healed.Should().Be(lost / 2, "half of the damage dealt after the divine armour");
    }

    [Theory]
    [InlineData(11)]
    [InlineData(15)]
    public void SummonDrain136_ADrainOnATargetWithLessHpThanTheRoll_HealsHalfTheHpItHad(int slot)
    {
        var (lost, healed) = Drain(slot, Wolf(hp: 60));
        lost.Should().Be(60, "the target had 60 HP and the roll is far more");
        healed.Should().Be(30, "half of the HP the target lost, not of the roll");
    }

    [Fact]
    public void SummonDrain136_ADrainByAnNpcSageTeammate_OnABossBehindDivineArmour_HealsHalfTheHpItLost()
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var sage = Caster(Member("Sage Mate", CharacterClass.Sage, level: 55));
            var target = Wolf(boss: true);
            var rig = Rig(G.Hero(1000), new List<Character> { sage }, target);
            typeof(CombatEngine).GetField("random", F)!.SetValue(rig.Engine, new Random(seed));
            rig.Engine.BossContext = new BossCombatContext { DivineArmorReduction = 0.5 };
            long before = target.HP, hp = sage.HP;
            bool acted = ((Task<bool>)typeof(CombatEngine).GetMethod("TryTeammateOffensiveSpell", F)!
                .Invoke(rig.Engine, new object[] { sage, rig.Monsters, rig.Result })!).GetAwaiter().GetResult();
            if (!acted || target.HP == before) continue;
            rig.Result.CombatLog.Should().Contain(l => l.Contains("casts Energy Drain"), "the level 55 Sage's best single target attack");
            long lost = before - target.HP;
            (sage.HP - hp).Should().Be(lost / 2, "half of the damage dealt after the divine armour");
            return;
        }
        throw new Exception("the teammate never cast its drain in 40 seeds");
    }

    // ---------------------------------------------------------------- rule 3: Grand Finale's count

    [Theory]
    [InlineData(CharacterClass.Bard)]
    [InlineData(CharacterClass.Jester)]
    public void SummonDrain136_GrandFinale_AddsItsDamageToTheFightTotalAndThePlayersRecord(CharacterClass cls)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try
        {
            GameConfig.PlayerHitRollEnabled = false;
            var hero = Member("Bard", cls);
            var rig = Rig(hero, new List<Character>(), Wolf(), Wolf(), Wolf());
            var ability = ClassAbilitySystem.GetAbility("grand_finale");
            ability.Should().NotBeNull();
            ability!.SpecialEffect.Should().Be("grand_finale_jester");
            var use = new ClassAbilityResult { Success = true, AbilityUsed = ability, Damage = 1000, SpecialEffect = ability.SpecialEffect };
            long total = rig.Result.TotalDamageDealt, recorded = hero.Statistics.TotalDamageDealt;
            ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                .Invoke(rig.Engine, new object?[] { hero, rig.Monsters[0], rig.Monsters, use, rig.Result })!).GetAwaiter().GetResult();
            long lost = rig.Monsters.Sum(m => 10_000_000 - m.HP);
            lost.Should().BeGreaterThan(0);
            (rig.Result.TotalDamageDealt - total).Should().Be(lost, "the fight's total rises by the damage Grand Finale dealt");
            (hero.Statistics.TotalDamageDealt - recorded).Should().Be(lost, "and so does the player's recorded damage");
        }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }
}
