using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.13: equip moves take the listed item instance, the Home / Inn / Dungeon equip menus save each move at
/// once in the order for its direction, and the throne challenge reads a player king by its save key.
/// </summary>
[Collection("SharedGameSingletons")]
public class DeferredFixes1113Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Locations", file));

    private static string MethodBody(string file, string method)
    {
        string src = Source(file);
        var m = Regex.Match(src, @"(?:private|internal|public|protected)[^\n;=]*\b" + Regex.Escape(method) + @"\s*\(");
        m.Success.Should().BeTrue($"{file}: {method} must exist");
        int open = src.IndexOf('{', m.Index);
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException("unbalanced braces");
    }

    /// <summary>A location with a scripted terminal and the given player, for driving its private menus.</summary>
    private static T Rig<T>(T loc, Character hero, IEnumerable<string> lines) where T : BaseLocation
    {
        var term = new TerminalEmulator(new LineStream(lines), new MemoryStream());
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
        return loc;
    }

    private static Task Run(object loc, string method, params object[] args) =>
        (Task)loc.GetType().GetMethod(method, F)!.Invoke(loc, args)!;

    private static Item TwinRing(int wis) =>
        new Item { Name = "Test Twin Ring", Type = ObjType.Fingers, Wisdom = wis, IsIdentified = true };

    // ---------- 1. the chosen instance ----------

    [Fact]
    public async Task EquipBest_TwoSameNamedRings_GivesTheStrongerOne_AndKeepsTheWeakerInThePack()
    {
        // v1.1.13: the +2 is first in the pack; the +20 is scored best, and the +2 was the one removed
        var worn = new Equipment { Name = "Test Other Ring +10", Slot = EquipmentSlot.LFinger, WisdomBonus = 10, MinLevel = 1 };
        EquipmentDatabase.RegisterDynamic(worn);
        var npc = TeamCornerRig.Npc("npc-twin-best", "Twinner", "");
        npc.EquipItem(worn, EquipmentSlot.RFinger, out _).Should().BeTrue();
        var hero = TeamCornerRig.Hero();
        var weak = TwinRing(2);
        var strong = TwinRing(20);
        hero.Inventory.Add(weak);
        hero.Inventory.Add(strong);

        BaseLocation.GearSaveHookForTests = _ => Task.CompletedTask;
        try { await new TeamCornerRig(hero, new[] { "Y" }).Run("RunEquipBestGear", npc); }
        finally { BaseLocation.GearSaveHookForTests = null; }

        npc.GetEquipment(EquipmentSlot.LFinger)!.WisdomBonus.Should().Be(20);
        npc.GetEquipment(EquipmentSlot.RFinger)!.WisdomBonus.Should().Be(10, "the +2 is no upgrade on the +10");
        hero.Inventory.Should().ContainSingle().Which.Should().BeSameAs(weak);
    }

    [Fact]
    public async Task HomeEquip_TwoSameNamedRings_TakesTheOnePicked()
    {
        // v1.1.13: rings have no attack or armor, so the name-and-power match removed the first twin
        var npc = TeamCornerRig.Npc("npc-twin-home", "Homer", "");
        var hero = TeamCornerRig.Hero();
        var weak = TwinRing(2);
        var strong = TwinRing(20);
        hero.Inventory.Add(weak);
        hero.Inventory.Add(strong);
        var order = new List<string>();
        BaseLocation.GearSaveHookForTests = step => { order.Add(step); return Task.CompletedTask; };
        try
        {
            var home = Rig(new HomeLocation(), hero, new[] { "13", "2", "Q" });   // left ring, the second (+20), done
            await Run(home, "EquipItemToCharacter", npc);
        }
        finally { BaseLocation.GearSaveHookForTests = null; }

        npc.GetEquipment(EquipmentSlot.LFinger)!.WisdomBonus.Should().Be(20);
        hero.Inventory.Should().ContainSingle().Which.Should().BeSameAs(weak);
        order.Should().Equal(new[] { "player", "shared" }, "a give saves the player first");
    }

    [Fact]
    public void TakeFromPlayerForEquip_RemovesTheListedInstance()
    {
        var hero = TeamCornerRig.Hero();
        var weak = TwinRing(2);
        var strong = TwinRing(20);
        hero.Inventory.Add(weak);
        hero.Inventory.Add(strong);
        var rig = new TeamCornerRig(hero, Array.Empty<string>());
        rig.Loc.TakeFromPlayerForEquip(new Equipment { Name = "Test Twin Ring" }, false, null, strong).Should().BeTrue();
        hero.Inventory.Should().ContainSingle().Which.Should().BeSameAs(weak);
        rig.Loc.TakeFromPlayerForEquip(new Equipment { Name = "Test Twin Ring" }, false, null, strong).Should().BeFalse("it is gone");
    }

    [Fact]
    public void NoEquipMenu_FindsThePackItemByName()
    {
        foreach (var (file, method) in new[] { ("TeamCornerLocation.cs", "EquipItemToCharacter"), ("HomeLocation.cs", "EquipItemToCharacter"),
                                               ("InnLocation.cs", "CompanionEquipItemToCharacter"), ("DungeonLocation.cs", "DungeonEquipItemToMember") })
        {
            string body = MethodBody(file, method);
            body.Should().Contain("TakeFromPlayerForEquip(selectedItem, wasEquipped, sourceSlot, sourceItem)", file);
            body.Should().NotContain("i.Name == selectedItem.Name", file);
        }
        string src = Source("BaseLocation.cs");
        int start = src.IndexOf("protected async Task RunEquipBestGear(");
        src.Substring(start, src.IndexOf("protected static int ScoreEquipment(") - start).Should().NotContain(".Name == bestCandidate.item.Name");
    }

    // ---------- 3. the save order in the Home, Inn and Dungeon menus ----------

    [Theory]
    [InlineData("HomeLocation.cs", "EquipItemToCharacter")]
    [InlineData("InnLocation.cs", "CompanionEquipItemToCharacter")]
    [InlineData("DungeonLocation.cs", "DungeonEquipItemToMember")]
    public void EveryEquip_SavesTheGiveThenTheDisplacedTake(string file, string method)
    {
        string equip = MethodBody(file, method);
        int take = equip.IndexOf("if (!TakeFromPlayerForEquip(");
        int eq = equip.IndexOf("target.EquipItem(");
        int give = equip.IndexOf("await SaveGearGivenToNpc(target)");
        int back = equip.IndexOf("currentPlayer.Inventory.Add(displaced)");
        int taken = equip.IndexOf("await SaveGearTakenFromNpc(target)");
        take.Should().BeGreaterThan(0).And.BeLessThan(eq);
        give.Should().BeGreaterThan(eq);
        back.Should().BeGreaterThan(give);
        taken.Should().BeGreaterThan(back);
        equip.Should().NotContain("AutoSave(").And.NotContain("SaveAllSharedState");
    }

    [Theory]
    [InlineData("HomeLocation.cs", "UnequipItemFromCharacter")]
    [InlineData("HomeLocation.cs", "TakeAllEquipment")]
    [InlineData("InnLocation.cs", "CompanionUnequipItemFromCharacter")]
    [InlineData("InnLocation.cs", "CompanionTakeAllEquipment")]
    [InlineData("DungeonLocation.cs", "DungeonUnequipItemFromMember")]
    public void EveryTake_IsSavedAtOnce_NpcSideFirst(string file, string method)
    {
        string body = MethodBody(file, method);
        int move = body.LastIndexOf("currentPlayer.Inventory.Add(legacyItem)");
        int save = body.IndexOf("await SaveGearTakenFromNpc(target)");
        move.Should().BeGreaterThan(0);
        save.Should().BeGreaterThan(move);
        body.Substring(move, save - move).Should().NotContain("await ");
        body.Should().NotContain("AutoSave(").And.NotContain("SaveAllSharedState");
    }

    [Theory]
    [InlineData("HomeLocation.cs", "ManageCharacterEquipment")]
    [InlineData("HomeLocation.cs", "EquipPartner")]
    [InlineData("InnLocation.cs", "ManageCompanionCharacterEquipment")]
    [InlineData("DungeonLocation.cs", "ManagePartyMemberEquipment")]
    public void TheMenus_NoLongerSaveOnceForMovesBothWays(string file, string method)
    {
        MethodBody(file, method).Should().NotContain("AutoSave(").And.NotContain("SaveAllSharedState");
    }

    [Fact]
    public async Task HomeUnequip_SavesTheNpcSideFirst()
    {
        var ring = new Equipment { Name = "Test Home Ring", Slot = EquipmentSlot.LFinger, WisdomBonus = 3, MinLevel = 1 };
        EquipmentDatabase.RegisterDynamic(ring);
        var npc = TeamCornerRig.Npc("npc-home-unequip", "Unwed", "");
        npc.EquipItem(ring, EquipmentSlot.LFinger, out _).Should().BeTrue();
        var hero = TeamCornerRig.Hero();
        var order = new List<string>();
        BaseLocation.GearSaveHookForTests = step => { order.Add(step); return Task.CompletedTask; };
        try { await Run(Rig(new HomeLocation(), hero, new[] { "1" }), "UnequipItemFromCharacter", npc); }
        finally { BaseLocation.GearSaveHookForTests = null; }

        npc.GetEquipment(EquipmentSlot.LFinger).Should().BeNull();
        hero.Inventory.Should().ContainSingle(i => i.Name == "Test Home Ring");
        order.Should().Equal("shared", "player");
    }

    [Fact]
    public async Task ACompanionsGear_IsCopiedBackBeforeThePlayerSave_WhichHoldsBothSides()
    {
        // v1.1.13: a companion is saved in the player's own save; the wrapper is synced before that one save
        var companion = CompanionSystem.Instance.GetCompanion(CompanionId.Lyris)!;
        var before = new Dictionary<EquipmentSlot, int>(companion.EquippedItems);
        var wrapper = new Character { Name2 = "Lyris", IsCompanion = true, CompanionId = CompanionId.Lyris };
        wrapper.EquippedItems[EquipmentSlot.LFinger] = 987654;
        var seen = new List<int?>();
        BaseLocation.GearSaveHookForTests = step =>
        {
            if (step == "player") seen.Add(companion.EquippedItems.TryGetValue(EquipmentSlot.LFinger, out var id) ? id : null);
            return Task.CompletedTask;
        };
        try
        {
            var rig = new TeamCornerRig(TeamCornerRig.Hero(), Array.Empty<string>());
            await rig.Loc.SaveGearGivenToNpc(wrapper);
            await rig.Loc.SaveGearTakenFromNpc(wrapper);
        }
        finally
        {
            BaseLocation.GearSaveHookForTests = null;
            companion.EquippedItems.Clear();
            foreach (var kv in before) companion.EquippedItems[kv.Key] = kv.Value;
        }
        seen.Should().Equal(987654, 987654);
    }

    // ---------- 4. the player king's save key ----------

    [Fact]
    public async Task APlayerKing_IsLoadedByItsSaveKey_WithItsRealLevel()
    {
        await TeamCornerRig.Online(async (db, path) =>
        {
            // an account keyed "aurelia" plays someone else; the king is "Aurelia" on "royal_account"
            TeamCornerRig.Exec(path, "INSERT INTO players (username, display_name, player_data) VALUES ('aurelia', 'Somebody', '{\"player\":{\"name2\":\"Somebody\",\"level\":3}}');");
            await db.WriteGameData("royal_account", new SaveGameData
            {
                Version = GameConfig.SaveVersion,
                Player = new PlayerData { Name1 = "royal_account", Name2 = "Aurelia", Level = 42, King = true }
            });
            // crowned as "Isolde", married since: display name "Isolde Stone"
            await db.WriteGameData("wed_account", new SaveGameData
            {
                Version = GameConfig.SaveVersion,
                Player = new PlayerData { Name1 = "wed_account", Name2 = "Isolde", FamilySurname = "Stone", Level = 37 }
            });

            db.ResolveKingSaveKey("Aurelia").Should().Be("royal_account");
            (await db.ReadKingSave("Aurelia"))!.Player.Level.Should().Be(42);
            (await db.ReadKingSave("Isolde"))!.Player.Level.Should().Be(37);
            (await db.ReadKingSave("Isolde Stone"))!.Player.Level.Should().Be(37);
            (await db.ReadKingSave("Nobody Here")).Should().BeNull("the stand-in is kept only then");

            var before = CastleLocation.GetCurrentKing();
            CastleLocation.SetKing(King.CreateNewKing("Aurelia", CharacterAI.Human, CharacterSex.Female));
            try
            {
                var castle = Rig(new CastleLocation(), TeamCornerRig.Hero("Challenger"), Array.Empty<string>());
                ((int)typeof(CastleLocation).GetMethod("GetKingLevel", F)!.Invoke(castle, null)!).Should().Be(42);
            }
            finally { CastleLocation.SetKing(before); }
        });
    }

    [Fact]
    public void NoPlayerKingLookup_ReadsTheThroneNameAsASaveKey()
    {
        foreach (var file in new[] { "Scripts/Locations/CastleLocation.cs", "Scripts/Systems/ChallengeSystem.cs" })
        {
            string src = File.ReadAllText(Path.Combine(RepoRoot(), file));
            Regex.IsMatch(src, @"ReadGameData\((currentKing|king)\.Name").Should().BeFalse(file);
        }
        MethodBody("CastleLocation.cs", "ChallengeThrone").Should().Contain("ReadKingSave(currentKing.Name)");
    }
}
