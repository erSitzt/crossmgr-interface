# CrossMgr RFID Interface

A Windows Forms application (`net9.0-windows`) that times motocross and enduro sessions from RFID transponder reads. It is run trackside on race day by volunteers, so the UI stays plain and in English, and code comments explain *why* something is the way it is - follow the style of the surrounding code.

## Build and test from WSL

There is no `dotnet` inside WSL. Use the Windows SDK, from a real Windows working directory, with the UNC project path:

```bash
cd /mnt/c/Users/Public
"/mnt/c/Program Files/dotnet/dotnet.exe" build '\\wsl.localhost\Ubuntu-Dennis\home\buehring\GIT\crossmgr-interface\CrossMgrInterface.csproj'
"/mnt/c/Program Files/dotnet/dotnet.exe" test  '\\wsl.localhost\Ubuntu-Dennis\home\buehring\GIT\crossmgr-interface\CrossMgrInterface.Tests\CrossMgrInterface.Tests.csproj'
```

The output is in German. CI (`.github/workflows/ci.yml`) builds and tests the same solution on `windows-latest`.

## The in-app help

- The words live in `HelpTopics.cs`: one topic per screen or task, written for a volunteer, using the names the application puts on its buttons. **When behaviour changes, change the topic that describes it** in the same change.
- `HelpBlock.Picture("name", "caption")` shows `help_images/name.png`, which is embedded in the application at build time.

## Help screenshots

The pictures in `help_images/` are generated, never taken by hand. `CrossMgrInterface.Tests/HelpScreenshots/` builds each screen with sample data - the race demo's fictional riders and the GSC circuit in `gsc.cmtrack` - and renders it off screen.

**Whenever a change alters how one of the screens below looks** - layout, labels, buttons, colours, or what its sample data shows - regenerate the pictures as part of that change:

```bash
cd /mnt/c/Users/Public
"/mnt/c/Program Files/dotnet/dotnet.exe" test '\\wsl.localhost\Ubuntu-Dennis\home\buehring\GIT\crossmgr-interface\CrossMgrInterface.Tests\CrossMgrInterface.Tests.csproj' --filter "FullyQualifiedName~HelpScreenshotTests" -e CROSSMGR_HELP_SCREENSHOTS=write
```

Then:

1. Look at every picture that changed with the Read tool. Check it shows what the caption and the topic say, with nothing cut off.
2. Build again - the pictures are embedded at build time - and run the full test suite.
3. Commit the pictures together with the change that caused them.

| Picture | Screen | Regenerate after changing | Topic |
|---|---|---|---|
| `race-day` | Race Day tab during a race, live timing on | `RaceDayView.cs` | race-day |
| `new-race-wizard` | New race wizard, Riders step | `NewRaceWizard.cs` | quick-start |
| `fix-laps` | Fix laps dialog with the suggested fix for a CHECK lap | `LapCorrectionDialog.cs`, `LapFixAdvisor.cs` | fixing |
| `unknown-transponder` | Identify transponder dialog | `AssignTagDialog.cs` | unknown |
| `track-map` | Track tab with riders on GSC | `TrackTabView.cs`, `TrackMapRenderer.cs`, `RiderDotLayout.cs`, `MapDrawResources.cs` | track |
| `circuit-editor` | Set up circuit, lining up a reference image | `TrackEditorDialog.cs`, `TrackMapRenderer.cs`, `ReferenceImageLayer.cs` | track |
| `past-sessions` | Past sessions dialog | `SessionManagerDialog.cs` | past-sessions |
| `reader-settings` | Reader connection dialog | `ReaderSettingsDialog.cs` | reader |
| `demo-picker` | Try a demo race dialog | `DemoPickerDialog.cs`, `DemoScenarios.cs` | demo |
| `publish-results` | Publish results dialog, before anything is sent | `PublishResultsDialog.cs`, `PublishPayloadBuilder.cs` | publish |
| `rider-list` | Rider list dialog, a number given twice and a missing class marked | `RiderListDialog.cs`, `RiderListCheck.cs` | rider-lists |
| `spectator-screen` | Spectator screen during a race, top 10 | `SpectatorWindow.cs`, `SpectatorBoard.cs`, `BoardText.cs` | race-day |

Also:

- Changing the sample data in `HelpScreenshotScenes.cs` or `gsc.cmtrack` means regenerating every picture that uses it.
- A normal test run renders every scene without writing anything, so CI catches a scene that no longer builds or draws blank.
- The map pictures use the tiles cached in `%LOCALAPPDATA%\CrossMgrInterface\tiles` and download any that are missing, so write them on a machine with internet. A normal run keeps the network off.
- **Adding a picture:** a scene in `HelpScreenshotScenes`, a `Picture(...)` block in the topic, and a row in the table above. `HelpScreenshotTests` fails when a picture has no scene; `HelpTopicsTests` fails when a picture is not embedded or not shown.
- Keep sample data fictional. Never put real rider names, or screenshots of other map services, into a picture.
