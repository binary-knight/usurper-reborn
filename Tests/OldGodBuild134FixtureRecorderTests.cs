using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4: the fixture of what RunBossCombat hands an Old God fight, recorded through the real method and
/// its two inert test seams (the dialogue's modifiers set after the dialogue step; the god and its context
/// read as the fight is about to receive them). The hero is at Mental 0, so the engine returns before any
/// round. With OG134_FIXTURE_OUT set, the recorder writes the fixture text to that path; it never writes
/// into the tree. The second test holds the committed fixture to what the method sets up now.
/// </summary>
[Collection("SharedGameSingletons")]
public class OldGodBuild134FixtureRecorderTests
{
    [Fact]
    public async Task TheRecorder_CoversEveryCase_AndWritesOnlyWhenAsked()
    {
        var cases = await OldGodBuild134Support.RecordAll();
        cases.Should().HaveCount(8 * 2 * 2, "eight boss rows, artifact counts 0 and 4, two dialogues");
        cases.Select(c => c.Key).Should().OnlyHaveUniqueItems();
        cases.Should().OnlyContain(c => c.Lines.Length > 0);
        var outPath = Environment.GetEnvironmentVariable("OG134_FIXTURE_OUT");
        if (!string.IsNullOrEmpty(outPath))
            File.WriteAllText(outPath, OldGodBuild134Support.FixtureText(cases));
    }

    [Fact]
    public async Task TheCommittedFixture_IsWhatRunBossCombatSetsUp()
    {
        var fixture = OldGodBuild134Support.ReadFixture();
        var cases = await OldGodBuild134Support.RecordAll();
        fixture.Keys.Should().BeEquivalentTo(cases.Select(c => c.Key));
        foreach (var (key, lines) in cases)
            lines.Should().Equal(fixture[key], $"{key}: RunBossCombat sets up the fight the fixture recorded");
    }
}
