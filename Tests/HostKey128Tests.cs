using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 (C3d): the SSH host key check of Online Play. The official server (the shipped official address and
/// port) is trusted only with its pinned fingerprint for the negotiated key type; any other server is trusted on
/// first use, remembered per host and port in online_known_hosts.txt, and refused if its key changes. The SSH
/// tests run against <see cref="SshKexDouble"/> on loopback; "connects" means the client finished the key
/// exchange (sent NEWKEYS), which it does only for a trusted key.
/// </summary>
[Collection("SharedGameSingletons")]
public class HostKey128Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"usurper-c3d-{Guid.NewGuid():N}");
    private string KnownHosts => Path.Combine(_root, OnlineKnownHosts.FileName);

    public HostKey128Tests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>
    /// Guard 1 of the SSH client side: the host is the loopback seam (<see cref="TestLoopback.Host"/>) and any host
    /// that is not a loopback IP literal is refused here, before the real transport (which holds guard 2).
    /// </summary>
    private static SshAuthTransport ConnectToDouble(string host, int port, OnlineHostKeyPolicy? policy)
    {
        if (!IPAddress.TryParse(host, out var ip) || !IPAddress.IsLoopback(ip))
            throw new InvalidOperationException($"host key tests reach only the loopback SSH double; refused {host}");
        // no policy: the game's own entry point, which uses OnlineHostKeyPolicy.ForGame()
        return policy == null ? SshAuthTransport.Connect(host, port) : SshAuthTransport.Connect(host, port, policy);
    }

    private static string DoubleHost => TestLoopback.Host.ToString();

    /// <summary>One connection to the double: whether the double saw NEWKEYS, and what the client threw.</summary>
    private static async Task<(bool Trusted, Exception? Error)> Attempt(SshKexDouble dbl, OnlineHostKeyPolicy? policy)
    {
        var serve = dbl.ServeOneAsync();
        Exception? error = null;
        try
        {
            using var transport = await Task.Run(() => ConnectToDouble(DoubleHost, dbl.Port, policy)).WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (Exception ex) { error = ex; }
        bool trusted = await serve.WaitAsync(TimeSpan.FromSeconds(20));
        return (trusted, error);
    }

    private OnlineHostKeyPolicy OfficialIsTheDouble(SshKexDouble dbl, IReadOnlyDictionary<string, string> pins) =>
        new(DoubleHost, dbl.Port, pins, KnownHosts);

    /// <summary>A policy whose official server is elsewhere (a name that never resolves), so the double is "another server".</summary>
    private OnlineHostKeyPolicy OtherServerPolicy() =>
        new("official.invalid", 4000, OfficialHostKeys.ByType, KnownHosts);

    // ================= the two network guards, proven first =================

    [Fact]
    public async Task Guard1_SshDoubleClient_RefusesAnyHostButALoopbackLiteral_BeforeTheTransport()
    {
        IPAddress.IsLoopback(TestLoopback.Host).Should().BeTrue("the seam defaults to loopback");
        var policy = OtherServerPolicy();
        var started = DateTime.UtcNow;
        foreach (var host in new[] { "0.0.0.0", "192.0.2.1", "server-a.invalid", "localhost" })
        {
            Func<Task> connect = () => Task.Run(() => ConnectToDouble(host, 4000, policy)).WaitAsync(TimeSpan.FromSeconds(2));
            await connect.Should().ThrowAsync<InvalidOperationException>().WithMessage("*loopback SSH double*");
        }
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1), "refused before any DNS or connect");
        File.Exists(KnownHosts).Should().BeFalse();

        using var dbl = new SshKexDouble();
        var (trusted, _) = await Attempt(dbl, policy);
        trusted.Should().BeTrue("a loopback literal reaches the double");
    }

    [Fact]
    public async Task Guard2_RealSshTransport_WithAPolicy_RefusesANonLoopbackHost_BeforeConnecting()
    {
        OnlineTestGuard.Active.Should().BeTrue("the module initializer sets it");
        var policy = OtherServerPolicy();
        var started = DateTime.UtcNow;
        // never leaves the machine even with the guard broken: 0.0.0.0 is this machine, the port is closed
        var closed = TestLoopback.Listen();
        int port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        Func<Task> ssh = () => Task.Run(() => SshAuthTransport.Connect("0.0.0.0", port, policy)).WaitAsync(TimeSpan.FromSeconds(2));
        await ssh.Should().ThrowAsync<InvalidOperationException>().WithMessage("Online Play under test*");
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1), "refused before any connect attempt");
        File.Exists(KnownHosts).Should().BeFalse();
    }

    // ================= the pins =================

    [Fact]
    public void Pins_EqualTheFingerprintsRecordedFromTheServer()
    {
        // recorded 2026-10-07 (ssh-keygen -lf on the server's .pub files; the served keys are the same)
        OfficialHostKeys.Ed25519.Should().Be("SHA256:WSDcsAljcssdVzgocPuc7BfF3ei370TOVlYsy2Q2l9s");
        OfficialHostKeys.Ecdsa.Should().Be("SHA256:W1zNGJgPxWKyItXLSm2vQYH5tV5IT5O/QKbSXczzW28");
        OfficialHostKeys.Rsa.Should().Be("SHA256:J3X2VcqzItS0d2CmKpHQ5/OQTWIzyW9qFPlj6wq174s");
        OfficialHostKeys.ByType.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["ssh-ed25519"] = OfficialHostKeys.Ed25519,
            ["ecdsa-sha2-nistp256"] = OfficialHostKeys.Ecdsa,
            ["ssh-rsa"] = OfficialHostKeys.Rsa,
        });
    }

    [Fact]
    public void Pin_MatchesTheServersEd25519PublicKey_AndTheOfficialAddressIsMatchedAsText()
    {
        // the server's /etc/ssh/ssh_host_ed25519_key.pub as recorded (a public key; no network here)
        var blob = Convert.FromBase64String("AAAAC3NzaC1lZDI1NTE5AAAAIKjJ44gYLtYBJrzMAux2NCMwDfxQJSk7wybAB95L5aqT");
        HostKeyFingerprint.KeyType(blob).Should().Be("ssh-ed25519");
        HostKeyFingerprint.Sha256(blob).Should().Be(OfficialHostKeys.Ed25519);

        var policy = new OnlineHostKeyPolicy("play.usurper-reborn.net", 4000, OfficialHostKeys.ByType, KnownHosts);
        policy.Check(" Play.Usurper-Reborn.NET ", 4000, blob).Verdict.Should().Be(HostKeyVerdict.Pinned, "case and spaces are normalised");

        var mutated = (byte[])blob.Clone();
        mutated[^1] ^= 1;
        var refused = policy.Check("play.usurper-reborn.net", 4000, mutated);
        refused.Verdict.Should().Be(HostKeyVerdict.RefusedOfficial);
        refused.Trusted.Should().BeFalse();
        File.Exists(KnownHosts).Should().BeFalse("the official server is never trusted on first use");

        policy.Check("play.usurper-reborn.net", 22, mutated).Verdict.Should().Be(HostKeyVerdict.FirstUse, "another port is another server");
    }

    [Fact]
    public void ForGame_UsesTheShippedOfficialServer_TheShippedPins_AndTheLoginDirectory()
    {
        OfficialHostKeys.Host.Should().Be("play.usurper-reborn.net");
        OfficialHostKeys.Port.Should().Be(4000);
        var policy = OnlineHostKeyPolicy.ForGame();
        policy.OfficialHost.Should().Be(OfficialHostKeys.Host);
        policy.OfficialPort.Should().Be(OfficialHostKeys.Port);
        policy.Pins.Should().BeSameAs(OfficialHostKeys.ByType);
        policy.KnownHostsPath.Should().Be(Path.Combine(OnlineLoginStore.DirectoryPath, "online_known_hosts.txt"));
    }

    [Fact]
    public async Task SysopServerSetting_ChangedToAnotherHost_IsTrustOnFirstUse_NotThePins()
    {
        using var dbl = new SshKexDouble();
        string oldAddress = GameConfig.OnlineServerAddress;
        int oldPort = GameConfig.OnlineServerPort;
        var oldDir = OnlineLoginStore.DirectoryOverride;
        try
        {
            // a sysop points the Online Play server setting at their own server (here the loopback double)
            GameConfig.OnlineServerAddress = DoubleHost;
            GameConfig.OnlineServerPort = dbl.Port;
            OnlineLoginStore.DirectoryOverride = () => _root;

            var (trusted, error) = await Attempt(dbl, null); // the game's entry point and policy
            trusted.Should().BeTrue("an unknown key on the sysop's server is trusted on first use");
            error.Should().NotBeOfType<HostKeyRefusedException>();
            File.ReadAllLines(KnownHosts).Should().Equal(
                $"[{DoubleHost}]:{dbl.Port} ecdsa-sha2-nistp256 {HostKeyFingerprint.Sha256(dbl.HostKeyBlob)}");
        }
        finally
        {
            GameConfig.OnlineServerAddress = oldAddress;
            GameConfig.OnlineServerPort = oldPort;
            OnlineLoginStore.DirectoryOverride = oldDir;
        }
    }

    [Fact]
    public void TheShippedOfficialServer_StillUsesThePins_WhateverTheServerSetting()
    {
        var blob = Convert.FromBase64String("AAAAC3NzaC1lZDI1NTE5AAAAIKjJ44gYLtYBJrzMAux2NCMwDfxQJSk7wybAB95L5aqT");
        var mutated = (byte[])blob.Clone();
        mutated[^1] ^= 1;
        string oldAddress = GameConfig.OnlineServerAddress;
        int oldPort = GameConfig.OnlineServerPort;
        var oldDir = OnlineLoginStore.DirectoryOverride;
        try
        {
            OnlineLoginStore.DirectoryOverride = () => _root;
            GameConfig.OnlineServerAddress = "sysop-server.invalid";
            GameConfig.OnlineServerPort = 4100;
            var policy = OnlineHostKeyPolicy.ForGame();
            policy.Check("PLAY.Usurper-Reborn.net", 4000, blob).Verdict.Should().Be(HostKeyVerdict.Pinned);
            policy.Check("play.usurper-reborn.net", 4000, mutated).Verdict.Should().Be(HostKeyVerdict.RefusedOfficial);
            policy.IsOfficial("sysop-server.invalid", 4100).Should().BeFalse();
            File.Exists(KnownHosts).Should().BeFalse("the official server is never written to the known hosts file");
        }
        finally
        {
            GameConfig.OnlineServerAddress = oldAddress;
            GameConfig.OnlineServerPort = oldPort;
            OnlineLoginStore.DirectoryOverride = oldDir;
        }
    }

    // ================= the official server, over SSH on loopback =================

    [Fact]
    public async Task Official_WithItsPinnedKey_Connects()
    {
        using var dbl = new SshKexDouble();
        string fp = HostKeyFingerprint.Sha256(dbl.HostKeyBlob);
        var pins = new Dictionary<string, string> { ["ecdsa-sha2-nistp256"] = fp };

        var (trusted, error) = await Attempt(dbl, OfficialIsTheDouble(dbl, pins));

        trusted.Should().BeTrue("the client finished the key exchange");
        error.Should().NotBeOfType<HostKeyRefusedException>();
        File.Exists(KnownHosts).Should().BeFalse("a pinned server is not written to the known hosts file");
    }

    [Fact]
    public async Task Official_WithAnyOtherKey_IsRefused_AndNothingIsSent()
    {
        using var dbl = new SshKexDouble();
        string fp = HostKeyFingerprint.Sha256(dbl.HostKeyBlob);

        var (trusted, error) = await Attempt(dbl, OfficialIsTheDouble(dbl, OfficialHostKeys.ByType));

        trusted.Should().BeFalse("the client stopped at the key exchange");
        var check = error.Should().BeOfType<HostKeyRefusedException>().Subject.Check;
        check.Verdict.Should().Be(HostKeyVerdict.RefusedOfficial);
        check.KeyType.Should().Be("ecdsa-sha2-nistp256");
        check.Fingerprint.Should().Be(fp, "the policy fingerprints the blob the server sent");
        check.ExpectedFingerprint.Should().Be(OfficialHostKeys.Ecdsa, "the pin for the negotiated type");
        File.Exists(KnownHosts).Should().BeFalse();
    }

    [Fact]
    public async Task Official_ThePinOfAnotherKeyType_DoesNotCount()
    {
        using var dbl = new SshKexDouble();
        var pins = new Dictionary<string, string> { ["ssh-ed25519"] = HostKeyFingerprint.Sha256(dbl.HostKeyBlob) };

        var (trusted, error) = await Attempt(dbl, OfficialIsTheDouble(dbl, pins));

        trusted.Should().BeFalse();
        error.Should().BeOfType<HostKeyRefusedException>().Which.Check.Verdict.Should().Be(HostKeyVerdict.RefusedOfficial);
    }

    // ================= other servers: trust on first use =================

    [Fact]
    public async Task OtherServer_FirstUseStores_SameKeyConnects_ChangedKeyRefused_DeletingTheLineAllowsReconnect()
    {
        using var dbl = new SshKexDouble();
        var policy = OtherServerPolicy();
        string entry = $"[{DoubleHost}]:{dbl.Port}";
        string firstFp = HostKeyFingerprint.Sha256(dbl.HostKeyBlob);

        // first connection: stored, connects
        var first = await Attempt(dbl, policy);
        first.Trusted.Should().BeTrue();
        first.Error.Should().NotBeOfType<HostKeyRefusedException>();
        File.ReadAllLines(KnownHosts).Should().Equal($"{entry} ecdsa-sha2-nistp256 {firstFp}");
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(KnownHosts).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Directory.EnumerateFiles(_root, "*.tmp").Should().BeEmpty();

        // same key again: connects, file unchanged
        var again = await Attempt(dbl, policy);
        again.Trusted.Should().BeTrue();
        again.Error.Should().NotBeOfType<HostKeyRefusedException>();
        policy.Check(DoubleHost, dbl.Port, dbl.HostKeyBlob).Verdict.Should().Be(HostKeyVerdict.Known);
        File.ReadAllLines(KnownHosts).Should().Equal($"{entry} ecdsa-sha2-nistp256 {firstFp}");

        // the server now offers another key on the same address and port: refused, file unchanged
        dbl.NewHostKey();
        string newFp = HostKeyFingerprint.Sha256(dbl.HostKeyBlob);
        var changed = await Attempt(dbl, policy);
        changed.Trusted.Should().BeFalse("the client stopped at the key exchange");
        var check = changed.Error.Should().BeOfType<HostKeyRefusedException>().Subject.Check;
        check.Verdict.Should().Be(HostKeyVerdict.RefusedChanged);
        check.Fingerprint.Should().Be(newFp);
        check.ExpectedFingerprint.Should().Be(firstFp);
        check.Entry.Should().Be(entry);
        check.KnownHostsPath.Should().Be(KnownHosts);
        File.ReadAllLines(KnownHosts).Should().Equal($"{entry} ecdsa-sha2-nistp256 {firstFp}");

        // the refusal names the file and the start of the line to delete
        var lines = OnlineHostKeyPolicy.RefusalLines(check, "en");
        lines.Should().Contain("    " + KnownHosts);
        lines.Should().Contain("    " + entry);
        lines.Should().Contain($"    ecdsa-sha2-nistp256 {firstFp}");
        lines.Should().Contain($"    ecdsa-sha2-nistp256 {newFp}");

        // the player deletes that line as told: the next connection is a first connection again
        File.WriteAllLines(KnownHosts, File.ReadAllLines(KnownHosts).Where(l => !l.StartsWith(entry + " ", StringComparison.Ordinal)));
        var cleared = await Attempt(dbl, policy);
        cleared.Trusted.Should().BeTrue();
        cleared.Error.Should().NotBeOfType<HostKeyRefusedException>();
        File.ReadAllLines(KnownHosts).Should().Equal($"{entry} ecdsa-sha2-nistp256 {newFp}");
    }

    [Fact]
    public void KnownHosts_KeepsOtherServers_KeysHostAndPortTogether_AndAKeyTypeChangeIsAChange()
    {
        var policy = OtherServerPolicy();
        byte[] a = Blob("ssh-ed25519", 1), b = Blob("ssh-ed25519", 2), c = Blob("ecdsa-sha2-nistp256", 3);

        policy.Check("Server-A.example", 4000, a).Verdict.Should().Be(HostKeyVerdict.FirstUse);
        policy.Check("server-b.example", 4000, b).Verdict.Should().Be(HostKeyVerdict.FirstUse);
        policy.Check("server-a.example", 4001, b).Verdict.Should().Be(HostKeyVerdict.FirstUse, "the port is part of the entry");
        policy.Check("SERVER-A.EXAMPLE", 4000, a).Verdict.Should().Be(HostKeyVerdict.Known, "the host is compared after case normalising");
        policy.Check("server-a.example", 4000, b).Verdict.Should().Be(HostKeyVerdict.RefusedChanged);
        policy.Check("server-a.example", 4000, c).Verdict.Should().Be(HostKeyVerdict.RefusedChanged, "a different key type is not a first use");
        File.ReadAllLines(KnownHosts).Should().HaveCount(3);

        // a file that cannot be parsed line by line is kept: unknown lines are ignored, never dropped
        File.AppendAllText(KnownHosts, "a line someone typed\n");
        policy.Check("server-c.example", 4000, c).Verdict.Should().Be(HostKeyVerdict.FirstUse);
        File.ReadAllLines(KnownHosts).Should().Contain("a line someone typed").And.HaveCount(5);
    }

    private static byte[] Blob(string type, byte seed)
    {
        var name = System.Text.Encoding.ASCII.GetBytes(type);
        var blob = new byte[4 + name.Length + 36];
        blob[3] = (byte)name.Length;
        name.CopyTo(blob, 4);
        blob[^1] = seed;
        return blob;
    }

    // ================= the text the player sees =================

    private static readonly string[] NewKeys =
    {
        "online.hostkey_official_1", "online.hostkey_official_2", "online.hostkey_official_3", "online.hostkey_offered",
        "online.hostkey_remembered", "online.hostkey_changed_1", "online.hostkey_changed_2", "online.hostkey_changed_3",
        "online.hostkey_changed_4",
    };

    [Fact]
    public void RefusalText_InFiveLanguages_RealTranslations_Within79InEnglishAndHungarian_FingerprintsOnly()
    {
        foreach (var key in NewKeys)
            foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{lang} {key}");
                Loc.GetIn(lang, key).Trim().Should().NotBeEmpty();
                if (lang != "en") Loc.GetIn(lang, key).Should().NotBe(Loc.GetIn("en", key), $"{lang} {key} is translated");
            }

        // a typical per-user path and server: the file path and the entry rows are the player's own data
        string path = "/home/player/.local/share/UsurperReloaded/online_known_hosts.txt";
        var blob = Convert.FromBase64String("AAAAC3NzaC1lZDI1NTE5AAAAIKjJ44gYLtYBJrzMAux2NCMwDfxQJSk7wybAB95L5aqT");
        var official = new HostKeyCheck("play.usurper-reborn.net", 4000, HostKeyVerdict.RefusedOfficial, "ecdsa-sha2-nistp256",
            OfficialHostKeys.Ed25519, "ecdsa-sha2-nistp256", OfficialHostKeys.Ecdsa, path);
        var changed = new HostKeyCheck("friends-server.example.org", 4000, HostKeyVerdict.RefusedChanged, "ecdsa-sha2-nistp256",
            OfficialHostKeys.Ecdsa, "ssh-ed25519", OfficialHostKeys.Ed25519, path);
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var o = OnlineHostKeyPolicy.RefusalLines(official, lang);
            o.Should().HaveCount(5);
            o.Should().Contain("    ecdsa-sha2-nistp256 " + OfficialHostKeys.Ed25519);
            var c = OnlineHostKeyPolicy.RefusalLines(changed, lang);
            c.Should().HaveCount(10);
            c.Should().Contain("    " + path).And.Contain("    [friends-server.example.org]:4000");
            foreach (var row in o.Concat(c))
            {
                row.Should().NotContain(Convert.ToBase64String(blob), "no key material beyond the fingerprint");
                if (lang is "en" or "hu") row.Length.Should().BeLessThanOrEqualTo(79, $"{lang}: {row}");
            }
        }
    }

    [Fact]
    public void KeyType_ComesFromTheBlob_ReducedToSafeCharacters()
    {
        HostKeyFingerprint.KeyType(Blob("ssh-rsa", 1)).Should().Be("ssh-rsa");
        HostKeyFingerprint.KeyType(Blob("ssh-ed\u001b[2Jx 25519", 1)).Should().Be("ssh-ed2Jx25519", "no escape codes or spaces reach the screen or the file");
        HostKeyFingerprint.KeyType(new byte[] { 0, 0 }).Should().Be("unknown");
        HostKeyFingerprint.KeyType(new byte[] { 0, 0, 0, 200, 1 }).Should().Be("unknown");
    }

    // ================= wiring =================

    [Fact]
    public void EverySshLogin_GoesThroughTheHostKeyCheck_AndARefusalIsShownBeforeOtherErrors()
    {
        var src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "OnlinePlaySystem.cs"));
        src.Should().Contain("internal static SshAuthTransport Connect(string host, int port) => Connect(host, port, OnlineHostKeyPolicy.ForGame());");
        src.Split("new SshClient(").Length.Should().Be(2, "the transport is the only place the game opens an SshClient");
        int ssh = src.IndexOf("new SshClient(host", StringComparison.Ordinal);
        int handler = src.IndexOf("client.HostKeyReceived +=", ssh, StringComparison.Ordinal);
        handler.Should().BeGreaterThan(ssh);
        handler.Should().BeLessThan(src.IndexOf("client.Connect();", ssh, StringComparison.Ordinal));
        src.Should().Contain("e.CanTrust = hostKeyCheck.Trusted;");

        int connectTransport = src.IndexOf("private async Task<IOnlineAuthTransport?> ConnectTransportAsync(", StringComparison.Ordinal);
        int refusedCatch = src.IndexOf("catch (HostKeyRefusedException ex)", connectTransport, StringComparison.Ordinal);
        refusedCatch.Should().BeGreaterThan(connectTransport);
        refusedCatch.Should().BeLessThan(src.IndexOf("catch (Renci.SshNet.Common.SshConnectionException ex)", connectTransport, StringComparison.Ordinal));
        refusedCatch.Should().BeLessThan(src.IndexOf("catch (Exception ex)", connectTransport, StringComparison.Ordinal));
        src.Should().Contain("OnlineHostKeyPolicy.RefusalLines(ex.Check)");
    }

    [Fact]
    public async Task BbsDoor_UsesDirectTcp_WithNoHostKeyCheck()
    {
        var src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "OnlinePlaySystem.cs"));
        src.Should().Contain("useTcpMode = isBbs;");
        int tcp = src.IndexOf("internal sealed class TcpAuthTransport", StringComparison.Ordinal);
        src.IndexOf("OnlineHostKeyPolicy", tcp, StringComparison.Ordinal).Should().Be(-1, "the door's TCP transport has no host key");

        var listener = TestLoopback.Listen();
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var transport = await OnlineTransport.ConnectRealAsync(DoubleHost, ((IPEndPoint)listener.LocalEndpoint).Port, directTcp: true);
            using var peer = await accept;
            transport.Should().BeOfType<TcpAuthTransport>();
        }
        finally { listener.Stop(); }
    }
}
