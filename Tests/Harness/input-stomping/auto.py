import sys, time, re, json, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from drive import Client, strip
# rules: ordered (regex, reply); target: regex that ends the run
RULES=[
 (r"Choice:\s*$", "__LOGIN__"),
 (r"Username:\s*$", "__USER__"),
 (r"Choose a username:\s*$", "__USER__"),
 (r"[Pp]assword:?\s*$", "__PASS__"),
 (r"Character name:\s*$", "__CHAR__"),
 (r"Your choice:\s*$", "__MENU__"),
 (r"(?i)press any key|press enter|press space|continue\.\.\.|\[Enter\]", ""),
 (r"\(Y/n\)\s*:?\s*$", "Y"),
 (r"\(y/N\)\s*:?\s*$", "N"),
 (r"(?i)\(y/n\)\s*:?\s*$", "Y"),
 (r"(?i)choice\s*[:>]?\s*$", "__CHOICE__"),
 (r"\[2\] Female:\s*$", "1"),
 (r"(?i)name\s*:\s*$", "__CHAR__"),
 (r">\s*$", ""),
]
def login_to_town(user, pw, char, target=r"Main Street >\s*$", header=None, telnet_reply=None, verbose=False, max_steps=60, register=False, client=None, telnet_reply_delay=0.0):
    c=client or Client(header, telnet_reply, telnet_reply_delay); menu_sent=False; choices=[]
    for step in range(max_steps):
        buf=c.read(1.2); text=strip(buf)
        if verbose and text.strip(): print("----"); print(text[-600:])
        if re.search(target, text): return c
        tail=text[-200:]
        for pat, reply in RULES:
            if re.search(pat, tail):
                if reply=="__LOGIN__": reply="R" if register else "L"; register=False
                elif reply=="__USER__": reply=user
                elif reply=="__PASS__": reply=pw
                elif reply=="__CHAR__": reply=char
                elif reply=="__MENU__":
                    if "Create new character" in text[-1500:]: reply="N"
                    elif "Quick" in text[-700:] and "[Q]" in text[-700:]: reply="Q"
                    else: reply="1"
                elif reply=="__CHOICE__":
                    reply = "Q" if ("Quick" in text[-700:] and "[Q]" in text[-700:]) else "1"
                c.line(reply); choices.append((tail[-40:].strip(), reply)); break
        else:
            if not text.strip(): c.line("")
    print("FAILED to reach target; choices:", choices); print(strip(c.log)[-1500:]); raise SystemExit(1)
if __name__=='__main__':
    c=login_to_town(sys.argv[1], sys.argv[2], sys.argv[3], verbose=False, register=(len(sys.argv)>4 and sys.argv[4]=="register"))
    print("=== REACHED TOWN ==="); print(strip(c.log)[-800:])
