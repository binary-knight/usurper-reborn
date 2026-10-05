using System;
using System.Runtime.CompilerServices;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.7: no test may touch the network. The telemetry sender is only ever made by
/// TelemetrySenderFactory; for the whole test run it throws, so an uploader built without its own fake
/// fails at once instead of posting.
/// </summary>
internal static class TelemetryNoNetwork
{
    internal sealed class NetworkInTestException : InvalidOperationException
    {
        public NetworkInTestException() : base("a test asked for the real telemetry sender; pass a fake") { }
    }

    internal static readonly Func<ITelemetrySender> Guard = () => throw new NetworkInTestException();

    [ModuleInitializer]
    internal static void InstallGuard() => TelemetrySenderFactory.Create = Guard;
}
