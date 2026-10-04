using System;
using System.Collections.Generic;
using UsurperRemake.Systems;
using UsurperRemake.Utils;

/// <summary>1.2.6: the side of a monster fight a combatant is on, for the combat event stream.
/// Team is everyone fighting with the player: companions, hired NPCs, pets, grouped followers.</summary>
public enum CombatSide { Player = 0, Team = 1, Monster = 2 }

/// <summary>1.2.6: what kind of hit or heal a combat event is.</summary>
public enum CombatEventKind { Basic, Ability, Spell, Dot, Heal, Potion }

/// <summary>
/// 1.2.6: one hit or heal in a monster fight, raised where the engine applies the amount it already
/// computed. A use event (IsUse) marks one spell cast, ability use or potion drunk by the actor; it has
/// amount 0 and its target is the actor's own side. TargetFell is set on the first event that left a
/// player or team target at 0 HP in this fight.
/// </summary>
public readonly record struct CombatEvent(
    CombatSide Actor, CombatSide Target, long Amount, CombatEventKind Kind, bool IsUse = false, bool TargetFell = false);

/// <summary>1.2.6: the facts of a fight known before its first blow. FirstActor is 0 when the player's
/// side acts first, 1 when monsters won an ambush and struck first.</summary>
public readonly record struct CombatFightStart(int FloorActual, int Difficulty, int PartySize, int EncounterSize, int FirstActor);

/// <summary>1.2.6: receives a monster fight's events from CombatEngine.Observer. An exception thrown
/// here is caught by the engine and logged once per fight; the fight goes on.</summary>
public interface ICombatObserver
{
    void OnFightStart(CombatFightStart start);
    void OnEvent(CombatEvent e);
    void OnFightEnd(long playerHpEnd);
}

/// <summary>
/// 1.2.6: the per-fight accumulator on CombatResult. Every value is built from the engine's combat
/// events (CombatEngine.RaiseCombatEvent); nothing here reads a combatant or draws a random number.
/// The existing TotalDamageDealt and TotalDamageTaken are separate and unchanged.
/// </summary>
public sealed class CombatTally : ICombatObserver
{
    public int FloorActual { get; private set; }
    public int Difficulty { get; private set; }
    public int PartySize { get; private set; }
    public int EncounterSize { get; private set; }
    public int FirstActor { get; private set; }
    public long DmgToPlayerBasic { get; private set; }
    public long DmgToPlayerAbility { get; private set; }
    public long DmgToPlayerSpell { get; private set; }
    public long DmgToPlayerDot { get; private set; }
    public long DmgToTeam { get; private set; }
    public long DmgByPlayer { get; private set; }
    public long DmgByTeam { get; private set; }
    public long HealPlayer { get; private set; }
    public int PotionsUsed { get; private set; }
    public int AbilitiesUsed { get; private set; }
    public int SpellsUsed { get; private set; }
    public int TeammatesLost { get; private set; }
    public long PlayerHpEnd { get; private set; }
    /// <summary>True once the fight start arrived (the fight was entered).</summary>
    public bool Started { get; private set; }
    /// <summary>True once the fight end arrived (PlayerHpEnd is set).</summary>
    public bool Ended { get; private set; }

    public void OnFightStart(CombatFightStart start)
    {
        Started = true;
        FloorActual = start.FloorActual;
        Difficulty = start.Difficulty;
        PartySize = start.PartySize;
        EncounterSize = start.EncounterSize;
        FirstActor = start.FirstActor;
    }

    public void OnEvent(CombatEvent e)
    {
        if (e.TargetFell && e.Target == CombatSide.Team) TeammatesLost++;
        if (e.IsUse)
        {
            if (e.Actor != CombatSide.Player) return;
            switch (e.Kind)
            {
                case CombatEventKind.Potion: PotionsUsed++; break;
                case CombatEventKind.Ability: AbilitiesUsed++; break;
                case CombatEventKind.Spell: SpellsUsed++; break;
            }
            return;
        }
        if (e.Amount <= 0) return;
        bool heal = e.Kind == CombatEventKind.Heal || e.Kind == CombatEventKind.Potion;
        if (heal)
        {
            if (e.Target == CombatSide.Player) HealPlayer += e.Amount;
            return;
        }
        switch (e.Target)
        {
            case CombatSide.Player when e.Actor == CombatSide.Monster:
                switch (e.Kind)
                {
                    case CombatEventKind.Basic: DmgToPlayerBasic += e.Amount; break;
                    case CombatEventKind.Ability: DmgToPlayerAbility += e.Amount; break;
                    case CombatEventKind.Spell: DmgToPlayerSpell += e.Amount; break;
                    case CombatEventKind.Dot: DmgToPlayerDot += e.Amount; break;
                }
                break;
            case CombatSide.Team when e.Actor == CombatSide.Monster:
                DmgToTeam += e.Amount;
                break;
            case CombatSide.Monster when e.Actor == CombatSide.Player:
                DmgByPlayer += e.Amount;
                break;
            case CombatSide.Monster when e.Actor == CombatSide.Team:
                DmgByTeam += e.Amount;
                break;
        }
    }

    public void OnFightEnd(long playerHpEnd)
    {
        Ended = true;
        PlayerHpEnd = playerHpEnd;
    }
}

public partial class CombatEngine
{
    /// <summary>1.2.6: an outside listener for this engine's monster fight events (the balance tool,
    /// tests). The fight's own accumulator (CombatResult.Tally) is fed whether or not one is set.</summary>
    public ICombatObserver? Observer { get; set; }

    /// <summary>1.2.6: test seam. False stops every combat event, the accumulator included, so a test
    /// can run the same seeded fight with the events on and off.</summary>
    internal bool CombatEventsEnabled { get; set; } = true;

    private CombatTally? _eventTally;          // the running fight's accumulator, null outside a monster fight
    private bool _observerFailureLogged;      // one log line per fight for a throwing observer
    private bool _fightEventsStarted;
    private readonly HashSet<Character> _fellThisFight = new(ReferenceEqualityComparer.Instance);
    // The kind a shared damage helper reports (ApplySingleMonsterDamage, ApplyAoEDamage, the post-hit
    // riders): set for the length of an action by ActAs, Basic otherwise.
    private CombatEventKind _actKind = CombatEventKind.Basic;

    private readonly struct KindScope : IDisposable
    {
        private readonly CombatEngine _engine;
        private readonly CombatEventKind _previous;
        public KindScope(CombatEngine engine, CombatEventKind previous) { _engine = engine; _previous = previous; }
        public void Dispose() { if (_engine != null) _engine._actKind = _previous; }
    }

    /// <summary>Reports every hit inside the scope as <paramref name="kind"/>; the previous kind returns on dispose.</summary>
    private KindScope ActAs(CombatEventKind kind)
    {
        var scope = new KindScope(this, _actKind);
        _actKind = kind;
        return scope;
    }

    private void BeginFightEvents(CombatResult result)
    {
        _eventTally = CombatEventsEnabled ? result.Tally : null;
        _observerFailureLogged = false;
        ObserverFailureLogs = 0;
        _fightEventsStarted = false;
        _fellThisFight.Clear();
        _actKind = CombatEventKind.Basic;
    }

    /// <summary>Raised once, before the first blow: right before an ambush's free attacks, else at the
    /// first round.</summary>
    private void StartFightEvents(Character player, List<Monster> monsters, IEnumerable<Character>? teammates, bool monstersFirst)
    {
        if (_eventTally == null || _fightEventsStarted) return;
        _fightEventsStarted = true;
        try
        {
            int party = 1;
            if (teammates != null) foreach (var t in teammates) if (t != null && t.IsAlive) party++;
            int encounter = 0;
            foreach (var m in monsters) if (m != null && m.IsAlive) encounter++;
            var start = new CombatFightStart(MentalFightFloor(player), (int)player.Difficulty, party, encounter, monstersFirst ? 1 : 0);
            _eventTally.OnFightStart(start);
            var o = Observer;
            if (o != null) o.OnFightStart(start);
        }
        catch (Exception ex) { ObserverFailed(ex); }
    }

    /// <summary>Raised once when the fight's last round is over, before any outcome handling, so the
    /// player's HP is the HP the fight left.</summary>
    private void EndFightEvents(Character player)
    {
        var tally = _eventTally;
        if (tally == null || tally.Ended) return;
        try
        {
            long hp = player.HP;
            tally.OnFightEnd(hp);
            var o = Observer;
            if (o != null) o.OnFightEnd(hp);
        }
        catch (Exception ex) { ObserverFailed(ex); }
    }

    /// <summary>Test seam: the failure lines logged in the current or last fight (0 or 1).</summary>
    internal int ObserverFailureLogs { get; private set; }

    private void ObserverFailed(Exception ex)
    {
        if (_observerFailureLogged) return;
        _observerFailureLogged = true;
        ObserverFailureLogs++;
        DebugLogger.Instance.LogError("COMBAT_EVENTS", $"combat observer threw, the fight goes on: {ex.GetType().Name}: {ex.Message}");
    }

    private CombatSide SideOf(Character? c) =>
        c != null && ReferenceEquals(c, _combatOwner ?? currentPlayer) ? CombatSide.Player : CombatSide.Team;

    /// <summary>The one place an event leaves the engine. Reads only the values handed to it.</summary>
    private void RaiseCombatEvent(CombatEvent e)
    {
        var tally = _eventTally;
        if (tally == null) return;
        try { tally.OnEvent(e); }
        catch (Exception ex) { ObserverFailed(ex); }
        var o = Observer;
        if (o == null) return;
        try { o.OnEvent(e); }
        catch (Exception ex) { ObserverFailed(ex); }
    }

    /// <summary>Monster abilities counted as spells in the event stream; every other monster ability
    /// is an ability. Monsters have no separate spell action in play (Monster.DecideAction is unused).</summary>
    internal static readonly HashSet<MonsterAbilities.AbilityType> MonsterSpellAbilities = new()
    {
        MonsterAbilities.AbilityType.Spellcasting, MonsterAbilities.AbilityType.AncientMagic,
        MonsterAbilities.AbilityType.Fireball, MonsterAbilities.AbilityType.Hellfire,
        MonsterAbilities.AbilityType.Inferno, MonsterAbilities.AbilityType.Lightning,
        MonsterAbilities.AbilityType.HolySmite, MonsterAbilities.AbilityType.DivineJudgment,
        MonsterAbilities.AbilityType.Nightmare, MonsterAbilities.AbilityType.RealityBreak,
    };

    internal static CombatEventKind MonsterAbilityEventKind(MonsterAbilities.AbilityType type) =>
        MonsterSpellAbilities.Contains(type) ? CombatEventKind.Spell : CombatEventKind.Ability;

    /// <summary>What a heal of <paramref name="heal"/> restores to <paramref name="c"/> under its cap,
    /// read before the heal is applied.</summary>
    private static long HealRoom(Character c, long heal) => Math.Max(0, Math.Min(heal, c.MaxHP - c.HP));

    private bool Fell(Character? target) =>
        target != null && target.HP <= 0 && _fellThisFight.Add(target);

    /// <summary>The kind of the hits a player action makes: the plain attack is basic, a spell is a
    /// spell, a potion or herb is a potion, every other combat action (class abilities, Power Attack,
    /// Backstab, Smite and the rest, a Miracle) is an ability.</summary>
    internal static CombatEventKind ActionEventKind(CombatActionType type) => type switch
    {
        CombatActionType.Attack => CombatEventKind.Basic,
        CombatActionType.CastSpell => CombatEventKind.Spell,
        CombatActionType.Heal or CombatActionType.QuickHeal or CombatActionType.UseItem or CombatActionType.UseHerb => CombatEventKind.Potion,
        _ => CombatEventKind.Ability,
    };

    /// <summary>A player-side combatant hit a monster for <paramref name="amount"/>.</summary>
    private void EvHit(Character? attacker, long amount, CombatEventKind? kind = null)
    {
        if (_eventTally == null || amount <= 0) return;
        RaiseCombatEvent(new CombatEvent(SideOf(attacker), CombatSide.Monster, amount, kind ?? _actKind));
    }

    /// <summary>A monster (or a monster-side effect) hit a player-side combatant.</summary>
    private void EvHurt(Character target, long amount, CombatEventKind kind)
    {
        if (_eventTally == null || target == null) return;
        bool fell = Fell(target);
        if (amount <= 0 && !fell) return;
        RaiseCombatEvent(new CombatEvent(CombatSide.Monster, SideOf(target), Math.Max(0, amount), kind, TargetFell: fell));
    }

    /// <summary>A monster-side hit on a monster (confusion stumbles).</summary>
    private void EvMonsterSelf(long amount)
    {
        if (_eventTally == null || amount <= 0) return;
        RaiseCombatEvent(new CombatEvent(CombatSide.Monster, CombatSide.Monster, amount, CombatEventKind.Basic));
    }

    /// <summary>A heal on a player-side combatant, from a spell, ability (kind Heal) or potion.</summary>
    private void EvHeal(Character? healer, Character target, long amount, CombatEventKind kind = CombatEventKind.Heal)
    {
        if (_eventTally == null || target == null || amount <= 0) return;
        RaiseCombatEvent(new CombatEvent(healer == null ? SideOf(target) : SideOf(healer), SideOf(target), amount, kind));
    }

    /// <summary>One spell cast, ability use or potion drunk.</summary>
    private void EvUse(Character actor, CombatEventKind kind)
    {
        if (_eventTally == null || actor == null) return;
        var side = SideOf(actor);
        RaiseCombatEvent(new CombatEvent(side, side, 0, kind, IsUse: true));
    }
}
