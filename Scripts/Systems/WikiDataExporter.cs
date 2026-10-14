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
        Write(outputDir, "party", Party());
        Write(outputDir, "economy", Economy());
        Write(outputDir, "combat", Combat());

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

    /// <summary>
    /// 1.3.2 wiki depth: the party. Beasts from BeastData.Beasts, each with the level that meets both gates of a
    /// sighting: the region's own level (WildernessLocation.ExploreRegion refuses a region below it) and the beast's
    /// (asked of BeastData.PickEligibleBeast). The five story companions as a new CompanionSystem defines them, each
    /// with the class it fights as (CompanionSystem.BuildCombatCharacter) and the main hand weapon types its own rule
    /// admits (Equipment.CanEquip on a bare weapon of each type, so no class list, level or Strength decides). The
    /// ally stances (TeammateStances.PolicyFor) and the named constants the party guides print.
    /// </summary>
    private static object Party() => new
    {
        beasts = BeastData.Beasts.Select(b =>
        {
            var region = WildernessData.GetRegionByKey(b.RegionDirectionKey)
                ?? throw new InvalidOperationException("beast region: " + b.Id);
            return new
            {
                id = b.Id,
                name = Names(null, b.Name),   // a beast's name stays English (BeastData.LocSpecies)
                species = Names("dungeon.beast." + b.Id + ".species", b.Species),
                role = b.Role,
                region = new
                {
                    id = region.Id,
                    name = Names("wilderness.region." + region.Id + ".name", region.Name),
                    direction = region.Direction,
                    minLevel = region.MinLevel
                },
                beastMinLevel = b.MinPlayerLevel,
                sightedFromLevel = BeastSightedFromLevel(b, region),
                tameDifficulty = b.TameDifficulty,
                combatBase = b.Role == BeastData.BeastRole.Combat
                    ? new { hp = b.CombatBaseHP, attack = b.CombatBaseAttack, defence = b.CombatBaseDefence }
                    : null
            };
        }).ToArray(),
        companions = BuiltInCompanions().OrderBy(c => c.RecruitLevel).ThenBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => new
            {
                id = c.Id.ToString(),
                name = Names(null, c.Name),
                title = Names(c.KeyBase + ".title", c.Title),
                role = c.CombatRole,
                combatClass = CompanionSystem.BuildCombatCharacter(c).Class,
                recruitLevel = c.RecruitLevel,
                romanceAvailable = c.RomanceAvailable,
                baseStats = new
                {
                    hp = c.BaseStats.HP, attack = c.BaseStats.Attack, defence = c.BaseStats.Defense,
                    magic = c.BaseStats.MagicPower, speed = c.BaseStats.Speed, healing = c.BaseStats.HealingPower
                },
                mainHandWeaponTypes = CompanionMainHandTypes(c)
            }).ToArray(),
        stances = Enum.GetValues<TeammateStance>().Select(s =>
        {
            var p = TeammateStances.PolicyFor(s);
            return new
            {
                id = s,
                p.EmergencySelfPotion, p.PotionMostInjured, p.DefensiveFirst, p.Brace,
                p.MayTaunt, p.TargetWeightMultiplier
            };
        }).ToArray(),
        constants = PartyConstants()
    };

    /// <summary>The lowest level at which a sighting in the beast's region can pick it, or null if none.</summary>
    private static int? BeastSightedFromLevel(BeastData.BeastDefinition beast, WildernessRegion region)
    {
        // owning every other beast leaves this one as the only candidate the game's own pick can return
        var others = BeastData.Beasts.Where(b => b.Id != beast.Id).Select(b => b.Id).ToArray();
        for (int level = region.MinLevel; level <= GameConfig.MaxLevel; level++)
            if (BeastData.PickEligibleBeast(region.DirectionKey, level, new Random(0), others)?.Id == beast.Id)
                return level;
        return null;
    }

    /// <summary>
    /// The five companions as CompanionSystem.InitializeCompanions defines them, before a recruitment scales their
    /// stats. Built on an object whose constructor never runs: the constructor makes the new system the shared
    /// fallback (CompanionSystem.Instance) that other code and tests read, and the export must not replace it.
    /// </summary>
    private static List<Companion> BuiltInCompanions()
    {
        const BindingFlags own = BindingFlags.NonPublic | BindingFlags.Instance;
        var blank = (CompanionSystem)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CompanionSystem));
        var field = typeof(CompanionSystem).GetField("companions", own)
            ?? throw new InvalidOperationException("CompanionSystem.companions");
        var init = typeof(CompanionSystem).GetMethod("InitializeCompanions", own)
            ?? throw new InvalidOperationException("CompanionSystem.InitializeCompanions");
        field.SetValue(blank, new Dictionary<CompanionId, Companion>());
        init.Invoke(blank, null);
        return blank.GetAllCompanions().ToList();
    }

    /// <summary>The main hand weapon types the companion's own rule in Equipment.CanEquip admits.</summary>
    private static WeaponType[] CompanionMainHandTypes(Companion companion)
    {
        var probe = CompanionSystem.BuildCombatCharacter(companion);
        probe.IsCompanion = true;            // as CompanionSystem.GetCompanionsAsCharacters marks a fighting companion
        probe.CompanionId = companion.Id;
        probe.Level = GameConfig.MaxLevel;
        probe.Strength = int.MaxValue;
        return Enum.GetValues<WeaponType>().Where(t => t != WeaponType.None
            && new Equipment { Slot = EquipmentSlot.MainHand, WeaponType = t }.CanEquip(probe, out _)).ToArray();
    }

    private static SortedDictionary<string, object?> PartyConstants()
    {
        var constants = Named(typeof(GameConfig), string.Empty,
            nameof(GameConfig.WildernessMaxDailyExplorations), nameof(GameConfig.ShrineVeloraCharismaBonus),
            nameof(GameConfig.AbandonCompanionLoyaltyPenalty), nameof(GameConfig.CompanionLossPerMurder),
            nameof(GameConfig.MaxRoyalMercenaries), nameof(GameConfig.MercenaryBaseCost),
            nameof(GameConfig.MercenaryCostPerLevel), nameof(GameConfig.PermadeathChanceDungeonTeam),
            nameof(GameConfig.PermadeathLevelReduction), nameof(GameConfig.PermadeathPopulationFloor),
            nameof(GameConfig.PermadeathRaceFloor), nameof(GameConfig.NpcRecruitmentCostPerLevel),
            nameof(GameConfig.NpcRecruitmentCostPerLevelSquared), nameof(GameConfig.RecruitAboveLevelPercentPerLevel),
            nameof(GameConfig.RecruitAboveLevelMaxPercent), nameof(GameConfig.RecruitPriceFriendship),
            nameof(GameConfig.RecruitPriceTrust), nameof(GameConfig.RecruitPriceRespect),
            nameof(GameConfig.RecruitPriceNormal), nameof(GameConfig.RecruitPriceSuspicious),
            nameof(GameConfig.RecruitPriceAnger), nameof(GameConfig.AdultChildRecruitmentDiscount),
            nameof(GameConfig.RecruitPoolPerBand), nameof(GameConfig.RecruitPoolSize),
            nameof(GameConfig.GroupMaxSize), nameof(GameConfig.MaxTeamFacilityLevel));
        // BeastMaxRosterSize, BeastTameAttempts, and the encounter chance under its own name
        foreach (var entry in Named(typeof(BeastData), "Beast", nameof(BeastData.MaxRosterSize), nameof(BeastData.TameAttempts)))
            constants[entry.Key] = entry.Value;
        foreach (var entry in Named(typeof(BeastData), string.Empty, nameof(BeastData.BeastEncounterChancePercent)))
            constants[entry.Key] = entry.Value;
        foreach (var entry in Named(typeof(CompanionSystem), string.Empty, nameof(CompanionSystem.MaxActiveCompanions)))
            constants[entry.Key] = entry.Value;
        // the daily wage and the companion team constants, through the one file that reads the wage constants
        foreach (var entry in TeamSystem.WikiPartyConstants())
            constants[entry.Key] = entry.Value;
        return constants;
    }

    /// <summary>
    /// 1.3.2 wiki depth: items and the town economy. Crafting materials (GameConfig.CraftingMaterials) with what
    /// spends them: the Magic Shop enchant tiers and the Inn's stat training. The enchant tiers, the loot rarity
    /// table and the Home upgrade prices are private to their files, so they are read by reflection, as the party
    /// export reads CompanionSystem: a renamed member fails the export. No game file is changed for this.
    /// A named enchant's effect is what MagicShopLocation.ApplyNamedEnchant adds to a bare item.
    /// </summary>
    private static object Economy()
    {
        var enchants = Enchants();
        var oldGods = OldGodsData.GetAllOldGods();
        return new
        {
            materials = GameConfig.CraftingMaterials.Select(m => new
            {
                id = m.Id,
                name = Names("material." + m.Id + ".name", m.Name),
                floorMin = m.FloorMin,
                floorMax = m.FloorMax,
                enchants = enchants.Where(e => e.materials.Any(x => x.id == m.Id)).Select(e => e.number).ToArray(),
                // the stat trainings at the Inn that take it, counted from 1 (InnLocation.GetTrainingMaterialRequirements)
                statTrainings = Enumerable.Range(1, GameConfig.MaxStatTrainingsPerStat)
                    .Where(n => InnLocation.GetTrainingMaterialRequirements(n - 1)?.Any(r => r.materialId == m.Id) == true)
                    .ToArray()
            }).ToArray(),
            // spoiler: the Old God whose defeat or rescue gives each god's own material (OldGodBossSystem)
            materialGods = oldGods.Select(g => (god: g, material: GameConfig.CraftingMaterials
                    .FirstOrDefault(m => m.ThematicGod == g.Type.ToString())))
                .Where(x => x.material != null)
                .Select(x => new { god = x.god.Type.ToString(), floor = x.god.DungeonFloor, material = x.material!.Id })
                .ToArray(),
            enchants,
            gearSets = GearSetRegistry.Sets.Select(s => new
            {
                id = s.Id,
                name = Names(s.NameKey, Loc.GetIn("en", s.NameKey)),
                families = s.Families,
                tiers = s.Tiers.Select(t => new
                {
                    pieces = t.Pieces,
                    bonuses = t.Bonuses.Select(b => new
                    {
                        stat = b.Stat,
                        amount = b.Amount,
                        text = Loc.GetIn("en", "item.set.bonus." + b.Stat.ToString().ToLowerInvariant(), b.Amount)
                    }).ToArray()
                }).ToArray()
            }).ToArray(),
            lootRarities = LootRarities(),
            homeUpgrades = HomeUpgrades(),
            herbs = Enum.GetValues<HerbType>().Where(h => h != HerbType.None).Select(h => new
            {
                id = h,
                name = Names(null, HerbData.GetName(h)),
                gardenLevel = HerbData.GetGardenLevelRequired(h),
                maxCarry = GameConfig.HerbMaxCarry[(int)h],
                effect = HerbData.GetDescription(h)
            }).ToArray(),
            artifacts = Artifacts(oldGods),
            constants = EconomyConstants()
        };
    }

    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    private static T PrivateStaticField<T>(Type type, string name) =>
        (T)(type.GetField(name, PrivateStatic)?.GetValue(null) ?? throw new InvalidOperationException(type.Name + "." + name));

    private sealed record EnchantMaterial(string id, int count);

    private sealed record EnchantRow(int number, Dictionary<string, string> name, bool oneChosenStat, int statBonus,
        SortedDictionary<string, object> effects, long baseCost, int costPerLevel, int minLevel, EnchantMaterial[] materials);

    /// <summary>The Magic Shop enchant tiers in menu order (numbered from 1), with their materials.</summary>
    private static EnchantRow[] Enchants()
    {
        var tiers = PrivateStaticField<(string name, int bonus, long baseCost, int levelScale, int minLevel, string description)[]>(
            typeof(MagicShopLocation), "EnchantTiers");
        var materials = PrivateStaticField<Dictionary<int, (string materialId, int count)[]>>(
            typeof(MagicShopLocation), "EnchantMaterialRequirements");
        return tiers.Select((t, index) =>
        {
            // a stat tier is the one whose description is the one-stat line; the player picks the stat
            bool oneStat = t.description == "magic_shop.tier_desc_one_stat";
            return new EnchantRow(index + 1, Names(t.name, Loc.GetIn("en", t.name)), oneStat, oneStat ? t.bonus : 0,
                oneStat ? new SortedDictionary<string, object>() : NamedEnchantEffects(index + 1),
                t.baseCost, t.levelScale, t.minLevel,
                materials.TryGetValue(index, out var reqs) ? reqs.Select(r => new EnchantMaterial(r.materialId, r.count)).ToArray() : []);
        }).ToArray();
    }

    /// <summary>Every number or flag on an item that MagicShopLocation.ApplyNamedEnchant changes, by property name.</summary>
    private static SortedDictionary<string, object> NamedEnchantEffects(int tierChoice)
    {
        var bare = new Equipment();
        var enchanted = new Equipment();
        MagicShopLocation.ApplyNamedEnchant(enchanted, tierChoice);
        var effects = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var p in typeof(Equipment).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // stored fields only: a public getter and setter, no index (a computed total would count twice)
            if (!p.CanRead || p.SetMethod?.IsPublic != true || p.GetIndexParameters().Length > 0) continue;
            if (p.PropertyType != typeof(int) && p.PropertyType != typeof(long) && p.PropertyType != typeof(float)
                && p.PropertyType != typeof(double) && p.PropertyType != typeof(bool)) continue;
            var before = p.GetValue(bare);
            var after = p.GetValue(enchanted);
            if (Equals(before, after)) continue;
            effects[p.Name] = p.PropertyType == typeof(bool) ? after! : Convert.ToDouble(after) - Convert.ToDouble(before);
        }
        return effects;
    }

    /// <summary>LootGenerator's rarity table: power and value multipliers, effect counts and curse chance.</summary>
    private static object LootRarities()
    {
        var stats = PrivateStaticField<Dictionary<LootGenerator.ItemRarity, (float PowerMult, float ValueMult, int MinEffects, int MaxEffects, float CurseChance)>>(
            typeof(LootGenerator), "RarityStats");
        return stats.OrderBy(x => x.Key).Select(x => new
        {
            id = x.Key,
            // the word a dropped item's name starts with, none for Common
            namePrefix = LootGenerator.RarityPrefixKey(x.Key) is { } key ? Loc.GetIn("en", key) : null,
            powerMultiplier = x.Value.PowerMult,
            valueMultiplier = x.Value.ValueMult,
            minEffects = x.Value.MinEffects,
            maxEffects = x.Value.MaxEffects,
            curseChance = x.Value.CurseChance
        }).ToArray();
    }

    /// <summary>
    /// The Home upgrades by level. A price is HomeLocation's own cost of going from the level below; the cost
    /// methods are private instance methods that read no state, called on an object whose constructor never runs.
    /// </summary>
    private static object HomeUpgrades()
    {
        var home = (HomeLocation)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(HomeLocation));
        long Cost(string method, int fromLevel)
        {
            var m = typeof(HomeLocation).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("HomeLocation." + method);
            return (long)m.Invoke(home, [fromLevel])!;
        }
        return new
        {
            levels = Enumerable.Range(0, GameConfig.HomeRecoveryPercent.Length).Select(level => new
            {
                level,
                livingQuartersCost = level == 0 ? (long?)null : Cost("GetLivingQuartersCost", level - 1),
                restRecovery = GameConfig.HomeRecoveryPercent[level],
                restsPerDay = GameConfig.HomeRestsPerDay[level],
                bedCost = level == 0 ? (long?)null : Cost("GetBedCost", level - 1),
                bedFertility = GameConfig.BedFertilityModifier[level],
                chestCost = level == 0 ? (long?)null : Cost("GetChestUpgradeCost", level - 1),
                chestCapacity = GameConfig.ChestCapacity[level],
                hearthCost = level == 0 ? (long?)null : Cost("GetHearthCost", level - 1),
                hearthBonus = GameConfig.HearthDamageBonus[level],
                hearthFights = GameConfig.HearthCombatDuration[level],
                gardenCost = level == 0 ? (long?)null : Cost("GetGardenCost", level - 1),
                herbsPerDay = GameConfig.HerbsPerDay[level]
            }).ToArray(),
            // the Training Room: a level a time, each level +1 to six base attributes
            trainingRoom = Enumerable.Range(1, 10).Select(level => new { level, cost = Cost("GetTrainingRoomCost", level - 1) })
                .TakeWhile(x => x.cost != long.MaxValue).ToArray()
        };
    }

    /// <summary>
    /// The artifacts and what each grants when collected (ArtifactSystem.ArtifactStatGrants at the normal multiplier,
    /// summed per stat, plus the weapon power, Chivalry and Darkness it keeps apart), with the Old God that gives it.
    /// </summary>
    private static object Artifacts(List<OldGodBossData> oldGods) => new ArtifactSystem().GetAllArtifacts()
        .OrderBy(a => a.Type).Select(a =>
        {
            // the god whose defeat or rescue hands it over; Manwe gives none (OldGodBossSystem)
            var god = oldGods.Where(g => g.Type != OldGodType.Manwe && g.ArtifactDropped == a.Type)
                .OrderBy(g => g.DungeonFloor).FirstOrDefault();
            int Kept(string key) => a.StatBonuses.FirstOrDefault(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
            return new
            {
                id = a.Type,
                name = Names(null, a.Name),
                oldGod = god?.Type.ToString(),
                floor = god?.DungeonFloor,
                grants = ArtifactSystem.ArtifactStatGrants(a, 1.0f).GroupBy(g => g.stat)
                    .Select(g => new { stat = g.Key, amount = g.Sum(x => x.amount) }).ToArray(),
                weaponPower = Kept("WeapPow"),
                chivalry = Kept("Chivalry"),
                darkness = Kept("Darkness")
            };
        }).ToArray();

    private static SortedDictionary<string, object?> EconomyConstants() => Named(typeof(GameConfig), string.Empty,
        nameof(GameConfig.MaterialDropChanceRegular), nameof(GameConfig.MaterialDropChanceMiniBoss),
        nameof(GameConfig.MaterialDropChanceTreasure), nameof(GameConfig.MaterialDropCountBossMin),
        nameof(GameConfig.MaterialDropCountBossMax), nameof(GameConfig.MaxEnchantments),
        nameof(GameConfig.FourthEnchantFailChance), nameof(GameConfig.FifthEnchantFailChance),
        nameof(GameConfig.FireEnchantProcChance), nameof(GameConfig.FireEnchantDamageMultiplier),
        nameof(GameConfig.FrostEnchantProcChance), nameof(GameConfig.FrostEnchantDuration),
        nameof(GameConfig.LootBaseDropChance), nameof(GameConfig.LootLevelDropScale),
        nameof(GameConfig.LootPartyBonusPerMember), nameof(GameConfig.LootMaxDropChance),
        nameof(GameConfig.LootTeammateTargetChance), nameof(GameConfig.ShopPriceMultiplier),
        nameof(GameConfig.ShopPowerMultiplier), nameof(GameConfig.StatTrainingBaseCostPerLevel),
        nameof(GameConfig.MaxStatTrainingsPerStat), nameof(GameConfig.HealingPotionBaseCost),
        nameof(GameConfig.ManaPotionBaseCost), nameof(GameConfig.RehabBaseCost),
        nameof(GameConfig.RehabPerAddictionCost), nameof(GameConfig.MaxWillowDraughts),
        nameof(GameConfig.SongBuffDuration), nameof(GameConfig.SongWarMarchBonus),
        nameof(GameConfig.SongIronLullabyBonus), nameof(GameConfig.SongFortuneBonus),
        nameof(GameConfig.SongBattleHymnBonus), nameof(GameConfig.GuardSalaryPerLevel),
        nameof(GameConfig.MinLevelForGuard), nameof(GameConfig.MaxDarknessForGuard),
        nameof(GameConfig.DefaultBankRobberyAttempts), nameof(GameConfig.BankRobberyMaxTake),
        nameof(GameConfig.BankVaultInitial), nameof(GameConfig.BankVaultDailyRefill),
        nameof(GameConfig.BankVaultRefillRatePercent), nameof(GameConfig.BankVaultCap),
        nameof(GameConfig.BankTransferFeePercent), nameof(GameConfig.InnRoomCostPerLevel),
        nameof(GameConfig.MaxSleepGuards), nameof(GameConfig.GuardCostMultiplierPerExtra),
        nameof(GameConfig.DormitorySleepCost), nameof(GameConfig.MaxDrinkingGamesPerDay),
        nameof(GameConfig.HighLowPayoutMultiplier), nameof(GameConfig.GamblingMaxDoubleDown),
        nameof(GameConfig.BlackjackPayoutMultiplier), nameof(GameConfig.BlackjackBonusPayout),
        nameof(GameConfig.ArmWrestleBetPerLevel), nameof(GameConfig.MaxArmWrestlesPerDay),
        nameof(GameConfig.SleeperGoldTheftPercent), nameof(GameConfig.SleeperXPLossPercent),
        nameof(GameConfig.MaxSteroidShopPurchases), nameof(GameConfig.MaxAlchemistINTBoosts),
        nameof(GameConfig.DrugOverdoseChance), nameof(GameConfig.BlackMarketGearMarkup),
        nameof(GameConfig.BlackMarketFreelanceSurcharge), nameof(GameConfig.BlackMarketRankDiscount),
        nameof(GameConfig.MaxPoisonVials), nameof(GameConfig.InformantCost),
        nameof(GameConfig.LoanSharkDailyInterest), nameof(GameConfig.DefaultThiefAttempts),
        nameof(GameConfig.StudyXPBonus), nameof(GameConfig.ServantsDailyGoldBase),
        nameof(GameConfig.ServantsDailyGoldPerLevel), nameof(GameConfig.ReinforcedDoorCost),
        nameof(GameConfig.MaxPlayerChildren));

    /// <summary>
    /// 1.3.2 wiki depth: combat. The statuses with what the game's own code does with each: its category, whether
    /// it stops acting or casting (StatusEffectExtensions), the damage one tick does at sample levels
    /// (Character.StatusDamagePerTurn) and whether a round's tick counts it down (Character.ProcessStatusEffects
    /// on a probe). The weight a monster gives each class when it picks whom to hit (CombatEngine.GetTargetWeight,
    /// a private instance method that reads only the character and the fight's leader, called on an object whose
    /// constructor never runs, with the probe as the leader and its armor power and maximum HP in the bands that
    /// add nothing). The mana and combat stamina regained at the end of a round, from the game's own functions.
    /// </summary>
    private static object Combat() => new
    {
        statuses = Enum.GetValues<StatusEffect>().Where(s => s != StatusEffect.None).Select(s => new
        {
            id = s,
            category = s.GetCategory(),
            preventsAction = s.PreventsAction(),
            preventsSpellcasting = s.PreventsSpellcasting(),
            countsDownEachRound = StatusCountsDown(s),
            damagePerRound = StatusTickLevels.Select(level => Character.StatusDamagePerTurn(s, level) is { } d
                ? new { level, min = d.min, max = d.max } : null).Where(x => x != null).ToArray()
        }).ToArray(),
        targetWeights = GameConfig.ClassStartingAttributes.Keys.OrderBy(c => c).Select(c => new
        {
            classId = c,
            weight = TargetWeight(c, defending: false),
            weightWhileDefending = TargetWeight(c, defending: true)
        }).ToArray(),
        regeneration = RegenStatSamples.Select(stat => new
        {
            stat,
            manaPerRound = StatEffectsSystem.GetManaRegenPerRound(stat),
            staminaPerRoundLight = StaminaRegen(stat, ArmorWeightClass.Light),
            staminaPerRoundMedium = StaminaRegen(stat, ArmorWeightClass.Medium),
            staminaPerRoundHeavy = StaminaRegen(stat, ArmorWeightClass.Heavy)
        }).ToArray(),
        constants = CombatConstants()
    };

    private static readonly int[] StatusTickLevels = [1, 25, 50, 100];
    private static readonly int[] RegenStatSamples = [10, 15, 20, 30, 40, 50, 60, 75, 100, 130, 150, 200];

    /// <summary>Whether one round's tick lowers the status's rounds left, as ProcessStatusEffects does it.</summary>
    private static bool StatusCountsDown(StatusEffect status)
    {
        var probe = new Character { Level = 1, MaxHP = 1000, HP = 1000 };
        probe.ApplyStatus(status, 2);
        probe.ProcessStatusEffects();
        return !probe.ActiveStatuses.TryGetValue(status, out var left) || left < 2;
    }

    /// <summary>A monster's weight for the fight's leader of this class (not an ally: no stance applies).</summary>
    private static int TargetWeight(CharacterClass characterClass, bool defending)
    {
        var engine = (CombatEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(CombatEngine));
        var probe = new Character { Class = characterClass, ArmPow = 20, MaxHP = 200, HP = 200, IsDefending = defending };
        (typeof(CombatEngine).GetField("currentPlayer", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CombatEngine.currentPlayer")).SetValue(engine, probe);
        var method = typeof(CombatEngine).GetMethod("GetTargetWeight", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("CombatEngine.GetTargetWeight");
        return (int)method.Invoke(engine, [probe])!;
    }

    /// <summary>Combat stamina regained in one round at this Stamina, wearing one armor piece of the weight
    /// class (none for Light: an unarmored character counts as Light), by Character.RegenerateCombatStamina.</summary>
    private static int StaminaRegen(int stamina, ArmorWeightClass weight)
    {
        var probe = new Character { Level = GameConfig.MaxLevel, Stamina = stamina, CurrentCombatStamina = 0 };
        if (weight != ArmorWeightClass.Light)
        {
            var armor = EquipmentDatabase.GetAllArmor().OrderBy(e => e.Id).First(e => e.WeightClass == weight);
            probe.EquippedItems[armor.Slot] = armor.Id;
        }
        if (probe.GetArmorWeightTier() != weight) throw new InvalidOperationException("armor weight probe: " + weight);
        return probe.RegenerateCombatStamina();
    }

    private static SortedDictionary<string, object?> CombatConstants()
    {
        var constants = Named(typeof(GameConfig), string.Empty,
            nameof(GameConfig.PlayerHitMinNeeded), nameof(GameConfig.PlayerHitMaxNeededAbove),
            nameof(GameConfig.AtLevelAccuracyGap), nameof(GameConfig.PreciseStrikeNeededBonus),
            nameof(GameConfig.MonsterAccuracyMinNeeded), nameof(GameConfig.MonsterAccuracyMaxNeededAtLevel),
            nameof(GameConfig.MonsterAccuracyMaxNeededWeak), nameof(GameConfig.TwoHandedDamageBonus),
            nameof(GameConfig.BossFirstRoundsDamageCapPercent), nameof(GameConfig.BossFirstRoundsDamageCapRounds),
            nameof(GameConfig.OldGodTeammateDamageCapPercent), nameof(GameConfig.MaxEquipmentLifeStealPercent),
            nameof(GameConfig.MaxTotalLifestealPercent), nameof(GameConfig.StunImmunityRoundsAfterRecovery),
            nameof(GameConfig.MaxStunDurationNormal), nameof(GameConfig.MaxStunDurationBoss),
            nameof(GameConfig.BossStunResistChance), nameof(GameConfig.BossSoftControlResistPercent),
            nameof(GameConfig.StunDRWindowRounds), nameof(GameConfig.CharmSkipAttackChance),
            nameof(GameConfig.KnightDamageBonus), nameof(GameConfig.GrandChampionDamageBonus),
            nameof(GameConfig.FatigueCostCombat), nameof(GameConfig.FatigueTiredThreshold),
            nameof(GameConfig.FatigueExhaustedThreshold), nameof(GameConfig.FatigueReductionHomeRest),
            nameof(GameConfig.FatigueReductionInnRest), nameof(GameConfig.FatigueReductionDungeonRest),
            nameof(GameConfig.AutoCombatHealPercentDefault), nameof(GameConfig.AutoCombatHealPercentMin),
            nameof(GameConfig.AutoCombatHealPercentMax), nameof(GameConfig.ScaledPotionMaxHPFraction),
            nameof(GameConfig.LightArmorDodgeBonus), nameof(GameConfig.MediumArmorDodgeBonus),
            nameof(GameConfig.LightArmorStaminaBonus), nameof(GameConfig.MediumArmorStaminaBonus),
            nameof(GameConfig.PvPBlindedMissPercent), nameof(GameConfig.CreatorsEyeCritBonus),
            nameof(GameConfig.MiracleCritMultiplier), nameof(GameConfig.DivineFavorBaseChance),
            nameof(GameConfig.DivineFavorMaxChance), nameof(GameConfig.RiteOfReturnBaseCost),
            nameof(GameConfig.RiteOfReturnCostPerLevelSquared), nameof(GameConfig.RiteOfReturnCooldownHours),
            nameof(GameConfig.EliteMinFloor));
        // the Venomous elite's poison and the monster target preferences, under their own names
        foreach (var entry in Named(typeof(EliteAffixes), string.Empty, nameof(EliteAffixes.VenomChancePercent), nameof(EliteAffixes.VenomRounds)))
            constants[entry.Key] = entry.Value;
        foreach (var entry in Named(typeof(MonsterTactics), string.Empty, nameof(MonsterTactics.PredatorHpPercent),
                     nameof(MonsterTactics.PredatorBonus), nameof(MonsterTactics.SkirmisherBonus)))
            constants[entry.Key] = entry.Value;
        return constants;
    }

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

    /// <summary>
    /// 1.3.2: balance values no live code reads are not exported, so the wiki cannot print them as rules. The crit
    /// chance and multiplier come from StatEffectsSystem; Backstab's live multiplier is a literal in
    /// ExecuteBackstabMultiMonster (the moddable one is read only by the dead single-monster ExecuteBackstab);
    /// Berserk, Doom rounds and the tank's AoE share have no reader. A server's balance.json still loads them.
    /// </summary>
    private static readonly string[] UnreadBalanceValues =
    [
        nameof(BalanceConfig.CriticalHitChance), nameof(BalanceConfig.CriticalHitMultiplier),
        nameof(BalanceConfig.BackstabMultiplier), nameof(BalanceConfig.BerserkMultiplier),
        nameof(BalanceConfig.BossDoomDefaultRounds), nameof(BalanceConfig.BossTankAoEAbsorption)
    ];

    private static object Balance()
    {
        var defaults = new BalanceConfig();
        var moddable = new Dictionary<string, object?>();
        foreach (var property in typeof(BalanceConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => !UnreadBalanceValues.Contains(p.Name)).OrderBy(p => p.MetadataToken))
            moddable[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = property.GetValue(defaults);
        return new
        {
            moddableDefaults = moddable,
            characterConstants = Constants("Specialization"),
            combatConstants = Constants("BaseMonster", "EarlyFloor"),
            godConstants = Constants("GodBoon", "GodWard", "GodDeed", "GodTaboo"),
            mentalConstants = Constants("Mental")
        };
    }

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
