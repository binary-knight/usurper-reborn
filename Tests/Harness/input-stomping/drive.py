import socket, sys, time, re, json, os
def strip(b):
    s=b.decode('utf-8','replace'); s=re.sub(r'\x1b\[[0-9;?]*[A-Za-z]','',s); s=re.sub(r'[\x00-\x08\x0b-\x1f\x7f]','',s); return s
class Client:
    def __init__(self, header=None, telnet_reply=None, telnet_reply_delay=0.0):
        self.s=socket.create_connection(('127.0.0.1', int(os.environ.get('USURPER_PORT', '4999')))); self.s.settimeout(0.2); self.log=b''
        if header:
            for h in header: self.s.sendall(h.encode()+b'\n')
        if telnet_reply:
            # The server first waits ~500ms for an AUTH/X- header line, then sends its telnet
            # negotiation. A reply that must answer the TTYPE probe has to arrive after that.
            if telnet_reply_delay:
                # wait for the server's TTYPE SEND (IAC SB TTYPE SEND IAC SE) before answering
                end=time.time()+telnet_reply_delay+2.0; seen=b''
                while time.time()<end and b'\xff\xfa\x18\x01\xff\xf0' not in seen:
                    try: seen+=self.s.recv(65536)
                    except socket.timeout: pass
                self.log+=seen
            self.s.sendall(telnet_reply)
    def read(self, secs=1.0):
        out=b''; end=time.time()+secs
        while time.time()<end:
            try:
                d=self.s.recv(65536)
                if not d: break
                out+=d
            except socket.timeout: pass
        self.log+=out; return out
    def expect(self, pat, secs=8.0):
        buf=b''; end=time.time()+secs
        while time.time()<end:
            buf+=self.read(0.3)
            if re.search(pat, strip(buf)): return buf
        raise TimeoutError(f"expected {pat!r}; got:\n{strip(buf)[-1200:]}")
    def send(self, text): self.s.sendall(text.encode())
    def line(self, text): self.s.sendall(text.encode()+b'\r\n')
def run(steps, header=None, telnet_reply=None, tail=1500):
    c=Client(header, telnet_reply)
    for pat, reply in steps:
        buf=c.expect(pat)
        if reply is None: print("=== matched", repr(pat)); print(strip(buf)[-tail:]); return c
        c.line(reply)
    return c
if __name__=='__main__':
    steps=json.loads(sys.argv[1]); run(steps)
