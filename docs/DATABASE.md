# DATABASE

SQLite is the persistent local database.

Logical entities:
- FavoriteGroups
- Favorites
- ViewSessions
- ViewEvents (optional/buffered)
- ViewAggregates

View session may contain SessionId, source identity/path, start/end, duration, total images, images viewed, start/end position and Viewer mode.

Archive image identity should combine archive identity with internal entry path and relevant metadata.

Do not require hashing every image just to record views.

Use buffered DB writes, transactions, indexes, foreign keys and schema migrations. Keep statistics local; never upload them.
