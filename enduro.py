"""Drives an enduro: three classes leaving the gate a minute apart, every
rider timed from their own class's gate.

Set the application up as a race with the classes starting in waves - MX1 at
the gate, MX2 +1:00, Youth +1:00 - with riders_250.csv loaded. Press START RACE
first, then run this: it counts its own clock from launch, so as long as it is
started after the gate every class's first crossing lands after its wave has
gone. Each class starts crossing one lap after its wave, so what the app sees
is what a real staggered field produces:

  MX1   crosses from about 0:45, four riders on 40-48 s laps
  MX2   crosses from about 1:45, four riders on 42-50 s laps
  Youth crosses from about 2:45, four riders on 46-56 s laps

A Youth rider is also read once at 0:20, long before Youth has started - a bike
wheeled over the loop on the way to the line - and must be ignored, not scored.

Keeps sending for two laps of the slowest pace after the race duration plus
the last wave's delay, so the race can finish. Pass the race length in
minutes as the only argument (default 3).
"""
import csv
import os
import socket
import sys
import time
from datetime import datetime

HOST, PORT = "127.0.0.1", 53135
WAVES = [("MX1", 0), ("MX2", 60), ("Youth", 60)]   # class, seconds after the previous wave
RIDERS_PER_CLASS = 4
PACE = {"MX1": (40, 48), "MX2": (42, 50), "Youth": (46, 56)}


def load_roster():
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "riders_250.csv")
    by_class = {}
    with open(path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            by_class.setdefault(row["class"], []).append(row["tagid"])
    return by_class


def send(sock, tag, count, note=""):
    now = datetime.now()
    msg = (f"DA{tag} {now.strftime('%H:%M:%S.%f')[:-3]} 10 "
           f"{count:05d} C7 date={now.strftime('%Y%m%d')}\r")
    sock.send(msg.encode("ascii"))
    print(f"  {now.strftime('%H:%M:%S')}  {tag} crossing {count} {note}", flush=True)


def main():
    minutes = float(sys.argv[1]) if len(sys.argv) > 1 else 3.0
    roster = load_roster()

    s = socket.socket()
    s.settimeout(15)
    s.connect((HOST, PORT))
    s.send(b"N0001EnduroSim\r")
    print(f"handshake: {s.recv(1024).decode('ascii', 'replace').strip()!r}", flush=True)
    now = datetime.now()
    s.send(f"GT{now.strftime('%H%M%S%f')[:-3]} date={now.strftime('%Y%m%d')}\r".encode())
    print(f"handshake: {s.recv(1024).decode('ascii', 'replace').strip()!r}", flush=True)

    start = time.time()
    wave_at = {}
    offset = 0
    for cls, delay in WAVES:
        offset += delay
        wave_at[cls] = start + offset

    pace, next_cross, counts = {}, {}, {}
    for cls, _ in WAVES:
        lo, hi = PACE[cls]
        tags = roster.get(cls, [])[:RIDERS_PER_CLASS]
        for i, tag in enumerate(tags):
            pace[tag] = lo + (hi - lo) * i / max(1, len(tags) - 1)
            # First crossing one lap after the wave, a couple of seconds apart.
            next_cross[tag] = wave_at[cls] + pace[tag] + i * 2.0
            counts[tag] = 0
        print(f"{cls}: {', '.join(tags)} from +{int(wave_at[cls] - start)}s", flush=True)

    # A fifth Youth rider, read once before their class has started.
    youth = roster.get("Youth", [])
    stray = youth[RIDERS_PER_CLASS] if len(youth) > RIDERS_PER_CLASS else None
    stray_sent = stray is None

    end = start + minutes * 60 + (wave_at[WAVES[-1][0]] - start) + 2 * max(pace.values())

    while time.time() < end:
        now = time.time()

        if not stray_sent and now >= start + 20:
            send(s, stray, 1, "(before Youth has started - must be ignored)")
            stray_sent = True

        for tag, due in list(next_cross.items()):
            if due <= now:
                counts[tag] += 1
                send(s, tag, counts[tag])
                next_cross[tag] = due + pace[tag]

        time.sleep(0.2)

    print("\nenduro complete", flush=True)
    s.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
