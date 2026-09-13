# CapPicker 2.0.0

CapPicker is a lightweight and fast Windows utility. Only useful features belong here:
basic screen capture, color picking, and essential editing.

가볍고 빠른 Windows 유틸리티입니다. 꼭 필요한 기능만을 담습니다.
기본 화면 캡처, 색상 추출, 기초 편집이 전부입니다.

## Direction / 기본 방향

- Lightweight, fast, and useful features only.
- No cloud upload, no accounts, no auto-update, no plugins.
- If a feature is not used almost every day, it does not belong in CapPicker.
- On normal PCs the full experience stays fast and comfortable.
  Reduced behavior applies only under Low-spec PC optimization.

- 가벼움, 빠름, 유용한 기능만을 지향합니다.
- 클라우드 업로드, 계정, 자동 업데이트, 플러그인은 넣지 않습니다.
- 거의 매일 쓰지 않는 기능은 넣지 않습니다.
- 일반 PC에서는 빠르고 편한 그대로, 완화된 동작은 저사양 PC 최적화에만 적용합니다.

## Features / 주요 기능

- Rectangle capture (`Alt + Shift + S`), fixed-size, window, full-screen, last-region capture
- Screen color picker (`Alt + Shift + C`) and image-local picker with live HEX/RGB, pixel grid, 6x–128x zoom
- Pen, highlighter, shapes, arrow, check, text, eraser, crop, rotation, undo/redo
- Image zoom up to 1000% (`Ctrl + mouse wheel`)
- CapPicker-style print preview + Windows print dialog (portrait/landscape, alignment)
- Korean/English UI with automatic Windows display-language detection
- Per-Monitor V2 high-DPI support (120 DPI = 1.0 reference)

- 사각형/고정크기/윈도우/전체화면/지난영역 캡처
- 화면·이미지 컬러피커 (실시간 HEX/RGB, 픽셀 격자, 6x~128x)
- 펜, 형광펜, 도형, 화살표, 텍스트, 지우개, 자르기, 회전, 실행 취소/다시 실행
- 한국어·영어 UI (Windows 표시 언어 자동 감지)

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

## Download and run / 다운로드 및 실행

Download `CapPicker.exe` from the latest [Release](https://github.com/jjaeeungm/CapPicker/releases) and run it.
Single file, no installer.

최신 릴리스의 `CapPicker.exe`를 내려받아 실행하세요. 단일 파일이며 설치 과정이 없습니다.

## Build from source / 소스에서 빌드

1. Clone the repository or download the source code.
2. Run `BUILD.cmd` (requires .NET Framework 4.8 Advanced Services for `csc.exe`).
3. `CapPicker.exe` is created in the same folder and launched.

1. 저장소를 복제하거나 소스를 내려받습니다.
2. `BUILD.cmd`를 실행합니다. (`csc.exe`용 .NET Framework 4.8 필요)
3. 같은 폴더에 `CapPicker.exe`가 생성되고 실행됩니다.

## Version / 버전

- Product: **2.0.0**, Assembly/File: **2.0.0.0**
