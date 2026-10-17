using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.3: the version and the telemetry body. The release notes, Steam text, README and website roadmap checks join
/// in the bump commit. Nothing here touches the network.
/// </summary>
public class Bump133Tests
{
    [Fact]
    public void Version_Is133_NamedReckoning_AndTheTelemetryBodySays133()
    {
        GameConfig.Version.Should().Be("1.3.3");
        GameConfig.VersionName.Should().Be("Reckoning");
        byte[] body = TelemetryUploader.BuildBody(new List<string> { TelemetryBody127Tests.MaxLine() },
            TelemetryBody127Tests.FixtureInstallId, TelemetrySource.Single);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("version").EnumerateArray().Select(v => v.GetInt64()).Should().Equal(1L, 3L, 3L);
    }
}
