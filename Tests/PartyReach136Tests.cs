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
/// 1.3.6 G5 (CP1 and singles): who a party effect reaches. A party effect reaches the caster once at its own share and
/// every other living member once at the teammates' share: the player is reached when an NPC teammate casts, and the
/// caster never takes the teammates' share on top of its own. With the player as caster, nothing changes. Also: an NPC's
/// ally heal can pick the player, an NPC's Earthbind Totem and Bloodlust work through their own holder, Spirit Link
/// with every listed teammate dead gives the solo heal, a teammate's Turn Undead lands only on an undead or demon foe, an
/// NPC Bard's ability use runs Bardic Inspiration, and a teammate never takes Disappearing Act.
/// </summary>
[Collection("SharedGameSingletons")]
public class PartyReach136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const long Big = 1_000_000;

    internal sealed class Rig
    {
        public CombatEngine Engine = null!;
        public CombatResult Result = null!;
        public List<Monster> Monsters = new();
        public MemoryStream Output = new();
        public TerminalEmulator Terminal = null!;
        public string Shown()
        {
            Terminal.StreamWriterInternal?.Flush();
            return Encoding.UTF8.GetString(Output.ToArray());
        }
    }

    /// <summary>The fight's player <paramref name="hero"/> with <paramref name="team"/> as result.Teammates (and the
    /// engine's teammates, as in a fight); <paramref name="script"/> is the typed input.</summary>
    internal static Rig NewRig(Character hero, List<Character> team, string script = "", int seed = 136, params Monster[] monsters)
    {
        _ = new GriefSystem();
        var rig = new Rig();
        rig.Terminal = new TerminalEmulator(new G.ScriptedStream(script), rig.Output);
        rig.Engine = new CombatEngine(rig.Terminal);
        typeof(CombatEngine).GetField("random", F)!.SetValue(rig.Engine, new Random(seed));
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(rig.Engine, hero);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(rig.Engine, hero);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(rig.Engine, team);
        rig.Result = new CombatResult { Player = hero, Teammates = team, CombatLog = new List<string>(), CurrentRound = 2 };
        rig.Monsters = monsters.Length > 0 ? monsters.ToList() : new List<Monster> { G.Dummy() };
        return rig;
    }

    internal static Character Hero(CharacterClass cls = CharacterClass.Warrior)
    {
        var h = G.Hero(hp: Big, level: 50, cls: cls);
        h.HP = 1000;
        return h;
    }

    internal static Character Npc(string name, CharacterClass cls = CharacterClass.Warrior)
    {
        var m = G.Mate(name, hp: Big, level: 50);
        m.Class = cls;
        m.HP = 1000;
        return m;
    }

    /// <summary>One cast of <paramref name="id"/> by <paramref name="actor"/> with these values, through the live ability
    /// path (ApplyAbilityEffectsMultiMonster: the player's and, through TryTeammateClassAbility, every teammate's).</summary>
    internal static void Apply(Rig rig, Character actor, string id, int healing = 0, int attack = 0, int defense = 0, int duration = 3)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try
        {
            GameConfig.PlayerHitRollEnabled = false;
            var used = ClassAbilitySystem.GetAbility(id)!;
            var r = new ClassAbilityResult
            {
                Success = true, Healing = healing, AttackBonus = attack, DefenseBonus = defense, Duration = duration,
                AbilityUsed = used, SpecialEffect = used.SpecialEffect,
            };
            ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                .Invoke(rig.Engine, new object?[] { actor, rig.Monsters[0], rig.Monsters, r, rig.Result })!).GetAwaiter().GetResult();
        }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    private static int Entry(Character c, BonusSlot slot, string source) =>
        (slot == BonusSlot.Attack ? c.AttackBonusEntries : c.DefenseBonusEntries).Where(e => e.Source == source).Sum(e => e.Value);

    private static int Entries(Character c, string source) =>
        c.AttackBonusEntries.Count(e => e.Source == source) + c.DefenseBonusEntries.Count(e => e.Source == source);

    /// <summary>The player, an NPC caster listed in the teammates (as in a real party) and one more NPC teammate.</summary>
    private static (Rig rig, Character hero, Character caster, Character mate) NpcParty(CharacterClass cls)
    {
        var hero = Hero();
        var caster = Npc("Caster", cls);
        var mate = Npc("Mate");
        return (NewRig(hero, new List<Character> { caster, mate }), hero, caster, mate);
    }

    /// <summary>The player casting, with two NPC teammates.</summary>
    private static (Rig rig, Character hero, Character a, Character b) PlayerParty(CharacterClass cls)
    {
        var hero = Hero(cls);
        var a = Npc("MateA");
        var b = Npc("MateB");
        return (NewRig(hero, new List<Character> { a, b }), hero, a, b);
    }

    // ---------------------------------------------------------------- rule 1: Cleric

    [Theory]
    [InlineData("circle_of_healing")]
    [InlineData("holy_covenant")]
    [InlineData("beacon_of_light")]
    public void PartyReach136_Cleric_NpcCaster_ReachesThePlayerOnce_AndItselfOnce(string id)
    {
        var (rig, hero, caster, mate) = NpcParty(CharacterClass.Cleric);
        Apply(rig, caster, id, healing: 400, defense: id == "beacon_of_light" ? 30 : 0);
        hero.HP.Should().Be(1000 + 300, $"{id}: the player takes the teammates' 75 percent share once");
        caster.HP.Should().Be(1000 + 400, $"{id}: the caster takes its own share once, not a teammate's share on top");
        mate.HP.Should().Be(1000 + 300, $"{id}: every other teammate once");
        if (id == "beacon_of_light")
        {
            Entry(hero, BonusSlot.Defense, id).Should().Be(30, "the player gets the beacon's full defence");
            Entries(hero, id).Should().Be(1);
        }
    }

    [Fact]
    public void PartyReach136_Cleric_PlayerCaster_Unchanged()
    {
        var (rig, hero, a, b) = PlayerParty(CharacterClass.Cleric);
        Apply(rig, hero, "circle_of_healing", healing: 400);
        hero.HP.Should().Be(1400);
        a.HP.Should().Be(1300);
        b.HP.Should().Be(1300);
    }

    // ---------------------------------------------------------------- rule 1: Bard

    [Fact]
    public void PartyReach136_Bard_NpcSong_ReachesThePlayerOnce_AndTheBardOnce()
    {
        var (rig, hero, caster, mate) = NpcParty(CharacterClass.Bard);
        Apply(rig, caster, "song_of_rest", healing: 400);
        hero.HP.Should().Be(1300, "the player takes the song's 75 percent share");
        caster.HP.Should().Be(1400, "the Bard takes its own heal once");
        mate.HP.Should().Be(1300);

        Apply(rig, caster, "inspiring_tune", attack: 50, defense: 40, duration: 5);
        Entry(hero, BonusSlot.Attack, "inspiring_tune").Should().Be(30, "the player takes 60 percent of the attack bonus");
        Entry(hero, BonusSlot.Defense, "inspiring_tune").Should().Be(24, "and 60 percent of the defence bonus");
        Entry(caster, BonusSlot.Attack, "inspiring_tune").Should().Be(50, "the Bard holds its own full bonus");
    }

    [Fact]
    public void PartyReach136_Bard_NpcCountercharmAndFinale_ReachThePlayer()
    {
        var (rig, hero, caster, mate) = NpcParty(CharacterClass.Bard);
        hero.Poison = 5; hero.PoisonTurns = 3; hero.ApplyStatus(StatusEffect.Poisoned, 3);
        Apply(rig, caster, "countercharm");
        hero.PoisonTurns.Should().Be(0, "the NPC Bard's Countercharm cleanses the player");
        hero.HasStatus(StatusEffect.Poisoned).Should().BeFalse();

        Apply(rig, caster, "grand_finale");
        Entry(hero, BonusSlot.Attack, "grand_finale").Should().Be(15, "Grand Finale's +15 attack reaches the player");
        Entry(mate, BonusSlot.Attack, "grand_finale").Should().Be(15);
        Entries(caster, "grand_finale").Should().Be(1, "the caster holds one entry");
    }

    [Fact]
    public void PartyReach136_Bard_PlayerSong_Unchanged()
    {
        var (rig, hero, a, b) = PlayerParty(CharacterClass.Bard);
        Apply(rig, hero, "song_of_rest", healing: 400);
        hero.HP.Should().Be(1400);
        a.HP.Should().Be(1300);
        b.HP.Should().Be(1300);
    }

    // ---------------------------------------------------------------- rule 1: Shaman totems

    private static void Totem(Rig rig, Character holder, int type)
    {
        holder.ActiveTotemType = type;
        holder.ActiveTotemRounds = 3;
        typeof(CombatEngine).GetMethod("ProcessShamanTotemEffects", F)!.Invoke(rig.Engine, new object?[] { holder, rig.Monsters, rig.Result });
    }

    [Fact]
    public void PartyReach136_Shaman_NpcHealingTotem_HealsThePlayerOnce_AndTheShamanOnce()
    {
        var (rig, hero, caster, mate) = NpcParty(CharacterClass.MysticShaman);
        Totem(rig, caster, 1);
        long tick = (long)(Big * GameConfig.ShamanTotemHealPercentGroup);
        hero.HP.Should().Be(1000 + tick, "the NPC Shaman's healing totem heals the player at the group rate");
        caster.HP.Should().Be(1000 + tick, "the Shaman once");
        mate.HP.Should().Be(1000 + tick);
    }

    [Fact]
    public void PartyReach136_Shaman_PlayerHealingTotem_Unchanged()
    {
        var (rig, hero, a, b) = PlayerParty(CharacterClass.MysticShaman);
        Totem(rig, hero, 1);
        long tick = (long)(Big * GameConfig.ShamanTotemHealPercentGroup);
        hero.HP.Should().Be(1000 + tick);
        a.HP.Should().Be(1000 + tick);
        b.HP.Should().Be(1000 + tick);
    }

    [Fact]
    public void PartyReach136_Shaman_NpcSpiritLink_SharesWithThePlayer_EachMemberOnce()
    {
        var (rig, hero, caster, mate) = NpcParty(CharacterClass.MysticShaman);
        hero.MaxHP = 2000; hero.HP = 200;
        caster.MaxHP = 1000; caster.HP = 900;
        mate.MaxHP = 1000; mate.HP = 500;
        Totem(rig, caster, 5);
        // 1600 HP over 4000 maximum: 40 percent each
        hero.HP.Should().Be(800, "the player is in the link");
        caster.HP.Should().Be(400, "the Shaman counted once");
        mate.HP.Should().Be(400);
    }

    // ---------------------------------------------------------------- rule 4: Spirit Link with every teammate dead

    [Fact]
    public void PartyReach136_SpiritLink_EveryListedTeammateDead_GivesTheSoloHeal()
    {
        var (rig, hero, a, b) = PlayerParty(CharacterClass.MysticShaman);
        a.HP = 0; b.HP = 0;
        Totem(rig, hero, 5);
        hero.HP.Should().Be(1000 + (long)(Big * GameConfig.ShamanSpiritLinkPercent), "no other living member: the solo heal, as with no teammates");
    }

    [Fact]
    public void PartyReach136_SpiritLink_NoTeammates_SoloHealUnchanged()
    {
        var hero = Hero(CharacterClass.MysticShaman);
        var rig = NewRig(hero, new List<Character>());
        Totem(rig, hero, 5);
        hero.HP.Should().Be(1000 + (long)(Big * GameConfig.ShamanSpiritLinkPercent));
    }

    // ---------------------------------------------------------------- rule 1: the AUDIT-F party handlers

    [Theory]
    [InlineData("harmonic_shield", CharacterClass.Wavecaller)]
    [InlineData("oceans_voice", CharacterClass.Tidesworn)]
    [InlineData("inspiring_cadence", CharacterClass.Wavecaller)]
    [InlineData("crescendo", CharacterClass.Wavecaller)]
    public void PartyReach136_PartyHandlers_NpcCaster_BuffThePlayerOnce(string id, CharacterClass cls)
    {
        var (rig, hero, caster, mate) = NpcParty(cls);
        Apply(rig, caster, id, duration: 3);
        Entries(hero, id).Should().BeGreaterThan(0, $"{id}: the NPC caster's party buff reaches the player");
        foreach (var slot in new[] { BonusSlot.Attack, BonusSlot.Defense })
        {
            Entry(hero, slot, id).Should().Be(Entry(mate, slot, id), $"{id}: the player takes the teammates' share");
            (slot == BonusSlot.Attack ? hero.AttackBonusEntries : hero.DefenseBonusEntries).Count(e => e.Source == id).Should().BeLessThan(2);
        }
        if (id == "crescendo")
            rig.Shown().Should().Contain(Loc.Get("combat.ability_crescendo_aoe_allies", 2, 100), "two allies besides the caster: the player and the mate");
    }

    [Fact]
    public void PartyReach136_PartyHandlers_NpcHeals_ReachThePlayerOnce_AndTheCasterOnce()
    {
        var (rig, hero, caster, mate) = NpcParty(CharacterClass.Tidesworn);
        Apply(rig, caster, "oceans_embrace", healing: 300);
        hero.HP.Should().Be(1300, "Ocean's Embrace heals the player");
        caster.HP.Should().Be(1300, "the caster once");
        mate.HP.Should().Be(1300);

        var (rig2, hero2, caster2, mate2) = NpcParty(CharacterClass.Wavecaller);
        Apply(rig2, caster2, "tidal_harmony");
        hero2.HP.Should().Be(1200, "Tidal Harmony's 200 reaches the player");
        caster2.HP.Should().Be(1000, "the caster's own heal is its ability heal, not the teammates' 200");
        mate2.HP.Should().Be(1200);
    }

    [Fact]
    public void PartyReach136_PartyHandlers_NpcCalmWaters_ShieldsThePlayer()
    {
        var (rig, hero, caster, mate) = NpcParty(CharacterClass.Wavecaller);
        Apply(rig, caster, "calm_waters");
        hero.CalmWatersRounds.Should().Be(GameConfig.CalmWatersShieldDuration);
        caster.CalmWatersRounds.Should().Be(GameConfig.CalmWatersShieldDuration);
        mate.CalmWatersRounds.Should().Be(GameConfig.CalmWatersShieldDuration);
    }

    [Fact]
    public void PartyReach136_PartyHandlers_PlayerCaster_Unchanged()
    {
        var (rig, hero, a, b) = PlayerParty(CharacterClass.Tidesworn);
        Apply(rig, hero, "oceans_embrace", healing: 300);
        hero.HP.Should().Be(1300);
        a.HP.Should().Be(1300);
        b.HP.Should().Be(1300);
        Apply(rig, hero, "oceans_voice", duration: 3);
        Entry(a, BonusSlot.Attack, "oceans_voice").Should().Be(50);
        Entry(b, BonusSlot.Defense, "oceans_voice").Should().Be(30);
        Entries(hero, "oceans_voice").Should().Be(0, "the player's own part is its ability bonus (none in this cast)");
    }

    [Fact]
    public void PartyReach136_Duel_ReachesNoOne()
    {
        var a = Hero(); var b = Hero();
        var duel = new CombatResult { Player = a, Opponent = b, CombatLog = new List<string>() };
        CombatEngine.PartyReachOthers(b, duel).Should().BeEmpty("a duel has no party: the opponent is never reached");
        CombatEngine.PartyReachOthers(a, new CombatResult { Player = a }).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- rule 2: the ally heal pick

    [Fact]
    public void PartyReach136_NpcAllyHeal_PicksThePlayer_WhenThePlayerIsMostHurt()
    {
        var (rig, hero, caster, mate) = NpcParty(CharacterClass.Paladin);
        hero.HP = Big / 10;      // 10 percent
        caster.HP = Big / 2;     // 50 percent
        mate.HP = Big * 3 / 10;  // 30 percent
        Apply(rig, caster, "lay_on_hands", healing: 500);
        hero.HP.Should().Be(Big / 10 + 500, "the most hurt member by HP share is the player");
        mate.HP.Should().Be(Big * 3 / 10);
        caster.HP.Should().Be(Big / 2);
    }

    [Fact]
    public void PartyReach136_NpcAllyHeal_PicksTheMostHurtTeammate_AsBefore()
    {
        var (rig, hero, caster, mate) = NpcParty(CharacterClass.Paladin);
        hero.HP = Big / 2;
        caster.HP = Big * 6 / 10;
        mate.HP = Big / 10;
        Apply(rig, caster, "lay_on_hands", healing: 500);
        mate.HP.Should().Be(Big / 10 + 500);
        hero.HP.Should().Be(Big / 2);
    }

    [Fact]
    public void PartyReach136_PlayerAllyHealMenu_NumberingUnchanged()
    {
        var hero = Hero(CharacterClass.Paladin);
        var a = Npc("MateA"); var b = Npc("MateB");
        var rig = NewRig(hero, new List<Character> { a, b }, script: "2\n");
        Apply(rig, hero, "lay_on_hands", healing: 500);
        string shown = rig.Shown();
        shown.Should().Contain("[0] " + Loc.Get("combat.yourself"));
        shown.Should().Contain("[1] MateA");
        shown.Should().Contain("[2] MateB");
        b.HP.Should().Be(1500, "2 is the second living teammate in team order");
        a.HP.Should().Be(1000);
        hero.HP.Should().Be(1000);
    }

    // ---------------------------------------------------------------- rule 3: Earthbind and Bloodlust

    private static long PlayerLossFromOneSwing(bool npcTotem, int seed)
    {
        var hero = G.Hero(hp: 50_000, level: 30);
        var shaman = Npc("Shaman", CharacterClass.MysticShaman);
        if (npcTotem) { shaman.ActiveTotemType = 2; shaman.ActiveTotemRounds = 3; }
        var foe = G.Foe("Orc", 50_000, 400, level: 30, defence: 5);
        var rig = NewRig(hero, new List<Character> { shaman }, seed: seed, monsters: foe);
        rig.Result.Teammates = new List<Character>();   // the swing goes to the player; the Shaman is in the engine's party
        long hp0 = hero.HP;
        ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(rig.Engine, new object?[] { foe, hero, rig.Result, null })!).GetAwaiter().GetResult();
        return hp0 - hero.HP;
    }

    [Fact]
    public void PartyReach136_NpcEarthbind_SlowsTheSwingOnThePlayer()
    {
        int compared = 0;
        for (int seed = 1; seed < 60 && compared < 5; seed++)
        {
            long without = PlayerLossFromOneSwing(false, seed);
            if (without < 50) continue;   // a miss or a glance
            long with = PlayerLossFromOneSwing(true, seed);
            with.Should().BeLessThan(without, $"seed {seed}: the NPC Shaman's own Earthbind Totem slows the monster");
            compared++;
        }
        compared.Should().BeGreaterThan(0, "some swing landed");
    }

    private static long MateLossFromOneSwing(bool npcTotem, int seed)
    {
        var hero = G.Hero(hp: 50_000, level: 30);
        var shaman = Npc("Shaman", CharacterClass.MysticShaman);
        if (npcTotem) { shaman.ActiveTotemType = 2; shaman.ActiveTotemRounds = 3; }
        var mate = G.Mate("Target", hp: 50_000, level: 30);
        var foe = G.Foe("Orc", 50_000, 400, level: 30, defence: 5);
        var rig = NewRig(hero, new List<Character> { shaman, mate }, seed: seed, monsters: foe);
        long hp0 = mate.HP;
        ((Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
            .Invoke(rig.Engine, new object?[] { foe, mate, rig.Result, null })!).GetAwaiter().GetResult();
        return hp0 - mate.HP;
    }

    [Fact]
    public void PartyReach136_NpcEarthbind_SlowsTheSwingOnATeammate()
    {
        int compared = 0;
        for (int seed = 1; seed < 60 && compared < 5; seed++)
        {
            long without = MateLossFromOneSwing(false, seed);
            if (without < 50) continue;
            long with = MateLossFromOneSwing(true, seed);
            with.Should().BeLessThan(without, $"seed {seed}: the NPC Shaman's own Earthbind Totem slows the monster");
            compared++;
        }
        compared.Should().BeGreaterThan(0, "some swing landed");
    }

    [Fact]
    public void PartyReach136_NpcBloodlust_HealsTheNpcOnItsOwnKill()
    {
        var hero = Hero();
        hero.HasBloodlust = true;
        var npc = Npc("Berserk", CharacterClass.Barbarian);
        npc.HasBloodlust = true;
        var foe = G.Foe("Orc", 10, 1, level: 5, defence: 0);
        var rig = NewRig(hero, new List<Character> { npc }, monsters: foe);
        ((Task)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
            .Invoke(rig.Engine, new object?[] { foe, 5000L, rig.Result, "attack", npc, false })!).GetAwaiter().GetResult();
        foe.HP.Should().Be(0);
        npc.HP.Should().Be(1000 + npc.Level * 2 + 20, "the holder heals on its own kill");
        hero.HP.Should().Be(1000, "the player does not heal on a teammate's kill");
        rig.Shown().Should().Contain(Loc.Get("combat.bloodlust_trigger_npc", npc.DisplayName, npc.Level * 2 + 20));
    }

    [Fact]
    public void PartyReach136_PlayerBloodlust_Unchanged()
    {
        var hero = Hero();
        hero.HasBloodlust = true;
        var foe = G.Foe("Orc", 10, 1, level: 5, defence: 0);
        var rig = NewRig(hero, new List<Character>(), monsters: foe);
        ((Task)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
            .Invoke(rig.Engine, new object?[] { foe, 5000L, rig.Result, "attack", hero, false })!).GetAwaiter().GetResult();
        hero.HP.Should().Be(1000 + hero.Level * 2 + 20);
        rig.Shown().Should().Contain(Loc.Get("combat.bloodlust_trigger", hero.Level * 2 + 20));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void PartyReach136_NpcBloodlustLine_Fits79Columns(string lang)
    {
        string line = Loc.GetIn(lang, "combat.bloodlust_trigger_npc", new string('W', 20), 99_999);
        line.Should().NotContain("{");
        line.Length.Should().BeLessThanOrEqualTo(79, line);
    }

    // ---------------------------------------------------------------- rule 5: Turn Undead on the teammate path

    private static Task<bool> TeammateSpell(Rig rig, Character teammate) =>
        (Task<bool>)typeof(CombatEngine).GetMethod("TryTeammateOffensiveSpell", F)!.Invoke(rig.Engine, new object?[] { teammate, rig.Monsters, rig.Result })!;

    private static Character Cleric22()
    {
        var c = Npc("Priest", CharacterClass.Cleric);
        c.Level = 22;   // Turn Undead (slot 6) is the Cleric's one attack spell at this level
        c.Wisdom = 200; c.BaseWisdom = 200; c.Intelligence = 200; c.BaseIntelligence = 200;
        c.MaxMana = 100_000; c.Mana = 100_000;
        return c;
    }

    [Fact]
    public void PartyReach136_TeammateTurnUndead_LandsOnlyOnTheUndead()
    {
        var hero = Hero();
        var cleric = Cleric22();
        var living = G.Foe("Orc", 40, 1, level: 10);
        var dead = G.Foe("Ghoul", 1_000_000, 1, level: 10);
        dead.MonsterClass = MonsterClass.Undead;
        var rig = NewRig(hero, new List<Character> { cleric }, monsters: new[] { living, dead });
        int casts = 0;
        for (int i = 0; i < 40; i++)
        {
            cleric.Mana = 100_000;
            TeammateSpell(rig, cleric).GetAwaiter().GetResult();
        }
        casts = rig.Shown().Split('\n').Count(l => l.Contains("Turn Undead"));
        casts.Should().BeGreaterThan(0, "the teammate cast Turn Undead with an undead foe present");
        living.HP.Should().Be(40, "Turn Undead never lands on the living foe, even as the lowest HP one");
        dead.HP.Should().BeLessThan(1_000_000, "it lands on the undead foe");
    }

    [Fact]
    public void PartyReach136_TeammateTurnUndead_NotCast_WithNoUndeadOrDemon()
    {
        var hero = Hero();
        var cleric = Cleric22();
        var living = G.Foe("Orc", 1_000_000, 1, level: 10);
        var rig = NewRig(hero, new List<Character> { cleric }, monsters: living);
        for (int i = 0; i < 20; i++)
        {
            cleric.Mana = 100_000;
            TeammateSpell(rig, cleric).GetAwaiter().GetResult().Should().BeFalse("no other attack spell at this level: the teammate does something else");
        }
        living.HP.Should().Be(1_000_000);
    }

    // ---------------------------------------------------------------- rule 6: Bardic Inspiration on the teammate path

    private static Task<bool> TeammateAbility(Rig rig, Character teammate) =>
        (Task<bool>)typeof(CombatEngine).GetMethod("TryTeammateClassAbility", F)!.Invoke(rig.Engine, new object?[] { teammate, rig.Monsters, rig.Result })!;

    private static void Ready(Rig rig, Character c)
    {
        c.CurrentCombatStamina = 10_000;
        c.MaxMana = 10_000; c.Mana = 10_000;
        ((System.Collections.IDictionary)typeof(CombatEngine).GetField("teammateCooldowns", F)!.GetValue(rig.Engine)!).Clear();
    }

    [Fact]
    public void PartyReach136_NpcBard_AbilityUse_RunsBardicInspiration()
    {
        var hero = Hero();
        var bard = G.Hero(hp: Big, level: 60, cls: CharacterClass.Bard, main: 30, mainType: WeaponType.Instrument, name: "Minstrel");
        bard.AI = CharacterAI.Computer;
        bard.HP = 1000;
        var mate = Npc("Mate");
        var rig = NewRig(hero, new List<Character> { bard, mate });
        int used = 0;
        bool inspired = false;
        for (int i = 0; i < 300 && !inspired; i++)
        {
            Ready(rig, bard);
            if (TeammateAbility(rig, bard).GetAwaiter().GetResult()) used++;
            inspired = new[] { hero, bard, mate }.Any(c => c.AttackBonusEntries.Any(e => e.Source == "bardic_inspiration"));
        }
        used.Should().BeGreaterThan(0, "the NPC Bard used abilities");
        inspired.Should().BeTrue("an NPC Bard's ability use runs Bardic Inspiration");
    }

    // ---------------------------------------------------------------- rule 7: no Disappearing Act for a teammate

    [Fact]
    public void PartyReach136_TeammatePick_NeverTakesDisappearingAct()
    {
        var hero = Hero();
        var jester = Npc("Joker", CharacterClass.Jester);
        jester.Level = 60;
        var rig = NewRig(hero, new List<Character> { jester });
        string name = ClassAbilitySystem.GetAbility("disappearing_act")!.DisplayName;
        int used = 0;
        for (int i = 0; i < 300; i++)
        {
            Ready(rig, jester);
            jester.HP = i % 2 == 0 ? Big / 10 : Big;   // wounded and whole
            if (TeammateAbility(rig, jester).GetAwaiter().GetResult()) used++;
        }
        used.Should().BeGreaterThan(20, "the Jester used abilities");
        rig.Shown().Should().NotContain(name, "a teammate never spends its turn on Disappearing Act");
    }
}
