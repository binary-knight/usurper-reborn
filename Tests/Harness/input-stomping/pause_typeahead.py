"""v1.1.13: a command typed at a "Press Enter to continue" pause is the next command.

Usage (server and the alpha account as in README):
    python3 pause_typeahead.py MUD|WEB [command]
Enters the dungeon from Main Street, answers the first pause with the command (default /time)
instead of a bare Enter, and checks the next prompt ran it without another line being sent.
Pass: the pause says "Press Enter" (not "any key"), the dungeon prompt echoes the command,
and the command's own pause follows.
"""
import sys, os, re, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from auto import login_to_town
from drive import strip
kind = sys.argv[1] if len(sys.argv) > 1 else "MUD"
cmd = sys.argv[2] if len(sys.argv) > 2 else "/time"
if kind == "WEB":
    A = login_to_town("alpha", "secret123", "Alpha", header=["X-IP:127.0.0.1", "X-Client:Web"])
else:
    A = login_to_town("alpha", "secret123", "Alpha", telnet_reply=b'\xff\xfd\x01\xff\xfd\x03')
A.read(1.0)
A.line("D")
pause = strip(A.expect(r"Press (Enter|any key)[^\n]*$"))
print(f"[{kind}] pause prompt:", repr(pause.strip().splitlines()[-1]))
print(f"[{kind}] pause says Enter, not any key:", "Press Enter" in pause.splitlines()[-1] and "any key" not in pause.splitlines()[-1])
A.line(cmd)                      # typed at the pause, not a bare Enter
after = ""
for _ in range(6):               # later pauses on the way in still wait for their own Enter
    after += strip(A.read(2.0))
    if re.search(r">\s*" + re.escape(cmd), after): break
    if re.search(r"Press Enter[^\n]*$", after): A.line("")
ran = re.search(r">\s*" + re.escape(cmd), after) is not None
print(f"[{kind}] next prompt echoed {cmd!r}:", ran)
print(f"[{kind}] the command's own pause followed:", "Press Enter" in after[after.find(cmd):] if ran else False)
print(after[-600:])
