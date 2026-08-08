# FILE OPERATIONS

Operations: Copy, Move, Delete, Rename, Copy To, Move To.

Move safety:
1. validate;
2. transfer safely;
3. verify destination;
4. remove source only after successful verification.

Conflict dialog:
- source preview;
- destination preview;
- filename;
- size;
- dimensions for images;
- useful metadata;
- conflict reason;
- Replace/Rename/Skip/Cancel;
- Apply to all for current operation only.

Compare cheaply first: name/size/metadata; hash only when justified.

Large operations need progress, current item, counts/percentage when known, throughput/ETA when reliable and cancellation.

Handle access denied, locks, missing files, full disk, invalid/long paths, network errors, cancellation and concurrent changes. User messages should be understandable; technical details go to logs.
