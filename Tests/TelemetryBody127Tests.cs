using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.7 (T2b): the client's request body and the server's reader joined by one shared file. The body
/// BuildBody makes for a full batch of 100 rows with every value at its maximum is committed as
/// Tests/Fixtures/telemetry-body-max.json; this test makes it again and compares byte for byte, and the
/// web tests (web/test/telemetry-api.test.js, telemetry-loopback.test.js) send the same file to the
/// endpoint, which must take all 100 rows under its 128 KB cap. Nothing here touches the network.
/// </summary>
public class TelemetryBody127Tests
{
    /// <summary>Fixed so the bytes never change: the install_id, the largest version the server takes
    /// (0 to 1000 each part), the largest source, and a queue day (the day stays local).</summary>
    internal const string FixtureInstallId = "0123456789abcdef0123456789abcdef";
    internal const string FixtureVersion = "1000.1000.1000";
    internal const TelemetrySource FixtureSource = TelemetrySource.Server;
    internal const long FixtureQueueDay = 20366;
    internal const int ServerCapBytes = 131072;

    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "telemetry-body-max.json");

    /// <summary>One queue line whose row has every column at its upper bound.</summary>
    internal static string MaxLine()
    {
        var row = new JsonObject();
        foreach (var c in TelemetryRow.Columns) row[c.Key] = c.Max;
        return new JsonObject { ["d"] = FixtureQueueDay, ["r"] = row }.ToJsonString();
    }

    internal static byte[] MaxBody(int lines) =>
        TelemetryUploader.BuildBody(Enumerable.Repeat(MaxLine(), lines).ToList(), FixtureInstallId, FixtureSource, FixtureVersion);

    [Fact]
    public void T2b_MaxBody_IsTheFixture_ByteForByte()
    {
        var expected = File.ReadAllBytes(FixturePath);
        var actual = MaxBody(TelemetryUploader.MaxBatchRows);
        actual.Length.Should().Be(expected.Length, "the body BuildBody makes is the committed fixture");
        actual.Should().Equal(expected);
        // it needs the 128 KB cap: over the old 64 KB, inside 131072 bytes
        expected.Length.Should().BeGreaterThan(65536).And.BeLessThanOrEqualTo(ServerCapBytes);
        // a longer queue still sends at most 100 rows: the same bytes
        MaxBody(TelemetryUploader.MaxBatchRows + 1).Should().Equal(expected);
    }

    [Fact]
    public void T2b_MaxBody_HoldsOneHundredRows_EveryValueAtItsMaximum()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(FixturePath));
        var root = doc.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal("schema", "version", "source", "install_id", "rows");
        root.GetProperty("schema").GetInt32().Should().Be(TelemetryUploader.Schema);
        root.GetProperty("version").EnumerateArray().Select(v => v.GetInt64()).Should().Equal(1000L, 1000L, 1000L);
        root.GetProperty("source").GetInt32().Should().Be((int)TelemetrySource.Server);
        root.GetProperty("install_id").GetString().Should().Be(FixtureInstallId);
        var rows = root.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().HaveCount(TelemetryUploader.MaxBatchRows);
        foreach (var r in rows)
        {
            TelemetryRow.IsValid(r).Should().BeTrue();
            r.EnumerateObject().Select(p => (p.Name, p.Value.GetInt64()))
                .Should().Equal(TelemetryRow.Columns.Select(c => (c.Key, c.Max)));
        }
    }
}
