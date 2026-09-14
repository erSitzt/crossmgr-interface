"""A full-size team race: 40 riders in 20 teams from riders_teams_40.csv, for
seeing how the live views and the results sheets cope with a real field rather
than with the handful of entries in teams.py.

Set the app up with the wizard: **Race**, tick **Team event** and import
riders_teams_40.csv, **5 minutes**, **0 extra laps**, clock starts **when the
first rider crosses**. Then run this script. It takes about 7 minutes.

Every team is two riders taking turns in stints of two to four laps, each rider
at their own pace, with 6-12s of changeover on every handover lap. Three teams
(Team Speedline, Hardenduro Crew, Team Zweitakt) share one transponder. Three
things go wrong on purpose:

  RSV Blitz       - the rider taking over is read 3s after the one coming in
                    crosses, at the first handover: "not counted".
  Enduro Freunde  - the second rider goes out early once: a TWO ON TRACK? lap,
    Lahntal         and the other rider's next crossing is flagged too.
  Dirt Devils     - one crossing is never sent: a lap of about double length,
                    flagged CHECK.

The schedule comes from a fixed seed, so every run sends the same reads.
Everything sent is written to teams_40_sent.jsonl.

Run with WINDOWS python (C:\\Python310\\python.exe). Under WSL2 NAT a WSL-side
script cannot reach the app.
"""
import csv
import json
import os
import random
import socket
import sys
import time
from datetime import datetime, timedelta

HOST, PORT = "127.0.0.1", 53135
RACE_SECONDS = 300
# Two laps past the clock, so every team finishes the lap it is on.
LAST_READ = 5 + RACE_SECONDS + 120

WAITING_TEAM = "RSV Blitz"
OVERLAP_TEAM = "Enduro Freunde Lahntal"
MISSED_TEAM = "Dirt Devils"


def load_teams():
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "riders_teams_40.csv")
    teams = {}
    with open(path, encoding="utf-8") as f:
        for row in csv.DictReader(f):
            teams.setdefault(row["team"], []).append(row)
    return teams


def schedule(teams):
    """Every read as (offset_seconds, tag, who, note), in time order."""
    rng = random.Random(40)
    events = []

    for index, (team, riders) in enumerate(teams.items()):
        paces = [rng.uniform(40.0, 52.0) for _ in riders]
        rider = 0
        stint = rng.randint(2, 4)
        in_stint = 0
        handovers = 0
        lap = 0
        t = 5.0 + index

        def add(offset, who, note=""):
            events.append((round(offset, 3), riders[who]["tagid"],
                           f"#{riders[who]['number']} {riders[who]['name']} ({team})", note))

        add(t, rider, "start")

        while True:
            lap += 1
            duration = paces[rider] + rng.uniform(-1.5, 1.5)
            note = ""

            in_stint += 1
            if in_stint > stint:
                rider = 1 - rider
                in_stint = 1
                stint = rng.randint(2, 4)
                duration += rng.uniform(6.0, 12.0)
                handovers += 1
                note = "handover"

            if team == OVERLAP_TEAM and lap == 5:
                # The other rider is already out: read at 40% of this lap.
                add(t + duration * 0.4, 1 - rider, "TWO ON TRACK?")

            t += duration
            if t > LAST_READ:
                break

            if team == MISSED_TEAM and lap == 4:
                continue  # never sent: the next lap arrives double length

            add(t, rider, note)

            if team == WAITING_TEAM and note == "handover" and handovers == 1:
                # The rider going out next is still standing by the loop.
                add(t + 3.0, 1 - rider, "waiting near the loop - not counted")

    events.sort(key=lambda e: (e[0], e[1]))
    return events


def send(sock, tag, count, stamp):
    msg = (f"DA{tag} {stamp.strftime('%H:%M:%S.%f')} 10 "
           f"{count:05d} C7 date={stamp.strftime('%Y%m%d')}\r")
    sock.send(msg.encode("ascii"))


def handshake(sock):
    sock.send(b"N0001Teams40Reader\r")
    print(f"handshake: {sock.recv(1024).decode('ascii', 'replace').strip()!r}", flush=True)
    now = datetime.now()
    sock.send(f"GT{now.strftime('%H%M%S%f')[:-3]} date={now.strftime('%Y%m%d')}\r".encode())
    print(f"handshake: {sock.recv(1024).decode('ascii', 'replace').strip()!r}", flush=True)


def main():
    events = schedule(load_teams())

    sock = socket.socket()
    sock.settimeout(15)
    sock.connect((HOST, PORT))
    handshake(sock)
    time.sleep(1)

    base = datetime.now()
    counts = {}
    print(f"\n{len(events)} reads, the last at {events[-1][0]:.0f}s\n", flush=True)

    with open("teams_40_sent.jsonl", "w", encoding="utf-8") as log:
        for offset, tag, who, note in events:
            due = base + timedelta(seconds=offset)
            wait = (due - datetime.now()).total_seconds()
            if wait > 0:
                time.sleep(wait)

            counts[tag] = counts.get(tag, 0) + 1
            send(sock, tag, counts[tag], due)

            log.write(json.dumps({"offset": offset, "tag": tag, "who": who,
                                  "at": due.isoformat(timespec="milliseconds"), "note": note},
                                 ensure_ascii=False) + "\n")
            log.flush()
            print(f"  {offset:7.1f}s  {who:<55} {note}", flush=True)

    print("\nall reads sent - leaving the connection open so the reader stays green.", flush=True)
    try:
        time.sleep(900)
    except KeyboardInterrupt:
        pass
    sock.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
