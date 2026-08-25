"""Drives a small race that deliberately produces every condition the
correction dialog is meant to handle, so the dialog can be exercised on purpose
rather than waiting for a random race to happen to generate them.

  Carla Hoff (RIDER003)  - a missed read at lap 4, so the lap runs ~2x long and
                           should be flagged CHECK
  Elif Yilmaz (RIDER005) - a duplicate read a few seconds after lap 3, which
                           should be rejected as a short lap and be restorable
  STRAY99                - a transponder not in the rider list, so it shows as
                           UNKNOWN and can be identified or merged
  everyone else          - clean laps, for add / edit / delete / DNF
"""
import socket
import sys
import time
from datetime import datetime

HOST, PORT = "127.0.0.1", 53135
LAP = 26.0            # seconds per lap: short, so laps accumulate quickly
LAPS = 6
RIDERS = [f"RIDER{i:03d}" for i in range(1, 9)]
STRAY = "STRAY99"


def send(sock, tag, count):
    now = datetime.now()
    msg = (f"DA{tag} {now.strftime('%H:%M:%S.%f')[:-3]} 10 "
           f"{count:05d} C7 date={now.strftime('%Y%m%d')}\r")
    sock.send(msg.encode("ascii"))
    print(f"  {now.strftime('%H:%M:%S')}  {tag} lap {count}", flush=True)


def main():
    s = socket.socket()
    s.settimeout(15)
    s.connect((HOST, PORT))
    s.send(b"N0001ScenarioReader\r")
    print(f"handshake: {s.recv(1024).decode('ascii','replace').strip()!r}", flush=True)
    now = datetime.now()
    s.send(f"GT{now.strftime('%H%M%S%f')[:-3]} date={now.strftime('%Y%m%d')}\r".encode())
    print(f"handshake: {s.recv(1024).decode('ascii','replace').strip()!r}", flush=True)
    time.sleep(1)

    counts = {r: 0 for r in RIDERS}
    counts[STRAY] = 0

    for lap in range(1, LAPS + 1):
        for i, r in enumerate(RIDERS):
            # Carla misses her lap-4 read entirely: no message is sent, so her
            # lap 4 arrives as one long lap and should be flagged.
            if r == "RIDER003" and lap == 4:
                print(f"  -- skipping {r} lap {lap} (simulated missed read)", flush=True)
                continue
            counts[r] += 1
            send(s, r, counts[r])
            time.sleep(LAP / (len(RIDERS) + 1))

        # The stray transponder joins from lap 2, so it has laps of its own.
        if lap >= 2:
            counts[STRAY] += 1
            send(s, STRAY, counts[STRAY])

        # Elif gets a second read four seconds after her lap-3 crossing: too
        # soon to be a lap, so it should be rejected and reviewable.
        if lap == 3:
            time.sleep(4)
            print("  -- duplicate read for RIDER005 (should be rejected)", flush=True)
            counts["RIDER005"] += 1
            send(s, "RIDER005", counts["RIDER005"])
            counts["RIDER005"] -= 1

    print("\nscenario complete - leaving the connection open", flush=True)
    time.sleep(600)
    s.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
