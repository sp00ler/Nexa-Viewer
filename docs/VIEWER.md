# VIEWER

## Standard counter
Format `TOTAL/CURRENT`, e.g. `469/69`. User-visible positions are always 1-based.

## Viewer status
Show total/current, size, dimensions, type, full path in title and useful EXIF. Hide empty EXIF fields.

## Scaling
Fit down large images proportionally; do not upscale small images by default; preserve aspect ratio; respect EXIF orientation.

## Sequential
Adjacent navigation. End behavior must be explicit; no silent looping.

At either end the position does not move and the status bar says which end was reached —
`конец списка` / `end of list`, `начало списка` / `start of list`. Pressing again changes
nothing. No wrap, no exit. See DECISION-0023.

## Random history
Random navigation is history, not repeated random generation. Example `35 -> 102 -> 17 -> 88`; Backspace returns `17 -> 102 -> 35`.

## Exit
Esc and Enter exit Viewer and restore Explorer selection to the exact active image.

## Intro Counter
Format `X(Y)/Z`:
- X = current position within cycle
- Y = introductory physical-image count excluded from cycle
- Z = cycle length

Introductory images are physically viewed but do NOT count toward cycle position.

Example 951:
- intro 20
- cycle 100
- physical 1..20 = introductory state
- physical 21 = `1(20)/100`
- physical 105 = `85(20)/100`
- warning begins at physical 105, 15 images before cycle end.

Example 469:
- intro 15
- cycle 50
- physical 1..15 = introductory state
- physical 16 = `1(15)/50`
- physical 50 = `35(15)/50` + warning.

## Established ranges
| Total | Intro | Cycle |
|---:|---:|---:|
| 1–50 | 5 | special `-` state |
| 51–77 | ceil(N/10) | 5 |
| 78–127 | 10 | 5 |
| 128–177 | 15 | 7 |
| 178–227 | 20 | 10 |
| 228–299 | 10 | 30! |
| 300–799 | 15 | unresolved below |
| 800–1199 | 20 | ceil(N/100)*10 |
| >1199 | unresolved |

For N > 500 within 300–799: cycle = ceil(N/100)*10, intro = 15. Examples: 505 -> 15/60, 645 -> 15/70.

For 800–1199: intro = 20, cycle = ceil(N/100)*10. Example 951 -> 20/100.

The cycle length is N/10 rounded up to the nearest ten. Earlier revisions of this document
wrote the formula as `ceil(N/10)*10`, which contradicts all four worked examples
(505 -> 510, 645 -> 650, 951 -> 960 instead of 60, 70, 100). Corrected per DECISION-0001.

## Warning
The warning begins 15 cycle positions before the end, using the cycle position, not the physical image number. Thus for 951/100 cycle it begins at `85(20)/100`, physically the 105th image.

## End of cycle
The cycle position does not wrap. It keeps growing past the cycle length: for 951 the
physical image 121 shows `101(20)/100` and the last image shows `931(20)/100`. Confirmed
for v1 — see DECISION-0002. Consequence: once the warning threshold is passed it stays
active for the rest of the gallery, and Reset Cycle (positions 1–10) becomes permanently
unavailable after it.

## Cycle-control buttons

All four controls modify only the helper counter. The standard `TOTAL/CURRENT` counter always tracks the actual physical image.

### 1. Reset Cycle
Resets the current cycle to position 1 without restarting/recounting the introductory block.

Examples:
- `1(15)/50` -> Reset -> `1(15)/50`, reset count = 1
- `9(15)/50` -> Reset -> `1(15)/50`, reset count = 2
- `4(15)/50` -> Reset -> `1(15)/50`, reset count = 3

Show the reset count beside the helper counter in bold, slightly larger text:
- 1–3 normal
- 4 orange
- 5 red and followed by `!`

Reset does not change the introductory count. Reset is enabled only at cycle positions 1–10 inclusive; at 11+ it is disabled.

### 2. Minus 10
Subtract 10 from the helper cycle position.
Example: `34(15)/50` -> `24(15)/50`.

Enabled only after position 10 (`11+`). Never allow an invalid position below the first valid post-intro cycle position.

### 3. Minus 1
Subtract 1 from the helper cycle position.
- Total <=299: enabled from position 11 onward; e.g. `10(5)/30` disabled, `11(5)/30` enabled.
- Total >=300: enabled from warning threshold `cycleLength - 15` onward.
Examples:
- `35(15)/50` -> `34(15)/50`
- `55(15)/70` -> `54(15)/70`
- `85(20)/100` -> `84(20)/100`

### 4. Stop / Do Not Count
Always enabled.

It marks the current physical image as not counted by the helper cycle. Example:
`15(15)/50` -> Stop -> next physical image still shows `15(15)/50` in helper counter.

The standard `TOTAL/CURRENT` counter still advances normally. Stop must not be undone by ordinary UI refreshes or tab switching.

### Button-state summary
| Control | Availability |
|---|---|
| Reset Cycle | cycle position 1–10 inclusive |
| Minus 10 | cycle position >10 |
| Minus 1, total <=299 | cycle position >10 |
| Minus 1, total >=300 | cycle position >= cycleLength-15 |
| Stop | always |

## BLOCKED
Do not invent exact 300–500 behavior or >1199 continuation. Ask the user before implementing those parts.

Further items found during Phase 0 that this document leaves undefined. Each is guarded by
a `BlockedRequirementException` and a test in `tests/ViewerPrn.Domain.Tests/BlockedRequirementTests.cs`:

- **Totals 1–50.** Intro is 5 and the cycle is a "special `-` state" whose display format is never given.
- **Introductory display.** Physical images 1..Y are called the "introductory state", but the string shown during them is never specified.
- **Stop / Do Not Count.** One-shot skip of the next image, or a mode that stays on until pressed again? The two readings differ from the second image onward.
- **Reset count beyond 5.** Colours are defined for 1–3, 4 and 5 only, and the document never says when the reset count clears (new cycle / new gallery / new session / never).
- **469 conflict.** `docs/TESTING.md` makes 469 -> intro 15, cycle 50 mandatory, but 469 lies inside the BLOCKED 300–500 range. Note that intro 15 + `ceil(N/100)*10` reproduces it exactly; confirmation required before relying on that.
- **`10(5)/30` in TESTING.md** is not producible by the range table: intro 5 belongs to totals 1–50, cycle 30 to totals 228–299 where intro is 10.
- **Backward navigation.** The effect of moving to a previous image on the helper counter is not specified.
- **Random mode.** In random order, "physical image N" is ambiguous: the count of images viewed, or the index within the gallery?
- **Scope and lifetime.** Whether the helper counter is per gallery, per tab or per session, and whether it survives a restart, is not specified.
