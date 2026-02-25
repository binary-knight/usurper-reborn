using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Team Balance System - Prevents high-level NPCs from trivializing dungeons
    ///
    /// Two balancing mechanisms:
    /// 1. Per-dungeon entry fee based on level gap (paid to NPC teammates)
    /// 2. XP penalty when teammates are significantly higher level
    ///
    /// Relationship bonuses reduce fees (spouse/lover discounts)
    /// </summary>
    public class TeamBalanceSystem
    {
        private static TeamBalanceSystem? _instance;
        public static TeamBalanceSystem Instance => _instance ??= new TeamBalanceSystem();

        // Configuration constants
        public const int LevelGapThreshold = 5;           // No penalty within this range
        public const int MaxLevelGap = 20;                // Cap for calculations
        public const int BaseFeePerLevel = 100;           // Gold per level above threshold

        // XP penalty tiers (cumulative with level gap)
        // +5 levels = 75% XP, +10 = 50%, +15 = 25%, +20+ = 10%
        private static readonly (int gap, float multiplier)[] XPPenaltyTiers = new[]
        {
            (5, 1.0f),    // 0-5 levels higher: 100% XP
            (10, 0.75f),  // 6-10 levels higher: 75% XP
            (15, 0.50f),  // 11-15 levels higher: 50% XP
            (20, 0.25f),  // 16-20 levels higher: 25% XP
            (999, 0.10f)  // 21+ levels higher: 10% XP
        };

        // Relationship discount rates
        public const float SpouseDiscount = 0.50f;        // 50% off for spouse
        public const float LoverDiscount = 0.25f;         // 25% off for lover
        public const float HighRelationDiscount = 0.15f;  // 15% off for high relationship (75+)
        public const float LowRelationSurcharge = 0.25f;  // 25% surcharge for low relationship (<25)

        public TeamBalanceSystem()
        {
            _instance = this;
        }

        /// <summary>
        /// Calculate the dungeon entry fee for a single NPC teammate
        /// </summary>
        public long CalculateEntryFee(Character player, NPC teammate)
        {
            int levelGap = teammate.Level - player.Level;

            // No fee if NPC is at or below player's level + threshold
            if (levelGap <= LevelGapThreshold)
                return 0;

            // Calculate base fee: (gap - threshold) * baseFeePerLevel * teammate level factor
            int effectiveGap = Math.Min(levelGap - LevelGapThreshold, MaxLevelGap);
            long baseFee = effectiveGap * BaseFeePerLevel * (1 + teammate.Level / 10);

            // Apply relationship modifiers
            float discount = GetRelationshipDiscount(player, teammate);
            long finalFee = (long)(baseFee * (1.0f - discount));

            return Math.Max(0, finalFee);
        }

        /// <summary>
        /// Calculate total dungeon entry fees for all NPC teammates
        /// </summary>
        public long CalculateTotalEntryFees(Character player, List<Character> teammates)
        {
            long totalFee = 0;

            foreach (var teammate in teammates)
            {
                if (teammate is NPC npc)
                {
                    totalFee += CalculateEntryFee(player, npc);
                }
            }

            return totalFee;
        }

        /// <summary>
        /// Get detailed breakdown of fees per teammate
        /// </summary>
        public List<(NPC npc, long fee, string reason)> GetFeeBreakdown(Character player, List<Character> teammates)
        {
            var breakdown = new List<(NPC npc, long fee, string reason)>();

            foreach (var teammate in teammates)
            {
                if (teammate is NPC npc)
                {
                    long fee = CalculateEntryFee(player, npc);
                    int levelGap = npc.Level - player.Level;

                    string reason;
                    if (levelGap <= LevelGapThreshold)
                    {
                        reason = "Within level range - no fee";
                    }
                    else
                    {
                        float discount = GetRelationshipDiscount(player, npc);
                        if (discount >= SpouseDiscount)
                            reason = $"+{levelGap} levels (spouse discount)";
                        else if (discount >= LoverDiscount)
                            reason = $"+{levelGap} levels (lover discount)";
                        else if (discount > 0)
                            reason = $"+{levelGap} levels (relationship bonus)";
                        else if (discount < 0)
                            reason = $"+{levelGap} levels (low relationship surcharge)";
                        else
                            reason = $"+{levelGap} levels above you";
                    }

                    breakdown.Add((npc, fee, reason));
                }
            }

            return breakdown;
        }

        /// <summary>
        /// Get relationship-based discount for an NPC
        /// Returns a value between -0.25 (surcharge) and 0.50 (discount)
        /// </summary>
        public float GetRelationshipDiscount(Character player, NPC npc)
        {
            float discount = 0f;

            // Check if spouse
            var romanceTracker = RomanceTracker.Instance;
            if (romanceTracker.Spouses.Any(s => s.NPCId == npc.ID))
            {
                return SpouseDiscount; // 50% discount for spouse
            }

            // Check if lover
            if (romanceTracker.CurrentLovers.Any(l => l.NPCId == npc.ID))
            {
                discount = LoverDiscount; // 25% discount for lover
            }

            // Check relationship level via RelationshipSystem
            var relationship = RelationshipSystem.GetRelationshipStatus(player, npc);
            if (relationship >= 75)
            {
                discount = Math.Max(discount, HighRelationDiscount);
            }
            else if (relationship < 25 && discount == 0)
            {
                // Low relationship surcharge (only if no other bonus)
                discount = -LowRelationSurcharge;
            }

            return discount;
        }

        /// <summary>
        /// Calculate XP multiplier based on highest-level teammate
        /// Player XP is reduced when carried by high-level NPCs
        /// </summary>
        public float CalculateXPMultiplier(Character player, List<Character> teammates)
        {
            if (teammates == null || teammates.Count == 0)
                return 1.0f;

            // Find the highest level NPC teammate (excluding companions which have separate system)
            int highestTeammateLevel = 0;
            foreach (var teammate in teammates)
            {
                // Check all character types - NPCs added to party are NPC instances
                // Skip grouped players (they have their own independent XP calculation)
                if (!teammate.IsCompanion && !teammate.IsGroupedPlayer && teammate.IsAlive)
                {
                    highestTeammateLevel = Math.Max(highestTeammateLevel, teammate.Level);
                }
            }

            if (highestTeammateLevel <= player.Level)
                return 1.0f; // No penalty if player is highest level

            int levelGap = highestTeammateLevel - player.Level;

            // Find appropriate penalty tier
            foreach (var (gap, multiplier) in XPPenaltyTiers)
            {
                if (levelGap <= gap)
                    return multiplier;
            }

            return 0.10f; // Minimum 10% XP
        }

        /// <summary>
        /// Get a description of the current XP penalty for display
        /// </summary>
        public string GetXPPenaltyDescription(Character player, List<Character> teammates)
        {
            float multiplier = CalculateXPMultiplier(player, teammates);

            if (multiplier >= 1.0f)
                return "Full XP (no penalty)";
            else if (multiplier >= 0.75f)
                return $"{(int)(multiplier * 100)}% XP (minor level gap penalty)";
            else if (multiplier >= 0.50f)
                return $"{(int)(multiplier * 100)}% XP (moderate level gap penalty)";
            else if (multiplier >= 0.25f)
                return $"{(int)(multiplier * 100)}% XP (significant level gap penalty)";
            else
                return $"{(int)(multiplier * 100)}% XP (severe level gap penalty)";
        }

        /// <summary>
        /// Check if player can afford the dungeon entry fees
        /// </summary>
        public bool CanAffordEntry(Character player, List<Character> teammates)
        {
            long totalFee = CalculateTotalEntryFees(player, teammates);
            return player.Gold >= totalFee;
        }

        /// <summary>
        /// Pay dungeon entry fees - deducts gold from player
        /// Returns true if payment successful
        /// </summary>
        public bool PayEntryFees(Character player, List<Character> teammates)
        {
            long totalFee = CalculateTotalEntryFees(player, teammates);

            if (player.Gold < totalFee)
                return false;

            player.Gold -= totalFee;
            // GD.Print($"[TeamBalance] Player paid {totalFee} gold in dungeon entry fees");
            return true;
        }

        /// <summary>
        /// Display fee information to terminal
        /// </summary>
        public async System.Threading.Tasks.Task DisplayFeeInfo(
            TerminalEmulator terminal,
            Character player,
            List<Character> teammates)
        {
            var breakdown = GetFeeBreakdown(player, teammates);
            long totalFee = breakdown.Sum(b => b.fee);
            float xpMult = CalculateXPMultiplier(player, teammates);

            if (totalFee == 0 && xpMult >= 1.0f)
            {
                // No fees or penalties - no need to show anything
                return;
            }

            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("═══ PARTY BALANCE ═══");

            // Show fee breakdown
            if (breakdown.Any(b => b.fee > 0))
            {
                terminal.SetColor("white");
                terminal.WriteLine("Dungeon entry fees (higher-level allies demand payment):");

                foreach (var (npc, fee, reason) in breakdown)
                {
                    if (fee > 0)
                    {
                        terminal.SetColor("gray");
                        terminal.Write($"  {npc.Name} (Lv {npc.Level}): ");
                        terminal.SetColor("yellow");
                        terminal.Write($"{fee:N0} gold ");
                        terminal.SetColor("darkgray");
                        terminal.WriteLine($"({reason})");
                    }
                }

                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Total fee: {totalFee:N0} gold");
                terminal.SetColor("gray");
                terminal.WriteLine($"  Your gold: {player.Gold:N0}");
            }

            // Show XP penalty
            if (xpMult < 1.0f)
            {
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine($"XP Penalty: {GetXPPenaltyDescription(player, teammates)}");
                terminal.SetColor("gray");
                terminal.WriteLine("  (High-level allies reduce your learning from combat)");
            }

            terminal.WriteLine("");
        }
    }
}
