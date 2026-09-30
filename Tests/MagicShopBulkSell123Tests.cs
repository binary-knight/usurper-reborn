using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.3: the magic shop's sell all ([A]) removed and paid for every accessory in the backpack,
/// including the cursed and unidentified ones the list skips, so it sold more than it showed and paid
/// more than the quoted total. It now sells exactly the listed rows.
/// </summary>
[Collection("SharedGameSingletons")]
public class MagicShopBulkSell123Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>A save backend that writes nothing, so the sale's autosave stays off disk.</summary>
    public class NullBackend : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var rt = method!.ReturnType;
            if (rt == typeof(Task<bool>)) return Task.FromResult(true);
            if (rt == typeof(Task)) return Task.CompletedTask;
            if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(rt.GetGenericArguments()[0])
                    .Invoke(null, new object?[] { null });
            if (rt == typeof(string)) return "";
            if (rt == typeof(bool)) return false;
            if (rt == typeof(void)) return null;
            return rt.IsValueType ? Activator.CreateInstance(rt) : Activator.CreateInstance(rt);
        }
    }

    private static Item Ring(string name, long value, bool cursed = false, bool identified = true) => new()
    {
        Name = name, Type = ObjType.Fingers, Value = value, IsCursed = cursed, Cursed = cursed, IsIdentified = identified,
    };

    private static async Task<(Character hero, string shown)> SellAll(params Item[] items)
    {
        var hero = new Character
        {
            Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 8, HP = 500, MaxHP = 500,
            AI = CharacterAI.Human, Gold = 0,
        };
        hero.Inventory.AddRange(items);

        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("A\nY\n\n\n")), output);
        var shop = new MagicShopLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);

        var field = typeof(SaveSystem).GetField("backend", F)!;
        var real = field.GetValue(SaveSystem.Instance);
        try
        {
            field.SetValue(SaveSystem.Instance, DispatchProxy.Create<ISaveBackend, NullBackend>());
            await (Task)typeof(MagicShopLocation).GetMethod("SellAccessory", F)!.Invoke(shop, new object[] { hero })!;
        }
        finally { field.SetValue(SaveSystem.Instance, real); }

        term.StreamWriterInternal?.Flush();
        return (hero, Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public async Task SellAll_KeepsCursedAndUnidentifiedAccessories()
    {
        var plain = Ring("Plain Band", 200);
        var cursed = Ring("Cursed Band", 400, cursed: true);
        var hidden = Ring("Unknown Band", 800, identified: false);

        var (hero, _) = await SellAll(plain, cursed, hidden);

        hero.Inventory.Should().NotContain(plain);
        hero.Inventory.Should().Contain(cursed).And.Contain(hidden);
    }

    [Fact]
    public async Task SellAll_PaysExactlyTheQuotedTotal()
    {
        var plain = Ring("Plain Band", 200);
        var other = Ring("Other Band", 60);
        var cursed = Ring("Cursed Band", 400, cursed: true);
        var hidden = Ring("Unknown Band", 800, identified: false);

        float fence = FactionSystem.Instance.GetFencePriceModifier();
        long quoted = (long)(Math.Max(1, 200 / 2) * fence) + (long)(Math.Max(1, 60 / 2) * fence);

        var (hero, shown) = await SellAll(plain, other, cursed, hidden);

        hero.Gold.Should().Be(quoted);
        shown.Should().Contain(quoted.ToString("N0"));
    }
}
