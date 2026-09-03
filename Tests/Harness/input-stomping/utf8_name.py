"""Multi-byte character names must round-trip through login, storage, /who, tells, and say.

Usage (server and beta as in README):
    python3 utf8_name.py jose José MUD register   # first run creates the account
    python3 utf8_name.py jose José WEB
    python3 utf8_name.py jose José SSH
Every line printed should say True.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from auto import login_to_town
from drive import strip
from ptyclient import PtyClient
USER, CHAR, kind = sys.argv[1], sys.argv[2], sys.argv[3]
register = len(sys.argv) > 4 and sys.argv[4] == "register"
PW = "secret123"
if kind == "MUD":
    A = login_to_town(USER, PW, CHAR, telnet_reply=b'\xff\xfd\x01\xff\xfd\x03', register=register)
elif kind == "WEB":
    A = login_to_town(USER, PW, CHAR, header=["X-IP:127.0.0.1", "X-Client:Web"], register=register)
elif kind == "SSH":
    A = login_to_town(USER, PW, CHAR, client=PtyClient(["--mud-relay", "--mud-port", os.environ.get("USURPER_PORT", "4999")]), register=register)
else:
    raise SystemExit("kind must be MUD, WEB, or SSH")
B = login_to_town("beta", PW, "Beta", telnet_reply=b'\xff\xfd\x01')
A.read(2.5); B.read(2.5)
B.line("/who"); who = strip(B.read(2.0)); B.line(""); B.read(1.0)   # /who ends with "press any key"
print(f"[{kind}] /who lists the name intact:", CHAR in who)
B.line(f"/tell {CHAR} ping ünïcode"); a = strip(A.read(2.5)); b = strip(B.read(1.5))
print(f"[{kind}] tell addressed by accented name delivered:", "tells you: ping ünïcode" in a, "| sender confirmation names them:", f"You tell {CHAR}:" in b)
B.line(f"/tell {USER} ping by username"); a = strip(A.read(2.5)); B.read(1.0)
print(f"[{kind}] tell by username delivered:", "ping by username" in a)
A.line(f"/say Hola desde {CHAR}"); A.read(1.5); b = strip(B.read(1.5))
print(f"[{kind}] say carries the name and text intact:", f"{CHAR} says: Hola desde {CHAR}" in b)
if kind == "SSH":
    os.kill(A.pid, 9)
