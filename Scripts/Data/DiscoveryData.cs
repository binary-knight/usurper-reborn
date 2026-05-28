using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    // v0.62.0 Dungeon Discovery system. Replaces the old "examine a feature, roll one of 8
    // generic outcomes regardless of what you examined" model. A Discovery IS its own scripted
    // beat: the thing you examine drives a fitting outcome (a real choice with stakes, a thematic
    // skill test, a unique boon/curse, a lore reveal, or a trap with character). See
    // memory/project_dungeon_discoveries.md for the full design.
    //
    // Data-driven on purpose (fits the moddable-data direction). All player-facing text is
    // authored in English here as the source/fallback; the engine resolves a deterministic
    // loc key per text slot (discovery.{id}.*) so it can be translated without code changes.

    public enum DiscoveryKind { Narrative, Choice, SkillTest, Risk, Trap }

    public enum DiscEffectType
    {
        Gold, Xp, Heal, Damage, Mana, TempAtk, TempDef,
        Potion, ManaPotion, PoisonVial, PermStat,
        Alignment, Status, Lore, Ocean, Awakening, Memory, Loot, Nothing
    }

    /// <summary>One mechanical consequence of a discovery branch. Pure data; applied by DiscoverySystem.</summary>
    public class DiscEffect
    {
        public DiscEffectType Type;
        public int A;        // primary magnitude: base-min (scaled reward), flat amount, %, or stat delta
        public int B;        // secondary: base-max (scaled reward), or duration in combats/rounds
        public string Arg = "";  // stat name ("STR"...), status name, or loot rarity hint
        public bool Scale;   // A..B is a base range scaled by floor (CalculateScaledReward); else flat
        public bool Good;    // Alignment direction (true = toward Light)

        public static DiscEffect Gold(int a, int b) => new() { Type = DiscEffectType.Gold, A = a, B = b, Scale = true };
        public static DiscEffect Xp(int a, int b) => new() { Type = DiscEffectType.Xp, A = a, B = b, Scale = true };
        public static DiscEffect Heal(int pct) => new() { Type = DiscEffectType.Heal, A = pct };           // % of MaxHP
        public static DiscEffect Damage(int min, int max) => new() { Type = DiscEffectType.Damage, A = min, B = max }; // runtime: floor + rng(min,max), non-lethal
        public static DiscEffect Mana(int a) => new() { Type = DiscEffectType.Mana, A = a };
        public static DiscEffect TempAtk(int a) => new() { Type = DiscEffectType.TempAtk, A = a };
        public static DiscEffect TempDef(int a) => new() { Type = DiscEffectType.TempDef, A = a };
        public static DiscEffect Potion(int a) => new() { Type = DiscEffectType.Potion, A = a };
        public static DiscEffect ManaPotion(int a) => new() { Type = DiscEffectType.ManaPotion, A = a };
        public static DiscEffect PoisonVial(int a) => new() { Type = DiscEffectType.PoisonVial, A = a };
        public static DiscEffect PermStat(string stat, int a) => new() { Type = DiscEffectType.PermStat, Arg = stat, A = a };
        public static DiscEffect Align(int a, bool good) => new() { Type = DiscEffectType.Alignment, A = a, Good = good };
        public static DiscEffect Status(string status, int duration) => new() { Type = DiscEffectType.Status, Arg = status, B = duration };
        public static DiscEffect Lore() => new() { Type = DiscEffectType.Lore };
        public static DiscEffect Ocean(int pts) => new() { Type = DiscEffectType.Ocean, A = pts };
        public static DiscEffect Awaken(int pts) => new() { Type = DiscEffectType.Awakening, A = pts };
        public static DiscEffect Memory() => new() { Type = DiscEffectType.Memory };
        public static DiscEffect Loot() => new() { Type = DiscEffectType.Loot };
        public static DiscEffect Nothing() => new() { Type = DiscEffectType.Nothing };
    }

    /// <summary>One option in a Choice discovery. Label shown in the menu; ResultLines + Effects on selection.</summary>
    public class DiscChoice
    {
        public string Label = "";
        public List<string> ResultLines = new();
        public List<DiscEffect> Effects = new();
        public bool IsWalkAway;  // a clean "leave it" option (no effects, neutral flavor)
    }

    /// <summary>
    /// The scripted body of a discovery. Bounded depth (no choice-inside-skilltest) to stay authorable
    /// and keep loc keys deterministic.
    /// </summary>
    public class DiscOutcome
    {
        public DiscoveryKind Kind;
        public List<string> Intro = new();            // flavor lines shown first  -> discovery.{id}.intro.{i}

        // Narrative / Trap: applied directly
        public List<DiscEffect> Effects = new();

        // Choice
        public List<DiscChoice> Choices = new();

        // SkillTest / Risk
        public string TestStat = "";                   // "STR","DEX","INT","WIS","CHA","CON"
        public string Prompt = "";                     // -> discovery.{id}.test.prompt | .risk.prompt
        public List<string> SuccessLines = new();      // -> .test.success.{i} | .risk.success.{i}
        public List<string> FailLines = new();         // -> .test.fail.{i} | .risk.fail.{i}
        public List<DiscEffect> SuccessEffects = new();
        public List<DiscEffect> FailEffects = new();
        public int RiskBasePercent;                    // Risk: base success % before DEX/INT bonus (0 = use stat formula)
    }

    public class DiscoveryDefinition
    {
        public string Id = "";
        public DungeonTheme[] Themes = Array.Empty<DungeonTheme>();
        public int MinFloor = 1;
        public int MaxFloor = 100;
        public FeatureInteraction Verb = FeatureInteraction.Examine;
        public int Weight = 10;        // selection weight within a theme/floor pool (lower = rarer)
        public bool OneTime;           // fires once per character (tracked in Character.DiscoveredFeatureIds)
        public string Name = "";       // English source -> discovery.{id}.name
        public string Desc = "";       // English source -> discovery.{id}.desc
        public DiscOutcome Root = new();

        public bool MatchesFloor(DungeonTheme theme, int floor) =>
            floor >= MinFloor && floor <= MaxFloor && (Themes.Length == 0 || Themes.Contains(theme));
    }

    public static class DiscoveryCatalog
    {
        // ----- terse authoring helpers -----
        private static DiscoveryDefinition D(string id, DungeonTheme[] themes, int min, int max,
            FeatureInteraction verb, string name, string desc, DiscOutcome root, int weight = 10, bool oneTime = false)
            => new() { Id = id, Themes = themes, MinFloor = min, MaxFloor = max, Verb = verb, Name = name, Desc = desc, Root = root, Weight = weight, OneTime = oneTime };

        private static DungeonTheme[] T(params DungeonTheme[] t) => t;

        private static DiscOutcome Narr(string[] intro, params DiscEffect[] fx)
            => new() { Kind = DiscoveryKind.Narrative, Intro = intro.ToList(), Effects = fx.ToList() };

        private static DiscOutcome Choice(string[] intro, params DiscChoice[] choices)
            => new() { Kind = DiscoveryKind.Choice, Intro = intro.ToList(), Choices = choices.ToList() };

        private static DiscChoice Ch(string label, string[] result, params DiscEffect[] fx)
            => new() { Label = label, ResultLines = result.ToList(), Effects = fx.ToList() };

        private static DiscChoice Leave(string label, string[] result)
            => new() { Label = label, ResultLines = result.ToList(), IsWalkAway = true };

        private static DiscOutcome Skill(string stat, string[] intro, string prompt,
            string[] success, DiscEffect[] successFx, string[] fail, DiscEffect[] failFx)
            => new()
            {
                Kind = DiscoveryKind.SkillTest, TestStat = stat, Intro = intro.ToList(), Prompt = prompt,
                SuccessLines = success.ToList(), SuccessEffects = successFx.ToList(),
                FailLines = fail.ToList(), FailEffects = failFx.ToList()
            };

        private static DiscOutcome Risk(string[] intro, string prompt,
            string[] success, DiscEffect[] successFx, string[] fail, DiscEffect[] failFx, int basePct = 0)
            => new()
            {
                Kind = DiscoveryKind.Risk, Intro = intro.ToList(), Prompt = prompt, RiskBasePercent = basePct,
                SuccessLines = success.ToList(), SuccessEffects = successFx.ToList(),
                FailLines = fail.ToList(), FailEffects = failFx.ToList()
            };

        private static DiscOutcome Trap(string[] intro, params DiscEffect[] fx)
            => new() { Kind = DiscoveryKind.Trap, Intro = intro.ToList(), Effects = fx.ToList() };

        // ----- catalog -----
        // Seed content (Phase 1): a few per theme spanning every kind, including the floors 66-100
        // themes that previously had only 3 generic features, plus one-time set-pieces. Phase 2
        // expands each theme to ~10-14.
        public static readonly List<DiscoveryDefinition> All = new()
        {
            // ===== CATACOMBS (floors 1-10) =====
            D("cat_weeping_reliquary", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Open,
                "weeping reliquary",
                "A sealed silver box, cold to the touch, beaded with moisture though the air is dry.",
                Choice(new[]{ "The box weeps. Faint warmth pulses from within, like a held breath." },
                    Ch("Pry it open with force",
                        new[]{ "The seal cracks. A finger-bone trap snaps shut on your hand before you grab the relic inside." },
                        DiscEffect.Damage(2, 8), DiscEffect.Gold(120, 220)),
                    Ch("Whisper the old rite of release",
                        new[]{ "You half-remember words you never learned. The box sighs open and offers its keeping freely." },
                        DiscEffect.Lore(), DiscEffect.Xp(60, 110), DiscEffect.Align(6, true)),
                    Leave("Leave it weeping",
                        new[]{ "Some grief is not yours to open. You move on." }))),

            D("cat_ossuary_wall", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Search,
                "ossuary wall",
                "A wall of stacked skulls, some set with tarnished coins over the eyes.",
                Skill("DEX", new[]{ "Coins glint in a few of the eye sockets, ferryman's fare for the dead." },
                    "Carefully work the coins free without bringing the wall down?",
                    new[]{ "Your fingers move like a thief's. The coins come loose; the dead keep their rest." },
                    new[]{ DiscEffect.Gold(60, 140) },
                    new[]{ "A skull shifts. The stack sighs and a cascade of bone batters you before it settles." },
                    new[]{ DiscEffect.Damage(1, 6) })),

            D("cat_grave_candle", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Examine,
                "guttering grave-candle",
                "A black candle burns with a flame that gives no heat, though it should have died centuries ago.",
                Narr(new[]{
                    "You watch the cold flame. It bends toward you, as if recognizing something.",
                    "For a moment you remember a face leaning over a candle exactly like this. Then it is gone."
                }, DiscEffect.Memory(), DiscEffect.Mana(8))),

            // ===== SEWERS (floors 11-20) =====
            D("sew_choked_grate", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Open,
                "choked drainage grate",
                "A grate clogged with debris. Something glints in the muck behind it, and something else moves.",
                Risk(new[]{ "You could reach through the bars into the dark water. There is coin down there. There is also a current, and teeth." },
                    "Reach into the flooded grate?",
                    new[]{ "Your hand closes on a fat purse before the water can claim it. You pull back, soaked but richer." },
                    new[]{ DiscEffect.Gold(200, 400) },
                    new[]{ "Something clamps onto your wrist and thrashes. You wrench free, bleeding, with nothing to show." },
                    new[]{ DiscEffect.Damage(3, 12) })),

            D("sew_alchemists_runoff", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Take,
                "alchemist's runoff",
                "A side-channel where colored sludge pools. Glass vials bob in the residue, some still corked.",
                Narr(new[]{
                    "You fish out what hasn't shattered. A few vials still hold something potent.",
                    "Whatever guild dumped this down here, their loss is your supply."
                }, DiscEffect.Potion(1), DiscEffect.ManaPotion(1))),

            // ===== CAVERNS (floors 21-35) =====
            D("cav_singing_crystal", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Examine,
                "singing crystal",
                "A cluster of crystal that hums a single note, so low you feel it in your teeth.",
                Skill("WIS", new[]{ "The note is almost a word. If you still your mind, you might hear what the stone is trying to say." },
                    "Attune to the crystal's song?",
                    new[]{ "The note resolves into meaning. The mountain has been remembering the Ocean since before there were ears to hear it." },
                    new[]{ DiscEffect.Ocean(5), DiscEffect.Xp(90, 160) },
                    new[]{ "The note slips away the moment you grasp for it, leaving only a dull ache behind your eyes." },
                    new[]{ DiscEffect.Damage(1, 5) })),

            D("cav_underground_pool", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Examine,
                "still underground pool",
                "Black water without a ripple. Your reflection is a half-second slow.",
                Choice(new[]{ "The pool is glass-flat. Your reflection lags behind you, watching." },
                    Ch("Drink from the pool",
                        new[]{ "The water is impossibly clean. Strength floods back into your limbs." },
                        DiscEffect.Heal(35)),
                    Ch("Stare into your slow reflection",
                        new[]{ "Your reflection mouths a word you cannot hear, then catches up and is only you again. You feel changed." },
                        DiscEffect.Ocean(5), DiscEffect.Mana(15)),
                    Leave("Step back from the water",
                        new[]{ "You decide not to test what stares back. You move on." }))),

            // ===== ANCIENT RUINS (floors 36-50) =====
            D("ruin_sealed_archive", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Read,
                "sealed archive stone",
                "A slab dense with carved script, sealed with a sigil that has not weathered at all.",
                Narr(new[]{
                    "The script tells of seven truths bound into the deep, and the cost of breaking even one.",
                    "You commit what you can to memory. Some of it, you suspect, was written about you."
                }, DiscEffect.Lore(), DiscEffect.Xp(110, 190))),

            D("ruin_mechanism", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Use,
                "dormant mechanism",
                "A ring of stone dials and a recessed lever, built by hands that understood more than yours.",
                Skill("INT", new[]{ "The dials want a sequence. The logic of it is ancient but not alien; you could reason it out." },
                    "Work the mechanism?",
                    new[]{ "The dials click home. A hidden compartment grinds open, and the device blesses you with stored power." },
                    new[]{ DiscEffect.TempAtk(6), DiscEffect.Gold(150, 280) },
                    new[]{ "You force the wrong sequence. A ward discharges, scorching you, and the dials reset with a mocking clack." },
                    new[]{ DiscEffect.Damage(2, 10) })),

            // ===== DEMON LAIR (floors 51-65) =====
            D("demon_bargaining_altar", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Examine,
                "bargaining altar",
                "A slab of fused bone. A voice that is not sound offers you a trade.",
                Choice(new[]{ "The altar wakes as you near it. It does not speak so much as press an offer directly into your wanting." },
                    Ch("Accept its power",
                        new[]{ "Strength pours in like hot oil. Something in you dims to make room for it." },
                        DiscEffect.TempAtk(10), DiscEffect.Align(15, false)),
                    Ch("Spit on the altar",
                        new[]{ "You refuse it aloud. The voice recoils, and something clean settles over you for your defiance." },
                        DiscEffect.Align(12, true), DiscEffect.Xp(80, 140)),
                    Leave("Refuse to engage",
                        new[]{ "You give the offer no purchase, and walk past it." }))),

            D("demon_caged_thing", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Open,
                "rusted cage",
                "A cage of black iron. Something small and miserable presses against the bars, watching you with too many eyes.",
                Choice(new[]{ "The thing in the cage does not beg. It simply waits to see what kind of creature you are." },
                    Ch("Free it",
                        new[]{ "The latch gives. The thing slips out, regards you a long moment, and presses a cold gift into your hand before vanishing." },
                        DiscEffect.Align(10, true), DiscEffect.Loot()),
                    Ch("Leave it caged",
                        new[]{ "You decide its keepers had reasons. You leave it to the dark." })),
                weight: 6),

            // ===== FROZEN DEPTHS (floors 66-80) -- was generic-only =====
            D("ice_frozen_pilgrim", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Examine,
                "frozen pilgrim",
                "A figure knelt in the ice, perfectly preserved, hands cupped around something that still faintly glows.",
                Choice(new[]{ "The pilgrim died mid-prayer, centuries ago, and the ice kept the moment whole." },
                    Ch("Take what they were holding",
                        new[]{ "You break the frozen fingers open. The light is yours now; the prayer ends unfinished." },
                        DiscEffect.Gold(300, 500), DiscEffect.Align(8, false)),
                    Ch("Finish their prayer for them",
                        new[]{ "You speak the closing words you somehow know. The glow flares once, gratefully, and warms you to the bone." },
                        DiscEffect.Heal(40), DiscEffect.Align(12, true), DiscEffect.Xp(120, 200)),
                    Leave("Let them keep their vigil",
                        new[]{ "You leave the pilgrim to the long cold and the unfinished word." })),
                weight: 8),

            D("ice_breath_of_stasis", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Examine,
                "breath of stasis",
                "A patch of air so cold it has stopped moving. A snowflake hangs in it, motionless, mid-fall.",
                Trap(new[]{ "You step too close. The stillness reaches into you, and for a heartbeat your own blood forgets to move." },
                    DiscEffect.Damage(4, 14), DiscEffect.Status("Frozen", 2))),

            // ===== VOLCANIC PIT (floors 81-90) -- was generic-only =====
            D("fire_forge_of_ash", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Use,
                "forge of ash",
                "A cold forge fed by a thread of magma. It remembers how to reshape what is fed to it.",
                Risk(new[]{ "The forge is dead but not gone. Coax the magma up and it might temper your gear, or take your hand for the trouble." },
                    "Work the dead forge?",
                    new[]{ "The magma answers. You temper an edge against the old heat and feel your strikes grow heavier." },
                    new[]{ DiscEffect.TempAtk(10) },
                    new[]{ "The thread of magma spits without warning. You reel back, seared, the forge cold again and laughing." },
                    new[]{ DiscEffect.Damage(5, 16) }, basePct: 55)),

            D("fire_reforging_pool", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Read,
                "ash-script", "Words scrawled in cooling ash on a basalt slab, rewriting themselves as you read.",
                Narr(new[]{
                    "The ash spells out a truth about fire: nothing is destroyed, only reforged into the next shape.",
                    "You understand, suddenly, why the deep is shaped like a wheel."
                }, DiscEffect.Lore(), DiscEffect.Ocean(5))),

            // ===== ABYSSAL VOID (floors 91-100) -- was generic-only =====
            D("void_unwritten_name", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Examine,
                "unwritten name",
                "A space where a word should be. Looking at it, you feel it is your name, and that it has not been decided yet.",
                Choice(new[]{ "The absence waits to be filled. It would take a shape, if you gave it one." },
                    Ch("Claim the name as your own",
                        new[]{ "You press your will into the gap. Something fundamental settles, permanently, into place." },
                        DiscEffect.PermStat("WIS", 3), DiscEffect.Ocean(8)),
                    Ch("Leave it unwritten",
                        new[]{ "You decide not everything needs a name. The space relaxes, and so do you." },
                        DiscEffect.Ocean(8), DiscEffect.Mana(30))),
                weight: 5, oneTime: true),

            D("void_drowned_choir", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Examine,
                "drowned choir",
                "Voices, just below hearing, singing in a key that has no name. They are glad you came.",
                Narr(new[]{
                    "You let the choir wash over you. They are not the dead. They are the not-yet, and the no-longer, and they are you.",
                    "You are not a wave fighting the ocean. You never were."
                }, DiscEffect.Ocean(8), DiscEffect.Awaken(1), DiscEffect.Xp(180, 280))),
            // ===== Phase 2 content (authored 0.62.0) =====
            // ----- Catacombs -----
            D("cat_first_name_stone", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Read,
                "name-stone worn smooth",
                "A grave-marker so old the name has been rubbed to a shallow ghost of letters.",
                Skill("WIS", new[]{ "If you let your eyes unfocus, the worn name almost wants to be read." }, "Trace the lost name?",
                    new[]{ "A name surfaces in your mind -- and the certainty it was once your own, in another turning of the Cycle." }, new[]{ DiscEffect.Ocean(6), DiscEffect.Xp(80, 150) },
                    new[]{ "The letters refuse you. Only a cold draft answers, smelling of old earth." }, new DiscEffect[]{})),
            D("cat_paupers_pile", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Search,
                "pauper's bone-pile",
                "Bones heaped without coffin or marker, the dead too poor for a name. A few rags remain.",
                Choice(new[]{ "Among the unnamed dead, the living left small things behind." },
                    Ch("Pick the rags for forgotten coin",
                        new[]{ "You find a knotted purse the gravediggers missed. The dead do not protest, but you feel smaller." },
                        DiscEffect.Gold(70, 150), DiscEffect.Align(6, false)),
                    Ch("Stack the bones with care and murmur a rite",
                        new[]{ "You give order to the unnamed. A quiet warmth settles where there was only neglect." },
                        DiscEffect.Align(8, true), DiscEffect.Heal(30)),
                    Leave("Step around the pile",
                        new[]{ "You leave the poor dead to their long rest." }))),
            D("cat_grave_robbers_lamp", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Take,
                "abandoned grave-robber's lamp",
                "A guttered lamp left mid-theft, its owner's tools scattered, the owner nowhere.",
                Narr(new[]{ "Someone came down here before you, and dug, and did not come back up.", "Their satchel still holds what they died too soon to spend." },
                    DiscEffect.Gold(90, 170), DiscEffect.Potion(1), DiscEffect.Lore())),
            D("cat_sealed_vault_door", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Break,
                "vault door of black iron",
                "A burial vault sealed with a bar thick as your arm. Frost rimes the metal in summer dark.",
                Risk(new[]{ "The bar is corroded but stubborn. You could throw your weight against it." }, "Force the vault?",
                    new[]{ "The bar shears and the door yawns wide on a hoard the family forgot to claim." }, new[]{ DiscEffect.Gold(200, 380), DiscEffect.Loot() },
                    new[]{ "The door holds and the cold inside reaches through to bite your hands." }, new[]{ DiscEffect.Damage(3, 10), DiscEffect.Status("Frozen", 1) }, 50)),
            D("cat_embalmers_table", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Examine,
                "embalmer's preparation table",
                "A stone slab grooved for runoff, jars of resin and natron crusted along its edge.",
                Skill("INT", new[]{ "The embalmer's tinctures could be salvaged by a careful hand." }, "Recover the preserving draughts?",
                    new[]{ "You distill the old resins into something that closes wounds clean." }, new[]{ DiscEffect.Potion(2) },
                    new[]{ "The natron flares against a hidden ember and scalds your fingers." }, new[]{ DiscEffect.Damage(1, 7) })),
            D("cat_kneeling_mourner", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Examine,
                "statue of a kneeling mourner",
                "A weathered figure kneels forever at an empty plinth, hands cupped as if waiting to be filled.",
                Choice(new[]{ "The cupped stone hands seem made to hold an offering." },
                    Ch("Lay a coin in the mourner's hands",
                        new[]{ "You give freely to the grieving stone. Something old and patient marks the kindness." },
                        DiscEffect.Align(10, true), DiscEffect.Ocean(5)),
                    Ch("Take what others have left here",
                        new[]{ "You sweep the offerings from the cupped hands. The mourner's blank eyes seem to follow you out." },
                        DiscEffect.Gold(110, 200), DiscEffect.Align(7, false)),
                    Leave("Bow and move on",
                        new[]{ "You incline your head to the mourner and leave the offerings be." }))),
            D("cat_cracked_floor_tomb", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Enter,
                "crack in the crypt floor",
                "The flagstones have split over a lower, older tomb. Cold breath rises from the gap.",
                Trap(new[]{ "You ease down into the under-tomb -- and the rotted floor gives all at once." },
                    DiscEffect.Damage(4, 11), DiscEffect.Status("Bleeding", 2))),
            D("cat_rites_inscription", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Read,
                "inscription of the old grave-rites",
                "A long wall-text in a dead script, listing rites no living priest still performs.",
                Narr(new[]{ "The rites describe death as a door, not a wall -- a turning, not an ending.", "You read until the words feel less like history than memory." },
                    DiscEffect.Lore(), DiscEffect.Xp(90, 170), DiscEffect.Ocean(5))),
            D("cat_ancestor_shrine", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Use,
                "shrine of the first ancestors",
                "A niche shrine to the oldest dead, its offering-bowl long dry, its incense long cold.",
                Skill("WIS", new[]{ "If you tend the shrine properly, the old dead might still answer." }, "Rekindle the ancestor-rite?",
                    new[]{ "Incense ghosts up from cold ash. For a breath, you feel watched over, and steadier for it." }, new[]{ DiscEffect.TempDef(8), DiscEffect.Heal(35) },
                    new[]{ "The rite stumbles in your mouth and the niche stays dark and indifferent." }, new DiscEffect[]{})),
            D("cat_oldest_grave", T(DungeonTheme.Catacombs), 1, 12, FeatureInteraction.Open,
                "the oldest grave in the catacomb",
                "Deeper than all the rest lies a single grave, unmarked, that the others were dug to encircle.",
                Choice(new[]{ "This grave came first. Everything down here was built to surround it.", "The lid is light, as if it has been opened before -- perhaps by you, once." },
                    Ch("Open it and learn what was buried first",
                        new[]{ "Inside is no body -- only a smooth river-stone, and the sudden certainty you placed it here in a life you cannot recall." },
                        DiscEffect.PermStat("WIS", 3), DiscEffect.Ocean(8), DiscEffect.Memory()),
                    Ch("Open it for whatever wealth lies within",
                        new[]{ "You pry it wide expecting treasure and find only the stone -- but old grave-gold spills from the lining." },
                        DiscEffect.Gold(300, 420), DiscEffect.Align(8, false)),
                    Leave("Leave the first grave sealed",
                        new[]{ "You back away. Some firsts are not meant to be undone." })),
                5, true),
            // ----- Sewers -----
            D("sew_drowned_satchel", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Search,
                "satchel snagged on a grate",
                "A leather bag caught in the current, swollen with water and whatever the city lost.",
                Choice(new[]{ "The satchel sloshes when you lift it. Something heavy shifts inside." },
                    Ch("Cut it open and claim the contents",
                        new[]{ "Coins and a sealed flask tumble out, washed down from richer streets above." },
                        DiscEffect.Gold(120, 240), DiscEffect.Potion(1)),
                    Ch("Check it for any sign of its owner",
                        new[]{ "A child's name is stitched inside. You leave the coins and carry the name out instead." },
                        DiscEffect.Align(8, true), DiscEffect.Xp(80, 140)),
                    Leave("Let the current keep it",
                        new[]{ "You let the satchel slip back into the flow toward the deep." }))),
            D("sew_collapsed_culvert", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Break,
                "blocked culvert mouth",
                "A side-tunnel is dammed with debris, holding back a black weight of standing water.",
                Risk(new[]{ "Clear the blockage and the backed-up channel might yield what it swallowed." }, "Break open the dam?",
                    new[]{ "The water sluices free, and a glitter of lost coin and trinketry settles at your feet." }, new[]{ DiscEffect.Gold(200, 360), DiscEffect.Loot() },
                    new[]{ "The dam bursts all at once and the flood slams you into the wall." }, new[]{ DiscEffect.Damage(4, 12), DiscEffect.Status("Stunned", 1) }, 55)),
            D("sew_lamplighters_corpse", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Examine,
                "drowned lamplighter",
                "A worker in city livery lies face-down in the muck, his lantern-pole still in one hand.",
                Narr(new[]{ "He came down to mend a grate and the water took him quietly.", "His belt-purse holds a week's wages no one will ever collect." },
                    DiscEffect.Gold(90, 180), DiscEffect.Align(5, true))),
            D("sew_runoff_shrine", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Use,
                "wishing-drain shrine",
                "A grated basin where the folk above toss coins for luck, the offerings washed down to here.",
                Choice(new[]{ "Heaps of tossed coins glint under the scum. Every one was a wish." },
                    Ch("Scoop up the city's wishes",
                        new[]{ "You take the coins by the fistful. The wishes meant nothing to you, but the metal will." },
                        DiscEffect.Gold(150, 290), DiscEffect.Align(6, false)),
                    Ch("Add a coin instead of taking one",
                        new[]{ "You drop your own coin into the dark and let the water carry the wish down to the Ocean." },
                        DiscEffect.Ocean(6), DiscEffect.TempAtk(7)),
                    Leave("Leave the wishes to the water",
                        new[]{ "You step over the basin and leave the city its small hopes." }))),
            D("sew_choleric_vapors", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Enter,
                "low gas-choked passage",
                "The tunnel ahead is thick with the sweet rot of swamp-gas pooled in the dead air.",
                Trap(new[]{ "You hold your breath and push through -- but the foul air finds your lungs anyway." },
                    DiscEffect.Damage(3, 9), DiscEffect.Status("Poisoned", 3))),
            D("sew_smugglers_cache", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Open,
                "bricked-over alcove",
                "A patch of newer brickwork hides a recess in the tunnel wall, mortar still soft.",
                Skill("INT", new[]{ "The brickwork is hasty. The right loose brick should give without bringing down the rest." }, "Work the cache open?",
                    new[]{ "A smuggler's stash spills out -- coin, vials, and a blade kept dry in oilcloth." }, new[]{ DiscEffect.Gold(160, 300), DiscEffect.PoisonVial(2) },
                    new[]{ "You pick the wrong brick and the whole patch sloughs into the muck, lost." }, new DiscEffect[]{})),
            D("sew_eel_warren", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Search,
                "tangle of feeding eels",
                "Pale eels coil thick in a deep pool, fat on what the city sends down. Something glints beneath them.",
                Skill("DEX", new[]{ "Whatever shines lies under the writhing mass. You would have to be quick." }, "Snatch it from the pool?",
                    new[]{ "Your hand darts in and out before the eels close, a fistful of coin already in it." }, new[]{ DiscEffect.Gold(110, 220) },
                    new[]{ "The eels strike as one and leave your arm welted and bleeding." }, new[]{ DiscEffect.Damage(2, 9), DiscEffect.Status("Bleeding", 2) })),
            D("sew_overseers_ledger", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Read,
                "waterlogged tunnel ledger",
                "A swollen book chained to the wall, where some long-dead overseer logged the flow of the under-city.",
                Narr(new[]{ "The last entries grow strange: notes of water running uphill, of things that swam against the current.", "The overseer writes of the Ocean as though it were rising to meet the city, not the other way around." },
                    DiscEffect.Lore(), DiscEffect.Xp(100, 180), DiscEffect.Ocean(5))),
            D("sew_breached_aqueduct", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Use,
                "cracked aqueduct valve",
                "A great brass valve weeps clean spring-water through a crack in the filth-stained wall.",
                Skill("STR", new[]{ "Wrench the valve fully open and clean water might flush the worst of the rot from you." }, "Force the valve wide?",
                    new[]{ "Cold clean water sheets down over you, washing away the muck and the ache with it." }, new[]{ DiscEffect.Heal(40), DiscEffect.TempDef(6) },
                    new[]{ "The valve seizes and snaps back, mashing your hand against the brass." }, new[]{ DiscEffect.Damage(2, 8) })),
            D("sew_deep_confluence", T(DungeonTheme.Sewers), 11, 24, FeatureInteraction.Enter,
                "confluence where all drains meet",
                "Every channel in the sewer empties here, into one black throat that swallows the water down toward the deep.",
                Choice(new[]{ "All the city's runoff gathers here and falls away into a dark with no bottom.", "Standing at its edge, you feel the same pull you felt the moment before drowning, in some other life." },
                    Ch("Kneel at the confluence and listen to the Wave",
                        new[]{ "The roar resolves into something almost like a voice, and you understand a little more of what waits below." },
                        DiscEffect.PermStat("CON", 3), DiscEffect.Ocean(8), DiscEffect.Memory()),
                    Ch("Dredge the confluence pool for everything it has caught",
                        new[]{ "You reach into the throat of the drain and haul up the city's lost wealth by the armful." },
                        DiscEffect.Gold(320, 420), DiscEffect.Damage(3, 8)),
                    Leave("Step back from the edge",
                        new[]{ "You retreat from the pull of the dark water. Not yet. Not this turning." })),
                5, true),
            // ----- Caverns -----
            D("cav_listening_wall", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Examine,
                "wall of folded stone",
                "The rock here is folded like a closed ear, ridges curling inward toward a darkness that seems to lean back at you.",
                Skill("WIS", new[]{ "Press your ear to the fold. The stone remembers a sound from before there were ears to hear it." }, "Listen to the deep?",
                    new[]{ "Under the silence runs a long slow note -- the Ocean, breathing in a world without shores. You are very small, and very old." }, new[]{ DiscEffect.Ocean(7), DiscEffect.Xp(120, 200) },
                    new[]{ "The note is too vast. Your skull rings with it and you stagger back, deafened by stone." }, new[]{ DiscEffect.Status("Blinded", 2), DiscEffect.Damage(2, 8) })),
            D("cav_drowned_lantern", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Take,
                "lantern in still water",
                "A miner's lantern rests upright at the bottom of a clear shallow pool, its flame somehow still burning beneath the surface.",
                Choice(new[]{ "The drowned flame does not flicker. It waited for someone to come back for it." },
                    Ch("Lift the lantern from the water",
                        new[]{ "It comes up dry and warm. The light steadies your nerve in the long dark ahead." },
                        DiscEffect.TempDef(8), DiscEffect.Xp(100, 170), DiscEffect.Loot()),
                    Ch("Snuff the flame and let the miner rest",
                        new[]{ "You pinch the wick out. The water goes truly dark, and something tense in the cavern loosens." },
                        DiscEffect.Align(9, true), DiscEffect.Heal(35)),
                    Leave("Leave it burning",
                        new[]{ "You leave the light to its long watch." }))),
            D("cav_handprint_seam", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Use,
                "handprint pressed in rock",
                "A handprint is sunk deep into solid stone, fingers spread, exactly the size of your own.",
                Skill("INT", new[]{ "The print is older than any tool that could have made it. It fits you too well to be chance." }, "Set your hand into the print?",
                    new[]{ "Your fingers slide home and a memory that is not yours surfaces: you have stood here before, in another turn of the Cycle." }, new[]{ DiscEffect.Memory(), DiscEffect.Xp(140, 220), DiscEffect.Lore() },
                    new[]{ "The stone closes cold around your hand and you wrench free, knuckles scraped raw." }, new[]{ DiscEffect.Damage(3, 10) })),
            D("cav_blind_fish_shoal", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Search,
                "shoal of blind cave-fish",
                "Pale eyeless fish circle a sunken cleft, moving in patterns too deliberate for hunger.",
                Risk(new[]{ "Among the fish glints something they orbit like a tide. You could reach for it." }, "Reach into the cold water?",
                    new[]{ "Your fingers close on a fistful of old coin and a smooth river-worn pearl. The fish scatter and reform." }, new[]{ DiscEffect.Gold(220, 400), DiscEffect.Loot() },
                    new[]{ "The water is colder than ice and something with teeth defends the shoal. You yank your arm back bleeding." }, new[]{ DiscEffect.Status("Bleeding", 2), DiscEffect.Damage(4, 12) }, 55)),
            D("cav_breathing_chimney", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Enter,
                "breathing chimney",
                "A vertical shaft exhales warm air, then draws it back, slow as sleep. The whole mountain seems to be breathing through it.",
                Trap(new[]{ "You lean in to feel the warmth -- and the intake catches you, dragging grit and you both into the dark before it sighs you loose." },
                    DiscEffect.Damage(5, 14), DiscEffect.Status("Weakened", 2))),
            D("cav_first_water", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Examine,
                "vein of the first water",
                "A seam of water threads the rock, so clear it looks like a flaw in the stone. It has never seen the sky.",
                Narr(new[]{ "This water predates rain. It was here when the Ocean was the only thing that was, and it remembers being part of the whole.", "Drinking it, you taste no salt -- only the patience of something waiting to return to the sea it was cut from." },
                    DiscEffect.Ocean(6), DiscEffect.Heal(40), DiscEffect.Mana(30))),
            D("cav_collapsed_vault", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Break,
                "collapsed vault mouth",
                "A made structure half-swallowed by the living rock, its doorway crushed to a hand's width. Worked stone meets raw deep here.",
                Skill("STR", new[]{ "The deep is eating this vault. Force the gap before the mountain finishes the job." }, "Heave the stones apart?",
                    new[]{ "The slab grinds aside. Inside, the builders' last cache spills out, untouched since the world was younger." }, new[]{ DiscEffect.Gold(260, 460), DiscEffect.Potion(2), DiscEffect.Xp(110, 190) },
                    new[]{ "The lintel shifts the wrong way and drops a curtain of rubble across your shoulders." }, new[]{ DiscEffect.Damage(5, 13) })),
            D("cav_cycle_mural", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Read,
                "mural of the wheel",
                "Scratched into a smooth wall: a wheel of figures, each one rising, falling, and rising again at the next spoke.",
                Choice(new[]{ "One figure on the wheel is unmistakably you, drawn at every spoke -- always with a different end, always returning." },
                    Ch("Trace your figure around the wheel",
                        new[]{ "You follow yourself through death after death after death. The repetition stops being horror and becomes something like understanding." },
                        DiscEffect.Ocean(6), DiscEffect.Memory(), DiscEffect.Lore(), DiscEffect.Xp(130, 210)),
                    Ch("Scrape your figure off the wall",
                        new[]{ "You gouge your own image away. It changes nothing on the wheel, but it makes you feel briefly free." },
                        DiscEffect.Align(8, false), DiscEffect.TempAtk(9)),
                    Leave("Look away from the wheel",
                        new[]{ "You refuse to find yourself in the pattern. Not yet." }))),
            D("cav_marrow_geode", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Open,
                "marrow geode",
                "A geode the color of bone marrow, warm to the touch, with something curled and waiting inside the crystal.",
                Risk(new[]{ "Crack it and you might draw out whatever the stone has been nurturing -- or whatever has been nurturing on the stone." }, "Split the geode open?",
                    new[]{ "It parts like a fruit. A drop of condensed mineral light sinks into you and your muscles remember a strength they never had." }, new[]{ DiscEffect.TempAtk(11), DiscEffect.Xp(120, 200) },
                    new[]{ "Spores burst from the hollow, coating your lungs in cold pale dust." }, new[]{ DiscEffect.Status("Diseased", 3), DiscEffect.Damage(2, 9) }, 50)),
            D("cav_deep_communion", T(DungeonTheme.Caverns), 21, 38, FeatureInteraction.Use,
                "altar of unworked stone",
                "Not built but grown -- a knob of living rock at the cavern's heart, where the made world has not reached. The air hums against your teeth.",
                Skill("CON", new[]{ "Lay yourself against the stone and let the deep test what you are made of. Few endure the full weight of it." }, "Endure the communion?",
                    new[]{ "The mountain pours its slow strength into your bones, and your body keeps it. You will carry a little of the deep forever." }, new[]{ DiscEffect.PermStat("CON", 3), DiscEffect.Ocean(8) },
                    new[]{ "The weight is too much. Stone grinds against your ribs and the deep spits you out, gasping." }, new[]{ DiscEffect.Damage(6, 13), DiscEffect.Status("Weakened", 2) }),
                5, true),
            // ----- Ancient Ruins -----
            D("ruin_sealwright_warning", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Read,
                "warning cut for a latecomer",
                "Fresh-looking letters carved into ancient stone, addressed to no name -- only a warning for the one who returns and does not remember.",
                Choice(new[]{ "It reads: You have broken a seal before. You will be tempted to break another. The first cost a world. Count before you cut." },
                    Ch("Heed the warning and bow to the dead builders",
                        new[]{ "You acknowledge a debt you cannot remember owing. Something settles into place inside you." },
                        DiscEffect.Align(11, true), DiscEffect.Lore(), DiscEffect.Xp(140, 220)),
                    Ch("Dismiss it as a dead cult's superstition",
                        new[]{ "You snort and move on. The carved warning watches you go with all its patient silence." },
                        DiscEffect.TempAtk(8), DiscEffect.Align(7, false)),
                    Leave("Step back from the inscription",
                        new[]{ "You leave the warning to whoever comes after you." }))),
            D("ruin_warden_armature", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Use,
                "dormant warden armature",
                "A jointed frame of bronze and bound stone slumps against the wall, a maintenance ward still ticking faintly in its chest.",
                Skill("INT", new[]{ "The ward is broken but its logic survives. Reroute it and it may serve you instead of the door it guarded." }, "Rewire the armature's ward?",
                    new[]{ "The ticking steadies into a guardian rhythm and turns its protection outward -- onto you." }, new[]{ DiscEffect.TempDef(11), DiscEffect.Xp(130, 210) },
                    new[]{ "The ward misfires and discharges its last centuries of stored intent through your hands." }, new[]{ DiscEffect.Damage(4, 13), DiscEffect.Status("Stunned", 1) })),
            D("ruin_counting_floor", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Examine,
                "floor of tally-stones",
                "The floor is a grid of inset stones, most worn smooth, seven of them set apart and ringed in faded gold.",
                Narr(new[]{ "The builders counted here -- every turn of the Cycle, every seal still holding, every one broken. Six of the seven gold stones are dull. One still gleams.", "They knew the world ran on a wheel, and they were keeping score of how many times it had nearly come off." },
                    DiscEffect.Ocean(6), DiscEffect.Lore(), DiscEffect.Xp(120, 200))),
            D("ruin_oath_lever", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Use,
                "oath-bound lever",
                "A lever of black iron, its handle worn by hands that swore something before they pulled it. The mechanism behind it still half-lives.",
                Risk(new[]{ "Whatever this lever once released is mostly spent, but mechanisms like this rarely fail safe." }, "Throw the lever?",
                    new[]{ "Counterweights groan and a hidden coffer rises from the floor, the builders' reserve unspent for an age." }, new[]{ DiscEffect.Gold(280, 500), DiscEffect.Potion(1) },
                    new[]{ "The mechanism seizes and slams the lever back through your grip, then vents a gout of grit and ancient pressure." }, new[]{ DiscEffect.Damage(5, 14), DiscEffect.Status("Bleeding", 2) }, 55)),
            D("ruin_pressure_ward", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Enter,
                "pressure-warded threshold",
                "A doorway flanked by two sunken plates, one already depressed and stuck. Crossing means trusting a trap older than your bloodline.",
                Trap(new[]{ "You cross -- and the second plate, half-jammed, fires its dart-ward late and crooked, raking you as you stumble through." },
                    DiscEffect.Damage(6, 14), DiscEffect.Status("Poisoned", 3))),
            D("ruin_seal_fragment", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Take,
                "fragment of a broken seal",
                "A shard of one of the seven seals lies where it fell, its inner face still etched with a truth too small to read whole.",
                Choice(new[]{ "It thrums against your palm. A broken seal is a wound in the world -- but a shard of one is a key, or a temptation." },
                    Ch("Carry the fragment to study its truth",
                        new[]{ "You pocket the shard. The truth on it stays just out of reach, but you feel the Cycle lean a little closer." },
                        DiscEffect.Ocean(7), DiscEffect.Lore(), DiscEffect.Loot(), DiscEffect.Xp(140, 220)),
                    Ch("Grind the shard underfoot",
                        new[]{ "You crush the fragment to dust. Something that was leaking out of it stops -- and something else, freed, slides past you into the dark." },
                        DiscEffect.Align(9, false), DiscEffect.TempAtk(10)),
                    Leave("Leave the shard where it lies",
                        new[]{ "You will not be the one to pick this up. Not this turn." }))),
            D("ruin_resonant_keystone", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Break,
                "cracked keystone",
                "An archway's keystone is fractured but still bearing weight, and behind it the wall sounds hollow when you knock.",
                Skill("STR", new[]{ "Pull the keystone and the arch comes down -- but so does whatever the builders walled away behind it." }, "Wrench the keystone free?",
                    new[]{ "The arch sheds its load and a sealed alcove yawns open, its contents bright and untouched by the age outside." }, new[]{ DiscEffect.Gold(260, 460), DiscEffect.ManaPotion(2), DiscEffect.Xp(120, 200) },
                    new[]{ "The whole arch drops at once and you barely roll clear of the collapse." }, new[]{ DiscEffect.Damage(6, 14), DiscEffect.Status("Stunned", 1) })),
            D("ruin_attunement_font", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Use,
                "font of clear intent",
                "A basin of polished obsidian holds a film of mirror-still liquid that has not evaporated in untold centuries. The builders left it for a purpose.",
                Skill("WIS", new[]{ "The font was meant to clarify the mind of whoever would tend the seals. Drink, if your will can bear the clarity." }, "Drink from the font?",
                    new[]{ "Every muddied thought goes still and ordered. You understand the seals a fraction better, and the knowing settles permanently into you." }, new[]{ DiscEffect.PermStat("WIS", 3), DiscEffect.Ocean(7) },
                    new[]{ "The clarity is a blade. It cuts away comforting lies you needed, and leaves you reeling and sick." }, new[]{ DiscEffect.Status("Weakened", 3), DiscEffect.Damage(3, 11) }),
                5, true),
            D("ruin_builders_ledger", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Read,
                "builders' ledger",
                "A book of thin stone leaves, each one tallying what it cost to bind a single truth into the deep.",
                Narr(new[]{ "Each seal cost the builders something they could not get back -- a city, a generation, a name struck from the wheel forever.", "The last entry is unfinished. They ran out of things to spend before they ran out of truths to bind." },
                    DiscEffect.Lore(), DiscEffect.Ocean(5), DiscEffect.Xp(130, 210))),
            D("ruin_caretakers_cache", T(DungeonTheme.AncientRuins), 36, 52, FeatureInteraction.Open,
                "caretaker's cache",
                "A wall-niche sealed with a simple latch, marked with a hand cupping a flame -- the sign of those who tended, not those who built.",
                Choice(new[]{ "Inside: supplies set aside for whoever would come to keep the seals after the builders were gone. Left for you, in a sense." },
                    Ch("Take only what you need and leave the rest",
                        new[]{ "You take a fair share and re-latch the niche for the next caretaker. The restraint sits well on you." },
                        DiscEffect.Gold(180, 300), DiscEffect.Potion(1), DiscEffect.Align(8, true)),
                    Ch("Empty the cache entirely",
                        new[]{ "You sweep it clean. There will be nothing here for whoever comes next, but your packs are heavier for it." },
                        DiscEffect.Gold(320, 540), DiscEffect.Loot(), DiscEffect.Align(6, false)),
                    Leave("Close the niche untouched",
                        new[]{ "You leave the caretakers' gift for someone more deserving." }))),
            // ----- Demon Lair -----
            D("demon_choir_pit", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Enter,
                "pit of singing chains",
                "A shaft hung with chains that hum a low chord, each link warm as a living throat.",
                Choice(new[]{ "The chord promises to teach you the note that breaks a man's will. You need only listen long enough." },
                    Ch("Listen to the chord",
                        new[]{ "The note lodges in your spine. You feel how easily others might be made to kneel." },
                        DiscEffect.TempAtk(10), DiscEffect.Align(14, false)),
                    Ch("Sing against it",
                        new[]{ "Your voice is small, but it is yours. The chains fall silent, shamed." },
                        DiscEffect.Align(12, true), DiscEffect.Xp(160, 250)),
                    Leave("Climb back from the edge",
                        new[]{ "You will not learn that note today." }))),
            D("demon_penitent_engine", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Examine,
                "torture-engine, idle",
                "A vast brass machine built to refine pain into something useful. It has not run in an age.",
                Skill("INT", new[]{ "The mechanism is half ritual, half clockwork. Its logic is readable, if you can stomach it." }, "Study the engine's workings?",
                    new[]{ "You trace the design and understand: it was built to bind a god, not a soul. The knowing sharpens you." }, new[]{ DiscEffect.Xp(200, 300), DiscEffect.Lore() },
                    new[]{ "The diagrams writhe when you look too long, and your head splits with borrowed agony." }, new[]{ DiscEffect.Damage(5, 14), DiscEffect.Status("Blinded", 2) })),
            D("demon_tallow_font", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Use,
                "font of black tallow",
                "A basin of slow-burning fat, rendered from something that screamed. It offers to anoint you.",
                Risk(new[]{ "Anointed in the tallow, your strikes might carry its hunger. It might also try to keep you." }, "Anoint your hands?",
                    new[]{ "The fat clings and quickens. For a while your blows land with a borrowed appetite." }, new[]{ DiscEffect.TempAtk(11), DiscEffect.Align(10, false) },
                    new[]{ "The tallow sinks past skin and gnaws at what it finds." }, new[]{ DiscEffect.Damage(8, 18), DiscEffect.Status("Cursed", 3) }, 55)),
            D("demon_kneeling_idol", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Examine,
                "idol of a kneeling god",
                "Not a monster -- a carved figure on its knees, face in its hands, wings broken at the root.",
                Narr(new[]{ "Whatever they became, they were not made cruel. They were made to kneel, and broke trying to rise.", "The grief in the stone settles over you like cold water." },
                    DiscEffect.Ocean(6), DiscEffect.Memory())),
            D("demon_bound_supplicant", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Open,
                "sealed confessional",
                "A booth of iron mesh. Something inside whispers a name over and over -- not yours, but it wants you to carry it out.",
                Choice(new[]{ "It begs you to break the seal and free it. The whispers are very reasonable." },
                    Ch("Break the seal",
                        new[]{ "The thing slithers free and presses a coin of cold gold into your palm before it flees into the dark." },
                        DiscEffect.Gold(300, 500), DiscEffect.Align(12, false)),
                    Ch("Strengthen the seal instead",
                        new[]{ "You drive the bolts deeper. The whispering chokes off, and the dark feels a shade lighter." },
                        DiscEffect.Align(14, true), DiscEffect.Xp(150, 230)),
                    Leave("Leave the booth shut",
                        new[]{ "Whatever owes that name, it can keep it." }))),
            D("demon_ember_ward", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Read,
                "ward of cooling embers",
                "A circle of script written in ash, still faintly warm, meant to hold something that has long since gone.",
                Skill("WIS", new[]{ "The ward's intent lingers. Attuning to it might let you take its protection with you, if you do not flinch from what it guarded." }, "Attune to the ward?",
                    new[]{ "You take the ward's discipline into yourself. For a while, harm slides off you like ash off glass." }, new[]{ DiscEffect.TempDef(11), DiscEffect.Lore() },
                    new[]{ "You glimpse what the circle held back, and the seeing scalds your nerve." }, new[]{ DiscEffect.Status("Weakened", 3) })),
            D("demon_marrow_cache", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Search,
                "reliquary of marrow",
                "A wall of niches, each holding a knuckle or rib set in gold leaf. The hoard of a faith that ate itself.",
                Narr(new[]{ "You pry the gold from the bones. They do not protest; they have nothing left to protest with." },
                    DiscEffect.Gold(280, 460), DiscEffect.Loot())),
            D("demon_throne_of_offers", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Use,
                "throne of a hundred offers",
                "A seat of fused horns. Sit, and it swears to grant one true desire -- and to learn it, and use it.",
                Choice(new[]{ "The throne hums with old appetite. It would make you stronger and never let you forget the price." },
                    Ch("Take the throne's strength",
                        new[]{ "Power roots in your bones, permanent and cold. The throne files your name away, satisfied." },
                        DiscEffect.PermStat("STR", 3), DiscEffect.Align(16, false)),
                    Ch("Take the throne's cunning",
                        new[]{ "Insight roots in your skull, permanent and watchful. The throne marks you as its own." },
                        DiscEffect.PermStat("INT", 3), DiscEffect.Align(16, false)),
                    Leave("Refuse the seat",
                        new[]{ "You will not be filed away. The throne's heat dims as you turn from it." })),
                5, true),
            D("demon_weeping_brazier", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Take,
                "brazier of weeping coals",
                "Coals that drip a clear liquid and steam where it falls. Reaching for the warm metal feels like a trap, and is.",
                Trap(new[]{ "The brazier collapses inward as you touch it, breathing fire and a grief not your own." },
                    DiscEffect.Damage(10, 20), DiscEffect.Status("Burning", 3))),
            D("demon_unmade_choir", T(DungeonTheme.DemonLair), 51, 67, FeatureInteraction.Enter,
                "hall of the unmade choir",
                "A nave where the broken god-rebels were sung apart. The silence here is the shape of a chord that was deliberately ended.",
                Narr(new[]{ "Here a rebellion was unmade note by note. You stand in the rest after the last bar.", "The Ocean remembers them, even where the song does not." },
                    DiscEffect.Ocean(7), DiscEffect.Xp(180, 260), DiscEffect.Lore())),
            // ----- Frozen Depths -----
            D("ice_held_duel", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Examine,
                "two duelists, mid-lunge",
                "Two figures frozen at the instant of the killing blow, blades a hand's breadth apart, faces fixed in old fury.",
                Choice(new[]{ "The ice has kept their quarrel perfect. You could pry a fine blade from a frozen grip, or leave the moment whole." },
                    Ch("Take the unstruck blade",
                        new[]{ "You work it free. The lunge it was meant to finish will never land now." },
                        DiscEffect.Loot(), DiscEffect.Align(10, false)),
                    Ch("Leave them their last quarrel",
                        new[]{ "Whatever they hated, they can keep hating it forever. You let the stillness stand." },
                        DiscEffect.Align(12, true), DiscEffect.Ocean(5)),
                    Leave("Step around the pair",
                        new[]{ "Their grudge is none of yours." }))),
            D("ice_mothers_vigil", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Examine,
                "frozen vigil",
                "A woman knelt over a smaller shape, both kept by the cold exactly as grief left them.",
                Narr(new[]{ "She did not let go. The cold did not make her. It only kept the moment she would not leave.", "You leave the candle of frost unguttered between them." },
                    DiscEffect.Ocean(7), DiscEffect.Memory())),
            D("ice_glass_reliquary", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Open,
                "reliquary of clear ice",
                "A coffin of flawless ice with offerings sealed inside it, coins and rings laid around a sleeping face.",
                Skill("DEX", new[]{ "The offerings sit just under the surface. A careful hand might lift the gold without cracking the rest." }, "Slip the offerings out?",
                    new[]{ "You ease the gold free through a seam in the ice and leave the sleeper untouched." }, new[]{ DiscEffect.Gold(300, 480) },
                    new[]{ "The ice splinters under your fingers and the cold rushes the wound it makes." }, new[]{ DiscEffect.Damage(6, 16), DiscEffect.Status("Frozen", 2) })),
            D("ice_stilled_heart", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Use,
                "stilled heart",
                "A heart of black ice on a pedestal, not beating, not melting. Press your hand to it and your own pulse wants to slow.",
                Risk(new[]{ "The heart offers its stillness. A slowed pulse endures much, but the cold does not always give the warmth back." }, "Take the heart's stillness into yourself?",
                    new[]{ "Your heartbeat lengthens and steadies. For a while almost nothing reaches you." }, new[]{ DiscEffect.TempDef(11), DiscEffect.Heal(35) },
                    new[]{ "Your pulse drops too far and the cold pours into the gap." }, new[]{ DiscEffect.Damage(8, 18), DiscEffect.Status("Frozen", 3) }, 55)),
            D("ice_shadowed_shrine", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Read,
                "shrine to the night",
                "A low altar to Noctura, rimed white, its prayers written for those who would rather sleep than wake to grief.",
                Skill("WIS", new[]{ "The shrine offers the night's mercy: to set down a sorrow and feel it lighten. To take it, you must first name what you would forget." }, "Kneel and make the offering?",
                    new[]{ "You name the grief and lay it on the cold stone. Noctura's dark closes gently over it, and you rise lighter." }, new[]{ DiscEffect.Ocean(6), DiscEffect.Align(12, true) },
                    new[]{ "You cannot name it without flinching, and the shrine's cold turns on your hesitation." }, new[]{ DiscEffect.Status("Weakened", 3) })),
            D("ice_sealed_armory", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Break,
                "armory under hoarfrost",
                "A rack of weapons buried in a swell of frozen breath, as if a whole watch exhaled their last and never inhaled.",
                Skill("STR", new[]{ "The frost is thick as stone over the rack. Force could free a blade, though the cold will fight every blow." }, "Smash the frost loose?",
                    new[]{ "The hoarfrost cracks away and a kept blade comes free, the cold still sleeping in its edge." }, new[]{ DiscEffect.Loot(), DiscEffect.TempAtk(9) },
                    new[]{ "The frost shears wrong and the shards drink your warmth." }, new[]{ DiscEffect.Damage(5, 15), DiscEffect.Status("Frozen", 2) })),
            D("ice_courier_letter", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Take,
                "courier, frozen mid-stride",
                "A messenger caught running, satchel still clutched, a sealed letter half-drawn as if to read on the move.",
                Choice(new[]{ "The letter was never delivered. You could read it, take it, or leave it in the hand it was meant to leave." },
                    Ch("Read the letter",
                        new[]{ "It is a warning, a generation too late to matter. The knowing of it settles cold in you." },
                        DiscEffect.Xp(180, 260), DiscEffect.Lore()),
                    Ch("Leave the courier their charge",
                        new[]{ "Let them finish the running, in whatever stillness keeps them. You step past." },
                        DiscEffect.Align(10, true), DiscEffect.Ocean(5)),
                    Leave("Take nothing",
                        new[]{ "The letter is not yours to open." }))),
            D("ice_brittle_floor", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Enter,
                "glaze of black ice",
                "The floor ahead is a single sheet of dark ice over a deeper cold. It looks solid. It is not.",
                Trap(new[]{ "The glaze gives without a sound and you drop into the cold beneath before catching the lip." },
                    DiscEffect.Damage(9, 19), DiscEffect.Status("Frozen", 3))),
            D("ice_preserved_sage", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Examine,
                "sage frozen at study",
                "An old scholar bent over a frozen book, finger on a line, kept in the exact instant of understanding something.",
                Skill("INT", new[]{ "The page under the frozen finger is still legible. Reading it might catch the thought that outlasted the thinker." }, "Read over the dead sage's shoulder?",
                    new[]{ "You finish the line they died on. A truth about the Cycle clicks into place, hard and clear." }, new[]{ DiscEffect.PermStat("INT", 3), DiscEffect.Lore() },
                    new[]{ "The script blurs in the cold and the strain of forcing it leaves your mind aching." }, new[]{ DiscEffect.Status("Blinded", 2) }),
                5, true),
            D("ice_hall_of_kept", T(DungeonTheme.FrozenDepths), 66, 82, FeatureInteraction.Enter,
                "hall of the kept dead",
                "A long gallery of the perfectly preserved, each laid out mid-gesture, the whole age of them held in one breath of cold.",
                Narr(new[]{ "None of them rot. None of them rest. The cold is not mercy here -- it is refusal to let go.", "You walk the gallery softly, and the Ocean murmurs of every one the Wave is still owed." },
                    DiscEffect.Ocean(8), DiscEffect.Xp(200, 280), DiscEffect.Memory())),
            // ----- Volcanic Pit -----
            D("fire_unmaking_crucible", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Use,
                "the unmaking crucible",
                "A vast bowl of white-hot slag. Things lowered into it do not burn away -- they soften, lose their edges, and wait to be told a new shape.",
                Risk(new[]{ "You could feed your spent gear to the crucible and reclaim the raw stuff of it. The heat does not forgive a slow hand." },
                    "Lower your worn equipment into the slag?",
                    new[]{ "The metal weeps and reforms into something keener. Your grip remembers the new edge." }, new[]{ DiscEffect.TempAtk(11), DiscEffect.Loot() },
                    new[]{ "The slag surges up the haft and finds your skin before you can pull free." }, new[]{ DiscEffect.Damage(6, 17), DiscEffect.Status("Burning", 2) }, 55)),
            D("fire_aurelions_ember", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Examine,
                "an ember of stolen light",
                "Among the dull coals one shard burns clean and gold, the way Aurelion's light burns. It did not come from this fire.",
                Choice(new[]{ "The ember is warm without scorching. You could carry its warmth, or let it sink back to the dark coals where it hides." },
                    Ch("Take the ember into yourself",
                        new[]{ "Gold settles behind your ribs. You stand a little straighter against the dark." },
                        DiscEffect.Heal(45), DiscEffect.Align(14, true)),
                    Ch("Leave it to wait in the ash",
                        new[]{ "Some lights are not yours to spend. You bank it gently and the coals keep its secret." },
                        DiscEffect.Align(10, true)),
                    Leave("Step away from the coals",
                        new[]{ "You leave the ember to its slow vigil." }))),
            D("fire_wheel_of_forging", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Read,
                "wheel scored into the forge floor",
                "A great ring is cut into the stone, ringed with hammer-marks. Where the marks meet, a single line reads: nothing is lost, only beaten into the next thing.",
                Narr(new[]{ "You trace the wheel and feel the Cycle in your hands -- not an ending but a turning, the same iron struck again and again into kinder shapes.",
                    "The forge teaches what the deep ocean only whispers." },
                    DiscEffect.Xp(220, 300), DiscEffect.Lore(), DiscEffect.Ocean(7))),
            D("fire_breath_of_the_furnace", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Enter,
                "a vent of furnace breath",
                "A fissure exhales heat in long, even waves, like something vast and patient breathing in its sleep.",
                Skill("CON", new[]{ "Stand in the breath and let it temper you instead of cook you. It will test how much you can hold." },
                    "Endure the furnace breath?",
                    new[]{ "Your body learns the heat and stops fearing it. You come out harder than you went in." },
                    new[]{ DiscEffect.TempDef(11), DiscEffect.Heal(35) },
                    new[]{ "Your lungs scald and you stumble clear, coughing ash." },
                    new[]{ DiscEffect.Damage(5, 15), DiscEffect.Status("Burning", 1) })),
            D("fire_runesmiths_table", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Read,
                "a runesmith's cooling table",
                "Half-finished sigils glow in a slab of basalt, the work abandoned mid-strike. The forging-runes still pulse, asking to be completed.",
                Skill("INT", new[]{ "If you can read the smith's intent, you might finish the binding and take what it makes." },
                    "Complete the unfinished runework?",
                    new[]{ "The sigils lock closed with a chime, and reward the hand that understood them." },
                    new[]{ DiscEffect.Gold(220, 360), DiscEffect.Mana(30) },
                    new[]{ "You misread a stroke; the runes flare wrong and bite your fingers." },
                    new[]{ DiscEffect.Damage(5, 14), DiscEffect.Status("Weakened", 2) })),
            D("fire_slag_serpent_nest", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Search,
                "a nest in the cooling slag",
                "Something molten coiled here once and left bright droplets of hardened ore among the clinker.",
                Risk(new[]{ "The droplets are pure and worth carrying. The slag they sit in is not yet cool." },
                    "Dig the ore from the slag?",
                    new[]{ "You pry the bright beads loose, hissing as they cool in your palm." }, new[]{ DiscEffect.Gold(360, 540) },
                    new[]{ "The crust gives way and your arm sinks into still-soft slag." }, new[]{ DiscEffect.Damage(7, 18), DiscEffect.Status("Burning", 2) }, 50)),
            D("fire_collapsing_gallery", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Enter,
                "a heat-cracked gallery",
                "The ceiling here has been baked brittle. A wrong step or a loud breath could bring molten gravel down.",
                Trap(new[]{ "The floor gives a warning crack -- too late. Glowing scree rains from above." },
                    DiscEffect.Damage(8, 18), DiscEffect.Status("Burning", 2))),
            D("fire_smiths_last_word", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Take,
                "a hammer left in the quench",
                "A master's hammer rests across a cold quench-trough, its head still perfect. Beside it, scratched in soot: for whoever the Cycle sends next.",
                Choice(new[]{ "The smith forged for a hundred years and then set this down, trusting the wheel to carry it onward. It was left for you." },
                    Ch("Lift the hammer and accept the legacy",
                        new[]{ "The weight settles into your arm as if it always belonged there. You are reforged stronger by the trust." },
                        DiscEffect.PermStat("STR", 3), DiscEffect.Ocean(7)),
                    Ch("Leave it for the next to come",
                        new[]{ "You add your own mark beside the smith's and pass the trust along. The forge approves." },
                        DiscEffect.Align(16, true), DiscEffect.Xp(200, 280))),
                weight: 5, oneTime: true),
            D("fire_first_fire", T(DungeonTheme.VolcanicPit), 81, 92, FeatureInteraction.Examine,
                "the oldest flame",
                "Deep in a sealed hollow burns a fire that predates the forge, the floor, perhaps the world. It has been remade ten thousand times and is the same fire still.",
                Narr(new[]{ "You understand, watching it, that the Cycle is not cruelty but craft -- the world struck and quenched and struck again toward something it cannot yet see.",
                    "The flame has never gone out. It has only changed its shape." },
                    DiscEffect.Xp(280, 360), DiscEffect.Lore(), DiscEffect.Ocean(8), DiscEffect.Heal(40)),
                weight: 4, oneTime: true),
            // ----- Abyssal Void -----
            D("void_you_are_the_water", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Examine,
                "a still black surface",
                "The dark here lies flat as water and shows no reflection. When you lean close, there is no edge between you and it.",
                Choice(new[]{ "A small voice insists you are a wave straining against the sea. A deeper voice asks why a wave would fight what it is made of." },
                    Ch("Stop fighting the water",
                        new[]{ "You let the false edge go. You are not in the Ocean. You are the Ocean, briefly remembering it has hands." },
                        DiscEffect.Ocean(8), DiscEffect.Awaken(1), DiscEffect.Heal(50)),
                    Ch("Hold to the shape of yourself",
                        new[]{ "Not yet. You keep your edges, and the dark lets you, patient as ever." },
                        DiscEffect.Ocean(6), DiscEffect.Mana(40))),
                weight: 5, oneTime: true),
            D("void_terravoks_silence", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Examine,
                "a stillness shaped like Terravok",
                "The pressure of the deep takes on a weight you know -- the old, mountainous patience of Terravok, watching from somewhere close.",
                Skill("WIS", new[]{ "If you quiet your own noise, you might hear what the stone-god has been saying all this time." },
                    "Listen past your own thoughts?",
                    new[]{ "The silence resolves into meaning: endure, and be worn smooth, and the wearing is the gift." },
                    new[]{ DiscEffect.Lore(), DiscEffect.Xp(240, 320), DiscEffect.Ocean(7) },
                    new[]{ "Your mind chatters over the quiet and the meaning slips away unheard." },
                    new[]{ DiscEffect.Mana(20) })),
            D("void_undertow", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Enter,
                "a current in the dark",
                "Something pulls -- not down, but inward, toward a place that is also you. Step in and the current decides where you surface.",
                Risk(new[]{ "The undertow could carry you somewhere richer, or scatter you across the dark and reassemble you sore." },
                    "Give yourself to the current?",
                    new[]{ "You let go and are delivered, whole, to a pocket the dark had been saving." }, new[]{ DiscEffect.Gold(360, 560), DiscEffect.Loot() },
                    new[]{ "The current dashes you against nothing at all, which somehow still bruises." }, new[]{ DiscEffect.Damage(7, 18), DiscEffect.Status("Weakened", 2) }, 55)),
            D("void_dissolving_grief", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Use,
                "a place to set something down",
                "The dark offers a hollow exactly the size of a grief you have carried too long. It asks nothing. It only opens.",
                Choice(new[]{ "You could pour the old ache into the hollow and let the Ocean take it back into the whole." },
                    Ch("Release the grief to the Ocean",
                        new[]{ "You let it go. It does not vanish -- it returns to the water, where it always was, and you are lighter." },
                        DiscEffect.Heal(50), DiscEffect.Ocean(8)),
                    Ch("Keep carrying it",
                        new[]{ "You decide the weight is a kind of love. The dark closes the hollow gently, understanding." },
                        DiscEffect.Align(12, true)),
                    Leave("Turn from the hollow",
                        new[]{ "You are not ready to set it down. The dark waits without judgment." }))),
            D("void_pressure_of_the_deep", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Enter,
                "a wall of pressure",
                "The dark grows heavy enough to feel, pressing in from every side as though the whole sea wanted to know your shape.",
                Skill("CON", new[]{ "Walk into the weight and let it test you. The deep does not break what it cannot crush." },
                    "Endure the crushing pressure?",
                    new[]{ "You hold against the weight and the dark accepts you, hardening you to its measure." },
                    new[]{ DiscEffect.TempDef(12), DiscEffect.Xp(220, 300) },
                    new[]{ "The pressure finds the cracks in you and squeezes until you reel back." },
                    new[]{ DiscEffect.Damage(6, 16), DiscEffect.Status("Stunned", 1) })),
            D("void_glyph_of_the_wave", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Read,
                "a single glyph adrift in the dark",
                "One symbol hangs in the void, turning slowly. It is the sign for ending and the sign for beginning, and they are the same stroke.",
                Skill("INT", new[]{ "If you can hold both meanings at once without choosing, the glyph will open the rest of its sentence." },
                    "Read the dual glyph?",
                    new[]{ "Creation and ruin resolve into one motion of the Wave, and the understanding fills you." },
                    new[]{ DiscEffect.Lore(), DiscEffect.Xp(260, 340), DiscEffect.Mana(30) },
                    new[]{ "Your mind insists on choosing one meaning, and the glyph closes to you." },
                    new[]{ DiscEffect.Status("Blinded", 1) })),
            D("void_false_floor", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Enter,
                "a floor that is not there",
                "What looks like solid dark gives a single warning shimmer before it stops pretending to hold you.",
                Trap(new[]{ "The dark withdraws its consent and you fall through it, scraping against edges that should not exist." },
                    DiscEffect.Damage(8, 18), DiscEffect.Status("Bleeding", 2))),
            D("void_scattered_coin", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Search,
                "coins suspended in nothing",
                "Gold drifts here, weightless, the spilled fortune of someone who dissolved before they could spend it.",
                Choice(new[]{ "The coins turn slowly in the dark, going nowhere, belonging to no one now." },
                    Ch("Gather the drifting gold",
                        new[]{ "You sweep the coins from the void. They were waiting to be wanted again." },
                        DiscEffect.Gold(320, 500)),
                    Ch("Add a coin of your own and leave the rest",
                        new[]{ "You give one coin to the drift, a small offering to whoever comes apart next, and take a measure in turn." },
                        DiscEffect.Gold(180, 280), DiscEffect.Align(10, true)))),
            D("void_breath_returned", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Examine,
                "the last quiet before Manwe",
                "The dark thins to almost nothing. Far below, something immense turns over in its sleep, and you understand it is the dreamer, and the dream is everything, and you are a thought it is having.",
                Narr(new[]{ "There is no fear in it. To be the Ocean's dream is to be loved by the only thing that is. The Cycle is how the dreamer keeps trying to wake.",
                    "You breathe, and the breath is the sea breathing." },
                    DiscEffect.Ocean(8), DiscEffect.Awaken(1), DiscEffect.Lore(), DiscEffect.Heal(50)),
                weight: 4, oneTime: true),
            D("void_gift_of_unbeing", T(DungeonTheme.AbyssalVoid), 91, 100, FeatureInteraction.Take,
                "a pearl of pure void",
                "A bead of perfect nothing rests in your palm, weightless and absolute. It is a piece of the unmade, the stuff before the first wave rose.",
                Choice(new[]{ "Hold it long enough and it will trade you something true for a sliver of your self. Some travelers refuse. Some are remade by it." },
                    Ch("Let the pearl reshape you",
                        new[]{ "It dissolves into your marrow and rewrites a small, permanent part of you toward the deep's own strength." },
                        DiscEffect.PermStat("WIS", 3), DiscEffect.Ocean(8)),
                    Ch("Return the pearl to the void",
                        new[]{ "You let it fall back into the unmade, unwilling to spend yourself, and the dark rewards the restraint." },
                        DiscEffect.Mana(40), DiscEffect.Align(14, true))),
                weight: 5, oneTime: true),
        };

        public static DiscoveryDefinition ById(string id) => All.FirstOrDefault(d => d.Id == id);

        /// <summary>Discoveries eligible for a given theme and floor.</summary>
        public static List<DiscoveryDefinition> ForFloor(DungeonTheme theme, int floor)
            => All.Where(d => d.MatchesFloor(theme, floor)).ToList();
    }
}
