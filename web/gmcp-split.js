'use strict';
// Telnet splitter for the /play page (connections opened as /ws?gmcp=1 in MUD mode).
//
// The game offers IAC WILL GMCP to every interactive connection and turns GMCP on only if
// the client answers IAC DO GMCP inside a 250 ms window (MudServer.cs ProbeTtypeAsync).
// xterm.js never answers, so the proxy answers for the page, from the same host. This
// module reads the game's byte stream and splits it in two:
//   - terminal bytes, with every telnet command removed, go to xterm as binary frames;
//   - each IAC SB GMCP ... IAC SE frame goes to the page as one JSON text frame.
// Every other connection keeps the plain byte pipe; nothing here runs for them.

const IAC = 0xff;
const SE = 0xf0;
const SB = 0xfa;
const WILL = 0xfb;
const WONT = 0xfc;
const DO = 0xfd;
const DONT = 0xfe;
const GMCP = 0xc9;

// The game drops any whole frame over 16384 bytes (GmcpBridge.MaxPayloadBytes), counted
// with the framing, so a legal frame's payload is under 16 KB. The proxy keeps payloads up
// to 20 KB and drops larger ones whole.
const MAX_FRAME_BYTES = 20 * 1024;
// A subnegotiation still open after this many bytes has no end the proxy can trust. The
// session is closed rather than guessing where terminal text starts again.
const MAX_SB_BYTES = 64 * 1024;

const DO_GMCP = Buffer.from([IAC, DO, GMCP]);
const PACKAGE_NAME = /^[A-Za-z][A-Za-z0-9_.]{0,63}$/;

// True when a websocket request URL asks for the split stream (/ws?gmcp=1).
function wantsGmcp(reqUrl) {
  try {
    return new URL(String(reqUrl || ''), 'http://localhost').searchParams.get('gmcp') === '1';
  } catch {
    return false;
  }
}

// One GMCP payload ("Package.Name {json}") as the text frame the page reads, or null when the
// package name or the JSON is not valid. A package with no JSON gets data null.
function gmcpEnvelope(payload) {
  const text = payload.toString('utf8');
  const space = text.indexOf(' ');
  const pkg = space === -1 ? text : text.slice(0, space);
  if (!PACKAGE_NAME.test(pkg)) return null;
  const body = space === -1 ? '' : text.slice(space + 1).trim();
  let data = null;
  if (body) {
    try { data = JSON.parse(body); } catch { return null; }
  }
  return JSON.stringify({ gmcp: pkg, data });
}

// A splitter for one session. Handlers:
//   onTerminal(buf)  terminal bytes, telnet commands removed
//   onGmcp(json)     one JSON text frame for the page
//   onReply(buf)     bytes to write back to the game (IAC DO GMCP, once)
//   onFatal(reason)  framing can no longer be trusted; the caller closes the session
// push(chunk) takes the game's bytes in any chunking; the state carries across chunks.
function createGmcpSplitter({ onTerminal, onGmcp, onReply, onFatal, maxFrameBytes = MAX_FRAME_BYTES, maxSbBytes = MAX_SB_BYTES }) {
  // States: data, iac (after IAC), opt (after WILL/WONT/DO/DONT), sbopt (after IAC SB),
  // sb (inside a subnegotiation), sbiac (IAC inside a subnegotiation), dead.
  let state = 'data';
  let cmd = 0;
  let sbIsGmcp = false;
  let sbBytes = 0;
  const frame = Buffer.alloc(maxFrameBytes);
  let frameLen = 0;
  let oversized = false;
  let answeredGmcp = false;

  function fail(reason) {
    state = 'dead';
    onFatal(reason);
  }

  function endFrame() {
    if (sbIsGmcp && !oversized) {
      const json = gmcpEnvelope(frame.subarray(0, frameLen));
      if (json) onGmcp(json);
    }
    frameLen = 0;
    oversized = false;
  }

  function sbByte(b) {
    sbBytes += 1;
    if (sbBytes > maxSbBytes) { fail('subnegotiation over ' + maxSbBytes + ' bytes with no end'); return; }
    if (!sbIsGmcp || oversized) return;
    if (frameLen >= maxFrameBytes) { oversized = true; frameLen = 0; return; }
    frame[frameLen++] = b;
  }

  function push(chunk) {
    if (state === 'dead') return;
    const out = [];
    let runStart = -1;
    const flushRun = (end) => {
      if (runStart !== -1 && end > runStart) out.push(chunk.subarray(runStart, end));
      runStart = -1;
    };
    for (let i = 0; i < chunk.length && state !== 'dead'; i++) {
      const b = chunk[i];
      switch (state) {
        case 'data':
          if (b === IAC) { flushRun(i); state = 'iac'; }
          else if (runStart === -1) runStart = i;
          break;
        case 'iac':
          if (b === IAC) { out.push(Buffer.of(IAC)); state = 'data'; }
          else if (b === WILL || b === WONT || b === DO || b === DONT) { cmd = b; state = 'opt'; }
          else if (b === SB) state = 'sbopt';
          else state = 'data'; // NOP, GA, a stray SE and the rest: one byte, dropped
          break;
        case 'opt':
          if (cmd === WILL && b === GMCP && !answeredGmcp) { answeredGmcp = true; onReply(DO_GMCP); }
          state = 'data';
          break;
        case 'sbopt':
          sbIsGmcp = b === GMCP;
          sbBytes = 0;
          frameLen = 0;
          oversized = false;
          state = 'sb';
          break;
        case 'sb':
          if (b === IAC) state = 'sbiac';
          else sbByte(b);
          break;
        case 'sbiac':
          if (b === SE) { endFrame(); state = 'data'; }
          else if (b === IAC) { sbByte(IAC); if (state !== 'dead') state = 'sb'; }
          else fail('IAC ' + b + ' inside a subnegotiation');
          break;
        default:
          break;
      }
      // A partial frame is never written to the terminal: only 'data' bytes reach out.
    }
    flushRun(chunk.length);
    if (out.length) onTerminal(out.length === 1 ? out[0] : Buffer.concat(out));
  }

  return { push, get dead() { return state === 'dead'; } };
}

module.exports = { wantsGmcp, gmcpEnvelope, createGmcpSplitter, MAX_FRAME_BYTES, MAX_SB_BYTES };
