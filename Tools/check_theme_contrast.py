#!/usr/bin/env python3
"""
check_theme_contrast.py — WCAG AAA contrast auditor/fixer for the suite's
Themes/*.xaml files (every app shares the brush-key contract documented in
../../Themes_Reference.md).

Standalone by design — not part of any shipped app, pure stdlib.

Rules (2026-09-25, WCAG 2.2 AAA remediation):
  BrushText, BrushMuted            >= 7.0:1 on Bg, Surf, Surf2, Bar
  BrushHint                        >= 7.0:1 on Bg, Surf         (real text: placeholders, labels)
  BrushDivider (new)               >= 3.0:1 on Bg, Surf         (lines/borders only — never text)
  FocusRingBrush                   >= 7.0:1 on Bg, Surf; >= 3.0:1 on Surf2
  BrushRedText/BlueText/GreenText/AmberText (new)
                                   >= 7.0:1 on Bg, Surf         (accent-coloured TEXT)
  BrushOnAccent (new)              >= 7.0:1 on BrushBlue AND BrushRed (text/glyphs on accent fills)
  BrushBgForChecked                >= 3.0:1 on BrushBlue        (CheckBox tick, non-text)

Themes whose colours are x:Static SystemColors (HighContrast*) are system-managed:
they are exempt from ratio checks but must still define every key.

Usage:
  python3 check_theme_contrast.py                        # report ./Themes, exit 1 on failures
  python3 check_theme_contrast.py --dir PATH             # report another Themes/ folder
  python3 check_theme_contrast.py --app-dir APPROOT      # APPROOT/Themes + missing-key check:
        every resource key the app references (DynamicResource/StaticResource in XAML,
        FindResource/TryFindResource/SetResourceReference/Resources["..."] in C#) that is
        not defined outside Themes/ must exist in EVERY theme file.
  Themes/_*.xaml files are style modules (BadgeOverlay): excluded from theme checks,
  included in the key scan.
  --fix  rewrite failing brushes in place and generate missing contract keys
         (BrushDivider, *Text, BrushOnAccent; BrushOrange when the app uses it).

Fix strategy: HSL-lightness shift of the foreground (hue/saturation kept),
trying both directions and keeping the smallest change that passes. When no
foreground can pass (background too mid-tone), the offending background
(never BrushBg) is shifted away from the foreground instead — e.g. a hover
Surf2 moves toward Bg. Passes repeat until stable. Mechanical last resort:
the printed deltas should be eyeballed on C64, Palenight, Abyss,
RosePineDawn, EverforestLight.
"""
import argparse
import colorsys
import glob
import os
import re
import sys

HEX_RE = r"#([0-9A-Fa-f]{6})"
BG4 = ("BrushBg", "BrushSurf", "BrushSurf2", "BrushBar")
BG2 = ("BrushBg", "BrushSurf")

# (fg_key, min_ratio, bg_keys, may_shift_bg)
RULES = [
    ("BrushText",         7.0, BG4, True),
    ("BrushMuted",        7.0, BG4, True),
    ("BrushHint",         7.0, BG2, True),
    ("BrushDivider",      3.0, BG2, False),
    ("FocusRingBrush",    7.0, BG2, True),
    ("FocusRingBrush",    3.0, ("BrushSurf2",), True),
    ("BrushRedText",      7.0, BG2, True),
    ("BrushBlueText",     7.0, BG2, True),
    ("BrushGreenText",    7.0, BG2, True),
    ("BrushAmberText",    7.0, BG2, True),
    ("BrushOnAccent",     7.0, ("BrushBlue", "BrushRed"), True),
    ("BrushBgForChecked", 3.0, ("BrushBlue",), False),
    # FileCopy (2026-10-13): app-referenced extras, checked only where defined.
    ("GoldFocusBrush",    7.0, BG2, True),
    ("GoldFocusBrush",    3.0, ("BrushSurf2",), True),
    ("BrushSelectedText", 7.0, ("BrushBlue", "BrushRed"), True),
]

# Keys every theme must define. Generated keys: key -> seed key (value copied
# before any fixing, then the rules move it). BrushOnAccent is seeded to #000/#FFF.
CONTRACT = ["BrushBg", "BrushSurf", "BrushSurf2", "BrushText", "BrushMuted", "BrushHint",
            "BrushDivider", "BrushBlue", "BrushRed", "BrushGreen", "BrushAmber",
            "BrushRedText", "BrushBlueText", "BrushGreenText", "BrushAmberText",
            "BrushOnAccent", "BrushBar", "BrushDisabled", "FocusRingBrush"]
GENERATED = {
    "BrushDivider":   "BrushHint",
    "BrushRedText":   "BrushRed",
    "BrushBlueText":  "BrushBlue",
    "BrushGreenText": "BrushGreen",
    "BrushAmberText": "BrushAmber",
    "BrushOnAccent":  None,
    "BrushOrange":    "BrushAmber",   # only generated when the app references it
    "GoldFocusBrush": "FocusRingBrush",   # FileCopy focus ring alias — app-referenced only
    "BrushSelectedText": None,            # #000/#FFF best on Blue+Red — app-referenced only
}
# SystemColors equivalents for system-managed (HighContrast) themes.
SYSTEM_SEED = {
    "BrushDivider":   "GrayTextColor",
    "BrushRedText":   "WindowTextColor",
    "BrushBlueText":  "WindowTextColor",
    "BrushGreenText": "WindowTextColor",
    "BrushAmberText": "WindowTextColor",
    "BrushOnAccent":  "HighlightTextColor",
    "BrushOrange":    "HighlightColor",
    "GoldFocusBrush": "HighlightColor",
    "BrushSelectedText": "HighlightTextColor",
}


def hex_to_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def rgb_to_hex(rgb):
    r, g, b = (max(0, min(255, round(c))) for c in rgb)
    return f"#{r:02X}{g:02X}{b:02X}"


def _srgb_channel(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4


def relative_luminance(rgb):
    r, g, b = rgb
    return 0.2126 * _srgb_channel(r) + 0.7152 * _srgb_channel(g) + 0.0722 * _srgb_channel(b)


def contrast_ratio(a, b):
    if isinstance(a, str):
        a = hex_to_rgb(a)
    if isinstance(b, str):
        b = hex_to_rgb(b)
    l1 = relative_luminance(a) + 0.05
    l2 = relative_luminance(b) + 0.05
    return max(l1, l2) / min(l1, l2)


def parse_theme(path):
    with open(path, encoding="utf-8-sig", newline="") as fh:
        text = fh.read()
    brushes, keys = {}, set()
    for m in re.finditer(r'x:Key="(\w+)"\s+Color="([^"]+)"', text):
        keys.add(m.group(1))
        hm = re.fullmatch(HEX_RE, m.group(2))
        if hm:
            brushes[m.group(1)] = "#" + hm.group(1).upper()
    system = "SystemColors." in text
    return text, brushes, keys, system


def _shift(hex_, target_l):
    r, g, b = hex_to_rgb(hex_)
    h, _, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
    rr, gg, bb = colorsys.hls_to_rgb(h, target_l, s)
    return rgb_to_hex((rr * 255, gg * 255, bb * 255))


def _lightness(hex_):
    r, g, b = hex_to_rgb(hex_)
    return colorsys.rgb_to_hls(r / 255, g / 255, b / 255)[1]


def shift_to_pass(hex_, others, target):
    """Smallest HSL-lightness move of hex_ (either direction) so that
    contrast(hex_, o) >= target for every o in others. Returns (hex, passed)."""
    l0 = _lightness(hex_)
    best = None
    for end in (1.0, 0.0):
        if all(contrast_ratio(_shift(hex_, end), o) >= target for o in others):
            lo, hi = l0, end          # lo = failing side, hi = passing side
            cand = _shift(hex_, end)
            for _ in range(40):
                mid = (lo + hi) / 2
                c = _shift(hex_, mid)
                if all(contrast_ratio(c, o) >= target for o in others):
                    hi, cand = mid, c
                else:
                    lo = mid
            d = abs(_lightness(cand) - l0)
            if best is None or d < best[1]:
                best = (cand, d)
    if best:
        return best[0], True
    # No lightness passes against all: take the extreme that maximises the worst ratio.
    ends = [_shift(hex_, e) for e in (1.0, 0.0)]
    return max(ends, key=lambda c: min(contrast_ratio(c, o) for o in others)), False


def evaluate(brushes, system):
    """Return list of (fg, target, worst_bg, ratio) failures for hex themes."""
    fails = []
    if system:
        return fails
    for fg, target, bgs, _ in RULES:
        if fg not in brushes:
            continue
        for bg in bgs:
            if bg in brushes:
                r = contrast_ratio(brushes[fg], brushes[bg])
                if r < target - 1e-9:
                    fails.append((fg, target, bg, r))
    return fails


def set_brush(text, key, value):
    pat = re.compile(r'(x:Key="' + re.escape(key) + r'"\s+Color=")[^"]+(")')
    text, n = pat.subn(lambda m: m.group(1) + value + m.group(2), text, count=1)
    return text, n == 1


def add_keys(text, entries):
    """Insert <SolidColorBrush> lines before </ResourceDictionary>."""
    if not entries:
        return text
    width = max(len(k) for k, _ in entries) + 3
    lines = ["  <!-- WCAG AAA text tokens (check_theme_contrast.py, 2026-09-25):"
             " accent fills stay BrushRed/Blue/Green/Amber; text uses *Text; text on fills uses BrushOnAccent -->"]
    for k, v in entries:
        lines.append(f'  <SolidColorBrush x:Key="{k}"'.ljust(26 + width) + f' Color="{v}"/>')
    block = "\n".join(lines) + "\n"
    idx = text.rfind("</ResourceDictionary>")
    return text[:idx] + block + text[idx:]


def process(path, do_fix, required_extra):
    text, brushes, keys, system = parse_theme(path)
    name = os.path.basename(path)
    missing = [k for k in CONTRACT + sorted(required_extra) if k not in keys]
    before = evaluate(brushes, system)
    log = []
    if not do_fix:
        return missing, before, log

    # 1. Generate missing keys (seeded before any fixing).
    new_entries = []
    for k in missing:
        if k not in GENERATED:
            continue
        if system:
            new_entries.append((k, "{x:Static SystemColors." + SYSTEM_SEED[k] + "}"))
            continue
        seed = GENERATED[k]
        if k in ("BrushOnAccent", "BrushSelectedText"):
            fills = [brushes[f] for f in ("BrushBlue", "BrushRed") if f in brushes]
            val = max(("#000000", "#FFFFFF"),
                      key=lambda c: min(contrast_ratio(c, f) for f in fills) if fills else 0)
        elif k == "BrushOrange":
            r, g, b = hex_to_rgb(brushes[seed])
            h, l, s = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
            rr, gg, bb = colorsys.hls_to_rgb(25 / 360, l, max(s, 0.6))
            val = rgb_to_hex((rr * 255, gg * 255, bb * 255))
        else:
            val = brushes[seed]
        brushes[k] = val
        new_entries.append((k, val))
        log.append(f"  + {k} = {val}")
    text = add_keys(text, new_entries)

    # 2. Iterate rules until stable.
    for _ in range(8):
        changed = False
        for fg, target, bgs, may_shift_bg in RULES:
            if system or fg not in brushes:
                continue
            present = [b for b in bgs if b in brushes]
            if all(contrast_ratio(brushes[fg], brushes[b]) >= target for b in present):
                continue
            others = [brushes[b] for b in present]
            new, ok = shift_to_pass(brushes[fg], others, target)
            if new != brushes[fg]:
                log.append(f"  {fg} {brushes[fg]} -> {new}")
                brushes[fg] = new
                text, _ = set_brush(text, fg, new)
                changed = True
            if not ok and may_shift_bg:
                for b in present:
                    if b == "BrushBg" or contrast_ratio(brushes[fg], brushes[b]) >= target:
                        continue
                    nb, _ = shift_to_pass(brushes[b], [brushes[fg]], target)
                    log.append(f"  {b} {brushes[b]} -> {nb} (bg moved for {fg})")
                    brushes[b] = nb
                    text, _ = set_brush(text, b, nb)
                    changed = True
        if not changed:
            break

    with open(path, "w", encoding="utf-8", newline="") as fh:
        fh.write(text)
    if log:
        log.insert(0, f"[FIXED] {name}")
    _, brushes2, keys2, _ = parse_theme(path)
    missing2 = [k for k in CONTRACT + sorted(required_extra) if k not in keys2]
    return missing2, evaluate(brushes2, system), log


# ---------------- app-dir key scan ----------------
XAML_REF = re.compile(r'\{(?:Dynamic|Static)Resource\s+(?:ResourceKey=)?([A-Za-z_]\w*)\s*\}')
CS_REF = re.compile(r'(?:FindResource|TryFindResource|SetResourceReference\([^,]+,)\s*\(?\s*"(\w+)"'
                    r'|Resources\[\s*"(\w+)"\s*\]')
DEF = re.compile(r'x:Key="(\w+)"')
SKIP = re.compile(r'[\\/](bin|obj|Builds|Themes)[\\/]')


def is_style_module(path):
    """Themes/_*.xaml (e.g. BadgeOverlay _Buttons/_Inputs/_Indicators) are style modules,
    not themes: never ratio-checked or fixed, but scanned for key definitions/uses."""
    return os.path.basename(path).startswith("_")


def scan_app(app_dir):
    used, defined = {}, set()
    for p in glob.glob(os.path.join(app_dir, "**", "*.xaml"), recursive=True) + \
             glob.glob(os.path.join(app_dir, "**", "*.cs"), recursive=True):
        rel = os.path.relpath(p, app_dir)
        if SKIP.search(os.sep + rel) and not is_style_module(p):
            continue
        t = open(p, encoding="utf-8-sig", errors="ignore").read()
        t_nc = re.sub(r"<!--.*?-->", "", t, flags=re.S) if p.endswith(".xaml") else t
        if p.endswith(".xaml"):
            defined.update(DEF.findall(t_nc))
            for k in XAML_REF.findall(t_nc):
                used.setdefault(k, rel)
        else:
            for a, b in CS_REF.findall(t):
                used.setdefault(a or b, rel)
    # Contract/generated brush keys always come from the theme, even if a fixed palette outside
    # Themes/ (e.g. NoteStation Theming/OverlayPalette.xaml, 2026-09-25) also defines them.
    return {k: v for k, v in used.items()
            if k not in defined or k in CONTRACT or k in GENERATED}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fix", action="store_true")
    ap.add_argument("--dir", default=None, help="Themes/ directory (default ./Themes)")
    ap.add_argument("--app-dir", default=None, help="app root: checks APP/Themes + missing keys")
    ap.add_argument("--themes-dir", default=None,
                    help="theme folder for --app-dir (default: APP/Themes if it holds themes, else <suite>/Shared/Themes)")
    args = ap.parse_args()

    required_extra = set()
    if args.app_dir:
        themes_dir = args.themes_dir or os.path.join(args.app_dir, "Themes")
        # 2026-10-13: apps link the canonical suite themes from Shared/Themes instead of keeping copies.
        if not args.themes_dir and not [f for f in glob.glob(os.path.join(themes_dir, "*.xaml")) if not is_style_module(f)]:
            themes_dir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Shared", "Themes")
        used = scan_app(args.app_dir)
        required_extra = set(used) - set(CONTRACT)
        print(f"App references {len(used)} theme-level key(s): {', '.join(sorted(used))}")
    else:
        themes_dir = args.dir or os.path.join(os.getcwd(), "Themes")

    files = sorted(f for f in glob.glob(os.path.join(themes_dir, "*.xaml")) if not is_style_module(f))
    if not files:
        print(f"No .xaml files found in {themes_dir}", file=sys.stderr)
        return 2

    n_miss = n_fail = n_fixed = 0
    for path in files:
        missing, fails, log = process(path, args.fix, required_extra)
        name = os.path.basename(path)
        if log:
            n_fixed += 1
            if args.fix:
                print("\n".join(log))
        for k in missing:
            print(f"[MISSING] {name}: {k}")
        for fg, target, bg, r in fails:
            print(f"[FAIL] {name}: {fg} on {bg} = {r:.2f}:1 (need {target}:1)")
        n_miss += len(missing)
        n_fail += len(fails)

    print()
    if args.fix:
        print(f"{n_fixed} file(s) rewritten.")
    print(f"{n_fail} contrast failure(s), {n_miss} missing key(s) across {len(files)} theme file(s).")
    return 0 if (n_fail + n_miss) == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
