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

    /// <summary>When true, writes are not tracked (server echo of keystrokes, the redraw itself).</summary>
    public bool Suppress { get; set; }

    public string CurrentLine
    {
        get { lock (_lock) return _csiStart >= 0 ? _line.ToString(0, _csiStart) : _line.ToString(); }
    }

    public void Reset()
    {
        lock (_lock) { _line.Clear(); _csiStart = -1; _escMode = 0; }
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

    // Escape-sequence state: 0 = text, 1 = after ESC, 2 = CSI (ESC [), 3 = OSC (ESC ]),
    // 4 = one-byte intermediate (ESC ( / ESC ) charset selects and similar).
    private int _escMode;
    private bool _oscEsc; // inside OSC, previous char was ESC (looking for the ST terminator ESC \)

    private void TrackCore(char c)
    {
        switch (_escMode)
        {
            case 1: // after ESC
                _line.Append(c);
                if (c == '[') { _escMode = 2; return; }
                if (c == ']') { _escMode = 3; _oscEsc = false; return; }
                if (c == '(' || c == ')' || c == '#' || c == '%') { _escMode = 4; return; }
                // Two-byte escape (ESC 7, ESC =, ...): drop it whole.
                _line.Length = _csiStart; _csiStart = -1; _escMode = 0;
                return;
            case 2: // CSI
                _line.Append(c);
                if (c >= '@' && c <= '~')
                {
                    // Final byte. Keep colour (m), drop everything else; 2J / 2K also empty the line.
                    bool clearAll = (c == 'J' || c == 'K') && _line.Length - _csiStart == 4 && _line[_csiStart + 2] == '2';
                    if (c != 'm') _line.Length = _csiStart;
                    _csiStart = -1; _escMode = 0;
                    if (clearAll) _line.Clear();
                }
                return;
            case 3: // OSC: runs to BEL or ESC \ ; never part of a prompt, drop it whole
                if (c == '\x07' || (_oscEsc && c == '\\'))
                {
                    _line.Length = _csiStart; _csiStart = -1; _escMode = 0;
                    return;
                }
                _oscEsc = c == '\x1b';
                return;
            case 4: // one intermediate byte then done
                _line.Length = _csiStart; _csiStart = -1; _escMode = 0;
                return;
        }

        if (c == '\n' || c == '\r') { _line.Clear(); return; }
        if (c == '\x1b') { _csiStart = _line.Length; _escMode = 1; _line.Append(c); return; }
        _line.Append(c);
        if (_line.Length > MaxLength)
        {
            // Trim from the front, but never inside an escape sequence: cut at the
            // first ESC on or after the cut point when there is one.
            int cut = _line.Length - MaxLength;
            for (int k = cut; k < _line.Length; k++)
            {
                if (_line[k] == '\x1b') { cut = k; break; }
            }
            _line.Remove(0, cut);
        }
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
