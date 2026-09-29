using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 5: the nine called Miracles in a monster fight. [M] shows only while
/// the day's Miracle is ready and of use; calling it spends the day's use and does its god's
/// work. Grouped followers each have their own. Not in PvP.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodMiraclesCombat1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>A Chosen follower of god, worshipping through the game's own god store (as combat reads it).</summary>
    private static Character Chosen(string name, string god, int favor = 80)
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 20, HP = 400, MaxHP = 400,
            Mana = 10, MaxMana = 200, BaseMaxHP = 400, BaseStrength = 30, BaseDexterity = 20, BaseConstitution = 20,
            BaseIntelligence = 20, BaseWisdom = 20, BaseCharisma = 10, BaseDefence = 5, BaseStamina = 10, BaseAgility = 10,
            Mental = 80, Class = CharacterClass.Warrior, Race = CharacterRace.Human, CombatSpeed = CombatSpeed.Instant
        };
        GodRegistry.SetWorshippedGod(c, god).Should().BeTrue();
        c.GodFavor = favor;
        FavorSystem.Bind(c);
        c.MaxHP = 400; c.HP = 400; c.MaxMana = 200; c.Mana = 10;
        c.MiracleUsedToday = false;
        return c;
    }

    private static Monster Foe(string name = "Ogre", bool boss = false, bool mini = false, MonsterClass cls = MonsterClass.Normal) =>
        new Monster { Name = name, Level = 20, HP = 5000, MaxHP = 5000, Strength = 50, Defence = 20, IsBoss = boss, IsMiniBoss = mini, MonsterClass = cls };

    private static (CombatEngine engine, MemoryStream output) Engine(Character actor, Random? rng = null)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output));
        if (rng != null) typeof(CombatEngine).GetField("random", F)!.SetValue(engine, rng);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, actor);
        return (engine, output);
    }

    private static string Text(CombatEngine engine, MemoryStream output)
    {
        var term = (TerminalEmulator)typeof(CombatEngine).GetField("terminal", F)!.GetValue(engine)!;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    private static void Execute(CombatEngine engine, Character actor, List<Monster> monsters, CombatResult result, int? target = null)
    {
        var action = new CombatAction { Type = CombatActionType.Miracle, TargetIndex = target };
        ((Task)typeof(CombatEngine).GetMethod("ExecuteMiracle", F)!
            .Invoke(engine, new object[] { actor, monsters, action, result })!).GetAwaiter().GetResult();
    }

    private static CombatResult Fight(Character leader, List<Monster> monsters, params Character[] mates) =>
        new CombatResult { Player = leader, Monsters = monsters, Teammates = mates.ToList() };

    // ---------------- When [M] is offered ----------------

    [Fact]
    public void TheMiracle_IsOfferedOnlyWhileReady_AndNeverBelowChosen_NorMortis()
    {
        var foes = new[] { Foe() };
        var c = Chosen("GmcOffer", "Judicar");
        MiracleSystem.OfferedInFight(c, foes, null, true).Should().Be(GodDomain.Law);
        c.MiracleUsedToday = true;
        MiracleSystem.OfferedInFight(c, foes, null, true).Should().Be(GodDomain.None, "used today");

        MiracleSystem.OfferedInFight(Chosen("GmcLow", "Judicar", 74), foes, null, true).Should().Be(GodDomain.None);
        MiracleSystem.OfferedInFight(Chosen("GmcMortis", "Mortis"), foes, null, true).Should().Be(GodDomain.None, "Mortis acts by itself");
        MiracleSystem.OfferedInFight(Chosen("GmcNoFoe", "Judicar"), new[] { new Monster { Name = "Dead", HP = 0, MaxHP = 10 } }, null, true)
            .Should().Be(GodDomain.None, "no living foe");
    }

    [Fact]
    public void EachMiracle_IsOfferedOnlyWhereItHasWorkToDo()
    {
        var ogre = new[] { Foe() };
        var ghoul = new[] { Foe(), Foe("Ghoul", cls: MonsterClass.Undead) };

        MiracleSystem.OfferedInFight(Chosen("GmcL1", "Solarius"), ogre, null, true).Should().Be(GodDomain.None, "no undead or demon");
        MiracleSystem.OfferedInFight(Chosen("GmcL2", "Solarius"), ghoul, null, true).Should().Be(GodDomain.Light);

        var earth = Chosen("GmcE", "Terran");
        MiracleSystem.OfferedInFight(earth, ogre, null, true).Should().Be(GodDomain.None, "already at full HP");
        earth.HP = 10;
        MiracleSystem.OfferedInFight(earth, ogre, null, true).Should().Be(GodDomain.Earth);

        var magic = Chosen("GmcM", "Arcanus");
        MiracleSystem.OfferedInFight(magic, ogre, null, true).Should().Be(GodDomain.Magic);
        magic.Mana = magic.MaxMana;
        MiracleSystem.OfferedInFight(magic, ogre, null, true).Should().Be(GodDomain.None, "mana already full");

        var love = Chosen("GmcLove", "Amara");
        var mate = new Character { Name1 = "GmcMate", Name2 = "GmcMate", HP = 400, MaxHP = 400 };
        MiracleSystem.OfferedInFight(love, ogre, new[] { mate }, true).Should().Be(GodDomain.None, "nobody hurt");
        mate.HP = 50;
        MiracleSystem.OfferedInFight(love, ogre, new[] { mate }, true).Should().Be(GodDomain.Love);

        MiracleSystem.OfferedInFight(Chosen("GmcS1", "Umbrath"), ogre, null, true).Should().Be(GodDomain.Shadow);
        MiracleSystem.OfferedInFight(Chosen("GmcS2", "Umbrath"), ogre, null, false).Should().Be(GodDomain.None, "no fleeing on Nightmare");

        MiracleSystem.OfferedInFight(Chosen("GmcW", "Valorian"), ogre, null, true).Should().Be(GodDomain.War);
        MiracleSystem.OfferedInFight(Chosen("GmcN", "Sylvana"), ogre, null, true).Should().Be(GodDomain.Nature);
        MiracleSystem.OfferedInFight(Chosen("GmcC", "Discordia"), ogre, null, true).Should().Be(GodDomain.Chaos);
    }

    [Fact]
    public void APlayerGodsFollower_IsOfferedTheImmortalsDomain()
    {
        var c = Chosen("GmcPg", "Zephyrine");
        GodBoonSystem.SetPlayerGodBoon(c, "Zephyrine", GodDomain.Chaos, 50);
        MiracleSystem.OfferedInFight(c, new[] { Foe() }, null, true).Should().Be(GodDomain.Chaos);
    }

    // ---------------- Each Miracle's work ----------------

    [Fact]
    public void Solarius_BanishesAnUndeadFoe_AndSpendsTheDay()
    {
        var c = Chosen("GmcBanish", "Solarius");
        var monsters = new List<Monster> { Foe(), Foe("Ghoul", cls: MonsterClass.Undead) };
        var result = Fight(c, monsters);
        var (engine, output) = Engine(c);
        Execute(engine, c, monsters, result, target: 0);   // the Ogre is not undead: a fitting foe is chosen

        monsters[1].HP.Should().Be(0);
        monsters[0].HP.Should().Be(5000);
        result.DefeatedMonsters.Should().Contain(monsters[1]);
        c.MiracleUsedToday.Should().BeTrue();
        Text(engine, output).Should().Contain(Loc.Get("miracle.banish", "Ghoul"));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Solarius_DoesNotBanishABossMiniBossOrOldGod_ItTakesHeavyHolyDamage(bool boss, bool mini, bool oldGod)
    {
        var c = Chosen("GmcBoss" + boss + mini + oldGod, "Solarius");
        var lich = Foe("Lich", boss, mini, MonsterClass.Undead);
        if (oldGod) lich.FamilyName = "OldGod";
        var monsters = new List<Monster> { lich };
        var (engine, output) = Engine(c);
        Execute(engine, c, monsters, Fight(c, monsters), target: 0);

        lich.IsAlive.Should().BeTrue("a boss is never removed at once");
        lich.HP.Should().BeLessThan(5000, "it takes the Miracle's damage");
        MiracleSystem.BanishKillsOutright(lich).Should().BeFalse();
        MiracleSystem.BanishBossDamage(lich).Should().Be(5000 * GameConfig.MiracleBanishBossDamagePct / 100);
        Text(engine, output).Should().Contain(Loc.Get("miracle.banish_resist", "Lich"));
    }

    [Fact]
    public void Valorian_TheNextSwingIsACertainCritical()
    {
        var c = Chosen("GmcCrit", "Valorian");
        var ogre = Foe();
        var (engine, output) = Engine(c, new HighRandom());
        c.MiracleCritPending = true;
        typeof(CombatEngine).GetMethod("ComputePlayerSwingDamage", F)!.Invoke(engine, new object[] { c, ogre, false, 0, 1 });
        c.MiracleCritPending.Should().BeFalse("spent on the swing");
        string text = Text(engine, output);
        text.Should().Contain(Loc.Get("miracle.crit"));
        text.Should().NotContain(Loc.Get("combat.critical_hit"), "the natural 20 is not rolled on top");

        var (engine2, output2) = Engine(c, new HighRandom());
        typeof(CombatEngine).GetMethod("ComputePlayerSwingDamage", F)!.Invoke(engine2, new object[] { c, ogre, false, 0, 1 });
        Text(engine2, output2).Should().NotContain(Loc.Get("miracle.crit"));
    }

    [Fact]
    public void Valorian_CallsTheAttackWithTheCriticalSet()
    {
        string body = GodMiracles1115Tests.Body("Scripts/Systems/CombatEngine.cs", "private async Task ExecuteMiracle(");
        int set = body.IndexOf("actor.MiracleCritPending = true;", StringComparison.Ordinal);
        int attack = body.IndexOf("Type = CombatActionType.Attack", StringComparison.Ordinal);
        set.Should().BeGreaterThan(0);
        attack.Should().BeGreaterThan(set);
    }

    [Fact]
    public void Amara_HealsEveryLivingAllyToFull_NotTheFallen()
    {
        var c = Chosen("GmcAmara", "Amara");
        c.HP = 20;
        var hurt = new Character { Name1 = "GmcHurt", Name2 = "GmcHurt", HP = 5, MaxHP = 300 };
        var fallen = new Character { Name1 = "GmcFallen", Name2 = "GmcFallen", HP = 0, MaxHP = 300 };
        var monsters = new List<Monster> { Foe() };
        var (engine, _) = Engine(c);
        Execute(engine, c, monsters, Fight(c, monsters, hurt, fallen));

        c.HP.Should().Be(c.MaxHP);
        hurt.HP.Should().Be(300);
        fallen.HP.Should().Be(0, "a Miracle of healing does not raise the dead");
        c.MiracleUsedToday.Should().BeTrue();
    }

    [Fact]
    public void Judicar_BindsAFoe_UnderTheHoldRules()
    {
        var c = Chosen("GmcBind", "Judicar");
        var ogre = Foe();
        var monsters = new List<Monster> { ogre };
        var (engine, output) = Engine(c, new HighRandom());
        Execute(engine, c, monsters, Fight(c, monsters), target: 0);
        ogre.IsStunned.Should().BeTrue();
        ogre.StunDuration.Should().Be(GameConfig.MiracleBindRounds);
        ogre.HoldsThisFight.Should().Be(1, "it counts in the shared hold budget");
        Text(engine, output).Should().Contain(Loc.Get("miracle.bind", "Ogre", GameConfig.MiracleBindRounds));
    }

    [Fact]
    public void Judicar_OnABoss_TheBossResistRollAndCapApply()
    {
        var resisted = Foe("Warlord", boss: true);
        var (e1, o1) = Engine(Chosen("GmcBindB1", "Judicar"), new LowRandom());
        e1.TryHoldMonster(resisted, CombatEngine.HoldKind.Bind, GameConfig.MiracleBindRounds).Should().BeFalse("the boss resist roll");
        resisted.IsStunned.Should().BeFalse();

        var held = Foe("Warlord", boss: true);
        var (e2, _) = Engine(Chosen("GmcBindB2", "Judicar"), new HighRandom());
        e2.TryHoldMonster(held, CombatEngine.HoldKind.Bind, GameConfig.MiracleBindRounds).Should().BeTrue();
        held.StunDuration.Should().Be(GameConfig.MaxStunDurationBoss);

        var bound = Foe();
        var (e3, _) = Engine(Chosen("GmcBindTwice", "Judicar"), new HighRandom());
        e3.TryHoldMonster(bound, CombatEngine.HoldKind.Freeze, 2).Should().BeTrue();
        e3.TryHoldMonster(bound, CombatEngine.HoldKind.Bind, 2).Should().BeFalse("one hold at a time");
    }

    [Fact]
    public void Umbrath_TheLeaderVanishes_WithNoFleePenalty()
    {
        var c = Chosen("GmcVanish", "Umbrath");
        var monsters = new List<Monster> { Foe() };
        var (engine, _) = Engine(c);
        Execute(engine, c, monsters, Fight(c, monsters));
        ((bool)typeof(CombatEngine).GetField("globalEscape", F)!.GetValue(engine)!).Should().BeTrue();
        ((bool)typeof(CombatEngine).GetField("_miracleVanished", F)!.GetValue(engine)!).Should().BeTrue();

        string src = GodMiracles1115Tests.Source("Scripts/Systems/CombatEngine.cs");
        src.Should().Contain("bool fledThisFight = globalEscape && !_miracleVanished;");
        src.Should().Contain("ApplyMentalFightEnd(result, mentalFloor, fledThisFight,");
        src.Should().Contain("if (fledThisFight && !result.PlayerActuallyDied) GodDeedSystem.Record(player, GodAct.Fled, terminal);");
        src.Should().Contain("if (result.Player.Fame > 0 && !_miracleVanished)");
        GodMiracles1115Tests.Count(src, "_miracleVanished = false;").Should().Be(1, "reset once at each fight's start");
    }

    [Fact]
    public void Umbrath_AGroupedFollowerVanishesAlone_NotThePartysEscape()
    {
        string turn = GodMiracles1115Tests.Body("Scripts/Systems/CombatEngine.cs", "private async Task ProcessGroupedPlayerTurn(");
        int vanish = turn.IndexOf("MiracleSystem.GetMiracle(teammate) == GodDomain.Shadow", StringComparison.Ordinal);
        int retreat = turn.IndexOf("if (action.Type == CombatActionType.Retreat)", StringComparison.Ordinal);
        int process = turn.IndexOf("await ProcessPlayerActionMultiMonster(action, teammate, monsters, result);", StringComparison.Ordinal);
        vanish.Should().BeGreaterThan(0);
        vanish.Should().BeLessThan(retreat).And.BeLessThan(process);
        int end = turn.IndexOf("// Individual retreat for followers", StringComparison.Ordinal);
        end.Should().BeGreaterThan(vanish);
        string branch = turn.Substring(vanish, end - vanish);
        branch.Should().Contain("result.Teammates?.Remove(teammate);").And.Contain("MiracleSystem.TryConsume(teammate)");
        branch.Should().NotContain("globalEscape");
    }

    [Fact]
    public void Terran_RestoresFullHp_Arcanus_RefillsMana()
    {
        var t = Chosen("GmcTerran", "Terran");
        t.HP = 7;
        var monsters = new List<Monster> { Foe() };
        var (e1, _) = Engine(t);
        Execute(e1, t, monsters, Fight(t, monsters));
        t.HP.Should().Be(t.MaxHP);
        t.MiracleUsedToday.Should().BeTrue();

        var a = Chosen("GmcArcanus", "Arcanus");
        var (e2, _) = Engine(a);
        Execute(e2, a, monsters, Fight(a, monsters));
        a.Mana.Should().Be(a.MaxMana);
        a.MiracleUsedToday.Should().BeTrue();
    }

    [Fact]
    public void Sylvana_CallsABeastAlly_ForThisFightOnly()
    {
        var c = Chosen("GmcSylvana", "Sylvana");
        var monsters = new List<Monster> { Foe() };
        var dungeonParty = new List<Character>();
        var result = new CombatResult { Player = c, Monsters = monsters, Teammates = new List<Character>(dungeonParty) };
        var (engine, output) = Engine(c);
        Execute(engine, c, monsters, result);

        var beast = result.Teammates.Should().ContainSingle().Subject;
        beast.IsPet.Should().BeTrue("the tamed-pet combat teammate");
        beast.PetSpeciesId.Should().Be(GameConfig.MiracleBeastId);
        beast.IsMiracleAlly.Should().BeTrue();
        beast.Level.Should().Be(c.Level);
        beast.Name2.Should().Be(Loc.Get("miracle.beast_name"));
        dungeonParty.Should().BeEmpty("the fight's own list; it never joins the party");
        Text(engine, output).Should().Contain(Loc.Get("miracle.beast", beast.DisplayName));
    }

    [Fact]
    public void SylvanasBeast_LeavesBeforeTheOutcome_SoItTakesNoXpShare()
    {
        string src = GodMiracles1115Tests.Source("Scripts/Systems/CombatEngine.cs");
        int remove = src.IndexOf("result.Teammates?.RemoveAll(t => t != null && t.IsMiracleAlly);", StringComparison.Ordinal);
        int outcome = src.IndexOf("        // Determine combat outcome\n        if (globalEscape)", StringComparison.Ordinal);
        remove.Should().BeGreaterThan(0);
        outcome.Should().BeGreaterThan(remove);
        GodMiracles1115Tests.Count(src, "IsMiracleAlly").Should().Be(1, "only this removal reads it");
    }

    [Fact]
    public void TheTamedPet_UsesTheSameBeastWrapper()
    {
        GodMiracles1115Tests.Body("Scripts/Locations/DungeonLocation.cs", "private void AddActivePetToParty(")
            .Should().Contain("UsurperRemake.Data.BeastData.BuildCombatWrapper(def, effLevel, pet.Name)");
    }

    [Fact]
    public void Discordia_ConfusesEveryFoe_WithMassConfusionsBossLimit()
    {
        var c = Chosen("GmcChaos", "Discordia");
        var monsters = new List<Monster> { Foe(), Foe("Rat"), Foe("Warlord", boss: true) };
        var (engine, output) = Engine(c, new HighRandom());
        Execute(engine, c, monsters, Fight(c, monsters));
        monsters[0].IsConfused.Should().BeTrue();
        monsters[0].ConfusedDuration.Should().Be(GameConfig.MiracleConfuseRounds);
        monsters[1].IsConfused.Should().BeTrue();
        monsters[2].IsConfused.Should().BeTrue();
        monsters[2].ConfusedDuration.Should().BeLessThanOrEqualTo(GameConfig.MassConfusionBossMaxRounds);
        c.MiracleUsedToday.Should().BeTrue();
    }

    [Fact]
    public void ACalledMiracle_IsOnceADay()
    {
        var t = Chosen("GmcTwice", "Terran");
        t.HP = 7;
        var monsters = new List<Monster> { Foe() };
        var (engine, output) = Engine(t);
        Execute(engine, t, monsters, Fight(t, monsters));
        t.HP = 9;
        Execute(engine, t, monsters, Fight(t, monsters));
        t.HP.Should().Be(9, "the second call does nothing");
        Text(engine, output).Should().Contain(Loc.Get("miracle.not_ready"));
    }

    // ---------------- Menus ----------------

    private static string Menu(string method, GodDomain miracle, bool follower = false)
    {
        var c = Chosen("GmcMenu" + method + miracle, "Terran");
        var (engine, output) = Engine(c);
        typeof(CombatEngine).GetMethod(method, F)!.Invoke(engine, new object[]
        {
            c, false, false, new List<(string key, string name, bool available)>(), follower, miracle
        });
        return Text(engine, output);
    }

    [Theory]
    [InlineData("ShowDungeonCombatMenuBBS")]
    [InlineData("ShowDungeonCombatMenuScreenReader")]
    [InlineData("ShowDungeonCombatMenuStandard")]
    public void EveryDungeonMenu_ShowsTheMiracleKey_OnlyWhenOffered(string method)
    {
        string label = MiracleSystem.MenuLabel(GodDomain.Earth);
        Menu(method, GodDomain.Earth).Should().Contain(label);
        Menu(method, GodDomain.Earth, follower: true).Should().Contain(label, "grouped followers see their own");
        Menu(method, GodDomain.None).Should().NotContain(MiracleSystem.MenuLabel(GodDomain.Earth))
            .And.NotContain("[M]").And.NotContain("M - ");
    }

    [Fact]
    public void TheBbsMiracleRow_FitsEightyColumns_AndTheBoxLabelFitsItsColumn()
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            using var doc = JsonDocument.Parse(GodMiracles1115Tests.Source($"Localization/{lang}.json"));
            string menu = doc.RootElement.GetProperty("miracle.menu").GetString()!;
            foreach (var d in MiracleSystem.AllCalled)
            {
                string name = doc.RootElement.GetProperty("miracle.name." + d.ToString().ToLowerInvariant()).GetString()!;
                string label = string.Format(menu, name);
                label.Length.Should().BeLessThanOrEqualTo(34, $"{lang} {d}: the box menu pads the label to 34");
                (" [M]" + label).Length.Should().BeLessThanOrEqualTo(80);
            }
        }
    }

    [Fact]
    public void TheMKey_IsReadForTheLeaderAndForFollowers_AndIsNotInPvP()
    {
        string leader = GodMiracles1115Tests.Body("Scripts/Systems/CombatEngine.cs", "private async Task<(CombatAction action, bool enableAutoCombat)> GetPlayerActionMultiMonster(");
        leader.Should().Contain("case \"M\":").And.Contain("action.Type = CombatActionType.Miracle;");
        string follower = GodMiracles1115Tests.Body("Scripts/Systems/CombatEngine.cs", "private CombatAction ParseGroupCombatInput(");
        follower.Should().Contain("if (trimmed.StartsWith(\"M\"))").And.Contain("MiracleOffered(teammate, monsters, result)");
        GodMiracles1115Tests.Body("Scripts/Systems/CombatEngine.cs", "private async Task<CombatAction> GetPlayerAction(")
            .Should().NotContain("Miracle", "the PvP menu path has no Miracle");
    }

    [Theory]
    [InlineData("miracle.menu", 1)]
    [InlineData("miracle.not_ready", 0)]
    [InlineData("miracle.called", 2)]
    [InlineData("miracle.banish_pick", 0)]
    [InlineData("miracle.banish", 1)]
    [InlineData("miracle.banish_resist", 1)]
    [InlineData("miracle.crit", 0)]
    [InlineData("miracle.healed", 3)]
    [InlineData("miracle.bind", 2)]
    [InlineData("miracle.bind_resist", 1)]
    [InlineData("miracle.vanish", 0)]
    [InlineData("miracle.vanish_other", 1)]
    [InlineData("miracle.mana", 2)]
    [InlineData("miracle.beast", 1)]
    [InlineData("miracle.beast_name", 0)]
    [InlineData("miracle.confuse", 0)]
    public void CombatMiracleText_IsInAllFiveLanguages(string key, int placeholders)
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            using var doc = JsonDocument.Parse(GodMiracles1115Tests.Source($"Localization/{lang}.json"));
            doc.RootElement.TryGetProperty(key, out var v).Should().BeTrue($"{lang} {key}");
            string s = v.GetString()!;
            s.Should().NotBeNullOrWhiteSpace();
            for (int i = 0; i < placeholders; i++) s.Should().Contain("{" + i + "}", $"{lang} {key}");
            s.Should().NotContain("{" + placeholders + "}", $"{lang} {key}");
            s.Should().NotContain("\u2014").And.NotContain("\u2013");
        }
    }
}
