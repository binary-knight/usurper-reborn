using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using UsurperRemake.Server;
using Xunit;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// v1.0.2 semantic color roles.
    ///
    /// Player report: "Text color in general is just inconsistent as all get-out.
    /// Please stop using darkgray for important readouts like my status effects
    /// and combat damage readouts. Use it for things that are irrelevant to the
    /// flow of the game, like stat rolls."
    ///
    /// The cause was structural: call sites named a COLOR, so every author
    /// guessed. Switching themes could not have fixed it either -- ClassicDark
    /// collapses white / gray / dark_gray / green into a single dim_green, so
    /// under it an urgent line and an ignorable one render identically.
    ///
    /// These tests pin the property that makes roles worth having: in EVERY
    /// theme, something you must act on is visually distinct from something you
    /// can ignore.
    /// </summary>
    public class ColorRoleTests : IDisposable
    {
        // ColorTheme.Current falls back to a process-wide static when no session
        // is attached, and xUnit runs test classes in parallel. Establishing a
        // SessionContext (AsyncLocal) keeps every theme switch below scoped to
        // this test's own execution flow, so it cannot leak into a concurrent
        // class the way the shared singletons did earlier in this release.
        public ColorRoleTests()
        {
            SessionContext.Current = new SessionContext
            {
                InputStream = Stream.Null,
                OutputStream = Stream.Null,
            };
        }

        public void Dispose() => SessionContext.Current = null;

        [Fact]
        public void ThemeSwitchesAreFlowLocal_NotProcessWide()
        {
            // Guards the isolation the constructor sets up. If this ever fails,
            // these tests have started mutating global state again.
            ColorTheme.Current = ColorThemeType.GreenPhosphor;
            SessionContext.Current!.ColorTheme.Should().Be(ColorThemeType.GreenPhosphor,
                "the theme must be written to the session, not the process-wide default");
        }

        private static readonly string[] AllRoles =
        {
            ColorRole.Critical, ColorRole.Success, ColorRole.Action,
            ColorRole.Notice, ColorRole.Narration, ColorRole.Derived,
            ColorRole.Disabled,
        };

        private static IEnumerable<ColorThemeType> AllThemes =>
            Enum.GetValues<ColorThemeType>();

        [Fact]
        public void EveryRoleResolvesInEveryTheme_AndNeverToARoleName()
        {
            foreach (var theme in AllThemes)
            {
                ColorTheme.Current = theme;
                foreach (var role in AllRoles)
                {
                    var got = ColorTheme.Resolve(role);
                    got.Should().NotBeNullOrEmpty($"{role} must resolve under {theme}");
                    ColorTheme.IsRole(got).Should().BeFalse(
                        $"{role} under {theme} must resolve to a concrete color, not another role");
                }
            }
        }

        [Fact]
        public void DerivedIsAlwaysDistinctFromCritical_TheWholePointOfRoles()
        {
            // If these two ever collapse, an urgent readout and an ignorable
            // stat roll look identical -- which is exactly the reported bug.
            foreach (var theme in AllThemes)
            {
                ColorTheme.Current = theme;
                ColorTheme.Resolve(ColorRole.Derived).Should().NotBe(
                    ColorTheme.Resolve(ColorRole.Critical),
                    $"{theme} must keep ignorable text distinct from urgent text");
            }
        }

        [Fact]
        public void DerivedIsAlwaysDistinctFromNarration()
        {
            // Body text and skippable derivations must not read the same, or
            // the player cannot tell what is safe to skim.
            foreach (var theme in AllThemes)
            {
                ColorTheme.Current = theme;
                ColorTheme.Resolve(ColorRole.Derived).Should().NotBe(
                    ColorTheme.Resolve(ColorRole.Narration),
                    $"{theme} must keep derivations dimmer than body text");
            }
        }

        [Fact]
        public void ClassicDark_KeepsRolesDistinct_WhereItsLiteralMapCollapsesThem()
        {
            // The literal map sends white, gray, dark_gray and green all to
            // dim_green. Roles must NOT be routed through that collapse.
            ColorTheme.Current = ColorThemeType.ClassicDark;

            ColorTheme.Resolve("white").Should().Be("dim_green");
            ColorTheme.Resolve("dark_gray").Should().Be("dim_green");

            var narration = ColorTheme.Resolve(ColorRole.Narration);
            var derived = ColorTheme.Resolve(ColorRole.Derived);
            derived.Should().NotBe(narration,
                "roles must bypass the literal collapse that makes ClassicDark unreadable for this");
        }

        [Fact]
        public void MonochromeThemes_StillSeparateIgnorableFromActionable()
        {
            // Three brightness tiers only, so some roles share one. The
            // collapse is deliberate, but Derived must never share with an
            // actionable role.
            foreach (var theme in new[] { ColorThemeType.AmberRetro, ColorThemeType.GreenPhosphor })
            {
                ColorTheme.Current = theme;
                var derived = ColorTheme.Resolve(ColorRole.Derived);
                foreach (var loud in new[] { ColorRole.Critical, ColorRole.Success, ColorRole.Action })
                {
                    ColorTheme.Resolve(loud).Should().NotBe(derived,
                        $"{theme}: {loud} must not look like ignorable text");
                }
            }
        }

        [Fact]
        public void HighContrast_DoesNotFlattenEverythingIntoOneShade()
        {
            ColorTheme.Current = ColorThemeType.HighContrast;
            var distinct = AllRoles.Select(r => ColorTheme.Resolve(r)).Distinct().Count();
            distinct.Should().BeGreaterThan(1, "a contrast theme must still have a hierarchy");
        }

        [Fact]
        public void LiteralColorNamesStillWork_MigrationIsIncremental()
        {
            ColorTheme.Current = ColorThemeType.Default;
            ColorTheme.Resolve("bright_red").Should().Be("bright_red");
            ColorTheme.Resolve("").Should().Be("");
            ColorTheme.IsRole("bright_red").Should().BeFalse();
            ColorTheme.IsRole(ColorRole.Critical).Should().BeTrue();
        }

        [Fact]
        public void EveryRoleResolvesToAColorTheRendererActuallyKnows()
        {
            // Resolve() returning an unrecognized name is not a loud failure --
            // GetAnsiColorCode silently falls back to white, so a typo in a theme
            // map would ship as "everything is white in AmberRetro" and nobody
            // would see it in a unit test that only compared strings.
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "black", "blue", "bright_blue", "bright_cyan", "bright_green",
                "bright_magenta", "bright_red", "bright_white", "bright_yellow",
                "brown", "cyan", "dark_blue", "dark_cyan", "dark_gray",
                "dark_green", "dark_magenta", "dark_red", "dark_yellow",
                "dim_green", "gray", "green", "grey", "magenta", "red",
                "white", "yellow",
            };

            foreach (var theme in AllThemes)
            {
                ColorTheme.Current = theme;
                foreach (var role in AllRoles)
                {
                    known.Should().Contain(ColorTheme.Resolve(role),
                        $"{role} under {theme} must map to a color the ANSI renderer recognizes");
                }
            }
        }

        [Fact]
        public void UnknownRoleFallsBackLegible_NeverInvisible()
        {
            ColorTheme.Current = ColorThemeType.Default;
            var got = ColorTheme.Resolve("role_does_not_exist");
            got.Should().Be("white", "an unrecognized role must be readable rather than dim or blank");
        }
    }
}
