# CrossMgr RFID Interface

Times motocross races from transponder reads. A Windows Forms application that
listens on the CrossMgr reader port, turns the raw tag reads into a live
leaderboard, and produces the sheets a meeting actually needs: results, gate
pick order, and a transponder check.

It is built to be run by a volunteer in a timing tent, on a laptop that may have
no internet, by someone who did not install it and cannot debug it. That
constraint shapes most of what follows.

## What it does

- **Three session types.** A **race** is scored on laps and finishes on a laps
  target. **Free practice** and **timed qualifying** are scored on the clock:
  when it runs out the flag comes out, every rider finishes the lap they are on,
  and that lap counts. Only qualifying produces a gate pick order.
- **Live race day view.** Positions, gaps, lap counts and the race clock, in a
  layout meant to be read across a tent rather than leant over.
- **Track map.** The circuit drawn on an OpenStreetMap basemap with rider dots
  moving round it in real time, so "where is everyone?" has an answer. Tiles are
  cached to disk, so the map still works on a field with no signal.
- **Transponder check.** Finds the tags that are *not* being read, before the
  session that matters.
- **Corrections.** A missed read, a double read, an unknown transponder, a rider
  who should not be counted — each is fixable during the session, with undo.
- **Rider lists and classes.** Import riders from CSV; filter and report by
  class.
- **Crash recovery.** The race is written to a local database as it happens. If
  the laptop restarts mid-session, the race, the rider list and the reader
  connection all come back.

## Getting it

Download the `CrossMgrInterface-vX.Y.Z-win-x64.zip` from the
[latest release](../../releases/latest), extract it anywhere, and run
`CrossMgrInterface.exe`. The build is self-contained — no .NET install, no
administrator rights.

## Running a session

1. **Race → New race…** (`Ctrl+N`) walks through session type, length, and when
   the clock starts. To change the session type later, you do not need the
   wizard again.
2. **Race → Import riders…** (`Ctrl+I`) loads the rider list. Without it,
   riders show as raw transponder IDs, which still times the race correctly but
   makes every sheet unreadable.
3. **Reader → Start reader connection** opens the port the transponder reader
   connects to (53135 by default). If the reader was connected when the
   application last closed, it reconnects by itself.
4. Run the session. The clock starts on the first crossing, or on
   **Race → Start race** (`F5`) if you chose to start it yourself.
5. **Race → Results…** (`Ctrl+P`) prints or exports to Excel; qualifying also
   offers **Gate pick order…**.

Press `F1` for the in-application quick start.

## The tabs

By default the application shows the calm view — the tabs a volunteer needs:

| Tab | What it is for |
|---|---|
| **Race Day** | The live view: positions, gaps, clock, flag state. |
| **Riders** | The grid. Where corrections are made (right-click a rider). |
| **Qualifying** | Best laps and the gate pick order they produce. Qualifying sessions only. |
| **Transponders** | Which tags are being read and which are missing. Timed sessions only. |
| **Track** | The circuit map with live rider positions. |

**View → Show advanced tabs** (`Ctrl+Shift+A`) adds the diagnostics: the reader
feed, raw tag events, race statistics, the lap chart, lap progression, and the
full race settings. The choice is remembered.

`Ctrl+1`, `Ctrl+2` and `Ctrl+3` jump to Race Day, Riders and the track map.

## Fixing things during a session

Transponder timing goes wrong in a small number of predictable ways, and all of
them are fixable from the riders grid — right-click a rider, or **Riders → Fix
laps…** (`F2`), which opens the rider most in need of it.

- **A missed read** shows up as one lap of roughly double the usual time,
  flagged `CHECK`. The lap can be split in two.
- **A read too soon after the last one** is rejected as not-a-lap and shown as a
  grey row. If it was real, it can be counted.
- **An unknown transponder** can be identified — named, or merged onto a rider
  already in the race.
- **A rider who should not be scored** can be stopped, and started again later.
- Laps can be added, edited, deleted, and a rider marked DNF.

**Riders → Undo last change** (`Ctrl+Z`) reverses the last correction.

Missed-read detection is configurable (**Race Settings → Missed read
detection…**), as is the threshold below which a lap is too fast to be real.

## Rider lists

CSV, with a header row. Column names are matched loosely, so most exports from a
club's entry system work as-is:

| Field | Accepted column names |
|---|---|
| Transponder | `tagid`, `tag`, `id` |
| Name | `name`, `fullname`, `rider`, or `firstname`/`first` + `lastname`/`last`/`surname` |
| Number | `number`, `ridernumber`, `bib` |
| Class | `category`, `class`, `division` |

A row with no transponder ID is skipped and reported. A file with no recognisable
transponder column is refused, and the import names the columns it did find
instead — a partly-unreadable rider list used to import silently and the missing
riders only surfaced mid-race.

## Where it keeps things

Everything lives under `%LOCALAPPDATA%\CrossMgrInterface\`:

| Path | What |
|---|---|
| `races.db` | The race database. |
| `settings.json` | Reader port, advanced mode, last rider list, and so on. |
| `tracks.json` | Surveyed circuits. A plain file, so a club can email one to the next club using the same venue. |
| `tiles\` | Cached map tiles, kept indefinitely. |
| `logs\` | Rolling text logs: every race event, every correction, window layout, and a render-cost summary every 30 seconds. |

**Help → Open log folder** opens the last of these. The log is usually faster to
read than watching the screen.

## Reader protocol

The application implements the CrossMgr side of the timing protocol used by
Impinj and similar readers. It listens; the reader connects.

| Message | Meaning |
|---|---|
| `GT` | Time sync request. Answered with `GT{HHmmssfff} date={YYYYMMDD}`. |
| `S0000` | Setup, sent before tag reads begin. |
| `DA{tag} {time} 10 {count} C7 date={date}` | A tag read. |

Example: `DA10000001 17:50:37.786398 10  00006      C7 date=20250709`

A transponder prefix filter is available for venues where other tags are in
range.

## Building from source

Requires the .NET 9 SDK and Windows — the project targets `net9.0-windows` and
uses Windows Forms, so it does not build or test on Linux or macOS.

```bash
dotnet build crossmgr-interface.sln -c Release
dotnet test crossmgr-interface.sln -c Release
dotnet run --project CrossMgrInterface.csproj
```

See [TESTING.md](TESTING.md) for the simulation harnesses and the deliberately
broken fixture files, which reproduce every condition the correction dialog
handles without needing a track and 40 riders.

## Continuous integration

Two GitHub Actions workflows, both on Windows runners:

- **Build and test** (`.github/workflows/ci.yml`) runs on every push and pull
  request to `main`, and uploads the test results.
- **Release build** (`.github/workflows/release.yml`) runs when a release is
  published. It builds that tag, runs the tests, and attaches the self-contained
  `win-x64` zip to the release. It can also be run by hand against an existing
  tag from the Actions tab.

## License

Provided as-is for CrossMgr timing system integration.
