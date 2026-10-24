using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 test and code hygiene: the curse removal width test no longer depends on test order, the real
/// curse removal rows wrap at 79 columns for the longest loot name, and the secret boss manager is built
/// once and whole however many sessions ask first.
/// </summary>
[Collection("SharedGameSingletons")]
public class LF137Hygiene
{
    private const int MaxWidth = 79;

    [Fact]
    public void CurseRowTest_IgnoresDynamicLoot()
    {
        // A dynamic item with a 60 character name, as another test (or a long loot drop) registers it.
        int id = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = "Venomsteel Exquisite Dagger of the World Slayer of Ages",
            Slot = EquipmentSlot.MainHand
        });
        try
        {
            EquipmentDatabase.GetAll().Any(e => e.Id == id).Should().BeTrue("the dynamic item is in GetAll");
            new DashWidth124Tests().CurseRemovalRows_Fit();
        }
        finally
        {
            // No public remove (ClearDynamicEquipment is global), so take only this one item out.
            var all = (System.Collections.IDictionary)typeof(EquipmentDatabase)
                .GetField("_allEquipment", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            lock (typeof(EquipmentDatabase).GetField("_lock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
                all.Remove(id);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void CurseRemovalRows_WrapForTheLongestLootName(string lang)
    {
        string longest = ItemNames.LootTemplates()
            .SelectMany(t => LootGenerator.AllNameFormsFor(t, "en").Distinct()
                .SelectMany(f => LootGenerator.WorldBossNamePrefixes.Select(p => p + " " + f).Prepend(f))
                .Select(s => ItemNames.DisplayIn(lang, s, t)))
            .OrderByDescending(n => n.Length).First();
        // The longest real name reaches the length that once overflowed (41 and up); the 70 character one is a margin.
        longest.Length.Should().BeGreaterThan(40);
        foreach (string name in new[] { longest, new string('W', 10) + " " + new string('x', 58) })
        {
            var rows = new List<string>
            {
                Loc.GetIn(lang, "magic_shop.cursed_item_entry", "3", name, "9,999,999"),
                Loc.GetIn(lang, "magic_shop.cursed_worn_entry", "3", name, "Main Hand", "9,999,999"),
                Loc.GetIn(lang, "magic_shop.cursed_team_entry", "3", name, "Aranyszivu Hosszunevu Kalandor", "Main Hand", "9,999,999"),
            };
            foreach (var row in rows)
                MagicShopLocation.WrapRows(row).Should().OnlyContain(r => r.Length <= MaxWidth, lang + ": " + row);
        }
    }

    private static FieldInfo InstanceField =>
        typeof(SecretBossManager).GetField("_lazy", BindingFlags.NonPublic | BindingFlags.Static)
        ?? typeof(SecretBossManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void SecretBossManager_ParallelFirstCalls_GiveOneWholeInstance()
    {
        var field = InstanceField;
        object? original = field.GetValue(null);
        try
        {
            field.SetValue(null, field.FieldType == typeof(SecretBossManager)
                ? null
                : new Lazy<SecretBossManager>(() => new SecretBossManager(), LazyThreadSafetyMode.ExecutionAndPublication));
            const int N = 16;
            using var barrier = new Barrier(N);
            var tasks = Enumerable.Range(0, N).Select(_ => Task.Factory.StartNew(() =>
            {
                barrier.SignalAndWait();
                var m = SecretBossManager.Instance;
                return (m, whole: m.GetBoss(SecretBossType.TheFirstWave) != null);
            }, TaskCreationOptions.LongRunning)).ToArray();
            Task.WaitAll(tasks);
            tasks.Select(t => t.Result.m).Distinct().Should().HaveCount(1);
            tasks.Should().OnlyContain(t => t.Result.whole);
        }
        finally { field.SetValue(null, original); }
    }

    [Fact]
    public void SecretBossManager_ConstructorDoesNotReplaceTheInstance()
    {
        var first = SecretBossManager.Instance;
        _ = new SecretBossManager();
        SecretBossManager.Instance.Should().BeSameAs(first);
    }
}
