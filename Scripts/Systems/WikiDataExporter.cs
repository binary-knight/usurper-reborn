using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using UsurperRemake.Data;
using UsurperRemake.Locations;

namespace UsurperRemake.Systems;

/// <summary>Build-only, read-only export of fixed player reference data.</summary>
public static class WikiDataExporter
{
    public const int SchemaVersion = 1;
    private static readonly string[] Languages = ["en", "es", "fr", "hu", "it"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Handles <c>--export-wiki &lt;dir&gt;</c>. Returns null when the flag is absent, otherwise the exit code.</summary>
    public static int? RunCommandLine(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var flag = Array.IndexOf(args, "--export-wiki");
        if (flag < 0) return null;
        if (flag + 1 >= args.Length || string.IsNullOrWhiteSpace(args[flag + 1]) || args[flag + 1].StartsWith("--"))
        {
            stderr.WriteLine("Usage: UsurperReborn --export-wiki <output directory>");
            return 2;
        }
        var outputDir = Path.GetFullPath(args[flag + 1]);
        Export(outputDir);
        stdout.WriteLine($"Wiki data exported to: {outputDir}");
        return 0;
    }

    /// <summary>Writes the built-in datasets. Values come from shipped defaults, not from the current
    /// difficulty, server multipliers or loaded spell and ability overrides.</summary>
    public static void Export(string outputDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        Loc.Initialize();
        Directory.CreateDirectory(outputDir);

        Write(outputDir, "items", Items());
        Write(outputDir, "monsters", Monsters());
        Write(outputDir, "classes", Classes());
        Write(outputDir, "races", Races());
        Write(outputDir, "spells", Spells());
        Write(outputDir, "abilities", Abilities());
        Write(outputDir, "gods", Gods());
        Write(outputDir, "mental", Mental());
        Write(outputDir, "balance", Balance());
        Write(outputDir, "achievements", Achievements());
        Write(outputDir, "bosses", Bosses());
        Write(outputDir, "characters", Characters());

        File.WriteAllText(Path.Combine(outputDir, "meta.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            gameVersion = GameConfig.Version,
            generatedAtUtc = DateTimeOffset.UtcNow,
            commit = Commit(),
            languages = Languages
        }, JsonOptions));
    }

    private static string? Commit()
    {
        var sha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(sha)) return sha.Trim();
        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            });
            if (git == null) return null;
            var output = git.StandardOutput.ReadToEnd().Trim();
            if (!git.WaitForExit(5000) || git.ExitCode != 0) return null;
            return Regex.IsMatch(output, "^[0-9a-f]{40}$") ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Write<T>(string outputDir, string fileName, T data)
    {
        File.WriteAllText(Path.Combine(outputDir, fileName + ".json"), JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            gameVersion = GameConfig.Version,
            data
        }, JsonOptions));
    }

    private static Dictionary<string, string> Names(string? key, string english)
    {
        var names = new Dictionary<string, string> { ["en"] = english };
        if (key == null) return names;
        foreach (var lang in Languages)
            if (Loc.HasIn(lang, key)) names[lang] = Loc.GetIn(lang, key);
        return names;
    }

    private static string KeyPart(string name) =>
        Regex.Replace(name, "([a-z])([A-Z])", "$1_$2").ToLowerInvariant();

    private static object Items() => EquipmentDatabase.GetBuiltInTemplates().Select(item => new
    {
        id = item.Id,
        name = Names(ItemNames.KeyOf(item.Name), item.Name),   // v1.2.5: each language its own name; en is the stored name
        description = item.Description,
        slot = item.Slot,
        handedness = item.Handedness,
        weaponType = item.WeaponType,
        armorType = item.ArmorType,
        weightClass = item.WeightClass,
        rarity = item.Rarity,
        value = item.Value,
        sellValue = item.SellValue,
        minLevel = item.MinLevel,
        strengthRequired = item.StrengthRequired,
        requiresGood = item.RequiresGood,
        requiresEvil = item.RequiresEvil,
        classes = item.ClassRestrictions.OrderBy(x => x).ToArray(),
        stats = new
        {
            item.WeaponPower, item.ArmorClass, item.ShieldBonus, item.BlockChance,
            item.StrengthBonus, item.DexterityBonus, item.ConstitutionBonus,
            item.IntelligenceBonus, item.WisdomBonus, item.CharismaBonus,
            item.MaxHPBonus, item.MaxManaBonus, item.DefenceBonus, item.StaminaBonus,
            item.AgilityBonus, item.CriticalChanceBonus, item.CriticalDamageBonus,
            item.MagicResistance, item.PoisonDamage, item.LifeSteal,
            item.ManaSteal, item.ArmorPiercing, item.Thorns, item.HPRegen, item.ManaRegen
        },
        item.IsUnique, item.IsCursed, item.HasFireEnchant, item.HasFrostEnchant,
        item.HasLightningEnchant, item.HasPoisonEnchant, item.HasHolyEnchant,
        item.HasShadowEnchant, item.HasBossSlayer, item.HasTitanResolve
    }).ToArray();

    private static object Monsters() => MonsterFamilies.GetBuiltInFamilies()
        .OrderBy(f => f.FamilyName).Select(family => new
        {
            id = KeyPart(family.FamilyName),
            name = Names(MonsterNames.FamilyKeyOf(family.FamilyName), family.FamilyName),   // v1.2.5: monster.family.*
            description = family.Description,
            attackType = family.AttackType,
            tiers = family.Tiers.Select(tier => new
            {
                id = KeyPart(tier.Name),
                name = Names(MonsterNames.KeyOf(tier.Name), tier.Name),   // v1.2.5: monster.name.*; en is the stored name
                tier.MinLevel, tier.MaxLevel, tier.PowerMultiplier,
                abilities = tier.SpecialAbilities,
                normalStatsAtMinLevel = MonsterStats(tier.MinLevel, tier.PowerMultiplier),
                normalStatsAtMaxLevel = MonsterStats(tier.MaxLevel, tier.PowerMultiplier)
            }).ToArray()
        }).ToArray();

    private static object MonsterStats(int level, float powerMultiplier)
    {
        var s = MonsterGenerator.GetWikiStats(level, powerMultiplier);
        return new { level, s.HP, s.Strength, s.Defence, s.Punch, s.WeaponPower, s.ArmorPower };
    }

    private static object Classes() => GameConfig.ClassStartingAttributes.OrderBy(x => x.Key)
        .Select(entry => new
        {
            id = entry.Key.ToString(),
            name = Names("class." + KeyPart(entry.Key.ToString()), GameConfig.ClassNames[(int)entry.Key]),
            prestige = entry.Key >= CharacterClass.Tidesworn && entry.Key <= CharacterClass.Voidreaver,
            allowedRaces = Enum.GetValues<CharacterRace>()
                .Where(race => !GameConfig.InvalidCombinations.GetValueOrDefault(race, []).Contains(entry.Key))
                .ToArray(),
            startingAttributes = entry.Value,
            growthPerLevel = ClassGrowth(entry.Key),
            // 1.3.1 wiki depth: what the game itself holds for a class: whether it has mana, the roles it lists
            // (a starting class through its specializations, a prestige class through the fallback list of
            // SpecializationData.GetDefaultRolesForClass), the training points of a level before Intelligence
            // and Wisdom, and what it may equip
            usesMana = new Character { Class = entry.Key }.IsManaClass,
            defaultRoles = SpecializationData.GetDefaultRolesForClass(entry.Key),
            trainingPointsPerLevel = TrainingSystem.CalculateTrainingPointsPerLevel(new Character { Class = entry.Key }),
            equipment = ClassEquipment(entry.Key),
            specializations = SpecializationData.GetSpecsForClass(entry.Key).OrderBy(s => s.Spec)
                .Select(spec => new
                {
                    id = spec.Spec.ToString(),
                    name = Names(spec.NameKey, spec.Name),   // v1.2.5: es fr hu it through spec.{class}.{spec}.name; en is the table name
                    description = Names(spec.DescriptionKey, "").GetValueOrDefault("en", ""),
                    role = spec.Role,
                    bonusesPerLevel = new
                    {
                        spec.BonusStrength, spec.BonusConstitution, spec.BonusMaxHP,
                        spec.BonusDefence, spec.BonusIntelligence, spec.BonusWisdom,
                        spec.BonusCharisma, spec.BonusMaxMana, spec.BonusDexterity,
                        spec.BonusAgility, spec.BonusStamina
                    }
                }).ToArray()
        }).ToArray();

    private static object ClassGrowth(CharacterClass characterClass)
    {
        const long baseline = 100;
        var character = new Character
        {
            Class = characterClass, Race = CharacterRace.Human,
            BaseStrength = baseline, BaseDexterity = baseline,
            BaseConstitution = baseline, BaseIntelligence = baseline,
            BaseWisdom = baseline, BaseCharisma = baseline,
            BaseMaxHP = baseline, BaseMaxMana = baseline,
            BaseDefence = baseline, BaseStamina = baseline, BaseAgility = baseline
        };
        LevelMasterLocation.ApplyClassStatIncreases(character);
        return new
        {
            strength = character.BaseStrength - baseline,
            dexterity = character.BaseDexterity - baseline,
            constitution = character.BaseConstitution - baseline,
            intelligence = character.BaseIntelligence - baseline,
            wisdom = character.BaseWisdom - baseline,
            charisma = character.BaseCharisma - baseline,
            maxHP = character.BaseMaxHP - baseline,
            maxMana = character.BaseMaxMana - baseline,
            defence = character.BaseDefence - baseline,
            stamina = character.BaseStamina - baseline,
            agility = character.BaseAgility - baseline
        };
    }

    /// <summary>
    /// 1.3.1 wiki depth: what a class may equip, asked of the game's own check (Equipment.CanEquip) on the items
    /// the shops stock (ShopItemGenerator.GenerateAllShopItems, built from the weapon and shield templates loot
    /// uses too). The probe character is level 100 with Strength far above any requirement, so only the class
    /// list, the Mystic Shaman main hand rule and the prestige bypass decide. Counts are templates, not items:
    /// one template gives one shop item per level step. Bows are left out: four bow names exist twice in the
    /// templates with different class lists, and a dropped bow is matched to a template by name alone.
    /// </summary>
    private static object ClassEquipment(CharacterClass characterClass)
    {
        var probe = new Character
        {
            Class = characterClass, Race = CharacterRace.Human, Level = GameConfig.MaxLevel,
            Strength = int.MaxValue, BaseStrength = int.MaxValue
        };
        var stock = ShopStock.Value;
        return new
        {
            // a body piece of each weight with no class list and no requirement, put to the same check
            armorWeights = Enum.GetValues<ArmorWeightClass>().Where(w => w != ArmorWeightClass.None
                && new Equipment { Slot = EquipmentSlot.Body, WeightClass = w }.CanEquip(probe, out _)).ToArray(),
            weapons = TemplateCounts(stock.Where(e => e.Slot == EquipmentSlot.MainHand), probe),
            shields = TemplateCounts(stock.Where(e => e.Slot == EquipmentSlot.OffHand), probe)
        };
    }

    private static readonly Lazy<List<Equipment>> ShopStock = new(() => ShopItemGenerator.GenerateAllShopItems()
        .Where(e => e.WeaponType != WeaponType.None && e.WeaponType != WeaponType.Bow).ToList());

    private static object TemplateCounts(IEnumerable<Equipment> items, Character probe) => items
        .GroupBy(e => e.WeaponType).OrderBy(g => g.Key)
        .Select(g => new
        {
            type = g.Key,
            usable = g.Where(e => e.CanEquip(probe, out _)).Select(e => e.Family).Distinct().Count(),
            total = g.Select(e => e.Family).Distinct().Count()
        }).ToArray();

    /// <summary>
    /// 1.3.1 wiki depth: character rules that are data or public functions: proficiency tiers, the experience
    /// of each level, the weapon and shield templates with their class lists, the armour type weights, and the
    /// named constants the Characters guides print through directives.
    /// </summary>
    private static object Characters()
    {
        var weaponTemplates = LootGenerator.GetWeaponTemplates();
        return new
        {
            proficiency = Enum.GetValues<TrainingSystem.ProficiencyLevel>().Select(level => new
            {
                id = level.ToString(),
                effectMultiplier = TrainingSystem.GetEffectMultiplier(level),
                rollModifier = TrainingSystem.GetRollModifier(level),
                failureChance = TrainingSystem.GetFailureChance(level),
                // the tier above costs (progress needed) x (training points per progress); none above Legendary
                trainingPointsToNext = level == TrainingSystem.ProficiencyLevel.Legendary
                    ? (int?)null : TrainingSystem.GetTotalCostToNextLevel(level, 0),
                improveByUseChance = TrainingSystem.GetCombatImprovementChance(level)
            }).ToArray(),
            experienceByLevel = Enumerable.Range(1, GameConfig.MaxLevel).Select(level => new
            {
                level,
                totalExperience = GameConfig.GetExperienceForLevel(level),
                killExperienceMultiplier = Math.Round(GameConfig.GetEarlyGameXPMultiplier(level), 2)
            }).ToArray(),
            // the weapon type and the hands are decided as the shop decides them: by name
            // (ShopItemGenerator.InferWeaponType), two hands from LootGenerator.TwoHandedWeaponStartIndex on
            weaponTemplates = weaponTemplates.Select((t, index) => (t, type: ShopItemGenerator.InferWeaponType(t.Name), index))
                .Where(x => x.type != WeaponType.Bow)
                .Select(x => new
                {
                    name = x.t.Name, weaponType = x.type,
                    handedness = x.index >= LootGenerator.TwoHandedWeaponStartIndex
                        ? WeaponHandedness.TwoHanded : ShopItemGenerator.InferHandedness(x.type),
                    classes = x.t.Classes, minLevel = x.t.MinLevel, maxLevel = x.t.MaxLevel
                }).ToArray(),
            shieldTemplates = LootGenerator.GetShieldTemplates().Select(t => new
            {
                name = t.Name, weaponType = ShopItemGenerator.InferShieldType(t.Name),
                classes = t.Classes, minLevel = t.MinLevel, maxLevel = t.MaxLevel
            }).ToArray(),
            armorTypeWeights = Enum.GetValues<ArmorType>().Where(a => a != ArmorType.None)
                .Select(a => new { armorType = a, weightClass = a.GetWeightClass() }).ToArray(),
            constants = CharacterConstants()
        };
    }

    private static SortedDictionary<string, object?> CharacterConstants()
    {
        var constants = Named(typeof(GameConfig), string.Empty,
            nameof(GameConfig.AlchemistPotionMasteryBonus), nameof(GameConfig.HerbHealPercent),
            nameof(GameConfig.HerbBuffDuration), nameof(GameConfig.HerbSwiftDuration),
            nameof(GameConfig.MagicianArcaneSpellBonus), nameof(GameConfig.ClericDivineGraceBonus),
            nameof(GameConfig.PaladinDivineResolveDamageBonus), nameof(GameConfig.PaladinDivineResolveStatusResist),
            nameof(GameConfig.BardInspirationChance), nameof(GameConfig.BardInspirationAttackBonus),
            nameof(GameConfig.BardInspirationDuration), nameof(GameConfig.JesterTrickstersLuckChance),
            nameof(GameConfig.JesterLuckBonusDamage), nameof(GameConfig.JesterLuckStaminaRefund),
            nameof(GameConfig.AssassinLethalPrecisionCritBonus), nameof(GameConfig.AssassinLethalPrecisionPoisonBonus),
            nameof(GameConfig.ShamanEnchantBaseDamage), nameof(GameConfig.ShamanElementalMastery),
            nameof(GameConfig.ShamanEnchantPowerCap), nameof(GameConfig.ShamanEnchantDuration),
            nameof(GameConfig.ShamanTotemBaseDuration), nameof(GameConfig.ShamanTotemHealPercent),
            nameof(GameConfig.ShamanTotemHealPercentGroup), nameof(GameConfig.ShamanWindfuryProcChance),
            nameof(GameConfig.ShamanSpiritLinkPercent), nameof(GameConfig.TideswornOceansBlessingBonus),
            nameof(GameConfig.TideswornOceansResiliencePercent), nameof(GameConfig.TideswornOceansResilienceBelowHalfBonus),
            nameof(GameConfig.WavecallerHarmonicResonanceBonus), nameof(GameConfig.WavecallerOceansVoiceCritBonus),
            nameof(GameConfig.WavecallerReflectionPercent), nameof(GameConfig.CalmWatersShieldDuration),
            nameof(GameConfig.CalmWatersResistChance), nameof(GameConfig.CyclebreakerPassiveEvadePercent),
            nameof(GameConfig.CyclebreakerDebuffResistChance), nameof(GameConfig.CyclebreakerCycleXPBonus),
            nameof(GameConfig.CyclebreakerCycleXPBonusCap), nameof(GameConfig.AbysswardenAbyssalSiphonPercent),
            nameof(GameConfig.AbysswardenPrisonWardResist), nameof(GameConfig.AbysswardenCorruptionHealPercent),
            nameof(GameConfig.VoidreaverVoidHungerPercent), nameof(GameConfig.VoidreaverSoulEaterManaPercent),
            nameof(GameConfig.VoidreaverPainThresholdBonus), nameof(GameConfig.VoidreaverReflectionPercent),
            nameof(GameConfig.HealerSpecHealBonus), nameof(GameConfig.SoftTauntStickChance),
            nameof(GameConfig.BossChannelInterruptChance), nameof(GameConfig.BossChannelInterruptSpeedThreshold),
            nameof(GameConfig.RespecBaseGoldCost), nameof(GameConfig.RespecGoldPerLevel),
            nameof(GameConfig.NPCProficiencyCap), nameof(GameConfig.CompanionProficiencyCap),
            nameof(GameConfig.DefaultStartingGold), nameof(GameConfig.DefaultStartingHealing),
            nameof(GameConfig.MaxLevel));
        // the Awakening boons, as AwakeningBonusWisdom1 and so on
        foreach (var entry in Named(typeof(AwakeningBonus), nameof(AwakeningBonus),
            nameof(AwakeningBonus.Wisdom1), nameof(AwakeningBonus.Mana2), nameof(AwakeningBonus.XP3),
            nameof(AwakeningBonus.HP4), nameof(AwakeningBonus.Damage5), nameof(AwakeningBonus.Defense6),
            nameof(AwakeningBonus.Stage7Extra)))
            constants[entry.Key] = entry.Value;
        return constants;
    }

    /// <summary>Named public constants of a type, by exact name; a name that is not a constant fails the export.</summary>
    private static SortedDictionary<string, object?> Named(Type type, string prefix, params string[] names) =>
        new(names.ToDictionary(n => prefix + n, n =>
        {
            var field = type.GetField(n, BindingFlags.Public | BindingFlags.Static);
            if (field == null || !field.IsLiteral) throw new InvalidOperationException(type.Name + "." + n);
            return field.GetRawConstantValue();
        }));

    private static object Races() => GameConfig.RaceAttributes.OrderBy(x => x.Key)
        .Select(entry => new
        {
            id = entry.Key.ToString(),
            name = Names("race." + KeyPart(entry.Key.ToString()), GameConfig.RaceNames[(int)entry.Key]),
            bonusesAndCreationRanges = entry.Value,
            npcLifespanYears = GameConfig.RaceLifespan[entry.Key]
        }).ToArray();

    private static object Spells() => SpellSystem.BuiltInTemplate().Select(spell =>
    {
        var info = SpellSystem.GetSpellInfo(spell.Class, spell.Level);
        var key = "spell." + spell.Class.ToString().ToLowerInvariant() + "." + spell.Level;
        return new
        {
            id = spell.Class + ":" + spell.Level,
            classId = spell.Class,
            spell.Level,
            name = Names(key + ".name", spell.Name ?? ""),
            description = Names(key + ".desc", spell.Description ?? ""),
            // 1.2.8: what the game applies, not the stored template values: a cast charges 10 + 5 per slot
            // before Wisdom (the template ManaCost is never charged), and the level that opens the spell is
            // the higher of the slot's level and the listed one
            ManaCost = SpellSystem.BaseManaCost(spell.Level),
            LevelRequired = SpellSystem.LevelGate(spell.Level, spell.LevelRequired ?? 0),
            spell.MagicWords,
            info.IsMultiTarget, info.SpellType
        };
    }).ToArray();

    private static object Abilities() => ClassAbilitySystem.BuiltInTemplate().Select(values =>
    {
        var ability = ClassAbilitySystem.GetAbility(values.Id)!;
        return new
        {
            id = values.Id,
            name = Names(null, ability.Name),
            description = Names(null, ability.Description),
            type = ability.Type,
            classes = ability.AvailableToClasses,
            requiredWeaponTypes = ability.RequiredWeaponTypes,
            ability.RequiresShield, ability.CanTargetAlly, ability.SpecialEffect,
            values.LevelRequired, values.StaminaCost, values.ManaCost, values.Cooldown,
            values.BaseDamage, values.BaseHealing, values.DefenseBonus,
            values.AttackBonus, values.Duration
        };
    }).ToArray();

    private static object Gods()
    {
        var pantheon = new GodSystem().GetAllGods().ToDictionary(g => g.Name);
        var tiers = new[]
        {
            new { name = GodFavorTier.Follower, minFavor = GameConfig.GodFavorMin, strengthPercent = GodBoonSystem.TierStrengthPct(GodFavorTier.Follower) },
            new { name = GodFavorTier.Devout, minFavor = GameConfig.GodFavorTierDevoutMin, strengthPercent = GodBoonSystem.TierStrengthPct(GodFavorTier.Devout) },
            new { name = GodFavorTier.Zealot, minFavor = GameConfig.GodFavorTierZealotMin, strengthPercent = GodBoonSystem.TierStrengthPct(GodFavorTier.Zealot) },
            new { name = GodFavorTier.Chosen, minFavor = GameConfig.GodFavorTierChosenMin, strengthPercent = GodBoonSystem.TierStrengthPct(GodFavorTier.Chosen) }
        };
        var gods = GameConfig.CanonGodNames.Select(name =>
        {
            var god = pantheon[name];
            var domain = GodBoonSystem.DomainOfCanon(name);
            return new
            {
                id = name.ToLowerInvariant(),
                name = Names(null, name),
                domain,
                domainName = Names("god.domain." + domain.ToString().ToLowerInvariant(), domain.ToString()),
                description = god.Properties.GetValueOrDefault("Description")?.ToString() ?? "",
                echoesOldGod = god.Properties.GetValueOrDefault("EchoesOldGod")?.ToString() ?? "",
                wards = GodBoonSystem.WardsOf(domain),
                boonConstants = Constants("GodBoon" + name),
                deedsAndTaboos = Enum.GetValues<GodAct>().Select(act => new { act, favor = GodDeedSystem.Worth(act, domain) })
                    .Where(x => x.favor != 0).ToArray()
            };
        }).ToArray();
        return new
        {
            tiers,
            favorRules = Constants("GodFavor"),
            gods
        };
    }

    private static object Mental() => new
    {
        max = GameConfig.MaxMentalStability,
        bands = new[]
        {
            new { name = MentalBand.Broken, min = 0, max = 0 },
            new { name = MentalBand.Breaking, min = GameConfig.MentalBreakingThreshold, max = GameConfig.MentalShakenThreshold - 1 },
            new { name = MentalBand.Shaken, min = GameConfig.MentalShakenThreshold, max = GameConfig.MentalStrainedThreshold - 1 },
            new { name = MentalBand.Strained, min = GameConfig.MentalStrainedThreshold, max = GameConfig.MentalStableThreshold - 1 },
            new { name = MentalBand.Stable, min = GameConfig.MentalStableThreshold, max = GameConfig.MaxMentalStability }
        },
        constants = Constants("Mental")
    };

    private static object Balance() => new
    {
        moddableDefaults = new BalanceConfig(),
        characterConstants = Constants("Specialization"),
        combatConstants = Constants("CriticalHit", "Backstab", "Berserk", "BaseMonster", "EarlyFloor"),
        godConstants = Constants("GodBoon", "GodWard", "GodDeed", "GodTaboo"),
        mentalConstants = Constants("Mental")
    };

    private static SortedDictionary<string, object?> Constants(params string[] prefixes) =>
        new(typeof(GameConfig).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly && prefixes.Any(prefix => field.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(field => field.Name)
            .ToDictionary(field => field.Name, field => field.GetRawConstantValue()));

    private static object Achievements() => AchievementSystem.GetBuiltInAchievements()
        .OrderBy(a => a.Id).Select(a => new
        {
            id = a.Id, name = Names(a.KeyOf("name", a.Name), a.Name), description = Names(a.KeyOf("desc", a.Description), a.Description),   // v1.2.5: es fr hu it through achievement.{id}.*
            a.Category, a.Tier, a.IsSecret, spoiler = a.IsSecret, a.SecretHint, a.PointValue,
            a.GoldReward, a.ExperienceReward
        }).ToArray();

    private static object Bosses()
    {
        var oldGods = OldGodsData.GetAllOldGods().Select(b => new
        {
            id = b.Type.ToString(), kind = "oldGod", spoiler = true,
            name = Names(MonsterNames.KeyOf(b.Name), b.Name), b.Title, baseLevel = b.Level, b.DungeonFloor,   // v1.2.5: oldgod.{key}.name
            baseHP = b.HP, baseStrength = b.Strength, baseDefence = b.Defence,
            baseAgility = b.Agility, b.AttacksPerRound,
            phases = new[] { b.Phase1Abilities, b.Phase2Abilities, b.Phase3Abilities }
        }).ToArray();
        var world = WorldBossDatabase.GetAllBosses().OrderBy(b => b.Id).Select(b => new
        {
            id = b.Id, kind = "world", spoiler = false,
            name = Names(null, b.Name), b.Title, b.Element, b.BaseLevel,
            b.BaseHP, b.BaseStrength, b.BaseDefence, b.BaseAgility,
            b.AttacksPerRound,
            phases = new[] { b.Phase1Abilities, b.Phase2Abilities, b.Phase3Abilities }
                .Select(group => group.Select(a => new
                {
                    a.Name, a.Description, a.DamageMultiplier, a.AppliedStatus,
                    a.StatusDuration, a.IsAoE, a.IsUnavoidable, a.SelfHealPercent, a.Kind
                }).ToArray()).ToArray()
        }).ToArray();
        var manager = SecretBossManager.Instance;
        var secret = Enum.GetValues<SecretBossType>().Select(type => manager.GetBoss(type))
            .Where(b => b != null).Select(b => new
            {
                id = b!.Type.ToString(), kind = "secret", spoiler = true,
                name = Names(null, b.Name), b.Title, b.FloorLevel, b.BaseLevel,
                baseHP = b.Stats.HP, baseAttack = b.Stats.Attack,
                baseDefense = b.Stats.Defense, baseMagicPower = b.Stats.MagicPower,
                baseSpeed = b.Stats.Speed, baseCritChance = b.Stats.CritChance,
                b.Abilities
            }).ToArray();
        return new { oldGods, world, secret };
    }
}
