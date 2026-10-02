using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.BBS;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: BaseLocation's own text in the player's language. The help screens (box and screen reader), the
/// MUD prompt suffixes, the founder hub, the Active Buffs rows, the equipment totals, the mailbox, bounty and
/// auction screens, and the trade, bounty and auction notices (each in the reader's language). Typed slash
/// commands stay exactly as typed. Every changed row, and the shared rows a sample of child locations shows,
/// fits 79 columns in English and Hungarian with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class TownBase125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";
    private const long Big = 123456;

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private static long GoldCap =>
        (long)typeof(BankLocation).GetField("MaxGold", FS)!.GetValue(null)!;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-townbase-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
        if (_db == null) return;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ---------- helpers ----------

    private static (TerminalEmulator term, MemoryStream output) Term(string input = "")
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Strip(Encoding.UTF8.GetString(output.ToArray()));
    }

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    // A framed box (the help screen's) is drawn exactly 80 columns wide, as every WriteBoxHeader box is; a row
    // inside it must close at column 80, not past it. Every other row fits in 79.
    private static void EveryRowFits(IEnumerable<string> rows, string screen)
    {
        foreach (var row in rows)
        {
            if (row.Length > 0 && "╔║╠╚".IndexOf(row[0]) >= 0)
                row.Length.Should().Be(MaxWidth + 1, $"the {screen} box keeps its width and every row closes it: \"{row}\"");
            else
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
        }
    }

    private static void EveryRowFits(string text, string screen) => EveryRowFits(Rows(text), screen);

    private static T InLanguage<T>(string lang, Func<T> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            return body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static Character Hero(CharacterClass cls = CharacterClass.Warrior) => new()
    {
        Name1 = "tester", Name2 = LongName, Class = cls, Level = 100, HP = Big, MaxHP = Big, Mana = Big, MaxMana = Big,
        AI = CharacterAI.Human, Mental = 100, Gold = 1_000_000_000, Healing = 99, Height = 250, Weight = 400,
        CurrentCombatStamina = 61000,
    };

    private static T At<T>(T location, TerminalEmulator term, Character hero) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return location;
    }

    private sealed class Plain : BaseLocation
    {
        public Plain() : base(GameLocation.TheInn, "The Inn", "") { }
        protected override List<NPC> GetLiveNPCsAtLocation() => new();
    }

    private static object? Call(BaseLocation loc, string method, params object?[] args)
    {
        var m = typeof(BaseLocation).GetMethod(method, F)!;
        var r = m.Invoke(loc, args);
        if (r is Task t) t.GetAwaiter().GetResult();
        return r;
    }

    private static readonly FieldInfo MudMode = typeof(DoorMode).GetField("_mudServerMode", FS)!;
    private static readonly FieldInfo ChatFallback = typeof(OnlineChatSystem).GetField("_fallbackInstance", FS)!;

    /// <summary>Runs body with the MUD server mode on and an online chat system present (the online help rows).</summary>
    private static T Online<T>(Func<T> body)
    {
        bool mud = (bool)MudMode.GetValue(null)!;
        var chat = ChatFallback.GetValue(null);
        try
        {
            MudMode.SetValue(null, true);
            ChatFallback.SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(OnlineChatSystem)));
            return body();
        }
        finally { MudMode.SetValue(null, mud); ChatFallback.SetValue(null, chat); }
    }

    private static string Src() =>
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "BaseLocation.cs"));

    private static JsonElement LangJson(string lang) => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Localization", lang + ".json"))).RootElement;

    [Fact]
    public void LongName_IsTheLongestPlayerName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        GoldCap.Should().Be(long.MaxValue / 100 * 99);
    }

    // ---------- 1. the help screens ----------

    /// <summary>Every command the help screens list, as typed.</summary>
    private static readonly string[] TypedCommands =
    {
        "/stats", "/inventory", "/quests", "/q", "/journal", "/next", "/train", "look", "/gold", "/g", "/health", "/hp",
        "/gear", "/eq", "/potion", "/pot", "/herb", "/j", "/materials", "/mat", "/time", "/prefs", "/p", "/mail",
        "/trade", "/auction", "/boss", "/town", "/compact", "/autolook", "/bug", "/say", "/shout", "/tell", "/emote",
        "/who", "/gossip", "/guild", "/gcreate", "/ginvite", "/gleave", "/gkick", "/gc", "/gbank", "/gdeposit",
        "/gwithdraw <#>", "/grank <p>", "/gtransfer", "/ginfo", "/group", "/leave", "/disband", "/party", "/accept", "/deny",
    };

    private static readonly string[] HelpKeys =
    {
        "base.help_stats", "base.help_inventory", "base.help_quests", "base.help_gold", "base.help_health", "base.help_gear",
        "base.help_potion", "base.help_herb", "base.help_materials", "base.help_time", "base.help_prefs", "base.help_mail",
        "base.help_trade", "base.help_auction", "base.help_boss", "base.help_town", "base.help_compact", "base.help_bug",
        "base.help_say", "base.help_tell", "base.help_guild", "base.help_gtransfer", "base.help_deny",
    };

    private static string Help(string lang, bool screenReader) => InLanguage(lang, () => Online(() =>
    {
        var (term, output) = Term("\n\n\n");
        var loc = At(new Plain(), term, Hero());
        GameConfig.ScreenReaderMode = screenReader;
        Call(loc, screenReader ? "ShowQuickCommandsHelpSR" : "ShowQuickCommandsHelp");
        return Shown(term, output);
    }));

    [Theory]
    [InlineData("en", false)] [InlineData("hu", false)] [InlineData("en", true)] [InlineData("hu", true)]
    public void HelpScreen_EveryRowFits_AndTheCommandsStayAsTyped(string lang, bool screenReader)
    {
        string shown = Help(lang, screenReader);
        Capture($"town-base-help{(screenReader ? "-sr" : "")}-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} help screen");
        foreach (var cmd in TypedCommands)
            shown.Should().Contain(cmd, $"the typed command {cmd} is shown as typed in {lang}");
        shown.Should().Contain(screenReader ? $"/stats {L(lang, "base.or")} % " : $"{"/stats",-10} {L(lang, "base.or")} %");
        shown.Should().Contain($"/say {L(lang, "base.help_arg_msg")}").And.Contain($"/gtransfer {L(lang, "base.help_arg_player")}");
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HelpScreen_Hungarian_HasNoEnglishLeft(bool screenReader)
    {
        string shown = Help("hu", screenReader);
        shown.Should().NotContain(" or ").And.NotContain("<msg>").And.NotContain("<player>").And.NotContain("<name>")
            .And.NotContain("<action>").And.NotContain("<guild>").And.NotContain("<rank>");
        foreach (var key in HelpKeys)
            shown.Should().Contain(L("hu", key).Trim().TrimStart('-').Trim(), key);
    }

    // ---------- 2. the MUD prompt ----------

    private static string Prompt(string lang, CharacterClass cls) => InLanguage(lang, () => Online(() =>
    {
        var (term, output) = Term("x\n");
        var loc = At(new Plain(), term, Hero(cls));
        var hero = (Character)typeof(BaseLocation).GetField("currentPlayer", F)!.GetValue(loc)!;
        Call(loc, "GetUserChoice");
        return Shown(term, output);
    }));

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void MudPrompt_Suffixes_AreKeyed(string lang)
    {
        string mana = Prompt(lang, CharacterClass.Magician);
        string stamina = Prompt(lang, CharacterClass.Warrior);
        Capture($"town-base-prompt-{lang}.txt", mana + "\n" + stamina);
        mana.Should().StartWith($"[{L(lang, "base.prompt_hp", Big)} {L(lang, "base.prompt_mp", Big)}] ");
        stamina.Should().StartWith($"[{L(lang, "base.prompt_hp", Big)} {L(lang, "base.prompt_st", 61000)}] ");
        EveryRowFits(mana + "\n" + stamina, $"{lang} MUD prompt");
        if (lang == "en") mana.Should().StartWith($"[{Big}hp {Big}mp] ", "the English prompt is unchanged");
        else stamina.Should().NotContain($"{Big}hp").And.NotContain("61000st");
    }

    // ---------- 3. the founder hub and the language error ----------

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void FounderHub_Fits_AndIsInThePlayersLanguage(string lang)
    {
        string shown = InLanguage(lang, () =>
        {
            var (term, output) = Term("R\n");
            Call(At(new Plain(), term, Hero()), "ShowFounderHubMenu");
            return Shown(term, output);
        });
        Capture($"town-base-founder-hub-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} founder hub");
        foreach (var key in new[] { "base.founder_hub_title", "base.founder_hub_intro", "base.founder_hub_pantheon", "base.founder_hub_castle", "base.founder_hub_plinths", "ui.return", "inn.choose" })
            shown.Should().Contain(L(lang, key).Trim(), key);
        if (lang == "hu") shown.Should().NotContain("Hall of Statues").And.NotContain("Choose where");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void UnknownLanguage_IsInThePlayersLanguage(string lang)
    {
        string shown = InLanguage(lang, () =>
        {
            var (term, output) = Term();
            var hero = Hero();
            Call(At(new Plain(), term, hero), "HandleSettingsCommand", "lang xx");
            return Shown(term, output);
        });
        shown.Should().Contain(L(lang, "base.unknown_language", "xx", string.Join(", ", Loc.LoadedLanguages)));
        EveryRowFits(shown, $"{lang} language error");
    }

    // ---------- 4. the Active Buffs rows ----------

    private static readonly CharacterClass[] BuffClasses =
    {
        CharacterClass.Alchemist, CharacterClass.Magician, CharacterClass.Bard, CharacterClass.Jester, CharacterClass.Assassin,
        CharacterClass.MysticShaman, CharacterClass.Paladin, CharacterClass.Cleric, CharacterClass.Tidesworn,
        CharacterClass.Wavecaller, CharacterClass.Cyclebreaker, CharacterClass.Abysswarden, CharacterClass.Voidreaver,
    };

    /// <summary>A hero with every buff the status screen lists for this class, and the given murder weight.</summary>
    private static Character Buffed(CharacterClass cls, float murderWeight)
    {
        var h = Hero(cls);
        h.MurderWeight = murderWeight;
        h.IsKnighted = true; h.NobleTitle = "Queen";
        h.ArenaChampionTier = (int)UsurperRemake.Data.GauntletChampionData.ArenaTier.GrandChampion;
        h.ShamanEnchantType = 1; h.ShamanEnchantRounds = 99;
        h.GodSlayerCombats = 999; h.GodSlayerDamageBonus = 0.25f; h.GodSlayerDefenseBonus = 0.25f;
        h.DarkPactCombats = 999; h.DarkPactDamageBonus = 0.25f;
        h.SettlementBuffType = (int)SettlementBuffType.TrapResist; h.SettlementBuffCombats = 999; h.SettlementBuffValue = 0.25f;
        h.WellRestedCombats = 999; h.WellRestedBonus = 0.25f;
        h.SongBuffType = 2; h.SongBuffCombats = 999; h.SongBuffValue = 0.25f;
        h.FoodBuffType = 4; h.FoodBuffCombats = 999;
        h.LoversBlissCombats = 999; h.DivineBlessingCombats = 999;
        return h;
    }

    private static string Status(string lang, Character hero) => InLanguage(lang, () =>
    {
        var (term, output) = Term(string.Concat(Enumerable.Repeat("\n", 20)));
        Call(At(new Plain(), term, hero), "ShowStatus");
        return Shown(term, output);
    });

    /// <summary>The Active Buffs rows: after the heading, up to the blank row.</summary>
    private static List<string> BuffBlock(string shown, string lang)
    {
        var rows = Rows(shown);
        int at = rows.FindIndex(r => r == L(lang, "base.stat_active_buffs"));
        at.Should().BeGreaterOrEqualTo(0, "the Active Buffs block is drawn");
        return rows.Skip(at + 1).TakeWhile(r => r.Length > 0).ToList();
    }

    private static readonly string[] EnglishBuffWords =
    {
        "Blood Price", "Murder Weight", "Honor", "Mantle", "Bardic", "Lethal", "Elemental", "Totem Duration", "Active Enchant",
        "Flametongue", "Divine", "Ocean", "Harmonic", "Reflection", "Probability", "Cycle Memory", "Siphon", "Warden",
        "Harvest", "Void Hunger", "Pain", "Soul Eater", "God Slayer", "Dark Pact", "Prison (", "Well-Rested",
        "Mushroom", "Lover", "combats", "Session",
    };

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void ActiveBuffs_EveryRowFits_ForEveryClass(string lang)
    {
        var all = new StringBuilder();
        foreach (var cls in BuffClasses)
        {
            float weight = cls == CharacterClass.Wavecaller ? 99f : 0f;
            string shown = Status(lang, Buffed(cls, weight));
            var block = BuffBlock(shown, lang);
            all.AppendLine($"== {cls}").AppendLine(string.Join("\n", block));
            EveryRowFits(block, $"{lang} {cls} Active Buffs");
            if (lang == "hu")
                foreach (var word in EnglishBuffWords)
                    string.Join("\n", block).Should().NotContain(word, $"{cls}: the buff rows are in Hungarian");
            // the basic information row with height and weight
            var info = Rows(shown).First(r => r.Contains(L(lang, "base.height_cm", 250)));
            info.Should().Contain(L(lang, "base.weight_kg", 400));
            info.Length.Should().BeLessOrEqualTo(MaxWidth);
        }
        Capture($"town-base-buffs-{lang}.txt", all.ToString());
    }

    [Fact]
    public void ActiveBuffs_English_RowsThatFit_AreUnchanged_AndLongRowsWrapUnderTheText()
    {
        string shown = Status("en", Buffed(CharacterClass.Wavecaller, 99f));
        var block = BuffBlock(shown, "en");
        block.Should().Contain($"  - Harmonic Resonance: +{(int)(GameConfig.WavecallerHarmonicResonanceBonus * 100)}% healing from abilities and spells");
        block.Should().Contain("  - Queen's Honor: " + $"+{(int)(GameConfig.KnightDamageBonus * 100)}% damage, +{(int)(GameConfig.KnightDefenseBonus * 100)}% defense (permanent)");
        string reflection = $"  - Damage Reflection: {(int)(GameConfig.WavecallerReflectionPercent * 100)}% damage reflected when Harmonic Shield or Empathic Link active";
        reflection.Length.Should().BeGreaterThan(MaxWidth, "this row was over 79 before 1.2.5");
        var rows = BaseLocation.BuffRows(reflection);
        rows.Count.Should().Be(2);
        rows[0].Should().StartWith("  - Damage Reflection:");
        rows[1].Should().StartWith("    ").And.NotStartWith("     ");
        string.Join(" ", rows.Select(r => r.Trim())).Should().Be(reflection.Trim());
        block.Should().Contain(rows[0]).And.Contain(rows[1]);
    }

    // ---------- 5. the equipment totals and the gear header ----------

    private static Character Geared()
    {
        var h = Hero();
        foreach (var slot in new[] { EquipmentSlot.MainHand, EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Arms, EquipmentSlot.Hands,
                     EquipmentSlot.Legs, EquipmentSlot.Feet, EquipmentSlot.Waist, EquipmentSlot.Neck, EquipmentSlot.Face, EquipmentSlot.Cloak,
                     EquipmentSlot.LFinger, EquipmentSlot.RFinger, EquipmentSlot.OffHand })
        {
            var e = new Equipment
            {
                Name = "Test " + slot, Slot = slot, WeaponPower = 1, ArmorClass = 1, MinLevel = 1,
                StrengthBonus = GameConfig.MaxItemStatBonus, DexterityBonus = GameConfig.MaxItemStatBonus, AgilityBonus = GameConfig.MaxItemStatBonus,
                ConstitutionBonus = GameConfig.MaxItemStatBonus, IntelligenceBonus = GameConfig.MaxItemStatBonus, WisdomBonus = GameConfig.MaxItemStatBonus,
                CharismaBonus = GameConfig.MaxItemStatBonus, MaxHPBonus = GameConfig.MaxItemVitalBonus, MaxManaBonus = GameConfig.MaxItemVitalBonus,
                DefenceBonus = GameConfig.MaxItemStatBonus, StaminaBonus = GameConfig.MaxItemStatBonus,
            };
            h.EquippedItems[slot] = EquipmentDatabase.RegisterDynamic(e);
        }
        return h;
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void EquipmentTotals_EveryRowFits_WithEveryBonusAtTheCap(string lang)
    {
        string shown = InLanguage(lang, () =>
        {
            var (term, output) = Term();
            Call(At(new Plain(), term, Geared()), "DisplayEquipmentTotals");
            return Shown(term, output);
        });
        Capture($"town-base-equipment-totals-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} equipment totals");
        foreach (var key in new[] { "ui.stat_str", "ui.stat_dex", "ui.stat_agi", "ui.stat_con", "ui.stat_int", "ui.stat_wis", "ui.stat_cha", "base.bonus_maxhp", "base.bonus_maxmp", "ui.stat_def", "ui.stat_sta" })
            shown.Should().Contain(L(lang, key) + " +", key);
        if (lang == "hu") shown.Should().NotContain("MaxHP").And.NotContain("Str +");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void GearHeader_IsKeyed_AndFits(string lang)
    {
        string shown = InLanguage(lang, () =>
        {
            var (term, output) = Term();
            Call(At(new Plain(), term, Hero()), "ShowDetailedGear", new object?[] { null });
            return Shown(term, output);
        });
        Capture($"town-base-gear-{lang}.txt", shown);
        shown.Should().Contain(L(lang, "base.gear_header", LongName));
        Rows(shown).Take(5).Should().OnlyContain(r => r.Length <= MaxWidth);
    }

    // ---------- 6. the mailbox, bounty and auction columns ----------

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void ColumnHeaders_EachLabelFitsItsField(string lang)
    {
        InLanguage(lang, () =>
        {
            foreach (var (key, width) in new[] { ("base.from_label", 16), ("base.col_date", 12), ("base.col_message", 36),
                         ("base.quest_target", 20), ("anchor_road.bounty_col_bounty", 15), ("base.col_posted_by", 20),
                         ("marketplace.col_item", 22), ("base.col_stats", 16), ("marketplace.col_price", 10),
                         ("marketplace.col_seller", 13), ("base.col_expires", 7) })
                L(lang, key).Length.Should().BeLessOrEqualTo(width, $"{key} fits its {width}-column field");

            string mail = BaseLocation.MailboxHeader();
            mail.Should().Be($"{"#",-4} {L(lang, "base.from_label"),-16} {L(lang, "base.col_date"),-12} {L(lang, "base.col_message"),-36}");
            string mailRow = BaseLocation.MailboxRow("*", 10, LongName, "2026-10-02", BaseLocation.MailPreview(new string('m', 200)));
            mail.IndexOf(L(lang, "base.col_date")).Should().Be(mailRow.IndexOf("2026-10-02"), "the Date label sits over the dates");

            string bounty = BaseLocation.BountyHeader();
            string amountCell = BaseLocation.BountyAmountCell(GoldCap);
            string row = $"  {10,-4} {BaseLocation.Cell(LongName, 20)} {amountCell}{BaseLocation.Cell(LongName, BaseLocation.BountyPlacedByWidth(amountCell))}";
            BaseLocation.BountyPlacedByWidth(BaseLocation.BountyAmountCell(1_000_000)).Should().Be(20, "an everyday amount keeps the 20-column field");
            bounty.IndexOf(L(lang, "anchor_road.bounty_col_bounty")).Should().Be(row.IndexOf(GoldCap.ToString("N0")));
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"a bounty row with two 30-character names and the gold cap fits: \"{row}\"");

            string auction = BaseLocation.AuctionListHeader();
            if (lang == "en")
            {
                mail.Should().Be($"{"#",-4} {"From",-16} {"Date",-12} {"Message",-36}", "the English header is unchanged");
                bounty.Should().Be($"  {"#",-4} {"Target",-20} {"Bounty",-15} {"Posted By",-20}");
                auction.Should().Be($"  {"#",-4} {"Item",-22} {"Stats",-16} {"Price".PadLeft(10)} {"Seller",-13} {"Expires"}");
            }
            foreach (var r in new[] { mail, mailRow, bounty, auction }) r.Length.Should().BeLessOrEqualTo(MaxWidth);
            return 0;
        });
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void AuctionItemDetails_Fit_AndAreInThePlayersLanguage(string lang)
    {
        var item = new Item
        {
            Name = "Blade", Type = ObjType.Weapon, Attack = 9999, Armor = 9999, HP = 9999, Strength = 999, Defence = 999,
            Stamina = 999, Agility = 999, Dexterity = 999, Wisdom = 999, Charisma = 999, Mana = 9999, MinLevel = 1,
        };
        var listing = new AuctionListing
        {
            Id = 1, Seller = LongName, ItemName = new string('I', 40), Price = GoldCap, ExpiresAt = DateTime.UtcNow.AddHours(71.6),
            ItemJson = JsonSerializer.Serialize(item),
        };
        string shown = InLanguage(lang, () =>
        {
            var (term, output) = Term("n\n\n");
            var hero = Hero(); hero.Gold = GoldCap;
            Call(At(new Plain(), term, hero), "ShowAuctionItemDetails", listing, item, "buyer", null);
            return Shown(term, output);
        });
        Capture($"town-base-auction-details-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} auction item details");
        shown.Should().Contain(L(lang, "base.auction_seller_expires", LongName, L(lang, "base.auction_hours", 72)));
        shown.Should().Contain(L(lang, "base.auction_price_gold", GoldCap.ToString("N0")));
        shown.Should().Contain(L(lang, "base.auction_your_gold", GoldCap.ToString("N0")));
        foreach (var key in new[] { "ui.stat_attack", "dungeon.armor_label", "ui.stat_strength", "combat.status_defence_label", "ui.stat_stamina", "ui.stat_agility", "ui.stat_dexterity", "ui.stat_wisdom", "ui.stat_charisma", "ui.stat_mana" })
            shown.Should().Contain($"  {L(lang, key),-12} +", key);
        shown.Should().Contain(L(lang, "base.auction_type", L(lang, "base.item_type_weapon"), "0"));
        if (lang == "en") shown.Should().Contain("  Defence      +999", "the English label is unchanged");
        else shown.Should().NotContain("Seller:").And.NotContain("Price:").And.NotContain("Strength").And.NotContain("Defence").And.NotContain("Weapon");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void AuctionDurations_ShowHoursInThePlayersLanguage_AndTheFeeSplit(string lang)
    {
        Player("seller_key", LongName, lang);
        string shown = InLanguage(lang, () =>
        {
            var (term, output) = Term("1\n1000000\n\n");
            var hero = Hero(); hero.Inventory.Add(new Item { Name = "Blade", Type = ObjType.Weapon, Value = 10 });
            Call(At(new Plain(), term, hero), "SellOnAuction", Db);
            return Shown(term, output);
        });
        Capture($"town-base-auction-durations-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} auction durations");
        foreach (int h in new[] { 12, 24, 48, 72 })
            shown.Should().Contain($"] {L(lang, "base.auction_hours", h),-12}");
        shown.Should().Contain(L(lang, "base.auction_fee_split", 2, 0));
        if (lang == "en") shown.Should().Contain("] 12 hours    ").And.Contain("(5% base + 0% tax)");
        else shown.Should().NotContain("hours").And.NotContain("tax)");
        ((int[])typeof(BaseLocation).GetField("AuctionDurations", FS)!.GetValue(null)!).Should().Equal(12, 24, 48, 72);
    }

    // ---------- 7. notices to another player, in that player's language ----------

    private void Player(string key, string display, string lang) =>
        Exec("INSERT INTO players (username, display_name, player_data, language, last_login) VALUES (@u, @d, @p, @l, datetime('now'));",
            ("@u", key), ("@d", display), ("@p", $"{{\"player\":{{\"name2\":\"{display}\",\"level\":50,\"hp\":100}}}}"), ("@l", lang));

    private void Exec(string sql, params (string Name, object Value)[] args)
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private List<(string From, string To, string Type, string Message)> Mails()
    {
        var list = new List<(string, string, string, string)>();
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT from_player, to_player, message_type, message FROM messages ORDER BY id;";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    private async Task<TradeOffer> Offer(bool withItems)
    {
        string items = withItems
            ? JsonSerializer.Serialize(new[] { InventoryItemData.FromItem(new Item { Name = "Blade", Type = ObjType.Weapon, Value = 10 }) },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
            : "[]";
        long id = await Db.CreateTradeOffer("sender_key", "taker", items, 100, "");
        return (await Db.GetTradeOffer(id))!;
    }

    private void RunAsTaker(string method, TradeOffer offer)
    {
        InLanguage("en", () =>
        {
            var (term, _) = Term();
            var hero = Hero(); hero.Name2 = "Taker";
            Call(At(new Plain(), term, hero), method, Db, offer);
            return 0;
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ADeclinedPackage_MailsTheSender_InTheSendersLanguage(bool withItems)
    {
        Player("sender_key", "Sender", "hu");
        Player("taker", "Taker", "en");
        RunAsTaker("DeclineTradeOffer", await Offer(withItems));
        var mail = Mails().Should().ContainSingle().Subject;
        mail.From.Should().Be("System", "the sender name stays the stored System identifier");
        mail.To.Should().Be("Sender", "the same address SendMessage gives the save key");
        mail.Type.Should().Be("trade");
        mail.Message.Should().Be(L("hu", withItems ? "base.trade_mail_declined_items" : "base.trade_mail_declined_gold", "Taker"));
        mail.Message.Should().NotContain("declined");
    }

    [Fact]
    public async Task AnAcceptedPackage_MailsTheSender_InTheSendersLanguage()
    {
        Player("sender_key", "Sender", "hu");
        Player("taker", "Taker", "en");
        RunAsTaker("AcceptTradeOffer", await Offer(false));
        var mail = Mails().Should().ContainSingle().Subject;
        mail.Should().Be(("System", "Sender", "trade", L("hu", "base.trade_mail_accepted", "Taker")));
    }

    [Fact]
    public void ABounty_MailsTheTarget_InTheTargetsLanguage()
    {
        Player("poster", "Poster", "en");
        Player("victim", "Victim", "hu");
        InLanguage("en", () =>
        {
            var (term, _) = Term("Victim\n500\n");
            var hero = Hero(); hero.Name2 = "Poster"; hero.Gold = 10_000;
            Call(At(new Plain(), term, hero), "PlaceBounty", Db);
            return 0;
        });
        var mail = Mails().Should().ContainSingle().Subject;
        mail.Should().Be(("Poster", "Victim", "bounty", L("hu", "base.bounty_mail_placed", "500")));
    }

    [Fact]
    public void LiveNotices_GoThroughThePerRecipientLanguagePaths()
    {
        string src = Src();
        src.Should().NotContain("MudServer.Instance?.SendToPlayer(", "every live notice to another player is built in that player's language");
        Regex.Matches(src, @"MudServer\.Instance\?\.SendToPlayerLocalized\(").Count.Should().Be(6);
        src.Should().Contain("MudServer.Instance?.BroadcastLocalized(");
        Regex.Matches(src, @"backend\.SendMessage\(").Count.Should().Be(1, "only player-written mail (/mail) is stored as typed");
    }

    // ---------- 7b. the mailbox, trade and bounty screens, rendered ----------

    private static readonly FieldInfo SaveBackend = typeof(SaveSystem).GetField("backend", F)!;

    /// <summary>Renders a BaseLocation screen that reads SaveSystem's backend, with this test's database behind it.</summary>
    private string Screen(string lang, string method, string input = "Q\n")
    {
        var before = SaveBackend.GetValue(SaveSystem.Instance);
        SaveBackend.SetValue(SaveSystem.Instance, Db);
        try
        {
            return InLanguage(lang, () =>
            {
                var (term, output) = Term(input);
                Call(At(new Plain(), term, Hero()), method);
                return Shown(term, output);
            });
        }
        finally { SaveBackend.SetValue(SaveSystem.Instance, before); }
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task MailboxScreen_Fits_AndItsHeaderIsInThePlayersLanguage(string lang)
    {
        Player(LongName.ToLower(), LongName, lang);
        await Db.SendMessage(LongName, LongName, "mail", new string('m', 200));
        string shown = Screen(lang, "ShowMailbox");
        Capture($"town-base-mailbox-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} mailbox");
        shown.Should().Contain(BaseLocation.Cell(L(lang, "base.from_label"), 16) + " " + BaseLocation.Cell(L(lang, "base.col_date"), 12));
        if (lang == "hu") shown.Should().NotContain("From").And.NotContain("Message");
        string bar = Rows(shown).Single(r => r.StartsWith("[R]"));
        if (lang == "en") bar.Should().Be("[R]ead #  [S]end  [D]elete #  [N]ext Page  [P]rev Page  [Q]uit", "the English bar is unchanged");
        else bar.Should().Be("[R] Olvasás #  [S] Küldés  [D] Törlés #  [N] Következő  [P] Előző  [Q] Kilépés");
        foreach (var lang2 in AllLanguages)
            InLanguage(lang2, () =>
            {
                foreach (var k in new[] { "read", "send", "delete", "next", "prev", "quit" })
                    BaseLocation.MenuKeyLabel("X", L(lang2, "base.mail_bar_" + k)).Tail.Should().NotStartWith("]");
                return 0;
            });
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task TradeScreen_Fits_AndItsRowsAreInThePlayersLanguage(string lang)
    {
        string me = LongName.ToLower();
        Player(me, LongName, lang);
        Player("other_key", "Other", "en");
        await Db.CreateTradeOffer("other_key", me, "[]", 999_999_999, "");
        await Db.CreateTradeOffer(me, "other_key", "[]", 0, "");
        string shown = Screen(lang, "ShowTradeMenu");
        Capture($"town-base-trade-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} trade menu");
        shown.Should().Contain(L(lang, "base.trade_sent_row", "Other", L(lang, "base.trade_empty_package")));
        if (lang == "hu") shown.Should().NotContain("(pending)").And.NotContain("(empty)").And.NotContain(" To ");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task BountyBoard_Fits_AndIsInThePlayersLanguage(string lang)
    {
        Player("victim_key", LongName, "en");
        await Db.PlaceBounty(LongName.ToLower(), LongName.ToLower(), 1_000_000_000);
        string shown = Screen(lang, "ShowBountyMenu");
        Capture($"town-base-bounty-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} bounty board");
        shown.Should().Contain(InLanguage(lang, BaseLocation.BountyHeader)).And.Contain(L(lang, "anchor_road.gold_amount", "1,000,000,000"));
        if (lang == "en") shown.Should().Contain("1,000,000,000 gold ");
        else shown.Should().NotContain("Posted By").And.NotContain("Target").And.NotContain(" gold");
    }

    // ---------- 8. identifiers kept as their readers expect ----------

    [Fact]
    public void Identifiers_StayWhatTheirReadersExpect()
    {
        string src = Src();
        // mail senders: PruneOldSystemMail matches from_player = "System"; the Auction House name is the sale mail sender
        src.Should().Contain("SendMessageLocalized(\"System\", offer.FromPlayer, \"trade\"");
        src.Should().Contain("SendMessageLocalized(\"Auction House\", listing.Seller, \"auction\"");
        SqlSaveBackend.SystemMailSender.Should().Be("System");
        // the guard is a monster name: CombatEngine reads names for drop chance and undead checks
        foreach (var lang in AllLanguages)
        {
            string guard = L(lang, "base.royal_guard");
            guard.Should().NotBeNullOrWhiteSpace();
            foreach (var word in new[] { "Boss", "Chief", "Lord", "King", "Skeleton", "Zombie", "Ghost", "Lich", "Wraith", "Vampire", "Undead", "Revenant", "Demon", "Devil" })
                guard.Should().NotContain(word, $"{lang}: CombatEngine.cs reads monster names for {word}");
        }
        // typed slash commands are passed as literals, never through a key
        foreach (var lang in AllLanguages)
            foreach (var p in LangJson(lang).EnumerateObject().Where(p => p.Name.StartsWith("base.help_arg_")))
                p.Value.GetString()!.Should().StartWith("<").And.EndWith(">").And.NotContain("/");
        src.Should().Contain("SrAliasRow(\"/stats\", \"%\"").And.Contain("WriteCmdAlias(\"/stats\", \"%\"");
    }

    // ---------- 9. the shared rows of a sample of child locations ----------

    public static IEnumerable<object[]> ChildSample() =>
        from lang in new[] { "en", "hu" }
        from place in new[] { "main", "inn", "bank", "magic", "temple", "alley" }
        from mana in new[] { true, false }
        select new object[] { lang, place, mana };

    private static BaseLocation Child(string where) => where switch
    {
        "main" => new MainStreetLocation(),
        "inn" => new InnLocation(),
        "bank" => new BankLocation(),
        "magic" => new MagicShopLocation(),
        "temple" => new TempleLocation(),
        _ => new DarkAlleyLocation(),
    };

    [Theory]
    [MemberData(nameof(ChildSample))]
    public void ChildLocations_SharedRows_Fit(string lang, string where, bool mana)
    {
        string shown = InLanguage(lang, () =>
        {
            var (term, output) = Term();
            var hero = Hero(mana ? CharacterClass.Magician : CharacterClass.Warrior);
            hero.Gold = GoldCap; hero.Stamina = 61000;
            var loc = At(Child(where), term, hero);
            Call(loc, "ShowStatusLine");
            Call(loc, "ShowBBSStatusLine");
            Call(loc, "ShowBBSQuickCommands");
            return Shown(term, output);
        });
        Capture($"town-base-chrome-{where}-{(mana ? "mana" : "stamina")}-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} {where} shared rows");
    }

    // ---------- 10. the keys ----------

    [Fact]
    public void EveryKeyThisFileUses_IsInFiveLanguages_WithTheSamePlaceholders()
    {
        var langs = AllLanguages.ToDictionary(l => l, LangJson);
        var source = Src();
        var used = Regex.Matches(source, "Loc\\.Get(?:In)?\\((?:lang, )?\"([a-z0-9_.]+)\"").Select(m => m.Groups[1].Value).Where(k => !k.EndsWith("_"))
            .Concat(new[] { "base.help_arg_msg", "base.help_arg_name", "base.help_arg_action", "base.help_arg_player", "base.help_arg_guild",
                "base.help_arg_rank", "base.trade_mail_declined_items", "base.trade_mail_declined_gold" })
            .Concat(new[] { "read", "send", "delete", "next", "prev", "quit" }.Select(id => "base.mail_bar_" + id))
            .Concat(new[] { "weapon", "helm", "armor", "arms", "gloves", "ring", "legs", "boots", "belt", "necklace", "face", "shield",
                "cloak", "food", "drink", "magic", "potion", "item" }.Select(id => "base.item_type_" + id))
            .Distinct().ToList();
        used.Count.Should().BeGreaterThan(300);
        var sameInHu = new HashSet<string> { "base.height_cm", "base.weight_kg", "base.prompt_mp", "base.bonus_maxmp", "base.help_arg_rank" };
        foreach (var key in used)
        {
            var en = langs["en"].TryGetProperty(key, out var e) ? e.GetString() : null;
            en.Should().NotBeNull($"{key} is in en.json");
            var holes = Regex.Matches(en!, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).ToList();
            foreach (var lang in AllLanguages.Skip(1))
            {
                langs[lang].TryGetProperty(key, out var v).Should().BeTrue($"{key} is in {lang}.json");
                v.GetString().Should().NotBeNullOrWhiteSpace();
                Regex.Matches(v.GetString()!, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).Should().Equal(holes, $"{lang} {key} keeps the placeholders");
            }
        }
        foreach (var key in used.Where(k => k.StartsWith("base.buff_") || k.StartsWith("base.founder_") || k.StartsWith("base.trade_mail") ||
                                            k.StartsWith("base.auction_") || k.StartsWith("base.col_") || k.StartsWith("base.news_") || k.StartsWith("base.item_type_")))
            if (!sameInHu.Contains(key))
                langs["hu"].GetProperty(key).GetString().Should().NotBe(langs["en"].GetProperty(key).GetString(), $"{key} is translated");
        foreach (var lang in AllLanguages)
            foreach (var key in used)
            {
                string v = langs[lang].GetProperty(key).GetString()!;
                v.Should().NotContain("\u2014").And.NotContain("\u2013");
            }
    }
}
