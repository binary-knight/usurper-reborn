using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;
using Xunit.Abstractions;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8, the room fight's mixed encounter line ("You face 1 A, 1 B, 1 C and 1 D!"): it was written piece by
/// piece with no wrapping and passed 79 columns with four or five long names on the deep floors. It is now
/// one row written through UIHelper.WriteRow. Row 1: the worst case wraps and no row passes 79 columns.
/// Row 2: a line that fits reads exactly as before. The other three lines of the block are checked and
/// left as they are.
/// </summary>
[Collection("SharedGameSingletons")]
public class EncounterLine128Tests
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] Affixes = { "frenzied", "warded", "venomous", "commanding", "mending", "swift" };
    private readonly ITestOutputHelper _out;
    public EncounterLine128Tests(ITestOutputHelper output) { _out = output; }

    /// <summary>The English plural of the room fight (DungeonLocation.GetPluralName), enough for these names.</summary>
    private static string Plural(string name) => name.EndsWith("f") ? name.Substring(0, name.Length - 1) + "ves" : name + "s";

    private sealed class NoInput : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>What a terminal session receives for the given writes, as text.</summary>
    private static string Sent(Action<TerminalEmulator> write)
    {
        var output = new MemoryStream();
        var terminal = new TerminalEmulator(new NoInput(), output);
        write(terminal);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>The rows of a transcript as the player reads them: colour codes off, line ends split.</summary>
    private static List<string> Rows(string sent)
    {
        var rows = UIHelper.StripAnsi(sent).Replace("\r\n", "\n").Split('\n').ToList();
        if (rows.Count > 0 && rows[rows.Count - 1] == "") rows.RemoveAt(rows.Count - 1);
        return rows;
    }

    /// <summary>The line as the room fight wrote it at 3a8dd9dc: piece by piece, no wrapping.</summary>
    private static void WriteAsBefore(TerminalEmulator terminal, List<Monster> monsters)
    {
        var monsterGroups = monsters.GroupBy(m => m.Name).Select(g => new { Count = g.Count(), Sample = g.First() }).ToList();
        terminal.Write(Loc.Get("dungeon.you_face_prefix"));
        for (int i = 0; i < monsterGroups.Count; i++)
        {
            var group = monsterGroups[i];
            string plural = MonsterNames.Count(group.Count, group.Sample, Plural);
            if (i > 0 && i == monsterGroups.Count - 1)
                terminal.Write(Loc.Get("dungeon.and_separator"));
            else if (i > 0)
                terminal.Write(", ");
            terminal.Write($"{group.Count} {plural}");
        }
        terminal.WriteLine("!");
    }

    private static Monster Plain(string tier) => new Monster { Name = tier, TierName = tier };

    /// <summary>An elite named as MonsterGenerator.PromoteElite names it: the first affix is the outermost word.</summary>
    private static Monster Elite(string member, string tier, params string[] affixes)
    {
        var m = new Monster { Name = member, TierName = tier, IsElite = true };
        m.EliteAffixes.AddRange(affixes);
        for (int i = affixes.Length - 1; i >= 0; i--) m.Name = MonsterNames.FromKey("monster.elite." + affixes[i], m.Name);
        return m;
    }

    private static IEnumerable<(string Tier, bool Caster)> Tiers() =>
        MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Select(t => (t.Name, MonsterGenerator.CasterSpells.ContainsKey(f.FamilyName))));

    /// <summary>The elite with the longest name a reader of lang can meet: two affixes (from floor 51) on the
    /// caster of the longest tier of a family that casts.</summary>
    private static Monster LongestElite(string lang)
    {
        string tier = Tiers().Where(t => t.Caster).OrderByDescending(t => MonsterNames.DisplayIn(lang, t.Tier).Length).First().Tier;
        var affixes = Affixes.OrderByDescending(a => Loc.GetIn(lang, "monster.elite." + a, "").Length).Take(2).ToArray();
        return Elite(MonsterNames.FromKey("monster.role.caster", tier), tier, affixes);
    }

    /// <summary>Five different members: the longest elite and the four longest tier names in lang.</summary>
    private static List<Monster> WorstGroup(string lang)
    {
        var elite = LongestElite(lang);
        var group = new List<Monster> { elite };
        group.AddRange(Tiers().Select(t => t.Tier).Distinct().Where(t => t != elite.TierName)
            .OrderByDescending(t => MonsterNames.DisplayIn(lang, t).Length).Take(4).Select(Plain));
        return group;
    }

    private static T In<T>(string lang, Func<T> read)
    {
        string saved = GameConfig.Language;
        try { GameConfig.Language = lang; return read(); }
        finally { GameConfig.Language = saved; }
    }

    // ---------------------------------------------------------------- row 1

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void Row1_TheWorstCaseLine_IsWrapped_AndNoRowPasses79Columns(string lang)
    {
        UIHelper.WrapWidth.Should().Be(79);
        var group = WorstGroup(lang);
        group.Select(m => m.Name).Distinct().Should().HaveCount(5);
        var (line, sent) = In(lang, () => (DungeonLocation.MixedEncounterLine(group, Plural),
            Sent(t => { t.SetColor("yellow"); DungeonLocation.WriteMixedEncounterLine(t, group, Plural); })));
        _out.WriteLine($"{lang} {line.Length}: {line}");
        line.Length.Should().BeGreaterThan(79, "the worst case does not fit one row in {0}", lang);
        line.Should().Contain(MonsterNames.DisplayIn(lang, group[0])).And.NotContain("[").And.NotContain("\u001b");

        var rows = Rows(sent);
        rows.Count.Should().BeGreaterThan(1);
        foreach (string row in rows)
            row.Length.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row);
        string.Join(" ", rows).Should().Be(line, "wrapping keeps every word, in order");
        // every row in the colour the room fight sets, as a plain WriteLine of that row sends it
        sent.Should().Be(Sent(t => { t.SetColor("yellow"); foreach (string row in rows) t.WriteLine(row); }));
    }

    [Fact]
    public void Row1_TheRoomFight_WritesTheMixedLine_ThroughTheWrappingWriter()
    {
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        src.Should().Contain("WriteMixedEncounterLine(terminal, monsters, GetPluralName);");
        src.Should().NotContain("terminal.Write(Loc.Get(\"dungeon.you_face_prefix\"));", "the line is no longer written piece by piece");
        System.Text.RegularExpressions.Regex.Matches(src, "\"dungeon\\.you_face_prefix\"").Count.Should().Be(1, "one builder holds the key");
    }

    // ---------------------------------------------------------------- row 2

    [Fact]
    public void Row2_ALineThatFits_IsTheSameRow_AsBefore()
    {
        var group = new List<Monster> { Plain("Wolf"), Plain("Goblin"), Plain("Wolf"), Plain("Goblin"), Plain("Goblin"), Elite(MonsterNames.FromKey("monster.role.caster", "Kobold"), "Kobold", "venomous") };
        var expected = new Dictionary<string, string>
        {
            ["en"] = "You face 2 Wolves, 3 Goblins and 1 Venomous Kobold Shaman!",
        };
        foreach (string lang in Langs)
        {
            var (line, sent, before) = In(lang, () => (DungeonLocation.MixedEncounterLine(group, Plural),
                Sent(t => { t.SetColor("yellow"); DungeonLocation.WriteMixedEncounterLine(t, group, Plural); }),
                Sent(t => { t.SetColor("yellow"); WriteAsBefore(t, group); })));
            line.Length.Should().BeLessThanOrEqualTo(79);
            if (expected.TryGetValue(lang, out string? text)) line.Should().Be(text);
            line.Should().StartWith(Loc.GetIn(lang, "dungeon.you_face_prefix")).And.Contain(", ").And.Contain(Loc.GetIn(lang, "dungeon.and_separator") + "1 ").And.EndWith("!");
            // one row, the text of the piece by piece line of 3a8dd9dc, byte for byte
            Rows(sent).Should().Equal(new[] { line });
            Encoding.UTF8.GetBytes(UIHelper.StripAnsi(sent)).Should().Equal(Encoding.UTF8.GetBytes(UIHelper.StripAnsi(before)), "{0}", lang);
            // and in the same colour: what one WriteLine of the row sends
            sent.Should().Be(Sent(t => { t.SetColor("yellow"); t.WriteLine(line); }));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Row2_TwoAndThreeNames_ReadAsBefore(string lang)
    {
        // two names: "and" only; three names: one comma, then "and"
        foreach (var group in new[]
        {
            new List<Monster> { Plain("Wolf"), Plain("Goblin") },
            new List<Monster> { Plain("Wolf"), Plain("Goblin"), Plain("Kobold") },
        })
        {
            var (line, sent, before) = In(lang, () => (DungeonLocation.MixedEncounterLine(group, Plural),
                Sent(t => DungeonLocation.WriteMixedEncounterLine(t, group, Plural)), Sent(t => WriteAsBefore(t, group))));
            string and = Loc.GetIn(lang, "dungeon.and_separator");
            string Shown(int i) => "1 " + MonsterNames.DisplayIn(lang, group[i]);
            line.Should().Be(Loc.GetIn(lang, "dungeon.you_face_prefix") + (group.Count == 2 ? Shown(0) + and + Shown(1) : Shown(0) + ", " + Shown(1) + and + Shown(2)) + "!");
            UIHelper.StripAnsi(sent).Should().Be(UIHelper.StripAnsi(before));
        }
    }

    // ---------------------------------------------------------------- the three other lines of the block

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void TheOtherLinesOfTheBlock_Fit79Columns_AtTheirWorst(string lang)
    {
        var elite = LongestElite(lang);
        // one family: five of the longest count name
        string family = In(lang, () => Tiers().Select(t => Loc.Get("dungeon.face_monsters", 5, MonsterNames.CountIn(lang, 5, t.Tier, Plural)))
            .OrderByDescending(r => r.Length).First());
        // one monster: the longest elite, alone
        string single = In(lang, () => Loc.Get("dungeon.monster_attacks", GameConfig.ArticulateForLanguage(MonsterNames.Display(elite))));
        _out.WriteLine($"{lang} face_monsters {family.Length}: {family}");
        _out.WriteLine($"{lang} monster_attacks {single.Length}: {single}");
        family.Length.Should().BeLessThanOrEqualTo(79);
        single.Length.Should().BeLessThanOrEqualTo(79);

        // the followers' broadcast: the summary of the room fight in the follower's language, through FollowerMessage
        var group = WorstGroup(lang);
        string summary = string.Join(", ", group.Select(m => MonsterNames.DisplayIn(lang, m)));
        foreach (string message in new[]
        {
            $"\u001b[1;31m  *** {Loc.GetIn(lang, "dungeon.bc_boss_encounter", summary)} ***\u001b[0m",
            $"\u001b[1;33m  {Loc.GetIn(lang, "dungeon.bc_combat", summary)}\u001b[0m",
        })
        {
            UIHelper.VisibleLength(message).Should().BeGreaterThan(79);
            var rows = DungeonLocation.FollowerMessage(_ => message)(lang).Split('\n');
            _out.WriteLine($"{lang} broadcast {UIHelper.VisibleLength(message)} in {rows.Length} rows");
            rows.Length.Should().BeGreaterThan(1);
            foreach (string row in rows)
                UIHelper.VisibleLength(row).Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row);
        }
    }

    [Fact]
    public void TheFollowersBroadcast_OfTheRoomFight_GoesThroughTheWrappingBroadcast()
    {
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        src.Should().Contain("BroadcastDungeonEvent(lang => bossRoom");
        src.Should().Contain("GroupSystem.Instance!.BroadcastToAllGroupSessionsLocalized(group, FollowerMessage(buildMessage),");
    }

    // ================================================================ the wandering encounter (MonsterEncounter)
    // The same fault at the second site: "You encounter 1 A, 1 B and 1 C!" (dungeon.you_encounter), each group in
    // its own [colour]...[/] pair, written piece by piece with no wrapping.

    private static readonly System.Text.RegularExpressions.Regex Tag = new(@"\[(/|[a-z_]+)\]");
    private static readonly string[] Colours = { "bright_red", "green", "cyan", "magenta", "bright_blue" };

    /// <summary>The text of a row without its [colour] tags: what a client that renders the markup shows.</summary>
    private static string Visible(string row) => Tag.Replace(row, "");

    private static List<Monster> Coloured(List<Monster> group)
    {
        var names = group.Select(m => m.Name).Distinct().ToList();
        foreach (var m in group) m.MonsterColor = Colours[names.IndexOf(m.Name)];
        return group;
    }

    /// <summary>The wandering line as MonsterEncounter wrote it at 3a8dd9dc, up to the row end its caller writes.</summary>
    private static void WriteWanderingAsBefore(TerminalEmulator terminal, List<Monster> monsters)
    {
        var monsterGroups = monsters.GroupBy(m => m.Name).Select(g => new { Count = g.Count(), Color = g.First().MonsterColor, Sample = g.First() }).ToList();
        terminal.Write(Loc.Get("dungeon.you_encounter"));
        for (int i = 0; i < monsterGroups.Count; i++)
        {
            var group = monsterGroups[i];
            string plural = MonsterNames.Count(group.Count, group.Sample, Plural);
            if (i > 0 && i == monsterGroups.Count - 1)
                terminal.Write(Loc.Get("dungeon.and_separator"));
            else if (i > 0)
                terminal.Write(", ");
            terminal.Write($"[{group.Color}]{group.Count} {plural}[/]");
        }
        terminal.Write("!");
        terminal.WriteLine("");
    }

    /// <summary>The wandering line as MonsterEncounter writes it now, with the row end of its caller.</summary>
    private static string SentWandering(List<Monster> group) =>
        Sent(t => { t.SetColor("yellow"); DungeonLocation.WriteWanderingEncounterLine(t, group, Plural); t.WriteLine(""); });

    /// <summary>The words of the line with no markup, as one row.</summary>
    private static string WanderingText(string lang, List<Monster> group)
    {
        var shown = group.GroupBy(m => m.Name).Select(g => $"{g.Count()} {MonsterNames.CountIn(lang, g.Count(), g.First(), Plural)}").ToList();
        return Loc.GetIn(lang, "dungeon.you_encounter") + string.Join(", ", shown.Take(shown.Count - 1))
            + Loc.GetIn(lang, "dungeon.and_separator") + shown[shown.Count - 1] + "!";
    }

    /// <summary>The rows the stream terminal sends (it sends the tags of a Write as they are), and for each
    /// colour the text inside its pairs, row after row.</summary>
    private static (List<string> Rows, Dictionary<string, List<string>> Inside) WanderingRows(string sent, string lang)
    {
        var rows = Rows(sent);
        var inside = new Dictionary<string, List<string>>();
        foreach (string row in rows)
        {
            string? open = null;
            int from = 0;
            foreach (System.Text.RegularExpressions.Match tag in Tag.Matches(row))
            {
                if (tag.Value == "[/]")
                {
                    open.Should().NotBeNull("{0}: a closing tag with no pair open in [{1}]", lang, row);
                    if (!inside.TryGetValue(open!, out var texts)) inside[open!] = texts = new List<string>();
                    texts.Add(row.Substring(from, tag.Index - from));
                    open = null;
                }
                else
                {
                    open.Should().BeNull("{0}: a pair opened inside a pair in [{1}]", lang, row);
                    open = tag.Value.Trim('[', ']');
                    from = tag.Index + tag.Length;
                }
            }
            open.Should().BeNull("{0}: every pair opened in a row is closed in that row: [{1}]", lang, row);
        }
        return (rows, inside);
    }

    // ---------------------------------------------------------------- row 3: the wandering line wraps

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void Row3_TheWorstCaseWanderingLine_IsWrapped_NoRowPasses79_AndEveryRowIsBalanced(string lang)
    {
        var group = Coloured(WorstGroup(lang));
        string text = WanderingText(lang, group);
        text.Length.Should().BeGreaterThan(79, "the worst case does not fit one row in {0}", lang);
        string sent = In(lang, () => SentWandering(group));
        var (rows, inside) = WanderingRows(sent, lang);
        _out.WriteLine($"{lang} {text.Length} in {rows.Count} rows:");
        foreach (string row in rows) _out.WriteLine($"  {Visible(row).Length,2} {row}");

        rows.Count.Should().BeGreaterThan(1);
        foreach (string row in rows)
            Visible(row).Length.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row);
        string.Join(" ", rows.Select(Visible)).Should().Be(text, "wrapping keeps every word, in order");
        // every group reads whole in its own colour, and nothing else is coloured
        inside.Keys.Should().BeEquivalentTo(group.Select(m => m.MonsterColor));
        foreach (var m in group)
            string.Join(" ", inside[m.MonsterColor]).Should().Be("1 " + MonsterNames.DisplayIn(lang, m), "{0}", lang);
        // no markup beyond the pairs of the groups: a group cut by a row end has one pair on each row
        Tag.Matches(sent).Count.Should().Be(2 * inside.Values.Sum(t => t.Count));
        UIHelper.StripAnsi(sent).Should().NotContain("\u001b");
    }

    [Fact]
    public void Row3_ARowEndInsideAGroup_GivesAWholePairOnEachRow()
    {
        // 79 columns end inside "1 Commanding Frenzied Goblin Champion Shaman": the pair is not left open
        var elite = Elite(MonsterNames.FromKey("monster.role.caster", "Goblin Champion"), "Goblin Champion", "commanding", "frenzied");
        var group = Coloured(new List<Monster> { Plain("Ancient Construct"), Plain("Adamantine Golem"), Plain("Gelatinous Cube"), elite });
        var rows = In("en", () => DungeonLocation.WanderingEncounterRows(group, Plural));
        rows.Select(r => string.Concat(r)).Should().Equal(
            "You encounter [bright_red]1 Ancient Construct[/], [green]1 Adamantine Golem[/], [cyan]1 Gelatinous Cube[/] and [magenta]1[/]",
            "[magenta]Commanding Frenzied Goblin Champion Shaman[/]!");
        var (_, inside) = WanderingRows(In("en", () => SentWandering(group)), "en");
        inside["magenta"].Should().Equal("1", "Commanding Frenzied Goblin Champion Shaman");
    }

    [Fact]
    public void Row3_MonsterEncounter_WritesTheMixedLine_ThroughTheWrappingWriter()
    {
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        src.Should().Contain("WriteWanderingEncounterLine(terminal, monsters, GetPluralName);");
        src.Should().NotContain("terminal.Write(Loc.Get(\"dungeon.you_encounter\"));", "the line is no longer written with no wrapping");
        System.Text.RegularExpressions.Regex.Matches(src, "\"dungeon\\.you_encounter\"").Count.Should().Be(1, "one builder holds the key");
        src.Should().Contain("var wrapped = UsurperRemake.UI.UIHelper.MessageRows(plain);");
    }

    // ---------------------------------------------------------------- row 4: a wandering line that fits

    [Fact]
    public void Row4_AWanderingLineThatFits_IsSentExactly_AsBefore()
    {
        var group = Coloured(new List<Monster> { Plain("Wolf"), Plain("Goblin"), Plain("Wolf"), Plain("Goblin"), Plain("Goblin"), Elite(MonsterNames.FromKey("monster.role.caster", "Kobold"), "Kobold", "venomous") });
        foreach (string lang in Langs)
        {
            var (sent, before, rows) = In(lang, () => (SentWandering(group), Sent(t => { t.SetColor("yellow"); WriteWanderingAsBefore(t, group); }),
                DungeonLocation.WanderingEncounterRows(group, Plural)));
            WanderingText(lang, group).Length.Should().BeLessThanOrEqualTo(79);
            // the same writes in the same order: what the terminal receives is the same, colour codes and all
            Encoding.UTF8.GetBytes(sent).Should().Equal(Encoding.UTF8.GetBytes(before), "{0}", lang);
            rows.Should().ContainSingle();
            Visible(string.Concat(rows[0])).Should().Be(WanderingText(lang, group));
            Rows(sent).Should().HaveCount(1, "one row and no blank row after it, as before");
            if (lang == "en")
                string.Concat(rows[0]).Should().Be("You encounter [bright_red]2 Wolves[/], [green]3 Goblins[/] and [cyan]1 Venomous Kobold Shaman[/]!");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Row4_TwoAndThreeNames_AreSentAsBefore(string lang)
    {
        foreach (var group in new[]
        {
            Coloured(new List<Monster> { Plain("Wolf"), Plain("Goblin") }),
            Coloured(new List<Monster> { Plain("Wolf"), Plain("Goblin"), Plain("Kobold") }),
        })
        {
            var (sent, before) = In(lang, () => (SentWandering(group), Sent(t => { t.SetColor("yellow"); WriteWanderingAsBefore(t, group); })));
            sent.Should().Be(before);
            Visible(Rows(sent).Single()).Should().Be(WanderingText(lang, group));
        }
    }

    // ---------------------------------------------------------------- measured, not changed

    private static IEnumerable<string> BossNames()
    {
        var pool = typeof(DungeonLocation).GetField("BossNamePool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        foreach (var names in ((System.Collections.IDictionary)pool).Values)
            foreach (string name in (string[])names!) yield return name;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void TheFamilyLine_AndABossAlone_Fit79Columns_AtTheirWorst(string lang)
    {
        BossNames().Should().HaveCount(40);
        string Widest(IEnumerable<string> rows) => rows.OrderByDescending(r => Visible(r).Length).First();
        var measured = In(lang, () =>
        {
            // one family of the wandering encounter: five of a tier, then its family
            string family = Widest(MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Select(t =>
                Loc.Get("dungeon.encounter_group_family", $"[white]5 {MonsterNames.CountIn(lang, 5, t.Name, Plural)}[/]", MonsterNames.Family(f.FamilyName)))));
            // a boss room's boss alone (the room fight), the longest boss name
            string bossAttacks = Widest(BossNames().Select(b => Loc.Get("dungeon.monster_attacks", GameConfig.ArticulateForLanguage(MonsterNames.Display(b)))));
            // a boss alone in the wandering encounter, both of its lines
            string BossWithArticle(string b) => lang == "en"
                ? $"{GameConfig.GetIndefiniteArticle(MonsterNames.Display(b))} {Loc.Get("dungeon.boss_powerful")} [white]{MonsterNames.Display(b)}[/]"
                : $"[white]{MonsterNames.Display(b)}[/]";
            string bossVisual = Widest(BossNames().Select(b => Loc.Get("dungeon.boss_blocks_path_visual", BossWithArticle(b))));
            string bossSr = Widest(BossNames().Select(b => Loc.Get("dungeon.boss_blocks_path_sr", BossWithArticle(b))));
            // one monster of the wandering encounter: the longest elite
            var elite = LongestElite(lang);
            string appears = Loc.Get("dungeon.monster_appears", lang == "en"
                ? $"{GameConfig.GetIndefiniteArticle(elite.Name)} [white]{elite.Name}[/]" : $"[white]{MonsterNames.Display(elite)}[/]");
            return new[] { ("encounter_group_family", family), ("monster_attacks (boss)", bossAttacks), ("boss_blocks_path_visual", bossVisual),
                ("boss_blocks_path_sr", bossSr), ("monster_appears (elite)", appears) };
        });
        foreach (var (name, row) in measured)
        {
            _out.WriteLine($"{lang} {name} {Visible(row).Length}: {Visible(row)}");
            Visible(row).Length.Should().BeLessThanOrEqualTo(79, "{0} {1}", lang, name);
        }
    }
}
