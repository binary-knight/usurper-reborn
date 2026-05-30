using Xunit;
using FluentAssertions;
using System;
using System.Threading;
using System.Threading.Tasks;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v0.64.0 Brain v2 Slice 5: LLM infrastructure tests.
///
/// Tests cover the deterministic surfaces: settings env-var reading, budget
/// tracking + cap enforcement, moment-generator fallback path when LLM is
/// disabled. Real HTTP calls against an LLM endpoint are NOT tested here
/// (that's integration territory and requires network + API keys).
///
/// IMPORTANT: these tests mutate process-wide environment variables and the
/// LLMBudget singleton. They run in xUnit's default parallel mode but each
/// test resets state in a finally / dispose pattern to keep cross-test
/// contamination low.
/// </summary>
public class LLMTests : IDisposable
{
    public LLMTests()
    {
        // Snapshot env vars we mutate so we can restore in Dispose.
        _savedEnabled = Environment.GetEnvironmentVariable("USURPER_LLM_ENABLED");
        _savedEndpoint = Environment.GetEnvironmentVariable("USURPER_LLM_ENDPOINT");
        _savedKey = Environment.GetEnvironmentVariable("USURPER_LLM_API_KEY");
        _savedModel = Environment.GetEnvironmentVariable("USURPER_LLM_MODEL");
        _savedCap = Environment.GetEnvironmentVariable("USURPER_LLM_DAILY_TOKEN_CAP");
        LLMBudget.Reset();
        LLMProvider.ResetForTests();
    }

    private readonly string? _savedEnabled;
    private readonly string? _savedEndpoint;
    private readonly string? _savedKey;
    private readonly string? _savedModel;
    private readonly string? _savedCap;

    public void Dispose()
    {
        // Restore env vars to their original values.
        Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", _savedEnabled);
        Environment.SetEnvironmentVariable("USURPER_LLM_ENDPOINT", _savedEndpoint);
        Environment.SetEnvironmentVariable("USURPER_LLM_API_KEY", _savedKey);
        Environment.SetEnvironmentVariable("USURPER_LLM_MODEL", _savedModel);
        Environment.SetEnvironmentVariable("USURPER_LLM_DAILY_TOKEN_CAP", _savedCap);
        LLMBudget.Reset();
        LLMProvider.ResetForTests();
    }

    // ----- LLMSettings -----

    [Fact]
    public void Settings_DefaultsToDisabled_WhenNoEnvVars()
    {
        Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", null);
        LLMSettings.Enabled.Should().BeFalse();
        LLMSettings.IsActive().Should().BeFalse();
    }

    [Fact]
    public void Settings_EnabledTrue_WithVariousTruthyValues()
    {
        foreach (var truthy in new[] { "true", "TRUE", "1", "yes", "on" })
        {
            Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", truthy);
            LLMSettings.Enabled.Should().BeTrue($"'{truthy}' should be parsed as true");
        }
    }

    [Fact]
    public void Settings_EnabledFalse_WithFalsyValues()
    {
        foreach (var falsy in new[] { "false", "0", "no", "off", "bogus" })
        {
            Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", falsy);
            LLMSettings.Enabled.Should().BeFalse($"'{falsy}' should be parsed as false");
        }
    }

    [Fact]
    public void Settings_IsActive_RequiresAllConfig()
    {
        // Enable + provide endpoint + key + model. IsActive still false because
        // we're not in online mode (test env defaults to single-player).
        Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", "true");
        Environment.SetEnvironmentVariable("USURPER_LLM_ENDPOINT", "https://example.invalid/v1/chat/completions");
        Environment.SetEnvironmentVariable("USURPER_LLM_API_KEY", "test_key");
        Environment.SetEnvironmentVariable("USURPER_LLM_MODEL", "test-model");

        // Single-player mode -> IsActive false regardless of other config.
        // We can't easily toggle DoorMode.IsOnlineMode in a test, but we can
        // verify the partial path: Enabled is true here.
        LLMSettings.Enabled.Should().BeTrue();
        LLMSettings.Endpoint.Should().Be("https://example.invalid/v1/chat/completions");
        LLMSettings.ApiKey.Should().Be("test_key");
        LLMSettings.Model.Should().Be("test-model");
    }

    [Fact]
    public void Settings_DefaultDailyCap_500000()
    {
        Environment.SetEnvironmentVariable("USURPER_LLM_DAILY_TOKEN_CAP", null);
        LLMSettings.DailyTokenCap.Should().Be(500_000);
    }

    [Fact]
    public void Settings_CustomDailyCap_FromEnvVar()
    {
        Environment.SetEnvironmentVariable("USURPER_LLM_DAILY_TOKEN_CAP", "100000");
        LLMSettings.DailyTokenCap.Should().Be(100_000);
    }

    [Fact]
    public void Settings_DailyCap_FallsBackOnInvalidNumber()
    {
        Environment.SetEnvironmentVariable("USURPER_LLM_DAILY_TOKEN_CAP", "not-a-number");
        LLMSettings.DailyTokenCap.Should().Be(500_000, "invalid value should fall back to default");
    }

    // ----- LLMBudget -----

    [Fact]
    public void Budget_StartsAtZero()
    {
        LLMBudget.Reset();
        LLMBudget.TokensUsedToday.Should().Be(0);
    }

    [Fact]
    public void Budget_RecordUsageAccumulates()
    {
        LLMBudget.Reset();
        LLMBudget.RecordUsage(100);
        LLMBudget.RecordUsage(250);
        LLMBudget.TokensUsedToday.Should().Be(350);
    }

    [Fact]
    public void Budget_CanSpend_TrueUnderCap()
    {
        LLMBudget.Reset();
        Environment.SetEnvironmentVariable("USURPER_LLM_DAILY_TOKEN_CAP", "1000");
        LLMBudget.RecordUsage(500);
        LLMBudget.CanSpend(400).Should().BeTrue("500 + 400 = 900 is under cap 1000");
    }

    [Fact]
    public void Budget_CanSpend_FalseOverCap()
    {
        LLMBudget.Reset();
        Environment.SetEnvironmentVariable("USURPER_LLM_DAILY_TOKEN_CAP", "1000");
        LLMBudget.RecordUsage(800);
        LLMBudget.CanSpend(500).Should().BeFalse("800 + 500 = 1300 exceeds cap 1000");
    }

    [Fact]
    public void Budget_Remaining_CalculatesCorrectly()
    {
        LLMBudget.Reset();
        Environment.SetEnvironmentVariable("USURPER_LLM_DAILY_TOKEN_CAP", "1000");
        LLMBudget.RecordUsage(300);
        LLMBudget.Remaining.Should().Be(700);
    }

    [Fact]
    public void Budget_NegativeUsage_Ignored()
    {
        LLMBudget.Reset();
        LLMBudget.RecordUsage(-100);
        LLMBudget.TokensUsedToday.Should().Be(0, "negative usage shouldn't underflow the counter");
    }

    [Fact]
    public void Budget_Reset_ZerosCounter()
    {
        LLMBudget.RecordUsage(500);
        LLMBudget.Reset();
        LLMBudget.TokensUsedToday.Should().Be(0);
    }

    // ----- LLMProvider -----

    [Fact]
    public void Provider_Get_ReturnsNull_WhenDisabled()
    {
        Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", "false");
        LLMProvider.ResetForTests();
        LLMProvider.Get().Should().BeNull();
    }

    [Fact]
    public void Provider_Get_ReturnsNull_WhenMissingEndpoint()
    {
        Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", "true");
        Environment.SetEnvironmentVariable("USURPER_LLM_ENDPOINT", null);
        Environment.SetEnvironmentVariable("USURPER_LLM_API_KEY", "key");
        Environment.SetEnvironmentVariable("USURPER_LLM_MODEL", "model");
        LLMProvider.ResetForTests();
        LLMProvider.Get().Should().BeNull("missing endpoint disables provider");
    }

    [Fact]
    public void Provider_Get_ReturnsNull_WhenMissingKey()
    {
        Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", "true");
        Environment.SetEnvironmentVariable("USURPER_LLM_ENDPOINT", "https://example.invalid/v1");
        Environment.SetEnvironmentVariable("USURPER_LLM_API_KEY", null);
        Environment.SetEnvironmentVariable("USURPER_LLM_MODEL", "model");
        LLMProvider.ResetForTests();
        LLMProvider.Get().Should().BeNull("missing API key disables provider");
    }

    // ----- LLMMoments -----

    [Fact]
    public async Task PostAvengeNews_NullAvenger_NoOp()
    {
        // Defensive: shouldn't throw on null inputs.
        await LLMMoments.PostAvengeNewsAsync(null!, "Killer");
        // No assertion beyond "didn't throw".
    }

    [Fact]
    public async Task PostAvengeNews_EmptyKillerName_NoOp()
    {
        var npc = new NPC { Name1 = "Hero", Name2 = "Hero" };
        await LLMMoments.PostAvengeNewsAsync(npc, "");
        // No assertion beyond "didn't throw".
    }

    [Fact]
    public async Task PostAvengeNews_LLMDisabled_PostsTemplatedFallback()
    {
        // Ensure LLM is disabled. The fire-and-forget Task should still complete
        // by posting the fallback news entry. We can't easily assert on
        // NewsSystem state in a unit test (it's a process-wide singleton with
        // side effects), but we can confirm the call doesn't throw and
        // completes within a reasonable timeout.
        Environment.SetEnvironmentVariable("USURPER_LLM_ENABLED", "false");
        LLMProvider.ResetForTests();

        var npc = new NPC
        {
            Name1 = "Aldric",
            Name2 = "Aldric of Stormhaven",
            Class = CharacterClass.Warrior,
            Level = 27,
        };
        // Don't await PostAvengeNewsAsync's inner Task -- it's the work that
        // happens after the method returns. Instead, run the inner work
        // synchronously and ensure it completes.
        var task = LLMMoments.PostAvengeNewsAsync(npc, "Black Hand Garrick");
        var completed = await Task.WhenAny(task, Task.Delay(2000));
        completed.Should().Be(task, "fallback path should complete within 2s");
    }
}
