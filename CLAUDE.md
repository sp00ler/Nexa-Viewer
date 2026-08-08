# CLAUDE.md — Master Development Contract

## Mission
Build a private Windows-only lightweight image browser/viewer and file explorer based on the user's established ACDSee workflows. This is not an ACDSee clone: do not copy proprietary code, assets, databases, branding, or closed implementation details.

Priorities:
1. Reliability and protection of user files
2. Correctness
3. Responsive UI
4. Low resource usage
5. Fast image browsing
6. Familiar Windows-like UX
7. Visual polish
8. Extra features only when they do not materially increase complexity

Read every file in `docs/` before implementation.

## Non-negotiable
- Never invent missing product requirements.
- Never silently reinterpret explicit requirements.
- Never replace a requested behavior with an easier approximation.
- If ambiguity changes user-visible behavior, mark it BLOCKED and ask the user.
- Never sacrifice file safety for UI convenience.
- No telemetry, cloud sync, accounts, advertising, network reporting or required Internet connection.
- No photo editor.
- Do not implement ACDSee features merely because they exist.

## Development protocol
Before coding:
1. Inspect repository and environment.
2. Verify .NET SDK, Windows SDK and WinUI/Windows App SDK tooling.
3. Read all project docs.
4. Identify blockers.
5. Document architecture.
6. Create a minimal buildable skeleton and test infrastructure.
7. Build and run tests.

For every phase: Plan -> Implement small increment -> Build -> Test -> Review resource/file safety -> Update docs -> Record important decisions.

Never claim completion without tests.

## Baseline stack
Preferred:
- Windows 10/11
- C#
- .NET 10 LTS
- WinUI 3 / Windows App SDK
- SQLite

Do not change the stack without documenting a concrete reason and asking the user if behavior/architecture materially changes.

Review external libraries for license, maintenance, compatibility, Windows support, performance, memory, security and edge cases.

## Architecture
Separate UI, application/domain and infrastructure logic.

Use abstractions for filesystem, archive containers, image decoding/metadata, thumbnails, viewer navigation, file operations, collision handling, favorites, sessions, view statistics and logging.

Use async I/O, virtualization, lazy loading, bounded caches and background workers. Never block the UI thread with enumeration, archive scanning, decoding, hashing, DB work or file operations.

## Critical 1-based rule
All user-visible image positions are 1-based. UI must never show image 0. Internal zero-based indexing is allowed only behind explicit conversion:
`displayPosition = internalIndex + 1`

## Critical Intro Counter examples
For 951 images:
- intro = 20
- cycle = 100
- physical 1..20 = introductory state
- physical 21 = `1(20)/100`
- physical 105 = `85(20)/100`
- warning begins at physical 105 because 15 images remain in the cycle.

For 469:
- intro = 15
- cycle = 50
- physical 16 = `1(15)/50`
- physical 50 = `35(15)/50` + warning.

See `docs/VIEWER.md`; unresolved ranges must remain BLOCKED, never guessed.

## File safety
For Move: safely transfer, verify destination, then remove source. Handle locks, permissions, missing files, full disks, network errors, invalid/long paths, source/destination changes and cancellation.

Never silently overwrite conflicts.

## Performance
Measure rather than guess. Benchmark startup, idle RAM/CPU, 1k/10k/100k folders, large ZIP/RAR, thumbnails, Viewer navigation, 25 tabs and DB growth. Record results in `docs/PERFORMANCE.md`.

## Testing
Required: unit, integration, DB, file-operation, navigation, archive and counter tests. Test counter boundaries including 1, 50, 51, 77, 78, 127, 128, 177, 178, 227, 228, 299, 300, 500, 501, 505, 645, 799, 800, 951, 1199.

## Definition of Done
Implemented + integrated + error-handled + tested + documented + reviewed for performance/resource safety.

## First task
Do NOT implement the whole app. First inspect repo/environment, read docs, identify blockers, establish architecture, create a minimal buildable skeleton and tests, build and test, then report findings and the Phase 1 plan.
