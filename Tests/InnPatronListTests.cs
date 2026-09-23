using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: the Inn's patron list stopped at the first eight, in an order that never changes, so on
/// a busy night the rest could not be spoken to, including the target of a contract (player
/// report). Every patron must be reachable: by page, by a number that counts across pages, and by
/// name; and the targets of the player's own contracts come first.
/// </summary>
[Collection("SharedGameSingletons")]
public class InnPatronListTests
{
    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data; private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos); Array.Copy(_data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => _data.Length; public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Puts <paramref name="count"/> patrons in the Inn, runs the patron screen on a script, returns what it printed.</summary>
    private static async Task<string> Patrons(int count, string script, string? contractOn = null, bool distinctLoginNames = false)
    {
        var hero = new Character { Name1 = "patron_tester", Name2 = "Patron Tester", Class = CharacterClass.Warrior, Level = 20, HP = 300, MaxHP = 300 };
        var patrons = Enumerable.Range(1, count)
            .Select(i => new NPC { ID = $"npc_patron_{i}", Name1 = distinctLoginNames ? $"patron_key_{i}" : $"Patron {i:00}", Name2 = $"Patron {i:00}", Level = 20, HP = 100, MaxHP = 100, CurrentLocation = "Inn" })
            .ToList();
        var spawner = NPCSpawnSystem.Instance;
        foreach (var p in patrons) spawner.ActiveNPCs.Add(p);
        Quest? contract = null;
        if (contractOn != null)
        {
            contract = new Quest { Id = "inn_patron_test_contract", Occupier = hero.Name2, TargetNPCName = contractOn };
            QuestSystem.AddQuestToDatabase(contract);
        }
        try
        {
            var inn = new InnLocation();
            var output = new MemoryStream();
            var term = new TerminalEmulator(new ScriptedStream(script), output);
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(inn, term);
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(inn, hero);
            await (Task)typeof(InnLocation).GetMethod("TalkToPatrons", F)!.Invoke(inn, null)!;
            term.StreamWriterInternal!.Flush();
            return new System.Text.RegularExpressions.Regex("\u001b\\[[0-9;]*[A-Za-z]").Replace(Encoding.UTF8.GetString(output.ToArray()), "");
        }
        finally
        {
            foreach (var p in patrons) spawner.ActiveNPCs.Remove(p);
            if (contract != null) contract.Deleted = true;
        }
    }

    private static string TalkingTo(string name) => Loc.Get("inn.interacting_with", name);

    [Fact]
    public async Task ThePatronPastTheEighth_CanBeChosenByNumber()
    {
        var shown = await Patrons(18, "15\n0\n");
        shown.Should().Contain(TalkingTo("Patron 15"), "patron 15 used to be beyond reach");
    }

    [Fact]
    public async Task TheList_Pages_AndTheSecondPageHoldsTheRest()
    {
        var first = await Patrons(18, "0\n");
        first.Should().Contain("[10] Patron 10").And.NotContain("[11]");
        first.Should().Contain("(18)", "the header counts everyone present, not the eight shown");

        var second = await Patrons(18, "N\n0\n");
        second.Should().Contain("[11] Patron 11").And.Contain("[18] Patron 18");
    }

    [Fact]
    public async Task APatron_CanBeFoundByName()
    {
        var shown = await Patrons(18, "patron 17\n0\n");
        shown.Should().Contain(TalkingTo("Patron 17"));
    }

    [Fact]
    public async Task APartOfANameThatFitsSeveral_ListsOnlyThem()
    {
        var shown = await Patrons(18, "Patron 1\n0\n");
        shown.Should().Contain(Loc.Get("inn.patrons_matching", "Patron 1"));
    }

    [Fact]
    public async Task TheTargetOfYourContract_IsListedFirst_AndMarked()
    {
        // a contract names its target by display name (Character.Name is Name2); the login-style
        // Name1 differs here so the test knows which field it guards
        var shown = await Patrons(18, "0\n", contractOn: "Patron 17", distinctLoginNames: true);
        shown.Should().Contain($"[1] Patron 17").And.Contain(Loc.Get("inn.patrons_contract"));
    }

    [Fact]
    public async Task AnEmptyLine_ClearsANameFilter_BeforeItLeaves()
    {
        // "Patron 1" lists the nine from 10 to 18; an empty line brings back everyone; a second leaves
        var shown = await Patrons(18, "Patron 1\n\n\n");
        string header = Loc.Get("inn.patrons_matching", "Patron 1");
        shown.Should().Contain(header);
        string afterFilter = shown.Substring(shown.LastIndexOf(header) + header.Length);
        afterFilter.Should().Contain("[2] Patron 02", "the full list, which the filter had hidden, is shown again");
    }

    [Fact]
    public async Task ANumberPastTheEnd_SaysSo()
    {
        var shown = await Patrons(18, "40\n\n0\n");
        shown.Should().Contain(Loc.Get("inn.patrons_no_number", 40, 18));
    }

    [Fact]
    public async Task AFewPatrons_ShowNoPaging()
    {
        var shown = await Patrons(5, "0\n");
        shown.Should().Contain("[5] Patron 05").And.NotContain("Page 1/");
    }
}
