using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// v0.64.0 Brain v2 Slice 2: goal-driven utility scorer.
///
/// Consumes the picker's modifier-chain output (post personality, time-of-day,
/// memory, relationships, neighbor pressure, world events, memes, role weights)
/// as a baseline, then layers in:
///   1. Goal alignment -- top-priority goal from NPCBrain.Goals boosts the
///      verbs that advance it.
///   2. Need satisfaction -- HP, gold, XP-ready-for-levelup, gear gap each
///      promote the obvious verb.
///   3. Recency penalty -- discourages doing the same verb N ticks in a row
///      so NPCs don't lock into a single behavior.
///   4. Argmax pick -- highest score wins. Personality.Impulsiveness gates a
///      small random-pick override for behavioral variety.
///
/// This is a pure function over (NPC, candidates, RNG). No side effects on
/// the NPC. Called from WorldSimulator.BrainV2ProcessActivities once per tick
/// for IsAIDriven=true NPCs.
/// </summary>
public static class BrainV2Scorer
{
    /// <summary>
    /// Pick the highest-utility action from the picker-built candidate list.
    /// Returns the chosen verb string. Always returns a non-null verb; if
    /// candidates is empty, returns "move" as a safe default.
    /// </summary>
    public static string PickAction(NPC npc, List<(string action, double weight)> candidates, Random random)
    {
        if (candidates == null || candidates.Count == 0) return "move";

        // Score every candidate by layering goal / need / recency on top of the
        // picker's base weight. Clamp to a minimum so any candidate that passed
        // the picker's gates is at least eligible.
        // Combat-outcome feedback is read ONCE per pick (it's the same per
        // verb-family across all candidates), then applied selectively below.
        double combatLossPenalty = RecentCombatLossPenalty(npc);

        var scored = new List<(string verb, double score)>(candidates.Count);
        foreach (var (verb, baseWeight) in candidates)
        {
            double goal = GoalAlignment(verb, npc);
            double need = NeedSatisfaction(verb, npc);
            double recencyPenalty = RecencyPenalty(verb, npc);
            double combatFeedback = IsCombatVerb(verb) ? combatLossPenalty : 1.0;

            double score = Math.Max(0.001, baseWeight * goal * need * recencyPenalty * combatFeedback);
            scored.Add((verb, score));
        }

        // Argmax with stochasticity.
        double impulsiveness = npc.Brain?.Personality?.Impulsiveness ?? 0.0f;
        if (impulsiveness > 0.7f && random.NextDouble() < 0.2)
        {
            // Highly impulsive NPCs occasionally pick something other than the
            // top-scoring action. Sample weighted-random over the full set so the
            // pick is still biased toward higher utility, just not strictly argmax.
            double total = scored.Sum(s => s.score);
            double roll = random.NextDouble() * total;
            double cumulative = 0;
            foreach (var (verb, score) in scored)
            {
                cumulative += score;
                if (roll <= cumulative) return verb;
            }
        }

        // Strict argmax pick. Ties broken by candidate order (which is the order
        // the picker added them -- shop before train before levelup etc.).
        return scored.OrderByDescending(s => s.score).First().verb;
    }

    /// <summary>
    /// How much this verb advances the NPC's current top-priority goal.
    /// Returns a multiplier in roughly [0.6, 3.0]. The default is 1.0 (verb
    /// neither helps nor hurts the goal).
    /// </summary>
    private static double GoalAlignment(string verb, NPC npc)
    {
        var topGoal = npc.Brain?.Goals?.GetPriorityGoal();
        if (topGoal == null) return 1.0;

        // Goal-type buckets: each goal type lifts a small family of verbs.
        double typeBoost = topGoal.Type switch
        {
            GoalType.Economic => verb switch
            {
                "shop"        => 1.5,
                "marketplace" => 1.6,
                "bank"        => 1.4,
                "dark_alley"  => 1.3,
                "train"       => 0.9,
                "inn"         => 0.8,
                _             => 1.0,
            },
            GoalType.Social => verb switch
            {
                "inn"          => 1.5,
                "love_street"  => 1.4,
                "team_recruit" => 1.5,
                "go_home"      => 1.6,
                "castle"       => 1.2,
                "settlement"   => 1.3,
                "team_dungeon" => 1.2,
                "dungeon"      => 0.8,
                _              => 1.0,
            },
            GoalType.Personal => verb switch
            {
                "train"   => 1.6,
                "levelup" => 2.0,
                "heal"    => 1.5,
                "temple"  => 1.4,
                "inn"     => 1.1,
                "shop"    => 0.9,
                _         => 1.0,
            },
            GoalType.Combat => verb switch
            {
                "dungeon"      => 1.8,
                "team_dungeon" => 1.7,
                "train"        => 1.3,
                "dark_alley"   => 1.2,
                "shop"         => 1.1,
                "heal"         => 1.4,
                _              => 1.0,
            },
            _ => 1.0,
        };

        // Name-based fine-grained boosts. The seeded archetype goals from
        // NPCBrain.InitializeShoppingBehavior / RelationshipBehavior /
        // GangBehavior have specific names that map cleanly to specific verbs.
        double nameBoost = 1.0;
        string goalName = topGoal.Name ?? "";
        if (goalName.Contains("Weapon") && verb == "shop") nameBoost = 1.4;
        else if (goalName.Contains("Equipment") && verb == "shop") nameBoost = 1.3;
        else if (goalName.Contains("Magic Item") && verb == "shop") nameBoost = 1.3;
        else if (goalName.Contains("Mana Potion") && verb == "shop") nameBoost = 1.3;
        else if (goalName.Contains("Life Partner") && verb == "love_street") nameBoost = 1.4;
        else if (goalName.Contains("Life Partner") && verb == "inn") nameBoost = 1.2;
        else if (goalName.Contains("Make Friends") && verb == "inn") nameBoost = 1.3;
        else if (goalName.Contains("Gang") && verb == "team_recruit") nameBoost = 1.5;
        else if (goalName.Contains("Defend Territory") && verb == "team_dungeon") nameBoost = 1.3;
        else if (goalName.Contains("Health") && verb == "heal") nameBoost = 1.5;
        else if (goalName.Contains("Help Others") && (verb == "temple" || verb == "settlement")) nameBoost = 1.3;
        else if (goalName.Contains("Spread Faith") && verb == "temple") nameBoost = 1.4;
        else if (goalName.Contains("Serve") && verb == "temple") nameBoost = 1.3;

        return typeBoost * nameBoost;
    }

    /// <summary>
    /// How urgently the NPC's current state demands this verb. Returns a
    /// multiplier roughly in [0.3, 5.0]. Strong needs (low HP, levelup ready)
    /// dwarf the goal layer; mild needs add gentle boosts.
    /// </summary>
    private static double NeedSatisfaction(string verb, NPC npc)
    {
        double mult = 1.0;
        double hpFrac = npc.MaxHP > 0 ? (double)npc.HP / npc.MaxHP : 1.0;

        // HP-driven needs.
        if (hpFrac < 0.3)
        {
            // Urgent: very wounded NPCs prioritize survival over anything else.
            if (verb == "heal") mult *= 5.0;
            else if (verb == "inn") mult *= 2.0;
            else if (verb == "temple") mult *= 1.5;
            else if (verb == "dungeon" || verb == "team_dungeon")
                mult *= 0.15;  // refuse to fight while bleeding out
        }
        else if (hpFrac < 0.5)
        {
            if (verb == "heal") mult *= 2.5;
            else if (verb == "inn") mult *= 1.4;
            else if (verb == "dungeon" || verb == "team_dungeon")
                mult *= 0.5;
        }
        else if (hpFrac < 0.7)
        {
            if (verb == "heal") mult *= 1.3;
            else if (verb == "inn") mult *= 1.2;
        }

        // Level-up readiness: an NPC sitting on enough XP for the next level
        // and capable of spending should level up FAST. Picker already weights
        // levelup at 2.0; scorer adds a need-driven multiplier on top.
        if (verb == "levelup")
        {
            long expForNext = GameConfig.GetExperienceForLevel(npc.Level + 1);
            if (npc.Experience >= expForNext && npc.Level < 100)
                mult *= 3.0;
            else
                mult *= 0.1;  // not eligible, don't pick (defensive; picker should have gated)
        }

        // Gear gap. v0.64.0 Slice 6: reads LIVE WeapPow/ArmPow (gear-influenced
        // after RecalculateStats) not Base* (spawn-time intrinsic, never grows).
        // Expected baseline is well above intrinsic spawn (Level*5/4) so freshly-
        // spawned immigrants with only intrinsic gear DO register a meaningful
        // gap and shop aggressively until their live values climb past the
        // threshold. Once well-geared (live WeapPow >= Level*10), shop boost
        // turns off and the scorer's other layers (goal alignment, need) drive
        // verb selection. Closes the Slice 2 promise: NPCs detect their own
        // under-gearing and resolve it.
        if (verb == "shop")
        {
            // Weapon gap. Target: ~Level*12 for a well-geared NPC (intrinsic
            // ~Level*5 plus equipped weapon ~Level*7). Under-geared at < Level*7;
            // critically under-geared at < Level*4.
            long expectedWeapPow = npc.Level * 12;
            if (npc.WeapPow < expectedWeapPow * 0.4 && npc.Gold > 100)
                mult *= 1.8;  // critically under-geared and has gold to spend
            else if (npc.WeapPow < expectedWeapPow * 0.7 && npc.Gold > 200)
                mult *= 1.3;  // moderately under-geared

            // Armor gap. Mirror shape. Target: ~Level*10 well-geared (intrinsic
            // ~Level*4 plus equipped armor pieces ~Level*6).
            long expectedArmPow = npc.Level * 10;
            if (npc.ArmPow < expectedArmPow * 0.4 && npc.Gold > 100)
                mult *= 1.5;  // smaller boost than weapon (weapon matters more
                              // for win rate, armor for survival)
            else if (npc.ArmPow < expectedArmPow * 0.7 && npc.Gold > 200)
                mult *= 1.2;
        }

        // Bank withdrawal: NPC running low on cash with deposits available
        // should hit the bank harder than the picker's baseline.
        if (verb == "bank" && npc.Gold < 100 && npc.BankGold > 0)
            mult *= 2.0;

        // Training: NPC whose level is climbing through the early game (when
        // proficiencies matter most) should train more.
        if (verb == "train" && npc.Level < 25 && npc.Gold > 50)
            mult *= 1.3;

        return mult;
    }

    /// <summary>
    /// Penalize verbs the NPC just did. Without this, the scorer can lock onto
    /// a single high-utility verb and never deviate, producing boring NPCs.
    /// Recency tracked via NPCBrain.lastActivities (DateTime per verb). Returns
    /// a multiplier in [0.3, 1.0].
    /// </summary>
    /// <summary>
    /// v0.64.0 Brain v2 Slice 3: combat-outcome feedback. If the NPC has been
    /// recently attacked (NPC-vs-NPC street fight, faction ambush, gang war),
    /// they should think twice before walking back into another fight. Reads
    /// `MemoryType.Attacked` events from the past 4 hours. Recent + important
    /// = stronger penalty. Returns a multiplier in [0.3, 1.0] applied to
    /// combat-flavored verbs only (dungeon, team_dungeon, dark_alley,
    /// castle on the "challenge throne" path).
    ///
    /// Dungeon-loss outcomes (NPC died to a monster in NPCExploreDungeon /
    /// NPCTeamDungeonRun) don't currently write Attacked memories -- that
    /// gap is left for Slice 4 (real CombatEngine for Tier A NPCs, where
    /// loss attribution becomes structured). For Slice 3 we cover the
    /// NPC-vs-NPC outcomes which DO write Attacked via WorldSimulator
    /// SimulateNPCAttack -> NPCBrain.RecordInteraction.
    /// </summary>
    private static double RecentCombatLossPenalty(NPC npc)
    {
        var memory = npc.Brain?.Memory;
        if (memory == null) return 1.0;

        var recent = memory.GetMemoriesOfType(MemoryType.Attacked)
            .Where(m => m.IsRecent(4)) // past 4 hours
            .ToList();
        if (recent.Count == 0) return 1.0;

        // Score by aggregate importance. Each recent attacked memory worth
        // its Importance value; total > 1.0 = "I've been in real fights
        // recently" = heavy penalty.
        double totalImportance = recent.Sum(m => (double)m.Importance);
        if (totalImportance >= 1.0) return 0.3;   // multiple recent attacks
        if (totalImportance >= 0.5) return 0.5;   // one heavy attack
        return 0.7;                                // one minor attack
    }

    /// <summary>
    /// Verbs whose primary outcome is a combat fight. These get the
    /// RecentCombatLossPenalty multiplier; non-combat verbs ignore it.
    /// </summary>
    private static bool IsCombatVerb(string verb)
    {
        switch (verb)
        {
            case "dungeon":
            case "team_dungeon":
            case "dark_alley":  // pit fights, fence muggings
            case "castle":      // royal guard / throne challenges
                return true;
            default:
                return false;
        }
    }

    private static double RecencyPenalty(string verb, NPC npc)
    {
        var elapsed = npc.Brain?.TimeSinceActivity(verb);
        if (!elapsed.HasValue) return 1.0;  // never done before

        // Within 5 minutes: heavy penalty (NPC just did this).
        if (elapsed.Value.TotalMinutes < 5) return 0.3;
        // Within 30 minutes: moderate penalty.
        if (elapsed.Value.TotalMinutes < 30) return 0.6;
        // Within 2 hours: mild discount.
        if (elapsed.Value.TotalHours < 2) return 0.85;
        return 1.0;
    }
}
