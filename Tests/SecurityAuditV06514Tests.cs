using System;
using FluentAssertions;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// Regression tests for the v0.65.14 security audit fixes.
    ///
    /// F1 -- the Implementor account is auto-promoted to the top wizard tier on
    /// every login and can never be demoted, so on a fresh deploy whoever
    /// registered that name first owned the server. Self-service registration
    /// must refuse it, case-insensitively, at BOTH account-creation paths
    /// (RegisterPlayer and AutoProvisionPlayer share IsReservedUsername).
    /// </summary>
    public class SecurityAuditV06514Tests
    {
        [Theory]
        [InlineData("rage")]
        [InlineData("Rage")]
        [InlineData("RAGE")]
        [InlineData("rAgE")]
        [InlineData("  rage  ")] // trimmed before comparison
        public void ImplementorUsername_IsReservedFromRegistration(string attempt)
        {
            SqlSaveBackend.IsReservedUsername(attempt).Should().BeTrue(
                "registering the Implementor account would grant a stranger permanent superuser");
        }

        [Theory]
        [InlineData("ragefire")]     // prefix, not the reserved name
        [InlineData("notrage")]      // suffix
        [InlineData("rag")]
        [InlineData("Griffon")]
        [InlineData("")]
        [InlineData("   ")]
        public void OrdinaryUsernames_AreNotReserved(string attempt)
        {
            SqlSaveBackend.IsReservedUsername(attempt).Should().BeFalse(
                "the reservation must be an exact (case-insensitive) match, not a substring rule");
        }

        [Fact]
        public void ImplementorUsername_DefaultsToCanonicalOwner()
        {
            // No env override in the test process.
            WizardConstants.ImplementorUsername.Should().Be(WizardConstants.DefaultImplementorUsername);
            WizardConstants.IMPLEMENTOR_USERNAME.Should().Be(WizardConstants.ImplementorUsername,
                "the legacy constant name must keep resolving to the live value");
        }

        [Fact]
        public void ImplementorUsername_IsOperatorConfigurable()
        {
            // Self-hosters must be able to point the superuser account at one
            // THEY control rather than inheriting the canonical server's name.
            const string varName = "USURPER_IMPLEMENTOR";
            var original = Environment.GetEnvironmentVariable(varName);
            try
            {
                Environment.SetEnvironmentVariable(varName, "SelfHostOwner");
                WizardConstants.ImplementorUsername.Should().Be("selfhostowner",
                    "the configured name is normalized to lowercase to match storage");
                SqlSaveBackend.IsReservedUsername("selfhostowner").Should().BeTrue();
                SqlSaveBackend.IsReservedUsername("rage").Should().BeFalse(
                    "once overridden, the default name is an ordinary account again");
            }
            finally
            {
                Environment.SetEnvironmentVariable(varName, original);
            }
        }
    }
}
