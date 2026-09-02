import sys, time, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from auto import login_to_town
from ptyclient import PtyClient
from drive import strip
typed = sys.argv[1] if len(sys.argv)>1 else "hello wor"
relay = PtyClient(["--mud-relay","--mud-port","4999"])
A = login_to_town("alpha","secret123","Alpha", client=relay)
B = login_to_town("beta","secret123","Beta", telnet_reply=b'\xff\xfd\x01')
A.read(1.0); B.read(1.0)
print("[SSH relay] PTY attrs during game:", A.attrs())
for ch in typed:
    A.send(ch); time.sleep(0.05)
echoed=A.read(1.5)
print("  bytes on the SSH user's screen while typing:", repr(echoed))
B.line("/tell Alpha ping")
got=A.read(2.5)
print("  bytes when tell arrived:", repr(got))
A.line("")
after=A.read(2.0)
print("  after Enter:", repr(strip(after)[:200]))
os.kill(relay.pid, 9)
