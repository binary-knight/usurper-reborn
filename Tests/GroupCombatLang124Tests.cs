using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4: group combat in each member's language. A follower's turn runs on the leader's session, so
/// it is drawn inside a Loc.RenderLanguage scope in the follower's language; the scope never writes the
/// leader's session language.
/// </summary>
[Collection("SharedGameSingletons")]
public class GroupCombatLang124Tests
{
    // ---------- 1. the render language scope ----------

    private static void WithLeaderSession(Action<SessionContext> body)
    {
        var saved = SessionContext.Current;
        var ctx = new SessionContext { Username = "leader", Language = "en" };
        SessionContext.Current = ctx;
        try { body(ctx); }
        finally { SessionContext.Current = saved; }
    }

    [Fact]
    public void RenderLanguage_ResolvesInTheScopeLanguage_AndRestoresAfterIt()
    {
        WithLeaderSession(ctx =>
        {
            using (Loc.RenderLanguage("hu"))
            {
                GameConfig.Language.Should().Be("hu");
                Loc.Get("combat.round_label", 3).Should().Be(Loc.GetIn("hu", "combat.round_label", 3));
            }
            GameConfig.Language.Should().Be("en", "the scope restores on exit");
            Loc.RenderLanguageOverride.Should().BeNull();
            Loc.Get("combat.round_label", 3).Should().Be("Round 3");
        });
    }

    [Fact]
    public void RenderLanguage_DoesNotWriteTheLeadersSessionLanguage()
    {
        WithLeaderSession(ctx =>
        {
            using (Loc.RenderLanguage("hu"))
            {
                ctx.Language.Should().Be("en", "the leader's own setting is not touched");
                // the setter inside a scope does nothing: neither the override nor the leader's setting moves
                GameConfig.Language = "fr";
                GameConfig.Language.Should().Be("hu");
                ctx.Language.Should().Be("en");
                CombatEngine.InLanguage("it", () => ctx.Language).Should().Be("en");
            }
            ctx.Language.Should().Be("en");
            GameConfig.Language.Should().Be("en");
            CombatEngine.InLanguage("hu", () => GameConfig.Language + "/" + ctx.Language).Should().Be("hu/en");
            ctx.Language.Should().Be("en");
        });
    }

    [Fact]
    public void RenderLanguage_RestoresAfterAnExceptionInside()
    {
        WithLeaderSession(ctx =>
        {
            Action act = () =>
            {
                using (Loc.RenderLanguage("hu"))
                    throw new InvalidOperationException("boom");
            };
            act.Should().Throw<InvalidOperationException>();
            GameConfig.Language.Should().Be("en");
            Loc.RenderLanguageOverride.Should().BeNull();
            ctx.Language.Should().Be("en");
        });
    }

    [Fact]
    public async Task RenderLanguage_HoldsAcrossAwaits_AndNests()
    {
        var saved = SessionContext.Current;
        var ctx = new SessionContext { Username = "leader", Language = "en" };
        SessionContext.Current = ctx;
        try
        {
            using (Loc.RenderLanguage("hu"))
            {
                await Task.Yield();
                await Task.Delay(5);
                GameConfig.Language.Should().Be("hu");
                using (Loc.RenderLanguage("fr"))
                {
                    await Task.Yield();
                    GameConfig.Language.Should().Be("fr");
                }
                GameConfig.Language.Should().Be("hu", "the inner scope gives back the outer one");
                using (Loc.SessionLanguage())
                    GameConfig.Language.Should().Be("en", "a session scope reads the leader's own language");
                GameConfig.Language.Should().Be("hu");
            }
            GameConfig.Language.Should().Be("en");
            ctx.Language.Should().Be("en");
        }
        finally { SessionContext.Current = saved; }
    }

    [Fact]
    public void Recordings_Nest_SoAnInnerOneDoesNotEndTheOuter()
    {
        var outer = Loc.BeginRecording();
        string first, second;
        try
        {
            first = Loc.Get("combat.round_label", 1);
            var inner = Loc.BeginRecording();
            try { Loc.Get("combat.party_label"); }
            finally { Loc.EndRecording(); }
            second = Loc.Get("combat.round_label", 2);
            inner.Render("Party:", "hu").Should().Be(Loc.GetIn("hu", "combat.party_label"));
        }
        finally { Loc.EndRecording(); }
        outer.Render(first + " " + second + " Party:", "hu").Should().Be(
            $"{Loc.GetIn("hu", "combat.round_label", 1)} {Loc.GetIn("hu", "combat.round_label", 2)} {Loc.GetIn("hu", "combat.party_label")}");
    }
}
