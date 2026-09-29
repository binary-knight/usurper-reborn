using System.Runtime.CompilerServices;
using UsurperRemake;

namespace UsurperReborn.Tests;

/// <summary>
/// Turns presentation pauses off for the whole test run. This is the only assignment of Pacing.Disabled.
/// </summary>
internal static class TestPacing
{
    [ModuleInitializer]
    internal static void DisablePacing()
    {
        Pacing.Disabled = true;
    }
}
