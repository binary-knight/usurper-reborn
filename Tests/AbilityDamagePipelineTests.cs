using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// Guards the fix for the PR #114 follow-up regression.
    ///
    /// The combat audit added 18 effects to CombatEngine's `isAoEAbility` list so the
    /// base damage block would stop double-applying damage for handlers that apply
    /// their own. That was a real bug and the change was correct for the seven
    /// genuinely-AoE effects. But eleven of the eighteen are SINGLE-TARGET, and the
    /// flag gates the whole base block -- which is where the crit roll, the Hidden
    /// (Umbral Step / stealth) guaranteed crit and its consumption, the Marked +30%
    /// bonus, damage statistics, and ApplyPostHitEnchantments all live.
    ///
    /// So those eleven silently stopped critting, silently wasted Hidden, and silently
    /// stopped proccing Lifedrinker / Siphon / elemental enchants. No test failed and
    /// nothing crashed, which is exactly why it needs a structural guard: the fix is
    /// invisible to behavioural tests.
    ///
    /// These assert on source structure because the damage path is a 25k-line private
    /// method with no seam to call into.
    /// </summary>
    public class AbilityDamagePipelineTests
    {
        private static string CombatEngineSource()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
                dir = dir.Parent;
            dir.Should().NotBeNull("the test must be able to find the repo root");
            return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Systems", "CombatEngine.cs"));
        }

        /// <summary>The single-target effects that must keep the full damage pipeline.</summary>
        private static readonly string[] SingleTargetHandlers =
        {
            "echo_25", "cycles_end", "overflow_aoe", "soul_leech", "consume_soul",
            "execute_reap", "devour", "entropic_blade", "annihilation", "wrath_deep",
            "shaman_lightning_bolt",
        };

        [Fact]
        public void SingleTargetHandlers_RouteThroughTheSharedDamagePipeline()
        {
            var src = CombatEngineSource();

            foreach (var effect in SingleTargetHandlers)
            {
                int start = src.IndexOf($"case \"{effect}\":", System.StringComparison.Ordinal);
                start.Should().BeGreaterThan(0, $"handler for {effect} must exist");

                // Look at the handler body only, not the whole switch.
                string body = src.Substring(start, System.Math.Min(2000, src.Length - start));
                int next = body.IndexOf("\n            case \"", System.StringComparison.Ordinal);
                if (next > 0) body = body.Substring(0, next);

                body.Should().Contain("ApplyHandlerAbilityDamage",
                    $"{effect} is single-target and sits in the AoE skip list, so it must route " +
                    "its damage through the shared pipeline or it loses crit, Hidden consumption, " +
                    "the Marked bonus, damage statistics and weapon enchant procs");
            }
        }

        [Fact]
        public void TheSharedPipeline_AppliesCritMarkedAndEnchants()
        {
            var src = CombatEngineSource();
            int start = src.IndexOf("private long ApplyHandlerAbilityDamage(", System.StringComparison.Ordinal);
            start.Should().BeGreaterThan(0, "the shared ability-damage pipeline must exist");

            string body = src.Substring(start, System.Math.Min(3000, src.Length - start));

            body.Should().Contain("StatusEffect.Hidden", "the guaranteed crit must be honored and consumed");
            body.Should().Contain("RollCriticalHit", "abilities must be able to crit");
            body.Should().Contain("IsMarked", "the Marked +30% bonus must apply (v0.60.9 fix)");
            body.Should().Contain("ApplyPostHitEnchantments", "weapon enchants must still proc (v0.61.7 fix)");
            body.Should().Contain("RecordDamageDealt", "damage must be reported, or kill summaries read 0");
        }

        [Fact]
        public void SingleTargetHandlers_StayInTheSkipList_SoDamageIsNotDoubleApplied()
        {
            // The other half of the invariant: routing through the pipeline is only
            // correct while the base block still skips these. If someone "fixes" this
            // by removing them from the list, damage doubles again.
            var src = CombatEngineSource();
            int start = src.IndexOf("bool isAoEAbility =", System.StringComparison.Ordinal);
            start.Should().BeGreaterThan(0);

            // Offset-based rather than substring-based: this reports WHICH effect
            // drifted out of the list and by how far, instead of dumping a truncated
            // blob that FluentAssertions elides.
            foreach (var effect in SingleTargetHandlers)
            {
                int at = src.IndexOf($"or \"{effect}\"", start, System.StringComparison.Ordinal);
                at.Should().BeGreaterThan(0, $"{effect} must still appear in the isAoEAbility list");
                (at - start).Should().BeLessThan(2500,
                    $"{effect} applies its own damage, so it must stay inside the isAoEAbility " +
                    "declaration and let the base block skip it, or damage is applied twice");
            }
        }
    }
}
