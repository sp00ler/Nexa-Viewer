# TESTING

Required: unit, integration, database, file-operation, Viewer navigation, archive, session recovery and Intro Counter tests.

Counter boundary set:
1, 5, 50, 51, 59, 60, 77, 78, 127, 128, 177, 178, 227, 228, 299, 300, 500, 501, 505, 645, 799, 800, 951, 1199.

Mandatory:
951 -> intro 20, cycle 100, physical 21 = 1(20)/100, physical 105 = 85(20)/100 + warning.
469 -> intro 15, cycle 50, physical 16 = 1(15)/50, physical 50 = 35(15)/50 + warning.

Regression: first image must display 1, never 0.

Random history:
35,102,17,88; Backspace must return 17,102,35.

## Cycle-control tests
Reset:
- `1(15)/50` -> reset -> `1(15)/50`, reset count 1.
- `9(15)/50` -> reset -> `1(15)/50`, reset count 2.
- `4(15)/50` -> reset -> `1(15)/50`, reset count 3.
- fourth reset count is orange; fifth is red + `!`.
- Reset disabled at `11(15)/50`.

Minus 10:
- `10(15)/50` disabled.
- `11(15)/50` enabled.
- `34(15)/50` -> `24(15)/50`.

Minus 1:
- `10(5)/30` disabled; `11(5)/30` enabled.
- `35(15)/50` -> `34(15)/50`.
- `55(15)/70` -> `54(15)/70`.
- `85(20)/100` -> `84(20)/100`.

Stop:
- `15(15)/50` -> Stop -> next physical image still shows `15(15)/50` in helper counter.
- Standard `TOTAL/CURRENT` still increments.
- Stop must not be undone by refresh/tab switching.

File safety: conflicts, identical files, rename conflicts, permission denied, locks, full disk, cancellation, source disappearance, destination disappearance and partial failure.

Session: 1 and 25 tabs, restart, corrupted state, crash recovery, missing path.

Archives: ZIP/RAR, normal images, unsupported files, corrupted entries, large archives, duplicate internal names.
