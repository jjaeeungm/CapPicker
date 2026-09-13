# CapPicker — Technical Notes / 기술 노트

Developer-oriented implementation notes for CapPicker 2.0.0.
User-facing documentation lives in [README.md](README.md) / [README.txt](README.txt).

## Build / 빌드

- Language: C# WinForms on .NET Framework 4.x. No NuGet packages, no `.csproj`.
- `BUILD.cmd` compiles 15 `.cs` files directly with `csc.exe`
  (`/target:winexe /optimize+`, x64 preferred, x86 fallback).
- Output: single `CapPicker.exe` (~185 KB). No installer.
- Build with zero warnings (csc `/nologo`, warnings treated as pre-existing only).

## Capture engine / 캡처 엔진

- Rectangle / fixed-size: fullscreen selection overlay → `CopyFromScreen`.
- Window capture: `PrintWindow(PW_RENDERFULLCONTENT → default)` first;
  on failure or obviously-blank/transparent result, falls back to
  `CopyFromScreen` of the DWM visible frame (`DWMWA_EXTENDED_FRAME_BOUNDS`).
- Minimized windows are excluded from window picking target candidates.
- Window picking: `WindowFromPoint` + `GA_ROOT` promotion first,
  Z-order traversal fallback (also skips CapPicker's own highlight in hit-testing).
- A settle delay after the selection border disappears lets DWM clear it
  before the capture runs.

## Color pickers / 컬러피커

- Screen picker (`Alt + Shift + C`): top-most magnifier host + info card.
  Reference magnifier 408x408 px, growing to 612x612 above 64x zoom.
- Image-local picker (eyedropper next to Copy): same shared
  HEX/RGB/XY/Zoom information-card renderer, with image-local XY values.
  While active, mouse wheel and `Ctrl` + wheel change picker magnification
  instead of zooming/scrolling the image.
- Clicking an exact pixel selects the color and copies its RGB value.
- Low-spec PC optimization: screen magnifier fixed to 300 reference px
  (no large-window growth above 64x), gentler refresh cadence,
  `DwmFlush` synced every 5th frame instead of every frame,
  image-local sample grid capped at 31x31 cells.

## Editing / 편집

- Tools: pen, highlighter, shapes, arrow, check mark, emoji, text, eraser,
  pixel eraser, crop, rotation, undo/redo.
- Undo history: max 8 entries, targeting ~100 MB total (PNG-serialized
  current + clean bitmaps, oldest dropped first; the last entry is kept).
- Canvas checkerboard reuses a 24x24 tile brush instead of repainting
  thousands of rectangles per frame.
- `Ctrl` + mouse wheel zooms the image 25%–1000% (`Ctrl + 0` resets to 100%).
- With no tool selected, dragging on the image measures the region
  (shown in the status bar, not painted).
- Text editing follows Paint-style behavior with IME composition support.

## DPI / 고DPI

- Reference screen: 1920x1080 at Windows 125% (120 DPI).
- Single UI scale reference: 120 DPI = 1.0. At 100/125/150/175/200%,
  windows, buttons, icons, and spacing scale by real monitor DPI / 120.
- Per-Monitor V2 is declared (`app.manifest` + `SetProcessDpiAwarenessContext`
  with `SetProcessDPIAware` fallback); moving across differently-scaled
  monitors re-lays out on the same basis.
- Capture/screen coordinates always stay in real Windows screen pixels,
  separated from the UI scale.
- Status bar widths are fixed from representative maximum strings per
  language/DPI and are never recalculated during mouse moves.

## Settings & language / 설정·언어

- Settings UI: radio buttons for language (Windows Auto / 한국어 / English),
  minimize-button behavior (Taskbar default / Tray), close-button behavior
  (Tray default / Exit app), performance (Normal default / Low-spec).
- Stored in `%LocalAppData%\CapPicker\settings.txt` (behavior) and
  `language.txt` (language). Plain text, no secrets.
- Manually selected language applies to the full UI immediately, no restart.
- English button widths are measured from localized captions before the
  initial minimum window width is calculated (prevents clipping of
  Last Region / Color Picker / Copy).

## Printing / 인쇄

- Custom CapPicker-style preview window (not `PrintPreviewControl`):
  real paper ratio with default margins, portrait/landscape switching,
  horizontal/vertical alignment shared with actual printing.
- `Print...` / `Ctrl + P` opens the standard Windows print dialog;
  the preview renders a scaled copy and never duplicates the source bitmap.
- JPEG save flattens transparency onto white; PNG preserves it.

## Performance model / 성능 모델

- No fixed high-frequency loop in Normal mode: adaptive refresh
  (~40 fps while moving, ~20 fps idle; lower under Low-spec optimization).
- The low-level mouse hook resumes the fast cadence immediately on movement.
- Global hotkeys: `Alt + Shift + S` (rectangle), `Alt + Shift + C` (picker).
  Registration conflicts are reported in the button tooltips.
- Clipboard copy failures and save failures show `ex.Message` only
  (no stack traces in user builds).
