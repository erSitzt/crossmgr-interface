"""Load harness for a large field.

test_simulation.py staggers each rider's start by their index, which at 250
riders means the back of the field would not start for four minutes. This drives
a realistic mass start instead, and records the exact moment every crossing was
sent so the recorded times can be checked for drift and drops afterwards.
"""
import json
import random
import socket
import sys
import time
from datetime import datetime

HOST, PORT = "127.0.0.1", 53135
RIDERS = 250
RACE_MINUTES = 6.0        # how long to keep sending
GRID_SPREAD_S = 25.0      # mass start: whole field away inside this window
SENT_LOG = r"C:\Users\Public\CrossMgrRun\stress_sent.jsonl"


def connect():
    s = socket.socket()
    s.settimeout(15)
    s.connect((HOST, PORT))
    return s


def handshake(s):
    s.send(b"N0001StressReader-250\r")
    print("sent identification", flush=True)
    resp = s.recv(1024).decode("ascii", "replace")
    print(f"got: {resp.strip()!r}", flush=True)
    now = datetime.now()
    s.send(f"GT{now.strftime('%H%M%S%f')[:-3]} date={now.strftime('%Y%m%d')}\r".encode())
    resp = s.recv(1024).decode("ascii", "replace")
    print(f"got: {resp.strip()!r}", flush=True)


def main():
    random.seed(250)
    riders = [f"RIDER{i:03d}" for i in range(1, RIDERS + 1)]

    # Lap times spread across a plausible field: a fast front, a long tail.
    pace = {}
    for i, r in enumerate(riders):
        if i < 5:      pace[r] = random.uniform(34, 36)
        elif i < 40:   pace[r] = random.uniform(36, 40)
        elif i < 150:  pace[r] = random.uniform(40, 46)
        else:          pace[r] = random.uniform(46, 54)

    sock = connect()
    handshake(sock)
    time.sleep(1)

    start = time.time()
    end = start + RACE_MINUTES * 60
    # Mass start: everyone away within GRID_SPREAD_S, front runners first.
    next_cross = {r: start + (i / len(riders)) * GRID_SPREAD_S + random.uniform(0, 2)
                  for i, r in enumerate(riders)}
    lap = {r: 0 for r in riders}

    sent = 0
    bursts = []
    out = open(SENT_LOG, "w", encoding="utf-8")

    while time.time() < end:
        now = time.time()
        due = [r for r in riders if next_cross[r] <= now]
        for r in due:
            lap[r] += 1
            ts = datetime.now()
            msg = (f"DA{r} {ts.strftime('%H:%M:%S.%f')[:-3]} 10 "
                   f"{lap[r]:05d} C7 date={ts.strftime('%Y%m%d')}\r")
            try:
                sock.send(msg.encode("ascii"))
            except Exception as e:
                print(f"send failed after {sent}: {e}", flush=True)
                out.close()
                return 1
            out.write(json.dumps({"tag": r, "lap": lap[r],
                                  "sent": ts.strftime("%H:%M:%S.%f")[:-3]}) + "\n")
            sent += 1
            next_cross[r] = now + pace[r] + random.uniform(-2, 2)

        if due:
            bursts.append(len(due))
        time.sleep(0.25)

    out.close()
    elapsed = time.time() - start
    print(f"\nsent {sent} crossings in {elapsed:.0f}s "
          f"= {sent/elapsed:.1f}/s", flush=True)
    if bursts:
        print(f"burst size: max {max(bursts)}, mean {sum(bursts)/len(bursts):.1f}", flush=True)
    print(f"leaders reached lap {max(lap.values())}", flush=True)
    sock.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
