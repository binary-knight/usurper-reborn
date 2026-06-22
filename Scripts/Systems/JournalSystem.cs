using System;
using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Data;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems;

/// <summary>
/// v0.64.2: The Adventurer's Journal -- one key (/journal) that answers
/// "what should I do now?" from anywhere.
///
/// Design (game-designer pass, 2026-06): this is a VISIBILITY feature, not a
/// content feature. Almost every piece of "what's next" data already exists
/// in the engine -- active quests, merc-contract readiness, companion quest
/// availability, story breadcrumbs, unspent training points, claimable
/// dailies -- but it's scattered across 15+ pull-based commands and one-shot
/// notifications the player must already know about. The journal aggregates
/// it into one priority-ordered screen. The only genuinely NEW logic is the
/// next-step recommendation ladder below.
///
/// Architecture: pure static reads over live state (Character + existing
/// singletons). ZERO new save fields -- same derived-live pattern as
/// Dread/Renown/Dynasty standing. Every rung and section builder is
/// independently guarded so a broken subsystem degrades to a missing line,
/// never a broken journal. Works in single-player, online, and BBS (no LLM,
/// no DB reads on this path).
/// </summary>
public static class JournalSystem
{
    /// <summary>
    /// A recommended next step: a loc key plus its format args, resolved at
    /// display time in the viewer's session language. Tests assert on LocKey.
    /// </summary>
    public sealed class JournalNextStep
    {
        public string LocKey { get; init; } = "";
        public object[] Args { get; init; } = Array.Empty<object>();
    }

    // Old God boss floors in ascending order (Manwe excluded -- his floor-100
    // encounter is the ending sequence, not a "go fight him" recommendation;
    // mirrors GetNextUnencounteredGod's exclusion in DungeonLocation).
    private static readonly (OldGodType God, int Floor)[] GodFloors =
    {
        (OldGodType.Maelketh, 25),
        (OldGodType.Veloura,  40),
        (OldGodType.Thorgrim, 55),
        (OldGodType.Noctura,  70),
        (OldGodType.Aurelion, 85),
        (OldGodType.Terravok, 95),
    };

    // Seal floors (mirrors DungeonLocation.SealFloors).
    private static readonly int[] SealFloors = { 15, 30, 45, 60, 80, 99 };

    /// <summary>
    /// The next-step recommendation ladder. First match wins. Ordered from
    /// "urgent and concrete" down to "default direction". Every rung that
    /// touches a singleton is guarded -- on any failure the ladder simply
    /// falls through to the next rung.
    /// </summary>
    public static JournalNextStep GetNextStep(Character player)
    {
        if (player == null)
            return new JournalNextStep { LocKey = "journal.next_delve" };

        // 1. Brand-new character: the bounce-cliff arrow. Permanent (unlike
        //    the one-shot HintSystem tips) until the first kill lands.
        if (player.Level <= 1 && player.MKills <= 0)
            return new JournalNextStep { LocKey = "journal.next_first_fight" };

        // 2. Critically hurt with no potions: don't recommend anything else
        //    while the player is one bad fight from a resurrection counter.
        if (player.MaxHP > 0 && player.HP < player.MaxHP / 4 && player.Healing <= 0)
            return new JournalNextStep { LocKey = "journal.next_heal" };

        // 3. Level-up banked (auto-level off, so it won't fire on its own).
        try
        {
            if (!player.AutoLevelUp && player.Level < 100
                && player.Experience >= GameConfig.GetExperienceForLevel(player.Level + 1))
                return new JournalNextStep { LocKey = "journal.next_level_up" };
        }
        catch { }

        // 4. A quest ready to turn in beats starting anything new.
        try
        {
            var quests = QuestSystem.GetActiveQuestsForPlayer(player.Name2 ?? player.DisplayName ?? "");
            var ready = quests?.FirstOrDefault(q => q.AreAllObjectivesComplete());
            if (ready != null)
            {
                return ready.IsMercContract
                    ? new JournalNextStep { LocKey = "journal.next_merc_turn_in", Args = new object[] { ready.GetDisplayTitle() } }
                    : new JournalNextStep { LocKey = "journal.next_turn_in", Args = new object[] { ready.GetDisplayTitle() } };
            }
        }
        catch { }

        // 5. Unspent training points.
        if (player.TrainingPoints > 0)
            return new JournalNextStep { LocKey = "journal.next_training", Args = new object[] { player.TrainingPoints } };

        // 6. A companion's personal quest is waiting.
        try
        {
            var comp = CompanionSystem.Instance?.GetRecruitedCompanions()?
                .FirstOrDefault(c => c.PersonalQuestAvailable && !c.PersonalQuestCompleted);
            if (comp != null)
                return new JournalNextStep { LocKey = "journal.next_companion_quest", Args = new object[] { comp.Name, comp.PersonalQuestName } };
        }
        catch { }

        // 7. Story progression: the next seal or Old God within the player's
        //    accessible floor band (level +/- 10). Whichever floor is lower.
        try
        {
            int maxFloor = Math.Min(100, player.Level + 10);
            int? sealFloor = NextSealFloor(player);
            var god = NextUnresolvedGod();

            bool sealInBand = sealFloor.HasValue && sealFloor.Value <= maxFloor;
            bool godInBand = god.HasValue && god.Value.Floor <= maxFloor;

            if (sealInBand && (!godInBand || sealFloor!.Value <= god!.Value.Floor))
                return new JournalNextStep { LocKey = "journal.next_seal", Args = new object[] { sealFloor!.Value } };
            if (godInBand)
            {
                string godName = OldGodsData.GetGodBossData(god!.Value.God)?.Name ?? god.Value.God.ToString();
                return new JournalNextStep { LocKey = "journal.next_god", Args = new object[] { godName, god.Value.Floor } };
            }
        }
        catch { }

        // 8. First incomplete objective of the oldest active quest.
        try
        {
            var quests = QuestSystem.GetActiveQuestsForPlayer(player.Name2 ?? player.DisplayName ?? "");
            var oldest = quests?.FirstOrDefault();
            var obj = oldest?.Objectives?.FirstOrDefault(o => !o.IsComplete && !o.IsOptional);
            if (oldest != null && obj != null)
                return new JournalNextStep
                {
                    LocKey = "journal.next_quest_objective",
                    Args = new object[] { obj.GetDisplayDescription(), oldest.GetDisplayTitle() }
                };
        }
        catch { }

        // 9. Default: delve. Resume the remembered floor when there is one.
        if (player.LastDungeonFloor > 0)
            return new JournalNextStep { LocKey = "journal.next_delve_resume", Args = new object[] { player.LastDungeonFloor } };
        return new JournalNextStep { LocKey = "journal.next_delve" };
    }

    /// <summary>
    /// IN PROGRESS section: active quests with objective progress, merc
    /// contracts ready to turn in, companion personal quests pending.
    /// Returns (text, color) pairs ready to print; empty list = skip section.
    /// </summary>
    public static List<(string Text, string Color)> BuildInProgressLines(Character player)
    {
        var lines = new List<(string, string)>();
        if (player == null) return lines;

        try
        {
            var quests = QuestSystem.GetActiveQuestsForPlayer(player.Name2 ?? player.DisplayName ?? "");
            if (quests != null)
            {
                foreach (var quest in quests.Take(5))
                {
                    bool ready = quest.AreAllObjectivesComplete();
                    if (ready)
                    {
                        lines.Add((Loc.Get(quest.IsMercContract ? "journal.line_merc_ready" : "journal.line_quest_ready",
                            quest.GetDisplayTitle()), "bright_green"));
                        continue;
                    }
                    lines.Add(($"{quest.GetDisplayTitle()} ({Loc.Get("base.quest_days_left", quest.DaysRemaining)})", "bright_yellow"));
                    if (quest.Objectives != null)
                    {
                        foreach (var obj in quest.Objectives.Where(o => !o.IsComplete).Take(3))
                        {
                            lines.Add(($"  [ ] {obj.GetDisplayDescription()} ({obj.CurrentProgress}/{obj.RequiredProgress})", "gray"));
                        }
                    }
                }
            }
        }
        catch { }

        try
        {
            var comps = CompanionSystem.Instance?.GetRecruitedCompanions();
            if (comps != null)
            {
                foreach (var c in comps)
                {
                    if (c.PersonalQuestCompleted) continue;
                    if (c.PersonalQuestStarted)
                        lines.Add((Loc.Get("journal.line_companion_quest_active", c.Name, c.PersonalQuestName), "cyan"));
                    else if (c.PersonalQuestAvailable)
                        lines.Add((Loc.Get("journal.line_companion_quest", c.Name, c.PersonalQuestName), "bright_cyan"));
                }
            }
        }
        catch { }

        return lines;
    }

    /// <summary>
    /// READY TO SPEND / CLAIM section: training points, banked level-ups,
    /// the daily free Renown blessing. Pole-gated rows reuse the source
    /// system's own gates so the journal never recommends something the
    /// player is barred from.
    /// </summary>
    public static List<(string Text, string Color)> BuildClaimLines(Character player)
    {
        var lines = new List<(string, string)>();
        if (player == null) return lines;

        if (player.TrainingPoints > 0)
            lines.Add((Loc.Get("journal.line_training_points", player.TrainingPoints), "bright_green"));

        try
        {
            if (!player.AutoLevelUp && player.Level < 100
                && player.Experience >= GameConfig.GetExperienceForLevel(player.Level + 1))
                lines.Add((Loc.Get("journal.line_level_ready"), "bright_green"));
        }
        catch { }

        // Free Temple blessing (Paragon+ Renown, Good/Holy pole, once per
        // day) -- mirrors ChurchLocation's gate exactly.
        try
        {
            if (!player.FreeBlessingClaimedToday
                && player.Chivalry >= GameConfig.FreeBlessingMinRenownChivalry)
            {
                var alignment = AlignmentSystem.Instance.GetAlignment(player);
                if (alignment == AlignmentSystem.AlignmentType.Good
                    || alignment == AlignmentSystem.AlignmentType.Holy)
                    lines.Add((Loc.Get("journal.line_free_blessing"), "bright_cyan"));
            }
        }
        catch { }

        return lines;
    }

    /// <summary>
    /// THE WORLD section: seal progress, the next unresolved Old God, the
    /// remembered dungeon floor.
    /// </summary>
    public static List<(string Text, string Color)> BuildWorldLines(Character player)
    {
        var lines = new List<(string, string)>();
        if (player == null) return lines;

        try
        {
            int collected = StoryProgressionSystem.Instance?.CollectedSeals?.Count ?? 0;
            int? nextSeal = NextSealFloor(player);
            if (nextSeal.HasValue)
                lines.Add((Loc.Get("journal.line_seals", collected, 7, nextSeal.Value), "white"));
            else if (collected > 0)
                lines.Add((Loc.Get("journal.line_seals_done", collected, 7), "white"));
        }
        catch { }

        try
        {
            var god = NextUnresolvedGod();
            if (god.HasValue)
            {
                string godName = OldGodsData.GetGodBossData(god.Value.God)?.Name ?? god.Value.God.ToString();
                lines.Add((Loc.Get("journal.line_next_god", godName, god.Value.Floor), "dark_cyan"));
            }
        }
        catch { }

        if (player.LastDungeonFloor > 0)
            lines.Add((Loc.Get("journal.line_resume_floor", player.LastDungeonFloor), "gray"));

        return lines;
    }

    /// <summary>First seal floor the player hasn't cleared yet, or null.</summary>
    private static int? NextSealFloor(Character player)
    {
        foreach (var floor in SealFloors)
        {
            if (player.ClearedSpecialFloors == null || !player.ClearedSpecialFloors.Contains(floor))
                return floor;
        }
        return null;
    }

    /// <summary>
    /// First Old God (floor order, Manwe excluded) whose status isn't
    /// resolved (Defeated / Saved / Allied / Consumed). Mirrors
    /// OldGodBossSystem.CanEncounterBoss's refusal list.
    /// </summary>
    private static (OldGodType God, int Floor)? NextUnresolvedGod()
    {
        var story = StoryProgressionSystem.Instance;
        if (story?.OldGodStates == null) return null;

        foreach (var (god, floor) in GodFloors)
        {
            if (story.OldGodStates.TryGetValue(god, out var state))
            {
                if (state.Status == GodStatus.Defeated || state.Status == GodStatus.Saved
                    || state.Status == GodStatus.Allied || state.Status == GodStatus.Consumed)
                    continue;
            }
            return (god, floor);
        }
        return null;
    }
}
