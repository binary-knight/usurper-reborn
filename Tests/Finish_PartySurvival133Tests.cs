using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.PartySurvival133Harness;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.3 party survival (PS-2 1.4, 1.7): the announced finish. A predator (Beast, Insectoid, Shadow, Undead)
/// or any boss spends its first action of a round to announce a finish on the earliest Downed ally, and in a
/// later round spends it to finish; a taunt, a hold or a rescue loses it. Only the finish runs the death
/// handlers (KillCompanion, grief, the left-to-die penalty, now "did not pick up").
/// </summary>
[Collection("SharedGameSingletons")]
public class Finish_PartySurvival133Tests
{
    private static Task<bool> Act(Rig r, Monster m, int round)
    {
        r.Result.CurrentRound = round;
        return r.Engine.TryFinishOrAnnounce(m, r.Result);
    }

    /// <summary>The monster's whole turn, as the round loop calls it (first action when firstOfRound).</summary>
    private static async Task Turn(Rig r, Monster m, int round, bool firstOfRound = true)
    {
        r.Result.CurrentRound = round;
        if (firstOfRound) m.StatusTickedThisRound = false;
        await CallAsync(r.Engine, "ProcessMonsterAction", m, r.Owner, r.Result, r.Result.Monsters);
    }

    // ---------- 4. a non predator never announces or finishes; a predator announces, then finishes later ----------

    [Fact]
    public async Task ANonPredator_NeverAnnouncesOrFinishes()
    {
        var ally = Ally();
        var goblin = Mob("Goblin", "Goblinoid");
        var r = NewRig(team: new[] { ally }, monsters: new[] { goblin });
        await Down(r, ally);
        for (int round = 1; round <= 4; round++)
            (await Act(r, goblin, round)).Should().BeFalse();
        ally.IsDowned.Should().BeTrue();
        r.Engine.HasPendingFinish(goblin).Should().BeFalse();
        CombatEngine.IsEligibleFinisher(goblin).Should().BeFalse();
        goblin.IsMiniBoss = true; goblin.IsElite = true;
        CombatEngine.IsEligibleFinisher(goblin).Should().BeFalse("minibosses and elites follow the family rule");
    }

    [Theory]
    [InlineData("Beast")]
    [InlineData("Insectoid")]
    [InlineData("Shadow")]
    [InlineData("Undead")]
    public async Task APredator_Announces_ThenFinishesOnlyInALaterRound(string family)
    {
        var ally = Ally();
        var wolf = Mob("Hunter", family);
        var r = NewRig(team: new[] { ally }, monsters: new[] { wolf });
        await Down(r, ally);
        (await Act(r, wolf, 1)).Should().BeTrue("the announce spends the action");
        r.Text.Should().Contain(Loc.Get("combat.finish_announce", MonsterNames.Display(wolf), ally.DisplayName));
        ally.IsDowned.Should().BeTrue("no finish in the announce round");
        (await Act(r, wolf, 1)).Should().BeFalse("5: asked again in the announce round, it does not finish");
        ally.IsDowned.Should().BeTrue("5: the finish never lands in the announce round");
        (await Act(r, wolf, 2)).Should().BeTrue();
        r.Text.Should().Contain(Loc.Get("combat.finish_lands", MonsterNames.Display(wolf), ally.DisplayName));
        ally.IsDowned.Should().BeFalse();
        r.Result.Teammates.Should().NotContain(ally, "the death handler removed the finished ally");
        r.Engine.StatsFor(ally).Finished.Should().BeTrue();
    }

    [Fact]
    public async Task TheAnnounce_TakesTheEarliestDown_AndASecondPredatorTakesTheNext()
    {
        var first = Ally("Mira"); var second = Ally("Kade");
        var a = Mob("Wolf A"); var b = Mob("Wolf B");
        var r = NewRig(team: new[] { first, second }, monsters: new[] { a, b });
        await Down(r, first);
        await Down(r, second);
        (await Act(r, a, 1)).Should().BeTrue();
        (await Act(r, b, 1)).Should().BeTrue();
        r.Text.Should().Contain(Loc.Get("combat.finish_announce", MonsterNames.Display(a), first.DisplayName));
        r.Text.Should().Contain(Loc.Get("combat.finish_announce", MonsterNames.Display(b), second.DisplayName));
    }

    // ---------- 5. the finish never lands in the announce round (a multi attack boss included) ----------

    [Fact]
    public async Task AMultiAttackBoss_CannotAnnounceAndFinishInOneRound()
    {
        var ally = Ally("Mira", hp: 100);
        var other = Ally("Kade", hp: 5000, maxHp: 5000);
        var boss = Mob("Old One", "Demonic", boss: true, hp: 50000, str: 5);
        var r = NewRig(owner: Hero(hp: 50000), team: new[] { ally, other }, monsters: new[] { boss });
        await Down(r, ally);
        await Turn(r, boss, 1);                       // first action: the announce
        r.Engine.HasPendingFinish(boss).Should().BeTrue();
        await Turn(r, boss, 1, firstOfRound: false);  // second attack this round
        await Turn(r, boss, 1, firstOfRound: false);  // third
        ally.IsDowned.Should().BeTrue("the finish never lands in the announce round");
        await Turn(r, boss, 2);
        ally.IsDowned.Should().BeFalse("the next round's first action finishes");
        r.Result.Teammates.Should().NotContain(ally);
    }

    // ---------- 6. a taunted or held finisher loses its finish ----------

    [Fact]
    public async Task ATauntedFinisher_LosesItsFinish_AnUntauntedOneKeepsIt()
    {
        var ally = Ally();
        var wolf = Mob();
        var r = NewRig(team: new[] { ally }, monsters: new[] { wolf });
        await Down(r, ally);
        (await Act(r, wolf, 1)).Should().BeTrue();
        wolf.TauntRoundsLeft = 2; wolf.TauntedBy = r.Owner.DisplayName;
        (await Act(r, wolf, 2)).Should().BeFalse("taunted, it attacks the taunter instead");
        r.Engine.HasPendingFinish(wolf).Should().BeFalse();
        r.Text.Should().Contain(Loc.Get("combat.finish_lost_taunt", MonsterNames.Display(wolf), ally.DisplayName));
        ally.IsDowned.Should().BeTrue();
        (await Act(r, wolf, 3)).Should().BeFalse("no announce while taunted");
        wolf.TauntRoundsLeft = 0; wolf.TauntedBy = null;
        (await Act(r, wolf, 4)).Should().BeTrue("it may announce again later");
        (await Act(r, wolf, 5)).Should().BeTrue();
        ally.IsDowned.Should().BeFalse("an untaunted finisher keeps its finish");
    }

    [Fact]
    public async Task AHeldFinisher_LosesItsFinish()
    {
        var ally = Ally();
        var wolf = Mob(str: 1);
        var r = NewRig(owner: Hero(hp: 50000), team: new[] { ally }, monsters: new[] { wolf });
        await Down(r, ally);
        await Turn(r, wolf, 1);
        r.Engine.HasPendingFinish(wolf).Should().BeTrue();
        wolf.IsFrozen = true; wolf.FrozenDuration = 1;
        await Turn(r, wolf, 2);
        r.Engine.HasPendingFinish(wolf).Should().BeFalse("held, it loses the finish");
        r.Text.Should().Contain(Loc.Get("combat.finish_lost_held", MonsterNames.Display(wolf), ally.DisplayName));
        ally.IsDowned.Should().BeTrue();
        await Turn(r, wolf, 3);
        ally.IsDowned.Should().BeTrue("round 3 is a new announce, not a finish");
        r.Engine.HasPendingFinish(wolf).Should().BeTrue();
    }

    // ---------- 7. a boss finishes after its announced window; not without one ----------

    [Fact]
    public async Task ABoss_FinishesAfterItsWindow_NotWithoutOne()
    {
        var ally = Ally();
        var boss = Mob("Old One", "Demonic", boss: true);
        var r = NewRig(team: new[] { ally }, monsters: new[] { boss });
        CombatEngine.IsEligibleFinisher(boss).Should().BeTrue("a boss of any family");
        await Down(r, ally);
        (await Act(r, boss, 3)).Should().BeTrue("announce");
        ally.IsDowned.Should().BeTrue("no finish without a window");
        (await Act(r, boss, 4)).Should().BeTrue("finish");
        ally.IsDowned.Should().BeFalse();
    }

    // ---------- 8. a finish runs KillCompanion once; a fight end runs it zero times ----------

    [Fact]
    public async Task AFinish_KillsTheCompanion_AndBeginsGrief()
    {
        try
        {
            var w = FreshCompanion();
            var wolf = Mob();
            var r = NewRig(team: new[] { w }, monsters: new[] { wolf }, script: Enters);
            await Down(r, w);
            GriefSystem.Instance.IsGrieving.Should().BeFalse("13: no grief at the down");
            await Act(r, wolf, 1);
            await Act(r, wolf, 2);
            CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!.IsDead.Should().BeTrue();
            CompanionSystem.Instance.GetFallenCompanions().Count(f => f.Companion.Id == CompanionId.Aldric).Should().Be(1, "killed once");
            GriefSystem.Instance.IsGrieving.Should().BeTrue("13: grief begins at the finish");
        }
        finally { RestoreSilence(); }
    }

    [Fact]
    public async Task AFightEnd_WithADownedCompanion_LeavesItAlive()
    {
        try
        {
            var w = FreshCompanion();
            var r = NewRig(team: new[] { w }, monsters: new[] { Mob() });
            await Down(r, w);
            r.Engine.ResolveDownedAtFightEnd(r.Result);
            CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!.IsDead.Should().BeFalse();
            CompanionSystem.Instance.GetFallenCompanions().Should().BeEmpty();
            GriefSystem.Instance.IsGrieving.Should().BeFalse();
        }
        finally { RestoreSilence(); }
    }

    // ---------- 14. left to die, rekeyed to "did not pick up" ----------

    [Fact]
    public async Task LeftToDie_NeedsADownAtTheTurnStart_APotion_AndNoRescue()
    {
        var ally = Ally();
        var r = NewRig(owner: Hero(potions: 3), team: new[] { ally });
        var attack = new CombatAction { Type = CombatActionType.Attack };
        r.Engine.NoteOwnerTurn(r.Owner, r.Team, attack);
        await Down(r, ally);
        r.Engine.CouldHaveHelped(ally, r.Owner).Should().BeFalse("not Downed when the owner chose");
        r.Engine.NoteOwnerTurn(r.Owner, r.Team, attack);
        r.Engine.CouldHaveHelped(ally, r.Owner).Should().BeTrue("Downed at the turn start, a potion held, no Rescue");
        r.Engine.NoteOwnerTurn(r.Owner, r.Team, new CombatAction { Type = CombatActionType.Rescue, RescueTarget = ally });
        r.Engine.CouldHaveHelped(ally, r.Owner).Should().BeFalse("a Rescue was attempted");
        r.Owner.Healing = 0;
        r.Engine.NoteOwnerTurn(r.Owner, r.Team, attack);
        r.Engine.CouldHaveHelped(ally, r.Owner).Should().BeFalse("no potion to pick them up with");
    }

    [Fact]
    public async Task LeftToDie_TheFinishPrintsThePenaltyLine_OnlyWhenItApplies()
    {
        try
        {
            var w = FreshCompanion(loyalty: 60);
            var wolf = Mob();
            var r = NewRig(owner: Hero(potions: 3), team: new[] { w }, monsters: new[] { wolf }, script: Enters);
            await Down(r, w);
            r.Engine.NoteOwnerTurn(r.Owner, r.Team, new CombatAction { Type = CombatActionType.Attack });
            await Act(r, wolf, 1);
            await Act(r, wolf, 2);
            r.Text.Should().Contain(Loc.Get("combat.ally_death_could_have_helped", w.DisplayName));

            var w2 = FreshCompanion(loyalty: 60);
            var r2 = NewRig(owner: Hero(potions: 0), team: new[] { w2 }, monsters: new[] { wolf }, script: Enters);
            await Down(r2, w2);
            r2.Engine.NoteOwnerTurn(r2.Owner, r2.Team, new CombatAction { Type = CombatActionType.Attack });
            await Act(r2, wolf, 5);
            await Act(r2, wolf, 6);
            r2.Result.Teammates.Should().NotContain(w2);
            r2.Text.Should().NotContain(Loc.Get("combat.ally_death_could_have_helped", w2.DisplayName), "no potion to pick them up with");
        }
        finally { RestoreSilence(); }
    }

    // ---------- 22. Mortis's Miracle: grouped players keep it; an ally in scope never reaches it ----------

    [Fact]
    public async Task Mortis_IsANoOpForAnAllyInScope_AtTheFinish()
    {
        var ally = Ally();
        var wolf = Mob();
        var r = NewRig(team: new[] { ally }, monsters: new[] { wolf });
        ((bool)Call(r.Engine, "TryMortisMiracleForFollower", ally, r.Result)!).Should().BeFalse("not a grouped player");
        await Down(r, ally);
        await Act(r, wolf, 1);
        await Act(r, wolf, 2);
        ally.HP.Should().Be(0, "no miracle lifted the finished ally");
        r.Result.Teammates.Should().NotContain(ally);
    }

    [Fact]
    public void Mortis_StaysAtTheThreeGroupedDeathSites()
    {
        string src = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "Scripts/Systems/CombatEngine.cs"));
        int count = 0, at = 0;
        while ((at = src.IndexOf("TryMortisMiracleForFollower(", at, StringComparison.Ordinal)) >= 0) { count++; at++; }
        count.Should().Be(4, "the helper and the three follower death sites, each now behind the step's out of scope branch");
        string ps = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "Scripts/Systems/CombatEngine.PartySurvival.cs"));
        ps.IndexOf("TryMortisMiracleForFollower(ally, result)", StringComparison.Ordinal)
            .Should().BeLessThan(ps.IndexOf("await HandleCompanionDeath(ally", StringComparison.Ordinal), "the finish keeps the call before the handler");
    }

    private static string RepoRoot()
    {
        var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "usurper-reloaded.csproj"))) d = d.Parent;
        return d!.FullName;
    }
}
