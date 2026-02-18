using System;
using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Systems;

/// <summary>
/// Social Influence System — Inspired by Project Sid (Altera.AL, 2024).
/// Enables emergent social dynamics: opinion propagation, witness events,
/// faction recruitment, role adaptation, and player reputation spreading.
/// </summary>
public class SocialInfluenceSystem
{
    private static SocialInfluenceSystem? _instance;
    public static SocialInfluenceSystem? Instance => _instance;

    // Rate-limiting: "source|listener|subject" -> tick when last shared
    private readonly Dictionary<string, int> _opinionShareCooldowns = new();
    private const int OPINION_SHARE_COOLDOWN_TICKS = 20; // ~10 min between same gossip triple

    // Virality cap: subject name -> share count this sim-day
    private readonly Dictionary<string, int> _dailyOpinionShares = new();
    private const int MAX_DAILY_SHARES_PER_SUBJECT = 8;

    // Role adaptation runs less frequently
    private const int ROLE_ADAPTATION_INTERVAL_TICKS = 60; // ~30 min

    // Social locations where gossip and influence happen
    private static readonly string[] SocialLocations = new[]
    {
        "Inn", "Main Street", "Temple", "Love Street", "Auction House",
        "Church", "Castle", "Armor Shop", "Weapon Shop"
    };

    // Emergent role definitions: role name -> (personality requirements, activity boosts)
    private static readonly Dictionary<string, (Func<PersonalityProfile, float> Score, (string activity, double boost)[] Boosts)> RoleDefinitions = new()
    {
        ["Defender"] = (p => p.Aggression * 0.4f + p.Courage * 0.4f + p.Loyalty * 0.2f,
            new[] { ("dungeon", 1.5), ("train", 1.3) }),
        ["Merchant"] = (p => p.Greed * 0.4f + p.Sociability * 0.3f + p.Intelligence * 0.3f,
            new[] { ("shop", 1.5), ("marketplace", 1.4), ("bank", 1.3) }),
        ["Healer"] = (p => p.Mysticism * 0.4f + p.Patience * 0.3f + p.Trustworthiness * 0.3f,
            new[] { ("temple", 1.5), ("heal", 1.3) }),
        ["Explorer"] = (p => p.Courage * 0.4f + p.Intelligence * 0.3f + p.Ambition * 0.3f,
            new[] { ("dungeon", 1.4), ("move", 1.3) }),
        ["Guard"] = (p => p.Loyalty * 0.4f + p.Trustworthiness * 0.3f + p.Courage * 0.3f,
            new[] { ("castle", 1.4), ("train", 1.2) }),
        ["Coordinator"] = (p => p.Sociability * 0.4f + p.Intelligence * 0.3f + p.Patience * 0.3f,
            new[] { ("inn", 1.4), ("team_recruit", 1.5) }),
        ["Socialite"] = (p => p.Sociability * 0.5f + p.Impulsiveness * 0.3f + p.Courage * 0.2f,
            new[] { ("inn", 1.5), ("love_street", 1.3) }),
    };

    private readonly Random _random = new();

    public SocialInfluenceSystem()
    {
        _instance = this;
    }

    /// <summary>
    /// Reset daily rate-limiting counters. Called from WorldSimulator on sim-day boundary.
    /// </summary>
    public void ResetDailyCounters()
    {
        _dailyOpinionShares.Clear();
        // Prune old cooldowns (keep only recent ones)
        var cutoff = _opinionShareCooldowns.Values.DefaultIfEmpty(0).Max() - 100;
        var stale = _opinionShareCooldowns.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
        foreach (var key in stale)
            _opinionShareCooldowns.Remove(key);
    }

    // ========================================================================
    // SYSTEM 1: Opinion Propagation (Gossip With Teeth)
    // ========================================================================

    /// <summary>
    /// NPCs at social locations share opinions about third parties.
    /// These opinions actually modify the listener's characterImpressions.
    /// </summary>
    public void ProcessOpinionPropagation(List<NPC> npcs, int currentTick)
    {
        if (_random.NextDouble() > 0.03) return; // 3% chance per tick

        // Find a speaker at a social location with opinions to share
        var candidates = npcs.Where(n =>
            n.IsAlive && !n.IsDead &&
            n.Brain?.Memory != null &&
            n.Brain.Personality != null &&
            SocialLocations.Contains(n.CurrentLocation) &&
            n.Brain.Memory.CharacterImpressions.Count > 0).ToList();

        if (candidates.Count == 0) return;
        var speaker = candidates[_random.Next(candidates.Count)];

        // Find a listener at the same location (different NPC)
        var listeners = npcs.Where(n =>
            n != speaker && n.IsAlive && !n.IsDead &&
            n.Brain?.Memory != null &&
            n.Brain.Personality != null &&
            n.CurrentLocation == speaker.CurrentLocation).ToList();

        if (listeners.Count == 0) return;
        var listener = listeners[_random.Next(listeners.Count)];

        // Speaker picks their strongest opinion
        var speakerImpressions = speaker.Brain.Memory.CharacterImpressions;
        var bestSubject = speakerImpressions
            .Where(kv => kv.Key != listener.Name && kv.Key != (listener.Name2 ?? listener.Name))
            .OrderByDescending(kv => Math.Abs(kv.Value))
            .FirstOrDefault();

        if (bestSubject.Key == null || Math.Abs(bestSubject.Value) < 0.15f) return;

        string subject = bestSubject.Key;
        float speakerOpinion = bestSubject.Value;

        // Rate-limit: check cooldown for this specific gossip triple
        string cooldownKey = $"{speaker.Name}|{listener.Name}|{subject}";
        if (_opinionShareCooldowns.TryGetValue(cooldownKey, out int lastTick) &&
            (currentTick - lastTick) < OPINION_SHARE_COOLDOWN_TICKS)
            return;

        // Virality cap
        if (_dailyOpinionShares.TryGetValue(subject, out int shares) && shares >= MAX_DAILY_SHARES_PER_SUBJECT)
            return;

        // Calculate influence strength
        float influenceFactor = CalculateInfluenceFactor(speaker, listener, subject);
        float delta = speakerOpinion * influenceFactor;
        delta = Math.Clamp(delta, -0.3f, 0.3f);

        if (Math.Abs(delta) < 0.02f) return; // Too small to matter

        // Apply impression change to listener
        var listenerImpressions = listener.Brain.Memory.CharacterImpressions;
        float existing = listenerImpressions.GetValueOrDefault(subject, 0f);
        float newVal = Math.Clamp(existing + delta, -1f, 1f);
        listenerImpressions[subject] = newVal;

        // Record memories
        string speakerName = speaker.Name2 ?? speaker.Name;
        string listenerName = listener.Name2 ?? listener.Name;

        listener.Brain.Memory.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.HeardGossip,
            Description = delta > 0
                ? $"Heard good things about {subject} from {speakerName}"
                : $"Heard warnings about {subject} from {speakerName}",
            InvolvedCharacter = subject,
            Importance = Math.Abs(delta),
            EmotionalImpact = delta * 0.5f
        });

        speaker.Brain.Memory.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.SharedOpinion,
            Description = $"Shared thoughts about {subject} with {listenerName}",
            InvolvedCharacter = listenerName,
            Importance = 0.2f,
            EmotionalImpact = 0f
        });

        // Update rate-limiting
        _opinionShareCooldowns[cooldownKey] = currentTick;
        _dailyOpinionShares[subject] = shares + 1;

        // Generate news for dramatic gossip (strong opinions about well-known subjects)
        if (Math.Abs(speakerOpinion) > 0.5f && _random.NextDouble() < 0.15)
        {
            if (speakerOpinion < 0)
                NewsSystem.Instance?.Newsy($"{speakerName} was overheard warning {listenerName} about {subject}");
            else
                NewsSystem.Instance?.Newsy($"{speakerName} sang {subject}'s praises to {listenerName}");
        }

        UsurperRemake.Systems.DebugLogger.Instance?.LogDebug("SOCIAL",
            $"Opinion propagation: {speakerName} told {listenerName} about {subject} (delta={delta:F2}, new={newVal:F2})");
    }

    private float CalculateInfluenceFactor(NPC speaker, NPC listener, string subject)
    {
        var sp = speaker.Brain.Personality;
        var lp = listener.Brain.Personality;

        // Base factor
        float baseFactor = 0.15f;

        // Speaker sociability: talkative people are more persuasive
        float sociabilityMod = 0.5f + sp.Sociability; // 0.5 - 1.5

        // Trust: how much the listener trusts the speaker
        float trustValue = 0f;
        var rel = listener.Brain?.Relationships?.GetRelationship(speaker.Name);
        if (rel != null)
            trustValue = Math.Clamp(rel.GetTotalValue(), -1f, 1f);
        float trustMod = Math.Max(0.05f, 0.3f + trustValue * 0.7f); // 0.05 - 1.0

        // Resistance: strong existing opinions resist change
        float existingImpression = listener.Brain.Memory.GetCharacterImpression(subject);
        float resistanceMod = 1.0f - Math.Abs(existingImpression) * 0.5f; // 0.5 - 1.0

        // Exaggeration: impulsive speakers amplify opinions
        float exaggerationMod = 0.8f + sp.Impulsiveness * 0.4f; // 0.8 - 1.2

        // Skepticism: intelligent listeners are harder to sway
        float skepticismMod = 1.1f - lp.Intelligence * 0.3f; // 0.8 - 1.1

        return baseFactor * sociabilityMod * trustMod * resistanceMod * exaggerationMod * skepticismMod;
    }

    // ========================================================================
    // SYSTEM 2: Witness System
    // ========================================================================

    /// <summary>
    /// Record witnesses at a location who observe an event between two parties.
    /// Called from event sites (brawls, challenges, theft, murder, etc.)
    /// </summary>
    public static void RecordWitnesses(List<NPC> npcs, string location,
        string actorName, string targetName, WitnessEventType eventType)
    {
        if (npcs == null || string.IsNullOrEmpty(location)) return;

        var witnesses = npcs.Where(n =>
            n.IsAlive && !n.IsDead &&
            n.Brain?.Memory != null &&
            n.CurrentLocation == location &&
            (n.Name2 ?? n.Name) != actorName &&
            n.Name != actorName &&
            (n.Name2 ?? n.Name) != targetName &&
            n.Name != targetName).ToList();

        if (witnesses.Count == 0) return;

        foreach (var witness in witnesses)
        {
            float impressionDelta = GetWitnessImpressionDelta(eventType);

            // Personality modifiers
            if (witness.Brain?.Personality != null)
            {
                var p = witness.Brain.Personality;

                // Aggressive NPCs are less bothered by violence
                if ((eventType == WitnessEventType.SawAttack || eventType == WitnessEventType.SawMurder ||
                     eventType == WitnessEventType.SawBrawl) && p.Aggression > 0.6f)
                    impressionDelta *= 0.5f;

                // Trustworthy NPCs are more offended by theft
                if (eventType == WitnessEventType.SawTheft && p.Trustworthiness > 0.6f)
                    impressionDelta *= 1.5f;

                // Loyal NPCs defend friends — if target is a friend, negative impression of actor is stronger
                if (impressionDelta < 0 && p.Loyalty > 0.6f)
                {
                    float targetImpression = witness.Brain.Memory.GetCharacterImpression(targetName);
                    if (targetImpression > 0.3f)
                        impressionDelta *= 1.3f;
                }

                // Cautious NPCs are more affected by witnessing violence
                if ((eventType == WitnessEventType.SawAttack || eventType == WitnessEventType.SawMurder) &&
                    p.Caution > 0.6f)
                    impressionDelta *= 1.2f;
            }

            // Clamp
            impressionDelta = Math.Clamp(impressionDelta, -0.8f, 0.8f);

            if (Math.Abs(impressionDelta) < 0.01f) continue;

            // Update impression of the actor
            var impressions = witness.Brain.Memory.CharacterImpressions;
            float existing = impressions.GetValueOrDefault(actorName, 0f);
            impressions[actorName] = Math.Clamp(existing + impressionDelta, -1f, 1f);

            // Record witness memory
            string witnessName = witness.Name2 ?? witness.Name;
            witness.Brain.Memory.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.WitnessedEvent,
                Description = $"Saw {actorName} {GetWitnessVerb(eventType)} {targetName}",
                InvolvedCharacter = actorName,
                Importance = Math.Min(0.9f, Math.Abs(impressionDelta) + 0.2f),
                EmotionalImpact = impressionDelta * 0.5f,
                Location = location
            });

            // Emotional response to witnessing
            if (witness.EmotionalState != null)
            {
                if (impressionDelta < -0.2f)
                    witness.EmotionalState.AddEmotion(EmotionType.Fear, Math.Abs(impressionDelta) * 0.5f, 60);
                else if (impressionDelta > 0.2f)
                    witness.EmotionalState.AddEmotion(EmotionType.Gratitude, impressionDelta * 0.3f, 60);
            }
        }

        // Generate news for multiple witnesses
        if (witnesses.Count >= 2)
        {
            string verb = GetWitnessVerb(eventType);
            NewsSystem.Instance?.Newsy($"Several townsfolk witnessed {actorName} {verb} {targetName} at the {location}");
        }

        UsurperRemake.Systems.DebugLogger.Instance?.LogDebug("SOCIAL",
            $"Witness event: {eventType} by {actorName} against {targetName} at {location}, {witnesses.Count} witnesses");
    }

    private static float GetWitnessImpressionDelta(WitnessEventType eventType)
    {
        return eventType switch
        {
            WitnessEventType.SawAttack => -0.3f,
            WitnessEventType.SawTheft => -0.4f,
            WitnessEventType.SawGenerosity => 0.2f,
            WitnessEventType.SawChallenge => -0.05f, // Mostly neutral, slight negative
            WitnessEventType.SawMurder => -0.6f,
            WitnessEventType.SawDefense => 0.3f,
            WitnessEventType.SawHealing => 0.2f,
            WitnessEventType.SawBrawl => -0.15f,
            _ => 0f
        };
    }

    private static string GetWitnessVerb(WitnessEventType eventType)
    {
        return eventType switch
        {
            WitnessEventType.SawAttack => "attack",
            WitnessEventType.SawTheft => "steal from",
            WitnessEventType.SawGenerosity => "help",
            WitnessEventType.SawChallenge => "challenge",
            WitnessEventType.SawMurder => "murder",
            WitnessEventType.SawDefense => "defend",
            WitnessEventType.SawHealing => "heal",
            WitnessEventType.SawBrawl => "brawl with",
            _ => "interact with"
        };
    }

    // ========================================================================
    // SYSTEM 4: Faction Recruitment via Social Influence
    // ========================================================================

    /// <summary>
    /// Faction-affiliated NPCs recruit unaffiliated NPCs at social locations.
    /// </summary>
    public void ProcessFactionRecruitment(List<NPC> npcs, int currentTick)
    {
        if (_random.NextDouble() > 0.01) return; // 1% per tick

        // Find a faction-affiliated NPC at a social location
        var recruiters = npcs.Where(n =>
            n.IsAlive && !n.IsDead &&
            n.NPCFaction != null &&
            n.Brain?.Personality != null &&
            n.Brain.Personality.Sociability > 0.4f &&
            SocialLocations.Contains(n.CurrentLocation)).ToList();

        if (recruiters.Count == 0) return;
        var recruiter = recruiters[_random.Next(recruiters.Count)];

        // Find an unaffiliated NPC at the same location
        var targets = npcs.Where(n =>
            n != recruiter && n.IsAlive && !n.IsDead &&
            n.NPCFaction == null &&
            n.Brain?.Personality != null &&
            n.CurrentLocation == recruiter.CurrentLocation).ToList();

        if (targets.Count == 0) return;
        var target = targets[_random.Next(targets.Count)];

        // Calculate recruitment success based on personality match
        float successChance = CalculateFactionRecruitmentChance(recruiter, target);

        if (_random.NextDouble() > successChance) return;

        // Recruit!
        target.NPCFaction = recruiter.NPCFaction;
        string recruiterName = recruiter.Name2 ?? recruiter.Name;
        string targetName = target.Name2 ?? target.Name;
        string factionName = recruiter.NPCFaction switch
        {
            Faction.TheCrown => "The Crown",
            Faction.TheShadows => "The Shadows",
            Faction.TheFaith => "The Faith",
            _ => "unknown"
        };

        // Record memories
        target.Brain.Memory.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.SocialInteraction,
            Description = $"Was recruited into {factionName} by {recruiterName}",
            InvolvedCharacter = recruiterName,
            Importance = 0.6f,
            EmotionalImpact = 0.3f
        });

        recruiter.Brain.Memory.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.PersonalAchievement,
            Description = $"Recruited {targetName} into {factionName}",
            InvolvedCharacter = targetName,
            Importance = 0.4f,
            EmotionalImpact = 0.2f
        });

        NewsSystem.Instance?.Newsy($"{recruiterName} recruited {targetName} into {factionName}");

        UsurperRemake.Systems.DebugLogger.Instance?.LogInfo("SOCIAL",
            $"Faction recruitment: {recruiterName} recruited {targetName} into {factionName}");
    }

    private float CalculateFactionRecruitmentChance(NPC recruiter, NPC target)
    {
        var tp = target.Brain.Personality;
        float baseChance = 0.10f;

        // Personality match with faction
        float personalityMatch = recruiter.NPCFaction switch
        {
            Faction.TheCrown => tp.Loyalty * 0.4f + tp.Trustworthiness * 0.3f + (1f - tp.Aggression) * 0.3f,
            Faction.TheShadows => tp.Ambition * 0.3f + tp.Greed * 0.3f + tp.Courage * 0.2f + tp.Aggression * 0.2f,
            Faction.TheFaith => tp.Mysticism * 0.4f + tp.Patience * 0.3f + tp.Sociability * 0.3f,
            _ => 0.3f
        };

        // Relationship bonus
        float relBonus = 0f;
        var rel = target.Brain?.Relationships?.GetRelationship(recruiter.Name);
        if (rel != null)
        {
            float relVal = rel.GetTotalValue();
            relBonus = relVal > 0 ? relVal * 0.2f : relVal * 0.3f; // Friends help, enemies hurt more
        }

        // Recruiter's sociability helps
        float charismaBonus = recruiter.Brain.Personality.Sociability * 0.15f;

        return Math.Clamp(baseChance + personalityMatch * 0.3f + relBonus + charismaBonus, 0.02f, 0.40f);
    }

    // ========================================================================
    // SYSTEM 5: Emergent Role Adaptation
    // ========================================================================

    /// <summary>
    /// NPCs adapt roles based on community needs. Runs every ~30 min.
    /// </summary>
    public void ProcessRoleAdaptation(List<NPC> npcs, int currentTick)
    {
        if (currentTick % ROLE_ADAPTATION_INTERVAL_TICKS != 0) return;

        var aliveNpcs = npcs.Where(n => n.IsAlive && !n.IsDead && n.Brain?.Personality != null).ToList();
        if (aliveNpcs.Count < 10) return; // Need a minimum community size

        // Increment stability for NPCs with existing roles
        foreach (var npc in aliveNpcs.Where(n => !string.IsNullOrEmpty(n.EmergentRole)))
        {
            npc.RoleStabilityTicks += ROLE_ADAPTATION_INTERVAL_TICKS;

            // Announce stable roles
            if (npc.RoleStabilityTicks == ROLE_ADAPTATION_INTERVAL_TICKS * 2) // After ~1 hour
            {
                string npcName = npc.Name2 ?? npc.Name;
                NewsSystem.Instance?.Newsy($"{npcName} has become known as the town's {npc.EmergentRole}");
            }
        }

        // Survey current role distribution
        var roleCounts = new Dictionary<string, int>();
        foreach (var role in RoleDefinitions.Keys)
            roleCounts[role] = 0;

        foreach (var npc in aliveNpcs)
        {
            if (!string.IsNullOrEmpty(npc.EmergentRole) && roleCounts.ContainsKey(npc.EmergentRole))
                roleCounts[npc.EmergentRole]++;
        }

        // Find underrepresented roles (< 10% of alive NPCs)
        int threshold = Math.Max(1, aliveNpcs.Count / 10);
        var neededRoles = roleCounts.Where(kv => kv.Value < threshold).Select(kv => kv.Key).ToList();

        if (neededRoles.Count == 0) return;

        // Find candidates: NPCs without a role or with unstable roles
        var candidates = aliveNpcs.Where(n =>
            string.IsNullOrEmpty(n.EmergentRole) || n.RoleStabilityTicks < ROLE_ADAPTATION_INTERVAL_TICKS * 2)
            .Where(n => n.Brain.Personality.Sociability > 0.3f || n.Brain.Personality.Ambition > 0.3f)
            .ToList();

        // Assign roles to up to 3 candidates per cycle
        int assigned = 0;
        foreach (var candidate in candidates.OrderBy(_ => _random.Next()))
        {
            if (assigned >= 3) break;

            // Find the best fitting needed role for this NPC's personality
            string bestRole = null;
            float bestScore = 0f;
            foreach (var role in neededRoles)
            {
                float score = RoleDefinitions[role].Score(candidate.Brain.Personality);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRole = role;
                }
            }

            if (bestRole != null && bestScore > 0.3f)
            {
                string oldRole = candidate.EmergentRole;
                candidate.EmergentRole = bestRole;
                candidate.RoleStabilityTicks = 0;
                assigned++;

                // Log role changes
                if (!string.IsNullOrEmpty(oldRole) && oldRole != bestRole)
                {
                    string npcName = candidate.Name2 ?? candidate.Name;
                    NewsSystem.Instance?.Newsy($"{npcName} has taken up a new calling as {bestRole}");
                }

                UsurperRemake.Systems.DebugLogger.Instance?.LogDebug("SOCIAL",
                    $"Role adaptation: {candidate.Name2 ?? candidate.Name} -> {bestRole} (score={bestScore:F2})");
            }
        }
    }

    /// <summary>
    /// Apply emergent role weight boosts to NPC activity selection.
    /// Called from WorldSimulator.ProcessNPCActivities().
    /// </summary>
    public static void ApplyRoleWeights(List<(string action, double weight)> activities, NPC npc)
    {
        if (string.IsNullOrEmpty(npc.EmergentRole)) return;

        if (!RoleDefinitions.TryGetValue(npc.EmergentRole, out var roleDef)) return;

        for (int i = 0; i < activities.Count; i++)
        {
            foreach (var (activity, boost) in roleDef.Boosts)
            {
                if (activities[i].action == activity)
                {
                    activities[i] = (activities[i].action, activities[i].weight * boost);
                }
            }
        }
    }

    // ========================================================================
    // SYSTEM 6: Player Reputation Propagation
    // ========================================================================

    /// <summary>
    /// Player's reputation spreads through NPC networks faster than NPC gossip.
    /// </summary>
    public void ProcessPlayerReputationSpread(List<NPC> npcs, string playerName, int currentTick)
    {
        if (string.IsNullOrEmpty(playerName)) return;

        // Blood Price: murder news travels twice as fast
        var player = GameEngine.Instance?.CurrentPlayer;
        float spreadChance = (player != null && player.MurderWeight > 0) ? 0.10f : 0.05f;
        if (_random.NextDouble() > spreadChance) return; // 5% normally, 10% for killers

        // Find an NPC who has a strong impression of the player
        var spreaders = npcs.Where(n =>
            n.IsAlive && !n.IsDead &&
            n.Brain?.Memory != null &&
            n.Brain.Personality != null &&
            Math.Abs(n.Brain.Memory.GetCharacterImpression(playerName)) > 0.3f &&
            SocialLocations.Contains(n.CurrentLocation)).ToList();

        if (spreaders.Count == 0) return;
        var spreader = spreaders[_random.Next(spreaders.Count)];

        float spreaderOpinion = spreader.Brain.Memory.GetCharacterImpression(playerName);

        // Find a listener at the same location who doesn't know the player well
        var listeners = npcs.Where(n =>
            n != spreader && n.IsAlive && !n.IsDead &&
            n.Brain?.Memory != null &&
            n.CurrentLocation == spreader.CurrentLocation &&
            Math.Abs(n.Brain.Memory.GetCharacterImpression(playerName)) < 0.4f).ToList();

        if (listeners.Count == 0) return;
        var listener = listeners[_random.Next(listeners.Count)];

        // Rate-limit per player-subject pair
        string cooldownKey = $"{spreader.Name}|{listener.Name}|{playerName}";
        if (_opinionShareCooldowns.TryGetValue(cooldownKey, out int lastTick) &&
            (currentTick - lastTick) < OPINION_SHARE_COOLDOWN_TICKS / 2) // Half cooldown for player news
            return;

        // Calculate influence with 1.5x multiplier for player news (more interesting)
        float influenceFactor = CalculateInfluenceFactor(spreader, listener, playerName) * 1.5f;
        float delta = spreaderOpinion * influenceFactor;
        delta = Math.Clamp(delta, -0.35f, 0.35f); // Slightly higher cap for player gossip

        if (Math.Abs(delta) < 0.02f) return;

        // Apply
        var listenerImpressions = listener.Brain.Memory.CharacterImpressions;
        float existing = listenerImpressions.GetValueOrDefault(playerName, 0f);
        listenerImpressions[playerName] = Math.Clamp(existing + delta, -1f, 1f);

        string spreaderName = spreader.Name2 ?? spreader.Name;
        string listenerName = listener.Name2 ?? listener.Name;

        listener.Brain.Memory.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.HeardGossip,
            Description = delta > 0
                ? $"Heard tales of {playerName}'s heroism from {spreaderName}"
                : $"Heard dark whispers about {playerName} from {spreaderName}",
            InvolvedCharacter = playerName,
            Importance = Math.Abs(delta) + 0.1f,
            EmotionalImpact = delta * 0.3f
        });

        _opinionShareCooldowns[cooldownKey] = currentTick;

        // Faction amplification: if spreader is in a faction, spread at 50% to other faction members
        if (spreader.NPCFaction != null)
        {
            var factionMembers = npcs.Where(n =>
                n != spreader && n != listener &&
                n.IsAlive && !n.IsDead &&
                n.NPCFaction == spreader.NPCFaction &&
                n.Brain?.Memory != null &&
                Math.Abs(n.Brain.Memory.GetCharacterImpression(playerName)) < 0.3f).ToList();

            float factionDelta = delta * 0.5f;
            foreach (var member in factionMembers.Take(3)) // Max 3 faction members per spread
            {
                var memberImpressions = member.Brain.Memory.CharacterImpressions;
                float memberExisting = memberImpressions.GetValueOrDefault(playerName, 0f);
                memberImpressions[playerName] = Math.Clamp(memberExisting + factionDelta, -1f, 1f);
            }
        }

        // Generate news for significant reputation spread
        int npcsThatKnow = npcs.Count(n =>
            n.Brain?.Memory != null &&
            Math.Abs(n.Brain.Memory.GetCharacterImpression(playerName)) > 0.2f);

        if (npcsThatKnow >= 20 && _random.NextDouble() < 0.05)
        {
            if (spreaderOpinion > 0)
                NewsSystem.Instance?.Newsy($"Tales of {playerName}'s heroism have spread across the realm");
            else
                NewsSystem.Instance?.Newsy($"Dark whispers about {playerName} circulate through the taverns");
        }

        UsurperRemake.Systems.DebugLogger.Instance?.LogDebug("SOCIAL",
            $"Player reputation spread: {spreaderName} told {listenerName} about {playerName} (delta={delta:F2})");
    }

    /// <summary>
    /// Get the player's overall reputation score across all NPCs.
    /// Returns (averageImpression, npcCount) for NPCs who have any impression.
    /// </summary>
    public static (float avgImpression, int knownBy) GetPlayerReputation(List<NPC> npcs, string playerName)
    {
        if (npcs == null || string.IsNullOrEmpty(playerName)) return (0f, 0);

        var impressions = npcs
            .Where(n => n.Brain?.Memory != null)
            .Select(n => n.Brain.Memory.GetCharacterImpression(playerName))
            .Where(imp => Math.Abs(imp) > 0.05f)
            .ToList();

        if (impressions.Count == 0) return (0f, 0);
        return (impressions.Average(), impressions.Count);
    }
}

// ========================================================================
// Supporting Types
// ========================================================================

public enum WitnessEventType
{
    SawAttack,
    SawTheft,
    SawGenerosity,
    SawChallenge,
    SawMurder,
    SawDefense,
    SawHealing,
    SawBrawl
}
