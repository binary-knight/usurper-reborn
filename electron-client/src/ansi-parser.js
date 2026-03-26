// Usurper Reborn — ANSI Stream Parser
// Converts raw ANSI escape sequences into structured span data
// for rendering as styled HTML instead of raw terminal output.

// SGR code → CSS color mapping (matches TerminalEmulator.AnsiColorCodes)
const SGR_COLORS = {
  30: '#000000', 31: '#aa0000', 32: '#00aa00', 33: '#aa5500',
  34: '#5555ff', 35: '#aa00aa', 36: '#00aaaa', 37: '#c0c0c0',
  90: '#555555', 91: '#ff5555', 92: '#55ff55', 93: '#ffff55',
  94: '#5555ff', 95: '#ff55ff', 96: '#55ffff', 97: '#ffffff'
};

const DEFAULT_FG = '#c0c0c0';

class AnsiParser {
  constructor() {
    // Current text style state
    this.fg = DEFAULT_FG;
    this.bold = false;
    this.dim = false;

    // Parse buffer for incomplete sequences
    this.buffer = '';

    // Callback for parsed output
    this.onLine = null;       // (spans[]) => void — complete line
    this.onClearScreen = null; // () => void
    this.onBell = null;       // () => void
  }

  // Feed raw data from WebSocket into the parser
  feed(data) {
    this.buffer += (typeof data === 'string') ? data : new TextDecoder().decode(data);
    this._process();
  }

  _process() {
    const buf = this.buffer;
    let pos = 0;
    let currentSpans = [];
    let textStart = pos;

    while (pos < buf.length) {
      const ch = buf[pos];

      // ESC sequence
      if (ch === '\x1b') {
        // Flush text before escape
        if (pos > textStart) {
          const text = buf.substring(textStart, pos);
          if (text) currentSpans.push(this._makeSpan(text));
        }

        // Check if we have enough buffer for the sequence
        if (pos + 1 >= buf.length) {
          // Incomplete — save remainder and wait for more data
          this.buffer = buf.substring(pos);
          this._flushSpans(currentSpans);
          return;
        }

        const next = buf[pos + 1];

        if (next === '[') {
          // CSI sequence: ESC [ ... final_byte
          const csiEnd = this._findCSIEnd(buf, pos + 2);
          if (csiEnd === -1) {
            // Incomplete CSI — save and wait
            this.buffer = buf.substring(pos);
            this._flushSpans(currentSpans);
            return;
          }

          const params = buf.substring(pos + 2, csiEnd);
          const finalByte = buf[csiEnd];
          this._handleCSI(params, finalByte, currentSpans);
          pos = csiEnd + 1;
          textStart = pos;
          continue;
        }

        if (next === ']') {
          // OSC sequence: ESC ] ... BEL or ST
          const oscEnd = this._findOSCEnd(buf, pos + 2);
          if (oscEnd === -1) {
            this.buffer = buf.substring(pos);
            this._flushSpans(currentSpans);
            return;
          }
          // Skip OSC sequences (future: extract usurper JSON events)
          pos = oscEnd + 1;
          textStart = pos;
          continue;
        }

        // Unknown escape — skip ESC + next char
        pos += 2;
        textStart = pos;
        continue;
      }

      // Bell
      if (ch === '\x07') {
        if (pos > textStart) {
          currentSpans.push(this._makeSpan(buf.substring(textStart, pos)));
        }
        if (this.onBell) this.onBell();
        pos++;
        textStart = pos;
        continue;
      }

      // Newline — emit line
      if (ch === '\n') {
        if (pos > textStart) {
          const text = buf.substring(textStart, pos);
          // Strip trailing \r
          const clean = text.endsWith('\r') ? text.slice(0, -1) : text;
          if (clean) currentSpans.push(this._makeSpan(clean));
        }
        this._emitLine(currentSpans);
        currentSpans = [];
        pos++;
        textStart = pos;
        continue;
      }

      // Carriage return alone (without \n following)
      if (ch === '\r') {
        if (pos + 1 < buf.length && buf[pos + 1] === '\n') {
          // \r\n — let \n handler deal with it
          if (pos > textStart) {
            currentSpans.push(this._makeSpan(buf.substring(textStart, pos)));
          }
          textStart = pos; // include \r, \n handler strips it
          pos++;
          continue;
        }
        // Bare \r — carriage return (reset cursor to start of line)
        if (pos > textStart) {
          currentSpans.push(this._makeSpan(buf.substring(textStart, pos)));
        }
        // Could handle line overwrite here; for now just skip
        pos++;
        textStart = pos;
        continue;
      }

      pos++;
    }

    // Flush remaining text (no newline yet — partial line)
    if (pos > textStart) {
      currentSpans.push(this._makeSpan(buf.substring(textStart, pos)));
    }

    // If we have partial spans without a newline, keep them for inline display
    if (currentSpans.length > 0) {
      this._flushSpans(currentSpans);
    }

    this.buffer = '';
  }

  _findCSIEnd(buf, start) {
    for (let i = start; i < buf.length; i++) {
      const code = buf.charCodeAt(i);
      // Final byte is 0x40-0x7E (@ through ~)
      if (code >= 0x40 && code <= 0x7E) return i;
    }
    return -1; // Incomplete
  }

  _findOSCEnd(buf, start) {
    for (let i = start; i < buf.length; i++) {
      // BEL terminates OSC
      if (buf[i] === '\x07') return i;
      // ST (ESC \) terminates OSC
      if (buf[i] === '\x1b' && i + 1 < buf.length && buf[i + 1] === '\\') return i + 1;
    }
    return -1; // Incomplete
  }

  _handleCSI(params, finalByte, spans) {
    switch (finalByte) {
      case 'm': // SGR — Select Graphic Rendition (colors/styles)
        this._handleSGR(params);
        break;

      case 'H': // CUP — Cursor Position (or home if no params)
      case 'f': // HVP — same as CUP
        // Ignore cursor positioning for now
        break;

      case 'J': // ED — Erase Display
        if (params === '2' || params === '') {
          if (this.onClearScreen) this.onClearScreen();
        }
        break;

      case 'K': // EL — Erase Line
        // Ignore for now
        break;

      case 'A': case 'B': case 'C': case 'D': // Cursor movement
        // Ignore for now
        break;

      default:
        // Unknown CSI — ignore
        break;
    }
  }

  _handleSGR(params) {
    if (!params || params === '0') {
      // Reset all attributes
      this.fg = DEFAULT_FG;
      this.bold = false;
      this.dim = false;
      return;
    }

    const codes = params.split(';').map(Number);
    let i = 0;

    while (i < codes.length) {
      const code = codes[i];

      if (code === 0) {
        this.fg = DEFAULT_FG;
        this.bold = false;
        this.dim = false;
      } else if (code === 1) {
        this.bold = true;
      } else if (code === 2) {
        this.dim = true;
      } else if (code === 22) {
        this.bold = false;
        this.dim = false;
      } else if (code >= 30 && code <= 37) {
        // Standard foreground color
        if (this.bold && SGR_COLORS[code + 60]) {
          // Bold + standard = bright (BBS convention)
          this.fg = SGR_COLORS[code + 60];
        } else {
          this.fg = SGR_COLORS[code] || DEFAULT_FG;
        }
      } else if (code >= 90 && code <= 97) {
        // Bright foreground color
        this.fg = SGR_COLORS[code] || DEFAULT_FG;
      } else if (code === 38) {
        // Extended color: 38;2;R;G;B (24-bit) or 38;5;N (256-color)
        if (i + 1 < codes.length && codes[i + 1] === 2 && i + 4 < codes.length) {
          // 24-bit true color
          const r = codes[i + 2], g = codes[i + 3], b = codes[i + 4];
          this.fg = `#${r.toString(16).padStart(2,'0')}${g.toString(16).padStart(2,'0')}${b.toString(16).padStart(2,'0')}`;
          i += 4;
        } else if (i + 1 < codes.length && codes[i + 1] === 5 && i + 2 < codes.length) {
          // 256-color — map to closest standard color
          i += 2;
        }
      }
      // Ignore background colors (40-47, 100-107) for now

      i++;
    }
  }

  _makeSpan(text) {
    return {
      text: text,
      fg: this.fg,
      bold: this.bold,
      dim: this.dim
    };
  }

  _emitLine(spans) {
    // Filter empty spans
    const filtered = spans.filter(s => s.text.length > 0);
    if (this.onLine) {
      this.onLine(filtered.length > 0 ? filtered : [{ text: '', fg: DEFAULT_FG, bold: false, dim: false }]);
    }
  }

  _flushSpans(spans) {
    // Partial line (no newline yet) — emit as partial
    const filtered = spans.filter(s => s.text.length > 0);
    if (filtered.length > 0 && this.onPartialLine) {
      this.onPartialLine(filtered);
    }
  }
}

// Export for use in renderer
window.AnsiParser = AnsiParser;
