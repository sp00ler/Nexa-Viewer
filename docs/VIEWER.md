# VIEWER

## Standard counter
Format `CURRENT/TOTAL`, e.g. `69/469` — the sixth image of 155 reads `6/155`. Earlier revisions
of this document said total first; the user corrected that on 2026-08-08. User-visible positions
are always 1-based.

## Viewer status
Show total/current, size, dimensions, type, full path in title and useful EXIF. Hide empty EXIF fields.

## Scaling
Fit down large images proportionally; do not upscale small images by default; preserve aspect ratio; respect EXIF orientation.

A large picture fills the window edge to edge in the dimension that binds, with the aspect ratio kept. Decoding is capped at 3840 on the longest edge for memory only — never to the size of the window, which was the earlier mistake: the Viewbox does not enlarge what it is given, so a picture decoded to the viewport of the moment was drawn smaller than the window.

## Sequential
Adjacent navigation. End behavior must be explicit; no silent looping.

At either end the position does not move and the status bar says which end was reached —
`конец списка` / `end of list`, `начало списка` / `start of list`. Pressing again changes
nothing. No wrap, no exit. See DECISION-0023.

## Modes
Space means "next". In sequential mode that is the next image, and it stops at the end exactly as the arrows do. Only in random mode does it draw a random one. The mode is a setting (View -> Random viewing) and is remembered.

## Random history
Random navigation is history, not repeated random generation. Example `35 -> 102 -> 17 -> 88`; Backspace returns `17 -> 102 -> 35`. With no random history behind it — the end reached with End or the arrows records none — Backspace steps back one image rather than reporting the start of the list.

## End of random viewing
Each random draw comes from the images not yet shown, so no image repeats and a gallery of N
ends after exactly N of them. Space then does nothing at all, and says nothing. Landing on the
last physical image partway through is an ordinary draw and ends nothing. Everything else still moves — Left/Up and Right/Down step one image,
Backspace walks back through what was seen, Home and End go to the first and last physical
image. The counters are untouched by the dead key, because nothing moves. The stop lasts for as
long as the gallery does: it is not lifted by stepping away from the image that triggered it.
See DECISION-0042.

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

There are two reset buttons, identical except for the count (DECISION-0035):
- **`Сброс 5!` / `Reset 5!`** — the reset described above, which adds to the reset count.
- **`Сброс` / `Reset`** — same position change, reset count untouched.

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
| Reset Cycle, both buttons | cycle position 1–10 inclusive |
| Minus 10 | cycle position >10 |
| Minus 1, total <=299 | cycle position >10 |
| Minus 1, total >=300 | cycle position >= cycleLength-15 |
| Stop | always |

## Resolved rules (answered by the user, 2026-08-08)

These were BLOCKED. They are now specified and implemented.

### Ranges
The cycle length is `ceil(N/100)*10` for **every** total from 300 upward, with no upper bound in
the rule; it is implemented to 9999.

Intro follows bands. Below 300 the established table stands, because it is explicit data the
formula would contradict (a 100-image gallery is cycle 5 by the table, 10 by the formula).

| Total | Intro | Cycle |
|---|---|---|
| 1–50 | 5 | none — displayed as `-(5)/-` |
| 51–77 | ceil(N/10) | 5 |
| 78–127 | 10 | 5 |
| 128–177 | 15 | 7 |
| 178–227 | 20 | 10 |
| 228–299 | 10 | 30 |
| 300–500 | 15 | 50 — a flat value, not the formula |
| 501–799 | 15 | ceil(N/100)*10 |
| 800–1199 | 20 | ceil(N/100)*10 |
| 1200–1599 | 25 | ceil(N/100)*10 |
| each further band of 400 | previous + 5 | ceil(N/100)*10 |

**Assumption, flagged.** The user gave the band step (400, so 1200–1599 next) but not what intro
does in those bands. Bands exist only to set intro — the cycle is a formula and needs none — so
the +5 per band seen from 300→800 is continued. Intro at 9999 is therefore 130.

### Display — two phases
The counter first counts the introductory block, and only the step after that block ends switches
it to counting the cycle.

Totals from 228 up:

| | |
|---|---|
| Phase one | `X/intro(cycle)` — 951 gives `1/20(100)` … `20/20(100)` |
| Phase two | `X(intro)/cycle` — the next step gives `1(20)/100` |

Totals below 228 are written the other way round: phase one omits the cycle, phase two puts the
cycle in the brackets.

| | |
|---|---|
| Phase one | `X/intro` — 149 gives `1/15` … `15/15` |
| Phase two | `X(cycle)/intro` — the next step gives `1(7)/15` |

Worked examples, all confirmed by the user:

```
149 → 1/15      … 15/15      → 1(7)/15
110 → 1/10      … 10/10      → 1(5)/10
269 → 1/10(30)  … 10/10(30)  → 1(10)/30
345 → 1/15(50)  … 15/15(50)  → 1(15)/50
769 → 1/15(80)  … 15/15(80)  → 1(15)/80
951 → 1/20(100) … 20/20(100) → 1(20)/100
```

Totals of 1–50 have no cycle at all and show `-(5)/-`.

### Stop / Do Not Count
Always enabled. One press freezes the counter for exactly one advance. Presses do not accumulate:
it is a flag on the image in front of the user, so three presses still cost one image
(DECISION-0034). The standard `TOTAL/CURRENT` counter keeps moving throughout.

### Reset count
1–3 normal, 4 orange, 5 red with `!`. From the sixth reset onward it stays at the fifth state.
It clears when the Viewer is closed, not before.

### Backward navigation
Going back walks the path already taken and decrements the cycle position. Going forward again
retraces the same path to the same position — **including in random mode**, where forward after
back replays what was seen rather than drawing a new random image. Only forward past the end of
the history draws a new one.

### What "physical image N" means
The index in the gallery's sorted-at-open order, which is what the standard `TOTAL/CURRENT`
counter shows. In random mode it does not track how many images have been seen: `115/69` can
mean the 69th image of 115 is on screen after 95 have been viewed.

The helper counter counts **images viewed**, not that index. The two coincide in sequential mode
and diverge in random mode.

### Scope and lifetime
Per tab, because different tabs hold different galleries. It resets when the Viewer is left for
the Explorer, not on tab switch and not on gallery change within one Viewer session.

## Correction to docs/TESTING.md
`10(5)/30` was a typo. Cycle 30 belongs to totals 228–299, where intro is 10, so the case is
`10(10)/30` disabled and `11(10)/30` enabled.
