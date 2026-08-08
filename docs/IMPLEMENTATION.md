# IMPLEMENTATION PHASES

0. Environment, architecture, skeleton and tests. — **done 2026-08-07**
1. Application shell: window, menus, theme, status bar, tabs. — **done 2026-08-07**
2. Explorer: enumeration, virtualization, sorting, selection, rename/delete. — **done 2026-08-07**
3. Tabs/session: 25 tabs, persistence, atomic save, recovery. — **done 2026-08-08**
4. Image engine: metadata, orientation, thumbnails, cache. — **done 2026-08-08**. Full-image decoding moved into Phase 5, where the Viewer defines what it needs.
5. Viewer: sequential, random, history, keyboard, exit, F6, status. — **done 2026-08-08**. No prefetch yet; the Intro Counter stays in Phase 11.
6. Archives: ZIP, RAR, virtual folders, image viewing. — **done 2026-08-08**. ZIP verified end to end; RAR unverified for lack of a fixture (DECISION-0006).
7. File operations: Copy/Move/Copy To/Move To/progress/cancel. — **done 2026-08-08**
8. Conflicts: previews, comparisons, Replace/Rename/Skip/Cancel/Apply all. — **done 2026-08-08**, together with phase 7: conflict handling is part of the operation, not a layer on top.
9. Favorites. — **done 2026-08-08**
10. SQLite statistics. — **done 2026-08-08**. Recording only; no statistics UI, because none is specified.
11. Intro Counter and its four controls — BLOCKED until unresolved ranges are clarified.
12. Random Explorer. — **done 2026-08-08**
13. Reliability/recovery. — **done 2026-08-08**
14. Performance benchmarking/optimization. — **done 2026-08-08**. Suite in `tools/NexaViewer.Bench`; nothing optimised, because nothing measured is yet the bottleneck.
15. Packaging and clean-machine test.

Do not jump directly to a complete implementation.
