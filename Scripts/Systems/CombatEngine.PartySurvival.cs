using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using UsurperRemake.Utils;

/// <summary>
/// 1.3.3 party survival. A story companion or NPC ally that reaches 0 HP in a fight is Downed: it stays
/// in the party at 0 HP, out of every target, heal and turn list (all of them already skip HP 0). The
/// combat owner can lift it with Rescue [U] and one healing potion, once per ally per fight. A predator (Beast, Insectoid, Shadow,
/// Undead) or a boss spends its action to announce a finish on the earliest Downed ally and, in a later
/// round, spends its action to finish it: only then do the death handlers run (permadeath, grief, the
/// left-to-die penalty, the fall in teammates_lost). A taunt, a hold or the finisher's death loses the
/// finish. At the fight's end every ally still Downed gets up at half its max HP.
/// Out of scope and unchanged: grouped players, mercenaries, echoes and the Miracle beast die as before.
/// </summary>
public partial class CombatEngine
{
    // per fight; cleared with the other per fight state (ResetPartySurvival at the fight's start)
    private readonly Dictionary<Character, (Monster Finisher, int Round)> _pendingFinish = new(ReferenceEqualityComparer.Instance);
    private readonly List<Character> _downOrder = new();
    private readonly HashSet<Character> _downedAtTurnStart = new(ReferenceEqualityComparer.Instance);
    private bool _ownerRescuedThisTurn;
    private bool _finishGateReached;
    // 1.3.6: each Downed ally is lifted once per fight; a second down of the same ally cannot be rescued
    private readonly HashSet<Character> _rescuedThisFight = new(ReferenceEqualityComparer.Instance);

    private void ResetPartySurvival()
    {
        _pendingFinish.Clear();
        _downOrder.Clear();
        _downedAtTurnStart.Clear();
        _ownerRescuedThisTurn = false;
        _finishGateReached = false;
        _rescuedThisFight.Clear();
    }

    /// <summary>Who can be Downed: a story companion or an NPC ally (spouse, lover, team member, pet: every
    /// teammate the death dispatch hands to HandleCompanionDeath or HandleNpcTeammateDeath). Grouped players,
    /// mercenaries, echoes and the Miracle beast keep today's death.</summary>
    internal static bool IsDownable(Character? c) =>
        c != null && !c.IsGroupedPlayer && !c.IsMercenary && !c.IsEcho && !c.IsMiracleAlly;

    /// <summary>Downed allies still in the fight, earliest down first.</summary>
    internal List<Character> DownedAllies(CombatResult? result = null)
    {
        var team = result?.Teammates ?? currentTeammates;
        return _downOrder.Where(a => a.IsDowned && (team == null || team.Contains(a))).ToList();
    }

    /// <summary>A follower line for the group, unless the leader's screen is being captured (the capture
    /// already carries it to the followers).</summary>
    private void BroadcastPartyLine(CombatResult result, string color, string key, params object[] args)
    {
        if (terminal.IsCapturing) return;
        BroadcastGroupLocalized(result, lang => UIHelper.AnsiRows(color, $"  {Loc.GetIn(lang, key, args)}"));   // wrapped at 79
    }

    /// <summary>
    /// The one step every combat shape calls when a teammate's HP reaches 0. Idempotent: an ally already
    /// Downed, or above 0 HP, is left as it is. An out of scope actor runs <paramref name="dieToday"/> (the
    /// shape's own death handling, unchanged), or the death dispatch when none is given. An in scope ally
    /// is Downed and stays in the party.
    /// </summary>
    internal async Task EnterDownedOrDie(Character tm, string killerName, Monster? killer, CombatResult result, Func<Task>? dieToday = null)
    {
        if (tm == null || tm.IsDowned || tm.HP > 0) return;
        if (!IsDownable(tm))
        {
            if (dieToday != null) await dieToday();
            else await HandleTeammateDeathDispatch(tm, killerName, result);
            return;
        }
        tm.HP = 0;
        tm.Lifecycle = AllyLifecycle.Downed;
        StatsFor(tm).Downs++;
        _downOrder.Remove(tm);
        _downOrder.Add(tm);
        _pendingFinish.Remove(tm);
        UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.ally_downed", tm.DisplayName)}", "bright_red");
        BroadcastPartyLine(result, "\u001b[1;31m", "combat.ally_downed_group", tm.DisplayName);
        result.CombatLog.Add($"{tm.DisplayName} is down");
        if (tm.IsCompanion) CompanionSystem.Instance.SyncCompanionHP(tm);
    }

    private static bool IsTaunted(Monster m) => m.TauntRoundsLeft > 0 && !string.IsNullOrEmpty(m.TauntedBy);

    /// <summary>A finisher: any boss, or a regular monster of a predator family. Read from the monster
    /// itself, so a tactics switch or a boss context never changes it.</summary>
    internal static bool IsEligibleFinisher(Monster? m) =>
        m != null && (m.IsBoss || MonsterTactics.IsPredatorFamily(m.FamilyName));

    private bool HasLiveFinisher(Character ally) =>
        _pendingFinish.TryGetValue(ally, out var p) && p.Finisher.IsAlive;

    /// <summary>
    /// Called on a monster's first action of the round, after its holds and before its target choice.
    /// True when the monster spent its action on a finish or an announce.
    /// </summary>
    internal async Task<bool> TryFinishOrAnnounce(Monster monster, CombatResult result)
    {
        _finishGateReached = true;
        int round = result.CurrentRound;
        Character? mine = _pendingFinish.FirstOrDefault(kv => ReferenceEquals(kv.Value.Finisher, monster)).Key;
        if (mine != null)
        {
            var (_, announced) = _pendingFinish[mine];
            if (!mine.IsDowned || result.Teammates == null || !result.Teammates.Contains(mine))
            {
                _pendingFinish.Remove(mine);
            }
            else if (IsTaunted(monster))
            {
                _pendingFinish.Remove(mine);
                UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.finish_lost_taunt", MonsterNames.Display(monster), mine.DisplayName)}", "cyan");
                BroadcastPartyLine(result, "\u001b[36m", "combat.finish_lost_taunt", MonsterNames.Display(monster), mine.DisplayName);
                return false;
            }
            else if (announced < round)
            {
                await RunFinish(monster, mine, result);
                return true;
            }
            else
            {
                return false;
            }
        }

        if (!IsEligibleFinisher(monster) || IsTaunted(monster)) return false;
        var target = DownedAllies(result).FirstOrDefault(a => !HasLiveFinisher(a));
        if (target == null) return false;
        _pendingFinish[target] = (monster, round);
        UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.finish_announce", MonsterNames.Display(monster), target.DisplayName)}", "bright_red");
        BroadcastPartyLine(result, "\u001b[1;31m", "combat.finish_announce", MonsterNames.Display(monster), target.DisplayName);
        result.CombatLog.Add($"{monster.Name} closes on {target.DisplayName}");
        await Pacing.Wait(GetCombatDelay(600));
        return true;
    }

    /// <summary>A held finisher (its turn skipped before the finish gate) loses its finish.</summary>
    private void LoseFinishHeld(Monster monster, CombatResult result)
    {
        Character? mine = _pendingFinish.FirstOrDefault(kv => ReferenceEquals(kv.Value.Finisher, monster)).Key;
        if (mine == null) return;
        _pendingFinish.Remove(mine);
        if (!monster.IsAlive || !mine.IsDowned) return;
        UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.finish_lost_held", MonsterNames.Display(monster), mine.DisplayName)}", "cyan");
        BroadcastPartyLine(result, "\u001b[36m", "combat.finish_lost_held", MonsterNames.Display(monster), mine.DisplayName);
    }

    internal bool HasPendingFinish(Monster monster) =>
        _pendingFinish.Values.Any(p => ReferenceEquals(p.Finisher, monster));

    /// <summary>The finish: a scripted step (no damage roll). The fall is counted, then the death handlers
    /// run as a death at 0 HP ran them before 1.3.3.</summary>
    private async Task RunFinish(Monster monster, Character ally, CombatResult result)
    {
        _pendingFinish.Remove(ally);
        _downOrder.Remove(ally);
        StatsFor(ally).Finished = true;
        ally.Lifecycle = AllyLifecycle.Active;   // dead, not Downed: the world sweeps see a death
        ally.HP = 0;
        UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.finish_lands", MonsterNames.Display(monster), ally.DisplayName)}", "red");
        BroadcastPartyLine(result, "\u001b[1;31m", "combat.finish_lands", MonsterNames.Display(monster), ally.DisplayName);
        EvFall(ally);
        if (TryMortisMiracleForFollower(ally, result)) return;   // a no-op for an ally in scope; kept as settled
        RecordFinishWitnesses(ally, monster, result);   // 1.3.8: the allies who saw it remember it (CombatEngine.Cascade.cs)
        BroadcastGroupLocalized(result, lang =>
            $"\u001b[1;31m  ═══ {Loc.GetIn(lang, "combat.npc_fallen_battle_banner", ally.DisplayName)} ═══\u001b[0m");
        if (ally.IsCompanion && ally.CompanionId.HasValue)
            await HandleCompanionDeath(ally, monster.Name, result);
        else
            await HandleNpcTeammateDeath(ally, monster.Name, result);
        if (ally.IsCompanion) CompanionSystem.Instance.SyncCompanionHP(ally);
    }

    /// <summary>1.3.6: the ally was already rescued in this fight, so it cannot be lifted again.</summary>
    internal bool RescuedThisFight(Character ally) => _rescuedThisFight.Contains(ally);

    /// <summary>Downed allies that can still be lifted: each ally once per fight (1.3.6).</summary>
    internal List<Character> RescuableAllies(CombatResult? result = null) =>
        DownedAllies(result).Where(a => !_rescuedThisFight.Contains(a)).ToList();

    /// <summary>Rescue is offered to the combat owner while an ally not yet rescued in this fight is Downed,
    /// a healing potion is on the belt, and no Old God potion cooldown runs.</summary>
    internal bool RescueOffered(Character owner) =>
        RescuableAllies().Count > 0 && owner.Healing > 0 && !(BossContext != null && owner.PotionCooldownRounds > 0);

    /// <summary>The [U] key: the Downed ally to lift (a prompt only when two or more can be lifted), or null.
    /// 1.3.6: an ally already rescued in this fight is not offered; with only such allies Downed, one line
    /// says so.</summary>
    private async Task<CombatAction?> ChooseRescue(Character owner)
    {
        if (DownedAllies().Count == 0) return null;
        var downed = RescuableAllies();
        if (downed.Count == 0)
        {
            UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.rescue_already_used", DownedAllies()[0].DisplayName)}", "yellow");
            await Pacing.Wait(GetCombatDelay(800));
            return null;
        }
        if (!RescueOffered(owner))
        {
            if (owner.Healing > 0)
                UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.potion_recharging", owner.PotionCooldownRounds)}", "yellow");
            else
                UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.rescue_no_potion")}", "yellow");
            await Pacing.Wait(GetCombatDelay(800));
            return null;
        }
        Character target = downed[0];
        if (downed.Count > 1)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("combat.rescue_which"));
            for (int i = 0; i < downed.Count; i++)
            {
                terminal.SetColor("bright_yellow");
                terminal.Write($"  [{i + 1}] ");
                terminal.SetColor("white");
                terminal.WriteLine(downed[i].DisplayName);
            }
            terminal.SetColor("white");
            terminal.Write(Loc.Get("ui.choice"));
            var pick = (await terminal.GetInput("")).Trim();
            if (!int.TryParse(pick, out int n) || n < 1 || n > downed.Count) return null;
            target = downed[n - 1];
        }
        return new CombatAction { Type = CombatActionType.Rescue, RescueTarget = target };
    }

    /// <summary>One healing potion from the owner's belt lifts the ally, healed as Heal Ally heals.</summary>
    private async Task ExecuteRescue(Character owner, Character? ally, CombatResult result)
    {
        if (ally == null || !ally.IsDowned || result.Teammates == null || !result.Teammates.Contains(ally)) return;
        if (_rescuedThisFight.Contains(ally))
        {
            UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.rescue_already_used", ally.DisplayName)}", "yellow");
            return;
        }
        if (owner.Healing <= 0 || (BossContext != null && owner.PotionCooldownRounds > 0))
        {
            UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.rescue_no_potion")}", "yellow");
            return;
        }
        owner.Healing--;
        long heal = PotionBonus.BaseHeal(owner, random, receiver: ally);   // at least 20% of the ally's max HP
        heal = DifficultySystem.ApplyHealingMultiplier(heal);
        heal = PotionBonus.ApplyOwnerBonuses(owner, heal, result.Monsters);
        heal = Math.Max(1, Math.Min(heal, ally.MaxHP));
        EvUse(owner, CombatEventKind.Potion);
        EvHeal(owner, ally, heal, CombatEventKind.Potion);
        ally.HP = heal;
        ally.Lifecycle = AllyLifecycle.Active;
        _pendingFinish.Remove(ally);
        _downOrder.Remove(ally);
        _rescuedThisFight.Add(ally);
        StatsFor(ally).Rescues++;
        owner.Statistics?.RecordPotionUsed(heal);
        if (BossContext != null)
            owner.PotionCooldownRounds = GameConfig.ModBossPotionCooldownRounds + 1;   // as ExecuteHeal
        UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.rescue_done", ally.DisplayName, ally.HP)}", "bright_green");
        BroadcastPartyLine(result, "\u001b[1;32m", "combat.rescue_done", ally.DisplayName, ally.HP);
        GodDeedSystem.Record(owner, GodAct.AllyHealed, terminal);   // the Love deed, as Heal Ally
        if (ally.IsCompanion && ally.CompanionId.HasValue) CompanionSystem.Instance.SyncCompanionHP(ally);
        result.CombatLog.Add($"{owner.DisplayName} rescues {ally.DisplayName}");
        await Pacing.Wait(GetCombatDelay(800));
    }

    /// <summary>At the fight's end every ally still Downed gets up at max(1, MaxHP / 2). Never a finish.
    /// Runs before the outcome branch and once more in the finally (a no-op after the normal end).</summary>
    internal void ResolveDownedAtFightEnd(CombatResult? result)
    {
        var allies = (result?.Teammates ?? new List<Character>()).Concat(_downOrder).Distinct().ToList();
        _pendingFinish.Clear();
        _downOrder.Clear();
        foreach (var ally in allies)
        {
            if (ally == null || !ally.IsDowned) continue;
            ally.HP = Math.Max(1, ally.MaxHP / 2);
            ally.Lifecycle = AllyLifecycle.Active;
            try
            {
                UIHelper.WriteRow(terminal, $"  {Loc.Get("combat.ally_recovers", ally.DisplayName, ally.HP, ally.MaxHP)}", "green");
                if (result != null) BroadcastPartyLine(result, "\u001b[32m", "combat.ally_recovers", ally.DisplayName, ally.HP, ally.MaxHP);
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("COMBAT", $"recovery line failed: {ex.Message}"); }
            if (ally.IsCompanion && ally.CompanionId.HasValue) CompanionSystem.Instance.SyncCompanionHP(ally);
        }
    }

    /// <summary>The fall of a teammate for teammates_lost: once per ally per fight, at a permanent loss.</summary>
    private void EvFall(Character target)
    {
        if (_eventTally == null || target == null) return;
        if (!_fellThisFight.Add(target)) return;
        RaiseCombatEvent(new CombatEvent(CombatSide.Monster, SideOf(target), 0, CombatEventKind.Basic, TargetFell: true));
    }
}
