# CapPicker

CapPicker is a tiny, fast Windows screenshot, color picker, and quick annotation utility.
Capture exactly what you need, pick colors precisely, and annotate immediately.
One ~185 KB EXE. No installer, no account, no cloud.

CapPicker는 작고 빠른 Windows 캡처·컬러피커·빠른 편집 도구입니다.
필요한 화면을 정확히 캡처하고, 색상을 정밀하게 확인하고, 바로 표시·편집할 수 있습니다.
약 185KB 단일 EXE. 설치·계정·클라우드가 필요 없습니다.

## Screenshots / 스크린샷

![Main window / 메인 화면](screenshots/screenshot_01.png)

*Main window: capture bar, quick editing tools, color readout. / 캡처 바, 빠른 편집 도구, 색상 표시가 있는 메인 화면.*

![Annotating a capture / 캡처 편집](screenshots/screenshot_02.png)

*Rectangle annotation with color presets and width control. / 색상 프리셋과 두께 조절로 사각형을 그리는 중.*

![Precision color picker / 정밀 컬러피커](screenshots/screenshot_03.png)

*48x magnifier with pixel grid, crosshair, and live HEX/RGB card. / 픽셀 격자·십자선·실시간 HEX/RGB 카드가 있는 48x 확대경.*

## Features / 주요 기능

### Capture / 캡처

- Rectangle (`Alt + Shift + S`), fixed-size, window, full-screen, last-region capture
- Window capture renders directly via PrintWindow first, with visible-screen fallback for unsupported/GPU windows

- 사각형(`Alt + Shift + S`), 크기지정, 윈도우창, 전체화면, 지난영역 캡처

### Color Picker / 컬러피커

- Screen picker (`Alt + Shift + C`) and image-local picker
- Live HEX/RGB readout, pixel grid, center crosshair, 6x–128x mouse-wheel zoom
- Clicking a pixel copies its RGB value

- 화면 컬러피커(`Alt + Shift + C`)와 이미지 내부 컬러피커
- 실시간 HEX/RGB, 픽셀 격자, 중심 십자선, 6x~128x 마우스 휠 확대

### Quick Edit / 빠른 편집

- Pen, highlighter, shapes, arrow, check mark, text, eraser, crop, rotate, undo/redo
- Image zoom up to 1000% (`Ctrl + mouse wheel`)

- 펜, 형광펜, 도형, 화살표, 체크, 텍스트, 지우개, 자르기, 회전, 실행 취소/다시 실행

### Print / 인쇄

- CapPicker-style print preview with portrait/landscape and horizontal/vertical alignment
- Prints through the standard Windows print dialog

- CapPicker 스타일 인쇄 미리보기 (용지 방향·가로/세로 맞춤) + Windows 표준 인쇄

## Why CapPicker / 왜 CapPicker인가

- **Tiny:** one ~185 KB EXE. No installer, no setup.
- **No strings attached:** no account, no cloud, no auto-update, no plugins.
- **Fast on normal PCs:** full experience with adaptive refresh; reduced behavior applies only under Low-spec PC optimization.
- **Precise:** pixel-grid magnifier up to 128x with live HEX/RGB for exact color work.
- **Bilingual:** Korean/English UI with automatic Windows display-language detection.

- **작음:** 약 185KB 단일 EXE. 설치 과정 없음.
- **깔끔함:** 계정·클라우드·자동 업데이트·플러그인 없음.
- **빠름:** 일반 PC에서는 적응형 갱신으로 쾌적하게, 완화된 동작은 저사양 PC 최적화에만.
- **정밀함:** 최대 128x 픽셀 격자 확대경과 실시간 HEX/RGB.
- **한영 지원:** Windows 표시 언어 자동 감지.

## Download and run / 다운로드 및 실행

Download `CapPicker.exe` from the latest [Release](https://github.com/jjaeeungm/CapPicker/releases) and run it.

최신 릴리스의 `CapPicker.exe`를 내려받아 실행하세요.

## Shortcuts / 단축키

| Action / 기능 | Shortcut / 단축키 |
| --- | --- |
| Rectangle capture / 사각형 캡처 | `Alt + Shift + S` |
| Screen color picker / 화면 컬러피커 | `Alt + Shift + C` |
| Help / 도움말 | `F1` |
| Copy / 복사 | `Ctrl + C` |
| Save / 저장 | `Ctrl + S` |
| Print preview / 인쇄 미리보기 | `Ctrl + P` |
| Undo / 실행 취소 | `Ctrl + Z` |
| Redo / 다시 실행 | `Ctrl + Y` |
| Image zoom / 이미지 확대·축소 | `Ctrl + mouse wheel` |

## Build from source / 소스에서 빌드

1. Clone the repository or download the source code.
2. Run `BUILD.cmd` (requires .NET Framework 4.8 Advanced Services for `csc.exe`).
3. `CapPicker.exe` is created in the same folder and launched.

1. 저장소를 복제하거나 소스를 내려받습니다.
2. `BUILD.cmd`를 실행합니다. (`csc.exe`용 .NET Framework 4.8 필요)
3. 같은 폴더에 `CapPicker.exe`가 생성되고 실행됩니다.

## Philosophy / 철학

CapPicker stays small on purpose. Only useful features belong here — if a feature
is not used almost every day, it does not belong in CapPicker.

작은 것을 의도합니다. 거의 매일 쓰지 않는 기능은 넣지 않습니다.

## Technical notes / 기술 노트

Implementation details (magnifier sizes, DPI reference, capture fallbacks, settings
paths) live in [TECHNICAL_NOTES.md](TECHNICAL_NOTES.md).

구현 세부사항은 [TECHNICAL_NOTES.md](TECHNICAL_NOTES.md)를 참고하세요.

## Version / 버전

- Product: **2.0.0**, Assembly/File: **2.0.0.0**

## License / 라이선스

MIT — see [LICENSE](LICENSE).
