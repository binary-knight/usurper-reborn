using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Phase 1 / 1.5 of the NPC dialogue dynamics plan (memory/project_npc_dialogue.md).
    ///
    /// Layered contextual flavor on top of existing dialogue templates. The base
    /// template still comes from the existing pools (VisualNovelDialogueSystem's
    /// inline greeting arrays, NPCDialogueGenerator's relationship-tiered pools,
    /// etc.). This helper PROBABILISTICALLY enhances a chosen line with one or
    /// more contextual flavor additions drawn from already-tracked NPC state.
    ///
    /// Layers (each has a wall-clock probability gate AND coherence gates):
    ///   * Mood prepend          (NPC dominant emotion)
    ///   * Memory append         (recent interaction memory with this player)
    ///   * Witness append        (NPC saw player do something noteworthy)
    ///   * Personality aside     (Greed / Trustworthiness / Compassion / Courage)
    ///   * Faction tension       (Crown / Shadows / Faith, suppressed for romance)
    ///
    /// Coherence rules prevent contradictory stacks (e.g. *beaming,* + "you and
    /// I have unfinished business" + "Don't tell your priests we spoke" all in
    /// the same line). The chosen mood sets a valence (positive/negative/neutral)
    /// and downstream layers either select a compatible variant or skip.
    ///
    /// Variety: a per-NPC sliding window of the last few flavor lines used is
    /// kept so the same line doesn't fire twice in a row.
    ///
    /// Zero LLM cost. Pure substitution on existing state. Phase 2 (LLM variant
    /// pools) drops into this same Enhance() seam.
    /// </summary>
    public static class DialogueEnhancer
    {
        private static readonly Random _random = new Random();

        // Per-layer probabilities. Tuned conservatively so the chat doesn't
        // suddenly become a wall of contextual asides; most lines still go
        // through unmodified, but a meaningful fraction picks up flavor.
        private const double EmotionPrependChance      = 0.22;
        private const double MemoryAppendChance        = 0.18;
        private const double WitnessAppendChance       = 0.14;
        private const double PlayerStateAppendChance   = 0.25;
        private const double GriefAsideChance          = 0.18;
        private const double PersonalityAsideChance    = 0.10;
        private const double FactionAppendChance       = 0.12;

        // Player-state thresholds: NPC notices the player's condition when
        // it's outside normal range (bleeding visibly hurt, walking around
        // with a king's ransom). Higher chance than other layers because
        // the gates are concrete world-state, not probabilistic.
        private const float PlayerHurtThreshold = 0.30f;
        private const long PlayerWealthThreshold = 100000;

        // Grief gates: an NPC reads as "grieving" only when Sadness is at
        // least this intense AND they have a witnessed-death memory within
        // the recent window. Phase 1.5 lightweight version (no NPC-side
        // GriefSystem -- that's full Phase 2 work).
        private const float GriefSadnessThreshold = 0.5f;
        private const int GriefMemoryWindowDays = 7;

        // Variety cache: per-(npcId, layerName) keep the last N flavor lines used
        // and skip them on subsequent picks. Cleared opportunistically when it
        // grows past a soft cap so the static doesn't leak forever in long MUD
        // sessions. Keyed on `{npc.ID}|{layer}` so two NPCs can independently
        // repeat each other's lines without colliding.
        private const int RecentLineWindow = 3;
        private const int RecentCacheSoftCap = 1024;
        private static readonly Dictionary<string, Queue<string>> _recentLines = new Dictionary<string, Queue<string>>();

        /// <summary>
        /// Emotion valence: positive moods (Joy, Gratitude, Hope, Peace,
        /// Confidence, Pride) discourage hostile follow-ons; negative moods
        /// (Anger, Fear, Sadness, Envy) discourage warm follow-ons. Greed,
        /// Loneliness, and "no dominant emotion" are neutral.
        /// </summary>
        private enum Tone { Neutral = 0, Positive = 1, Negative = -1 }

        /// <summary>
        /// Wrap a base dialogue line with probabilistic contextual flavor.
        /// Safe to call with any input; returns the unmodified `baseLine` if
        /// no layer fires or if accessors return null.
        ///
        /// v0.61.4: language-gated to supported languages (currently en + hu).
        /// Both the flavor pools and the underlying VN templates this helper
        /// wraps (greetings, farewells, chat-topic replies, personal-question
        /// replies in VisualNovelDialogueSystem) are now localized into en
        /// and hu. Spanish / French / Italian sessions still see the
        /// unmodified base line because their VN templates and flavor pools
        /// have not been translated yet; the gate widens per future loc
        /// passes.
        /// </summary>
        public static string Enhance(string baseLine, global::NPC npc, global::Character player)
        {
            if (string.IsNullOrWhiteSpace(baseLine) || npc == null || player == null)
                return baseLine ?? "";

            // Language gate (see method docstring).
            if (!IsSupportedLanguage())
                return baseLine;

            string result = baseLine;

            // Step 1: pick a mood (sets the tone for downstream coherence
            // checks). We always *evaluate* the mood even if the prepend roll
            // fails, because memory/faction layers still want to know whether
            // the NPC is angry or beaming when picking phrasing.
            var (moodFlavor, tone) = EvaluateMood(npc);

            if (moodFlavor != null && _random.NextDouble() < EmotionPrependChance)
            {
                result = $"{moodFlavor} {result}";
            }

            // Step 2: memory layer. Variant phrasing is gated by the mood
            // tone so a beaming NPC doesn't bring up unfinished business and
            // a scowling NPC doesn't reminisce fondly.
            if (_random.NextDouble() < MemoryAppendChance)
            {
                string? mem = GetMemoryFlavor(npc, player, tone);
                if (!string.IsNullOrEmpty(mem))
                    result = $"{result} {mem}";
            }

            // Step 3: witness layer. NPCs who watched the player commit a
            // notable act (murder, fight, theft) get a chance to allude to it.
            // Skipped under positive tone because "I saw you slit that man's
            // throat" doesn't pair with a friendly mood.
            if (tone != Tone.Positive && _random.NextDouble() < WitnessAppendChance)
            {
                string? witness = GetWitnessFlavor(npc, player);
                if (!string.IsNullOrEmpty(witness))
                    result = $"{result} {witness}";
            }

            // Step 4: player-state awareness. NPC notices visible cues
            // about the player ("you look terrible" if hurt, "purse looks
            // heavy" if wealthy). Concrete world-state gates, not just
            // probability rolls, so it grounds the chat in what's actually
            // happening rather than feeling random.
            if (_random.NextDouble() < PlayerStateAppendChance)
            {
                string? state = GetPlayerStateFlavor(npc, player, tone);
                if (!string.IsNullOrEmpty(state))
                    result = $"{result} {state}";
            }

            // Step 5: grief aside. NPC who is sad AND has recently witnessed
            // a death surfaces grief. Higher base probability than other
            // layers because grief, when it applies, should land -- it's a
            // strong narrative beat and the gate (Sadness >= 0.5 + recent
            // death memory) is already narrow.
            if (_random.NextDouble() < GriefAsideChance)
            {
                string? grief = GetGriefFlavor(npc);
                if (!string.IsNullOrEmpty(grief))
                    result = $"{result} {grief}";
            }

            // Step 6: personality aside. Surfaces a trait-driven line ("I
            // always have time for paying customers" for Greed, etc.).
            // Tone-gated: callous greed lines don't land if the NPC is mid-
            // grief, and warm "hope you're well" lines don't land if angry.
            if (_random.NextDouble() < PersonalityAsideChance)
            {
                string? trait = GetPersonalityFlavor(npc, tone);
                if (!string.IsNullOrEmpty(trait))
                    result = $"{result} {trait}";
            }

            // Step 7: faction tension. Suppressed entirely for romance
            // partners (Spouse / Lover / FWB) and softened on positive mood.
            if (_random.NextDouble() < FactionAppendChance)
            {
                string? faction = GetFactionTensionFlavor(npc, player, tone);
                if (!string.IsNullOrEmpty(faction))
                    result = $"{result} {faction}";
            }

            return result;
        }

        // ---- Mood ----

        /// <summary>
        /// Returns a (flavor, tone) pair for the NPC's current dominant
        /// emotion. The flavor is the prepend snippet (or null if no strong
        /// emotion); the tone is always set so downstream layers can gate.
        /// </summary>
        private static (string? flavor, Tone tone) EvaluateMood(global::NPC npc)
        {
            if (npc?.EmotionalState == null) return (null, Tone.Neutral);
            var dom = npc.EmotionalState.GetDominantEmotion();
            if (!dom.HasValue) return (null, Tone.Neutral);

            Tone t = dom.Value switch
            {
                EmotionType.Anger      => Tone.Negative,
                EmotionType.Fear       => Tone.Negative,
                EmotionType.Sadness    => Tone.Negative,
                EmotionType.Envy       => Tone.Negative,
                EmotionType.Joy        => Tone.Positive,
                EmotionType.Gratitude  => Tone.Positive,
                EmotionType.Hope       => Tone.Positive,
                EmotionType.Peace      => Tone.Positive,
                EmotionType.Confidence => Tone.Positive,
                EmotionType.Pride      => Tone.Positive,
                _                      => Tone.Neutral,
            };

            string? flavor = dom.Value switch
            {
                EmotionType.Anger      => PickFreshLoc(npc, "dialogue.enhance.mood_anger", 4),
                EmotionType.Fear       => PickFreshLoc(npc, "dialogue.enhance.mood_fear", 4),
                EmotionType.Joy        => PickFreshLoc(npc, "dialogue.enhance.mood_joy", 4),
                EmotionType.Sadness    => PickFreshLoc(npc, "dialogue.enhance.mood_sadness", 4),
                EmotionType.Confidence => PickFreshLoc(npc, "dialogue.enhance.mood_confidence", 4),
                EmotionType.Greed      => PickFreshLoc(npc, "dialogue.enhance.mood_greed", 3),
                EmotionType.Gratitude  => PickFreshLoc(npc, "dialogue.enhance.mood_gratitude", 3),
                EmotionType.Loneliness => PickFreshLoc(npc, "dialogue.enhance.mood_loneliness", 3),
                EmotionType.Envy       => PickFreshLoc(npc, "dialogue.enhance.mood_envy", 3),
                EmotionType.Pride      => PickFreshLoc(npc, "dialogue.enhance.mood_pride", 3),
                EmotionType.Hope       => PickFreshLoc(npc, "dialogue.enhance.mood_hope", 3),
                EmotionType.Peace      => PickFreshLoc(npc, "dialogue.enhance.mood_peace", 3),
                _                      => null,
            };

            return (flavor, t);
        }

        /// <summary>
        /// Public legacy accessor for the mood snippet alone, preserving the
        /// original v0.61.3 API for any external caller. Language-gated like
        /// `Enhance()` so non-English sessions consistently see no flavor.
        /// </summary>
        public static string? GetMoodFlavor(global::NPC npc)
            => IsEnglish() ? EvaluateMood(npc).flavor : null;

        // ---- Memory ----

        /// <summary>
        /// Pull the most-important recent memory of this player and shape it
        /// into a brief reference. Tone-gated: under negative mood we skip
        /// positive memory lines (won't gush about a kindness while scowling)
        /// and vice versa. If the chosen-tone variant isn't available we fall
        /// through to neutral phrasing rather than dropping the layer.
        /// </summary>
        private static string? GetMemoryFlavor(global::NPC npc, global::Character player, Tone tone)
        {
            if (npc?.Brain?.Memory == null || player == null) return null;

            var playerKey = player.Name2 ?? player.Name1 ?? "";
            if (string.IsNullOrEmpty(playerKey)) return null;

            var memories = npc.Brain.Memory.GetMemoriesAboutCharacter(playerKey);
            if (memories == null || memories.Count == 0) return null;

            var pick = memories.OrderByDescending(m => m.Importance)
                               .ThenByDescending(m => m.Timestamp)
                               .First();

            var age = pick.GetAge();
            string when = age.TotalDays switch
            {
                < 1  => Loc.Get("dialogue.enhance.when_today"),
                < 3  => Loc.Get("dialogue.enhance.when_other_day"),
                < 10 => Loc.Get("dialogue.enhance.when_recently"),
                < 30 => Loc.Get("dialogue.enhance.when_a_while_ago"),
                _    => Loc.Get("dialogue.enhance.when_some_time_back")
            };

            bool memoryIsPositive = pick.EmotionalImpact > 0.3f;
            bool memoryIsNegative = pick.EmotionalImpact < -0.3f;

            // Coherence: a beaming NPC shouldn't pull a hostile memory beat;
            // a scowling NPC shouldn't gush. Force-fallback to neutral
            // phrasing if the chosen tone clashes with the memory valence.
            bool forceNeutral =
                (tone == Tone.Positive && memoryIsNegative) ||
                (tone == Tone.Negative && memoryIsPositive);

            // Localized memory lines carry the `{0}` placeholder for `when`,
            // resolved at pool-build time so PickFresh's variety cache sees
            // the final formatted string (otherwise the cache would treat
            // "I haven't forgotten {0}" and "I haven't forgotten earlier today"
            // as different lines and let exact repeats slip through).
            if (memoryIsPositive && !forceNeutral)
            {
                return PickFresh(npc, "dialogue.enhance.memory_pos",
                    Loc.Get("dialogue.enhance.memory_pos_1", when),
                    Loc.Get("dialogue.enhance.memory_pos_2", when),
                    Loc.Get("dialogue.enhance.memory_pos_3", when));
            }

            if (memoryIsNegative && !forceNeutral)
            {
                return PickFresh(npc, "dialogue.enhance.memory_neg",
                    Loc.Get("dialogue.enhance.memory_neg_1", when),
                    Loc.Get("dialogue.enhance.memory_neg_2", when),
                    Loc.Get("dialogue.enhance.memory_neg_3", when));
            }

            return PickFresh(npc, "dialogue.enhance.memory_neu",
                Loc.Get("dialogue.enhance.memory_neu_1", when),
                Loc.Get("dialogue.enhance.memory_neu_2", when),
                Loc.Get("dialogue.enhance.memory_neu_3", when));
        }

        /// <summary>
        /// Legacy accessor for callers who want memory flavor without tone
        /// gating. Returns neutral-tone-blocked phrasing. Language-gated.
        /// </summary>
        public static string? GetMemoryFlavor(global::NPC npc, global::Character player)
            => IsEnglish() ? GetMemoryFlavor(npc, player, Tone.Neutral) : null;

        // ---- Witness ----

        /// <summary>
        /// Surfaces a witnessed-event memory. SocialInfluenceSystem.RecordWitnesses
        /// writes WitnessedEvent memories with descriptions of the form
        /// "Saw {actor} {verb} {target}", so we scan for ones where the
        /// player's name appears as the actor (the start of the description
        /// past "Saw ").
        /// </summary>
        private static string? GetWitnessFlavor(global::NPC npc, global::Character player)
        {
            if (npc?.Brain?.Memory == null || player == null) return null;
            var playerName = player.Name2 ?? player.Name1 ?? "";
            if (string.IsNullOrEmpty(playerName)) return null;

            List<MemoryEvent> witnessed;
            try
            {
                witnessed = npc.Brain.Memory.GetMemoriesOfType(MemoryType.WitnessedEvent);
            }
            catch
            {
                return null;
            }
            if (witnessed == null || witnessed.Count == 0) return null;

            // Filter to ones the player was the actor for. The description
            // shape is "Saw {actorName} {verb} {targetName}" -- match the
            // player's name appearing right after "Saw ".
            var prefix = $"Saw {playerName} ";
            var match = witnessed
                .Where(m => !string.IsNullOrEmpty(m.Description) && m.Description.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.Importance)
                .ThenByDescending(m => m.Timestamp)
                .FirstOrDefault();

            if (match == null) return null;

            // Don't read out the raw description ("Saw Lumina murder Vex");
            // surface an oblique reference. Positive-EmotionalImpact witness
            // events are rare (defending an ally) and get a different beat.
            if (match.EmotionalImpact > 0.2f)
            {
                return PickFreshLoc(npc, "dialogue.enhance.witness_pos", 3);
            }

            return PickFreshLoc(npc, "dialogue.enhance.witness_neg", 3);
        }

        // ---- Player state (HP, wealth) ----

        /// <summary>
        /// NPC notices the player's visible condition. Two gates: visibly
        /// hurt (HP below PlayerHurtThreshold of MaxHP) and visibly wealthy
        /// (Gold above PlayerWealthThreshold). Hurt comments are tone-aware
        /// (a hostile NPC mocks rather than worries). Wealth comments are
        /// gated to merchant-like personalities (high Greed) so a paladin
        /// type doesn't suddenly start ogling the player's purse.
        /// </summary>
        private static string? GetPlayerStateFlavor(global::NPC npc, global::Character player, Tone tone)
        {
            if (player == null || npc == null) return null;

            // Hurt check: only fire when both HP and MaxHP are sane values.
            // Default Character() has HP=0 / MaxHP=0 which would divide by
            // zero; guard so test-shaped characters don't trigger spurious
            // "you look terrible" output.
            if (player.MaxHP > 0 && player.HP > 0)
            {
                float hpFrac = (float)player.HP / player.MaxHP;
                if (hpFrac < PlayerHurtThreshold)
                {
                    if (tone == Tone.Negative)
                        return PickFreshLoc(npc, "dialogue.enhance.state_hurt_hostile", 3);
                    return PickFreshLoc(npc, "dialogue.enhance.state_hurt_friendly", 4);
                }
            }

            // Wealth check: only merchants / greedy types notice (Greed > 0.55).
            // Spouses / lovers are filtered out at the faction layer anyway,
            // but the wealth note is awkward from a partner so we skip here
            // for strong relationships too.
            if (player.Gold >= PlayerWealthThreshold && npc.Personality != null && npc.Personality.Greed > 0.55f)
            {
                return PickFreshLoc(npc, "dialogue.enhance.state_wealth_greedy", 4);
            }

            return null;
        }

        // ---- Grief (NPC-side, lightweight) ----

        /// <summary>
        /// Surfaces a grief reference when the NPC reads as actively
        /// mourning. Lightweight Phase 1.5 detector: requires both a strong
        /// Sadness emotion AND a recent death memory. Avoids false positives
        /// from NPCs who are sad-for-other-reasons (lost a job, breakup,
        /// post-combat fatigue). The flavor is intentionally non-specific
        /// (no spouse name) because we don't have an NPC-side bereavement
        /// tracker yet -- so we can't say WHO died without risking saying
        /// the wrong thing.
        /// </summary>
        private static string? GetGriefFlavor(global::NPC npc)
        {
            if (npc?.EmotionalState == null || npc.Brain?.Memory == null) return null;

            float sadness = npc.EmotionalState.GetEmotionIntensity(EmotionType.Sadness);
            if (sadness < GriefSadnessThreshold) return null;

            // Check for any recent death memory. SocialInfluenceSystem and
            // NPCSpawnSystem both write MemoryType.SawDeath when a known
            // character permadies. A witnessed-death memory close enough in
            // time is the proxy for "still grieving."
            List<MemoryEvent> deathMemories;
            try
            {
                deathMemories = npc.Brain.Memory.GetMemoriesOfType(MemoryType.SawDeath);
            }
            catch
            {
                return null;
            }
            if (deathMemories == null || deathMemories.Count == 0) return null;

            var cutoff = DateTime.Now.AddDays(-GriefMemoryWindowDays);
            bool hasRecentDeath = deathMemories.Any(m => m.Timestamp >= cutoff);
            if (!hasRecentDeath) return null;

            return PickFreshLoc(npc, "dialogue.enhance.grief", 4);
        }

        // ---- Personality ----

        /// <summary>
        /// One-shot trait-driven aside. Reads the dominant trait off the
        /// NPC's PersonalityProfile and produces a short flavor line that
        /// makes the NPC feel like a specific person rather than a template.
        /// </summary>
        private static string? GetPersonalityFlavor(global::NPC npc, Tone tone)
        {
            var p = npc?.Personality;
            if (p == null) return null;

            // Pick the trait with the largest deviation from neutral (0.5).
            // Avoids firing on lukewarm 0.5/0.5 NPCs and gives the strongest
            // voice to whichever trait is actually salient. Compassion is
            // derived from the *inverse* of Aggression and Vengefulness --
            // averaged inverses minus 0.5 baseline so an A=V=0.3 NPC reads
            // as 0.2 strength (salient) instead of 0 (the original formula
            // required both traits under 0.2 total to fire, which excluded
            // basically every NPC that wasn't a literal saint).
            float compassionStrength = MathF.Max(0f,
                ((1f - p.Aggression) + (1f - p.Vengefulness)) / 2f - 0.5f);

            var candidates = new (string label, float strength)[]
            {
                ("greed",          MathF.Max(0f, p.Greed - 0.5f)),
                ("trustworthy",    MathF.Max(0f, p.Trustworthiness - 0.5f)),
                ("compassion",     compassionStrength),
                ("courage",        MathF.Max(0f, p.Courage - 0.5f)),
                ("caution",        MathF.Max(0f, p.Caution - 0.5f)),
                ("ambition",       MathF.Max(0f, p.Ambition - 0.5f)),
            };

            var top = candidates.OrderByDescending(c => c.strength).First();
            if (top.strength < 0.15f) return null; // not salient enough to comment on

            // Tone gates: greed/ambition lines feel callous if NPC is grieving
            // (Sadness mood); compassion/peace asides clash with anger.
            switch (top.label)
            {
                case "greed":
                    if (tone == Tone.Negative) return null;
                    return PickFreshLoc(npc, "dialogue.enhance.trait_greed", 3);
                case "trustworthy":
                    return PickFreshLoc(npc, "dialogue.enhance.trait_trust", 3);
                case "compassion":
                    if (tone == Tone.Negative) return null;
                    return PickFreshLoc(npc, "dialogue.enhance.trait_compassion", 3);
                case "courage":
                    return PickFreshLoc(npc, "dialogue.enhance.trait_courage", 3);
                case "caution":
                    return PickFreshLoc(npc, "dialogue.enhance.trait_caution", 3);
                case "ambition":
                    return PickFreshLoc(npc, "dialogue.enhance.trait_ambition", 3);
                default:
                    return null;
            }
        }

        // ---- Faction ----

        /// <summary>
        /// Faction tension flavor based on NPC vs player faction relationship.
        /// Suppressed entirely for romance partners (Spouse/Lover/FWB) -- a
        /// spouse doesn't open with "Don't tell your priests we spoke."
        /// Softened on positive mood; standard hostile phrasing on negative.
        /// </summary>
        private static string? GetFactionTensionFlavor(global::NPC npc, global::Character player, Tone tone)
        {
            if (npc == null) return null;
            var npcFac = npc.NPCFaction;
            var playerFac = FactionSystem.Instance?.PlayerFaction;
            if (!npcFac.HasValue || !playerFac.HasValue) return null;

            // Same faction: bonded, not tense. Skip flavor.
            if (npcFac.Value == playerFac.Value) return null;

            // Romance suppression: a spouse / lover / FWB shouldn't surface
            // faction-tension lines. Their relationship has overwritten the
            // factional axis.
            try
            {
                // v0.63.0 slice 4 (audit M12): route through RomanceTracker.Instance
                // which already does the SessionContext -> fallback chain. Pre-fix
                // a direct SessionContext.Current?.Romance read returned null in
                // any non-online context (single-player + world-sim tick + tests)
                // and the faction-tension flavor line fired even for a Spouse.
                var romance = global::UsurperRemake.Systems.RomanceTracker.Instance;
                if (romance != null && !string.IsNullOrEmpty(npc.ID))
                {
                    var rel = romance.GetRelationType(npc.ID);
                    if (rel == RomanceRelationType.Spouse
                        || rel == RomanceRelationType.Lover
                        || rel == RomanceRelationType.FWB)
                    {
                        return null;
                    }
                }
            }
            catch
            {
                // Romance lookup failure is non-fatal -- just fall through.
            }

            bool faithShadowsConflict =
                (npcFac == Faction.TheFaith && playerFac == Faction.TheShadows) ||
                (npcFac == Faction.TheShadows && playerFac == Faction.TheFaith);
            if (faithShadowsConflict)
            {
                // Softer phrasing under positive mood (NPC is warm to the
                // player despite faction), full hostile under negative.
                if (tone == Tone.Positive)
                {
                    return npcFac == Faction.TheFaith
                        ? PickFreshLoc(npc, "dialogue.enhance.fac_fs_warm_faith", 2)
                        : PickFreshLoc(npc, "dialogue.enhance.fac_fs_warm_shadow", 2);
                }
                return npcFac == Faction.TheFaith
                    ? PickFreshLoc(npc, "dialogue.enhance.fac_fs_faith", 3)
                    : PickFreshLoc(npc, "dialogue.enhance.fac_fs_shadow", 3);
            }

            bool crownShadowsTension =
                (npcFac == Faction.TheCrown && playerFac == Faction.TheShadows) ||
                (npcFac == Faction.TheShadows && playerFac == Faction.TheCrown);
            if (crownShadowsTension)
            {
                if (tone == Tone.Positive)
                {
                    return npcFac == Faction.TheCrown
                        ? PickFreshLoc(npc, "dialogue.enhance.fac_cs_warm_crown", 2)
                        : PickFreshLoc(npc, "dialogue.enhance.fac_cs_warm_shadow", 2);
                }
                return npcFac == Faction.TheCrown
                    ? PickFreshLoc(npc, "dialogue.enhance.fac_cs_crown", 3)
                    : PickFreshLoc(npc, "dialogue.enhance.fac_cs_shadow", 3);
            }

            return null;
        }

        /// <summary>
        /// Legacy two-arg signature for any external caller. Defaults to
        /// neutral tone, which produces the standard hostile phrasing.
        /// Language-gated.
        /// </summary>
        public static string? GetFactionTensionFlavor(global::NPC npc, global::Character player)
            => IsEnglish() ? GetFactionTensionFlavor(npc, player, Tone.Neutral) : null;

        // ---- Language gate ----

        /// <summary>
        /// v0.61.4: enhancer flavor pools and the VN templates the enhancer
        /// wraps are now both localized to English AND Hungarian. Other
        /// languages (es/fr/it) still see the unmodified base line because
        /// VN templates and flavor pools have not been translated for them
        /// yet. The gate widens as more languages are translated.
        /// Returns true when the current session language is supported.
        /// Test/world-sim contexts without a SessionContext resolve to
        /// English via `GameConfig.Language`'s fallback chain (still supported).
        /// </summary>
        private static bool IsSupportedLanguage()
        {
            try
            {
                var lang = GameConfig.Language;
                if (string.IsNullOrEmpty(lang)) return true; // default to en
                return lang.Equals("en", StringComparison.OrdinalIgnoreCase)
                    || lang.Equals("hu", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }

        /// <summary>Legacy name kept for backward compat; delegates to <see cref="IsSupportedLanguage"/>.</summary>
        private static bool IsEnglish() => IsSupportedLanguage();

        // ---- Variety / cache ----

        /// <summary>
        /// Localized version of <see cref="PickFresh"/>. Builds numbered
        /// loc keys `{keyPrefix}_1` .. `{keyPrefix}_{count}`, resolves each
        /// via `Loc.Get`, then delegates to PickFresh for variety / cache.
        /// Uses keyPrefix as the cache layer so the recent-line window is
        /// shared across language switches mid-session (acceptable -- a
        /// player who switches language during a chat gets a brief variety
        /// dip, not a crash).
        /// </summary>
        private static string? PickFreshLoc(global::NPC npc, string keyPrefix, int count)
        {
            if (count <= 0) return null;
            var options = new string[count];
            for (int i = 0; i < count; i++)
                options[i] = Loc.Get($"{keyPrefix}_{i + 1}");
            return PickFresh(npc, keyPrefix, options);
        }

        /// <summary>
        /// Pick a random option, skipping anything in the per-(npc, layer)
        /// recent-line window. Updates the window with the chosen line.
        /// </summary>
        private static string? PickFresh(global::NPC npc, string layer, params string[] options)
        {
            if (options == null || options.Length == 0) return null;

            // Cache key per (npc, layer). Fall back to layer-only if NPC has
            // no stable ID -- prevents NRE and still gives some variety.
            string key = $"{npc?.ID ?? ""}|{layer}";

            Queue<string>? recent;
            if (!_recentLines.TryGetValue(key, out recent))
            {
                recent = new Queue<string>(RecentLineWindow);
                _recentLines[key] = recent;
            }

            // Soft cap guard so the static doesn't accumulate forever in
            // long-running MUD processes. When over cap, blow away the oldest
            // half wholesale -- the recency window is for short-term variety,
            // historical entries are fine to drop.
            if (_recentLines.Count > RecentCacheSoftCap)
            {
                var stale = _recentLines.Keys.Take(_recentLines.Count / 2).ToList();
                foreach (var k in stale) _recentLines.Remove(k);
            }

            var avoid = recent.ToHashSet();
            var fresh = options.Where(o => !avoid.Contains(o)).ToList();
            string choice = fresh.Count > 0
                ? fresh[_random.Next(fresh.Count)]
                : options[_random.Next(options.Length)];

            recent.Enqueue(choice);
            while (recent.Count > RecentLineWindow) recent.Dequeue();

            return choice;
        }
    }
}
