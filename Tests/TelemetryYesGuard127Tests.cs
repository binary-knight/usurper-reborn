using System.Linq;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.7 (T5): guards on the telemetry question's yes. In every shipped language the offered yes and
/// the negative words share no entry, so no answer is both. A missing ui.yes or ui.yn_prompt key never
/// turns a stray letter into a yes: Loc.Get gives the key itself for a key missing in every language,
/// and the key's letters are not taken. No Loc table is changed here (other test classes read it in
/// parallel); the missing key texts are passed to OfferedYesWords as Loc.Get would give them.
/// </summary>
[Collection("SharedGameSingletons")]
public class TelemetryYesGuard127Tests
{
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] NegativeWords = { "N", "NO", "NON", "NEM" };

    private static string[] Letters() => Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).ToArray();

    [Fact]
    public void LocGet_GivesTheKeyItself_ForAKeyMissingInEveryLanguage()
    {
        const string missing = "telemetry.t5_no_such_key";
        foreach (string lang in Languages)
        {
            using var _ = Loc.RenderLanguage(lang);
            Loc.Get(missing).Should().Be(missing, lang);
            Loc.GetIn(lang, missing).Should().Be(missing, lang);
        }
    }

    [Fact]
    public void TheOfferedYes_AndTheNegativeWords_ShareNoEntry_InEveryLanguage()
    {
        foreach (string lang in Languages)
        {
            using var _ = Loc.RenderLanguage(lang);
            var yes = GameConfig.OfferedYesWords();
            yes.Should().NotBeEmpty(lang);
            yes.Where(GameConfig.IsNegative).Should().BeEmpty($"{lang}: no offered yes is also a no");
            foreach (string w in Letters().Concat(NegativeWords).Concat(yes))
                (GameConfig.IsOfferedYes(w) && GameConfig.IsNegative(w)).Should().BeFalse($"{lang}: {w}");
            foreach (string part in Loc.Get("ui.no").Split('='))
                GameConfig.IsOfferedYes(part.Trim()).Should().BeFalse($"{lang}: the language's own no, {part.Trim()}");
        }
    }

    [Fact]
    public void AMissingYesKey_NeverMakesAStrayLetterAYes()
    {
        foreach (string lang in Languages)
        {
            using var _ = Loc.RenderLanguage(lang);
            string yes = Loc.Get("ui.yes"), prompt = Loc.Get("ui.yn_prompt");
            var cases = new (string Name, string Yes, string Prompt)[]
            {
                ("ui.yes missing everywhere", "ui.yes", prompt),
                ("ui.yn_prompt missing everywhere", yes, "ui.yn_prompt"),
                ("ui.yes missing in this language", Loc.GetIn("en", "ui.yes"), prompt),
                ("ui.yn_prompt missing in this language", yes, Loc.GetIn("en", "ui.yn_prompt")),
            };
            foreach (var c in cases)
            {
                var words = GameConfig.OfferedYesWords(c.Yes, c.Prompt);
                words.Should().NotBeEmpty($"{lang}, {c.Name}: the other text still gives a yes");
                foreach (string letter in Letters().Where(words.Contains))
                    GameConfig.IsAffirmative(letter).Should().BeTrue($"{lang}, {c.Name}: {letter} is no language's yes");
                words.Should().NotContain(w => w.StartsWith("UI"), $"{lang}, {c.Name}: no part of the key");
                words.Where(GameConfig.IsNegative).Should().BeEmpty($"{lang}, {c.Name}");
            }
            GameConfig.OfferedYesWords("ui.yes", "ui.yn_prompt").Should().BeEmpty($"{lang}: both missing, nothing is a yes");
        }
    }
}
