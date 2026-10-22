using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;
using K = global::CombatEngine.AbilityKind;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G2 (CP9, Q9, Q19, the game half of T8): one source for an ability's kinds (CombatEngine.AbilityKinds),
/// read by the base block, the hit roll, the off-hand swing, the quickbar target prompt and the teammate picks.
/// </summary>
[Collection("SharedGameSingletons")]
public class AbilityKinds136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int D = 400;

    // ---- the kinds of every ability (the list in the piece's REPORT) ----

    private static readonly Dictionary<string, K> Kinds = new()
    {
        ["power_strike"] = K.None,
        ["shield_wall"] = K.None,
        ["battle_cry"] = K.None,
        ["thundering_roar"] = K.Area | K.OwnDamage,
        ["shield_wall_formation"] = K.None,
        ["divine_mandate"] = K.None,
        ["rage_challenge"] = K.None,
        ["shield_bash"] = K.None,
        ["holy_shield_slam"] = K.None,
        ["execute"] = K.Execute,
        ["crushing_blow"] = K.None,
        ["last_stand"] = K.None,
        ["savage_charge"] = K.None,
        ["whirlwind"] = K.Area,
        ["bulwark"] = K.None,
        ["maelketh_fury"] = K.None,
        ["iron_fortress"] = K.None,
        ["champion_strike"] = K.None,
        ["wild_swing"] = K.None,
        ["rage"] = K.None,
        ["reckless_attack"] = K.None,
        ["intimidate"] = K.Mind,
        ["bloodlust"] = K.None,
        ["frenzy"] = K.None,
        ["primal_scream"] = K.Area | K.Mind,
        ["unstoppable"] = K.None,
        ["avatar_of_destruction"] = K.None,
        ["lay_on_hands"] = K.None,
        ["divine_smite"] = K.None,
        ["aura_of_protection"] = K.None,
        ["holy_avenger"] = K.None,
        ["cleansing_light"] = K.None,
        ["divine_shield"] = K.None,
        ["aurelion_blessing"] = K.None,
        ["judgment_day"] = K.Area | K.OwnDamage,
        ["avatar_of_light"] = K.None,
        ["prayer_of_mending"] = K.None,
        ["holy_smite"] = K.None,
        ["sanctuary"] = K.None,
        ["purifying_flame"] = K.None,
        ["greater_heal"] = K.None,
        ["circle_of_healing"] = K.None,
        ["divine_aegis"] = K.None,
        ["beacon_of_light"] = K.None,
        ["wrath_of_heaven"] = K.Area | K.OwnDamage,
        ["resurrection_prayer"] = K.None,
        ["holy_covenant"] = K.None,
        ["divine_intervention"] = K.None,
        ["avatar_of_grace"] = K.None,
        ["backstab"] = K.None,
        ["poison_blade"] = K.None,
        ["shadow_step"] = K.None,
        ["death_mark"] = K.None,
        ["biaxin"] = K.None,
        ["assassinate"] = K.Execute,
        ["vanish"] = K.None,
        ["noctura_embrace"] = K.None,
        ["blade_dance"] = K.Area,
        ["death_blossom"] = K.Area | K.OwnDamage,
        ["precise_shot"] = K.None,
        ["hunters_mark"] = K.None,
        ["evasive_roll"] = K.None,
        ["natures_blessing"] = K.None,
        ["volley"] = K.Area,
        ["camouflage"] = K.None,
        ["terravok_call"] = K.None,
        ["arrow_storm"] = K.Area,
        ["legendary_shot"] = K.None,
        ["mock"] = K.Mind,
        ["inspiring_tune"] = K.None,
        ["song_of_rest"] = K.None,
        ["charm"] = K.Mind,
        ["cutting_words"] = K.NotMelee,
        ["ballad_of_blades"] = K.None,
        ["countercharm"] = K.None,
        ["dissonant_whispers"] = K.Mind,
        ["war_drummers_cadence"] = K.None,
        ["disappearing_act"] = K.None,
        ["jugglers_trick"] = K.None,
        ["pratfall"] = K.None,
        ["deadly_joke"] = K.Mind,
        ["veloura_serenade"] = K.None,
        ["grand_finale"] = K.Area | K.OwnDamage,
        ["carnival_of_chaos"] = K.Area | K.Mind,
        ["last_laugh"] = K.Mind,
        ["legend_incarnate"] = K.None,
        ["throw_bomb"] = K.NotMelee,
        ["curative_tincture"] = K.None,
        ["healing_elixir"] = K.None,
        ["mending_meditation"] = K.None,
        ["smoke_bomb"] = K.None,
        ["acid_splash"] = K.NotMelee,
        ["mutagen"] = K.None,
        ["frost_bomb"] = K.NotMelee,
        ["greater_elixir"] = K.None,
        ["philosophers_bomb"] = K.Area | K.NotMelee,
        ["stimulant_brew"] = K.None,
        ["healing_mist"] = K.None,
        ["antidote_bomb"] = K.None,
        ["explosive_flask"] = K.Area | K.NotMelee,
        ["smoke_screen"] = K.None,
        ["rejuvenating_brew"] = K.None,
        ["battle_tincture"] = K.None,
        ["corrosive_cloud"] = K.Area | K.OwnDamage,
        ["grand_remedy"] = K.None,
        ["transmutation"] = K.None,
        ["cataclysm_bomb"] = K.Area | K.NotMelee,
        ["flametongue"] = K.None,
        ["healing_totem"] = K.None,
        ["rockbiter"] = K.None,
        ["earthbind_totem"] = K.None,
        ["lightning_bolt"] = K.OwnDamage,
        ["frostbrand"] = K.None,
        ["searing_totem"] = K.None,
        ["stormstrike"] = K.None,
        ["windfury_totem"] = K.None,
        ["chain_lightning"] = K.Area | K.OwnDamage | K.Primary,
        ["spirit_link_totem"] = K.None,
        ["ancestral_guidance"] = K.None,
        ["second_wind"] = K.None,
        ["focus"] = K.None,
        ["rally"] = K.None,
        ["desperate_strike"] = K.None,
        ["iron_will"] = K.None,
        ["undertow_stance"] = K.None,
        ["riptide_strike"] = K.None,
        ["breakwater"] = K.None,
        ["living_waters"] = K.None,
        ["maelstrom_faithful"] = K.Area | K.OwnDamage,
        ["abyssal_anchor"] = K.None,
        ["sanctified_torrent"] = K.Area | K.OwnDamage,
        ["oceans_embrace"] = K.None,
        ["tidal_colossus"] = K.None,
        ["eternal_vigil"] = K.None,
        ["wrath_of_the_deep"] = K.OwnDamage,
        ["inspiring_cadence"] = K.None,
        ["wave_echo"] = K.None,
        ["calm_waters"] = K.None,
        ["empathic_link"] = K.None,
        ["crescendo"] = K.Area | K.OwnDamage,
        ["harmonic_shield"] = K.None,
        ["dissonant_wave"] = K.Area | K.OwnDamage,
        ["resonance_cascade"] = K.Area | K.OwnDamage,
        ["tidal_harmony"] = K.None,
        ["oceans_voice"] = K.None,
        ["harmonic_crescendo"] = K.Area | K.OwnDamage,
        ["temporal_feint"] = K.None,
        ["borrowed_power"] = K.None,
        ["fracture_reality"] = K.OwnDamage,
        ["quantum_state"] = K.None,
        ["entropy_cascade"] = K.Area | K.OwnDamage,
        ["timeline_split"] = K.None,
        ["causality_loop"] = K.None,
        ["chrono_surge"] = K.None,
        ["singularity"] = K.Area | K.OwnDamage,
        ["temporal_prison"] = K.None,
        ["cycles_end"] = K.OwnDamage,
        ["shadow_harvest"] = K.None,
        ["corrupting_touch"] = K.None,
        ["umbral_step"] = K.None,
        ["wardens_authority"] = K.None,
        ["seal_fracture"] = K.Spread | K.OwnDamage,
        ["soul_leech"] = K.OwnDamage,
        ["abyssal_eruption"] = K.Area | K.OwnDamage,
        ["dark_pact"] = K.None,
        ["prison_wardens_command"] = K.None,
        ["consume_soul"] = K.OwnDamage,
        ["abyss_unchained"] = K.Area | K.OwnDamage,
        ["hungering_strike"] = K.None,
        ["offer_flesh"] = K.None,
        ["reap"] = K.OwnDamage | K.Execute,
        ["void_shroud"] = K.None,
        ["apotheosis_of_ruin"] = K.None,
        ["devour"] = K.OwnDamage,
        ["entropic_blade"] = K.OwnDamage,
        ["blood_frenzy"] = K.None,
        ["void_rupture"] = K.Area | K.OwnDamage,
        ["deaths_embrace"] = K.None,
        ["annihilation"] = K.OwnDamage,
    };

    [Fact]
    public void AbilityKinds136_EveryAbility_HasTheKindsOfTheList()
    {
        var all = ClassAbilitySystem.GetAllAbilities();
        all.Select(a => a.Id).Should().BeEquivalentTo(Kinds.Keys, "every ability is in the list, so a new one cannot fall through");
        foreach (var a in all)
            CombatEngine.AbilityKinds(a).Should().Be(Kinds[a.Id], a.Id);
        // the result of a use reads the same kinds
        foreach (var a in all)
            CombatEngine.AbilityKinds(new ClassAbilityResult { AbilityUsed = a, SpecialEffect = a.SpecialEffect }).Should().Be(Kinds[a.Id], a.Id);
    }

    // ---- rule 3: the quickbar target prompt ----

    [Fact]
    public void AbilityKinds136_AreaAbilities_TakeNoTarget_SealFracture_AsksForOne()
    {
        foreach (var id in new[] { "maelstrom_faithful", "sanctified_torrent", "resonance_cascade", "harmonic_crescendo", "singularity",
                     "abyssal_eruption", "abyss_unchained", "void_rupture", "whirlwind", "death_blossom", "grand_finale",
                     "dissonant_wave", "corrosive_cloud", "thundering_roar" })
            CombatEngine.QuickbarAbilityAsksForTarget(Ability(id)).Should().BeFalse($"{id} is an area ability");
        foreach (var id in new[] { "seal_fracture", "power_strike", "throw_bomb", "assassinate", "cutting_words", "acid_splash", "death_mark" })
            CombatEngine.QuickbarAbilityAsksForTarget(Ability(id)).Should().BeTrue($"{id} hits one target");
        CombatEngine.QuickbarAbilityAsksForTarget(Ability("shield_wall")).Should().BeFalse("a defense takes no monster target");
    }

    // ---- ruling 2: an area ability that treats its chosen target differently keeps the prompt ----

    [Fact]
    public void AbilityKinds136_ChainLightning_KeepsTheTargetPrompt_TheOtherAreaAbilitiesDoNot()
    {
        CombatEngine.QuickbarAbilityAsksForTarget(Ability("chain_lightning")).Should().BeTrue("its chosen target takes the full hit, the rest half");
        foreach (var a in ClassAbilitySystem.GetAllAbilities().Where(a => (Kinds[a.Id] & K.Area) != 0 && a.Id != "chain_lightning"))
            CombatEngine.QuickbarAbilityAsksForTarget(a).Should().BeFalse($"{a.Id} hits every foe the same");
        ClassAbilitySystem.GetAllAbilities().Where(a => (Kinds[a.Id] & K.Primary) != 0).Select(a => a.Id).Should().Equal("chain_lightning");
    }

    [Fact]
    public void AbilityKinds136_ChainLightning_HitsItsChosenTargetInFull_TheOtherHalf()
    {
        var first = Foe();
        var chosen = Foe();
        Use("chain_lightning", 20, Hero(dualWield: false), chosen, first);
        (chosen.MaxHP - chosen.HP).Should().Be(D, "the chosen target takes the full hit");
        (first.MaxHP - first.HP).Should().Be(D / 2, "the other takes half");
    }

    // ---- rule 4: the teammate picks ----

    [Fact]
    public void AbilityKinds136_TeammateAreaPick_TakesChainLightning_NotThrowBomb()
    {
        CombatEngine.TeammateAreaPick(new[] { Ability("lightning_bolt"), Ability("chain_lightning") }).Should().BeSameAs(Ability("chain_lightning"));
        CombatEngine.TeammateAreaPick(new[] { Ability("throw_bomb") }).Should().BeNull("Throw Bomb hits one target");
        CombatEngine.TeammateAreaPick(new[] { Ability("throw_bomb"), Ability("explosive_flask") }).Should().BeSameAs(Ability("explosive_flask"));
        foreach (var a in ClassAbilitySystem.GetAllAbilities().Where(a => (Kinds[a.Id] & K.Area) != 0))
            CombatEngine.TeammateAreaPick(new[] { Ability("power_strike"), a }).Should().BeSameAs(a, $"{a.Id} is an area ability");
    }

    [Fact]
    public void AbilityKinds136_TeammateExecutePick_TakesExecuteAssassinateAndReap()
    {
        foreach (var id in new[] { "execute", "assassinate", "reap" })
            CombatEngine.IsTeammateExecutePick(Ability(id)).Should().BeTrue(id);
        foreach (var id in new[] { "power_strike", "death_blossom", "throw_bomb", "wrath_of_the_deep", "annihilation" })
            CombatEngine.IsTeammateExecutePick(Ability(id)).Should().BeFalse(id);
        CombatEngine.IsTeammateExecutePick(null).Should().BeFalse();
    }

    // ---- the live ability path: rolls, procs, Thorns and the off-hand swing ----

    /// <summary>Every twenty-sided die (Next(1, 21)) shows Face and is counted.</summary>
    private sealed class DieRandom : Random
    {
        public readonly int Face;
        public int Dice;
        public DieRandom(int face) : base(136) { Face = face; }
        public override int Next(int minValue, int maxValue)
        {
            if (minValue == 1 && maxValue == 21) { Dice++; return Face; }
            return base.Next(minValue, maxValue);
        }
    }

    private sealed class Outcome
    {
        public long Dealt;
        public long HeroHpChange;
        public int Dice;
        public bool OffHand;
    }

    private static ClassAbilitySystem.ClassAbility Ability(string id)
    {
        var a = ClassAbilitySystem.GetAbility(id);
        a.Should().NotBeNull(id);
        return a!;
    }

    private static int Blade(int lifeSteal) => EquipmentDatabase.RegisterDynamic(new Equipment
    {
        Name = lifeSteal > 0 ? "AK136 Leech Blade" : "AK136 Plain Blade", Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Sword,
        Handedness = WeaponHandedness.OneHanded, WeaponPower = 40, MinLevel = 1, Rarity = EquipmentRarity.Common, LifeSteal = lifeSteal,
    });

    private static Character Hero(bool dualWield, int lifeSteal = 0)
    {
        var hero = G.Hero(50_000, 300, 50, CharacterClass.Warrior, 30, main: 40, off: dualWield ? 35 : null);
        hero.EquippedItems[EquipmentSlot.MainHand] = Blade(lifeSteal);
        hero.RecalculateStats();
        hero.HP = hero.MaxHP / 2;
        return hero;
    }

    private static Monster Foe(bool thorns = false)
    {
        var m = G.Dummy(50, 0);
        if (thorns) { m.ThornsRounds = 3; m.ThornsPercent = 25; }
        return m;
    }

    /// <summary>One use of the ability through the live path (ApplyAbilityEffectsMultiMonster), the to-hit roll on,
    /// every die showing `face`; returns what the first foe lost, the hero's HP change, the dice and the off-hand row.</summary>
    private static Outcome Use(string id, int face, Character hero, params Monster[] foes)
    {
        bool savedRoll = GameConfig.PlayerHitRollEnabled;
        string savedLang = GameConfig.Language;
        try
        {
            GameConfig.PlayerHitRollEnabled = true;
            GameConfig.Language = "en";
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            var engine = new CombatEngine(term);
            var random = new DieRandom(face);
            typeof(CombatEngine).GetField("random", F)!.SetValue(engine, random);
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
            typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
            var result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>() };
            foreach (var m in foes) result.Monsters.Add(m);
            var ability = Ability(id);
            var use = new ClassAbilityResult { Success = true, AbilityUsed = ability, Damage = D, SpecialEffect = ability.SpecialEffect, Duration = 2 };
            long foeBefore = foes[0].HP, heroBefore = hero.HP;
            ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                .Invoke(engine, new object?[] { hero, foes[0], new List<Monster>(foes), use, result })!).GetAwaiter().GetResult();
            term.StreamWriterInternal?.Flush();
            string text = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            string offHandRow = Loc.Get("combat.off_hand_strike_at", MonsterNames.Display(foes[0]));
            return new Outcome
            {
                Dealt = foeBefore - foes[0].HP, HeroHpChange = hero.HP - heroBefore, Dice = random.Dice,
                OffHand = text.Contains(offHandRow, StringComparison.Ordinal),
            };
        }
        finally
        {
            GameConfig.PlayerHitRollEnabled = savedRoll;
            GameConfig.Language = savedLang;
        }
    }

    private static readonly string[] NotMelee = { "acid_splash", "frost_bomb", "explosive_flask", "philosophers_bomb", "cataclysm_bomb", "cutting_words" };

    [Fact]
    public void AbilityKinds136_AMeleeAbility_StillRolls_Procs_TakesThorns_AndGivesTheOffHandSwing()
    {
        var missed = Use("power_strike", 1, Hero(dualWield: true), Foe());
        missed.Dice.Should().BeGreaterThan(0, "Power Strike rolls");
        missed.Dealt.Should().Be(0, "a natural 1 misses: the main hand and the off hand");
        var leech = Use("power_strike", 20, Hero(dualWield: true, lifeSteal: 50), Foe());
        leech.Dealt.Should().BeGreaterThan(0);
        leech.HeroHpChange.Should().BeGreaterThan(0, "Lifedrinker on the weapon heals");
        leech.OffHand.Should().BeTrue("a dual wielder's off hand follows a melee ability");
        var thorned = Use("power_strike", 20, Hero(dualWield: false), Foe(thorns: true));
        thorned.HeroHpChange.Should().BeLessThan(0, "a melee hit on Thorns wounds the attacker");
    }

    [Theory]
    [InlineData("acid_splash")]
    [InlineData("frost_bomb")]
    [InlineData("explosive_flask")]
    [InlineData("philosophers_bomb")]
    [InlineData("cataclysm_bomb")]
    [InlineData("cutting_words")]
    [InlineData("throw_bomb")]
    public void AbilityKinds136_ABombAndCuttingWords_DoNotMiss_ProcNothing_TakeNoThorns_AndGiveNoOffHandSwing(string id)
    {
        var shot = Use(id, 1, Hero(dualWield: true, lifeSteal: 50), Foe(), Foe());
        shot.Dice.Should().Be(0, $"{id} draws no hit die");
        shot.Dealt.Should().BeGreaterThan(0, $"{id} cannot miss");
        shot.HeroHpChange.Should().Be(0, $"{id} sets off no Lifedrinker");
        shot.OffHand.Should().BeFalse($"{id} gives no off-hand swing");
        var thorned = Use(id, 20, Hero(dualWield: false), Foe(thorns: true), Foe(thorns: true));
        thorned.Dealt.Should().BeGreaterThan(0);
        thorned.HeroHpChange.Should().Be(0, $"{id} is not a melee hit, so Thorns does not answer it");
    }

    // ---- rule 5 (Q19): no off-hand swing after an area ability ----

    [Theory]
    [InlineData("death_blossom")]       // effect execute_all
    [InlineData("grand_finale")]        // effect grand_finale_jester
    [InlineData("chain_lightning")]
    [InlineData("maelstrom_faithful")]
    [InlineData("void_rupture")]
    [InlineData("whirlwind")]
    public void AbilityKinds136_AreaAbilities_GiveNoOffHandSwing(string id)
    {
        var used = Use(id, 20, Hero(dualWield: true), Foe(), Foe());
        used.Dealt.Should().BeGreaterThan(0);
        used.OffHand.Should().BeFalse($"{id} is an area ability");
        CombatEngine.AbilityGivesOffHandSwing(CombatEngine.AbilityKinds(Ability(id))).Should().BeFalse(id);
    }

    [Fact]
    public void AbilityKinds136_OffHandSwing_FollowsOnlyTheAbilitiesOfNoAreaSpreadMindOrThrownKind()
    {
        foreach (var a in ClassAbilitySystem.GetAllAbilities())
        {
            bool expected = (Kinds[a.Id] & (K.Area | K.Spread | K.Mind | K.NotMelee)) == 0;
            CombatEngine.AbilityGivesOffHandSwing(CombatEngine.AbilityKinds(a)).Should().Be(expected, a.Id);
        }
        CombatEngine.AbilityGivesOffHandSwing(CombatEngine.AbilityKinds(Ability("seal_fracture"))).Should().BeFalse("its spread is an area hit");
    }

    // ---- rule 2: Resonance Cascade and Harmonic Crescendo hit each target once ----

    [Theory]
    [InlineData("resonance_cascade", 1.0, 1.25)]   // +25 percent per additional enemy
    [InlineData("harmonic_crescendo", 1.0, 1.0)]   // no buffs to consume
    public void AbilityKinds136_ResonanceCascadeAndHarmonicCrescendo_HitEachTargetOnce(string id, double oneShare, double twoShare)
    {
        var alone = Foe();
        Use(id, 20, Hero(dualWield: false), alone);
        (alone.MaxHP - alone.HP).Should().Be((long)(D * oneShare), "one target takes D once");
        var a = Foe();
        var b = Foe();
        Use(id, 20, Hero(dualWield: false), a, b);
        (a.MaxHP - a.HP).Should().Be((long)(D * twoShare), "the first of two takes its share once");
        (b.MaxHP - b.HP).Should().Be((long)(D * twoShare), "the second of two takes its share once");
    }

    // ---- rule 2, the roll half: Resonance Cascade and Harmonic Crescendo never miss as a whole ----

    [Theory]
    [InlineData("resonance_cascade")]
    [InlineData("harmonic_crescendo")]
    public void AbilityKinds136_ResonanceCascadeAndHarmonicCrescendo_DrawNoHitDie_AndAlwaysLand(string id)
    {
        var a = Foe();
        var b = Foe();
        var used = Use(id, 1, Hero(dualWield: false), a, b);
        used.Dice.Should().Be(0, $"{id} is an area ability: no roll");
        used.Dealt.Should().BeGreaterThan(0, $"{id} cannot miss as a whole");
        (b.MaxHP - b.HP).Should().BeGreaterThan(0, "the second foe is hit too");
    }
}
