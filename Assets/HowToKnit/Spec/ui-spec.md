# How to Knit — UI spec

Reference screen: **1920 × 1080**. All positions are in pixels from the top-left of the screen. The same numbers are in `ui-spec.json`.

## Colour

| Token | Hex | Use |
|---|---|---|
| Ink | `#1a1c22` | lines, text, dark buttons |
| Paper | `#f6f7fb` | page background, under the grid |
| Card | `#fbfcfe` | cards, panels, browser toolbar |
| White | `#ffffff` | animation card, keycaps, chat bubbles |
| Chip | `#dfe8fb` | pills, address bar, current stage row |
| Hover | `#e9eefa` | tab and toolbar-button hover |
| Yarn | `#3b72d9` | the only accent: "Knit", ticks, progress, selection |
| Muted | `#4d5566` | secondary text |
| Error | `#9e2b55` | "try again" text |
| Grid | pink rows `rgba(214,112,166,.13)` + blue columns `rgba(66,108,210,.13)` | 24px squares (`graph_paper_tile.png`) |

## Type

| Role | Font | Size |
|---|---|---|
| Title "How to / Knit" | Silkscreen Bold | 172 |
| Page heading "Tutorials" | Silkscreen Bold | 64 |
| Lesson heading | Silkscreen Bold | 46 |
| Panel title / lesson card title | Silkscreen Bold | 34 / 28 |
| Buttons | Silkscreen | 24 (menu), 21–22 (dialog, start) |
| Tabs, URL | Silkscreen | 22, 20 |
| Chips, labels | Silkscreen | 16 |
| Body, messages | DM Mono | 21 |
| Captions, status | DM Mono | 17–20 |
| Small keys in lists | DM Mono | 15 |

## Lines and corners

- **Outlines:** 2px ink. A selected lesson card gets a 3px yarn outline. Keycaps use a 3px outline.
- **Keycap base:** the up keycap has a 10px bottom lip; pressed, it has 3px and the cap drops 7px.
- **Corner radius:** card 22, animation card 24, dialog 26, button 16, keycap 14, row highlight 12, step box 9, tick 4. Pills and the address bar are fully rounded.

## Shadows

| Element | Shadow |
|---|---|
| Cards and dialog | soft, about 0 24px 52px at 13–20% ink |
| Chat bubbles | two layers: 0 20px 44px at 38% plus 0 5px 12px at 22% |
| Button hover | the face moves up-left 3px; a hard ink shadow appears 5px down-right |

## Browser frame (every screen)

| Part | Rect / value |
|---|---|
| Toolbar | 0,0 1920×80, card fill, 2px rule at the bottom |
| Window dots | 16px rings at x 28 / 54 / 80 |
| Back / forward / reload | 52px hit areas, 26px icons |
| Address bar | height 48, starts after forward, ends 94px from the right edge |
| Address text | `https://www.howtoknit.love` in ink, then the path in muted |
| Tab row | 0,80 1920×84, rule at the bottom |
| Tabs | start at x 60, 6px apart |
| Active tab | 56 tall, covers the rule beneath it |
| Inactive tab | 54 tall, hover fill only |
| Page content | starts at y 164 |

## Title

| Element | Rect / value |
|---|---|
| "How to" | x 120, y ≈ 244 |
| "Knit" | x 120, y ≈ 399, in yarn |
| Start learning | 120,628 540×76, dark |
| Settings | 120,722 540×76, light |
| Back card | 1160,254 640×370, rotated 5° clockwise, 55% opacity, knit drawing (contain, 18px padding, shifted up 12%) |
| Front card | 990,374 760×480, rotated 3° anticlockwise, soft shadow, cast-on drawing (contain, scale 1.18, 28px down) |

## Lesson select

| Element | Rect / value |
|---|---|
| "Tutorials" | 60,204 |
| Subtitle | 60,286 |
| Progress | "1 of 6 lessons" and six 40×16 ticks, right-aligned at x 1860 |
| Cards | 3 × 2 grid from 60,330; each 580×286 with 30 gaps; 168px picture area above a rule |
| Card text | chip "Lesson 01", status on the right, title below |
| Card states | done (check mark) · up next · locked (dashed outline and padlock) · selected (3px yarn outline, lifted 6px) |
| Detail bar | 60,968 1800×104: lesson title and description, then "Start lesson" (dark 300×68) or "Finish Lesson 02 first" (dashed) |

## Gameplay (cast on)

| Element | Rect / value |
|---|---|
| Breadcrumb | 60,188, DM Mono 18 muted |
| Heading | 60,218, Silkscreen Bold 46 |
| Animation card | 60,282 1280×720 (16:9), white, 24 radius, soft shadow |
| Status line | 60,1016 |
| Step panel | 1380,282 480×206: "Step n of 4", stage name, verb plus small keycap, progress bar (428×12) |
| Stage list | 1380,508 480×248: four 50px rows, current row on a chip-blue highlight, finished rows get an ink box with a check |
| Stitch counter | 1380,778: 25 ticks across 480px, 3px gaps, 22 tall |

### Key prompt

The keycap sits on the animation card. It's 88px with a pill tag below.

| Stage | Centre on card (% from top-left) |
|---|---|
| Ready yarn | 22%, 30% |
| Needle under / over | 84%, 28% |
| Pull through & tighten | 85%, 36% |

The keycap has four modes:

- **Press / Let go:** blinks between up and pressed every 0.3s.
- **Hold:** stays pressed.
- **Get ready:** shows the next key, still, at 60% opacity.

### Messages

| Setting | Value |
|---|---|
| Bubble | 440 wide, right edge at x 1831, newest bottom at y 999, 16px gaps |
| Text | DM Mono 21, 18/24 padding |
| Tail | squared-off corner at the bottom right |
| Start | after 3 stitches (so while the 4th is being made); they stay until the lesson ends |
| Timing | one message every 3.2s, typing dots for the first 1.3s of each |
| Entry | the gap opens over 0.36s, then the bubble slides in from 480px right over 0.44s, ease-out |

## Dog-shadow transition (title → lessons)

| Setting | Value |
|---|---|
| Art | `dog_shadow_blurred` (1460×938, includes 80px of blur padding), 22% opacity |
| Position | bottom edge 60px below the screen |
| Walk | x keys (fraction of time → left edge): 0 → 2000, 0.34 → 470, 0.66 → 330, 1 → −1450; ease-in-out per segment; 4.2s |
| Squish | scale-x 1 → 0.8 over 0.36–0.44, hold to 0.56, back to 1 by 0.64, pivot at the bottom of the silhouette |
| Bob | 18px up and down every 0.5s, tilting up to 1.5° |
| Then | a 0.7s pause before the next scene |
