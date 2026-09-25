using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.13: the street throne challenge is unreachable: no method body in the game assembly (compiler-made
/// state machines and lambdas included) calls, or takes a delegate to, ExecuteThroneChallenge or the finder
/// that would pick its challenger.
/// </summary>
public class StreetEncounterSystemReachabilityTests
{
    private static readonly HashSet<short> CallOpcodes = new()
    {
        System.Reflection.Emit.OpCodes.Call.Value, System.Reflection.Emit.OpCodes.Callvirt.Value,
        System.Reflection.Emit.OpCodes.Newobj.Value, System.Reflection.Emit.OpCodes.Ldftn.Value,
        System.Reflection.Emit.OpCodes.Ldvirtftn.Value,
    };

    private static readonly Dictionary<short, System.Reflection.Emit.OpCode> OpCodes =
        typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    /// <summary>Every method a method body calls or takes the address of, read from its IL.</summary>
    private static IEnumerable<MethodBase> Callees(MethodBase caller)
    {
        byte[]? il;
        try { il = caller.GetMethodBody()?.GetILAsByteArray(); } catch { yield break; }
        if (il == null) yield break;
        for (int i = 0; i < il.Length;)
        {
            short value = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
            var op = OpCodes[value];
            i += op.Size;
            int operand = op.OperandType switch
            {
                System.Reflection.Emit.OperandType.InlineNone => 0,
                System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                System.Reflection.Emit.OperandType.InlineVar => 2,
                System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
                System.Reflection.Emit.OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
            if (CallOpcodes.Contains(value))
            {
                MethodBase? callee = null;
                try
                {
                    callee = caller.Module.ResolveMethod(BitConverter.ToInt32(il, i),
                        caller.DeclaringType?.IsGenericType == true ? caller.DeclaringType.GetGenericArguments() : null,
                        caller.IsGenericMethod ? caller.GetGenericArguments() : null);
                }
                catch { }
                if (callee != null) yield return callee;
            }
            i += operand;
        }
    }

    [Fact]
    public void TheStreetThroneChallenge_HasNoCallerInTheGame()
    {
        var target = typeof(StreetEncounterSystem).GetMethod("ExecuteThroneChallenge", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var finder = typeof(StreetEncounterSystem).GetMethod("FindThroneChallenger", BindingFlags.NonPublic | BindingFlags.Instance)!;
        target.Should().NotBeNull();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var assembly = typeof(StreetEncounterSystem).Assembly;

        int scanned = 0;
        var callers = new List<string>();
        var controlCallers = new List<string>();
        var control = typeof(StreetEncounterSystem).GetMethod("ExecuteGrudgeConfrontation", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var type in assembly.GetTypes())
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                scanned++;
                foreach (var callee in Callees(method))
                {
                    if (callee == (MethodBase)target || callee == (MethodBase)finder) callers.Add($"{type.FullName}.{method.Name}");
                    if (callee == (MethodBase)control) controlCallers.Add($"{type.FullName}.{method.Name}");
                }
            }

        scanned.Should().BeGreaterThan(10_000, "the whole game assembly is scanned");
        controlCallers.Should().NotBeEmpty("the scan does find the caller of a sibling encounter that is reachable");
        callers.Should().BeEmpty("nothing reaches the street throne challenge");
    }
}
