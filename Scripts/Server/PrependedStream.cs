using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UsurperRemake.Server;

/// <summary>
/// v0.64.0: Stream wrapper that serves a pre-read byte prefix before
/// delegating to the underlying stream. Used by MudServer.HandleConnectionAsync
/// when PROXY protocol detection reads bytes that turn out NOT to be a PROXY
/// header (so they're real user data) and need to be re-injected into the
/// subsequent ReadLineAsync auth flow.
///
/// After the prefix is exhausted, reads pass through directly to the
/// underlying stream. Writes always pass through. The wrapper does not
/// own the underlying stream -- Dispose/Close is a no-op on the underlying
/// (the original NetworkStream stays owned by its TcpClient).
/// </summary>
internal sealed class PrependedStream : Stream
{
    private readonly Stream _underlying;
    private byte[]? _prefix;
    private int _prefixPos;

    public PrependedStream(Stream underlying, byte[] prefix)
    {
        _underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
        _prefix = prefix != null && prefix.Length > 0 ? prefix : null;
        _prefixPos = 0;
    }

    /// <summary>
    /// True if a non-blocking read would return data immediately: either the
    /// prefix has unread bytes, or the underlying NetworkStream has data
    /// available. Used by ReadLineAsync's \r\n drain optimization.
    /// </summary>
    public bool HasReadableData
    {
        get
        {
            if (_prefix != null && _prefixPos < _prefix.Length) return true;
            if (_underlying is System.Net.Sockets.NetworkStream ns) return ns.DataAvailable;
            return false;
        }
    }

    public override bool CanRead => _underlying.CanRead;
    public override bool CanWrite => _underlying.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_prefix != null)
        {
            int avail = _prefix.Length - _prefixPos;
            int take = Math.Min(avail, count);
            Array.Copy(_prefix, _prefixPos, buffer, offset, take);
            _prefixPos += take;
            if (_prefixPos >= _prefix.Length) _prefix = null;
            return take;
        }
        return _underlying.Read(buffer, offset, count);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        if (_prefix != null)
        {
            int avail = _prefix.Length - _prefixPos;
            int take = Math.Min(avail, count);
            Array.Copy(_prefix, _prefixPos, buffer, offset, take);
            _prefixPos += take;
            if (_prefixPos >= _prefix.Length) _prefix = null;
            return take;
        }
        return await _underlying.ReadAsync(buffer, offset, count, ct);
    }

    public override void Write(byte[] buffer, int offset, int count)
        => _underlying.Write(buffer, offset, count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => _underlying.WriteAsync(buffer, offset, count, ct);

    public override void Flush() => _underlying.Flush();
    public override Task FlushAsync(CancellationToken ct) => _underlying.FlushAsync(ct);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    // Do NOT dispose the underlying stream -- it's owned by the TcpClient.
    protected override void Dispose(bool disposing) { /* no-op */ }
}
