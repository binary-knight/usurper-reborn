using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems;

/// <summary>
/// 1.2.0 Temple gods piece 3: an act in the game that a god's domain cares about. Each call site
/// records the act once (GodDeedSystem.Record); the table in GodDeedSystem says which domains gain
/// or lose Favor from it.
/// </summary>
public enum GodAct
{
    // Deeds
    UndeadSlain,          // Light: an undead or demon slain
    StrongerFoeBeaten,    // War: a fight won against a higher-level foe
    AllyHealed,           // Love: an ally healed in combat
    Marriage,             // Love deed, Chaos taboo
    FriendMade,           // Love: a new friendship
    BountyCollected,      // Law: a bounty paid out
    ArrestOrdered,        // Law: the crown sends someone to prison
    Theft,                // Shadow deed, Law taboo
    StealthKill,          // Shadow: a foe killed by a backstab
    HerbGathered,         // Earth: an herb gathered from the garden
    SettlementWork,       // Earth: gold given to the settlement's building
    DeathWitnessed,       // Death: a death seen or grieved
    SpellLearned,         // Magic: a new spell learned
    LibraryRead,          // Magic: reading at the settlement library
    WildernessExplored,   // Nature: a wilderness expedition
    PvpWin,               // Chaos: an arena fight won against a player
    StreetBrawl,          // Chaos: a street brawl won
    // Taboos
    DrugUse,              // Light
    Fled,                 // War: fled a monster fight
    Murder,               // Love and Law
    Imprisoned,           // Law: sent to prison
    Confession,           // Shadow: confessing sins
    Desecration,          // Earth: an altar desecrated
    UndeadRaised,         // Death: raising the dead with a scroll
    NoCastWeek,           // Magic: a spellcaster who cast nothing for GodTabooNoCastDays daily resets
}

/// <summary>
/// 1.2.0 Temple gods piece 3: deeds and taboos. A deed of the worshipped god's domain gains Favor
/// (FavorSource.Deed, at most GodFavorDeedDailyCap a day, and it counts as devotion for neglect);
/// a taboo of that domain loses Favor. The domain is GodBoonSystem.GetDomain: a canon god's own,
/// or the domain a player-god's immortal chose. Every change goes through FavorSystem, so a tier
/// crossing is handled there.
/// </summary>
public static class GodDeedSystem
{
    /// <summary>What each act is worth to each domain (positive a deed, negative a taboo).</summary>
    private static readonly Dictionary<GodAct, (GodDomain Domain, int Delta)[]> Table = new()
    {
        [GodAct.UndeadSlain] = new[] { (GodDomain.Light, GameConfig.GodDeedMinor) },
        [GodAct.StrongerFoeBeaten] = new[] { (GodDomain.War, GameConfig.GodDeedMinor) },
        [GodAct.AllyHealed] = new[] { (GodDomain.Love, GameConfig.GodDeedMinor) },
        [GodAct.Marriage] = new[] { (GodDomain.Love, GameConfig.GodDeedMajor), (GodDomain.Chaos, -GameConfig.GodTabooMajor) },
        [GodAct.FriendMade] = new[] { (GodDomain.Love, GameConfig.GodDeedMinor) },
        [GodAct.BountyCollected] = new[] { (GodDomain.Law, GameConfig.GodDeedMajor) },
        [GodAct.ArrestOrdered] = new[] { (GodDomain.Law, GameConfig.GodDeedMajor) },
        [GodAct.Theft] = new[] { (GodDomain.Shadow, GameConfig.GodDeedMinor), (GodDomain.Law, -GameConfig.GodTabooMinor) },
        [GodAct.StealthKill] = new[] { (GodDomain.Shadow, GameConfig.GodDeedMinor) },
        [GodAct.HerbGathered] = new[] { (GodDomain.Earth, GameConfig.GodDeedMinor) },
        [GodAct.SettlementWork] = new[] { (GodDomain.Earth, GameConfig.GodDeedMinor) },
        [GodAct.DeathWitnessed] = new[] { (GodDomain.Death, GameConfig.GodDeedMinor) },
        [GodAct.SpellLearned] = new[] { (GodDomain.Magic, GameConfig.GodDeedMajor) },
        [GodAct.LibraryRead] = new[] { (GodDomain.Magic, GameConfig.GodDeedMinor) },
        [GodAct.WildernessExplored] = new[] { (GodDomain.Nature, GameConfig.GodDeedMinor) },
        [GodAct.PvpWin] = new[] { (GodDomain.Chaos, GameConfig.GodDeedMajor) },
        [GodAct.StreetBrawl] = new[] { (GodDomain.Chaos, GameConfig.GodDeedMinor) },
        [GodAct.DrugUse] = new[] { (GodDomain.Light, -GameConfig.GodTabooMinor) },
        [GodAct.Fled] = new[] { (GodDomain.War, -GameConfig.GodTabooMinor) },
        [GodAct.Murder] = new[] { (GodDomain.Love, -GameConfig.GodTabooGrave), (GodDomain.Law, -GameConfig.GodTabooMajor) },
        [GodAct.Imprisoned] = new[] { (GodDomain.Law, -GameConfig.GodTabooMajor) },
        [GodAct.Confession] = new[] { (GodDomain.Shadow, -GameConfig.GodTabooMajor) },
        [GodAct.Desecration] = new[] { (GodDomain.Earth, -GameConfig.GodTabooGrave) },
        [GodAct.UndeadRaised] = new[] { (GodDomain.Death, -GameConfig.GodTabooMajor) },
        [GodAct.NoCastWeek] = new[] { (GodDomain.Magic, -GameConfig.GodTabooMajor) },
    };

    /// <summary>What an act is worth to a domain before the daily cap: above 0 a deed, below 0 a taboo, 0 nothing.</summary>
    public static int Worth(GodAct act, GodDomain domain)
    {
        if (domain == GodDomain.None || !Table.TryGetValue(act, out var rows)) return 0;
        foreach (var (d, delta) in rows)
            if (d == domain) return delta;
        return 0;
    }

    /// <summary>
    /// Applies an act to the character's Favor with their god: a deed of the god's domain gains up
    /// to its worth (never past the daily deed cap) and counts as devotion; a taboo loses its worth.
    /// NPCs and characters with no god or no domain get 0. Returns the Favor change applied.
    /// </summary>
    public static int Apply(Character c, GodAct act, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return 0;
        int worth = Worth(act, GodBoonSystem.GetDomain(c, gods));
        if (worth > 0)
        {
            FavorSystem.MarkDevotion(c);
            return FavorSystem.GainCapped(c, FavorSource.Deed, worth, GameConfig.GodFavorDeedDailyCap, gods);
        }
        return worth < 0 ? FavorSystem.Change(c, worth, gods) : 0;
    }

    /// <summary>
    /// The hook each call site uses: Apply, then the Favor line (FavorUi.ReportChange) on the given
    /// terminal, or on the session's own terminal when the character is the session's player.
    /// Returns the Favor change applied.
    /// </summary>
    public static int Record(Character? c, GodAct act, TerminalEmulator? terminal = null, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return 0;
        int applied = Apply(c, act, gods);
        if (applied != 0)
        {
            var engine = GameEngine.Instance;
            var term = terminal ?? (engine != null && ReferenceEquals(c, engine.CurrentPlayer) ? engine.Terminal : null);
            FavorUi.ReportChange(term, c, applied, gods);
        }
        return applied;
    }

    /// <summary>
    /// A monster fight won: UndeadSlain when an undead or demon was among the slain
    /// (DivineBlessingSystem.IsUndeadOrDemon), StrongerFoeBeaten when one was above the player's
    /// level. Each at most once per fight. Called from CombatEngine.HandleVictoryMultiMonster.
    /// </summary>
    public static void RecordVictory(Character? player, IEnumerable<Monster>? defeated, TerminalEmulator? terminal = null, GodSystem? gods = null)
    {
        if (player == null || player.IsNPC || defeated == null) return;
        var slain = defeated.Where(m => m != null).ToList();
        if (slain.Any(DivineBlessingSystem.IsUndeadOrDemon)) Record(player, GodAct.UndeadSlain, terminal, gods);
        if (slain.Any(m => m.Level > player.Level)) Record(player, GodAct.StrongerFoeBeaten, terminal, gods);
    }

    /// <summary>A spell cast: the Magic taboo count starts over. Called from SpellSystem.CastSpell.</summary>
    public static void MarkSpellCast(Character c)
    {
        if (c == null || c.IsNPC) return;
        c.DaysSinceSpellCast = 0;
    }

    /// <summary>
    /// Daily reset: a spellcaster counts one more day without a cast; at GodTabooNoCastDays the
    /// NoCastWeek taboo applies once and the count starts over. Other classes are not counted (they
    /// cannot cast). Called once per day from DailySystemManager.RunBasicDailyReset, after the Favor
    /// reset. Returns the Favor change applied.
    /// </summary>
    public static int ApplyDailyReset(Character c, TerminalEmulator? terminal = null, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC || !ClassAbilitySystem.IsSpellcaster(c.Class)) return 0;
        if (c.DaysSinceSpellCast < int.MaxValue) c.DaysSinceSpellCast++;
        if (c.DaysSinceSpellCast < GameConfig.GodTabooNoCastDays) return 0;
        c.DaysSinceSpellCast = 0;
        return Record(c, GodAct.NoCastWeek, terminal, gods);
    }
}
