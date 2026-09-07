"""Drives an enduro: the classes leave the gate one after another and every
rider is timed from their own class's gate.

Two presets. The quick one is for checking the wave mechanics in a few
minutes; the full one is the race the club actually runs, in real time.

  enduro.py           three classes from riders_250.csv, four riders each, a
                      minute apart, 40-56 s laps, a 3-minute race. One Youth
                      rider is read at 0:20, before Youth has started, and
                      must be ignored.

  enduro.py --full    all 250 riders in all five classes, a minute apart,
                      15-20 minute laps, a 2-hour race, and a realistic field:
                      lap times spread per class and drifting with fatigue,
                      some riders retiring, a few missed reads, one duplicate
                      read, one early read, and the MX1 front lapping the
                      Youth tail. Takes about 2h45 of wall clock.

Set the application up as a race with the classes starting in waves in the
order below, press START RACE first, then run this: it counts its own clock
from launch, so as long as it is started after the gate every class's first
crossing lands after its wave has gone. Each class starts crossing one lap
after its wave, which is what a real staggered field produces.

Every crossing sent, and every one deliberately not sent, is written to
enduro_sent.jsonl beside this script so the recorded race can be checked
against what was actually put in.
"""
import argparse
import csv
import json
import os
import random
import socket
import sys
import time
from datetime import datetime

HOST, PORT = "127.0.0.1", 53135
HERE = os.path.dirname(os.path.abspath(__file__))
SENT_LOG = os.path.join(HERE, "enduro_sent.jsonl")

QUICK = dict(
    classes=["MX1", "MX2", "Youth"],
    riders_per_class=4,
    gap=60.0,
    minutes=3.0,
    pace={"MX1": (40, 48), "MX2": (42, 50), "Youth": (46, 56)},
    realistic=False,
)

FULL = dict(
    classes=["MX1", "MX2", "Quad", "Veterans", "Youth"],
    riders_per_class=None,             # everyone on the list
    gap=60.0,
    minutes=120.0,
    # Seconds per lap on a 10 km loop, fastest to slowest within the class.
    pace={"MX1": (900, 1000), "MX2": (950, 1060), "Quad": (1000, 1120),
          "Veterans": (1040, 1150), "Youth": (1080, 1200)},
    realistic=True,
)

# The realistic field, as fractions of the riders in it.
RETIRE_SHARE = 0.06        # stop after a random lap and never cross again
MISSED_READ_SHARE = 0.04   # one crossing not sent, so that lap arrives doubled
FATIGUE_PER_LAP = 0.008    # every lap a little slower than the last


def load_roster():
    path = os.path.join(HERE, "riders_250.csv")
    by_class = {}
    with open(path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            by_class.setdefault(row["class"], []).append(row["tagid"])
    return by_class


class Sim:
    def __init__(self, preset, minutes, gap, seed):
        self.rng = random.Random(seed)
        self.minutes = minutes if minutes is not None else preset["minutes"]
        self.gap = gap if gap is not None else preset["gap"]
        self.classes = preset["classes"]
        self.realistic = preset["realistic"]
        roster = load_roster()

        self.tags = {}          # class -> tags in the field
        self.pace = {}          # tag -> base seconds per lap
        for cls in self.classes:
            tags = roster.get(cls, [])
            if preset["riders_per_class"]:
                tags = tags[:preset["riders_per_class"]]
            self.tags[cls] = tags
            lo, hi = preset["pace"][cls]
            for i, tag in enumerate(tags):
                self.pace[tag] = lo + (hi - lo) * i / max(1, len(tags) - 1)

        self.field = [t for cls in self.classes for t in self.tags[cls]]
        self.cls_of = {t: cls for cls in self.classes for t in self.tags[cls]}

        # The incidents, decided up front so the plan can be printed and checked.
        self.retire_after = {}
        self.skip_read_at = {}
        self.duplicate_rider = None
        self.early_rider = None
        if self.realistic:
            expected_laps = int(self.minutes * 60 / max(self.pace.values()))
            for tag in self.rng.sample(self.field, max(1, int(len(self.field) * RETIRE_SHARE))):
                self.retire_after[tag] = self.rng.randint(2, max(2, expected_laps - 1))
            rest = [t for t in self.field if t not in self.retire_after]
            for tag in self.rng.sample(rest, max(1, int(len(self.field) * MISSED_READ_SHARE))):
                self.skip_read_at[tag] = self.rng.randint(3, max(3, expected_laps - 1))
            rest = [t for t in rest if t not in self.skip_read_at]
            self.duplicate_rider = self.rng.choice(rest)
        # An early read, quick or full: the last Youth rider in the field.
        youth = self.tags.get("Youth", [])
        self.early_rider = youth[-1] if youth else None

    def wave_offsets(self):
        return {cls: i * self.gap for i, cls in enumerate(self.classes)}


def send(sock, out, tag, count, sent_at, note=""):
    stamp = datetime.now()
    msg = (f"DA{tag} {stamp.strftime('%H:%M:%S.%f')[:-3]} 10 "
           f"{count:05d} C7 date={stamp.strftime('%Y%m%d')}\r")
    sock.send(msg.encode("ascii"))
    out.write(json.dumps({"tag": tag, "lap": count, "sent": stamp.strftime("%H:%M:%S.%f")[:-3],
                          "note": note}) + "\n")
    out.flush()
    if note or count <= 1:
        print(f"  {stamp.strftime('%H:%M:%S')}  {tag} crossing {count} {note}", flush=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--full", action="store_true", help="the real race: 250 riders, 5 classes, 2 hours")
    ap.add_argument("--minutes", type=float, help="race length set in the application")
    ap.add_argument("--gap", type=float, help="seconds between classes at the gate")
    ap.add_argument("--seed", type=int, default=2026, help="for a repeatable field")
    args = ap.parse_args()

    sim = Sim(FULL if args.full else QUICK, args.minutes, args.gap, args.seed)
    offsets = sim.wave_offsets()

    print(f"{len(sim.field)} riders, {len(sim.classes)} classes, {sim.minutes:.0f}-minute race, "
          f"{sim.gap:.0f} s between classes", flush=True)
    for cls in sim.classes:
        lo, hi = min(sim.pace[t] for t in sim.tags[cls]), max(sim.pace[t] for t in sim.tags[cls])
        print(f"  {cls:<9} {len(sim.tags[cls]):>3} riders, gate at +{offsets[cls]:.0f}s, laps {lo:.0f}-{hi:.0f} s", flush=True)
    if sim.retire_after:
        print(f"  retiring: {', '.join(f'{t} after lap {n}' for t, n in sorted(sim.retire_after.items()))}", flush=True)
    if sim.skip_read_at:
        print(f"  missed reads: {', '.join(f'{t} at lap {n}' for t, n in sorted(sim.skip_read_at.items()))}", flush=True)
    if sim.duplicate_rider:
        print(f"  duplicate read: {sim.duplicate_rider} 3 s after lap 3", flush=True)
    if sim.early_rider:
        print(f"  early read: {sim.early_rider} at +20 s, before {sim.cls_of[sim.early_rider]} has started", flush=True)

    s = socket.socket()
    s.settimeout(15)
    s.connect((HOST, PORT))
    s.send(b"N0001EnduroSim\r")
    print(f"handshake: {s.recv(1024).decode('ascii', 'replace').strip()!r}", flush=True)
    now = datetime.now()
    s.send(f"GT{now.strftime('%H%M%S%f')[:-3]} date={now.strftime('%Y%m%d')}\r".encode())
    print(f"handshake: {s.recv(1024).decode('ascii', 'replace').strip()!r}", flush=True)

    start = time.time()
    wave_at = {cls: start + offsets[cls] for cls in sim.classes}

    next_cross, counts, lap_pace = {}, {}, {}
    for cls in sim.classes:
        for i, tag in enumerate(sim.tags[cls]):
            # First crossing one lap after the wave, the field strung out
            # behind the gate a second or two per rider.
            next_cross[tag] = wave_at[cls] + sim.pace[tag] + i * 1.5
            counts[tag] = 0
            lap_pace[tag] = sim.pace[tag]

    duplicate_due = None
    early_sent = sim.early_rider is None
    end = start + sim.minutes * 60 + max(offsets.values()) + 2 * max(sim.pace.values())
    sent = 0
    out = open(SENT_LOG, "w", encoding="utf-8")

    while time.time() < end:
        now = time.time()

        if not early_sent and now >= start + 20:
            send(s, out, sim.early_rider, 1, now, "(before their class has started - must be ignored)")
            early_sent = True

        if duplicate_due is not None and now >= duplicate_due:
            tag = sim.duplicate_rider
            send(s, out, tag, counts[tag], now, "(duplicate read 3 s after the last - must be rejected)")
            duplicate_due = None

        for tag, due in list(next_cross.items()):
            if due > now:
                continue

            counts[tag] += 1
            lap = counts[tag]

            if sim.skip_read_at.get(tag) == lap:
                out.write(json.dumps({"tag": tag, "lap": lap, "sent": None, "note": "missed read - not sent"}) + "\n")
                print(f"  {datetime.now().strftime('%H:%M:%S')}  {tag} lap {lap} NOT sent (missed read)", flush=True)
            else:
                send(s, out, tag, lap, now)
                sent += 1
                if tag == sim.duplicate_rider and lap == 3:
                    duplicate_due = now + 3.0

            if sim.retire_after.get(tag) == lap:
                out.write(json.dumps({"tag": tag, "lap": lap, "sent": None, "note": "retired after this lap"}) + "\n")
                print(f"  {datetime.now().strftime('%H:%M:%S')}  {tag} retires after lap {lap}", flush=True)
                del next_cross[tag]
                continue

            if sim.realistic:
                lap_pace[tag] *= 1 + FATIGUE_PER_LAP
                next_cross[tag] = due + lap_pace[tag] * sim.rng.uniform(0.97, 1.03)
            else:
                next_cross[tag] = due + lap_pace[tag]

        time.sleep(0.2)

    out.close()
    print(f"\nenduro complete - {sent} crossings sent in {(time.time() - start) / 60:.0f} minutes", flush=True)
    s.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
