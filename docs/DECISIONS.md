# ARCHITECTURAL DECISIONS

Record material decisions in this format:

## DECISION-XXXX — Title
Date:
Status:
Context:
Decision:
Alternatives:
Reason:
Consequences:
Tests/verification:

---

## DECISION-0001 — Cycle length formula is ceil(N/100)*10
Date: 2026-08-07
Status: Accepted
Context: `docs/VIEWER.md` stated `cycle = ceil(N/10)*10` for totals above 500. That formula
contradicts every worked example in the same document: 505 -> 510, 645 -> 650, 951 -> 960,
while the document requires 60, 70 and 100.
Decision: The cycle length is N/10 rounded up to the nearest ten, i.e. `ceil(N/100)*10`.
`docs/VIEWER.md` has been corrected.
Alternatives: Treat the examples as wrong and keep the literal formula — rejected, the
examples are repeated in `docs/TESTING.md` as mandatory acceptance criteria.
Reason: Confirmed by the user; reproduces 469 -> 50, 505 -> 60, 645 -> 70, 951 -> 100, 1199 -> 120.
Consequences: The literal formula text was a transcription error, not a requirement change.
Tests/verification: `CycleTableTests.Resolve_ReturnsSpecifiedIntroAndCycle`.

## DECISION-0002 — The cycle position does not wrap
Date: 2026-08-07
Status: Accepted (v1)
Context: No document specified what the helper counter shows once the cycle position passes
the cycle length. For 951 images (intro 20, cycle 100) this affects physical images 121–951,
i.e. most of the gallery.
Decision: The position keeps incrementing past the cycle length. Physical 121 shows
`101(20)/100`; the last image shows `931(20)/100`. No wrap, no second cycle, no freeze.
Alternatives: Restart at 1 for each new cycle; freeze at the cycle length.
Reason: User decision for the first version.
Consequences: The warning becomes permanent once the threshold is crossed, and Reset Cycle
(available only at positions 1–10) becomes permanently unavailable after position 10 of the
first cycle. Flagged to the user; revisit if v2 should cycle.
Tests/verification: `IntroCounterTests.CyclePositionGrowsPastCycleLength`.

## DECISION-0003 — UI stack: WinUI 3 on .NET 10, unpackaged
Date: 2026-08-07
Status: Accepted
Context: `CLAUDE.md` prefers C#, .NET 10 LTS, WinUI 3 / Windows App SDK. The machine had no
.NET 10 SDK and no Visual Studio.
Decision: .NET 10 SDK 10.0.302 (installed during Phase 0), Windows App SDK 1.8.260710003,
`net10.0-windows10.0.19041.0`, x64, `WindowsPackageType=None`. Package versions are pinned.
Solution file is `.slnx` (the SDK 10 default).
Alternatives: WPF (mature tooling, weaker modern controls); Avalonia (cross-platform, not needed
for a Windows-only product).
Reason: Matches the contract. Building without Visual Studio works via `dotnet build`; the
Windows App SDK ships as NuGet packages and needs no workload.
Consequences: No XAML designer. XAML is authored by hand and validated by the build.
Tests/verification: `dotnet build ViewerPrn.slnx` succeeds; `ViewerPrn.App.dll` is produced.

## DECISION-0004 — Unpackaged x64 executable carrying its own Windows App SDK
Date: 2026-08-07 (revised during Phase 1)
Status: Accepted
Context: The product is private, offline, single-user. No Store distribution. The first Phase 1
build was framework-dependent on the machine-installed Windows App Runtime and refused to
start with the dialog `ViewerPrn.App.exe - This application could not be started`: CoreCLR
loaded, then the Windows App Runtime bootstrapper failed to find a matching runtime among the
nine 1.8.x versions installed on this machine.
Decision: Ship an unpackaged WinExe, x64, with `WindowsAppSDKSelfContained=true`. The .NET
runtime stays framework-dependent for now.
Alternatives: MSIX packaging; pinning the SDK package to whichever runtime happens to be
installed — rejected, it makes the build depend on machine state.
Reason: A private offline tool has no reason to depend on a separately serviced runtime whose
version it does not control. Reliability is priority 1.
Consequences: Output grows from 49 to 155 files. `RuntimeIdentifier` and `Platform` must be set
explicitly in the app project — the self-contained targets reject `AnyCPU`, and a
`RuntimeIdentifier` condition in `Directory.Build.props` cannot work because that file is
imported before the project sets `TargetFramework`.
Tests/verification: The app starts and its window titled `ViewerPrn` appears; startup measured
in `docs/PERFORMANCE.md`. Full clean-machine verification stays in Phase 15, including whether
the .NET runtime should be bundled too.

## DECISION-0013 — Closing a tab activates the one to the right
Date: 2026-08-07
Status: Accepted
Context: `docs/REQUIREMENTS.md:10` sets the 25-tab limit but does not say which tab becomes
active after the active one is closed.
Decision: Focus moves to the tab on the right; when the closed tab was last, to the one on the
left. Closing the only tab leaves no active tab.
Alternatives: Always focus the left neighbour; return to a most-recently-used tab.
Reason: This is what File Explorer and every browser do, and priority 6 is familiar
Windows-like UX. Nothing is lost either way, so it is not worth blocking on.
Consequences: If a most-recently-used order is wanted later, it replaces this rule.
Tests/verification: `TabSetTests` — right neighbour, left neighbour and last-tab cases.

## DECISION-0016 — Delete sends entries to the Recycle Bin
Date: 2026-08-07
Status: Accepted
Context: Phase 2 needs Delete. Priority 1 is protecting the user's files.
Decision: Delete moves entries to the Recycle Bin, after a confirmation dialog, using
`Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile/DeleteDirectory` with
`RecycleOption.SendToRecycleBin`.
Alternatives: `File.Delete`/`Directory.Delete` — permanent, unrecoverable; a hand-written
`SHFileOperation` or `IFileOperation` P/Invoke — about forty lines of interop for the same result.
Reason: `Microsoft.VisualBasic` ships in the shared framework, so this is two lines and no new
dependency. A deletion that cannot be undone fails the first priority in `CLAUDE.md`.
Consequences: Deleting to the Recycle Bin is slower than unlinking, and fails on volumes that
have no Recycle Bin (some network shares). That failure surfaces as an error rather than
silently falling back to permanent deletion.
Tests/verification: `WindowsFileSystemServiceTests` — files and folders disappear from the
source folder, and deleting something already gone throws.

## DECISION-0017 — Name ordering uses the shell's own comparison
Date: 2026-08-07
Status: Accepted
Context: `docs/REQUIREMENTS.md:4` asks for "normal name order". In Windows that means `img2`
sorts before `img10`, which no ordinal or culture comparer does.
Decision: `NaturalStringComparer` P/Invokes `StrCmpLogicalW` from shlwapi.dll — the function
File Explorer itself uses.
Alternatives: A hand-written digit-aware comparer — an imitation that would drift from Explorer
on edge cases; `StringComparer.OrdinalIgnoreCase` — visibly wrong on numbered photo files, which
is the main use case for this product.
Reason: Priority 6 is familiar Windows-like UX, and this is literally the same ordering.
Consequences: One P/Invoke per comparison, which measurably dominates sorting large folders
(see `docs/PERFORMANCE.md`, Phase 2). Sorting therefore runs on a worker thread.
`DllImport` rather than `LibraryImport`, because the source generator would require
`AllowUnsafeBlocks` across the whole project for one call.
Tests/verification: `NaturalStringComparerTests`, including a test asserting that the ordinal
comparer gets the same input wrong.

## DECISION-0018 — Hidden and system entries follow the user's Explorer settings
Date: 2026-08-07 (revised the same day)
Status: Accepted
Context: Nothing in the documents said whether hidden and system files should appear. The user
answered: follow Windows, which is how ACDSee behaves.
Decision: Read `HKCU\...\Explorer\Advanced` per listing — `Hidden` ("Show hidden files, folders,
and drives") and `ShowSuperHidden` ("Hide protected operating system files") — and apply
Explorer's own rule. An entry marked Hidden follows `Hidden`. An entry marked Hidden *and*
System is a protected operating system file and follows `ShowSuperHidden` instead. An entry
marked System alone is not hidden and is always listed.
Alternatives: A private setting inside this application — a second place to configure the same
thing, which then disagrees with Explorer.
Reason: Priority 6 is familiar Windows-like UX. There is no reason for this product to hold its
own opinion about a setting Windows already owns.
Consequences: The first version of this filter skipped anything marked Hidden *or* System, which
wrongly hid System-only entries that Explorer shows. Reading the registry per listing means an
Explorer settings change takes effect without restarting. No in-app toggle, by design.
Tests/verification: `EntryVisibilityTests` covers the full attribute/setting matrix as a pure
function; `WindowsFileSystemServiceTests.HiddenEntriesFollowTheUsersExplorerSetting` asserts the
listing agrees with whatever the machine is actually configured to do.

## DECISION-0015 — Localisation via .resx and ResourceManager
Date: 2026-08-07
Status: Accepted
Context: Russian is the primary UI language, English is the alternative, and the language must
be selectable.
Decision: `Strings.resx` (Russian, the neutral set) and `Strings.en.resx` (English satellite),
read through `ResourceManager` with plain string keys. The language is applied at startup by
setting `CurrentUICulture`; changing it takes effect at the next start.
Alternatives: WinUI `.resw` with `x:Uid` and `ApplicationLanguages.PrimaryLanguageOverride` —
the platform-native route, but it pulls in MRT resource wiring for roughly thirty strings.
Reason: `.resx` is in the BCL, needs no project configuration in an SDK-style project, and the
satellite assembly is produced automatically.
Consequences: No live language switching, and keys are not compile-checked. A typo shows the
key itself in the UI rather than throwing.
Tests/verification: `ResourceParityTests` compares the key sets of both files, so a translation
cannot silently go missing.

## DECISION-0014 — Unreadable settings fall back to defaults
Date: 2026-08-07
Status: Accepted
Context: A corrupt or unreadable `settings.json` must not stop the application from starting.
Decision: Log a warning, use `AppSettings.Default`, and leave the bad file in place. The next
successful save replaces it and keeps the previous content as `settings.json.bak`.
Alternatives: Refuse to start; delete the bad file.
Reason: Settings are convenience state, not user data. Deleting the file would destroy the only
evidence of what went wrong.
Consequences: A user whose settings file breaks silently gets defaults; the warning in the log
is the only signal. Acceptable for theme and accent; it would not be acceptable for session
state, which is why Phase 3 handles that separately.
Tests/verification: `JsonSettingsStoreTests.CorruptFileFallsBackToDefaultsAndIsLeftInPlace`.

## DECISION-0023 — Sequential navigation stops at both ends and says so
Date: 2026-08-08
Status: Accepted
Context: `docs/VIEWER.md` required the end behaviour to be explicit and forbade silent looping,
but never said what should actually happen.
Decision: The position does not move. The status bar shows which end was reached. Pressing
again does nothing. No wrap, no exit from the Viewer.
Alternatives: Wrap after a first press that only warns; leave the Viewer at the end.
Reason: User decision. It is also the only option where a held-down arrow key cannot silently
carry the user back to the start of a long gallery.
Consequences: `ViewerNavigator.Edge` carries which end was hit and is cleared by the next
successful move, so the indication does not linger.
Tests/verification: `ViewerNavigatorTests` — both ends, repeated presses, and the indication
clearing after a move.

## DECISION-0024 — The Viewer scales through a Viewbox, not arithmetic
Date: 2026-08-08
Status: Accepted
Context: `docs/VIEWER.md:10` asks for large images fitted down proportionally and small images
left at native size.
Decision: `<Viewbox Stretch="Uniform" StretchDirection="DownOnly">` around the `Image`. Decode
size is capped separately by `BitmapImage.DecodePixelWidth`, computed with `ImageScaling.FitDown`
against the host size, so a 50-megapixel photo does not become 200 MB of pixels.
Alternatives: Compute a display size and set it on the `Image` — the same rule reimplemented in
code, with a resize handler to keep it right.
Reason: `StretchDirection="DownOnly"` is exactly the stated requirement, as a platform feature,
with no code and nothing to keep in sync on resize. `BitmapImage` also applies EXIF orientation
by itself, which is why no `IImageDecoder` was ever needed.
Consequences: An image decoded for a small window stays at that resolution until it is
reloaded, so enlarging the window can look soft until the next navigation.
Tests/verification: The sizing rule is tested in `ImageScalingTests`; the Viewbox behaviour is
the platform's.

## DECISION-0021 — Thumbnails come from the Windows shell
Date: 2026-08-08
Status: Accepted
Context: The Explorer list needs a thumbnail per image row, for folders that can hold 100 000
files.
Decision: `StorageFile.GetThumbnailAsync`, which reads the shell thumbnail cache — the same one
File Explorer fills. Requests are bounded to four at a time and the results are held in a
byte-bounded LRU cache keyed by path, size and last-write time.
Alternatives: Decode every image ourselves through WIC at a reduced size — correct, but it
repeats work the operating system has already done and stored on disk.
Reason: For any folder the user has already browsed the thumbnail is a cache read. Measured at
90 ms for twelve images cold and 1.4 ms served from memory (docs/PERFORMANCE.md, Phase 4).
Consequences: Thumbnail appearance follows the shell, including its own generation rules and
failures. Files the shell has no thumbnail for fall back to the type glyph. The last-write time
in the cache key means an edited file does not keep showing its old picture.
Tests/verification: `ImageServicesTests` — a thumbnail is produced for a real file, the second
request is the same instance, and a missing file yields null rather than an error.

## DECISION-0022 — Rows request thumbnails through ContainerContentChanging
Date: 2026-08-08
Status: Accepted
Context: Row objects are created for the whole listing, so requesting a thumbnail per row object
would issue 100 000 requests for a 100 000-file folder.
Decision: Request thumbnails from `ListView.ContainerContentChanging`, which fires only for rows
the control actually realises, and defer the fetch to a later phase so text appears first.
Alternatives: Fetch in the row constructor; fetch the visible range manually on scroll.
Reason: It is the virtualisation hook the platform provides for exactly this, and it costs about
twenty lines.
Consequences: Scrolling fast issues and abandons requests; the concurrency limit in the provider
keeps that bounded. Thumbnails appear a beat after the text, by design.
Tests/verification: Not unit-tested — it is a UI virtualisation callback. Verified by running
the application against a folder of images.

## DECISION-0005 — Image decoding via WIC, metadata via WIC properties
Date: 2026-08-07 (revised 2026-08-08)
Status: Accepted
Context: The Viewer must decode common formats fast, honour EXIF orientation, and scale down
without loading full-size bitmaps when a smaller one suffices.
Decision: Decode through the Windows Imaging Component (`Windows.Graphics.Imaging`), which is
part of the OS. Read metadata through the same decoder's `System.Photo.*` properties — MetadataExtractor
turned out not to be needed at all.
Alternatives: MetadataExtractor (Apache-2.0) for EXIF; ImageSharp (Six Labors Split License —
restricted for commercial use, acceptable here but an avoidable constraint); Magick.NET (large
native footprint).
Reason: Zero third-party dependency; WIC supports decode-time downscaling, which is the main
lever for both memory and latency; HEIF/AVIF/WebP come from OS codecs. WIC already exposes
orientation, date taken, camera, focal length, aperture, exposure and ISO — every field the
Viewer status bar needs — so a metadata library would have been a dependency for nothing.
Consequences: Format support follows the codecs installed on the machine. Codecs that carry no
EXIF (BMP, and some that refuse the whole property request) yield dimensions only, which the
reader handles by returning empty optional fields rather than failing.
Tests/verification: `ImageServicesTests` reads dimensions from a file written by the test,
confirms optional fields stay empty when there is no EXIF, and confirms a non-image fails.
Orientation and fit-down maths are covered separately in `ImageScalingTests`.

## DECISION-0032 — One folder tree, expansion remembered per tab
Date: 2026-08-08
Status: Accepted
Context: The first Explorer had no tree at all: folders were reached through a picker dialog,
double-click and Backspace. The user's verdict on seeing it was that this is not a file browser.
Tabs complicate the fix — 25 tabs could mean 25 trees.
Decision: One `TreeView` in the shell. Which nodes are expanded, and which folder is current, are
stored against the tab and replayed when it is activated. A new tab inherits the expanded set of
the tab it was opened from. The set is part of the session, so it survives a restart.
Alternatives: A tree control per tab — 25 controls and 25 sets of directory reads held live; one
shared tree with shared expansion — cheapest, but then switching tabs silently moves the other
tab's tree.
Reason: Expansion is a list of strings. Storing that per tab costs nothing, while a control per
tab costs a control per tab.
Consequences: Switching tabs re-reads the expanded directories. With a handful of expanded nodes
that is a few milliseconds; with a deeply expanded tree it would be worth caching. The pane is a
fixed 280 px — not resizable yet.
Tests/verification: `NavigationHistoryTests` covers back/forward. The tree itself is UI and was
verified by running the application.

## DECISION-0031 — The distributable is a self-contained build, not a publish
Date: 2026-08-08
Status: Accepted
Context: Phase 15 needed an output that runs on a machine with neither the .NET runtime nor the
Windows App Runtime installed.
Decision: `dotnet build -c Release -p:SelfContained=true`. The build output is the distributable:
529 files, about 219 MB, carrying both runtimes.
Alternatives: `dotnet publish --self-contained`, which is the obvious command and the wrong one
here — its output starts and then dies with `XamlParseException`, because it does not copy the
application's own compiled XAML (`*.xbf`) or its `resources.pri`. Reproduced with and without
`-o`, and with and without `UseArtifactsOutput`; the build output has four `.xbf` files, the
published output has none.
Reason: The build output is the thing that has been run and verified after every phase. Shipping
what was tested beats shipping what a different command produced.
Consequences: 219 MB. `PublishTrimmed` is not an option — WinUI 3 resolves XAML types by
reflection. Anyone reaching for `dotnet publish` will get a broken folder, so README says so.
Tests/verification: Runs with `DOTNET_ROOT` pointed at a non-existent drive, exits cleanly, and
leaves no logs behind. `runtimeconfig.json` reports `includedFrameworks`, which is what marks a
self-contained application. A genuine clean machine was not available.

## DECISION-0030 — A surviving transient log is how a crash is detected
Date: 2026-08-08
Status: Accepted
Context: `docs/REQUIREMENTS.md:37` asks for recovery to be offered after a crash, but the
application cannot know at shutdown that it is about to be killed.
Decision: The transient log is deleted on a clean shutdown, so finding one at startup means the
previous run died. Leftovers are renamed to `crashed-*.log`, kept, and reported once with an
option to open the log folder. The tabs themselves are already back by then, restored from the
last committed session.
Alternatives: A "running" marker file — a second thing to keep in sync with the log; a registry
flag — state outside the folder that holds everything else.
Reason: The log already has exactly the lifetime the detection needs, so no new state is
introduced at all.
Consequences: Two instances running at once would each see the other's live log. The rename is
guarded: a file still held open is left alone.
Tests/verification: `FileLoggingServiceTests` — an abandoned log is kept and renamed, the current
run's own log is not collected, and a clean previous run leaves nothing. Verified end to end by
killing the process and restarting it.

## DECISION-0028 — SQLitePCLRaw is pinned above what Microsoft.Data.Sqlite asks for
Date: 2026-08-08
Status: Accepted
Context: `Microsoft.Data.Sqlite` pulls `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 transitively, which
carries GHSA-2m69-gcr7-jv3q, rated high. The build treats warnings as errors, so it failed.
Decision: Reference `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 directly, which ships the patched
native SQLite, and take `Microsoft.Data.Sqlite` 10.0.10.
Alternatives: Suppress NU1903.
Reason: A suppressed advisory is a vulnerability with a note attached.
Consequences: The pin has to be revisited when Microsoft.Data.Sqlite catches up, or it will
silently hold the version back.
Tests/verification: The build fails if the vulnerable version returns.

## DECISION-0029 — View statistics buffer in memory and flush in batches
Date: 2026-08-08
Status: Accepted
Context: `docs/REQUIREMENTS.md:34` requires buffering rather than committing on every navigation.
Decision: `RecordImageView` only touches memory. The buffer is written in one transaction when it
reaches 64 entries, when a session ends, or when the application asks — the last of which happens
on shutdown.
Alternatives: A flush timer; writing per navigation.
Reason: A keystroke must never wait for a disk write, and one transaction per batch is the reason
for buffering in the first place.
Consequences: A crash loses at most one batch of view counts. **This nearly shipped with a race**:
the background flush was fire-and-forget, so its batch could still be in flight when a session
ended and the totals were read — the test that counts 200 views saw 198. Flushes are now
serialised behind a semaphore and the last background flush is awaited before a session closes.
Tests/verification: `FavoritesAndStatisticsTests` — a 200-view session that exceeds the threshold,
nothing written before the session ends, and accumulation across visits.

## DECISION-0026 — Copy is a manual stream copy, Move verifies before deleting
Date: 2026-08-08
Status: Accepted
Context: `docs/FILE_OPERATIONS.md` requires progress, cancellation, and a Move that removes the
source only after the destination has been written and verified.
Decision: Copy reads and writes in 1 MiB chunks, which is what makes byte-level progress and
prompt cancellation possible at all. Each file is written to a `.nexapart` name and moved into
place, so a failure or cancellation never leaves a partial file wearing the real name. Move
within a volume is `File.Move` — atomic, nothing ever in flight. Move across volumes copies,
compares the destination's length against the source, and only then deletes.
Alternatives: `File.Copy` — no progress, no cancellation; `CopyFileEx` via P/Invoke — progress
callbacks, but interop for something a loop already does.
Reason: The safety requirement is the point of the phase, and a hand-written loop is what makes
it observable. Length is the cheap comparison the specification asks for; the bytes were written
by this process moments earlier, so hashing would buy nothing.
Consequences: Slower than `File.Copy` for many tiny files. Throughput and ETA are withheld for
the first second, because before that the figure swings wildly and a wrong ETA is worse than none.
Tests/verification: `FileOperationServiceTests` — copy leaves the source, move removes it,
cross-volume move between C: and E:, folder trees, cancellation leaving no partial file, and one
failing item not abandoning the rest.

## DECISION-0027 — Conflicts are resolved through a callback, not a policy object
Date: 2026-08-08
Status: Accepted
Context: Conflicts need a dialog showing both sides, four answers, and "apply to all" scoped to
the current operation.
Decision: `ExecuteAsync` takes a `Func<FileConflict, Task<ConflictChoice>>`. The service remembers
a choice only when `ApplyToAll` is set, in a local variable that dies with the call.
Alternatives: A `ConflictPolicy` class holding the remembered answer.
Reason: The remembering is three lines. A class for it would need its own lifetime rules to
guarantee what a local variable guarantees for free — that the answer cannot leak into the next
operation.
Consequences: The service cannot be asked what it decided last time, which is exactly the
requirement.
Tests/verification: `FileOperationServiceTests` — each of the four answers, "apply to all" asking
once, and a fresh operation forgetting the previous answer.

## DECISION-0025 — Archive entries are extracted to a cache, not streamed
Date: 2026-08-08
Status: Accepted
Context: Thumbnails, metadata and the Viewer all work on file paths. Browsing inside archives
could either plumb streams through all three, or turn an entry into a file.
Decision: `IArchiveService.MaterialiseAsync` returns a real path: an ordinary path unchanged, an
archive entry extracted into `%LOCALAPPDATA%\NexaViewer\cache\archives` on first use. The cache
is cleared on a clean shutdown.
Alternatives: Stream plumbing — a stream overload on the metadata reader, on the thumbnail
provider and on the Viewer's image loading, plus lifetime management for each.
Reason: One method instead of changes in four places, and every existing test keeps its meaning.
Consequences: Disk is used for the entries actually looked at, not for the whole archive.
Extraction is written to a `.partial` file and moved into place, so a failure never leaves
something that later looks like a valid cache hit. An archive edited while open would keep
serving the cached entry until the cache is cleared.
Tests/verification: `ArchiveServiceTests` — extraction, cache reuse, ordinary paths passing
through, missing entries, no `.partial` leftovers, and cache clearing.

## DECISION-0006 — ZIP and RAR both through SharpCompress
Date: 2026-08-07 (revised 2026-08-08)
Status: Accepted
Context: The Viewer needs access to individual entries inside an archive, not just sequential
extraction. The original decision chose the UnRAR library for RAR because it is the reference
decoder and handles every RAR variant including solid archives.
Decision: Both formats go through SharpCompress (MIT, fully managed), behind `IArchiveService`.
Alternatives: UnRAR (P/Invoke) as originally decided; `System.IO.Compression` for ZIP with a
separate RAR path.
Reason: UnRAR means shipping a native binary that has to be fetched from rarlab and committed as
a blob, under a licence that is not OSI. SharpCompress is one `PackageReference`, no binary in
the repository, and it reads RAR4 and RAR5. Using it for ZIP as well means one code path instead
of a format switch. Reliability is still priority 1, but a dependency the build cannot restore
by itself is its own reliability problem.
Consequences: **Solid RAR archives are the known weak point** — SharpCompress accesses them
forward-only, so jumping to an entry in the middle of a solid archive is slow or unsupported.
Reconsider UnRAR if that turns out to matter in practice.
Tests/verification: `ArchiveServiceTests` covers ZIP against real files written by the tests:
root and nested listings, sizes, extraction, corrupt archives. **RAR is not covered** — this
machine has no RAR writer, so no fixture could be produced. RAR support is therefore unverified
and must be tried against real archives before it is trusted.

## DECISION-0007 — SQLite via Microsoft.Data.Sqlite, hand-written migrations
Date: 2026-08-07
Status: Proposed
Context: Statistics and favourites need a local database with low startup cost.
Decision: `Microsoft.Data.Sqlite` (MIT). WAL journal mode, `foreign_keys=ON`, schema version
tracked in `user_version`, migrations written by hand. Writes are buffered and batched into
transactions; navigation never writes synchronously.
Alternatives: EF Core (heavier startup, more machinery than a handful of tables needs);
`System.Data.SQLite` (larger, slower release cadence).
Reason: Small dependency, direct control over pragmas and batching.
Consequences: Schema changes require an explicit migration step and a test.
Tests/verification: Phase 10 — migration, buffering and growth tests.

## DECISION-0019 — One artifacts output root for the repository
Date: 2026-08-08
Status: Accepted
Context: The app project sets `Platform=x64`, which the SDK puts into the default output path.
Building the solution and building the project therefore produced two different binaries, in
`bin\Release\` and `bin\x64\Release\`. A smoke test was run against the stale one and the
feature under test looked broken when it was not.
Decision: `UseArtifactsOutput=true` in `Directory.Build.props`. Everything lands under
`artifacts/bin/<project>/<configuration>/`, with no platform segment.
Alternatives: Always build the same way and remember which — a rule that holds until it does not.
Reason: An SDK feature, one line, removes the whole class of mistake.
Consequences: Output paths changed; anything referring to `bin/` needs updating.
Tests/verification: A clean rebuild produces exactly one `ViewerPrn.App.exe`.

## DECISION-0020 — The session is written on every structural change
Date: 2026-08-08
Status: Accepted
Context: Tabs must survive both a normal restart and a crash (docs/TESTING.md:43).
Decision: Write the session after opening, closing, navigating and re-sorting a tab, and once
more synchronously on window close. Selection is captured at those moments, not on every
selection change.
Alternatives: Save only at shutdown — loses everything in a crash; save on every selection
change with a debounce timer — a timer to own and get wrong for state that costs little to lose.
Reason: The expensive thing to lose is the set of open tabs, and that only changes on those four
actions. Selection is cheap to re-make.
Consequences: A crash can lose a selection change made since the last structural change. The
tabs themselves are never lost. The shutdown write is waited on rather than fire-and-forget,
because the process is about to exit.
Tests/verification: `JsonSessionStoreTests`, including an interrupted-write case that leaves a
stale `.tmp` behind and asserts the previous session still loads.

## DECISION-0008 — Session state as atomically written JSON, not in the database
Date: 2026-08-07 (accepted 2026-08-08)
Status: Accepted
Context: Up to 25 tabs must survive a restart, and must be recoverable after a crash.
Decision: Persist session state as JSON (`System.Text.Json`), written to a temporary file and
committed with `File.Replace` and a backup file.
Alternatives: Store the session in SQLite alongside statistics.
Reason: Session recovery must work even when the database is locked or corrupt; a single
atomic file replace is easier to reason about and to verify.
Consequences: Two persistence mechanisms exist. The session file needs its own corruption
handling and a documented format version.
Tests/verification: Phase 3 — 1 tab, 25 tabs, restart, corrupted file, missing path, crash recovery.

## DECISION-0009 — Logging is first-party, not a framework
Date: 2026-08-07
Status: Proposed
Context: The requirement is unusual: a transient log deleted after a clean shutdown, and a
crash log that survives abnormal termination with process and viewer state attached.
Decision: Implement `ILoggingService` directly over a buffered file writer.
Alternatives: Serilog or `Microsoft.Extensions.Logging`.
Reason: The transient/crash split is the whole requirement and is a few dozen lines. A logging
framework would be configured around it rather than helping with it, and adds startup cost.
Consequences: Retention, rotation and flush-on-crash are this project's responsibility.
Tests/verification: Phase 13 — clean shutdown removes the transient log; a simulated crash
leaves a crash report containing the required fields.

## DECISION-0010 — Cache sizing is measured, not chosen up front
Date: 2026-08-07 (settled 2026-08-08 in Phase 14)
Status: Accepted
Context: `docs/PERFORMANCE.md` forbids inventing performance claims.
Decision: Bounded LRU caches for decoded images and thumbnails, bounded by total bytes rather
than entry count, with a small fixed Viewer prefetch window. The actual limits are set in
Phase 14 from measurements and recorded in `docs/PERFORMANCE.md`.
Alternatives: Pick round numbers now.
Reason: Image sizes vary by orders of magnitude; an entry-count limit gives no memory guarantee.
Consequences: Phases 4–6 use deliberately conservative placeholder limits, marked in code.
Tests/verification: Phase 14 benchmarks.

## DECISION-0011 — Blocked requirements fail loudly
Date: 2026-08-07
Status: Accepted
Context: `CLAUDE.md` forbids inventing missing requirements or substituting easier
approximations, but code still has to compile and run.
Decision: Every undefined requirement throws `BlockedRequirementException`, naming the
requirement and its specification reference. Each one has a test asserting that it throws.
Alternatives: Return a placeholder value; silently skip.
Reason: A guessed value would be indistinguishable from a specified one once written. The
tests double as the live checklist of what is still open.
Consequences: Some inputs (galleries of 1–50 or 300–500 images, the introductory display, the
Stop control) currently fail rather than render. They must be answered before Phase 11.
Tests/verification: `tests/ViewerPrn.Domain.Tests/BlockedRequirementTests.cs`.

## DECISION-0012 — Abstractions are introduced when their phase starts
Date: 2026-08-07
Status: Accepted
Context: `docs/ARCHITECTURE.md` lists thirteen abstractions. Declaring all of them in Phase 0
would mean inventing method signatures for behaviour that is not yet specified.
Decision: Create each interface at the start of the phase that implements it. Phase 0 defines
only `ILoggingService`, which Phase 0 crash handling already needs.
Alternatives: Declare all thirteen now as empty or speculative interfaces.
Reason: A speculative signature is a guessed requirement wearing a type name.
Consequences: The Application layer grows through Phases 2–12. The layer boundary is enforced
from the start by project references, not by having every interface present.
Tests/verification: Project reference graph: App -> Infrastructure -> Application -> Domain.
