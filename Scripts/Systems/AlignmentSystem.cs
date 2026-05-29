using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Alignment System - Makes Chivalry and Darkness meaningful throughout the game
    /// Affects: Shop prices, NPC reactions, location access, quest availability, combat bonuses
    /// Based on Usurper's original alignment mechanics from Pascal code
    /// </summary>
    public class AlignmentSystem
    {
        private static AlignmentSystem? _fallbackInstance;
        public static AlignmentSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Alignment;
                return _fallbackInstance ??= new AlignmentSystem();
            }
        }

        private Random _random = Random.Shared;

        /// <summary>
        /// Alignment categories based on Chivalry vs Darkness
        /// </summary>
        public enum AlignmentType
        {
            Holy,           // Very high chivalry, no darkness
            Good,           // High chivalry
            Neutral,        // Balanced or low both
            Dark,           // High darkness
            Evil,           // Very high darkness, no chivalry
            Balanced        // v0.57.0: both scales > 100 and within 100 of each other — walks between the light and the dark
        }

        /// <summary>
        /// Get a character's alignment type
        /// </summary>
        public AlignmentType GetAlignment(Character character)
        {
            // v0.57.0 — null guard. IsBalanced and callers from tax/knighting/dialogue code can
            // theoretically pass a null character during edge cases (dead NPC references, etc.).
            if (character == null) return AlignmentType.Neutral;
            long chivalry = character.Chivalry;
            long darkness = character.Darkness;
            long diff = chivalry - darkness;

            if (chivalry >= 800 && darkness < 100) return AlignmentType.Holy;
            if (darkness >= 800 && chivalry < 100) return AlignmentType.Evil;
            if (diff >= 400) return AlignmentType.Good;
            if (diff <= -400) return AlignmentType.Dark;
            // v0.57.0 Balanced: both scales materially accumulated AND within 100 of each other.
            // This is the reward for walking both paths — access to dialogue/merchants on both sides.
            if (chivalry > 100 && darkness > 100 && Math.Abs(diff) < 100) return AlignmentType.Balanced;
            return AlignmentType.Neutral;
        }

        /// <summary>
        /// v0.57.0: true when the player has meaningfully accumulated both Chivalry and Darkness
        /// and is walking the line between them — grants access to both good-aligned and evil-aligned
        /// NPC dialogue/quests. Dialogue/faction code that previously checked only good-vs-evil should
        /// also check `IsBalanced` to open both branches.
        /// </summary>
        public bool IsBalanced(Character character)
        {
            return GetAlignment(character) == AlignmentType.Balanced;
        }

        /// <summary>
        /// Get alignment display string with color
        /// </summary>
        public (string text, string color) GetAlignmentDisplay(Character character)
        {
            var alignment = GetAlignment(character);
            return alignment switch
            {
                AlignmentType.Holy => (Loc.Get("alignment.holy"), "bright_yellow"),
                AlignmentType.Good => (Loc.Get("alignment.good"), "bright_green"),
                AlignmentType.Neutral => (Loc.Get("alignment.neutral"), "gray"),
                AlignmentType.Dark => (Loc.Get("alignment.dark"), "red"),
                AlignmentType.Evil => (Loc.Get("alignment.evil"), "bright_red"),
                AlignmentType.Balanced => (Loc.Get("alignment.balanced"), "bright_magenta"),
                _ => (Loc.Get("alignment.unknown"), "white")
            };
        }

        /// <summary>
        /// Get price modifier based on alignment and shop type
        /// Holy/Good get better prices at legitimate shops, Dark/Evil at shady ones
        /// </summary>
        public float GetPriceModifier(Character character, bool isShadyShop)
        {
            var alignment = GetAlignment(character);

            if (isShadyShop)
            {
                // Shady shops favor dark characters (Balanced gets neutral — they walk in without trouble)
                float baseMod = alignment switch
                {
                    AlignmentType.Holy => 1.5f,      // 50% markup for holy characters
                    AlignmentType.Good => 1.25f,    // 25% markup for good characters
                    AlignmentType.Neutral => 1.0f,  // Normal price
                    AlignmentType.Balanced => 0.95f, // v0.57.0 — slight discount; they know the owners
                    AlignmentType.Dark => 0.9f,     // 10% discount
                    AlignmentType.Evil => 0.75f,    // 25% discount for evil
                    _ => 1.0f
                };
                // v0.62.x Dread ladder: a feared Dark/Evil player intimidates shady merchants into
                // deeper discounts as their standing climbs (Cutthroat 0.95x .. Nightmare 0.80x).
                // Only on the Dark/Evil band so a Balanced line-walker keeps the flat 0.95 rate.
                if (alignment == AlignmentType.Dark || alignment == AlignmentType.Evil)
                    baseMod *= GetDreadPriceMultiplier(character);
                return baseMod;
            }
            else
            {
                // Legitimate shops favor good characters (Balanced accepted — not a pariah like evil)
                float baseMod = alignment switch
                {
                    AlignmentType.Holy => 0.8f,     // 20% discount for holy
                    AlignmentType.Good => 0.9f,    // 10% discount for good
                    AlignmentType.Neutral => 1.0f, // Normal price
                    AlignmentType.Balanced => 0.95f, // v0.57.0 — slight discount; they're respected on both sides
                    AlignmentType.Dark => 1.15f,   // 15% markup
                    AlignmentType.Evil => 1.4f,    // 40% markup for evil
                    _ => 1.0f
                };
                // v0.62.x Renown ladder: a celebrated Good/Holy player earns deeper goodwill at honest
                // shops as their standing climbs (Defender 0.95x .. Legend 0.80x). Mirror of Dread.
                if (alignment == AlignmentType.Holy || alignment == AlignmentType.Good)
                    baseMod *= GetRenownPriceMultiplier(character);
                return baseMod;
            }
        }

        /// <summary>
        /// Check if character can access a location based on alignment
        /// </summary>
        public (bool canAccess, string reason) CanAccessLocation(Character character, GameLocation location)
        {
            var alignment = GetAlignment(character);

            switch (location)
            {
                case GameLocation.Church:
                case GameLocation.Temple:
                    if (alignment == AlignmentType.Evil)
                        return (false, Loc.Get("alignment.wards_repel"));
                    if (alignment == AlignmentType.Dark && character.Darkness > 600)
                        return (false, Loc.Get("alignment.priests_bar_door"));
                    break;

                // v0.62.x Phase 6: The Sanctum (Light activity hub) -- mirrors the Church/Temple
                // ward against Evil players. Dark Tier players are admitted but coldly received
                // (handled per-action inside SanctumLocation rather than blocking at the door,
                // since Dark may still want to give alms to wash darkness via paired movement).
                case GameLocation.Sanctum:
                    if (alignment == AlignmentType.Evil)
                        return (false, Loc.Get("alignment.wards_repel"));
                    break;

                case GameLocation.DarkAlley:
                case GameLocation.Darkness:
                    // Anyone can enter, but good characters get warned
                    if (alignment == AlignmentType.Holy)
                        return (true, Loc.Get("alignment.holy_aura_dimming"));
                    break;
            }

            return (true, null);
        }

        /// <summary>
        /// Get NPC reaction modifier based on alignment compatibility
        /// </summary>
        public float GetNPCReactionModifier(Character player, NPC npc)
        {
            var playerAlignment = GetAlignment(player);
            bool npcIsEvil = npc.Darkness > npc.Chivalry + 200;
            bool npcIsGood = npc.Chivalry > npc.Darkness + 200;

            // Similar alignments get along better
            if ((playerAlignment == AlignmentType.Holy || playerAlignment == AlignmentType.Good) && npcIsGood)
                return ApplyVeloraReactionBonus(player, 1.5f); // 50% better reactions

            if ((playerAlignment == AlignmentType.Evil || playerAlignment == AlignmentType.Dark) && npcIsEvil)
                return ApplyVeloraReactionBonus(player, 1.5f); // Evil characters respect each other

            // v0.57.0 Balanced — good NPCs and evil NPCs both treat the player better than strangers,
            // because the player has meaningfully acted on both sides. Pragmatism earns respect.
            if (playerAlignment == AlignmentType.Balanced && (npcIsGood || npcIsEvil))
                return ApplyVeloraReactionBonus(player, 1.3f);

            // Opposite alignments clash
            if ((playerAlignment == AlignmentType.Holy || playerAlignment == AlignmentType.Good) && npcIsEvil)
                return ApplyVeloraReactionBonus(player, 0.5f); // 50% worse reactions

            if ((playerAlignment == AlignmentType.Evil || playerAlignment == AlignmentType.Dark) && npcIsGood)
                return ApplyVeloraReactionBonus(player, 0.5f);

            return ApplyVeloraReactionBonus(player, 1.0f); // Neutral reactions
        }

        /// <summary>
        /// v0.61.0 Druid's Shrines + Beast Taming: applies social-modifier deltas from
        /// active long-term effects. Veloura attunement = +15% reactions (warmer);
        /// Cave Spider active pet = -10% reactions (creepy). All stack multiplicatively
        /// on top of alignment-compatibility math.
        /// v0.63.1: Dynasty standing also folds in here. Founder / Patriarch /
        /// Matriarch / Bloodline / Dynasty tiers grant +5 / +10 / +15 / +20%
        /// reactions, derived live from the player's adult-children set. Closes
        /// the "Dynasty tier shows on the status sheet but does nothing
        /// mechanical" gap the v0.63.0 release notes implicitly promised.
        /// </summary>
        private static float ApplyVeloraReactionBonus(Character player, float baseModifier)
        {
            float modifier = baseModifier;
            if (player != null)
            {
                if (player.IsAttunedTo("veloura"))
                    modifier *= (1f + GameConfig.ShrineVeloraReactionBonus);
                if (player.HasActivePet("cave_spider"))
                    modifier *= 0.90f; // -10% reactions (creepy companion)

                // v0.63.1: Dynasty standing reaction bonus.
                int dynastyBonus = UsurperRemake.Systems.FamilySystem.Instance
                    ?.GetDynastyReactionBonus(player) ?? 0;
                if (dynastyBonus > 0)
                    modifier *= (1f + dynastyBonus / 100f);
            }
            return modifier;
        }

        /// <summary>
        /// Get combat modifier based on alignment
        /// </summary>
        public (float attackMod, float defenseMod) GetCombatModifiers(Character character)
        {
            var alignment = GetAlignment(character);

            return alignment switch
            {
                // Holy: Bonus vs evil enemies, slight defense boost
                AlignmentType.Holy => (1.0f, 1.1f),
                // Good: Balanced slight bonuses
                AlignmentType.Good => (1.05f, 1.05f),
                // Neutral: No bonuses
                AlignmentType.Neutral => (1.0f, 1.0f),
                // v0.57.0 Balanced: both-sides player. Slight attack and defense bonus — their willingness to
                // act on either moral path gives them breadth, matching the Good tier's symmetric bonus.
                AlignmentType.Balanced => (1.05f, 1.05f),
                // Dark: Attack boost, slight defense penalty
                AlignmentType.Dark => (1.1f, 0.95f),
                // Evil: Strong attack, defense penalty
                AlignmentType.Evil => (1.2f, 0.9f),
                _ => (1.0f, 1.0f)
            };
        }

        // ──────────────────────────────────────────────────────────────────────
        // Alignment combat passives (the "named abilities" made real). Scale with how
        // committed the path is (Dark < Evil, Good < Holy). Dark = leech/fear/crit glass
        // cannon; Light = regen/smite/crit bruiser. Magnitudes are playtest starting points.
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>Shadow Strike (dark) / Righteous Fury (light): flat crit-chance bonus.</summary>
        public int GetAlignmentCritBonus(Character c) => GetAlignment(c) switch
        {
            AlignmentType.Dark => 3,
            AlignmentType.Evil => 5,
            AlignmentType.Good => 3,
            AlignmentType.Holy => 5,
            _ => 0
        };

        /// <summary>Soul Drain (Evil/Dark): heal this fraction of melee damage dealt.</summary>
        public float GetSoulDrainPercent(Character c) => GetAlignment(c) switch
        {
            AlignmentType.Dark => 0.03f,
            AlignmentType.Evil => 0.06f,
            _ => 0f
        };

        /// <summary>Terror Incarnate (Evil) / Fear Aura (Dark): % chance to Fear on hit.</summary>
        public int GetFearOnHitChance(Character c) => GetAlignment(c) switch
        {
            AlignmentType.Dark => 5,
            AlignmentType.Evil => 10,
            _ => 0
        };

        /// <summary>Blessed Aura (Holy/Good): regen this fraction of MaxHP per combat round.</summary>
        public float GetBlessedRegenPercent(Character c) => GetAlignment(c) switch
        {
            AlignmentType.Good => 0.02f,
            AlignmentType.Holy => 0.04f,
            _ => 0f
        };

        /// <summary>Holy Smite (Holy/Good): bonus melee damage vs undead/demonic foes.</summary>
        public float GetHolySmiteBonus(Character c) => GetAlignment(c) switch
        {
            AlignmentType.Good => 0.05f,
            AlignmentType.Holy => 0.10f,
            _ => 0f
        };

        // ──────────────────────────────────────────────────────────────────────
        // Dread / Renown notoriety ladders (v0.62.x "Light and Dark" Phase 2).
        // Two mirrored standing tracks derived live from Darkness / Chivalry (no new
        // save fields). They ESCALATE effects that already key off alignment (shop
        // prices here; more "the world reacts to your name" content in a later slice)
        // so committing deep into a pole feels different from dabbling. Only the pole
        // matching the player's alignment BAND is treated as active at call sites, so a
        // Balanced line-walker never collects both ladders' escalation.
        // ──────────────────────────────────────────────────────────────────────

        public enum DreadTier { None, Cutthroat, Marauder, Terror, Nightmare }
        public enum RenownTier { None, Defender, Paragon, Hero, Legend }

        /// <summary>Dread standing from raw Darkness. Pure; gate "active" on the Dark/Evil band at call sites.</summary>
        public DreadTier GetDreadTier(Character c)
        {
            if (c == null) return DreadTier.None;
            return c.Darkness switch
            {
                >= 800 => DreadTier.Nightmare,
                >= 650 => DreadTier.Terror,
                >= 450 => DreadTier.Marauder,
                >= 250 => DreadTier.Cutthroat,
                _ => DreadTier.None
            };
        }

        /// <summary>Renown standing from raw Chivalry. Pure; gate "active" on the Good/Holy band at call sites.</summary>
        public RenownTier GetRenownTier(Character c)
        {
            if (c == null) return RenownTier.None;
            return c.Chivalry switch
            {
                >= 800 => RenownTier.Legend,
                >= 650 => RenownTier.Hero,
                >= 450 => RenownTier.Paragon,
                >= 250 => RenownTier.Defender,
                _ => RenownTier.None
            };
        }

        /// <summary>Tiered shady-shop discount multiplier for a feared player. 1.0 = no effect.</summary>
        public float GetDreadPriceMultiplier(Character c) => GetDreadTier(c) switch
        {
            DreadTier.Nightmare => 0.80f,
            DreadTier.Terror => 0.85f,
            DreadTier.Marauder => 0.90f,
            DreadTier.Cutthroat => 0.95f,
            _ => 1.0f
        };

        /// <summary>Tiered honest-shop discount multiplier for a renowned player. 1.0 = no effect.</summary>
        public float GetRenownPriceMultiplier(Character c) => GetRenownTier(c) switch
        {
            RenownTier.Legend => 0.80f,
            RenownTier.Hero => 0.85f,
            RenownTier.Paragon => 0.90f,
            RenownTier.Defender => 0.95f,
            _ => 1.0f
        };

        /// <summary>
        /// Full "Notoriety/Renown: {tier} ({what it does})" line for the [%] status readout, or "" if the
        /// player has no active standing. Picks the Dread track for Dark/Evil and the Renown track for
        /// Good/Holy; Neutral/Balanced return "" (a line-walker has no single name the world fears or sings).
        /// </summary>
        public string GetNotorietyStandingLine(Character c)
        {
            var band = GetAlignment(c);
            if (band == AlignmentType.Dark || band == AlignmentType.Evil)
            {
                var t = GetDreadTier(c);
                if (t == DreadTier.None) return "";
                string name = Loc.Get($"dread.tier_{t.ToString().ToLowerInvariant()}");
                string effect = t >= DreadTier.Terror
                    ? Loc.Get("dread.standing_effect_high")
                    : Loc.Get("dread.standing_effect_low");
                return Loc.Get("reputation.dread_standing", name, effect);
            }
            if (band == AlignmentType.Holy || band == AlignmentType.Good)
            {
                var t = GetRenownTier(c);
                if (t == RenownTier.None) return "";
                string name = Loc.Get($"renown.tier_{t.ToString().ToLowerInvariant()}");
                string effect = t >= RenownTier.Paragon
                    ? Loc.Get("renown.standing_effect_high")
                    : Loc.Get("renown.standing_effect_low");
                return Loc.Get("reputation.renown_standing", name, effect);
            }
            return "";
        }

        /// <summary>
        /// Apply alignment change with news generation
        /// </summary>
        public void ModifyAlignment(Character character, int chivalryChange, int darknessChange, string reason)
        {
            // v0.57.12: clamp via GameConfig.AlignmentCap (was hardcoded 1000). Character setter also clamps — belt and suspenders.
            character.Chivalry = Math.Clamp(character.Chivalry + chivalryChange, 0L, GameConfig.AlignmentCap);
            character.Darkness = Math.Clamp(character.Darkness + darknessChange, 0L, GameConfig.AlignmentCap);

            // Generate news for significant alignment shifts
            if (Math.Abs(chivalryChange) >= 20 || Math.Abs(darknessChange) >= 20)
            {
                var newsSystem = NewsSystem.Instance;
                if (newsSystem != null)
                {
                    if (chivalryChange >= 20)
                        newsSystem.Newsy(true, $"{character.Name} performed a noble deed: {reason}");
                    else if (darknessChange >= 20)
                        newsSystem.Newsy(true, $"{character.Name} committed a dark act: {reason}");
                }
            }
        }

        /// <summary>
        /// v0.57.0 paired alignment movement: a good deed raises Chivalry AND lowers Darkness,
        /// an evil deed raises Darkness AND lowers Chivalry. The opposite side moves 50% of the
        /// primary amount (minimum 1 when amount > 0). Feedback from playtesters: without this,
        /// staying neutral was almost impossible because nothing reduced one scale when the other
        /// moved. Paired movement lets evil deeds naturally cleanse accumulated chivalry and
        /// vice versa, making "Balanced" a meaningful alignment target.
        ///
        /// v0.60.0 alignment audit: diminishing returns on the GAINING side. Player feedback:
        /// "Getting 1000 should be hard." Pre-fix: every grant landed at full value, so a player
        /// could chain Old God saves (+150 each), knighthood (+50), castle/temple/church donations
        /// (now capped at 25-50 each from the cheese pass), and a handful of quests to max out
        /// in a couple of sessions. The DR curve scales the gain by the current scale value:
        ///
        ///   below 500 = 100% (full grant; early-mid alignment progresses normally)
        ///   500..699  = 75%
        ///   700..849  = 50%
        ///   850..949  = 25%
        ///   950+      = 10%
        ///
        /// Only the GAIN side is scaled; the paired opposite-side reduction uses the original
        /// (un-scaled) amount/2 so an evil deed at high chivalry still cleanses meaningfully.
        /// All 98+ ChangeAlignment call sites benefit automatically.
        /// </summary>
        public void ChangeAlignment(Character character, int amount, bool isGood, string reason)
        {
            if (amount <= 0) return;

            long currentScale = isGood ? character.Chivalry : character.Darkness;
            int scaledAmount = ScaleAlignmentByDR(amount, currentScale);

            // Paired movement uses the un-scaled amount so the opposite scale is cleansed
            // at full strength. Otherwise a maxed-chivalry player committing a small evil
            // deed would barely lose any chivalry, defeating the cleansing purpose.
            int opposite = Math.Max(1, amount / 2);

            if (isGood)
                ModifyAlignment(character, scaledAmount, -opposite, reason);
            else
                ModifyAlignment(character, -opposite, scaledAmount, reason);
        }

        /// <summary>
        /// v0.60.0 alignment-audit DR helper. Scales an alignment GAIN by the current value
        /// of the relevant scale so the last 200 points cost roughly 10x what the first 500 do.
        /// Public for test access.
        /// </summary>
        public static int ScaleAlignmentByDR(int amount, long currentValue)
        {
            if (amount <= 0) return amount;
            int scaled = currentValue switch
            {
                < 500  => amount,
                < 700  => (amount * 75) / 100,
                < 850  => (amount * 50) / 100,
                < 950  => (amount * 25) / 100,
                _      => (amount * 10) / 100
            };
            // Don't let a small grant disappear entirely from rounding; minimum 1 so progress
            // is always possible even at 999 (10 attempts * 1 point each = inch toward cap).
            return Math.Max(1, scaled);
        }

        /// <summary>
        /// v0.57.12: Retroactive paired-movement heal for pre-v0.57.12 saves that exceeded GameConfig.AlignmentCap
        /// through bypass mutation sites (Church donations, DarkAlley evil deeds, quest rewards, etc. that
        /// used raw `+=`/`-=` without routing through ChangeAlignment/ModifyAlignment). When a scale overflows,
        /// reduce the OPPOSITE scale by excess/2 — this is what paired movement would have done at mutation time
        /// if the site had used the helper. Then clamp the overflowing scale to the cap.
        ///
        /// Called from GameEngine load paths before the Character setter fires, so we operate on raw save values.
        /// Returns the healed (chivalry, darkness) pair, both guaranteed within [0, AlignmentCap].
        /// </summary>
        public static (long chivalry, long darkness) HealOverflow(long rawChivalry, long rawDarkness)
        {
            long cap = GameConfig.AlignmentCap;
            long chiv = rawChivalry;
            long dark = rawDarkness;

            if (chiv > cap)
            {
                long excess = chiv - cap;
                dark = Math.Max(0, dark - excess / 2);
                chiv = cap;
            }
            if (dark > cap)
            {
                long excess = dark - cap;
                chiv = Math.Max(0, chiv - excess / 2);
                dark = cap;
            }
            // Floor at zero in case either raw input was negative (shouldn't happen, but defense in depth).
            chiv = Math.Max(0, chiv);
            dark = Math.Max(0, dark);
            return (chiv, dark);
        }

        /// <summary>Convenience accessor for object-initializer syntax at load sites.</summary>
        public static long HealOverflowChivalry(long rawChivalry, long rawDarkness)
            => HealOverflow(rawChivalry, rawDarkness).chivalry;

        /// <summary>Convenience accessor for object-initializer syntax at load sites.</summary>
        public static long HealOverflowDarkness(long rawChivalry, long rawDarkness)
            => HealOverflow(rawChivalry, rawDarkness).darkness;

        /// <summary>
        /// Get special abilities available based on alignment
        /// </summary>
        public List<string> GetAlignmentAbilities(Character character)
        {
            var abilities = new List<string>();
            var alignment = GetAlignment(character);

            switch (alignment)
            {
                case AlignmentType.Holy:
                    abilities.Add(Loc.Get("alignment.ability_divine_protection"));
                    abilities.Add(Loc.Get("alignment.ability_holy_smite"));
                    abilities.Add(Loc.Get("alignment.ability_blessed_aura"));
                    abilities.Add(Loc.Get("alignment.ability_temple_sanctuary"));
                    break;

                case AlignmentType.Good:
                    abilities.Add(Loc.Get("alignment.ability_righteous_fury"));
                    abilities.Add(Loc.Get("alignment.ability_merchant_trust"));
                    abilities.Add(Loc.Get("alignment.ability_guard_respect"));
                    break;

                case AlignmentType.Neutral:
                    abilities.Add(Loc.Get("alignment.ability_diplomatic_immunity"));
                    abilities.Add(Loc.Get("alignment.ability_balanced_path"));
                    break;

                case AlignmentType.Balanced:
                    // v0.57.0 — walking both paths opens up dialogue, merchants, and quests on both sides.
                    abilities.Add(Loc.Get("alignment.ability_walks_both_paths"));
                    abilities.Add(Loc.Get("alignment.ability_diplomatic_immunity"));
                    abilities.Add(Loc.Get("alignment.ability_merchant_trust"));
                    abilities.Add(Loc.Get("alignment.ability_criminal_respect"));
                    break;

                case AlignmentType.Dark:
                    abilities.Add(Loc.Get("alignment.ability_shadow_strike"));
                    abilities.Add(Loc.Get("alignment.ability_fear_aura"));
                    abilities.Add(Loc.Get("alignment.ability_black_market"));
                    break;

                case AlignmentType.Evil:
                    abilities.Add(Loc.Get("alignment.ability_soul_drain"));
                    abilities.Add(Loc.Get("alignment.ability_terror_incarnate"));
                    abilities.Add(Loc.Get("alignment.ability_dark_pact"));
                    abilities.Add(Loc.Get("alignment.ability_criminal_respect"));
                    break;
            }

            return abilities;
        }

        /// <summary>
        /// Check for random alignment-based events
        /// </summary>
        public async Task<bool> CheckAlignmentEvent(Character player, TerminalEmulator terminal)
        {
            var alignment = GetAlignment(player);

            // 5% chance of alignment event
            if (_random.Next(100) >= 5) return false;

            switch (alignment)
            {
                case AlignmentType.Holy:
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("alignment.event_warm_light"));
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("alignment.event_gods_smile"));
                    player.HP = Math.Min(player.MaxHP, player.HP + player.MaxHP / 10);
                    terminal.WriteLine(Loc.Get("alignment.event_hp_restored", player.MaxHP / 10));
                    // v0.62.x Phase 3 (Renown reward loop): at Hero+ Renown, an admirer presses
                    // a gift item into the player's hand on top of the HP restoration. Mirror of
                    // the Dread bounty-hunter loot-roll on the Light side.
                    TryGrantRenownGift(player, terminal);
                    await Task.Delay(2000);
                    return true;

                case AlignmentType.Good:
                    if (_random.Next(2) == 0)
                    {
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("alignment.event_merchant_approaches"));
                        terminal.SetColor("white");
                        int gold = _random.Next(20, 100);
                        terminal.WriteLine(Loc.Get("alignment.event_merchant_thanks", gold));
                        player.Gold += gold;
                        // v0.62.x Phase 3: at Hero+ Renown the merchant's "thanks" is materially
                        // bigger -- they include a gift item with the gold.
                        TryGrantRenownGift(player, terminal);
                        await Task.Delay(2000);
                        return true;
                    }
                    break;

                case AlignmentType.Dark:
                    if (_random.Next(2) == 0)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("alignment.event_shadow_whispers"));
                        terminal.SetColor("gray");

                        // Dynamic hints based on player level
                        int playerLevel = player.Level;
                        int[] sealFloors = { 15, 30, 45, 60, 80, 99 };
                        int[] godFloors = DungeonLocation.OldGodFloors;
                        string[] godNames = { "Maelketh", "Veloura", "Thorgrim", "Noctura", "Aurelion", "Terravok", "Manwe" };

                        var hints = new List<string>();

                        int nearestSeal = sealFloors.FirstOrDefault(f => f > playerLevel);
                        if (nearestSeal > 0 && nearestSeal - playerLevel <= 15)
                            hints.Add($"\"Something ancient is sealed near floor {nearestSeal}. Worth investigating...\"");

                        int godIdx = -1;
                        for (int i = 0; i < godFloors.Length; i++)
                        {
                            if (godFloors[i] > playerLevel) { godIdx = i; break; }
                        }
                        if (godIdx >= 0 && godFloors[godIdx] - playerLevel <= 20)
                            hints.Add($"\"I've heard whispers of {godNames[godIdx]} near floor {godFloors[godIdx]}. Dangerous... but profitable.\"");

                        if (playerLevel < 10)
                            hints.Add($"\"The dungeon rewards those who push deeper. Floor {playerLevel + 5} holds richer prey.\"");
                        else if (playerLevel < 40)
                            hints.Add("\"The shadows in the deep have secrets the surface folk never learn.\"");
                        else
                            hints.Add("\"Even I fear what stirs in the lowest depths. But you... you might survive.\"");

                        terminal.WriteLine(hints[_random.Next(hints.Count)]);
                        int xpGain = Math.Max(50, playerLevel * 10);
                        player.Experience += xpGain;
                        terminal.WriteLine(Loc.Get("alignment.event_forbidden_xp", xpGain));
                        await Task.Delay(2000);
                        return true;
                    }
                    break;

                case AlignmentType.Evil:
                    terminal.SetColor("bright_red");
                    terminal.WriteLine(Loc.Get("alignment.event_dark_energy"));
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("alignment.event_wickedness_empowers"));
                    player.Strength += 1;
                    terminal.WriteLine(Loc.Get("alignment.event_str_temp"));
                    await Task.Delay(2000);
                    return true;

                case AlignmentType.Balanced:
                    // v0.57.0 — Balanced players walk both paths, so their events are a coin flip
                    // between a good-aligned merchant encounter and a dark-aligned XP whisper.
                    // Without this case they'd never trigger events at all.
                    if (_random.Next(2) == 0)
                    {
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("alignment.event_merchant_approaches"));
                        terminal.SetColor("white");
                        int gold = _random.Next(20, 100);
                        terminal.WriteLine(Loc.Get("alignment.event_merchant_thanks", gold));
                        player.Gold += gold;
                        await Task.Delay(2000);
                        return true;
                    }
                    else
                    {
                        terminal.SetColor("bright_magenta");
                        terminal.WriteLine(Loc.Get("alignment.event_shadow_whispers"));
                        terminal.SetColor("gray");
                        int xpGain = Math.Max(50, player.Level * 10);
                        player.Experience += xpGain;
                        terminal.WriteLine(Loc.Get("alignment.event_forbidden_xp", xpGain));
                        await Task.Delay(2000);
                        return true;
                    }
            }

            return false;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Mercenary / Sellsword rank (v0.62.x "Light and Dark" Phase 4 reward-loop content).
        // Career counter ladder: separate from Dread/Renown alignment standing, NOT pole-gated
        // (the merc path is alignment-agnostic by design -- it's the freelance lane that runs
        // alongside the Light/Dark poles). Derived live from Character.MercContractsCompleted.
        // Tier-up thresholds live in GameConfig.MercRankContractsRequired and never decay.
        // ──────────────────────────────────────────────────────────────────────

        public enum MercRank { None, Recruit, Sellsword, Veteran, Ironbound, Legend }

        /// <summary>Pure function of Character.MercContractsCompleted; never decays. Used for
        /// tier-up gating, slot count, payout multiplier, and the [%] standing readout.</summary>
        public MercRank GetMercRank(Character c)
        {
            if (c == null) return MercRank.None;
            int completed = c.MercContractsCompleted;
            var thresholds = GameConfig.MercRankContractsRequired;
            // Walk from highest to lowest so the highest reached tier wins.
            for (int t = thresholds.Length - 1; t >= 1; t--)
            {
                if (completed >= thresholds[t]) return (MercRank)t;
            }
            return MercRank.None;
        }

        /// <summary>Localized "Sellsword Hall: {rank}" line for the [%] status readout when the
        /// player has at least one merc contract completed. Empty otherwise (unranked freelancers
        /// don't show a standing -- you have to do at least one job to have a name on the board).</summary>
        public string GetMercStandingLine(Character c)
        {
            if (c == null) return "";
            var rank = GetMercRank(c);
            if (rank == MercRank.None) return "";
            string rankName = Loc.Get($"merc.rank_{rank.ToString().ToLowerInvariant()}");
            return Loc.Get("reputation.merc_standing", rankName, c.MercContractsCompleted);
        }

        /// <summary>
        /// v0.62.x Phase 3 (Renown reward loop): at Hero+ Renown, an admirer or grateful
        /// citizen presses a class-appropriate loot item into the player's hand as part of
        /// the existing Good/Holy alignment-event flow. Pole-gated so a Balanced line-walker
        /// doesn't trigger it. Silent no-op below Hero (renown gold/HP still grants normally).
        /// </summary>
        private void TryGrantRenownGift(Character player, TerminalEmulator terminal)
        {
            var band = GetAlignment(player);
            if (band != AlignmentType.Good && band != AlignmentType.Holy) return;
            if (GetRenownTier(player) < RenownTier.Hero) return;

            try
            {
                var gift = LootGenerator.GenerateDungeonLoot(player.Level, player.Class);
                if (gift == null || player.Inventory == null) return;
                player.Inventory.Add(gift);
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("alignment.event_renown_gift", gift.Name));
            }
            catch { /* defensive: a loot-generator hiccup must not break the alignment event */ }
        }

        /// <summary>
        /// Display alignment status to player
        /// </summary>
        public void DisplayAlignmentStatus(Character character, TerminalEmulator terminal)
        {
            var (text, color) = GetAlignmentDisplay(character);
            var alignment = GetAlignment(character);

            terminal.SetColor("bright_cyan");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine("═══════════════════════════════════════");
            }
            terminal.WriteLine($"         {Loc.Get("alignment.status_header")}");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine("═══════════════════════════════════════");
            }
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.Write(Loc.Get("alignment.chivalry_label"));
            terminal.SetColor("bright_green");
            terminal.Write($"{character.Chivalry}");
            terminal.SetColor("gray");
            terminal.WriteLine("/1000");

            terminal.SetColor("yellow");
            terminal.Write(Loc.Get("alignment.darkness_label"));
            terminal.SetColor("red");
            terminal.Write($"{character.Darkness}");
            terminal.SetColor("gray");
            terminal.WriteLine("/1000");

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.Write($"{Loc.Get("ui.alignment")}: ");
            terminal.SetColor(color);
            terminal.WriteLine(text);

            // Show alignment bar
            terminal.WriteLine("");
            if (GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("alignment.sr_bar", character.Chivalry, character.Darkness));
            }
            else
            {
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("alignment.holy_label"));
                terminal.SetColor("bright_green");
                int chivBars = (int)Math.Min(10, character.Chivalry / 100);
                int darkBars = (int)Math.Min(10, character.Darkness / 100);
                terminal.Write(new string('█', chivBars));
                terminal.SetColor("gray");
                terminal.Write(new string('░', 10 - chivBars));
                terminal.Write(" | ");
                terminal.SetColor("red");
                terminal.Write(new string('█', darkBars));
                terminal.SetColor("gray");
                terminal.Write(new string('░', 10 - darkBars));
                terminal.WriteLine(Loc.Get("alignment.evil_label"));
            }

            // Show abilities
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("alignment.abilities_header"));
            terminal.SetColor("white");
            foreach (var ability in GetAlignmentAbilities(character))
            {
                terminal.WriteLine($"  - {ability}");
            }
        }

        /// <summary>
        /// Actions that affect alignment
        /// </summary>
        public static class Actions
        {
            // Good actions
            public const string HelpedBeggar = "helped_beggar";
            public const string DonatedToTemple = "donated_temple";
            public const string DefendedInnocent = "defended_innocent";
            public const string SparedEnemy = "spared_enemy";
            public const string CompletedHolyQuest = "holy_quest";

            // Evil actions
            public const string MurderedInnocent = "murdered_innocent";
            public const string StoleFromMerchant = "stole_merchant";
            public const string BetrayedAlly = "betrayed_ally";
            public const string UsedDarkMagic = "dark_magic";
            public const string ServedDemon = "served_demon";
        }

        /// <summary>
        /// Process an alignment action
        /// </summary>
        public void ProcessAction(Character character, string action)
        {
            switch (action)
            {
                // Good actions
                case Actions.HelpedBeggar:
                    ModifyAlignment(character, 10, 0, "gave alms to the poor");
                    break;
                case Actions.DonatedToTemple:
                    ModifyAlignment(character, 25, -5, "donated generously to the temple");
                    break;
                case Actions.DefendedInnocent:
                    ModifyAlignment(character, 30, 0, "defended an innocent");
                    break;
                case Actions.SparedEnemy:
                    ModifyAlignment(character, 15, -10, "showed mercy to a defeated foe");
                    break;
                case Actions.CompletedHolyQuest:
                    ModifyAlignment(character, 50, -20, "completed a holy quest");
                    break;

                // Evil actions
                case Actions.MurderedInnocent:
                    ModifyAlignment(character, -30, 40, "murdered an innocent");
                    break;
                case Actions.StoleFromMerchant:
                    ModifyAlignment(character, -15, 20, "stole from a merchant");
                    break;
                case Actions.BetrayedAlly:
                    ModifyAlignment(character, -25, 30, "betrayed an ally");
                    break;
                case Actions.UsedDarkMagic:
                    ModifyAlignment(character, -10, 25, "used forbidden dark magic");
                    break;
                case Actions.ServedDemon:
                    ModifyAlignment(character, -50, 50, "made a pact with darkness");
                    break;
            }
        }
    }
}
