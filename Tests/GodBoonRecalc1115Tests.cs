using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 2 follow-up: Terran's max HP boon is applied in RecalculateStats, so
/// every change of its inputs recalculates at once: the worshipped god (GodRegistry and the
/// Temple's own writes), a Favor change that crosses a tier, the player-god boon refresh (login,
/// Temple entry, joining, scale) and a player-god's domain choice. HP is only clamped, never raised.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodBoonRecalc1115Tests
{
    private static Character Hero(string name) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 10, HP = 100, MaxHP = 100,
        BaseMaxHP = 100, BaseStrength = 10, BaseDexterity = 10, BaseConstitution = 10, BaseIntelligence = 20,
        BaseWisdom = 20, BaseCharisma = 10, BaseDefence = 5, BaseStamina = 10, BaseAgility = 10,
        Mental = 80, Class = CharacterClass.Warrior, Race = CharacterRace.Human
    };

    /// <summary>The character's max HP with no god, from the stat pipeline.</summary>
    private static long PlainMaxHp(Character c)
    {
        GodRegistry.SetWorshippedGod(c, null);
        c.RecalculateStats();
        return c.MaxHP;
    }

    private static long TerranAt(long plain, int strengthPct) =>
        plain + GodBoonSystem.Bonus(plain, GameConfig.GodBoonTerranMaxHpPct * strengthPct / 100.0);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    /// <summary>The text of a method from its signature to the next member at the same indent.</summary>
    private static string Body(string file, string signature)
    {
        string src = Source(file);
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{signature} must exist in {file}");
        var next = new Regex(@"\n    (private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    private static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    // ---------------- Worship ----------------

    [Fact]
    public void Worship_AGodChange_RecalculatesMaxHp_AndNeverHeals()
    {
        var c = Hero("GbrWorship");
        long plain = PlainMaxHp(c);
        c.HP = plain;
        try
        {
            GodRegistry.SetWorshippedGod(c, "Terran").Should().BeTrue();
            c.MaxHP.Should().Be(TerranAt(plain, GameConfig.GodBoonFollowerStrengthPct), "a new Terran follower's max HP follows at once");
            c.HP.Should().Be(plain, "a higher max never heals");

            c.HP = c.MaxHP;
            GodRegistry.SetWorshippedGod(c, null);
            c.MaxHP.Should().Be(plain, "the boon goes with the god");
            c.HP.Should().Be(plain, "HP is clamped to the lower max");
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    [Fact]
    public void Worship_InAnotherGodSystem_LeavesTheStats_SinceTheStatsReadTheSharedOne()
    {
        var c = Hero("GbrOtherGods");
        c.MaxHP = 12345; c.HP = 12345;
        GodRegistry.SetWorshippedGod(c, "Terran", new GodSystem()).Should().BeTrue();
        c.MaxHP.Should().Be(12345);
        c.HP.Should().Be(12345);
    }

    [Fact]
    public void RecalculateForBoon_SkipsNpcs()
    {
        var npc = Hero("GbrNpc");
        npc.AI = CharacterAI.Computer;
        npc.MaxHP = 12345; npc.HP = 12345;
        GodBoonSystem.RecalculateForBoon(npc);
        npc.MaxHP.Should().Be(12345);
    }

    // ---------------- Favor tier ----------------

    [Fact]
    public void Favor_AChangeThatCrossesATier_RecalculatesMaxHp()
    {
        var c = Hero("GbrFavor");
        long plain = PlainMaxHp(c);
        try
        {
            GodRegistry.SetWorshippedGod(c, "Terran").Should().BeTrue();
            c.MaxHP.Should().Be(TerranAt(plain, GameConfig.GodBoonFollowerStrengthPct));
            FavorSystem.Change(c, GameConfig.GodFavorTierZealotMin).Should().Be(GameConfig.GodFavorTierZealotMin);
            c.MaxHP.Should().Be(TerranAt(plain, GameConfig.GodBoonZealotStrengthPct), "Zealot gives the full boon at once");
            c.HP = c.MaxHP;
            FavorSystem.Change(c, -GameConfig.GodFavorMax);
            c.MaxHP.Should().Be(TerranAt(plain, GameConfig.GodBoonFollowerStrengthPct));
            c.HP.Should().Be(c.MaxHP, "HP is clamped to the lower max");
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    // ---------------- Player-god boon refresh (login, Temple, scale) ----------------

    [Fact]
    public async Task Refresh_RecalculatesMaxHp_FromTheRefreshedCache()
    {
        UsurperRemake.BBS.DoorMode.IsOnlineMode.Should().BeFalse("offline the refresh clears a player-god cache");
        var c = Hero("GbrRefresh");
        long plain = PlainMaxHp(c);
        try
        {
            GodRegistry.SetWorshippedGod(c, "GbrStoneGod").Should().BeTrue();
            c.GodFavor = GameConfig.GodFavorTierZealotMin;
            GodBoonSystem.SetPlayerGodBoon(c, "GbrStoneGod", GodDomain.Earth, 100);
            c.RecalculateStats();
            c.MaxHP.Should().Be(TerranAt(plain, 100));
            c.HP = c.MaxHP;

            await GodBoonSystem.RefreshPlayerGodBoonAsync(c);
            c.PlayerGodBoonDomain.Should().Be(GodDomain.None);
            c.MaxHP.Should().Be(plain, "the refreshed boon is on max HP at once");
            c.HP.Should().Be(plain);
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    [Fact]
    public void Wiring_TheLoginAndTheTempleRefreshTheBoon()
    {
        Body("Scripts/Core/GameEngine.cs", "private async Task LoadSaveByFileName(")
            .Should().Contain("await GodBoonSystem.RefreshPlayerGodBoonAsync(currentPlayer);");
        var entry = Body("Scripts/Locations/TempleLocation.cs", "public async Task<string> ProcessLocation(");
        int verify = entry.IndexOf("await VerifyPlayerGodExists();", StringComparison.Ordinal);
        verify.Should().BeGreaterThanOrEqualTo(0);
        entry.IndexOf("await GodBoonSystem.RefreshPlayerGodBoonAsync(currentPlayer);", StringComparison.Ordinal)
            .Should().BeGreaterThan(verify, "the entry refresh also covers a god cleared by VerifyPlayerGodExists");
        Body("Scripts/Locations/TempleLocation.cs", "private async Task WorshipImmortalGod(")
            .Should().Contain("await GodBoonSystem.RefreshPlayerGodBoonAsync(currentPlayer);");
    }

    // ---------------- The Temple's own worship writes ----------------

    [Fact]
    public void Temple_EveryCanonWorshipWrite_GoesThroughTheRecalculatingHelper()
    {
        string temple = Source("Scripts/Locations/TempleLocation.cs");
        Body("Scripts/Locations/TempleLocation.cs", "private void SetCanonWorship(")
            .Should().Contain("godSystem.SetPlayerGod(currentPlayer.Name2, god);")
            .And.Contain("GodBoonSystem.RecalculateForBoon(currentPlayer);");
        Count(temple, "godSystem.SetPlayerGod(currentPlayer.Name2").Should().Be(1, "only the helper writes the canon god");
        var worship = Body("Scripts/Locations/TempleLocation.cs", "private async Task ProcessWorship(");
        Count(worship, "SetCanonWorship(\"\");").Should().Be(1, "leaving a canon god");
        Count(worship, "SetCanonWorship(selectedGod.Name);").Should().Be(1, "choosing a canon god");
        Count(Body("Scripts/Locations/TempleLocation.cs", "private async Task VerifyPlayerGodExists("), "SetCanonWorship(\"\");").Should().Be(1);
        Count(Body("Scripts/Locations/TempleLocation.cs", "private async Task WorshipImmortalGod("), "SetCanonWorship(\"\");").Should().Be(1);
    }

    [Fact]
    public void Temple_LeavingAPlayerGod_Recalculates()
    {
        var leave = new Regex(@"currentPlayer\.WorshippedGod = """";\s*GodBoonSystem\.RecalculateForBoon\(currentPlayer\);");
        leave.IsMatch(Body("Scripts/Locations/TempleLocation.cs", "private async Task ProcessWorship(")).Should().BeTrue("abandoning a player-god for the canon gods");
        leave.IsMatch(Body("Scripts/Locations/TempleLocation.cs", "private async Task LeaveImmortalFaith(")).Should().BeTrue("leaving a player-god's faith");
    }

    // ---------------- A player-god's domain ----------------

    [Fact]
    public void DomainChange_GivesItsFollowersTheBoon_AndRecalculates_OnlyForThatGod()
    {
        var f = Hero("GbrDomFollower");
        var other = Hero("GbrDomOther");
        long plain = PlainMaxHp(f);
        PlainMaxHp(other);
        try
        {
            GodRegistry.SetWorshippedGod(f, "GbrDomGod").Should().BeTrue();
            f.GodFavor = GameConfig.GodFavorTierZealotMin;
            GodBoonSystem.SetPlayerGodBoon(f, "GbrDomGod", GodDomain.None, 0);
            GodRegistry.SetWorshippedGod(other, "GbrDomRival").Should().BeTrue();
            other.GodFavor = GameConfig.GodFavorTierZealotMin;
            other.MaxHP = 12345;
            f.MaxHP.Should().Be(plain);

            GodBoonSystem.ApplyDomainChange("GbrDomGod", GodDomain.Earth, 100, new[] { f, other });
            f.PlayerGodBoonDomain.Should().Be(GodDomain.Earth);
            f.PlayerGodBoonScalePct.Should().Be(100);
            f.MaxHP.Should().Be(TerranAt(plain, 100), "the follower's max HP follows the chosen domain at once");
            other.PlayerGodBoonGod.Should().BeEmpty("a follower of another god is untouched");
            other.MaxHP.Should().Be(12345);
        }
        finally
        {
            GodRegistry.SetWorshippedGod(f, null);
            GodRegistry.SetWorshippedGod(other, null);
        }
    }

    [Fact]
    public void Wiring_ThePickerAppliesTheDomainToTheFollowersOnline()
    {
        Regex.IsMatch(Body("Scripts/Systems/GodDomainPicker.cs", "public static async Task<bool> PickAsync("),
                @"immortal\.DivineDomain = chosen\.ToString\(\);\s*await GodBoonSystem\.ApplyDomainChangeAsync\(immortal\);")
            .Should().BeTrue();
        Body("Scripts/Systems/GodBoonSystem.cs", "public static async Task ApplyDomainChangeAsync(")
            .Should().Contain("int scale = PlayerGodScalePct(StandingOf(standings, god), StrongestCanon(standings), 0);")
            .And.Contain("ApplyDomainChange(god, ParseDomain(immortal.DivineDomain), scale, followers);");
    }

    // ---------------- A player-god's configured boons (Pantheon) ----------------

    private static Character Caster(string name)
    {
        var c = Hero(name);
        c.Class = CharacterClass.Magician;
        c.BaseMaxMana = 100; c.MaxMana = 100; c.Mana = 100;
        return c;
    }

    [Fact]
    public void Leaving_APlayerGod_ClearsTheConfiguredBoons_AndClampsHpAndMana()
    {
        var c = Caster("GbrLeave");
        long plain = PlainMaxHp(c);
        long plainMana = c.MaxMana;
        plainMana.Should().BeGreaterThan(0);
        try
        {
            GodRegistry.SetWorshippedGod(c, "GbrLeaveGod").Should().BeTrue();
            GodBoonSystem.SetConfiguredBoons(c, "divine_vitality:3,mana_well:3");
            c.MaxHP.Should().BeGreaterThan(plain);
            c.MaxMana.Should().BeGreaterThan(plainMana);
            c.HP = c.MaxHP; c.Mana = c.MaxMana;

            GodRegistry.SetWorshippedGod(c, null);
            c.CachedBoonEffects.Should().BeNull("the configured boons go with the god");
            c.MaxHP.Should().Be(plain);
            c.MaxMana.Should().Be(plainMana);
            c.HP.Should().Be(plain, "HP is clamped to the lower max");
            c.Mana.Should().Be(plainMana, "mana is clamped to the lower max");
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    [Fact]
    public void Reconfig_TheFollowersTakeTheNewBoons_AndRecalculate()
    {
        var c = Caster("GbrReconfig");
        long plain = PlainMaxHp(c);
        long plainMana = c.MaxMana;
        try
        {
            GodRegistry.SetWorshippedGod(c, "GbrReconfigGod").Should().BeTrue();
            GodBoonSystem.SetConfiguredBoons(c, "divine_vitality:3");
            var effects = DivineBoonRegistry.CalculateEffects("divine_vitality:3");
            c.MaxHP.Should().Be(plain + (long)(plain * effects.MaxHPPercent), "the new boons are on max HP at once");
            c.MaxMana.Should().Be(plainMana);
            c.HP = c.MaxHP;

            GodBoonSystem.SetConfiguredBoons(c, "");
            c.MaxHP.Should().Be(plain, "a boon taken away is off max HP at once");
            c.HP.Should().Be(plain, "HP is clamped to the lower max");
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }

        Body("Scripts/Locations/PantheonLocation.cs", "private static void NotifyOnlineFollowers(")
            .Should().Contain("GodBoonSystem.SetConfiguredBoons(player, newConfig);")
            .And.NotContain("CachedBoonEffects =");
    }

    [Fact]
    public void Recruit_TheFollowerTakesTheGodsDomainAndBoons_AtOnce()
    {
        var f = Hero("GbrRecruit");
        long plain = PlainMaxHp(f);
        var god = new Character
        {
            Name1 = "GbrRecruitImm", Name2 = "GbrRecruitImm", AI = CharacterAI.Human, IsImmortal = true,
            DivineName = "GbrRecruitGod", DivineDomain = "Earth", DivineBoonConfig = "divine_vitality:1"
        };
        try
        {
            GodRegistry.SetWorshippedGod(f, "GbrOldGod").Should().BeTrue();
            GodBoonSystem.SetConfiguredBoons(f, "divine_vitality:3");
            GodRegistry.SetWorshippedGod(f, "GbrRecruitGod").Should().BeTrue();
            f.GodFavor = GameConfig.GodFavorTierZealotMin;

            GodBoonSystem.ApplyRecruit(god, f, 100);
            f.PlayerGodBoonGod.Should().Be("GbrRecruitGod");
            f.PlayerGodBoonDomain.Should().Be(GodDomain.Earth, "the new god's domain is cached at once");
            f.PlayerGodBoonScalePct.Should().Be(100);
            long withVitality = plain + (long)(plain * DivineBoonRegistry.CalculateEffects("divine_vitality:1").MaxHPPercent);
            f.MaxHP.Should().Be(TerranAt(withVitality, 100), "the new god's boons replace the old god's");
        }
        finally { GodRegistry.SetWorshippedGod(f, null); }

        Regex.IsMatch(Body("Scripts/Locations/PantheonLocation.cs", "private async Task ApplyRecruitToPlayer("),
                @"GodRegistry\.SetWorshippedGod\(player, godName\);[^\n]*\n\s*await GodBoonSystem\.ApplyRecruitAsync\(currentPlayer, player\);")
            .Should().BeTrue();
    }
}
