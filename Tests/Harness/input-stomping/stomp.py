import sys, time, re, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from auto import login_to_town
from drive import strip
kind=sys.argv[1]  # MUD | WEB
typed=sys.argv[2] if len(sys.argv)>2 else "hello wor"
if kind=="MUD":
    A=login_to_town("alpha","secret123","Alpha", telnet_reply=b'\xff\xfd\x01\xff\xfd\x03')   # DO ECHO, DO SGA
elif kind=="WEB":
    A=login_to_town("alpha","secret123","Alpha", header=["X-IP:127.0.0.1","X-Client:Web"])
elif kind=="SYNCTERM":
    # DO ECHO, DO SGA, WILL TTYPE, then TTYPE IS "SYNCTERM" so the server enables CP437
    A=login_to_town("alpha","secret123","Alpha", telnet_reply=b'\xff\xfd\x01\xff\xfd\x03\xff\xfb\x18\xff\xfa\x18\x00SYNCTERM\xff\xf0', telnet_reply_delay=0.9)
elif kind=="DESKTOP":
    # Steam/desktop client: sends AUTH on the raw TCP stream, holds and echoes its own line locally,
    # sends whole lines. The server must never echo or erase for it.
    from drive import Client
    c=Client(header=["AUTH:alpha:secret123:Steam:1.0.6"])
    A=login_to_town("alpha","secret123","Alpha", client=c)
B=login_to_town("beta","secret123","Beta", telnet_reply=b'\xff\xfd\x01')
A.read(1.0); B.read(1.0)
print(f"[{kind}] A at prompt; typing {typed!r} char by char")
if kind=="DESKTOP":
    echoed=A.read(1.5)   # desktop holds the line locally; nothing is sent until Enter
else:
    for ch in typed:
        A.send(ch); time.sleep(0.05)
    echoed=A.read(1.5)
print("  bytes A received while typing:", repr(echoed))
B.line(f"/tell Alpha ping")
got=A.read(2.5)
print("  B saw:", repr(strip(B.read(0.5))[-160:]))
print("  bytes A received when tell arrived:", repr(got))
A.line(typed if kind=="DESKTOP" else "")
after=A.read(2.0)
print("  after Enter, A got:", repr(strip(after)[:300]))
if kind=="SYNCTERM": print("  CP437 box byte present:", b'\xc9' in after, " UTF-8 box present:", '\u2554'.encode() in after)
