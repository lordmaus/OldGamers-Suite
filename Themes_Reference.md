# Shared Theme System — Reference

> **2026-10-13:** themes now live once in `Shared/Themes/` (108 files, superset of every app's keys,
> NoteStation/TimerStudio palette family) and are linked into each app. Edit them there only.


All apps in this suite share the same ResourceDictionary key names.
Copy any `.xaml` file from `TimerStudio/Themes/` into another app's `Themes/` folder,
wire it into `App.xaml`, and it works with zero code changes.

---

## Brush key contract

Every theme file must define exactly these keys:

| Key | Role |
|-----|------|
| `BrushBg` | Window / page background |
| `BrushSurf` | Card / panel surface (one step lighter/darker than Bg) |
| `BrushSurf2` | Raised surface, hover/selected rows, secondary panels |
| `BrushText` | Primary body text |
| `BrushMuted` | Secondary text, labels |
| `BrushHint` | Placeholder / hint **text** (real text — AAA) |
| `BrushDivider` | Lines, separators, borders — **never text** (added 2026-09-25) |
| `BrushBlue` | Informational accent — **fills, strokes, icons only** |
| `BrushRed` | Danger / alert accent — fills, strokes, icons only |
| `BrushGreen` | Success / running accent — fills, strokes, icons only |
| `BrushAmber` | Warning / paused accent — fills, strokes, icons only |
| `BrushBlueText` | Link / info **text** (added 2026-09-25) |
| `BrushRedText` | Error / validation **text** (added 2026-09-25) |
| `BrushGreenText` | Success status **text** (added 2026-09-25) |
| `BrushAmberText` | Warning status **text** (added 2026-09-25) |
| `BrushOnAccent` | Text / glyphs drawn **on** a BrushBlue or BrushRed fill (added 2026-09-25) |
| `BrushBar` | Top/bottom toolbar background |
| `BrushDisabled` | Disabled controls and text (WCAG-exempt) |
| `FocusRingBrush` | 2px keyboard focus ring |

Optional keys (defined by most themes; required in any app that references them —
see `--app-dir` below): `BrushOrange`, `BrushYellow`, `BrushPurple`, `BrushPink`,
`BrushTeal`, `BrushBgForChecked` (CheckBox tick on a BrushBlue fill).

System-managed themes (`HighContrast*.xaml`, values are `{x:Static SystemColors.*}`)
must define every key but are exempt from ratio checks: `*Text` → `WindowTextColor`,
`BrushOnAccent` → `HighlightTextColor`, `BrushDivider` → `GrayTextColor`.

---

## WCAG AAA rules (2026-09-25)

| Foreground | Min | Against |
|---|---|---|
| `BrushText`, `BrushMuted` | 7:1 | `BrushBg`, `BrushSurf`, `BrushSurf2`, `BrushBar` |
| `BrushHint` | 7:1 | `BrushBg`, `BrushSurf` |
| `BrushDivider` | 3:1 | `BrushBg`, `BrushSurf` |
| `FocusRingBrush` | 7:1 | `BrushBg`, `BrushSurf` |
| `FocusRingBrush` | 3:1 | `BrushSurf2` |
| `BrushRedText` / `BrushBlueText` / `BrushGreenText` / `BrushAmberText` | 7:1 | `BrushBg`, `BrushSurf` |
| `BrushOnAccent` | 7:1 | `BrushBlue` **and** `BrushRed` |
| `BrushBgForChecked` (if defined) | 3:1 | `BrushBlue` |

Usage rules for XAML:
- Never set an accent (`BrushBlue/Red/Green/Amber/Orange`) as a text `Foreground` —
  use the matching `*Text` key (`BrushOrange` text → `BrushAmberText`).
- Text/glyphs on an accent fill (primary/danger buttons, selected chips) use `BrushOnAccent`.
- `BrushHint` is text; use `BrushDivider` for lines and borders.
- Hover/selected rows (Surf2) must also carry a non-colour cue (accent bar, border or ✓),
  so the state is not colour-only.

History: 2026-09-07 tightened FocusRing (Bg+Surf) and Muted to 7:1 and Hint to 4.5:1
(RosePineDawn ring 2.79:1, C64 Muted 1.65:1). 2026-09-25 raised Hint to 7:1 (4.5:1 is AA),
added Surf2/Bar backgrounds, the `*Text` / `BrushOnAccent` / `BrushDivider` tokens.

### Checker — `Tools/check_theme_contrast.py`

```
python3 Tools/check_theme_contrast.py --app-dir <App>          # report; exit 1 on any failure
python3 Tools/check_theme_contrast.py --app-dir <App> --fix    # generate missing keys + re-derive failing brushes
python3 Tools/check_theme_contrast.py --dir <App>/Themes       # contrast + contract keys only
```

- `--app-dir` also fails when any key the app references (`DynamicResource` /
  `StaticResource` in XAML, `FindResource` / `TryFindResource` / `SetResourceReference` /
  `Resources["…"]` in C#) is not defined outside `Themes/` and is missing from any theme.
- `--fix` seeds new keys (`BrushDivider` ← old Hint, `*Text` ← accent, `BrushOnAccent` ←
  #000/#FFF, `BrushOrange` ← Amber hue 25°) then HSL-lightness-shifts failing foregrounds
  (smallest move, either direction). If no foreground can pass, it moves the offending
  background (never `BrushBg`) — e.g. a mid-tone accent fill is darkened/lightened so
  `BrushOnAccent` clears 7:1.
- Spot-check after `--fix`: C64, Palenight, Abyss, RosePineDawn, EverforestLight.
- Contract keys (and generated keys) are always required from `Themes/`, even when a fixed
  palette outside `Themes/` also defines them (2026-09-25).
- **Fixed-dark palettes** (NoteStation `Theming/OverlayPalette.xaml`, 2026-09-25): windows that paint
  their own dark chrome regardless of theme (clip/task overlays, Quick Capture, Focus Mode) merge a
  full-contract palette into `Window.Resources`, so shared styles' `DynamicResource` brushes resolve
  against the dark surface. Validate it by copying it alone into a temp folder and running
  `--dir <temp>` (must report 0 failures).
- **Style modules** (BadgeOverlay `Themes/_Buttons.xaml`, `_Inputs.xaml`, `_Indicators.xaml`,
  2026-09-25): any `Themes/_*.xaml` is treated as a style dictionary, not a theme — skipped by the
  ratio/missing-key pass, but scanned for key definitions and references like any other XAML.
- **Fixed-colour chrome without a palette** (BadgeOverlay InfoOverlay title bar, 2026-09-25): a local
  style with literal colours (text `#F2FFFFFF`, ring `#FFD700`, hover white outline) is acceptable for
  a handful of controls on a fixed dark surface; use a palette once shared styles are involved.
- **NoteStation literal audit (2026-10-12, NS-09)**: hover/pressed/selected in `App.xaml` now use
  `BrushSurf2` + `BrushText`/`BrushBlueText` borders (no alpha-white tints — invisible on light themes).
  CommandPaletteWindow joined `OverlayPalette`. Hide-note overlay, sticky undo toast and move grip use
  theme tokens; word count + resize glyph follow the note's text colour. Literals deliberately kept:
  OverlayPalette-scoped windows (QuickCapture, FocusMode, Clip/Task overlays), ScreenCaptureOverlay
  dimming, sticky-note colour swatches / title-bar / footer tint / checkmark halo (drawn on the user's
  own note colour), StickyNoteFormatDialog default hex values (data), NotesSidebar card edge on user
  colour, `PriorityToColorConverter` badge fills (black glyph ≥ 7:1 by design), FormatToolbarHost.

## Light-theme flag

In `App.xaml.cs`, add the theme name to `_lightThemes` if `BrushBg` is light-valued.
This controls `App.IsDark`, which is checked by any control that needs to
swap an icon or border color based on mode.

---

## Original 12 themes (Timer Studio v2026.05.11.011 — historical values, superseded by Themes/*.xaml)

### Dark themes

#### Dark (default)
```
BrushBg       #1C1C1C    BrushSurf     #2A2A2A    BrushSurf2    #333333
BrushText     #E0E0E0    BrushMuted    #AAAAAA    BrushHint     #666666
BrushBlue     #6BCAFA    BrushRed      #F28B82    BrushGreen    #81C995
BrushAmber    #FFD700    BrushBar      #141414    BrushDisabled #555555
FocusRing     #FFD700
```

#### One Dark
> Source: https://github.com/atom/one-dark-syntax
```
BrushBg       #282C34    BrushSurf     #353B45    BrushSurf2    #3E4451
BrushText     #ABB2BF    BrushMuted    #9DA5B4    BrushHint     #636D83
BrushBlue     #61AFEF    BrushRed      #E06C75    BrushGreen    #98C379
BrushAmber    #E5C07B    BrushBar      #21252B    BrushDisabled #636D83
FocusRing     #E5C07B
```

#### Gruvbox Dark
> Source: https://github.com/morhetz/gruvbox
```
BrushBg       #282828    BrushSurf     #3C3836    BrushSurf2    #504945
BrushText     #EBDBB2    BrushMuted    #D5C4A1    BrushHint     #928374
BrushBlue     #83A598    BrushRed      #FB4934    BrushGreen    #B8BB26
BrushAmber    #FABD2F    BrushBar      #1D2021    BrushDisabled #928374
FocusRing     #FABD2F
```

#### Tokyo Night
> Source: https://github.com/enkia/tokyo-night-vscode-theme
```
BrushBg       #1A1B26    BrushSurf     #24283B    BrushSurf2    #2F3549
BrushText     #C0CAF5    BrushMuted    #A9B1D6    BrushHint     #565F89
BrushBlue     #7AA2F7    BrushRed      #F7768E    BrushGreen    #9ECE6A
BrushAmber    #E0AF68    BrushBar      #16161E    BrushDisabled #565F89
FocusRing     #E0AF68
```

#### Nord
> Source: https://www.nordtheme.com
```
BrushBg       #2E3440    BrushSurf     #3B4252    BrushSurf2    #434C5E
BrushText     #ECEFF4    BrushMuted    #D8DEE9    BrushHint     #81A1C1
BrushBlue     #88C0D0    BrushRed      #BF616A    BrushGreen    #A3BE8C
BrushAmber    #EBCB8B    BrushBar      #242933    BrushDisabled #616E88
FocusRing     #EBCB8B
```

#### Dracula
> Source: https://draculatheme.com
```
BrushBg       #282A36    BrushSurf     #383A47    BrushSurf2    #44475A
BrushText     #F8F8F2    BrushMuted    #BFBFBF    BrushHint     #6272A4
BrushBlue     #8BE9FD    BrushRed      #FF5555    BrushGreen    #50FA7B
BrushAmber    #F1FA8C    BrushBar      #21222C    BrushDisabled #6272A4
FocusRing     #FFD700
```

#### Mocha (Catppuccin)
> Source: https://github.com/catppuccin/catppuccin
```
BrushBg       #1E1E2E    BrushSurf     #313244    BrushSurf2    #45475A
BrushText     #CDD6F4    BrushMuted    #BAC2DE    BrushHint     #7F849C
BrushBlue     #89B4FA    BrushRed      #F38BA8    BrushGreen    #A6E3A1
BrushAmber    #F9E2AF    BrushBar      #181825    BrushDisabled #6C7086
FocusRing     #FFD700
```

#### Rosé Pine
> Source: https://rosepinetheme.com
```
BrushBg       #191724    BrushSurf     #1F1D2E    BrushSurf2    #26233A
BrushText     #E0DEF4    BrushMuted    #D4D0E3    BrushHint     #6E6A86
BrushBlue     #9CCFD8    BrushRed      #EB6F92    BrushGreen    #31748F
BrushAmber    #F6C177    BrushBar      #13111C    BrushDisabled #6E6A86
FocusRing     #F6C177
```

---

### Light themes

#### Light (default)
```
BrushBg       #F5F5F5    BrushSurf     #E8E8E8    BrushSurf2    #D8D8D8
BrushText     #1C1C1C    BrushMuted    #4A4A4A    BrushHint     #777777
BrushBlue     #1565C0    BrushRed      #B71C1C    BrushGreen    #2E7D32
BrushAmber    #E65100    BrushBar      #DCDCDC    BrushDisabled #9E9E9E
FocusRing     #0040CC
```

#### Warm Light (Solarized-inspired)
```
BrushBg       #FDF6E3    BrushSurf     #EEE8D5    BrushSurf2    #D6D0BB
BrushText     #2C2B2A    BrushMuted    #4A4845    BrushHint     #657B83
BrushBlue     #0A5FA8    BrushRed      #B5201E    BrushGreen    #1A7A72
BrushAmber    #7A5C00    BrushBar      #E8E2D0    BrushDisabled #888880
FocusRing     #0040CC
```

#### Catppuccin Latte
> Source: https://github.com/catppuccin/catppuccin
> Accent colors darkened vs. official values to meet AAA on #EFF1F5.
```
BrushBg       #EFF1F5    BrushSurf     #E6E9EF    BrushSurf2    #DCE0E8
BrushText     #4C4F69    BrushMuted    #5C5F77    BrushHint     #7C7F93
BrushBlue     #1E4FCC    BrushRed      #A30D2E    BrushGreen    #1E6E3B
BrushAmber    #7A4F00    BrushBar      #E6E9EF    BrushDisabled #ACB0BE
FocusRing     #1E4FCC
```

#### GitHub Light
> Source: https://github.com/primer/primitives
> Accent colors darkened vs. GitHub web values to meet AAA on #FFFFFF.
```
BrushBg       #FFFFFF    BrushSurf     #F6F8FA    BrushSurf2    #EAEEF2
BrushText     #1F2328    BrushMuted    #4A525A    BrushHint     #6E7781
BrushBlue     #0550AE    BrushRed      #A40E26    BrushGreen    #116329
BrushAmber    #7D4E00    BrushBar      #F6F8FA    BrushDisabled #6E7781
FocusRing     #0550AE
```

---

## Adding a theme to another app (checklist)

1. Copy `Themes/<Name>.xaml` into the target app's `Themes/` folder.
2. In `App.xaml`, add a `<ResourceDictionary Source="Themes/<Name>.xaml"/>` entry
   (or swap it in at runtime via the same `ApplyThemeUri` pattern).
3. In the equivalent of `App.xaml.cs`:
   - Add the name to `ThemeNames`.
   - If it is a light theme, add the name to `_lightThemes`.
4. Add a menu entry with a display label in the theme picker.
5. Build and verify contrast — open the app, press Tab through every control,
   and confirm the gold/blue focus ring is visible.

---

*Last updated: 2026-09-25 — WCAG 2.2 AAA contract (TimerStudio remediation). Per-theme hex tables above predate the 2026-09 re-derivation; the `Themes/*.xaml` files are authoritative.*
