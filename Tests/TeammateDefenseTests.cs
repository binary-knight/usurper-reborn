using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2: companions and NPC teammates look after themselves. Below 40 percent HP the ability
/// picker reaches for a defensive or evasive ability first; below 35 percent with nothing left
/// they brace (half damage for the round) instead of attacking; Defend clears at round end.
/// </summary>
[Collection("SharedGameSingletons")]
public class TeammateDefenseTests
{
    private static Character Teammate(long hp, long maxHp = 100) => new Character
    {
        Name2 = "Mira", Class = CharacterClass.Cleric, Race = CharacterRace.Elf, HP = hp, MaxHP = maxHp,
    };

    private static ClassAbilitySystem.ClassAbility Ability(string id, ClassAbilitySystem.AbilityType type, string effect = "", int defense = 0, int level = 1) =>
        new() { Id = id, Name = id, Type = type, SpecialEffect = effect, DefenseBonus = defense, LevelRequired = level };

    [Fact]
    public void DefensiveSelection_KeepsShieldsSidestepsAndDefensiveBuffs_DropsAttacks()
    {
        var pool = new List<ClassAbilitySystem.ClassAbility>
        {
            Ability("power_strike", ClassAbilitySystem.AbilityType.Attack),
            Ability("shield_wall", ClassAbilitySystem.AbilityType.Defense),
            Ability("evasive_roll", ClassAbilitySystem.AbilityType.Utility, effect: "dodge_next"),
            Ability("smoke_bomb", ClassAbilitySystem.AbilityType.Utility, effect: "smoke"),
            Ability("war_cry", ClassAbilitySystem.AbilityType.Buff, defense: 0),
            Ability("stone_skin", ClassAbilitySystem.AbilityType.Buff, defense: 8),
            Ability("hex", ClassAbilitySystem.AbilityType.Debuff),
        };
        CombatEngine.SelectDefensiveAbilities(pool).Select(a => a.Id)
            .Should().BeEquivalentTo(new[] { "shield_wall", "evasive_roll", "smoke_bomb", "stone_skin" });
    }

    [Fact]
    public void Defend_TriggersBelowTheThreshold_OnceOnly()
    {
        var engine = new CombatEngine();
        var tm = Teammate(30);
        engine.TryTeammateDefend(tm).Should().BeTrue();
        tm.IsDefending.Should().BeTrue();
        tm.ActiveStatuses.Should().ContainKey(StatusEffect.Defending);
        engine.TryTeammateDefend(tm).Should().BeFalse("already bracing this round");
    }

    [Fact]
    public void Defend_DoesNotTrigger_WhenHealthy()
    {
        var engine = new CombatEngine();
        engine.TryTeammateDefend(Teammate(50)).Should().BeFalse("half health is not desperate");
        engine.TryTeammateDefend(Teammate(35)).Should().BeFalse("the threshold is strictly below 35 percent");
        engine.TryTeammateDefend(Teammate(34)).Should().BeTrue();
    }

    [Fact]
    public async System.Threading.Tasks.Task Defend_DoesNotLeakIntoTheNextFight_WhenVictorySkipsTheRoundEnd()
    {
        // A brace on the round the last monster falls never sees the round-end clear (victory
        // breaks the round loop). The next fight's per-combat teammate scrub must clear it, or the
        // teammate enters every later fight already defending and never braces again.
        var owner = new Character
        {
            Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
            HP = 500, MaxHP = 500, BaseMaxHP = 500, Strength = 80, BaseStrength = 80, Defence = 40, BaseDefence = 40,
            Dexterity = 30, BaseDexterity = 30, Agility = 25, BaseAgility = 25, Constitution = 30, BaseConstitution = 30,
            CombatSpeed = CombatSpeed.Instant,
        };
        var tm = Teammate(100); tm.Level = 10; tm.Strength = 50; tm.BaseStrength = 50; tm.BaseMaxHP = 100;
        tm.IsDefending = true; tm.ActiveStatuses[StatusEffect.Defending] = 1; // left over from the previous fight
        var script = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("A\n", 12)) + string.Concat(Enumerable.Repeat("P\n", 6))));
        var term = new TerminalEmulator(script, new System.IO.MemoryStream());
        var engine = new CombatEngine(term);
        engine.SeedRandomForTests(1114);   // v1.1.14: the same rolls every run
        var rat = new Monster { Name = "Sewer Rat", Level = 1, HP = 1, MaxHP = 1, Strength = 1, Defence = 0, Experience = 5, Gold = 3 };
        var result = await engine.PlayerVsMonsters(owner, new List<Monster> { rat }, new List<Character> { tm }, offerMonkEncounter: false);
        result.Outcome.Should().Be(CombatOutcome.Victory);
        tm.IsDefending.Should().BeFalse("the per-combat scrub must clear a brace left over from the last fight");
        tm.ActiveStatuses.Should().NotContainKey(StatusEffect.Defending);
    }

    [Fact]
    public void Defend_ClearsAtRoundEnd()
    {
        var engine = new CombatEngine();
        var tm = Teammate(20);
        engine.TryTeammateDefend(tm).Should().BeTrue();
        var field = typeof(CombatEngine).GetField("currentTeammates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(engine, new List<Character> { tm });
        var owner = new Character { Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, HP = 100, MaxHP = 100 };
        typeof(CombatEngine).GetMethod("ProcessEndOfRoundAbilityEffects", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(engine, new object[] { owner });
        tm.IsDefending.Should().BeFalse();
        tm.ActiveStatuses.Should().NotContainKey(StatusEffect.Defending);
    }
}

public class TeammateDefensePriorityTests
{
    [Fact]
    public void WoundedForDefense_IsStrictlyBelowForty()
    {
        var c = new Character { HP = 40, MaxHP = 100 };
        CombatEngine.IsWoundedForDefense(c).Should().BeFalse();
        c.HP = 39;
        CombatEngine.IsWoundedForDefense(c).Should().BeTrue();
        c.MaxHP = 0;
        CombatEngine.IsWoundedForDefense(c).Should().BeFalse("a zero max HP is not a wound");
    }

    [Fact]
    public void AllyPermadeath_UsesTheTeamRate()
    {
        // An ally who dies beside the player is priced as "died with team", not "player killed an NPC".
        string src = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        int i = src.IndexOf("private async Task HandleNpcTeammateDeath(", System.StringComparison.Ordinal);
        string body = src.Substring(i, 4000);
        body.Should().Contain("GameConfig.PermadeathChanceDungeonTeam");
        body.Should().NotContain("GameConfig.PermadeathChancePlayerKill");
        GameConfig.PermadeathChanceDungeonTeam.Should().BeLessThan(GameConfig.PermadeathChancePlayerKill);
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new System.IO.DirectoryNotFoundException("repo root");
    }
}

[Collection("SharedGameSingletons")]
public class TeammateTauntVersusShieldTests
{
    /// <summary>A wounded tank with a shield and a taunt both affordable, and no monster taunted,
    /// must pick the shield: the taunt block may not overwrite the low-health choice.</summary>
    [Fact]
    public async System.Threading.Tasks.Task WoundedTank_PicksTheShield_NotTheTaunt()
    {
        var owner = new Character { Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 20, HP = 300, MaxHP = 300 };
        var tank = new Character
        {
            Name2 = "Aldric", Class = CharacterClass.Paladin, Race = CharacterRace.Human, Level = 20,
            HP = 30, MaxHP = 100, CurrentCombatStamina = 100, Mana = 0, MaxMana = 0,
        };
        tank.EquippedItems[EquipmentSlot.OffHand] = EquipmentDatabase.GetShields().First().Id; // Shield Wall and Aura of Protection require a shield
        var term = new TerminalEmulator(new System.IO.MemoryStream(), new System.IO.MemoryStream());
        var engine = new CombatEngine(term);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(CombatEngine).GetField("_combatOwner", flags)!.SetValue(engine, owner);
        typeof(CombatEngine).GetField("currentPlayer", flags)!.SetValue(engine, owner);
        typeof(CombatEngine).GetField("currentTeammates", flags)!.SetValue(engine, new List<Character> { tank });
        var monsters = new List<Monster> { new Monster { Name = "Ogre", Level = 15, HP = 400, MaxHP = 400, Strength = 30 } };
        var result = new CombatResult { Player = owner, Monsters = monsters, Teammates = new List<Character> { tank }, CombatLog = new List<string>() };

        var method = typeof(CombatEngine).GetMethod("TryTeammateClassAbility", flags)!;
        bool used = await (System.Threading.Tasks.Task<bool>)method.Invoke(engine, new object[] { tank, monsters, result })!;
        used.Should().BeTrue("a level-20 Paladin at 30 percent has shields and a taunt affordable");

        var cooldowns = (Dictionary<string, Dictionary<string, int>>)typeof(CombatEngine).GetField("teammateCooldowns", flags)!.GetValue(engine)!;
        var mine = cooldowns.Values.Single();
        mine.Keys.Should().Contain(k => k == "aura_of_protection" || k == "shield_wall", "the shield was used");
        mine.Keys.Should().NotContain("thundering_roar", "a taunt draws more hits onto a teammate who is already at 30 percent");
    }
}
