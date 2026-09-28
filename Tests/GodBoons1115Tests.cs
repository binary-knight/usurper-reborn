using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 2, canon boons: one distinct boon per canon god, scaled by the
/// follower's tier (Follower 1/3, Devout 2/3, Zealot and Chosen full), applied where it belongs,
/// and each god's Mental ward at Devout and up, applied inside MentalSystem.
/// Pure boon tests pass their own GodSystem; the call-site tests (MentalSystem, RecalculateStats,
/// the spell and heal helpers) read the shared singleton and clear their worship in finally.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodBoons1115Tests
{
    private static Character Hero(string name) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 10, HP = 100, MaxHP = 100,
        BaseMaxHP = 100, BaseStrength = 10, BaseDexterity = 10, BaseConstitution = 10, BaseIntelligence = 20,
        BaseWisdom = 20, BaseCharisma = 10, BaseDefence = 5, BaseStamina = 10, BaseAgility = 10,
        Mental = 80, Class = CharacterClass.Warrior, Race = CharacterRace.Human
    };

    private static (Character c, GodSystem gods) Follower(string name, string god, int favor)
    {
        var gods = new GodSystem();
        var c = Hero(name);
        GodRegistry.SetWorshippedGod(c, god, gods).Should().BeTrue();
        c.GodFavor = favor;
        return (c, gods);
    }

    /// <summary>Worship through the shared singleton (for call sites without a GodSystem parameter), cleared after.</summary>
    private static T WithSingletonGod<T>(Character c, string god, int favor, Func<T> body)
    {
        GodRegistry.SetWorshippedGod(c, god).Should().BeTrue();
        c.GodFavor = favor;
        try { return body(); }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    private static Monster Foe(string name, MonsterClass cls = MonsterClass.Normal) =>
        new Monster { Name = name, Level = 5, HP = 100, MaxHP = 100, IsActive = true, MonsterClass = cls };

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    /// <summary>The text of a method from its signature to the next member at the same indent.</summary>
    private static string Body(string file, string signature)
    {
        string src = Source(file);
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{signature} must exist in {file}");
        var next = new Regex(@"\n    (private|public|internal|protected) ", RegexOptions.Compiled).Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    // ---------------- Domains and tiers ----------------

    [Fact]
    public void EachCanonGod_HasItsOwnDomain_AndNoOtherGodDoes()
    {
        var domains = GameConfig.CanonGodNames.Select(GodBoonSystem.DomainOfCanon).ToList();
        domains.Should().Equal(GodDomain.Light, GodDomain.War, GodDomain.Love, GodDomain.Law, GodDomain.Shadow,
            GodDomain.Earth, GodDomain.Death, GodDomain.Magic, GodDomain.Nature, GodDomain.Chaos);
        GodBoonSystem.DomainOfCanon("Manwe").Should().Be(GodDomain.None);
        GodBoonSystem.DomainOfCanon("SomePlayerGod").Should().Be(GodDomain.None);
        foreach (var d in GodBoonSystem.AllDomains) GodBoonSystem.DomainOfCanon(GodBoonSystem.CanonGodOf(d)).Should().Be(d);
    }

    [Theory]
    [InlineData(0, 33)]
    [InlineData(24, 33)]
    [InlineData(25, 67)]
    [InlineData(49, 67)]
    [InlineData(50, 100)]
    [InlineData(75, 100)]
    [InlineData(100, 100)]
    public void Strength_IsOneThird_TwoThirds_ThenFull_ByTier(int favor, int expected)
    {
        var (c, gods) = Follower("GbTier", "Arcanus", favor);
        GodBoonSystem.GetStrengthPct(c, gods).Should().Be(expected);
    }

    [Fact]
    public void Boon_OnlyForTheGodsOwnDomain_NeverForNpcsOrTheGodless()
    {
        var (c, gods) = Follower("GbOwn", "Umbrath", 60);
        GodBoonSystem.Pct(c, GodDomain.Shadow, 10, gods).Should().Be(10);
        GodBoonSystem.Pct(c, GodDomain.Light, 10, gods).Should().Be(0);
        var godless = Hero("GbNone");
        GodBoonSystem.Pct(godless, GodDomain.Shadow, 10, new GodSystem()).Should().Be(0);
        var npc = new Character { Name1 = "GbNpc", Name2 = "GbNpc", AI = CharacterAI.Computer };
        var npcGods = new GodSystem();
        npcGods.SetPlayerGod("GbNpc", "Umbrath");
        GodBoonSystem.Pct(npc, GodDomain.Shadow, 10, npcGods).Should().Be(0);
    }

    [Theory]
    [InlineData(100, 10.0, 10)]
    [InlineData(3, 10.0, 1)]      // 0.3 rounds to 0, the floor of 1 holds
    [InlineData(25, 10.0, 3)]     // 2.5 rounds half up
    [InlineData(24, 10.0, 2)]
    [InlineData(0, 10.0, 0)]
    [InlineData(100, 0.0, 0)]
    public void Bonus_RoundsHalfUp_AtLeastOneWhileActive(long value, double pct, long expected) =>
        GodBoonSystem.Bonus(value, pct).Should().Be(expected);

    // ---------------- Combat boons ----------------

    [Fact]
    public void Solarius_AddsDamageAgainstUndeadAndDemons_Only()
    {
        var (c, gods) = Follower("GbSol", "Solarius", 60);
        var dbs = new DivineBlessingSystem();
        dbs.CalculateBonusDamage(c, Foe("Skeleton Knight"), 1000, gods).Should().Be(150);
        dbs.CalculateBonusDamage(c, Foe("Pit Lord", MonsterClass.Demon), 1000, gods).Should().Be(150);
        dbs.CalculateBonusDamage(c, Foe("Goblin"), 1000, gods).Should().Be(0);
        var (f, fGods) = Follower("GbSolF", "Solarius", 0);
        dbs.CalculateBonusDamage(f, Foe("Zombie"), 1000, fGods).Should().Be(50, "a Follower gets a third: 15 x 33% = 4.95%");
    }

    [Fact]
    public void Valorian_AddsDamageBelowHalfHp_Only()
    {
        var (c, gods) = Follower("GbVal", "Valorian", 60);
        var dbs = new DivineBlessingSystem();
        c.HP = 49; c.MaxHP = 100;
        dbs.CalculateBonusDamage(c, Foe("Goblin"), 1000, gods).Should().Be(100);
        c.HP = 50;
        dbs.CalculateBonusDamage(c, Foe("Goblin"), 1000, gods).Should().Be(0);
    }

    [Fact]
    public void Judicar_ReducesDamageTaken()
    {
        var (c, gods) = Follower("GbJud", "Judicar", 60);
        new DivineBlessingSystem().CalculateDamageReduction(c, 1000, gods).Should().Be(100);
        var (o, oGods) = Follower("GbJudO", "Amara", 60);
        new DivineBlessingSystem().CalculateDamageReduction(o, 1000, oGods).Should().Be(0);
    }

    [Fact]
    public void Umbrath_AddsCriticalChance_ByTier()
    {
        var dbs = new DivineBlessingSystem();
        var (z, zGods) = Follower("GbUmbZ", "Umbrath", 60);
        dbs.GetCriticalHitBonus(z, zGods).Should().Be(10);
        var (f, fGods) = Follower("GbUmbF", "Umbrath", 0);
        dbs.GetCriticalHitBonus(f, fGods).Should().Be(3);
        var (o, oGods) = Follower("GbUmbO", "Valorian", 60);
        dbs.GetCriticalHitBonus(o, oGods).Should().Be(0, "the old table gave every dark or balanced god crit; now only Umbrath");
    }

    [Fact]
    public void Arcanus_AddsSpellDamage_InTheSpellScaling()
    {
        var method = typeof(SpellSystem).GetMethod("ScaleSpellEffect", BindingFlags.NonPublic | BindingFlags.Static)!;
        var c = Hero("GbArcSpell");
        int plain = (int)method.Invoke(null, new object[] { 1000, c, new Random(7), 1.0f })!;
        int blessed = WithSingletonGod(c, "Arcanus", 60, () => (int)method.Invoke(null, new object[] { 1000, c, new Random(7), 1.0f })!);
        blessed.Should().Be((int)(plain + GodBoonSystem.Bonus(plain, 10)));
    }

    [Fact]
    public void Arcanus_ManaRegen_AndItIsWiredIntoTheCombatRound()
    {
        var (c, gods) = Follower("GbArcMana", "Arcanus", 60);
        GodBoonSystem.ManaRegen(c, 30, gods).Should().Be(33);
        GodBoonSystem.ManaRegen(c, 3, gods).Should().Be(4, "at least one while active");
        Body("Scripts/Systems/CombatEngine.cs", "private void ProcessEndOfRoundAbilityEffects(")
            .Should().Contain("GodBoonSystem.ManaRegen(player, StatEffectsSystem.GetManaRegenPerRound(player.Wisdom))");
    }

    [Fact]
    public void Amara_BoostsTheHealsAFollowerCasts()
    {
        var method = typeof(CombatEngine).GetMethod("ApplyHealerSpecBonus", BindingFlags.NonPublic | BindingFlags.Static)!;
        var c = Hero("GbAmaHeal");
        ((int)method.Invoke(null, new object[] { c, 1000 })!).Should().Be(1000);
        WithSingletonGod(c, "Amara", 60, () => (int)method.Invoke(null, new object[] { c, 1000 })!).Should().Be(1150);
    }

    [Fact]
    public void Amara_BoostsThePartyWardAFollowerRaises()
    {
        var (c, gods) = Follower("GbAmaWard", "Amara", 60);
        GodBoonSystem.PartyWard(c, 100, gods).Should().Be(115);
        Body("Scripts/Systems/CombatEngine.cs", "private void WardPartyFromHeal(Character caster, List<Character> party")
            .Should().Contain("GodBoonSystem.PartyWard(caster, SageWardStrength(caster, spellResult.ProtectionBonus))");
    }

    [Fact]
    public void Discordia_PvpDamage_AndItIsWiredIntoThePvpHit()
    {
        var (c, gods) = Follower("GbDisPvp", "Discordia", 60);
        GodBoonSystem.PvpDamage(c, 1000, gods).Should().Be(1100);
        Body("Scripts/Systems/CombatEngine.cs", "private async Task ExecutePvPSingleHit(")
            .Should().Contain("attackPower = GodBoonSystem.PvpDamage(attacker, attackPower);");
    }

    [Fact]
    public void Discordia_FirstActionFails_RolledAtStart_ConsumedOnTheMonstersTurn()
    {
        var (c, gods) = Follower("GbDisFirst", "Discordia", 60);
        GodBoonSystem.DiscordiaFirstActionFailPct(c, gods).Should().Be(15);
        var (o, oGods) = Follower("GbDisFirstO", "Mortis", 60);
        GodBoonSystem.DiscordiaFirstActionFailPct(o, oGods).Should().Be(0);
        Body("Scripts/Systems/CombatEngine.cs", "public async Task<CombatResult> PlayerVsMonsters(")
            .Should().Contain("RollDiscordiaFirstActionFail(player, monsters);");
        Body("Scripts/Systems/CombatEngine.cs", "private async Task ProcessMonsterAction(")
            .Should().Contain("if (ConsumeDiscordiaFail(monster))");
    }

    [Fact]
    public void Discordia_StruckFoe_LosesExactlyOneAction()
    {
        var engine = new CombatEngine();
        var hero = Hero("GbDisRoll");
        var foe = Foe("Goblin");
        WithSingletonGod(hero, "Discordia", 60, () =>
        {
            bool struck = false;
            for (int i = 0; i < 200 && !struck; i++)
            {
                engine.RollDiscordiaFirstActionFail(hero, new[] { foe });
                struck = engine.ConsumeDiscordiaFail(foe);
            }
            struck.Should().BeTrue("15% a roll over 200 rolls");
            engine.ConsumeDiscordiaFail(foe).Should().BeFalse("only the first action");
            return 0;
        });
        for (int i = 0; i < 200; i++)
        {
            engine.RollDiscordiaFirstActionFail(hero, new[] { foe });
            engine.ConsumeDiscordiaFail(foe).Should().BeFalse("no Discordia, no roll");
        }
    }

    // ---------------- Boons outside combat ----------------

    [Fact]
    public void Terran_MaxHp_ThroughRecalculateStats_AndNeverStoredOnTheBase()
    {
        var c = Hero("GbTerHp");
        c.RecalculateStats();
        long plain = c.MaxHP;
        long blessed = WithSingletonGod(c, "Terran", 60, () => { c.RecalculateStats(); c.RecalculateStats(); return c.MaxHP; });
        blessed.Should().Be(plain + GodBoonSystem.Bonus(plain, 10));
        c.BaseMaxHP.Should().Be(100);
        c.RecalculateStats();
        c.MaxHP.Should().Be(plain, "the bonus goes with the god");
    }

    [Fact]
    public void Terran_Yields_GardenAndSettlement()
    {
        var (c, gods) = Follower("GbTerYield", "Terran", 60);
        GodBoonSystem.EarthYield(c, 5, gods).Should().Be(6);
        GodBoonSystem.EarthYield(c, 1000, gods).Should().Be(1200);
        Body("Scripts/Locations/HomeLocation.cs", "private async Task GatherHerbs(")
            .Should().Contain("GodBoonSystem.EarthYield(currentPlayer, GameConfig.HerbsPerDay[gardenLevel])");
        Source("Scripts/Locations/SettlementLocation.cs").Should().Contain("share = GodBoonSystem.EarthYield(currentPlayer, share);");
    }

    [Fact]
    public void Judicar_Bounty_InBothPayoutsAndTheDuelMessage()
    {
        var (c, gods) = Follower("GbJudBounty", "Judicar", 60);
        GodBoonSystem.BountyReward(c, 1000, gods).Should().Be(1200);
        var quest = Source("Scripts/Systems/QuestSystem.cs");
        quest.Should().Contain("reward = GodBoonSystem.BountyReward(winner, reward);");
        quest.Should().Contain("reward = GodBoonSystem.BountyReward(player, reward);");
        Source("Scripts/Systems/CombatEngine.cs").Should().Contain("GodBoonSystem.BountyReward(result.Player, QuestSystem.BountyReward(q))");
        Source("Scripts/Locations/ArenaLocation.cs").Should().Contain("bountyReward = GodBoonSystem.BountyReward(currentPlayer, bountyReward);");
    }

    [Fact]
    public void Umbrath_Theft_RaisesTheChanceAndItsCap()
    {
        var (c, gods) = Follower("GbUmbTheft", "Umbrath", 60);
        GodBoonSystem.TheftChanceBonusPct(c, gods).Should().Be(10);
        var alley = Body("Scripts/Locations/DarkAlleyLocation.cs", "private async Task VisitPickpocket(");
        alley.Should().Contain("GodBoonSystem.TheftChanceBonusPct(currentPlayer)");
        alley.Should().Contain("Math.Min(0.75f + umbrath,");
    }

    [Fact]
    public void Mortis_DeathGoldLoss_ReducedInBothDeathPaths()
    {
        var (c, gods) = Follower("GbMorGold", "Mortis", 60);
        GodBoonSystem.DeathGoldLoss(c, 1000, gods).Should().Be(750);
        var (o, oGods) = Follower("GbMorGoldO", "Terran", 60);
        GodBoonSystem.DeathGoldLoss(o, 1000, oGods).Should().Be(1000);
        Body("Scripts/Systems/CombatEngine.cs", "private async Task ApplyDeathPenalties(")
            .Should().Contain("goldLoss = GodBoonSystem.DeathGoldLoss(player, goldLoss);");
        Source("Scripts/Systems/CombatEngine.cs").Should().Contain("long goldLost = GodBoonSystem.DeathGoldLoss(player, (long)(player.Gold * 0.75));");
    }

    [Fact]
    public void Sylvana_DoublesWildernessGains_AtFull()
    {
        var (c, gods) = Follower("GbSylWild", "Sylvana", 60);
        GodBoonSystem.WildernessGain(c, 500, gods).Should().Be(1000);
        var (f, fGods) = Follower("GbSylWildF", "Sylvana", 0);
        GodBoonSystem.WildernessGain(f, 300, fGods).Should().Be(399);
        Regex.Matches(Source("Scripts/Locations/WildernessLocation.cs"), Regex.Escape("GodBoonSystem.WildernessGain(currentPlayer, ")).Count
            .Should().Be(6, "the victory gold, three foraging finds, the ruins treasure and the shrine XP");
    }

    // ---------------- Prayer blessing ----------------

    [Theory]
    [InlineData(GodFavorTier.Follower, 120, 20)]
    [InlineData(GodFavorTier.Devout, 120, 20)]
    [InlineData(GodFavorTier.Zealot, 240, 40)]
    [InlineData(GodFavorTier.Chosen, 240, 40)]
    public void PrayerBlessing_LastsTwiceAsLong_AtZealotAndUp(GodFavorTier tier, int minutes, int combats)
    {
        DivineBlessingSystem.PrayerBlessingMinutes(tier, 1.0f).Should().Be(minutes);
        DivineBlessingSystem.PrayerBlessingCombats(tier).Should().Be(combats);
    }

    [Fact]
    public void PrayerBlessing_BothKindsOfGodUseTheTierRule()
    {
        Body("Scripts/Systems/DivineBlessingSystem.cs", "public TemporaryBlessing? GrantPrayerBlessing(")
            .Should().Contain("PrayerBlessingMinutes(FavorSystem.GetTier(FavorSystem.GetFavor(character)), prayerMultiplier)");
        Source("Scripts/Locations/TempleLocation.cs")
            .Should().Contain("int prayerCombats = DivineBlessingSystem.PrayerBlessingCombats(FavorSystem.GetTier(FavorSystem.GetFavor(currentPlayer)));");
    }

    [Fact]
    public void TemporaryBlessings_StillAddToDamageDefenceAndXp()
    {
        var (c, gods) = Follower("GbTemp", "Valorian", 60);
        GodRegistry.SetWorshippedGod(c, "Valorian").Should().BeTrue();
        try
        {
            var dbs = new DivineBlessingSystem();
            var t = dbs.GrantSacrificeBlessing(c, 10_000, "Valorian");
            t.Should().NotBeNull();
            var b = dbs.GetBlessings(c, gods);
            b.HasTemporaryBlessing.Should().BeTrue();
            (b.TemporaryDamageBonus + b.TemporaryDefenseBonus + b.TemporaryXPBonus).Should().BeGreaterThan(0);
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    // ---------------- The old table is gone ----------------

    [Fact]
    public void TheOldBlessingTable_DeadFieldsAndSpecialAbilities_AreRemoved()
    {
        typeof(DivineBlessing).GetProperties().Select(p => p.Name).OrderBy(n => n).Should().Equal(new[]
        {
            "Domain", "GodName", "HasTemporaryBlessing", "IsActive", "StrengthPct", "TemporaryBlessingExpires",
            "TemporaryBlessingName", "TemporaryDamageBonus", "TemporaryDefenseBonus", "TemporaryXPBonus"
        }.OrderBy(n => n));
        var src = Source("Scripts/Systems/DivineBlessingSystem.cs");
        src.Should().NotContain("SpecialAbility");
        src.Should().NotContain("CheckDivineIntervention");
        src.Should().NotContain("CalculateLifesteal");
        typeof(BlessingTypeProbe).Assembly.GetType("UsurperRemake.Systems.BlessingType").Should().BeNull();
    }

    private sealed class BlessingTypeProbe { }

    // ---------------- Mental wards ----------------

    [Fact]
    public void Wards_EachGodHasOne_FromItsDomain()
    {
        GodBoonSystem.WardsOf(GodDomain.Love).Should().Equal(MentalWard.Grief);
        GodBoonSystem.WardsOf(GodDomain.Death).Should().Equal(MentalWard.Death, MentalWard.Witness);
        GodBoonSystem.WardsOf(GodDomain.Nature).Should().Equal(MentalWard.Strain);
        GodBoonSystem.WardsOf(GodDomain.Light).Should().Equal(MentalWard.Boss);
        GodBoonSystem.WardsOf(GodDomain.War).Should().Equal(MentalWard.NearDeath);
        GodBoonSystem.WardsOf(GodDomain.Law).Should().Equal(MentalWard.Withdrawal);
        GodBoonSystem.WardsOf(GodDomain.Shadow).Should().Equal(MentalWard.Flee);
        GodBoonSystem.WardsOf(GodDomain.Earth).Should().Equal(MentalWard.DrugCrash);
        GodBoonSystem.WardsOf(GodDomain.Magic).Should().Equal(MentalWard.OldGod);
        GodBoonSystem.WardsOf(GodDomain.Chaos).Should().Equal(MentalWard.Fear);
    }

    [Fact]
    public void Ward_OnlyAtDevoutAndUp()
    {
        var (f, fGods) = Follower("GbWardF", "Amara", 24);
        GodBoonSystem.HasWard(f, MentalWard.Grief, fGods).Should().BeFalse();
        var (d, dGods) = Follower("GbWardD", "Amara", 25);
        GodBoonSystem.HasWard(d, MentalWard.Grief, dGods).Should().BeTrue();
        GodBoonSystem.HasWard(d, MentalWard.Death, dGods).Should().BeFalse("not Amara's ward");
    }

    private static int Loss(string god, int favor, Func<Character, int> apply)
    {
        var c = Hero("GbWardLoss" + god);
        c.Mental = 100;
        return WithSingletonGod(c, god, favor, () => -apply(c));
    }

    [Fact]
    public void Amara_Ward_HalvesGriefLosses()
    {
        int half(int v) => v - v / 2;
        Loss("Amara", 30, MentalSystem.ApplyCompanionGrief).Should().Be(half(GameConfig.MentalCompanionGriefLoss));
        Loss("Amara", 30, MentalSystem.ApplyNpcGrief).Should().Be(half(GameConfig.MentalNpcGriefLoss));
        Loss("Amara", 30, c => MentalSystem.ApplyGriefStage(c, GriefStage.Depression)).Should().Be(half(GameConfig.MentalGriefDepressionLoss));
        Loss("Amara", 10, MentalSystem.ApplyCompanionGrief).Should().Be(GameConfig.MentalCompanionGriefLoss, "Follower: no ward");
    }

    [Fact]
    public void Mortis_Ward_HalvesDeathAndWitnessLosses()
    {
        int half(int v) => v - v / 2;
        Loss("Mortis", 30, MentalSystem.ApplyDeath).Should().Be(half(GameConfig.MentalDeathLoss));
        Loss("Mortis", 30, MentalSystem.ApplyWitnessLoss).Should().Be(half(GameConfig.MentalWitnessLoss));
        Loss("Mortis", 30, c => MentalSystem.ApplyFightEnd(c, 0, 0, false, false, false, false, died: true))
            .Should().Be(half(GameConfig.MentalDeathLoss));
        Loss("Solarius", 30, MentalSystem.ApplyDeath).Should().Be(GameConfig.MentalDeathLoss);
    }

    [Fact]
    public void Sylvana_Ward_CutsDungeonStrainByTenPercent()
    {
        var plain = Hero("GbStrainPlain");
        var ward = Hero("GbStrainWard");
        MentalSystem.AddStrain(plain, 5000, 0);
        WithSingletonGod(ward, "Sylvana", 30, () => MentalSystem.AddStrain(ward, 5000, 0));
        long plainUnits = (long)(80 - plain.Mental) * MentalSystem.StrainUnitsPerPoint + plain.MentalStrainRemainder;
        long wardUnits = (long)(80 - ward.Mental) * MentalSystem.StrainUnitsPerPoint + ward.MentalStrainRemainder;
        wardUnits.Should().Be(plainUnits - plainUnits * GameConfig.GodWardSylvanaStrainCutPct / 100);
    }

    [Fact]
    public void OtherWards_BossOldGodNearDeathFleeWithdrawalCrashAndFear()
    {
        int half(int v) => v - v / 2;
        Loss("Solarius", 30, c => MentalSystem.ApplyFightEnd(c, 0, 0, false, false, true, false)).Should().Be(half(GameConfig.MentalBossLoss));
        Loss("Arcanus", 30, c => MentalSystem.ApplyFightEnd(c, 0, 0, false, false, true, true)).Should().Be(half(GameConfig.MentalOldGodLoss));
        Loss("Valorian", 30, c => MentalSystem.ApplyFightEnd(c, 0, 0, false, true, false, false)).Should().Be(half(GameConfig.MentalNearDeathLoss));
        Loss("Umbrath", 30, c => MentalSystem.ApplyFightEnd(c, 0, 0, true, false, false, false)).Should().Be(half(GameConfig.MentalFleeLoss));
        Loss("Judicar", 30, c => MentalSystem.ApplyWithdrawal(c, 2)).Should().Be(half(GameConfig.MentalWithdrawalLossPerSeverity * 2));
        Loss("Terran", 30, MentalSystem.ApplyOverdose).Should().Be(half(GameConfig.MentalOverdoseLoss));
        Loss("Terran", 30, c => { c.MentalDrugBoost = 10; c.MentalDrugUses = 1; return MentalSystem.ApplyDrugCrash(c); })
            .Should().Be(half(MentalSystem.GetDrugCrash(10, 1)));
        Loss("Umbrath", 30, c => MentalSystem.ApplyFightEnd(c, 0, 0, false, false, true, false)).Should().Be(GameConfig.MentalBossLoss, "not Umbrath's ward");
    }

    [Fact]
    public void Discordia_Ward_HalvesTheFearChance()
    {
        int Fears(Character c)
        {
            var rng = new Random(11);
            int n = 0;
            for (int i = 0; i < 4000; i++) if (MentalSystem.RollFear(c, rng)) n++;
            return n;
        }
        var plain = Hero("GbFearPlain"); plain.Mental = 5;
        var ward = Hero("GbFearWard"); ward.Mental = 5;
        int plainFears = Fears(plain);
        int wardFears = WithSingletonGod(ward, "Discordia", 30, () => Fears(ward));
        plainFears.Should().BeInRange(600, 1000, "20% of 4000");
        wardFears.Should().BeInRange(250, 550, "10% of 4000");
    }

    // ---------------- Display and Loc ----------------

    [Fact]
    public void Display_TempleListAndStatusSheet_ShowTheBoonAndWard()
    {
        var temple = Body("Scripts/Locations/TempleLocation.cs", "private void DisplayGodListCompact(");
        temple.Should().Contain("GodBoonSystem.DescribeBoon(boonDomain, 100)");
        temple.Should().Contain("GodBoonSystem.DescribeWard(boonDomain)");
        var status = Source("Scripts/Locations/BaseLocation.cs");
        status.Should().Contain("GodBoonSystem.DescribeBoon(boonDomain, strength), strength");
        status.Should().Contain("GodBoonSystem.DescribeWard(boonDomain)");
    }

    [Fact]
    public void Loc_EveryBoonAndWardKey_InAllFiveLanguages()
    {
        var keys = new[] { "god.boon_line", "god.ward_line", "god.ward_line_active", "combat.discordia_first_action_fails" }
            .Concat(GodBoonSystem.AllDomains.Select(d => "god.boon." + d.ToString().ToLowerInvariant()))
            .Concat(GodBoonSystem.AllDomains.Select(d => "god.ward." + d.ToString().ToLowerInvariant()))
            .ToList();
        foreach (var lang in new[] { "en", "es", "fr", "it", "hu" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")));
            foreach (var k in keys)
                doc.RootElement.TryGetProperty(k, out var v).Should().BeTrue($"{lang} has {k}");
        }
    }
}
