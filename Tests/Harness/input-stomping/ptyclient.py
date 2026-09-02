import os, pty, sys, time, re, select, termios
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from drive import strip
EXE=os.environ.get("USURPER_EXE", os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../../bin/Release/net8.0/UsurperReborn"))
class PtyClient:
    """Runs the relay under a real PTY (cooked mode by default), like sshd ForceCommand."""
    def __init__(self, args, raw=False):
        pid, fd = pty.fork()
        if pid == 0:
            os.execv(EXE, [EXE]+args)
        self.pid, self.fd = pid, fd; self.log=b''
        if raw:
            attrs=termios.tcgetattr(fd); attrs[3] &= ~(termios.ICANON|termios.ECHO); termios.tcsetattr(fd, termios.TCSANOW, attrs)
    def read(self, secs=1.0):
        out=b''; end=time.time()+secs
        while time.time()<end:
            r,_,_=select.select([self.fd],[],[],0.1)
            if r:
                try: d=os.read(self.fd,65536)
                except OSError: break
                if not d: break
                out+=d
        self.log+=out; return out
    def send(self, text): os.write(self.fd, text.encode())
    def line(self, text): os.write(self.fd, text.encode()+b'\r')
    def attrs(self):
        a=termios.tcgetattr(self.fd); return {"ICANON": bool(a[3]&termios.ICANON), "ECHO": bool(a[3]&termios.ECHO)}
if __name__=='__main__':
    c=PtyClient(["--mud-relay","--mud-port","4999"])
    print(strip(c.read(3.0))[-800:]); print(c.attrs())
    os.kill(c.pid, 9)
