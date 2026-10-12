using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8: the smallest SSH server the SSH.NET client accepts as far as its host key check: the version lines,
/// KEXINIT, one ecdh-sha2-nistp256 key exchange signed with an ecdsa-sha2-nistp256 host key, then it reads the
/// client's next packet. The client sends NEWKEYS only after it verified the signature and trusted the host key,
/// so <see cref="ServeOneAsync"/> returns true for a trusted key and false for a refused one; the double then
/// closes the connection (no encryption, no login). It listens through <see cref="TestLoopback.Listen"/>
/// (loopback only, two guards); the key can be swapped between connections on the same port.
/// </summary>
internal sealed class SshKexDouble : IDisposable
{
    private const string KexName = "ecdh-sha2-nistp256";
    private const string KeyName = "ecdsa-sha2-nistp256";
    private readonly TcpListener _listener;

    internal ECDsa HostKey { get; private set; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    internal SshKexDouble()
    {
        _listener = TestLoopback.Listen();
    }

    /// <summary>The public key blob the double presents: string type, string curve, string point.</summary>
    internal byte[] HostKeyBlob
    {
        get
        {
            var q = HostKey.ExportParameters(false).Q;
            var w = new Wire();
            w.String(KeyName);
            w.String("nistp256");
            w.String(Point(q));
            return w.ToArray();
        }
    }

    /// <summary>Present a new host key from the next connection on (same address and port).</summary>
    internal void NewHostKey()
    {
        HostKey.Dispose();
        HostKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }

    /// <summary>Serve one connection. True when the client answered the key exchange with NEWKEYS (trusted the key).</summary>
    internal async Task<bool> ServeOneAsync()
    {
        using var tcp = await _listener.AcceptTcpClientAsync();
        if (!IPAddress.IsLoopback(((IPEndPoint)tcp.Client.RemoteEndPoint!).Address))
            throw new InvalidOperationException("the SSH double accepts loopback peers only");
        var stream = tcp.GetStream();
        stream.ReadTimeout = 10000;
        try
        {
            const string serverVersion = "SSH-2.0-UsurperTestDouble";
            var vs = Encoding.ASCII.GetBytes(serverVersion + "\r\n");
            stream.Write(vs, 0, vs.Length);
            string clientVersion = ReadVersion(stream);

            var serverKexInit = KexInit();
            WritePacket(stream, serverKexInit);
            byte[] clientKexInit = ReadPacket(stream);
            if (clientKexInit[0] != 20) return false;

            byte[] init = ReadPacket(stream);
            if (init[0] != 30) return false;
            var r = new Reader(init, 1);
            byte[] qc = r.String();

            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            byte[] qs = Point(ecdh.ExportParameters(false).Q);
            using var peer = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = qc[1..33], Y = qc[33..65] },
            });
            byte[] k = ecdh.DeriveRawSecretAgreement(peer.PublicKey);

            byte[] ks = HostKeyBlob;
            var h = new Wire();
            h.String(clientVersion);
            h.String(serverVersion);
            h.String(clientKexInit);
            h.String(serverKexInit);
            h.String(ks);
            h.String(qc);
            h.String(qs);
            h.Mpint(k);
            byte[] exchangeHash = SHA256.HashData(h.ToArray());

            byte[] rs = HostKey.SignData(exchangeHash, HashAlgorithmName.SHA256); // r || s, 32 bytes each
            var sigInner = new Wire();
            sigInner.Mpint(rs[..32]);
            sigInner.Mpint(rs[32..]);
            var sig = new Wire();
            sig.String(KeyName);
            sig.String(sigInner.ToArray());

            var reply = new Wire();
            reply.Byte(31);
            reply.String(ks);
            reply.String(qs);
            reply.String(sig.ToArray());
            WritePacket(stream, reply.ToArray());

            byte[] next = ReadPacket(stream);
            return next.Length > 0 && next[0] == 21; // SSH_MSG_NEWKEYS
        }
        catch (IOException) { return false; }
    }

    private static byte[] Point(ECPoint q)
    {
        var p = new byte[65];
        p[0] = 4;
        q.X!.CopyTo(p, 1);
        q.Y!.CopyTo(p, 33);
        return p;
    }

    private static byte[] KexInit()
    {
        var w = new Wire();
        w.Byte(20);
        w.Raw(RandomNumberGenerator.GetBytes(16));
        w.String(KexName);
        w.String(KeyName);
        w.String("aes128-ctr");
        w.String("aes128-ctr");
        w.String("hmac-sha2-256");
        w.String("hmac-sha2-256");
        w.String("none");
        w.String("none");
        w.String("");
        w.String("");
        w.Byte(0);
        w.UInt32(0);
        return w.ToArray();
    }

    private static string ReadVersion(Stream stream)
    {
        while (true)
        {
            var line = new StringBuilder();
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                if (b == '\n') break;
                if (b != '\r') line.Append((char)b);
                if (line.Length > 255) throw new IOException("version line too long");
            }
            if (line.ToString().StartsWith("SSH-", StringComparison.Ordinal)) return line.ToString();
        }
    }

    private static byte[] ReadPacket(Stream stream)
    {
        byte[] head = ReadExactly(stream, 4);
        int length = head[0] << 24 | head[1] << 16 | head[2] << 8 | head[3];
        if (length < 5 || length > 35000) throw new IOException("bad packet length");
        byte[] body = ReadExactly(stream, length);
        int padding = body[0];
        return body[1..(length - padding)];
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }
        return buffer;
    }

    private static void WritePacket(Stream stream, byte[] payload)
    {
        int padding = 8 - (4 + 1 + payload.Length) % 8;
        if (padding < 4) padding += 8;
        var w = new Wire();
        w.UInt32((uint)(1 + payload.Length + padding));
        w.Byte((byte)padding);
        w.Raw(payload);
        w.Raw(new byte[padding]);
        var bytes = w.ToArray();
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public void Dispose()
    {
        try { _listener.Stop(); } catch { }
        HostKey.Dispose();
    }

    private sealed class Wire
    {
        private readonly MemoryStream _ms = new();
        public void Byte(byte b) => _ms.WriteByte(b);
        public void Raw(byte[] b) => _ms.Write(b, 0, b.Length);
        public void UInt32(uint v) { Byte((byte)(v >> 24)); Byte((byte)(v >> 16)); Byte((byte)(v >> 8)); Byte((byte)v); }
        public void String(byte[] b) { UInt32((uint)b.Length); Raw(b); }
        public void String(string s) => String(Encoding.ASCII.GetBytes(s));
        public void Mpint(byte[] unsignedBigEndian)
        {
            int i = 0;
            while (i < unsignedBigEndian.Length && unsignedBigEndian[i] == 0) i++;
            var v = unsignedBigEndian[i..];
            if (v.Length > 0 && (v[0] & 0x80) != 0)
            {
                var padded = new byte[v.Length + 1];
                v.CopyTo(padded, 1);
                v = padded;
            }
            String(v);
        }
        public byte[] ToArray() => _ms.ToArray();
    }

    private sealed class Reader
    {
        private readonly byte[] _b;
        private int _p;
        public Reader(byte[] b, int start) { _b = b; _p = start; }
        public byte[] String()
        {
            int n = _b[_p] << 24 | _b[_p + 1] << 16 | _b[_p + 2] << 8 | _b[_p + 3];
            _p += 4;
            var s = _b[_p..(_p + n)];
            _p += n;
            return s;
        }
    }
}
