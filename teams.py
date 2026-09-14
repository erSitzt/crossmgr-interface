"""Drives a team race that produces every condition team events are meant to
handle, so the scoring, the per-rider breakdown and the two-on-track warning
can be checked against a known answer.

Set the app up with the wizard: **Race**, tick **Team event** on the riders
step and import riders_teams.csv, **4 minutes**, **0 extra laps**, clock starts
**when the first rider crosses**. Then run this script. (Manual start works
too: press START RACE, then run it straight away.)

The field, from riders_teams.csv:

  MSC Adler  - #11 Anna Berger and #12 Ben Fischer, each on their own
               transponder. Anna rides four laps and hands over to Ben. Ben is
               read once near the loop 3s after Anna crosses (waiting to take
               over): that read must be "not counted", not a lap.
  RC Falke   - #21 Carla Hoff and #22 David Kern sharing ONE transponder, the
               way teams used to be entered - and "rc falke" on David's row, so
               the grouping has to ignore case. Never flagged, no per-rider times.
  Team West  - #31 Elif Yilmaz (MX2) and #32 Frank Weber (MX1). Frank goes out
               while Elif is still riding: his read 15s after hers and hers 15s
               before his next are the TWO ON TRACK? laps. Classes tie, so the
               team is scored in the first row's class, MX2, with a warning.
  Greta Lang - "Team Sued" on her row only, so she races solo.
  Hugo Reiter- no team at all, solo.

Expected, as crossings arrive (seconds after the script starts; the clock
starts on Anna's read at 5s, so time runs out at 245s and RC Falke's 258s read
is the one that notices; everyone then finishes the lap they are on):

  Entry          Laps  Last counted  Notes
  Team West        9      289s       laps 4 (15.0s) and 6 (15.0s) TWO ON TRACK?
  RC Falke         7      258s       one shared line, no times
  MSC Adler        7      259s       Ben's 128s read "not counted"
  Greta Lang       7      266s
  Hugo Reiter      7      279s

  Order: Team West, then on 7 laps by total time RC Falke (253s), MSC Adler
  (254s), Greta (261s), Hugo (274s). Every entry's next read after that is
  refused - it arrives once the race is over, as a post-race crossing.

  Team members sheet:
    Team West   #31 Elif Yilmaz  4 laps  best 44.000  avg 44.000
                #32 Frank Weber  5 laps  best 45.000  avg 45.000
    RC Falke    #21 Carla Hoff / #22 David Kern  7 laps  shared transponder
    MSC Adler   #11 Anna Berger  4 laps  best 40.000  avg 40.000
                #12 Ben Fischer  3 laps  best 42.000  avg 42.000

  Delete Team West's laps 4 and 6 in Fix laps: both warnings clear, Team West
  drops to 7 laps (284s) and last place.

Modes:

  python teams.py
      As above.

  python teams.py --parked
      Ben stands near the loop and is read every 3s from 128s to 146s. Every
      one of those reads must be refused - the ones more than 10s after Anna's
      crossing because they are 3s after Ben's own previous read - and the
      "keeps being read near the loop" warning must appear once. Same answer
      sheet. Without the per-transponder rule the 137s read counted as a lap,
      giving MSC Adler 8 laps with a TWO ON TRACK? at lap 5.

  python teams.py --stray
      Ben rides on a spare, SPARE01, which is not on the rider list: it shows
      up as UNKNOWN from 175s. Before the flag at ~258s, right-click it,
      Identify, "This transponder belongs to a team", MSC Adler, #12 Ben
      Fischer. Its laps move to the team and the answer sheet is the same, with
      SPARE01 listed as Ben's transponder.

Crossing timestamps are computed exactly and put on the wire, as in
qualifying.py. Everything sent, including the reads that must be refused, is
written to teams_sent.jsonl.

Run with WINDOWS python (C:\\Python310\\python.exe). Under WSL2 NAT a WSL-side
script cannot reach the app.
"""
import argparse
import json
import socket
import sys
import time
from datetime import datetime, timedelta

HOST, PORT = "127.0.0.1", 53135

WHO = {
    "TEAMA01": "#11 Anna Berger (MSC Adler)",
    "TEAMA02": "#12 Ben Fischer (MSC Adler)",
    "SPARE01": "#12 Ben Fischer on a spare (MSC Adler)",
    "TEAMB00": "RC Falke (shared)",
    "TEAMC01": "#31 Elif Yilmaz (Team West)",
    "TEAMC02": "#32 Frank Weber (Team West)",
    "SOLO001": "#41 Greta Lang",
    "SOLO002": "#42 Hugo Reiter",
}


def schedule(parked, stray):
    """Every read as (offset_seconds, tag, note), in time order."""
    events = []

    def add(tag, offsets, note=""):
        events.extend((o, tag, note) for o in offsets)

    # MSC Adler: Anna's stint, then Ben's.
    add("TEAMA01", [5, 45, 85, 125])
    if parked:
        add("TEAMA02", [128, 131, 134, 137, 140, 143, 146], "waiting near the loop - refuse")
    else:
        add("TEAMA02", [128], "waiting near the loop - not counted")
    ben = "SPARE01" if stray else "TEAMA02"
    add(ben, [175], "handover")
    add(ben, [217, 259])
    add(ben, [301], "after the flag - refuse")

    # RC Falke: one transponder for both riders.
    add("TEAMB00", [6, 47, 88, 129])
    add("TEAMB00", [176], "handover (shared, unseen)")
    add("TEAMB00", [217, 258])
    add("TEAMB00", [299], "after the flag - refuse")

    # Team West: Frank goes out while Elif is still riding.
    add("TEAMC01", [7, 51, 95])
    add("TEAMC02", [110], "TWO ON TRACK?")
    add("TEAMC01", [139], "Elif comes in")
    add("TEAMC02", [154], "TWO ON TRACK?")
    add("TEAMC02", [199, 244, 289])
    add("TEAMC02", [334], "after the flag - refuse")

    # Solo riders.
    add("SOLO001", [8, 51, 94, 137, 180, 223, 266])
    add("SOLO001", [309], "after the flag - refuse")
    add("SOLO002", [9, 54, 99, 144, 189, 234, 279])
    add("SOLO002", [324], "after the flag - refuse")

    events.sort(key=lambda e: (e[0], e[1]))
    return events


def send(sock, tag, count, stamp):
    msg = (f"DA{tag} {stamp.strftime('%H:%M:%S.%f')} 10 "
           f"{count:05d} C7 date={stamp.strftime('%Y%m%d')}\r")
    sock.send(msg.encode("ascii"))


def handshake(sock):
    sock.send(b"N0001TeamsReader\r")
    print(f"handshake: {sock.recv(1024).decode('ascii', 'replace').strip()!r}", flush=True)
    now = datetime.now()
    sock.send(f"GT{now.strftime('%H%M%S%f')[:-3]} date={now.strftime('%Y%m%d')}\r".encode())
    print(f"handshake: {sock.recv(1024).decode('ascii', 'replace').strip()!r}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--parked", action="store_true",
                      help="Ben is read every 3s near the loop while waiting to take over")
    mode.add_argument("--stray", action="store_true",
                      help="Ben rides on a spare transponder that is not on the rider list")
    args = parser.parse_args()

    events = schedule(args.parked, args.stray)

    sock = socket.socket()
    sock.settimeout(15)
    sock.connect((HOST, PORT))
    handshake(sock)
    time.sleep(1)

    base = datetime.now()
    counts = {}
    print(f"\nrace starts on the first read at 5s; last read at {events[-1][0]:.0f}s", flush=True)
    if args.stray:
        print("SPARE01 turns up at 175s - identify it as #12 Ben Fischer of MSC Adler before ~258s.", flush=True)
    print(flush=True)

    with open("teams_sent.jsonl", "w", encoding="utf-8") as log:
        for offset, tag, note in events:
            due = base + timedelta(seconds=offset)
            wait = (due - datetime.now()).total_seconds()
            if wait > 0:
                time.sleep(wait)

            counts[tag] = counts.get(tag, 0) + 1

            # The exact intended moment goes on the wire, not datetime.now(), so
            # the lap times are exact rather than however long sleep() took.
            send(sock, tag, counts[tag], due)

            log.write(json.dumps({"offset": offset, "tag": tag, "who": WHO[tag],
                                  "at": due.isoformat(timespec="milliseconds"), "note": note}) + "\n")
            log.flush()
            print(f"  {offset:6.1f}s  {WHO[tag]:<40} {note}", flush=True)

    print("\nall reads sent - leaving the connection open so the reader stays green.", flush=True)
    print("Check the standings and the TWO ON TRACK? laps, then print the results.", flush=True)

    try:
        time.sleep(900)
    except KeyboardInterrupt:
        pass
    sock.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
