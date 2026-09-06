# CapPicker

**CapPicker** is a lightweight Windows screenshot, color picker, and quick annotation utility.

> 화면 캡처 · 색상 추출 · 간편 편집을 하나의 작은 Windows 도구로 제공합니다.

## Highlights

- Rectangle capture (`Alt + Shift + S`)
- Fixed-size, window, full-screen, and last-region capture
- Screen color picker (`Alt + Shift + C`)
- Image-local color picker with pixel grid and magnification
- RGB copy on color selection
- Pen, highlighter, shapes, arrow, check, text, eraser, crop, rotation, undo/redo
- Paint-style Korean/IME text input
- Korean / English UI with instant language switching from `F1` Help
- Tray minimize behavior and clipboard auto-copy after capture

## Screenshots

Screenshots are included under `docs/screenshots/`.

## Download

A ready-built executable is provided at `dist/CapPicker.exe`.

For a local build, download `dist/CapPicker_1.0.0_LocalBuild.zip`, extract it, and run `BUILD.cmd`.

## Build

CapPicker is written in C# WinForms and can be built with the .NET Framework compiler already included with Windows 10/11 on most systems.

1. Download or clone this repository.
2. Run `BUILD.cmd`.
3. `CapPicker.exe` is created in the same folder and launched.

If the compiler is unavailable, enable **.NET Framework 4.8 Advanced Services** in Windows Features.

## Shortcuts

| Action | Shortcut |
| --- | --- |
| Rectangle capture | `Alt + Shift + S` |
| Screen color picker | `Alt + Shift + C` |
| Help / Language | `F1` |
| Copy | `Ctrl + C` |
| Save | `Ctrl + S` |
| Undo | `Ctrl + Z` |
| Redo | `Ctrl + Y` |

## Color picker

The screen and image-local color pickers support a pixel grid, center crosshair, and wheel zoom from 6× to 128×. Above 64×, the magnifier area grows to 1.5× its normal size. Clicking a color copies its RGB value.

## Language

CapPicker follows the Windows display language by default: Korean Windows uses Korean, and other display languages use English. Open Help with `F1` to switch language; the UI updates immediately without restarting.

## Version

**1.0.0**
