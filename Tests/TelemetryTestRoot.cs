using System;
using System.IO;
using System.Runtime.CompilerServices;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.7: points every telemetry store of the test run at a temp folder, so no test reads or writes the
/// telemetry folder of a real save directory (TelemetryConsent.CurrentStore otherwise follows
/// SaveSystem.Instance, the developer's own saves). This is the only assignment of RootOverride.
/// </summary>
internal static class TelemetryTestRoot
{
    internal static readonly string Root = Path.Combine(Path.GetTempPath(), $"usurper-tests-telemetry-{Guid.NewGuid():N}");

    [ModuleInitializer]
    internal static void RedirectTelemetry()
    {
        TelemetryConsent.RootOverride = Root;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, true); } catch { }
        };
    }
}
