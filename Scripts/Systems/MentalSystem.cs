using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems;

/// <summary>Mental health bands (v1.1.15). Display strings live elsewhere.</summary>
public enum MentalBand
{
    Stable,     // 75-100
    Strained,   // 50-74
    Shaken,     // 25-49
    Breaking,   // 1-24
    Broken      // 0
}

/// <summary>
/// v1.1.15: once-a-day Mental recovery sources, saved on Character.MentalRecoveryUsedToday and
/// cleared by MentalSystem.ApplyDailyReset. MentalSystem.TryDailyGain applies a source's gain and
/// marks it; InnTable and InnFriend share one daily use. HomeSleep covers online home sleep only.
/// </summary>
[Flags]
public enum MentalDailySource
{
    None = 0,
    InnTable = 1 << 0,
    InnFriend = 1 << 1,        // a friend present at the Inn table, the +8 variant
    Spouse = 1 << 2,
    TemplePrayer = 1 << 3,
    Confession = 1 << 4,
    FriendTalk = 1 << 5,
    Wilderness = 1 << 6,
    Learning = 1 << 7,
    WitnessLoss = 1 << 8,
    HomeRest = 1 << 9,
    HomeSleep = 1 << 10,      // online home sleep behind the reinforced door
}

/// <summary>
/// v1.1.15 Mental health core: bands, the addiction cap, capped gains and uncapped losses, dungeon
/// strain and the combat penalty. Pure logic, no UI. NPCs are skipped by Change and AddStrain.
/// </summary>
public static class MentalSystem
{
    /// <summary>Strain is kept in hundredths of a per mille, so one Mental point is this many units.</summary>
    public const int StrainUnitsPerPoint = GameConfig.MentalStrainPerPoint * 100;

    public static MentalBand GetBand(int mental)
    {
        if (mental >= GameConfig.MentalStableThreshold) return MentalBand.Stable;
        if (mental >= GameConfig.MentalStrainedThreshold) return MentalBand.Strained;
        if (mental >= GameConfig.MentalShakenThreshold) return MentalBand.Shaken;
        if (mental >= GameConfig.MentalBreakingThreshold) return MentalBand.Breaking;
        return MentalBand.Broken;
    }

    /// <summary>Highest Mental the character can hold: max minus half the addiction, in [0, max].</summary>
    public static int GetCap(Character c) =>
        Math.Clamp(GameConfig.MaxMentalStability - c.Addict / 2, 0, GameConfig.MaxMentalStability);

    /// <summary>
    /// Changes Mental by delta and returns the change actually applied (after minus before).
    /// A loss subtracts from the current value and floors at 0, ignoring the cap: Mental already
    /// above the cap (addiction rose later) still loses the full amount and can land above the cap.
    /// A gain stops at the cap and never lowers Mental already above it (a gain then applies 0).
    /// NPCs are skipped and return 0.
    /// </summary>
    public static int Change(Character c, int delta)
    {
        if (c == null || c.IsNPC) return 0;
        int before = c.Mental;
        int after;
        // The sum is taken in long so an absurd delta (int.MaxValue, int.MinValue) cannot wrap.
        if (delta > 0)
        {
            int cap = GetCap(c);
            after = before >= cap ? before : (int)Math.Min((long)before + delta, cap);
        }
        else
        {
            after = (int)Math.Max((long)before + delta, 0L);
        }
        c.Mental = after;
        return after - before;
    }

    /// <summary>
    /// Strain percentage for race and class: the race percent times the class percent, divided by
    /// 100 and rounded half up to a whole percent, computed as (racePct x classPct + 50) / 100 in
    /// integer math. Race: Troll, Orc, Gnoll, Mutant 80; Elf, Hobbit 120; HalfElf, Gnome 110;
    /// others 100. Class: Assassin, Abysswarden, Voidreaver 85; Barbarian 90; Cleric, Paladin,
    /// Tidesworn 90; Bard, Jester 110; Sage 85; others 100. Examples: Troll Sage 68, Elf Bard 132,
    /// HalfElf Sage 93.5 rounds to 94.
    /// </summary>
    public static int GetStrainPct(CharacterClass cls, CharacterRace race)
    {
        int racePct = race switch
        {
            CharacterRace.Troll or CharacterRace.Orc or CharacterRace.Gnoll or CharacterRace.Mutant => GameConfig.MentalStrainRacePctHardy,
            CharacterRace.Elf or CharacterRace.Hobbit => GameConfig.MentalStrainRacePctSensitive,
            CharacterRace.HalfElf or CharacterRace.Gnome => GameConfig.MentalStrainRacePctUneasy,
            _ => 100
        };
        int classPct = cls switch
        {
            CharacterClass.Assassin or CharacterClass.Abysswarden or CharacterClass.Voidreaver => GameConfig.MentalStrainClassPctDark,
            CharacterClass.Barbarian => GameConfig.MentalStrainClassPctBarbarian,
            CharacterClass.Cleric or CharacterClass.Paladin or CharacterClass.Tidesworn => GameConfig.MentalStrainClassPctDevout,
            CharacterClass.Bard or CharacterClass.Jester => GameConfig.MentalStrainClassPctPerformer,
            CharacterClass.Sage => GameConfig.MentalStrainClassPctSage,
            _ => 100
        };
        return (racePct * classPct + 50) / 100;
    }

    /// <summary>Companion cut in percent: 10 per story companion in the party, at most 20.</summary>
    public static int GetCompanionCutPct(int storyCompanionsInParty) =>
        Math.Clamp(storyCompanionsInParty * GameConfig.MentalStrainCompanionCutPct, 0, GameConfig.MentalStrainCompanionCutMaxPct);

    /// <summary>
    /// Adds dungeon strain in per mille and returns the Mental points lost (0 or more).
    /// Rounding: the strain is scaled to hundredths of a per mille as
    /// perMille x strainPct x (100 - companionCutPct) / 100, floored by integer division, and added
    /// to Character.MentalStrainRemainder. Each full 100_000 units (1000 per mille) costs 1 Mental;
    /// the rest carries to the next call. The loss goes through Change, so it ignores the cap: Mental
    /// already above the cap loses the full amount and can stay above the cap. Non-positive strain
    /// and NPCs do nothing.
    /// </summary>
    public static int AddStrain(Character c, int perMille, int storyCompanionsInParty)
    {
        if (c == null || c.IsNPC) return 0;
        int points = TakeStrainPoints(c, perMille, storyCompanionsInParty);
        if (points == 0) return 0;
        return -Change(c, -points);
    }

    /// <summary>Scales the strain, updates the remainder and returns the whole points it costs, without applying them.</summary>
    private static int TakeStrainPoints(Character c, int perMille, int storyCompanionsInParty)
    {
        if (perMille <= 0) return 0;
        long scaled = (long)perMille * GetStrainPct(c.Class, c.Race) * (100 - GetCompanionCutPct(storyCompanionsInParty)) / 100;
        long total = c.MentalStrainRemainder + scaled;
        c.MentalStrainRemainder = (int)(total % StrainUnitsPerPoint);
        return (int)Math.Min(total / StrainUnitsPerPoint, int.MaxValue);
    }

    /// <summary>Story companions (Lyris, Aldric, Mira, Vex, Melodia) alive in the party; dead ones do not count.</summary>
    public static int CountStoryCompanions(IEnumerable<Character>? party) =>
        party?.Count(t => t != null && t.IsAlive && t.IsCompanion && t.CompanionId.HasValue) ?? 0;

    /// <summary>At or below GameConfig.MentalNearDeathHpPct of max HP and still standing.</summary>
    public static bool IsNearDeath(Character c) =>
        c != null && c.MaxHP > 0 && c.HP > 0 && c.HP * 100 <= c.MaxHP * GameConfig.MentalNearDeathHpPct;

    /// <summary>Strain for entering a new dungeon room: floor x MentalRoomStrainPerFloor per mille. Returns points lost.</summary>
    public static int ApplyRoomStrain(Character c, int floor, int storyCompanionsInParty) =>
        floor <= 0 ? 0 : AddStrain(c, floor * GameConfig.MentalRoomStrainPerFloor, storyCompanionsInParty);

    /// <summary>Flat fight-end losses: flee, near death, and an Old God (which replaces the boss loss) or a boss.</summary>
    public static int GetFightEndFlatLoss(bool fled, bool nearDeath, bool boss, bool oldGod) =>
        (fled ? GameConfig.MentalFleeLoss : 0)
        + (nearDeath ? GameConfig.MentalNearDeathLoss : 0)
        + (oldGod ? GameConfig.MentalOldGodLoss : boss ? GameConfig.MentalBossLoss : 0);

    /// <summary>
    /// Monster fight end as one net change: the strain points (floor x MentalFightStrainPerFloor per
    /// mille, through the race x class multiplier and the companion cut) plus the flat losses, applied
    /// by a single Change. Floor 0 (outside the dungeon) adds no strain. Returns the change applied
    /// (0 or negative). NPCs are skipped. died adds MentalDeathLoss to the same change and drops
    /// flee and near death (used for a grouped follower who died in the leader's fight).
    /// </summary>
    public static int ApplyFightEnd(Character c, int floor, int storyCompanionsInParty, bool fled, bool nearDeath, bool boss, bool oldGod, bool died = false)
    {
        if (c == null || c.IsNPC) return 0;
        int points = floor <= 0 ? 0 : TakeStrainPoints(c, floor * GameConfig.MentalFightStrainPerFloor, storyCompanionsInParty);
        long loss = (long)points + (died ? GameConfig.MentalDeathLoss + GetFightEndFlatLoss(false, false, boss, oldGod) : GetFightEndFlatLoss(fled, nearDeath, boss, oldGod));
        if (loss <= 0) return 0;
        return Change(c, (int)-Math.Min(loss, int.MaxValue));
    }

    /// <summary>Death in a monster fight: MentalDeathLoss. Returns the change applied.</summary>
    public static int ApplyDeath(Character c) => Change(c, -GameConfig.MentalDeathLoss);

    /// <summary>
    /// Daily reset and returns the change actually applied. Clears MentalRecoveryUsedToday to
    /// None so tomorrow's once-a-day sources are available again. If Mental is above GetCap(c) it
    /// drops straight to the cap and the daily gain is skipped. Otherwise it gains
    /// GameConfig.MentalDailyReset through Change, which stops at the cap. NPCs are skipped and
    /// return 0. Called once per day from DailySystemManager.RunBasicDailyReset only.
    /// A pending drug high is kept: while the drug is active (OnDrugs) the surplus drops only to
    /// the cap plus MentalDrugBoost (at most MaxMentalStability). Order at the daily boundary: this
    /// reset runs first, then DrugSystem.ProcessDailyDrugEffects (ProcessPlayerDailyEvents or
    /// ProcessDailyEvents) wears the drug off and applies the crash from the kept high, so a crash
    /// is never lost to the surplus drop and never doubled by it.
    /// </summary>
    public static int ApplyDailyReset(Character c)
    {
        if (c == null || c.IsNPC) return 0;
        c.MentalRecoveryUsedToday = MentalDailySource.None;
        int cap = GetCap(c);
        int limit = c.OnDrugs && c.MentalDrugBoost > 0
            ? (int)Math.Min((long)cap + c.MentalDrugBoost, GameConfig.MaxMentalStability)
            : cap;
        if (c.Mental > limit)
        {
            int before = c.Mental;
            c.Mental = limit;
            return limit - before;
        }
        if (c.Mental > cap) return 0;
        return Change(c, GameConfig.MentalDailyReset);
    }

    /// <summary>Tolerance: the high's percent after uses counted uses, 100 less MentalDrugHighStepPct per extra use, at least MentalDrugHighMinPct.</summary>
    public static int GetDrugHighPct(int uses) =>
        (int)Math.Max(GameConfig.MentalDrugHighMinPct, 100L - (long)GameConfig.MentalDrugHighStepPct * (Math.Max(1, uses) - 1));

    /// <summary>
    /// The crash for a boost after uses counted uses: boost x (2 + 0.5 x (uses - 1)), rounded half
    /// up, in integer math as (boost x (CrashBaseHalves + CrashStepHalves x (uses - 1)) + 1) / 2.
    /// </summary>
    public static int GetDrugCrash(int boost, int uses)
    {
        if (boost <= 0) return 0;
        long halves = GameConfig.MentalDrugCrashBaseHalves + (long)GameConfig.MentalDrugCrashStepHalves * (Math.Max(1, uses) - 1);
        return (int)Math.Min(((long)boost * halves + 1) / 2, int.MaxValue);
    }

    /// <summary>
    /// A drug high on use. Tolerance first: a use within MentalDrugToleranceWindowDays of
    /// MentalLastDrugDay adds one to MentalDrugUses, any other use (or a day counter that went
    /// back) starts over at 1; MentalLastDrugDay becomes currentDay. The high is the base
    /// (MentalDrugHighStrongGain for DarkEssence and DemonBlood, else MentalDrugHighGain) times
    /// GetDrugHighPct / 100. It may pass the addiction cap but never MaxMentalStability (the only
    /// over-cap path besides RestoreFull), and the amount applied is added to MentalDrugBoost for
    /// the crash. Returns the change applied. NPCs are skipped and return 0.
    /// </summary>
    public static int ApplyDrugHigh(Character c, DrugType drug, int currentDay)
    {
        if (c == null || c.IsNPC) return 0;
        long since = (long)currentDay - c.MentalLastDrugDay;
        c.MentalDrugUses = c.MentalDrugUses > 0 && since >= 0 && since <= GameConfig.MentalDrugToleranceWindowDays
            ? c.MentalDrugUses + 1
            : 1;
        c.MentalLastDrugDay = currentDay;
        int baseHigh = drug == DrugType.DarkEssence || drug == DrugType.DemonBlood
            ? GameConfig.MentalDrugHighStrongGain
            : GameConfig.MentalDrugHighGain;
        int high = baseHigh * GetDrugHighPct(c.MentalDrugUses) / 100;
        int applied = Math.Clamp(high, 0, Math.Max(0, GameConfig.MaxMentalStability - c.Mental));
        c.Mental += applied;
        c.MentalDrugBoost = (int)Math.Min((long)c.MentalDrugBoost + applied, int.MaxValue);
        return applied;
    }

    /// <summary>
    /// The crash when a drug wears off: GetDrugCrash(MentalDrugBoost, MentalDrugUses) as a loss
    /// through Change (ignores the cap), then MentalDrugBoost is 0. Returns the change applied.
    /// NPCs are skipped and return 0.
    /// </summary>
    public static int ApplyDrugCrash(Character c)
    {
        if (c == null || c.IsNPC) return 0;
        int crash = GetDrugCrash(c.MentalDrugBoost, c.MentalDrugUses);
        c.MentalDrugBoost = 0;
        return crash > 0 ? Change(c, -crash) : 0;
    }

    /// <summary>Overdose: MentalOverdoseLoss. Returns the change applied.</summary>
    public static int ApplyOverdose(Character c) => Change(c, -GameConfig.MentalOverdoseLoss);

    /// <summary>A day of withdrawal: MentalWithdrawalLossPerSeverity x severity (Addict / 25). Returns the change applied.</summary>
    public static int ApplyWithdrawal(Character c, int severity) =>
        severity <= 0 ? 0 : Change(c, (int)-Math.Min((long)GameConfig.MentalWithdrawalLossPerSeverity * severity, int.MaxValue));

    /// <summary>True if source's bit is already set in the character's daily recovery-used flags.</summary>
    public static bool UsedToday(Character c, MentalDailySource source) =>
        c != null && (c.MentalRecoveryUsedToday & source) != 0;

    /// <summary>Sets source's bit in the character's daily recovery-used flags.</summary>
    public static void MarkUsed(Character c, MentalDailySource source)
    {
        if (c == null) return;
        c.MentalRecoveryUsedToday |= source;
    }

    /// <summary>
    /// A once-a-day recovery source: returns 0 if source (or any bit of sharedWith, a source that
    /// shares the same daily use) is already used today, else applies amount through Change (stops
    /// at the cap) and returns the change applied. The source is marked used only when something
    /// was applied, so a use at the cap is quiet and does not spend the day. NPCs and non-positive
    /// amounts return 0.
    /// </summary>
    public static int TryDailyGain(Character c, MentalDailySource source, int amount, MentalDailySource sharedWith = MentalDailySource.None)
    {
        if (c == null || c.IsNPC || amount <= 0) return 0;
        if (UsedToday(c, source | sharedWith)) return 0;
        int applied = Change(c, amount);
        if (applied > 0) MarkUsed(c, source);
        return applied;
    }

    /// <summary>
    /// True if a gain from source would apply now: not used today (with sharedWith) and Mental
    /// below the cap. Paid sources check this before charging and tell the player when false.
    /// </summary>
    public static bool GainAvailable(Character c, MentalDailySource source = MentalDailySource.None, MentalDailySource sharedWith = MentalDailySource.None) =>
        c != null && !c.IsNPC && c.Mental < GetCap(c) && (source == MentalDailySource.None || !UsedToday(c, source | sharedWith));

    /// <summary>
    /// An NPC friend for the Inn table: the player's relationship toward them is
    /// RelationFriendship (40) or better (lower is better), the same test the Inn uses to label a
    /// patron a close friend: RelationshipSystem.GetRelationshipStatus(player, npc) &lt;= GameConfig.RelationFriendship.
    /// </summary>
    public static bool IsFriend(Character player, Character npc) =>
        player != null && npc != null && !ReferenceEquals(player, npc)
        && RelationshipSystem.GetRelationshipStatus(player, npc) <= GameConfig.RelationFriendship;

    /// <summary>
    /// Partner time at Home counts for a spouse or a current lover (not friends with benefits or
    /// exes): RomanceTracker.GetRelationType(npcId) is Spouse or Lover. Both share the Spouse bit.
    /// </summary>
    public static bool IsPartner(string? npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return false;
        var type = RomanceTracker.Instance.GetRelationType(npcId);
        return type == RomanceRelationType.Spouse || type == RomanceRelationType.Lover;
    }

    /// <summary>
    /// Inn table rest: MentalInnFriendGain when any NPC friend is present, else MentalInnTableGain.
    /// InnTable and InnFriend share one daily use. Returns the change applied.
    /// </summary>
    public static int ApplyInnTable(Character c, IEnumerable<Character>? present)
    {
        bool friend = present?.Any(n => n != null && n.IsAlive && IsFriend(c, n)) == true;
        return friend
            ? TryDailyGain(c, MentalDailySource.InnFriend, GameConfig.MentalInnFriendGain, MentalDailySource.InnTable)
            : TryDailyGain(c, MentalDailySource.InnTable, GameConfig.MentalInnTableGain, MentalDailySource.InnFriend);
    }

    /// <summary>
    /// Talking with an NPC: MentalFriendTalkGain once a day (FriendTalk) when the NPC is a friend
    /// by IsFriend, the same test the Inn table uses. Anyone else gives 0 and leaves the day unspent.
    /// Returns the change applied.
    /// </summary>
    public static int ApplyFriendTalk(Character player, Character npc) =>
        IsFriend(player, npc) ? TryDailyGain(player, MentalDailySource.FriendTalk, GameConfig.MentalFriendTalkGain) : 0;

    /// <summary>First wilderness exploration of the day: MentalWildernessGain once a day (Wilderness). Returns the change applied.</summary>
    public static int ApplyWilderness(Character c) =>
        TryDailyGain(c, MentalDailySource.Wilderness, GameConfig.MentalWildernessGain);

    /// <summary>
    /// Learning: a new spell, a training session or Library reading. One Learning bit a day shared
    /// by all three; the first that applies wins. Returns the change applied.
    /// </summary>
    public static int ApplyLearning(Character c) =>
        TryDailyGain(c, MentalDailySource.Learning, GameConfig.MentalLearningGain);

    /// <summary>
    /// Healer talk therapy: sets Mental to GameConfig.MaxMentalStability even above the addiction
    /// cap (with drug highs, one of only two sources that may pass the cap) and clears the Broken
    /// affliction. Returns the change applied. NPCs are skipped and return 0.
    /// </summary>
    public static int RestoreFull(Character c)
    {
        if (c == null || c.IsNPC) return 0;
        int before = c.Mental;
        c.Mental = GameConfig.MaxMentalStability;
        c.MentalBroken = false;
        return c.Mental - before;
    }

    /// <summary>True if talk therapy has something to treat: Mental below the maximum, or the Broken affliction.</summary>
    public static bool NeedsTherapy(Character c) =>
        c != null && !c.IsNPC && (c.Mental < GameConfig.MaxMentalStability || c.MentalBroken);

    /// <summary>
    /// Talk therapy price in gold before the Healer's tax: the missing points (MaxMentalStability
    /// minus Mental, never below 0; at least MentalTherapyBrokenMinPoints while Broken) times
    /// (MentalTherapyCostBase + MentalTherapyCostPerLevel x Level), in long.
    /// </summary>
    public static long TherapyCost(Character c)
    {
        if (c == null) return 0;
        long missing = Math.Max(0L, (long)GameConfig.MaxMentalStability - c.Mental);
        if (c.MentalBroken)
            missing = Math.Max(missing, GameConfig.MentalTherapyBrokenMinPoints);
        long perPoint = GameConfig.MentalTherapyCostBase + (long)GameConfig.MentalTherapyCostPerLevel * Math.Max(0, c.Level);
        return missing * perPoint;
    }

    /// <summary>Willow Draught base price before the Healer's tax: MentalWillowPotionMultiplier healing potions at this level.</summary>
    public static long WillowDraughtPrice(int level) =>
        GameConfig.MentalWillowPotionMultiplier * GameConfig.GetHealingPotionCost(level);

    /// <summary>
    /// Drinks one Willow Draught: MentalWillowDraughtGain through Change (stops at the cap) and one
    /// draught used. With none carried, or Mental already at or above the cap, nothing is drunk and
    /// the count is kept. Returns the change applied. NPCs are skipped and return 0.
    /// </summary>
    public static int DrinkWillowDraught(Character c)
    {
        if (c == null || c.IsNPC || c.WillowDraughts <= 0) return 0;
        if (c.Mental >= GetCap(c)) return 0;
        c.WillowDraughts--;
        return Change(c, GameConfig.MentalWillowDraughtGain);
    }

    /// <summary>
    /// Healer rehab, called after the addiction is cleared (so the cap has already lifted): clears
    /// the Broken affliction and gains MentalRehabGain through Change. Returns the change applied.
    /// NPCs are skipped and return 0.
    /// </summary>
    public static int ApplyRehab(Character c)
    {
        if (c == null || c.IsNPC) return 0;
        c.MentalBroken = false;
        return Change(c, GameConfig.MentalRehabGain);
    }

    /// <summary>A story companion died and grief began: MentalCompanionGriefLoss. Returns the change applied.</summary>
    public static int ApplyCompanionGrief(Character c) => Change(c, -GameConfig.MentalCompanionGriefLoss);

    /// <summary>An NPC teammate, spouse or lover died and NPC grief began: MentalNpcGriefLoss. Returns the change applied.</summary>
    public static int ApplyNpcGrief(Character c) => Change(c, -GameConfig.MentalNpcGriefLoss);

    /// <summary>
    /// A grief entered a new stage: Depression loses MentalGriefDepressionLoss, Acceptance gains
    /// MentalGriefAcceptanceGain through Change (stops at the cap). Other stages change nothing.
    /// Returns the change applied.
    /// </summary>
    public static int ApplyGriefStage(Character c, GriefStage stage) => stage switch
    {
        GriefStage.Depression => Change(c, -GameConfig.MentalGriefDepressionLoss),
        GriefStage.Acceptance => Change(c, GameConfig.MentalGriefAcceptanceGain),
        _ => 0
    };

    /// <summary>
    /// Witnessing a town NPC death or a world disaster: MentalWitnessLoss at most once a day
    /// (WitnessLoss, cleared by ApplyDailyReset). The day is spent on the first witness even at 0.
    /// Returns the change applied (0 or negative). NPCs are skipped and return 0.
    /// </summary>
    public static int ApplyWitnessLoss(Character c)
    {
        if (c == null || c.IsNPC || UsedToday(c, MentalDailySource.WitnessLoss)) return 0;
        MarkUsed(c, MentalDailySource.WitnessLoss);
        return Change(c, -GameConfig.MentalWitnessLoss);
    }

    /// <summary>
    /// Combat penalty from Mental alone, as a positive fraction of damage and defence lost:
    /// 0 for Stable and Strained, 0.05 for Shaken, 0.10 for Breaking and Broken.
    /// Taking the worse of this and Grief, and the -15% Mental plus Fatigue cap, belong to a later
    /// piece (band effects), not here.
    /// </summary>
    public static float GetCombatPenalty(int mental) => GetBand(mental) switch
    {
        MentalBand.Shaken => GameConfig.MentalShakenCombatPenalty,
        MentalBand.Breaking or MentalBand.Broken => GameConfig.MentalBreakingCombatPenalty,
        _ => 0f
    };
}
