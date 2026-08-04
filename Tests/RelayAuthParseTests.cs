using FluentAssertions;
using UsurperRemake.Server;
using Xunit;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// Regression tests for the SSH relay's AUTH parser.
    ///
    /// v1.0.1: v0.65.13 appended a trailing client-version field to the login
    /// AUTH line that the desktop and Steam clients send, and updated
    /// MudServer's parser -- but not the relay's. Those clients connect
    /// SSH-encrypted by default, which routes through the relay, so every one
    /// of them was rejected with "Invalid AUTH format: 4 parts" and bounced
    /// back to the main menu. Direct-TCP clients skipped the relay and kept
    /// working, which disguised a transport-wide outage as an account problem.
    ///
    /// These tests pin EVERY wire shape the relay must accept, so adding a
    /// field to the AUTH line again cannot silently break the default
    /// connection path.
    /// </summary>
    public class RelayAuthParseTests
    {
        [Fact]
        public void Login_Unversioned_LegacyClient()
        {
            var r = RelayClient.ParseDirectAuth("AUTH:Rage:hunter2:Steam");
            r.Should().NotBeNull();
            r!.Value.username.Should().Be("Rage");
            r.Value.password.Should().Be("hunter2");
            r.Value.connectionType.Should().Be("Steam");
            r.Value.isRegistration.Should().BeFalse();
            r.Value.clientVersion.Should().BeNull();
        }

        [Fact]
        public void Login_Versioned_IsTheShapeThatWasBroken()
        {
            // The exact line a 1.0.0 Steam client sends.
            var r = RelayClient.ParseDirectAuth("AUTH:Rage:hunter2:Steam:1.0.0");
            r.Should().NotBeNull("this shape was rejected outright before v1.0.1");
            r!.Value.username.Should().Be("Rage");
            r.Value.password.Should().Be("hunter2");
            r.Value.connectionType.Should().Be("Steam");
            r.Value.isRegistration.Should().BeFalse();
            r.Value.clientVersion.Should().Be("1.0.0");
        }

        [Fact]
        public void Register_Unversioned()
        {
            var r = RelayClient.ParseDirectAuth("AUTH:NewGuy:pw:REGISTER:Local");
            r.Should().NotBeNull();
            r!.Value.username.Should().Be("NewGuy");
            r.Value.connectionType.Should().Be("Local");
            r.Value.isRegistration.Should().BeTrue();
        }

        [Fact]
        public void Register_Versioned()
        {
            var r = RelayClient.ParseDirectAuth("AUTH:NewGuy:pw:REGISTER:Local:1.0.1");
            r.Should().NotBeNull();
            r!.Value.connectionType.Should().Be("Local");
            r.Value.isRegistration.Should().BeTrue();
            r.Value.clientVersion.Should().Be("1.0.1");
        }

        [Fact]
        public void Register_DiscriminatorWinsOverVersionedLoginShape()
        {
            // Both are 4 payload fields. REGISTER must be recognized first, or a
            // registration would be parsed as a login with connectionType="REGISTER".
            var r = RelayClient.ParseDirectAuth("AUTH:Someone:pw:register:Web");
            r.Should().NotBeNull();
            r!.Value.isRegistration.Should().BeTrue("the discriminator is case-insensitive");
            r.Value.connectionType.Should().Be("Web");
        }

        [Theory]
        [InlineData("AUTH:onlyuser")]
        [InlineData("AUTH:user:pass")]
        [InlineData("AUTH:a:b:c:d:e:f:g")]
        public void MalformedShapesAreStillRejected(string line)
        {
            RelayClient.ParseDirectAuth(line).Should().BeNull();
        }
    }
}
