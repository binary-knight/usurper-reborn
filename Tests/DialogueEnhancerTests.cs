using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// Smoke tests for the Phase 1 / 1.5 NPC dialogue enhancer
/// (see memory/project_npc_dialogue.md for full design).
///
/// These tests don't lock specific flavor strings -- the variant pools
/// shift between releases -- they lock the contract: null-safe inputs,
/// no exceptions on bare NPCs without Brain/Personality/EmotionalState,
/// base line preserved when no layer fires, and the recent-line cache
/// not crashing on repeated calls.
/// </summary>
public class DialogueEnhancerTests
{
    [Fact]
    public void Enhance_NullBaseLine_ReturnsEmptyString()
    {
        var npc = new NPC();
        var player = new Character();

        var result = DialogueEnhancer.Enhance(null!, npc, player);

        result.Should().Be("");
    }

    [Fact]
    public void Enhance_EmptyBaseLine_ReturnsEmptyString()
    {
        var npc = new NPC();
        var player = new Character();

        var result = DialogueEnhancer.Enhance("", npc, player);

        result.Should().Be("");
    }

    [Fact]
    public void Enhance_NullNpc_ReturnsBaseLineUnchanged()
    {
        var player = new Character();

        var result = DialogueEnhancer.Enhance("Hello, traveler.", null!, player);

        result.Should().Be("Hello, traveler.");
    }

    [Fact]
    public void Enhance_NullPlayer_ReturnsBaseLineUnchanged()
    {
        var npc = new NPC();

        var result = DialogueEnhancer.Enhance("Hello, traveler.", npc, null!);

        result.Should().Be("Hello, traveler.");
    }

    [Fact]
    public void Enhance_BareNpc_NoBrainOrPersonality_DoesNotThrow()
    {
        // Deserialization-shaped NPC: Brain / Personality / Memory all null.
        // Every layer must guard against this.
        var npc = new NPC();
        var player = new Character { Name2 = "Hero" };

        // Run many iterations to give every probabilistic layer a chance to fire.
        for (int i = 0; i < 200; i++)
        {
            var result = DialogueEnhancer.Enhance("Hello.", npc, player);
            result.Should().NotBeNullOrEmpty();
            // Base line "Hello." must appear in the output regardless of which
            // layers fire (layers only prepend / append, never mutate the middle).
            result.Should().Contain("Hello.");
        }
    }

    [Fact]
    public void Enhance_BasicNpc_WithEmptyAccessors_ReturnsAtLeastBaseLine()
    {
        // NPC with systems initialized but no memories / no faction / no
        // strong emotion -- every layer should opt out and return base line.
        // (We can't easily zero out everything, but we can construct a
        // minimal NPC and confirm the result always contains the base line.)
        var npc = new NPC();
        npc.Name1 = "Bob";
        npc.Name2 = "Bob";
        var player = new Character { Name2 = "Hero" };

        for (int i = 0; i < 50; i++)
        {
            var result = DialogueEnhancer.Enhance("How are you?", npc, player);
            result.Should().Contain("How are you?");
        }
    }

    [Fact]
    public void Enhance_RepeatedCalls_DoNotCrashRecentLineCache()
    {
        // Variety cache uses a static dictionary -- hammer it to make sure
        // no concurrent-modification / unbounded-growth issues surface.
        var npc = new NPC { Name1 = "Alice", Name2 = "Alice" };
        var player = new Character { Name2 = "Hero" };

        for (int i = 0; i < 1000; i++)
        {
            DialogueEnhancer.Enhance("Greeting.", npc, player);
        }
    }

    [Fact]
    public void GetMoodFlavor_BareNpc_ReturnsNull()
    {
        var npc = new NPC();
        var result = DialogueEnhancer.GetMoodFlavor(npc);
        result.Should().BeNull("a bare NPC with no EmotionalState shouldn't surface mood flavor");
    }

    [Fact]
    public void GetMoodFlavor_NullNpc_ReturnsNull()
    {
        var result = DialogueEnhancer.GetMoodFlavor(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void GetMemoryFlavor_BareNpc_ReturnsNull()
    {
        var npc = new NPC();
        var player = new Character { Name2 = "Hero" };
        var result = DialogueEnhancer.GetMemoryFlavor(npc, player);
        result.Should().BeNull();
    }

    [Fact]
    public void GetFactionTensionFlavor_BareNpc_ReturnsNull()
    {
        // NPC with no NPCFaction set + no faction system should not throw
        // and should return null (no tension to surface).
        var npc = new NPC();
        var player = new Character { Name2 = "Hero" };
        var result = DialogueEnhancer.GetFactionTensionFlavor(npc, player);
        result.Should().BeNull();
    }

    [Fact]
    public void Enhance_UnsupportedLanguage_ReturnsBaseLineUnchanged()
    {
        // v0.61.4 language gate: enhancer is gated to supported languages
        // (currently en + hu). Other languages (es / fr / it) still see the
        // raw base line because their VN templates and flavor pools haven't
        // been translated yet. Gate widens per future loc passes.
        var npc = new NPC();
        var player = new Character { Name2 = "Hero" };
        var originalLang = GameConfig.Language;

        try
        {
            foreach (var lang in new[] { "es", "fr", "it" })
            {
                GameConfig.Language = lang;
                for (int i = 0; i < 50; i++)
                {
                    var result = DialogueEnhancer.Enhance("Hello there.", npc, player);
                    result.Should().Be("Hello there.",
                        $"language '{lang}' must skip enhancement and return base line exactly");
                }
            }
        }
        finally
        {
            GameConfig.Language = originalLang;
        }
    }

    [Fact]
    public void GetMoodFlavor_UnsupportedLanguage_ReturnsNull()
    {
        var npc = new NPC();
        var originalLang = GameConfig.Language;
        try
        {
            GameConfig.Language = "es";
            var result = DialogueEnhancer.GetMoodFlavor(npc);
            result.Should().BeNull("unsupported-language sessions must not surface mood flavor");
        }
        finally
        {
            GameConfig.Language = originalLang;
        }
    }

    [Fact]
    public void Enhance_PreservesBaseLineContent_WhenNoLayerFires()
    {
        // With a deserialization-shaped NPC (no systems initialized), no
        // layer should ever fire -- the base line must come through intact.
        var npc = new NPC();
        var player = new Character { Name2 = "Hero" };

        var baseLine = "The merchant nods at you.";
        var result = DialogueEnhancer.Enhance(baseLine, npc, player);

        // Result always contains the base line as a substring -- layers
        // only prepend or append, never mutate the middle.
        result.Should().Contain(baseLine);
    }
}
