# PERFORMANCE

Measure actual builds; never invent performance claims.

Record hardware, OS, build configuration, runtime and dataset characteristics.

Benchmark startup, idle RAM/CPU, 1k/10k/100k folders, large ZIP/RAR, thumbnail generation, Viewer latency, Random Viewer, 25 tabs and SQLite growth.

Use bounded concurrency. Do not load complete collections into memory.

Document observed values, bottlenecks, optimizations and remaining limitations.

---

## Reference machine

| | |
|---|---|
| CPU | Intel Core i5-11400H, 6 cores / 12 threads, 2.70 GHz |
| RAM | 15.7 GB |
| OS | Windows 11 Pro 10.0.22631 (build 22631) |
| .NET SDK | 10.0.302 |
| Windows App SDK | 1.8.260710003 |
| Windows SDK | 10.0.26100 |
| Repository volume | E: — disk 0, ST1000LM049, **HDD** |
| System volume | C: — disk 1, MSI M450, SSD |

The repository sits on a spinning disk while the OS is on SSD. Enumeration and thumbnail
benchmarks must state which volume the dataset was on — the two differ by orders of magnitude
and mixing them would make every folder-scan number meaningless.

## Phase 0 — 2026-08-07

Only the toolchain exists at this point. These are build-system numbers, not product numbers.

| Measurement | Value | Method |
|---|---|---|
| Clean solution build, Release | 10.3 s | `bin`/`obj` deleted, `dotnet build ViewerPrn.slnx -c Release` |
| Full test run, Release | 4.0 s wall, 0.32 s in tests | `dotnet test --no-build` |
| Tests | 63 passed, 0 failed, 0 skipped | 62 domain + 1 infrastructure |

Not yet measured at this point, and deliberately not estimated: application startup, idle RAM and CPU,
folder enumeration at 1k/10k/100k, archive scanning, thumbnail throughput, Viewer navigation
latency, 25-tab memory and SQLite growth. There is no UI, no decoder, no database and no
enumeration yet. Startup measurement begins in Phase 1; the rest follow their phases and are
appended here as sections, never replaced.

## Phase 1 — 2026-08-07

Shell only: menu bar, empty tab strip, status bar. No enumeration, no decoding, no database.
Release build, self-contained Windows App SDK, run from the E: (HDD) volume.

| Measurement | Value | Method |
|---|---|---|
| Startup, cold (first run after build) | 957 ms | process start to shell ready, logged by the app itself |
| Startup, warm (runs 2 and 3) | 428 ms, 378 ms | same |
| Idle working set, no tabs open | 135.4 / 135.8 / 135.8 MB | `Process.WorkingSet64` ~2 s after the window appears |
| Clean solution build, Release | 10.3 s | unchanged from Phase 0 |
| Tests | 89 passed, 0 failed | 76 domain + 13 infrastructure |

Startup is measured from process start, so it includes runtime and Windows App SDK
initialisation, not only application code.

## Phase 2 — 2026-08-07

Directory listing and sorting, measured directly against `WindowsFileSystemService` and
`EntrySorter` through a throwaway harness — no UI in the loop. Synthetic folders of
`imgN.jpg` files on the E: (HDD) volume, Release build.

**Warm cache.** Each folder was listed once and discarded before the measured run, so these are
best-case numbers. Cold-cache listing on this spinning disk will be worse; measuring it needs a
cache drop that could not be done here.

| Entries | Enumerate | Natural sort |
|---:|---:|---:|
| 1 000 | 2 ms | 14 ms |
| 10 000 | 6 ms | 34 ms |
| 100 000 | 88 ms | 373 ms |

Enumeration is cheap; the natural-order sort dominates, because `StrCmpLogicalW` is a P/Invoke
per comparison. This changed the code: sorting and row construction were running on the UI
thread, and 373 ms of it at 100 000 entries is a visible freeze. Both now run on a worker
thread. That is the whole reason for measuring before optimising — the bottleneck was not where
the enumeration work is.

Still not measured: cold-cache listing, network paths, and memory for a 100 000-entry listing
held as `FileSystemEntry` records (roughly 100 bytes each by inspection, not by measurement).

## Phase 3 — 2026-08-08

Session restore, Release build, warm start. Session file held three tabs, one of them pointing
at a path that no longer exists.

| Measurement | Value |
|---|---|
| Startup with 3 tabs restored | 610 ms |
| Listing the active tab (9 entries, E: HDD) | 49 ms |
| Working set with 3 tabs restored, 1 listed | 150.5 MB |

Only the active tab is listed at startup; the other two stay empty until they are first shown.
Confirmed in the log — one `Listed ... entries` line for three restored tabs.

## Phase 4 — 2026-08-08

Metadata and thumbnails, measured against the real services through a throwaway harness.
Twelve JPEGs of 36 KB to 1.4 MB copied from `C:\Windows\Web`, on the C: (SSD) volume.

| Measurement | Value |
|---|---|
| Metadata read, sequential | 219 ms for 12 files — **18.8 ms each** |
| Thumbnails, first pass (4 concurrent) | 90 ms for 12 files |
| Thumbnails, second pass from cache | 1.4 ms for 12 files |
| Thumbnail cache footprint | 66 KB for 12 — about 5.5 KB each |

Thumbnails are cheap because they come from the shell cache the operating system has already
filled. At ~5.5 KB each the placeholder 64 MB budget holds roughly twelve thousand of them.

Metadata at 18.8 ms per file is the expensive one, and most of it is `StorageFile` rather than
the decode. That is acceptable where it is used — the Viewer reads one image at a time — and it
is the reason the Explorer list does **not** read metadata per row. If a future view needs
dimensions for every row, this number says do not get them this way.

## Phase 6 — 2026-08-08

An 8.1 MB ZIP of 12 JPEGs plus a nested folder, opened directly as a tab.

| Measurement | Value |
|---|---|
| Listing the archive root (13 entries) | 306 ms |
| Working set, archive tab with thumbnails | 193.9 MB |
| Entries extracted to cache | 12 — the images the thumbnails needed |
| Cache after a clean shutdown | removed |

Listing an archive costs an order of magnitude more than listing a folder (306 ms against 32 ms
for the same images on disk) because the whole central directory is read. Acceptable for a
container that is opened deliberately; it would not be acceptable per keystroke.

**Observation to carry forward.** 135 MB idle for an empty shell is a large share of any
sensible budget for this product, and priority 4 is low resource usage. Nothing has been
optimised yet and nothing should be, on one machine and one build configuration — but the
number is the WinUI 3 baseline before a single image is decoded, so the image and thumbnail
caches in Phases 4-6 must be sized against it rather than against zero. Revisit in Phase 14.

