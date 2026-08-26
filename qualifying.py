"""Drives a timed qualifying session that produces every condition the gate
pick sheet is meant to handle, so the ranking can be checked against a known
answer rather than against whatever a random session happened to produce.

Import riders_qualifying.csv first, and set the session up as **Timed
qualifying** with a **6 minute** length.

What the field is built to prove:

  Ben Fischer (#22)    - the FEWEST timed laps but the quickest one, so he
                         takes pole. If the sheet is sorting on laps completed
                         he will not be first, which is the whole point.
  David Kern (#44)     - best lap of exactly 42.000, set on his lap 2
  Anna Berger (#11)    - best lap of exactly 42.000 too, set on her lap 5, so
                         she ranks behind David: whoever set it first wins.
  Greta Lang (#77)     - goes out, completes only an out-lap, so NO TIME
  Ida Vogt (#9)        - on the roster, never leaves the paddock
  Jonas Ritter (#10)   - the same, and numbered so the two of them also show
                         whether start numbers sort numerically: Ida (9) must
                         come before Jonas (10), not after.

Expected overall order:

   1  #22 Ben Fischer     38.500   MX1
   2  #55 Elif Yilmaz     39.200   MX2
   3  #33 Carla Hoff      40.000   MX1
   4  #66 Frank Weber     41.000   MX2
   5  #44 David Kern      42.000   MX1   <- set first
   6  #11 Anna Berger     42.000   MX1   <- set later
   7  #88 Hugo Reiter     43.500   MX2
   8  #77 Greta Lang      NO TIME
   9  #9  Ida Vogt        NO TIME (did not go out)
  10  #10 Jonas Ritter    NO TIME (did not go out)

Two modes:

  python qualifying.py
      Everyone is back in the paddock by about 4:30, so the 6 minute clock runs
      out with nobody on track. Nothing crosses the loop to notice. That used to
      hang the session on a running clock at 00:00 forever.

  python qualifying.py --late
      Frank Weber stays out. He crosses ~37s after the flag with a 38.000 lap,
      which must COUNT and take pole off Ben - then crosses again, which must be
      rejected because the flag only buys one more lap.

Crossing timestamps are computed exactly and put on the wire, so the lap times
land on the intended values to the microsecond. That is what makes the tie-break
above testable at all - two laps paced by sleep() are never exactly equal.

Run with WINDOWS python (C:\\Python310\\python.exe). Under WSL2 NAT a WSL-side
script cannot reach the app.
"""
import argparse
import socket
import sys
import time
from datetime import datetime, timedelta

HOST, PORT = "127.0.0.1", 53135

# (tag, number, name, out-lap seconds, [lap times...])
FIELD = [
    ("QUAL001", "11", "Anna Berger",  20.0, [45.0, 44.0, 43.5, 42.0, 43.0]),
    ("QUAL002", "22", "Ben Fischer",  25.0, [46.0, 38.5]),
    ("QUAL003", "33", "Carla Hoff",   18.0, [41.0, 40.0, 41.5, 40.5, 42.0, 41.0]),
    ("QUAL004", "44", "David Kern",   22.0, [42.0, 44.5, 45.0]),
    ("QUAL005", "55", "Elif Yilmaz",  21.0, [40.0, 39.2, 40.5, 39.8]),
    ("QUAL006", "66", "Frank Weber",  23.0, [43.0, 41.0, 42.0]),
    ("QUAL007", "77", "Greta Lang",   24.0, []),
    ("QUAL008", "88", "Hugo Reiter",  26.0, [44.0, 43.5, 45.0]),
    # Ida and Jonas are on the roster and never send a read at all.
]

# Frank's extra laps in --late mode. The 38.0 completes after the flag and must
# count; the 42.0 after it must not.
FRANK_LATE = [42.0, 42.0, 42.0, 42.0, 42.0, 38.0, 42.0]

# --tags rewrites the field to exercise the transponder check instead of the
# ranking: every category it can report has to be produced on purpose, because
# waiting for a real bad tag to turn up is not a test.
#
#   Carla   - two missed reads, so two laps run at ~2x her pace
#   David   - four clean laps and then silence while the rest ride on
#   Elif    - a second read four seconds after each of two laps
#   Greta   - out-lap only, as before
#   Ida/Jonas - on the roster, never leave the paddock
TAGS_FIELD = [
    ("QUAL001", "11", "Anna Berger",  20.0, [45.0, 44.0, 43.5, 42.0, 43.0, 44.0]),
    ("QUAL002", "22", "Ben Fischer",  25.0, [46.0, 38.5, 44.0, 43.0, 44.0]),
    # 82.0 and 84.0 are two laps' worth each: one missed read apiece.
    ("QUAL003", "33", "Carla Hoff",   18.0, [41.0, 82.0, 41.5, 84.0, 42.0]),
    ("QUAL004", "44", "David Kern",   22.0, [42.0, 44.5, 45.0]),
    ("QUAL005", "55", "Elif Yilmaz",  21.0, [40.0, 39.2, 40.5, 39.8, 41.0]),
    ("QUAL006", "66", "Frank Weber",  23.0, [43.0, 41.0, 42.0, 42.0, 43.0]),
    ("QUAL007", "77", "Greta Lang",   24.0, []),
    ("QUAL008", "88", "Hugo Reiter",  26.0, [44.0, 43.5, 45.0, 44.0, 44.5]),
]

# Extra reads sent 4s after the rider's own crossing at these lap numbers.
DOUBLE_READS = {"QUAL005": [2, 4]}


def schedule(late, scale, tags=False):
    """Every crossing as (offset_seconds, tag, lap_number, duplicate), in time order."""
    events = []
    field = TAGS_FIELD if tags else FIELD

    for tag, _number, _name, outlap, laps in field:
        times = list(laps)
        if late and tag == "QUAL006":
            times = times + FRANK_LATE

        offset = outlap
        events.append((offset, tag, 1, False))
        for i, lap in enumerate(times):
            offset += lap
            lap_number = i + 2
            events.append((offset, tag, lap_number, False))

            # A duplicate is a second read of the same pass: too soon to be a
            # lap, so the app should reject it and count it here.
            if tags and lap_number in DOUBLE_READS.get(tag, []):
                events.append((offset + 4.0, tag, lap_number, True))

    events.sort(key=lambda e: e[0])
    return [(o * scale, t, n, d) for o, t, n, d in events]


def send(sock, tag, count, stamp):
    msg = (f"DA{tag} {stamp.strftime('%H:%M:%S.%f')} 10 "
           f"{count:05d} C7 date={stamp.strftime('%Y%m%d')}\r")
    sock.send(msg.encode("ascii"))


def handshake(sock):
    sock.send(b"N0001QualifyingReader\r")
    print(f"handshake: {sock.recv(1024).decode('ascii', 'replace').strip()!r}", flush=True)
    now = datetime.now()
    sock.send(f"GT{now.strftime('%H%M%S%f')[:-3]} date={now.strftime('%Y%m%d')}\r".encode())
    print(f"handshake: {sock.recv(1024).decode('ascii', 'replace').strip()!r}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--late", action="store_true",
                        help="Frank Weber stays out past the flag")
    parser.add_argument("--tags", action="store_true",
                        help="drive the transponder check instead: missed reads, "
                             "a tag that goes quiet, duplicate reads")
    parser.add_argument("--scale", type=float, default=1.0,
                        help="shrink every lap time by this factor to test faster "
                             "(0.5 gives ~20s laps; below 0.35 laps get rejected "
                             "as short reads)")
    args = parser.parse_args()

    if args.scale < 0.35:
        print(f"refusing to run at scale {args.scale}: laps would fall under the "
              f"10s minimum lap time and be rejected as short reads.")
        return 2

    field = TAGS_FIELD if args.tags else FIELD
    names = {tag: (number, name) for tag, number, name, _o, _l in field}
    events = schedule(args.late, args.scale, args.tags)

    sock = socket.socket()
    sock.settimeout(15)
    sock.connect((HOST, PORT))
    handshake(sock)
    time.sleep(1)

    base = datetime.now()
    counts = {}
    print(f"\nsession starts now; last crossing at {events[-1][0]:.0f}s", flush=True)
    if args.late:
        print("Frank Weber (#66) will stay out past the flag.", flush=True)
    else:
        print("everyone is in by about "
              f"{max(o for o, _t, _n, _d in events):.0f}s - the clock should run out "
              "with an empty track.", flush=True)
    print(flush=True)

    for offset, tag, lap, duplicate in events:
        due = base + timedelta(seconds=offset)
        wait = (due - datetime.now()).total_seconds()
        if wait > 0:
            time.sleep(wait)

        # A duplicate re-sends the lap count the rider already has, which is what
        # a tag being read twice in one pass actually looks like on the wire.
        if not duplicate:
            counts[tag] = counts.get(tag, 0) + 1

        # The exact intended moment goes on the wire, not datetime.now(), so the
        # lap times are exact rather than however long sleep() actually took.
        send(sock, tag, counts.get(tag, 1), due)

        number, name = names[tag]
        note = " (out-lap)" if lap == 1 else (" DUPLICATE" if duplicate else "")
        print(f"  {offset:6.1f}s  #{number:<3} {name:<14} lap {lap}{note}", flush=True)

    print("\nall crossings sent - leaving the connection open so the reader "
          "stays green.", flush=True)
    print("Watch the clock run out, then print the gate pick order.", flush=True)

    try:
        time.sleep(900)
    except KeyboardInterrupt:
        pass
    sock.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
