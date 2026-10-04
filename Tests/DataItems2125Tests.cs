using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5 (data-items2): rolled loot is stored in English whatever the dropper's language and shown in each
/// reader's language through ItemNames, part by part (rarity or curse word, effect word, template, a world boss
/// element word kept English). A name rolled in another language before 1.2.5 loads, shows as stored and still
/// resolves its gear set. The loot templates without keys got them, the remaining raw item-name sites show
/// the reader's language, and every row they print fits 79 columns with the longest rolled name.
/// </summary>
[Collection("SharedGameSingletons")]
public class DataItems2125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";
    private const int LongestTableName = 27;   // "Robes of the Grand Sorcerer", DataItems125Tests

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] OtherLanguages = { "es", "fr", "hu", "it" };

    /// <summary>The loot templates that had no key before this piece; their old drops kept the English base.</summary>
    private static readonly string[] NewTemplates =
    {
        "Ranger's Leather", "Noble's Vestments", "Regal Coat", "Sovereign's Raiment", "Imperial Regalia Armor",
        "Noble Circlet", "Sovereign's Crown", "Imperial Diadem", "Herald's Armguards", "Regal Vambraces",
        "Orator's Gloves", "Majestic Gauntlets", "Noble's Mantle", "Glamour Cloak", "Sovereign's Cape",
        "Reinforced Buckler", "Forged Buckler", "Runed Buckler", "Ring of Charm", "Siren's Band",
        "Ring of the Sovereign", "Crown Jewel Ring", "Ring of Allure", "Pendant of Glamour", "Orator's Medallion",
        "Siren's Choker", "Amulet of the Muse", "Imperial Regalia",
    };

    // ---------- helpers ----------

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static Screen At(BaseLocation location, Character hero, params string[] lines)
    {
        var s = NewScreen(lines);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return s;
    }

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            return await body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static T InLang<T>(string lang, Func<T> body) =>
        InLanguage(lang, () => Task.FromResult(body())).GetAwaiter().GetResult();

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 100, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 2_000_000_000, Strength = 500,
    };

    private static PlayerData Save(Character hero) =>
        (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;

    private static Character Reload(Character hero)
    {
        var data = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(Save(hero)))!;
        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
        try { return (Character)restore.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "Scripts" }.Concat(parts).ToArray()));

    private sealed class ZeroRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0;
    }

    private static readonly FieldInfo LootRandom = typeof(LootGenerator).GetField("random", SNP)!;
    private static readonly FieldInfo DungeonRandom = typeof(DungeonLocation).GetField("dungeonRandom", F)!;

    private static T Seeded<T>(int seed, Func<T> body)
    {
        var prev = LootRandom.GetValue(null);
        LootRandom.SetValue(null, new Random(seed));
        try { return body(); }
        finally { LootRandom.SetValue(null, prev); }
    }

    private static readonly string[] Elements = { "Water", "Void", "Shadow", "Fire", "Undead", "Physical", "Poison", "Eldritch" };

    /// <summary>The same rolls the base capture made (Tests/Fixtures/loot-names-base-3b913e0.tsv).</summary>
    private static global::Item Roll(int s)
    {
        var classes = (CharacterClass[])Enum.GetValues(typeof(CharacterClass));
        int level = 1 + (s * 7) % 100;
        var cls = classes[s % classes.Length];
        return Seeded(s, () => (s % 8) switch
        {
            0 => LootGenerator.GenerateWorldBossLoot(level, LootGenerator.ItemRarity.Rare, Elements[s % Elements.Length], cls),
            1 => LootGenerator.GenerateRing(level),
            2 => LootGenerator.GenerateNecklace(level),
            3 => LootGenerator.GenerateShield(level),
            4 => LootGenerator.GenerateBossLoot(level, cls),
            _ => LootGenerator.GenerateDungeonLoot(level, cls),
        });
    }

    /// <summary>What the code at 3b913e0 stored for each seed in each language (seeds 0 to 399).</summary>
    private static Dictionary<(string Lang, int Seed), string> BaseNames()
    {
        var map = new Dictionary<(string, int), string>();
        foreach (var line in File.ReadAllLines(Path.Combine(RepoRoot(), "Tests", "Fixtures", "loot-names-base-3b913e0.tsv")))
        {
            var p = line.Split('\t');
            map[(p[0], int.Parse(p[1]))] = p[2];
        }
        return map;
    }

    private static readonly int[] Seeds = Enumerable.Range(0, 400).ToArray();

    private static string Stats(global::Item i) =>
        $"{i.Attack}/{i.Armor}/{i.Strength}/{i.Dexterity}/{i.Agility}/{i.Wisdom}/{i.Charisma}/{i.Defence}/{i.HP}/{i.Mana}/{i.Value}/" +
        $"{i.ShieldBonus}/{i.BlockChance}/{i.IsCursed}/{i.Rarity}/{i.Family}/" + string.Join(",", i.LootEffects.Select(e => $"{e.EffectType}:{e.Value}"));

    /// <summary>The longest name each language's generator form gives a loot template, and the English one.</summary>
    private static (string English, string Family, Dictionary<string, string> Shown) LongestRolled()
    {
        string longestElement = LootGenerator.WorldBossNamePrefixes.OrderByDescending(p => p.Length).First();
        string bestFamily = "";
        string bestEnglish = "";
        foreach (var t in ItemNames.LootTemplates())
            foreach (var f in LootGenerator.AllNameFormsFor(t, "en"))
                if (f.Length > bestEnglish.Length) { bestEnglish = f; bestFamily = t; }
        var shown = new Dictionary<string, string>();
        foreach (var lang in AllLanguages)
        {
            var (t, i) = ItemNames.LootTemplates()
                .SelectMany(t => LootGenerator.AllNameFormsFor(t, lang).Select((f, i) => (t, i, f)))
                .OrderByDescending(x => x.f.Length).Select(x => (x.t, x.i)).First();
            string english = LootGenerator.AllNameFormsFor(t, "en").ElementAt(i);
            shown[lang] = $"{longestElement} {ItemNames.DisplayIn(lang, english, t)}";
        }
        return ($"{longestElement} {bestEnglish}", bestFamily, shown);
    }

    // ---------- 1. the templates that had no key ----------

    [Fact]
    public void EveryLootTemplate_HasItsKey_TranslatedInEveryLanguage()
    {
        var all = LootGenerator.GetWeaponTemplates().Select(t => t.Name)
            .Concat(LootGenerator.GetBodyArmorTemplates().Select(t => t.Name)).Concat(LootGenerator.GetHeadArmorTemplates().Select(t => t.Name))
            .Concat(LootGenerator.GetArmsArmorTemplates().Select(t => t.Name)).Concat(LootGenerator.GetHandsArmorTemplates().Select(t => t.Name))
            .Concat(LootGenerator.GetLegsArmorTemplates().Select(t => t.Name)).Concat(LootGenerator.GetFeetArmorTemplates().Select(t => t.Name))
            .Concat(LootGenerator.GetWaistArmorTemplates().Select(t => t.Name)).Concat(LootGenerator.GetFaceArmorTemplates().Select(t => t.Name))
            .Concat(LootGenerator.GetCloakArmorTemplates().Select(t => t.Name)).Concat(LootGenerator.GetShieldTemplates().Select(t => t.Name))
            .Concat(LootGenerator.GetRingTemplates().Select(t => t.Name)).Concat(LootGenerator.GetNecklaceTemplates().Select(t => t.Name))
            .Distinct().ToList();
        foreach (var t in all)
            ItemNames.KeyOf(t).Should().NotBeNull($"the loot template {t} shows through its key");
        foreach (var t in NewTemplates)
        {
            all.Should().Contain(t);
            string key = LootGenerator.TemplateLocKey(t);
            Loc.GetIn("en", key).Should().Be(t);
            foreach (var lang in OtherLanguages)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{key} in {lang}");
                string shown = Loc.GetIn(lang, key);
                shown.Should().NotBe(t, $"{key} is translated in {lang}");
                shown.Length.Should().BeLessOrEqualTo(LongestTableName, $"{key} in {lang} is no longer than the longest English item name");
            }
        }
    }

    // ---------- 2. a new drop is stored in English ----------

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void ANewDrop_IsStoredInEnglish_WhateverTheDroppersLanguage_ByteForByteAsBefore(string lang)
    {
        var baseNames = BaseNames();
        foreach (var s in Seeds)
        {
            var item = InLang(lang, () => Roll(s));
            item.Name.Should().Be(baseNames[("en", s)], $"seed {s} rolled in {lang} is stored as the English drop of 3b913e0");
        }
    }

    [Theory]
    [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void ANewDrop_HasTheSameStats_InEveryLanguage(string lang)
    {
        // The thematic bonuses read English keywords in the stored name; a name stored in another language missed
        // them, so the same roll gave a different item by language.
        foreach (var s in Seeds)
            Stats(InLang(lang, () => Roll(s))).Should().Be(Stats(InLang("en", () => Roll(s))), $"seed {s} in {lang}");
    }

    [Fact]
    public void ANewDrop_ShowsInEachReadersLanguage_TheNameTheOldCodeStoredForThatLanguage()
    {
        var baseNames = BaseNames();
        foreach (var s in Seeds)
        {
            var item = InLang("hu", () => Roll(s));
            foreach (var lang in AllLanguages)
            {
                string expected = baseNames[(lang, s)];
                // a template that had no key before was stored with its English base in every language
                if (NewTemplates.Contains(item.Family))
                    expected = expected.Replace(item.Family, Loc.GetIn(lang, LootGenerator.TemplateLocKey(item.Family)));
                ItemNames.DisplayIn(lang, item).Should().Be(expected, $"seed {s} read in {lang}");
                InLang(lang, () => ItemNames.Display(item)).Should().Be(expected);
            }
        }
    }

    [Fact]
    public void EveryLootForm_InEveryLanguage_ShowsAsTheGeneratorBuildsIt()
    {
        var withoutFamily = new List<string>();
        int forms = 0;
        foreach (var t in ItemNames.LootTemplates())
        {
            var english = LootGenerator.AllNameFormsFor(t, "en").ToList();
            foreach (var lang in AllLanguages)
            {
                var built = LootGenerator.AllNameFormsFor(t, lang).ToList();
                for (int i = 0; i < english.Count; i++)
                {
                    forms++;
                    ItemNames.DisplayIn(lang, english[i], t).Should().Be(built[i], $"{english[i]} on {t} in {lang}");
                    if (ItemNames.DisplayIn(lang, english[i]) != built[i]) withoutFamily.Add($"{lang} {t}: {english[i]}");
                }
            }
        }
        forms.Should().BeGreaterThan(100_000);
        // Without a stored Family a whole template wins over a split: Fine Dagger, Holy Mace and Spiked Flail are
        // templates of their own, so the Dagger, Mace and Flail drops with those words read as them in es, fr, it.
        withoutFamily.Should().BeEquivalentTo(new[]
        {
            "es Dagger: Fine Dagger", "fr Dagger: Fine Dagger", "it Dagger: Fine Dagger",
            "es Mace: Holy Mace", "fr Mace: Holy Mace", "it Mace: Holy Mace",
            "es Flail: Spiked Flail", "fr Flail: Spiked Flail", "it Flail: Spiked Flail",
        });
    }

    [Fact]
    public void AWorldBossDrop_KeepsItsElementWord_AndShowsTheRestInTheReadersLanguage()
    {
        string hu(string k) => Loc.GetIn("hu", k);
        foreach (var element in LootGenerator.WorldBossNamePrefixes)
        {
            string stored = $"{element} Holy Glaive";
            string expected = $"{element} {hu("item.effect.holy_damage.prefix")} {hu("item.glaive")}";
            ItemNames.DisplayIn("hu", stored, "Glaive").Should().Be(expected);
            ItemNames.DisplayIn("hu", stored).Should().Be(expected);
            ItemNames.DisplayIn("hu", stored + " +4 Dex", "Glaive").Should().Be(expected + " +4 Dex", "the enchant parts stay as stored");
            ItemNames.DisplayIn("en", stored, "Glaive").Should().Be(stored);
        }
    }

    [Fact]
    public void ABasicDrop_IsStoredInEnglish_AndShowsItsSlotWord()
    {
        var weapon = InLang("hu", () => (global::Item)typeof(LootGenerator).GetMethod("CreateBasicWeapon", SNP)!
            .Invoke(null, new object[] { 10, LootGenerator.ItemRarity.Artifact })!);
        weapon.Name.Should().Be("Mythic Weapon");
        ItemNames.DisplayIn("hu", weapon).Should().Be($"{Loc.GetIn("hu", "item.rarity.mythic")} {Loc.GetIn("hu", "item.slot.weapon")}");
        var boots = InLang("fr", () => (global::Item)typeof(LootGenerator).GetMethod("CreateBasicArmor", SNP)!
            .Invoke(null, new object[] { 10, LootGenerator.ItemRarity.Common, ObjType.Feet })!);
        boots.Name.Should().Be("Boots");
        // "Boots" is also a table name; a whole template is read first, either way not English
        ItemNames.DisplayIn("it", boots).Should().Be(ItemNames.KeyOf("Boots") is string bootsKey ? Loc.GetIn("it", bootsKey) : Loc.GetIn("it", "item.slot.boots"));
        ItemNames.DisplayIn("it", boots).Should().NotBe("Boots");
    }

    // ---------- 3. names stored in another language before 1.2.5 ----------

    [Fact]
    public void ALegacyNameRolledInHungarian_LoadsShowsAsStored_AndResolvesItsGearSet()
    {
        string set = LootGenerator.GetBodyArmorTemplates().Select(t => t.Name).First(t => GearSetRegistry.ForFamily(t) != null);
        // what 1.2.4 stored for a Hungarian player: the hu effect word and the hu base
        string legacy = LootGenerator.AllNameFormsFor(set, "hu").First(f => f.StartsWith(Loc.GetIn("hu", "item.effect.fire_damage.prefix") + " "));
        legacy.Should().NotContain(set);

        var hero = Hero();
        hero.Inventory.Add(new global::Item { Name = legacy, Type = ObjType.Body, Armor = 20, Family = "", IsIdentified = true });
        var worn = new Equipment { Name = legacy, Slot = EquipmentSlot.Body, ArmorClass = 20, Family = "" };
        EquipmentDatabase.RegisterDynamic(worn);
        hero.EquippedItems[EquipmentSlot.Body] = worn.Id;

        var back = InLang("hu", () => Reload(hero));
        var bag = back.Inventory.Single(i => i.Type == ObjType.Body);
        var wornBack = back.GetEquipment(EquipmentSlot.Body)!;
        bag.Name.Should().Be(legacy, "a legacy name is never rewritten on load");
        wornBack.Name.Should().Be(legacy);
        foreach (var lang in AllLanguages)
        {
            ItemNames.DisplayIn(lang, bag).Should().Be(legacy, $"a legacy name shows as stored ({lang})");
            ItemNames.DisplayIn(lang, wornBack).Should().Be(legacy);
        }
        GearSetFamilyResolver.FamilyOf(bag).Should().Be(set, "the resolver still reads a Hungarian name");
        GearSetFamilyResolver.FamilyOf(wornBack).Should().Be(set);
    }

    // ---------- 4. save and reload ----------

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void RolledLoot_SurvivesSaveAndReload_StoredEnglish_ShownInTheReadersLanguage(string lang)
    {
        var baseNames = BaseNames();
        var hero = Hero();
        var rolls = new[] { 4, 5, 13, 27 }.Select(s => (Seed: s, Item: InLang(lang, () => Roll(s)))).ToList();
        foreach (var (_, item) in rolls) { item.IsIdentified = true; hero.Inventory.Add(item); }
        var wornItem = rolls.Select(r => r.Item).First(i => i.Type == ObjType.Weapon);
        var worn = Character.BuildEquipmentFromItem(wornItem, EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword);
        EquipmentDatabase.RegisterDynamic(worn);
        hero.EquippedItems[EquipmentSlot.MainHand] = worn.Id;

        var back = InLang(lang, () => Reload(hero));
        for (int i = 0; i < rolls.Count; i++)
        {
            var (seed, item) = rolls[i];
            var loaded = back.Inventory[i];
            loaded.Name.Should().Be(baseNames[("en", seed)], "the stored name is English whatever the language");
            loaded.Family.Should().Be(item.Family);
            string expected = NewTemplates.Contains(item.Family)
                ? baseNames[(lang, seed)].Replace(item.Family, Loc.GetIn(lang, LootGenerator.TemplateLocKey(item.Family)))
                : baseNames[(lang, seed)];
            InLang(lang, () => ItemNames.Display(loaded)).Should().Be(expected);
        }
        var wornBack = back.GetEquipment(EquipmentSlot.MainHand)!;
        wornBack.Name.Should().Be(wornItem.Name);
        InLang(lang, () => ItemNames.Display(wornBack)).Should().Be(ItemNames.DisplayIn(lang, wornItem));
    }

    // ---------- 5. groups and recorded combat lines ----------

    [Fact]
    public void ADropSeenByAGroupOfFiveLanguages_IsStoredOnce_InEnglish_AndEachReaderSeesTheirOwn()
    {
        var drop = InLang("hu", () => Roll(13));
        drop.Name.Should().Be(InLang("en", () => Roll(13)).Name);
        var shown = AllLanguages.ToDictionary(l => l, l => ItemNames.DisplayIn(l, drop));
        shown["en"].Should().Be(drop.Name);
        foreach (var lang in OtherLanguages)
        {
            shown[lang].Should().NotBe(drop.Name, $"{lang} reads its own words");
            L(lang, "combat.loot_added_inventory", shown[lang]).Should().Contain(shown[lang]);
        }
        // the per-reader loot lines pass the item, not its name, so the stored Family reads the drop
        Src("Systems", "CombatEngine.cs").Should().NotContain("lootItem.Name)}").And.NotContain("DisplayIn(followerLang, lootItem.Name)");
    }

    [Fact]
    public void ARecordedLine_ShowsTheItemInEachMembersLanguage_AndNeverReplacesItAsFreeText()
    {
        var drop = InLang("hu", () => Roll(13));
        string huName = ItemNames.DisplayIn("hu", drop);
        var (captured, rec) = InLang("hu", () =>
        {
            var r = Loc.BeginRecording();
            try { return (Loc.Get("combat.loot_added_inventory", ItemNames.Display(drop)) + "\n" + "[" + huName + "]", r); }
            finally { Loc.EndRecording(r); }
        });
        var rows = Rows(rec.Render(captured, "fr"));
        rows[0].Should().Be(L("fr", "combat.loot_added_inventory", ItemNames.DisplayIn("fr", drop)));
        rows[1].Should().Be("[" + huName + "]", "a shown name is only an argument, not free text to replace");
        Rows(rec.Render(captured, "en"))[0].Should().Be(L("en", "combat.loot_added_inventory", drop.Name));
    }

    // ---------- 6. the sites that printed the raw name ----------

    private static Equipment Piece(string name, string family, EquipmentSlot slot, int minLevel = 1)
    {
        var e = new Equipment
        {
            Name = name, Family = family, Slot = slot, MinLevel = minLevel, Value = 5000,
            WeaponPower = slot == EquipmentSlot.MainHand ? 40 : 0, ArmorClass = slot == EquipmentSlot.MainHand ? 0 : 20,
            Handedness = slot == EquipmentSlot.MainHand ? WeaponHandedness.OneHanded : WeaponHandedness.None,
        };
        EquipmentDatabase.RegisterDynamic(e);
        return e;
    }

    private static async Task<string> AcceptDeath(string lang, Character hero)
    {
        return await InLanguage(lang, async () =>
        {
            var s = NewScreen("1", "", "", "");
            var engine = new CombatEngine(s.Term);
            typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new ZeroRandom());
            await (Task)typeof(CombatEngine).GetMethod("PresentResurrectionChoices", F)!.Invoke(engine, new object[] { new CombatResult { Player = hero } })!;
            return s.Text;
        });
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task DeathPenalty_NamesTheLostAndUnequippedGear_InTheReadersLanguage_AndFits(string lang)
    {
        var (longest, family, _) = LongestRolled();
        var hero = Hero();
        hero.Level = 10; hero.Resurrections = 0; hero.TempleResurrectionsUsed = 3; hero.Darkness = 0;
        var lost = Piece(longest, family, EquipmentSlot.MainHand);
        var demoted = Piece(longest, family, EquipmentSlot.Head, minLevel: 8);
        hero.EquippedItems[EquipmentSlot.MainHand] = lost.Id;
        hero.EquippedItems[EquipmentSlot.Head] = demoted.Id;

        string text = await AcceptDeath(lang, hero);
        Capture($"data-items2-death-{lang}.txt", text);
        string shown = ItemNames.DisplayIn(lang, lost);
        string flat = Regex.Replace(text, @"\s+", " ");
        flat.Should().Contain(Regex.Replace(L(lang, "combat.death_item_destroyed", shown), @"\s+", " ").Trim());
        flat.Should().Contain(Regex.Replace(L(lang, "combat.death_unequipped", L(lang, "combat.death_unequipped_item", shown, 8)), @"\s+", " ").Trim());
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"\"{row}\" ({lang})");
        if (lang == "en") text.Should().Contain($"Item destroyed: {longest}");
    }

    [Fact]
    public async Task DeathPenalty_WithNothingWorn_SaysNone_InTheReadersLanguage()
    {
        var hero = Hero();
        hero.Level = 1; hero.Resurrections = 0; hero.TempleResurrectionsUsed = 3;
        (await AcceptDeath("hu", hero)).Should().Contain(L("hu", "combat.death_item_destroyed", L("hu", "combat.death_item_none")).Trim());
        (await AcceptDeath("en", hero)).Should().Contain("Item destroyed: none");
    }

    [Theory]
    [InlineData("hu")] [InlineData("it")]
    public async Task PvPSalvage_NamesThePieceItPriced_InTheReadersLanguage(string lang)
    {
        var sword = EquipmentDatabase.GetBuiltInTemplates().First(e => e.Slot == EquipmentSlot.MainHand && e.Value > 1000 && ItemNames.KeyOf(e.Name) != null);
        var hero = Hero();
        hero.Level = 50;
        var foe = new NPC { ID = "npc_salvage_" + lang, Name1 = "Foe", Name2 = "Foe", Level = 50, HP = 0, MaxHP = 400 };
        foe.EquippedItems[EquipmentSlot.MainHand] = sword.Id;
        string text = await InLanguage(lang, async () =>
        {
            var s = NewScreen("", "", "", "");
            var engine = new CombatEngine(s.Term);
            typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new ZeroRandom());
            await (Task)typeof(CombatEngine).GetMethod("DeterminePvPOutcome", F)!.Invoke(engine, new object[] { new CombatResult { Player = hero, Opponent = foe } })!;
            return s.Text;
        });
        string shown = ItemNames.DisplayIn(lang, sword);
        text.Should().Contain(L(lang, "combat.equipment_salvaged"));
        Rows(text).Should().Contain(r => r.StartsWith("  • " + shown + " "), "the priced piece is named, in the reader's language");
        text.Should().NotContain("• " + L(lang, "ui.none"), "the empty legacy slot's word is not the salvaged item");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("fr")] [InlineData("it")] [InlineData("es")]
    public async Task DungeonMerchant_ShowsItsWaresInTheReadersLanguage_AndEveryRowFits(string lang)
    {
        var hero = Hero();
        hero.Gold = 0;
        var dungeon = new DungeonLocation();
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(dungeon, 60);
        var s = At(dungeon, hero, "A", "", "L", "", "");
        string text = await InLanguage(lang, async () =>
        {
            var prev = LootRandom.GetValue(null);
            LootRandom.SetValue(null, new Random(11));
            DungeonRandom.SetValue(dungeon, new Random(3));
            try { await (Task)typeof(DungeonLocation).GetMethod("MerchantTradeMenu", F)!.Invoke(dungeon, new object[] { hero })!; }
            finally { LootRandom.SetValue(null, prev); }
            return s.Text;
        });
        Capture($"data-items2-merchant-{lang}.txt", text);
        DungeonRandom.SetValue(dungeon, new Random(3));
        var wares = InLang(lang, () => Seeded(11, () => (System.Collections.IList)typeof(DungeonLocation).GetMethod("GenerateMerchantRareItems", F)!.Invoke(dungeon, new object[] { 60 })!));
        wares.Count.Should().BeGreaterThan(0);
        string flat = Regex.Replace(text, @"\s+", " ");
        foreach (var w in wares)
        {
            var loot = (global::Item)w.GetType().GetProperty("LootItem")!.GetValue(w)!;
            string stored = (string)w.GetType().GetProperty("Name")!.GetValue(w)!;
            stored.Should().Be(loot.Name);
            flat.Should().Contain(ItemNames.DisplayIn(lang, loot), $"the ware {loot.Name} shows in {lang}");
        }
        var first = (global::Item)wares[0]!.GetType().GetProperty("LootItem")!.GetValue(wares[0])!;
        long price = (long)wares[0]!.GetType().GetProperty("Price")!.GetValue(wares[0])!;
        flat.Should().Contain(Regex.Replace(L(lang, "dungeon.merchant_need_gold", price, ItemNames.DisplayIn(lang, first)), @"\s+", " ").Trim(),
            "a ware the player cannot afford is named in the reader's language");
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"\"{row}\" ({lang})");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("it")]
    public void DungeonMerchant_AWareWithTheLongestRolledName_Wraps(string lang)
    {
        var (longest, family, _) = LongestRolled();
        var merchantType = typeof(DungeonLocation).GetNestedType("MerchantRareItem", BindingFlags.NonPublic)!;
        var ware = Activator.CreateInstance(merchantType)!;
        var loot = new global::Item { Name = longest, Family = family, Type = ObjType.Weapon, Attack = 100, IsIdentified = true };
        merchantType.GetProperty("Name")!.SetValue(ware, longest);
        merchantType.GetProperty("LootItem")!.SetValue(ware, loot);
        merchantType.GetProperty("Price")!.SetValue(ware, 9_999_999L);
        merchantType.GetProperty("Description")!.SetValue(ware, InLang(lang, () => L(lang, "dungeon.merchant_stat_atk", 999)));
        var hero = Hero();
        hero.Gold = 0;
        var dungeon = new DungeonLocation();
        var s = At(dungeon, hero, "", "", "");
        InLanguage(lang, async () =>
        {
            await (Task)typeof(DungeonLocation).GetMethod("PurchaseRareItem", F)!.Invoke(dungeon, new object[] { hero, ware })!;
            return 0;
        }).GetAwaiter().GetResult();
        string text = s.Text;
        string flat = Regex.Replace(text, @"\s+", " ");
        flat.Should().Contain(ItemNames.DisplayIn(lang, loot));
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"\"{row}\" ({lang})");
    }

    [Fact]
    public void ElectronSlotPicker_NamesTheWornItemAndTheSlots_InTheReadersLanguage()
    {
        var hero = Hero();
        var ring = Piece("Ring of Charm", "Ring of Charm", EquipmentSlot.LFinger);
        hero.EquippedItems[EquipmentSlot.LFinger] = ring.Id;
        InLang("hu", () => InventorySystem.SlotPickCurrent(hero, EquipmentSlot.LFinger)).Should().Be(Loc.GetIn("hu", "item.ring_of_charm"));
        InLang("hu", () => InventorySystem.SlotPickCurrent(hero, EquipmentSlot.RFinger)).Should().Be(Loc.GetIn("hu", "home.slot_empty"));
        InLang("en", () => InventorySystem.SlotPickCurrent(hero, EquipmentSlot.RFinger)).Should().Be("(empty)");
        L("en", "inventory.slot_pick_left_finger").Should().Be("Left Finger");
        L("en", "inventory.slot_pick_right_finger").Should().Be("Right Finger");
        L("en", "equip.slot.MainHand").Should().Be("Main Hand");
        L("en", "equip.slot.OffHand").Should().Be("Off Hand");
        string src = Src("Systems", "InventorySystem.cs");
        src.Should().Contain("label = Loc.Get(\"inventory.slot_pick_left_finger\"), current = SlotPickCurrent(player, EquipmentSlot.LFinger)");
        src.Should().Contain("label = Loc.Get(\"equip.slot.MainHand\"), current = SlotPickCurrent(player, EquipmentSlot.MainHand)");
        src.Should().NotContain("?.Name ?? \"(empty)\"");
    }

    [Fact]
    public void NpcPurchaseNews_NamesTheItemInTheWritersLanguage()
    {
        var npc = new NPC { ID = "npc_shopper_d10", Name1 = "Shopper", Name2 = "Shopper", Level = 30, HP = 300, MaxHP = 300, Gold = 50_000_000 };
        var sim = new WorldSimulator();
        typeof(WorldSimulator).GetField("random", F)!.SetValue(sim, new ZeroRandom());
        var before = NewsSystem.Instance.GetTodaysNews().Count;
        InLang("hu", () => { typeof(WorldSimulator).GetMethod("NPCGoShopping", F)!.Invoke(sim, new object[] { npc }); return 0; });
        var bought = npc.EquippedItems.Values.Select(EquipmentDatabase.GetById).OfType<Equipment>().ToList();
        bought.Should().NotBeEmpty();
        var news = NewsSystem.Instance.GetTodaysNews().Skip(Math.Max(0, before - 5)).ToList();
        news.Should().Contain(n => bought.Any(b => n.Contains(L("hu", "worldsim.news_purchased", "Shopper", ItemNames.DisplayIn("hu", b)))));
    }

    [Fact]
    public void SleepMurderMail_NamesTheStolenItemInTheSleepersLanguage()
    {
        var drop = InLang("en", () => Roll(13));
        WorldSimulator.SleepMurderMail("hu", LongName, 50, drop.Name, drop.Family)
            .Should().Be(L("hu", "worldsim.mail_sleep_murder_item", LongName, $"{50:N0}", ItemNames.DisplayIn("hu", drop)));
        WorldSimulator.SleepMurderMail("en", "Bo", 50, drop.Name, drop.Family).Should().Be($"Bo murdered you in your sleep! Lost 50 gold and {drop.Name}.");
        Src("Systems", "WorldSimulator.cs").Should().Contain("SleepMurderMail(lang, attackerNPC.Name2, stolenGold, stolenItemName, stolenItemFamily)");
    }

    [Fact]
    public void IdentifyInCombat_ListsTheBelongingsInTheReadersLanguage()
    {
        var drop = InLang("en", () => Roll(13));
        var caster = Hero();
        caster.Inventory.Add(drop);
        string text = InLang("hu", () =>
        {
            var s = NewScreen();
            var engine = new CombatEngine(s.Term);
            typeof(CombatEngine).GetMethod("HandleSpecialSpellEffect", F)!.Invoke(engine, new object?[] { caster, null, "identify", 0 });
            return s.Text;
        });
        text.Should().Contain(L("hu", "combat.identify_item_row", ItemNames.DisplayIn("hu", drop), drop.Type, drop.Attack, drop.Armor));
    }

    [Fact]
    public void AnUnknownWornItemId_ReadsInTheReadersLanguage()
    {
        var hero = Hero();
        hero.RHand = 987654;
        InLang("hu", () => hero.WeaponName).Should().Be(L("hu", "item.unknown_id", 987654));
        InLang("en", () => hero.WeaponName).Should().Be("Unknown Item #987654");
    }

    [Fact]
    public void AnUnidentifiedItem_ReadsInTheReadersLanguage_EnglishAsBefore()
    {
        var item = new global::Item { Name = "Blazing Long Sword", Family = "Long Sword", Type = ObjType.Weapon, Attack = 250, IsIdentified = false };
        LootGenerator.GetUnidentifiedNameIn("en", item).Should().Be("Glowing Unidentified Weapon");
        LootGenerator.GetUnidentifiedNameIn("hu", item).Should().Be(L("hu", "item.unidentified.glowing", L("hu", "inn.unid_weapon")));
        item.Attack = 150;
        LootGenerator.GetUnidentifiedNameIn("en", item).Should().Be("Shimmering Unidentified Weapon");
        item.Attack = 60; item.Type = ObjType.Hands;
        LootGenerator.GetUnidentifiedNameIn("en", item).Should().Be("Ornate Unidentified Gauntlets");
        LootGenerator.GetUnidentifiedNameIn("it", item).Should().Be(L("it", "item.unidentified.ornate", L("it", "inn.unid_gauntlets")));
        item.Attack = 10; item.Type = ObjType.Food;
        LootGenerator.GetUnidentifiedNameIn("en", item).Should().Be("Unidentified Item");
        InLang("fr", () => LootGenerator.GetUnidentifiedName(item)).Should().Be(L("fr", "inn.equip_slot_unidentified"));
        item.IsIdentified = true; item.Attack = 250; item.Type = ObjType.Weapon;
        LootGenerator.GetUnidentifiedNameIn("hu", item).Should().Be(ItemNames.DisplayIn("hu", item));
        foreach (var lang in OtherLanguages)
            foreach (var k in new[] { "item.unidentified.glowing", "item.unidentified.shimmering", "item.unidentified.ornate" })
                L(lang, k, "X").Should().NotBe(L("en", k, "X"));
    }

    [Fact]
    public void MagicShopPurify_RenamesInTheLanguageTheNameIsStoredIn()
    {
        ItemNames.PurifiedName("Cursed Long Sword").Should().Be("Purified Long Sword");
        foreach (var lang in OtherLanguages)
        {
            string legacy = $"{L(lang, "item.rarity.cursed")} {L(lang, "item.long_sword")}";
            ItemNames.PurifiedName(legacy).Should().Be($"{L(lang, "item.effect.poison_resist.prefix")} {L(lang, "item.long_sword")}", lang);
        }
        ItemNames.PurifiedName("Long Sword").Should().Be("Long Sword");
        ItemNames.PurifiedName("Cursed").Should().Be("Cursed");
        // a purified English drop reads in each language
        ItemNames.DisplayIn("hu", "Purified Long Sword", "Long Sword").Should().Be($"{L("hu", "item.effect.poison_resist.prefix")} {L("hu", "item.long_sword")}");
        string src = Src("Locations", "MagicShopLocation.cs");
        src.Should().Contain("targetItem.Name = ItemNames.PurifiedName(targetItem.Name);");
        src.Should().Contain("targetEquip.Name = ItemNames.PurifiedName(targetEquip.Name);");
    }

    [Fact]
    public void TheSupremeItemsAndTheNpcQualityEffectTexts_AreGone()
    {
        Src("Core", "Items.cs").Should().NotContain("Lantern of Eternal Light").And.NotContain("CreateSupremeItem");
        Src("Systems", "NPCItemGenerator.cs").Should().NotContain("Magic Damage +5").And.NotContain("Drains Life").And.NotContain("Holy Light");
    }

    // ---------- 7. widths ----------

    /// <summary>The rows a rolled drop's name is printed in, as (indent, key, arguments): I the item, U an
    /// unidentified item's name, N a 30-character name, G a large gold amount, P a percentage. The rows this
    /// piece writes wrapped (CombatEngine.WriteItemRow, UIHelper.WriteWrapped) are marked wrapped.</summary>
    private static readonly (string Indent, string Key, string Args, bool Wrapped)[] LootRows =
    {
        ("  ", "", "I", false), ("", "combat.loot_added_inventory", "I", false), ("", "combat.inventory_full_dropped", "I", false),
        ("", "combat.loot_teammate_takes", "NI", false), ("", "combat.loot_teammate_equips", "NI", false), ("", "combat.loot_teammate_passes", "NI", false),
        ("  ", "combat.other_takes", "NI", false), ("", "combat.other_equips", "NI", false), ("  ", "combat.other_passes", "NI", false),
        ("", "combat.loot_ally_picks_up", "NIP", false), ("", "combat.loot_equipped_on_companion", "IN", false), ("", "combat.loot_added_to_companion", "IN", false),
        ("", "combat.loot_group_unidentified", "U", false), ("  ", "", "U", false),
        ("", "combat.loot_displaced_dropped", "I", true), ("", "combat.loot_displaced_to_inventory", "I", true), ("", "combat.loot_displaced_to_player", "IN", true),
        ("", "combat.death_item_destroyed", "I", true), ("", "combat.death_unequipped", "D", true), ("", "combat.salvaged_item_row", "IG", true),
        ("", "dungeon.merchant_need_gold", "GI", true), ("  ", "dungeon.merchant_acquired", "I", true),
    };

    /// <summary>A row as CombatEngine.WriteItemRow writes it: unchanged when it fits, else wrapped under its indent.</summary>
    private static List<string> Wrapped(string row)
    {
        if (row.Length <= MaxWidth) return new List<string> { row };
        string body = row.TrimStart(' ');
        string indent = row.Substring(0, row.Length - body.Length);
        return UIHelper.WordWrap(body, MaxWidth - indent.Length).Select(r => indent + r).ToList();
    }

    private static string RenderRow(string lang, (string Indent, string Key, string Args, bool Wrapped) m, string item, string unidentified)
    {
        object[] args = m.Args.Select(c => (object)(c switch
        {
            'I' => item, 'U' => unidentified, 'N' => LongName, 'G' => "2,000,000,000", 'P' => "999",
            'D' => L(lang, "combat.death_unequipped_item", item, 100),
            _ => throw new ArgumentException(m.Args),
        })).ToArray();
        return m.Indent + (m.Key == "" ? (string)args[0] : L(lang, m.Key, args));
    }

    [Fact]
    public void EveryLootRow_ThatFitsWithTheLongestEnglishRolledName_FitsWithTheLongestShownOne_InEveryLanguage()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        var (english, _, shown) = LongestRolled();
        var unidentifiedTypes = new[] { ObjType.Weapon, ObjType.Body, ObjType.Head, ObjType.Arms, ObjType.Hands, ObjType.Legs, ObjType.Feet,
                                        ObjType.Shield, ObjType.Fingers, ObjType.Neck, ObjType.Waist, ObjType.Face, ObjType.Abody, ObjType.Food };
        string LongestUnidentified(string lang) => unidentifiedTypes
            .Select(t => LootGenerator.GetUnidentifiedNameIn(lang, new global::Item { Type = t, Attack = 250, IsIdentified = false }))
            .OrderByDescending(n => n.Length).First();
        var older = new List<string>();
        var tooWide = new List<string>();
        var sb = new StringBuilder();
        foreach (var m in LootRows)
        {
            if (m.Key != "") Loc.HasIn("en", m.Key).Should().BeTrue(m.Key);
            foreach (var lang in AllLanguages)
            {
                if (m.Wrapped)
                {
                    // written wrapped: every row fits at the longest shown name, a row that fits is unchanged
                    foreach (var r in Wrapped(RenderRow(lang, m, shown[lang], LongestUnidentified(lang))))
                    {
                        sb.AppendLine($"{lang} {r.Length,3} {r}");
                        if (r.Length > MaxWidth) tooWide.Add($"{m.Key} ({lang}, {r.Length}): \"{r}\"");
                    }
                    continue;
                }
                string before = RenderRow(lang, m, english, LongestUnidentified("en"));
                if (before.Length > MaxWidth) { older.Add($"{lang} {m.Key} {before.Length}"); continue; }
                string row = RenderRow(lang, m, shown[lang], LongestUnidentified(lang));
                sb.AppendLine($"{lang} {row.Length,3} {row}");
                if (row.Length > MaxWidth) tooWide.Add($"{m.Key} ({lang}, {row.Length}): \"{row}\"");
            }
        }
        Capture("data-items2-loot-rows.txt", sb + "\nover 79 with the English name already (wrapped where this piece writes them, else older):\n" + string.Join("\n", older));
        tooWide.Should().BeEmpty("the shown name never makes a fitting row too wide");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void AnItemRowTooLong_Wraps_UnderItsIndent_AndARowThatFits_IsUnchanged(string lang)
    {
        var (_, _, shown) = LongestRolled();
        var s = NewScreen();
        var engine = new CombatEngine(s.Term);
        string longRow = L(lang, "combat.loot_displaced_dropped", shown[lang]);
        string shortRow = L(lang, "combat.death_item_destroyed", "X");
        engine.WriteItemRow(longRow);
        engine.WriteItemRow(shortRow);
        var rows = Rows(s.Text).Where(r => r.Length > 0).ToList();
        rows.Should().HaveCountGreaterThan(2);
        rows.Last().Should().Be(shortRow);
        foreach (var r in rows) { r.Length.Should().BeLessOrEqualTo(MaxWidth, r); r.Should().StartWith("  "); }
        Regex.Replace(string.Join(" ", rows.Take(rows.Count - 1)), @"\s+", " ").Trim().Should().Be(Regex.Replace(longRow, @"\s+", " ").Trim());
    }
}
