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
            spell.ManaCost, spell.LevelRequired, spell.MagicWords,
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
