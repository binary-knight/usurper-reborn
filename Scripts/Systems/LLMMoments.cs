using System;
using System.Threading;
using System.Threading.Tasks;

namespace UsurperRemake.Systems;

/// <summary>
/// v0.64.0 Brain v2 Slice 5: in-world LLM moment generators.
///
/// Slice 5 ships ONE moment type to validate the architecture: PostAvengeNews
/// fires when an NPC's family-revenge goal completes (Avenge {killer} marked
/// IsCompleted via GoalSystem.OnGoalCompleted). The killer was already tracked
/// by the v0.63.0 family memory system + the v0.64.0 Slice 3 goal promotion,
/// so the LLM has rich context to render a dramatic news beat.
///
/// Slice 5b will add the other four moment types from the design doc:
///   - Personality summary on first contact (Team Corner examine, dialogue)
///   - Daily news rendering for top events (batch over 10-20 world events)
///   - Death epitaphs for named NPCs (permadeath cascade)
///   - Player-NPC relationship reflection (weekly per top-3 NPCs)
///
/// All five share the same pattern: structured context in, 1-2 sentence
/// rendered text out, fall back to a templated string when LLM is disabled or
/// the call fails. The LLM is decorative; the game runs fine with no key
/// configured.
/// </summary>
public static class LLMMoments
{
    // Shared style rule appended to every moment's system prompt. The
    // post-processing pass in SanitizeLLMOutput also strips these as
    // defense in depth -- Sonnet usually obeys but the constraint is soft.
    private const string PunctuationRule =
        " Use ASCII punctuation only. Do not use em-dashes, en-dashes, or " +
        "ellipsis characters; use commas, colons, two ASCII hyphens, or three " +
        "ASCII periods for pauses.";

    private const string SystemPromptAvenge =
        "You write brief in-world news entries for a dark fantasy MUD. " +
        "Output EXACTLY one or two sentences in third person, present tense. " +
        "Evocative but spare. Never break the fourth wall. Never use modern slang. " +
        "Never include quotation marks or character dialogue. " +
        "Focus on the act and its weight, not the inner monologue." +
        PunctuationRule;

    private const string SystemPromptEpitaph =
        "You write brief eulogies for fallen characters in a dark fantasy MUD. " +
        "Output EXACTLY one or two sentences in third person, past tense. " +
        "Solemn, evocative, never sentimental. Never break the fourth wall. " +
        "Never include quotation marks or character dialogue. " +
        "Reference the character's class or station, the killer, and the manner of death. " +
        "End with a sense of weight, not a moral." +
        PunctuationRule;

    private const string SystemPromptPersonalitySummary =
        "You write brief in-world character impressions for a dark fantasy MUD. " +
        "Output EXACTLY two or three sentences in third person, present tense. " +
        "Describe what kind of person this NPC seems to be, based on their " +
        "personality traits, archetype, and recent activity. " +
        "Spare and observational, like a stranger's first impression. " +
        "Never break the fourth wall. Never include quotation marks." +
        PunctuationRule;

    // v0.64.0 Brain v2 Slice 11a: dialogue mood prefix prompt.
    // Output shape is a 2-5 word italicized stage-direction prefix like
    // *scowling,* or *eyes alight,* -- short enough to prepend to a base
    // dialogue line without changing its meaning, just coloring its voice.
    // ASCII asterisks, trailing comma, no preamble.
    private const string SystemPromptDialogueMoodPrefix =
        "You write very short stage-direction prefixes for NPC dialogue in a " +
        "dark fantasy MUD. Output EXACTLY one short phrase between ASCII " +
        "asterisks followed by a comma, like *scowling,* or *eyes alight,* " +
        "or *voice tight with grief,*. Two to six words inside the asterisks. " +
        "Match the emotion and the NPC's personality / class / archetype so " +
        "the prefix feels specific to them rather than generic. Output ONLY " +
        "the prefix, no preamble, no quotation marks, no explanation." +
        PunctuationRule;

    // v0.64.0 Brain v2 Slice 11b: dialogue memory callback prompt.
    // The line is an OBLIQUE reference to a past interaction between the
    // NPC and the player. Append-position (after the base dialog line), so
    // it reads naturally as a follow-on thought. Short sentence, in-character
    // for the NPC, NEVER narrating the event verbatim ("I see you murdered
    // someone yesterday" is bad; "I haven't forgotten what you did" is good).
    private const string SystemPromptDialogueMemoryRef =
        "You write very short in-character lines for NPC dialogue in a dark " +
        "fantasy MUD. The line is an OBLIQUE reference to a past interaction " +
        "between the NPC and the player. Output EXACTLY one short sentence " +
        "(under 100 characters) in the NPC's voice. Match the emotional " +
        "valence (positive / negative / neutral). NEVER narrate the event " +
        "verbatim; allude to it sideways. Output ONLY the sentence, no " +
        "preamble, no quotation marks, no asterisks." +
        PunctuationRule;

    // v0.64.0 Brain v2 Slice 11c-batch: shared append-line prompt for the
    // remaining DialogueEnhancer layers (witness / player-state observation /
    // NPC grief / personality trait aside / faction tension). All five sit
    // in append position after the base dialog line. The user prompt carries
    // the per-layer framing (what kind of line: an allusion, an observation,
    // a confession, an aside, a barbed faction reference).
    private const string SystemPromptDialogueAppendLine =
        "You write very short in-character lines for NPC dialogue in a dark " +
        "fantasy MUD. The line appears AFTER the base dialog line as a " +
        "follow-on thought, observation, allusion, or aside (the user prompt " +
        "tells you which). Output EXACTLY one short sentence (under 100 " +
        "characters) in the NPC's voice. Match the layer's intent and tone. " +
        "Be specific to the NPC's personality, class, and archetype rather " +
        "than generic. Output ONLY the sentence, no preamble, no quotation " +
        "marks, no asterisks." +
        PunctuationRule;

    // v0.64.0 Brain v2 Slice 12b: dramatic-fork decision prompt. Used by
    // DecideForkAsync to pick one of N character-defining choices (accept
    // marriage / refuse, flee combat / press on, spare or kill defeated
    // enemy, etc). Lower temperature than dialogue / goal generation
    // because we want consistent character-driven decisions, not creative
    // variety. Output is constrained to a single number (the chosen
    // option's 1-indexed number).
    private const string SystemPromptForkDecision =
        "You are an NPC in a dark fantasy MUD making a character-defining " +
        "decision. Read the situation and the numbered options. Pick the " +
        "ONE option that best fits THIS SPECIFIC NPC's personality, " +
        "history, class, and the weight of the moment. Output ONLY the " +
        "number of your chosen option (e.g. just \"1\" or \"2\"). No " +
        "explanation, no quotes, no preamble." +
        PunctuationRule;

    // v0.64.0 Brain v2 Slice 12a: strategic-goal generator prompt. The LLM
    // designs 1-3 long-arc life goals for an NPC based on their personality,
    // class, level, current goal stack, recent significant memories, and
    // world state. Output is structured JSON so the goal system can parse
    // it reliably; we instruct the model to keep goals to short imperative
    // names that the existing GoalSystem's name-keyed dedup understands.
    private const string SystemPromptStrategicGoals =
        "You design 1-3 long-arc life goals for an NPC in a dark fantasy MUD. " +
        "Each goal MUST advance the NPC's character given their personality, " +
        "class, level, memories, and current world state. Goals should be " +
        "DIFFERENT from short-term necessity (the game's reactive system " +
        "handles 'heal wounds', 'earn money', 'become ruler' on its own). " +
        "Design strategic trajectory: vengeance, ambition, faith, family, " +
        "love, legacy. " +
        "OUTPUT FORMAT: a JSON array of objects, each with fields: " +
        "\"name\" (short imperative phrase under 40 chars), " +
        "\"type\" (one of: Personal, Social, Economic, Combat, Exploration), " +
        "\"priority\" (number 0.0-1.0, higher = more urgent), " +
        "\"target\" (string, optional NPC name for revenge/romance goals, " +
        "empty string if N/A). " +
        "Output ONLY the JSON array, no preamble, no explanation. Example: " +
        "[{\"name\":\"Avenge Maelketh's mark\",\"type\":\"Combat\"," +
        "\"priority\":0.85,\"target\":\"Old God Maelketh\"}]";

    /// <summary>
    /// Fire-and-forget post of an Avenge-completion news entry. Tries LLM
    /// first; falls back to templated text if LLM disabled / over budget /
    /// times out / errors. Either way, exactly one news entry is posted via
    /// NewsSystem within a few seconds.
    ///
    /// Callers (GoalSystem.OnGoalCompleted) do NOT need to await this -- it
    /// runs in the background and the world-sim tick continues.
    /// </summary>
    public static Task PostAvengeNewsAsync(NPC avenger, string killerName)
    {
        if (avenger == null || string.IsNullOrWhiteSpace(killerName))
        {
            return Task.CompletedTask;
        }

        // Capture context snapshot BEFORE awaiting so the async path sees
        // stable values (the world-sim tick may mutate avenger state by the
        // time the LLM responds).
        string avengerName = avenger.Name2 ?? avenger.Name1 ?? "An unknown soul";
        string avengerClass = avenger.Class.ToString();
        int avengerLevel = (int)avenger.Level;

        // Templated fallback (always available; matches the Slice 3
        // OnGoalCompleted text that was previously posted inline).
        string fallback = $"{avengerName} has avenged the blood of their kin. {killerName} is dead.";

        return Task.Run(async () =>
        {
            string text = fallback;
            string? failureReason = "llm_disabled";
            int promptTok = 0, completionTok = 0, totalTok = 0, responseMs = 0;
            bool succeeded = false;

            try
            {
                var provider = LLMProvider.Get();
                if (provider != null)
                {
                    string userPrompt =
                        $"{avengerName}, a {avengerClass} (Lv {avengerLevel}), " +
                        $"has finally killed {killerName}, the slayer of " +
                        $"{avengerName}'s kin. Write the news flash announcing this moment.";

                    var llmResp = await provider.CompleteAsync(new LLMRequest
                    {
                        SystemPrompt = SystemPromptAvenge,
                        UserPrompt = userPrompt,
                        MaxTokens = 120,
                        Temperature = 0.85,
                    }, CancellationToken.None);

                    if (llmResp != null && !string.IsNullOrWhiteSpace(llmResp.Text))
                    {
                        text = SanitizeLLMOutput(llmResp.Text);
                        promptTok = llmResp.PromptTokens;
                        completionTok = llmResp.CompletionTokens;
                        totalTok = llmResp.TotalTokens;
                        responseMs = llmResp.ResponseMs;
                        succeeded = true;
                        failureReason = null;
                    }
                    else
                    {
                        failureReason = "llm_call_returned_null";
                    }
                }
                NewsSystem.Instance?.Newsy(text);
            }
            catch (Exception ex)
            {
                // Never let the LLM moment break the world. Always post fallback.
                failureReason = $"exception: {ex.GetType().Name}: {ex.Message}";
                DebugLogger.Instance.LogError("LLM",
                    $"PostAvengeNewsAsync failed: {ex.Message}. Posting templated fallback.");
                try { NewsSystem.Instance?.Newsy(fallback); } catch { }
            }
            finally
            {
                // Always record the attempt for the balance dashboard.
                try
                {
                    SqlBackend?.RecordLLMUsage(
                        "avenge", avengerName, succeeded,
                        promptTok, completionTok, totalTok, responseMs,
                        text, failureReason);
                }
                catch { /* recording is best-effort */ }
            }
        });
    }

    // v0.64.0 Brain v2 Slice 10: shared access to the SQL backend for LLM
    // telemetry. Reuses the already-wired WorldSimulator.SqlBackend static
    // property so we don't take a second dependency on save-system init order.
    private static SqlSaveBackend? SqlBackend => WorldSimulator.SqlBackend;

    /// <summary>
    /// v0.64.0 Brain v2 Slice 9a: death epitaph for a notable NPC who just
    /// died. Same pattern as PostAvengeNewsAsync -- fire-and-forget,
    /// templated fallback always works, LLM-rendered when available. Wired
    /// into WorldSimulator.MarkNPCDead for Tier A NPCs (named/notable subset)
    /// so commoner deaths don't spam the news feed. The standard
    /// NewsSystem.WriteDeathNews still fires immediately for all deaths;
    /// this adds a dramatic supplemental beat for the ones that warrant it.
    /// </summary>
    public static Task PostDeathEpitaphAsync(NPC deceased, string killerName, string location)
    {
        if (deceased == null) return Task.CompletedTask;

        string name = deceased.Name2 ?? deceased.Name1 ?? "An unknown soul";
        string charClass = deceased.Class.ToString();
        int level = (int)deceased.Level;
        string killer = string.IsNullOrWhiteSpace(killerName) ? "an unknown hand" : killerName;
        string loc = string.IsNullOrWhiteSpace(location) ? "the wilds" : location;

        string fallback = $"{name} the {charClass} (Lv {level}) has fallen at {loc} to {killer}. The realm marks the loss.";

        return Task.Run(async () =>
        {
            string text = fallback;
            string? failureReason = "llm_disabled";
            int promptTok = 0, completionTok = 0, totalTok = 0, responseMs = 0;
            bool succeeded = false;

            try
            {
                var provider = LLMProvider.Get();
                if (provider != null)
                {
                    string userPrompt =
                        $"{name}, a Lv {level} {charClass}, has been killed by {killer} at {loc}. " +
                        $"Write the eulogy.";

                    var llmResp = await provider.CompleteAsync(new LLMRequest
                    {
                        SystemPrompt = SystemPromptEpitaph,
                        UserPrompt = userPrompt,
                        MaxTokens = 100,
                        Temperature = 0.8,
                    }, CancellationToken.None);

                    if (llmResp != null && !string.IsNullOrWhiteSpace(llmResp.Text))
                    {
                        text = SanitizeLLMOutput(llmResp.Text);
                        promptTok = llmResp.PromptTokens;
                        completionTok = llmResp.CompletionTokens;
                        totalTok = llmResp.TotalTokens;
                        responseMs = llmResp.ResponseMs;
                        succeeded = true;
                        failureReason = null;
                    }
                    else
                    {
                        failureReason = "llm_call_returned_null";
                    }
                }
                NewsSystem.Instance?.Newsy(text);
            }
            catch (Exception ex)
            {
                failureReason = $"exception: {ex.GetType().Name}: {ex.Message}";
                DebugLogger.Instance.LogError("LLM",
                    $"PostDeathEpitaphAsync failed: {ex.Message}. Posting templated fallback.");
                try { NewsSystem.Instance?.Newsy(fallback); } catch { }
            }
            finally
            {
                try
                {
                    SqlBackend?.RecordLLMUsage(
                        "death_epitaph", name, succeeded,
                        promptTok, completionTok, totalTok, responseMs,
                        text, failureReason);
                }
                catch { /* recording is best-effort */ }
            }
        });
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 9b: personality summary for an NPC the player
    /// is examining for the first time. Cached on the NPC's `Brain.LastPersonalitySummary`
    /// after first generation so subsequent examines are free. Returns a
    /// rendered summary string -- caller (TeamCornerLocation.ExamineMember
    /// or similar) displays it. Unlike Avenge / Epitaph this isn't
    /// fire-and-forget; the caller awaits because the player is waiting on
    /// a UI screen.
    ///
    /// Returns the templated fallback immediately when LLM is disabled or
    /// the call fails. Cap caller's wait at the LLM timeout (default 3s).
    /// </summary>
    public static async Task<string> GeneratePersonalitySummaryAsync(NPC npc, CancellationToken ct)
    {
        if (npc == null) return "";

        // Cache check.
        var cached = npc.PersonalitySummaryCache;
        if (!string.IsNullOrEmpty(cached)) return cached;

        string name = npc.Name2 ?? npc.Name1 ?? "This stranger";
        string charClass = npc.Class.ToString();
        string archetype = npc.Archetype ?? "citizen";
        var p = npc.Brain?.Personality;

        string fallback = FormatTemplatedPersonality(name, charClass, archetype, p);

        string resultText = fallback;
        string? failureReason = "llm_disabled";
        int promptTok = 0, completionTok = 0, totalTok = 0, responseMs = 0;
        bool succeeded = false;

        try
        {
            var provider = LLMProvider.Get();
            if (provider != null)
            {
                // Compose a structured personality description for the prompt.
                string personalityDesc = p == null ? "balanced disposition" : FormatPersonalityTraits(p);
                string userPrompt =
                    $"{name} is a Lv {npc.Level} {charClass} of the {archetype} archetype. " +
                    $"Their personality: {personalityDesc}. " +
                    $"They are currently at {npc.CurrentLocation}. " +
                    $"Write the first-impression summary.";

                var llmResp = await provider.CompleteAsync(new LLMRequest
                {
                    SystemPrompt = SystemPromptPersonalitySummary,
                    UserPrompt = userPrompt,
                    MaxTokens = 150,
                    Temperature = 0.75,
                }, ct);

                if (llmResp != null && !string.IsNullOrWhiteSpace(llmResp.Text))
                {
                    resultText = SanitizeLLMOutput(llmResp.Text);
                    promptTok = llmResp.PromptTokens;
                    completionTok = llmResp.CompletionTokens;
                    totalTok = llmResp.TotalTokens;
                    responseMs = llmResp.ResponseMs;
                    succeeded = true;
                    failureReason = null;
                }
                else
                {
                    failureReason = "llm_call_returned_null";
                }
            }
            npc.PersonalitySummaryCache = resultText;
        }
        catch (Exception ex)
        {
            failureReason = $"exception: {ex.GetType().Name}: {ex.Message}";
            DebugLogger.Instance.LogError("LLM",
                $"GeneratePersonalitySummaryAsync failed: {ex.Message}. Returning fallback.");
            npc.PersonalitySummaryCache = fallback;
            resultText = fallback;
        }
        finally
        {
            try
            {
                SqlBackend?.RecordLLMUsage(
                    "personality_summary", name, succeeded,
                    promptTok, completionTok, totalTok, responseMs,
                    resultText, failureReason);
            }
            catch { /* recording is best-effort */ }
        }

        return resultText;
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 11a/11b: per-NPC LLM dialogue flavor enrichment.
    /// Called from DialogueEnhancer's pick helpers on a cache miss --
    /// generates a single short flavor line grounded in the NPC's actual
    /// personality / class / archetype (and optionally a context blurb the
    /// caller supplies, e.g. memory description), then caches it on the NPC
    /// so future enhances of the same key reuse the generated voice.
    /// Fire-and-forget from the caller's perspective (the caller has already
    /// returned the localized template synchronously); this method runs on a
    /// background Task.Run.
    ///
    /// Slice 11a handles mood layers (keyPrefix starts with "mood_").
    /// Slice 11b adds memory layers (keyPrefix starts with "memory_") --
    /// the caller passes the actual memory description in `extraContext`
    /// so the LLM can ground the line in what actually happened instead of
    /// guessing. Other layers (witness, state, trait, grief, faction)
    /// still return null and DialogueEnhancer keeps the existing localized
    /// pool; 11c expands to cover those.
    /// </summary>
    public static async Task<string?> GenerateDialogueFlavorAsync(
        NPC npc, string layer, string tone, CancellationToken ct,
        string? extraContext = null)
    {
        if (npc == null || string.IsNullOrEmpty(layer)) return null;

        // Layer-family dispatch.
        // - mood_* (Slice 11a): stage-direction prefix shape (*phrase,*)
        // - memory_* (Slice 11b): oblique callback to past interaction
        // - witness_* / state_* / grief / trait_* / fac_* (Slice 11 batch):
        //   short append-position observation in NPC voice
        // Anything else short-circuits so future expansion is purely additive.
        bool isMood = layer.StartsWith("mood_", StringComparison.OrdinalIgnoreCase);
        bool isMemory = layer.StartsWith("memory_", StringComparison.OrdinalIgnoreCase);
        bool isAppend =
            layer.StartsWith("witness_", StringComparison.OrdinalIgnoreCase) ||
            layer.StartsWith("state_", StringComparison.OrdinalIgnoreCase) ||
            layer.Equals("grief", StringComparison.OrdinalIgnoreCase) ||
            layer.StartsWith("trait_", StringComparison.OrdinalIgnoreCase) ||
            layer.StartsWith("fac_", StringComparison.OrdinalIgnoreCase);
        if (!isMood && !isMemory && !isAppend) return null;

        string name = npc.Name2 ?? npc.Name1 ?? "the NPC";
        string charClass = npc.Class.ToString();
        string archetype = npc.Archetype ?? "citizen";
        var p = npc.Brain?.Personality;
        string personalityDesc = p == null ? "balanced disposition" : FormatPersonalityTraits(p);

        string systemPrompt;
        string userPrompt;

        if (isMood)
        {
            string emotion = layer.Substring("mood_".Length);
            systemPrompt = SystemPromptDialogueMoodPrefix;
            userPrompt =
                $"NPC: {name}, a {charClass} of the {archetype} archetype. " +
                $"Personality: {personalityDesc}. " +
                $"Current dominant emotion: {emotion} (tone band: {tone}). " +
                $"Write the stage-direction prefix.";
        }
        else if (isMemory)
        {
            string valence = layer.Substring("memory_".Length); // pos / neg / neu
            string valenceWord = valence switch
            {
                "pos" => "positive (a kindness, debt, gratitude)",
                "neg" => "negative (a wrong, grudge, betrayal)",
                _ => "neutral (a meaningful but ambiguous prior interaction)",
            };
            string contextLine = string.IsNullOrWhiteSpace(extraContext)
                ? "(no specific memory description available)"
                : $"Memory: {extraContext}";
            systemPrompt = SystemPromptDialogueMemoryRef;
            userPrompt =
                $"NPC: {name}, a {charClass} of the {archetype} archetype. " +
                $"Personality: {personalityDesc}. " +
                $"Valence: {valenceWord}. " +
                $"{contextLine} " +
                $"Write the oblique callback line.";
        }
        else // isAppend -- witness / state / grief / trait / faction
        {
            systemPrompt = SystemPromptDialogueAppendLine;
            string framing = BuildAppendLayerFraming(layer, extraContext);
            userPrompt =
                $"NPC: {name}, a {charClass} of the {archetype} archetype. " +
                $"Personality: {personalityDesc}. " +
                $"{framing} " +
                $"Write the line.";
        }

        string? resultText = null;
        string? failureReason = "llm_disabled";
        int promptTok = 0, completionTok = 0, totalTok = 0, responseMs = 0;
        bool succeeded = false;

        try
        {
            var provider = LLMProvider.Get();
            if (provider != null)
            {
                var llmResp = await provider.CompleteAsync(new LLMRequest
                {
                    SystemPrompt = systemPrompt,
                    UserPrompt = userPrompt,
                    MaxTokens = isMood ? 40 : 80,
                    Temperature = 0.9,
                }, ct);

                if (llmResp != null && !string.IsNullOrWhiteSpace(llmResp.Text))
                {
                    resultText = isMood
                        ? SanitizeDialogueFlavor(llmResp.Text)
                        : SanitizeDialogueSentence(llmResp.Text);
                    promptTok = llmResp.PromptTokens;
                    completionTok = llmResp.CompletionTokens;
                    totalTok = llmResp.TotalTokens;
                    responseMs = llmResp.ResponseMs;
                    succeeded = !string.IsNullOrWhiteSpace(resultText);
                    failureReason = succeeded ? null : "llm_sanitize_empty";
                }
                else
                {
                    failureReason = "llm_call_returned_null";
                }
            }
        }
        catch (Exception ex)
        {
            failureReason = $"exception: {ex.GetType().Name}: {ex.Message}";
            DebugLogger.Instance.LogError("LLM",
                $"GenerateDialogueFlavorAsync({layer}) failed: {ex.Message}. " +
                $"DialogueEnhancer keeps templated fallback.");
            resultText = null;
        }
        finally
        {
            try
            {
                SqlBackend?.RecordLLMUsage(
                    $"dialogue_{layer}", name, succeeded,
                    promptTok, completionTok, totalTok, responseMs,
                    resultText, failureReason);
            }
            catch { /* recording is best-effort */ }
        }

        return resultText;
    }

    /// <summary>
    /// Per-sub-layer framing for the SystemPromptDialogueAppendLine prompt
    /// family. Builds the layer-specific intent + valence portion of the
    /// user prompt so each append layer (witness, state, grief, trait,
    /// faction) lands with the right framing without needing its own
    /// system prompt.
    /// </summary>
    private static string BuildAppendLayerFraming(string layer, string? extraContext)
    {
        string ctxLine = string.IsNullOrWhiteSpace(extraContext)
            ? ""
            : $"Context: {extraContext} ";

        // witness_*: NPC saw the player do something noteworthy.
        if (layer.StartsWith("witness_", StringComparison.OrdinalIgnoreCase))
        {
            string valence = layer.Substring("witness_".Length);
            if (valence.Equals("pos", StringComparison.OrdinalIgnoreCase))
                return $"Layer: WITNESS (positive). You saw the player do something " +
                       $"admirable (defending an ally, an act of mercy). Allude " +
                       $"to it sideways; hint you noticed without spelling it out. " +
                       $"{ctxLine}";
            return $"Layer: WITNESS (negative). You saw the player do something " +
                   $"wrong (a killing, theft, cruelty). Allude to it sideways; " +
                   $"hint you noticed without naming the act. {ctxLine}";
        }

        // state_*: NPC observes the player's current condition.
        if (layer.StartsWith("state_", StringComparison.OrdinalIgnoreCase))
        {
            string kind = layer.Substring("state_".Length);
            return kind.ToLowerInvariant() switch
            {
                "hurt_hostile" =>
                    "Layer: OBSERVATION. The player looks visibly hurt. You have " +
                    "no warmth for them; mock or note coldly. " + ctxLine,
                "hurt_friendly" =>
                    "Layer: OBSERVATION. The player looks visibly hurt. You care; " +
                    "note their condition with concern. " + ctxLine,
                "wealth_greedy" =>
                    "Layer: OBSERVATION. The player carries an obvious purse of gold " +
                    "and you are greedy by nature. Acknowledge it suggestively " +
                    "without crossing into begging. " + ctxLine,
                _ =>
                    "Layer: OBSERVATION. Note something about the player's current " +
                    "condition. " + ctxLine,
            };
        }

        // grief: NPC's own recent loss surfaces.
        if (layer.Equals("grief", StringComparison.OrdinalIgnoreCase))
        {
            return "Layer: GRIEF. You are mourning a recent loss. Surface the grief " +
                   "OBLIQUELY without naming a specific person -- you don't owe the " +
                   "player a name, just a flicker of the wound. " + ctxLine;
        }

        // trait_*: NPC personality aside.
        if (layer.StartsWith("trait_", StringComparison.OrdinalIgnoreCase))
        {
            string trait = layer.Substring("trait_".Length);
            return trait.ToLowerInvariant() switch
            {
                "greed" =>
                    "Layer: TRAIT (greed). You are visibly greedy. The line reveals " +
                    "that trait casually: a side remark about coin, transactions, " +
                    "or who is paying. " + ctxLine,
                "trust" =>
                    "Layer: TRAIT (trustworthiness). You are notably honest. The line " +
                    "reveals straight-shooter character. " + ctxLine,
                "compassion" =>
                    "Layer: TRAIT (compassion). You are warm-hearted. The line reveals " +
                    "that warmth without being saccharine. " + ctxLine,
                "courage" =>
                    "Layer: TRAIT (courage). You are bold. The line reveals fearless " +
                    "or stubborn character. " + ctxLine,
                "caution" =>
                    "Layer: TRAIT (caution). You are watchful, careful, slow to commit. " +
                    "The line reveals that wariness. " + ctxLine,
                "ambition" =>
                    "Layer: TRAIT (ambition). You are driven, climbing. The line reveals " +
                    "appetite for advancement or power. " + ctxLine,
                _ =>
                    "Layer: TRAIT. Reveal a defining personality trait in the line. " + ctxLine,
            };
        }

        // fac_*: faction tension. Sublayers carry the shape:
        //   fac_fs_faith / fac_fs_shadow                (Faith vs Shadows hostile)
        //   fac_fs_warm_faith / fac_fs_warm_shadow      (Faith vs Shadows softened)
        //   fac_cs_crown / fac_cs_shadow                (Crown vs Shadows hostile)
        //   fac_cs_warm_crown / fac_cs_warm_shadow      (Crown vs Shadows softened)
        if (layer.StartsWith("fac_", StringComparison.OrdinalIgnoreCase))
        {
            bool isWarm = layer.Contains("_warm_");
            bool isFsAxis = layer.StartsWith("fac_fs_", StringComparison.OrdinalIgnoreCase);
            bool isCsAxis = layer.StartsWith("fac_cs_", StringComparison.OrdinalIgnoreCase);
            string npcFac = layer.EndsWith("_faith") ? "Faith"
                : layer.EndsWith("_shadow") ? "Shadows"
                : layer.EndsWith("_crown") ? "Crown"
                : "your faction";
            string playerFac;
            if (isFsAxis)
                playerFac = npcFac == "Faith" ? "Shadows" : "Faith";
            else if (isCsAxis)
                playerFac = npcFac == "Crown" ? "Shadows" : "Crown";
            else
                playerFac = "the opposing faction";

            string toneHint = isWarm
                ? "the mood is positive so the line is WARY rather than hostile -- " +
                  "civil tension, not open scorn"
                : "the line is openly hostile or pointedly cold";

            return $"Layer: FACTION TENSION. You are sworn to the {npcFac}; the " +
                   $"player is sworn to the {playerFac}. Surface that tension: " +
                   $"{toneHint}. Reference your faction's stance obliquely (its " +
                   $"oaths, priests, masters, codes). {ctxLine}";
        }

        // Default fallback (shouldn't reach here given dispatch above).
        return "Layer: GENERIC APPEND. Write a short in-character follow-on line. " + ctxLine;
    }

    /// <summary>
    /// Sanitizer for non-prefix dialogue lines (memory callbacks, witness
    /// allusions). Reuses SanitizeLLMOutput's punctuation normalization
    /// then enforces: no wrapping asterisks (we're an APPEND line, not a
    /// stage direction), no wrapping quotes, ends with terminal punctuation,
    /// length cap. Returns null if the model emitted something so off-shape
    /// that the templated fallback is better than caching the result.
    /// </summary>
    private static string? SanitizeDialogueSentence(string raw)
    {
        var t = SanitizeLLMOutput(raw);
        if (string.IsNullOrWhiteSpace(t)) return null;

        // Strip wrapping asterisks if the model accidentally produced a
        // stage-direction shape for an append-position line.
        if (t.StartsWith("*") && t.EndsWith("*") && t.Length >= 2)
            t = t.Substring(1, t.Length - 2).Trim();

        // Cap length defensively (memory callbacks should be < 120 chars).
        if (t.Length > 160) t = t.Substring(0, 157).TrimEnd() + "...";

        if (string.IsNullOrWhiteSpace(t)) return null;

        // Ensure terminal punctuation so the line concatenates cleanly with
        // the base dialog line above it. If the model ended mid-thought,
        // tack on a period.
        char last = t[t.Length - 1];
        if (last != '.' && last != '!' && last != '?' && last != '"' && last != ')')
            t += ".";

        return t;
    }

    /// <summary>
    /// Tighter sanitizer for dialogue-prefix output. Reuses SanitizeLLMOutput's
    /// punctuation normalization, then additionally:
    ///   * trims trailing periods / colons that don't belong on a prefix
    ///   * enforces the asterisk-wrapped shape (rejects unwrapped output)
    ///   * caps length so a runaway response doesn't dominate the dialog line
    /// Returns null if the model produced something so off-shape that it
    /// shouldn't be cached or used.
    /// </summary>
    private static string? SanitizeDialogueFlavor(string raw)
    {
        var t = SanitizeLLMOutput(raw);
        if (string.IsNullOrWhiteSpace(t)) return null;

        // Cap length defensively (mood prefixes should be < 60 chars).
        if (t.Length > 80) t = t.Substring(0, 80).TrimEnd();

        // Require the *phrase,* shape so the prefix matches the existing
        // localized pool's voice (mood prefixes in en.json/hu.json look like
        // *scowling,* -- comma BEFORE the closing asterisk, not after).
        // If the model dropped the asterisks, wrap. If it placed the comma
        // outside the asterisks (or omitted it), normalize to inside.
        int firstStar = t.IndexOf('*');
        int lastStar = t.LastIndexOf('*');
        if (firstStar < 0 || lastStar <= firstStar)
        {
            // No paired asterisks -- treat as plain text and wrap.
            t = t.TrimEnd('.', ',', ';', ':', '!', '?');
            if (string.IsNullOrWhiteSpace(t)) return null;
            t = $"*{t},*";
        }
        else
        {
            // Has paired asterisks. Strip any trailing punctuation that
            // appears AFTER the closing star (a stray "*phrase,*," or
            // "*phrase,*." from a careless model). Then ensure the inside
            // ends with a comma right before the closing star.
            string inside = t.Substring(firstStar + 1, lastStar - firstStar - 1).TrimEnd();
            inside = inside.TrimEnd('.', ';', ':', '!', '?'); // keep commas, strip other terminal punct
            if (!inside.EndsWith(","))
                inside += ",";
            if (string.IsNullOrWhiteSpace(inside.TrimEnd(','))) return null;
            t = $"*{inside}*";
        }

        return t;
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 12a: candidate goal returned from the LLM
    /// strategic-goal generator. Caller (GoalSystem.TryRefreshStrategicGoals)
    /// converts each candidate into a Goal via AddGoal, where the existing
    /// name-keyed dedup prevents duplicates if a long-arc goal name persists
    /// across refreshes.
    /// </summary>
    public class GoalCandidate
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "Personal";
        public float Priority { get; set; } = 0.5f;
        public string? TargetCharacter { get; set; }
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 12b: LLM-arbitrated dramatic-fork decision.
    /// Unlike Slice 11/12a which return text and fall back to templates,
    /// forks BLOCK on a decision -- the caller awaits because the fork is
    /// happening NOW (player just proposed marriage, NPC about to flee or
    /// press on, etc.). Returns the 0-indexed choice the LLM picked, or
    /// the deterministicFallback on any failure (LLM disabled, timeout,
    /// parse error, exception). Always returns a valid index into `choices`.
    ///
    /// Forks should be LOW-FREQUENCY, HIGH-STAKES decisions where the
    /// ~2-3s LLM latency is acceptable (one-shot narrative moments, not
    /// per-round combat). High-frequency forks (per-tick scoring decisions)
    /// belong in BrainV2Scorer, not here -- those are deterministic by
    /// design and would blow the latency budget if LLM-arbitrated.
    /// </summary>
    public static async Task<int> DecideForkAsync(
        NPC npc, string forkType, string situation,
        System.Collections.Generic.IReadOnlyList<string> choices,
        int deterministicFallback, CancellationToken ct)
    {
        if (npc == null || choices == null || choices.Count == 0) return deterministicFallback;
        if (deterministicFallback < 0 || deterministicFallback >= choices.Count) deterministicFallback = 0;

        string name = npc.Name2 ?? npc.Name1 ?? "the NPC";
        string charClass = npc.Class.ToString();
        string archetype = npc.Archetype ?? "citizen";
        var p = npc.Brain?.Personality;
        string personalityDesc = p == null ? "balanced disposition" : FormatPersonalityTraits(p);

        int chosen = deterministicFallback;
        string? failureReason = "llm_disabled";
        int promptTok = 0, completionTok = 0, totalTok = 0, responseMs = 0;
        bool succeeded = false;
        string? rawText = null;

        try
        {
            var provider = LLMProvider.Get();
            if (provider != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"You are {name}, a Lv {(int)npc.Level} {charClass} of the {archetype} archetype.");
                sb.AppendLine($"Personality: {personalityDesc}.");
                sb.AppendLine($"Current state: HP {(int)npc.HP}/{(int)npc.MaxHP}, Gold {npc.Gold}.");
                sb.AppendLine();
                sb.AppendLine($"Situation: {situation}");
                sb.AppendLine();
                sb.AppendLine("Choices:");
                for (int i = 0; i < choices.Count; i++)
                {
                    sb.AppendLine($"{i + 1}. {choices[i]}");
                }
                sb.AppendLine();
                sb.AppendLine("Output ONLY the number of your chosen option.");
                string userPrompt = sb.ToString();

                var llmResp = await provider.CompleteAsync(new LLMRequest
                {
                    SystemPrompt = SystemPromptForkDecision,
                    UserPrompt = userPrompt,
                    MaxTokens = 10,
                    Temperature = 0.6, // lower than dialogue: we want consistent character decisions
                }, ct);

                if (llmResp != null && !string.IsNullOrWhiteSpace(llmResp.Text))
                {
                    rawText = llmResp.Text;
                    promptTok = llmResp.PromptTokens;
                    completionTok = llmResp.CompletionTokens;
                    totalTok = llmResp.TotalTokens;
                    responseMs = llmResp.ResponseMs;

                    int parsed = ParseForkChoice(llmResp.Text, choices.Count);
                    if (parsed >= 0)
                    {
                        chosen = parsed;
                        succeeded = true;
                        failureReason = null;
                    }
                    else
                    {
                        failureReason = "llm_parse_unparseable";
                    }
                }
                else
                {
                    failureReason = "llm_call_returned_null";
                }
            }
        }
        catch (Exception ex)
        {
            failureReason = $"exception: {ex.GetType().Name}: {ex.Message}";
            DebugLogger.Instance.LogError("LLM",
                $"DecideForkAsync({forkType}) failed for {name}: {ex.Message}. " +
                $"Falling back to deterministic choice {deterministicFallback}.");
        }
        finally
        {
            try
            {
                string summary = chosen >= 0 && chosen < choices.Count
                    ? $"chose {chosen + 1}: {choices[chosen]}"
                    : rawText ?? "";
                SqlBackend?.RecordLLMUsage(
                    $"fork_{forkType}", name, succeeded,
                    promptTok, completionTok, totalTok, responseMs,
                    summary, failureReason);
            }
            catch { /* recording is best-effort */ }
        }

        return chosen;
    }

    /// <summary>
    /// Extract a 1-indexed choice number from the model's response and convert
    /// to a 0-indexed array index. Returns -1 if no valid number found.
    /// Tolerant of leading whitespace, surrounding prose, and trailing
    /// punctuation -- finds the FIRST digit sequence and takes that.
    /// </summary>
    private static int ParseForkChoice(string raw, int choiceCount)
    {
        if (string.IsNullOrWhiteSpace(raw) || choiceCount <= 0) return -1;

        // Find the first digit run in the response.
        int start = -1;
        int end = -1;
        for (int i = 0; i < raw.Length; i++)
        {
            if (char.IsDigit(raw[i]))
            {
                if (start < 0) start = i;
                end = i;
            }
            else if (start >= 0)
            {
                break;
            }
        }
        if (start < 0) return -1;

        if (!int.TryParse(raw.Substring(start, end - start + 1), out int n)) return -1;
        if (n < 1 || n > choiceCount) return -1;

        return n - 1; // convert to 0-indexed
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 12a: strategic-goal generator. Called from
    /// GoalSystem.TryRefreshStrategicGoals when the per-NPC throttle window
    /// elapses (default 6 wall-clock hours per NPC). Returns 1-3 LLM-designed
    /// long-arc goals tailored to this NPC's personality / class / memories /
    /// current world state.
    ///
    /// Slice 12a scope: Brain v2 cohort only, online mode only. Heuristic
    /// cohort and single-player keep the existing reactive-only goal flow.
    /// Fire-and-forget from the caller; goals land in the NPC's stack
    /// asynchronously and the next BrainV2Scorer tick reads them.
    /// </summary>
    public static async Task<List<GoalCandidate>> GenerateStrategicGoalsAsync(
        NPC npc, CancellationToken ct)
    {
        var empty = new List<GoalCandidate>();
        if (npc == null) return empty;

        string name = npc.Name2 ?? npc.Name1 ?? "An unknown soul";
        string charClass = npc.Class.ToString();
        string archetype = npc.Archetype ?? "citizen";
        int level = (int)npc.Level;
        var p = npc.Brain?.Personality;
        string personalityDesc = p == null ? "balanced disposition" : FormatPersonalityTraits(p);

        // Snapshot the current goal stack so the LLM doesn't duplicate.
        string currentGoals = "(none)";
        try
        {
            var existing = npc.Brain?.Goals?.GetActiveGoals();
            if (existing != null && existing.Count > 0)
                currentGoals = string.Join("; ", existing.Take(8).Select(g => g.Name));
        }
        catch { /* defensive */ }

        // Recent significant memories: top 5 by importance from the past week.
        string recentMemories = "(none)";
        try
        {
            var memList = npc.Brain?.Memory?.AllMemories;
            if (memList != null && memList.Count > 0)
            {
                var top = memList
                    .Where(m => m.IsRecent(168)) // last week
                    .OrderByDescending(m => m.Importance)
                    .Take(5)
                    .Select(m => m.Description)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .ToList();
                if (top.Count > 0) recentMemories = string.Join("; ", top);
            }
        }
        catch { /* defensive */ }

        string family = "(unknown)";
        try
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(npc.MotherName)) parts.Add($"mother {npc.MotherName}");
            if (!string.IsNullOrEmpty(npc.FatherName)) parts.Add($"father {npc.FatherName}");
            if (npc.Married || npc.IsMarried) parts.Add($"married to {npc.SpouseName ?? "spouse"}");
            if (parts.Count > 0) family = string.Join(", ", parts);
        }
        catch { /* defensive */ }

        var results = new List<GoalCandidate>();
        string? failureReason = "llm_disabled";
        int promptTok = 0, completionTok = 0, totalTok = 0, responseMs = 0;
        bool succeeded = false;
        string? rawText = null;

        try
        {
            var provider = LLMProvider.Get();
            if (provider != null)
            {
                string userPrompt =
                    $"NPC: {name}, a Lv {level} {charClass} of the {archetype} archetype. " +
                    $"Personality: {personalityDesc}. " +
                    $"Current goals: {currentGoals}. " +
                    $"Recent memories: {recentMemories}. " +
                    $"Family: {family}. " +
                    $"Design 1 to 3 strategic life goals.";

                var llmResp = await provider.CompleteAsync(new LLMRequest
                {
                    SystemPrompt = SystemPromptStrategicGoals,
                    UserPrompt = userPrompt,
                    MaxTokens = 300,
                    Temperature = 0.85,
                }, ct);

                if (llmResp != null && !string.IsNullOrWhiteSpace(llmResp.Text))
                {
                    rawText = llmResp.Text;
                    promptTok = llmResp.PromptTokens;
                    completionTok = llmResp.CompletionTokens;
                    totalTok = llmResp.TotalTokens;
                    responseMs = llmResp.ResponseMs;

                    results = ParseStrategicGoalsJson(llmResp.Text);
                    if (results.Count > 0)
                    {
                        succeeded = true;
                        failureReason = null;
                    }
                    else
                    {
                        failureReason = "llm_parse_empty";
                    }
                }
                else
                {
                    failureReason = "llm_call_returned_null";
                }
            }
        }
        catch (Exception ex)
        {
            failureReason = $"exception: {ex.GetType().Name}: {ex.Message}";
            DebugLogger.Instance.LogError("LLM",
                $"GenerateStrategicGoalsAsync({name}) failed: {ex.Message}. " +
                $"Goal stack unchanged for this NPC.");
        }
        finally
        {
            try
            {
                // Telemetry summary: store the goal names in rendered_text so
                // the dashboard can show what the LLM actually picked, not
                // the raw JSON which is noisy.
                string summary = results.Count > 0
                    ? string.Join(" | ", results.Select(g => $"{g.Name} ({g.Type} {g.Priority:F2})"))
                    : rawText ?? "";
                SqlBackend?.RecordLLMUsage(
                    "strategic_goals", name, succeeded,
                    promptTok, completionTok, totalTok, responseMs,
                    summary, failureReason);
            }
            catch { /* recording is best-effort */ }
        }

        return results;
    }

    /// <summary>
    /// Parse the strategic-goals JSON response. Robust to leading prose
    /// (some models prefix "Here are the goals:" despite system prompt),
    /// trailing commas, varying property cases. Returns empty list on
    /// any parse failure rather than throwing.
    /// </summary>
    private static List<GoalCandidate> ParseStrategicGoalsJson(string raw)
    {
        var results = new List<GoalCandidate>();
        if (string.IsNullOrWhiteSpace(raw)) return results;

        // Find the JSON array boundaries -- model may have prefixed prose.
        int arrayStart = raw.IndexOf('[');
        int arrayEnd = raw.LastIndexOf(']');
        if (arrayStart < 0 || arrayEnd <= arrayStart) return results;

        string json = raw.Substring(arrayStart, arrayEnd - arrayStart + 1);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json,
                new System.Text.Json.JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                });
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return results;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                string gName = TryGetProperty(el, "name") ?? "";
                if (string.IsNullOrWhiteSpace(gName)) continue;
                if (gName.Length > 40) gName = gName.Substring(0, 40).TrimEnd();

                string gType = TryGetProperty(el, "type") ?? "Personal";
                float gPriority = 0.5f;
                if (el.TryGetProperty("priority", out var prEl)
                    && prEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                {
                    gPriority = (float)prEl.GetDouble();
                }
                gPriority = Math.Clamp(gPriority, 0.0f, 1.0f);

                string? gTarget = TryGetProperty(el, "target");
                if (string.IsNullOrWhiteSpace(gTarget)) gTarget = null;

                results.Add(new GoalCandidate
                {
                    Name = gName,
                    Type = gType,
                    Priority = gPriority,
                    TargetCharacter = gTarget,
                });

                if (results.Count >= 3) break; // hard cap
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogInfo("LLM",
                $"Strategic-goals JSON parse failed: {ex.Message}. Raw: {raw.Substring(0, Math.Min(120, raw.Length))}");
            return new List<GoalCandidate>(); // empty on parse failure
        }

        return results;
    }

    private static string? TryGetProperty(System.Text.Json.JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var el) && el.ValueKind == System.Text.Json.JsonValueKind.String)
            return el.GetString();
        // Case-insensitive fallback
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                return prop.Value.GetString();
        }
        return null;
    }

    private static string FormatTemplatedPersonality(string name, string charClass, string archetype, PersonalityProfile? p)
    {
        if (p == null)
            return $"{name} is a {charClass} -- {archetype} by trade, unremarkable on a first look.";

        string tone = p.Aggression > 0.7f ? "hard-eyed"
            : p.Sociability > 0.7f ? "open and easy"
            : p.Patience < 0.3f ? "restless"
            : "watchful";
        string drive = p.Greed > 0.7f ? "the coin in your purse"
            : p.Ambition > 0.7f ? "what you can do for them"
            : p.Loyalty > 0.7f ? "where you stand with their kin"
            : "the room and who else is in it";
        return $"{name} the {charClass} is {tone}. Their attention is on {drive}.";
    }

    private static string FormatPersonalityTraits(PersonalityProfile p)
    {
        var traits = new System.Collections.Generic.List<string>();
        if (p.Aggression > 0.7f) traits.Add("aggressive");
        else if (p.Aggression < 0.3f) traits.Add("peaceable");
        if (p.Greed > 0.7f) traits.Add("greedy");
        if (p.Courage > 0.7f) traits.Add("bold");
        else if (p.Courage < 0.3f) traits.Add("cautious");
        if (p.Loyalty > 0.7f) traits.Add("loyal");
        if (p.Vengefulness > 0.7f) traits.Add("vengeful");
        if (p.Sociability > 0.7f) traits.Add("gregarious");
        else if (p.Sociability < 0.3f) traits.Add("reclusive");
        if (p.Ambition > 0.7f) traits.Add("ambitious");
        if (p.Patience < 0.3f) traits.Add("impatient");
        if (traits.Count == 0) return "balanced disposition";
        return string.Join(", ", traits);
    }

    /// <summary>
    /// Strip common LLM output quirks. Removes wrapping quotes, "Here's the
    /// news flash:" preambles, and trailing whitespace. Defensive -- we trust
    /// the system prompt but harden against models that ignore it.
    /// </summary>
    private static string SanitizeLLMOutput(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        string t = raw.Trim();

        // Strip "Here is..." / "Here's..." preambles followed by a colon.
        int colonIdx = t.IndexOf(':');
        if (colonIdx > 0 && colonIdx < 60)
        {
            string before = t.Substring(0, colonIdx).ToLowerInvariant();
            if (before.StartsWith("here") || before.StartsWith("news flash") || before.StartsWith("response"))
            {
                t = t.Substring(colonIdx + 1).TrimStart();
            }
        }

        // Strip wrapping quotes (single or double).
        if (t.Length >= 2)
        {
            char first = t[0];
            char last = t[t.Length - 1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                t = t.Substring(1, t.Length - 2).Trim();
            }
        }

        // Normalize Unicode punctuation the LLM still slips in despite the
        // system prompt rule: em-dash (U+2014) and en-dash (U+2013) collapse
        // to two ASCII hyphens; horizontal ellipsis (U+2026) collapses to
        // three ASCII dots; curly quotes (U+2018 / U+2019 / U+201C / U+201D)
        // collapse to straight. Keeps player-facing news / epitaphs /
        // impressions ASCII-clean per project convention.
        t = t.Replace("\u2014", "--")
             .Replace("\u2013", "--")
             .Replace("\u2026", "...")
             .Replace('\u2018', '\'').Replace('\u2019', '\'')
             .Replace('\u201c', '"').Replace('\u201d', '"');

        // Cap length at ~300 chars to keep news feed clean.
        if (t.Length > 300) t = t.Substring(0, 297).TrimEnd() + "...";

        return t;
    }
}
