#!/usr/bin/env python3
"""
Generate manual.pdf for File Copy Studio v2026.05.09.0001
Uses reportlab Platypus for multi-page layout.
"""

from reportlab.lib.pagesizes import letter
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import inch
from reportlab.lib import colors
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle,
    HRFlowable, PageBreak, KeepTogether
)
from reportlab.platypus.tableofcontents import TableOfContents
from reportlab.lib.enums import TA_LEFT, TA_CENTER, TA_RIGHT
from datetime import date
import os

OUT_PATH = "/sessions/inspiring-exciting-turing/mnt/Utility Apps/FileCopy/manual.pdf"

# ── Colour palette ─────────────────────────────────────────────────────────────
C_BLUE      = colors.HexColor("#0050A0")
C_DARKGRAY  = colors.HexColor("#333333")
C_MIDGRAY   = colors.HexColor("#555555")
C_LIGHTGRAY = colors.HexColor("#F0F0F0")
C_GOLD      = colors.HexColor("#B8860B")
C_BLACK     = colors.black
C_WHITE     = colors.white
C_RULE      = colors.HexColor("#CCCCCC")

# ── Styles ─────────────────────────────────────────────────────────────────────
base = getSampleStyleSheet()

def s(name, **kw):
    ps = ParagraphStyle(name, **kw)
    return ps

TITLE   = s("ManTitle",   fontName="Helvetica-Bold",  fontSize=28, textColor=C_BLUE,
             spaceAfter=6, leading=34, alignment=TA_CENTER)
SUBTITLE= s("ManSub",     fontName="Helvetica",       fontSize=13, textColor=C_MIDGRAY,
             spaceAfter=4, alignment=TA_CENTER)
BYLINE  = s("ManByline",  fontName="Helvetica",       fontSize=10, textColor=C_MIDGRAY,
             spaceAfter=2, alignment=TA_CENTER)

H1      = s("ManH1",      fontName="Helvetica-Bold",  fontSize=16, textColor=C_BLUE,
             spaceBefore=18, spaceAfter=6, leading=20)
H2      = s("ManH2",      fontName="Helvetica-Bold",  fontSize=12, textColor=C_DARKGRAY,
             spaceBefore=12, spaceAfter=4, leading=15)
H3      = s("ManH3",      fontName="Helvetica-BoldOblique", fontSize=10, textColor=C_MIDGRAY,
             spaceBefore=8, spaceAfter=3, leading=13)

BODY    = s("ManBody",    fontName="Helvetica",       fontSize=10, textColor=C_DARKGRAY,
             spaceBefore=2, spaceAfter=4, leading=14)
BULLET  = s("ManBullet",  fontName="Helvetica",       fontSize=10, textColor=C_DARKGRAY,
             spaceBefore=1, spaceAfter=2, leading=14, leftIndent=16, bulletIndent=4)
CODE    = s("ManCode",    fontName="Courier",         fontSize=8.5, textColor=C_DARKGRAY,
             spaceBefore=4, spaceAfter=4, leading=12, backColor=C_LIGHTGRAY,
             leftIndent=12, rightIndent=12)
NOTE    = s("ManNote",    fontName="Helvetica-Oblique", fontSize=9, textColor=C_MIDGRAY,
             spaceBefore=2, spaceAfter=2, leading=12, leftIndent=12)
FOOTER  = s("ManFooter",  fontName="Helvetica",       fontSize=8, textColor=C_MIDGRAY,
             alignment=TA_CENTER)
TH      = s("ManTH",      fontName="Helvetica-Bold",  fontSize=9, textColor=C_WHITE,
             alignment=TA_LEFT)
TD      = s("ManTD",      fontName="Helvetica",       fontSize=9, textColor=C_DARKGRAY,
             alignment=TA_LEFT, leading=12)

# ── Page template ──────────────────────────────────────────────────────────────
def on_page(canvas, doc):
    canvas.saveState()
    w, h = letter
    # header rule
    canvas.setStrokeColor(C_RULE)
    canvas.setLineWidth(0.5)
    canvas.line(0.75*inch, h - 0.55*inch, w - 0.75*inch, h - 0.55*inch)
    # header text
    canvas.setFont("Helvetica", 8)
    canvas.setFillColor(C_MIDGRAY)
    canvas.drawString(0.75*inch, h - 0.48*inch, "File Copy Studio — User Manual")
    canvas.drawRightString(w - 0.75*inch, h - 0.48*inch, f"v2026.05.09.0001")
    # footer rule
    canvas.line(0.75*inch, 0.55*inch, w - 0.75*inch, 0.55*inch)
    # page number
    canvas.drawCentredString(w / 2, 0.38*inch, f"Page {doc.page}")
    canvas.restoreState()

def on_first_page(canvas, doc):
    pass  # cover page — no header/footer

# ── Helpers ────────────────────────────────────────────────────────────────────
def rule():
    return HRFlowable(width="100%", thickness=0.5, color=C_RULE, spaceAfter=6, spaceBefore=6)

def h1(text):  return Paragraph(text, H1)
def h2(text):  return Paragraph(text, H2)
def h3(text):  return Paragraph(text, H3)
def p(text):   return Paragraph(text, BODY)
def b(text):   return Paragraph(f"<bullet>•</bullet>{text}", BULLET)
def code(text):return Paragraph(text.replace(" ", "&nbsp;").replace("\n", "<br/>"), CODE)
def note(text):return Paragraph(f"<i>Note: {text}</i>", NOTE)
def sp(n=6):   return Spacer(1, n)

def tbl(headers, rows, col_widths=None):
    """Build a styled table. headers = list of str, rows = list of list of str."""
    data = [[Paragraph(h, TH) for h in headers]]
    for row in rows:
        data.append([Paragraph(str(c), TD) for c in row])
    t = Table(data, colWidths=col_widths, repeatRows=1)
    t.setStyle(TableStyle([
        ("BACKGROUND",  (0,0), (-1,0), C_BLUE),
        ("ROWBACKGROUNDS", (0,1), (-1,-1), [C_WHITE, C_LIGHTGRAY]),
        ("GRID",        (0,0), (-1,-1), 0.4, C_RULE),
        ("VALIGN",      (0,0), (-1,-1), "TOP"),
        ("LEFTPADDING", (0,0), (-1,-1), 6),
        ("RIGHTPADDING",(0,0), (-1,-1), 6),
        ("TOPPADDING",  (0,0), (-1,-1), 4),
        ("BOTTOMPADDING",(0,0),(-1,-1), 4),
    ]))
    return t

# ── Document build ─────────────────────────────────────────────────────────────
doc = SimpleDocTemplate(
    OUT_PATH,
    pagesize=letter,
    leftMargin=0.85*inch, rightMargin=0.85*inch,
    topMargin=0.75*inch,  bottomMargin=0.75*inch,
    title="File Copy Studio — User Manual",
    author="Matthew Pittman",
    subject="File Copy Studio v2026.05.09.0001 User Manual",
)

story = []

# ── Cover page ─────────────────────────────────────────────────────────────────
story.append(Spacer(1, 1.4*inch))
story.append(Paragraph("File Copy Studio", TITLE))
story.append(sp(4))
story.append(Paragraph("User Manual", SUBTITLE))
story.append(sp(2))
story.append(Paragraph("Version 2026.05.09.0001", BYLINE))
story.append(sp(2))
story.append(Paragraph(f"&copy; 2026 Matthew Pittman", BYLINE))
story.append(Spacer(1, 0.4*inch))
story.append(rule())
story.append(sp(8))
intro_cover = (
    "File Copy Studio is a professional folder-to-folder file transfer utility for Windows. "
    "It offers configurable conflict resolution, CRC32 integrity verification, crash-safe "
    "resume, a built-in job scheduler, and full WCAG 2.2 AAA accessibility — all in a compact "
    "self-contained executable that requires no .NET runtime on the target machine."
)
story.append(Paragraph(intro_cover, BODY))
story.append(PageBreak())

# ── Section 1: Overview ────────────────────────────────────────────────────────
story.append(h1("1  Overview"))
story.append(rule())
story.append(p(
    "File Copy Studio is designed for power users, IT professionals, and anyone who needs "
    "reliable, repeatable file transfers. Unlike a basic drag-and-drop copy, it gives you "
    "full visibility into every planned action before a single byte is written, and lets you "
    "tune every aspect of the transfer — parallelism, buffer size, bandwidth throttle, "
    "conflict strategy, and more."
))
story.append(sp())
story.append(p("Key capabilities at a glance:"))
story.append(b("Conflict resolution with six modes: Overwrite, Overwrite-if-newer, Overwrite-if-older, Skip-if-same-date, Skip-all, and Rename"))
story.append(b("CRC32 verification after every copy, or as a standalone verify-only pass"))
story.append(b("Crash-safe resume: interrupted copies restart from the last 64 MB checkpoint"))
story.append(b("Multi-threaded parallel transfers with automatic drive-affinity detection"))
story.append(b("Built-in job scheduler with cron-syntax expressions"))
story.append(b("Dry-run mode: plan and preview without touching the destination"))
story.append(b("Bandwidth throttle in Mbps to protect shared network links"))
story.append(b("Symlink policy: follow, skip, or copy as link"))
story.append(b("5 named presets for instant one-click job recall"))
story.append(b("Portable mode for USB drives — no installer, no registry"))
story.append(b("WCAG 2.2 AAA accessibility throughout"))
story.append(sp())

# ── Section 2: System Requirements ────────────────────────────────────────────
story.append(h1("2  System Requirements"))
story.append(rule())
story.append(tbl(
    ["Requirement", "Minimum", "Recommended"],
    [
        ["OS",         "Windows 10 (1809+) x64", "Windows 11 x64"],
        [".NET runtime", "None — self-contained", "None — self-contained"],
        ["RAM",        "64 MB", "256 MB+"],
        ["Disk",       "~45 MB (app)", "SSD recommended for large transfers"],
        ["Display",    "1024×600", "1920×1080 or higher"],
    ],
    col_widths=[1.6*inch, 2.1*inch, 2.8*inch],
))
story.append(sp())
story.append(note("No .NET runtime installation is required. The executable is fully self-contained."))
story.append(sp())

# ── Section 3: Installation ────────────────────────────────────────────────────
story.append(h1("3  Installation"))
story.append(rule())
story.append(h2("3.1  Standard Installation"))
story.append(p(
    "Run <b>FileCopyStudio_Setup.exe</b> and follow the on-screen prompts. "
    "The installer will place the application in <i>C:\\Program Files\\File Copy Studio\\</i>, "
    "create Start Menu and Desktop shortcuts, register the <i>.fclist</i> file type, "
    "and add shell context-menu entries to folders and drives."
))
story.append(sp())
story.append(h2("3.2  Portable Installation"))
story.append(p(
    "Extract <b>FileCopyStudio_Portable_&lt;version&gt;.zip</b> to any folder — "
    "including a USB drive. The archive contains:"
))
for line in [
    "<b>FileCopyStudio.exe</b> — the application",
    "<b>portable.flag</b> — activates portable mode on startup",
    "<b>manual.pdf</b> — this document",
    "<b>Data\\</b> — created on first run; stores prefs.json and logs",
]:
    story.append(b(line))
story.append(sp())
story.append(p(
    "In portable mode all settings and logs are written to the <i>Data\\</i> folder "
    "next to the executable — nothing is written to <i>%APPDATA%</i> or the registry. "
    "The title bar displays <b>[Portable]</b> to confirm the mode is active. "
    "Delete <i>portable.flag</i> to switch back to installed mode."
))
story.append(sp())
story.append(h2("3.3  Silent / Scripted Installation"))
story.append(code("FileCopyStudio_Setup.exe /S"))
story.append(p("Installs silently to the default directory with no UI."))
story.append(sp())
story.append(h2("3.4  Uninstallation"))
story.append(p(
    "Use <i>Add or Remove Programs</i> → <b>File Copy Studio</b>, or run "
    "<i>Uninstall.exe</i> from the installation folder. "
    "The uninstaller prompts whether to keep your saved presets and settings."
))
story.append(sp())

# ── Section 4: Interface tour ──────────────────────────────────────────────────
story.append(h1("4  Interface Tour"))
story.append(rule())
story.append(p(
    "The main window is divided into six horizontal zones. "
    "A resize grip in the bottom-right corner allows free window resizing."
))
story.append(sp())

zones = [
    ("Zone 1 — Identity / Scale",
     "Shows the application name and a UI-scale selector (Compact / Standard / Medium / Large / Extra Large). "
     "Also displays the current theme and version. The scale selector resizes all controls proportionally "
     "for high-DPI displays or accessibility needs."),
    ("Zone 2 — Navigation Row",
     "Contains all primary action buttons: Browse Source, Browse Destination, Swap Paths, Scan, "
     "Verify Only, Start, Pause/Resume, Cancel, Dry Run, Load Job, Save Job, Scheduler, and About. "
     "All buttons include keyboard shortcuts and ARIA labels."),
    ("Zone 3 — Path / Progress",
     "Source and destination path text boxes with Browse buttons. Below them, two progress bars "
     "show current-file progress and overall transfer progress, with a live speed readout "
     "(e.g. 1.2 GB/s) and a sparkline chart of recent throughput."),
    ("Zone 4 — Options / Sliders",
     "All copy settings: conflict mode drop-down, checkboxes for Subfolders / Timestamps / Attributes / "
     "Verify CRC / Move / Resume / Auto-parallel / Tray / Sound / Pause on battery, "
     "sliders for Buffer size, Parallel workers, and Throttle, "
     "and text fields for Include/Exclude masks and the Symlink policy drop-down. "
     "A summary line shows the scan result count at the bottom."),
    ("Zone 5 — File Grid",
     "A sortable, filterable grid listing all planned file actions. Columns: Status icon, "
     "Relative path, Size, Source modified, Destination modified, Action, and Error. "
     "Right-click any row for context-menu options including Discard partial and Open in Explorer. "
     "The error log below the grid shows per-file errors with timestamps; errors can be copied "
     "to clipboard or cleared."),
    ("Zone 6 — Preset Bar",
     "Five preset slots (1–5). A filled circle indicates the slot is populated. Click a slot "
     "to load its saved paths and options. Ctrl+Shift+1–5 saves the current settings to a slot. "
     "The Save Preset button opens a dialog to name the preset."),
]

for title, desc in zones:
    story.append(KeepTogether([
        h3(title),
        p(desc),
        sp(4),
    ]))

# ── Section 5: Quick Start ─────────────────────────────────────────────────────
story.append(h1("5  Quick Start"))
story.append(rule())
story.append(p("To perform a basic folder copy in four steps:"))
story.append(sp())

steps = [
    ("Set paths",
     "Click <b>Browse Source</b> (or type directly) to choose the source folder. "
     "Click <b>Browse Dest</b> to choose the destination folder. "
     "You can also drag-and-drop folders onto either path box."),
    ("Scan",
     "Click <b>Scan</b> (or press Ctrl+S). The engine enumerates both folders, "
     "compares files, and populates the file grid with the planned action for each file. "
     "Review the grid to confirm the planned actions before proceeding."),
    ("Adjust options (optional)",
     "In Zone 4, change the conflict mode, toggle Verify CRC, set parallelism, etc. "
     "After changing any option that affects planning, click Scan again to refresh the grid."),
    ("Start",
     "Click <b>Start</b> (or press F5). Progress is shown in Zone 3. "
     "Click <b>Pause</b> to suspend the transfer at any time; click it again to resume. "
     "Click <b>Cancel</b> to abort — already-copied files are kept."),
]

for i, (title, desc) in enumerate(steps, 1):
    story.append(KeepTogether([
        h3(f"Step {i}: {title}"),
        p(desc),
        sp(4),
    ]))

story.append(sp())
story.append(note(
    "If the transfer is interrupted (power loss, crash), simply re-run. "
    "The next Scan will detect .fctemp partial files and mark them for Resume "
    "instead of full re-copy."
))
story.append(sp())

# ── Section 6: Conflict Modes ──────────────────────────────────────────────────
story.append(h1("6  Conflict Resolution"))
story.append(rule())
story.append(p(
    "When a file already exists at the destination, File Copy Studio applies the configured "
    "conflict mode. The mode is set in the <b>On conflict</b> drop-down in Zone 4."
))
story.append(sp())
story.append(tbl(
    ["Mode", "Description"],
    [
        ["Overwrite",            "Always overwrite the destination file."],
        ["Overwrite if newer",   "Overwrite only if the source is newer than the destination. Default."],
        ["Overwrite if older",   "Overwrite only if the source is older than the destination."],
        ["Skip if same date",    "Skip if source and destination have the same last-modified timestamp."],
        ["Skip all",             "Never overwrite; always skip existing files."],
        ["Rename",               "Copy and rename the source file (appends _1, _2, … until unique)."],
    ],
    col_widths=[1.8*inch, 4.7*inch],
))
story.append(sp())

# ── Section 7: CRC Verification ───────────────────────────────────────────────
story.append(h1("7  CRC32 Verification"))
story.append(rule())
story.append(p(
    "File Copy Studio uses CRC32 (via System.IO.Hashing) to verify file integrity. "
    "Verification can be run in two ways:"
))
story.append(b("<b>Post-copy verify</b> — enable <i>Verify CRC</i> in Zone 4 before starting a copy. "
               "After each file is written, its CRC is computed and compared against the source CRC. "
               "Files that fail verification are marked Failed in the grid and logged."))
story.append(b("<b>Standalone verify</b> — click <i>Verify Only</i> (or press Ctrl+V) at any time. "
               "This reads every listed file from the destination and verifies it against the source "
               "without copying anything. Use this to audit a previous transfer."))
story.append(sp())
story.append(h2("7.1  CRC Sidecar Files"))
story.append(p(
    "When <b>Write CRC sidecar</b> is enabled, a <i>.crc</i> file is written alongside each "
    "destination file after a successful copy (e.g. <i>photo.jpg.crc</i>). "
    "Subsequent verify-only passes use these sidecar files to confirm integrity without "
    "needing to read the original source."
))
story.append(sp())

# ── Section 8: Resume ──────────────────────────────────────────────────────────
story.append(h1("8  Crash-Safe Resume"))
story.append(rule())
story.append(p(
    "File Copy Studio writes a pair of resume marker files during every copy:"
))
story.append(tbl(
    ["File", "Purpose"],
    [
        ["<dest>.fctemp",       "The partial destination payload — raw bytes, no embedded header."],
        ["<dest>.fctemp.meta",  "JSON sidecar: source length, source mtime ticks, partial CRC32, byte offset."],
    ],
    col_widths=[2.4*inch, 4.1*inch],
))
story.append(sp())
story.append(p(
    "Every 64 MB of written data, the meta sidecar is atomically updated (write-to-temp, rename). "
    "If the process is killed between updates, at most 64 MB of data is re-copied on resume. "
    "On the next Scan, the engine detects <i>.fctemp</i> files and marks those items "
    "<i>Resume</i> instead of <i>Copy</i>."
))
story.append(sp())
story.append(p(
    "If the source file has changed since the partial was written (different size or mtime), "
    "the partial is automatically discarded and the copy restarts from the beginning."
))
story.append(sp())
story.append(note(
    "To discard a partial manually, right-click the file in the grid and choose "
    "Discard partial. Do not delete .fctemp files in Explorer while a copy is running."
))
story.append(sp())

# ── Section 9: Parallel transfers ─────────────────────────────────────────────
story.append(h1("9  Parallel Transfers"))
story.append(rule())
story.append(p(
    "File Copy Studio can copy multiple files simultaneously using worker threads. "
    "The optimal number of workers depends on your hardware:"
))
story.append(tbl(
    ["Scenario", "Recommended setting"],
    [
        ["Source and destination on the same spinning disk", "1× (sequential — avoids head thrashing)"],
        ["Source and destination on different disks",        "2–4× (auto-detected with Auto-parallel)"],
        ["NVMe SSD source or destination",                  "4–8× (storage is not the bottleneck)"],
        ["Network destination (NAS, SMB share)",             "2–4× (depends on network bandwidth)"],
    ],
    col_widths=[2.8*inch, 3.7*inch],
))
story.append(sp())
story.append(p(
    "When <b>Auto-parallel</b> is enabled (default), the engine detects whether source and "
    "destination are on the same physical volume and sets parallelism to 1 if so, "
    "or to your configured maximum if they are on different volumes. "
    "Disable Auto-parallel to force a fixed thread count."
))
story.append(sp())

# ── Section 10: Scheduler ──────────────────────────────────────────────────────
story.append(h1("10  Job Scheduler"))
story.append(rule())
story.append(p(
    "The built-in scheduler lets you automate recurring transfers without any external "
    "task scheduler. Open it with the <b>Scheduler</b> button in Zone 2 "
    "or press Ctrl+Shift+S."
))
story.append(sp())
story.append(h2("10.1  Creating a Scheduled Job"))
story.append(p("In the Scheduler window:"))
for step in [
    "Click <b>New Job</b>.",
    "Enter a descriptive name.",
    "Set Source path and Destination path.",
    "Choose conflict mode, include/exclude masks, and any other options.",
    "Enter a schedule expression in the <i>Schedule</i> field (cron syntax — see below).",
    "Optionally set a per-job throttle in Mbps.",
    "Click <b>Save job</b>. The job appears in the list and is enabled immediately.",
]:
    story.append(b(step))
story.append(sp())
story.append(h2("10.2  Cron Schedule Syntax"))
story.append(p("Schedule expressions use standard 5-field cron syntax:"))
story.append(code(
    "MINUTE  HOUR  DAY-OF-MONTH  MONTH  DAY-OF-WEEK\n"
    "  0–59   0–23      1–31      1–12     0–7 (0/7=Sun)"
))
story.append(sp())
story.append(tbl(
    ["Expression", "Meaning"],
    [
        ["0 2 * * *",    "Every day at 02:00"],
        ["0 * * * *",    "Every hour (on the hour)"],
        ["*/15 * * * *", "Every 15 minutes"],
        ["30 18 * * 5",  "Every Friday at 18:30"],
        ["0 3 1 * *",    "First day of every month at 03:00"],
        ["0 8,17 * * 1-5","Weekdays at 08:00 and 17:00"],
    ],
    col_widths=[2.0*inch, 4.5*inch],
))
story.append(sp())
story.append(h2("10.3  Managing Jobs"))
story.append(p(
    "Jobs can be enabled or disabled individually using the toggle in the job list. "
    "Disabled jobs are retained but will not fire on schedule. "
    "To edit a job, select it in the list and modify the fields, then click Save job. "
    "To delete a job, select it and click Delete job."
))
story.append(sp())
story.append(note(
    "Scheduled jobs run inside the File Copy Studio process. "
    "The application must be running (or in the system tray) for scheduled jobs to fire. "
    "For fully unattended operation, configure Windows Task Scheduler to launch "
    "File Copy Studio at startup with the --minimized flag."
))
story.append(sp())

# ── Section 11: Presets ────────────────────────────────────────────────────────
story.append(h1("11  Presets"))
story.append(rule())
story.append(p(
    "Presets save a complete snapshot of your current settings — source path, destination path, "
    "conflict mode, include/exclude masks, all checkboxes, and buffer/parallel/throttle sliders. "
    "Up to five named presets are stored persistently in your preferences file."
))
story.append(sp())
story.append(tbl(
    ["Action", "Method"],
    [
        ["Save preset",   "Click Save Preset (Zone 6), or Ctrl+Shift+1–5"],
        ["Load preset",   "Click the slot button (Zone 6), or Ctrl+1–5"],
        ["Rename preset", "Hold Ctrl and click the slot, or use Save Preset dialog"],
        ["Clear preset",  "Load the slot then click Save Preset and leave the name blank"],
    ],
    col_widths=[1.8*inch, 4.7*inch],
))
story.append(sp())

# ── Section 12: Job Files ──────────────────────────────────────────────────────
story.append(h1("12  Job Files (.fclist)"))
story.append(rule())
story.append(p(
    "A job file saves the complete transfer configuration plus the planned file list "
    "as a JSON document with the <i>.fclist</i> extension. "
    "Job files let you define repeatable batch transfers outside the scheduler."
))
story.append(sp())
story.append(h2("12.1  Save a job"))
story.append(p(
    "After scanning, click <b>Save Job</b> in Zone 2. "
    "Choose a location and filename. The file includes paths, options, and the full planned "
    "action list — useful for auditing exactly what was (or will be) transferred."
))
story.append(sp())
story.append(h2("12.2  Load a job"))
story.append(p(
    "Click <b>Load Job</b> and select an <i>.fclist</i> file. "
    "The paths and options are restored. Click Scan to refresh the planned action list "
    "against the current state of the file system (the stored list in the file may be stale)."
))
story.append(sp())
story.append(h2("12.3  Shell integration"))
story.append(p(
    "Double-clicking an <i>.fclist</i> file in Explorer opens it in File Copy Studio. "
    "If the application is already running, the file is forwarded to the existing instance "
    "via named pipe."
))
story.append(sp())

# ── Section 13: Command Line ───────────────────────────────────────────────────
story.append(h1("13  Command-Line Interface"))
story.append(rule())
story.append(tbl(
    ["Argument", "Description"],
    [
        ["--source &lt;path&gt;",  "Set the source path on launch."],
        ["--dest &lt;path&gt;",    "Set the destination path on launch."],
        ["--scan",              "Trigger an automatic scan after launch."],
        ["&lt;path&gt; (positional)", "Treated as --source."],
    ],
    col_widths=[2.2*inch, 4.3*inch],
))
story.append(sp())
story.append(p("Examples:"))
story.append(code(
    "FileCopyStudio.exe D:\\Photos\n"
    "FileCopyStudio.exe --source D:\\Photos --dest E:\\Backup --scan"
))
story.append(sp())
story.append(p(
    "If File Copy Studio is already running, the new instance forwards its arguments "
    "to the running instance via named pipe and then exits immediately. "
    "The running window comes to the foreground and applies the new paths."
))
story.append(sp())

# ── Section 14: Keyboard shortcuts ────────────────────────────────────────────
story.append(h1("14  Keyboard Shortcuts"))
story.append(rule())
story.append(tbl(
    ["Shortcut", "Action"],
    [
        ["Ctrl+S",           "Scan"],
        ["F5",               "Start copy"],
        ["Esc",              "Cancel"],
        ["Ctrl+P",           "Pause / Resume"],
        ["Ctrl+D",           "Dry run"],
        ["Ctrl+V",           "Verify only"],
        ["Ctrl+Shift+S",     "Open Scheduler"],
        ["Ctrl+1 – Ctrl+5",  "Load preset slot 1–5"],
        ["Ctrl+Shift+1 – 5", "Save to preset slot 1–5"],
        ["Alt+F4",           "Exit (or minimise to tray if enabled)"],
    ],
    col_widths=[2.2*inch, 4.3*inch],
))
story.append(sp())

# ── Section 15: Accessibility ──────────────────────────────────────────────────
story.append(h1("15  Accessibility"))
story.append(rule())
story.append(p(
    "File Copy Studio is designed to meet WCAG 2.2 Level AAA throughout its interface."
))
story.append(sp())
story.append(tbl(
    ["Feature", "Implementation"],
    [
        ["Contrast (SC 1.4.6)",        "All text ≥ 7:1 contrast ratio on both Dark and Light themes."],
        ["Non-text contrast (SC 1.4.11)", "All icons, borders, and focus indicators ≥ 3:1."],
        ["Touch targets (SC 2.5.5)",   "All interactive controls ≥ 44px in both dimensions."],
        ["Focus indicators (SC 2.4.11)", "2px Gold (dark theme) / Cyan (light theme) focus ring on all controls."],
        ["No colour-only info (SC 1.4.1)", "Status icons use glyphs (⚠, ✓, ⊘) alongside colour."],
        ["Live regions (SC 4.1.3)",    "Status text and progress are exposed to screen readers via AutomationPeer."],
        ["High-contrast",              "Automatically detects Windows High Contrast and swaps to system palette."],
        ["UI scale",                   "Five scale levels (Compact → Extra Large) for low-vision users."],
    ],
    col_widths=[2.4*inch, 4.1*inch],
))
story.append(sp())

# ── Section 16: Settings reference ────────────────────────────────────────────
story.append(h1("16  Settings Reference"))
story.append(rule())
story.append(tbl(
    ["Setting", "Default", "Description"],
    [
        ["Subfolders",       "On",  "Include subdirectories in scan and copy."],
        ["Timestamps",       "On",  "Preserve source last-write timestamp on copied files."],
        ["Attributes",       "On",  "Preserve source file attributes (hidden, read-only, etc.)."],
        ["Verify CRC",       "Off", "Compute and compare CRC32 after each file copy."],
        ["Write CRC sidecar","Off", "Write a .crc file alongside each copied file."],
        ["Move (delete src)","Off", "Delete source file after a verified successful copy."],
        ["Resume partials",  "On",  "Detect and resume interrupted .fctemp files."],
        ["Auto-parallel",    "On",  "Automatically set thread count based on drive topology."],
        ["Parallel workers", "1",   "Maximum number of concurrent copy threads (1–8)."],
        ["Buffer size",      "1024 KB", "Read/write buffer per worker (64 KB – 64 MB)."],
        ["Throttle",         "0",   "Maximum bandwidth in Mbps (0 = unlimited)."],
        ["Symlink policy",   "Follow", "Follow / Skip / Copy-as-link."],
        ["Include mask",     "*.*", "Semicolon-separated wildcard patterns to include."],
        ["Exclude mask",     "(empty)", "Semicolon-separated wildcard patterns to exclude."],
        ["Minimize to tray", "Off", "Hide to system tray instead of taskbar on minimize."],
        ["Completion sound", "On",  "Play a system sound when a transfer completes."],
        ["Open dest on done","Off", "Open destination folder in Explorer after clean finish."],
        ["Pause on battery", "Off", "Automatically pause transfers when on battery power."],
        ["Theme",            "Dark","Dark / Light / Auto (follows Windows setting)."],
        ["UI scale",         "Standard", "Compact / Standard / Medium / Large / Extra Large."],
    ],
    col_widths=[1.7*inch, 0.9*inch, 3.9*inch],
))
story.append(sp())

# ── Section 17: Troubleshooting ────────────────────────────────────────────────
story.append(h1("17  Troubleshooting"))
story.append(rule())

issues = [
    ("Scan returns 0 files",
     "Check that the source path exists and is accessible. "
     "If an include mask is set, ensure it matches the files you expect (default is *.*).",),
    ("Files fail CRC verification",
     "This indicates a read error or a write error during the copy. "
     "Retry the copy. If failures persist, check the source media for errors "
     "(run chkdsk or the drive's S.M.A.R.T. diagnostics)."),
    ("Resume does not trigger",
     "Resume only activates when Resume partials is enabled (Zone 4) and the "
     ".fctemp + .fctemp.meta pair is present and valid. "
     "If the source file has changed since the partial was written, the partial "
     "is discarded and a full copy is performed instead."),
    ("Scheduled job does not fire",
     "The application must be running or minimised to the system tray for jobs to fire. "
     "Verify the schedule expression is correct. Check the log file in "
     "%APPDATA%\\File Copy Studio\\logs\\ for any scheduler errors."),
    ("Shell context menu not appearing",
     "Right-click a folder and look for 'Copy with File Copy Studio'. "
     "If missing, repair the installation by running the installer again. "
     "Context menus can take a few seconds to appear on first Explorer restart."),
    ("Application is very slow on a network share",
     "Reduce parallelism to 1–2 workers. "
     "Increase buffer size to 4096 KB or more. "
     "Consider enabling throttle to avoid saturating the link."),
    ("High memory usage during large transfers",
     "Each parallel worker allocates one buffer. "
     "With 8 workers and a 4096 KB buffer, peak allocation is ~32 MB from the pool. "
     "If memory is constrained, reduce parallel workers or buffer size."),
]

for title, desc in issues:
    story.append(KeepTogether([
        h3(title),
        p(desc),
        sp(4),
    ]))

# ── Section 18: Log files ──────────────────────────────────────────────────────
story.append(h1("18  Log Files"))
story.append(rule())
story.append(p("File Copy Studio writes structured logs to a daily rolling file:"))
story.append(tbl(
    ["Mode", "Log location"],
    [
        ["Installed",  "%APPDATA%\\File Copy Studio\\logs\\filecopy-studio-YYYY-MM-DD.log"],
        ["Portable",   ".\\Data\\logs\\filecopy-studio-YYYY-MM-DD.log  (next to the exe)"],
    ],
    col_widths=[1.1*inch, 5.4*inch],
))
story.append(sp())
story.append(p(
    "Logs include all file-level operations, errors, scheduler events, and performance metrics. "
    "They are written in plain text and are safe to open in any text editor. "
    "Old log files are retained indefinitely — you may delete them manually at any time."
))
story.append(sp())

# ── Section 19: Build / Developer Notes ───────────────────────────────────────
story.append(h1("19  Build Notes"))
story.append(rule())
story.append(h2("19.1  Prerequisites"))
story.append(tbl(
    ["Tool", "Version", "Purpose"],
    [
        [".NET SDK",   "10.0",   "Compile and publish"],
        ["NSIS",       "3.x",    "Build setup installer (optional)"],
        ["Python",     "3.10+",  "Regenerate this PDF (optional)"],
    ],
    col_widths=[1.4*inch, 1.1*inch, 3.9*inch],
))
story.append(sp())
story.append(h2("19.2  Build commands"))
story.append(code(
    "cd FileCopy\n"
    "build.bat               # versioned installer + exe\n"
    "build-portable.bat      # portable zip"
))
story.append(p(
    "build.bat auto-increments the build number (yyyy.MM.dd.NNNN), "
    "patches FileCopy.csproj and installer.nsi, publishes a self-contained single-file "
    "Release exe, copies README.md and manual.pdf to the dist folder, "
    "then runs makensis if NSIS is on PATH."
))
story.append(sp())
story.append(h2("19.3  Project structure"))
story.append(tbl(
    ["File / folder", "Purpose"],
    [
        ["FileCopy.csproj",          "Project file — .NET 10, WPF, self-contained publish"],
        ["App.xaml / App.xaml.cs",   "Application entry, global styles, theme switching"],
        ["MainWindow.xaml",          "Main window XAML layout"],
        ["MainWindow.*.cs",          "Partial classes: Copy, Scan, Scheduler, Settings, UI"],
        ["CopyEngine.cs",            "Core copy/verify engine with async parallel workers"],
        ["ICopyEngine.cs",           "DI abstraction for unit testing"],
        ["AppPrefs.cs",              "JSON-serialized user settings (Source Gen)"],
        ["SchedulerWindow.xaml",     "Built-in job scheduler UI"],
        ["Themes/Dark.xaml",         "Dark colour palette"],
        ["Themes/Light.xaml",        "Light colour palette"],
        ["PartialFile.cs",           "Resume marker read/write/finalize logic"],
        ["DriveAffinity.cs",         "Drive topology detection for auto-parallel"],
        ["SizeFormat.cs",            "Human-readable byte formatting"],
        ["installer.nsi",            "NSIS installer script"],
        ["build.bat",                "Build script (version increment + publish + installer)"],
        ["build-portable.bat",       "Portable zip build script"],
        ["manual.pdf",               "This document"],
    ],
    col_widths=[2.2*inch, 4.3*inch],
))
story.append(sp())

# ── Section 20: Version history (recent) ─────────────────────────────────────
story.append(h1("20  Recent Version History"))
story.append(rule())
story.append(tbl(
    ["Version", "Highlights"],
    [
        ["2026.05.09.0001",
         "WCAG 2.2 AAA certification pass: BrushSelectedText on PrimaryButton/DangerButton, "
         "indeterminate CheckBox visual, BrushRed contrast correction. "
         "Performance: meta flush raised 16→64 MB, single FileInfo stat in Enumerate, "
         "CreateDirectory HashSet caching per group."],
        ["2026.05.08.x",
         "SchedulerWindow WCAG fixes: Save job button contrast, 44px form controls, "
         "focus rings on LstJobs. Preset button Move left/right. "
         "BtnUiScale AutomationProperties.Name fix."],
        ["2026.05.07.x",
         "Built-in job scheduler with cron expressions. Per-job throttle and conflict options. "
         "Tray minimize on scheduled run."],
        ["2026.05.04.x",
         "Initial public release. Core copy engine, CRC32 verify, crash-safe resume, "
         "parallel workers, presets, job files, shell context menu integration."],
    ],
    col_widths=[1.7*inch, 4.8*inch],
))
story.append(sp())

# ── Back cover ─────────────────────────────────────────────────────────────────
story.append(PageBreak())
story.append(Spacer(1, 3*inch))
story.append(rule())
story.append(sp(8))
story.append(Paragraph("File Copy Studio", TITLE))
story.append(sp(4))
story.append(Paragraph("Version 2026.05.09.0001", SUBTITLE))
story.append(sp(2))
story.append(Paragraph(f"&copy; 2026 Matthew Pittman. All rights reserved.", BYLINE))
story.append(sp(2))
story.append(Paragraph(
    "This document is provided for informational purposes. "
    "Specifications are subject to change without notice.",
    NOTE
))

# ── Build ──────────────────────────────────────────────────────────────────────
doc.build(
    story,
    onFirstPage=on_first_page,
    onLaterPages=on_page,
)
print(f"Generated: {OUT_PATH}")
  