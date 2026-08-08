# PERFORMANCE

Measure actual builds; never invent performance claims.

Record hardware, OS, build configuration, runtime and dataset characteristics.

Benchmark startup, idle RAM/CPU, 1k/10k/100k folders, large ZIP/RAR, thumbnail generation, Viewer latency, Random Viewer, 25 tabs and SQLite growth.

Use bounded concurrency. Do not load complete collections into memory.

Document observed values, bottlenecks, optimizations and remaining limitations.

---

## How to reproduce

```
dotnet run --project tools/NexaViewer.Bench -c Release -- <scratch-directory> [sample-images]
```

Everything the benchmark cannot ask headlessly — startup, idle memory, restored tabs — comes
from running the application and reading its own log.

## Reference machine

| | |
|---|---|
| CPU | Intel Core i5-11400H, 6 cores / 12 threads, 2.70 GHz |
| RAM | 15.7 GB |
| OS | Windows 11 Pro 10.0.22631 (build 22631) |
| .NET SDK | 10.0.302 |
| Windows App SDK | 1.8.260710003, self-contained |
| Repository volume | E: — disk 0, ST1000LM049, **HDD** |
| System volume | C: — disk 1, MSI M450, SSD |

The repository sits on a spinning disk while the OS is on SSD. Every measurement below states
which volume it used; mixing the two would make the numbers meaningless.

---

## Phase 14 — full suite, 2026-08-08

Release build. Synthetic folders and archive on **E: (HDD)**; the twelve sample JPEGs
(36 KB to 1.4 MB) on **C: (SSD)**.

**Warm figures.** Each section runs one throwaway pass first. That is not cosmetic: without it
the first call in a section pays for loading and initialising a whole subsystem, and the cost
lands on whatever was measured first. It is why the corrections below exist.

### Enumeration and sorting (E:, HDD)

| Entries | Enumerate | Natural sort | Random shuffle |
|---:|---:|---:|---:|
| 1 000 | 8.7 ms | 9.8 ms | 0.6 ms |
| 10 000 | 9.0 ms | 27.5 ms | 1.7 ms |
| 100 000 | 93.5 ms | 286.2 ms | 3.3 ms |

Sorting still costs three times what reading the directory costs, because `StrCmpLogicalW` is a
P/Invoke per comparison. The shuffle, which compares nothing, is two orders of magnitude cheaper —
which is the clearest possible confirmation of where the time goes. Both run on a worker thread.

### Archives (E:, HDD, 2 000-entry ZIP)

| | |
|---|---:|
| List the archive root | 8.6 ms |
| List one folder inside it | 6.8 ms |
| Extract one entry | 18.0 ms |
| Extract the same entry again | 0.1 ms |

### Images (C:, SSD, 12 JPEGs)

| | |
|---|---:|
| Metadata | 1.7 ms per file |
| Thumbnails, first pass (4 concurrent) | 74.6 ms |
| Thumbnails, from memory | 1.1 ms |
| Thumbnail cache | 66 KB for 12, about 5.5 KB each |

### Database (E:, HDD)

| | |
|---|---:|
| Migrate a new database (cold; loads native SQLite) | 261.8 ms |
| Record 100 000 image views | 1 565 ms |
| Query aggregates over 100 000 rows | 20.0 ms |
| Database size after 100 000 views | 18.9 MB |

About 190 bytes per recorded view. A user who views a hundred images a day reaches roughly
7 MB a year, so growth needs no pruning strategy.

### Application (E:, HDD)

| | |
|---|---:|
| Startup, empty session, warm | 378–766 ms |
| Startup, 25 tabs restored | 2 984 ms |
| Idle working set, no tabs | 135 MB |
| Working set, 25 tabs restored (1 listed) | 166.7 MB |
| Working set, folder of 12 images with thumbnails | 193.9 MB |

25 tabs cost about 32 MB over an empty shell — roughly 1.3 MB per tab, because only the active
one is listed. Startup with 25 tabs is dominated by creating 25 controls, not by any listing.

---

## Corrections to earlier phases

Two numbers recorded in earlier phases were wrong, and both were wrong the same way: they
included a one-time initialisation cost that had nowhere else to land.

| Recorded earlier | Actually | Why |
|---|---|---|
| Metadata **18.8 ms per file** (Phase 4) | **1.7 ms per file** | The first `StorageFile` call initialises the WinRT storage stack. Divided across twelve files it looked like a per-file cost. Measured cold and unwarmed it reached 306 ms per file. |
| Listing an archive **306 ms** (Phase 6) | **8.6 ms** | The first call loads and initialises the archive decoder. |

The conclusion drawn from the bad metadata figure — that the Explorer list must not read
metadata per row — still holds, but for a different reason: 1.7 ms is cheap for one image in the
Viewer and still nearly three minutes for a hundred thousand rows.

The cold costs are real, they are simply startup costs rather than per-item costs, and they are
paid once per subsystem per run.

---

## Cache sizes, now measured rather than guessed

DECISION-0010 deferred these to this phase.

- **Thumbnails: 64 MB.** At the measured ~5.5 KB each that holds roughly twelve thousand — more
  than any realistic scroll session, and 64 MB is under half the idle footprint of the shell.
  Kept as it stands, now for a reason rather than as a placeholder.
- **Viewer decode cap.** Bounded by the window size, falling back to 3840 px. A 50-megapixel
  photo would otherwise be about 200 MB of pixels; the cap makes it about 33 MB.

## Still not measured

- **Cold-cache enumeration.** Dropping the Windows file cache needs privileges this run did not have.
- **Viewer navigation latency and Random Viewer.** Both need synthetic input; the underlying
  decode and metadata costs are measured above, but the end-to-end keystroke-to-pixel time is not.
- **RAR.** No RAR writer on this machine, so no fixture exists (DECISION-0006).
- **Network paths.**

## Observation carried forward

135 MB idle for an empty shell is the WinUI 3 baseline before a single image is decoded, and
priority 4 is low resource usage. It is the floor, not the product's own consumption: the twelve
image thumbnails add 59 MB and 25 tabs add 32 MB. Nothing here is optimised, because nothing
here is yet the bottleneck.
