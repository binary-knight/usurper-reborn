"""A client that drops its socket must end its session, not spin on empty input.

Usage (server and the jose/José account as in README and utf8_name.py):
    python3 hardclose.py town     # close at the Main Street prompt
    python3 hardclose.py fight    # close at a dungeon combat prompt
Expect "Connection lost" in the server log within a few seconds, "Session ended",
no CRASH line, and server CPU back at zero. Set USURPER_LOG to the server's log file.
"""
import sys, os, re, time, subprocess
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from auto import login_to_town
from drive import strip
LOG = os.environ.get("USURPER_LOG", "/tmp/usurper-harness/server.log")
PORT = os.environ.get("USURPER_PORT", "4999")
def cpu():
    p = subprocess.check_output(f"pgrep -f 'mud-port {PORT}'", shell=True).decode().split()[0]
    return subprocess.check_output(f"ps -o %cpu= -p {p}", shell=True).decode().strip()
where = sys.argv[1] if len(sys.argv) > 1 else "town"
A = login_to_town("jose", "secret123", "José", telnet_reply=b'\xff\xfd\x01\xff\xfd\x03'); A.read(2.0)
if where == "fight":
    A.line("D"); A.expect(r"Press Enter to continue"); A.line("")
    A.expect(r"Dungeon Fl\.\d+ >\s*$"); A.line("E")
    A.expect(r"\[F\] Fight the monsters"); A.line("F")
    A.expect(r"Choose action")
print(f"[{where}] at prompt; closing the socket without logging out")
size = len(open(LOG, errors="replace").read())
A.s.close(); time.sleep(2.5)
new = open(LOG, errors="replace").read()[size:]
lines = [l for l in new.splitlines() if "jose" in l.lower() or "CRASH" in l]
print(f"[{where}] connection lost logged:", any("Connection lost" in l for l in lines), "| session ended:", any("Session ended" in l for l in lines), "| crash logged:", any("CRASH" in l for l in lines))
print(f"[{where}] server cpu% now: {cpu()}"); time.sleep(3); print(f"[{where}] server cpu% after 3s: {cpu()}")
