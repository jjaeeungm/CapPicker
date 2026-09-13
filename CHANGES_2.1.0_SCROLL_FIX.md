# CapPicker 2.1.0 Scroll Stitch + Icon Fix

이번 소스는 사용자가 제공한 `P260913_CapPicker2(1).zip`을 기준으로 직접 수정한 개발본이다.

- ScrollCapture: previous/current 인접 프레임 매칭
- 고정영역/overlap 중복 삽입 제거, 신규 하단 strip만 append
- 최근 scroll delta median 기반 alias 억제
- 실패 시 동일 위치 재캡처 최대 2회
- RGB/Luma 허용오차 기반 매칭
- 중앙 child HWND 우선 스크롤
- 설정 아이콘: 표준 8톱니 cog 형태
- 좌우/상하 반전: 대칭축 + 양측 도형 형태

제공된 기존 실패 debug frame으로 재구성했을 때 기존 6,586px 결과가 5,864px로 정상화되고, 중간 고정영역 반복 삽입이 제거되는 것을 확인했다.

Windows 실빌드는 `BUILD.cmd`로 최종 확인한다.

## 추가 검증 후 보정 (stitch matcher v2)

실사용 로그에서 `s2~s6`은 약 399~400px 이동을 정상 검출했지만, `s7`에서 실제 화면은 동일한 약 400px 이동임에도 `overlap=-1`, `best=0.929`, `bestH=58`로 3회 연속 실패했다.

원인은 `RowsMatch()`가 표본 초반의 mismatch 비율이 임계값을 잠깐 넘는 즉시 후보를 탈락시키는 early-reject 구조였다. 동적 카드/hover/합성 변화가 seam 근처에 있으면 전체 overlap은 매우 높은데도 정답 후보를 너무 일찍 버릴 수 있었다.

보정 내용:
- `RowsMatch()`는 제한된 표본 전체를 끝까지 계산한 뒤 similarity를 판정
- 표본 밀도를 줄여 전체 평가 비용 상쇄 (`StripRows=96`, 폭에 따라 xStep 4/6/8)
- 최근 정상 이동량이 있는 경우 `MinExpectedSimilarity=0.87`으로 연속성 기반 후보 허용
- broad fallback은 `MinBroadSimilarity=0.92`로 보수적으로 유지
- 실제 제공 프레임의 문제 구간(6→7)을 동일 방식으로 재검증했을 때 약 400px 이동 후보가 최고점으로 복구됨

## 시작/종료 판정 + 저사양 타이밍 보강 (v3)

스크롤 캡처의 시작점과 종료점을 단순 메시지 전송/동일 이미지 1회에 의존하지 않도록 보강했다.

- 시작 전 맨 위 이동:
  - `WM_VSCROLL + SB_TOP`
  - modifier 없는 upward mouse-wheel 반복
  을 함께 사용한다. `Ctrl + Home`은 브라우저 줌 오작동 가능성 때문에 사용하지 않는다.
- 같은 top 명령을 반복한 뒤 캡처 화면이 실질적으로 더 움직이지 않는지 비교하여 **맨 위 도달을 시각적으로 검증**한다.
- 최하단은 exact 동일 이미지 1회가 아니라 **실질 이동 없음 2회 연속**으로 확정한다.
  - 첫 no-move 후에는 `PageDown`으로 한 번 더 독립 확인한다.
  - 8px 이하의 tiny shift도 no-move 후보로 취급한다.
  - 마지막 20~60px 같은 실제 소폭 이동은 먼저 정상 append하고, 그 다음 2회의 no-move에서 종료한다.
- tolerant `FramesStable()`은 overlap 매칭이 실패했을 때만 no-move 보조 판정으로 사용한다. 따라서 실제 작은 마지막 이동을 먼저 버리지 않는다.
- 저사양 PC 최적화 시 캡처 간격을 더 넉넉하게 적용한다.
  - 일반: 기본 settle 500ms / retry 160ms / top probe 450ms
  - 저사양: 기본 settle 1000ms / retry 360ms / top probe 900ms
  - PageDown은 기본 settle에 300ms를 추가한다.
- `scroll-dbg-*.png` 저장은 기본 비활성화하여 스크롤 캡처 중 PNG 인코딩/디스크 I/O 부담을 제거했다. `scroll-last.log`는 진단용으로 유지한다.

평상시에는 타이머·백그라운드 스레드·상시 캡처를 사용하지 않으므로 스크롤 캡처 기능 추가 자체의 상시 CPU/메모리 부담은 사실상 없다. 실제 이미지 처리 비용은 사용자가 스크롤 캡처를 실행한 동안에만 발생한다.

## No-Zoom + prefix 안정화 + 공통 지연 캡처 (v4)

실사용에서 브라우저 화면이 확대되는 문제와 `prefix=242 → 109` 급변 후 스티칭이 끊기는 로그를 반영했다.

- **브라우저 줌 보호**
  - 최상단 이동에서 `Ctrl + Home` 키 주입을 완전히 제거했다.
  - `SB_TOP` + modifier 없는 upward `WM_MOUSEWHEEL`만 사용한다.
  - upward wheel은 한 번의 비정상적으로 큰 delta 대신 작은 delta를 여러 번 보내도록 변경했다.
  - 최상단 확인 시도 횟수를 늘려 긴 페이지에서도 실제 no-move 상태를 확인한다.
- **sticky prefix 안정화**
  - 첫 정상 스티칭에서 얻은 prefix를 최근 5개 median으로 유지한다.
  - 새 raw prefix가 기준에서 96px 이상 급변하면 해당 값을 버리고 안정된 prefix를 사용한다.
  - 최근 정상 scroll delta가 있는 경우 예상 위치 우선 매칭의 허용 similarity를 `0.87`로 완화하고 broad fallback은 `0.92`로 유지한다.
- **지연 캡처를 전체 캡처 공통 기능으로 승격**
  - 0 / 3 / 5초 지연을 사각형, 크기지정, 윈도우, 전체화면, 지난영역, 스크롤 캡처에 적용한다.
  - 사각형/크기지정: 지연 후 선택 오버레이를 시작한다.
  - 윈도우/스크롤: 대상 창을 먼저 선택한 뒤 지연하여, 사용자가 해당 창의 메뉴/드롭다운 상태를 준비할 수 있게 한다.
  - 전체화면/지난영역: 지연 후 바로 캡처한다.
  - 카운트다운 동안 CapPicker 창을 다시 `Show()/Activate()`하지 않으므로 대상 앱의 포커스를 빼앗지 않는다.
  - `Esc`로 카운트다운을 취소할 수 있다.
  - 저사양 모드라도 사용자가 선택한 3/5초 자체는 늘리지 않으며, 스크롤 캡처의 frame settle/retry 간격만 더 넉넉하게 유지한다.
- **소스 패키지 보완**
  - `BUILD.cmd`가 참조하는 `WindowPicker.cs`를 소스 ZIP에 복구했다.

평상시에는 지연 캡처/스크롤 캡처를 위한 백그라운드 루프나 상시 타이머를 추가하지 않으므로 상시 리소스 부담은 사실상 없다.

## 1.0 반응성 복원 + 현행 경량화 유지 (v5)

1.0.0 소스와 v4를 직접 비교해 편집 영역의 체감 지연을 만드는 시간 기반 refresh 제한만 선택적으로 되돌렸다.

- **Editor 일반 모드**
  - 16ms preview throttle을 제거하고 1.0 계열처럼 mouse-move마다 `Invalidate()`를 요청한다.
  - `Invalidate()`는 동기식 전체 redraw가 아니라 Windows가 `WM_PAINT`를 합칠 수 있는 비동기 invalidation이므로, 포인터 반응성을 복원하면서 불필요한 강제 redraw는 만들지 않는다.
  - 펜/형광펜/지우개 등의 커서는 기존대로 **실제 선 두께와 동일한 지름의 원형 표시**를 유지한다.
  - 상태바 좌표/색상 정보는 일반 16ms, 저사양 80ms 제한을 유지한다. 시각 커서와 분리하여 불필요한 `GetPixel`/상태바 갱신을 줄인다.
- **Editor 저사양 모드**
  - preview 50ms 제한을 그대로 유지한다.
- **화면 컬러피커 일반 모드**
  - 1.0 계열과 동일하게 20ms 고정 cadence로 복원한다.
  - 정지 시 50ms로 늦췄다가 이동 시 복귀하던 adaptive cadence를 일반 모드에서 제거해 재이동 순간의 체감 지연을 없앤다.
- **화면 컬러피커 저사양 모드**
  - 이동 50ms / 정지 160ms adaptive cadence와 주기적 정확 색상 보정을 유지한다.
- **그대로 유지한 경량화**
  - checkerboard `TextureBrush`
  - 이미지 컬러피커 `LockBits`
  - 셀별 Brush 생성 제거/재사용
  - 중복 상태 문자열 갱신 억제
  - 공용 Font 사용
  - Undo 메모리 상한
  - 저사양 대형 화면/확대경 보호
  - 스크롤 캡처의 비활성 시 무부하 구조

방향은 **일반 모드 = 1.0 수준의 즉각적 반응**, **저사양 모드 = 명시적 frame 제한**, 그리고 두 모드 모두에서 실제 연산 자체를 싸게 만드는 최적화는 유지하는 것이다.

## v6 ResponsiveLean — 1.0 반응성 + 부분 무효화

- 편집기 visual feedback의 시간 기반 frame throttle을 제거했습니다.
  - 일반/저사양 모두 펜·형광펜·도형·지우개 커서와 편집 preview는 1.0 계열처럼 마우스 이벤트를 즉시 따라갑니다.
  - 저사양 모드는 좌표/색상 status sampling, 축소 interpolation, 이미지 picker grid 제한, 스크롤 캡처 안정화 대기 등 실제 비용 절감 쪽에만 집중합니다.
- 마우스 이동 시 전체 Canvas를 다시 invalidate하지 않도록 변경했습니다.
  - 두께 커서는 이전 위치와 새 위치의 작은 영역만 invalidate.
  - 펜/형광펜/지우개/픽셀 지우개는 이동 segment 주변만 invalidate.
  - 도구 미선택 상태의 크기 측정 가이드는 이전/새 외곽선 영역만 invalidate.
- `OnPaint`의 이미지 clip을 `CombineMode.Intersect`로 변경했습니다.
  - 기존 `SetClip(imageBounds)`는 Windows의 WM_PAINT dirty region을 대체하여 작은 invalidate도 전체 이미지 repaint로 확대될 수 있었습니다.
  - 이제 dirty region을 보존하여 큰 캡처 이미지에서도 커서 이동 repaint 비용을 줄입니다.
- 기존 리소스 절감 최적화는 유지했습니다.
  - checkerboard TextureBrush, 이미지 picker LockBits/brush 재사용, 중복 status text 방지, Undo 메모리 제한 등.
- 목표: **보이는 반응성은 1.0 수준, repaint/연산량은 1.0보다 작게**.

## v7 Responsive/Click-Lean
- 스크롤 캡처의 `scroll-last.log` 및 `scroll-dbg-*.png` 생성 코드 완전 제거
- 일반 편집 MouseDown/MouseUp의 전체 캔버스 repaint를 최소화하고 변경 영역/실제 두께 커서 영역만 갱신
- 도구 미선택 측정은 단순 클릭이 아니라 실제 드래그(3px 이상)부터 시작
- 형광펜 확정 시 전체 캔버스 크기 임시 Bitmap 대신 stroke bounding box 크기 overlay 사용
- Undo 스냅샷에서 변경되지 않은 `clean` PNG를 캐시/공유하여 반복 압축 비용 절감
- v6의 즉각적인 MouseMove/dirty-region repaint와 1.0 수준 반응성 유지
