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
| `qualifying.py` | A timed qualifying session with a known-correct answer sheet. 10 riders, 6 minutes. The pole-setter has the *fewest* laps but the quickest one, and two riders share an identical best lap, so a gate pick order sorted on the wrong column is obvious. |
| `stress_250.py` | Load harness: 250 riders, mass start, ~6 crossings/second for 6 minutes. Writes `stress_sent.jsonl` recording exactly what was sent, so recorded lap times can be checked for drift and dropped crossings. |
| `enduro.py` | A staggered start. By default MX1, MX2 and Youth from `riders_250.csv`, four riders each, a minute apart, 40-56 s laps, a 3-minute race, with one Youth rider read before Youth has started who must be ignored. `--full` is the real race: all 250 riders in five classes a minute apart, 15-20 minute laps, 2 hours, with riders retiring, missed reads, a duplicate read and an early read; it takes about 2h45. Set the app up as a race in waves in that class order, press START RACE, then run it. Writes `enduro_sent.jsonl`, including the crossings deliberately not sent. |
| `teams.py` | A team race. Two teams on their own transponders, one team sharing a transponder, and two solo riders from `riders_teams.csv`, 4 minutes, with a handover, a rider waiting near the loop, and two riders of one team out at once. `--parked` has the waiting rider read every 3 s; `--stray` puts a rider on a spare transponder to identify. Writes `teams_sent.jsonl`. |
| `teams_40.py` | A full-size team race: 40 riders in 20 teams from `riders_teams_40.csv`, three classes, 5 minutes. Stints of two to four laps with a changeover on every handover, three teams on a shared transponder, long real-looking EPCs, and one each of a waiting rider, two riders on track and a missed read. For checking how the live views and the results sheets cope with a real field. Writes `teams_40_sent.jsonl`. |

Both race harnesses keep sending for two laps **after** the race duration. The
application's clock starts on the first crossing and a race only notices it has
run out on the crossing after that; the leader then rides the lap in progress
plus the extra laps, and everyone else finishes theirs. A harness that stopped
on the clock left the race unfinished forever, with every total time a little
short of the duration. Set the application's race length to match the harness.

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

### What `qualifying.py` sets up

Use it with `riders_qualifying.csv`, session type **Timed qualifying**, and a
6-minute session.

- **Ben Fischer (#22)** takes pole on the fewest laps, because his single
  quickest lap is the quickest of the session. A sheet sorting on laps completed
  puts him nowhere near first.
- **David Kern (#44)** and **Anna Berger (#11)** both set a best lap of exactly
  42.000. David set his first, so he ranks ahead.

### What `teams.py` sets up

Use it with `riders_teams.csv`, a **Race** with **Team event** ticked, a
4-minute length and no extra laps. The script's docstring has the full answer
sheet.

- **MSC Adler** - Anna and Ben on their own transponders. Anna hands over to
  Ben; Ben's read 3 s after Anna's crossing is someone waiting near the loop
  and must show as "not counted", not as a lap. The Team members section shows
  4 laps for Anna and 3 for Ben, the handover lap left out of Ben's times.
- **RC Falke** - both riders on one transponder, the way teams were entered
  before. Scored as one team, one "shared transponder" line, never flagged.
  One row says `rc falke`, so the grouping has to ignore case.
- **Team West** - Frank goes out while Elif is still riding. Laps 4 and 6 are
  flagged `TWO OUT`; deleting them clears both warnings. The two riders are
  in different classes, which the wizard warns about.
- **Greta Lang** and **Hugo Reiter** race solo: Greta's team name is on her
  row only, Hugo has none.

`--parked` keeps Ben near the loop for 18 seconds: none of those reads may
count, and the "waiting too close to the loop" warning appears. `--stray` puts
Ben on `SPARE01`, which is not on the list; identify it as his transponder
(**This transponder belongs to a team**) before the flag.

## Rider lists

| File | Purpose |
|---|---|
| `sample_riders.csv` | The original 40-rider list. Matches the tags `test_simulation.py` sends. |
| `riders_small.csv` | 8 named riders. Matches `scenario.py`. |
| `riders_250.csv` | 250 riders across 5 classes. Matches `stress_250.py`. |
| `riders_qualifying.csv` | 10 riders across 2 classes. Matches `qualifying.py`. |
| `riders_teams.csv` | 8 rows making 3 teams and 2 solo riders, for a **team event**. Matches `teams.py`. |
| `riders_teams_40.csv` | 40 riders in 20 teams across three classes, with a long club name and 24-character EPCs. Matches `teams_40.py`. |
| `riders_teams_conflict.csv` | **Deliberately broken.** One transponder on a team rider's row and on a solo rider's row, which the wizard must refuse for a team event. |
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
