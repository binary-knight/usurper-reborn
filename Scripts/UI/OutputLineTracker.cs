using System;
using System.IO;
using System.Text;

/// <summary>
/// v1.0.6: remembers the text the game has written on the current output line
/// (everything since the last newline, carriage return, or clear), so that when a
/// chat or world message arrives while the player is typing, the terminal can
/// redraw the real prompt line afterwards. Before this the redraw printed
/// GetInput's <c>prompt</c> argument, which is usually empty or "Your choice:",
/// while the visible prompt ("[25hp 100st] Main Street >") had been written
/// separately and was lost after every message.
///
/// Only SGR colour sequences are kept; cursor movement and erase sequences are
/// dropped so the stored line can be replayed verbatim. ESC[2J and ESC[2K empty it.
/// </summary>
public sealed class OutputLineTracker
{
    private const int MaxLength = 1024;
    private readonly StringBuilder _line = new();
    private readonly object _lock = new();
    private int _csiStart = -1;   // index in _line of a pending ESC, -1 when not inside a sequence
    private bool _csiHasBracket;

    /// <summary>When true, writes are not tracked (server echo of keystrokes, the redraw itself).</summary>
    public bool Suppress { get; set; }

    public string CurrentLine
    {
        get { lock (_lock) return _csiStart >= 0 ? _line.ToString(0, _csiStart) : _line.ToString(); }
    }

    public void Reset()
    {
        lock (_lock) { _line.Clear(); _csiStart = -1; }
    }

    public void Track(char c)
    {
        if (Suppress) return;
        lock (_lock) TrackCore(c);
    }

    public void Track(string? text)
    {
        if (Suppress || string.IsNullOrEmpty(text)) return;
        lock (_lock)
        {
            foreach (var c in text) TrackCore(c);
        }
    }

    private void TrackCore(char c)
    {
        if (_csiStart >= 0)
        {
            _line.Append(c);
            if (!_csiHasBracket)
            {
                if (c == '[') { _csiHasBracket = true; return; }
                // ESC followed by something other than '[' (e.g. ESC 7): not a CSI, drop it.
                _line.Length = _csiStart; _csiStart = -1; return;
            }
            if (c >= '@' && c <= '~')
            {
                // Final byte. Keep colour (m), drop everything else; 2J / 2K also empty the line.
                bool clearAll = (c == 'J' || c == 'K') && _line.Length - _csiStart == 4 && _line[_csiStart + 2] == '2';
                if (c != 'm') _line.Length = _csiStart;
                _csiStart = -1;
                if (clearAll) _line.Clear();
            }
            return;
        }

        if (c == '\n' || c == '\r') { _line.Clear(); return; }
        if (c == '\x1b') { _csiStart = _line.Length; _csiHasBracket = false; _line.Append(c); return; }
        _line.Append(c);
        if (_line.Length > MaxLength) _line.Remove(0, _line.Length - MaxLength);
    }
}

/// <summary>
/// StreamWriter that feeds every write through an <see cref="OutputLineTracker"/>.
/// The span overload is deliberately not overridden: StreamWriter routes it to
/// Write(char[], int, int) for subclasses, so tracking it too would double-count.
/// </summary>
internal sealed class LineTrackingStreamWriter : StreamWriter
{
    private readonly OutputLineTracker _tracker;

    public LineTrackingStreamWriter(Stream stream, Encoding encoding, OutputLineTracker tracker) : base(stream, encoding)
    {
        _tracker = tracker;
    }

    public override void Write(char value) { _tracker.Track(value); base.Write(value); }
    public override void Write(string? value) { _tracker.Track(value); base.Write(value); }
    public override void Write(char[]? buffer) { if (buffer != null) _tracker.Track(new string(buffer)); base.Write(buffer); }
    public override void Write(char[] buffer, int index, int count) { _tracker.Track(new string(buffer, index, count)); base.Write(buffer, index, count); }
    public override void WriteLine(string? value) { _tracker.Track(value); _tracker.Track('\n'); base.WriteLine(value); }
    public override void WriteLine() { _tracker.Track('\n'); base.WriteLine(); }
}
