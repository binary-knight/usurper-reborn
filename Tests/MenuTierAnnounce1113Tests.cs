using System;
using System.IO;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: Main Street names the places a menu tier rise opens, once, and never old unlocks.</summary>
[Collection("SharedGameSingletons")]
public class MenuTierAnnounce1113Tests
{
    private static Character Hero(int level) => new Character { Name1 = "Tier Hero", Name2 = "Tier Hero", Level = level };

    [Fact]
    public void TierRise_IsAnnouncedOnce()
    {
        var hero = Hero(1);
        MainStreetLocation.TakeTierUnlockAnnouncement(hero).Should().BeEmpty("a new character has nothing new yet");
        hero.HintsShown.Should().Contain("menu_tier_1");

        hero.Level = GameConfig.MenuTier2Level;
        string line = string.Join("\n", MainStreetLocation.TakeTierUnlockAnnouncement(hero));
        line.Should().NotBeEmpty();
        line.Should().Contain(Loc.Get("menu.action.temple")).And.Contain(Loc.Get("menu.action.bank"))
            .And.NotContain(Loc.Get("menu.action.auction_house"));
        MainStreetLocation.TakeTierUnlockAnnouncement(hero).Should().BeEmpty("the rise is told once");

        hero.Level = GameConfig.MenuTier3Level;
        string.Join("\n", MainStreetLocation.TakeTierUnlockAnnouncement(hero)).Should().Contain(Loc.Get("menu.action.auction_house"))
            .And.NotContain(Loc.Get("menu.action.temple"));
        MainStreetLocation.TakeTierUnlockAnnouncement(hero).Should().BeEmpty();
    }

    [Fact]
    public void ExistingTier3Character_SeesNothing()
    {
        var hero = Hero(20);
        MainStreetLocation.TakeTierUnlockAnnouncement(hero).Should().BeEmpty();
        hero.HintsShown.Should().Contain(new[] { "menu_tier_1", "menu_tier_2", "menu_tier_3" });
        MainStreetLocation.TakeTierUnlockAnnouncement(hero).Should().BeEmpty();
    }

    [Fact]
    public void MenuMethods_AreUntouched_AndBothPathsAnnounce()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Locations", "MainStreetLocation.cs"));
        int display = src.IndexOf("protected override void DisplayLocation()");
        int bbs = src.IndexOf("private void DisplayLocationBBS()");
        src.Substring(display, bbs - display).Split("ShowTierUnlockAnnouncement();").Length.Should().Be(3, "the BBS and the full screen each call it");
        src.Substring(bbs).Should().NotContain("ShowTierUnlockAnnouncement", "only the two display paths call it");
    }
}
