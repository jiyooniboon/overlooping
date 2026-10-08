# How to Knit — Unity UI export

UI kit for the `knit_sprite` project (Unity 6000.5, URP 2D, uGUI), taken from the "How to Knit" design canvas.

Nothing in your project has been changed. To use the kit, copy one folder in.

## What's here

```
UnityExport/
├─ README.md                         this file
├─ Spec/
│  ├─ ui-spec.md                     colours, type, sizes, layout, motion (human-readable)
│  ├─ ui-spec.json                   the same numbers, machine-readable
│  ├─ sprites.json                   every sprite: size, 9-slice border, image type, use
│  ├─ sprite-sheet-preview.png       all sprites on one sheet, labelled
│  ├─ ui-kit-reference.png           the design sheet from the canvas
│  └─ SVG/                           icons as SVG
└─ HowToKnit/                        ← copy this whole folder into Assets/
   ├─ Editor/HowToKnitTextureImporter.cs   sets import settings + 9-slice borders automatically
   ├─ Scripts/                       page builders (namespace KnitSprite.UI.HowToKnit)
   └─ Resources/HowToKnit/
      ├─ UI/        31 UI sprites @2x (cards, buttons, tabs, keycaps, bubbles, ticks…)
      ├─ Icons/     5 white icons @2x (tint them)
      ├─ Textures/  graph_paper_tile (repeats at 24px)
      ├─ Art/       your drawings + dog silhouette (+ pre-blurred shadow)
      └─ Fonts/     Silkscreen + DM Mono (.ttf) and their OFL licences
```

## Install

1. Copy `UnityExport/HowToKnit` into your project as `Assets/HowToKnit`.
2. Let Unity import. `HowToKnitTextureImporter` sets everything under `Resources/HowToKnit/`:
   - **UI and Icons:** Sprite, 200 PPU, Full Rect mesh, and the 9-slice borders.
   - **Art:** Sprite, 100 PPU.
   - **Textures:** Repeat wrap.

   If you copied the files in before the importer script compiled, right-click `Assets/HowToKnit` and choose **Reimport**.
3. Check the Console for compile errors. The scripts were type-checked outside Unity against stand-in Unity APIs. They have not been compiled or run in Unity itself.

## Use the pages

Each page builds itself at runtime, the same way `WebPageGameplayUI` does. You don't need prefabs or any Inspector wiring.

| Screen | Component | Notes |
|---|---|---|
| Title | `HtkTitlePage` | "Start learning" plays the dog walk, then loads `lessonSelectScene` (if set) and fires `onStart`. |
| Lesson select | `HtkLessonSelectPage` | Lessons, states, art and scenes are editable in the Inspector. |
| Cast-on gameplay | `HtkGameplayPage` | Films the existing sprite into the page and reads `ProgressManager`. **Disable `WebPageGameplayUI` and `StoryModeHud`** in that scene. Both draw their own page or HUD on screen, and showing them alongside this page would double up the UI. |

To try a page, add an empty GameObject to a scene, add the component, and press Play. For the gameplay page, use the scene that already has `ProgressManager` and the animated sprite (`ImageSequenceScene` / `altSequenceScene`).

### How the gameplay page maps to your ProgressManager

It only reads `CurrentStep`, `CompletedStitches`, `TryGetActivePhase` and `OnStitchComplete`. It never changes the state machine.

| ProgressManager step | Stage shown | Key prompt |
|---|---|---|
| Idle | 1 · Ready yarn | **Hold A** (keycap shown pressed) |
| ReadyingYarn | 2 · Needle under | **Press Y** (blinks) |
| MovedToA | 3 · Needle over | **Press I** (blinks). Bar shows the Y→I timing window |
| MovedToB | 4 · Pull through & tighten | **A, "Get ready"** (60%, early warning) |
| Tightening | 4 · Pull through & tighten | **Let go of A** (blinks). Bar shows the tighten window |

Other behaviour:

- **Stitch counter:** the 25-box counter fills from `CompletedStitches`.
- **Messages:** the chat bubbles start after 3 stitches and stay on screen.
- **Lesson complete:** at 25 stitches the "Cast on!" dialog appears and `ProgressManager` is paused. "Cast on again" resets the count.

> The design canvas used **hold S** for the last stage. Your `ProgressManager` finishes on **releasing A**, so this kit follows your code. If you change the input later, update the `Stages` table at the top of `HtkGameplayPage.cs`.

## Building your own layouts

- **Sprites:** `HtkTheme.UI("card")`, `HtkTheme.Icon("icon_lock")`, `HtkTheme.Art("title_card_caston")`.
- **Colours and fonts:** `HtkTheme.Ink`, `HtkTheme.Yarn`, … and `HtkTheme.Pixel`, `HtkTheme.PixelBold`, `HtkTheme.Mono`.
- **Building blocks:** `HtkUI` (top-left positioning in 1920×1080 pixels, rounded masked cards, soft shadows, graph paper).

Using a sprite by hand in an Image:

- **Image Type:** Sliced, or Tiled for the `*_dashed` ones.
- **Pixels Per Unit Multiplier:** 1.
- **Icons:** they're white, so set the Image colour to `#1a1c22`.
- **Colour variants:** `tick_filled`, `progress_fill` and `card_outline_selected` are drawn in yarn blue. For another accent, make a recoloured copy. Tinting a sprite only darkens it.

## Text

The scripts use legacy `UnityEngine.UI.Text` with the bundled TTFs, matching your existing UI code. If you move to TextMeshPro, create font assets from the same TTFs (**Window › TextMeshPro › Font Asset Creator**). The type sizes are in `Spec/ui-spec.md`.

## Licences

Silkscreen (Jason Kottke) and DM Mono (Colophon Foundry) are under the SIL Open Font License 1.1. That allows bundling them in a game. The licence texts are next to the fonts. The drawings and dog silhouette are your own art.
