# NexaViewer

A private, offline image browser and file explorer for Windows. No telemetry, no accounts, no
network access of any kind.

Built against the contract in [CLAUDE.md](CLAUDE.md) and the specification in [docs/](docs).

## What works

| | |
|---|---|
| Explorer | Async listing, natural name order (`img2` before `img10`), sort by name, size, type or date, multi-select, rename, delete to the Recycle Bin |
| Hidden files | Follows your Windows folder options, read live — no separate setting here |
| Tabs | Up to 25, opened without a dialog like a browser, restored on start, only the active one is listed |
| Viewer | Sequential and random navigation, random history, EXIF orientation, fit-down without upscaling, full path in the title, status line with size, dimensions and EXIF |
| Archives | Browse ZIP and RAR as folders, view images inside them. Read-only |
| File operations | Copy, Move, Copy To, Move To, and the Windows clipboard. Progress, throughput, ETA, cancellation |
| Conflicts | Both sides shown side by side with previews. Replace, Rename, Skip, Cancel, and "apply to all" for that operation only |
| Favorites | Named groups of folders, archives and images. Broken targets can be repaired or removed |
| Statistics | Local view history in SQLite. Buffered, never uploaded |
| Language | Russian and English, switchable in View → Language |
| Intro Counter | `X(Y)/Z` beside the standard counter, with Reset, −10, −1 and Stop, and the reset count in colour |
| Recovery | An unclean shutdown is detected at the next start and its logs are kept |

Requires Windows 10 version 2004 (build 19041) or later, x64.

## Build

Needs the .NET 10 SDK. Nothing else — no Visual Studio, no workloads.

```powershell
dotnet build ViewerPrn.slnx -c Release
dotnet test  ViewerPrn.slnx -c Debug
```

The application lands in `artifacts/bin/ViewerPrn.App/release_win-x64/NexaViewer.exe`.

## A build that runs anywhere

```powershell
dotnet build src/ViewerPrn.App/ViewerPrn.App.csproj -c Release -p:SelfContained=true
```

About 219 MB and 529 files, carrying its own .NET runtime and Windows App SDK. Copy the folder
to a machine with neither installed and it runs.

**Use `build`, not `publish`.** `dotnet publish` produces an output that starts and then dies with
`XamlParseException`: it does not copy this application's compiled XAML (`*.xbf`) or its
`resources.pri`. That is true with and without `-o`, and with and without `UseArtifactsOutput`.
See DECISION-0031.

## Benchmarks

```powershell
dotnet run --project tools/NexaViewer.Bench -c Release -- <scratch-directory> [sample-images]
```

Results and the machine they came from are in [docs/PERFORMANCE.md](docs/PERFORMANCE.md).

## Application icon

Replace `src/ViewerPrn.App/Assets/AppIcon.ico` and rebuild. It becomes the executable, taskbar
and title-bar icon. `AppIcon.source.png` is the artwork it was generated from.

## Where your data lives

`%LOCALAPPDATA%\NexaViewer`

| | |
|---|---|
| `settings.json` | Theme, accent, language |
| `session.json` | Open tabs, written atomically with a `.bak` |
| `viewerprn.db` | Favorites and view statistics |
| `logs\` | Transient log, deleted on a clean exit; crash reports kept |
| `cache\archives\` | Entries extracted from archives, cleared on a clean exit |

Delete the folder to reset everything. Nothing outside it is touched.

## Outstanding

- **RAR is unverified.** The code path exists; no RAR fixture could be produced on the build
  machine. Solid RAR archives are the known weak spot (DECISION-0006).
- **One assumption in the Intro Counter.** The intro grows by five per band of 400 above 800,
  continuing the step the specification shows from 300 to 800. The user gave the band step but
  not the intro rule inside the new bands. Flagged in [docs/VIEWER.md](docs/VIEWER.md).
- **Nothing has been verified by eye.** Every behaviour here is covered by tests or by running
  the application and reading its log, but no screenshot was ever looked at.
