using System;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8: no Online Play test may reach a real host. Guard 1: for the whole test run the transport factory every
/// login connection goes through is this loopback-only one (a host that is not a loopback IP literal is refused
/// before any DNS or connect; there is no SSH server in the tests, so a loopback host gets plain TCP). Guard 2:
/// the real SSH and TCP transports themselves refuse any non-loopback host while OnlineTestGuard.Active is set, so
/// a transport made without the factory cannot leave the machine either.
/// </summary>
internal static class OnlineNoNetwork
{
    internal static readonly Func<string, int, bool, Task<IOnlineAuthTransport>> LoopbackOnly = async (host, port, directTcp) =>
    {
        if (!IPAddress.TryParse(host, out var ip) || !IPAddress.IsLoopback(ip))
            throw new InvalidOperationException($"Online Play tests connect only to loopback; refused {host}");
        return await TcpAuthTransport.ConnectAsync(host, port);
    };

    [ModuleInitializer]
    internal static void InstallGuards()
    {
        OnlineTestGuard.Active = true;
        OnlineTransport.Factory = LoopbackOnly;
    }
}
