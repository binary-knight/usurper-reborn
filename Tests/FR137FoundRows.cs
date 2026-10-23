using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Spectre.Console;
using Spectre.Console.Rendering;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 found rows: the spell learning screens wrap each description to 79 columns, and the
/// offline save editor toggles the Voidreaver Constitution marker.
/// </summary>
[Collection("SharedGameSingletons")]
public class FR137FoundRows
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;

    // ---------------- the spell learning screens ----------------

    private static (TerminalEmulator term, MemoryStream output) Term(string input)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
    }

    private static List<CharacterClass> SpellClasses() =>
        Enum.GetValues<CharacterClass>().Where(c => SpellSystem.HasSpells(new Character { Class = c })).ToList();

    private static Character Caster(CharacterClass cls, int level, bool known)
    {
        var p = new Character
        {
            Name1 = "fr137", Name2 = "Fr137", Class = cls, Level = level, HP = 500, MaxHP = 500, Mana = 5000, MaxMana = 5000,
            Intelligence = 100, Wisdom = 100, AI = CharacterAI.Human,
        };
        for (int i = 0; i < 9; i++) p.Quickbar[i] = null;
        var ensure = typeof(SpellLearningSystem).GetMethod("EnsureSpellSlot", SNP)!;
        foreach (var s in SpellSystem.GetAllSpellsForClass(cls))
        {
            ensure.Invoke(null, new object?[] { p, s.Level });
            p.Spell[s.Level - 1][0] = known;
        }
        return p;
    }

    private static async Task<string> Render(Character player, string lang, bool screenReader = false)
    {
        var prevLang = GameConfig.Language;
        var prevSr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = screenReader;
            var (term, output) = Term("X\n");
            await SpellLearningSystem.ShowSpellLearningMenu(player, term);
            return Shown(term, output);
        }
        finally
        {
            GameConfig.Language = prevLang;
            GameConfig.ScreenReaderMode = prevSr;
        }
    }

    /// <summary>The screens that between them show every spell in every row form: on the quickbar
    /// (nine at a time), known and unequipped, learnable, and locked.</summary>
    private static IEnumerable<(string form, Character player)> Screens(CharacterClass cls)
    {
        var spells = SpellSystem.GetAllSpellsForClass(cls).OrderBy(s => s.Level).ToList();
        for (int start = 0; start < spells.Count; start += 9)
        {
            var p = Caster(cls, 100, known: true);
            for (int i = 0; i < 9 && start + i < spells.Count; i++) p.Quickbar[i] = $"spell:{spells[start + i].Level}";
            yield return ($"quickbar from {start}", p);
        }
        yield return ("known", Caster(cls, 100, known: true));
        yield return ("learnable", Caster(cls, 100, known: false));
        yield return ("locked", Caster(cls, 1, known: false));
    }

    private static string Words(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task FR137FoundRows_SpellLearningRows_FitIn79Columns_ForEverySpell(string lang)
    {
        var classes = SpellClasses();
        classes.Should().Contain(new[] { CharacterClass.Cleric, CharacterClass.Magician, CharacterClass.Sage, CharacterClass.Voidreaver });
        foreach (var cls in classes)
        {
            foreach (var (form, player) in Screens(cls))
            {
                string text = await Render(player, lang);
                var rows = text.Replace("\r", "").Split('\n');
                foreach (var row in rows)
                    row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"{lang} {cls} {form}: \"{row}\"");

                // every description the screen shows is there in full, its words in order across its rows
                string flat = Words(text);
                foreach (var spell in SpellSystem.GetAllSpellsForClass(cls))
                {
                    string desc = Words(WithLanguage(lang, () => spell.DisplayDescription));
                    if (form == "locked" && player.Level >= SpellSystem.GetLevelRequired(cls, spell.Level)) continue;
                    if (form.StartsWith("quickbar") && !player.Quickbar.Contains($"spell:{spell.Level}")) continue;
                    flat.Should().Contain(desc, $"{lang} {cls} {form} spell {spell.Level}");
                }
            }
        }
    }

    private static T WithLanguage<T>(string lang, Func<T> body)
    {
        var prev = GameConfig.Language;
        try { GameConfig.Language = lang; return body(); }
        finally { GameConfig.Language = prev; }
    }

    [Fact]
    public async Task FR137FoundRows_SpellLearningRows_ScreenReaderMode_PrintsTheSameWrappedRows()
    {
        foreach (var cls in new[] { CharacterClass.Cleric, CharacterClass.Sage })
        {
            var visual = await Render(Caster(cls, 100, known: false), "hu");
            var reader = await Render(Caster(cls, 100, known: false), "hu", screenReader: true);
            string Spells(string t) => string.Join("\n", t.Replace("\r", "").Split('\n')
                .Where(r => !r.Contains(Loc.GetIn("hu", "spell_learning.menu_sr")) && !r.Contains(Loc.GetIn("hu", "spell_learning.menu_visual"))));
            foreach (var row in reader.Replace("\r", "").Split('\n'))
                row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"screen reader {cls}: \"{row}\"");
            reader.Should().Contain(Loc.GetIn("hu", "spell_learning.menu_sr"));
            Spells(reader).Should().Contain(Spells(visual).Split('\n').First(r => r.Contains("[L1 ]")),
                "the spell rows have no separate screen reader form");
        }
    }

    private static List<string> DescriptionRows(int prefixWidth, string text, int rowIndent)
    {
        var m = typeof(SpellLearningSystem).GetMethod("DescriptionRows", SNP);
        m.Should().NotBeNull("the spell rows wrap through one row builder");
        return (List<string>)m!.Invoke(null, new object?[] { prefixWidth, text, rowIndent })!;
    }

    [Fact]
    public void FR137FoundRows_DescriptionRows_HangUnderTheDescription_OrUnderTheName()
    {
        string text = string.Join(" ", Enumerable.Repeat("word", 40));

        // a prefix of 40 leaves 39 columns: the later rows start at column 40, under the description
        var near = DescriptionRows(40, text, 6);
        near.Count.Should().BeGreaterThan(1);
        (40 + near[0].Length).Should().BeLessOrEqualTo(79);
        foreach (var row in near.Skip(1))
        {
            row.Should().StartWith(new string(' ', 40)).And.NotStartWith(new string(' ', 41));
            row.Length.Should().BeLessOrEqualTo(79);
        }

        // a prefix of 55 leaves 24 columns: the later rows start under the spell name
        var far = DescriptionRows(55, text, 7);
        (55 + far[0].Length).Should().BeLessOrEqualTo(79);
        foreach (var row in far.Skip(1))
        {
            row.Should().StartWith(new string(' ', 7)).And.NotStartWith(new string(' ', 8));
            row.Length.Should().BeLessOrEqualTo(79);
        }
        Words(string.Join(" ", far)).Should().Be(text);

        // a description that fits stays on its row
        DescriptionRows(40, "Heals a little.", 6).Should().Equal("Heals a little.");
    }

    // ---------------- the save editor toggle ----------------

    private sealed class ScriptedKeys : IAnsiConsoleInput
    {
        private readonly Queue<char> _keys;
        public ScriptedKeys(string text) { _keys = new Queue<char>(text); }
        public bool IsKeyAvailable() => _keys.Count > 0;
        public ConsoleKeyInfo? ReadKey(bool intercept)
        {
            if (_keys.Count == 0) throw new InvalidOperationException("scripted input ran out");
            char c = _keys.Dequeue();
            return c == '\n' ? new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
                             : new ConsoleKeyInfo(c, ConsoleKey.A, false, false, false);
        }
        public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) => Task.FromResult(ReadKey(intercept));
    }

    private sealed class ScriptedConsole : IAnsiConsole
    {
        private readonly IAnsiConsole _inner;
        public ScriptedConsole(IAnsiConsole inner, IAnsiConsoleInput input) { _inner = inner; Input = input; }
        public Profile Profile => _inner.Profile;
        public IAnsiConsoleCursor Cursor => _inner.Cursor;
        public IAnsiConsoleInput Input { get; }
        public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;
        public RenderPipeline Pipeline => _inner.Pipeline;
        public void Clear(bool home) => _inner.Clear(home);
        public void Write(IRenderable renderable) => _inner.Write(renderable);
    }

    private static readonly Type Editor = typeof(GameEngine).Assembly.GetType("UsurperRemake.Editor.PlayerSaveEditor", throwOnError: true)!;

    /// <summary>Runs editor code against scripted keys and returns what it printed.</summary>
    private static string WithKeys(string keys, Action body)
    {
        var writer = new StringWriter();
        var inner = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
        });
        var old = AnsiConsole.Console;
        AnsiConsole.Console = new ScriptedConsole(inner, new ScriptedKeys(keys));
        try { body(); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        finally { AnsiConsole.Console = old; }
        return writer.ToString();
    }

    /// <summary>Picks the Voidreaver marker row of Status and Cleanup, saves the file and reads it back.</summary>
    private static (bool marker, string shown) ToggleSaveAndReadBack(bool start)
    {
        Console.IsInputRedirected.Should().BeTrue("the editor menu reads a typed number when stdin is not a terminal");
        var data = new SaveGameData { Player = new PlayerData { Name1 = "fr137", Name2 = "Fr137", Class = CharacterClass.Voidreaver, Level = 20 } };
        data.Player.VoidreaverConstitutionApplied = start;

        var options = (JsonSerializerOptions)Editor.GetField("GameSaveJsonOptions", SNP)!.GetValue(null)!;
        string dir = Path.Combine(Path.GetTempPath(), "fr137-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "fr137.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(data, options));
            string shown = WithKeys("12\n\n0\n", () => Editor.GetMethod("EditStatusAndCleanup", SNP)!.Invoke(null, new object[] { data.Player }));
            bool saved = false;
            WithKeys("\n", () => saved = (bool)Editor.GetMethod("SaveBack", SNP)!.Invoke(null, new object[] { path, data })!);
            saved.Should().BeTrue();
            var back = JsonSerializer.Deserialize<SaveGameData>(File.ReadAllText(path), options)!;
            return (back.Player.VoidreaverConstitutionApplied, shown);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static void RowFits(string shown)
    {
        var rows = shown.Replace("\r", "").Split('\n').Where(r => r.Contains("(VoidreaverConstitutionApplied=")).ToList();
        rows.Should().HaveCount(2, "the menu is shown before and after the toggle");
        foreach (var row in rows)
        {
            row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"\"{row}\"");
            row.Trim().Should().StartWith("12 ", "the row after the NG+ cycle marker toggle");
        }
    }

    [Fact]
    public void FR137FoundRows_SaveEditor_TogglesTheVoidreaverMarker_OffToOn()
    {
        var (marker, shown) = ToggleSaveAndReadBack(start: false);
        RowFits(shown);
        shown.Should().Contain("VoidreaverConstitutionApplied=False");
        shown.Should().Contain("VoidreaverConstitutionApplied = True.");
        marker.Should().BeTrue("the toggle is written to the save file and read back");
    }

    [Fact]
    public void FR137FoundRows_SaveEditor_TogglesTheVoidreaverMarker_OnToOff()
    {
        var (marker, shown) = ToggleSaveAndReadBack(start: true);
        RowFits(shown);
        shown.Should().Contain("VoidreaverConstitutionApplied=True");
        shown.Should().Contain("VoidreaverConstitutionApplied = False.");
        marker.Should().BeFalse("the toggle is written to the save file and read back");
    }
}
