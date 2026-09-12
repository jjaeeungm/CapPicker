# CapPicker

CapPicker는 화면 캡처, 색상 추출, 간단한 이미지 편집과 인쇄를 한곳에서 처리하는 Windows용 도구입니다.

현재 버전: **1.1.7**

## 주요 기능

- 사각형, 고정 크기, 창, 전체 화면, 마지막 영역 캡처
- 화면 및 캡처 이미지 내부 컬러피커
- 실시간 HEX/RGB, 픽셀 격자, 중심 십자선, 6×~128× 확대
- 펜, 형광펜, 도형, 화살표, 체크, 텍스트, 지우개, 자르기, 회전, 실행 취소/다시 실행
- 캡처 이미지 크기 측정과 최대 1000% 확대
- CapPicker 스타일 인쇄 미리보기, 용지 방향 및 가로·세로 맞춤 설정
- 한국어/영어 UI와 Windows 표시 언어 자동 감지
- 작업표시줄/트레이 최소화 및 닫기 동작 설정
- 일반 모드와 저사양 PC 최적화 모드
- Windows 배율 100%~200% 및 혼합 DPI 모니터 지원

## 다운로드 및 실행

[Releases](https://github.com/jjaeeungm/CapPicker/releases)에서 최신 릴리즈의 `CapPicker.exe`를 내려받아 실행합니다.

현재 별도 설치 프로그램, 포터블 ZIP, 로컬 빌드 ZIP은 제공하지 않습니다. 배포 파일은 `CapPicker.exe` 단일 파일입니다.

Windows에서 인터넷에서 받은 실행 파일에 대한 보안 경고가 표시될 수 있습니다. 파일 출처를 확인한 뒤 실행 여부를 선택하세요.

## 단축키

| 기능 | 단축키 |
| --- | --- |
| 사각형 캡처 | `Alt + Shift + S` |
| 화면 컬러피커 | `Alt + Shift + C` |
| 도움말 | `F1` |
| 복사 | `Ctrl + C` |
| 저장 | `Ctrl + S` |
| 인쇄 미리보기 | `Ctrl + P` |
| 실행 취소 | `Ctrl + Z` |
| 다시 실행 | `Ctrl + Y` |
| 이미지 확대/축소 | `Ctrl + 마우스 휠` |

## 설정

상단의 톱니바퀴 버튼에서 다음 항목을 변경할 수 있습니다.

- 언어: Windows 설정(자동), 한국어, English
- 최소화 버튼: 작업표시줄 또는 트레이
- 닫기 버튼: 트레이 또는 앱 종료
- 성능 옵션: 일반 또는 저사양 PC 최적화

설정은 `%LocalAppData%\CapPicker`에 저장됩니다.

## 소스에서 빌드

CapPicker는 C# WinForms로 작성되었으며 Windows .NET Framework 도구 체인을 사용합니다.

1. 저장소를 복제하거나 소스 코드를 내려받습니다.
2. 모든 소스 파일이 같은 폴더에 있는지 확인합니다.
3. `BUILD.cmd`를 실행합니다.
4. 빌드가 성공하면 같은 폴더에 `CapPicker.exe`가 생성되고 실행됩니다.

C# 컴파일러를 찾지 못하면 Windows 기능에서 **.NET Framework 4.8 Advanced Services**를 활성화하세요.

## 스크린샷

![CapPicker screenshot 1](screenshots/screenshot_01.png)

![CapPicker screenshot 2](screenshots/screenshot_02.png)

![CapPicker screenshot 3](screenshots/screenshot_03.png)

![CapPicker screenshot 4](screenshots/screenshot_04.png)

![CapPicker screenshot 5](screenshots/screenshot_05.png)

## 버전 1.1.7

- 인쇄 미리보기 상단 옵션의 간격과 정렬 개선
- 설정 라벨과 버튼 높이 통일
- 인쇄/닫기 버튼 폭 확대 및 텍스트 잘림 방지
- 인쇄 미리보기 최소 폭 확대
