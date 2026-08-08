# REQUIREMENTS

## Explorer
Windows-like browser for folders/files/images/ZIP/RAR. Default: folders first, files second, normal name order. User can change sort criterion/direction.

## Random Explorer
Separate command that mixes folders, files and archives into one random list. Reversible to normal sorting. It must not randomize the internal order of images in a gallery.

## Tabs
Maximum 25. Preserve path, order, active tab, selection and meaningful view/sort state. Persist across restart. Do not eagerly load all tabs.

## Viewer
Open images from directories and archives. Show full source path in title, total/current counter, file size, dimensions and available EXIF. Sequential and random modes. Esc/Enter exits and restores Explorer selection. F6 minimizes only in Viewer.

## Scaling
Large image: proportional fit-down. Small image: native size by default; do not upscale. Preserve aspect ratio and honor EXIF orientation.

## Sequential
Adjacent navigation. End behavior must be explicit; no silent looping.

## Random Viewer
Space = next random. Backspace = previous viewed. Use browser-like history.

## File operations
Copy, Cut, Paste, Copy To, Move To, Rename, Delete. Progress and cancellation for lengthy operations.

## Conflicts
Never silently overwrite. Show source/destination previews when possible, comparison info, Replace/Rename/Skip/Cancel, and Apply to all for the current operation only. Compare cheaply first; hash only when justified.

## Favorites
Named groups containing references to folders/ZIP/RAR and optionally images. One target may belong to multiple groups. Broken targets can be repaired or removed.

## Statistics
Local SQLite view history for sources/galleries and images. Record first/last viewed, sessions, opens, total view time, unique images, total image views, last image/position and coverage where practical. Buffer events instead of synchronously committing every navigation.

## Logging
Transient diagnostic log during normal operation. Remove it after successful shutdown. Preserve crash logs with timestamp, version, OS/runtime, exception, stack trace, current operation/path/file/tab/viewer state. Offer recovery after crash.

## Theme
Dark, Light, System and configurable accent where practical.

## Context menus
Useful Windows-like actions: Open, Open in new tab, Copy, Cut, Paste, Copy To, Move To, Rename, Delete, Favorites, Open in Explorer, Copy Path, Properties.

## Out of scope
No photo editing, RAW development, layers, filters, ACDSee catalog replacement, face recognition, cloud, accounts, telemetry, advertising, AI image analysis, printing or batch photo processing unless explicitly approved.

## Hidden and system files
Follow the user's Windows Explorer folder options, read live: "Show hidden files, folders, and drives" governs hidden entries, "Hide protected operating system files" governs entries marked both Hidden and System. No separate in-app setting. See DECISION-0018.

## Tab limit behaviour
Opening a 26th tab shows a message: the maximum is open, and opening more than 25 tabs is coming soon — the feature is under development and optimisation. The command stays enabled at the limit so it can explain itself.

## Language
Russian is the primary UI language; English is available. The language is selectable in View -> Language and takes effect at the next start.

## Intro Counter controls
Four controls affect only the helper counter; the standard `TOTAL/CURRENT` counter is never changed:
1. Reset Cycle
2. Minus 10
3. Minus 1
4. Stop / Do Not Count

Reset count is shown beside the helper counter in bold/slightly larger text; 4 = orange, 5 = red + `!`.
