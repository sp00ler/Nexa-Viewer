# ARCHITECTURE

Preferred stack: C#, .NET 10 LTS, WinUI 3/Windows App SDK, SQLite.

Suggested separation:
- UI
- Application
- Domain
- Infrastructure

Suggested abstractions:
`IFileContainer`, `IFileSystemService`, `IArchiveService`, `IImageDecoder`, `IImageMetadataReader`, `IThumbnailProvider`, `IViewerNavigationService`, `IFileOperationService`, `ICollisionResolver`, `IFavoritesService`, `ISessionService`, `IViewStatisticsService`, `ILoggingService`.

Physical directories and read-only archive containers should expose a common model where practical.

Never block UI with enumeration, archive scanning, decoding, thumbnail generation, hashing, DB work or file operations.

Use virtualization, lazy loading, bounded caches, limited Viewer prefetch and async/background work.

Session persistence must use atomic writes and recovery/backup where practical.

Logging: normal transient log is deleted after clean shutdown; crash log survives abnormal termination.

## Solution layout (Phase 0)

```
ViewerPrn.slnx
├─ src/ViewerPrn.Domain          net10.0                       no dependencies
├─ src/ViewerPrn.Application     net10.0                       -> Domain
├─ src/ViewerPrn.Infrastructure  net10.0-windows10.0.19041.0   -> Application
├─ src/ViewerPrn.App             net10.0-windows10.0.19041.0   -> Infrastructure   WinUI 3
├─ tests/ViewerPrn.Domain.Tests           net10.0                       xUnit
└─ tests/ViewerPrn.Infrastructure.Tests   net10.0-windows10.0.19041.0   xUnit
```

The dependency direction is enforced by project references: nothing references the UI, and
the Domain references nothing. Domain targets plain `net10.0` on purpose — the counter and
navigation rules must be testable without any Windows dependency.

Abstractions are added at the start of the phase that implements them rather than all at once
(DECISION-0012). Undefined requirements throw `BlockedRequirementException` instead of being
approximated (DECISION-0011).
