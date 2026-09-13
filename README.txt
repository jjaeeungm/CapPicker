CapPicker 2.0.0 - Light, fast, and useful only.

Direction / 기본 방향
- CapPicker stays lightweight and fast. Only useful features belong here:
  basic screen capture, color picking, and essential editing.
- No cloud upload, no accounts, no auto-update, no plugins.
  If a feature is not used almost every day, it does not belong in CapPicker.
- On normal PCs the full experience stays fast and comfortable.
  Reduced behavior applies only under Low-spec PC optimization.
- CapPicker는 가볍고 빠릅니다. 꼭 필요한 기능만 담습니다:
  기본 화면 캡처, 색상 추출, 기초 편집이 전부입니다.
- 클라우드 업로드, 계정, 자동 업데이트, 플러그인은 넣지 않습니다.
  거의 매일 쓰지 않는 기능은 CapPicker에 넣지 않습니다.
- 일반 PC에서는 빠르고 편한 그대로, 완화된 동작은 저사양 PC 최적화에만 적용합니다.

Language / 언어
- Default: follow the Windows display language automatically.
  Other Windows display languages use the English UI.
- Press F1 to open Help. Language selection has moved to Settings.
- Settings uses radio buttons for Windows setting (Auto) / 한국어 / English.
- A manually selected language is saved under LocalAppData and is applied to the full UI immediately without restarting.
- English button widths are measured from the localized captions before the initial minimum window width is calculated, preventing Last Region / Color Picker / Copy clipping.
- 한국어(ko-*) Windows에서는 한국어 UI가 표시됩니다.

Help / 도움말
- F1 opens the built-in help window.
- Help is now content-focused; language selection is handled only in Settings.
- The document area has left/right/top/bottom padding, numbered section headings in bold, and bullet items for readability.
- F1 remains available from the main window and the Settings window.
- F1은 내장 도움말 창을 엽니다. 언어 선택은 설정에서만 합니다.

Color pickers / 컬러피커
- Screen Color Picker: Alt + Shift + C.
- Live HEX/RGB, pixel grid, center crosshair, mouse-wheel zoom from 6x to 128x.
- Normal mode uses a 408x408 reference-pixel magnifier and grows to 612x612 above 64x. Low-spec PC optimization fixes it to 300x300 reference pixels to reduce compositor load.
- The eyedropper immediately to the left of Copy uses the same shared HEX/RGB/XY/Zoom information-card renderer as the screen picker, with image-local XY values.
- With the image-local picker active, both mouse wheel and Ctrl+mouse wheel change picker magnification. They do not zoom or scroll the captured image.
- Clicking an exact image pixel selects the color and copies its RGB value.
- 화면 컬러피커: Alt + Shift + C. 실시간 HEX/RGB, 픽셀 격자, 중심 십자선, 6x~128x 확대를 지원합니다.

Version / 버전
- Product version: 2.0.0
- Assembly/File version: 2.0.0.0
- Window title: CapPicker 2.0.0

Key features / 주요 기능
- Rectangle capture: Alt + Shift + S
- Window capture: direct HWND rendering via PrintWindow first, with visible-screen fallback for unsupported/GPU windows
- Pen, highlighter, shapes, arrow, check, text, eraser, crop, rotation, undo/redo
- Ctrl + mouse wheel image zoom up to 1000% (except while image-local picker is active)
- Print preview (CapPicker style) + Windows print dialog: portrait/landscape, horizontal/vertical alignment
- Minimize caption button behavior is configurable: Taskbar (default) or Tray; Win+D / Show Desktop keeps normal Windows behavior
- 사각형 캡처: Alt + Shift + S. 펜, 형광펜, 도형, 화살표, 텍스트, 지우개, 자르기, 회전, 실행 취소/다시 실행을 지원합니다.

[High DPI / 고DPI]
- Reference screen: 1920x1080 at Windows 125% (120 DPI).
- The UI layout uses 120 DPI = 1.0 as the single scale reference.
- At 100/125/150/175/200%, windows, buttons, icons, and spacing scale by the real monitor DPI / 120 ratio.
- Status bar widths are fixed from representative maximum strings per language/DPI and are never recalculated during mouse moves.
- Per-Monitor V2 is kept: moving across monitors with different scales re-lays out on the same basis.
- Capture/screen coordinates stay in real Windows screen pixels, separated from the UI scale.
- 기준 화면: 1920x1080 / Windows 125% (120 DPI). UI는 120 DPI = 1.0 단일 기준으로 확대/축소합니다.

[Settings / 설정]
- Open the popup Settings window from the gear icon at the right of the top HEX/RGB result.
- Radio-button selection (no combo boxes).
- Language: Windows setting (Auto) / 한국어 / English
- Minimize button behavior: Taskbar (default) / Tray
- Close button behavior: Tray (default) / Exit app
- Performance: Normal (recommended, default) / Low-spec PC optimization
- Help (F1) is available from the Settings window.
- Behavior settings are stored in LocalAppData\CapPicker\settings.txt, language in language.txt.
- 상단 HEX/RGB 결과 오른쪽의 설정(톱니) 아이콘에서 설정 창을 엽니다.

Build
1. Extract the ZIP.
2. Run BUILD.cmd.
3. CapPicker.exe is created in the same folder and launched.
