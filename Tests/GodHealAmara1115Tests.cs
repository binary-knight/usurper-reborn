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
/// 1.2.0: Amara's +15% heal boon on every heal a follower casts (self or ally; spells, abilities
/// and songs), through the one cast-heal helper Solarius shares, once per heal. And the healer
/// spec +20% on the heal spells an NPC healer-spec teammate casts; player casts are unchanged.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodHealAmara1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Hero(string name, CharacterClass cls = CharacterClass.Cleric) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = cls, Level = 40,
        HP = 10, MaxHP = 100_000, Mana = 100_000, MaxMana = 100_000, Wisdom = 50, Intelligence = 50,
        CombatSpeed = CombatSpeed.Instant,
    };

    private static T WithAmara<T>(Character c, Func<T> body)
    {
        GodRegistry.SetWorshippedGod(c, "Amara").Should().BeTrue();
        c.GodFavor = 60;
        try { return body(); }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    private static CombatEngine Engine(Character current, List<Character>? teammates = null)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, current);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, teammates ?? new List<Character>());
        return engine;
    }

    private static Monster Goblin() => new Monster { Name = "Goblin", Level = 5, HP = 100, MaxHP = 100, IsActive = true };

    private static void SpellEffects(CombatEngine engine, Character target, int healing, Character? healer) =>
        typeof(CombatEngine).GetMethod("ApplySpellEffects", F)!
            .Invoke(engine, new object?[] { target, null, new SpellSystem.SpellResult { Success = true, Healing = healing }, null, null, healer });

    private static void Ability(CombatEngine engine, Character user, string abilityId, int healing, CombatResult result)
    {
        var m = Goblin();
        var used = ClassAbilitySystem.GetAbility(abilityId)!;
        var ability = new ClassAbilityResult { Success = true, Healing = healing, AbilityUsed = used, SpecialEffect = used.SpecialEffect };
        ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
            .Invoke(engine, new object[] { user, m, new List<Monster> { m }, ability, result })!).GetAwaiter().GetResult();
    }

    // ---------------- (a) Amara on every heal the follower casts ----------------

    [Fact]
    public void Amara_SelfCastHealSpell_IsBoosted_AndNotForTheGodless()
    {
        var c = Hero("AmSelf");
        var engine = Engine(c);
        SpellEffects(engine, c, 1000, null);
        c.HP.Should().Be(1010, "a non-worshipper's self heal is as cast");
        c.HP = 10;
        WithAmara(c, () => { SpellEffects(engine, c, 1000, null); return 0; });
        c.HP.Should().Be(1160, "an Amara Zealot's self heal: 1000 x 1.15 = 1150");
    }

    [Fact]
    public void Amara_HealSpellOnAnAlly_FollowsTheCaster()
    {
        var caster = Hero("AmAllyCaster");
        var ally = Hero("AmAllyTarget");
        var engine = Engine(caster, new List<Character> { ally });
        WithAmara(caster, () => { SpellEffects(engine, ally, 1000, caster); return 0; });
        ally.HP.Should().Be(1160, "the caster follows Amara: 1000 x 1.15 = 1150");
        ally.HP = 10;
        WithAmara(ally, () => { SpellEffects(engine, ally, 1000, caster); return 0; });
        ally.HP.Should().Be(1010, "the boon is the caster's, and the caster follows no god");
    }

    [Fact]
    public void Amara_AbilityHeal_OnSelfAndOnAnAlly_IsBoostedOnce()
    {
        var c = Hero("AmAbility");
        var engine = Engine(c);
        WithAmara(c, () => { Ability(engine, c, "greater_heal", 1000, new CombatResult { Player = c }); return 0; });
        c.HP.Should().Be(1160, "1000 x 1.15 once; twice would be 1322");

        // An ally heal ability used by a follower who is not the current player (the AI target path)
        var paladin = Hero("AmPaladin", CharacterClass.Paladin);
        paladin.HP = paladin.MaxHP;
        var ally = Hero("AmAbilityAlly");
        var engine2 = Engine(Hero("AmLeader"), new List<Character> { ally });
        WithAmara(paladin, () =>
        {
            Ability(engine2, paladin, "lay_on_hands", 1000, new CombatResult { Player = paladin, Teammates = new List<Character> { ally } });
            return 0;
        });
        ally.HP.Should().Be(1160, "the paladin follows Amara: 1000 x 1.15 on the ally");
    }

    [Fact]
    public void Amara_SongHeal_IsBoostedOnce_OnTheBardAndTheParty()
    {
        var bard = Hero("AmBard", CharacterClass.Bard);
        var ally = Hero("AmSongAlly");
        var engine = Engine(bard, new List<Character> { ally });
        WithAmara(bard, () =>
        {
            Ability(engine, bard, "song_of_rest", 1000, new CombatResult { Player = bard, Teammates = new List<Character> { ally } });
            return 0;
        });
        bard.HP.Should().Be(1160, "the bard's own heal: 1000 x 1.15");
        ally.HP.Should().Be(10 + 862, "the party gets 75% of the boosted heal (1150 x 0.75); a second boon would give 991");
    }

    [Fact]
    public void Amara_SongHeal_ForAHealerSpecBard_TakesTheSpecBonusOnce()
    {
        var bard = Hero("AmMinstrel", CharacterClass.Bard);
        bard.Specialization = ClassSpecialization.Minstrel;
        var ally = Hero("AmMinstrelAlly");
        var engine = Engine(bard, new List<Character> { ally });
        Ability(engine, bard, "song_of_rest", 1000, new CombatResult { Player = bard, Teammates = new List<Character> { ally } });
        bard.HP.Should().Be(1210, "1000 x 1.2");
        ally.HP.Should().Be(10 + 900, "75% of 1200; applying the spec twice would give 1080");
    }

    [Fact]
    public void Amara_WardStaysAsItWas_AndPartyHealHasOneCaller()
    {
        var gods = new GodSystem();
        var c = Hero("AmWard");
        GodRegistry.SetWorshippedGod(c, "Amara", gods).Should().BeTrue();
        c.GodFavor = 60;
        GodBoonSystem.PartyWard(c, 100, gods).Should().Be(115, "Amara's ward boon is unchanged");
        GodBoonSystem.CastHeal(c, 1000, null, gods).Should().Be(1150);

        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "UsurperReborn.sln"))) root = Path.GetDirectoryName(root)!;
        var callers = Directory.GetFiles(Path.Combine(root!, "Scripts"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f).Select(l => (f, l)))
            .Where(x => x.l.Contains("PartyHeal(") && !x.l.Contains("public static long PartyHeal("))
            .ToList();
        callers.Should().HaveCount(1, "Amara's heal boon is applied only inside CastHeal, so no heal takes it twice");
        callers[0].l.Should().Contain("HealAgainstUndead(caster, PartyHeal(caster, heal, gods), monsters, gods)");
    }

    // ---------------- (b) the healer spec bonus on an NPC healer's heal spell ----------------

    private static long TeammateHealTotal(Character teammate, int casts)
    {
        var target = Hero("SpecTarget");
        var engine = Engine(target, new List<Character> { teammate });
        var result = new CombatResult { Player = target };
        var method = typeof(CombatEngine).GetMethod("TeammateHealWithSpell", F)!;
        long total = 0;
        for (int i = 0; i < casts; i++)
        {
            teammate.Mana = teammate.MaxMana;
            target.HP = 10;
            ((Task<bool>)method.Invoke(engine, new object[] { teammate, target, result })!).GetAwaiter().GetResult();
            total += target.HP - 10;
        }
        return total;
    }

    private static Character NpcCleric(string name, ClassSpecialization spec)
    {
        var c = Hero(name);
        c.AI = CharacterAI.Computer;
        c.Specialization = spec;
        return c;
    }

    [Fact]
    public void HealerSpec_ReachesAnNpcHealersHealSpell()
    {
        // Heal spells roll their size; 300 casts each put the means well inside the 1.2x gap.
        long plain = TeammateHealTotal(NpcCleric("SpecPlain", ClassSpecialization.None), 300);
        long holy = TeammateHealTotal(NpcCleric("SpecHoly", ClassSpecialization.Holy), 300);
        plain.Should().BeGreaterThan(0);
        ((double)holy / plain).Should().BeGreaterThan(1.1, "a Holy NPC's heal spell gets the healer spec +20%");
    }

    [Fact]
    public void HealerSpec_PlayerSelfCastSpell_Unchanged()
    {
        var c = Hero("SpecPlayer");
        c.Specialization = ClassSpecialization.Holy;
        SpellEffects(Engine(c), c, 1000, null);
        c.HP.Should().Be(1010, "a player's self-cast heal spell takes no healer spec bonus, as before");
    }
}
