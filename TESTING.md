# Test fixtures and harnesses

Scripts and rider lists for exercising the application against simulated
transponder traffic. All of them connect to the reader port (53135 by default),
so start the reader connection first — **Reader → Start reader connection**, or
let it reconnect by itself if it was connected when the app last closed.

Run them with **Windows** Python. Under WSL they cannot reach the application:
WSL2's default networking means `localhost` is not the same machine.

## Harnesses

| Script | What it does |
|---|---|
| `test_simulation.py` | The original simulator. 40 riders, 10 minutes, with its own missed reads and DNFs. Staggers each rider's start by their index, so it is not suitable for large fields. |
| `scenario.py` | A small, deliberate race that produces every condition the correction dialog handles. 8 riders, ~2.5 minutes. |
| `stress_250.py` | Load harness: 250 riders, mass start, ~6 crossings/second for 6 minutes. Writes `stress_sent.jsonl` recording exactly what was sent, so recorded lap times can be checked for drift and dropped crossings. |

### What `scenario.py` sets up

Use it with `riders_small.csv` and a 3-minute race.

- **Carla Hoff (RIDER003)** — her lap-4 read is never sent, so the lap arrives
  roughly twice as long and should be flagged `CHECK`. Use it to test **Split
  this lap**.
- **Elif Yilmaz (RIDER005)** — a second read arrives four seconds after her lap-3
  crossing. Too soon to be a lap, so it should be rejected and appear as a grey
  "not counted" row in the correction dialog. Use it to test **Count this read**.
- **STRAY99** — a transponder that is not in the rider list, so it shows as
  `UNKNOWN`. Use it to test **Identify this transponder**, including merging onto
  a rider already in the race.
- Everyone else laps cleanly, for add / edit / delete / DNF.

## Rider lists

| File | Purpose |
|---|---|
| `sample_riders.csv` | The original 40-rider list. Matches the tags `test_simulation.py` sends. |
| `riders_small.csv` | 8 named riders. Matches `scenario.py`. |
| `riders_250.csv` | 250 riders across 5 classes. Matches `stress_250.py`. |
| `riders_flawed.csv` | **Deliberately broken.** Three usable rows and two with no transponder ID, to check that the import reports skipped rows rather than silently dropping them. |
| `riders_nocolumn.csv` | **Deliberately broken.** No `tagid` column at all, to check that the import says so and names the columns it did find. |

The last two are not mistakes. A partly-unreadable rider list used to import
silently and report a happy count, and the missing riders only surfaced
mid-race; these keep that fixed.

## Checking the results

The application writes a log to
`%LOCALAPPDATA%\CrossMgrInterface\logs\`, which records the tab layout, the
window geometry, every race event, every correction, and a render-cost summary
every 30 seconds. It is usually faster to read than to watch the screen.

To confirm nothing was dropped, compare `stress_sent.jsonl` against the `Tag:`
lines in that log. Crossings sent after the flag are refused on purpose, so
expect a rider's recorded laps to stop at their final allowed lap.
